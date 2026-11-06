using System.Numerics;
using System.Runtime.InteropServices;
using Silk.NET.Core.Native;
using Silk.NET.Vulkan;

namespace Aigis.Render;

/// <summary>
/// TASK-041/TASK-051: a screen-space text/HUD overlay, drawn last into the same box render
/// pass (see <see cref="HeadlessRenderer.RenderPrimitiveScene"/>, the only headless call site
/// of <see cref="DrawOverlay"/>), after the 3D scene, debug lines, UI shapes and overlay
/// images, with depth test <b>and</b> write disabled and alpha blending on — text always
/// draws on top, resolution-/camera-independent (see
/// <see cref="OverlaySource"/>'s doc comment for the NDC-mapping convention). Follows the same
/// partial-class-per-pipeline convention as <c>HeadlessRenderer.Boxes.cs</c>/
/// <c>HeadlessRenderer.Lines.cs</c>.
/// </summary>
public sealed unsafe partial class HeadlessRenderer
{
    [StructLayout(LayoutKind.Sequential)]
    private struct OverlayPush
    {
        public Vector4 Rect;  // (scaleX, scaleY, translateX, translateY) — see OverlaySource.
        public Vector4 Color;
    }

    private bool _overlayReady;
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

    /// <summary>Draws <paramref name="quads"/> (already-rasterized glyph rectangles, see
    /// <see cref="ScreenText.BuildQuads(IReadOnlyList{ScreenTextDraw})"/>) as opaque, unlit, screen-space quads. A caller
    /// with no text can pass an empty list — this is then a no-op (no pipeline bind, no
    /// draw call), same "empty means skip" contract as <see cref="DrawLines"/>.</summary>
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

        foreach (var quad in quads)
        {
            var push = new OverlayPush
            {
                // OverlayRect is the ONE place pixel space becomes NDC for every overlay path
                // (hard rule 4) — shared with the textured overlay so the two can never disagree.
                Rect = OverlayRect.ToNdcScaleTranslate(quad.CenterPixels, quad.SizePixels, _width, _height),
                Color = quad.Color,
            };
            _vk.CmdPushConstants(cmd, _overlayPipelineLayout,
                ShaderStageFlags.VertexBit | ShaderStageFlags.FragmentBit, 0, (uint)sizeof(OverlayPush), &push);
            _vk.CmdDraw(cmd, 6, 1, 0, 0);
        }
    }

    private void EnsureOverlayPipeline()
    {
        if (_overlayReady)
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
            var viewport = new Viewport { X = 0, Y = 0, Width = _width, Height = _height, MinDepth = 0, MaxDepth = 1 };
            var scissor = new Rect2D(new Offset2D(0, 0), new Extent2D(_width, _height));
            var viewportState = new PipelineViewportStateCreateInfo { SType = StructureType.PipelineViewportStateCreateInfo, ViewportCount = 1, PViewports = &viewport, ScissorCount = 1, PScissors = &scissor };
            var rasterizer = new PipelineRasterizationStateCreateInfo { SType = StructureType.PipelineRasterizationStateCreateInfo, PolygonMode = PolygonMode.Fill, CullMode = CullModeFlags.None, FrontFace = FrontFace.CounterClockwise, LineWidth = 1f };
            var multisample = new PipelineMultisampleStateCreateInfo { SType = StructureType.PipelineMultisampleStateCreateInfo, RasterizationSamples = SampleCountFlags.Count1Bit, MinSampleShading = 1f };

            // Depth test AND write off — an overlay must never be occluded by, nor occlude
            // via leftover depth, the 3D scene it draws on top of (see class doc comment).
            var depthStencil = new PipelineDepthStencilStateCreateInfo { SType = StructureType.PipelineDepthStencilStateCreateInfo, DepthTestEnable = false, DepthWriteEnable = false, DepthCompareOp = CompareOp.Always };

            // Alpha blending, matching OverlayImagePipeline's factors exactly (see its own note on
            // why the ALPHA factors are One/OneMinusSrcAlpha).
            //
            // This pipeline was opaque until UI surfaces arrived, and that was a latent defect, not a
            // choice: a caller could always put an alpha in a ScreenTextDraw's colour — several
            // HudPalette tokens do (Backdrop 0.48, BarTrack 0.55, IconFaded 0.65) — and it was
            // silently discarded, so "half-transparent" text rendered fully opaque. Blending also
            // makes text fade expressible at all, which is what lets a label enter and leave with
            // the surface it belongs to instead of popping while its card is still fading in.
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
                Layout = _overlayPipelineLayout,
                RenderPass = _boxRenderPass,
                Subpass = 0,
            };
            Check(_vk.CreateGraphicsPipelines(_device, default, 1, in pipelineInfo, null, out _overlayPipeline), "CreateGraphicsPipelines(overlay)");
        }
        finally
        {
            SilkMarshal.Free((nint)entry);
        }

        _overlayReady = true;
    }

    /// <summary>A single unit quad ([-0.5,0.5]^2, two CCW triangles) reused for every
    /// <see cref="OverlayQuad"/> draw — per-instance position/size/colour all travel via
    /// push constants (see <see cref="OverlayPush"/>), same "one shared unit mesh + push
    /// constant transform" shape as the cube/sphere/capsule primitive meshes.</summary>
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

        if (_overlayQuadBuffer.Handle != 0)
        {
            _vk.DestroyBuffer(_device, _overlayQuadBuffer, null);
        }

        if (_overlayQuadMemory.Handle != 0)
        {
            _vk.FreeMemory(_device, _overlayQuadMemory, null);
        }
    }
}
