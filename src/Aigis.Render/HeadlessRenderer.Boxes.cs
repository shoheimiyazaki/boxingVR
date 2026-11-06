using System.Collections.Immutable;
using System.Numerics;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using Silk.NET.Core.Native;
using Silk.NET.Vulkan;

namespace Aigis.Render;

/// <summary>
/// Milestone C: draw lit unit-cube instances with a depth buffer and a camera. This is
/// the first render that reflects real simulation state — the physics box, on screen.
/// </summary>
public sealed unsafe partial class HeadlessRenderer
{
    private const Format DepthFormat = Format.D32Sfloat;

    [StructLayout(LayoutKind.Sequential)]
    private struct BoxPush
    {
        public Matrix4x4 Mvp;
        public Vector4 Color;
        public Vector4 Shape;
        public Vector4 Rotation;
    }

    /// <summary>Mirrors <see cref="ShaderSource.BoxFrag"/>/<see cref="ShaderSource.PrimitiveFrag"/>'s
    /// <c>Lighting</c> uniform block layout exactly. See <see cref="SceneLighting"/>'s doc comment
    /// for why this is a UBO, not push constants, and <see cref="ShadowMapConfig"/>/
    /// <see cref="ShaderSource.PrimitiveFrag"/> for TASK-148's added <see cref="ShadowParams"/>
    /// field (unused by the legacy <see cref="ShaderSource.BoxFrag"/>, which reads the sun,
    /// ambient and render style — a pipeline's descriptor set/UBO may be larger than what one shader variant
    /// reads from it; see <see cref="EnsureLightingDescriptor"/>'s doc comment).
    /// <para>
    /// <b>TASK-149 std140 byte layout</b> (verified by hand against std140's rules — every field
    /// here is already a <c>vec4</c>/<c>PointLightGpu</c> of two <c>vec4</c>s, so every offset is
    /// a multiple of 16 and NO padding is ever inserted, unlike the classic <c>vec3</c>-in-an-
    /// array trap std140 has; <c>PrimitiveLightingUboLayoutTests</c> pins this by
    /// <c>Marshal.OffsetOf</c>/<c>sizeof</c>, not just by this comment):
    /// <code>
    /// offset   0  vec4  SunDirectionIntensity
    /// offset  16  vec4  SunColor
    /// offset  32  vec4  AmbientColorIntensity
    /// offset  48  vec4  ShadowParams
    /// offset  64  vec4  PointLightCount        (x = count as float, yzw unused/reserved)
    /// offset  80  PointLightGpu PointLights[0] { vec4 PositionRange; vec4 ColorIntensity; }
    /// offset 112  PointLightGpu PointLights[1]
    /// offset 144  PointLightGpu PointLights[2]
    /// offset 176  PointLightGpu PointLights[3]
    /// offset 208  PointLightGpu PointLights[4]
    /// offset 240  PointLightGpu PointLights[5]
    /// offset 272  PointLightGpu PointLights[6]
    /// offset 304  PointLightGpu PointLights[7]
    /// offset 336  vec4  AmbientGroundColor          (TASK-151, appended -- see below)
    /// offset 352  vec4  CameraPositionSpecularIntensity
    /// offset 368  vec4  SpecularShininessExposureToneEnabled
    /// offset 384  vec4  SpotLightCount              (TASK-168, appended -- see below)
    /// offset 400  SpotLightGpu Spot0 { vec4 PositionRange; vec4 ColorIntensity;
    ///                                  vec4 DirectionCosOuter; vec4 ConeCosInner; }
    /// offset 464  SpotLightGpu Spot1
    /// offset 528  SpotLightGpu Spot2
    /// offset 592  SpotLightGpu Spot3
    /// offset 656  vec4  IrradianceOriginEnabled     (TASK-245, appended -- see below)
    /// offset 672  vec4  IrradianceScale
    /// offset 688  vec4  IrradianceBias
    /// </code>
    /// (the absolute offsets above were written when <c>MaxPointLights</c> was 8; the array has
    /// since grown, so read them as the SHAPE and the field ORDER - which is what matters, and what
    /// <c>PrimitiveLightingUboLayoutTests</c> actually pins - rather than as byte addresses.)
    /// TASK-151's 3 fields are appended after <see cref="PointLights"/>, never interspersed among the
    /// TASK-149 fields above — arch-reviewer TASK-151 finding: inserting them earlier would shift
    /// every existing offset <c>PrimitiveLightingUboLayoutTests</c> already pins, for zero benefit.
    /// TASK-168's spot fields are appended after those for the same reason, and as a SEPARATE array
    /// rather than a third <c>vec4</c> inside <see cref="PointLightGpu"/> — growing the per-light
    /// stride would move every offset from <see cref="PointLights"/> (80) onward. TASK-245's three
    /// irradiance fields are appended after the spots, for the third time and for the same reason.
    /// The larger UBO remains within
    /// inside the Vulkan-guaranteed-minimum 16KiB UBO range.
    /// </para>
    /// </summary>
    [StructLayout(LayoutKind.Sequential)]
    internal struct LightingUniform
    {
        public Vector4 SunDirectionIntensity;
        public Vector4 SunColor;
        public Vector4 AmbientColorIntensity;

        /// <summary>x = shadows enabled (0/1), y = <see cref="ShadowMapConfig.ComparisonBias"/>,
        /// z = one shadow-map texel in UV space, w unused.</summary>
        public Vector4 ShadowParams;

