using Silk.NET.Core.Native;
using Silk.NET.Vulkan;

namespace Aigis.Render;

/// <summary>
/// TASK-148: the sun shadow depth pass, kept in lockstep with
/// <c>HeadlessRenderer.Shadows.cs</c> — hard rule 6, both render paths must produce the same
/// image. See that file's class doc comment for the design (a single fitted 2048&#178; depth-only
/// render, 3x3 PCF-sampled by <see cref="ShaderSource.PrimitiveFrag"/>) and TASK-148's entry in
/// tasks/backlog-details.md for why one map, not cascades, is enough for every shipped Triage
/// stage.
/// <para>
/// Unlike the main <see cref="WindowedRenderer"/> pipeline (dynamic viewport, since the window
/// can resize), the shadow map is always <see cref="ShadowMapConfig.MapSize"/>&#178; regardless
/// of window size, so its pipeline uses a static viewport exactly like
/// <see cref="HeadlessRenderer"/>'s does.
/// </para>
/// </summary>
public sealed unsafe partial class WindowedRenderer
{
    private const Format ShadowDepthFormat = Format.D32Sfloat;

    private bool _shadowResourcesReady;
    private bool _shadowPipelineReady;
    private RenderPass _shadowRenderPass;
    private Image _shadowDepthImage;
    private DeviceMemory _shadowDepthMemory;
    private ImageView _shadowDepthView;
    private Framebuffer _shadowFramebuffer;
    private readonly Image[] _fixtureShadowDepthImages = new Image[SceneLighting.MaxDownwardFixtureShadows];
    private readonly DeviceMemory[] _fixtureShadowDepthMemories = new DeviceMemory[SceneLighting.MaxDownwardFixtureShadows];
    private readonly ImageView[] _fixtureShadowDepthViews = new ImageView[SceneLighting.MaxDownwardFixtureShadows];
    private readonly Framebuffer[] _fixtureShadowFramebuffers = new Framebuffer[SceneLighting.MaxDownwardFixtureShadows];
    private Sampler _shadowSampler;
    private Pipeline _shadowPipeline;
    private Pipeline _fixtureShadowPipeline;
    private PipelineLayout _shadowPipelineLayout;
    private ShaderModule _shadowVert;
    private ShaderModule _fixtureShadowVert;

