using System.Numerics;

namespace Aigis.Render.Voxels;

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

    /// <summary>Fraction of the vertical speed kept on a bounce.</summary>
    public float Bounce { get; set; } = 0.35f;

    /// <summary>Fraction of the sliding speed kept per bounce (floor friction).</summary>
    public float Friction { get; set; } = 0.55f;

    public int Count => _count;

    /// <summary>Adds a piece of <paramref name="size"/> (full edge lengths) spinning at <paramref name="spin"/> rad/s.</summary>
    public void Spawn(Vector3 position, Vector3 velocity, Quaternion orientation, Vector3 size, Vector3 spin, Vector4 color)
    {
        var piece = new Piece(position, velocity, orientation, spin, size, color, 0f);
        if (_count < _pieces.Length)
        {
            _pieces[_count++] = piece;
            return;
        }

        // Full: replace the oldest piece (live pieces are swap-removed, so slot order does not follow age).
        var oldest = 0;
        for (var i = 1; i < _count; i++)
        {
            if (_pieces[i].Age > _pieces[oldest].Age)
            {
                oldest = i;
            }
        }

        _pieces[oldest] = piece;
    }

    public void Step(float dt)
    {
        if (!(dt > 0f))
        {
            return;
        }

        for (var i = 0; i < _count;)
        {
            var p = _pieces[i];
            p.Age += dt;
            if (p.Age >= LifeSeconds)
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
            var fade = Math.Clamp((LifeSeconds - p.Age) / (LifeSeconds / 3f), 0f, 1f);
            var model = Matrix4x4.CreateScale(p.Size * fade) * Matrix4x4.CreateFromQuaternion(p.Orientation) * Matrix4x4.CreateTranslation(p.Position);
            into.Add(new PrimitiveInstance(PrimitiveKind.Cube, model, Vector3.Zero, p.Color));
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
    }
}
