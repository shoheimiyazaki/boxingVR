using System.Numerics;
using System.Runtime.InteropServices;
using Silk.NET.Core.Native;
using Silk.NET.Shaderc;
using Silk.NET.Vulkan;

namespace Aigis.Render;

/// <summary>
/// P4 foundation (a prerequisite for TASK-093's blood decals, not the decals themselves):
/// a generic render-to-texture primitive. An offscreen color attachment is drawn into by
/// its own render pass, then sampled back via a combined-image-sampler descriptor set and
/// alpha-blended onto the main color target by a second pass. This is the engine's first
/// sampler-backed descriptor set — the only prior descriptor set
/// (<see cref="EnsureLightingDescriptor"/>, TASK-036) is a plain uniform buffer with no
/// image/sampler involved. <see cref="RenderOffscreenThenComposite"/> draws a hardcoded
/// triangle purely to prove the pipeline end to end; no game content lives here — blood
/// decals (TASK-093) are a separate, not-yet-started piece of work that will reuse this.
/// </summary>
public sealed unsafe partial class HeadlessRenderer
{
    [StructLayout(LayoutKind.Sequential)]
    private struct CompositePush
    {
        public Vector4 Tint;
    }

    /// <summary>Fullscreen triangle from <c>gl_VertexIndex</c> alone (no vertex buffer) —
    /// the standard trick: 3 vertices that overshoot the screen so the clipped result covers
    /// every pixel exactly once, cheaper than a 4-vertex/6-index quad.</summary>
    private const string CompositeVertSource = """
        #version 450
        layout(location = 0) out vec2 vUv;
        vec2 positions[3] = vec2[](vec2(-1.0, -1.0), vec2(3.0, -1.0), vec2(-1.0, 3.0));
        void main() {
            gl_Position = vec4(positions[gl_VertexIndex], 0.0, 1.0);
            vUv = positions[gl_VertexIndex] * 0.5 + 0.5;
        }
        """;

    /// <summary>Samples the offscreen texture and re-uses its alpha channel as
    /// <c>pc.tint.a</c> so callers control blend strength without a second texture read —
    /// good enough for proving the primitive; a real decal shader would read alpha from the
    /// texture itself.</summary>
    private const string CompositeFragSource = """
        #version 450
        layout(location = 0) in vec2 vUv;
        layout(location = 0) out vec4 outColor;
        layout(set = 0, binding = 0) uniform sampler2D tex;
        layout(push_constant) uniform Push { vec4 tint; } pc;
        void main() {
            vec4 sampled = texture(tex, vUv);
            outColor = vec4(sampled.rgb, pc.tint.a);
        }
        """;

    private bool _renderToTextureReady;

    private Image _offscreenImage;
    private DeviceMemory _offscreenMemory;
    private ImageView _offscreenView;
    private RenderPass _offscreenRenderPass;
    private Framebuffer _offscreenFramebuffer;

    private Sampler _offscreenSampler;
    private DescriptorSetLayout _offscreenSetLayout;
    private DescriptorPool _offscreenPool;
    private DescriptorSet _offscreenSet;

    private Pipeline _compositePipeline;
    private PipelineLayout _compositePipelineLayout;
    private ShaderModule _compositeVert;
    private ShaderModule _compositeFrag;

