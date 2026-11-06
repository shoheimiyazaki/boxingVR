using Silk.NET.Vulkan;

namespace Aigis.Render;

/// <summary>
/// The windowed half of the miniature finishing pass — composition only; the Vulkan work is the
/// shared <see cref="PostProcessPipeline"/>, the same instance type the headless renderer uses, so a
/// window and an agent's screenshot finish the frame identically (hard rule 6).
/// </summary>
public sealed unsafe partial class WindowedRenderer
{
    private PostProcessPipeline? _postProcess;

    /// <summary>
    /// The fraction of the window's resolution the 3D scene is drawn at when the frame has a finishing
    /// pass (2026-10-16, 画質設定): 1 draws it full size; below 1 the scene pass renders into a smaller
    /// offscreen target and the finishing pass, which samples it by uv through a linear sampler, scales
    /// it up to the window. The HUD is drawn afterwards at full resolution, so text stays sharp. A frame
    /// without a finishing pass ignores it (there is no offscreen target to shrink). Clamped to
    /// [<see cref="MinRenderScale"/>, 1]; a change rebuilds the target once, after a device idle.
    /// </summary>
    public float RenderScale
    {
        get => _renderScale;
        set => _renderScale = float.IsFinite(value) ? Math.Clamp(value, MinRenderScale, 1f) : 1f;
    }

    private float _renderScale = 1f;

    /// <summary>The smallest <see cref="RenderScale"/> honoured.</summary>
    public const float MinRenderScale = 0.25f;

    /// <summary>The scene's own size this frame: the window's, scaled by <see cref="RenderScale"/>.</summary>
    private Extent2D SceneExtent => new(
        Math.Max(1u, (uint)MathF.Round(_swapchainExtent.Width * _renderScale)),
        Math.Max(1u, (uint)MathF.Round(_swapchainExtent.Height * _renderScale)));

    /// <summary>A rectangle in window pixels, in the scaled scene's pixels — edges rounded, so two views
    /// that meet in the window still meet in the scene.</summary>
    private RenderViewport ToScene(RenderViewport rect)
    {
        if (_renderScale >= 1f)
        {
            return rect;
        }

        var x0 = (int)MathF.Round(rect.XPx * _renderScale);
        var y0 = (int)MathF.Round(rect.YPx * _renderScale);
        var x1 = (int)MathF.Round((rect.XPx + rect.WidthPx) * _renderScale);
        var y1 = (int)MathF.Round((rect.YPx + rect.HeightPx) * _renderScale);
        return new RenderViewport(x0, y0, Math.Max(1, x1 - x0), Math.Max(1, y1 - y0));
    }

    /// <summary>Builds (or, after a resize, rebuilds) the offscreen scene target. Outside any render
    /// pass. Returns the framebuffer and pass the scene is recorded in.</summary>
    private (RenderPass Pass, Framebuffer Framebuffer) PreparePostProcess(in PostProcessSettings settings)
    {
        _postProcess ??= new PostProcessPipeline(_vk, _physicalDevice, _device);
        var scene = SceneExtent;
        _postProcess.Prepare(_renderPass, _swapchainFormat, DepthFormat,
            scene.Width, scene.Height, dynamicViewport: true);
        return (_postProcess.SceneRenderPass, _postProcess.SceneFramebuffer);
    }

    /// <summary>Called when the swapchain's image objects are torn down, with the device idle: the
    /// target is rebuilt at the new size on the next post-processed frame.</summary>
    private void ReleasePostProcessTarget() => _postProcess?.ReleaseTarget();

    /// <summary>The ambient occlusion's own pass, between the scene pass and the ordinary one — see
    /// <see cref="PostProcessPipeline.RecordOcclusion(CommandBuffer, in PostProcessSettings, System.Numerics.Matrix4x4, System.Numerics.Vector4?)"/>.
    /// Leaves the viewport and scissor at the occlusion target's; the caller sets its own again.</summary>
    private void RecordPostProcessOcclusion(
        CommandBuffer cmd, in PostProcessSettings settings,
        ReadOnlySpan<System.Numerics.Matrix4x4> viewProjections, ReadOnlySpan<System.Numerics.Vector4> sampleRects) =>
        _postProcess!.RecordOcclusion(cmd, settings, viewProjections, sampleRects);

    private void DrawPostProcess(
        CommandBuffer cmd, in PostProcessSettings settings, System.Numerics.Matrix4x4 viewProjection,
        System.Numerics.Vector4? sampleRect = null) =>
        _postProcess!.Draw(cmd, settings, viewProjection, sampleRect);

    private void DisposePostProcessResources() => _postProcess?.Dispose();
}
