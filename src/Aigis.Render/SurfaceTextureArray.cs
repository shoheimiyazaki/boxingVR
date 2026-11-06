using Silk.NET.Vulkan;

namespace Aigis.Render;

/// <summary>
/// The GPU half of <see cref="SurfaceTextureSet"/>: one R8 2D-array image with a full mip chain,
/// shared by both renderers for the reason <see cref="IrradianceVolumeTextures"/> gives (barriers,
/// layouts and lifetimes drift silently between two copies with no validation layers).
/// <para>
/// <b>Mips are built on the CPU</b> (2×2 box filter), once per set: the gameplay camera sees a floor
/// texel at well under a pixel, and without a chain triplanar sampling shimmers as the camera or a
/// patient moves — exactly the aliasing the procedural detail classes go out of their way to avoid.
/// </para>
/// <para>
/// <b>A 1-layer neutral fallback always exists</b>, because the descriptor must point at a real image
/// even in a frame with no set. Its texel is <see cref="SurfaceTextureSet.NeutralLevel"/>, so a stray
/// textured instance renders exactly as if it were untextured.
/// </para>
/// </summary>
internal sealed unsafe class SurfaceTextureArray : IDisposable
{
    private const Format TexelFormat = Format.R8Unorm;

    private static readonly SurfaceTextureSet Fallback = SurfaceTextureSet.FromImages(
        [new TextureData(1, 1, [SurfaceTextureSet.NeutralLevel, SurfaceTextureSet.NeutralLevel, SurfaceTextureSet.NeutralLevel, 255])]);

    private readonly Vk _vk;
    private readonly PhysicalDevice _physicalDevice;
    private readonly Device _device;
    private readonly Queue _queue;
    private readonly CommandPool _commandPool;

    private Image _image;
    private DeviceMemory _memory;
    private ImageView _view;
    private Sampler _sampler;
    private SurfaceTextureSet? _resident;

    public SurfaceTextureArray(Vk vk, PhysicalDevice physicalDevice, Device device, Queue queue, CommandPool commandPool)
    {
        _vk = vk;
        _physicalDevice = physicalDevice;
        _device = device;
        _queue = queue;
        _commandPool = commandPool;
    }

    /// <summary>Set when <see cref="Prepare"/> replaced the image; the composer rewrites its
    /// descriptor and calls <see cref="AcknowledgeRebind"/>.</summary>
    public bool NeedsRebind { get; private set; }

    public Sampler Sampler => _sampler;

    public ImageView View => _view;

    public void AcknowledgeRebind() => NeedsRebind = false;

    /// <summary>Makes <paramref name="set"/> (or the neutral fallback) resident. <b>Call outside any
    /// render pass</b> — it submits its own upload. A no-op for the set already resident.</summary>
    public void Prepare(SurfaceTextureSet? set)
    {
        var wanted = set ?? Fallback;
        if (ReferenceEquals(_resident, wanted))
        {
            return;
        }

        EnsureSampler();
        if (_resident is not null)
        {
            Check(_vk.QueueWaitIdle(_queue), "QueueWaitIdle(surfaceTexturesReplace)");
            DestroyImage();
        }

        Upload(wanted);
        _resident = wanted;
        NeedsRebind = true;
    }

    // ---- upload ---------------------------------------------------------------------------------

