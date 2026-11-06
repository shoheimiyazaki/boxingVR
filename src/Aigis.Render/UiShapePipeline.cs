using System.Numerics;
using System.Runtime.InteropServices;
using Silk.NET.Core.Native;
using Silk.NET.Vulkan;

namespace Aigis.Render;

/// <summary>
/// The alpha-blended, analytic rounded-rectangle overlay draw (see <see cref="UiShapeSource"/> for
/// the shader and why it exists) — owned in ONE place and composed by BOTH renderers, the same
/// arrangement <see cref="OverlayImagePipeline"/> established.
/// <para>
/// <b>Why shared rather than duplicated per renderer.</b> Every other simple pipeline in this engine
/// is hand-copied into a <c>HeadlessRenderer.*.cs</c>/<c>WindowedRenderer.*.cs</c> pair, and for the
/// text overlay that is defensible: it is a short, frozen recipe. This one is neither frozen nor
/// short — the push-constant layout is seven vectors that the GLSL indexes positionally, so a
/// reordering applied to one copy and not the other would not fail to build, would not throw, and
/// (these renderers run without validation layers — see docs/architecture.md) would not even log.
/// It would render one host's HUD with the border colour in the gradient slot. One implementation
/// removes that failure mode by construction, and the composition seam is parameters rather than
/// inheritance for the same reason it is there: the two renderers share no base class.
/// </para>
/// <para>
/// Hard rule 6 holds by construction: the headless renderer composes this same class, so every card,
/// shadow and gauge an agent needs to see is reproducible in a screenshot with no window involved.
/// </para>
/// </summary>
internal sealed unsafe class UiShapePipeline : IDisposable
{
    /// <summary>
    /// The shader's push constant, laid out to match <see cref="UiShapeSource.UiShapeFrag"/>'s
    /// <c>Push</c> block field for field. 112 bytes — inside the 128-byte range Vulkan guarantees on
    /// every implementation, which is what keeps this pipeline descriptor-free and therefore
    /// bind-free per draw.
    /// <para>
    /// <b>The field order is the ABI.</b> GLSL matches by offset, not by name, so reordering these
    /// silently rewires every shape on screen. Any change here has to be made in
    /// <see cref="UiShapeSource"/> in the same edit, and the SPIR-V regenerated (see
    /// <see cref="PrecompiledShaders"/>).
    /// </para>
    /// </summary>
    [StructLayout(LayoutKind.Sequential)]
    private struct UiShapePush
    {
        /// <summary>NDC scale.xy + translate.xy — built ONLY by <see cref="OverlayRect"/> (hard rule 4).</summary>
        public Vector4 Rect;

        /// <summary>Gradient's near stop, straight alpha.</summary>
        public Vector4 Fill;

        /// <summary>Gradient's far stop; equal to <see cref="Fill"/> for a flat fill.</summary>
        public Vector4 FillEnd;

        /// <summary>Inset stroke colour.</summary>
        public Vector4 Border;

        /// <summary>Corner radii in pixels: top-left, top-right, bottom-right, bottom-left.</summary>
        public Vector4 Radii;

        /// <summary>Unpadded half-extent .xy, border width .z, feather radius .w.</summary>
        public Vector4 Shape;

        /// <summary>Gradient axis .xy, padded half-extent .zw.</summary>
        public Vector4 Extra;
    }

    private readonly Vk _vk;
    private readonly PhysicalDevice _physicalDevice;
    private readonly Device _device;

    private bool _pipelineReady;
    private Pipeline _pipeline;
    private PipelineLayout _pipelineLayout;
    private ShaderModule _vert;
    private ShaderModule _frag;
    private Silk.NET.Vulkan.Buffer _quadBuffer;
    private DeviceMemory _quadMemory;

    public UiShapePipeline(Vk vk, PhysicalDevice physicalDevice, Device device)
    {
        _vk = vk;
        _physicalDevice = physicalDevice;
        _device = device;
    }

