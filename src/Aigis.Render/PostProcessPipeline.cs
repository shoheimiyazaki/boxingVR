using System.Runtime.InteropServices;
using Silk.NET.Core.Native;
using Silk.NET.Vulkan;

namespace Aigis.Render;

/// <summary>
/// The miniature finishing pass (<see cref="PostProcessSettings"/>), owned in ONE place and composed
/// by BOTH renderers for the reason <see cref="WorldDecalPipeline"/> gives: render-pass dependencies,
/// image layouts and descriptor lifetime are where two hand-maintained copies drift silently, and
/// these renderers run without validation layers.
/// <para>
/// <b>Shape of a post-processed frame.</b> The caller begins <see cref="SceneRenderPass"/> on
/// <see cref="SceneFramebuffer"/> instead of its own pass and records the 3D scene there; that pass
/// leaves the colour in <see cref="ImageLayout.ShaderReadOnlyOptimal"/>. The caller then begins its
/// ORDINARY pass on its ordinary target, records <see cref="Draw"/> first (a fullscreen triangle that
/// overwrites every pixel, so the ordinary clear costs nothing visible), and the HUD after it.
/// Between the two passes, outside both, it records <see cref="RecordOcclusion(CommandBuffer, in PostProcessSettings, System.Numerics.Matrix4x4, System.Numerics.Vector4?)"/>:
/// the ambient occlusion's own half-resolution pass, which <see cref="Draw"/> reads.
/// </para>
/// <para>
/// <b>Why every existing pipeline still works in the scene pass.</b> <see cref="SceneRenderPass"/> is
/// render-pass-COMPATIBLE with the caller's: same attachment count, formats and sample counts, which
/// is all Vulkan compatibility asks. Only load/store ops, layouts and dependencies differ. So no
/// primitive, decal, particle or line pipeline is rebuilt.
/// </para>
/// <para>
/// <b>The scene pass has its OWN depth image</b>, kept and sampled by <see cref="Draw"/> for the
/// screen-space ambient occlusion (<see cref="PostProcessSettings.AmbientOcclusion"/>). It cannot be
/// the caller's depth: the ordinary pass re-attaches and clears that one while the post triangle would
/// be reading it, which is a feedback loop. Same format as the caller's, so compatibility holds.
/// </para>
/// <para>
/// The target is (re)built whenever the size changes, after a device idle — rare enough (a window
/// resize) that an idle is the cheap, obviously correct synchronisation. The cost is one colour and
/// one D32 image at the target size plus one R8 image at a quarter of it (about 17 MB together at
/// 1080p).
/// </para>
/// </summary>
internal sealed unsafe class PostProcessPipeline : IDisposable
{
    /// <summary>The height <see cref="PostProcessSettings.MaxBlurPixels"/> and
    /// <see cref="PostProcessSettings.BloomRadiusPixels"/> are authored at.</summary>
    private const float ReferenceHeight = 1080f;

    [StructLayout(LayoutKind.Sequential)]
    private struct PostProcessPush
    {
        public System.Numerics.Vector4 Focus;
        public System.Numerics.Vector4 Bloom;
        public System.Numerics.Vector4 Grade;

        // This draw's own rectangle, as two unorm16 (x, y) pairs: a tap inside the target but outside
        // it is another view's and is skipped (see PostProcessSource's Foreign).
        public uint SampleMin;
        public uint SampleMax;
        public float HeightScale;
        public float AmbientOcclusion;

        // Rebuilds a world position from (uv, depth) for the ambient occlusion. Takes the block to
        // exactly 128 bytes, Vulkan's guaranteed push-constant minimum: there is no room left.
        public System.Numerics.Matrix4x4 InverseViewProjection;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct OcclusionPush
    {
        public uint SampleMin;
        public uint SampleMax;
        public float HeightScale;
        public float AmbientOcclusion;
        public System.Numerics.Matrix4x4 InverseViewProjection;
    }

    /// <summary>The occlusion target's format: one channel, and 8 bits is far finer than the noise
    /// of 24 taps.</summary>
    private const Format OcclusionFormat = Format.R8Unorm;

    private static readonly System.Numerics.Vector4 WholeTarget = new(0f, 0f, 1f, 1f);

    private readonly Vk _vk;
    private readonly PhysicalDevice _physicalDevice;
    private readonly Device _device;

    private Format _colorFormat;
    private Format _depthFormat;
    private uint _width;
    private uint _height;
    private Image _depthImage;
    private DeviceMemory _depthMemory;
    private ImageView _depthView;
    private Sampler _depthSampler;

    private Image _sceneImage;
    private DeviceMemory _sceneMemory;
    private ImageView _sceneView;
    private RenderPass _sceneRenderPass;
    private Framebuffer _sceneFramebuffer;

    private Sampler _sampler;
    private DescriptorSetLayout _setLayout;
    private DescriptorPool _descriptorPool;
    private DescriptorSet _set;

    private Pipeline _pipeline;
    private PipelineLayout _pipelineLayout;
    private ShaderModule _vert;
    private ShaderModule _frag;

    private Image _occlusionImage;
    private DeviceMemory _occlusionMemory;
    private ImageView _occlusionView;
    private Framebuffer _occlusionFramebuffer;
    private RenderPass _occlusionRenderPass;
    private DescriptorSetLayout _occlusionSetLayout;
    private DescriptorSet _occlusionSet;
    private Pipeline _occlusionPipeline;
    private PipelineLayout _occlusionPipelineLayout;
    private ShaderModule _occlusionFrag;

    public PostProcessPipeline(Vk vk, PhysicalDevice physicalDevice, Device device)
    {
        _vk = vk;
        _physicalDevice = physicalDevice;
        _device = device;
    }

    /// <summary>The pass the caller records its 3D scene in, in place of its own — see the type's
    /// doc comment. Valid after <see cref="Prepare"/>.</summary>
    public RenderPass SceneRenderPass => _sceneRenderPass;

    /// <summary>The offscreen colour plus the caller's depth. Valid after <see cref="Prepare"/>.</summary>
    public Framebuffer SceneFramebuffer => _sceneFramebuffer;

