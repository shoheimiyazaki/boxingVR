using Silk.NET.Vulkan;

namespace Aigis.Render;

/// <summary>
/// TASK-245: the headless half of the baked indirect-light field. Nearly all of the work is in the
/// shared <see cref="IrradianceVolumeTextures"/> — see that class's doc comment for why this
/// capability, like the sampled-texture overlay, is shared rather than duplicated per renderer.
/// What is here is composition: create it against this renderer's handles, upload before the
/// render pass opens, and rewrite the three descriptors when the resident volume changes.
/// <para>
/// This partial is what keeps hard rule 6 true for the feature: the same volume produces the same
/// ambient term in an agent's screenshot as in a window, because both renderers bind the same three
/// textures against the same three UBO fields and the shader has only one code path.
/// </para>
/// </summary>
public sealed unsafe partial class HeadlessRenderer
{
    /// <summary>First of the irradiance volume's descriptor bindings — 7, 8, 9 follow the six the
    /// lighting set already had (UBO, sun shadow map, four fixture shadow maps, fixture UBO).</summary>
    internal const int IrradianceFirstBinding = 7;

    /// <summary>One binding per colour channel; see <see cref="IrradianceVolumeTextures"/> on why
    /// the split is by colour rather than by coefficient.</summary>
    internal const int IrradianceBindingCount = 3;

    /// <summary>Total bindings in the lighting descriptor set. Named rather than repeated as a
    /// literal in four places, which is how the previous count (7) came to appear as a bare
    /// number in the layout, the pool, the write array and the update call at once.</summary>
    internal const int LightingBindingCount = SurfaceTextureBinding + 1;

    /// <summary>2026-10-06: the surface-texture array (<see cref="SurfaceTextureSet"/>), after the
    /// irradiance volume's three.</summary>
    internal const int SurfaceTextureBinding = IrradianceFirstBinding + IrradianceBindingCount;

    /// <summary>Combined image samplers in the lighting set: sun + four fixture shadow maps, the
    /// irradiance channels, the surface textures.</summary>
    internal const int LightingSamplerCount = 5 + IrradianceBindingCount + 1;

    private IrradianceVolumeTextures? _irradiance;

    /// <summary>Creates the texture set and makes its 1x1x1 fallback resident, so
    /// <see cref="EnsureLightingDescriptor"/> has real image views to point bindings 7-9 at before
    /// any caller has supplied a volume.</summary>
    private void EnsureIrradianceTextures()
    {
        if (_irradiance is not null)
        {
            return;
        }

        _irradiance = new IrradianceVolumeTextures(_vk, _physicalDevice, _device, _queue, _commandPool);
        _irradiance.Prepare(null);
    }

    /// <summary>
    /// Uploads <paramref name="volume"/> if it is not the resident one, and rebinds the three
    /// descriptors if that replaced the images. Called BEFORE the render pass opens: the upload
    /// records image barriers and a buffer→image copy, neither legal inside one — the same
    /// constraint <see cref="PrepareOverlayImages"/> works under.
    /// </summary>
    private void PrepareIrradiance(IrradianceVolume? volume)
    {
        EnsureIrradianceTextures();
        _irradiance!.Prepare(volume);
        if (!_irradiance.NeedsRebind)
        {
            return;
        }

        var writes = stackalloc WriteDescriptorSet[IrradianceBindingCount];
        var infos = stackalloc DescriptorImageInfo[IrradianceBindingCount];
        for (var i = 0; i < IrradianceBindingCount; i++)
        {
            infos[i] = new DescriptorImageInfo
            {
                Sampler = _irradiance.Sampler,
                ImageView = _irradiance.View(i),
                ImageLayout = ImageLayout.ShaderReadOnlyOptimal,
            };
            writes[i] = new WriteDescriptorSet
            {
                SType = StructureType.WriteDescriptorSet,
                DstSet = _lightingSet,
                DstBinding = (uint)(IrradianceFirstBinding + i),
                DescriptorCount = 1,
                DescriptorType = DescriptorType.CombinedImageSampler,
                PImageInfo = infos + i,
            };
        }

        _vk.UpdateDescriptorSets(_device, IrradianceBindingCount, writes, 0, null);
        _irradiance.AcknowledgeRebind();
    }

    /// <summary>Fills bindings 7-9 of an initial whole-set write. Shares the packing with
    /// <see cref="PrepareIrradiance"/>'s incremental rewrite by being the same three fields, and
    /// is separate only because the initial write happens while the rest of the set is being
    /// written for the first time.</summary>
    private void WriteIrradianceDescriptors(WriteDescriptorSet* writes, DescriptorImageInfo* infos)
    {
        for (var i = 0; i < IrradianceBindingCount; i++)
        {
            infos[i] = new DescriptorImageInfo
            {
                Sampler = _irradiance!.Sampler,
                ImageView = _irradiance.View(i),
                ImageLayout = ImageLayout.ShaderReadOnlyOptimal,
            };
            writes[IrradianceFirstBinding + i] = new WriteDescriptorSet
            {
                SType = StructureType.WriteDescriptorSet,
                DstSet = _lightingSet,
                DstBinding = (uint)(IrradianceFirstBinding + i),
                DescriptorCount = 1,
                DescriptorType = DescriptorType.CombinedImageSampler,
                PImageInfo = infos + i,
            };
        }

        _irradiance!.AcknowledgeRebind();
    }

    private void DisposeIrradianceResources() => _irradiance?.Dispose();
}