        /// <summary>x = number of valid entries in <see cref="PointLights"/>
        /// (0..<see cref="SceneLighting.MaxPointLights"/>), as a float (GLSL reads it via
        /// <c>int(light.pointLightCount.x)</c> — see <see cref="ShaderSource.PrimitiveFrag"/>).
        /// yzw reserved/unused.</summary>
        public Vector4 PointLightCount;

        public PointLightGpuSlots PointLights;

        /// <summary>TASK-151: rgb = <see cref="AmbientLight.GroundColor"/>, w unused/reserved.</summary>
        public Vector4 AmbientGroundColor;

        /// <summary>TASK-151: xyz = the camera eye world position (nullable at the C# call
        /// boundary — see <see cref="RenderPrimitiveScene"/>'s <c>cameraPosition</c> parameter;
        /// zero-filled when specular is disabled, since <see cref="ShaderSource.PrimitiveFrag"/>
        /// gates every read of xyz on <c>w &gt; 0</c> first), w = <see cref="SpecularConfig.Intensity"/>.</summary>
        public Vector4 CameraPositionSpecularIntensity;

        /// <summary>TASK-151: x = <see cref="SpecularConfig.Shininess"/>, y = <see cref="ToneMapping.Exposure"/>,
        /// z = <see cref="ToneMapping.Enabled"/> as 0/1, w unused/reserved.</summary>
        public Vector4 SpecularShininessExposureToneEnabled;

        /// <summary>TASK-168: x = number of valid entries in <see cref="Spot0"/>..<see cref="Spot3"/>
        /// (0..<see cref="SceneLighting.MaxSpotLights"/>) as a float, read via
        /// <c>int(light.spotLightCount.x)</c>. yz = <see cref="SceneLighting.PatchesOffsetXz"/>
        /// (2026-09-29), w reserved.</summary>
        public Vector4 SpotLightCount;

        public SpotLightGpu Spot0;
        public SpotLightGpu Spot1;
        public SpotLightGpu Spot2;
        public SpotLightGpu Spot3;

        /// <summary>TASK-245: xyz = the bound <see cref="IrradianceVolume"/>'s world-space origin,
        /// w = 1 when a volume is bound at all. The shader gates every irradiance texture fetch on
        /// that w, so leaving it 0 (the state of every caller that passes no volume) makes the
        /// ambient term byte-for-byte the pre-TASK-245 <c>HemisphereAmbient</c>.</summary>
        public Vector4 IrradianceOriginEnabled;

        /// <summary>TASK-245: xyz = world metres to 3D-texture coordinates per axis,
        /// w = <see cref="IrradianceVolume.NormalBiasMetres"/>. Built by
        /// <see cref="IrradianceVolumeTextures.Uniforms"/> - the one place that packing is written
        /// down, because the half-texel half of it is invisible when wrong.</summary>
        public Vector4 IrradianceScale;

        /// <summary>TASK-245: xyz = the half-texel offset that lands probe 0 on texel 0's centre,
        /// w = <see cref="IrradianceVolume.FadeMetres"/>.</summary>
        public Vector4 IrradianceBias;

        /// <summary>x = scene render style; remaining lanes reserved.</summary>
        public Vector4 RenderStyle;
    }

    [StructLayout(LayoutKind.Sequential)]
    internal struct FixtureShadowUniform
    {
        public Matrix4x4 ViewProjection0;
        public Matrix4x4 ViewProjection1;
        public Matrix4x4 ViewProjection2;
        public Matrix4x4 ViewProjection3;
    }

    /// <summary>One <see cref="PointLight"/>, packed for the GPU: two consecutive <c>vec4</c>s (32
    /// bytes, std140-clean — a multiple of 16, so an array of these needs no per-element padding).
    /// Slots beyond a frame's actual <see cref="SceneLighting.PointLights"/> count are zero-filled
    /// (<see cref="UpdateLighting"/>) — zero <c>ColorIntensity</c> contributes nothing even if a
    /// shader ever read past <see cref="LightingUniform.PointLightCount"/> by mistake, a
    /// belt-and-suspenders match for the explicit count gate <see cref="ShaderSource.PrimitiveFrag"/>
    /// already applies.</summary>
    [StructLayout(LayoutKind.Sequential)]
    internal struct PointLightGpu
    {
        /// <summary>xyz = world-space position, w = <see cref="PointLight.Range"/>.</summary>
        public Vector4 PositionRange;

        /// <summary>xyz = colour, w = <see cref="PointLight.Intensity"/>.</summary>
        public Vector4 ColorIntensity;
    }

    [InlineArray(SceneLighting.MaxPointLights)]
    internal struct PointLightGpuSlots
    {
        private PointLightGpu _element0;
    }

    /// <summary>TASK-168: one <see cref="SpotLight"/>, packed for the GPU as four consecutive
    /// <c>vec4</c>s (64 bytes — itself a multiple of 16, so an array needs no per-element padding,
    /// same std140 property that keeps <see cref="PointLightGpu"/> clean). Slots beyond a frame's
    /// actual <see cref="SceneLighting.SpotLights"/> count are zero-filled, matching
    /// <see cref="PointLightGpu"/>'s belt-and-suspenders convention.
    /// <para>
    /// Every field is a <c>vec4</c>, so a field-ORDER mistake against
    /// <see cref="ShaderSource.PrimitiveFrag"/>'s <c>SpotLightGpu</c> would leave both the struct
    /// size and the UBO total correct while scrambling colour against direction on the GPU —
    /// <c>PrimitiveLightingUboLayoutTests</c> therefore pins each field's own offset (0/16/32/48),
    /// not just the size (rendering-reviewer TASK-168 finding).
    /// </para></summary>
    [StructLayout(LayoutKind.Sequential)]
    internal struct SpotLightGpu
    {
        /// <summary>xyz = world-space position (the cone's apex), w = <see cref="SpotLight.Range"/>.</summary>
        public Vector4 PositionRange;

