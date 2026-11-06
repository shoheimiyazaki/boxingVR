using System.Numerics;

namespace Aigis.Render;

/// <summary>
/// One screen-space UI surface: a (optionally rounded, optionally bordered, optionally gradient-
/// filled, optionally feathered) rectangle, in the same pixel convention as
/// <see cref="ScreenTextDraw"/> and <see cref="OverlayImageDraw"/> — origin top-left, Y down,
/// centre-anchored — so a card, its icon and its label are all positioned in one coordinate system
/// and land on exactly the same screen rect (they share <see cref="OverlayRect"/>'s NDC mapping).
/// <para>
/// This is the primitive the HUD had been working around. Before it, a "panel" was either a run of
/// <c>█</c> glyphs on the opaque text path or a fixed-size authored texture on the image path; both
/// are recorded in <c>Game.Triage</c>'s own doc comments as substitutions rather than intent.
/// A shape is resolution-independent, size-independent and texture-free: the fragment shader
/// evaluates the rounded-rect signed distance analytically (see <see cref="UiShapeSource"/>), so the
/// same struct draws a 2px divider and a full-screen scrim with equally crisp corners.
/// </para>
/// <para>
/// <b>Draw order</b> is list order, and shapes as a whole draw <i>under</i>
/// <see cref="OverlayImageDraw"/> icons and <see cref="ScreenTextDraw"/> text. Painter's algorithm,
/// no depth: a caller stacks a shadow, then a card, then a border highlight by emitting them in that
/// order. There is no z-index and there should not be one — a HUD that needs one has a layout
/// problem, not a renderer problem.
/// </para>
/// </summary>
/// <param name="CenterPixels">Rect centre, pixels, origin top-left, Y down.</param>
/// <param name="SizePixels">Rect width/height in pixels, before <paramref name="SoftnessPixels"/>
/// spreads coverage beyond it. This is the size the shape reads as, so a caller lays out against it
/// directly and never has to reason about the padded quad the renderer actually draws.</param>
/// <param name="FillColor">Interior colour. Straight (non-premultiplied) alpha; <c>W = 0</c> makes
/// an outline-only shape.</param>
/// <param name="CornerRadiiPixels">Corner radii in pixels, ordered <b>top-left, top-right,
/// bottom-right, bottom-left</b> — clockwise from top-left in screen terms, since Y is down. Each is
/// clamped to half the shorter side by the shader, so a deliberately huge value is the supported way
/// to ask for a pill/circle without the caller measuring the box first.</param>
/// <param name="BorderColor">Stroke colour, drawn as an inset ring of
/// <paramref name="BorderPixels"/> width along the inside of the shape's edge. Inset rather than
/// centred or outset so a bordered card occupies exactly <paramref name="SizePixels"/> — the same
/// rule CSS's <c>box-sizing: border-box</c> follows, and for the same reason: layout math stays
/// independent of whether an element happens to have a border.</param>
/// <param name="BorderPixels">Stroke width. Zero (the default) draws no ring at all.</param>
/// <param name="SoftnessPixels">Half-width of the edge feather, in pixels. This one parameter spans
/// the whole range from antialiasing to blur: the shader floors it at 0.5px so every shape is
/// crisply antialiased by default, and a large value (16–40px) turns the same draw into a soft drop
/// shadow or glow. There is deliberately no separate shadow primitive — a shadow is this shape,
/// offset, dark, and feathered, emitted before the card it sits under.</param>
/// <param name="FillColorEnd">The gradient's far stop. Ignored unless
/// <paramref name="GradientAxis"/> is non-zero; the factory methods below keep it equal to
/// <paramref name="FillColor"/> for a flat fill so a caller never has to think about it.</param>
/// <param name="GradientAxis">Direction of the linear gradient in shape-local space, where the
/// shape spans -0.5..+0.5 on each axis. <c>(0, 1)</c> runs <paramref name="FillColor"/> at the top
/// to <paramref name="FillColorEnd"/> at the bottom; <c>(1, 0)</c> runs left to right; a longer
/// vector compresses the ramp toward the centre. <see cref="Vector2.Zero"/> (the default) is a flat
/// fill.</param>
public readonly record struct UiShapeDraw(
    Vector2 CenterPixels,
    Vector2 SizePixels,
    Vector4 FillColor,
    Vector4 CornerRadiiPixels = default,
    Vector4 BorderColor = default,
    float BorderPixels = 0f,
    float SoftnessPixels = 0f,
    Vector4 FillColorEnd = default,
    Vector2 GradientAxis = default)
{
    /// <summary>A flat-filled rounded rectangle with the same radius on all four corners — the
    /// common case, and the one every card, pill, track and chip is built from.</summary>
    public static UiShapeDraw Rounded(Vector2 centerPixels, Vector2 sizePixels, float radiusPixels, Vector4 fillColor) =>
        new(centerPixels, sizePixels, fillColor, Uniform(radiusPixels), FillColorEnd: fillColor);

    /// <summary>A rounded rectangle with a vertical two-stop gradient, <paramref name="topColor"/>
    /// at the top edge. The default surface treatment for a large panel: a flat translucent fill
    /// over a busy 3D scene reads as a grey smear, while a few percent of vertical falloff gives the
    /// panel an implied light source and separates it from whatever is behind it.</summary>
    public static UiShapeDraw VerticalGradient(
        Vector2 centerPixels, Vector2 sizePixels, float radiusPixels, Vector4 topColor, Vector4 bottomColor) =>
        new(centerPixels, sizePixels, topColor, Uniform(radiusPixels), FillColorEnd: bottomColor, GradientAxis: new Vector2(0f, 1f));

    /// <summary>An outline-only rounded rectangle — a focus ring, a selection outline, an empty
    /// slot. Transparent interior, so whatever it is drawn over still shows through.</summary>
    public static UiShapeDraw Outline(
        Vector2 centerPixels, Vector2 sizePixels, float radiusPixels, Vector4 borderColor, float borderPixels) =>
        new(centerPixels, sizePixels, default, Uniform(radiusPixels), borderColor, borderPixels);

    /// <summary>
    /// A soft drop shadow for a shape of <paramref name="sizePixels"/> at
    /// <paramref name="centerPixels"/>, blurred by <paramref name="blurPixels"/> and pushed down by
    /// <paramref name="offsetYPixels"/>.
    /// <para>
    /// Emit this <b>before</b> the shape it belongs to. The shadow's own rect is deliberately the
    /// caster's rect rather than an inflated one: the feather spreads outward from there on its own,
    /// which is what makes the penumbra sit correctly around the caster instead of starting at some
    /// arbitrary inflated edge.
    /// </para>
    /// </summary>
    public static UiShapeDraw Shadow(
        Vector2 centerPixels, Vector2 sizePixels, float radiusPixels, Vector4 shadowColor,
        float blurPixels, float offsetYPixels = 0f) =>
        new(
            centerPixels + new Vector2(0f, offsetYPixels),
            sizePixels,
            shadowColor,
            Uniform(radiusPixels),
            SoftnessPixels: blurPixels,
            FillColorEnd: shadowColor);

    /// <summary>The same radius on all four corners, in this type's TL/TR/BR/BL order.</summary>
    public static Vector4 Uniform(float radiusPixels) => new(radiusPixels, radiusPixels, radiusPixels, radiusPixels);

    /// <summary>Radii for a shape rounded only along its top edge — a tab, a card header, the top
    /// cap of a stacked group.</summary>
    public static Vector4 TopOnly(float radiusPixels) => new(radiusPixels, radiusPixels, 0f, 0f);

    /// <summary>Radii for a shape rounded only along its bottom edge.</summary>
    public static Vector4 BottomOnly(float radiusPixels) => new(0f, 0f, radiusPixels, radiusPixels);

    /// <summary>The far gradient stop actually used by the shader: <see cref="FillColorEnd"/> when a
    /// gradient was asked for, and <see cref="FillColor"/> otherwise. Centralized here rather than
    /// left to each renderer so the flat-fill default cannot be a <c>default(Vector4)</c> —
    /// transparent black — leaking into one path's gradient and not the other's.</summary>
    internal Vector4 EffectiveFillColorEnd => GradientAxis == Vector2.Zero ? FillColor : FillColorEnd;

    /// <summary>How far beyond <see cref="SizePixels"/> the drawn quad must extend so a feathered
    /// edge is not sliced off square at the shape's own bounds. One pixel of slack past the feather
    /// covers the shader's 0.5px antialiasing floor and rounding at either end.</summary>
    internal float PaddingPixels => MathF.Max(SoftnessPixels, 0.5f) + 1f;
}