    /// <summary>
    /// Renders a triangle into an offscreen texture (render-to-texture), then composites that
    /// texture onto the main color target through a sampler with alpha blending —
    /// <paramref name="tint"/>.W is the blend strength (0 = background only, 1 = offscreen
    /// texture only); <paramref name="tint"/>.XYZ is currently unused (the composite shader
    /// outputs the sampled texture's own RGB, not a tinted color — see
    /// <see cref="CompositeFragSource"/>). Exercises the whole P4 primitive in one call: offscreen color
    /// attachment -> render pass draws into it -> automatic layout transition to
    /// shader-read-only -> combined-image-sampler descriptor set -> blended fullscreen
    /// composite pass. Reuses <see cref="_trianglePipeline"/> for the offscreen draw — its
    /// render pass differs only in <c>finalLayout</c>, which Vulkan render-pass
    /// compatibility (matching attachment format + sample count) does not require to match.
    /// </summary>
    public byte[] RenderOffscreenThenComposite(Vector4 offscreenClear, Vector4 backgroundClear, Vector4 tint)
    {
        EnsureTrianglePipeline();
        EnsureRenderToTexture();

        var cmd = BeginCommands();

        BeginOffscreenRenderPass(cmd, offscreenClear);
        _vk.CmdBindPipeline(cmd, PipelineBindPoint.Graphics, _trianglePipeline);
        _vk.CmdDraw(cmd, 3, 1, 0, 0);
        _vk.CmdEndRenderPass(cmd); // finalLayout => ShaderReadOnlyOptimal (see CreateOffscreenRenderPass)

        BeginRenderPass(cmd, backgroundClear);
        _vk.CmdBindPipeline(cmd, PipelineBindPoint.Graphics, _compositePipeline);
        var set = _offscreenSet;
        _vk.CmdBindDescriptorSets(cmd, PipelineBindPoint.Graphics, _compositePipelineLayout, 0, 1, &set, 0, null);
        var push = new CompositePush { Tint = tint };
        _vk.CmdPushConstants(cmd, _compositePipelineLayout, ShaderStageFlags.FragmentBit, 0, (uint)sizeof(CompositePush), &push);
        _vk.CmdDraw(cmd, 3, 1, 0, 0);
        _vk.CmdEndRenderPass(cmd); // finalLayout => TransferSrcOptimal (shared _renderPass, see CreateRenderPass)

        CopyColorToReadback(cmd);
        EndAndSubmit(cmd);
        return ReadPixels();
    }

    private void EnsureRenderToTexture()
    {
        if (_renderToTextureReady)
        {
            return;
        }

        CreateOffscreenColorTarget();
        CreateOffscreenRenderPass();
        CreateOffscreenFramebuffer();
        CreateOffscreenSampler();
        CreateOffscreenDescriptor();
        CreateCompositePipeline();

        _renderToTextureReady = true;
    }