    /// <summary>
    /// Records <paramref name="draws"/> in list order (painter's algorithm — see
    /// <see cref="UiShapeDraw"/>), creating the pipeline against <paramref name="renderPass"/> on
    /// first use. Unlike <see cref="OverlayImagePipeline"/> there is nothing to upload and therefore
    /// no pre-render-pass step: this is safe to call from inside an open render pass, which is where
    /// both renderers call it.
    /// </summary>
    /// <param name="cmd">The frame's command buffer, with its render pass already open.</param>
    /// <param name="draws">Shapes to record, back to front.</param>
    /// <param name="renderPass">The render pass the pipeline is created against on first use.</param>
    /// <param name="dynamicViewport">Whether viewport/scissor are set per frame by the caller
    /// (<see cref="WindowedRenderer"/>) rather than baked into the pipeline
    /// (<see cref="HeadlessRenderer"/>).</param>
    /// <param name="targetWidthPixels">Render-target width — the swapchain extent for the windowed
    /// renderer, read fresh each frame, which is what makes a HUD survive a resize.</param>
    /// <param name="targetHeightPixels">Render-target height.</param>
    public void Draw(
        CommandBuffer cmd,
        IReadOnlyList<UiShapeDraw> draws,
        RenderPass renderPass,
        bool dynamicViewport,
        uint targetWidthPixels,
        uint targetHeightPixels)
    {
        if (draws.Count == 0)
        {
            return;
        }

        EnsurePipeline(renderPass, dynamicViewport, targetWidthPixels, targetHeightPixels);

        _vk.CmdBindPipeline(cmd, PipelineBindPoint.Graphics, _pipeline);
        var buffer = _quadBuffer;
        ulong offset = 0;
        _vk.CmdBindVertexBuffers(cmd, 0, 1, &buffer, &offset);

        foreach (var draw in draws)
        {
            var push = BuildPush(draw, targetWidthPixels, targetHeightPixels);
            _vk.CmdPushConstants(cmd, _pipelineLayout,
                ShaderStageFlags.VertexBit | ShaderStageFlags.FragmentBit, 0, (uint)sizeof(UiShapePush), &push);
            _vk.CmdDraw(cmd, 6, 1, 0, 0);
        }
    }

    /// <summary>
    /// Packs one draw. Separated from <see cref="Draw"/> so the padding rule — the one piece of
    /// non-obvious arithmetic here — is stated once, next to its reason.
    /// <para>
    /// The quad is scaled to the shape's size PLUS <see cref="UiShapeDraw.PaddingPixels"/> on every
    /// side, while <c>Shape.xy</c> keeps the unpadded half-extent the SDF is evaluated against.
    /// Without that, a feathered edge would be clipped at the shape's own bounds and a drop shadow
    /// would come out as a hard-edged rectangle with a blurry inside.
    /// </para>
    /// </summary>
    private static UiShapePush BuildPush(UiShapeDraw draw, float targetWidthPixels, float targetHeightPixels)
    {
        var halfSize = Vector2.Max(draw.SizePixels, Vector2.Zero) * 0.5f;
        var paddedHalfSize = halfSize + new Vector2(draw.PaddingPixels);

        return new UiShapePush
        {
            // OverlayRect is the ONE place pixel space becomes NDC for every overlay path
            // (hard rule 4) — shared with the text and textured overlays so all three agree.
            Rect = OverlayRect.ToNdcScaleTranslate(draw.CenterPixels, paddedHalfSize * 2f, targetWidthPixels, targetHeightPixels),
            Fill = draw.FillColor,
            FillEnd = draw.EffectiveFillColorEnd,
            Border = draw.BorderColor,
            Radii = draw.CornerRadiiPixels,
            Shape = new Vector4(halfSize.X, halfSize.Y, MathF.Max(draw.BorderPixels, 0f), MathF.Max(draw.SoftnessPixels, 0f)),
            Extra = new Vector4(draw.GradientAxis.X, draw.GradientAxis.Y, paddedHalfSize.X, paddedHalfSize.Y),
        };
    }

