namespace Aigis.Render;

/// <summary>
/// GLSL source for the screen-space text/HUD overlay pipeline (TASK-041/TASK-051) — a flat,
/// unlit, untextured quad: <see cref="ScreenText.BuildQuads(IReadOnlyList{ScreenTextDraw})"/> already reduces every glyph to
/// solid-colour rectangles (<see cref="OverlayQuad"/>) in pixel space, so unlike
/// <see cref="ShaderSource.BoxVert"/>/<see cref="ShaderSource.BoxFrag"/> this needs no
/// lighting term, no per-vertex normal, and no shape-kind branch.
/// <para>
/// Compiled at <em>build time</em> into <see cref="PrecompiledShaders.OverlayVertSpirv"/>/
/// <see cref="PrecompiledShaders.OverlayFragSpirv"/> (TASK-057), same as the box/primitive
/// and line pairs — <c>Aigis.Player.GameHost</c> wires a <c>buildOverlayText</c> hook
/// straight into the per-frame render call, so unlike the debug-only triangle pipeline this
/// one is on the critical path for a game's very first HUD frame: compiling it at runtime
/// via <see cref="ShaderCompiler"/> would reintroduce exactly the shaderc native-load +
/// parse cost <see cref="PrecompiledShaders"/> exists to remove. <see cref="ShaderCompiler"/>
/// remains available for future agent-authored/hot-reloaded shaders; only this GLSL source
/// is kept here for readability/regeneration — see <see cref="PrecompiledShaders"/>'s doc
/// comment for the regeneration procedure, and <c>PrecompiledShadersSyncTests</c> for the
/// gate that catches a forgotten regeneration.
/// </para>
/// <para>
/// The CPU side (<c>HeadlessRenderer.Overlay.cs</c>/<c>WindowedRenderer.Overlay.cs</c>)
/// converts each <see cref="OverlayQuad"/> (pixel-space centre + size) into this push
/// constant's <c>rect</c> (a scale+translate pair, no matrix): <c>rect.xy</c> scales the
/// unit quad's local <c>[-0.5, 0.5]</c> corners to the quad's pixel size (as a fraction of
/// render-target size, doubled — NDC spans <c>[-1, 1]</c>), <c>rect.zw</c> translates the
/// quad's centre into NDC. This is the <b>single place</b> pixel-space Y (top-left origin,
/// down-positive — <see cref="ScreenTextDraw"/>'s convention) becomes NDC Y: unlike
/// <see cref="Camera.ApplyVulkanClip"/>'s perspective-camera flip (needed because that path
/// starts from a Y-up world), pixel-space Y already agrees with Vulkan's Y-down NDC with
/// zero sign flip (see <see cref="Camera.ScreenPointToRay"/>'s own doc comment making the
/// same observation for mouse-picking) — so this path deliberately does not call
/// <see cref="Camera.ApplyVulkanClip"/> at all, rather than risk a second, competing flip.
/// </para>
/// </summary>
public static class OverlaySource
{
    public const string OverlayVert = """
        #version 450
        layout(location = 0) in vec2 inPos;
        layout(push_constant) uniform Push { vec4 rect; vec4 color; } pc;
        layout(location = 0) out vec4 vColor;
        void main() {
            vec2 ndc = inPos * pc.rect.xy + pc.rect.zw;
            gl_Position = vec4(ndc, 0.0, 1.0);
            vColor = pc.color;
        }
        """;

    public const string OverlayFrag = """
        #version 450
        layout(location = 0) in vec4 vColor;
        layout(location = 0) out vec4 outColor;
        void main() { outColor = vColor; }
        """;

    /// <summary>
    /// TASK-159's textured overlay quad — a SECOND pipeline rather than an extension of the pair
    /// above, deliberately: the text path draws thousands of glyph rectangles per frame and its
    /// value is being descriptor-free (no per-draw set bind), so bolting a sampler onto it would
    /// tax every glyph for a capability only HUD icons use.
    /// <para>
    /// Shares the <c>rect</c> push constant and the <b>same</b> unit-quad vertex buffer as
    /// <see cref="OverlayVert"/> — no second vertex attribute and no second mesh: the UV is derived
    /// from the position, since the quad's local <c>[-0.5, 0.5]</c> corners map to <c>[0, 1]</c> by
    /// a single <c>+ 0.5</c>. That is also why there is no Y flip in the UV: local <c>-0.5</c> in Y
    /// is the rect's TOP edge on screen (pixel space is Y-down and already agrees with Vulkan NDC —
    /// see this class's doc comment), <c>uv.y = 0</c> samples the texture's first row, and
    /// <c>TextureData</c>/<c>PngReader</c> both put the image's top row first. Top-of-rect therefore
    /// samples top-of-image with zero sign flips anywhere in the chain — verified by rendering an
    /// asymmetric four-quadrant texture, not by this argument alone (TASK-159 render tests).
    /// </para>
    /// </summary>
    public const string OverlayImageVert = """
        #version 450
        layout(location = 0) in vec2 inPos;
        layout(push_constant) uniform Push { vec4 rect; vec4 tint; } pc;
        layout(location = 0) out vec2 vUv;
        layout(location = 1) out vec4 vTint;
        void main() {
            vec2 ndc = inPos * pc.rect.xy + pc.rect.zw;
            gl_Position = vec4(ndc, 0.0, 1.0);
            vUv = inPos + 0.5;
            vTint = pc.tint;
        }
        """;

    /// <summary>Samples the icon and multiplies by the draw's tint. The pipeline blends with
    /// <c>SrcAlpha</c>/<c>OneMinusSrcAlpha</c>, so the alpha this writes is what cuts a transparent
    /// icon out of its rect — see <c>OverlayImagePipeline</c>.</summary>
    public const string OverlayImageFrag = """
        #version 450
        layout(location = 0) in vec2 vUv;
        layout(location = 1) in vec4 vTint;
        layout(set = 0, binding = 0) uniform sampler2D tex;
        layout(location = 0) out vec4 outColor;
        void main() { outColor = texture(tex, vUv) * vTint; }
        """;
}
