using System.Numerics;
using Silk.NET.Vulkan;
using Silk.NET.Vulkan.Extensions.KHR;
using Silk.NET.Windowing;
using Silk.NET.Maths;
using Silk.NET.Core.Native;

namespace Aigis.Render;

/// <summary>
/// A Vulkan renderer that presents directly to a real OS window's swapchain — the
/// human-facing counterpart to <see cref="HeadlessRenderer"/> (offscreen + CPU readback).
/// Deliberately a separate, independent class (no shared base) — see
/// docs/plans/human-play.md principle 3: the two renderers' resource lifetimes differ
/// enough (single offscreen image vs. N swapchain images + resize) that forcing a shared
/// abstraction now would just smuggle "resize-only" branches into shared code.
/// Milestone A: clear to a color, correct resize handling. No readback — the whole point
/// is presenting straight to the screen.
/// </summary>
public sealed unsafe partial class WindowedRenderer : IDisposable
{
    private readonly Vk _vk;
    private readonly IWindow? _window;
    private readonly nint _win32Hwnd;
    private readonly nint _win32Hinstance;
    private readonly Func<Vector2D<int>>? _win32FramebufferSize;

    /// <summary>This renderer's presentation-rate policy, resolved to a concrete Vulkan present mode
    /// every time the swapchain is (re)created — see <see cref="FrameThrottle"/> and
    /// <see cref="SelectPresentMode"/>.</summary>
    private readonly FrameThrottle _frameThrottle;

    private Instance _instance;
    private KhrSurface _khrSurface = null!;
    private SurfaceKHR _surface;
    private PhysicalDevice _physicalDevice;

    /// <summary>The chosen GPU, its vendor, driver and Vulkan version — what an error report needs to
    /// tell a driver bug from a game bug. Empty until the device is chosen.</summary>
    public string DeviceDescription { get; private set; } = string.Empty;
    private Device _device;
    private uint _graphicsFamily;
    private Queue _queue;
    private KhrSwapchain _khrSwapchain = null!;
    private CommandPool _commandPool;
    private CommandBuffer _commandBuffer;

    private Silk.NET.Vulkan.Semaphore _imageAvailable;
    private Silk.NET.Vulkan.Semaphore _renderFinished;
    private Fence _inFlight;

    private bool _resizeRequested;

    /// <param name="window">The window to present into. Must have been created with Vulkan support.</param>
    /// <param name="frameThrottle">Presentation-rate policy. Defaults to
    /// <see cref="FrameThrottle.VSync"/> — one presented frame per display refresh. See
    /// <see cref="FrameThrottle"/>'s own doc comment for the measurement that made vsync the default
    /// here and in <c>Aigis.Player.GameHost</c> on 2026-09-03; the two are deliberately the same
    /// value, so a host that builds a renderer directly (the pendulum demo, the WPF tool viewports)
    /// gets the same power behaviour as one that goes through <c>GameHost</c>.</param>
    public WindowedRenderer(IWindow window, FrameThrottle frameThrottle = FrameThrottle.VSync)
    {
        if (window.VkSurface is null)
        {
            throw new ArgumentException("Window was not created with Vulkan support (VkSurface is null).", nameof(window));
        }

        _window = window;
        _frameThrottle = frameThrottle;
        _vk = Vk.GetApi();

        try
        {
            CreateInstance();
            CreateSurface();
            PickPhysicalDeviceQueueAndPresentSupport();
            CreateDevice();
            CreateSwapchainObjects();
            CreateCommandPool();
        }
        catch
        {
            // A failure partway through (e.g. no queue family supports both graphics and
            // presentation to this surface — an expected, not "can't happen", failure mode)
            // must not leak the instance/surface/device already created: the caller's
            // constructor call threw, so it never entered a `using` scope to call Dispose().
            Dispose();
            throw;
        }
    }

