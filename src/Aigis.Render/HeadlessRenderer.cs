using System.Numerics;
using Silk.NET.Vulkan;

namespace Aigis.Render;

/// <summary>
/// A windowless Vulkan renderer that draws into an offscreen color image and reads it
/// back to CPU RGBA. This is the substrate for AI-agent screenshots and golden-image
/// verification: no surface, no swapchain, no display server — just
/// <c>Render(...) -> byte[] RGBA -> PNG</c>. Milestone A: clear to a color.
/// </summary>
public sealed unsafe partial class HeadlessRenderer : IDisposable
{
    private const Format ColorFormat = Format.R8G8B8A8Unorm;

    private readonly Vk _vk;
    private readonly uint _width;
    private readonly uint _height;

    private Instance _instance;
    private PhysicalDevice _physicalDevice;
    private Device _device;
    private uint _graphicsFamily;
    private Queue _queue;
    private CommandPool _commandPool;

    private Image _colorImage;
    private DeviceMemory _colorMemory;
    private ImageView _colorView;
    private RenderPass _renderPass;
    private Framebuffer _framebuffer;

    // false when the instance/device were handed in (ExternalVulkanDevice): Dispose must not destroy them.
    private readonly bool _ownsDevice = true;

    private Silk.NET.Vulkan.Buffer _readbackBuffer;
    private DeviceMemory _readbackMemory;
    private readonly ulong _readbackSize;

    /// <param name="width">Render-target width in pixels.</param>
    /// <param name="height">Render-target height in pixels.</param>
    /// <param name="shadowMapSize">Edge length, in texels, of every one of this renderer's shadow
    /// maps. Defaults to <see cref="ShadowMapConfig.MapSize"/>, which is what every existing caller
    /// gets and therefore renders exactly as before.
    /// <para>
    /// A parameter because the maps do NOT scale with the render target and are the single largest
    /// allocation a renderer makes: one sun map plus
    /// <see cref="SceneLighting.MaxDownwardFixtureShadows"/> fixture maps, at 2048&#178; D32, is
    /// <b>84 MB of device-local memory per renderer</b>. A renderer whose output is 72 pixels across
    /// — the spawn-portrait renderer a game host keeps alive for the whole session — cannot resolve
    /// any of that detail, and paid the full 84 MB for it.
    /// </para></param>
    public HeadlessRenderer(uint width, uint height, uint shadowMapSize = ShadowMapConfig.MapSize)
    {
        ArgumentOutOfRangeException.ThrowIfZero(shadowMapSize);
        _width = width;
        _height = height;
        _shadowMapSize = shadowMapSize;
        _readbackSize = (ulong)width * height * 4;
        _vk = Vk.GetApi();
        try
        {
            CreateInstance();
            PickPhysicalDeviceAndQueue();
            CreateDevice();
            CreateColorTarget();
            CreateRenderPass();
            CreateFramebuffer();
            CreateReadbackBuffer();
            CreateCommandPool();
        }
        catch
        {
            Dispose();
            throw;
        }
    }

    public uint Width => _width;

    public uint Height => _height;

    /// <summary>Clears the frame to <paramref name="color"/> (RGBA, 0..1) and returns the RGBA bytes.</summary>
    public byte[] RenderClear(Vector4 color)
    {
        var cmd = BeginCommands();
        BeginRenderPass(cmd, color);
        _vk.CmdEndRenderPass(cmd); // finalLayout => TransferSrcOptimal
        CopyColorToReadback(cmd);
        EndAndSubmit(cmd);
        return ReadPixels();
    }

    // --- setup ------------------------------------------------------------

    private void CreateInstance()
    {
        var appInfo = new ApplicationInfo
        {
            SType = StructureType.ApplicationInfo,
            ApiVersion = Vk.Version11,
        };
        var createInfo = new InstanceCreateInfo
        {
            SType = StructureType.InstanceCreateInfo,
            PApplicationInfo = &appInfo,
        };

        Check(_vk.CreateInstance(in createInfo, null, out _instance), "CreateInstance");
    }