        /// <summary>xyz = colour, w = <see cref="SpotLight.Intensity"/>.</summary>
        public Vector4 ColorIntensity;

        /// <summary>xyz = normalized direction the cone POINTS (the OPPOSITE convention from
        /// <see cref="DirectionalLight.Direction"/> — see <see cref="SpotLight.Direction"/>),
        /// w = <see cref="SpotLight.OuterCosine"/>.</summary>
        public Vector4 DirectionCosOuter;

        /// <summary>x = <see cref="SpotLight.InnerCosine"/> (always &gt; <c>DirectionCosOuter.w</c>,
        /// which is GLSL <c>smoothstep</c>'s requirement), yzw reserved/unused.</summary>
        public Vector4 ConeCosInner;
    }

    private bool _boxReady;
    private RenderPass _boxRenderPass;
    private Image _depthImage;
    private DeviceMemory _depthMemory;
    private ImageView _depthView;
    private Framebuffer _boxFramebuffer;
    private Pipeline _boxPipeline;
    private PipelineLayout _boxPipelineLayout;
    private ShaderModule _boxVert;
    private ShaderModule _boxFrag;
    private Silk.NET.Vulkan.Buffer _cubeVertexBuffer;
    private DeviceMemory _cubeVertexMemory;
    private uint _cubeVertexCount;

    private DescriptorSetLayout _lightingSetLayout;
    private DescriptorPool _lightingPool;
    private DescriptorSet _lightingSet;
    private Silk.NET.Vulkan.Buffer _lightingBuffer;
    private DeviceMemory _lightingMemory;
    private void* _lightingMapped;
    private Silk.NET.Vulkan.Buffer _fixtureShadowBuffer;
    private DeviceMemory _fixtureShadowMemory;
    private void* _fixtureShadowMapped;

    /// <summary>Clears, then draws each unit-cube instance; returns RGBA bytes.</summary>
    public byte[] RenderBoxes(Vector4 clearColor, Matrix4x4 viewProjection, IReadOnlyList<BoxInstance> instances, SceneLighting? lighting = null)
        => RenderScene(clearColor, viewProjection, instances, [], lighting);

    /// <summary>Clears, draws unit-cube instances, then overlays debug lines; returns RGBA bytes.</summary>
    /// <param name="clearColor">RGBA clear color (0..1).</param>
    /// <param name="viewProjection">Camera view-projection for <paramref name="instances"/>/<paramref name="lines"/>.</param>
    /// <param name="instances">Unit-cube instances to draw.</param>
    /// <param name="lines">Debug line overlay, drawn after <paramref name="instances"/>.</param>
    /// <param name="lighting">Directional + ambient light (TASK-036). Defaults to
    /// <see cref="SceneLighting.Default"/>, which reproduces the old hardcoded shader
    /// bit-for-bit — existing callers/goldens are unaffected by leaving this unset.</param>
    public byte[] RenderScene(Vector4 clearColor, Matrix4x4 viewProjection, IReadOnlyList<BoxInstance> instances, IReadOnlyList<DebugLine> lines, SceneLighting? lighting = null)
    {
        EnsureBoxPipeline();
        EnsureLinePipeline();
        UploadLines(lines);
        // The legacy BoxVert/BoxFrag pipeline has no shadow support (see PrimitiveVert's doc
        // comment for why: no separate model matrix survives push-constant baking to compute a
        // shadow-space position from) — always false here.
        UpdateLighting(lighting ?? SceneLighting.Default, shadowsEnabled: false);

        var cmd = BeginCommands();
        BeginBoxRenderPass(cmd, clearColor);

        _vk.CmdBindPipeline(cmd, PipelineBindPoint.Graphics, _boxPipeline);
        var lightingSet = _lightingSet;
        _vk.CmdBindDescriptorSets(cmd, PipelineBindPoint.Graphics, _boxPipelineLayout, 0, 1, &lightingSet, 0, null);
        var vertexBuffer = _cubeVertexBuffer;
        ulong offset = 0;
        _vk.CmdBindVertexBuffers(cmd, 0, 1, &vertexBuffer, &offset);
        foreach (var instance in instances)
        {
            var push = new BoxPush
            {
                Mvp = instance.Model * viewProjection,
                Color = instance.Color,
                Shape = Vector4.Zero,
                Rotation = RotationOf(instance.Model),
            };
            _vk.CmdPushConstants(cmd, _boxPipelineLayout, ShaderStageFlags.VertexBit | ShaderStageFlags.FragmentBit, 0, (uint)sizeof(BoxPush), &push);
            _vk.CmdDraw(cmd, _cubeVertexCount, 1, 0, 0);
        }

        DrawLines(cmd, viewProjection);

        _vk.CmdEndRenderPass(cmd);
        CopyColorToReadback(cmd);
        EndAndSubmit(cmd);
        return ReadPixels();
    }

    private static Vector4 RotationOf(Matrix4x4 model)
    {
        if (!Matrix4x4.Decompose(model, out _, out var rotation, out _))
        {
            rotation = Quaternion.Identity;
        }

        rotation = Quaternion.Normalize(rotation);
        return new Vector4(rotation.X, rotation.Y, rotation.Z, rotation.W);
    }

