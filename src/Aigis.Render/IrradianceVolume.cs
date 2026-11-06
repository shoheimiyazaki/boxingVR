using System.Numerics;

namespace Aigis.Render;

/// <summary>
/// A baked grid of L1 spherical-harmonic irradiance probes — the engine's "cheap GI" (TASK-245,
/// 2026-08-29 user request 「ヘッドレスで安価なグローバルイルミネーション的な方法が実装できないか
/// 検討してください。トレイラー作成用です」).
///
/// <para>
/// <b>What problem it solves.</b> <see cref="ShaderSource.PrimitiveFrag"/>'s ambient term is
/// <c>HemisphereAmbient</c>: a sky colour above, a ground colour below, linearly blended by the
/// normal's Y. It knows nothing about occlusion, so an indoor floor receives exactly as much sky as
/// an outdoor one, a corner is no darker than the middle of a room, and no surface ever picks up
/// the colour of the surface next to it. This type replaces that single term with a value sampled
/// from a volume that was ray-traced against the scene's own geometry.
/// </para>
///
/// <para>
/// <b>The encoding, and why the shader is three texture fetches and three dot products.</b> Each
/// probe stores, per colour channel, four numbers: a constant term <c>L0</c> and a linear term
/// <c>L1</c> (a 3-vector). The reconstruction for a surface with normal <c>n</c> is exactly
/// <c>L0 + dot(L1, n)</c> — i.e. <c>dot(vec4(n, 1), vec4(L1, L0))</c>. Every scale factor is
/// folded into the numbers at bake time by <see cref="IrradianceBaker"/>, so the GPU does no
/// arithmetic beyond that dot. That is what makes the runtime cost lower than one shadowed point
/// light.
/// </para>
///
/// <para>
/// <b>What the numbers mean: the incoming radiance field, read off in the normal direction.</b>
/// They are the least-squares linear (L1 spherical-harmonic) fit of the light arriving at the
/// probe, evaluated at <c>n</c> — <b>not</b> the cosine-convolved irradiance a physically-based
/// renderer would store. That is deliberate, and <see cref="IrradianceBaker"/>'s <c>Integrate</c>
/// documents the measurement behind it: the term this replaces reads the sky colour straight off
/// the normal with no cosine lobe anywhere, so adopting the textbook convolution would have
/// changed the brightness of every unoccluded surface in the engine the moment a volume was
/// attached. The engine's convention wins because it is the one already on screen — the same
/// reason the sun term here carries no 1/π and <c>colorLinear = vColor.rgb * lit</c> treats
/// <c>lit</c> as a plain reflectance multiplier.
/// </para>
///
/// <para>
/// <b>It is a strict superset of <c>HemisphereAmbient</c>, and that is checkable rather than
/// asserted.</b> Feed the baker an empty occluder list and an <see cref="AmbientLight"/> and every
/// probe comes back holding <c>L0 = (sky+ground)/2</c>, <c>L1 = (0, (sky-ground)/2, 0)</c>, whose
/// reconstruction <c>L0 + L1.y*n.y</c> is algebraically identical to
/// <c>mix(ground, sky, n.y*0.5+0.5)</c> — the shader's existing line.
/// <see cref="Uniform"/> builds exactly that volume, and
/// <c>IrradianceVolumeTests</c> pins the equality.
/// </para>
///
/// <para>
/// <b>Static, and deliberately so.</b> A volume is baked from a level's static geometry, once, and
/// reused for every frame of a shot. Moving actors RECEIVE its light but do not contribute to it —
/// bounce light comes from the room, not from a patient walking through it, and paying to re-bake
/// per frame would buy a difference nobody can see. For the trailer path this is not an
/// approximation of a dynamic system; it is the whole design.
/// </para>
/// </summary>
public sealed class IrradianceVolume
{
    /// <summary>Halfs per probe per channel: <c>(L1.x, L1.y, L1.z, L0)</c>. Matches the RGBA
    /// texel the renderers upload and the <c>vec4</c> the shader dots against
    /// <c>vec4(normal, 1)</c>, so a reordering here would be a silent shading error rather than a
    /// size mismatch.</summary>
    public const int ComponentsPerProbe = 4;

    private readonly Half[] _red;
    private readonly Half[] _green;
    private readonly Half[] _blue;

