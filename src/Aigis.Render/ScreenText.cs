using System.Numerics;

namespace Aigis.Render;

/// <summary>Horizontal alignment for a <see cref="ScreenTextDraw"/> — the HUD anchor points
/// (top-left/top-centre/top-right) TASK-051 requires. <see cref="ScreenTextDraw.ScreenPosPixels"/>
/// is always the anchor point itself; <see cref="TextAnchor.Center"/>/<see cref="TextAnchor.Right"/>
/// shift the rendered glyphs left of that point by half/all of the measured text width.</summary>
public enum TextAnchor
{
    Left,
    Center,
    Right,
}

/// <summary>One screen-space text draw request: <paramref name="Text"/> at
/// <paramref name="ScreenPosPixels"/> (pixels, origin top-left, Y increasing downward — the
/// same convention as <c>Aigis.Platform.InputSnapshot</c> and <c>Camera.ScreenPointToRay</c>,
/// so a HUD position and a mouse position are already directly comparable),
/// <paramref name="PixelHeight"/> tall, in <paramref name="Color"/>, anchored per
/// <paramref name="Anchor"/>. <paramref name="ScreenPosPixels"/>'s <c>X</c> is the horizontal
/// anchor point <see cref="TextAnchor"/> is relative to (left edge/center/right edge
/// depending on <paramref name="Anchor"/>); its <c>Y</c> is always the <b>top edge</b> of the
/// rendered text, unconditionally — there is no vertical equivalent of <see cref="TextAnchor"/>,
/// so a caller wanting e.g. bottom-anchored text must subtract the measured height itself.</summary>
public readonly record struct ScreenTextDraw(
    string Text,
    Vector2 ScreenPosPixels,
    float PixelHeight,
    Vector4 Color,
    TextAnchor Anchor = TextAnchor.Left);

/// <summary>One solid-colour overlay rectangle in pixel space (origin top-left, Y down —
/// same convention as <see cref="ScreenTextDraw"/>), centered at <paramref name="CenterPixels"/>.
/// This is <see cref="ScreenText.BuildQuads(IReadOnlyList{ScreenTextDraw})"/>'s output: pure pixel-space geometry that does
/// not know the render target's actual size — <c>HeadlessRenderer</c>/<c>WindowedRenderer</c>
/// convert it to NDC using their own width/height at draw time, which is exactly what makes
/// text resolution-independent (the same <see cref="OverlayQuad"/> list renders identically,
/// same pixel size and position, at any output resolution).</summary>
public readonly record struct OverlayQuad(Vector2 CenterPixels, Vector2 SizePixels, Vector4 Color);

/// <summary>
/// Builds screen-space overlay geometry for text — the resolution-/camera-independent HUD
/// path TASK-041/TASK-051 add alongside the world-space <see cref="BitmapFont"/> stopgap.
/// Reuses <see cref="GlyphRasterizer"/>'s scanline runs (solid rectangles, no font-atlas
/// texture or GPU sampler) so the renderer side only needs one more untextured, unlit quad
/// pipeline (see <c>HeadlessRenderer.Overlay.cs</c>/<c>WindowedRenderer.Overlay.cs</c>) — no
/// new descriptor-set infrastructure.
/// </summary>
public static class ScreenText
{
    /// <summary>
    /// TASK-153: digits and punctuation rasterise with visibly SHORTER ink than kanji at the
    /// identical nominal <see cref="ScreenTextDraw.PixelHeight"/> — GDI+'s measured line height
    /// (<see cref="GlyphRasterizer.RawGlyphs.ContentHeight"/>, the divisor <see cref="BuildQuads(IReadOnlyList{ScreenTextDraw})"/>'s
    /// <c>cell</c> is built from) reflects the font's full ascent+descent, which kanji glyphs use
    /// nearly all of and a bare digit/percent/punctuation mark does not (no descender, shorter
    /// cap-height). Frame-review of the pause menu's options screen measured <c>100%</c>/<c>＋</c>
    /// at nominal <c>HudTypeScale.H2</c> (20px) rasterizing to 15-16px of actual ink — under
    /// TASK-099's ~18px legibility floor — while kanji at the identical nominal size measured
    /// 19-21px. <c>HudTypeScale.H2</c> is already the largest step below <c>Display</c> available
    /// to a secondary control (see <c>PauseMenuHud.BuildOptions</c>'s own doc comment on why the
    /// volume LABEL is already forced up to H2), so a numeric readout has no larger ON-SCALE step
    /// left to move to.
    /// <para>
    /// The fix compensates the RASTERIZATION size only, for text classified as numeric/punctuation
    /// by <see cref="IsNumericOrPunctuationOnly"/> — it never touches the declared, tested
    /// <see cref="ScreenTextDraw.PixelHeight"/> itself (that stays exactly on
    /// <c>HudTypeScale</c>'s six steps; <c>PauseMenuTests.MenuAndOptionsDraws_UseOnlyTheSixTypeScaleSteps</c>
    /// still holds). Same conservative-margin convention <c>TriageHudStyleTests.TextHeightOvershootFactor</c>
    /// already uses elsewhere for GDI+'s measured overshoot, applied at the opposite end (GDI+
    /// UNDER-renders a short glyph's cap-height here, rather than over-rendering a full line's
    /// ascent+descent there).
    /// </para>
    /// </summary>
    internal const float NumericPunctuationInkCompensation = 1.3f;