    private void EnsurePipeline(RenderPass renderPass, bool dynamicViewport, uint width, uint height)
    {
        if (_pipelineReady)
        {
            return;
        }

        CreateQuadBuffer();

        _vert = CreateShaderModule(PrecompiledShaders.UiShapeVertSpirv);
        _frag = CreateShaderModule(PrecompiledShaders.UiShapeFragSpirv);

        var entry = (byte*)SilkMarshal.StringToPtr("main");
        try
        {
            var stages = stackalloc PipelineShaderStageCreateInfo[2];
            stages[0] = new PipelineShaderStageCreateInfo { SType = StructureType.PipelineShaderStageCreateInfo, Stage = ShaderStageFlags.VertexBit, Module = _vert, PName = entry };
            stages[1] = new PipelineShaderStageCreateInfo { SType = StructureType.PipelineShaderStageCreateInfo, Stage = ShaderStageFlags.FragmentBit, Module = _frag, PName = entry };

            // Position only, same unit-quad layout as both other overlay pipelines — the local pixel
            // coordinate the fragment stage needs is derived in the vertex shader from the padded
            // half-extent push constant, not from a second attribute.
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

            // The one genuine difference between the two hosts: HeadlessRenderer's target is a fixed
            // size so it bakes viewport/scissor; WindowedRenderer's window resizes, so it sets them
            // per frame as dynamic state (already set before this pipeline binds).
            var viewport = new Viewport { X = 0, Y = 0, Width = width, Height = height, MinDepth = 0, MaxDepth = 1 };
            var scissor = new Rect2D(new Offset2D(0, 0), new Extent2D(width, height));
            var viewportState = dynamicViewport
                ? new PipelineViewportStateCreateInfo { SType = StructureType.PipelineViewportStateCreateInfo, ViewportCount = 1, ScissorCount = 1 }
                : new PipelineViewportStateCreateInfo { SType = StructureType.PipelineViewportStateCreateInfo, ViewportCount = 1, PViewports = &viewport, ScissorCount = 1, PScissors = &scissor };

            var dynamicStates = stackalloc DynamicState[2] { DynamicState.Viewport, DynamicState.Scissor };
            var dynamicState = new PipelineDynamicStateCreateInfo { SType = StructureType.PipelineDynamicStateCreateInfo, DynamicStateCount = 2, PDynamicStates = dynamicStates };

            var rasterizer = new PipelineRasterizationStateCreateInfo { SType = StructureType.PipelineRasterizationStateCreateInfo, PolygonMode = PolygonMode.Fill, CullMode = CullModeFlags.None, FrontFace = FrontFace.CounterClockwise, LineWidth = 1f };
            var multisample = new PipelineMultisampleStateCreateInfo { SType = StructureType.PipelineMultisampleStateCreateInfo, RasterizationSamples = SampleCountFlags.Count1Bit, MinSampleShading = 1f };
            var depthStencil = new PipelineDepthStencilStateCreateInfo { SType = StructureType.PipelineDepthStencilStateCreateInfo, DepthTestEnable = false, DepthWriteEnable = false, DepthCompareOp = CompareOp.Always };

            // Identical blend factors to OverlayImagePipeline's, and for the identical reason: UI
            // shapes stack (a shadow under a card under a highlight), so destination alpha has to
            // accumulate as out.a = src.a + dst.a * (1 - src.a). See that class's own note on why
            // this deliberately differs from the RenderToTexture composite's One/Zero.
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

            var pushRange = new PushConstantRange { StageFlags = ShaderStageFlags.VertexBit | ShaderStageFlags.FragmentBit, Offset = 0, Size = (uint)sizeof(UiShapePush) };
            var layoutInfo = new PipelineLayoutCreateInfo { SType = StructureType.PipelineLayoutCreateInfo, PushConstantRangeCount = 1, PPushConstantRanges = &pushRange };
            Check(_vk.CreatePipelineLayout(_device, in layoutInfo, null, out _pipelineLayout), "CreatePipelineLayout(uiShape)");

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
            Check(_vk.CreateGraphicsPipelines(_device, default, 1, in pipelineInfo, null, out _pipeline), "CreateGraphicsPipelines(uiShape)");
        }
        finally
        {
            SilkMarshal.Free((nint)entry);
        }

        _pipelineReady = true;
    }

    /// <summary>Its own copy of the shared unit quad rather than borrowing another overlay path's —
    /// same reasoning as <see cref="OverlayImagePipeline"/>'s: those buffers are created lazily by
    /// their own pipelines, so a screen made only of shapes would bind a null handle.</summary>
    private void CreateQuadBuffer()
    {
        // Identical geometry and winding to the other overlay quads — CCW read as Y-up, clockwise on
        // screen under Y-down NDC, which is harmless only because CullMode is None.
        float[] vertices =
        [
            -0.5f, -0.5f, 0.5f, -0.5f, 0.5f, 0.5f,
            -0.5f, -0.5f, 0.5f, 0.5f, -0.5f, 0.5f,
        ];
        var size = (ulong)(vertices.Length * sizeof(float));
        var info = new BufferCreateInfo { SType = StructureType.BufferCreateInfo, Size = size, Usage = BufferUsageFlags.VertexBufferBit, SharingMode = SharingMode.Exclusive };
        Check(_vk.CreateBuffer(_device, in info, null, out _quadBuffer), "CreateBuffer(uiShapeQuad)");

        _vk.GetBufferMemoryRequirements(_device, _quadBuffer, out var req);
        _quadMemory = AllocateMemory(req, MemoryPropertyFlags.HostVisibleBit | MemoryPropertyFlags.HostCoherentBit);
        Check(_vk.BindBufferMemory(_device, _quadBuffer, _quadMemory, 0), "BindBufferMemory(uiShapeQuad)");

        void* mapped = null;
        Check(_vk.MapMemory(_device, _quadMemory, 0, size, 0, ref mapped), "MapMemory(uiShapeQuad)");
        vertices.AsSpan().CopyTo(new Span<float>(mapped, vertices.Length));
        _vk.UnmapMemory(_device, _quadMemory);
    }

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
            Check(_vk.CreateShaderModule(_device, in info, null, out var module), "CreateShaderModule(uiShape)");
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
                Check(_vk.AllocateMemory(_device, in allocInfo, null, out var memory), "AllocateMemory(uiShape)");
                return memory;
            }
        }

        throw new InvalidOperationException($"No Vulkan memory type satisfies {flags} for the UI-shape allocation.");
    }

    private static void Check(Result result, string what)
    {
        if (result != Result.Success)
        {
            throw new InvalidOperationException($"Vulkan {what} failed: {result}");
        }
    }

    /// <summary>Destroys every resource in reverse-of-creation order. Each guard means a renderer
    /// that never drew a single shape destroys nothing — all handles start zeroed.</summary>
    public void Dispose()
    {
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