    /// <summary>Creates a Vulkan swapchain directly for a WPF <c>HwndHost</c> child HWND.</summary>
    public WindowedRenderer(nint hwnd, nint hinstance, Func<Vector2D<int>> framebufferSize, FrameThrottle frameThrottle = FrameThrottle.VSync)
    {
        ArgumentOutOfRangeException.ThrowIfZero(hwnd);
        ArgumentOutOfRangeException.ThrowIfZero(hinstance);
        ArgumentNullException.ThrowIfNull(framebufferSize);

        _frameThrottle = frameThrottle;
        _win32Hwnd = hwnd;
        _win32Hinstance = hinstance;
        _win32FramebufferSize = framebufferSize;
        _vk = Vk.GetApi();

        try
        {
            CreateInstance();
            CreateSurface();
            PickPhysicalDeviceQueueAndPresentSupport();
            CreateDevice();
            CreateSwapchainObjects();
            CreateCommandPool();
        }
        catch
        {
            Dispose();
            throw;
        }
    }

    /// <summary>Call when the window's framebuffer size changes — the actual swapchain
    /// recreation happens lazily at the start of the next <see cref="RenderClearAndPresent"/>.</summary>
    public void NotifyFramebufferResized() => _resizeRequested = true;

    /// <summary>Clears the frame to <paramref name="color"/> (RGBA, 0..1) and presents it.
    /// Returns false if the frame was skipped (minimized window, or a swapchain that just
    /// got recreated and needs the next tick to acquire against it) — callers must not
    /// treat false as an error.</summary>
    public bool RenderClearAndPresent(Vector4 color)
    {
        if (!TryBeginFrame(out var imageIndex))
        {
            return false;
        }

        var begin = new CommandBufferBeginInfo { SType = StructureType.CommandBufferBeginInfo };
        Check(_vk.BeginCommandBuffer(_commandBuffer, in begin), "BeginCommandBuffer");

        // The shared _renderPass has 2 attachments (color + depth), both LoadOp=Clear, so
        // clearValueCount must cover both (VkRenderPassBeginInfo-clearValueCount) even
        // though this method only draws a clear — omitting the depth entry reads
        // out-of-bounds stack memory for the depth clear value (found by rendering-review;
        // no validation layers on this machine to catch it at run time).
        var clears = stackalloc ClearValue[2];
        clears[0] = new ClearValue { Color = new ClearColorValue(color.X, color.Y, color.Z, color.W) };
        clears[1] = new ClearValue { DepthStencil = new ClearDepthStencilValue(1.0f, 0) };
        var renderPassInfo = new RenderPassBeginInfo
        {
            SType = StructureType.RenderPassBeginInfo,
            RenderPass = _renderPass,
            Framebuffer = _swapchainFramebuffers[imageIndex],
            RenderArea = new Rect2D(new Offset2D(0, 0), _swapchainExtent),
            ClearValueCount = 2,
            PClearValues = clears,
        };
        _vk.CmdBeginRenderPass(_commandBuffer, in renderPassInfo, SubpassContents.Inline);
        _vk.CmdEndRenderPass(_commandBuffer);
        Check(_vk.EndCommandBuffer(_commandBuffer), "EndCommandBuffer");

        EndFrame(imageIndex);
        return true;
    }

    /// <summary>
    /// This renderer's opt-in CPU render-phase breakdown (<c>AIGIS_PROFILE_RENDER=1</c>) — inert and
    /// free otherwise. Read by the host after a sampling window; see <see cref="RenderPhaseProfiler"/>
    /// for what the phases mean and why the first one is the one to read first.
    /// </summary>
    public RenderPhaseProfiler RenderPhases { get; } = new();

