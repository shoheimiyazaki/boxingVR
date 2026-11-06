using Silk.NET.Vulkan;

namespace Aigis.Render;

/// <summary>
/// The headless half of the surface textures (<see cref="SurfaceTextureSet"/>): composition of the
/// shared <see cref="SurfaceTextureArray"/> against this renderer's lighting descriptor set, at
/// <see cref="SurfaceTextureBinding"/>. Same shape as the irradiance partial.
/// </summary>
public sealed unsafe partial class HeadlessRenderer
{
    private SurfaceTextureArray? _surfaceTextures;

    /// <summary>Creates the array and makes its neutral fallback resident, so the lighting set's
    /// initial write has a real view for <see cref="SurfaceTextureBinding"/>.</summary>
    private void EnsureSurfaceTextures()
    {
        if (_surfaceTextures is not null)
        {
            return;
        }

        _surfaceTextures = new SurfaceTextureArray(_vk, _physicalDevice, _device, _queue, _commandPool);
        _surfaceTextures.Prepare(null);
    }

    /// <summary>Uploads <paramref name="set"/> if new and rebinds the descriptor. BEFORE the render
    /// pass opens, like <see cref="PrepareIrradiance"/>.</summary>
    private void PrepareSurfaceTextures(SurfaceTextureSet? set)
    {
        EnsureSurfaceTextures();
        _surfaceTextures!.Prepare(set);
        if (!_surfaceTextures.NeedsRebind)
        {
            return;
        }

        var info = SurfaceTextureInfo();
        var write = SurfaceTextureWrite(&info);
        _vk.UpdateDescriptorSets(_device, 1, in write, 0, null);
        _surfaceTextures.AcknowledgeRebind();
    }

    private DescriptorImageInfo SurfaceTextureInfo() => new()
    {
        Sampler = _surfaceTextures!.Sampler,
        ImageView = _surfaceTextures.View,
        ImageLayout = ImageLayout.ShaderReadOnlyOptimal,
    };

    private WriteDescriptorSet SurfaceTextureWrite(DescriptorImageInfo* info) => new()
    {
        SType = StructureType.WriteDescriptorSet,
        DstSet = _lightingSet,
        DstBinding = SurfaceTextureBinding,
        DescriptorCount = 1,
        DescriptorType = DescriptorType.CombinedImageSampler,
        PImageInfo = info,
    };

    private void DisposeSurfaceTextureResources() => _surfaceTextures?.Dispose();
}
