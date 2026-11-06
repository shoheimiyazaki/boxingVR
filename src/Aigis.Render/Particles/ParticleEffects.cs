using System.Numerics;

namespace Aigis.Render;

/// <summary>
/// Ready-made <see cref="ParticleEffect"/> recipes for the effects a game actually asks for —
/// footstep dust, blood spray, explosion fire, smoke, sparks — plus
/// <see cref="EmitExplosion"/>, which layers three of them into the one effect nobody wants to
/// re-derive by hand.
/// <para>
/// These are tuned starting points, not constants to obey: every one is a <c>record</c>, so a
/// game that wants a bigger, wetter, slower version writes
/// <c>ParticleEffects.BloodSpatter with { Count = 60, Drag = 1.5f }</c> rather than a new
/// preset. Presets deliberately leave <see cref="ParticleEffect.GroundY"/> null — only the
/// caller knows where its floor is.
/// </para>
/// </summary>
public static class ParticleEffects
{
    /// <summary>Footstep/landing dust: a low, wide, short-lived puff that drifts up and hangs.
    /// The heavy <see cref="ParticleEffect.Drag"/> plus near-zero gravity is what separates
    /// "dust" from "gravel" — dust barely falls, it fades where it was kicked up.</summary>
    public static ParticleEffect Dust { get; } = new()
    {
        Count = 14,
        LifeSeconds = 0.7f,
        LifeJitter = 0.35f,
        Speed = 1.2f,
        SpeedJitter = 0.5f,
        ConeDegrees = 65f,
        StartSize = 0.12f,
        EndSize = 0.38f,
        SizeJitter = 0.35f,
        StartColor = new Vector4(0.72f, 0.66f, 0.54f, 0.55f),
        EndColor = new Vector4(0.76f, 0.72f, 0.64f, 0f),
        Acceleration = new Vector3(0f, -1.2f, 0f),
        Drag = 2.4f,
        SpinRadiansPerSecond = 1.2f,
        Blend = ParticleBlend.Alpha,
    };

    /// <summary>Blood spray from a wound or impact: fast, narrow, ballistic droplets that
    /// shrink as they fly. Gravity is the reference stage's −36 rather than −9.81 (see
    /// tasks/board.md's note on the port's real gravity) so droplets arc at the same rate as
    /// everything else in a Triage-like scene; a game on standard gravity should override it.
    /// Set <see cref="ParticleEffect.GroundY"/> to the floor to make the spray splat and slide
    /// instead of falling through.</summary>
    public static ParticleEffect BloodSpatter { get; } = new()
    {
        Count = 30,
        LifeSeconds = 0.9f,
        LifeJitter = 0.4f,
        Speed = 4.5f,
        SpeedJitter = 0.55f,
        ConeDegrees = 32f,
        // Droplets are physically tiny, but a 3-4 cm sprite disappears at any normal camera
        // distance — sized to read at the ~10 m framing games/Game.ParticleLab is verified at,
        // which a filmstrip review showed the physically-honest size failing at.
        StartSize = 0.11f,
        EndSize = 0.07f,
        SizeJitter = 0.45f,
        StartColor = new Vector4(0.62f, 0.05f, 0.06f, 1f),
        EndColor = new Vector4(0.34f, 0.02f, 0.02f, 0f),
        Acceleration = new Vector3(0f, -36f, 0f),
        Drag = 0.25f,
        GroundBounce = 0.05f,
        Blend = ParticleBlend.Alpha,
    };

    /// <summary>The bright core of an explosion: an omnidirectional (<c>ConeDegrees = 180</c>)
    /// additive fireball that expands and cools from yellow to deep orange. Additive is the
    /// whole point — fire is light, so it must brighten what is behind it rather than hide it.
    /// For a complete explosion use <see cref="EmitExplosion"/>, which adds smoke and sparks.</summary>
    public static ParticleEffect ExplosionFire { get; } = new()
    {
        Count = 40,
        LifeSeconds = 0.55f,
        LifeJitter = 0.4f,
        Speed = 7f,
        SpeedJitter = 0.6f,
        ConeDegrees = 180f,
        StartSize = 0.35f,
        EndSize = 0.95f,
        SizeJitter = 0.4f,
        StartColor = new Vector4(1f, 0.86f, 0.38f, 1f),
        EndColor = new Vector4(0.85f, 0.16f, 0.03f, 0f),
        Acceleration = new Vector3(0f, 2.5f, 0f),
        Drag = 3.8f,
        SpinRadiansPerSecond = 2.5f,
        Blend = ParticleBlend.Additive,
    };

