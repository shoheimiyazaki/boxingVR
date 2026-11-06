using System.Numerics;
using System.Runtime.InteropServices;
using Silk.NET.Core.Native;
using Silk.NET.Vulkan;

namespace Aigis.Render;

/// <summary>
/// TASK-159: the sampled-texture screen-space overlay draw — HUD icons and portraits — owned in
/// ONE place and composed by BOTH renderers (<c>HeadlessRenderer.OverlayImages.cs</c> and
/// <c>WindowedRenderer.OverlayImages.cs</c>).
/// <para>
/// <b>Why this breaks the per-renderer-partial convention.</b> Every other pipeline
/// (boxes/lines/particles/shadows/text-overlay) is duplicated verbatim across the two renderers,
/// and that is fine: those are copies of one stable, well-understood recipe. This is not. It is the
/// engine's first staging-buffer upload path, and image layout transitions + barrier stage/access
/// masks + descriptor-set lifetime are exactly the code where two independently-maintained copies
/// drift silently — the more so because these renderers run <b>without Vulkan validation layers</b>
/// (no SDK installed; see docs/architecture.md), so a wrong access mask produces a black or garbage
/// icon rather than an error message. One implementation, two composers.
/// </para>
/// <para>
/// The seam is deliberately parameters, not inheritance: the two renderers share no base class and
/// retrofitting one would be a far larger change than this task. This class receives the primitive
/// handles it needs (<see cref="Vk"/>, physical device, device, queue, command pool) and reaches
/// into nothing.
/// </para>
/// <para>
/// Hard rule 6 holds by construction: the headless renderer composes the same instance, so every
/// textured overlay is reproducible in a screenshot with no window involved.
/// </para>
/// </summary>
internal sealed unsafe class OverlayImagePipeline : IDisposable
{
    /// <summary>
    /// Ceiling on simultaneously resident textures. When a genuinely transient HUD (such as
    /// patient portraits) reaches this cap, the pipeline safely reclaims its uploads and rebuilds
    /// the current frame's set.
    /// <para>
    /// That is the intended behaviour, not a limitation left unfinished. The upload cache is keyed
    /// on <see cref="TextureData"/> instance identity, so a caller that (incorrectly) rebuilds its
    /// texture every frame would otherwise leak a GPU image per frame — invisible until VRAM ran
    /// out, hours later, far from the cause. Failing loudly on the 65th distinct texture surfaces
    /// that mistake immediately and names the fix. A host that legitimately needs more distinct
    /// icons than this over its lifetime should raise the cap deliberately.
    /// </para>
    /// </summary>
    internal const int MaxTextures = 96;

    [StructLayout(LayoutKind.Sequential)]
    private struct OverlayImagePush
    {
        public Vector4 Rect;  // scale+translate — built ONLY by OverlayRect (hard rule 4).
        public Vector4 Tint;
    }

    /// <summary>One uploaded texture's GPU-side resources. Independent of the swapchain — nothing
    /// here is sized by or derived from the render target, so a window resize must NOT recreate
    /// any of it.</summary>
    private sealed record UploadedTexture(Image Image, DeviceMemory Memory, ImageView View, DescriptorSet Set);

    private readonly Vk _vk;
    private readonly PhysicalDevice _physicalDevice;
    private readonly Device _device;
    private readonly Queue _queue;
    private readonly CommandPool _commandPool;

    /// <summary>Keyed on instance identity — see <see cref="TextureData"/>'s doc comment on why
    /// that is sound (the type is immutable) and what it asks of callers (build once, reuse).</summary>
    private readonly Dictionary<TextureData, UploadedTexture> _uploaded = new(ReferenceEqualityComparer.Instance);

    private bool _pipelineReady;
    private Pipeline _pipeline;
    private PipelineLayout _pipelineLayout;
    private ShaderModule _vert;
    private ShaderModule _frag;
    private Silk.NET.Vulkan.Buffer _quadBuffer;
    private DeviceMemory _quadMemory;
    private Sampler _sampler;
    private DescriptorSetLayout _setLayout;
    private DescriptorPool _descriptorPool;

