using System.Numerics;

namespace Aigis.Physics;

/// <summary>
/// Contact (collision-touch) observation, kept in its own partial file. The narrow-phase callback
/// (<see cref="NarrowPhaseCallbacks"/>) appends each touching pair's manifold summary to a shared
/// collector during <see cref="Step(float)"/>; this exposes it as an agent's collision sense — e.g.
/// foot-ground contact for a balance controller, which pixels and body poses can't show directly.
/// </summary>
public sealed partial class PhysicsWorld
{
    /// <summary>One touching contact pair from the most recent step's narrow phase.</summary>
    /// <param name="A">Handle value of the first collidable — a body id when <paramref name="StaticA"/>
    /// is false, an (unrelated-namespace) static handle when it is true. When exactly one side is
    /// static, A is always the dynamic side (see <paramref name="Normal"/>). NOTE: a body id is the
    /// raw Bepu handle value, stable only while bodies are never removed (anti-pattern A2) — a future
    /// despawn feature must route contact ids through the same id→handle map.</param>
    /// <param name="StaticA">True if the first collidable is a static (e.g. the ground).</param>
    /// <param name="B">Handle value of the second collidable (see <paramref name="A"/>). When exactly
    /// one side is static, B is always that static side.</param>
    /// <param name="StaticB">True if the second collidable is a static.</param>
    /// <param name="Normal">Contact normal in world space; points from collidable B to collidable A.
    /// Bepu's A/B order is arbitrary, so this is canonicalized: when one side is static the normal
    /// points away from the static surface (from B, the static, to A, the dynamic) — e.g. +Y for a
    /// foot resting on the ground. For a body-body contact neither side is static, so interpret the
    /// sign relative to the reported A/B, not as an absolute direction.</param>
    /// <param name="Depth">Depth in metres of the deepest contact: positive when penetrating, and
    /// down to a small negative tolerance (~-0.01 m) for a contact just shy of touching.</param>
    /// <param name="PointCount">Number of contact points in the manifold (a flat rest has more).</param>
    public readonly record struct ContactPoint(int A, bool StaticA, int B, bool StaticB, Vector3 Normal, float Depth, int PointCount);

    // Wired into NarrowPhaseCallbacks at construction (see the constructor) and cleared at the start
    // of each Step, so Contacts() reflects the most recent step only. Stepping is single-threaded
    // (no thread dispatcher passed to Timestep), so the collected order is deterministic.
    private readonly ContactCollector _contactCollector = new();

    /// <summary>The touching contact pairs from the most recent <see cref="Step(float)"/> — an agent's
    /// collision sense. Bepu also generates speculative contacts for nearby-but-not-yet-touching
    /// pairs; those (deep-negative depth) are filtered out, so a returned pair means "actually in
    /// contact" (its deepest contact is within a small tolerance of, or past, the surface).
    /// <para>Caveat: Bepu skips narrow phase for sleeping islands, so a body that has settled and gone
    /// to sleep stops reporting its contacts. This suits the intended use (foot-ground sensing for an
    /// actively-moving, awake ragdoll); a resting body's static contact is not continuously re-reported.</para></summary>
    public IReadOnlyList<ContactPoint> Contacts()
    {
        ThrowIfDisposed();
        return _contactCollector.Records.ToArray();
    }
}

/// <summary>Reference-type sink the (struct) <see cref="NarrowPhaseCallbacks"/> appends to during the
/// narrow phase — a class so the readonly callback can add to it without mutating its own struct.</summary>
internal sealed class ContactCollector
{
    public readonly List<PhysicsWorld.ContactPoint> Records = new();

    public void Clear() => Records.Clear();
}
