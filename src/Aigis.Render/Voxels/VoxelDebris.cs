using System.Numerics;

namespace Aigis.Render.Voxels;

/// <summary>A piece meeting the floor (its first landing, or a bounce) — for a landing sound.</summary>
/// <param name="Position">Where it landed.</param>
/// <param name="Speed">How fast it came down [m/s].</param>
/// <param name="Size">Its largest edge [m].</param>
/// <param name="Tag">What the spawner said it was (<see cref="VoxelDebris.Spawn"/>).</param>
public readonly record struct DebrisLanding(Vector3 Position, float Speed, float Size, byte Tag);

/// <summary>One cube of a chunk (<see cref="VoxelDebris.SpawnChunk"/>).</summary>
/// <param name="Offset">Its centre from the chunk's centre, in the chunk's frame.</param>
/// <param name="Color">Its colour.</param>
/// <param name="TornFaces">Its faces that were torn off something, one bit per face (+X, −X, +Y, −Y, +Z, −Z: bits 0..5):
/// only these glow while the chunk is hot.</param>
public readonly record struct DebrisCell(Vector3 Offset, Vector4 Color, byte TornFaces);

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
    /// <param name="heat">How long [s] it glows hot after it breaks off: drawn self-lit in <paramref name="hotColor"/>, cooling
    /// to its own colour, with a short streak of light behind it (<see cref="WriteTrails"/>). Zero: it never glows.</param>
    /// <param name="hotColor">The colour it glows while hot (white when not given).</param>
    public void Spawn(Vector3 position, Vector3 velocity, Quaternion orientation, Vector3 size, Vector3 spin, Vector4 color, float life = 0f, bool glow = false, byte tag = 0, float heat = 0f, Vector4? hotColor = null)
    {
        var piece = new Piece(position, velocity, orientation, spin, size, color, 0f)
        {
            Life = life > 0f ? life : LifeSeconds,
            Glow = glow,
            Tag = tag,
            Heat = MathF.Max(0f, heat),
            HotColor = hotColor ?? Vector4.One,
        };
        Add(piece);
    }

    /// <summary>
    /// Adds a chunk: several cubes of <paramref name="cellSize"/> flying and tumbling as one (one slot of the capacity).
    /// While hot (<paramref name="heat"/> seconds) only its torn faces (<see cref="DebrisCell.TornFaces"/>) glow, in
    /// <paramref name="hotColor"/>; its cubes keep their own colours. It draws no streak.
    /// </summary>
    /// <param name="position">Its centre.</param>
    /// <param name="velocity">How it flies off [m/s].</param>
    /// <param name="orientation">Its frame (the cells' offsets are in it).</param>
    /// <param name="cellSize">One cube's edges.</param>
    /// <param name="cells">Its cubes (at least one).</param>
    /// <param name="spin">Its spin [rad/s].</param>
    /// <param name="life">How long it lives [s]; zero or less uses <see cref="LifeSeconds"/>.</param>
    /// <param name="tag">What it is, for the caller (reported back with its landings).</param>
    /// <param name="heat">How long [s] its torn faces glow.</param>
    /// <param name="hotColor">The colour they glow (white when not given).</param>
    public void SpawnChunk(Vector3 position, Vector3 velocity, Quaternion orientation, Vector3 cellSize, IReadOnlyList<DebrisCell> cells, Vector3 spin, float life = 0f, byte tag = 0, float heat = 0f, Vector4? hotColor = null)
    {
        ArgumentNullException.ThrowIfNull(cells);
        ArgumentOutOfRangeException.ThrowIfZero(cells.Count);
        var piece = new Piece(position, velocity, orientation, spin, cellSize, cells[0].Color, 0f)
        {
            Life = life > 0f ? life : LifeSeconds,
            Tag = tag,
            Heat = MathF.Max(0f, heat),
            HotColor = hotColor ?? Vector4.One,
            Cells = [.. cells],
        };
        Add(piece);
    }

    private void Add(Piece piece)
    {
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
            var half = BottomDepth(p);
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

    /// <summary>Appends one cube per live piece (shrinking over the last third of its life). A hot piece is drawn
    /// self-lit, from its hot colour (brighter than white, so it blooms) cooling to its own.</summary>
    public void WriteInstances(ICollection<PrimitiveInstance> into)
    {
        ArgumentNullException.ThrowIfNull(into);
        for (var i = 0; i < _count; i++)
        {
            var p = _pieces[i];
            var fade = Math.Clamp((p.Life - p.Age) / (p.Life / 3f), 0f, 1f);
            if (p.Cells is not null)
            {
                WriteChunk(p, fade, into);
                continue;
            }

            var model = Matrix4x4.CreateScale(p.Size * fade) * Matrix4x4.CreateFromQuaternion(p.Orientation) * Matrix4x4.CreateTranslation(p.Position);
            var hot = HeatOf(p);
            if (hot > 0f)
            {
                var color = Vector4.Lerp(p.Color * CooledBrightness, p.HotColor * HotBrightness, hot) with { W = p.Color.W };
                into.Add(new PrimitiveInstance(PrimitiveKind.Cube, model, Vector3.Zero, color, MaterialKind.Emissive));
                continue;
            }

            into.Add(new PrimitiveInstance(PrimitiveKind.Cube, model, Vector3.Zero, p.Color, p.Glow ? MaterialKind.Emissive : MaterialKind.Standard));
        }
    }

    /// <summary>
    /// Appends a short streak of soft light behind every hot piece (see <see cref="Spawn"/>'s heat): a few additive dots
    /// back along its flight, about <see cref="TrailSeconds"/> of its travel long, fading with its heat and toward the tail.
    /// </summary>
    public void WriteTrails(ICollection<ParticleInstance> into)
    {
        ArgumentNullException.ThrowIfNull(into);
        for (var i = 0; i < _count; i++)
        {
            var p = _pieces[i];
            var hot = HeatOf(p);
            var speed = p.Velocity.Length();
            if (hot <= 0f || speed < 0.05f || p.Cells is not null)
            {
                continue;
            }

            var edge = MathF.Max(p.Size.X, MathF.Max(p.Size.Y, p.Size.Z));
            var back = p.Velocity / speed;
            var length = MathF.Min(speed * TrailSeconds, MaxTrailLength) * (0.4f + (0.6f * hot));
            for (var k = 0; k < TrailDots; k++)
            {
                var f = k / (float)(TrailDots - 1);
                // Soft dots fade from the centre (visible about half their size): twice the edge reads as the piece's width.
                var size = 2f * edge * (1f - (0.5f * f));
                var alpha = 0.75f * hot * (1f - (0.8f * f));
                into.Add(new ParticleInstance(p.Position - (back * length * f), size, p.HotColor with { W = alpha }, Blend: ParticleBlend.Additive));
            }
        }
    }

    /// <summary>How long a hot piece's streak is, in seconds of its travel.</summary>
    public const float TrailSeconds = 0.045f;

    private const float MaxTrailLength = 0.07f;
    private const int TrailDots = 6;
    private const float HotBrightness = 1.6f;
    private const float CooledBrightness = 0.75f;

    /// <summary>
    /// A chunk's cubes in their own colours, and, while it is hot, a thin self-lit plate on each torn face (just proud
    /// of the face, so it never fights it for depth), cooling from the hot colour to the cube's own.
    /// </summary>
    private static void WriteChunk(in Piece p, float fade, ICollection<PrimitiveInstance> into)
    {
        var frame = Matrix4x4.CreateFromQuaternion(p.Orientation) * Matrix4x4.CreateTranslation(p.Position);
        var cube = p.Size * fade;
        var hot = HeatOf(p);
        foreach (var cell in p.Cells!)
        {
            var at = cell.Offset * fade;
            into.Add(new PrimitiveInstance(PrimitiveKind.Cube, Matrix4x4.CreateScale(cube) * Matrix4x4.CreateTranslation(at) * frame, Vector3.Zero, cell.Color, MaterialKind.Standard));
            if (hot <= 0f || cell.TornFaces == 0)
            {
                continue;
            }

            var glow = Vector4.Lerp(cell.Color * CooledBrightness, p.HotColor * PlateBrightness, hot) with { W = cell.Color.W };
            for (var f = 0; f < 6; f++)
            {
                if ((cell.TornFaces & (1 << f)) == 0)
                {
                    continue;
                }

                var axis = f / 2;
                var sign = f % 2 == 0 ? 1f : -1f;
                var normal = axis switch { 0 => Vector3.UnitX, 1 => Vector3.UnitY, _ => Vector3.UnitZ } * sign;
                var plate = new Vector3(axis == 0 ? PlateThickness : PlateWidth, axis == 1 ? PlateThickness : PlateWidth, axis == 2 ? PlateThickness : PlateWidth) * cube;
                var center = at + (normal * (0.5f * Vector3.Dot(cube, normal * sign)));
                into.Add(new PrimitiveInstance(PrimitiveKind.Cube, Matrix4x4.CreateScale(plate) * Matrix4x4.CreateTranslation(center) * frame, Vector3.Zero, glow, MaterialKind.Emissive));
            }
        }
    }

    private const float PlateThickness = 0.12f;
    private const float PlateWidth = 0.8f;
    private const float PlateBrightness = 1.15f;

    /// <summary>How far below its centre a piece reaches (a chunk: its lowest cube as it is turned now).</summary>
    private static float BottomDepth(in Piece p)
    {
        var halfEdge = 0.5f * MathF.Max(p.Size.X, MathF.Max(p.Size.Y, p.Size.Z));
        if (p.Cells is null)
        {
            return halfEdge;
        }

        var lowest = 0f;
        foreach (var cell in p.Cells)
        {
            lowest = MathF.Min(lowest, Vector3.Transform(cell.Offset, p.Orientation).Y);
        }

        return halfEdge - lowest;
    }

    /// <summary>1 when it has just broken off, falling to 0 over its heat (eased, so it cools fast then lingers).</summary>
    private static float HeatOf(in Piece p)
    {
        if (p.Heat <= 0f || p.Age >= p.Heat)
        {
            return 0f;
        }

        var left = 1f - (p.Age / p.Heat);
        return left * left;
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
        public float Heat;
        public Vector4 HotColor;

        /// <summary>A chunk's cells (null for a single cube): <see cref="Size"/> is then one cell.</summary>
        public DebrisCell[]? Cells;
    }
}