    private void EnsureBoxPipeline()
    {
        if (_boxReady)
        {
            return;
        }

        CreateDepthTarget();
        CreateBoxRenderPass();
        CreateBoxFramebuffer();
        CreateCubeVertexBuffer();
        EnsureLightingDescriptor();

        _boxVert = CreateShaderModule(PrecompiledShaders.BoxVertSpirv);
        _boxFrag = CreateShaderModule(PrecompiledShaders.BoxFragSpirv);

        var entry = (byte*)SilkMarshal.StringToPtr("main");
        try
        {
            var stages = stackalloc PipelineShaderStageCreateInfo[2];
            stages[0] = new PipelineShaderStageCreateInfo
            {
                SType = StructureType.PipelineShaderStageCreateInfo,
                Stage = ShaderStageFlags.VertexBit,
                Module = _boxVert,
                PName = entry,
            };
            stages[1] = new PipelineShaderStageCreateInfo
            {
                SType = StructureType.PipelineShaderStageCreateInfo,
                Stage = ShaderStageFlags.FragmentBit,
                Module = _boxFrag,
                PName = entry,
            };

            var binding = new VertexInputBindingDescription
            {
                Binding = 0,
                Stride = 6 * sizeof(float),
                InputRate = VertexInputRate.Vertex,
            };
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
            var depthStencil = new PipelineDepthStencilStateCreateInfo
            {
                SType = StructureType.PipelineDepthStencilStateCreateInfo,
                DepthTestEnable = true,
                DepthWriteEnable = true,
                DepthCompareOp = CompareOp.Less,
                DepthBoundsTestEnable = false,
                StencilTestEnable = false,
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

            var pushRange = new PushConstantRange
            {
                StageFlags = ShaderStageFlags.VertexBit | ShaderStageFlags.FragmentBit,
                Offset = 0,
                Size = (uint)sizeof(BoxPush),
            };
            var setLayout = _lightingSetLayout;
            var layoutInfo = new PipelineLayoutCreateInfo
            {
                SType = StructureType.PipelineLayoutCreateInfo,
                SetLayoutCount = 1,
                PSetLayouts = &setLayout,
                PushConstantRangeCount = 1,
                PPushConstantRanges = &pushRange,
            };
            Check(_vk.CreatePipelineLayout(_device, in layoutInfo, null, out _boxPipelineLayout), "CreatePipelineLayout(box)");

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
                Layout = _boxPipelineLayout,
                RenderPass = _boxRenderPass,
                Subpass = 0,
            };
            Check(_vk.CreateGraphicsPipelines(_device, default, 1, in pipelineInfo, null, out _boxPipeline), "CreateGraphicsPipelines(box)");
        }
        finally
        {
            SilkMarshal.Free((nint)entry);
        }

        _boxReady = true;
    }

