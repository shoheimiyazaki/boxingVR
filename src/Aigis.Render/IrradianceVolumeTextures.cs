using System.Numerics;
using Silk.NET.Vulkan;

namespace Aigis.Render;

/// <summary>
/// TASK-245: the GPU side of an <see cref="IrradianceVolume"/> — three <c>R16G16B16A16_SFLOAT</c>
/// 3D images (one per colour channel), a linear sampler, and the upload that fills them. Owned in
/// ONE place and composed by BOTH renderers, for the same reason
/// <see cref="OverlayImagePipeline"/> is: this is staging-buffer + image-layout-transition code,
/// these renderers run without Vulkan validation layers, and two hand-maintained copies of a
/// barrier's access masks drift silently into a black frame with no error message.
///
/// <para>
/// <b>Why three RGBA textures instead of one bigger thing.</b> A probe holds twelve numbers (four
/// coefficients x three colours). Splitting by COLOUR rather than by coefficient is what lets the
/// shader answer with three <c>dot(vec4(n,1), texel)</c> — one fetch and one dot per channel, with
/// hardware trilinear interpolation doing the between-probes work for free. Splitting by
/// coefficient instead would need four fetches and a manual recombine.
/// </para>
///
/// <para>
/// <b>Half floats, and the format is not arbitrary.</b> The L1 coefficients are signed, which rules
/// out the unsigned-normalized formats; and <c>R16G16B16A16_SFLOAT</c> is one of the few float
/// formats Vulkan REQUIRES to support linear filtering on optimally-tiled sampled images, which
/// this depends on completely — a nearest-filtered volume shows its probe grid as visible cubes.
/// </para>
///
/// <para>
/// <b>A 1x1x1 fallback always exists.</b> Vulkan requires every declared descriptor to point at a
/// real resource even when a uniform flag means the shader will never sample it, so a renderer
/// that has never been handed a volume still binds these. The fallback carries
/// <see cref="IrradianceVolume.Uniform"/> of the engine's default ambient, which costs 24 bytes
/// and means a mistakenly-enabled read produces plausible light rather than garbage.
/// </para>
/// </summary>
internal sealed unsafe class IrradianceVolumeTextures : IDisposable
{
    /// <summary>Channels, in the order the shader binds them: red, green, blue.</summary>
    private const int ChannelCount = 3;

    /// <summary>See this class's doc comment on why this exact format.</summary>
    private const Format VolumeFormat = Format.R16G16B16A16Sfloat;

    private readonly Vk _vk;
    private readonly PhysicalDevice _physicalDevice;
    private readonly Device _device;
    private readonly Queue _queue;
    private readonly CommandPool _commandPool;

    private readonly Image[] _images = new Image[ChannelCount];
    private readonly DeviceMemory[] _memories = new DeviceMemory[ChannelCount];
    private readonly ImageView[] _views = new ImageView[ChannelCount];

    private Sampler _sampler;
    private IrradianceVolume? _resident;

    public IrradianceVolumeTextures(Vk vk, PhysicalDevice physicalDevice, Device device, Queue queue, CommandPool commandPool)
    {
        _vk = vk;
        _physicalDevice = physicalDevice;
        _device = device;
        _queue = queue;
        _commandPool = commandPool;
    }

    /// <summary>The volume currently resident on the GPU. Reference-compared against the frame's
    /// volume by <see cref="Prepare"/> — see <see cref="SceneLighting.Irradiance"/> on why callers
    /// must pass the same instance every frame.</summary>
    public IrradianceVolume? Resident => _resident;

    /// <summary>Set when <see cref="Prepare"/> last replaced the images, so a composer knows it has
    /// to rewrite its descriptor set. Cleared by <see cref="AcknowledgeRebind"/>.</summary>
    public bool NeedsRebind { get; private set; }

    public Sampler Sampler => _sampler;

    public ImageView View(int channel) => _views[channel];

    public void AcknowledgeRebind() => NeedsRebind = false;

