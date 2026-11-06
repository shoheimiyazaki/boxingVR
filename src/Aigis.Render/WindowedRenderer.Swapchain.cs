using Silk.NET.Vulkan;

namespace Aigis.Render;

public sealed unsafe partial class WindowedRenderer
{
    private const Format DepthFormat = Format.D32Sfloat;

    private SwapchainKHR _swapchain;
    private Image[] _swapchainImages = [];
    private ImageView[] _swapchainImageViews = [];
    private Framebuffer[] _swapchainFramebuffers = [];
    private RenderPass _renderPass;
    private Format _swapchainFormat;
    private Extent2D _swapchainExtent;

    /// <summary>The swapchain's current size in physical pixels — what every full-screen pass (the
    /// colour pass, the finishing pass) pays for. Printed beside <see cref="RenderPhases"/>'s report,
    /// because a GPU time means nothing without the pixel count it was measured at.</summary>
    public (int Width, int Height) TargetSize => ((int)_swapchainExtent.Width, (int)_swapchainExtent.Height);

    private Image _depthImage;
    private DeviceMemory _depthMemory;
    private ImageView _depthView;

    /// <summary>Builds the swapchain, its image views/framebuffers, and the (format-stable)
    /// render pass. Called once from the constructor and again — minus the render pass,
    /// which is assumed format-stable across resizes — from <see cref="RecreateSwapchain"/>.</summary>
    private void CreateSwapchainObjects()
    {
        var (capabilities, format, extent) = QuerySurfaceConfiguration();

        if (_renderPass.Handle != 0 && format.Format != _swapchainFormat)
        {
            // The render pass's format-stability assumption (doc comment above) just broke
            // — e.g. a monitor swap or colorspace change during this resize. Milestone A
            // does not rebuild the render pass, so fail loudly rather than silently drawing
            // through a now-mismatched attachment format (undefined behavior, and exactly
            // the class of bug this machine's missing Vulkan validation layers can't catch).
            throw new NotSupportedException(
                $"Swapchain surface format changed from {_swapchainFormat} to {format.Format} across a resize; " +
                "WindowedRenderer (Milestone A) does not yet support recreating the render pass.");
        }

        _swapchainFormat = format.Format;
        _swapchainExtent = extent;

        if (extent.Width == 0 || extent.Height == 0)
        {
            // Minimized: leave _swapchain default: RenderClearAndPresent skips these frames.
            return;
        }

        var presentMode = SelectPresentMode(_frameThrottle);

        // Mailbox needs a third image to have anything to swap INTO while one is queued and one is
        // being scanned out — with only two it degrades to FIFO's blocking behaviour, which would
        // make FrameThrottle.LowLatency silently do nothing on a driver reporting MinImageCount 2.
        var desiredImageCount = presentMode == PresentModeKHR.MailboxKhr
            ? Math.Max(capabilities.MinImageCount + 1, 3u)
            : capabilities.MinImageCount + 1;
        var imageCount = desiredImageCount;
        if (capabilities.MaxImageCount > 0 && imageCount > capabilities.MaxImageCount)
        {
            imageCount = capabilities.MaxImageCount;
        }

        var createInfo = new SwapchainCreateInfoKHR
        {
            SType = StructureType.SwapchainCreateInfoKhr,
            Surface = _surface,
            MinImageCount = imageCount,
            ImageFormat = format.Format,
            ImageColorSpace = format.ColorSpace,
            ImageExtent = extent,
            ImageArrayLayers = 1,
            ImageUsage = ImageUsageFlags.ColorAttachmentBit,
            ImageSharingMode = SharingMode.Exclusive,
            PreTransform = capabilities.CurrentTransform,
            CompositeAlpha = CompositeAlphaFlagsKHR.OpaqueBitKhr,
            // Resolved from this renderer's FrameThrottle against what the surface actually
            // supports — see SelectPresentMode. FIFO is the guaranteed fallback.
            PresentMode = presentMode,
            Clipped = true,
        };
        Check(_khrSwapchain.CreateSwapchain(_device, in createInfo, null, out _swapchain), "CreateSwapchainKHR");

        uint actualImageCount = 0;
        _khrSwapchain.GetSwapchainImages(_device, _swapchain, ref actualImageCount, null);
        _swapchainImages = new Image[actualImageCount];
        fixed (Image* p = _swapchainImages)
        {
            _khrSwapchain.GetSwapchainImages(_device, _swapchain, ref actualImageCount, p);
        }

        _swapchainImageViews = new ImageView[actualImageCount];
        for (var i = 0; i < actualImageCount; i++)
        {
            var viewInfo = new ImageViewCreateInfo
            {
                SType = StructureType.ImageViewCreateInfo,
                Image = _swapchainImages[i],
                ViewType = ImageViewType.Type2D,
                Format = _swapchainFormat,
                SubresourceRange = new ImageSubresourceRange(ImageAspectFlags.ColorBit, 0, 1, 0, 1),
            };
            Check(_vk.CreateImageView(_device, in viewInfo, null, out _swapchainImageViews[i]), "CreateImageView");
        }

        if (_renderPass.Handle == 0)
        {
            CreateRenderPass();
        }

        CreateDepthTarget();

        // One depth buffer shared by every swapchain framebuffer — safe because Milestone A
        // runs a single frame in flight (one command buffer, one fence wait per present), so
        // no two frames ever write the depth image concurrently.
        _swapchainFramebuffers = new Framebuffer[actualImageCount];
        var attachments = stackalloc ImageView[2];
        attachments[1] = _depthView;
        for (var i = 0; i < actualImageCount; i++)
        {
            attachments[0] = _swapchainImageViews[i];
            var framebufferInfo = new FramebufferCreateInfo
            {
                SType = StructureType.FramebufferCreateInfo,
                RenderPass = _renderPass,
                AttachmentCount = 2,
                PAttachments = attachments,
                Width = _swapchainExtent.Width,
                Height = _swapchainExtent.Height,
                Layers = 1,
            };
            Check(_vk.CreateFramebuffer(_device, in framebufferInfo, null, out _swapchainFramebuffers[i]), "CreateFramebuffer");
        }
    }