    public OverlayImagePipeline(Vk vk, PhysicalDevice physicalDevice, Device device, Queue queue, CommandPool commandPool)
    {
        _vk = vk;
        _physicalDevice = physicalDevice;
        _device = device;
        _queue = queue;
        _commandPool = commandPool;
    }

    /// <summary>
    /// Uploads any texture in <paramref name="draws"/> not already resident, and creates the
    /// pipeline against <paramref name="renderPass"/> on first use.
    /// <para>
    /// <b>Must be called before the caller begins its render pass.</b> Uploading records
    /// <c>vkCmdCopyBufferToImage</c> and image barriers, both illegal inside a render pass — so
    /// this runs on its own one-shot command buffer, submitted and fence-waited here. Textures are
    /// uploaded once and then reused, so that wait is a first-use cost, not a per-frame stall.
    /// </para>
    /// </summary>
    /// <exception cref="InvalidOperationException">More than <see cref="MaxTextures"/> distinct
    /// textures have been uploaded — see that constant's doc comment.</exception>
    public void PrepareUploads(IReadOnlyList<OverlayImageDraw> draws, RenderPass renderPass, bool dynamicViewport, uint width, uint height)
    {
        if (draws.Count == 0)
        {
            return;
        }

        EnsurePipeline(renderPass, dynamicViewport, width, height);

        var pendingTextureCount = 0;
        var pendingTextures = new HashSet<TextureData>(ReferenceEqualityComparer.Instance);
        foreach (var draw in draws)
        {
            if (draw.Texture is not null && !_uploaded.ContainsKey(draw.Texture) && pendingTextures.Add(draw.Texture))
            {
                pendingTextureCount++;
            }
        }
        if (_uploaded.Count + pendingTextureCount > MaxTextures)
        {
            ReclaimTransientUploads();
        }

        foreach (var draw in draws)
        {
            if (draw.Texture is null)
            {
                throw new ArgumentException($"An {nameof(OverlayImageDraw)} has a null {nameof(OverlayImageDraw.Texture)}.", nameof(draws));
            }

            if (_uploaded.ContainsKey(draw.Texture))
            {
                continue;
            }

            if (_uploaded.Count >= MaxTextures)
            {
                throw new InvalidOperationException(
                    $"Refusing to upload more than {MaxTextures} distinct overlay textures. The usual cause is building a new " +
                    $"{nameof(TextureData)} every frame instead of once — the upload cache is keyed on instance identity, so a " +
                    $"per-frame instance is a new texture every frame. Hoist the {nameof(TextureData)} into a field, or raise " +
                    $"{nameof(OverlayImagePipeline)}.{nameof(MaxTextures)} if this many distinct textures is genuinely intended.");
            }

            _uploaded.Add(draw.Texture, Upload(draw.Texture));
        }
    }

    private void ReclaimTransientUploads()
    {
        Check(_vk.QueueWaitIdle(_queue), "QueueWaitIdle(overlayImageReclaim)");
        foreach (var texture in _uploaded.Values)
        {
            if (texture.View.Handle != 0)
            {
                _vk.DestroyImageView(_device, texture.View, null);
            }

            if (texture.Image.Handle != 0)
            {
                _vk.DestroyImage(_device, texture.Image, null);
            }

            if (texture.Memory.Handle != 0)
            {
                _vk.FreeMemory(_device, texture.Memory, null);
            }
        }

        _uploaded.Clear();
        _vk.DestroyDescriptorPool(_device, _descriptorPool, null);
        var poolSize = new DescriptorPoolSize { Type = DescriptorType.CombinedImageSampler, DescriptorCount = MaxTextures };
        var poolInfo = new DescriptorPoolCreateInfo
        {
            SType = StructureType.DescriptorPoolCreateInfo,
            PoolSizeCount = 1,
            PPoolSizes = &poolSize,
            MaxSets = MaxTextures,
        };
        Check(_vk.CreateDescriptorPool(_device, in poolInfo, null, out _descriptorPool), "CreateDescriptorPool(overlayImageReclaim)");
    }