    /// <summary>
    /// Whether <paramref name="text"/> consists ENTIRELY of ASCII digits and a small, explicit set
    /// of HUD punctuation (see <see cref="NumericPunctuationInkCompensation"/>'s doc comment) —
    /// deliberately narrow rather than "no kanji", so this never silently compensates a real word
    /// (e.g. the allowed Latin key names <c>WASD</c>/<c>Shift</c>) that was never the measured
    /// defect. <c>「</c>/<c>」</c> are included so a FOCUSED numeric control (wrapped by
    /// <c>MenuFocusPresentation.FocusMark</c>) still classifies as numeric/punctuation.
    /// </summary>
    internal static bool IsNumericOrPunctuationOnly(string text)
    {
        foreach (var c in text)
        {
            if (!(char.IsAsciiDigit(c) || c is '%' or '+' or '-' or '.' or ' ' or '＋' or '－' or '「' or '」'))
            {
                return false;
            }
        }

        return true;
    }

    /// <summary>
    /// Converts each <see cref="ScreenTextDraw"/> into its glyph rectangles. A draw whose
    /// text is null/blank, or whose font rasterization is unavailable (see
    /// <see cref="GlyphRasterizer.Rasterize(string, float)"/>'s fallback), silently contributes no quads —
    /// a HUD must never throw over an unsupported/empty label.
    /// </summary>
    public static IReadOnlyList<OverlayQuad> BuildQuads(IReadOnlyList<ScreenTextDraw> draws)
    {
        var quads = new List<OverlayQuad>();
        BuildQuads(draws, quads);
        return quads;
    }

    /// <summary>
    /// <see cref="BuildQuads(IReadOnlyList{ScreenTextDraw})"/>'s allocation-free form: clears
    /// <paramref name="quads"/> and refills it with the same geometry, so a per-frame caller can own
    /// one buffer for the lifetime of the renderer instead of minting a list every frame. Identical
    /// output by construction — the allocating overload above is implemented in terms of this one.
    /// <para>
    /// Exists because this is the single largest per-frame allocation in a real game. Measured on a
    /// Level6 Triage session, a HUD of only 14 text draws expands to about 2,700 glyph rectangles —
    /// one <see cref="OverlayQuad"/> per scanline run — which is 85 KB a frame, right at the
    /// large-object-heap threshold, and it accounted for over half of the frame's total allocation
    /// on its own. Because it landed on the LOH, every gen1 collection was promoted to a full gen2.
    /// </para>
    /// <para>
    /// An instance buffer rather than a shared static one: <see cref="HeadlessRenderer"/> instances
    /// can legitimately be driven from different threads (render tests do), and a shared static
    /// scratch list would make two concurrent renders corrupt each other's geometry.
    /// </para>
    /// </summary>
    public static void BuildQuads(IReadOnlyList<ScreenTextDraw> draws, List<OverlayQuad> quads)
    {
        ArgumentNullException.ThrowIfNull(draws);
        ArgumentNullException.ThrowIfNull(quads);

        quads.Clear();
        foreach (var draw in draws)
        {
            if (string.IsNullOrWhiteSpace(draw.Text) || draw.PixelHeight <= 0f || !float.IsFinite(draw.PixelHeight))
            {
                continue;
            }

            // TASK-153: numeric/punctuation-only text is rasterized (and cell-scaled) at a taller
            // EFFECTIVE height than its declared PixelHeight — see NumericPunctuationInkCompensation's
            // own doc comment. The declared PixelHeight (used for layout/positioning below) is
            // untouched; only the rasterization/cell math this method owns sees the compensated value.
            var effectiveHeight = IsNumericOrPunctuationOnly(draw.Text)
                ? draw.PixelHeight * NumericPunctuationInkCompensation
                : draw.PixelHeight;

            // Rasterize AT the requested size (TASK-099), not at a fixed reference height rescaled
            // afterward — otherwise every size below the old 18px reference downscaled a bitmap whose
            // strokes were already ~1px, and shrinking a 1px stroke below 1px loses it instead of
            // thinning it (worst for kanji, which pack more strokes into the same em).
            var raw = GlyphRasterizer.Rasterize(draw.Text, effectiveHeight);
            if (raw.Runs.Length == 0 || raw.BitmapHeight == 0)
            {
                continue; // no font available on this platform — draw nothing, never throw.
            }

            // ContentHeight, NOT BitmapHeight: the bitmap carries +4 rows of antialias slack, and dividing
            // the requested size by the PADDED height rendered every text in the engine at roughly 0.6x
            // what the caller asked for. Display-sized text still read fine, which is why it survived —
            // caption-sized kanji did not, and frame-reviewer caught it on TASK-094's HUD. Now that
            // rasterization itself happens at ~effectiveHeight, this divide is a near-1.0 correction for
            // GDI+'s content-vs-em rounding, not a large down/up-scale.
            //
            // TASK-099 follow-up: GDI+'s MeasureString line height (ContentHeight) runs a bit TALLER
            // than the pixel font size requested (measured at Body 14px: ContentHeight 16, cell 0.875;
            // at Caption 11px: ContentHeight 13, cell 0.846) — small, but every source run is already
            // exactly 1px, so a sub-1.0 cell shrinks it below 1px and the overlay's non-antialiased
            // rasterization keeps or drops each run purely on sub-pixel phase (frame-reviewer caught two
            // identical "0" digits losing DIFFERENT strokes at the same size/line/colour). Floor the
            // cell at 1.0: a caller may end up very slightly larger than requested, never smaller —
            // "rasterize at or above the requested size" (the board's own TASK-099 fix direction) has to
            // hold at this final geometry step too, not just at the GlyphRasterizer height choice.
            var cell = MathF.Max(1f, effectiveHeight / raw.ContentHeight);
            var totalWidth = raw.BitmapWidth * cell;
            var anchorOffsetX = draw.Anchor switch
            {
                TextAnchor.Left => 0f,
                TextAnchor.Center => -totalWidth * 0.5f,
                TextAnchor.Right => -totalWidth,
                _ => throw new ArgumentOutOfRangeException(nameof(draws), $"Unsupported anchor: {draw.Anchor}."),
            };

            // TASK-115 (found by frame review of the result screen's chart cards): the TASK-153
            // compensation above makes the numeric run's CELL ~30% taller, and since the draw's Y is the
            // cell's TOP, all of that extra height used to hang off the bottom — every digit sat ~4px
            // lower than the kanji sharing its baseline (measured at Caption: kanji ink y 309-321, the
            // age digit's ink y 313-325). Pulling the compensated cell up by exactly the height it
            // gained re-aligns its bottom with an uncompensated neighbour's, which is where a digit's
            // baseline belongs. Zero for every non-compensated draw, so nothing else moves.
            var compensationLift = effectiveHeight - draw.PixelHeight;

            foreach (var run in raw.Runs)
            {
                var size = new Vector2(run.Width * cell, cell);
                var left = draw.ScreenPosPixels.X + anchorOffsetX + run.X * cell;
                var top = draw.ScreenPosPixels.Y - compensationLift + run.Y * cell;
                var center = new Vector2(left + size.X * 0.5f, top + size.Y * 0.5f);
                quads.Add(new OverlayQuad(center, size, draw.Color));
            }
        }
    }

