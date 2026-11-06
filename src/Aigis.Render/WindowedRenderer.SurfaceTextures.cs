using Silk.NET.Vulkan;

namespace Aigis.Render;

/// <summary>
/// The windowed half of the surface textures — composition of the shared
/// <see cref="SurfaceTextureArray"/>, written into EVERY presentation view's lighting set, like the
/// irradiance partial (a split-screen seat must not keep the previous set's textures).
/// </summary>
public sealed unsafe partial class WindowedRenderer
{
    private SurfaceTextureArray? _surfaceTextures;

    private void EnsureSurfaceTextures()
    {
        if (_surfaceTextures is not null)
        {
            return;
        }

        _surfaceTextures = new SurfaceTextureArray(_vk, _physicalDevice, _device, _queue, _commandPool);
        _surfaceTextures.Prepare(null);
    }

    /// <summary>Uploads <paramref name="set"/> if new and rewrites every view's binding. Before the
    /// render pass, and safe mid-session for the single-frame-in-flight reason
    /// <see cref="PrepareIrradiance"/> gives.</summary>
    private void PrepareSurfaceTextures(SurfaceTextureSet? set)
    {
        EnsureSurfaceTextures();
        _surfaceTextures!.Prepare(set);
        if (!_surfaceTextures.NeedsRebind)
        {
            return;
        }

        var info = SurfaceTextureInfo();
        for (var view = 0; view < MaxPresentationViews; view++)
        {
            var lightingSet = _multiViewLightingSets[view];
            if (lightingSet.Handle == 0)
            {
                continue;
            }

            var write = SurfaceTextureWrite(lightingSet, &info);
            _vk.UpdateDescriptorSets(_device, 1, in write, 0, null);
        }

        _surfaceTextures.AcknowledgeRebind();
    }

    private DescriptorImageInfo SurfaceTextureInfo() => new()
    {
        Sampler = _surfaceTextures!.Sampler,
        ImageView = _surfaceTextures.View,
        ImageLayout = ImageLayout.ShaderReadOnlyOptimal,
    };

    private static WriteDescriptorSet SurfaceTextureWrite(DescriptorSet lightingSet, DescriptorImageInfo* info) => new()
    {
        SType = StructureType.WriteDescriptorSet,
        DstSet = lightingSet,
        DstBinding = HeadlessRenderer.SurfaceTextureBinding,
        DescriptorCount = 1,
        DescriptorType = DescriptorType.CombinedImageSampler,
        PImageInfo = info,
    };

    private void DisposeSurfaceTextureResources() => _surfaceTextures?.Dispose();
}