    private void Upload(SurfaceTextureSet set)
    {
        var size = set.Size;
        var mipLevels = 1 + (int)MathF.Log2(size);
        var layers = set.LayerCount;

        // Every layer's full chain, mip-major within a layer, in one staging buffer.
        var bytes = 0L;
        for (var mip = 0; mip < mipLevels; mip++)
        {
            var edge = Math.Max(1, size >> mip);
            bytes += (long)edge * edge * layers;
        }

        var stagingInfo = new BufferCreateInfo
        {
            SType = StructureType.BufferCreateInfo,
            Size = (ulong)bytes,
            Usage = BufferUsageFlags.TransferSrcBit,
            SharingMode = SharingMode.Exclusive,
        };
        Check(_vk.CreateBuffer(_device, in stagingInfo, null, out var staging), "CreateBuffer(surfaceTexturesStaging)");
        _vk.GetBufferMemoryRequirements(_device, staging, out var stagingReq);
        var stagingMemory = AllocateMemory(stagingReq, MemoryPropertyFlags.HostVisibleBit | MemoryPropertyFlags.HostCoherentBit);
        Check(_vk.BindBufferMemory(_device, staging, stagingMemory, 0), "BindBufferMemory(surfaceTexturesStaging)");

        var regions = new BufferImageCopy[mipLevels * layers];
        void* mapped = null;
        Check(_vk.MapMemory(_device, stagingMemory, 0, (ulong)bytes, 0, ref mapped), "MapMemory(surfaceTexturesStaging)");
        var destination = new Span<byte>(mapped, (int)bytes);
        var offset = 0;
        for (var layer = 0; layer < layers; layer++)
        {
            var level = set.Layer(layer).ToArray();
            var edge = size;
            for (var mip = 0; mip < mipLevels; mip++)
            {
                level.AsSpan().CopyTo(destination[offset..]);
                regions[(layer * mipLevels) + mip] = new BufferImageCopy
                {
                    BufferOffset = (ulong)offset,
                    ImageSubresource = new ImageSubresourceLayers(ImageAspectFlags.ColorBit, (uint)mip, (uint)layer, 1),
                    ImageExtent = new Extent3D((uint)edge, (uint)edge, 1),
                };
                offset += level.Length;
                if (edge > 1)
                {
                    level = HalveBox(level, edge);
                    edge /= 2;
                }
            }
        }

        _vk.UnmapMemory(_device, stagingMemory);

        var imageInfo = new ImageCreateInfo
        {
            SType = StructureType.ImageCreateInfo,
            ImageType = ImageType.Type2D,
            Format = TexelFormat,
            Extent = new Extent3D((uint)size, (uint)size, 1),
            MipLevels = (uint)mipLevels,
            ArrayLayers = (uint)layers,
            Samples = SampleCountFlags.Count1Bit,
            Tiling = ImageTiling.Optimal,
            Usage = ImageUsageFlags.TransferDstBit | ImageUsageFlags.SampledBit,
            SharingMode = SharingMode.Exclusive,
            InitialLayout = ImageLayout.Undefined,
        };
        Check(_vk.CreateImage(_device, in imageInfo, null, out _image), "CreateImage(surfaceTextures)");
        _vk.GetImageMemoryRequirements(_device, _image, out var imageReq);
        _memory = AllocateMemory(imageReq, MemoryPropertyFlags.DeviceLocalBit);
        Check(_vk.BindImageMemory(_device, _image, _memory, 0), "BindImageMemory(surfaceTextures)");

        var range = new ImageSubresourceRange(ImageAspectFlags.ColorBit, 0, (uint)mipLevels, 0, (uint)layers);
        SubmitUpload(staging, regions, range);

        _vk.DestroyBuffer(_device, staging, null);
        _vk.FreeMemory(_device, stagingMemory, null);

        var viewInfo = new ImageViewCreateInfo
        {
            SType = StructureType.ImageViewCreateInfo,
            Image = _image,
            ViewType = ImageViewType.Type2DArray,
            Format = TexelFormat,
            SubresourceRange = range,
        };
        Check(_vk.CreateImageView(_device, in viewInfo, null, out _view), "CreateImageView(surfaceTextures)");
    }

    /// <summary>One mip step down: each output texel is the mean of a 2×2 block.</summary>
    private static byte[] HalveBox(byte[] level, int edge)
    {
        var half = edge / 2;
        var result = new byte[half * half];
        for (var y = 0; y < half; y++)
        {
            for (var x = 0; x < half; x++)
            {
                var sum = level[(2 * y * edge) + (2 * x)] + level[(2 * y * edge) + (2 * x) + 1]
                    + level[(((2 * y) + 1) * edge) + (2 * x)] + level[(((2 * y) + 1) * edge) + (2 * x) + 1];
                result[(y * half) + x] = (byte)((sum + 2) / 4);
            }
        }

        return result;
    }