    /// <summary>See <see cref="HeadlessRenderer.EnsureShadowResources"/> — identical resources,
    /// identical reasoning (the lighting descriptor's binding 1 needs a valid image view before
    /// the first frame that ever enables shadows).</summary>
    private void EnsureShadowResources()
    {
        if (_shadowResourcesReady)
        {
            return;
        }

        var imageInfo = new ImageCreateInfo
        {
            SType = StructureType.ImageCreateInfo,
            ImageType = ImageType.Type2D,
            Format = ShadowDepthFormat,
            Extent = new Extent3D(ShadowMapConfig.MapSize, ShadowMapConfig.MapSize, 1),
            MipLevels = 1,
            ArrayLayers = 1,
            Samples = SampleCountFlags.Count1Bit,
            Tiling = ImageTiling.Optimal,
            Usage = ImageUsageFlags.DepthStencilAttachmentBit | ImageUsageFlags.SampledBit,
            SharingMode = SharingMode.Exclusive,
            InitialLayout = ImageLayout.Undefined,
        };
        Check(_vk.CreateImage(_device, in imageInfo, null, out _shadowDepthImage), "CreateImage(shadowDepth)");

        _vk.GetImageMemoryRequirements(_device, _shadowDepthImage, out var req);
        _shadowDepthMemory = AllocateMemory(req, MemoryPropertyFlags.DeviceLocalBit);
        Check(_vk.BindImageMemory(_device, _shadowDepthImage, _shadowDepthMemory, 0), "BindImageMemory(shadowDepth)");

        var viewInfo = new ImageViewCreateInfo
        {
            SType = StructureType.ImageViewCreateInfo,
            Image = _shadowDepthImage,
            ViewType = ImageViewType.Type2D,
            Format = ShadowDepthFormat,
            SubresourceRange = new ImageSubresourceRange(ImageAspectFlags.DepthBit, 0, 1, 0, 1),
        };
        Check(_vk.CreateImageView(_device, in viewInfo, null, out _shadowDepthView), "CreateImageView(shadowDepth)");
        for (var i = 0; i < SceneLighting.MaxDownwardFixtureShadows; i++)
        {
            Check(_vk.CreateImage(_device, in imageInfo, null, out _fixtureShadowDepthImages[i]), $"CreateImage(fixtureShadow{i})");
            _vk.GetImageMemoryRequirements(_device, _fixtureShadowDepthImages[i], out req);
            _fixtureShadowDepthMemories[i] = AllocateMemory(req, MemoryPropertyFlags.DeviceLocalBit);
            Check(_vk.BindImageMemory(_device, _fixtureShadowDepthImages[i], _fixtureShadowDepthMemories[i], 0), $"BindImageMemory(fixtureShadow{i})");
            viewInfo.Image = _fixtureShadowDepthImages[i];
            Check(_vk.CreateImageView(_device, in viewInfo, null, out _fixtureShadowDepthViews[i]), $"CreateImageView(fixtureShadow{i})");
        }

        var samplerInfo = new SamplerCreateInfo
        {
            SType = StructureType.SamplerCreateInfo,
            MagFilter = Filter.Linear,
            MinFilter = Filter.Linear,
            MipmapMode = SamplerMipmapMode.Nearest,
            AddressModeU = SamplerAddressMode.ClampToBorder,
            AddressModeV = SamplerAddressMode.ClampToBorder,
            AddressModeW = SamplerAddressMode.ClampToBorder,
            BorderColor = BorderColor.FloatOpaqueWhite,
            CompareEnable = false,
            MinLod = 0f,
            MaxLod = 0f,
        };
        Check(_vk.CreateSampler(_device, in samplerInfo, null, out _shadowSampler), "CreateSampler(shadow)");

        var depthAttachment = new AttachmentDescription
        {
            Format = ShadowDepthFormat,
            Samples = SampleCountFlags.Count1Bit,
            LoadOp = AttachmentLoadOp.Clear,
            StoreOp = AttachmentStoreOp.Store,
            StencilLoadOp = AttachmentLoadOp.DontCare,
            StencilStoreOp = AttachmentStoreOp.DontCare,
            InitialLayout = ImageLayout.Undefined,
            FinalLayout = ImageLayout.ShaderReadOnlyOptimal,
        };
        var depthRef = new AttachmentReference(0, ImageLayout.DepthStencilAttachmentOptimal);
        var subpass = new SubpassDescription
        {
            PipelineBindPoint = PipelineBindPoint.Graphics,
            ColorAttachmentCount = 0,
            PDepthStencilAttachment = &depthRef,
        };

        var dependencies = stackalloc SubpassDependency[2];
        dependencies[0] = new SubpassDependency
        {
            SrcSubpass = Vk.SubpassExternal,
            DstSubpass = 0,
            SrcStageMask = PipelineStageFlags.FragmentShaderBit,
            DstStageMask = PipelineStageFlags.EarlyFragmentTestsBit,
            SrcAccessMask = AccessFlags.ShaderReadBit,
            DstAccessMask = AccessFlags.DepthStencilAttachmentWriteBit,
            DependencyFlags = DependencyFlags.ByRegionBit,
        };
        dependencies[1] = new SubpassDependency
        {
            SrcSubpass = 0,
            DstSubpass = Vk.SubpassExternal,
            SrcStageMask = PipelineStageFlags.LateFragmentTestsBit,
            DstStageMask = PipelineStageFlags.FragmentShaderBit,
            SrcAccessMask = AccessFlags.DepthStencilAttachmentWriteBit,
            DstAccessMask = AccessFlags.ShaderReadBit,
            DependencyFlags = DependencyFlags.ByRegionBit,
        };

        var renderPassInfo = new RenderPassCreateInfo
        {
            SType = StructureType.RenderPassCreateInfo,
            AttachmentCount = 1,
            PAttachments = &depthAttachment,
            SubpassCount = 1,
            PSubpasses = &subpass,
            DependencyCount = 2,
            PDependencies = dependencies,
        };
        Check(_vk.CreateRenderPass(_device, in renderPassInfo, null, out _shadowRenderPass), "CreateRenderPass(shadow)");

        var view = _shadowDepthView;
        var framebufferInfo = new FramebufferCreateInfo
        {
            SType = StructureType.FramebufferCreateInfo,
            RenderPass = _shadowRenderPass,
            AttachmentCount = 1,
            PAttachments = &view,
            Width = ShadowMapConfig.MapSize,
            Height = ShadowMapConfig.MapSize,
            Layers = 1,
        };
        Check(_vk.CreateFramebuffer(_device, in framebufferInfo, null, out _shadowFramebuffer), "CreateFramebuffer(shadow)");
        for (var i = 0; i < SceneLighting.MaxDownwardFixtureShadows; i++)
        {
            view = _fixtureShadowDepthViews[i];
            framebufferInfo.PAttachments = &view;
            Check(_vk.CreateFramebuffer(_device, in framebufferInfo, null, out _fixtureShadowFramebuffers[i]), $"CreateFramebuffer(fixtureShadow{i})");
        }

        _shadowResourcesReady = true;
    }