    private void PickPhysicalDeviceAndQueue()
    {
        uint deviceCount = 0;
        _vk.EnumeratePhysicalDevices(_instance, ref deviceCount, null);
        if (deviceCount == 0)
        {
            throw new InvalidOperationException("No Vulkan physical devices found.");
        }

        var devices = new PhysicalDevice[deviceCount];
        fixed (PhysicalDevice* p = devices)
        {
            _vk.EnumeratePhysicalDevices(_instance, ref deviceCount, p);
        }

        foreach (var device in devices)
        {
            uint familyCount = 0;
            _vk.GetPhysicalDeviceQueueFamilyProperties(device, ref familyCount, null);
            var families = new QueueFamilyProperties[familyCount];
            fixed (QueueFamilyProperties* f = families)
            {
                _vk.GetPhysicalDeviceQueueFamilyProperties(device, ref familyCount, f);
            }

            for (uint i = 0; i < familyCount; i++)
            {
                if ((families[i].QueueFlags & QueueFlags.GraphicsBit) != 0)
                {
                    _physicalDevice = device;
                    _graphicsFamily = i;
                    return;
                }
            }
        }

        throw new InvalidOperationException("No Vulkan device with a graphics queue.");
    }

    private void CreateDevice()
    {
        var priority = 1f;
        var queueInfo = new DeviceQueueCreateInfo
        {
            SType = StructureType.DeviceQueueCreateInfo,
            QueueFamilyIndex = _graphicsFamily,
            QueueCount = 1,
            PQueuePriorities = &priority,
        };
        var deviceInfo = new DeviceCreateInfo
        {
            SType = StructureType.DeviceCreateInfo,
            QueueCreateInfoCount = 1,
            PQueueCreateInfos = &queueInfo,
        };

        Check(_vk.CreateDevice(_physicalDevice, in deviceInfo, null, out _device), "CreateDevice");
        _vk.GetDeviceQueue(_device, _graphicsFamily, 0, out _queue);
    }

    private void CreateColorTarget()
    {
        var imageInfo = new ImageCreateInfo
        {
            SType = StructureType.ImageCreateInfo,
            ImageType = ImageType.Type2D,
            Format = ColorFormat,
            Extent = new Extent3D(_width, _height, 1),
            MipLevels = 1,
            ArrayLayers = 1,
            Samples = SampleCountFlags.Count1Bit,
            Tiling = ImageTiling.Optimal,
            Usage = ImageUsageFlags.ColorAttachmentBit | ImageUsageFlags.TransferSrcBit,
            SharingMode = SharingMode.Exclusive,
            InitialLayout = ImageLayout.Undefined,
        };
        Check(_vk.CreateImage(_device, in imageInfo, null, out _colorImage), "CreateImage");

        _vk.GetImageMemoryRequirements(_device, _colorImage, out var req);
        _colorMemory = AllocateMemory(req, MemoryPropertyFlags.DeviceLocalBit);
        Check(_vk.BindImageMemory(_device, _colorImage, _colorMemory, 0), "BindImageMemory");

        var viewInfo = new ImageViewCreateInfo
        {
            SType = StructureType.ImageViewCreateInfo,
            Image = _colorImage,
            ViewType = ImageViewType.Type2D,
            Format = ColorFormat,
            SubresourceRange = new ImageSubresourceRange(ImageAspectFlags.ColorBit, 0, 1, 0, 1),
        };
        Check(_vk.CreateImageView(_device, in viewInfo, null, out _colorView), "CreateImageView");
    }

    private void CreateRenderPass()
    {
        var colorAttachment = new AttachmentDescription
        {
            Format = ColorFormat,
            Samples = SampleCountFlags.Count1Bit,
            LoadOp = AttachmentLoadOp.Clear,
            StoreOp = AttachmentStoreOp.Store,
            StencilLoadOp = AttachmentLoadOp.DontCare,
            StencilStoreOp = AttachmentStoreOp.DontCare,
            InitialLayout = ImageLayout.Undefined,
            FinalLayout = ImageLayout.TransferSrcOptimal,
        };
        var colorRef = new AttachmentReference(0, ImageLayout.ColorAttachmentOptimal);
        var subpass = new SubpassDescription
        {
            PipelineBindPoint = PipelineBindPoint.Graphics,
            ColorAttachmentCount = 1,
            PColorAttachments = &colorRef,
        };

        // Order the color writes before the vkCmdCopyImageToBuffer readback that follows
        // the pass. Without this, the implicit external dependency has srcAccessMask=0, so
        // the transfer read can race the color writes (invisible on desktop NVIDIA, broken
        // on tiled GPUs). See CopyColorToReadback.
        var copyDependency = ReadbackDependency();

        var renderPassInfo = new RenderPassCreateInfo
        {
            SType = StructureType.RenderPassCreateInfo,
            AttachmentCount = 1,
            PAttachments = &colorAttachment,
            SubpassCount = 1,
            PSubpasses = &subpass,
            DependencyCount = 1,
            PDependencies = &copyDependency,
        };
        Check(_vk.CreateRenderPass(_device, in renderPassInfo, null, out _renderPass), "CreateRenderPass");
    }

