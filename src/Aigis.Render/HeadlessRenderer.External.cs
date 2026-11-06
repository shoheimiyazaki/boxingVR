using System.Numerics;
using Silk.NET.Vulkan;

namespace Aigis.Render;

/// <summary>
/// A Vulkan instance/device created by someone else (MiniRing: the OpenXR host, which must create
/// them with the extensions the XR runtime asks for). Raw handles so a caller need not share Silk
/// object identities.
/// </summary>
public readonly record struct ExternalVulkanDevice(nint Instance, nint PhysicalDevice, nint Device, uint QueueFamilyIndex, uint QueueIndex);

/// <summary>
/// External-target mode (added for the MiniRing VR host, not in upstream Aigis): render the same
/// primitive scene, but instead of reading the frame back to CPU, copy it on the GPU into an image the
/// caller owns — an OpenXR swapchain image. Same device, same queue, one synchronous submit per call.
/// <para><b>Format contract.</b> The scene is rendered into this renderer's own R8G8B8A8_UNORM target
/// holding display-ready (gamma-encoded) values, then copied bit-for-bit with vkCmdCopyImage. Copy it
/// into an R8G8B8A8_SRGB image and the compositor decodes exactly what a desktop window would show;
/// copying into a UNORM image is allowed but the runtime will treat the values as linear (too bright).
/// A BGRA target is NOT supported (a raw copy would swap red and blue).</para>
/// <para><b>Layout contract.</b> The target is expected in COLOR_ATTACHMENT_OPTIMAL on entry (OpenXR's
/// state for an acquired swapchain image) and is left in COLOR_ATTACHMENT_OPTIMAL (what OpenXR requires
/// at release).</para>
/// </summary>
public sealed unsafe partial class HeadlessRenderer
{
    private Image? _externalTarget;

    /// <summary>Builds a renderer on an existing device instead of creating its own. The caller keeps
    /// ownership: <see cref="Dispose"/> destroys only this renderer's own resources.</summary>
    public HeadlessRenderer(uint width, uint height, ExternalVulkanDevice device, uint shadowMapSize = ShadowMapConfig.MapSize)
    {
        ArgumentOutOfRangeException.ThrowIfZero(width);
        ArgumentOutOfRangeException.ThrowIfZero(height);
        ArgumentOutOfRangeException.ThrowIfZero(shadowMapSize);
        _width = width;
        _height = height;
        _shadowMapSize = shadowMapSize;
        _readbackSize = (ulong)width * height * 4;
        _ownsDevice = false;
        _vk = Vk.GetApi();
        try
        {
            _instance = new Instance(device.Instance);
            _physicalDevice = new PhysicalDevice(device.PhysicalDevice);
            _device = new Device(device.Device);
            _graphicsFamily = device.QueueFamilyIndex;
            _vk.GetDeviceQueue(_device, device.QueueFamilyIndex, device.QueueIndex, out _queue);
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

    /// <summary>
    /// Renders the scene and copies it into <paramref name="targetImage"/> (a VkImage handle of the same
    /// size, TRANSFER_DST usage). Blocks until the GPU has finished, so the caller may release the image
    /// to the compositor as soon as this returns.
    /// </summary>
    public void RenderPrimitiveSceneToImage(
        ulong targetImage,
        Vector4 clearColor,
        Matrix4x4 viewProjection,
        IReadOnlyList<PrimitiveInstance> instances,
        IReadOnlyList<DebugLine> lines,
        SceneLighting? lighting = null,
        ShadowMapConfig? shadow = null,
        Vector3? cameraPosition = null)
    {
        _externalTarget = new Image(targetImage);
        try
        {
            _ = RenderPrimitiveSceneForView(clearColor, viewProjection, instances, lines, null, null, lighting, shadow, cameraPosition,
                null, null, null, null, null, null, postProcessSampleRect: null);
        }
        finally
        {
            _externalTarget = null;
        }
    }

    private void CopyColorToImage(CommandBuffer cmd, Image target)
    {
        var range = new ImageSubresourceRange(ImageAspectFlags.ColorBit, 0, 1, 0, 1);
        var toTransfer = new ImageMemoryBarrier
        {
            SType = StructureType.ImageMemoryBarrier,
            SrcAccessMask = AccessFlags.ColorAttachmentWriteBit,
            DstAccessMask = AccessFlags.TransferWriteBit,
            OldLayout = ImageLayout.ColorAttachmentOptimal,
            NewLayout = ImageLayout.TransferDstOptimal,
            SrcQueueFamilyIndex = Vk.QueueFamilyIgnored,
            DstQueueFamilyIndex = Vk.QueueFamilyIgnored,
            Image = target,
            SubresourceRange = range,
        };
        _vk.CmdPipelineBarrier(cmd, PipelineStageFlags.ColorAttachmentOutputBit, PipelineStageFlags.TransferBit, 0, 0, null, 0, null, 1, in toTransfer);

        // Our own color image is already TRANSFER_SRC_OPTIMAL (the render pass's finalLayout) and the
        // pass's external dependency orders its writes before this transfer read.
        var layers = new ImageSubresourceLayers(ImageAspectFlags.ColorBit, 0, 0, 1);
        var region = new ImageCopy(layers, new Offset3D(0, 0, 0), layers, new Offset3D(0, 0, 0), new Extent3D(_width, _height, 1));
        _vk.CmdCopyImage(cmd, _colorImage, ImageLayout.TransferSrcOptimal, target, ImageLayout.TransferDstOptimal, 1, in region);

        var toAttachment = toTransfer with
        {
            SrcAccessMask = AccessFlags.TransferWriteBit,
            DstAccessMask = AccessFlags.ColorAttachmentWriteBit | AccessFlags.ColorAttachmentReadBit,
            OldLayout = ImageLayout.TransferDstOptimal,
            NewLayout = ImageLayout.ColorAttachmentOptimal,
        };
        _vk.CmdPipelineBarrier(cmd, PipelineStageFlags.TransferBit, PipelineStageFlags.ColorAttachmentOutputBit, 0, 0, null, 0, null, 1, in toAttachment);
    }
}
