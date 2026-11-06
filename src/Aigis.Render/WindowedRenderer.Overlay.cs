using System.Numerics;
using System.Runtime.InteropServices;
using Silk.NET.Core.Native;
using Silk.NET.Vulkan;

namespace Aigis.Render;

/// <summary>
/// <c>HeadlessRenderer.Overlay.cs</c>'s live-window counterpart — same screen-space
/// text/HUD overlay (TASK-041/TASK-051), same NDC-mapping convention (see
/// <see cref="OverlaySource"/>'s doc comment), drawn last in
/// <see cref="WindowedRenderer.RenderPrimitiveSceneAndPresent"/> with depth test/write
/// disabled. Uses dynamic viewport/scissor like every other <c>WindowedRenderer</c>
/// pipeline (already set per-frame before this pipeline binds — see that method), not a
/// baked one, because unlike <see cref="HeadlessRenderer"/> the window can resize.
/// </summary>
public sealed unsafe partial class WindowedRenderer
{
    [StructLayout(LayoutKind.Sequential)]
    private struct OverlayPush
    {
        public Vector4 Rect;  // (scaleX, scaleY, translateX, translateY) — see OverlaySource.
        public Vector4 Color;
    }

    private bool _overlayPipelineReady;
    private Pipeline _overlayPipeline;
    private PipelineLayout _overlayPipelineLayout;
    private ShaderModule _overlayVert;
    private ShaderModule _overlayFrag;
    private Silk.NET.Vulkan.Buffer _overlayQuadBuffer;
    private DeviceMemory _overlayQuadMemory;

    /// <summary>This renderer's own glyph-rectangle scratch buffer, refilled per frame by
    /// <see cref="ScreenText.BuildQuads(IReadOnlyList{ScreenTextDraw}, List{OverlayQuad})"/> — see
    /// that overload's doc comment for why a per-frame list was the largest allocation in the
    /// engine, and why this buffer is per-instance rather than shared/static.</summary>
    private readonly List<OverlayQuad> _overlayQuads = [];

    /// <summary>Draws <paramref name="quads"/> using the current swapchain extent for the
    /// pixel→NDC mapping (see <see cref="OverlaySource"/>) — resizes are picked up for free
    /// since this reads <see cref="_swapchainExtent"/> fresh every call rather than baking
    /// a size in.</summary>
    private void DrawOverlay(CommandBuffer cmd, List<OverlayQuad> quads)
    {
        if (quads.Count == 0)
        {
            return;
        }

        EnsureOverlayPipeline();

        _vk.CmdBindPipeline(cmd, PipelineBindPoint.Graphics, _overlayPipeline);
        var buffer = _overlayQuadBuffer;
        ulong offset = 0;
        _vk.CmdBindVertexBuffers(cmd, 0, 1, &buffer, &offset);

        var width = (float)_swapchainExtent.Width;
        var height = (float)_swapchainExtent.Height;
        foreach (var quad in quads)
        {
            var push = new OverlayPush
            {
                // OverlayRect is the ONE place pixel space becomes NDC for every overlay path
                // (hard rule 4) — shared with the textured overlay so the two can never disagree.
                Rect = OverlayRect.ToNdcScaleTranslate(quad.CenterPixels, quad.SizePixels, width, height),
                Color = quad.Color,
            };
            _vk.CmdPushConstants(cmd, _overlayPipelineLayout,
                ShaderStageFlags.VertexBit | ShaderStageFlags.FragmentBit, 0, (uint)sizeof(OverlayPush), &push);
            _vk.CmdDraw(cmd, 6, 1, 0, 0);
        }
    }