    /// <summary>Creates the one descriptor set (layout + pool + buffer) that
    /// <see cref="ShaderSource.BoxFrag"/>'s <c>Lighting</c> binding reads from. A single
    /// persistently-mapped host-coherent buffer is enough — <see cref="HeadlessRenderer"/>
    /// never has more than one frame in flight (see <see cref="BeginCommands"/>/<see
    /// cref="EndAndSubmit"/>: each render call fully submits and waits before returning).</summary>
    /// <summary>TASK-148 added binding 1 (a combined image sampler over the shadow depth map,
    /// see <c>HeadlessRenderer.Shadows.cs</c>) alongside the original binding 0 UBO — one
    /// descriptor set for both, since <see cref="ShaderSource.PrimitiveFrag"/> needs both and
    /// the legacy <see cref="ShaderSource.BoxFrag"/> simply never references binding 1 (a
    /// pipeline's descriptor set layout may declare more bindings than its shaders read; that
    /// is not an error). <see cref="EnsureShadowResources"/> must run first so binding 1 has a
    /// valid image view to point at even on a frame that never enables shadows.</summary>
    private void EnsureLightingDescriptor()
    {
        if (_lightingSetLayout.Handle != 0)
        {
            return;
        }

        EnsureShadowResources();

        EnsureIrradianceTextures();
        EnsureSurfaceTextures();

        var bindings = stackalloc DescriptorSetLayoutBinding[LightingBindingCount];
        bindings[0] = new DescriptorSetLayoutBinding
        {
            Binding = 0,
            DescriptorType = DescriptorType.UniformBuffer,
            DescriptorCount = 1,
            StageFlags = ShaderStageFlags.FragmentBit,
        };
        bindings[1] = new DescriptorSetLayoutBinding
        {
            Binding = 1,
            DescriptorType = DescriptorType.CombinedImageSampler,
            DescriptorCount = 1,
            StageFlags = ShaderStageFlags.FragmentBit,
        };
        for (uint i = 2; i <= 5; i++)
        {
            bindings[i] = new DescriptorSetLayoutBinding
            {
                Binding = i,
                DescriptorType = DescriptorType.CombinedImageSampler,
                DescriptorCount = 1,
                StageFlags = ShaderStageFlags.FragmentBit,
            };
        }
        bindings[6] = new DescriptorSetLayoutBinding
        {
            Binding = 6,
            DescriptorType = DescriptorType.UniformBuffer,
            DescriptorCount = 1,
            StageFlags = ShaderStageFlags.VertexBit,
        };

        // TASK-245: bindings 7/8/9 are the irradiance volume's three 3D textures (red/green/blue).
        for (var i = IrradianceFirstBinding; i < IrradianceFirstBinding + IrradianceBindingCount; i++)
        {
            bindings[i] = new DescriptorSetLayoutBinding
            {
                Binding = (uint)i,
                DescriptorType = DescriptorType.CombinedImageSampler,
                DescriptorCount = 1,
                StageFlags = ShaderStageFlags.FragmentBit,
            };
        }

        bindings[SurfaceTextureBinding] = new DescriptorSetLayoutBinding
        {
            Binding = SurfaceTextureBinding,
            DescriptorType = DescriptorType.CombinedImageSampler,
            DescriptorCount = 1,
            StageFlags = ShaderStageFlags.FragmentBit,
        };

        var setLayoutInfo = new DescriptorSetLayoutCreateInfo
        {
            SType = StructureType.DescriptorSetLayoutCreateInfo,
            BindingCount = (uint)LightingBindingCount,
            PBindings = bindings,
        };
        Check(_vk.CreateDescriptorSetLayout(_device, in setLayoutInfo, null, out _lightingSetLayout), "CreateDescriptorSetLayout(lighting)");

        var poolSizes = stackalloc DescriptorPoolSize[2];
        poolSizes[0] = new DescriptorPoolSize { Type = DescriptorType.UniformBuffer, DescriptorCount = 2 };
        poolSizes[1] = new DescriptorPoolSize { Type = DescriptorType.CombinedImageSampler, DescriptorCount = LightingSamplerCount };
        var poolInfo = new DescriptorPoolCreateInfo
        {
            SType = StructureType.DescriptorPoolCreateInfo,
            MaxSets = 1,
            PoolSizeCount = 2,
            PPoolSizes = poolSizes,
        };
        Check(_vk.CreateDescriptorPool(_device, in poolInfo, null, out _lightingPool), "CreateDescriptorPool(lighting)");

        var setLayout = _lightingSetLayout;
        var allocInfo = new DescriptorSetAllocateInfo
        {
            SType = StructureType.DescriptorSetAllocateInfo,
            DescriptorPool = _lightingPool,
            DescriptorSetCount = 1,
            PSetLayouts = &setLayout,
        };
        Check(_vk.AllocateDescriptorSets(_device, in allocInfo, out _lightingSet), "AllocateDescriptorSets(lighting)");

        var bufferInfo = new BufferCreateInfo
        {
            SType = StructureType.BufferCreateInfo,
            Size = (ulong)sizeof(LightingUniform),
            Usage = BufferUsageFlags.UniformBufferBit,
            SharingMode = SharingMode.Exclusive,
        };
        Check(_vk.CreateBuffer(_device, in bufferInfo, null, out _lightingBuffer), "CreateBuffer(lighting)");
        _vk.GetBufferMemoryRequirements(_device, _lightingBuffer, out var req);
        _lightingMemory = AllocateMemory(req, MemoryPropertyFlags.HostVisibleBit | MemoryPropertyFlags.HostCoherentBit);
        Check(_vk.BindBufferMemory(_device, _lightingBuffer, _lightingMemory, 0), "BindBufferMemory(lighting)");
        void* mapped = null;
        Check(_vk.MapMemory(_device, _lightingMemory, 0, (ulong)sizeof(LightingUniform), 0, ref mapped), "MapMemory(lighting)");
        _lightingMapped = mapped;

        var fixtureBufferInfo = new BufferCreateInfo
        {
            SType = StructureType.BufferCreateInfo,
            Size = (ulong)sizeof(FixtureShadowUniform),
            Usage = BufferUsageFlags.UniformBufferBit,
            SharingMode = SharingMode.Exclusive,
        };
        Check(_vk.CreateBuffer(_device, in fixtureBufferInfo, null, out _fixtureShadowBuffer), "CreateBuffer(fixtureShadows)");
        _vk.GetBufferMemoryRequirements(_device, _fixtureShadowBuffer, out req);
        _fixtureShadowMemory = AllocateMemory(req, MemoryPropertyFlags.HostVisibleBit | MemoryPropertyFlags.HostCoherentBit);
        Check(_vk.BindBufferMemory(_device, _fixtureShadowBuffer, _fixtureShadowMemory, 0), "BindBufferMemory(fixtureShadows)");
        mapped = null;
        Check(_vk.MapMemory(_device, _fixtureShadowMemory, 0, (ulong)sizeof(FixtureShadowUniform), 0, ref mapped), "MapMemory(fixtureShadows)");
        _fixtureShadowMapped = mapped;

        var descriptorBufferInfo = new DescriptorBufferInfo { Buffer = _lightingBuffer, Offset = 0, Range = (ulong)sizeof(LightingUniform) };
        var imageInfos = stackalloc DescriptorImageInfo[5];
        imageInfos[0] = new DescriptorImageInfo { Sampler = _shadowSampler, ImageView = _shadowDepthView, ImageLayout = ImageLayout.ShaderReadOnlyOptimal };
        for (var i = 0; i < SceneLighting.MaxDownwardFixtureShadows; i++)
        {
            imageInfos[i + 1] = new DescriptorImageInfo { Sampler = _shadowSampler, ImageView = _fixtureShadowDepthViews[i], ImageLayout = ImageLayout.ShaderReadOnlyOptimal };
        }
        var fixtureDescriptor = new DescriptorBufferInfo { Buffer = _fixtureShadowBuffer, Offset = 0, Range = (ulong)sizeof(FixtureShadowUniform) };
        var writes = stackalloc WriteDescriptorSet[LightingBindingCount];
        writes[0] = new WriteDescriptorSet
        {
            SType = StructureType.WriteDescriptorSet,
            DstSet = _lightingSet,
            DstBinding = 0,
            DescriptorCount = 1,
            DescriptorType = DescriptorType.UniformBuffer,
            PBufferInfo = &descriptorBufferInfo,
        };
        writes[1] = new WriteDescriptorSet
        {
            SType = StructureType.WriteDescriptorSet,
            DstSet = _lightingSet,
            DstBinding = 1,
            DescriptorCount = 1,
            DescriptorType = DescriptorType.CombinedImageSampler,
            PImageInfo = imageInfos,
        };
        for (uint i = 2; i <= 5; i++)
        {
            writes[i] = new WriteDescriptorSet { SType = StructureType.WriteDescriptorSet, DstSet = _lightingSet, DstBinding = i, DescriptorCount = 1, DescriptorType = DescriptorType.CombinedImageSampler, PImageInfo = imageInfos + (i - 1) };
        }
        writes[6] = new WriteDescriptorSet { SType = StructureType.WriteDescriptorSet, DstSet = _lightingSet, DstBinding = 6, DescriptorCount = 1, DescriptorType = DescriptorType.UniformBuffer, PBufferInfo = &fixtureDescriptor };
        var irradianceInfos = stackalloc DescriptorImageInfo[IrradianceBindingCount];
        WriteIrradianceDescriptors(writes, irradianceInfos);
        var surfaceInfo = SurfaceTextureInfo();
        writes[SurfaceTextureBinding] = SurfaceTextureWrite(&surfaceInfo);
        _surfaceTextures!.AcknowledgeRebind();
        _vk.UpdateDescriptorSets(_device, (uint)LightingBindingCount, writes, 0, null);
    }