    /// <summary>Waits for the in-flight fence, recreates the swapchain if a resize was
    /// flagged or the window is minimized, and acquires the next swapchain image. Returns
    /// false (skip this frame, not an error) if minimized or the swapchain was just
    /// recreated; otherwise the command buffer is reset and ready to record into, and the
    /// caller must follow with <see cref="EndFrame"/> after recording.</summary>
    private bool TryBeginFrame(out uint imageIndex)
    {
        imageIndex = 0;

        // FIRST, before the swapchain may be recreated under it: put back whatever the previous frame
        // left half-done. See RecoverAbandonedFrame — without this, one contained exception anywhere
        // between here and Submit() freezes the window for good.
        RecoverAbandonedFrame();

        if (!PrepareSwapchainForRender())
        {
            return false; // minimized — nothing to present to
        }

        Check(_vk.WaitForFences(_device, 1, in _inFlight, true, ulong.MaxValue), "WaitForFences");

        var acquireResult = _khrSwapchain.AcquireNextImage(_device, _swapchain, ulong.MaxValue, _imageAvailable, default, ref imageIndex);
        if (acquireResult == Result.ErrorOutOfDateKhr)
        {
            RecreateSwapchain();
            return false;
        }

        if (acquireResult != Result.Success && acquireResult != Result.SuboptimalKhr)
        {
            throw new InvalidOperationException($"Vulkan AcquireNextImage failed: {acquireResult}");
        }

        Check(_vk.ResetFences(_device, 1, in _inFlight), "ResetFences");
        Check(_vk.ResetCommandBuffer(_commandBuffer, CommandBufferResetFlags.None), "ResetCommandBuffer");

        // From here the frame is OPEN: the fence is unsignalled, _imageAvailable is signalled by the
        // acquire above, and only Submit() can put either right again.
        _frameOpen = true;
        _openImageIndex = imageIndex;
        return true;
    }

    /// <summary>Whether a frame has been begun and not yet submitted — see
    /// <see cref="RecoverAbandonedFrame"/>.</summary>
    private bool _frameOpen;

    /// <summary>The swapchain image <see cref="TryBeginFrame"/> acquired for the open frame, so an
    /// abandoned one can still be presented and its semaphore consumed.</summary>
    private uint _openImageIndex;

    /// <summary>
    /// Closes a frame that was begun and never submitted, so the next one can start.
    ///
    /// <para><b>The defect this exists for is a permanent, unkillable window freeze.</b>
    /// <see cref="TryBeginFrame"/> waits the in-flight fence, acquires an image, then RESETS the fence
    /// — and from that point only <see cref="Submit"/> can ever signal it again. Between the two sit a
    /// dozen operations that can throw: <c>lighting.Validate()</c>, <c>PrimitiveInstancePacker.Pack</c>
    /// (whose own <c>Validate</c>/<c>ValidateDecomposable</c> comment records "a crash a player hit
    /// twice"), the mesh upload, the instance-buffer growth, <c>BeginCommandBuffer</c>. A throw there
    /// is CONTAINED by <c>GameHost</c>'s render guard — the process lives, the simulation and the mixer
    /// carry on — and then the very next frame blocks in <c>WaitForFences(..., ulong.MaxValue)</c> on a
    /// fence nothing will ever signal. The window is frozen, the loop thread is gone, and closing it is
    /// the only way out.</para>
    ///
    /// <para><b>Why an empty submit rather than just leaving the fence signalled.</b> Not resetting the
    /// fence until later would fix the fence and leave the other half broken: the acquire has already
    /// signalled <c>_imageAvailable</c>, and nothing waits on it, so the next acquire would signal an
    /// already-signalled semaphore. Submitting an empty command buffer drains both — it waits
    /// <c>_imageAvailable</c>, signals <c>_renderFinished</c> and <c>_inFlight</c> — and presenting the
    /// acquired image consumes <c>_renderFinished</c>. The image was never rendered into, so one frame
    /// shows whatever was in it; that is the cost of an error frame, against a window that never
    /// updates again.</para>
    ///
    /// <para>Resetting the command buffer is legal whatever state the abandoned frame left it in: a
    /// buffer that is mid-recording resets to initial exactly as an executable one does, and this one
    /// cannot be pending because nothing was ever submitted from it.</para>
    /// </summary>
    private void RecoverAbandonedFrame()
    {
        if (!_frameOpen)
        {
            return;
        }

        Check(_vk.ResetCommandBuffer(_commandBuffer, CommandBufferResetFlags.None), "ResetCommandBuffer(abandoned frame)");
        var begin = new CommandBufferBeginInfo
        {
            SType = StructureType.CommandBufferBeginInfo,
            Flags = CommandBufferUsageFlags.OneTimeSubmitBit,
        };
        Check(_vk.BeginCommandBuffer(_commandBuffer, in begin), "BeginCommandBuffer(abandoned frame)");
        Check(_vk.EndCommandBuffer(_commandBuffer), "EndCommandBuffer(abandoned frame)");
        Submit();

        // Cleared only once the submit is in — the same rule EndFrame follows, and here it is what
        // makes recovery RETRYABLE. Every Check above throws without having submitted anything, so a
        // recovery that fails partway (a device-lost or out-of-memory class failure) leaves the frame
        // still marked open and the next one tries again. Clearing it first would spend the flag on a
        // recovery that did not happen, and the very next WaitForFences would be the permanent freeze
        // this method exists to prevent — one level removed, and with nothing left to catch it.
        _frameOpen = false;

        // The present result is deliberately ignored: this frame is already an error frame, and an
        // out-of-date swapchain here is handled by the ordinary path on the next one.
        _ = Present(_openImageIndex);
    }

