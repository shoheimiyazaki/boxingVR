using Silk.NET.Vulkan;

namespace Aigis.Render;

/// <summary>
/// TASK-159: the headless half of the sampled-texture overlay (HUD icons/portraits). Almost all of
/// the work lives in the shared <see cref="OverlayImagePipeline"/> — see that class's doc comment
/// for why this one capability is shared rather than duplicated per renderer like every other
/// pipeline. What remains here is composition: create it against this renderer's handles, run
/// uploads before the render pass opens, and draw at this renderer's fixed target size.
/// <para>
/// This partial is what makes hard rule 6 true for textured overlays: an icon is visible in an
/// agent's screenshot with no window anywhere in the picture, and the <c>visual</c> tests assert on
/// those pixels.
/// </para>
/// </summary>
public sealed unsafe partial class HeadlessRenderer
{
    private OverlayImagePipeline? _overlayImages;

    /// <summary>Uploads any not-yet-resident texture and prepares the pipeline. Called BEFORE
    /// <see cref="BeginBoxRenderPass"/>: the upload records image barriers and a buffer→image copy,
    /// neither of which is legal inside a render pass.</summary>
    private void PrepareOverlayImages(IReadOnlyList<OverlayImageDraw> images)
    {
        if (images.Count == 0)
        {
            return;
        }

        _overlayImages ??= new OverlayImagePipeline(_vk, _physicalDevice, _device, _queue, _commandPool);
        _overlayImages.PrepareUploads(images, _boxRenderPass, dynamicViewport: false, _width, _height);
    }

    /// <summary>Draws the textured overlays after the 3D scene and lines, before text so a HUD
    /// background image cannot obscure its labels. Empty list = no pipeline bind, no draw — the same "empty means skip"
    /// contract as <see cref="DrawOverlay"/> and <see cref="DrawLines"/>.</summary>
    private void DrawOverlayImages(CommandBuffer cmd, IReadOnlyList<OverlayImageDraw> images)
    {
        if (images.Count == 0)
        {
            return;
        }

        // Non-null whenever the list is non-empty: PrepareOverlayImages ran on the same list
        // earlier in this same render call.
        _overlayImages!.Draw(cmd, images, _width, _height);
    }

    private void DisposeOverlayImageResources() => _overlayImages?.Dispose();
}