    private void CreateOffscreenColorTarget()
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
            // ColorAttachmentBit: the offscreen render pass draws into it. SampledBit: the
            // composite pass reads it back through a sampler.
            Usage = ImageUsageFlags.ColorAttachmentBit | ImageUsageFlags.SampledBit,
            SharingMode = SharingMode.Exclusive,
            InitialLayout = ImageLayout.Undefined,
        };
        Check(_vk.CreateImage(_device, in imageInfo, null, out _offscreenImage), "CreateImage(offscreen)");

        _vk.GetImageMemoryRequirements(_device, _offscreenImage, out var req);
        _offscreenMemory = AllocateMemory(req, MemoryPropertyFlags.DeviceLocalBit);
        Check(_vk.BindImageMemory(_device, _offscreenImage, _offscreenMemory, 0), "BindImageMemory(offscreen)");

        var viewInfo = new ImageViewCreateInfo
        {
            SType = StructureType.ImageViewCreateInfo,
            Image = _offscreenImage,
            ViewType = ImageViewType.Type2D,
            Format = ColorFormat,
            SubresourceRange = new ImageSubresourceRange(ImageAspectFlags.ColorBit, 0, 1, 0, 1),
        };
        Check(_vk.CreateImageView(_device, in viewInfo, null, out _offscreenView), "CreateImageView(offscreen)");
    }

    /// <summary>The attachment's <c>finalLayout</c> does the
    /// ColorAttachmentOptimal -> ShaderReadOnlyOptimal transition for us (no manual
    /// <c>vkCmdPipelineBarrier</c> needed), and the subpass-to-external dependency makes that
    /// color write visible to the fragment shader's texture read in the composite pass that
    /// follows in the same command buffer — same srcAccess/dstAccess reasoning as
    /// <see cref="ReadbackDependency"/>, just targeting the fragment-shader stage instead of
    /// the transfer stage.</summary>
    private void CreateOffscreenRenderPass()
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
            FinalLayout = ImageLayout.ShaderReadOnlyOptimal,
        };
        var colorRef = new AttachmentReference(0, ImageLayout.ColorAttachmentOptimal);
        var subpass = new SubpassDescription
        {
            PipelineBindPoint = PipelineBindPoint.Graphics,
            ColorAttachmentCount = 1,
            PColorAttachments = &colorRef,
        };
        var dependency = new SubpassDependency
        {
            SrcSubpass = 0,
            DstSubpass = Vk.SubpassExternal,
            SrcStageMask = PipelineStageFlags.ColorAttachmentOutputBit,
            SrcAccessMask = AccessFlags.ColorAttachmentWriteBit,
            DstStageMask = PipelineStageFlags.FragmentShaderBit,
            DstAccessMask = AccessFlags.ShaderReadBit,
            DependencyFlags = DependencyFlags.ByRegionBit,
        };

        var info = new RenderPassCreateInfo
        {
            SType = StructureType.RenderPassCreateInfo,
            AttachmentCount = 1,
            PAttachments = &colorAttachment,
            SubpassCount = 1,
            PSubpasses = &subpass,
            DependencyCount = 1,
            PDependencies = &dependency,
        };
        Check(_vk.CreateRenderPass(_device, in info, null, out _offscreenRenderPass), "CreateRenderPass(offscreen)");
    }

    private void CreateOffscreenFramebuffer()
    {
        var view = _offscreenView;
        var info = new FramebufferCreateInfo
        {
            SType = StructureType.FramebufferCreateInfo,
            RenderPass = _offscreenRenderPass,
            AttachmentCount = 1,
            PAttachments = &view,
            Width = _width,
            Height = _height,
            Layers = 1,
        };
        Check(_vk.CreateFramebuffer(_device, in info, null, out _offscreenFramebuffer), "CreateFramebuffer(offscreen)");
    }

    private void CreateOffscreenSampler()
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
            MinLod = 0,
            MaxLod = 0,
            BorderColor = BorderColor.FloatOpaqueBlack,
        };
        Check(_vk.CreateSampler(_device, in info, null, out _offscreenSampler), "CreateSampler(offscreen)");
    }

    /// <summary>The engine's first combined-image-sampler descriptor set — mirrors the
    /// layout/pool/set/write shape of <see cref="EnsureLightingDescriptor"/> (TASK-036) but
    /// with a <see cref="DescriptorType.CombinedImageSampler"/> instead of a uniform buffer,
    /// and a <see cref="DescriptorImageInfo"/> instead of a <see cref="DescriptorBufferInfo"/>.
    /// One persistent set is enough for the same reason as the lighting one: this renderer
    /// never has more than one frame in flight.</summary>
    private void CreateOffscreenDescriptor()
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
        Check(_vk.CreateDescriptorSetLayout(_device, in setLayoutInfo, null, out _offscreenSetLayout), "CreateDescriptorSetLayout(offscreen)");

        var poolSize = new DescriptorPoolSize { Type = DescriptorType.CombinedImageSampler, DescriptorCount = 1 };
        var poolInfo = new DescriptorPoolCreateInfo
        {
            SType = StructureType.DescriptorPoolCreateInfo,
            MaxSets = 1,
            PoolSizeCount = 1,
            PPoolSizes = &poolSize,
        };
        Check(_vk.CreateDescriptorPool(_device, in poolInfo, null, out _offscreenPool), "CreateDescriptorPool(offscreen)");

        var setLayout = _offscreenSetLayout;
        var allocInfo = new DescriptorSetAllocateInfo
        {
            SType = StructureType.DescriptorSetAllocateInfo,
            DescriptorPool = _offscreenPool,
            DescriptorSetCount = 1,
            PSetLayouts = &setLayout,
        };
        Check(_vk.AllocateDescriptorSets(_device, in allocInfo, out _offscreenSet), "AllocateDescriptorSets(offscreen)");

        var imageInfo = new DescriptorImageInfo
        {
            Sampler = _offscreenSampler,
            ImageView = _offscreenView,
            ImageLayout = ImageLayout.ShaderReadOnlyOptimal,
        };
        var write = new WriteDescriptorSet
        {
            SType = StructureType.WriteDescriptorSet,
            DstSet = _offscreenSet,
            DstBinding = 0,
            DescriptorCount = 1,
            DescriptorType = DescriptorType.CombinedImageSampler,
            PImageInfo = &imageInfo,
        };
        _vk.UpdateDescriptorSets(_device, 1, in write, 0, null);
    }

    private void CreateCompositePipeline()
    {
        _shaderCompiler ??= new ShaderCompiler();
        _compositeVert = CreateShaderModule(_shaderCompiler.Compile(CompositeVertSource, ShaderKind.VertexShader, "composite.vert"));
        _compositeFrag = CreateShaderModule(_shaderCompiler.Compile(CompositeFragSource, ShaderKind.FragmentShader, "composite.frag"));

        var entry = (byte*)SilkMarshal.StringToPtr("main");
        try
        {
            var stages = stackalloc PipelineShaderStageCreateInfo[2];
            stages[0] = new PipelineShaderStageCreateInfo
            {
                SType = StructureType.PipelineShaderStageCreateInfo,
                Stage = ShaderStageFlags.VertexBit,
                Module = _compositeVert,
                PName = entry,
            };
            stages[1] = new PipelineShaderStageCreateInfo
            {
                SType = StructureType.PipelineShaderStageCreateInfo,
                Stage = ShaderStageFlags.FragmentBit,
                Module = _compositeFrag,
                PName = entry,
            };

            var vertexInput = new PipelineVertexInputStateCreateInfo { SType = StructureType.PipelineVertexInputStateCreateInfo };
            var inputAssembly = new PipelineInputAssemblyStateCreateInfo
            {
                SType = StructureType.PipelineInputAssemblyStateCreateInfo,
                Topology = PrimitiveTopology.TriangleList,
            };

            var viewport = new Viewport { X = 0, Y = 0, Width = _width, Height = _height, MinDepth = 0, MaxDepth = 1 };
            var scissor = new Rect2D(new Offset2D(0, 0), new Extent2D(_width, _height));
            var viewportState = new PipelineViewportStateCreateInfo
            {
                SType = StructureType.PipelineViewportStateCreateInfo,
                ViewportCount = 1,
                PViewports = &viewport,
                ScissorCount = 1,
                PScissors = &scissor,
            };

            var rasterizer = new PipelineRasterizationStateCreateInfo
            {
                SType = StructureType.PipelineRasterizationStateCreateInfo,
                PolygonMode = PolygonMode.Fill,
                CullMode = CullModeFlags.None,
                FrontFace = FrontFace.CounterClockwise,
                LineWidth = 1f,
            };
            var multisample = new PipelineMultisampleStateCreateInfo
            {
                SType = StructureType.PipelineMultisampleStateCreateInfo,
                RasterizationSamples = SampleCountFlags.Count1Bit,
                MinSampleShading = 1f,
            };

            // The one basic blend mode P4 asks for: standard (non-premultiplied) alpha blend
            // — what blood decals (TASK-093) need; additive is not required here.
            var blendAttachment = new PipelineColorBlendAttachmentState
            {
                BlendEnable = true,
                SrcColorBlendFactor = BlendFactor.SrcAlpha,
                DstColorBlendFactor = BlendFactor.OneMinusSrcAlpha,
                ColorBlendOp = BlendOp.Add,
                SrcAlphaBlendFactor = BlendFactor.One,
                DstAlphaBlendFactor = BlendFactor.Zero,
                AlphaBlendOp = BlendOp.Add,
                ColorWriteMask = ColorComponentFlags.RBit | ColorComponentFlags.GBit | ColorComponentFlags.BBit | ColorComponentFlags.ABit,
            };
            var colorBlend = new PipelineColorBlendStateCreateInfo
            {
                SType = StructureType.PipelineColorBlendStateCreateInfo,
                AttachmentCount = 1,
                PAttachments = &blendAttachment,
            };

            var pushRange = new PushConstantRange
            {
                StageFlags = ShaderStageFlags.FragmentBit,
                Offset = 0,
                Size = (uint)sizeof(CompositePush),
            };
            var setLayout = _offscreenSetLayout;
            var layoutInfo = new PipelineLayoutCreateInfo
            {
                SType = StructureType.PipelineLayoutCreateInfo,
                SetLayoutCount = 1,
                PSetLayouts = &setLayout,
                PushConstantRangeCount = 1,
                PPushConstantRanges = &pushRange,
            };
            Check(_vk.CreatePipelineLayout(_device, in layoutInfo, null, out _compositePipelineLayout), "CreatePipelineLayout(composite)");

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
                PColorBlendState = &colorBlend,
                Layout = _compositePipelineLayout,
                RenderPass = _renderPass,
                Subpass = 0,
            };
            Check(_vk.CreateGraphicsPipelines(_device, default, 1, in pipelineInfo, null, out _compositePipeline), "CreateGraphicsPipelines(composite)");
        }
        finally
        {
            SilkMarshal.Free((nint)entry);
        }
    }

    private void BeginOffscreenRenderPass(CommandBuffer cmd, Vector4 color)
    {
        var clear = new ClearValue { Color = new ClearColorValue(color.X, color.Y, color.Z, color.W) };
        var info = new RenderPassBeginInfo
        {
            SType = StructureType.RenderPassBeginInfo,
            RenderPass = _offscreenRenderPass,
            Framebuffer = _offscreenFramebuffer,
            RenderArea = new Rect2D(new Offset2D(0, 0), new Extent2D(_width, _height)),
            ClearValueCount = 1,
            PClearValues = &clear,
        };
        _vk.CmdBeginRenderPass(cmd, in info, SubpassContents.Inline);
    }

    private void DisposeRenderToTextureResources()
    {
        if (_compositePipeline.Handle != 0)
        {
            _vk.DestroyPipeline(_device, _compositePipeline, null);
        }

        if (_compositePipelineLayout.Handle != 0)
        {
            _vk.DestroyPipelineLayout(_device, _compositePipelineLayout, null);
        }

        if (_compositeVert.Handle != 0)
        {
            _vk.DestroyShaderModule(_device, _compositeVert, null);
        }

        if (_compositeFrag.Handle != 0)
        {
            _vk.DestroyShaderModule(_device, _compositeFrag, null);
        }

        if (_offscreenPool.Handle != 0)
        {
            // Freeing the pool also frees _offscreenSet — same pattern as _lightingPool.
            _vk.DestroyDescriptorPool(_device, _offscreenPool, null);
        }

        if (_offscreenSetLayout.Handle != 0)
        {
            _vk.DestroyDescriptorSetLayout(_device, _offscreenSetLayout, null);
        }

        if (_offscreenSampler.Handle != 0)
        {
            _vk.DestroySampler(_device, _offscreenSampler, null);
        }

        if (_offscreenFramebuffer.Handle != 0)
        {
            _vk.DestroyFramebuffer(_device, _offscreenFramebuffer, null);
        }

        if (_offscreenRenderPass.Handle != 0)
        {
            _vk.DestroyRenderPass(_device, _offscreenRenderPass, null);
        }

        if (_offscreenView.Handle != 0)
        {
            _vk.DestroyImageView(_device, _offscreenView, null);
        }

        if (_offscreenImage.Handle != 0)
        {
            _vk.DestroyImage(_device, _offscreenImage, null);
        }

        if (_offscreenMemory.Handle != 0)
        {
            _vk.FreeMemory(_device, _offscreenMemory, null);
        }
    }
}
