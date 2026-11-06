using System.Numerics;

namespace Aigis.Render;

/// <summary>
/// A fixed-capacity pool of simulated particles: footstep dust, blood spray, explosion fire,
/// smoke, sparks. One system owns one pool; a game typically keeps a single system for all its
/// effects and calls <see cref="Emit"/> from wherever an event happens.
/// <para>
/// <b>Deliberately not physics.</b> Particles integrate here — position/velocity/drag and at
/// most one ground plane — and never enter <c>Aigis.Physics</c>. Two reasons: thousands of
/// decorative sprites would cost real broadphase pairs, and (hard rule 5) a purely visual effect
/// must not be able to perturb the deterministic simulation the verify loop asserts against.
/// </para>
/// <para>
/// <b>Determinism.</b> Same seed + same <see cref="Emit"/>/<see cref="Step"/> call sequence ⇒
/// bit-identical particles, because the only randomness comes from this system's own seeded
/// generator. It follows that the calls themselves must be deterministic: emit from fixed-step
/// game logic, never from a render callback that runs a variable number of times per step.
/// </para>
/// <para>
/// <b>Mutability.</b> A pool is mutable state by nature — the same pragmatic exception
/// <c>PhysicsWorld</c> takes. Everything crossing the boundary out of it (<see cref="ParticleInstance"/>,
/// <see cref="ParticleEffect"/>) is an immutable value.
/// </para>
/// </summary>
public sealed class ParticleSystem
{
    /// <summary>Pool size when the caller does not pick one. Roughly a dozen simultaneous
    /// bursts at default <see cref="ParticleEffect.Count"/>s, which is well past what a scene
    /// reads as busy while still being a ~500 KB array.</summary>
    public const int DefaultCapacity = 4096;

    /// <summary>Fraction of horizontal speed kept when a particle hits
    /// <see cref="ParticleEffect.GroundY"/> — dust skids outward along the floor instead of
    /// stopping dead, which is what sells a footstep puff.</summary>
    private const float GroundFriction = 0.6f;

    private readonly Particle[] _particles;
    private readonly Random _random;
    private int _count;

    /// <summary>
    /// A multiplier applied to every emitted particle's <see cref="ParticleEffect.Acceleration"/>,
    /// so a scene can state its OWN gravity once instead of rewriting each preset. 1 (the default)
    /// leaves every existing scene byte-for-byte as it was.
    ///
    /// <para><b>Why the presets cannot just be right.</b> A <see cref="ParticleEffects"/> recipe is a
    /// LOOK — how fast blood arcs, how lazily a spark falls — and its acceleration is picked to match
    /// the world it was authored for (Triage's own −36, see <see cref="ParticleEffects.BloodSpatter"/>).
    /// That makes it a per-scene constant wearing a per-effect disguise, and the moment one game
    /// carries more than one gravity the disguise fails: measured on the 宇宙船 stage, blood sprayed
    /// in a WEIGHTLESS compartment fell to the deck at 36 m/s² while the body it came out of hung
    /// motionless beside it.</para>
    ///
    /// <para><b>A scale rather than an absolute vector</b>, because a preset's acceleration is not
    /// always exactly gravity — sparks use −25 to read as lighter than everything else — and an
    /// absolute would flatten those distinctions into one number. Scaling keeps each recipe's
    /// relationship to its own world and only changes which world that is. Zero is legal and is what
    /// weightlessness means.</para>
    /// </summary>
    /// <exception cref="ArgumentOutOfRangeException">Set to a non-finite or negative value.</exception>
    public float GravityScale
    {
        get => _gravityScale;
        set
        {
            if (!float.IsFinite(value) || value < 0f)
            {
                throw new ArgumentOutOfRangeException(nameof(value), value, "Must be finite and non-negative.");
            }

            _gravityScale = value;
        }
    }

    private float _gravityScale = 1f;