    /// <summary>
    /// Makes <paramref name="volume"/> (or the 1x1x1 fallback, for <see langword="null"/>)
    /// resident, uploading it if it is not the one already there.
    /// <para>
    /// <b>Must be called before the caller opens its render pass</b> — it records image barriers
    /// and a buffer→image copy on its own one-shot command buffer, neither of which is legal
    /// inside one. It is a no-op whenever the same instance comes back, which is the normal case:
    /// a volume is baked once per shot and handed to every frame of it.
    /// </para>
    /// </summary>
    public void Prepare(IrradianceVolume? volume)
    {
        var wanted = volume ?? Fallback;
        if (ReferenceEquals(_resident, wanted))
        {
            return;
        }

        EnsureSampler();

        // The images are sized to the volume, so a different volume means new images rather than a
        // re-fill. Waiting the queue idle first is the blunt instrument, and it is the right one:
        // this happens once per bake, never per frame, and the alternative (deferred deletion
        // queues) is machinery for a problem that does not exist on this path.
        if (_resident is not null)
        {
            Check(_vk.QueueWaitIdle(_queue), "QueueWaitIdle(irradianceReplace)");
            DestroyImages();
        }

        var extent = new Extent3D((uint)wanted.CountX, (uint)wanted.CountY, (uint)wanted.CountZ);
        for (var channel = 0; channel < ChannelCount; channel++)
        {
            var texels = channel switch
            {
                0 => wanted.Red,
                1 => wanted.Green,
                _ => wanted.Blue,
            };
            CreateAndUpload(channel, extent, texels);
        }

        _resident = wanted;
        NeedsRebind = true;
    }

    /// <summary>
    /// The three <c>vec4</c>s the shader needs to turn a world position into a texture coordinate,
    /// plus the flags that gate the whole thing. Kept here rather than in either renderer's
    /// uniform-writing code so the packing is stated once, next to the images it addresses.
    /// </summary>
    /// <param name="volume">The frame's volume, or <see langword="null"/> for "disabled".</param>
    /// <returns>
    /// <c>OriginEnabled</c>: xyz = the volume's origin in world space, w = 1 when the shader should
    /// sample at all.
    /// <c>Scale</c>: xyz = world metres to texture coordinates per axis, w = the normal bias in
    /// metres.
    /// <c>Bias</c>: xyz = the half-texel offset that puts probe 0 at the centre of texel 0 rather
    /// than on its corner, w = the fade width in metres.
    /// </returns>
    public static (Vector4 OriginEnabled, Vector4 Scale, Vector4 Bias) Uniforms(IrradianceVolume? volume)
    {
        if (volume is null)
        {
            return (Vector4.Zero, Vector4.Zero, Vector4.Zero);
        }

        // A texture coordinate runs 0..1 across COUNT texels, and probe k must land on texel k's
        // CENTRE. So one metre is 1/(cell*count) of the coordinate, and the whole thing shifts by
        // half a texel. Getting this wrong by that half texel shifts the entire lighting field by
        // half a probe spacing, which reads as light leaking through walls on one side of a room
        // and pooling short of them on the other.
        var scale = new Vector3(
            1f / (volume.CellSize.X * volume.CountX),
            1f / (volume.CellSize.Y * volume.CountY),
            1f / (volume.CellSize.Z * volume.CountZ));
        var bias = new Vector3(
            0.5f / volume.CountX,
            0.5f / volume.CountY,
            0.5f / volume.CountZ);

        return (
            new Vector4(volume.Origin, 1f),
            new Vector4(scale, volume.NormalBiasMetres),
            new Vector4(bias, volume.FadeMetres));
    }

    /// <summary>The 1x1x1 stand-in described in this class's doc comment. Static and shared: it
    /// holds no GPU state, only the numbers an upload copies.</summary>
    private static IrradianceVolume Fallback { get; } = IrradianceVolume.Uniform(AmbientLight.Default);

    private void EnsureSampler()
    {
        if (_sampler.Handle != 0)
        {
            return;
        }

        // ClampToEdge on every axis, not ClampToBorder: a sample just outside the volume should
        // read the nearest probe, because the shader is already cross-fading back to the flat
        // ambient over that same band and a border colour would fight it.
        var samplerInfo = new SamplerCreateInfo
        {
            SType = StructureType.SamplerCreateInfo,
            MagFilter = Filter.Linear,
            MinFilter = Filter.Linear,
            MipmapMode = SamplerMipmapMode.Nearest,
            AddressModeU = SamplerAddressMode.ClampToEdge,
            AddressModeV = SamplerAddressMode.ClampToEdge,
            AddressModeW = SamplerAddressMode.ClampToEdge,
            CompareEnable = false,
            MinLod = 0f,
            MaxLod = 0f,
        };
        Check(_vk.CreateSampler(_device, in samplerInfo, null, out _sampler), "CreateSampler(irradiance)");
    }