    /// <summary>See <see cref="HeadlessRenderer.EnsureShadowPipeline"/> — identical pipeline
    /// state (including the depth bias tuning), aside from using a static
    /// <see cref="ShadowMapConfig.MapSize"/>&#178; viewport unconditionally, same as
    /// <see cref="HeadlessRenderer"/> (this pass never resizes even though the main swapchain
    /// pass does).</summary>
    private void EnsureShadowPipeline()
    {
        if (_shadowPipelineReady)
        {
            return;
        }

        EnsureShadowResources();

        _shadowVert = CreateShaderModule(PrecompiledShaders.ShadowDepthVertSpirv);
        _fixtureShadowVert = CreateShaderModule(PrecompiledShaders.FixtureShadowDepthVertSpirv);

        var entry = (byte*)SilkMarshal.StringToPtr("main");
        try
        {
            var stage = new PipelineShaderStageCreateInfo
            {
                SType = StructureType.PipelineShaderStageCreateInfo,
                Stage = ShaderStageFlags.VertexBit,
                Module = _shadowVert,
                PName = entry,
            };

            var bindings = stackalloc VertexInputBindingDescription[2];
            bindings[0] = new VertexInputBindingDescription { Binding = 0, Stride = 6 * sizeof(float), InputRate = VertexInputRate.Vertex };
            bindings[1] = new VertexInputBindingDescription { Binding = 1, Stride = (uint)(PrimitiveInstancePacker.FloatsPerInstance * sizeof(float)), InputRate = VertexInputRate.Instance };

            var attributes = stackalloc VertexInputAttributeDescription[7];
            attributes[0] = new VertexInputAttributeDescription { Location = 0, Binding = 0, Format = Format.R32G32B32Sfloat, Offset = 0 };
            attributes[1] = new VertexInputAttributeDescription { Location = 1, Binding = 0, Format = Format.R32G32B32Sfloat, Offset = 3 * sizeof(float) };
            attributes[2] = new VertexInputAttributeDescription { Location = 7, Binding = 1, Format = Format.R32G32B32A32Sfloat, Offset = 20 * sizeof(float) };
            attributes[3] = new VertexInputAttributeDescription { Location = 9, Binding = 1, Format = Format.R32G32B32A32Sfloat, Offset = 28 * sizeof(float) };
            attributes[4] = new VertexInputAttributeDescription { Location = 10, Binding = 1, Format = Format.R32G32B32A32Sfloat, Offset = 32 * sizeof(float) };
            attributes[5] = new VertexInputAttributeDescription { Location = 11, Binding = 1, Format = Format.R32G32B32A32Sfloat, Offset = 36 * sizeof(float) };
            attributes[6] = new VertexInputAttributeDescription { Location = 12, Binding = 1, Format = Format.R32G32B32A32Sfloat, Offset = 40 * sizeof(float) };

            var vertexInput = new PipelineVertexInputStateCreateInfo
            {
                SType = StructureType.PipelineVertexInputStateCreateInfo,
                VertexBindingDescriptionCount = 2,
                PVertexBindingDescriptions = bindings,
                VertexAttributeDescriptionCount = 7,
                PVertexAttributeDescriptions = attributes,
            };

            var inputAssembly = new PipelineInputAssemblyStateCreateInfo { SType = StructureType.PipelineInputAssemblyStateCreateInfo, Topology = PrimitiveTopology.TriangleList };

            var viewport = new Viewport { X = 0, Y = 0, Width = ShadowMapConfig.MapSize, Height = ShadowMapConfig.MapSize, MinDepth = 0, MaxDepth = 1 };
            var scissor = new Rect2D(new Offset2D(0, 0), new Extent2D(ShadowMapConfig.MapSize, ShadowMapConfig.MapSize));
            var viewportState = new PipelineViewportStateCreateInfo { SType = StructureType.PipelineViewportStateCreateInfo, ViewportCount = 1, PViewports = &viewport, ScissorCount = 1, PScissors = &scissor };

            // Back-face culling, and it cannot change one texel of the result: this pass keeps the
            // NEAREST surface to the light, and a closed solid's back faces are by definition behind
            // its front ones. It halves the triangles rasterized into five maps every frame (the sun
            // plus up to four fixtures) -- the second-largest GPU cost in a Triage ward after the
            // main pass itself.
            //
            // This was CullMode.None until the winding across cube/sphere/capsule/cylinder was made
            // consistent and pinned by
            // PrimitiveMeshesTests.EveryMesh_IsWoundCounterClockwiseFromOutside (the sphere and both
            // cylinder caps used to be wound the other way, so culling here would have punched holes
            // in the depth map -- exactly what the old comment here feared).
            //
            // FrontFace stays COUNTER-CLOCKWISE, measured rather than reasoned: the projection's Y
            // flip for Vulkan's Y-down clip space (Camera.ApplyVulkanClip) and Vulkan's own Y-down
            // framebuffer winding rule cancel out, so an outward-CCW face is still counter-clockwise
            // where the cull test happens. Getting this backwards keeps the FAR side of each solid
            // instead of the near one, which makes an occluder's underside its shadow depth -- coplanar
            // with the floor it should be shadowing, so the shadow silently disappears.
            // ShadowMapRenderTests.DownwardFixturePointLight_IsOccludedByTheBoxBeneathIt is the test
            // that catches it; the sun-shadow tests do not.
            var rasterizer = new PipelineRasterizationStateCreateInfo
            {
                SType = StructureType.PipelineRasterizationStateCreateInfo,
                PolygonMode = PolygonMode.Fill,
                CullMode = CullModeFlags.BackBit,
                FrontFace = FrontFace.CounterClockwise,
                LineWidth = 1f,
                DepthBiasEnable = true,
                DepthBiasConstantFactor = ShadowMapConfig.DepthBiasConstantFactor,
                DepthBiasSlopeFactor = ShadowMapConfig.DepthBiasSlopeFactor,
            };
            var multisample = new PipelineMultisampleStateCreateInfo { SType = StructureType.PipelineMultisampleStateCreateInfo, RasterizationSamples = SampleCountFlags.Count1Bit, MinSampleShading = 1f };
            var depthStencil = new PipelineDepthStencilStateCreateInfo
            {
                SType = StructureType.PipelineDepthStencilStateCreateInfo,
                DepthTestEnable = true,
                DepthWriteEnable = true,
                DepthCompareOp = CompareOp.Less,
                DepthBoundsTestEnable = false,
                StencilTestEnable = false,
            };
            var colorBlend = new PipelineColorBlendStateCreateInfo { SType = StructureType.PipelineColorBlendStateCreateInfo, AttachmentCount = 0 };

            var pushRange = new PushConstantRange { StageFlags = ShaderStageFlags.VertexBit, Offset = 0, Size = sizeof(int) };
            var descriptorSetLayout = _lightingSetLayout;
            var layoutInfo = new PipelineLayoutCreateInfo { SType = StructureType.PipelineLayoutCreateInfo, SetLayoutCount = 1, PSetLayouts = &descriptorSetLayout, PushConstantRangeCount = 1, PPushConstantRanges = &pushRange };
            Check(_vk.CreatePipelineLayout(_device, in layoutInfo, null, out _shadowPipelineLayout), "CreatePipelineLayout(shadow)");

            var pipelineInfo = new GraphicsPipelineCreateInfo
            {
                SType = StructureType.GraphicsPipelineCreateInfo,
                StageCount = 1,
                PStages = &stage,
                PVertexInputState = &vertexInput,
                PInputAssemblyState = &inputAssembly,
                PViewportState = &viewportState,
                PRasterizationState = &rasterizer,
                PMultisampleState = &multisample,
                PDepthStencilState = &depthStencil,
                PColorBlendState = &colorBlend,
                Layout = _shadowPipelineLayout,
                RenderPass = _shadowRenderPass,
                Subpass = 0,
            };
            Check(_vk.CreateGraphicsPipelines(_device, default, 1, in pipelineInfo, null, out _shadowPipeline), "CreateGraphicsPipelines(shadow)");

            stage.Module = _fixtureShadowVert;
            var fixtureAttributes = stackalloc VertexInputAttributeDescription[6];
            fixtureAttributes[0] = new VertexInputAttributeDescription { Location = 0, Binding = 0, Format = Format.R32G32B32Sfloat, Offset = 0 };
            fixtureAttributes[1] = new VertexInputAttributeDescription { Location = 1, Binding = 0, Format = Format.R32G32B32Sfloat, Offset = 3 * sizeof(float) };
            fixtureAttributes[2] = new VertexInputAttributeDescription { Location = 7, Binding = 1, Format = Format.R32G32B32A32Sfloat, Offset = 20 * sizeof(float) };
            fixtureAttributes[3] = new VertexInputAttributeDescription { Location = 8, Binding = 1, Format = Format.R32G32B32A32Sfloat, Offset = 24 * sizeof(float) };
            fixtureAttributes[4] = new VertexInputAttributeDescription { Location = 13, Binding = 1, Format = Format.R32G32B32A32Sfloat, Offset = 44 * sizeof(float) };
            fixtureAttributes[5] = new VertexInputAttributeDescription { Location = 14, Binding = 1, Format = Format.R32G32B32A32Sfloat, Offset = 48 * sizeof(float) };
            vertexInput.VertexAttributeDescriptionCount = 6;
            vertexInput.PVertexAttributeDescriptions = fixtureAttributes;
            Check(_vk.CreateGraphicsPipelines(_device, default, 1, in pipelineInfo, null, out _fixtureShadowPipeline), "CreateGraphicsPipelines(fixtureShadow)");
        }
        finally
        {
            SilkMarshal.Free((nint)entry);
        }

        _shadowPipelineReady = true;
    }