    /// <summary>
    /// Builds the pipeline on first use and the offscreen target whenever its size or depth view
    /// changed. <b>Call outside any render pass.</b>
    /// </summary>
    /// <param name="mainRenderPass">The caller's ordinary pass, which <see cref="Draw"/> records
    /// into. The pipeline is created against it.</param>
    /// <param name="colorFormat">The caller's colour attachment format (the scene image copies it).</param>
    /// <param name="depthFormat">The caller's depth attachment format.</param>
    /// <param name="width">Target width in pixels.</param>
    /// <param name="height">Target height in pixels.</param>
    /// <param name="dynamicViewport">Whether the caller sets viewport/scissor dynamically (windowed)
    /// or bakes them into pipelines (headless).</param>
    public void Prepare(
        RenderPass mainRenderPass, Format colorFormat, Format depthFormat,
        uint width, uint height, bool dynamicViewport)
    {
        if (_pipeline.Handle == 0)
        {
            _colorFormat = colorFormat;
            _depthFormat = depthFormat;
            CreateSampler();
            CreateDescriptors();
            CreateSceneRenderPass();
            CreatePipeline(mainRenderPass, dynamicViewport, width, height);
            CreateOcclusionRenderPass();
            CreateOcclusionPipeline();
        }
        else if (colorFormat != _colorFormat || depthFormat != _depthFormat)
        {
            throw new InvalidOperationException(
                $"{nameof(PostProcessPipeline)} was built for {_colorFormat}/{_depthFormat} and is now asked for " +
                $"{colorFormat}/{depthFormat}; the scene pass would no longer be compatible with the caller's pipelines.");
        }

        if (width == _width && height == _height && _sceneImage.Handle != 0)
        {
            return;
        }

        if (_sceneImage.Handle != 0)
        {
            _vk.DeviceWaitIdle(_device);
            DestroyTarget();
        }

        _width = width;
        _height = height;
        CreateTarget();
    }

    /// <summary>The occlusion target's size: half the scene's, rounded up, so every scene pixel has a
    /// texel at or before it.</summary>
    private uint OcclusionWidth => (_width + 1) / 2;

    private uint OcclusionHeight => (_height + 1) / 2;

    /// <summary>
    /// Records the ambient occlusion's half-resolution pass for one camera. <b>Call outside any render
    /// pass</b>, after the scene pass ended and before the ordinary pass that records <see cref="Draw"/>
    /// begins. With the settings asking for no occlusion it only clears.
    /// <para>Leaves the viewport (and, when it drew, the scissor) set to the occlusion target: a caller whose pipelines take
    /// them dynamically sets its own again before drawing.</para>
    /// </summary>
    /// <param name="cmd">The caller's command buffer, outside any pass.</param>
    /// <param name="settings">This frame's finishing pass — the same value <see cref="Draw"/> gets.</param>
    /// <param name="viewProjection">The scene's camera — the same one <see cref="Draw"/> gets.</param>
    /// <param name="sampleRect">This view's uv rectangle, as <see cref="Draw"/> takes it; null is the
    /// whole target.</param>
    public void RecordOcclusion(
        CommandBuffer cmd, in PostProcessSettings settings, System.Numerics.Matrix4x4 viewProjection,
        System.Numerics.Vector4? sampleRect = null)
    {
        ReadOnlySpan<System.Numerics.Matrix4x4> cameras = [viewProjection];
        ReadOnlySpan<System.Numerics.Vector4> rects = [sampleRect ?? WholeTarget];
        RecordOcclusion(cmd, settings, cameras, rects);
    }

    /// <summary>
    /// The several-view form: each view's camera and the uv rectangle it owns — the same pairs its
    /// <see cref="Draw"/> calls receive. One render pass; each view draws scissored to its own
    /// rectangle, and anything no view covers stays "unoccluded".
    /// </summary>
    public void RecordOcclusion(
        CommandBuffer cmd, in PostProcessSettings settings,
        ReadOnlySpan<System.Numerics.Matrix4x4> viewProjections, ReadOnlySpan<System.Numerics.Vector4> sampleRects)
    {
        if (viewProjections.Length != sampleRects.Length)
        {
            throw new ArgumentException(
                $"{viewProjections.Length} cameras but {sampleRects.Length} sample rectangles; they are per view.", nameof(sampleRects));
        }

        // With the occlusion off the pass still runs, as a clear: the finishing pass's descriptor names
        // this image ShaderReadOnlyOptimal, and an image no pass has written is still UNDEFINED (one
        // half-resolution clear is the price of never leaving it so).
        var clear = new ClearValue { Color = new ClearColorValue(1f, 1f, 1f, 1f) };
        var extent = new Extent2D(OcclusionWidth, OcclusionHeight);
        var begin = new RenderPassBeginInfo
        {
            SType = StructureType.RenderPassBeginInfo,
            RenderPass = _occlusionRenderPass,
            Framebuffer = _occlusionFramebuffer,
            RenderArea = new Rect2D(new Offset2D(0, 0), extent),
            ClearValueCount = 1,
            PClearValues = &clear,
        };
        _vk.CmdBeginRenderPass(cmd, in begin, SubpassContents.Inline);
        _vk.CmdBindPipeline(cmd, PipelineBindPoint.Graphics, _occlusionPipeline);
        var set = _occlusionSet;
        _vk.CmdBindDescriptorSets(cmd, PipelineBindPoint.Graphics, _occlusionPipelineLayout, 0, 1, &set, 0, null);
        var viewport = new Viewport { X = 0, Y = 0, Width = extent.Width, Height = extent.Height, MinDepth = 0, MaxDepth = 1 };
        _vk.CmdSetViewport(cmd, 0, 1, &viewport);
        for (var i = 0; i < viewProjections.Length && settings.AmbientOcclusion > 0f; i++)
        {
            // A degenerate camera leaves its rectangle at the clear ("unoccluded"); Draw turns the
            // occlusion off for that view as well.
            if (!System.Numerics.Matrix4x4.Invert(viewProjections[i], out var inverse))
            {
                continue;
            }

            // Exactly the texels whose scene pixel (2q) lies in this view: rounded from the view's
            // FULL-resolution edges, so a view edge on an odd pixel does not hand the texel on the seam
            // to the neighbour drawn after it.
            var rect = sampleRects[i];
            var x0 = (int)Math.Clamp(MathF.Ceiling(MathF.Round(rect.X * _width) * 0.5f), 0f, extent.Width);
            var y0 = (int)Math.Clamp(MathF.Ceiling(MathF.Round(rect.Y * _height) * 0.5f), 0f, extent.Height);
            var x1 = (int)Math.Clamp(MathF.Ceiling(MathF.Round(rect.Z * _width) * 0.5f), 0f, extent.Width);
            var y1 = (int)Math.Clamp(MathF.Ceiling(MathF.Round(rect.W * _height) * 0.5f), 0f, extent.Height);
            if (x1 <= x0 || y1 <= y0)
            {
                continue;
            }

            var scissor = new Rect2D(new Offset2D(x0, y0), new Extent2D((uint)(x1 - x0), (uint)(y1 - y0)));
            _vk.CmdSetScissor(cmd, 0, 1, &scissor);
            var push = new OcclusionPush
            {
                SampleMin = PackUnorm16Pair(rect.X, rect.Y),
                SampleMax = PackUnorm16Pair(rect.Z, rect.W),
                HeightScale = _height / ReferenceHeight,
                AmbientOcclusion = settings.AmbientOcclusion,
                InverseViewProjection = inverse,
            };
            _vk.CmdPushConstants(cmd, _occlusionPipelineLayout, ShaderStageFlags.FragmentBit, 0, (uint)sizeof(OcclusionPush), &push);
            _vk.CmdDraw(cmd, 3, 1, 0, 0);
        }

        _vk.CmdEndRenderPass(cmd);
    }

