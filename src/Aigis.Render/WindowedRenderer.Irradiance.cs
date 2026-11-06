using Silk.NET.Vulkan;

namespace Aigis.Render;

/// <summary>
/// TASK-245: the windowed half of the baked indirect-light field — the twin of
/// <c>HeadlessRenderer.Irradiance.cs</c>, sharing the same <see cref="IrradianceVolumeTextures"/>
/// implementation and the same binding constants.
/// <para>
/// <b>It exists so the window is not a different renderer.</b> The engine's convention (see
/// <c>HeadlessRenderer.Primitives.cs</c>'s <c>_primitiveDepthFrag</c>) is that a pass present on one
/// renderer and absent from the other makes an agent's screenshot stop being evidence about what a
/// human sees. Since the shader now declares bindings 7-9 unconditionally, omitting them here would
/// not merely differ — it would leave this renderer's descriptor set layout incompatible with the
/// pipeline it creates.
/// </para>
/// <para>
/// Triage passes no volume today, so every window frame binds the 1x1x1 fallback with the enable
/// flag at 0 and draws exactly what it drew before this task. The plumbing is here for the day a
/// level ships a baked volume, and to keep the two renderers honest in the meantime.
/// </para>
/// </summary>
public sealed unsafe partial class WindowedRenderer
{
    private IrradianceVolumeTextures? _irradiance;

    /// <summary>Creates the texture set and makes its fallback resident, so
    /// <see cref="EnsureLightingDescriptor"/> has real views to point bindings 7-9 at. Called from
    /// there, before the layout is built.</summary>
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
    /// Uploads <paramref name="volume"/> if it is not already resident and rewrites the affected
    /// descriptors. Called before the render pass opens (image barriers and a buffer→image copy are
    /// illegal inside one), and a no-op on every frame that reuses the same volume instance.
    /// <para>
    /// Rewriting descriptors mid-session is safe here for the same reason
    /// <see cref="OverlayImagePipeline"/>'s per-texture sets are: this renderer waits on its single
    /// in-flight fence before re-recording, so no submitted command buffer is still reading the set
    /// when this runs. <b>If this renderer ever gains real multi-buffering, this becomes wrong</b>
    /// and the volume swap has to be deferred behind the frame's fences.
    /// </para>
    /// </summary>
    private void PrepareIrradiance(IrradianceVolume? volume)
    {
        EnsureIrradianceTextures();
        _irradiance!.Prepare(volume);
        if (!_irradiance.NeedsRebind)
        {
            return;
        }

        // Every presentation view's set points at the same three textures, so all of them are
        // rewritten -- a split-screen frame must not have one seat lit by the previous shot's
        // volume. The two scratch arrays are allocated ONCE, outside the loop (CA2014): a
        // stackalloc inside it would keep growing the frame with every view.
        var writes = stackalloc WriteDescriptorSet[HeadlessRenderer.IrradianceBindingCount];
        var infos = stackalloc DescriptorImageInfo[HeadlessRenderer.IrradianceBindingCount];
        for (var view = 0; view < MaxPresentationViews; view++)
        {
            var set = _multiViewLightingSets[view];
            if (set.Handle == 0)
            {
                continue;
            }

            for (var i = 0; i < HeadlessRenderer.IrradianceBindingCount; i++)
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
                    DstSet = set,
                    DstBinding = (uint)(HeadlessRenderer.IrradianceFirstBinding + i),
                    DescriptorCount = 1,
                    DescriptorType = DescriptorType.CombinedImageSampler,
                    PImageInfo = infos + i,
                };
            }

            _vk.UpdateDescriptorSets(_device, HeadlessRenderer.IrradianceBindingCount, writes, 0, null);
        }

        _irradiance.AcknowledgeRebind();
    }

    private void DisposeIrradianceResources() => _irradiance?.Dispose();
}