    private void CreateAndUpload(int channel, Extent3D extent, ReadOnlySpan<Half> texels)
    {
        var size = (ulong)(texels.Length * sizeof(ushort));
        var stagingInfo = new BufferCreateInfo
        {
            SType = StructureType.BufferCreateInfo,
            Size = size,
            Usage = BufferUsageFlags.TransferSrcBit,
            SharingMode = SharingMode.Exclusive,
        };
        Check(_vk.CreateBuffer(_device, in stagingInfo, null, out var staging), "CreateBuffer(irradianceStaging)");
        _vk.GetBufferMemoryRequirements(_device, staging, out var stagingReq);
        var stagingMemory = AllocateMemory(stagingReq, MemoryPropertyFlags.HostVisibleBit | MemoryPropertyFlags.HostCoherentBit);
        Check(_vk.BindBufferMemory(_device, staging, stagingMemory, 0), "BindBufferMemory(irradianceStaging)");

        void* mapped = null;
        Check(_vk.MapMemory(_device, stagingMemory, 0, size, 0, ref mapped), "MapMemory(irradianceStaging)");
        texels.CopyTo(new Span<Half>(mapped, texels.Length));
        _vk.UnmapMemory(_device, stagingMemory);

        var imageInfo = new ImageCreateInfo
        {
            SType = StructureType.ImageCreateInfo,
            ImageType = ImageType.Type3D,
            Format = VolumeFormat,
            Extent = extent,
            MipLevels = 1,
            ArrayLayers = 1,
            Samples = SampleCountFlags.Count1Bit,
            Tiling = ImageTiling.Optimal,
            Usage = ImageUsageFlags.TransferDstBit | ImageUsageFlags.SampledBit,
            SharingMode = SharingMode.Exclusive,
            InitialLayout = ImageLayout.Undefined,
        };
        Check(_vk.CreateImage(_device, in imageInfo, null, out _images[channel]), $"CreateImage(irradiance{channel})");
        _vk.GetImageMemoryRequirements(_device, _images[channel], out var imageReq);
        _memories[channel] = AllocateMemory(imageReq, MemoryPropertyFlags.DeviceLocalBit);
        Check(_vk.BindImageMemory(_device, _images[channel], _memories[channel], 0), $"BindImageMemory(irradiance{channel})");

        RecordAndSubmitUpload(staging, _images[channel], extent);

        _vk.DestroyBuffer(_device, staging, null);
        _vk.FreeMemory(_device, stagingMemory, null);

        var viewInfo = new ImageViewCreateInfo
        {
            SType = StructureType.ImageViewCreateInfo,
            Image = _images[channel],
            ViewType = ImageViewType.Type3D,
            Format = VolumeFormat,
            SubresourceRange = new ImageSubresourceRange(ImageAspectFlags.ColorBit, 0, 1, 0, 1),
        };
        Check(_vk.CreateImageView(_device, in viewInfo, null, out _views[channel]), $"CreateImageView(irradiance{channel})");
    }