    private void BeginShadowRenderPass(CommandBuffer command, Framebuffer framebuffer)
    {
        var clear = new ClearValue { DepthStencil = new ClearDepthStencilValue(1.0f, 0) };
        var info = new RenderPassBeginInfo
        {
            SType = StructureType.RenderPassBeginInfo,
            RenderPass = _shadowRenderPass,
            Framebuffer = framebuffer,
            RenderArea = new Rect2D(new Offset2D(0, 0), new Extent2D(ShadowMapConfig.MapSize, ShadowMapConfig.MapSize)),
            ClearValueCount = 1,
            PClearValues = &clear,
        };
        _vk.CmdBeginRenderPass(command, in info, SubpassContents.Inline);
    }

    /// <summary>See <see cref="HeadlessRenderer.DrawShadowBatches"/> — same per-kind batches,
    /// same shared instance buffer, different (depth-only) target.</summary>
    private void DrawShadowBatches(CommandBuffer command, bool includeShadowCastOnly = true)
    {
        if (_primitivePacker.TotalCount == 0)
        {
            return;
        }

        _vk.CmdBindPipeline(command, PipelineBindPoint.Graphics, _shadowPipeline);
        var buffers = stackalloc Silk.NET.Vulkan.Buffer[2];
        buffers[1] = _primitiveInstanceBuffer;
        var offsets = stackalloc ulong[2] { 0, 0 };
        var batches = includeShadowCastOnly ? _primitivePacker.Batches : _primitivePacker.VisibleBatches;
        foreach (var batch in batches)
        {
            var (mesh, vertexCount) = GeometryFor(batch);
            buffers[0] = mesh;
            _vk.CmdBindVertexBuffers(command, 0, 2, buffers, offsets);
            _vk.CmdDraw(command, vertexCount, (uint)batch.Count, 0, (uint)batch.Offset);
        }
    }

