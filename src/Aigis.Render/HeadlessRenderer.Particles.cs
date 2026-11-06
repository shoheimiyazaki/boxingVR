using System.Numerics;
using System.Runtime.InteropServices;
using Silk.NET.Core.Native;
using Silk.NET.Vulkan;

namespace Aigis.Render;

/// <summary>
/// Camera-facing particle billboards (dust, blood, fire, smoke, sparks), drawn into the box
/// render pass after the 3D scene with depth test ON and depth write OFF: an effect is hidden by
/// the wall in front of it, but never hides — or z-fights with — another particle.
/// <para>
/// Two pipelines, one shader pair, one draw each: alpha-blended particles (sorted far-to-near by
/// <see cref="ParticlePacker"/>) then additive ones. Blend state is baked into a Vulkan pipeline,
/// so "one pipeline with a switchable blend mode" is not a thing that exists; splitting the batch
/// by blend on the CPU is what makes both modes available in a single scene.
/// </para>
/// <para>
/// Instanced, unlike every other pipeline here: one shared 6-vertex quad plus a per-instance
/// buffer, so a thousand particles cost one <c>vkCmdDraw</c> rather than a thousand
/// push-constant updates the way <see cref="RenderPrimitiveScene"/>'s cubes do. That difference
/// is the reason particles can be a per-frame-rebuilt crowd at all.
/// </para>
/// </summary>
public sealed unsafe partial class HeadlessRenderer
{
    [StructLayout(LayoutKind.Sequential)]
    private struct ParticlePush
    {
        public Matrix4x4 ViewProjection;
        public Vector4 Right;   // Camera basis, xyz used; vec4 for std430-friendly 16-byte slots.
        public Vector4 Up;
    }

    private readonly ParticlePacker _particlePacker = new();
    private bool _particleReady;
    private Pipeline _particleAlphaPipeline;
    private Pipeline _particleAdditivePipeline;
    private PipelineLayout _particlePipelineLayout;
    private ShaderModule _particleVert;
    private ShaderModule _particleFrag;
    private Silk.NET.Vulkan.Buffer _particleQuadBuffer;
    private DeviceMemory _particleQuadMemory;
    private Silk.NET.Vulkan.Buffer _particleInstanceBuffer;
    private DeviceMemory _particleInstanceMemory;
    private int _particleInstanceCapacity;

    /// <summary>
    /// Packs and uploads <paramref name="particles"/> for this frame, building the pipelines on
    /// first use. Must run before command recording begins — it maps host-visible memory and can
    /// create pipelines, and every sibling pipeline here is likewise built outside the render
    /// pass (mirroring <see cref="UploadLines"/> + <see cref="EnsureLinePipeline"/>).
    /// <para>
    /// The build stays lazy — gated on there actually being particles — rather than being hoisted
    /// unconditionally next to <see cref="EnsureBoxPipeline"/>: a scene with no effects should not
    /// pay two pipeline compilations, which is exactly the cold-start cost
    /// <see cref="PrecompiledShaders"/> exists to avoid.
    /// </para>
    /// </summary>
    private void UploadParticles(IReadOnlyList<ParticleInstance> particles, Matrix4x4 viewProjection)
    {
        _particlePacker.Pack(particles, viewProjection);
        if (_particlePacker.TotalCount == 0)
        {
            return;
        }

        EnsureParticlePipelines();
        EnsureParticleCapacity(_particlePacker.TotalCount);

        var data = _particlePacker.Data;
        var size = (ulong)(data.Length * sizeof(float));
        void* mapped = null;
        Check(_vk.MapMemory(_device, _particleInstanceMemory, 0, size, 0, ref mapped), "MapMemory(particles)");
        data.CopyTo(new Span<float>(mapped, data.Length));
        _vk.UnmapMemory(_device, _particleInstanceMemory);
    }

    private void DrawParticles(CommandBuffer cmd, Matrix4x4 viewProjection)
    {
        if (_particlePacker.TotalCount == 0)
        {
            return;
        }

        var buffers = stackalloc Silk.NET.Vulkan.Buffer[2] { _particleQuadBuffer, _particleInstanceBuffer };
        var offsets = stackalloc ulong[2] { 0, 0 };
        _vk.CmdBindVertexBuffers(cmd, 0, 2, buffers, offsets);

        var (right, up) = ParticlePacker.CameraBasis(viewProjection);
        var push = new ParticlePush
        {
            ViewProjection = viewProjection,
            Right = new Vector4(right, 0f),
            Up = new Vector4(up, 0f),
        };
        _vk.CmdPushConstants(cmd, _particlePipelineLayout, ShaderStageFlags.VertexBit, 0, (uint)sizeof(ParticlePush), &push);

        if (_particlePacker.AlphaCount > 0)
        {
            _vk.CmdBindPipeline(cmd, PipelineBindPoint.Graphics, _particleAlphaPipeline);
            _vk.CmdDraw(cmd, ParticleQuad.VertexCount, (uint)_particlePacker.AlphaCount, 0, 0);
        }

        if (_particlePacker.AdditiveCount > 0)
        {
            // firstInstance skips the alpha block: the packer laid both out in one buffer,
            // alpha first, precisely so this is an offset rather than a second upload.
            _vk.CmdBindPipeline(cmd, PipelineBindPoint.Graphics, _particleAdditivePipeline);
            _vk.CmdDraw(cmd, ParticleQuad.VertexCount, (uint)_particlePacker.AdditiveCount, 0, (uint)_particlePacker.AlphaCount);
        }
    }