    private void EnsureOverlayPipeline()
    {
        if (_overlayPipelineReady)
        {
            return;
        }

        CreateOverlayQuadBuffer();

        _overlayVert = CreateShaderModule(PrecompiledShaders.OverlayVertSpirv);
        _overlayFrag = CreateShaderModule(PrecompiledShaders.OverlayFragSpirv);

        var entry = (byte*)SilkMarshal.StringToPtr("main");
        try
        {
            var stages = stackalloc PipelineShaderStageCreateInfo[2];
            stages[0] = new PipelineShaderStageCreateInfo { SType = StructureType.PipelineShaderStageCreateInfo, Stage = ShaderStageFlags.VertexBit, Module = _overlayVert, PName = entry };
            stages[1] = new PipelineShaderStageCreateInfo { SType = StructureType.PipelineShaderStageCreateInfo, Stage = ShaderStageFlags.FragmentBit, Module = _overlayFrag, PName = entry };

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

            var dynamicStates = stackalloc DynamicState[2] { DynamicState.Viewport, DynamicState.Scissor };
            var dynamicState = new PipelineDynamicStateCreateInfo { SType = StructureType.PipelineDynamicStateCreateInfo, DynamicStateCount = 2, PDynamicStates = dynamicStates };
            var viewportState = new PipelineViewportStateCreateInfo { SType = StructureType.PipelineViewportStateCreateInfo, ViewportCount = 1, ScissorCount = 1 };

            var rasterizer = new PipelineRasterizationStateCreateInfo { SType = StructureType.PipelineRasterizationStateCreateInfo, PolygonMode = PolygonMode.Fill, CullMode = CullModeFlags.None, FrontFace = FrontFace.CounterClockwise, LineWidth = 1f };
            var multisample = new PipelineMultisampleStateCreateInfo { SType = StructureType.PipelineMultisampleStateCreateInfo, RasterizationSamples = SampleCountFlags.Count1Bit, MinSampleShading = 1f };
            var depthStencil = new PipelineDepthStencilStateCreateInfo { SType = StructureType.PipelineDepthStencilStateCreateInfo, DepthTestEnable = false, DepthWriteEnable = false, DepthCompareOp = CompareOp.Always };
            // Alpha blending — kept identical to HeadlessRenderer.Overlay.cs's, which carries the
            // full note on why this pipeline stopped being opaque (hard rule 6: what a human sees in
            // the window and what an agent verifies from a screenshot must be the same frame, so
            // these two blend states must never diverge).
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

            var pushRange = new PushConstantRange { StageFlags = ShaderStageFlags.VertexBit | ShaderStageFlags.FragmentBit, Offset = 0, Size = (uint)sizeof(OverlayPush) };
            var layoutInfo = new PipelineLayoutCreateInfo { SType = StructureType.PipelineLayoutCreateInfo, PushConstantRangeCount = 1, PPushConstantRanges = &pushRange };
            Check(_vk.CreatePipelineLayout(_device, in layoutInfo, null, out _overlayPipelineLayout), "CreatePipelineLayout(overlay)");

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
                Layout = _overlayPipelineLayout,
                RenderPass = _renderPass,
                Subpass = 0,
            };
            Check(_vk.CreateGraphicsPipelines(_device, default, 1, in pipelineInfo, null, out _overlayPipeline), "CreateGraphicsPipelines(overlay)");
        }
        finally
        {
            SilkMarshal.Free((nint)entry);
        }

        _overlayPipelineReady = true;
    }

    private void CreateOverlayQuadBuffer()
    {
        // Read as Y-up these two triangles are CCW (matching FrontFace.CounterClockwise
        // below), but under Y-down NDC (see OverlaySource's doc comment) they rasterize as
        // clockwise on screen. Harmless only because CullMode is None — enabling back-face
        // culling here would drop the whole HUD.
        float[] vertices =
        [
            -0.5f, -0.5f, 0.5f, -0.5f, 0.5f, 0.5f,
            -0.5f, -0.5f, 0.5f, 0.5f, -0.5f, 0.5f,
        ];
        var size = (ulong)(vertices.Length * sizeof(float));
        var info = new BufferCreateInfo { SType = StructureType.BufferCreateInfo, Size = size, Usage = BufferUsageFlags.VertexBufferBit, SharingMode = SharingMode.Exclusive };
        Check(_vk.CreateBuffer(_device, in info, null, out _overlayQuadBuffer), "CreateBuffer(overlayQuad)");

        _vk.GetBufferMemoryRequirements(_device, _overlayQuadBuffer, out var req);
        _overlayQuadMemory = AllocateMemory(req, MemoryPropertyFlags.HostVisibleBit | MemoryPropertyFlags.HostCoherentBit);
        Check(_vk.BindBufferMemory(_device, _overlayQuadBuffer, _overlayQuadMemory, 0), "BindBufferMemory(overlayQuad)");

        void* mapped = null;
        Check(_vk.MapMemory(_device, _overlayQuadMemory, 0, size, 0, ref mapped), "MapMemory(overlayQuad)");
        vertices.AsSpan().CopyTo(new Span<float>(mapped, vertices.Length));
        _vk.UnmapMemory(_device, _overlayQuadMemory);
    }

    private void DisposeOverlayResources()
    {
        if (_overlayPipeline.Handle != 0)
        {
            _vk.DestroyPipeline(_device, _overlayPipeline, null);
        }

        if (_overlayPipelineLayout.Handle != 0)
        {
            _vk.DestroyPipelineLayout(_device, _overlayPipelineLayout, null);
        }

        if (_overlayVert.Handle != 0)
        {
            _vk.DestroyShaderModule(_device, _overlayVert, null);
        }

        if (_overlayFrag.Handle != 0)
        {
            _vk.DestroyShaderModule(_device, _overlayFrag, null);
        }

        DestroyMeshBuffer(_overlayQuadBuffer, _overlayQuadMemory);
    }
}
