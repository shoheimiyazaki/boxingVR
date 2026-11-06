using System.Numerics;
using BepuPhysics;
using BepuPhysics.Collidables;
using BepuPhysics.Trees;

namespace Aigis.Physics;

/// <summary>
/// Spatial queries against the live simulation (as opposed to PhysicsWorld.Joints.cs's
/// mutation of it). Kept in its own partial file — a query is a different concern from
/// adding/removing constraints, even though a caller (e.g. mouse-picking) typically uses both.
/// </summary>
public sealed partial class PhysicsWorld
{
    /// <summary>One ray-cast hit.</summary>
    /// <param name="Id">The hit collidable's stable id. When <see cref="IsStatic"/> is false this is
    /// a body id (usable with <see cref="GetState"/> etc); when true it is a Bepu static handle
    /// value from a disjoint id space (statics are never registered in this class's own body-id
    /// map) -- always branch on <see cref="IsStatic"/> before interpreting <see cref="Id"/>, the same
    /// convention <see cref="PhysicsWorld.ContactPoint"/> already uses for its A/B + StaticA/StaticB.</param>
    /// <param name="Point">World-space hit point (<c>origin + direction * Distance</c>).</param>
    /// <param name="Normal">World-space surface normal at the hit point.</param>
    /// <param name="Distance">Distance along the ray in world metres. <see cref="Raycast"/> normalizes
    /// <c>direction</c> before casting, so this is independent of the input direction's length
    /// (<c>Point == origin + normalize(direction) * Distance</c>).</param>
    /// <param name="IsStatic">True if the hit collidable is a static (only possible when
    /// <see cref="Raycast"/> was called with <c>includeStatics: true</c>).</param>
    public readonly record struct RaycastHit(int Id, Vector3 Point, Vector3 Normal, float Distance, bool IsStatic);

    /// <summary>
    /// Casts a ray against the simulation and returns the nearest hit. By default only <b>dynamic</b>
    /// bodies are considered (kinematics -- e.g. a mouse-grab cursor -- are never picked; statics are
    /// excluded too, since existing callers were written against dynamic-only semantics). Pass
    /// <paramref name="includeStatics"/><c> = true</c> to widen the filter to dynamics + statics (e.g.
    /// ground/slope detection, which needs to hit the ground -- itself a static). This is opt-in,
    /// not a change to the default, so no existing caller's behavior changes silently.
    ///
    /// <para><b>2026-08-31: <paramref name="includeKinematics"/> admits kinematic bodies too</b>, and
    /// it is a SEPARATE opt-in rather than a widening of <paramref name="includeStatics"/>. Widening
    /// was tried first, on the reasoning that a kinematic body is immovable and solid and therefore
    /// belongs to "the world's surfaces" -- and it broke nine tests across four unrelated features,
    /// because this game's kinematic bodies are not only platforms. A door leaf is kinematic and
    /// stands IN a doorway, so a nav probe that started seeing it found the doorway roofed and
    /// unwalkable; the ambulance, the supply van and every patient held on a table are kinematic too.
    /// "Immovable solid" and "part of the ground" are not the same set, and only the caller knows
    /// which one it is asking about.</para>
    ///
    /// <para><b>What it is for.</b> Triage's train maps give each wagon a kinematic deck so the two
    /// parallel trains can run at different speeds and change places. Without this the player walked
    /// straight off the front of a wagon they were visibly standing on, because
    /// <c>PlayerController</c>'s ground ray -- the ray that produces <c>Grounded</c>, coyote time and
    /// therefore the jump -- reported nothing under them. Nothing had caught the hole before because
    /// every other moving platform in the game is ridden by CONTACT, which was always correct; only a
    /// ray sees this gap.</para>
    ///
    /// <para><see cref="RaycastHit.IsStatic"/> keeps its narrow meaning -- literally a static -- so a
    /// caller that needs to tell a platform from the ground still can.</para>
    ///
    /// <para><b>2026-09-05: <paramref name="includeDynamics"/> can turn dynamics OFF</b>, which is the
    /// one thing the filter could not express before -- every overload admitted dynamics
    /// unconditionally. A caller asking "what does the LEVEL look like here" (Triage's
    /// <c>TriageNavGrid</c> surface probe is the case that forced this) has to look past loose bodies,
    /// and the only way to do that from outside was to cast, notice the hit was dynamic, and cast again
    /// from just below it. <b>That does not work</b>: a ray restarted inside a convex body hits that
    /// same body again at its own origin, so the retry crawls down through it by whatever epsilon the
    /// caller chose rather than emerging beneath it. The nav grid's version stepped 2 cm at a time with
    /// a budget of eight tries, so anything thicker than 0.16 m came back as "no surface at all" -- a
    /// hole in the floor wherever a crate happened to be standing. Excluding them in the FILTER is both
    /// correct and one ray instead of up to eight.</para>
    /// </summary>
    public RaycastHit? Raycast(
        Vector3 origin,
        Vector3 direction,
        float maxDistance,
        bool includeStatics = false,
        bool includeKinematics = false,
        bool includeDynamics = true)
    {
        ThrowIfDisposed();
        if (maxDistance <= 0f || !float.IsFinite(maxDistance))
        {
            throw new ArgumentOutOfRangeException(nameof(maxDistance));
        }

        if (direction.LengthSquared() <= float.Epsilon || !float.IsFinite(direction.LengthSquared()))
        {
            throw new ArgumentOutOfRangeException(nameof(direction), "direction must be finite and non-zero.");
        }

        var normalizedDirection = Vector3.Normalize(direction);
        var handler = new NearestHitHandler(includeStatics, includeKinematics, includeDynamics);
        _simulation.RayCast(origin, normalizedDirection, maxDistance, ref handler);
        if (!handler.Hit)
        {
            return null;
        }

        return new RaycastHit(handler.Id, origin + normalizedDirection * handler.T, handler.Normal, handler.T, handler.IsStatic);
    }

    private struct NearestHitHandler : IRayHitHandler
    {
        private readonly bool _includeStatics;
        private readonly bool _includeKinematics;
        private readonly bool _includeDynamics;
        public bool Hit;
        public int Id;
        public bool IsStatic;
        public Vector3 Normal;
        public float T;

        public NearestHitHandler(bool includeStatics, bool includeKinematics, bool includeDynamics)
        {
            _includeStatics = includeStatics;
            _includeKinematics = includeKinematics;
            _includeDynamics = includeDynamics;
        }

        public readonly bool AllowTest(CollidableReference collidable) =>
            collidable.Mobility switch
            {
                CollidableMobility.Dynamic => _includeDynamics,
                CollidableMobility.Static => _includeStatics,
                _ => _includeKinematics,
            };

        public readonly bool AllowTest(CollidableReference collidable, int childIndex) => true;

        public void OnRayHit(in RayData ray, ref float maximumT, float t, in Vector3 normal, CollidableReference collidable, int childIndex)
        {
            Hit = true;
            IsStatic = collidable.Mobility == CollidableMobility.Static;
            Id = IsStatic ? collidable.StaticHandle.Value : collidable.BodyHandle.Value;
            Normal = normal;
            T = t;
            maximumT = t;
        }
    }
}