    private void CreateDepthTarget()
    {
        var imageInfo = new ImageCreateInfo
        {
            SType = StructureType.ImageCreateInfo,
            ImageType = ImageType.Type2D,
            Format = DepthFormat,
            Extent = new Extent3D(_swapchainExtent.Width, _swapchainExtent.Height, 1),
            MipLevels = 1,
            ArrayLayers = 1,
            Samples = SampleCountFlags.Count1Bit,
            Tiling = ImageTiling.Optimal,
            Usage = ImageUsageFlags.DepthStencilAttachmentBit,
            SharingMode = SharingMode.Exclusive,
            InitialLayout = ImageLayout.Undefined,
        };
        Check(_vk.CreateImage(_device, in imageInfo, null, out _depthImage), "CreateImage(depth)");

        _vk.GetImageMemoryRequirements(_device, _depthImage, out var req);
        _depthMemory = AllocateMemory(req, MemoryPropertyFlags.DeviceLocalBit);
        Check(_vk.BindImageMemory(_device, _depthImage, _depthMemory, 0), "BindImageMemory(depth)");

        var viewInfo = new ImageViewCreateInfo
        {
            SType = StructureType.ImageViewCreateInfo,
            Image = _depthImage,
            ViewType = ImageViewType.Type2D,
            Format = DepthFormat,
            SubresourceRange = new ImageSubresourceRange(ImageAspectFlags.DepthBit, 0, 1, 0, 1),
        };
        Check(_vk.CreateImageView(_device, in viewInfo, null, out _depthView), "CreateImageView(depth)");
    }

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

