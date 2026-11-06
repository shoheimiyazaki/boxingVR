namespace Aigis.Render;

/// <summary>
/// GLSL for the screen-space <b>UI shape</b> pipeline — the analytic rounded-rectangle draw that
/// gives this engine's HUDs the surface vocabulary a modern interface is built out of: cards,
/// panels, pills, dividers, progress tracks, focus rings, scrims and soft drop shadows.
/// <para>
/// <b>Why a third overlay pipeline rather than reusing one of the two that exist.</b>
/// <see cref="OverlaySource.OverlayVert"/>'s untextured quad is the glyph path: it is deliberately
/// opaque (<c>BlendEnable = false</c>) and deliberately descriptor-free, because
/// <see cref="ScreenText.BuildQuads(IReadOnlyList{ScreenTextDraw})"/> emits thousands of one-pixel-tall scanline rectangles per
/// frame and every feature bolted onto it is paid for by all of them. It cannot express a
/// translucent surface at all. <see cref="OverlaySource.OverlayImageVert"/>'s textured quad
/// <i>can</i> blend, but its shape has to be authored INTO pixels at a fixed size and sampled
/// <c>Filter.Nearest</c>, so a card whose width depends on its content would need its own texture
/// (against a 96-texture residency cap) and would still show stair-stepped corners when drawn at
/// any size other than the authored one.
/// </para>
/// <para>
/// An SDF solves exactly that: the shape is evaluated per fragment from the rect's own geometry, so
/// one pipeline and zero textures draw a crisp rounded corner at <i>any</i> size, with a border, a
/// two-stop gradient and an arbitrarily large feather — all from push constants. The feather is the
/// same parameter twice over: at sub-pixel width it is antialiasing, and at 20px it is a drop
/// shadow. That is why there is no separate shadow pipeline.
/// </para>
/// <para>
/// <b>Conventions inherited unchanged.</b> The <c>rect</c> push constant is the same scale+translate
/// pair the other two overlay pipelines use, built by the same <see cref="OverlayRect"/> helper —
/// hard rule 4's "one place converts pixel space to NDC" holds across all three. Pixel space is
/// origin-top-left, Y-down, which already agrees with Vulkan's NDC, so there is no Y flip here
/// either (see <see cref="OverlaySource"/>'s doc comment for the full argument).
/// </para>
/// <para>
/// Compiled at build time into <see cref="PrecompiledShaders.UiShapeVertSpirv"/>/
/// <see cref="PrecompiledShaders.UiShapeFragSpirv"/> for the same reason every other built-in pair
/// is: a game's first HUD frame draws shapes, so compiling this via <see cref="ShaderCompiler"/> at
/// run time would reintroduce shaderc's native-load + parse cost on the critical path to
/// time-to-first-frame. <c>PrecompiledShadersSyncTests</c> recompiles this source at test time and
/// fails if the embedded words drift from it.
/// </para>
/// </summary>
public static class UiShapeSource
{
    /// <summary>
    /// Expands the shared unit quad to the shape's <b>padded</b> rect and hands the fragment stage
    /// its position in pixels relative to the shape's centre.
    /// <para>
    /// The padding is the reason <c>extra.zw</c> exists. A feathered edge spreads coverage OUTSIDE
    /// the shape's own rect, so a drop shadow drawn on a quad exactly the size of its shape would be
    /// sliced off square at the shape's bounds — the blur would be visible on the inside and absent
    /// on the outside. The CPU side therefore scales the quad to <c>size + 2·pad</c> and passes the
    /// padded half-extent here, while <c>shape.xy</c> keeps the <i>unpadded</i> half-extent the SDF
    /// is actually evaluated against. Deriving the local pixel coordinate from the padded extent
    /// rather than from the render-target size is what keeps this shader independent of resolution.
    /// </para>
    /// </summary>
    public const string UiShapeVert = """
        #version 450
        layout(location = 0) in vec2 inPos;
        layout(push_constant) uniform Push {
            vec4 rect;
            vec4 fill;
            vec4 fillEnd;
            vec4 border;
            vec4 radii;
            vec4 shape;
            vec4 extra;
        } pc;
        layout(location = 0) out vec2 vLocalPx;
        void main() {
            vec2 ndc = inPos * pc.rect.xy + pc.rect.zw;
            gl_Position = vec4(ndc, 0.0, 1.0);
            vLocalPx = inPos * (pc.extra.zw * 2.0);
        }
        """;

