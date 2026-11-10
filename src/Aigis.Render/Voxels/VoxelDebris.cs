using System.Numerics;

namespace Aigis.Render.Voxels;

/// <summary>A piece meeting the floor (its first landing, or a bounce) — for a landing sound.</summary>
/// <param name="Position">Where it landed.</param>
/// <param name="Speed">How fast it came down [m/s].</param>
/// <param name="Size">Its largest edge [m].</param>
/// <param name="Tag">What the spawner said it was (<see cref="VoxelDebris.Spawn"/>).</param>
public readonly record struct DebrisLanding(Vector3 Position, float Speed, float Size, byte Tag);

/// <summary>
/// Flying cubes for carved-off voxels: each tumbles under gravity, bounces and slides to rest on a ground plane, then
/// shrinks away. Visual only (no collision with bodies), CPU-simulated, fixed capacity (the oldest piece makes room for a
/// new one). Deterministic for a given sequence of <see cref="Spawn"/> and <see cref="Step"/> calls.
/// </summary>
public sealed class VoxelDebris
{
    private readonly Piece[] _pieces;
    private int _count;

    /// <param name="capacity">Most pieces alive at once.</param>
    /// <param name="groundY">Height of the floor pieces land on.</param>
    public VoxelDebris(int capacity = 4000, float groundY = 0f)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(capacity, 1);
        _pieces = new Piece[capacity];
        GroundY = groundY;
    }

    public float GroundY { get; set; }

    /// <summary>Optional floor height under a point (overrides <see cref="GroundY"/>): a ring top inside the ring, the floor outside.</summary>
    public Func<Vector3, float>? GroundAt { get; set; }

    /// <summary>Gravity along −Y [m/s²].</summary>
    public float Gravity { get; set; } = 9.81f;

    /// <summary>How long a piece lives [s] before it has shrunk away.</summary>
    public float LifeSeconds { get; set; } = 3.5f;

    /// <summary>Slowest downward speed [m/s] reported as a landing (slower is resting, not hitting).</summary>
    public float LandingSpeed { get; set; } = 0.3f;

    /// <summary>Fraction of the vertical speed kept on a bounce.</summary>
    public float Bounce { get; set; } = 0.35f;

    /// <summary>Fraction of the sliding speed kept per bounce (floor friction).</summary>
    public float Friction { get; set; } = 0.55f;

    public int Count => _count;

    /// <summary>Adds a piece of <paramref name="size"/> (full edge lengths) spinning at <paramref name="spin"/> rad/s.</summary>
    /// <param name="position">Where it starts (its centre).</param>
    /// <param name="velocity">How it flies off [m/s].</param>
    /// <param name="orientation">Its starting orientation.</param>
    /// <param name="size">Its edge lengths.</param>
    /// <param name="spin">Its spin [rad/s] (axis × speed).</param>
    /// <param name="color">Its colour.</param>
    /// <param name="life">How long this piece lives [s]; zero or less uses <see cref="LifeSeconds"/>.</param>
    /// <param name="glow">Draw it self-lit (<see cref="MaterialKind.Emissive"/>): a spark rather than a chip.</param>
    /// <param name="tag">What it is, for the caller (reported back with its landings — <see cref="DebrisLanding.Tag"/>).</param>
    public void Spawn(Vector3 position, Vector3 velocity, Quaternion orientation, Vector3 size, Vector3 spin, Vector4 color, float life = 0f, bool glow = false, byte tag = 0)
    {
        var piece = new Piece(position, velocity, orientation, spin, size, color, 0f) { Life = life > 0f ? life : LifeSeconds, Glow = glow, Tag = tag };
        if (_count < _pieces.Length)
        {
            _pieces[_count++] = piece;
            return;
        }

        // Full: replace the piece nearest the end of its life (live pieces are swap-removed, so slot order does not
        // follow age).
        var oldest = 0;
        for (var i = 1; i < _count; i++)
        {
            if (_pieces[i].Age / _pieces[i].Life > _pieces[oldest].Age / _pieces[oldest].Life)
            {
                oldest = i;
            }
        }

        _pieces[oldest] = piece;
    }

    /// <summary>Moves every piece by <paramref name="dt"/>.</summary>
    /// <param name="dt">Time step [s].</param>
    /// <param name="landed">Receives every piece that hit the floor this step coming down faster than
    /// <see cref="LandingSpeed"/> (its first landing and each real bounce).</param>
    public void Step(float dt, ICollection<DebrisLanding>? landed = null)
    {
        if (!(dt > 0f))
        {
            return;
        }

        for (var i = 0; i < _count;)
        {
            var p = _pieces[i];
            p.Age += dt;
            if (p.Age >= p.Life)
            {
                _pieces[i] = _pieces[--_count];
                continue;
            }

            var previousY = p.Position.Y;
            p.Velocity -= new Vector3(0f, Gravity * dt, 0f);
            p.Position += p.Velocity * dt;
            var half = 0.5f * MathF.Max(p.Size.X, MathF.Max(p.Size.Y, p.Size.Z));
            var ground = GroundAt?.Invoke(p.Position) ?? GroundY;
            // Land on a floor crossed from above during this step (however fast — no tunnelling), or resting on it.
            // A piece that was already below a raised floor (it fell past that floor's edge, e.g. off the side of a ring)
            // keeps falling to whatever is under it.
            var wasAbove = previousY - half >= ground - 1e-3f;
            if (p.Position.Y - half < ground && wasAbove)
            {
                p.Position.Y = ground + half;
                if (landed is not null && -p.Velocity.Y > LandingSpeed)
                {
                    landed.Add(new DebrisLanding(p.Position, -p.Velocity.Y, 2f * half, p.Tag));
                }

                if (p.Velocity.Y < 0f)
                {
                    p.Velocity = new Vector3(p.Velocity.X * Friction, -p.Velocity.Y * Bounce, p.Velocity.Z * Friction);
                    p.Spin *= Friction;
                }

                if (MathF.Abs(p.Velocity.Y) < 0.05f)
                {
                    p.Velocity = new Vector3(p.Velocity.X * (1f - MathF.Min(1f, 6f * dt)), 0f, p.Velocity.Z * (1f - MathF.Min(1f, 6f * dt)));
                    p.Spin *= 1f - MathF.Min(1f, 6f * dt);
                }
            }

            var angle = p.Spin.Length() * dt;
            if (angle > 1e-6f)
            {
                p.Orientation = Quaternion.Normalize(Quaternion.Concatenate(p.Orientation, Quaternion.CreateFromAxisAngle(Vector3.Normalize(p.Spin), angle)));
            }

            _pieces[i++] = p;
        }

    }

    /// <summary>Appends one cube per live piece (shrinking over the last third of its life).</summary>
    public void WriteInstances(ICollection<PrimitiveInstance> into)
    {
        ArgumentNullException.ThrowIfNull(into);
        for (var i = 0; i < _count; i++)
        {
            var p = _pieces[i];
            var fade = Math.Clamp((p.Life - p.Age) / (p.Life / 3f), 0f, 1f);
            var model = Matrix4x4.CreateScale(p.Size * fade) * Matrix4x4.CreateFromQuaternion(p.Orientation) * Matrix4x4.CreateTranslation(p.Position);
            into.Add(new PrimitiveInstance(PrimitiveKind.Cube, model, Vector3.Zero, p.Color, p.Glow ? MaterialKind.Emissive : MaterialKind.Standard));
        }
    }

    public void Clear()
    {
        _count = 0;
    }

    private struct Piece(Vector3 position, Vector3 velocity, Quaternion orientation, Vector3 spin, Vector3 size, Vector4 color, float age)
    {
        public Vector3 Position = position;
        public Vector3 Velocity = velocity;
        public Quaternion Orientation = orientation;
        public Vector3 Spin = spin;
        public Vector3 Size = size;
        public Vector4 Color = color;
        public float Age = age;
        public float Life;
        public bool Glow;
        public byte Tag;
    }
}