    /// <summary>Slow, rising, expanding smoke — an explosion's aftermath, a fire's plume, a
    /// damaged machine. Long-lived by particle standards (over two seconds), so a continuous
    /// source should go through <see cref="ParticleEmitter"/> rather than emitting per step.</summary>
    public static ParticleEffect Smoke { get; } = new()
    {
        Count = 18,
        LifeSeconds = 2.2f,
        LifeJitter = 0.35f,
        Speed = 1.3f,
        SpeedJitter = 0.5f,
        ConeDegrees = 40f,
        StartSize = 0.4f,
        EndSize = 1.5f,
        SizeJitter = 0.35f,
        StartColor = new Vector4(0.2f, 0.19f, 0.18f, 0.6f),
        EndColor = new Vector4(0.38f, 0.37f, 0.36f, 0f),
        Acceleration = new Vector3(0f, 0.7f, 0f),
        Drag = 1.1f,
        SpinRadiansPerSecond = 0.7f,
        Blend = ParticleBlend.Alpha,
    };

    /// <summary>Hot metal sparks: tiny, very fast, very short-lived, additive, and heavy enough
    /// to fall visibly within their lifetime. Impacts, grinding, an axe on stone.</summary>
    public static ParticleEffect Spark { get; } = new()
    {
        Count = 20,
        LifeSeconds = 0.45f,
        LifeJitter = 0.5f,
        Speed = 6f,
        SpeedJitter = 0.7f,
        ConeDegrees = 180f,
        StartSize = 0.1f,
        EndSize = 0.03f,
        SizeJitter = 0.5f,
        StartColor = new Vector4(1f, 0.95f, 0.6f, 1f),
        EndColor = new Vector4(1f, 0.35f, 0.08f, 0f),
        Acceleration = new Vector3(0f, -25f, 0f),
        Drag = 0.6f,
        Blend = ParticleBlend.Additive,
    };

    /// <summary>A gentle green cross-motif sparkle, rising and fading: recovery/healing.
    /// TASK-134 — ported from the Unity reference's own <c>RecoveryEffect.cs</c> (Alpha,
    /// <c>fb 2026-06-10</c>), a continuous <c>ParticleSystem</c> attached to a patient while
    /// <c>BedRestPhase.Recovering</c> that emits at 14/s, not a one-shot burst: pair this preset
    /// with <see cref="ParticleEmitter"/> rather than a bare <see cref="ParticleSystem.Emit"/> call,
    /// the same way the reference drives its own <c>rateOverTime</c>. <see cref="ParticleEffect.Count"/>
    /// is 1 for exactly that reason — the emitter's burst rate carries the "particles per second", not
    /// this recipe's own burst size.
    /// <para>
    /// Colour, size, speed, and the reference's Cone(14°)/upward emission are ported directly
    /// (<c>CrossColor</c> = (0.30, 0.95, 0.42, 0.95); <c>startSize</c> = 0.28; <c>startSpeed</c> =
    /// 0.9). The reference's <c>gravityModifier = -0.15</c> against this stage's own −36 m/s² gravity
    /// (see <see cref="BloodSpatter"/>'s doc comment) works out to +5.4 m/s² — particles genuinely
    /// accelerate upward — so <see cref="ParticleEffect.Drag"/> is added here (absent from the
    /// reference, which has none) to keep that from reading as particles flying off rather than floating, matching
    /// <see cref="Dust"/>'s own drag-over-ballistics choice for the same "hangs, does not fly" read.
    /// A textured "+" billboard is not reproduced — the built-in sprite is a radially symmetric soft
    /// disc (see <see cref="ParticleInstance.Rotation"/>'s doc comment) — so this reads as a rising
    /// green sparkle rather than a literal cross; the colour and motion carry the "healing" meaning.
    /// </para></summary>
    public static ParticleEffect Recovery { get; } = new()
    {
        Count = 1,
        LifeSeconds = 1.3f,
        LifeJitter = 0.25f,
        Speed = 0.9f,
        SpeedJitter = 0.35f,
        ConeDegrees = 14f,
        StartSize = 0.28f,
        EndSize = 0.28f,
        SizeJitter = 0.2f,
        StartColor = new Vector4(0.30f, 0.95f, 0.42f, 0.95f),
        EndColor = new Vector4(0.30f, 0.95f, 0.42f, 0f),
        Acceleration = new Vector3(0f, 5.4f, 0f),
        Drag = 1.4f,
        Blend = ParticleBlend.Alpha,
    };