    /// <param name="origin">World position of probe (0, 0, 0).</param>
    /// <param name="cellSize">Probe spacing, per axis, in world metres. Must be positive.</param>
    /// <param name="countX">Probes along X. Must be at least 1.</param>
    /// <param name="countY">Probes along Y. Must be at least 1.</param>
    /// <param name="countZ">Probes along Z. Must be at least 1.</param>
    /// <param name="red">X-major probe data for the red channel — see <see cref="Index"/> for the
    /// ordering and <see cref="ComponentsPerProbe"/> for the per-probe layout.</param>
    /// <param name="green">Same, green.</param>
    /// <param name="blue">Same, blue.</param>
    /// <param name="normalBiasMetres">How far along its own normal a surface is pushed before it
    /// samples this volume. The classic light-leak fix: a wall's own texel and the texel on the
    /// far side of it are the same trilinear neighbourhood at probe resolution, so a wall between
    /// a lit room and a dark one reads a blend of both without this. Half a cell is the usual
    /// choice; <see cref="IrradianceBaker"/> derives it from the spacing.</param>
    /// <param name="fadeMetres">Width of the band, measured inward from the volume's faces, over
    /// which the shader cross-fades from this volume back to <c>HemisphereAmbient</c>. A volume
    /// covers a level, not a world, and a surface just outside it must not fall off a cliff — see
    /// this class's own remarks.</param>
    public IrradianceVolume(
        Vector3 origin,
        Vector3 cellSize,
        int countX,
        int countY,
        int countZ,
        Half[] red,
        Half[] green,
        Half[] blue,
        float normalBiasMetres,
        float fadeMetres)
    {
        ArgumentNullException.ThrowIfNull(red);
        ArgumentNullException.ThrowIfNull(green);
        ArgumentNullException.ThrowIfNull(blue);
        ArgumentOutOfRangeException.ThrowIfLessThan(countX, 1);
        ArgumentOutOfRangeException.ThrowIfLessThan(countY, 1);
        ArgumentOutOfRangeException.ThrowIfLessThan(countZ, 1);

        if (!float.IsFinite(cellSize.X) || !float.IsFinite(cellSize.Y) || !float.IsFinite(cellSize.Z) ||
            cellSize.X <= 0f || cellSize.Y <= 0f || cellSize.Z <= 0f)
        {
            throw new ArgumentOutOfRangeException(nameof(cellSize), cellSize, "Probe spacing must be finite and positive on every axis.");
        }

        if (!float.IsFinite(origin.X) || !float.IsFinite(origin.Y) || !float.IsFinite(origin.Z))
        {
            throw new ArgumentOutOfRangeException(nameof(origin), origin, "Volume origin must be finite.");
        }

        if (!float.IsFinite(normalBiasMetres) || normalBiasMetres < 0f)
        {
            throw new ArgumentOutOfRangeException(nameof(normalBiasMetres), normalBiasMetres, "Normal bias must be finite and non-negative.");
        }

        if (!float.IsFinite(fadeMetres) || fadeMetres < 0f)
        {
            throw new ArgumentOutOfRangeException(nameof(fadeMetres), fadeMetres, "Fade width must be finite and non-negative.");
        }

        var expected = countX * countY * countZ * ComponentsPerProbe;
        ThrowIfWrongLength(red, expected, nameof(red));
        ThrowIfWrongLength(green, expected, nameof(green));
        ThrowIfWrongLength(blue, expected, nameof(blue));

        Origin = origin;
        CellSize = cellSize;
        CountX = countX;
        CountY = countY;
        CountZ = countZ;
        _red = red;
        _green = green;
        _blue = blue;
        NormalBiasMetres = normalBiasMetres;
        FadeMetres = fadeMetres;
    }

    /// <summary>World position of probe (0, 0, 0).</summary>
    public Vector3 Origin { get; }

    /// <summary>Probe spacing per axis, world metres.</summary>
    public Vector3 CellSize { get; }

    public int CountX { get; }

    public int CountY { get; }

    public int CountZ { get; }

    /// <summary>See the constructor parameter of the same name.</summary>
    public float NormalBiasMetres { get; }

    /// <summary>See the constructor parameter of the same name.</summary>
    public float FadeMetres { get; }

    /// <summary>Total probes.</summary>
    public int ProbeCount => CountX * CountY * CountZ;

    /// <summary>Red-channel texels, X-major (see <see cref="Index"/>) — exactly what a renderer
    /// uploads into one 3D image.</summary>
    public ReadOnlySpan<Half> Red => _red;

    public ReadOnlySpan<Half> Green => _green;

    public ReadOnlySpan<Half> Blue => _blue;

    /// <summary>The world-space box the probes span: <see cref="Origin"/> to
    /// <c>Origin + (Count-1) * CellSize</c>. Note the <c>-1</c> — probes sit ON the boundary, they
    /// do not centre cells inside it.</summary>
    public Vector3 Extent => new(
        (CountX - 1) * CellSize.X,
        (CountY - 1) * CellSize.Y,
        (CountZ - 1) * CellSize.Z);

    /// <summary>Element offset of probe <c>(x, y, z)</c>'s first component. X-major, then Y, then
    /// Z — the order a Vulkan 3D image expects its rows/slices in, so an upload is one
    /// <c>memcpy</c> rather than a per-slice loop.</summary>
    public int Index(int x, int y, int z) => (((z * CountY) + y) * CountX + x) * ComponentsPerProbe;