    /// <summary>
    /// Records the draws. Every texture must already be resident — call
    /// <see cref="PrepareUploads"/> outside the render pass first.
    /// <paramref name="targetWidthPixels"/>/<paramref name="targetHeightPixels"/> are the render
    /// target's current size (the swapchain extent for the windowed renderer, which is what makes
    /// a HUD survive a resize).
    /// </summary>
    public void Draw(
        CommandBuffer cmd,
        IReadOnlyList<OverlayImageDraw> draws,
        float targetWidthPixels,
        float targetHeightPixels)
    {
        if (draws.Count == 0)
        {
            return;
        }

        _vk.CmdBindPipeline(cmd, PipelineBindPoint.Graphics, _pipeline);
        var buffer = _quadBuffer;
        ulong offset = 0;
        _vk.CmdBindVertexBuffers(cmd, 0, 1, &buffer, &offset);

        foreach (var draw in draws)
        {
            if (!_uploaded.TryGetValue(draw.Texture, out var texture))
            {
                throw new InvalidOperationException(
                    $"Overlay texture was never uploaded — {nameof(PrepareUploads)} must be called with the same draw list, " +
                    "outside the render pass, before recording draws.");
            }

            var set = texture.Set;
            _vk.CmdBindDescriptorSets(cmd, PipelineBindPoint.Graphics, _pipelineLayout, 0, 1, &set, 0, null);

            var push = new OverlayImagePush
            {
                // OverlayRect is the ONE place pixel space becomes NDC for every overlay path
                // (hard rule 4) — do not inline this arithmetic here.
                Rect = OverlayRect.ToNdcScaleTranslate(draw.CenterPixels, draw.SizePixels, targetWidthPixels, targetHeightPixels),
                Tint = draw.Tint,
            };
            _vk.CmdPushConstants(cmd, _pipelineLayout, ShaderStageFlags.VertexBit | ShaderStageFlags.FragmentBit, 0, (uint)sizeof(OverlayImagePush), &push);
            _vk.CmdDraw(cmd, 6, 1, 0, 0);
        }
    }

    // --- upload -------------------------------------------------------------------------------

    private UploadedTexture Upload(TextureData texture)
    {
        var size = (ulong)texture.ByteLength;

        // Staging buffer: host-visible, written by the CPU, read by the transfer stage. Destroyed
        // as soon as the upload's fence has been waited on below — it is transient by nature, and
        // keeping a pool of them would be machinery for a problem this engine does not have (icons
        // upload once, at first use).
        var stagingInfo = new BufferCreateInfo
        {
            SType = StructureType.BufferCreateInfo,
            Size = size,
            Usage = BufferUsageFlags.TransferSrcBit,
            SharingMode = SharingMode.Exclusive,
        };
        Check(_vk.CreateBuffer(_device, in stagingInfo, null, out var staging), "CreateBuffer(overlayImageStaging)");
        _vk.GetBufferMemoryRequirements(_device, staging, out var stagingReq);
        var stagingMemory = AllocateMemory(stagingReq, MemoryPropertyFlags.HostVisibleBit | MemoryPropertyFlags.HostCoherentBit);
        Check(_vk.BindBufferMemory(_device, staging, stagingMemory, 0), "BindBufferMemory(overlayImageStaging)");

        void* mapped = null;
        Check(_vk.MapMemory(_device, stagingMemory, 0, size, 0, ref mapped), "MapMemory(overlayImageStaging)");
        texture.Rgba.CopyTo(new Span<byte>(mapped, texture.ByteLength));
        _vk.UnmapMemory(_device, stagingMemory);

        var imageInfo = new ImageCreateInfo
        {
            SType = StructureType.ImageCreateInfo,
            ImageType = ImageType.Type2D,
            // Unorm, not Srgb: TextureData carries authored 8-bit RGBA that the fragment shader
            // multiplies by a tint and blends directly into the color target, which the rest of
            // this engine also treats as linear-ish Unorm. An Srgb view here would silently
            // brighten every icon relative to the text overlay drawn beside it.
            Format = Format.R8G8B8A8Unorm,
            Extent = new Extent3D((uint)texture.Width, (uint)texture.Height, 1),
            MipLevels = 1,
            ArrayLayers = 1,
            Samples = SampleCountFlags.Count1Bit,
            Tiling = ImageTiling.Optimal,
            Usage = ImageUsageFlags.TransferDstBit | ImageUsageFlags.SampledBit,
            SharingMode = SharingMode.Exclusive,
            InitialLayout = ImageLayout.Undefined,
        };
        Check(_vk.CreateImage(_device, in imageInfo, null, out var image), "CreateImage(overlayImage)");
        _vk.GetImageMemoryRequirements(_device, image, out var imageReq);
        var imageMemory = AllocateMemory(imageReq, MemoryPropertyFlags.DeviceLocalBit);
        Check(_vk.BindImageMemory(_device, image, imageMemory, 0), "BindImageMemory(overlayImage)");

        RecordAndSubmitUpload(staging, image, (uint)texture.Width, (uint)texture.Height);

        // Fence already waited inside RecordAndSubmitUpload, so the staging copy is complete and its
        // resources are free to go.
        _vk.DestroyBuffer(_device, staging, null);
        _vk.FreeMemory(_device, stagingMemory, null);

        var viewInfo = new ImageViewCreateInfo
        {
            SType = StructureType.ImageViewCreateInfo,
            Image = image,
            ViewType = ImageViewType.Type2D,
            Format = Format.R8G8B8A8Unorm,
            SubresourceRange = new ImageSubresourceRange(ImageAspectFlags.ColorBit, 0, 1, 0, 1),
        };
        Check(_vk.CreateImageView(_device, in viewInfo, null, out var view), "CreateImageView(overlayImage)");

        return new UploadedTexture(image, imageMemory, view, AllocateDescriptorSet(view));
    }