    /// <summary>Writes <paramref name="lighting"/> (and TASK-148's shadow toggle) into the
    /// mapped uniform buffer; the memory is host-coherent so no explicit flush is needed before
    /// the next submit reads it.</summary>
    /// <param name="lighting">The full scene lighting state to write.</param>
    /// <param name="shadowsEnabled">TASK-148: whether the shadow depth pass ran this frame.</param>
    /// <param name="cameraPosition">TASK-151: the camera eye, needed only for
    /// <see cref="SceneLighting.Specular"/>'s view-dependent highlight — see
    /// <see cref="RenderPrimitiveScene"/>'s own parameter doc comment for the fail-loud contract
    /// (never silently zeroed) this method's caller already enforced before reaching here.</param>
    /// <param name="fixtureShadowCount">Number of leading point lights with a dedicated downward-fixture shadow map.</param>
    private void UpdateLighting(SceneLighting lighting, bool shadowsEnabled, Vector3? cameraPosition = null, int fixtureShadowCount = 0)
    {
        lighting.Validate();
        var uniform = new LightingUniform
        {
            SunDirectionIntensity = new Vector4(lighting.Sun.Direction, lighting.Sun.Intensity),
            SunColor = new Vector4(lighting.Sun.Color, 0f),
            AmbientColorIntensity = new Vector4(lighting.Ambient.Color, lighting.Ambient.Intensity),
            ShadowParams = new Vector4(
                shadowsEnabled ? 1f : 0f,
                ShadowMapConfig.ComparisonBias,
                1f / _shadowMapSize,
                fixtureShadowCount),
            PointLightCount = new Vector4(lighting.PointLights.Length, 0f, 0f, 0f),
            AmbientGroundColor = new Vector4(lighting.Ambient.GroundColor, 0f),
            CameraPositionSpecularIntensity = new Vector4(cameraPosition ?? Vector3.Zero, lighting.Specular.Intensity),
            SpecularShininessExposureToneEnabled = new Vector4(
                lighting.Specular.Shininess,
                lighting.ToneMap.Exposure,
                lighting.ToneMap.Enabled ? 1f : 0f,
                0f),
            // yz: SceneLighting.PatchesOffsetXz, in the reserved lanes of this vec4.
            SpotLightCount = new Vector4(
                lighting.SpotLights.Length, lighting.PatchesOffsetXz.X, lighting.PatchesOffsetXz.Y, 0f),
        };

        // TASK-245: what is described to the shader is the volume that is actually RESIDENT on the
        // GPU, not the one on `lighting`. Those are the same object in every ordinary frame
        // (PrepareIrradiance ran first), and keeping the distinction means a volume that was never
        // uploaded cannot switch on sampling of the textures a previous shot left behind.
        var (irradianceOrigin, irradianceScale, irradianceBias) =
            IrradianceVolumeTextures.Uniforms(_irradiance?.Resident is { } resident && ReferenceEquals(resident, lighting.Irradiance) ? resident : null);
        uniform.IrradianceOriginEnabled = irradianceOrigin;
        uniform.IrradianceScale = irradianceScale;
        uniform.IrradianceBias = irradianceBias;
        uniform.RenderStyle = new Vector4((float)lighting.Style, 0f, 0f, 0f);
        WritePointLights(ref uniform, lighting.PointLights);
        WriteSpotLights(ref uniform, lighting.SpotLights);
        *(LightingUniform*)_lightingMapped = uniform;
    }

    private void UpdateFixtureShadows(ReadOnlySpan<Matrix4x4> viewProjections)
    {
        var uniform = new FixtureShadowUniform
        {
            ViewProjection0 = viewProjections.Length > 0 ? viewProjections[0] : Matrix4x4.Identity,
            ViewProjection1 = viewProjections.Length > 1 ? viewProjections[1] : Matrix4x4.Identity,
            ViewProjection2 = viewProjections.Length > 2 ? viewProjections[2] : Matrix4x4.Identity,
            ViewProjection3 = viewProjections.Length > 3 ? viewProjections[3] : Matrix4x4.Identity,
        };
        *(FixtureShadowUniform*)_fixtureShadowMapped = uniform;
    }