    private void EnsureParticlePipelines()
    {
        if (_particleReady)
        {
            return;
        }

        CreateParticleQuadBuffer();
        _particleVert = CreateShaderModule(PrecompiledShaders.ParticleVertSpirv);
        _particleFrag = CreateShaderModule(PrecompiledShaders.ParticleFragSpirv);

        var entry = (byte*)SilkMarshal.StringToPtr("main");
        try
        {
            var stages = stackalloc PipelineShaderStageCreateInfo[2];
            stages[0] = new PipelineShaderStageCreateInfo { SType = StructureType.PipelineShaderStageCreateInfo, Stage = ShaderStageFlags.VertexBit, Module = _particleVert, PName = entry };
            stages[1] = new PipelineShaderStageCreateInfo { SType = StructureType.PipelineShaderStageCreateInfo, Stage = ShaderStageFlags.FragmentBit, Module = _particleFrag, PName = entry };

            var bindings = stackalloc VertexInputBindingDescription[2];
            bindings[0] = new VertexInputBindingDescription { Binding = 0, Stride = ParticleQuad.StrideBytes, InputRate = VertexInputRate.Vertex };
            bindings[1] = new VertexInputBindingDescription { Binding = 1, Stride = ParticlePacker.FloatsPerInstance * sizeof(float), InputRate = VertexInputRate.Instance };
            var attributes = stackalloc VertexInputAttributeDescription[5];
            attributes[0] = new VertexInputAttributeDescription { Location = 0, Binding = 0, Format = Format.R32G32Sfloat, Offset = 0 };
            attributes[1] = new VertexInputAttributeDescription { Location = 1, Binding = 1, Format = Format.R32G32B32Sfloat, Offset = 0 };
            attributes[2] = new VertexInputAttributeDescription { Location = 2, Binding = 1, Format = Format.R32Sfloat, Offset = 3 * sizeof(float) };
            attributes[3] = new VertexInputAttributeDescription { Location = 3, Binding = 1, Format = Format.R32G32B32A32Sfloat, Offset = 4 * sizeof(float) };
            attributes[4] = new VertexInputAttributeDescription { Location = 4, Binding = 1, Format = Format.R32Sfloat, Offset = 8 * sizeof(float) };
            var vertexInput = new PipelineVertexInputStateCreateInfo
            {
                SType = StructureType.PipelineVertexInputStateCreateInfo,
                VertexBindingDescriptionCount = 2,
                PVertexBindingDescriptions = bindings,
                VertexAttributeDescriptionCount = 5,
                PVertexAttributeDescriptions = attributes,
            };

            var inputAssembly = new PipelineInputAssemblyStateCreateInfo { SType = StructureType.PipelineInputAssemblyStateCreateInfo, Topology = PrimitiveTopology.TriangleList };
            var viewport = new Viewport { X = 0, Y = 0, Width = _width, Height = _height, MinDepth = 0, MaxDepth = 1 };
            var scissor = new Rect2D(new Offset2D(0, 0), new Extent2D(_width, _height));
            var viewportState = new PipelineViewportStateCreateInfo { SType = StructureType.PipelineViewportStateCreateInfo, ViewportCount = 1, PViewports = &viewport, ScissorCount = 1, PScissors = &scissor };
            var rasterizer = new PipelineRasterizationStateCreateInfo { SType = StructureType.PipelineRasterizationStateCreateInfo, PolygonMode = PolygonMode.Fill, CullMode = CullModeFlags.None, FrontFace = FrontFace.CounterClockwise, LineWidth = 1f };
            var multisample = new PipelineMultisampleStateCreateInfo { SType = StructureType.PipelineMultisampleStateCreateInfo, RasterizationSamples = SampleCountFlags.Count1Bit, MinSampleShading = 1f };
            var depthStencil = new PipelineDepthStencilStateCreateInfo
            {
                SType = StructureType.PipelineDepthStencilStateCreateInfo,
                DepthTestEnable = true,
                DepthWriteEnable = false,
                DepthCompareOp = CompareOp.Less,
            };

            var pushRange = new PushConstantRange { StageFlags = ShaderStageFlags.VertexBit, Offset = 0, Size = (uint)sizeof(ParticlePush) };
            var layoutInfo = new PipelineLayoutCreateInfo { SType = StructureType.PipelineLayoutCreateInfo, PushConstantRangeCount = 1, PPushConstantRanges = &pushRange };
            Check(_vk.CreatePipelineLayout(_device, in layoutInfo, null, out _particlePipelineLayout), "CreatePipelineLayout(particle)");

            var alphaBlend = ParticleQuad.BlendState(ParticleBlend.Alpha);
            var additiveBlend = ParticleQuad.BlendState(ParticleBlend.Additive);
            var alphaColorBlend = new PipelineColorBlendStateCreateInfo { SType = StructureType.PipelineColorBlendStateCreateInfo, AttachmentCount = 1, PAttachments = &alphaBlend };
            var additiveColorBlend = new PipelineColorBlendStateCreateInfo { SType = StructureType.PipelineColorBlendStateCreateInfo, AttachmentCount = 1, PAttachments = &additiveBlend };

            var template = new GraphicsPipelineCreateInfo
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
                PColorBlendState = &alphaColorBlend,
                Layout = _particlePipelineLayout,
                RenderPass = _boxRenderPass,
                Subpass = 0,
            };
            Check(_vk.CreateGraphicsPipelines(_device, default, 1, in template, null, out _particleAlphaPipeline), "CreateGraphicsPipelines(particleAlpha)");

            template.PColorBlendState = &additiveColorBlend;
            Check(_vk.CreateGraphicsPipelines(_device, default, 1, in template, null, out _particleAdditivePipeline), "CreateGraphicsPipelines(particleAdditive)");
        }
        finally
        {
            SilkMarshal.Free((nint)entry);
        }

