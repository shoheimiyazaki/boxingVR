using System.Numerics;

namespace Aigis.Render;

/// <summary>How a particle's colour combines with what is already in the frame.</summary>
public enum ParticleBlend
{
    /// <summary>Standard src-alpha / one-minus-src-alpha — dust, smoke, blood: matter that
    /// <i>occludes</i> what is behind it.</summary>
    Alpha,

    /// <summary>src-alpha / one — fire, sparks, muzzle flashes: light that <i>adds</i> to what
    /// is behind it. Order-independent, so these are never depth-sorted (see
    /// <see cref="ParticlePacker"/>).</summary>
    Additive,
}

/// <summary>
/// One camera-facing billboard to draw this frame — the particle equivalent of
/// <see cref="PrimitiveInstance"/>, and like it a pure description with no simulation state:
/// a caller that wants to draw particles from its own source (a replay, a hand-authored test
/// scene, a future GPU sim) never has to own a <see cref="ParticleSystem"/>.
/// </summary>
/// <param name="Position">World-space centre of the billboard.</param>
/// <param name="Size">World-space edge length of the (square) billboard. Non-positive means
/// "skip this one" rather than an error — a fading-out particle legitimately reaches zero.</param>
/// <param name="Color">RGBA, drawn unlit. Alpha multiplies the sprite's own soft-disc falloff.</param>
/// <param name="Rotation">Roll around the view axis, radians, counter-clockwise on screen.
/// <b>Currently a visual no-op</b>: the built-in sprite is a radially symmetric soft disc (see
/// <see cref="ShaderSource.ParticleFrag"/>), so spinning it changes no pixel. It is carried
/// through the vertex path and the instance layout anyway because it is the one piece a textured
/// or asymmetric sprite cannot be added without — and at that point its sign convention becomes
/// observable and must be pinned by a render test.</param>
/// <param name="Blend">Which of the two blend pipelines draws it.</param>
public readonly record struct ParticleInstance(
    Vector3 Position,
    float Size,
    Vector4 Color,
    float Rotation = 0f,
    ParticleBlend Blend = ParticleBlend.Alpha);

/// <summary>
/// A reusable recipe for one burst of particles: what a "footstep dust puff" or "blood spray"
/// <i>is</i>, independent of where and when it happens. Emission reads it, randomises within
/// the jitter bands, and never stores a reference to it — so a preset in
/// <see cref="ParticleEffects"/> can be a shared static, and a caller can tweak one with
/// <c>with</c> (<c>ParticleEffects.Dust with { Count = 30 }</c>) without affecting anyone else.
/// <para>
/// Every jitter is a <i>fraction</i> (0..1) of its base value rather than an absolute range, so
/// scaling an effect up or down (<see cref="ParticleSystem.Emit"/>'s <c>scale</c>) keeps its
/// character instead of turning a big burst uniform and a small one chaotic.
/// </para>
/// </summary>
/// <summary>One particle's first touch of its effect's ground plane — see
/// <see cref="ParticleEffect.ReportsGroundContact"/>. Carries what a caller needs to leave a mark
/// proportional to the impact: where it landed, how hard, and how big the droplet was.</summary>
/// <param name="Position">World point of contact — already snapped onto the ground plane.</param>
/// <param name="ImpactSpeed">Speed along the ground normal at contact, in m/s. A fast droplet
/// splashes wider than one that dribbled down.</param>
/// <param name="Size">The particle's current drawn size, in metres.</param>
public readonly record struct ParticleGroundContact(Vector3 Position, float ImpactSpeed, float Size);

public sealed record ParticleEffect
{
    /// <summary>How many particles one <see cref="ParticleSystem.Emit"/> call spawns.</summary>
    public int Count { get; init; } = 12;

    /// <summary>Mean lifetime in seconds. Must be positive.</summary>
    public float LifeSeconds { get; init; } = 0.6f;

    /// <summary>Lifetime spread as a fraction of <see cref="LifeSeconds"/> (0 = every particle
    /// dies at exactly the same time, which reads as a hard "pop").</summary>
    public float LifeJitter { get; init; } = 0.3f;

    /// <summary>Mean initial speed along the emission direction, m/s.</summary>
    public float Speed { get; init; } = 2f;

    /// <summary>Speed spread as a fraction of <see cref="Speed"/>.</summary>
    public float SpeedJitter { get; init; } = 0.4f;

    /// <summary>Half-angle of the emission cone around the direction passed to
    /// <see cref="ParticleSystem.Emit"/>, in degrees. 0 = a perfect beam, 180 = a full sphere
    /// (what a burst with no meaningful direction, like an explosion, wants).</summary>
    public float ConeDegrees { get; init; } = 45f;

    /// <summary>Billboard size at birth, metres.</summary>
    public float StartSize { get; init; } = 0.15f;

    /// <summary>Billboard size at death, metres. Larger than <see cref="StartSize"/> for
    /// expanding smoke/dust, smaller for shrinking sparks and droplets.</summary>
    public float EndSize { get; init; } = 0.3f;

    /// <summary>Per-particle size spread as a fraction of both size endpoints.</summary>
    public float SizeJitter { get; init; } = 0.3f;

    /// <summary>Colour at birth. Alpha participates: it is the particle's peak opacity.</summary>
    public Vector4 StartColor { get; init; } = Vector4.One;

    /// <summary>Colour at death. Nearly always alpha 0 — a particle that vanishes at full
    /// opacity pops.</summary>
    public Vector4 EndColor { get; init; } = new(1f, 1f, 1f, 0f);