    /// <summary>Records the fullscreen pass. Must be inside the caller's ordinary render pass, with
    /// its viewport set when <c>dynamicViewport</c> was passed to <see cref="Prepare"/>.</summary>
    /// <param name="cmd">The caller's command buffer, inside its ordinary pass.</param>
    /// <param name="settings">This frame's finishing pass.</param>
    /// <param name="viewProjection">The scene's camera, inverted here so the ambient occlusion can
    /// rebuild world positions from depth.</param>
    /// <param name="sampleRect">The uv rectangle (min x, min y, max x, max y; top-left origin) that is
    /// this draw's own picture. Null is the whole target. A split-screen view whose camera is its own
    /// passes its rectangle, and blur, bloom and occlusion taps that land in a neighbour drawn from
    /// another camera are skipped.</param>
    public void Draw(
        CommandBuffer cmd, in PostProcessSettings settings, System.Numerics.Matrix4x4 viewProjection,
        System.Numerics.Vector4? sampleRect = null)
    {
        _vk.CmdBindPipeline(cmd, PipelineBindPoint.Graphics, _pipeline);
        var set = _set;
        _vk.CmdBindDescriptorSets(cmd, PipelineBindPoint.Graphics, _pipelineLayout, 0, 1, &set, 0, null);
        var invertible = System.Numerics.Matrix4x4.Invert(viewProjection, out var inverse);
        var rect = sampleRect ?? WholeTarget;
        var push = new PostProcessPush
        {
            Focus = new(settings.FocusCenter, settings.FocusHalfHeight, settings.FocusFalloff, settings.MaxBlurPixels),
            Bloom = new(settings.BloomThreshold, settings.BloomIntensity, settings.BloomRadiusPixels, settings.Vignette),
            Grade = new(settings.Saturation, settings.Contrast, settings.Warmth, settings.Exposure),
            SampleMin = PackUnorm16Pair(rect.X, rect.Y),
            SampleMax = PackUnorm16Pair(rect.Z, rect.W),
            HeightScale = _height / ReferenceHeight,
            // A singular matrix (degenerate camera) turns the occlusion OFF (strength 0, which the shader
            // checks first) — a zero inverse alone would rebuild NaN positions and blank the frame.
            AmbientOcclusion = invertible ? settings.AmbientOcclusion : 0f,
            InverseViewProjection = invertible ? inverse : default,
        };
        _vk.CmdPushConstants(cmd, _pipelineLayout, ShaderStageFlags.FragmentBit, 0, (uint)sizeof(PostProcessPush), &push);
        _vk.CmdDraw(cmd, 3, 1, 0, 0);
    }

    /// <summary>GLSL's <c>packUnorm2x16</c>: <paramref name="x"/> in the low half.</summary>
    private static uint PackUnorm16Pair(float x, float y) =>
        (uint)MathF.Round(Math.Clamp(x, 0f, 1f) * 65535f) | ((uint)MathF.Round(Math.Clamp(y, 0f, 1f) * 65535f) << 16);

    /// <summary>Drops the offscreen target so the next <see cref="Prepare"/> rebuilds it. The caller
    /// guarantees the device is idle (the windowed renderer calls this mid swapchain recreation).</summary>
    public void ReleaseTarget()
    {
        DestroyTarget();
    }

    // --- target -------------------------------------------------------------------------------