    private bool PrepareSwapchainForRender()
    {
        if (_resizeRequested)
        {
            _resizeRequested = false;
            RecreateSwapchain();
        }

        return _swapchainExtent.Width != 0 && _swapchainExtent.Height != 0;
    }

    /// <summary>Submits the recorded (and already-ended) command buffer and presents.</summary>
    private void EndFrame(uint imageIndex)
    {
        Submit();

        // Cleared as soon as the submit is in: from here the fence WILL be signalled, so the next
        // frame's wait terminates and there is nothing for RecoverAbandonedFrame to put right.
        _frameOpen = false;

        var presentResult = Present(imageIndex);
        if (presentResult == Result.ErrorOutOfDateKhr || presentResult == Result.SuboptimalKhr)
        {
            RecreateSwapchain();
        }
        else if (presentResult != Result.Success)
        {
            throw new InvalidOperationException($"Vulkan QueuePresent failed: {presentResult}");
        }
    }

    private void Submit()
    {
        var waitStage = PipelineStageFlags.ColorAttachmentOutputBit;
        var imageAvailable = _imageAvailable;
        var renderFinished = _renderFinished;
        var commandBuffer = _commandBuffer;
        var submit = new SubmitInfo
        {
            SType = StructureType.SubmitInfo,
            WaitSemaphoreCount = 1,
            PWaitSemaphores = &imageAvailable,
            PWaitDstStageMask = &waitStage,
            CommandBufferCount = 1,
            PCommandBuffers = &commandBuffer,
            SignalSemaphoreCount = 1,
            PSignalSemaphores = &renderFinished,
        };
        Check(_vk.QueueSubmit(_queue, 1, in submit, _inFlight), "QueueSubmit");
    }

    private Result Present(uint imageIndex)
    {
        var renderFinished = _renderFinished;
        var swapchain = _swapchain;
        var presentInfo = new PresentInfoKHR
        {
            SType = StructureType.PresentInfoKhr,
            WaitSemaphoreCount = 1,
            PWaitSemaphores = &renderFinished,
            SwapchainCount = 1,
            PSwapchains = &swapchain,
            PImageIndices = &imageIndex,
        };
        return _khrSwapchain.QueuePresent(_queue, in presentInfo);
    }

    // --- setup --------------------------------------------------------------