    /// <summary>
    /// Evaluates the rounded-rectangle signed distance and composites fill, gradient and border.
    /// <para>
    /// <b>Push constant layout</b> (112 bytes, inside the 128-byte minimum Vulkan guarantees):
    /// <c>rect</c> = NDC scale.xy + translate.xy; <c>fill</c>/<c>fillEnd</c> = the gradient's two
    /// stops (equal for a flat fill); <c>border</c> = the stroke colour; <c>radii</c> = corner radii
    /// in pixels ordered <b>top-left, top-right, bottom-right, bottom-left</b> — clockwise from the
    /// top-left in <i>screen</i> terms, since Y is down here; <c>shape</c> = unpadded half-extent
    /// .xy, border width .z, feather radius .w; <c>extra</c> = gradient axis .xy, padded half-extent
    /// .zw.
    /// </para>
    /// <para>
    /// <b>The output is straight (non-premultiplied) alpha</b>, because the pipeline blends
    /// <c>SrcAlpha</c>/<c>OneMinusSrcAlpha</c> like the textured overlay does. Fill and border are
    /// therefore composited in premultiplied space (each colour weighted by its own alpha AND its
    /// own coverage) and divided back out at the end. Compositing them in straight space instead —
    /// the obvious <c>mix(fill, border, ring)</c> — darkens the seam wherever the two differ in
    /// alpha, which is every card that has a translucent fill and an opaque hairline border, i.e.
    /// the single most common shape this pipeline draws.
    /// </para>
    /// <para>
    /// The radius is clamped to half the shorter side so an over-large radius degrades to a
    /// stadium/circle instead of inverting the distance field. That makes "a pill" expressible as
    /// "a rect with a huge radius" and lets a caller pass a radius token without checking it against
    /// the box it is about to be applied to.
    /// </para>
    /// </summary>
    public const string UiShapeFrag = """
        #version 450
        layout(location = 0) in vec2 vLocalPx;
        layout(push_constant) uniform Push {
            vec4 rect;
            vec4 fill;
            vec4 fillEnd;
            vec4 border;
            vec4 radii;
            vec4 shape;
            vec4 extra;
        } pc;
        layout(location = 0) out vec4 outColor;

        float roundedBoxSdf(vec2 p, vec2 halfSize, float radius) {
            vec2 q = abs(p) - halfSize + radius;
            return min(max(q.x, q.y), 0.0) + length(max(q, vec2(0.0))) - radius;
        }

        void main() {
            vec2 halfSize = max(pc.shape.xy, vec2(0.0));
            float maxRadius = min(halfSize.x, halfSize.y);
            float topRadius = (vLocalPx.x > 0.0) ? pc.radii.y : pc.radii.x;
            float bottomRadius = (vLocalPx.x > 0.0) ? pc.radii.z : pc.radii.w;
            float radius = clamp((vLocalPx.y > 0.0) ? bottomRadius : topRadius, 0.0, maxRadius);

            float dist = roundedBoxSdf(vLocalPx, halfSize, radius);
            float feather = max(pc.shape.w, 0.5);
            float outerCoverage = 1.0 - smoothstep(-feather, feather, dist);
            float innerCoverage = 1.0 - smoothstep(-feather, feather, dist + max(pc.shape.z, 0.0));
            float ringCoverage = max(outerCoverage - innerCoverage, 0.0);

            vec2 span = max(halfSize * 2.0, vec2(0.0001));
            float t = clamp(dot(vLocalPx / span, pc.extra.xy) + 0.5, 0.0, 1.0);
            vec4 fillColor = mix(pc.fill, pc.fillEnd, t);

            float alpha = fillColor.a * innerCoverage + pc.border.a * ringCoverage;
            vec3 weighted = fillColor.rgb * (fillColor.a * innerCoverage)
                          + pc.border.rgb * (pc.border.a * ringCoverage);
            outColor = vec4(alpha > 0.0001 ? weighted / alpha : vec3(0.0), alpha);
        }
        """;
}