    /// <summary>TASK-168: the spot twin of <see cref="WritePointLights"/> — same explicit
    /// slot-by-slot shape and same reasons (see that method's doc comment). This is the one place
    /// <see cref="SpotLight.ConeAngleDegrees"/> becomes the two cosines the shader consumes, so the
    /// <c>InnerCosine &gt; OuterCosine</c> ordering GLSL <c>smoothstep</c> requires is established
    /// once here rather than at any call site.</summary>
    private static void WriteSpotLights(ref LightingUniform uniform, ImmutableArray<SpotLight> lights)
    {
        Span<SpotLightGpu> slots = stackalloc SpotLightGpu[SceneLighting.MaxSpotLights];
        for (var i = 0; i < lights.Length; i++)
        {
            var light = lights[i];
            slots[i] = new SpotLightGpu
            {
                PositionRange = new Vector4(light.Position, light.Range),
                ColorIntensity = new Vector4(light.Color, light.Intensity),
                DirectionCosOuter = new Vector4(Vector3.Normalize(light.Direction), light.OuterCosine),
                ConeCosInner = new Vector4(light.InnerCosine, 0f, 0f, 0f),
            };
        }

        uniform.Spot0 = slots[0];
        uniform.Spot1 = slots[1];
        uniform.Spot2 = slots[2];
        uniform.Spot3 = slots[3];
    }

    /// <summary>Fills <see cref="LightingUniform.PointLights"/>
    /// from <paramref name="lights"/> (already validated/capped by
    /// <see cref="SceneLighting.Validate"/> before this runs), zero-filling any slot beyond
    /// <paramref name="lights"/>'s length — see <see cref="PointLightGpu"/>'s doc comment for why
    /// a zeroed slot is harmless even without the explicit <see cref="LightingUniform.PointLightCount"/>
    /// gate. Explicit slot-by-slot assignment, not a loop over a C# array field, because
    /// <see cref="LightingUniform"/> deliberately has no array field (fixed-size buffers of a
    /// non-primitive element type need either <c>unsafe fixed</c> — primitives only — or C#12's
    /// <c>InlineArray</c>; explicit fields keep this struct's layout obviously exactly what the
    /// doc comment's byte table says, with zero risk of a subtly different runtime layout).</summary>
    private static void WritePointLights(ref LightingUniform uniform, ImmutableArray<PointLight> lights)
    {
        for (var i = 0; i < lights.Length; i++)
        {
            var light = lights[i];
            uniform.PointLights[i] = new PointLightGpu
            {
                PositionRange = new Vector4(light.Position, light.Range),
                ColorIntensity = new Vector4(light.Color, light.Intensity),
            };
        }

    }

    private void CreateDepthTarget()
    {
        var imageInfo = new ImageCreateInfo
        {
            SType = StructureType.ImageCreateInfo,
            ImageType = ImageType.Type2D,
            Format = DepthFormat,
            Extent = new Extent3D(_width, _height, 1),
            MipLevels = 1,
            ArrayLayers = 1,
            Samples = SampleCountFlags.Count1Bit,
            Tiling = ImageTiling.Optimal,
            Usage = ImageUsageFlags.DepthStencilAttachmentBit,
            SharingMode = SharingMode.Exclusive,
            InitialLayout = ImageLayout.Undefined,
        };
        Check(_vk.CreateImage(_device, in imageInfo, null, out _depthImage), "CreateImage(depth)");

        _vk.GetImageMemoryRequirements(_device, _depthImage, out var req);
        _depthMemory = AllocateMemory(req, MemoryPropertyFlags.DeviceLocalBit);
        Check(_vk.BindImageMemory(_device, _depthImage, _depthMemory, 0), "BindImageMemory(depth)");

        var viewInfo = new ImageViewCreateInfo
        {
            SType = StructureType.ImageViewCreateInfo,
            Image = _depthImage,
            ViewType = ImageViewType.Type2D,
            Format = DepthFormat,
            SubresourceRange = new ImageSubresourceRange(ImageAspectFlags.DepthBit, 0, 1, 0, 1),
        };
        Check(_vk.CreateImageView(_device, in viewInfo, null, out _depthView), "CreateImageView(depth)");
    }

