using System.Numerics;
using Silk.NET.Vulkan;

namespace Aigis.Render;

/// <summary>
/// The headless half of the world-space ground decal path (dynamic stain sheets — blood trails).
/// Almost all of the work lives in the shared <see cref="WorldDecalPipeline"/>; what remains here is
/// composition: create it against this renderer's handles, run uploads before the render pass opens,
/// and draw with this frame's camera matrix.
/// <para>
/// This partial is what makes hard rule 6 true for stains: a blood trail is visible in an agent's
/// screenshot with no window anywhere in the picture, which is the only way the playtest filmstrip
/// can show one at all.
/// </para>
/// </summary>
public sealed unsafe partial class HeadlessRenderer
{
    private WorldDecalPipeline? _decals;

    /// <summary>How many distinct decal sheets the last frame left out because it named more than the
    /// decal budget holds (see <c>WorldDecalPipeline.MaxTextures</c>). Zero on every healthy frame.</summary>
    public int DecalSurfacesDroppedLastFrame => _decals?.DroppedSurfacesLastFrame ?? 0;

    /// <summary>How many frames so far have left at least one decal sheet out — read by a host to report
    /// the overflow once instead of every frame.</summary>
    public long DecalFramesOverBudget => _decals?.FramesOverBudget ?? 0;

    /// <summary>Test visibility: device idles the decal pipeline has taken to free sheets.</summary>
    internal long DecalDeviceIdleWaits => _decals?.DeviceIdleWaits ?? 0;

    /// <summary>Test visibility: decal sheets resident now.</summary>
    internal int DecalResidentCount => _decals?.ResidentCount ?? 0;

    /// <summary>Uploads any sheet whose pixels the GPU does not already hold at its current
    /// revision. Called BEFORE <see cref="BeginBoxRenderPass"/>: the upload records image barriers
    /// and a buffer→image copy, neither of which is legal inside a render pass.</summary>
    private void PrepareDecals(IReadOnlyList<DecalQuadDraw> decals)
    {
        if (decals.Count == 0)
        {
            return;
        }

        _decals ??= new WorldDecalPipeline(_vk, _physicalDevice, _device, _queue, _commandPool);
        _decals.PrepareUploads(decals, _boxRenderPass, dynamicViewport: false, _width, _height);
    }

    /// <summary>Draws the stain sheets immediately after the opaque scene and BEFORE particles,
    /// lines and every screen-space overlay: a decal is floor, so anything that floats above the
    /// floor has to blend over it, not under it. Empty list = no pipeline bind, no draw.</summary>
    private void DrawDecals(CommandBuffer cmd, IReadOnlyList<DecalQuadDraw> decals, Matrix4x4 viewProjection)
    {
        if (decals.Count == 0)
        {
            return;
        }

        // Non-null whenever the list is non-empty: PrepareDecals ran on the same list earlier in
        // this same render call.
        _decals!.Draw(cmd, decals, viewProjection);
    }

    private void DisposeDecalResources() => _decals?.Dispose();
}