    /// <summary>Undefined → transfer-dst, copy, transfer-dst → shader-read-only, submit, wait. The
    /// same one-shot recipe <see cref="OverlayImagePipeline"/> uses, with a 3D extent.</summary>
    private void RecordAndSubmitUpload(Silk.NET.Vulkan.Buffer staging, Image image, Extent3D extent)
    {
        var allocInfo = new CommandBufferAllocateInfo
        {
            SType = StructureType.CommandBufferAllocateInfo,
            CommandPool = _commandPool,
            Level = CommandBufferLevel.Primary,
            CommandBufferCount = 1,
        };
        Check(_vk.AllocateCommandBuffers(_device, in allocInfo, out var cmd), "AllocateCommandBuffers(irradianceUpload)");

        var begin = new CommandBufferBeginInfo
        {
            SType = StructureType.CommandBufferBeginInfo,
            Flags = CommandBufferUsageFlags.OneTimeSubmitBit,
        };
        Check(_vk.BeginCommandBuffer(cmd, in begin), "BeginCommandBuffer(irradianceUpload)");

        var range = new ImageSubresourceRange(ImageAspectFlags.ColorBit, 0, 1, 0, 1);
        var toTransferDst = new ImageMemoryBarrier
        {
            SType = StructureType.ImageMemoryBarrier,
            OldLayout = ImageLayout.Undefined,
            NewLayout = ImageLayout.TransferDstOptimal,
            SrcQueueFamilyIndex = Vk.QueueFamilyIgnored,
            DstQueueFamilyIndex = Vk.QueueFamilyIgnored,
            Image = image,
            SubresourceRange = range,
            SrcAccessMask = AccessFlags.None,
            DstAccessMask = AccessFlags.TransferWriteBit,
        };
        _vk.CmdPipelineBarrier(cmd, PipelineStageFlags.TopOfPipeBit, PipelineStageFlags.TransferBit, 0, 0, null, 0, null, 1, in toTransferDst);

        var region = new BufferImageCopy
        {
            BufferOffset = 0,
            BufferRowLength = 0,      // tightly packed, which IrradianceVolume guarantees
            BufferImageHeight = 0,
            ImageSubresource = new ImageSubresourceLayers(ImageAspectFlags.ColorBit, 0, 0, 1),
            ImageOffset = new Offset3D(0, 0, 0),
            ImageExtent = extent,
        };
        _vk.CmdCopyBufferToImage(cmd, staging, image, ImageLayout.TransferDstOptimal, 1, in region);

        var toShaderRead = new ImageMemoryBarrier
        {
            SType = StructureType.ImageMemoryBarrier,
            OldLayout = ImageLayout.TransferDstOptimal,
            NewLayout = ImageLayout.ShaderReadOnlyOptimal,
            SrcQueueFamilyIndex = Vk.QueueFamilyIgnored,
            DstQueueFamilyIndex = Vk.QueueFamilyIgnored,
            Image = image,
            SubresourceRange = range,
            SrcAccessMask = AccessFlags.TransferWriteBit,
            DstAccessMask = AccessFlags.ShaderReadBit,
        };
        _vk.CmdPipelineBarrier(cmd, PipelineStageFlags.TransferBit, PipelineStageFlags.FragmentShaderBit, 0, 0, null, 0, null, 1, in toShaderRead);

        Check(_vk.EndCommandBuffer(cmd), "EndCommandBuffer(irradianceUpload)");

        var fenceInfo = new FenceCreateInfo { SType = StructureType.FenceCreateInfo };
        Check(_vk.CreateFence(_device, in fenceInfo, null, out var fence), "CreateFence(irradianceUpload)");
        var submit = new SubmitInfo
        {
            SType = StructureType.SubmitInfo,
            CommandBufferCount = 1,
            PCommandBuffers = &cmd,
        };
        Check(_vk.QueueSubmit(_queue, 1, in submit, fence), "QueueSubmit(irradianceUpload)");
        Check(_vk.WaitForFences(_device, 1, in fence, true, ulong.MaxValue), "WaitForFences(irradianceUpload)");

        _vk.DestroyFence(_device, fence, null);
        _vk.FreeCommandBuffers(_device, _commandPool, 1, in cmd);
    }

    private DeviceMemory AllocateMemory(MemoryRequirements req, MemoryPropertyFlags flags)
    {
        _vk.GetPhysicalDeviceMemoryProperties(_physicalDevice, out var props);
        for (uint i = 0; i < props.MemoryTypeCount; i++)
        {
            if ((req.MemoryTypeBits & (1u << (int)i)) != 0 && (props.MemoryTypes[(int)i].PropertyFlags & flags) == flags)
            {
                var allocInfo = new MemoryAllocateInfo
                {
                    SType = StructureType.MemoryAllocateInfo,
                    AllocationSize = req.Size,
                    MemoryTypeIndex = i,
                };
                Check(_vk.AllocateMemory(_device, in allocInfo, null, out var memory), "AllocateMemory(irradiance)");
                return memory;
            }
        }

        throw new InvalidOperationException($"No Vulkan memory type satisfies {flags} for the irradiance-volume allocation.");
    }

    private void DestroyImages()
    {
        for (var channel = 0; channel < ChannelCount; channel++)
        {
            if (_views[channel].Handle != 0)
            {
                _vk.DestroyImageView(_device, _views[channel], null);
                _views[channel] = default;
            }

            if (_images[channel].Handle != 0)
            {
                _vk.DestroyImage(_device, _images[channel], null);
                _images[channel] = default;
            }

            if (_memories[channel].Handle != 0)
            {
                _vk.FreeMemory(_device, _memories[channel], null);
                _memories[channel] = default;
            }
        }
    }

    private static void Check(Result result, string what)
    {
        if (result != Result.Success)
        {
            throw new InvalidOperationException($"Vulkan {what} failed: {result}");
        }
    }

    public void Dispose()
    {
        DestroyImages();
        if (_sampler.Handle != 0)
        {
            _vk.DestroySampler(_device, _sampler, null);
            _sampler = default;
        }

        _resident = null;
    }
}
