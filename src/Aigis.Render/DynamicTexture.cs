using System.Numerics;

namespace Aigis.Render;

/// <summary>
/// A CPU-side 8-bit RGBA raster that is <b>painted into while the game runs</b> and re-uploaded to
/// the GPU whenever it changes — the mutable counterpart to <see cref="TextureData"/>, and the
/// engine's first texture that is authored by gameplay rather than loaded from disk.
/// <para>
/// <b>Why a CPU raster and not a render-to-texture pass.</b> The obvious alternative is to draw
/// brush sprites into an offscreen colour attachment on the GPU (what
/// <c>HeadlessRenderer.RenderToTexture.cs</c> proves out, and what the Unity reference does via
/// <c>OnGUI</c>/<c>Graphics.DrawTexture</c>). This engine deliberately does not: a stain surface is
/// simulation-visible state that has to be identical run-to-run on the same machine (hard rule 5)
/// and inspectable by a headless test with no Vulkan device at all (hard rule 6). A CPU raster is
/// both by construction — <see cref="StampMax"/>/<see cref="StampErase"/> are integer-indexed loops
/// over deterministic float maths, unit-testable without a GPU, and the GPU never reads back.
/// The cost is one upload per changed frame, which is the trade this engine wants at the sizes
/// involved (a 1024×768 surface is 3 MB).
/// </para>
/// <para>
/// <b>Alpha is the stain mask; RGB is the stain colour.</b> A fully transparent texel is clean
/// surface, and the decal pipeline alpha-blends, so a freshly-cleared texture is invisible rather
/// than a black rectangle. That differs from the Unity reference, which keeps RGB=colour /
/// black=clean and no alpha (its decal shader treats black as "add nothing"); the alpha form is
/// what an ordinary alpha-blended quad needs, and it makes <see cref="StampErase"/> a real erase
/// instead of a darkening.
/// </para>
/// <para>
/// <b>Instance identity and <see cref="Revision"/>.</b> Renderers cache the GPU image per instance
/// (exactly as they do for <see cref="TextureData"/>) and re-upload when <see cref="Revision"/> has
/// moved since their last upload. So: build ONE instance and keep it for the surface's lifetime;
/// every mutating call bumps <see cref="Revision"/>, and a frame that paints nothing costs no
/// upload. The whole image is re-uploaded on change — uploading only the touched sub-rectangle is a
/// deliberate not-yet-done optimisation, so that the "which consumer has seen which pixels"問題
/// cannot exist while more than one renderer may observe the same instance.
/// </para>
/// </summary>
public sealed class DynamicTexture
{
    private readonly byte[] _rgba;

    /// <summary>Creates a fully transparent (clean) surface.</summary>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="width"/> or
    /// <paramref name="height"/> is not positive.</exception>
    public DynamicTexture(int width, int height)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(width);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(height);