    /// <summary>
    /// The width, in pixels, that <see cref="BuildQuads(IReadOnlyList{ScreenTextDraw})"/> would give <paramref name="text"/> rendered
    /// at <paramref name="pixelHeight"/> — the same <c>BitmapWidth * (pixelHeight / BitmapHeight)</c>
    /// scaling that method applies internally, exposed so a HUD can lay several differently-coloured
    /// runs out on ONE line (advance x by the previous run's measured width) instead of stacking them
    /// on separate lines or composing them into a single-colour string.
    /// <para>
    /// Returns 0 for text with nothing to rasterize (empty or whitespace-only — the same inputs
    /// <see cref="BuildQuads(IReadOnlyList{ScreenTextDraw})"/> skips and <see cref="GlyphRasterizer.Rasterize(string, float)"/> rejects), a
    /// non-finite/non-positive height, or a platform where glyph rasterization is unavailable (see
    /// that method's documented degrade-to-empty behaviour). Zero is the right answer in those cases
    /// rather than an estimate: <see cref="BuildQuads(IReadOnlyList{ScreenTextDraw})"/> emits no geometry for them either, so a
    /// caller advancing by this width lays out invisible text against invisible text. A caller that
    /// needs a gap between runs should add its own authored spacing rather than measuring a space.
    /// </para>
    /// </summary>
    public static float MeasurePixelWidth(string text, float pixelHeight)
    {
        if (string.IsNullOrWhiteSpace(text) || pixelHeight <= 0f || !float.IsFinite(pixelHeight))
        {
            return 0f;
        }

        // Must use the SAME rasterization height and divisor BuildQuads does (including the sub-1.0
        // floor and the TASK-153 numeric/punctuation ink compensation — see its comments), or a
        // caller laying text out from this measurement would position it against a different cell
        // size than it actually renders at.
        var effectiveHeight = IsNumericOrPunctuationOnly(text) ? pixelHeight * NumericPunctuationInkCompensation : pixelHeight;
        var raw = GlyphRasterizer.Rasterize(text, effectiveHeight);
        return raw.ContentHeight == 0 ? 0f : raw.BitmapWidth * MathF.Max(1f, effectiveHeight / raw.ContentHeight);
    }
}