    /// <summary>
    /// A 1x1x1 volume holding exactly <c>HemisphereAmbient</c>'s answer for <paramref name="ambient"/>
    /// — the identity element of this whole feature.
    /// <para>
    /// Two jobs. It is the fallback the renderers bind when a caller passes no volume (Vulkan
    /// requires every declared descriptor to point at something real, even when a uniform flag
    /// means the shader will not read it), and it is the fixture that lets a test assert the
    /// encoding reproduces the term it replaces rather than merely resembling it.
    /// </para>
    /// <para>
    /// The derivation is in this class's own summary: over a sphere split into a constant sky
    /// hemisphere and a constant ground hemisphere, the cosine-weighted average seen by a normal
    /// <c>n</c> works out to <c>(sky+ground)/2 + (sky-ground)/2 * n.y</c>.
    /// </para>
    /// </summary>
    public static IrradianceVolume Uniform(AmbientLight ambient)
    {
        var sky = ambient.Color * ambient.Intensity;
        var ground = ambient.GroundColor * ambient.Intensity;
        var l0 = (sky + ground) * 0.5f;
        var l1Y = (sky - ground) * 0.5f;

        return new IrradianceVolume(
            Vector3.Zero,
            Vector3.One,
            1,
            1,
            1,
            [Half.Zero, (Half)l1Y.X, Half.Zero, (Half)l0.X],
            [Half.Zero, (Half)l1Y.Y, Half.Zero, (Half)l0.Y],
            [Half.Zero, (Half)l1Y.Z, Half.Zero, (Half)l0.Z],
            normalBiasMetres: 0f,
            fadeMetres: 0f);
    }

    /// <summary>
    /// The CPU twin of the shader's sample: trilinearly interpolated, clamped at the edges,
    /// negatives clipped away. Exists for two callers that are not the GPU —
    /// <see cref="IrradianceBaker"/>'s second bounce (which gathers from the first bounce's own
    /// field rather than re-tracing) and tests that need a number instead of a picture.
    /// </summary>
    /// <param name="worldPosition">Where to sample. Outside the volume clamps to the edge probes;
    /// the SHADER additionally cross-fades back to <c>HemisphereAmbient</c> there
    /// (<see cref="FadeMetres"/>), which this method deliberately does not do — the baker wants the
    /// raw field.</param>
    /// <param name="normal">Surface normal; need not be unit length, but a non-unit normal scales
    /// the directional term, so pass a normalized one.</param>
    public Vector3 Sample(Vector3 worldPosition, Vector3 normal)
    {
        var local = (worldPosition - Origin) / CellSize;
        var (x0, fx) = Split(local.X, CountX);
        var (y0, fy) = Split(local.Y, CountY);
        var (z0, fz) = Split(local.Z, CountZ);
        var x1 = Math.Min(x0 + 1, CountX - 1);
        var y1 = Math.Min(y0 + 1, CountY - 1);
        var z1 = Math.Min(z0 + 1, CountZ - 1);

        var result = Vector3.Zero;
        for (var corner = 0; corner < 8; corner++)
        {
            var xi = (corner & 1) == 0 ? x0 : x1;
            var yi = (corner & 2) == 0 ? y0 : y1;
            var zi = (corner & 4) == 0 ? z0 : z1;
            var weight =
                ((corner & 1) == 0 ? 1f - fx : fx) *
                ((corner & 2) == 0 ? 1f - fy : fy) *
                ((corner & 4) == 0 ? 1f - fz : fz);
            if (weight <= 0f)
            {
                continue;
            }

            result += weight * ProbeValue(Index(xi, yi, zi), normal);
        }

        return Vector3.Max(result, Vector3.Zero);
    }

    private Vector3 ProbeValue(int index, Vector3 normal) => new(
        Reconstruct(_red, index, normal),
        Reconstruct(_green, index, normal),
        Reconstruct(_blue, index, normal));

    private static float Reconstruct(Half[] channel, int index, Vector3 normal) =>
        (float)channel[index + 3] +
        ((float)channel[index] * normal.X) +
        ((float)channel[index + 1] * normal.Y) +
        ((float)channel[index + 2] * normal.Z);

    /// <summary>Integer cell and fraction for one axis, clamped so a sample outside the volume
    /// reads the edge probe rather than wrapping or indexing out of range.</summary>
    private static (int Index, float Fraction) Split(float coordinate, int count)
    {
        if (!float.IsFinite(coordinate) || coordinate <= 0f)
        {
            return (0, 0f);
        }

        if (coordinate >= count - 1)
        {
            return (count - 1, 0f);
        }

        var floor = (int)MathF.Floor(coordinate);
        return (floor, coordinate - floor);
    }

    private static void ThrowIfWrongLength(Half[] channel, int expected, string name)
    {
        if (channel.Length != expected)
        {
            throw new ArgumentException(
                $"Expected {expected} halfs ({expected / ComponentsPerProbe} probes x {ComponentsPerProbe}), got {channel.Length}.",
                name);
        }
    }
}