    /// <summary>
    /// The one-shot upload: transition to transfer-dst, copy, transition to shader-read-only, then
    /// submit and WAIT. Submitting on its own command buffer (rather than folding this into the
    /// caller's frame command buffer) is what keeps it legal — barriers and buffer→image copies
    /// cannot be recorded inside a render pass. The wait is a dedicated fence, not
    /// <c>vkDeviceWaitIdle</c>: this happens at first use only, and there is no reason to stall
    /// unrelated GPU work.
    /// </summary>
    private void RecordAndSubmitUpload(Silk.NET.Vulkan.Buffer staging, Image image, uint width, uint height)
    {
        var allocInfo = new CommandBufferAllocateInfo
        {
            SType = StructureType.CommandBufferAllocateInfo,
            CommandPool = _commandPool,
            Level = CommandBufferLevel.Primary,
            CommandBufferCount = 1,
        };
        Check(_vk.AllocateCommandBuffers(_device, in allocInfo, out var cmd), "AllocateCommandBuffers(overlayImageUpload)");

        var begin = new CommandBufferBeginInfo
        {
            SType = StructureType.CommandBufferBeginInfo,
            Flags = CommandBufferUsageFlags.OneTimeSubmitBit,
        };
        Check(_vk.BeginCommandBuffer(cmd, in begin), "BeginCommandBuffer(overlayImageUpload)");

        var range = new ImageSubresourceRange(ImageAspectFlags.ColorBit, 0, 1, 0, 1);

        // Undefined -> TransferDstOptimal. Src stage TopOfPipe with no src access: there is nothing
        // to make visible, the image was just created and its contents are undefined.
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
            BufferRowLength = 0,   // 0 = tightly packed, which TextureData guarantees.
            BufferImageHeight = 0,
            ImageSubresource = new ImageSubresourceLayers(ImageAspectFlags.ColorBit, 0, 0, 1),
            ImageOffset = new Offset3D(0, 0, 0),
            ImageExtent = new Extent3D(width, height, 1),
        };
        _vk.CmdCopyBufferToImage(cmd, staging, image, ImageLayout.TransferDstOptimal, 1, in region);

        // TransferDstOptimal -> ShaderReadOnlyOptimal: make the transfer's writes visible to the
        // fragment shader's sampled reads. Getting either mask wrong here yields a black icon and
        // no diagnostic (no validation layers) — hence the explicitness.
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

        Check(_vk.EndCommandBuffer(cmd), "EndCommandBuffer(overlayImageUpload)");

