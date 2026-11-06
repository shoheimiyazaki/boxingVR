using System.Numerics;
using Silk.NET.Core.Native;
using Silk.NET.Vulkan;

namespace Aigis.Render;

/// <summary>
/// A debug-line overlay (gizmos, joints, velocity vectors), drawn unlit into the box
/// render pass with depth-test on but depth-write off — lines never occlude geometry.
/// The vertex buffer is host-visible and regrown on demand.
/// </summary>
public sealed unsafe partial class HeadlessRenderer
{
    private bool _lineReady;
    private Pipeline _linePipeline;
    private PipelineLayout _linePipelineLayout;
    private ShaderModule _lineVert;
    private ShaderModule _lineFrag;
    private Silk.NET.Vulkan.Buffer _lineBuffer;
    private DeviceMemory _lineMemory;
    private int _lineCapacityVertices;
    private uint _lineVertexCount;

    private void UploadLines(IReadOnlyList<DebugLine> lines)
    {
        _lineVertexCount = (uint)(lines.Count * 2);
        if (_lineVertexCount == 0)
        {
            return;
        }

        EnsureLineCapacity(lines.Count * 2);

        var data = new float[lines.Count * 2 * 6];
        var w = 0;
        foreach (var line in lines)
        {
            w = WriteVertex(data, w, line.A, line.Color);
            w = WriteVertex(data, w, line.B, line.Color);
        }

        var size = (ulong)(data.Length * sizeof(float));
        void* mapped = null;
        Check(_vk.MapMemory(_device, _lineMemory, 0, size, 0, ref mapped), "MapMemory(lines)");
        data.AsSpan().CopyTo(new Span<float>(mapped, data.Length));
        _vk.UnmapMemory(_device, _lineMemory);

        static int WriteVertex(float[] data, int w, Vector3 p, Vector4 color)
        {
            data[w++] = p.X;
            data[w++] = p.Y;
            data[w++] = p.Z;
            data[w++] = color.X;
            data[w++] = color.Y;
            data[w++] = color.Z;
            return w;
        }
    }

    private void DrawLines(CommandBuffer cmd, Matrix4x4 viewProjection)
    {
        if (_lineVertexCount == 0)
        {
            return;
        }

        _vk.CmdBindPipeline(cmd, PipelineBindPoint.Graphics, _linePipeline);
        var buffer = _lineBuffer;
        ulong offset = 0;
        _vk.CmdBindVertexBuffers(cmd, 0, 1, &buffer, &offset);
        _vk.CmdPushConstants(cmd, _linePipelineLayout, ShaderStageFlags.VertexBit, 0, (uint)sizeof(Matrix4x4), &viewProjection);
        _vk.CmdDraw(cmd, _lineVertexCount, 1, 0, 0);
    }