    private void CreateTarget()
    {
        var imageInfo = new ImageCreateInfo
        {
            SType = StructureType.ImageCreateInfo,
            ImageType = ImageType.Type2D,
            Format = _colorFormat,
            Extent = new Extent3D(_width, _height, 1),
            MipLevels = 1,
            ArrayLayers = 1,
            Samples = SampleCountFlags.Count1Bit,
            Tiling = ImageTiling.Optimal,
            Usage = ImageUsageFlags.ColorAttachmentBit | ImageUsageFlags.SampledBit,
            SharingMode = SharingMode.Exclusive,
            InitialLayout = ImageLayout.Undefined,
        };
        Check(_vk.CreateImage(_device, in imageInfo, null, out _sceneImage), "CreateImage(postScene)");
        _vk.GetImageMemoryRequirements(_device, _sceneImage, out var req);
        _sceneMemory = AllocateMemory(req, MemoryPropertyFlags.DeviceLocalBit);
        Check(_vk.BindImageMemory(_device, _sceneImage, _sceneMemory, 0), "BindImageMemory(postScene)");

        var viewInfo = new ImageViewCreateInfo
        {
            SType = StructureType.ImageViewCreateInfo,
            Image = _sceneImage,
            ViewType = ImageViewType.Type2D,
            Format = _colorFormat,
            SubresourceRange = new ImageSubresourceRange(ImageAspectFlags.ColorBit, 0, 1, 0, 1),
        };
        Check(_vk.CreateImageView(_device, in viewInfo, null, out _sceneView), "CreateImageView(postScene)");

        CreateDepth();
        var views = stackalloc ImageView[2] { _sceneView, _depthView };
        var framebufferInfo = new FramebufferCreateInfo
        {
            SType = StructureType.FramebufferCreateInfo,
            RenderPass = _sceneRenderPass,
            AttachmentCount = 2,
            PAttachments = views,
            Width = _width,
            Height = _height,
            Layers = 1,
        };
        Check(_vk.CreateFramebuffer(_device, in framebufferInfo, null, out _sceneFramebuffer), "CreateFramebuffer(postScene)");

        CreateOcclusionTarget();

        // The finishing pass's three (scene, depth, occlusion), then the occlusion pass's one (depth).
        // The occlusion is read by texelFetch, so its sampler never filters; the nearest one that
        // already exists for the depth serves.
        var imageDescriptors = stackalloc DescriptorImageInfo[4];
        imageDescriptors[0] = new DescriptorImageInfo { Sampler = _sampler, ImageView = _sceneView, ImageLayout = ImageLayout.ShaderReadOnlyOptimal };
        imageDescriptors[1] = new DescriptorImageInfo { Sampler = _depthSampler, ImageView = _depthView, ImageLayout = ImageLayout.DepthStencilReadOnlyOptimal };
        imageDescriptors[2] = new DescriptorImageInfo { Sampler = _depthSampler, ImageView = _occlusionView, ImageLayout = ImageLayout.ShaderReadOnlyOptimal };
        imageDescriptors[3] = imageDescriptors[1];
        var writes = stackalloc WriteDescriptorSet[4];
        for (var i = 0; i < 4; i++)
        {
            writes[i] = new WriteDescriptorSet
            {
                SType = StructureType.WriteDescriptorSet,
                DstSet = i < 3 ? _set : _occlusionSet,
                DstBinding = i < 3 ? (uint)i : 0u,
                DescriptorCount = 1,
                DescriptorType = DescriptorType.CombinedImageSampler,
                PImageInfo = &imageDescriptors[i],
            };
        }

        _vk.UpdateDescriptorSets(_device, 4, writes, 0, null);
    }

    /// <summary>The half-resolution occlusion: an attachment its own pass writes, then a texture
    /// <see cref="Draw"/> reads.</summary>
    private void CreateOcclusionTarget()
    {
        var imageInfo = new ImageCreateInfo
        {
            SType = StructureType.ImageCreateInfo,
            ImageType = ImageType.Type2D,
            Format = OcclusionFormat,
            Extent = new Extent3D(OcclusionWidth, OcclusionHeight, 1),
            MipLevels = 1,
            ArrayLayers = 1,
            Samples = SampleCountFlags.Count1Bit,
            Tiling = ImageTiling.Optimal,
            Usage = ImageUsageFlags.ColorAttachmentBit | ImageUsageFlags.SampledBit,
            SharingMode = SharingMode.Exclusive,
            InitialLayout = ImageLayout.Undefined,
        };
        Check(_vk.CreateImage(_device, in imageInfo, null, out _occlusionImage), "CreateImage(postOcclusion)");
        _vk.GetImageMemoryRequirements(_device, _occlusionImage, out var req);
        _occlusionMemory = AllocateMemory(req, MemoryPropertyFlags.DeviceLocalBit);
        Check(_vk.BindImageMemory(_device, _occlusionImage, _occlusionMemory, 0), "BindImageMemory(postOcclusion)");

        var viewInfo = new ImageViewCreateInfo
        {
            SType = StructureType.ImageViewCreateInfo,
            Image = _occlusionImage,
            ViewType = ImageViewType.Type2D,
            Format = OcclusionFormat,
            SubresourceRange = new ImageSubresourceRange(ImageAspectFlags.ColorBit, 0, 1, 0, 1),
        };
        Check(_vk.CreateImageView(_device, in viewInfo, null, out _occlusionView), "CreateImageView(postOcclusion)");

        var view = _occlusionView;
        var framebufferInfo = new FramebufferCreateInfo
        {
            SType = StructureType.FramebufferCreateInfo,
            RenderPass = _occlusionRenderPass,
            AttachmentCount = 1,
            PAttachments = &view,
            Width = OcclusionWidth,
            Height = OcclusionHeight,
            Layers = 1,
        };
        Check(_vk.CreateFramebuffer(_device, in framebufferInfo, null, out _occlusionFramebuffer), "CreateFramebuffer(postOcclusion)");
    }

    /// <summary>The scene pass's own depth: an attachment it stores, then a texture the post pass
    /// reads — see the type's doc comment for why it cannot be the caller's.</summary>
    private void CreateDepth()
    {
        var imageInfo = new ImageCreateInfo
        {
            SType = StructureType.ImageCreateInfo,
            ImageType = ImageType.Type2D,
            Format = _depthFormat,
            Extent = new Extent3D(_width, _height, 1),
            MipLevels = 1,
            ArrayLayers = 1,
            Samples = SampleCountFlags.Count1Bit,
            Tiling = ImageTiling.Optimal,
            Usage = ImageUsageFlags.DepthStencilAttachmentBit | ImageUsageFlags.SampledBit,
            SharingMode = SharingMode.Exclusive,
            InitialLayout = ImageLayout.Undefined,
        };
        Check(_vk.CreateImage(_device, in imageInfo, null, out _depthImage), "CreateImage(postDepth)");
        _vk.GetImageMemoryRequirements(_device, _depthImage, out var req);
        _depthMemory = AllocateMemory(req, MemoryPropertyFlags.DeviceLocalBit);
        Check(_vk.BindImageMemory(_device, _depthImage, _depthMemory, 0), "BindImageMemory(postDepth)");

        var viewInfo = new ImageViewCreateInfo
        {
            SType = StructureType.ImageViewCreateInfo,
            Image = _depthImage,
            ViewType = ImageViewType.Type2D,
            Format = _depthFormat,
            SubresourceRange = new ImageSubresourceRange(ImageAspectFlags.DepthBit, 0, 1, 0, 1),
        };
        Check(_vk.CreateImageView(_device, in viewInfo, null, out _depthView), "CreateImageView(postDepth)");
    }

