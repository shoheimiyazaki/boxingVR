using Silk.NET.Vulkan;

namespace Aigis.Render;

/// <summary>
/// <c>HeadlessRenderer.UiShapes.cs</c>'s live-window counterpart. The two differ only in the render
/// pass they build against and in using dynamic viewport/scissor (the window can resize, so the
/// extent is read fresh each frame rather than baked into the pipeline) — everything else is the
/// shared <see cref="UiShapePipeline"/>.
/// </summary>
public sealed unsafe partial class WindowedRenderer
{
    private UiShapePipeline? _uiShapes;

    /// <summary>Draws the UI shapes using the current swapchain extent for the pixel→NDC mapping, so
    /// a resize is picked up for free. Same "empty means skip" contract as
    /// <see cref="DrawOverlay"/>.</summary>
    private void DrawUiShapes(CommandBuffer cmd, IReadOnlyList<UiShapeDraw> shapes)
    {
        if (shapes.Count == 0)
        {
            return;
        }

        _uiShapes ??= new UiShapePipeline(_vk, _physicalDevice, _device);
        _uiShapes.Draw(cmd, shapes, _renderPass, dynamicViewport: true, _swapchainExtent.Width, _swapchainExtent.Height);
    }

    private void DisposeUiShapeResources() => _uiShapes?.Dispose();
}