    private void EnsureLinePipeline()
    {
        if (_lineReady)
        {
            return;
        }

        _lineVert = CreateShaderModule(PrecompiledShaders.LineVertSpirv);
        _lineFrag = CreateShaderModule(PrecompiledShaders.LineFragSpirv);

        var entry = (byte*)SilkMarshal.StringToPtr("main");
        try
        {
            var stages = stackalloc PipelineShaderStageCreateInfo[2];
            stages[0] = new PipelineShaderStageCreateInfo { SType = StructureType.PipelineShaderStageCreateInfo, Stage = ShaderStageFlags.VertexBit, Module = _lineVert, PName = entry };
            stages[1] = new PipelineShaderStageCreateInfo { SType = StructureType.PipelineShaderStageCreateInfo, Stage = ShaderStageFlags.FragmentBit, Module = _lineFrag, PName = entry };

            var binding = new VertexInputBindingDescription { Binding = 0, Stride = 6 * sizeof(float), InputRate = VertexInputRate.Vertex };
            var attributes = stackalloc VertexInputAttributeDescription[2];
            attributes[0] = new VertexInputAttributeDescription { Location = 0, Binding = 0, Format = Format.R32G32B32Sfloat, Offset = 0 };
            attributes[1] = new VertexInputAttributeDescription { Location = 1, Binding = 0, Format = Format.R32G32B32Sfloat, Offset = 3 * sizeof(float) };
            var vertexInput = new PipelineVertexInputStateCreateInfo
            {
                SType = StructureType.PipelineVertexInputStateCreateInfo,
                VertexBindingDescriptionCount = 1,
                PVertexBindingDescriptions = &binding,
                VertexAttributeDescriptionCount = 2,
                PVertexAttributeDescriptions = attributes,
            };

            var inputAssembly = new PipelineInputAssemblyStateCreateInfo { SType = StructureType.PipelineInputAssemblyStateCreateInfo, Topology = PrimitiveTopology.LineList };
            var viewport = new Viewport { X = 0, Y = 0, Width = _width, Height = _height, MinDepth = 0, MaxDepth = 1 };
            var scissor = new Rect2D(new Offset2D(0, 0), new Extent2D(_width, _height));
            var viewportState = new PipelineViewportStateCreateInfo { SType = StructureType.PipelineViewportStateCreateInfo, ViewportCount = 1, PViewports = &viewport, ScissorCount = 1, PScissors = &scissor };
            var rasterizer = new PipelineRasterizationStateCreateInfo { SType = StructureType.PipelineRasterizationStateCreateInfo, PolygonMode = PolygonMode.Fill, CullMode = CullModeFlags.None, FrontFace = FrontFace.CounterClockwise, LineWidth = 1f };
            var multisample = new PipelineMultisampleStateCreateInfo { SType = StructureType.PipelineMultisampleStateCreateInfo, RasterizationSamples = SampleCountFlags.Count1Bit, MinSampleShading = 1f };
            var depthStencil = new PipelineDepthStencilStateCreateInfo { SType = StructureType.PipelineDepthStencilStateCreateInfo, DepthTestEnable = false, DepthWriteEnable = false, DepthCompareOp = CompareOp.Always };
            var blendAttachment = new PipelineColorBlendAttachmentState { ColorWriteMask = ColorComponentFlags.RBit | ColorComponentFlags.GBit | ColorComponentFlags.BBit | ColorComponentFlags.ABit, BlendEnable = false };
            var colorBlend = new PipelineColorBlendStateCreateInfo { SType = StructureType.PipelineColorBlendStateCreateInfo, AttachmentCount = 1, PAttachments = &blendAttachment };

            var pushRange = new PushConstantRange { StageFlags = ShaderStageFlags.VertexBit, Offset = 0, Size = (uint)sizeof(Matrix4x4) };
            var layoutInfo = new PipelineLayoutCreateInfo { SType = StructureType.PipelineLayoutCreateInfo, PushConstantRangeCount = 1, PPushConstantRanges = &pushRange };
            Check(_vk.CreatePipelineLayout(_device, in layoutInfo, null, out _linePipelineLayout), "CreatePipelineLayout(line)");

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
                Layout = _linePipelineLayout,
                RenderPass = _boxRenderPass,
                Subpass = 0,
            };
            Check(_vk.CreateGraphicsPipelines(_device, default, 1, in pipelineInfo, null, out _linePipeline), "CreateGraphicsPipelines(line)");
        }
        finally
        {
            SilkMarshal.Free((nint)entry);
        }

        _lineReady = true;
    }

    private void EnsureLineCapacity(int vertices)
    {
        if (vertices <= _lineCapacityVertices)
        {
            return;
        }

        if (_lineBuffer.Handle != 0)
        {
            _vk.DestroyBuffer(_device, _lineBuffer, null);
            _vk.FreeMemory(_device, _lineMemory, null);
        }

        _lineCapacityVertices = Math.Max(vertices, 256);
        var size = (ulong)(_lineCapacityVertices * 6 * sizeof(float));
        var info = new BufferCreateInfo { SType = StructureType.BufferCreateInfo, Size = size, Usage = BufferUsageFlags.VertexBufferBit, SharingMode = SharingMode.Exclusive };
        Check(_vk.CreateBuffer(_device, in info, null, out _lineBuffer), "CreateBuffer(lines)");

        _vk.GetBufferMemoryRequirements(_device, _lineBuffer, out var req);
        _lineMemory = AllocateMemory(req, MemoryPropertyFlags.HostVisibleBit | MemoryPropertyFlags.HostCoherentBit);
        Check(_vk.BindBufferMemory(_device, _lineBuffer, _lineMemory, 0), "BindBufferMemory(lines)");
    }

    private void DisposeLineResources()
    {
        if (_linePipeline.Handle != 0)
        {
            _vk.DestroyPipeline(_device, _linePipeline, null);
        }

        if (_linePipelineLayout.Handle != 0)
        {
            _vk.DestroyPipelineLayout(_device, _linePipelineLayout, null);
        }

        if (_lineVert.Handle != 0)
        {
            _vk.DestroyShaderModule(_device, _lineVert, null);
        }

        if (_lineFrag.Handle != 0)
        {
            _vk.DestroyShaderModule(_device, _lineFrag, null);
        }

        if (_lineBuffer.Handle != 0)
        {
            _vk.DestroyBuffer(_device, _lineBuffer, null);
        }

        if (_lineMemory.Handle != 0)
        {
            _vk.FreeMemory(_device, _lineMemory, null);
        }
    }
}
