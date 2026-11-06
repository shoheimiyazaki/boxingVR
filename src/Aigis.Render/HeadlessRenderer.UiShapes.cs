using Silk.NET.Vulkan;

namespace Aigis.Render;

/// <summary>
/// The headless half of the SDF UI-shape overlay (cards, panels, gauges, shadows). Almost all of the
/// work lives in the shared <see cref="UiShapePipeline"/> — see that class's doc comment for why this
/// capability is shared rather than duplicated per renderer like the text overlay. What remains here
/// is composition: create it against this renderer's handles and draw at this renderer's fixed
/// target size.
/// <para>
/// This partial is what makes hard rule 6 true for UI surfaces: a card, its border and its shadow are
/// all visible in an agent's screenshot with no window anywhere in the picture.
/// </para>
/// </summary>
public sealed unsafe partial class HeadlessRenderer
{
    private UiShapePipeline? _uiShapes;

    /// <summary>Draws the UI shapes after the 3D scene and lines but before overlay images and text,
    /// so a panel is a background for its own icons and labels rather than something drawn over them.
    /// Empty list = no pipeline creation, no bind, no draw — the same "empty means skip" contract as
    /// <see cref="DrawOverlay"/> and <see cref="DrawLines"/>, which is what keeps every pre-existing
    /// caller and golden byte-identical.</summary>
    private void DrawUiShapes(CommandBuffer cmd, IReadOnlyList<UiShapeDraw> shapes)
    {
        if (shapes.Count == 0)
        {
            return;
        }

        _uiShapes ??= new UiShapePipeline(_vk, _physicalDevice, _device);
        _uiShapes.Draw(cmd, shapes, _boxRenderPass, dynamicViewport: false, _width, _height);
    }

    private void DisposeUiShapeResources() => _uiShapes?.Dispose();
}