    private void CreateBoxRenderPass()
    {
        var attachments = stackalloc AttachmentDescription[2];
        attachments[0] = new AttachmentDescription
        {
            Format = ColorFormat,
            Samples = SampleCountFlags.Count1Bit,
            LoadOp = AttachmentLoadOp.Clear,
            StoreOp = AttachmentStoreOp.Store,
            StencilLoadOp = AttachmentLoadOp.DontCare,
            StencilStoreOp = AttachmentStoreOp.DontCare,
            InitialLayout = ImageLayout.Undefined,
            FinalLayout = ImageLayout.TransferSrcOptimal,
        };
        attachments[1] = new AttachmentDescription
        {
            Format = DepthFormat,
            Samples = SampleCountFlags.Count1Bit,
            LoadOp = AttachmentLoadOp.Clear,
            StoreOp = AttachmentStoreOp.DontCare,
            StencilLoadOp = AttachmentLoadOp.DontCare,
            StencilStoreOp = AttachmentStoreOp.DontCare,
            InitialLayout = ImageLayout.Undefined,
            FinalLayout = ImageLayout.DepthStencilAttachmentOptimal,
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

        // Same color-write → transfer-read ordering as the color-only pass (see ReadbackDependency).
        var copyDependency = ReadbackDependency();

        var info = new RenderPassCreateInfo
        {
            SType = StructureType.RenderPassCreateInfo,
            AttachmentCount = 2,
            PAttachments = attachments,
            SubpassCount = 1,
            PSubpasses = &subpass,
            DependencyCount = 1,
            PDependencies = &copyDependency,
        };
        Check(_vk.CreateRenderPass(_device, in info, null, out _boxRenderPass), "CreateRenderPass(box)");
    }

    private void CreateBoxFramebuffer()
    {
        var views = stackalloc ImageView[2];
        views[0] = _colorView;
        views[1] = _depthView;
        var info = new FramebufferCreateInfo
        {
            SType = StructureType.FramebufferCreateInfo,
            RenderPass = _boxRenderPass,
            AttachmentCount = 2,
            PAttachments = views,
            Width = _width,
            Height = _height,
            Layers = 1,
        };
        Check(_vk.CreateFramebuffer(_device, in info, null, out _boxFramebuffer), "CreateFramebuffer(box)");
    }

    private void CreateCubeVertexBuffer()
    {
        var vertices = PrimitiveMeshes.BuildCubeVertices();
        _cubeVertexCount = (uint)(vertices.Length / 6);
        var size = (ulong)(vertices.Length * sizeof(float));

        var info = new BufferCreateInfo
        {
            SType = StructureType.BufferCreateInfo,
            Size = size,
            Usage = BufferUsageFlags.VertexBufferBit,
            SharingMode = SharingMode.Exclusive,
        };
        Check(_vk.CreateBuffer(_device, in info, null, out _cubeVertexBuffer), "CreateBuffer(cube)");

        _vk.GetBufferMemoryRequirements(_device, _cubeVertexBuffer, out var req);
        _cubeVertexMemory = AllocateMemory(req, MemoryPropertyFlags.HostVisibleBit | MemoryPropertyFlags.HostCoherentBit);
        Check(_vk.BindBufferMemory(_device, _cubeVertexBuffer, _cubeVertexMemory, 0), "BindBufferMemory(cube)");

        void* mapped = null;
        Check(_vk.MapMemory(_device, _cubeVertexMemory, 0, size, 0, ref mapped), "MapMemory(cube)");
        vertices.AsSpan().CopyTo(new Span<float>(mapped, vertices.Length));
        _vk.UnmapMemory(_device, _cubeVertexMemory);
    }

    private void BeginBoxRenderPass(CommandBuffer cmd, Vector4 color)
    {
        var clears = stackalloc ClearValue[2];
        clears[0] = new ClearValue { Color = new ClearColorValue(color.X, color.Y, color.Z, color.W) };
        clears[1] = new ClearValue { DepthStencil = new ClearDepthStencilValue(1.0f, 0) };
        var info = new RenderPassBeginInfo
        {
            SType = StructureType.RenderPassBeginInfo,
            RenderPass = _boxRenderPass,
            Framebuffer = _boxFramebuffer,
            RenderArea = new Rect2D(new Offset2D(0, 0), new Extent2D(_width, _height)),
            ClearValueCount = 2,
            PClearValues = clears,
        };
        _vk.CmdBeginRenderPass(cmd, in info, SubpassContents.Inline);
    }

    private void DisposeBoxResources()
    {
        if (_boxPipeline.Handle != 0)
        {
            _vk.DestroyPipeline(_device, _boxPipeline, null);
        }

        if (_boxPipelineLayout.Handle != 0)
        {
            _vk.DestroyPipelineLayout(_device, _boxPipelineLayout, null);
        }

        if (_boxVert.Handle != 0)
        {
            _vk.DestroyShaderModule(_device, _boxVert, null);
        }

        if (_boxFrag.Handle != 0)
        {
            _vk.DestroyShaderModule(_device, _boxFrag, null);
        }

        if (_cubeVertexBuffer.Handle != 0)
        {
            _vk.DestroyBuffer(_device, _cubeVertexBuffer, null);
        }

        if (_cubeVertexMemory.Handle != 0)
        {
            _vk.FreeMemory(_device, _cubeVertexMemory, null);
        }

        if (_boxFramebuffer.Handle != 0)
        {
            _vk.DestroyFramebuffer(_device, _boxFramebuffer, null);
        }

        if (_boxRenderPass.Handle != 0)
        {
            _vk.DestroyRenderPass(_device, _boxRenderPass, null);
        }

        if (_depthView.Handle != 0)
        {
            _vk.DestroyImageView(_device, _depthView, null);
        }

        if (_depthImage.Handle != 0)
        {
            _vk.DestroyImage(_device, _depthImage, null);
        }

        if (_depthMemory.Handle != 0)
        {
            _vk.FreeMemory(_device, _depthMemory, null);
        }

        if (_lightingBuffer.Handle != 0)
        {
            _vk.DestroyBuffer(_device, _lightingBuffer, null);
        }

        if (_lightingMemory.Handle != 0)
        {
            _vk.UnmapMemory(_device, _lightingMemory);
            _vk.FreeMemory(_device, _lightingMemory, null);
        }

        if (_fixtureShadowBuffer.Handle != 0)
        {
            _vk.DestroyBuffer(_device, _fixtureShadowBuffer, null);
        }

        if (_fixtureShadowMemory.Handle != 0)
        {
            _vk.UnmapMemory(_device, _fixtureShadowMemory);
            _vk.FreeMemory(_device, _fixtureShadowMemory, null);
        }

        if (_lightingPool.Handle != 0)
        {
            // Freeing the pool also frees _lightingSet — no separate FreeDescriptorSets call needed.
            _vk.DestroyDescriptorPool(_device, _lightingPool, null);
        }

        if (_lightingSetLayout.Handle != 0)
        {
            _vk.DestroyDescriptorSetLayout(_device, _lightingSetLayout, null);
        }
    }
}