        var fenceInfo = new FenceCreateInfo { SType = StructureType.FenceCreateInfo };
        Check(_vk.CreateFence(_device, in fenceInfo, null, out var fence), "CreateFence(overlayImageUpload)");
        var submit = new SubmitInfo
        {
            SType = StructureType.SubmitInfo,
            CommandBufferCount = 1,
            PCommandBuffers = &cmd,
        };
        Check(_vk.QueueSubmit(_queue, 1, in submit, fence), "QueueSubmit(overlayImageUpload)");
        Check(_vk.WaitForFences(_device, 1, in fence, true, ulong.MaxValue), "WaitForFences(overlayImageUpload)");

        _vk.DestroyFence(_device, fence, null);
        _vk.FreeCommandBuffers(_device, _commandPool, 1, in cmd);
    }

    /// <summary>
    /// One combined-image-sampler descriptor set per texture, allocated from a pool sized for
    /// <see cref="MaxTextures"/> and bound per draw.
    /// <para>
    /// No per-frame duplication of these sets is needed, and that is a property of the current
    /// renderers rather than a general truth: <see cref="HeadlessRenderer"/> submits and waits per
    /// call, and <see cref="WindowedRenderer"/> has a single command buffer and a single
    /// <c>_inFlight</c> fence it waits on before re-recording — one frame in flight, by
    /// construction. If <see cref="WindowedRenderer"/> ever gains real multi-buffering, this
    /// assumption must be re-audited (a set written while a previous frame still reads it).
    /// </para>
    /// </summary>
    private DescriptorSet AllocateDescriptorSet(ImageView view)
    {
        var setLayout = _setLayout;
        var allocInfo = new DescriptorSetAllocateInfo
        {
            SType = StructureType.DescriptorSetAllocateInfo,
            DescriptorPool = _descriptorPool,
            DescriptorSetCount = 1,
            PSetLayouts = &setLayout,
        };
        Check(_vk.AllocateDescriptorSets(_device, in allocInfo, out var set), "AllocateDescriptorSets(overlayImage)");

        var imageInfo = new DescriptorImageInfo
        {
            Sampler = _sampler,
            ImageView = view,
            ImageLayout = ImageLayout.ShaderReadOnlyOptimal,
        };
        var write = new WriteDescriptorSet
        {
            SType = StructureType.WriteDescriptorSet,
            DstSet = set,
            DstBinding = 0,
            DescriptorCount = 1,
            DescriptorType = DescriptorType.CombinedImageSampler,
            PImageInfo = &imageInfo,
        };
        _vk.UpdateDescriptorSets(_device, 1, in write, 0, null);
        return set;
    }

    // --- pipeline -----------------------------------------------------------------------------

    private void EnsurePipeline(RenderPass renderPass, bool dynamicViewport, uint width, uint height)
    {
        if (_pipelineReady)
        {
            return;
        }

        CreateQuadBuffer();
        CreateSampler();
        CreateDescriptorLayoutAndPool();

        _vert = CreateShaderModule(PrecompiledShaders.OverlayImageVertSpirv);
        _frag = CreateShaderModule(PrecompiledShaders.OverlayImageFragSpirv);

        var entry = (byte*)SilkMarshal.StringToPtr("main");
        try
        {
            var stages = stackalloc PipelineShaderStageCreateInfo[2];
            stages[0] = new PipelineShaderStageCreateInfo { SType = StructureType.PipelineShaderStageCreateInfo, Stage = ShaderStageFlags.VertexBit, Module = _vert, PName = entry };
            stages[1] = new PipelineShaderStageCreateInfo { SType = StructureType.PipelineShaderStageCreateInfo, Stage = ShaderStageFlags.FragmentBit, Module = _frag, PName = entry };

            // Position only — the UV is derived in the vertex shader (inPos + 0.5), so this shares
            // the text overlay's vertex layout and needs no second attribute. See OverlaySource.
            var binding = new VertexInputBindingDescription { Binding = 0, Stride = 2 * sizeof(float), InputRate = VertexInputRate.Vertex };
            var attribute = new VertexInputAttributeDescription { Location = 0, Binding = 0, Format = Format.R32G32Sfloat, Offset = 0 };
            var vertexInput = new PipelineVertexInputStateCreateInfo
            {
                SType = StructureType.PipelineVertexInputStateCreateInfo,
                VertexBindingDescriptionCount = 1,
                PVertexBindingDescriptions = &binding,
                VertexAttributeDescriptionCount = 1,
                PVertexAttributeDescriptions = &attribute,
            };

            var inputAssembly = new PipelineInputAssemblyStateCreateInfo { SType = StructureType.PipelineInputAssemblyStateCreateInfo, Topology = PrimitiveTopology.TriangleList };

            // The one genuine difference between the two hosts: HeadlessRenderer's target is a
            // fixed size so it bakes viewport/scissor; WindowedRenderer's window resizes, so it
            // sets them per frame as dynamic state (already set before this pipeline binds).
            var viewport = new Viewport { X = 0, Y = 0, Width = width, Height = height, MinDepth = 0, MaxDepth = 1 };
            var scissor = new Rect2D(new Offset2D(0, 0), new Extent2D(width, height));
            var viewportState = dynamicViewport
                ? new PipelineViewportStateCreateInfo { SType = StructureType.PipelineViewportStateCreateInfo, ViewportCount = 1, ScissorCount = 1 }
                : new PipelineViewportStateCreateInfo { SType = StructureType.PipelineViewportStateCreateInfo, ViewportCount = 1, PViewports = &viewport, ScissorCount = 1, PScissors = &scissor };

            var dynamicStates = stackalloc DynamicState[2] { DynamicState.Viewport, DynamicState.Scissor };
            var dynamicState = new PipelineDynamicStateCreateInfo { SType = StructureType.PipelineDynamicStateCreateInfo, DynamicStateCount = 2, PDynamicStates = dynamicStates };

            var rasterizer = new PipelineRasterizationStateCreateInfo { SType = StructureType.PipelineRasterizationStateCreateInfo, PolygonMode = PolygonMode.Fill, CullMode = CullModeFlags.None, FrontFace = FrontFace.CounterClockwise, LineWidth = 1f };
            var multisample = new PipelineMultisampleStateCreateInfo { SType = StructureType.PipelineMultisampleStateCreateInfo, RasterizationSamples = SampleCountFlags.Count1Bit, MinSampleShading = 1f };

            // Same as the text overlay: an overlay must never be occluded by, nor occlude via
            // leftover depth, the 3D scene it draws on top of.
            var depthStencil = new PipelineDepthStencilStateCreateInfo { SType = StructureType.PipelineDepthStencilStateCreateInfo, DepthTestEnable = false, DepthWriteEnable = false, DepthCompareOp = CompareOp.Always };

            // Alpha blending — the capability the untextured overlay pipeline lacks
            // (BlendEnable = false there), and the whole point of an icon with transparent margins.
            // The ALPHA factors are One/OneMinusSrcAlpha, deliberately differing from
            // HeadlessRenderer.RenderToTexture.cs's composite pipeline (One/Zero): that one is the
            // single, final blend onto an opaque background so it can discard destination alpha,
            // whereas HUD icons stack on each other and need alpha to accumulate correctly
            // (out.a = src.a + dst.a * (1 - src.a)). Do not "fix" this to match RenderToTexture.
            var blendAttachment = new PipelineColorBlendAttachmentState
            {
                ColorWriteMask = ColorComponentFlags.RBit | ColorComponentFlags.GBit | ColorComponentFlags.BBit | ColorComponentFlags.ABit,
                BlendEnable = true,
                SrcColorBlendFactor = BlendFactor.SrcAlpha,
                DstColorBlendFactor = BlendFactor.OneMinusSrcAlpha,
                ColorBlendOp = BlendOp.Add,
                SrcAlphaBlendFactor = BlendFactor.One,
                DstAlphaBlendFactor = BlendFactor.OneMinusSrcAlpha,
                AlphaBlendOp = BlendOp.Add,
            };
            var colorBlend = new PipelineColorBlendStateCreateInfo { SType = StructureType.PipelineColorBlendStateCreateInfo, AttachmentCount = 1, PAttachments = &blendAttachment };

            var pushRange = new PushConstantRange { StageFlags = ShaderStageFlags.VertexBit | ShaderStageFlags.FragmentBit, Offset = 0, Size = (uint)sizeof(OverlayImagePush) };
            var setLayout = _setLayout;
            var layoutInfo = new PipelineLayoutCreateInfo
            {
                SType = StructureType.PipelineLayoutCreateInfo,
                SetLayoutCount = 1,
                PSetLayouts = &setLayout,
                PushConstantRangeCount = 1,
                PPushConstantRanges = &pushRange,
            };
            Check(_vk.CreatePipelineLayout(_device, in layoutInfo, null, out _pipelineLayout), "CreatePipelineLayout(overlayImage)");

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
                RenderPass = renderPass,
                Subpass = 0,
            };
            Check(_vk.CreateGraphicsPipelines(_device, default, 1, in pipelineInfo, null, out _pipeline), "CreateGraphicsPipelines(overlayImage)");
        }
        finally
        {
            SilkMarshal.Free((nint)entry);
        }

        _pipelineReady = true;
    }

    /// <summary>Its own copy of the shared unit quad rather than borrowing the text overlay's:
    /// that buffer is created lazily by the text path, so a game drawing icons and no text would
    /// bind a null handle. Six vertices is not worth an initialisation-order dependency.</summary>
    private void CreateQuadBuffer()
    {
        // Identical geometry and winding to the text overlay's quad — CCW read as Y-up, clockwise
        // on screen under Y-down NDC, which is harmless only because CullMode is None.
        float[] vertices =
        [
            -0.5f, -0.5f, 0.5f, -0.5f, 0.5f, 0.5f,
            -0.5f, -0.5f, 0.5f, 0.5f, -0.5f, 0.5f,
        ];
        var size = (ulong)(vertices.Length * sizeof(float));
        var info = new BufferCreateInfo { SType = StructureType.BufferCreateInfo, Size = size, Usage = BufferUsageFlags.VertexBufferBit, SharingMode = SharingMode.Exclusive };
        Check(_vk.CreateBuffer(_device, in info, null, out _quadBuffer), "CreateBuffer(overlayImageQuad)");

        _vk.GetBufferMemoryRequirements(_device, _quadBuffer, out var req);
        _quadMemory = AllocateMemory(req, MemoryPropertyFlags.HostVisibleBit | MemoryPropertyFlags.HostCoherentBit);
        Check(_vk.BindBufferMemory(_device, _quadBuffer, _quadMemory, 0), "BindBufferMemory(overlayImageQuad)");

        void* mapped = null;
        Check(_vk.MapMemory(_device, _quadMemory, 0, size, 0, ref mapped), "MapMemory(overlayImageQuad)");
        vertices.AsSpan().CopyTo(new Span<float>(mapped, vertices.Length));
        _vk.UnmapMemory(_device, _quadMemory);
    }

    /// <summary><see cref="Filter.Nearest"/> and <see cref="SamplerAddressMode.ClampToEdge"/>: HUD
    /// icons draw axis-aligned in screen space, normally at or near their authored pixel size, so
    /// linear filtering would only soften crisp edges and bleed the transparent margin of an icon
    /// into its artwork. Nearest also makes the render tests' expected pixel colours exact rather
    /// than interpolated, which is what lets a four-quadrant texture prove UV orientation.</summary>
    private void CreateSampler()
    {
        var info = new SamplerCreateInfo
        {
            SType = StructureType.SamplerCreateInfo,
            MagFilter = Filter.Nearest,
            MinFilter = Filter.Nearest,
            AddressModeU = SamplerAddressMode.ClampToEdge,
            AddressModeV = SamplerAddressMode.ClampToEdge,
            AddressModeW = SamplerAddressMode.ClampToEdge,
            MipmapMode = SamplerMipmapMode.Nearest,
            MinLod = 0,
            MaxLod = 0,
            BorderColor = BorderColor.FloatTransparentBlack,
            UnnormalizedCoordinates = false,
        };
        Check(_vk.CreateSampler(_device, in info, null, out _sampler), "CreateSampler(overlayImage)");
    }

    private void CreateDescriptorLayoutAndPool()
    {
        var binding = new DescriptorSetLayoutBinding
        {
            Binding = 0,
            DescriptorType = DescriptorType.CombinedImageSampler,
            DescriptorCount = 1,
            StageFlags = ShaderStageFlags.FragmentBit,
        };
        var setLayoutInfo = new DescriptorSetLayoutCreateInfo
        {
            SType = StructureType.DescriptorSetLayoutCreateInfo,
            BindingCount = 1,
            PBindings = &binding,
        };
        Check(_vk.CreateDescriptorSetLayout(_device, in setLayoutInfo, null, out _setLayout), "CreateDescriptorSetLayout(overlayImage)");

        var poolSize = new DescriptorPoolSize { Type = DescriptorType.CombinedImageSampler, DescriptorCount = MaxTextures };
        var poolInfo = new DescriptorPoolCreateInfo
        {
            SType = StructureType.DescriptorPoolCreateInfo,
            PoolSizeCount = 1,
            PPoolSizes = &poolSize,
            MaxSets = MaxTextures,
        };
        Check(_vk.CreateDescriptorPool(_device, in poolInfo, null, out _descriptorPool), "CreateDescriptorPool(overlayImage)");
    }

    // --- helpers ------------------------------------------------------------------------------

    private ShaderModule CreateShaderModule(uint[] spirv)
    {
        fixed (uint* code = spirv)
        {
            var info = new ShaderModuleCreateInfo
            {
                SType = StructureType.ShaderModuleCreateInfo,
                CodeSize = (nuint)(spirv.Length * sizeof(uint)),
                PCode = code,
            };
            Check(_vk.CreateShaderModule(_device, in info, null, out var module), "CreateShaderModule(overlayImage)");
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
                var allocInfo = new MemoryAllocateInfo
                {
                    SType = StructureType.MemoryAllocateInfo,
                    AllocationSize = req.Size,
                    MemoryTypeIndex = i,
                };
                Check(_vk.AllocateMemory(_device, in allocInfo, null, out var memory), "AllocateMemory(overlayImage)");
                return memory;
            }
        }

        throw new InvalidOperationException($"No Vulkan memory type satisfies {flags} for the overlay-image allocation.");
    }

    private static void Check(Result result, string what)
    {
        if (result != Result.Success)
        {
            throw new InvalidOperationException($"Vulkan {what} failed: {result}");
        }
    }

    /// <summary>Destroys every resource in reverse-of-creation order. Each guard means a renderer
    /// that never drew a single textured overlay destroys nothing — all handles start zeroed.</summary>
    public void Dispose()
    {
        foreach (var texture in _uploaded.Values)
        {
            // Descriptor sets are freed wholesale by DestroyDescriptorPool below, so they are
            // deliberately not freed individually here (the pool was not created with
            // FreeDescriptorSetBit).
            if (texture.View.Handle != 0)
            {
                _vk.DestroyImageView(_device, texture.View, null);
            }

            if (texture.Image.Handle != 0)
            {
                _vk.DestroyImage(_device, texture.Image, null);
            }

            if (texture.Memory.Handle != 0)
            {
                _vk.FreeMemory(_device, texture.Memory, null);
            }
        }

        _uploaded.Clear();

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

        if (_pipeline.Handle != 0)
        {
            _vk.DestroyPipeline(_device, _pipeline, null);
        }

        if (_pipelineLayout.Handle != 0)
        {
            _vk.DestroyPipelineLayout(_device, _pipelineLayout, null);
        }

        if (_vert.Handle != 0)
        {
            _vk.DestroyShaderModule(_device, _vert, null);
        }

        if (_frag.Handle != 0)
        {
            _vk.DestroyShaderModule(_device, _frag, null);
        }

        if (_quadBuffer.Handle != 0)
        {
            _vk.DestroyBuffer(_device, _quadBuffer, null);
        }

        if (_quadMemory.Handle != 0)
        {
            _vk.FreeMemory(_device, _quadMemory, null);
        }
    }
}