    private void CreateFramebuffer()
    {
        var view = _colorView;
        var info = new FramebufferCreateInfo
        {
            SType = StructureType.FramebufferCreateInfo,
            RenderPass = _renderPass,
            AttachmentCount = 1,
            PAttachments = &view,
            Width = _width,
            Height = _height,
            Layers = 1,
        };
        Check(_vk.CreateFramebuffer(_device, in info, null, out _framebuffer), "CreateFramebuffer");
    }

    private void CreateReadbackBuffer()
    {
        var info = new BufferCreateInfo
        {
            SType = StructureType.BufferCreateInfo,
            Size = _readbackSize,
            Usage = BufferUsageFlags.TransferDstBit,
            SharingMode = SharingMode.Exclusive,
        };
        Check(_vk.CreateBuffer(_device, in info, null, out _readbackBuffer), "CreateBuffer");

        _vk.GetBufferMemoryRequirements(_device, _readbackBuffer, out var req);
        _readbackMemory = AllocateMemory(req, MemoryPropertyFlags.HostVisibleBit | MemoryPropertyFlags.HostCoherentBit);
        Check(_vk.BindBufferMemory(_device, _readbackBuffer, _readbackMemory, 0), "BindBufferMemory");
    }

    private void CreateCommandPool()
    {
        var info = new CommandPoolCreateInfo
        {
            SType = StructureType.CommandPoolCreateInfo,
            QueueFamilyIndex = _graphicsFamily,
            Flags = CommandPoolCreateFlags.ResetCommandBufferBit,
        };
        Check(_vk.CreateCommandPool(_device, in info, null, out _commandPool), "CreateCommandPool");
    }

    // --- per-frame --------------------------------------------------------

    private CommandBuffer BeginCommands()
    {
        var allocInfo = new CommandBufferAllocateInfo
        {
            SType = StructureType.CommandBufferAllocateInfo,
            CommandPool = _commandPool,
            Level = CommandBufferLevel.Primary,
            CommandBufferCount = 1,
        };
        Check(_vk.AllocateCommandBuffers(_device, in allocInfo, out var cmd), "AllocateCommandBuffers");

        var begin = new CommandBufferBeginInfo
        {
            SType = StructureType.CommandBufferBeginInfo,
            Flags = CommandBufferUsageFlags.OneTimeSubmitBit,
        };
        Check(_vk.BeginCommandBuffer(cmd, in begin), "BeginCommandBuffer");
        return cmd;
    }

    private void BeginRenderPass(CommandBuffer cmd, Vector4 color)
    {
        var clear = new ClearValue { Color = new ClearColorValue(color.X, color.Y, color.Z, color.W) };
        var info = new RenderPassBeginInfo
        {
            SType = StructureType.RenderPassBeginInfo,
            RenderPass = _renderPass,
            Framebuffer = _framebuffer,
            RenderArea = new Rect2D(new Offset2D(0, 0), new Extent2D(_width, _height)),
            ClearValueCount = 1,
            PClearValues = &clear,
        };
        _vk.CmdBeginRenderPass(cmd, in info, SubpassContents.Inline);
    }

    private void CopyColorToReadback(CommandBuffer cmd)
    {
        var region = new BufferImageCopy
        {
            BufferOffset = 0,
            BufferRowLength = 0,
            BufferImageHeight = 0,
            ImageSubresource = new ImageSubresourceLayers(ImageAspectFlags.ColorBit, 0, 0, 1),
            ImageOffset = new Offset3D(0, 0, 0),
            ImageExtent = new Extent3D(_width, _height, 1),
        };
        _vk.CmdCopyImageToBuffer(cmd, _colorImage, ImageLayout.TransferSrcOptimal, _readbackBuffer, 1, in region);
    }

