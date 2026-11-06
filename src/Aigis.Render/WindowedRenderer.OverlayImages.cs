using Silk.NET.Vulkan;

namespace Aigis.Render;

/// <summary>
/// <c>HeadlessRenderer.OverlayImages.cs</c>'s live-window counterpart (TASK-159) — the same
/// <see cref="OverlayImagePipeline"/> instance type, the same textures, the same UV/rect convention.
/// Having BOTH hosts is the point of the task: a game's icons must not be headless-only, or
/// CLAUDE.md's <c>games/</c> contract ("both hosts, zero per-game plumbing") is broken.
/// <para>
/// Two host-specific differences, both handled by parameters rather than a second implementation:
/// the pipeline uses dynamic viewport/scissor (the window resizes, and those are already set
/// per-frame before this binds), and the pixel→NDC mapping reads the current swapchain extent every
/// frame so a resize is picked up for free.
/// </para>
/// <para>
/// Nothing here reacts to swapchain recreation, deliberately: <c>RecreateSwapchain</c> does not
/// recreate <c>_renderPass</c> (format-stable across resizes, per its own doc comment), so the
/// pipeline handle stays valid, and the images/views/samplers/descriptor sets are not sized by the
/// render target at all. A resize hook here would be dead code.
/// </para>
/// </summary>
public sealed unsafe partial class WindowedRenderer
{
    private OverlayImagePipeline? _overlayImages;

    /// <summary>Uploads any not-yet-resident texture. Called before the frame's render pass begins
    /// — the upload's barriers and buffer→image copy are illegal inside one, and it runs on its own
    /// one-shot command buffer + fence rather than this renderer's per-frame command buffer.</summary>
    private void PrepareOverlayImages(IReadOnlyList<OverlayImageDraw> images)
    {
        if (images.Count == 0)
        {
            return;
        }

        _overlayImages ??= new OverlayImagePipeline(_vk, _physicalDevice, _device, _queue, _commandPool);
        _overlayImages.PrepareUploads(images, _renderPass, dynamicViewport: true, _swapchainExtent.Width, _swapchainExtent.Height);
    }

    private void DrawOverlayImages(CommandBuffer cmd, IReadOnlyList<OverlayImageDraw> images)
    {
        if (images.Count == 0)
        {
            return;
        }

        // Non-null whenever the list is non-empty — PrepareOverlayImages ran on the same list
        // earlier in this same frame.
        _overlayImages!.Draw(cmd, images, _swapchainExtent.Width, _swapchainExtent.Height);
    }

    private void DisposeOverlayImageResources() => _overlayImages?.Dispose();
}