    /// <summary>Constant acceleration, m/s². Gravity for debris, a positive Y for rising smoke,
    /// zero for a spark that should fly straight.</summary>
    public Vector3 Acceleration { get; init; } = new(0f, -9.81f, 0f);

    /// <summary>Exponential velocity damping, per second: <c>v *= exp(-Drag * dt)</c>. This is
    /// what makes dust <i>hang</i> instead of flying off ballistically. 0 = no drag.</summary>
    public float Drag { get; init; }

    /// <summary>Mean spin rate around the view axis, rad/s. Each particle gets a random start
    /// angle and a sign, so a positive value means "spins at about this rate, either way".
    /// Has no visible effect with the current radially symmetric sprite — see
    /// <see cref="ParticleInstance.Rotation"/>.</summary>
    public float SpinRadiansPerSecond { get; init; }

    /// <summary>Which blend pipeline draws this effect (see <see cref="ParticleBlend"/>).</summary>
    public ParticleBlend Blend { get; init; } = ParticleBlend.Alpha;

    /// <summary>Optional world Y of a floor plane the particles collide with. Null = they fall
    /// through everything. This is a single plane on purpose: routing particles through
    /// <c>Aigis.Physics</c> would make thousands of visual-only sprites cost broadphase pairs
    /// and — worse — would let a decorative effect perturb the deterministic simulation the
    /// verify loop depends on (hard rule 5).</summary>
    public float? GroundY { get; init; }

    /// <summary>Fraction of vertical speed kept on a <see cref="GroundY"/> bounce (0 = splat
    /// flat and slide, 1 = a perfectly elastic hop). Ignored when <see cref="GroundY"/> is null.</summary>
    public float GroundBounce { get; init; } = 0.2f;

    /// <summary>
    /// Whether a particle from this recipe reports the moment it first reaches
    /// <see cref="GroundY"/>, through <see cref="ParticleSystem.Step"/>'s optional callback.
    /// <para>
    /// Off by default because most effects are purely decorative and the callback is not free to
    /// reason about. It exists for the one thing a particle CANNOT express on its own: leaving a
    /// permanent mark where it landed. <c>Game.Triage</c> turns it on for blood so a spray burns
    /// into the floor's stain sheet exactly where the droplets fell, rather than fading and leaving
    /// a clean floor under it (user request, 2026-08-16).
    /// </para>
    /// <para>Reported ONCE per particle, on first contact — a droplet that then skids along the
    /// floor is one splash, not sixty. Ignored when <see cref="GroundY"/> is null: without a ground
    /// plane there is no contact to report.</para>
    /// </summary>
    public bool ReportsGroundContact { get; init; }

    /// <summary>Throws if this recipe could not produce a finite, drawable particle. Called by
    /// <see cref="ParticleSystem.Emit"/> rather than in an initialiser so a preset stays a plain
    /// <c>record</c> that a caller can freely <c>with</c>-modify — the boundary that matters is
    /// the one where the values are actually consumed.</summary>
    /// <param name="paramName">The calling method's own parameter name, so the thrown exception
    /// blames the argument the caller actually passed rather than this type.</param>
    public void Validate(string paramName)
    {
        Require(Count >= 0, nameof(Count), "must be non-negative");
        Require(LifeSeconds > 0f && float.IsFinite(LifeSeconds), nameof(LifeSeconds), "must be positive and finite");
        Require(IsFraction(LifeJitter), nameof(LifeJitter), "must be in [0,1]");
        Require(IsFraction(SpeedJitter), nameof(SpeedJitter), "must be in [0,1]");
        Require(IsFraction(SizeJitter), nameof(SizeJitter), "must be in [0,1]");
        Require(float.IsFinite(Speed), nameof(Speed), "must be finite");
        Require(ConeDegrees >= 0f && ConeDegrees <= 180f, nameof(ConeDegrees), "must be in [0,180] degrees");
        Require(StartSize >= 0f && float.IsFinite(StartSize), nameof(StartSize), "must be non-negative and finite");
        Require(EndSize >= 0f && float.IsFinite(EndSize), nameof(EndSize), "must be non-negative and finite");
        Require(Drag >= 0f && float.IsFinite(Drag), nameof(Drag), "must be non-negative and finite");
        Require(float.IsFinite(SpinRadiansPerSecond), nameof(SpinRadiansPerSecond), "must be finite");
        Require(IsFinite3(Acceleration), nameof(Acceleration), "must be finite");
        Require(IsFinite4(StartColor), nameof(StartColor), "must be finite");
        Require(IsFinite4(EndColor), nameof(EndColor), "must be finite");
        Require(GroundY is null || float.IsFinite(GroundY.Value), nameof(GroundY), "must be finite when set");
        Require(IsFraction(GroundBounce), nameof(GroundBounce), "must be in [0,1]");

        void Require(bool condition, string member, string requirement)
        {
            if (!condition)
            {
                throw new ArgumentOutOfRangeException(paramName, $"{nameof(ParticleEffect)}.{member} {requirement}.");
            }
        }

        static bool IsFraction(float value) => value >= 0f && value <= 1f;

        static bool IsFinite3(Vector3 v) => float.IsFinite(v.X) && float.IsFinite(v.Y) && float.IsFinite(v.Z);

        static bool IsFinite4(Vector4 v) =>
            float.IsFinite(v.X) && float.IsFinite(v.Y) && float.IsFinite(v.Z) && float.IsFinite(v.W);
    }
}