    private void DrawFixtureShadowBatches(CommandBuffer command, int fixtureIndex)
    {
        if (_primitivePacker.TotalCount == 0)
        {
            return;
        }

        _vk.CmdBindPipeline(command, PipelineBindPoint.Graphics, _fixtureShadowPipeline);
        var descriptorSet = _multiViewLightingSets[0];
        _vk.CmdBindDescriptorSets(command, PipelineBindPoint.Graphics, _shadowPipelineLayout, 0, 1, &descriptorSet, 0, null);
        _vk.CmdPushConstants(command, _shadowPipelineLayout, ShaderStageFlags.VertexBit, 0, sizeof(int), &fixtureIndex);
        var buffers = stackalloc Silk.NET.Vulkan.Buffer[2];
        buffers[1] = _primitiveInstanceBuffer;
        var offsets = stackalloc ulong[2] { 0, 0 };
        foreach (var batch in _primitivePacker.FixtureShadowBatches)
        {
            var (mesh, vertexCount) = GeometryFor(batch);
            buffers[0] = mesh;
            _vk.CmdBindVertexBuffers(command, 0, 2, buffers, offsets);
            _vk.CmdDraw(command, vertexCount, (uint)batch.Count, 0, (uint)batch.Offset);
        }
    }

    private void DisposeShadowResources()
    {
        if (_fixtureShadowPipeline.Handle != 0)
        {
            _vk.DestroyPipeline(_device, _fixtureShadowPipeline, null);
        }

        if (_fixtureShadowVert.Handle != 0)
        {
            _vk.DestroyShaderModule(_device, _fixtureShadowVert, null);
        }
        for (var i = 0; i < SceneLighting.MaxDownwardFixtureShadows; i++)
        {
            if (_fixtureShadowFramebuffers[i].Handle != 0)
            {
                _vk.DestroyFramebuffer(_device, _fixtureShadowFramebuffers[i], null);
            }
        }
        if (_shadowPipeline.Handle != 0)
        {
            _vk.DestroyPipeline(_device, _shadowPipeline, null);
        }

        if (_shadowPipelineLayout.Handle != 0)
        {
            _vk.DestroyPipelineLayout(_device, _shadowPipelineLayout, null);
        }

        if (_shadowVert.Handle != 0)
        {
            _vk.DestroyShaderModule(_device, _shadowVert, null);
        }

        if (_shadowFramebuffer.Handle != 0)
        {
            _vk.DestroyFramebuffer(_device, _shadowFramebuffer, null);
        }

        for (var i = 0; i < SceneLighting.MaxDownwardFixtureShadows; i++)
        {
            if (_fixtureShadowDepthViews[i].Handle != 0)
            {
                _vk.DestroyImageView(_device, _fixtureShadowDepthViews[i], null);
            }
        }

        if (_shadowRenderPass.Handle != 0)
        {
            _vk.DestroyRenderPass(_device, _shadowRenderPass, null);
        }

        if (_shadowSampler.Handle != 0)
        {
            _vk.DestroySampler(_device, _shadowSampler, null);
        }

        if (_shadowDepthView.Handle != 0)
        {
            _vk.DestroyImageView(_device, _shadowDepthView, null);
        }

        for (var i = 0; i < SceneLighting.MaxDownwardFixtureShadows; i++)
        {
            if (_fixtureShadowDepthImages[i].Handle != 0)
            {
                _vk.DestroyImage(_device, _fixtureShadowDepthImages[i], null);
            }

            if (_fixtureShadowDepthMemories[i].Handle != 0)
            {
                _vk.FreeMemory(_device, _fixtureShadowDepthMemories[i], null);
            }
        }

        if (_shadowDepthImage.Handle != 0)
        {
            _vk.DestroyImage(_device, _shadowDepthImage, null);
        }

        if (_shadowDepthMemory.Handle != 0)
        {
            _vk.FreeMemory(_device, _shadowDepthMemory, null);
        }
    }
}