    private void EndAndSubmit(CommandBuffer cmd)
    {
        Check(_vk.EndCommandBuffer(cmd), "EndCommandBuffer");

        var fenceInfo = new FenceCreateInfo { SType = StructureType.FenceCreateInfo };
        Check(_vk.CreateFence(_device, in fenceInfo, null, out var fence), "CreateFence");

        var submit = new SubmitInfo
        {
            SType = StructureType.SubmitInfo,
            CommandBufferCount = 1,
            PCommandBuffers = &cmd,
        };
        Check(_vk.QueueSubmit(_queue, 1, in submit, fence), "QueueSubmit");
        Check(_vk.WaitForFences(_device, 1, in fence, true, ulong.MaxValue), "WaitForFences");

        _vk.DestroyFence(_device, fence, null);
        _vk.FreeCommandBuffers(_device, _commandPool, 1, in cmd);
    }

    private byte[] ReadPixels()
    {
        void* mapped = null;
        Check(_vk.MapMemory(_device, _readbackMemory, 0, _readbackSize, 0, ref mapped), "MapMemory");
        var pixels = new byte[_readbackSize];
        new Span<byte>(mapped, (int)_readbackSize).CopyTo(pixels);
        _vk.UnmapMemory(_device, _readbackMemory);
        return pixels;
    }

    // --- helpers ----------------------------------------------------------

    private DeviceMemory AllocateMemory(MemoryRequirements req, MemoryPropertyFlags flags)
    {
        var allocInfo = new MemoryAllocateInfo
        {
            SType = StructureType.MemoryAllocateInfo,
            AllocationSize = req.Size,
            MemoryTypeIndex = FindMemoryType(req.MemoryTypeBits, flags),
        };
        Check(_vk.AllocateMemory(_device, in allocInfo, null, out var memory), "AllocateMemory");
        return memory;
    }

    private uint FindMemoryType(uint typeBits, MemoryPropertyFlags flags)
    {
        _vk.GetPhysicalDeviceMemoryProperties(_physicalDevice, out var props);
        for (uint i = 0; i < props.MemoryTypeCount; i++)
        {
            if ((typeBits & (1u << (int)i)) != 0 && (props.MemoryTypes[(int)i].PropertyFlags & flags) == flags)
            {
                return i;
            }
        }

        throw new InvalidOperationException("No suitable Vulkan memory type.");
    }

    /// <summary>Subpass-0 → external dependency making color writes available to the transfer readback.</summary>
    private static SubpassDependency ReadbackDependency() => new()
    {
        SrcSubpass = 0,
        DstSubpass = Vk.SubpassExternal,
        SrcStageMask = PipelineStageFlags.ColorAttachmentOutputBit,
        SrcAccessMask = AccessFlags.ColorAttachmentWriteBit,
        DstStageMask = PipelineStageFlags.TransferBit,
        DstAccessMask = AccessFlags.TransferReadBit,
        DependencyFlags = DependencyFlags.ByRegionBit,
    };

    private static void Check(Result result, string what)
    {
        if (result != Result.Success)
        {
            throw new InvalidOperationException($"Vulkan {what} failed: {result}");
        }
    }

    public void Dispose()
    {
        if (_vk is null)
        {
            return;
        }

        if (_device.Handle != 0)
        {
            _vk.DeviceWaitIdle(_device);
            DisposeMeshBuffers();
            DisposePipelineResources();
            if (_framebuffer.Handle != 0)
            {
                _vk.DestroyFramebuffer(_device, _framebuffer, null);
            }

            if (_colorView.Handle != 0)
            {
                _vk.DestroyImageView(_device, _colorView, null);
            }

            if (_renderPass.Handle != 0)
            {
                _vk.DestroyRenderPass(_device, _renderPass, null);
            }

            if (_colorImage.Handle != 0)
            {
                _vk.DestroyImage(_device, _colorImage, null);
            }

            if (_colorMemory.Handle != 0)
            {
                _vk.FreeMemory(_device, _colorMemory, null);
            }

            if (_readbackBuffer.Handle != 0)
            {
                _vk.DestroyBuffer(_device, _readbackBuffer, null);
            }

            if (_readbackMemory.Handle != 0)
            {
                _vk.FreeMemory(_device, _readbackMemory, null);
            }

            if (_commandPool.Handle != 0)
            {
                _vk.DestroyCommandPool(_device, _commandPool, null);
            }

            if (_ownsDevice)
            {
                _vk.DestroyDevice(_device, null);
            }
        }

        if (_ownsDevice && _instance.Handle != 0)
        {
            _vk.DestroyInstance(_instance, null);
        }

        _vk.Dispose();
    }
}