        _particleReady = true;
    }

    private void CreateParticleQuadBuffer()
    {
        var vertices = ParticleQuad.Vertices;
        var size = (ulong)(vertices.Length * sizeof(float));
        var info = new BufferCreateInfo { SType = StructureType.BufferCreateInfo, Size = size, Usage = BufferUsageFlags.VertexBufferBit, SharingMode = SharingMode.Exclusive };
        Check(_vk.CreateBuffer(_device, in info, null, out _particleQuadBuffer), "CreateBuffer(particleQuad)");

        _vk.GetBufferMemoryRequirements(_device, _particleQuadBuffer, out var requirements);
        _particleQuadMemory = AllocateMemory(requirements, MemoryPropertyFlags.HostVisibleBit | MemoryPropertyFlags.HostCoherentBit);
        Check(_vk.BindBufferMemory(_device, _particleQuadBuffer, _particleQuadMemory, 0), "BindBufferMemory(particleQuad)");

        void* mapped = null;
        Check(_vk.MapMemory(_device, _particleQuadMemory, 0, size, 0, ref mapped), "MapMemory(particleQuad)");
        vertices.AsSpan().CopyTo(new Span<float>(mapped, vertices.Length));
        _vk.UnmapMemory(_device, _particleQuadMemory);
    }

    private void EnsureParticleCapacity(int instances)
    {
        if (instances <= _particleInstanceCapacity)
        {
            return;
        }

        if (_particleInstanceBuffer.Handle != 0)
        {
            _vk.DestroyBuffer(_device, _particleInstanceBuffer, null);
            _vk.FreeMemory(_device, _particleInstanceMemory, null);
        }

        _particleInstanceCapacity = Math.Max(instances, ParticleQuad.InstanceMinimumCapacity);
        var size = (ulong)(_particleInstanceCapacity * ParticlePacker.FloatsPerInstance * sizeof(float));
        var info = new BufferCreateInfo { SType = StructureType.BufferCreateInfo, Size = size, Usage = BufferUsageFlags.VertexBufferBit, SharingMode = SharingMode.Exclusive };
        Check(_vk.CreateBuffer(_device, in info, null, out _particleInstanceBuffer), "CreateBuffer(particleInstances)");

        _vk.GetBufferMemoryRequirements(_device, _particleInstanceBuffer, out var requirements);
        _particleInstanceMemory = AllocateMemory(requirements, MemoryPropertyFlags.HostVisibleBit | MemoryPropertyFlags.HostCoherentBit);
        Check(_vk.BindBufferMemory(_device, _particleInstanceBuffer, _particleInstanceMemory, 0), "BindBufferMemory(particleInstances)");
    }

    private void DisposeParticleResources()
    {
        if (_particleAlphaPipeline.Handle != 0)
        {
            _vk.DestroyPipeline(_device, _particleAlphaPipeline, null);
        }

        if (_particleAdditivePipeline.Handle != 0)
        {
            _vk.DestroyPipeline(_device, _particleAdditivePipeline, null);
        }

        if (_particlePipelineLayout.Handle != 0)
        {
            _vk.DestroyPipelineLayout(_device, _particlePipelineLayout, null);
        }

        if (_particleVert.Handle != 0)
        {
            _vk.DestroyShaderModule(_device, _particleVert, null);
        }

        if (_particleFrag.Handle != 0)
        {
            _vk.DestroyShaderModule(_device, _particleFrag, null);
        }

        if (_particleQuadBuffer.Handle != 0)
        {
            _vk.DestroyBuffer(_device, _particleQuadBuffer, null);
        }

        if (_particleQuadMemory.Handle != 0)
        {
            _vk.FreeMemory(_device, _particleQuadMemory, null);
        }

        if (_particleInstanceBuffer.Handle != 0)
        {
            _vk.DestroyBuffer(_device, _particleInstanceBuffer, null);
        }

        if (_particleInstanceMemory.Handle != 0)
        {
            _vk.FreeMemory(_device, _particleInstanceMemory, null);
        }
    }
}