    private void CreateInstance()
    {
        if (_win32Hwnd != nint.Zero)
        {
            CreateWin32Instance();
            return;
        }
        var requiredExtensions = _window!.VkSurface!.GetRequiredExtensions(out var extensionCount);

        var appInfo = new ApplicationInfo
        {
            SType = StructureType.ApplicationInfo,
            ApiVersion = Vk.Version11,
        };
        var createInfo = new InstanceCreateInfo
        {
            SType = StructureType.InstanceCreateInfo,
            PApplicationInfo = &appInfo,
            EnabledExtensionCount = extensionCount,
            PpEnabledExtensionNames = requiredExtensions,
        };

        Check(_vk.CreateInstance(in createInfo, null, out _instance), "CreateInstance");
    }

    private void CreateSurface()
    {
        if (!_vk.TryGetInstanceExtension(_instance, out _khrSurface))
        {
            throw new InvalidOperationException("VK_KHR_surface is not available on this Vulkan instance.");
        }

        if (_win32Hwnd == nint.Zero)
        {
            _surface = _window!.VkSurface!.Create<AllocationCallbacks>(_instance.ToHandle(), null).ToSurface();
            return;
        }

        if (!_vk.TryGetInstanceExtension(_instance, out KhrWin32Surface win32Surface))
        {
            throw new InvalidOperationException("VK_KHR_win32_surface is not available on this Vulkan instance.");
        }

        var info = new Win32SurfaceCreateInfoKHR
        {
            SType = StructureType.Win32SurfaceCreateInfoKhr,
            Hinstance = _win32Hinstance,
            Hwnd = _win32Hwnd,
        };
        Check(win32Surface.CreateWin32Surface(_instance, in info, null, out _surface), "CreateWin32SurfaceKHR");
    }

    private void CreateWin32Instance()
    {
        var surfaceName = (byte*)SilkMarshal.StringToPtr(KhrSurface.ExtensionName);
        var win32Name = (byte*)SilkMarshal.StringToPtr(KhrWin32Surface.ExtensionName);
        try
        {
            var extensions = stackalloc byte*[2] { surfaceName, win32Name };
            var appInfo = new ApplicationInfo { SType = StructureType.ApplicationInfo, ApiVersion = Vk.Version11 };
            var createInfo = new InstanceCreateInfo
            {
                SType = StructureType.InstanceCreateInfo,
                PApplicationInfo = &appInfo,
                EnabledExtensionCount = 2,
                PpEnabledExtensionNames = extensions,
            };
            Check(_vk.CreateInstance(in createInfo, null, out _instance), "CreateInstance");
        }
        finally
        {
            SilkMarshal.Free((nint)surfaceName);
            SilkMarshal.Free((nint)win32Name);
        }
    }

    private void PickPhysicalDeviceQueueAndPresentSupport()
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
                if ((families[i].QueueFlags & QueueFlags.GraphicsBit) == 0)
                {
                    continue;
                }