    /// <summary>Presentation-ready-for-display attachment — no readback dependency needed
    /// (contrast with HeadlessRenderer.ReadbackDependency): the implicit external
    /// dependency already orders color writes before <c>vkQueuePresentKHR</c>'s implicit
    /// wait-semaphore synchronization.</summary>
    private void CreateRenderPass()
    {
        var colorAttachment = new AttachmentDescription
        {
            Format = _swapchainFormat,
            Samples = SampleCountFlags.Count1Bit,
            LoadOp = AttachmentLoadOp.Clear,
            StoreOp = AttachmentStoreOp.Store,
            StencilLoadOp = AttachmentLoadOp.DontCare,
            StencilStoreOp = AttachmentStoreOp.DontCare,
            InitialLayout = ImageLayout.Undefined,
            FinalLayout = ImageLayout.PresentSrcKhr,
        };
        var depthAttachment = new AttachmentDescription
        {
            Format = DepthFormat,
            Samples = SampleCountFlags.Count1Bit,
            LoadOp = AttachmentLoadOp.Clear,
            StoreOp = AttachmentStoreOp.DontCare,
            StencilLoadOp = AttachmentLoadOp.DontCare,
            StencilStoreOp = AttachmentStoreOp.DontCare,
            InitialLayout = ImageLayout.Undefined,
            FinalLayout = ImageLayout.DepthStencilAttachmentOptimal,
        };
        var colorRef = new AttachmentReference(0, ImageLayout.ColorAttachmentOptimal);
        var depthRef = new AttachmentReference(1, ImageLayout.DepthStencilAttachmentOptimal);
        var subpass = new SubpassDescription
        {
            PipelineBindPoint = PipelineBindPoint.Graphics,
            ColorAttachmentCount = 1,
            PColorAttachments = &colorRef,
            PDepthStencilAttachment = &depthRef,
        };
        var dependency = new SubpassDependency
        {
            SrcSubpass = Vk.SubpassExternal,
            DstSubpass = 0,
            SrcStageMask = PipelineStageFlags.ColorAttachmentOutputBit | PipelineStageFlags.EarlyFragmentTestsBit,
            SrcAccessMask = 0,
            DstStageMask = PipelineStageFlags.ColorAttachmentOutputBit | PipelineStageFlags.EarlyFragmentTestsBit,
            DstAccessMask = AccessFlags.ColorAttachmentWriteBit | AccessFlags.DepthStencilAttachmentWriteBit,
        };

        var attachments = stackalloc AttachmentDescription[2];
        attachments[0] = colorAttachment;
        attachments[1] = depthAttachment;
        var renderPassInfo = new RenderPassCreateInfo
        {
            SType = StructureType.RenderPassCreateInfo,
            AttachmentCount = 2,
            PAttachments = attachments,
            SubpassCount = 1,
            PSubpasses = &subpass,
            DependencyCount = 1,
            PDependencies = &dependency,
        };
        Check(_vk.CreateRenderPass(_device, in renderPassInfo, null, out _renderPass), "CreateRenderPass");
    }

    /// <summary>Waits for the device to go idle, tears down the swapchain-sized objects
    /// (images/views/framebuffers/swapchain — not the render pass, assumed format-stable),
    /// and rebuilds them against the window's current framebuffer size. A no-op-safe
    /// minimized window (zero extent) just leaves the swapchain torn down until resized
    /// back up — see docs/plans/human-play.md pitfall notes.</summary>
    private void RecreateSwapchain()
    {
        _vk.DeviceWaitIdle(_device);
        DestroySwapchainImageObjects();
        CreateSwapchainObjects();
    }

    /// <summary>
    /// Resolves <paramref name="throttle"/> against the present modes this surface actually reports,
    /// walking that policy's preference list and landing on <see cref="PresentModeKHR.FifoKhr"/> —
    /// the one mode the Vulkan spec guarantees — if nothing better is available. See
    /// <see cref="FrameThrottle"/> for what each policy means and why it is expressed as a
    /// preference rather than a concrete mode.
    /// </summary>
    private PresentModeKHR SelectPresentMode(FrameThrottle throttle)
    {
        uint modeCount = 0;
        _khrSurface.GetPhysicalDeviceSurfacePresentModes(_physicalDevice, _surface, ref modeCount, null);
        if (modeCount == 0)
        {
            return PresentModeKHR.FifoKhr;
        }

        var modes = new PresentModeKHR[modeCount];
        fixed (PresentModeKHR* p = modes)
        {
            _khrSurface.GetPhysicalDeviceSurfacePresentModes(_physicalDevice, _surface, ref modeCount, p);
        }

        // Ordered preference per policy. FIFO is deliberately last in every list rather than being a
        // separate "not found" branch — it is always supported, so the walk cannot fall off the end.
        ReadOnlySpan<PresentModeKHR> preference = throttle switch
        {
            FrameThrottle.Unlimited => [PresentModeKHR.ImmediateKhr, PresentModeKHR.MailboxKhr, PresentModeKHR.FifoKhr],
            FrameThrottle.LowLatency => [PresentModeKHR.MailboxKhr, PresentModeKHR.FifoKhr],
            _ => [PresentModeKHR.FifoKhr],
        };

        foreach (var candidate in preference)
        {
            if (Array.IndexOf(modes, candidate) >= 0)
            {
                return candidate;
            }
        }

        return PresentModeKHR.FifoKhr;
    }

