using System.Numerics;
using Silk.NET.Core.Native;
using Silk.NET.Shaderc;
using Silk.NET.Vulkan;

namespace Aigis.Render;

/// <summary>
/// Milestone B: a hardcoded-triangle graphics pipeline, proving the shader/pipeline path
/// (runtime GLSL -> SPIR-V -> VkPipeline) on top of the offscreen target.
/// </summary>
public sealed unsafe partial class HeadlessRenderer
{
    private const string TriangleVertSource = """
        #version 450
        layout(location = 0) out vec3 vColor;
        vec2 positions[3] = vec2[](vec2(0.0, -0.6), vec2(0.6, 0.6), vec2(-0.6, 0.6));
        vec3 colors[3]    = vec3[](vec3(1.0, 0.3, 0.2), vec3(0.2, 1.0, 0.3), vec3(0.3, 0.4, 1.0));
        void main() {
            gl_Position = vec4(positions[gl_VertexIndex], 0.0, 1.0);
            vColor = colors[gl_VertexIndex];
        }
        """;

    private const string TriangleFragSource = """
        #version 450
        layout(location = 0) in vec3 vColor;
        layout(location = 0) out vec4 outColor;
        void main() { outColor = vec4(vColor, 1.0); }
        """;

    private ShaderCompiler? _shaderCompiler;
    private Pipeline _trianglePipeline;
    private PipelineLayout _trianglePipelineLayout;
    private ShaderModule _triangleVert;
    private ShaderModule _triangleFrag;
    private bool _triangleReady;

    /// <summary>Clears to <paramref name="clearColor"/> then draws a test triangle; returns RGBA bytes.</summary>
    public byte[] RenderTriangle(Vector4 clearColor)
    {
        EnsureTrianglePipeline();

        var cmd = BeginCommands();
        BeginRenderPass(cmd, clearColor);
        _vk.CmdBindPipeline(cmd, PipelineBindPoint.Graphics, _trianglePipeline);
        _vk.CmdDraw(cmd, 3, 1, 0, 0);
        _vk.CmdEndRenderPass(cmd);
        CopyColorToReadback(cmd);
        EndAndSubmit(cmd);
        return ReadPixels();
    }

    private void EnsureTrianglePipeline()
    {
        if (_triangleReady)
        {
            return;
        }

        _shaderCompiler ??= new ShaderCompiler();
        _triangleVert = CreateShaderModule(_shaderCompiler.Compile(TriangleVertSource, ShaderKind.VertexShader, "triangle.vert"));
        _triangleFrag = CreateShaderModule(_shaderCompiler.Compile(TriangleFragSource, ShaderKind.FragmentShader, "triangle.frag"));

        var entry = (byte*)SilkMarshal.StringToPtr("main");
        try
        {
            var stages = stackalloc PipelineShaderStageCreateInfo[2];
            stages[0] = new PipelineShaderStageCreateInfo
            {
                SType = StructureType.PipelineShaderStageCreateInfo,
                Stage = ShaderStageFlags.VertexBit,
                Module = _triangleVert,
                PName = entry,
            };
            stages[1] = new PipelineShaderStageCreateInfo
            {
                SType = StructureType.PipelineShaderStageCreateInfo,
                Stage = ShaderStageFlags.FragmentBit,
                Module = _triangleFrag,
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
            var blendAttachment = new PipelineColorBlendAttachmentState
            {
                ColorWriteMask = ColorComponentFlags.RBit | ColorComponentFlags.GBit | ColorComponentFlags.BBit | ColorComponentFlags.ABit,
                BlendEnable = false,
            };
            var colorBlend = new PipelineColorBlendStateCreateInfo
            {
                SType = StructureType.PipelineColorBlendStateCreateInfo,
                AttachmentCount = 1,
                PAttachments = &blendAttachment,
            };

            var layoutInfo = new PipelineLayoutCreateInfo { SType = StructureType.PipelineLayoutCreateInfo };
            Check(_vk.CreatePipelineLayout(_device, in layoutInfo, null, out _trianglePipelineLayout), "CreatePipelineLayout");

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
                Layout = _trianglePipelineLayout,
                RenderPass = _renderPass,
                Subpass = 0,
            };
            Check(_vk.CreateGraphicsPipelines(_device, default, 1, in pipelineInfo, null, out _trianglePipeline), "CreateGraphicsPipelines");
        }
        finally
        {
            SilkMarshal.Free((nint)entry);
        }

        _triangleReady = true;
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
            Check(_vk.CreateShaderModule(_device, in info, null, out var module), "CreateShaderModule");
            return module;
        }
    }

    private void DisposePipelineResources()
    {
        if (_trianglePipeline.Handle != 0)
        {
            _vk.DestroyPipeline(_device, _trianglePipeline, null);
        }

        if (_trianglePipelineLayout.Handle != 0)
        {
            _vk.DestroyPipelineLayout(_device, _trianglePipelineLayout, null);
        }

        if (_triangleVert.Handle != 0)
        {
            _vk.DestroyShaderModule(_device, _triangleVert, null);
        }

        if (_triangleFrag.Handle != 0)
        {
            _vk.DestroyShaderModule(_device, _triangleFrag, null);
        }

        DisposeBoxResources();
        DisposeShadowResources();
        DisposePrimitiveMeshes();
        DisposeLineResources();
        DisposeParticleResources();
        DisposeOverlayResources();
        DisposeOverlayImageResources();
        DisposeIrradianceResources();
        DisposeDecalResources();
        DisposeVoxelResources();
        DisposePostProcessResources();
        DisposeSurfaceTextureResources();
        DisposeUiShapeResources();
        DisposeRenderToTextureResources();
        _shaderCompiler?.Dispose();
    }
}