    private void SubmitUpload(Silk.NET.Vulkan.Buffer staging, BufferImageCopy[] regions, ImageSubresourceRange range)
    {
        var allocInfo = new CommandBufferAllocateInfo
        {
            SType = StructureType.CommandBufferAllocateInfo,
            CommandPool = _commandPool,
            Level = CommandBufferLevel.Primary,
            CommandBufferCount = 1,
        };
        Check(_vk.AllocateCommandBuffers(_device, in allocInfo, out var cmd), "AllocateCommandBuffers(surfaceTexturesUpload)");
        var begin = new CommandBufferBeginInfo { SType = StructureType.CommandBufferBeginInfo, Flags = CommandBufferUsageFlags.OneTimeSubmitBit };
        Check(_vk.BeginCommandBuffer(cmd, in begin), "BeginCommandBuffer(surfaceTexturesUpload)");

        var toTransferDst = new ImageMemoryBarrier
        {
            SType = StructureType.ImageMemoryBarrier,
            OldLayout = ImageLayout.Undefined,
            NewLayout = ImageLayout.TransferDstOptimal,
            SrcQueueFamilyIndex = Vk.QueueFamilyIgnored,
            DstQueueFamilyIndex = Vk.QueueFamilyIgnored,
            Image = _image,
            SubresourceRange = range,
            SrcAccessMask = AccessFlags.None,
            DstAccessMask = AccessFlags.TransferWriteBit,
        };
        _vk.CmdPipelineBarrier(cmd, PipelineStageFlags.TopOfPipeBit, PipelineStageFlags.TransferBit, 0, 0, null, 0, null, 1, in toTransferDst);

        fixed (BufferImageCopy* pRegions = regions)
        {
            _vk.CmdCopyBufferToImage(cmd, staging, _image, ImageLayout.TransferDstOptimal, (uint)regions.Length, pRegions);
        }

        var toShaderRead = toTransferDst with
        {
            OldLayout = ImageLayout.TransferDstOptimal,
            NewLayout = ImageLayout.ShaderReadOnlyOptimal,
            SrcAccessMask = AccessFlags.TransferWriteBit,
            DstAccessMask = AccessFlags.ShaderReadBit,
        };
        _vk.CmdPipelineBarrier(cmd, PipelineStageFlags.TransferBit, PipelineStageFlags.FragmentShaderBit, 0, 0, null, 0, null, 1, in toShaderRead);
        Check(_vk.EndCommandBuffer(cmd), "EndCommandBuffer(surfaceTexturesUpload)");

        var fenceInfo = new FenceCreateInfo { SType = StructureType.FenceCreateInfo };
        Check(_vk.CreateFence(_device, in fenceInfo, null, out var fence), "CreateFence(surfaceTexturesUpload)");
        var submit = new SubmitInfo { SType = StructureType.SubmitInfo, CommandBufferCount = 1, PCommandBuffers = &cmd };
        Check(_vk.QueueSubmit(_queue, 1, in submit, fence), "QueueSubmit(surfaceTexturesUpload)");
        Check(_vk.WaitForFences(_device, 1, in fence, true, ulong.MaxValue), "WaitForFences(surfaceTexturesUpload)");
        _vk.DestroyFence(_device, fence, null);
        _vk.FreeCommandBuffers(_device, _commandPool, 1, in cmd);
    }

    /// <summary>Repeat-wrapped (the maps are seamless and tile across a wall), trilinear.</summary>
    private void EnsureSampler()
    {
        if (_sampler.Handle != 0)
        {
            return;
        }

        var info = new SamplerCreateInfo
        {
            SType = StructureType.SamplerCreateInfo,
            MagFilter = Filter.Linear,
            MinFilter = Filter.Linear,
            MipmapMode = SamplerMipmapMode.Linear,
            AddressModeU = SamplerAddressMode.Repeat,
            AddressModeV = SamplerAddressMode.Repeat,
            AddressModeW = SamplerAddressMode.ClampToEdge,
            MinLod = 0f,
            MaxLod = 16f,
        };
        Check(_vk.CreateSampler(_device, in info, null, out _sampler), "CreateSampler(surfaceTextures)");
    }

    private DeviceMemory AllocateMemory(MemoryRequirements req, MemoryPropertyFlags flags)
    {
        _vk.GetPhysicalDeviceMemoryProperties(_physicalDevice, out var props);
        for (uint i = 0; i < props.MemoryTypeCount; i++)
        {
            if ((req.MemoryTypeBits & (1u << (int)i)) != 0 && (props.MemoryTypes[(int)i].PropertyFlags & flags) == flags)
            {
                var allocInfo = new MemoryAllocateInfo { SType = StructureType.MemoryAllocateInfo, AllocationSize = req.Size, MemoryTypeIndex = i };
                Check(_vk.AllocateMemory(_device, in allocInfo, null, out var memory), "AllocateMemory(surfaceTextures)");
                return memory;
            }
        }

        throw new InvalidOperationException($"No Vulkan memory type satisfies {flags} for the surface textures.");
    }

    private void DestroyImage()
    {
        if (_view.Handle != 0)
        {
            _vk.DestroyImageView(_device, _view, null);
            _view = default;
        }

        if (_image.Handle != 0)
        {
            _vk.DestroyImage(_device, _image, null);
            _image = default;
        }

        if (_memory.Handle != 0)
        {
            _vk.FreeMemory(_device, _memory, null);
            _memory = default;
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
        DestroyImage();
        if (_sampler.Handle != 0)
        {
            _vk.DestroySampler(_device, _sampler, null);
            _sampler = default;
        }

        _resident = null;
    }
}