    /// <summary>
    /// The full explosion: <see cref="ExplosionFire"/> for the flash, <see cref="Spark"/> for
    /// the debris, and <see cref="Smoke"/> for what is left behind. Three layers rather than one
    /// preset because they need different blend modes — an additive-only explosion has no
    /// substance, and an alpha-only one has no light.
    /// <para>
    /// The smoke is emitted at the same instant rather than delayed: <see cref="ParticleSystem"/>
    /// holds no timers, so "later" would have to live in the caller. Its long lifetime and slow
    /// rise mean it reads as the aftermath anyway, since the fire is gone within ~0.6 s.
    /// </para>
    /// </summary>
    /// <param name="system">The pool to spawn into.</param>
    /// <param name="position">World-space centre of the blast.</param>
    /// <param name="scale">Spatial multiplier for all three layers (see
    /// <see cref="ParticleSystem.Emit"/>).</param>
    /// <returns>Total particles actually spawned across the three layers.</returns>
    public static int EmitExplosion(ParticleSystem system, Vector3 position, float scale = 1f)
    {
        ArgumentNullException.ThrowIfNull(system);
        return system.Emit(ExplosionFire, position, Vector3.UnitY, scale)
            + system.Emit(Spark, position, Vector3.UnitY, scale)
            + system.Emit(Smoke, position + new Vector3(0f, 0.3f * scale, 0f), Vector3.UnitY, scale);
    }
}

/// <summary>
/// Turns a burst recipe into a <i>continuous</i> source — a smoke plume, dust while a character
/// keeps running, a leaking pipe — by holding the fractional-burst remainder between steps.
/// Without this, a caller either emits a full burst every step (far too much) or hand-rolls the
/// same accumulator each time.
/// <para>
/// Mutable and stepped, like <see cref="ParticleSystem"/> itself, and deterministic for the same
/// reason: the accumulator only ever advances by the <c>dt</c> it is given.
/// </para>
/// </summary>
public sealed class ParticleEmitter
{
    /// <summary>Upper bound on bursts emitted from a single <see cref="Update"/>. A long
    /// <c>dt</c> (a debugger pause, a level load) would otherwise dump the accumulated backlog
    /// into one frame — the visual equivalent of a physics tunnelling spike. The remainder is
    /// discarded rather than carried, so the emitter recovers immediately instead of catching up.</summary>
    private const int MaxBurstsPerUpdate = 4;

    private float _pendingBursts;

    /// <param name="effect">The recipe each burst uses.</param>
    /// <param name="burstsPerSecond">How often a burst fires while the emitter is active.</param>
    public ParticleEmitter(ParticleEffect effect, float burstsPerSecond)
    {
        ArgumentNullException.ThrowIfNull(effect);
        if (!(burstsPerSecond > 0f) || !float.IsFinite(burstsPerSecond))
        {
            throw new ArgumentOutOfRangeException(nameof(burstsPerSecond), burstsPerSecond, "Burst rate must be positive and finite.");
        }

        Effect = effect;
        BurstsPerSecond = burstsPerSecond;
    }

    /// <summary>The recipe each burst uses.</summary>
    public ParticleEffect Effect { get; }

    /// <summary>Bursts per second while active.</summary>
    public float BurstsPerSecond { get; }

    /// <summary>
    /// Advances the emitter by <paramref name="dt"/> and fires whatever bursts are due at
    /// <paramref name="position"/>.
    /// </summary>
    /// <param name="system">Pool to spawn into.</param>
    /// <param name="dt">Fixed step, seconds.</param>
    /// <param name="position">Where the source is right now.</param>
    /// <param name="direction">Emission cone axis (see <see cref="ParticleSystem.Emit"/>).</param>
    /// <param name="scale">Spatial multiplier (see <see cref="ParticleSystem.Emit"/>).</param>
    /// <returns>Particles spawned this update.</returns>
    public int Update(ParticleSystem system, float dt, Vector3 position, Vector3 direction = default, float scale = 1f)
    {
        ArgumentNullException.ThrowIfNull(system);
        if (!(dt > 0f) || !float.IsFinite(dt))
        {
            throw new ArgumentOutOfRangeException(nameof(dt), dt, "Emitter dt must be positive and finite.");
        }

        _pendingBursts += dt * BurstsPerSecond;
        var spawned = 0;
        var bursts = 0;
        while (_pendingBursts >= 1f && bursts < MaxBurstsPerUpdate)
        {
            _pendingBursts -= 1f;
            bursts++;
            spawned += system.Emit(Effect, position, direction, scale);
        }

        if (_pendingBursts >= 1f)
        {
            _pendingBursts = 0f;
        }

        return spawned;
    }

    /// <summary>Drops the fractional remainder, so the next <see cref="Update"/> starts a fresh
    /// interval. Call when the source stops and restarts (a character stops running) — otherwise
    /// a nearly-full accumulator fires a burst on the very first step of the next run.</summary>
    public void Reset() => _pendingBursts = 0f;
}