    private (SurfaceCapabilitiesKHR Capabilities, SurfaceFormatKHR Format, Extent2D Extent) QuerySurfaceConfiguration()
    {
        Check(_khrSurface.GetPhysicalDeviceSurfaceCapabilities(_physicalDevice, _surface, out var capabilities),
            "GetPhysicalDeviceSurfaceCapabilities");

        uint formatCount = 0;
        _khrSurface.GetPhysicalDeviceSurfaceFormats(_physicalDevice, _surface, ref formatCount, null);
        if (formatCount == 0)
        {
            throw new InvalidOperationException("Surface reports no supported formats.");
        }

        var formats = new SurfaceFormatKHR[formatCount];
        fixed (SurfaceFormatKHR* p = formats)
        {
            _khrSurface.GetPhysicalDeviceSurfaceFormats(_physicalDevice, _surface, ref formatCount, p);
        }

        var chosenFormat = formats.FirstOrDefault(
            f => f.Format == Format.B8G8R8A8Unorm && f.ColorSpace == ColorSpaceKHR.SpaceSrgbNonlinearKhr,
            formats[0]);

        var frameBufferSize = _win32FramebufferSize?.Invoke() ?? _window!.FramebufferSize;
        Extent2D extent;
        if (capabilities.CurrentExtent.Width != uint.MaxValue)
        {
            extent = capabilities.CurrentExtent;
        }
        else if (frameBufferSize.X == 0 || frameBufferSize.Y == 0)
        {
            // Minimized: report zero straight through — clamping against MinImageExtent
            // (commonly 1x1) would otherwise mask this as a valid, non-zero extent and
            // defeat RenderClearAndPresent's "skip minimized frames" guard.
            extent = new Extent2D(0, 0);
        }
        else
        {
            extent = new Extent2D(
                Math.Clamp((uint)frameBufferSize.X, capabilities.MinImageExtent.Width, capabilities.MaxImageExtent.Width),
                Math.Clamp((uint)frameBufferSize.Y, capabilities.MinImageExtent.Height, capabilities.MaxImageExtent.Height));
        }

        return (capabilities, chosenFormat, extent);
    }

    private void DestroySwapchainImageObjects()
    {
        ReleasePostProcessTarget();
        foreach (var framebuffer in _swapchainFramebuffers)
        {
            _vk.DestroyFramebuffer(_device, framebuffer, null);
        }

        foreach (var view in _swapchainImageViews)
        {
            _vk.DestroyImageView(_device, view, null);
        }

        if (_depthView.Handle != 0)
        {
            _vk.DestroyImageView(_device, _depthView, null);
            _depthView = default;
        }

        if (_depthImage.Handle != 0)
        {
            _vk.DestroyImage(_device, _depthImage, null);
            _depthImage = default;
        }

        if (_depthMemory.Handle != 0)
        {
            _vk.FreeMemory(_device, _depthMemory, null);
            _depthMemory = default;
        }

        if (_swapchain.Handle != 0)
        {
            _khrSwapchain.DestroySwapchain(_device, _swapchain, null);
            _swapchain = default;
        }

        _swapchainFramebuffers = [];
        _swapchainImageViews = [];
        _swapchainImages = [];
    }

    private void DestroySwapchainObjects()
    {
        DestroySwapchainImageObjects();
        if (_renderPass.Handle != 0)
        {
            _vk.DestroyRenderPass(_device, _renderPass, null);
        }
    }
}