    /// <param name="capacity">Maximum simultaneously-alive particles. Emissions past this are
    /// dropped (see <see cref="DroppedCount"/>), never queued — a visual effect must not be able
    /// to build an unbounded backlog.</param>
    /// <param name="seed">Seed for this system's private generator. Fixed by default so a
    /// playtest replays identically; pass a per-session seed for run-to-run variety.</param>
    public ParticleSystem(int capacity = DefaultCapacity, int seed = 20260801)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(capacity, 1);
        _particles = new Particle[capacity];
        _random = new Random(seed);
    }

    /// <summary>Maximum simultaneously-alive particles.</summary>
    public int Capacity => _particles.Length;

    /// <summary>How many particles are alive right now.</summary>
    public int AliveCount => _count;

    /// <summary>How many particles have been refused since construction because the pool was
    /// full. Exposed rather than silently swallowed: a steadily climbing count is the signal
    /// that an emitter's rate or the pool's capacity is wrong, and an agent reading engine state
    /// can see it without a profiler.</summary>
    public long DroppedCount { get; private set; }

    /// <summary>
    /// Spawns <see cref="ParticleEffect.Count"/> particles at <paramref name="origin"/>, aimed
    /// into a cone around <paramref name="direction"/>.
    /// </summary>
    /// <param name="effect">The recipe (see <see cref="ParticleEffects"/> for ready-made ones).</param>
    /// <param name="origin">World-space burst origin.</param>
    /// <param name="direction">Cone axis. A zero/degenerate vector means straight up, so a
    /// caller with no meaningful direction (an explosion, a dust puff) can just omit it.</param>
    /// <param name="scale">Spatial multiplier applied to speed and both size endpoints — the
    /// same effect used for a small and a large version of itself. Acceleration is NOT scaled:
    /// gravity does not get stronger because the explosion is bigger. For the world's OWN gravity,
    /// see <see cref="GravityScale"/> — a different question with a different answer.</param>
    /// <returns>How many particles were actually spawned (less than
    /// <see cref="ParticleEffect.Count"/> when the pool filled up).</returns>
    public int Emit(ParticleEffect effect, Vector3 origin, Vector3 direction = default, float scale = 1f)
    {
        ArgumentNullException.ThrowIfNull(effect);
        effect.Validate(nameof(effect));
        if (!(scale > 0f) || !float.IsFinite(scale))
        {
            throw new ArgumentOutOfRangeException(nameof(scale), scale, "Emit scale must be positive and finite.");
        }

        if (!float.IsFinite(origin.X) || !float.IsFinite(origin.Y) || !float.IsFinite(origin.Z))
        {
            throw new ArgumentOutOfRangeException(nameof(origin), origin, "Emit origin must be finite.");
        }

        var axis = direction.LengthSquared() > 1e-12f && float.IsFinite(direction.LengthSquared())
            ? Vector3.Normalize(direction)
            : Vector3.UnitY;
        var (tangent, bitangent) = OrthonormalBasis(axis);
        var cosMax = MathF.Cos(float.DegreesToRadians(effect.ConeDegrees));

        var spawned = 0;
        for (var i = 0; i < effect.Count; i++)
        {
            if (_count >= _particles.Length)
            {
                DroppedCount += effect.Count - i;
                break;
            }

            // Uniform over the spherical cap: sampling cos(theta) uniformly (rather than theta)
            // is what keeps a wide cone from bunching every particle around its axis.
            var cosTheta = 1f - (NextUnit() * (1f - cosMax));
            var sinTheta = MathF.Sqrt(MathF.Max(0f, 1f - (cosTheta * cosTheta)));
            var phi = NextUnit() * MathF.Tau;
            var spread = (tangent * MathF.Cos(phi)) + (bitangent * MathF.Sin(phi));
            var heading = (axis * cosTheta) + (spread * sinTheta);

            var sizeFactor = scale * Jittered(effect.SizeJitter);
            _particles[_count++] = new Particle
            {
                Position = origin,
                Velocity = heading * (effect.Speed * scale * Jittered(effect.SpeedJitter)),
                // Baked in at SPAWN rather than read every step, so a mid-round change to the scale
                // cannot retroactively re-arc droplets that are already in the air -- and so the
                // integration loop stays exactly the two lines it was.
                Acceleration = effect.Acceleration * _gravityScale,
                Age = 0f,
                Life = MathF.Max(1e-4f, effect.LifeSeconds * Jittered(effect.LifeJitter)),
                StartSize = effect.StartSize * sizeFactor,
                EndSize = effect.EndSize * sizeFactor,
                StartColor = effect.StartColor,
                EndColor = effect.EndColor,
                Rotation = NextUnit() * MathF.Tau,
                // Magnitude in [0.5, 1] of the nominal rate, random sign: a uniform draw over
                // [-rate, rate] would leave most particles visibly not spinning at all.
                Spin = effect.SpinRadiansPerSecond * (0.5f + (0.5f * NextUnit())) * (NextUnit() < 0.5f ? -1f : 1f),
                Drag = effect.Drag,
                GroundY = effect.GroundY ?? 0f,
                HasGround = effect.GroundY.HasValue,
                GroundBounce = effect.GroundBounce,
                ReportsGroundContact = effect.ReportsGroundContact && effect.GroundY.HasValue,
                Blend = effect.Blend,
            };
            spawned++;
        }

        return spawned;
    }

    /// <summary>
    /// Advances every live particle by <paramref name="dt"/> and retires the expired ones.
    /// Call this exactly once per fixed step, from the same place the physics step happens —
    /// a variable-rate call site would break the determinism contract in the class summary.
    /// </summary>
    /// <param name="dt">Fixed timestep.</param>
    /// <param name="onGroundContact">Called once per particle whose recipe sets
    /// <see cref="ParticleEffect.ReportsGroundContact"/>, at the step it first reaches its ground
    /// plane — the hook for turning a landed droplet into a permanent mark. Invoked in pool order,
    /// which is deterministic for a given emit/step sequence, so a caller that paints from it stays
    /// reproducible. Anything it does must be VISUAL: this runs inside the particle step, and a
    /// callback that touched the simulation would let a decorative effect perturb physics (see the
    /// "deliberately not physics" note on this class).</param>
    public void Step(float dt, Action<ParticleGroundContact>? onGroundContact = null)
    {
        if (!(dt > 0f) || !float.IsFinite(dt))
        {
            throw new ArgumentOutOfRangeException(nameof(dt), dt, "Particle step dt must be positive and finite.");
        }

        var i = 0;
        while (i < _count)
        {
            ref var particle = ref _particles[i];
            particle.Age += dt;
            if (particle.Age >= particle.Life)
            {
                // Swap-remove: keeps the live particles densely packed at the front so stepping
                // and rendering never walk dead slots. Reorders survivors, which is harmless —
                // draw order is decided by depth sorting, not by pool position.
                _particles[i] = _particles[--_count];
                _particles[_count] = default;
                continue;
            }

            particle.Velocity += particle.Acceleration * dt;
            if (particle.Drag > 0f)
            {
                particle.Velocity *= MathF.Exp(-particle.Drag * dt);
            }

            particle.Position += particle.Velocity * dt;
            particle.Rotation += particle.Spin * dt;

            if (particle.HasGround && particle.Position.Y < particle.GroundY)
            {
                particle.Position.Y = particle.GroundY;
                // Read BEFORE the bounce rewrites it: the impact is how fast it arrived, not what
                // is left of that afterwards.
                var impactSpeed = MathF.Abs(particle.Velocity.Y);
                particle.Velocity = new Vector3(
                    particle.Velocity.X * GroundFriction,
                    impactSpeed * particle.GroundBounce,
                    particle.Velocity.Z * GroundFriction);

                if (particle.ReportsGroundContact && !particle.HasLanded)
                {
                    particle.HasLanded = true;
                    onGroundContact?.Invoke(new ParticleGroundContact(
                        particle.Position,
                        impactSpeed,
                        float.Lerp(particle.StartSize, particle.EndSize, particle.Age / particle.Life)));
                }
            }

            i++;
        }
    }

    /// <summary>Retires every particle at once (scene reset, level change). Does not reset
    /// <see cref="DroppedCount"/> or the random stream — a reset scene should still replay
    /// identically, and the drop total is a session-level diagnostic.</summary>
    public void Clear()
    {
        Array.Clear(_particles, 0, _count);
        _count = 0;
    }

    /// <summary>
    /// Appends this frame's drawable billboards to <paramref name="destination"/> — appends
    /// rather than clears, so several systems (or a system plus hand-authored sprites) can feed
    /// one render call. Fully faded particles are still written; the renderer skips them.
    /// </summary>
    public void WriteInstances(ICollection<ParticleInstance> destination)
    {
        ArgumentNullException.ThrowIfNull(destination);
        for (var i = 0; i < _count; i++)
        {
            ref readonly var particle = ref _particles[i];
            var t = Math.Clamp(particle.Age / particle.Life, 0f, 1f);
            destination.Add(new ParticleInstance(
                particle.Position,
                float.Lerp(particle.StartSize, particle.EndSize, t),
                Vector4.Lerp(particle.StartColor, particle.EndColor, t),
                particle.Rotation,
                particle.Blend));
        }
    }

    /// <summary>Allocating convenience over <see cref="WriteInstances"/>, for a game's
    /// <c>BuildParticles()</c> where one list per frame is not worth pooling.</summary>
    public IReadOnlyList<ParticleInstance> BuildInstances()
    {
        var instances = new List<ParticleInstance>(_count);
        WriteInstances(instances);
        return instances;
    }

    /// <summary>Any two unit vectors perpendicular to <paramref name="axis"/> and each other.
    /// The reference vector is chosen away from <paramref name="axis"/> so the cross product
    /// cannot degenerate for an axis-aligned emission direction (the common case).</summary>
    private static (Vector3 Tangent, Vector3 Bitangent) OrthonormalBasis(Vector3 axis)
    {
        var reference = MathF.Abs(axis.Y) < 0.9f ? Vector3.UnitY : Vector3.UnitX;
        var tangent = Vector3.Normalize(Vector3.Cross(reference, axis));
        return (tangent, Vector3.Cross(axis, tangent));
    }

    /// <summary>A multiplier in <c>[1-jitter, 1+jitter]</c> — the one place the "jitter is a
    /// fraction of the base value" convention in <see cref="ParticleEffect"/> is implemented.</summary>
    private float Jittered(float jitter) => 1f + (((NextUnit() * 2f) - 1f) * jitter);

    private float NextUnit() => (float)_random.NextDouble();

    /// <summary>One live particle. A mutable struct in a private array on purpose: this is the
    /// hot loop, and copying ~100 bytes per particle per step to honour immutability would be
    /// the one place in this engine where that rule costs more than it protects.</summary>
    private struct Particle
    {
        public Vector3 Position;
        public Vector3 Velocity;
        public Vector3 Acceleration;
        public Vector4 StartColor;
        public Vector4 EndColor;
        public float Age;
        public float Life;
        public float StartSize;
        public float EndSize;
        public float Rotation;
        public float Spin;
        public float Drag;
        public float GroundY;
        public float GroundBounce;
        public bool HasGround;
        public bool ReportsGroundContact;
        public bool HasLanded;
        public ParticleBlend Blend;
    }
}
