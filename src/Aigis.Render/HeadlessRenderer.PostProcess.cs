using System.Numerics;
using Silk.NET.Vulkan;

namespace Aigis.Render;

/// <summary>
/// The headless half of the miniature finishing pass. All of the Vulkan work is the shared
/// <see cref="PostProcessPipeline"/>; this partial only composes it against this renderer's handles,
/// so an agent's screenshot carries the same tilt-shift and grade a player sees (hard rule 6).
/// </summary>
public sealed unsafe partial class HeadlessRenderer
{
    private PostProcessPipeline? _postProcess;

    /// <summary>Opens the pass the 3D scene is recorded in: the post-process scene pass when
    /// <paramref name="settings"/> is set, otherwise the ordinary box pass exactly as before.</summary>
    private void BeginScenePass(CommandBuffer cmd, Vector4 clearColor, PostProcessSettings? settings)
    {
        if (settings is null)
        {
            BeginBoxRenderPass(cmd, clearColor);
            return;
        }

        _postProcess ??= new PostProcessPipeline(_vk, _physicalDevice, _device);
        _postProcess.Prepare(_boxRenderPass, ColorFormat, DepthFormat, _width, _height, dynamicViewport: false);

        var clears = stackalloc ClearValue[2];
        clears[0] = new ClearValue { Color = new ClearColorValue(clearColor.X, clearColor.Y, clearColor.Z, clearColor.W) };
        clears[1] = new ClearValue { DepthStencil = new ClearDepthStencilValue(1.0f, 0) };
        var info = new RenderPassBeginInfo
        {
            SType = StructureType.RenderPassBeginInfo,
            RenderPass = _postProcess.SceneRenderPass,
            Framebuffer = _postProcess.SceneFramebuffer,
            RenderArea = new Rect2D(new Offset2D(0, 0), new Extent2D(_width, _height)),
            ClearValueCount = 2,
            PClearValues = clears,
        };
        _vk.CmdBeginRenderPass(cmd, in info, SubpassContents.Inline);
    }

    /// <summary>Closes the scene pass and opens the ordinary one with the finished picture already in
    /// it, ready for the HUD. Records nothing when <paramref name="settings"/> is null — the scene was
    /// drawn straight into the ordinary pass.</summary>
    private void FinishScenePass(CommandBuffer cmd, Vector4 clearColor, PostProcessSettings? settings, Matrix4x4 viewProjection, Vector4? sampleRect)
    {
        if (settings is null)
        {
            return;
        }

        _vk.CmdEndRenderPass(cmd);
        _postProcess!.RecordOcclusion(cmd, settings.Value, viewProjection, sampleRect);
        BeginBoxRenderPass(cmd, clearColor);
        _postProcess!.Draw(cmd, settings.Value, viewProjection, sampleRect);
    }

    private void DisposePostProcessResources() => _postProcess?.Dispose();
}