                Check(_khrSurface.GetPhysicalDeviceSurfaceSupport(device, i, _surface, out var presentSupported),
                    "GetPhysicalDeviceSurfaceSupport");
                if (presentSupported)
                {
                    _physicalDevice = device;
                    _graphicsFamily = i;
                    _vk.GetPhysicalDeviceProperties(device, out var properties);
                    DeviceDescription = string.Create(System.Globalization.CultureInfo.InvariantCulture,
                        $"{Silk.NET.Core.Native.SilkMarshal.PtrToString((nint)properties.DeviceName)} (vendor 0x{properties.VendorID:X4}, driver 0x{properties.DriverVersion:X8}, Vulkan {properties.ApiVersion >> 22}.{(properties.ApiVersion >> 12) & 0x3FF}.{properties.ApiVersion & 0xFFF})");
                    return;
                }
            }
        }

        throw new InvalidOperationException(
            "No Vulkan device with a single queue family supporting both graphics and presentation to this surface " +
            "(v1 does not support separate graphics/present queue families).");
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

        var swapchainExtension = KhrSwapchain.ExtensionName;
        var enabledExtension = (byte*)Silk.NET.Core.Native.SilkMarshal.StringToPtr(swapchainExtension);
        try
        {
            var deviceInfo = new DeviceCreateInfo
            {
                SType = StructureType.DeviceCreateInfo,
                QueueCreateInfoCount = 1,
                PQueueCreateInfos = &queueInfo,
                EnabledExtensionCount = 1,
                PpEnabledExtensionNames = &enabledExtension,
            };

            Check(_vk.CreateDevice(_physicalDevice, in deviceInfo, null, out _device), "CreateDevice");
        }
        finally
        {
            Silk.NET.Core.Native.SilkMarshal.Free((nint)enabledExtension);
        }

        _vk.GetDeviceQueue(_device, _graphicsFamily, 0, out _queue);

        if (!_vk.TryGetDeviceExtension(_instance, _device, out _khrSwapchain))
        {
            throw new InvalidOperationException("VK_KHR_swapchain is not available on this Vulkan device.");
        }
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

        var allocInfo = new CommandBufferAllocateInfo
        {
            SType = StructureType.CommandBufferAllocateInfo,
            CommandPool = _commandPool,
            Level = CommandBufferLevel.Primary,
            CommandBufferCount = 1,
        };
        Check(_vk.AllocateCommandBuffers(_device, in allocInfo, out _commandBuffer), "AllocateCommandBuffers");

        var semaphoreInfo = new SemaphoreCreateInfo { SType = StructureType.SemaphoreCreateInfo };
        Check(_vk.CreateSemaphore(_device, in semaphoreInfo, null, out _imageAvailable), "CreateSemaphore(imageAvailable)");
        Check(_vk.CreateSemaphore(_device, in semaphoreInfo, null, out _renderFinished), "CreateSemaphore(renderFinished)");

        var fenceInfo = new FenceCreateInfo { SType = StructureType.FenceCreateInfo, Flags = FenceCreateFlags.SignaledBit };
        Check(_vk.CreateFence(_device, in fenceInfo, null, out _inFlight), "CreateFence");
    }

    private static void Check(Result result, string what)
    {
        if (result != Result.Success)
        {
            throw new InvalidOperationException($"Vulkan {what} failed: {result}");
        }
    }

    private bool _disposed;

    public void Dispose()
    {
        // Idempotent — the constructor's own catch block calls this on partial-init
        // failure, and a caller's `using` may also dispose normally afterward.
        if (_disposed)
        {
            return;
        }

        _disposed = true;

        if (_device.Handle != 0)
        {
            _vk.DeviceWaitIdle(_device);

            // Pipelines reference _renderPass — destroy them before DestroySwapchainObjects
            // tears the render pass down.
            DisposePrimitiveResources();
            DisposeMeshBuffers();
            DisposeShadowResources();
            DisposeLineResources();
            DisposeParticleResources();
            DisposeOverlayResources();
            DisposeOverlayImageResources();
            DisposeIrradianceResources();
            DisposeDecalResources();
            DisposePostProcessResources();
            DisposeSurfaceTextureResources();
            DisposeUiShapeResources();
            DestroyGpuTimestamps();

            DestroySwapchainObjects();

            if (_inFlight.Handle != 0)
            {
                _vk.DestroyFence(_device, _inFlight, null);
            }

            if (_renderFinished.Handle != 0)
            {
                _vk.DestroySemaphore(_device, _renderFinished, null);
            }

            if (_imageAvailable.Handle != 0)
            {
                _vk.DestroySemaphore(_device, _imageAvailable, null);
            }

            if (_commandPool.Handle != 0)
            {
                _vk.DestroyCommandPool(_device, _commandPool, null);
            }

            _vk.DestroyDevice(_device, null);
        }

        if (_surface.Handle != 0)
        {
            _khrSurface.DestroySurface(_instance, _surface, null);
        }

        if (_instance.Handle != 0)
        {
            _vk.DestroyInstance(_instance, null);
        }

        _khrSwapchain?.Dispose();
        _khrSurface?.Dispose();
        _vk.Dispose();
    }
}
