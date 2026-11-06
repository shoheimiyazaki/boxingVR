using System.Numerics;
using Silk.NET.Vulkan;

namespace Aigis.Render;

/// <summary>
/// <c>HeadlessRenderer.Decals.cs</c>'s live-window counterpart — the same
/// <see cref="WorldDecalPipeline"/> type, the same sheets, the same UV convention. Having BOTH
/// hosts is the point: a stain a player watches spread while dragging a body must be the same stain
/// an agent verifies in a headless screenshot, or CLAUDE.md's <c>games/</c> contract ("both hosts,
/// zero per-game plumbing") is broken.
/// <para>
/// The one host-specific difference is handled by a parameter rather than a second implementation:
/// dynamic viewport/scissor, already set per frame before this binds. Nothing here reacts to
/// swapchain recreation, deliberately — <c>RecreateSwapchain</c> does not recreate
/// <c>_renderPass</c>, and none of the images/views/samplers/descriptor sets are sized by the render
/// target (the same reasoning as <c>WindowedRenderer.OverlayImages.cs</c>).
/// </para>
/// </summary>
public sealed unsafe partial class WindowedRenderer
{
    private WorldDecalPipeline? _decals;

    /// <summary>How many distinct decal sheets the last frame left out because it named more than the
    /// decal budget holds (see <c>WorldDecalPipeline.MaxTextures</c>). Zero on every healthy frame.</summary>
    public int DecalSurfacesDroppedLastFrame => _decals?.DroppedSurfacesLastFrame ?? 0;

    /// <summary>How many frames so far have left at least one decal sheet out — read by a host to report
    /// the overflow once instead of every frame.</summary>
    public long DecalFramesOverBudget => _decals?.FramesOverBudget ?? 0;

    /// <summary>Uploads any sheet whose pixels the GPU does not already hold at its current
    /// revision. Called before the frame's render pass begins, on its own one-shot command buffer +
    /// fence rather than this renderer's per-frame command buffer.</summary>
    private void PrepareDecals(IReadOnlyList<DecalQuadDraw> decals)
    {
        if (decals.Count == 0)
        {
            return;
        }

        _decals ??= new WorldDecalPipeline(_vk, _physicalDevice, _device, _queue, _commandPool);
        _decals.PrepareUploads(decals, _renderPass, dynamicViewport: true, _swapchainExtent.Width, _swapchainExtent.Height);
    }

    private void DrawDecals(CommandBuffer cmd, IReadOnlyList<DecalQuadDraw> decals, Matrix4x4 viewProjection)
    {
        if (decals.Count == 0)
        {
            return;
        }

        // Non-null whenever the list is non-empty — PrepareDecals ran on the same list earlier in
        // this same frame.
        _decals!.Draw(cmd, decals, viewProjection);
    }

    private void DisposeDecalResources() => _decals?.Dispose();
}