    private void DestroyTarget()
    {
        if (_occlusionFramebuffer.Handle != 0)
        {
            _vk.DestroyFramebuffer(_device, _occlusionFramebuffer, null);
            _occlusionFramebuffer = default;
        }

        if (_occlusionView.Handle != 0)
        {
            _vk.DestroyImageView(_device, _occlusionView, null);
            _occlusionView = default;
        }

        if (_occlusionImage.Handle != 0)
        {
            _vk.DestroyImage(_device, _occlusionImage, null);
            _occlusionImage = default;
        }

        if (_occlusionMemory.Handle != 0)
        {
            _vk.FreeMemory(_device, _occlusionMemory, null);
            _occlusionMemory = default;
        }

        if (_sceneFramebuffer.Handle != 0)
        {
            _vk.DestroyFramebuffer(_device, _sceneFramebuffer, null);
            _sceneFramebuffer = default;
        }

        if (_sceneView.Handle != 0)
        {
            _vk.DestroyImageView(_device, _sceneView, null);
            _sceneView = default;
        }

        if (_sceneImage.Handle != 0)
        {
            _vk.DestroyImage(_device, _sceneImage, null);
            _sceneImage = default;
        }

        if (_sceneMemory.Handle != 0)
        {
            _vk.FreeMemory(_device, _sceneMemory, null);
            _sceneMemory = default;
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
    }

    // --- pipeline -----------------------------------------------------------------------------

    /// <summary>
    /// Compatible with the caller's pass (same formats), differing in what happens at its edges:
    /// colour ends <see cref="ImageLayout.ShaderReadOnlyOptimal"/> for <see cref="Draw"/> to sample.
    /// <para>The two dependencies order (1) the previous frame's post-pass READ of this image before
    /// this frame's scene writes it, and (2) this pass's colour and depth writes before the ordinary
    /// pass samples the colour and clears the shared depth.</para>
    /// </summary>
    private void CreateSceneRenderPass()
    {
        var attachments = stackalloc AttachmentDescription[2];
        attachments[0] = new AttachmentDescription
        {
            Format = _colorFormat,
            Samples = SampleCountFlags.Count1Bit,
            LoadOp = AttachmentLoadOp.Clear,
            StoreOp = AttachmentStoreOp.Store,
            StencilLoadOp = AttachmentLoadOp.DontCare,
            StencilStoreOp = AttachmentStoreOp.DontCare,
            InitialLayout = ImageLayout.Undefined,
            FinalLayout = ImageLayout.ShaderReadOnlyOptimal,
        };
        attachments[1] = new AttachmentDescription
        {
            Format = _depthFormat,
            Samples = SampleCountFlags.Count1Bit,
            LoadOp = AttachmentLoadOp.Clear,
            StoreOp = AttachmentStoreOp.Store,
            StencilLoadOp = AttachmentLoadOp.DontCare,
            StencilStoreOp = AttachmentStoreOp.DontCare,
            InitialLayout = ImageLayout.Undefined,
            FinalLayout = ImageLayout.DepthStencilReadOnlyOptimal,
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

        const PipelineStageFlags attachmentStages = PipelineStageFlags.ColorAttachmentOutputBit
            | PipelineStageFlags.EarlyFragmentTestsBit | PipelineStageFlags.LateFragmentTestsBit;
        const AccessFlags attachmentWrites = AccessFlags.ColorAttachmentWriteBit | AccessFlags.DepthStencilAttachmentWriteBit;
        var dependencies = stackalloc SubpassDependency[2];
        dependencies[0] = new SubpassDependency
        {
            SrcSubpass = Vk.SubpassExternal,
            DstSubpass = 0,
            SrcStageMask = PipelineStageFlags.FragmentShaderBit | attachmentStages,
            DstStageMask = attachmentStages,
            SrcAccessMask = attachmentWrites,
            DstAccessMask = attachmentWrites | AccessFlags.ColorAttachmentReadBit | AccessFlags.DepthStencilAttachmentReadBit,
        };
        dependencies[1] = new SubpassDependency
        {
            SrcSubpass = 0,
            DstSubpass = Vk.SubpassExternal,
            SrcStageMask = attachmentStages,
            DstStageMask = PipelineStageFlags.FragmentShaderBit | attachmentStages,
            SrcAccessMask = attachmentWrites,
            DstAccessMask = AccessFlags.ShaderReadBit | attachmentWrites | AccessFlags.DepthStencilAttachmentReadBit,
        };

        var info = new RenderPassCreateInfo
        {
            SType = StructureType.RenderPassCreateInfo,
            AttachmentCount = 2,
            PAttachments = attachments,
            SubpassCount = 1,
            PSubpasses = &subpass,
            DependencyCount = 2,
            PDependencies = dependencies,
        };
        Check(_vk.CreateRenderPass(_device, in info, null, out _sceneRenderPass), "CreateRenderPass(postScene)");
    }

    /// <summary>
    /// The occlusion's pass: cleared to 1 ("unoccluded"), ends <see cref="ImageLayout.ShaderReadOnlyOptimal"/>
    /// for <see cref="Draw"/>. Its depth input needs no dependency of its own — the scene pass's
    /// outgoing one already orders the depth writes before every later fragment-shader read.
    /// <para>The two dependencies order (1) the previous frame's <see cref="Draw"/> READ of this image
    /// before this frame's clear and writes, and (2) those writes before this frame's read.</para>
    /// </summary>
    private void CreateOcclusionRenderPass()
    {
        var attachment = new AttachmentDescription
        {
            Format = OcclusionFormat,
            Samples = SampleCountFlags.Count1Bit,
            LoadOp = AttachmentLoadOp.Clear,
            StoreOp = AttachmentStoreOp.Store,
            StencilLoadOp = AttachmentLoadOp.DontCare,
            StencilStoreOp = AttachmentStoreOp.DontCare,
            InitialLayout = ImageLayout.Undefined,
            FinalLayout = ImageLayout.ShaderReadOnlyOptimal,
        };
        var colorRef = new AttachmentReference(0, ImageLayout.ColorAttachmentOptimal);
        var subpass = new SubpassDescription
        {
            PipelineBindPoint = PipelineBindPoint.Graphics,
            ColorAttachmentCount = 1,
            PColorAttachments = &colorRef,
        };
        var dependencies = stackalloc SubpassDependency[2];
        dependencies[0] = new SubpassDependency
        {
            SrcSubpass = Vk.SubpassExternal,
            DstSubpass = 0,
            SrcStageMask = PipelineStageFlags.FragmentShaderBit,
            DstStageMask = PipelineStageFlags.ColorAttachmentOutputBit,
            SrcAccessMask = 0,
            DstAccessMask = AccessFlags.ColorAttachmentWriteBit,
        };
        dependencies[1] = new SubpassDependency
        {
            SrcSubpass = 0,
            DstSubpass = Vk.SubpassExternal,
            SrcStageMask = PipelineStageFlags.ColorAttachmentOutputBit,
            DstStageMask = PipelineStageFlags.FragmentShaderBit,
            SrcAccessMask = AccessFlags.ColorAttachmentWriteBit,
            DstAccessMask = AccessFlags.ShaderReadBit,
        };
        var info = new RenderPassCreateInfo
        {
            SType = StructureType.RenderPassCreateInfo,
            AttachmentCount = 1,
            PAttachments = &attachment,
            SubpassCount = 1,
            PSubpasses = &subpass,
            DependencyCount = 2,
            PDependencies = dependencies,
        };
        Check(_vk.CreateRenderPass(_device, in info, null, out _occlusionRenderPass), "CreateRenderPass(postOcclusion)");
    }

    /// <summary>The occlusion's fullscreen triangle: the finishing pass's vertex stage, its own
    /// fragment stage. Viewport and scissor are dynamic in headless and windowed alike, because the
    /// several-view <c>RecordOcclusion</c> scissors each view to its own rectangle.</summary>
    private void CreateOcclusionPipeline()
    {
        _occlusionFrag = CreateShaderModule(PrecompiledShaders.PostProcessOcclusionFragSpirv);
        var entry = (byte*)SilkMarshal.StringToPtr("main");
        try
        {
            var stages = stackalloc PipelineShaderStageCreateInfo[2];
            stages[0] = new PipelineShaderStageCreateInfo { SType = StructureType.PipelineShaderStageCreateInfo, Stage = ShaderStageFlags.VertexBit, Module = _vert, PName = entry };
            stages[1] = new PipelineShaderStageCreateInfo { SType = StructureType.PipelineShaderStageCreateInfo, Stage = ShaderStageFlags.FragmentBit, Module = _occlusionFrag, PName = entry };

            var vertexInput = new PipelineVertexInputStateCreateInfo { SType = StructureType.PipelineVertexInputStateCreateInfo };
            var inputAssembly = new PipelineInputAssemblyStateCreateInfo { SType = StructureType.PipelineInputAssemblyStateCreateInfo, Topology = PrimitiveTopology.TriangleList };
            var viewportState = new PipelineViewportStateCreateInfo { SType = StructureType.PipelineViewportStateCreateInfo, ViewportCount = 1, ScissorCount = 1 };
            var dynamicStates = stackalloc DynamicState[2] { DynamicState.Viewport, DynamicState.Scissor };
            var dynamicState = new PipelineDynamicStateCreateInfo { SType = StructureType.PipelineDynamicStateCreateInfo, DynamicStateCount = 2, PDynamicStates = dynamicStates };
            var rasterizer = new PipelineRasterizationStateCreateInfo { SType = StructureType.PipelineRasterizationStateCreateInfo, PolygonMode = PolygonMode.Fill, CullMode = CullModeFlags.None, FrontFace = FrontFace.CounterClockwise, LineWidth = 1f };
            var multisample = new PipelineMultisampleStateCreateInfo { SType = StructureType.PipelineMultisampleStateCreateInfo, RasterizationSamples = SampleCountFlags.Count1Bit, MinSampleShading = 1f };
            var depthStencil = new PipelineDepthStencilStateCreateInfo { SType = StructureType.PipelineDepthStencilStateCreateInfo };
            var blendAttachment = new PipelineColorBlendAttachmentState { ColorWriteMask = ColorComponentFlags.RBit, BlendEnable = false };
            var colorBlend = new PipelineColorBlendStateCreateInfo { SType = StructureType.PipelineColorBlendStateCreateInfo, AttachmentCount = 1, PAttachments = &blendAttachment };

            var pushRange = new PushConstantRange { StageFlags = ShaderStageFlags.FragmentBit, Offset = 0, Size = (uint)sizeof(OcclusionPush) };
            var setLayout = _occlusionSetLayout;
            var layoutInfo = new PipelineLayoutCreateInfo
            {
                SType = StructureType.PipelineLayoutCreateInfo,
                SetLayoutCount = 1,
                PSetLayouts = &setLayout,
                PushConstantRangeCount = 1,
                PPushConstantRanges = &pushRange,
            };
            Check(_vk.CreatePipelineLayout(_device, in layoutInfo, null, out _occlusionPipelineLayout), "CreatePipelineLayout(postOcclusion)");

            var pipelineInfo = new GraphicsPipelineCreateInfo
            {
                SType = StructureType.GraphicsPipelineCreateInfo,
                StageCount = 2,
                PStages = stages,
                PVertexInputState = &vertexInput,
                PInputAssemblyState = &inputAssembly,
                PViewportState = &viewportState,
                PRasterizationState = &rasterizer,
                PMultisampleState = &multisample,
                PDepthStencilState = &depthStencil,
                PColorBlendState = &colorBlend,
                PDynamicState = &dynamicState,
                Layout = _occlusionPipelineLayout,
                RenderPass = _occlusionRenderPass,
                Subpass = 0,
            };
            Check(_vk.CreateGraphicsPipelines(_device, default, 1, in pipelineInfo, null, out _occlusionPipeline), "CreateGraphicsPipelines(postOcclusion)");
        }
        finally
        {
            SilkMarshal.Free((nint)entry);
        }
    }

    private void CreatePipeline(RenderPass mainRenderPass, bool dynamicViewport, uint width, uint height)
    {
        _vert = CreateShaderModule(PrecompiledShaders.PostProcessVertSpirv);
        _frag = CreateShaderModule(PrecompiledShaders.PostProcessFragSpirv);

        var entry = (byte*)SilkMarshal.StringToPtr("main");
        try
        {
            var stages = stackalloc PipelineShaderStageCreateInfo[2];
            stages[0] = new PipelineShaderStageCreateInfo { SType = StructureType.PipelineShaderStageCreateInfo, Stage = ShaderStageFlags.VertexBit, Module = _vert, PName = entry };
            stages[1] = new PipelineShaderStageCreateInfo { SType = StructureType.PipelineShaderStageCreateInfo, Stage = ShaderStageFlags.FragmentBit, Module = _frag, PName = entry };

            var vertexInput = new PipelineVertexInputStateCreateInfo { SType = StructureType.PipelineVertexInputStateCreateInfo };
            var inputAssembly = new PipelineInputAssemblyStateCreateInfo { SType = StructureType.PipelineInputAssemblyStateCreateInfo, Topology = PrimitiveTopology.TriangleList };

            var viewport = new Viewport { X = 0, Y = 0, Width = width, Height = height, MinDepth = 0, MaxDepth = 1 };
            var scissor = new Rect2D(new Offset2D(0, 0), new Extent2D(width, height));
            var viewportState = dynamicViewport
                ? new PipelineViewportStateCreateInfo { SType = StructureType.PipelineViewportStateCreateInfo, ViewportCount = 1, ScissorCount = 1 }
                : new PipelineViewportStateCreateInfo { SType = StructureType.PipelineViewportStateCreateInfo, ViewportCount = 1, PViewports = &viewport, ScissorCount = 1, PScissors = &scissor };
            var dynamicStates = stackalloc DynamicState[2] { DynamicState.Viewport, DynamicState.Scissor };
            var dynamicState = new PipelineDynamicStateCreateInfo { SType = StructureType.PipelineDynamicStateCreateInfo, DynamicStateCount = 2, PDynamicStates = dynamicStates };

            var rasterizer = new PipelineRasterizationStateCreateInfo { SType = StructureType.PipelineRasterizationStateCreateInfo, PolygonMode = PolygonMode.Fill, CullMode = CullModeFlags.None, FrontFace = FrontFace.CounterClockwise, LineWidth = 1f };
            var multisample = new PipelineMultisampleStateCreateInfo { SType = StructureType.PipelineMultisampleStateCreateInfo, RasterizationSamples = SampleCountFlags.Count1Bit, MinSampleShading = 1f };
            // No depth test or write: the triangle replaces every pixel, and the HUD after it does not
            // depth-test against the scene.
            var depthStencil = new PipelineDepthStencilStateCreateInfo { SType = StructureType.PipelineDepthStencilStateCreateInfo };
            var blendAttachment = new PipelineColorBlendAttachmentState
            {
                ColorWriteMask = ColorComponentFlags.RBit | ColorComponentFlags.GBit | ColorComponentFlags.BBit | ColorComponentFlags.ABit,
                BlendEnable = false,
            };
            var colorBlend = new PipelineColorBlendStateCreateInfo { SType = StructureType.PipelineColorBlendStateCreateInfo, AttachmentCount = 1, PAttachments = &blendAttachment };

            var pushRange = new PushConstantRange { StageFlags = ShaderStageFlags.FragmentBit, Offset = 0, Size = (uint)sizeof(PostProcessPush) };
            var setLayout = _setLayout;
            var layoutInfo = new PipelineLayoutCreateInfo
            {
                SType = StructureType.PipelineLayoutCreateInfo,
                SetLayoutCount = 1,
                PSetLayouts = &setLayout,
                PushConstantRangeCount = 1,
                PPushConstantRanges = &pushRange,
            };
            Check(_vk.CreatePipelineLayout(_device, in layoutInfo, null, out _pipelineLayout), "CreatePipelineLayout(post)");

            var pipelineInfo = new GraphicsPipelineCreateInfo
            {
                SType = StructureType.GraphicsPipelineCreateInfo,
                StageCount = 2,
                PStages = stages,
                PVertexInputState = &vertexInput,
                PInputAssemblyState = &inputAssembly,
                PViewportState = &viewportState,
                PRasterizationState = &rasterizer,
                PMultisampleState = &multisample,
                PDepthStencilState = &depthStencil,
                PColorBlendState = &colorBlend,
                PDynamicState = dynamicViewport ? &dynamicState : null,
                Layout = _pipelineLayout,
                RenderPass = mainRenderPass,
                Subpass = 0,
            };
            Check(_vk.CreateGraphicsPipelines(_device, default, 1, in pipelineInfo, null, out _pipeline), "CreateGraphicsPipelines(post)");
        }
        finally
        {
            SilkMarshal.Free((nint)entry);
        }
    }

    /// <summary>Linear and clamped: the blur taps land between texels, and a tap past the frame edge
    /// must repeat the edge rather than wrap the opposite side in.</summary>
    private void CreateSampler()
    {
        var info = new SamplerCreateInfo
        {
            SType = StructureType.SamplerCreateInfo,
            MagFilter = Filter.Linear,
            MinFilter = Filter.Linear,
            AddressModeU = SamplerAddressMode.ClampToEdge,
            AddressModeV = SamplerAddressMode.ClampToEdge,
            AddressModeW = SamplerAddressMode.ClampToEdge,
            MipmapMode = SamplerMipmapMode.Nearest,
            MaxLod = 0,
            BorderColor = BorderColor.FloatOpaqueBlack,
        };
        Check(_vk.CreateSampler(_device, in info, null, out _sampler), "CreateSampler(post)");

        // Depth is read NEAREST: linear filtering of a D32 format is an optional device feature, and
        // a blended depth between a bed's edge and the floor below it is a position that exists nowhere.
        var depthInfo = info with { MagFilter = Filter.Nearest, MinFilter = Filter.Nearest };
        Check(_vk.CreateSampler(_device, in depthInfo, null, out _depthSampler), "CreateSampler(postDepth)");
    }

    private void CreateDescriptors()
    {
        var bindings = stackalloc DescriptorSetLayoutBinding[3];
        for (var i = 0; i < 3; i++)
        {
            bindings[i] = new DescriptorSetLayoutBinding
            {
                Binding = (uint)i,
                DescriptorType = DescriptorType.CombinedImageSampler,
                DescriptorCount = 1,
                StageFlags = ShaderStageFlags.FragmentBit,
            };
        }

        var setLayoutInfo = new DescriptorSetLayoutCreateInfo { SType = StructureType.DescriptorSetLayoutCreateInfo, BindingCount = 3, PBindings = bindings };
        Check(_vk.CreateDescriptorSetLayout(_device, in setLayoutInfo, null, out _setLayout), "CreateDescriptorSetLayout(post)");

        // The occlusion pass reads the depth alone: binding 0 of the same shape.
        var occlusionLayoutInfo = setLayoutInfo with { BindingCount = 1 };
        Check(_vk.CreateDescriptorSetLayout(_device, in occlusionLayoutInfo, null, out _occlusionSetLayout), "CreateDescriptorSetLayout(postOcclusion)");

        var poolSize = new DescriptorPoolSize { Type = DescriptorType.CombinedImageSampler, DescriptorCount = 4 };
        var poolInfo = new DescriptorPoolCreateInfo { SType = StructureType.DescriptorPoolCreateInfo, PoolSizeCount = 1, PPoolSizes = &poolSize, MaxSets = 2 };
        Check(_vk.CreateDescriptorPool(_device, in poolInfo, null, out _descriptorPool), "CreateDescriptorPool(post)");

        var setLayouts = stackalloc DescriptorSetLayout[2] { _setLayout, _occlusionSetLayout };
        var sets = stackalloc DescriptorSet[2];
        var allocInfo = new DescriptorSetAllocateInfo
        {
            SType = StructureType.DescriptorSetAllocateInfo,
            DescriptorPool = _descriptorPool,
            DescriptorSetCount = 2,
            PSetLayouts = setLayouts,
        };
        Check(_vk.AllocateDescriptorSets(_device, in allocInfo, sets), "AllocateDescriptorSets(post)");
        _set = sets[0];
        _occlusionSet = sets[1];
    }

    // --- helpers ------------------------------------------------------------------------------

    private ShaderModule CreateShaderModule(uint[] spirv)
    {
        if (spirv.Length == 0)
        {
            throw new InvalidOperationException(
                "PrecompiledShaders has no SPIR-V for the post-process pass. Run `dotnet run --project sandbox/ShaderRegenerator`.");
        }

        fixed (uint* code = spirv)
        {
            var info = new ShaderModuleCreateInfo
            {
                SType = StructureType.ShaderModuleCreateInfo,
                CodeSize = (nuint)(spirv.Length * sizeof(uint)),
                PCode = code,
            };
            Check(_vk.CreateShaderModule(_device, in info, null, out var module), "CreateShaderModule(post)");
            return module;
        }
    }

    private DeviceMemory AllocateMemory(MemoryRequirements req, MemoryPropertyFlags flags)
    {
        _vk.GetPhysicalDeviceMemoryProperties(_physicalDevice, out var props);
        for (uint i = 0; i < props.MemoryTypeCount; i++)
        {
            if ((req.MemoryTypeBits & (1u << (int)i)) != 0 && (props.MemoryTypes[(int)i].PropertyFlags & flags) == flags)
            {
                var allocInfo = new MemoryAllocateInfo { SType = StructureType.MemoryAllocateInfo, AllocationSize = req.Size, MemoryTypeIndex = i };
                Check(_vk.AllocateMemory(_device, in allocInfo, null, out var memory), "AllocateMemory(post)");
                return memory;
            }
        }

        throw new InvalidOperationException($"No Vulkan memory type satisfies {flags} for the post-process target.");
    }

    private static void Check(Result result, string what)
    {
        if (result != Result.Success)
        {
            throw new InvalidOperationException($"Vulkan {what} failed: {result}");
        }
    }

    /// <summary>Reverse-of-creation teardown; every handle starts zeroed, so a renderer that never
    /// post-processed destroys nothing.</summary>
    public void Dispose()
    {
        DestroyTarget();

        if (_occlusionPipeline.Handle != 0)
        {
            _vk.DestroyPipeline(_device, _occlusionPipeline, null);
        }

        if (_occlusionPipelineLayout.Handle != 0)
        {
            _vk.DestroyPipelineLayout(_device, _occlusionPipelineLayout, null);
        }

        if (_occlusionRenderPass.Handle != 0)
        {
            _vk.DestroyRenderPass(_device, _occlusionRenderPass, null);
        }

        if (_occlusionSetLayout.Handle != 0)
        {
            _vk.DestroyDescriptorSetLayout(_device, _occlusionSetLayout, null);
        }

        if (_occlusionFrag.Handle != 0)
        {
            _vk.DestroyShaderModule(_device, _occlusionFrag, null);
        }

        if (_pipeline.Handle != 0)
        {
            _vk.DestroyPipeline(_device, _pipeline, null);
        }

        if (_pipelineLayout.Handle != 0)
        {
            _vk.DestroyPipelineLayout(_device, _pipelineLayout, null);
        }

        if (_sceneRenderPass.Handle != 0)
        {
            _vk.DestroyRenderPass(_device, _sceneRenderPass, null);
        }

        if (_descriptorPool.Handle != 0)
        {
            _vk.DestroyDescriptorPool(_device, _descriptorPool, null);
        }

        if (_setLayout.Handle != 0)
        {
            _vk.DestroyDescriptorSetLayout(_device, _setLayout, null);
        }

        if (_sampler.Handle != 0)
        {
            _vk.DestroySampler(_device, _sampler, null);
        }

        if (_depthSampler.Handle != 0)
        {
            _vk.DestroySampler(_device, _depthSampler, null);
        }

        if (_vert.Handle != 0)
        {
            _vk.DestroyShaderModule(_device, _vert, null);
        }

        if (_frag.Handle != 0)
        {
            _vk.DestroyShaderModule(_device, _frag, null);
        }
    }
}