        Width = width;
        Height = height;
        _rgba = new byte[width * height * 4];
    }

    /// <summary>
    /// Creates a surface pre-filled with <paramref name="rgba"/> — for a sprite that is generated
    /// once and never painted into again (2026-08-20: <c>Game.Triage.FoliageSprites</c>' procedural
    /// grass and flower tufts).
    /// <para>
    /// <b>Why this type rather than <see cref="TextureData"/>, which is already the immutable
    /// raster.</b> Because <see cref="DecalQuadDraw"/> — the only world-space textured quad this
    /// engine has — takes a <see cref="DynamicTexture"/>, and its residency/upload contract is keyed
    /// on that type's <see cref="Revision"/>. Widening the decal path to accept either raster would
    /// duplicate that whole residency cache for no behavioural gain; a never-mutated
    /// <see cref="DynamicTexture"/> already costs exactly one upload, because
    /// <see cref="Revision"/> never moves again after construction.
    /// </para>
    /// </summary>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="width"/> or
    /// <paramref name="height"/> is not positive.</exception>
    /// <exception cref="ArgumentException"><paramref name="rgba"/> is not exactly
    /// <c>width * height * 4</c> bytes — the same fail-loud check <see cref="TextureData"/> makes,
    /// rather than padding or truncating a caller's mistake into a scrambled image.</exception>
    public DynamicTexture(int width, int height, ReadOnlySpan<byte> rgba)
        : this(width, height)
    {
        if (rgba.Length != _rgba.Length)
        {
            throw new ArgumentException(
                $"Expected exactly {_rgba.Length} bytes for a {width}x{height} RGBA image, got {rgba.Length}.",
                nameof(rgba));
        }

        rgba.CopyTo(_rgba);

        // Seed the running total from the supplied pixels rather than leaving it at zero: this
        // constructor is the one path that puts coverage on the surface without going through a
        // stamp, and a CoverageSum that disagreed with the bytes would be worse than no counter.
        for (var index = 3; index < _rgba.Length; index += 4)
        {
            CoverageSum += _rgba[index];
        }
    }

    /// <summary>Width in pixels.</summary>
    public int Width { get; }

    /// <summary>Height in pixels.</summary>
    public int Height { get; }

    /// <summary>Monotonically increasing version stamp, bumped by every mutating call. Starts at 0,
    /// which is what lets a consumer treat "never uploaded" as revision -1 and upload once even for
    /// a surface that is never painted.</summary>
    public int Revision { get; private set; }

    /// <summary>
    /// Sum of every texel's alpha byte — how much coverage is on this surface right now, in units of
    /// "texels at full opacity × 255". Zero on a clean surface; <c>255L * Width * Height</c> on one
    /// painted solid.
    /// <para>
    /// <b>Maintained incrementally, by the stamp loops that are already writing those bytes</b>, so
    /// asking the question costs nothing. The alternative — walking the raster when someone wants the
    /// answer — is a megabyte-scale scan of a 1024×1536 sheet, which is not something a caller can do
    /// on a whim; and a caller that could not afford to ask would end up estimating coverage from
    /// stamp COUNTS, which the Max/Min blends make meaningless (a hundred stamps in one place cover
    /// no more floor than one).
    /// </para>
    /// <para>
    /// <b>What it is for</b>: <c>Game.Triage</c>'s hospital-cleanliness metric
    /// (<c>BloodDecalSurfaces.StainedSquareMetres</c>) turns this into real floor area, which is what
    /// makes "how filthy is this ward" a measured fact rather than a guess. It is deliberately a raw
    /// coverage total and not a fraction: only the owner of the sheet knows what area it spans.
    /// </para>
    /// </summary>
    public long CoverageSum { get; private set; }

    /// <summary>The pixel bytes: 8-bit RGBA, row-major, row 0 = the image's TOP row (the same
    /// convention as <see cref="TextureData.Rgba"/>, and the row a <c>v = 0</c> texture coordinate
    /// samples).</summary>
    public ReadOnlySpan<byte> Rgba => _rgba;

    /// <summary>Total bytes an upload of this texture moves.</summary>
    public int ByteLength => _rgba.Length;

    /// <summary>
    /// Replaces every pixel with <paramref name="rgba"/> (same size, same row order) and bumps
    /// <see cref="Revision"/>, so the renderers re-upload into the SAME GPU image. For a surface whose
    /// whole picture changes now and then — a wall calendar turning its page — where building a new
    /// instance per change would leave each old one resident in the renderers' per-instance caches.
    /// <see cref="CoverageSum"/> becomes the new picture's alpha total.
    /// </summary>
    /// <exception cref="ArgumentException">The span is not exactly <see cref="ByteLength"/> bytes.</exception>
    public void Overwrite(ReadOnlySpan<byte> rgba)
    {
        if (rgba.Length != _rgba.Length)
        {
            throw new ArgumentException(
                $"Expected {_rgba.Length} bytes ({Width}x{Height} RGBA), got {rgba.Length}.", nameof(rgba));
        }

        rgba.CopyTo(_rgba);
        long coverage = 0;
        for (var i = 3; i < _rgba.Length; i += 4)
        {
            coverage += _rgba[i];
        }

        CoverageSum = coverage;
        Revision++;
    }

    /// <summary>Wipes the surface back to clean (fully transparent). The level-change path — the
    /// reference's own <c>ClearBlood()</c>.</summary>
    public void Clear()
    {
        Array.Clear(_rgba);
        CoverageSum = 0;
        Revision++;
    }

    /// <summary>
    /// Paints a soft-edged disc, keeping whichever of the existing and incoming coverage is
    /// stronger — the reference's <c>BlendOp Max</c> stamp (§8.3): overlapping stamps do not
    /// compound into an ever-darker blob, so a surface can be painted indefinitely without
    /// saturating into a solid rectangle. That is also why no decay is needed.
    /// </summary>
    /// <param name="centerPixels">Disc centre in pixels; may be outside the image (the clipped part
    /// simply does not draw).</param>
    /// <param name="radiusPixels">Disc radius in pixels. Non-positive radii draw nothing.</param>
    /// <param name="rgb">Stain colour written wherever the incoming coverage wins.</param>
    /// <param name="strength">Peak coverage at the disc centre, clamped to [0,1].</param>
    public void StampMax(Vector2 centerPixels, float radiusPixels, Vector3 rgb, float strength)
    {
        var peak = Math.Clamp(strength, 0f, 1f);
        if (radiusPixels <= 0f || peak <= 0f)
        {
            return;
        }

        var r = ToByte(rgb.X);
        var g = ToByte(rgb.Y);
        var b = ToByte(rgb.Z);
        var touched = false;

        var bounds = DiscBounds(centerPixels, radiusPixels);
        for (var y = bounds.MinY; y <= bounds.MaxY; y++)
        {
            for (var x = bounds.MinX; x <= bounds.MaxX; x++)
            {
                var coverage = CoverageAt(x, y, centerPixels, radiusPixels);
                if (coverage <= 0f)
                {
                    continue;
                }

                var index = ((y * Width) + x) * 4;
                var incoming = ToByte(peak * coverage);
                if (incoming <= _rgba[index + 3])
                {
                    continue;
                }

                _rgba[index] = r;
                _rgba[index + 1] = g;
                _rgba[index + 2] = b;
                CoverageSum += incoming - _rgba[index + 3];
                _rgba[index + 3] = incoming;
                touched = true;
            }
        }

        if (touched)
        {
            Revision++;
        }
    }

    /// <summary>
    /// How much of a <see cref="StampOver"/> disc's own coverage counts as its BODY rather than its
    /// feathered rim — the fraction above which that stamp's colour replaces what was underneath.
    /// <para>Half. The rim below it still contributes coverage (so a mark never has a hard edge against
    /// clean ground) but leaves the colour alone, which is what lets a chip of something pale sit IN a
    /// stain instead of punching a disc out of it.</para>
    /// </summary>
    public const float StampOverBodyCoverage = 0.5f;

    /// <summary>
    /// Paints a disc of MATTER lying on the surface: its colour replaces whatever was underneath across
    /// the disc's body, while its coverage still follows <see cref="StampMax"/>'s rule.
    ///
    /// <para><b>Why <see cref="StampMax"/> cannot do this.</b> That one is a stain model — coverage is
    /// the quantity, and the colour only comes along when the incoming coverage WINS. Two stamps of the
    /// same fluid at full strength are indistinguishable from one, which is exactly right for blood and
    /// exactly wrong for anything that is not blood: a bone chip painted into a saturated pool is
    /// silently discarded, because the pool is already at coverage 1. Matter is not more fluid. It is a
    /// different thing, lying on top.</para>
    ///
    /// <para><b>Coverage still only grows</b> (never falls), so the stained-area metric a caller builds
    /// on <see cref="CoverageSum"/> is unaffected by how many times a spot is re-coloured, and a surface
    /// still cannot be scrubbed by painting on it.</para>
    /// </summary>
    /// <param name="centerPixels">Disc centre in pixels; may be outside the image.</param>
    /// <param name="radiusPixels">Disc radius in pixels. Non-positive radii draw nothing.</param>
    /// <param name="rgb">The matter's colour, written across the disc's body.</param>
    /// <param name="strength">Peak coverage at the disc centre, clamped to [0,1].</param>
    public void StampOver(Vector2 centerPixels, float radiusPixels, Vector3 rgb, float strength)
    {
        var peak = Math.Clamp(strength, 0f, 1f);
        if (radiusPixels <= 0f || peak <= 0f)
        {
            return;
        }

        var r = ToByte(rgb.X);
        var g = ToByte(rgb.Y);
        var b = ToByte(rgb.Z);
        var touched = false;

        var bounds = DiscBounds(centerPixels, radiusPixels);
        for (var y = bounds.MinY; y <= bounds.MaxY; y++)
        {
            for (var x = bounds.MinX; x <= bounds.MaxX; x++)
            {
                var coverage = CoverageAt(x, y, centerPixels, radiusPixels);
                if (coverage <= 0f)
                {
                    continue;
                }

                var index = ((y * Width) + x) * 4;
                if (coverage >= StampOverBodyCoverage)
                {
                    _rgba[index] = r;
                    _rgba[index + 1] = g;
                    _rgba[index + 2] = b;
                    touched = true;
                }

                var incoming = ToByte(peak * coverage);
                if (incoming <= _rgba[index + 3])
                {
                    continue;
                }

                CoverageSum += incoming - _rgba[index + 3];
                _rgba[index + 3] = incoming;
                touched = true;
            }
        }

        if (touched)
        {
            Revision++;
        }
    }

    /// <summary>
    /// Paints a soft-edged <b>stroke</b>: the disc of <paramref name="radiusPixels"/> swept from
    /// <paramref name="fromPixels"/> to <paramref name="toPixels"/> — a stadium, not two discs and a
    /// gap. Same Max rule as <see cref="StampMax"/>.
    /// <para>
    /// This exists because sampling a moving brush at any finite rate leaves gaps between samples,
    /// and painting a disc per sample either dots the trail (sample rate too low) or costs a stamp
    /// per centimetre (sample rate high enough to overlap). Sweeping is what makes one sampled
    /// interval one continuous mark, at the cost of one rasterisation regardless of how far the
    /// brush travelled.
    /// </para>
    /// </summary>
    /// <param name="fromPixels">Where the disc centre started, in pixels.</param>
    /// <param name="toPixels">Where it ended. Equal to <paramref name="fromPixels"/> degenerates to
    /// a plain disc, which is the correct result for a brush that did not move.</param>
    /// <param name="radiusPixels">Disc radius in pixels. Non-positive radii draw nothing.</param>
    /// <param name="rgb">Stain colour written wherever the incoming coverage wins.</param>
    /// <param name="strength">Peak coverage along the stroke's spine, clamped to [0,1].</param>
    public void StampStrokeMax(Vector2 fromPixels, Vector2 toPixels, float radiusPixels, Vector3 rgb, float strength)
    {
        var peak = Math.Clamp(strength, 0f, 1f);
        if (radiusPixels <= 0f || peak <= 0f)
        {
            return;
        }

        var r = ToByte(rgb.X);
        var g = ToByte(rgb.Y);
        var b = ToByte(rgb.Z);
        var touched = false;

        var minX = Math.Max(0, (int)MathF.Floor(MathF.Min(fromPixels.X, toPixels.X) - radiusPixels));
        var maxX = Math.Min(Width - 1, (int)MathF.Ceiling(MathF.Max(fromPixels.X, toPixels.X) + radiusPixels));
        var minY = Math.Max(0, (int)MathF.Floor(MathF.Min(fromPixels.Y, toPixels.Y) - radiusPixels));
        var maxY = Math.Min(Height - 1, (int)MathF.Ceiling(MathF.Max(fromPixels.Y, toPixels.Y) + radiusPixels));

        var axis = toPixels - fromPixels;
        var lengthSquared = axis.LengthSquared();

        for (var y = minY; y <= maxY; y++)
        {
            for (var x = minX; x <= maxX; x++)
            {
                // Texel centres, as everywhere else here (see CoverageAt).
                var point = new Vector2(x + 0.5f, y + 0.5f);
                var t = lengthSquared <= 0f
                    ? 0f
                    : Math.Clamp(Vector2.Dot(point - fromPixels, axis) / lengthSquared, 0f, 1f);
                var coverage = DiscCoverage(Vector2.Distance(point, fromPixels + (axis * t)), radiusPixels);
                if (coverage <= 0f)
                {
                    continue;
                }

                var index = ((y * Width) + x) * 4;
                var incoming = ToByte(peak * coverage);
                if (incoming <= _rgba[index + 3])
                {
                    continue;
                }

                _rgba[index] = r;
                _rgba[index + 1] = g;
                _rgba[index + 2] = b;
                CoverageSum += incoming - _rgba[index + 3];
                _rgba[index + 3] = incoming;
                touched = true;
            }
        }

        if (touched)
        {
            Revision++;
        }
    }

    /// <summary>
    /// Carves coverage back out of a soft-edged disc, keeping whichever of the existing and
    /// incoming coverage is <em>weaker</em> — the reference's <c>BlendOp Min</c> eraser (§8.3), and
    /// the only operation here that can take stain away.
    /// <para>
    /// <b>The mop uses this</b> (<c>Game.Triage.TriageGame.Mop.cs</c>, 2026-08-21) — mostly through
    /// <see cref="StampStrokeErase"/> below, which is this operation swept, with the plain disc left
    /// for the first step of a wipe, where there is no previous position to sweep from. It was
    /// originally written for the grooves the Unity reference carves through its own drag trail, which
    /// this port does not need (its drag brush draws streaks geometrically, as gaps between the brush's
    /// circles — see <c>Game.Triage.DragBrush</c>), and was kept anyway because "remove coverage" is a
    /// real, tested capability of a stain sheet that some future effect would need. It is also the
    /// counterpart that makes <see cref="StampMax"/>'s Max rule meaningful.
    /// </para>
    /// </summary>
    /// <param name="centerPixels">Disc centre in pixels.</param>
    /// <param name="radiusPixels">Disc radius in pixels. Non-positive radii erase nothing.</param>
    /// <param name="amount">How much coverage the disc centre removes, clamped to [0,1]
    /// (1 = erases to fully clean).</param>
    public void StampErase(Vector2 centerPixels, float radiusPixels, float amount)
    {
        var peak = Math.Clamp(amount, 0f, 1f);
        if (radiusPixels <= 0f || peak <= 0f)
        {
            return;
        }

        var touched = false;
        var bounds = DiscBounds(centerPixels, radiusPixels);
        for (var y = bounds.MinY; y <= bounds.MaxY; y++)
        {
            for (var x = bounds.MinX; x <= bounds.MaxX; x++)
            {
                var coverage = CoverageAt(x, y, centerPixels, radiusPixels);
                if (coverage <= 0f)
                {
                    continue;
                }

                // The eraser's own value is a ceiling, so texels outside the brush (coverage 0,
                // ceiling 255) are provably untouched by the Min — the same property the
                // reference's Min-blend shader relies on.
                var index = ((y * Width) + x) * 4;
                var ceiling = ToByte(1f - (peak * coverage));
                if (ceiling >= _rgba[index + 3])
                {
                    continue;
                }

                CoverageSum += ceiling - _rgba[index + 3];
                _rgba[index + 3] = ceiling;
                touched = true;
            }
        }

        if (touched)
        {
            Revision++;
        }
    }

    /// <summary>
    /// Carves coverage back out of a soft-edged <b>stroke</b>: the disc of
    /// <paramref name="radiusPixels"/> swept from <paramref name="fromPixels"/> to
    /// <paramref name="toPixels"/>, keeping whichever of the existing and incoming coverage is
    /// <em>weaker</em>. The eraser counterpart of <see cref="StampStrokeMax"/>, on exactly the same Min
    /// rule as <see cref="StampErase"/>.
    /// <para>
    /// This exists for the same reason <see cref="StampStrokeMax"/> does, and the gap it closes is more
    /// visible on an eraser than on a stamp: a brush sampled at a finite rate leaves gaps between
    /// samples, and a wiped floor with unwiped stripes through it reads as a bug rather than as a
    /// coarse effect. <c>Game.Triage</c>'s mop head crosses a few centimetres per fixed step at its
    /// fastest — well under its own brush radius — but sweeping is what makes that a property of the
    /// geometry rather than a coincidence of the step rate.
    /// </para>
    /// <para>
    /// <b>Min, not subtract, and that is load-bearing for callers.</b> The lowest coverage one call can
    /// reach is <c>1 - amount</c>: repeated calls at a small <paramref name="amount"/> converge there
    /// and clean no further, because each one clamps rather than accumulating. A caller that wants a
    /// surface to actually come clean therefore passes <paramref name="amount"/> 1 and gets its
    /// gradualness from the brush having to physically cover the stain — which is what a mop does — not
    /// from the per-call strength.
    /// </para>
    /// </summary>
    /// <param name="fromPixels">Where the disc centre started, in pixels.</param>
    /// <param name="toPixels">Where it ended. Equal to <paramref name="fromPixels"/> degenerates to a
    /// plain disc, i.e. exactly <see cref="StampErase"/> — the correct result for a brush that did not
    /// move.</param>
    /// <param name="radiusPixels">Disc radius in pixels. Non-positive radii erase nothing.</param>
    /// <param name="amount">How much coverage the stroke's spine removes, clamped to [0,1]
    /// (1 = erases to fully clean).</param>
    public void StampStrokeErase(Vector2 fromPixels, Vector2 toPixels, float radiusPixels, float amount)
    {
        var peak = Math.Clamp(amount, 0f, 1f);
        if (radiusPixels <= 0f || peak <= 0f)
        {
            return;
        }

        var minX = Math.Max(0, (int)MathF.Floor(MathF.Min(fromPixels.X, toPixels.X) - radiusPixels));
        var maxX = Math.Min(Width - 1, (int)MathF.Ceiling(MathF.Max(fromPixels.X, toPixels.X) + radiusPixels));
        var minY = Math.Max(0, (int)MathF.Floor(MathF.Min(fromPixels.Y, toPixels.Y) - radiusPixels));
        var maxY = Math.Min(Height - 1, (int)MathF.Ceiling(MathF.Max(fromPixels.Y, toPixels.Y) + radiusPixels));

        var axis = toPixels - fromPixels;
        var lengthSquared = axis.LengthSquared();
        var touched = false;

        for (var y = minY; y <= maxY; y++)
        {
            for (var x = minX; x <= maxX; x++)
            {
                // Texel centres, as everywhere else here (see CoverageAt).
                var point = new Vector2(x + 0.5f, y + 0.5f);
                var t = lengthSquared <= 0f
                    ? 0f
                    : Math.Clamp(Vector2.Dot(point - fromPixels, axis) / lengthSquared, 0f, 1f);
                var coverage = DiscCoverage(Vector2.Distance(point, fromPixels + (axis * t)), radiusPixels);
                if (coverage <= 0f)
                {
                    continue;
                }

                // The eraser's own value is a ceiling, so texels the stroke does not cover (coverage 0,
                // ceiling 255) are provably untouched by the Min -- the same property StampErase and
                // the reference's Min-blend shader both rely on.
                var index = ((y * Width) + x) * 4;
                var ceiling = ToByte(1f - (peak * coverage));
                if (ceiling >= _rgba[index + 3])
                {
                    continue;
                }

                CoverageSum += ceiling - _rgba[index + 3];
                _rgba[index + 3] = ceiling;
                touched = true;
            }
        }

        if (touched)
        {
            Revision++;
        }
    }

    /// <summary>Coverage of one texel under a disc brush: flat across the inner
    /// <see cref="BrushCorePortion"/> of the radius and easing to zero at the rim, so a stamp has a
    /// soft edge instead of an aliased circle. Pure and separately testable — the falloff shape is
    /// the whole visual character of a stain, so it is not buried in a loop.</summary>
    public static float DiscCoverage(float distance, float radius)
    {
        if (radius <= 0f || distance >= radius)
        {
            return 0f;
        }

        var core = radius * BrushCorePortion;
        if (distance <= core)
        {
            return 1f;
        }

        var t = 1f - ((distance - core) / (radius - core));
        return t * t * (3f - (2f * t)); // smoothstep
    }

    /// <summary>Fraction of a brush's radius that is at full coverage before the rim falloff starts.
    /// 0.6 keeps a stamp reading as a solid mark at small pixel radii (a 3 px drip would otherwise be
    /// all falloff and look like a faint smudge) while still hiding the polygonal edge.</summary>
    public const float BrushCorePortion = 0.6f;

    /// <summary>The clipped texel rectangle a brush can possibly touch. Both stamp methods walk it
    /// inline rather than through a shared callback: a delegate per stamp would allocate on a path
    /// that runs several times a second while a bleeding body is dragged, which is exactly the kind
    /// of per-frame garbage docs/triage-performance.md exists to keep out.</summary>
    private (int MinX, int MinY, int MaxX, int MaxY) DiscBounds(Vector2 centerPixels, float radiusPixels) =>
        (Math.Max(0, (int)MathF.Floor(centerPixels.X - radiusPixels)),
         Math.Max(0, (int)MathF.Floor(centerPixels.Y - radiusPixels)),
         Math.Min(Width - 1, (int)MathF.Ceiling(centerPixels.X + radiusPixels)),
         Math.Min(Height - 1, (int)MathF.Ceiling(centerPixels.Y + radiusPixels)));

    /// <summary>Coverage of texel (<paramref name="x"/>, <paramref name="y"/>) sampled at its
    /// CENTRE, not its corner — a brush radius under half a texel would otherwise miss every sample
    /// and silently paint nothing.</summary>
    private static float CoverageAt(int x, int y, Vector2 centerPixels, float radiusPixels)
    {
        var dx = (x + 0.5f) - centerPixels.X;
        var dy = (y + 0.5f) - centerPixels.Y;
        return DiscCoverage(MathF.Sqrt((dx * dx) + (dy * dy)), radiusPixels);
    }

    private static byte ToByte(float channel01) => (byte)Math.Clamp(MathF.Round(channel01 * 255f), 0f, 255f);
}
