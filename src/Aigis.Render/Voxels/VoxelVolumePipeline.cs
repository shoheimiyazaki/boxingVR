using System.Numerics;
using Silk.NET.Core.Native;
using Silk.NET.Shaderc;
using Silk.NET.Vulkan;

namespace Aigis.Render.Voxels;

/// <summary>
/// Draws <see cref="VoxelVolume"/>s by building their surface ON THE GPU from the 3D texture alone: no vertex buffer, no
/// CPU mesh. Owned in one place and composed by both renderers (headless and windowed), like
/// <see cref="WorldDecalPipeline"/>, whose residency and upload pattern it follows.
/// <para>
/// <b>How the surface is made.</b> One draw per volume of <c>cells × 6 faces × 6 vertices</c>. The vertex shader turns
/// <c>gl_VertexIndex</c> into (cell, face, corner), reads the cell and its neighbour across that face with
/// <c>texelFetch</c>, and emits the face only where a filled cell (alpha ≥ 0.5) meets an empty one or the volume's edge —
/// the simplest boundary surface ("blocky" faces, no smoothing). Every other face collapses to one point outside the clip
/// volume, which the rasteriser discards. Flat-shaded with the engine's default sun and a hemisphere ambient.
/// </para>
/// <para>Opaque, depth-tested and depth-written: drawn with the opaque scene, before X-ray markers, decals and particles.</para>
/// </summary>
internal sealed unsafe class VoxelVolumePipeline : IDisposable
{
    /// <summary>Volumes resident at once (one descriptor set each).</summary>
    internal const int MaxVolumes = 256;

    /// <summary>A volume no frame has drawn for this many prepares is released (see <see cref="WorldDecalPipeline"/>).</summary>
    internal const long IdleReleaseFrames = 600;

    private const string VertexSource = """
        #version 450
        layout(set = 0, binding = 0) uniform sampler3D volume;
        layout(push_constant) uniform Push { mat4 viewProjection; mat4 cellToWorld; } pc;
        layout(location = 0) out vec3 vColor;
        layout(location = 1) out vec3 vNormal;

        const ivec3 DIRS[6] = ivec3[](ivec3(1,0,0), ivec3(-1,0,0), ivec3(0,1,0), ivec3(0,-1,0), ivec3(0,0,1), ivec3(0,0,-1));
        // Four corners of each face, in the cell's own unit cube.
        const vec3 CORNERS[24] = vec3[](
            vec3(1,0,0), vec3(1,1,0), vec3(1,1,1), vec3(1,0,1),
            vec3(0,0,0), vec3(0,0,1), vec3(0,1,1), vec3(0,1,0),
            vec3(0,1,0), vec3(0,1,1), vec3(1,1,1), vec3(1,1,0),
            vec3(0,0,0), vec3(1,0,0), vec3(1,0,1), vec3(0,0,1),
            vec3(0,0,1), vec3(1,0,1), vec3(1,1,1), vec3(0,1,1),
            vec3(0,0,0), vec3(0,1,0), vec3(1,1,0), vec3(1,0,0));
        const int TRI[6] = int[](0, 1, 2, 0, 2, 3);

        void main() {
            ivec3 size = textureSize(volume, 0);
            int cell = gl_VertexIndex / 36;
            int face = (gl_VertexIndex % 36) / 6;
            int corner = TRI[gl_VertexIndex % 6];
            ivec3 c = ivec3(cell % size.x, (cell / size.x) % size.y, cell / (size.x * size.y));
            vec4 texel = texelFetch(volume, c, 0);
            ivec3 n = c + DIRS[face];
            bool inside = all(greaterThanEqual(n, ivec3(0))) && all(lessThan(n, size));
            float neighbour = inside ? texelFetch(volume, n, 0).a : 0.0;
            vNormal = vec3(0.0, 1.0, 0.0);
            vColor = vec3(0.0);
            if (texel.a < 0.5 || neighbour >= 0.5) {
                gl_Position = vec4(2.0, 2.0, 2.0, 1.0); // hidden face: one point outside the clip volume
                return;
            }

            vec3 local = vec3(c) + CORNERS[face * 4 + corner];
            gl_Position = pc.viewProjection * (pc.cellToWorld * vec4(local, 1.0));
            // An axis-aligned normal through rotation and per-axis scale stays on that rotated axis.
            vNormal = normalize(mat3(pc.cellToWorld) * vec3(DIRS[face]));
            vColor = texel.rgb;
        }
        """;

    private const string FragmentSource = """
        #version 450
        layout(location = 0) in vec3 vColor;
        layout(location = 1) in vec3 vNormal;
        layout(location = 0) out vec4 outColor;

        void main() {
            // The engine's default sun (DirectionalLight.Default) and a sky/ground hemisphere ambient.
            vec3 sun = normalize(vec3(0.4, 0.85, 0.35));
            vec3 n = normalize(vNormal);
            float diffuse = 0.4 * max(dot(n, sun), 0.0);
            float ambient = mix(0.58, 0.72, n.y * 0.5 + 0.5); // bright: the tissues are told apart by colour
            outColor = vec4(vColor * (ambient + diffuse), 1.0);
        }
        """;

    private static readonly Lazy<(uint[] Vert, uint[] Frag)> Spirv = new(() =>
    {
        using var compiler = new ShaderCompiler();
        return (compiler.Compile(VertexSource, ShaderKind.VertexShader, "voxel_volume.vert"),
            compiler.Compile(FragmentSource, ShaderKind.FragmentShader, "voxel_volume.frag"));
    });

    private readonly Vk _vk;
    private readonly PhysicalDevice _physicalDevice;
    private readonly Device _device;
    private readonly Queue _queue;
    private readonly CommandPool _commandPool;
    private readonly Dictionary<VoxelVolume, Resident> _resident = new(ReferenceEqualityComparer.Instance);
    private readonly Stack<DescriptorSet> _spareSets = new();
    private readonly List<VoxelVolume> _leaving = [];
    private long _sequence;
    private bool _ready;
    private Pipeline _pipeline;
    private PipelineLayout _layout;
    private ShaderModule _vert;
    private ShaderModule _frag;
    private Sampler _sampler;
    private DescriptorSetLayout _setLayout;
    private DescriptorPool _pool;

    public VoxelVolumePipeline(Vk vk, PhysicalDevice physicalDevice, Device device, Queue queue, CommandPool commandPool)
    {
        _vk = vk;
        _physicalDevice = physicalDevice;
        _device = device;
        _queue = queue;
        _commandPool = commandPool;
    }

    /// <summary>Volumes left out of the last frame because more than <see cref="MaxVolumes"/> were named.</summary>
    public int DroppedLastFrame { get; private set; }

    private struct Push
    {
        public Matrix4x4 ViewProjection;
        public Matrix4x4 CellToWorld;
    }

    private sealed class Resident
    {
        public Image Image;
        public DeviceMemory Memory;
        public ImageView View;
        public DescriptorSet Set;
        public Silk.NET.Vulkan.Buffer Staging;
        public DeviceMemory StagingMemory;
        public int UploadedRevision = -1;
        public long LastUsed;
    }

    /// <summary>
    /// Creates the pipeline on first use and uploads every volume whose texels the GPU does not hold at its current
    /// revision. Must run BEFORE the caller's render pass begins (uploads record copies and barriers).
    /// </summary>
    public void PrepareUploads(IReadOnlyList<VoxelVolumeDraw> draws, RenderPass renderPass, bool dynamicViewport, uint width, uint height)
    {
        DroppedLastFrame = 0;
        if (draws.Count == 0)
        {
            return;
        }

        EnsurePipeline(renderPass, dynamicViewport, width, height);
        _sequence++;
        foreach (var draw in draws)
        {
            ArgumentNullException.ThrowIfNull(draw.Volume);
            if (_resident.TryGetValue(draw.Volume, out var r))
            {
                r.LastUsed = _sequence;
            }
        }

        ReleaseIdle();
        foreach (var draw in draws)
        {
            if (!_resident.TryGetValue(draw.Volume, out var resident))
            {
                if (_resident.Count >= MaxVolumes)
                {
                    DroppedLastFrame++;
                    continue;
                }

                resident = Create(draw.Volume);
                resident.LastUsed = _sequence;
                _resident.Add(draw.Volume, resident);
            }

            if (resident.UploadedRevision != draw.Volume.Revision)
            {
                Upload(resident, draw.Volume);
            }
        }
    }

    /// <summary>Records one draw per resident volume (inside the scene render pass).</summary>
    public void Draw(CommandBuffer cmd, IReadOnlyList<VoxelVolumeDraw> draws, Matrix4x4 viewProjection)
    {
        if (draws.Count == 0 || !_ready)
        {
            return;
        }

        _vk.CmdBindPipeline(cmd, PipelineBindPoint.Graphics, _pipeline);
        foreach (var draw in draws)
        {
            if (!_resident.TryGetValue(draw.Volume, out var resident))
            {
                continue; // over the budget this frame (counted in DroppedLastFrame)
            }

            var set = resident.Set;
            _vk.CmdBindDescriptorSets(cmd, PipelineBindPoint.Graphics, _layout, 0, 1, &set, 0, null);
            var push = new Push { ViewProjection = viewProjection, CellToWorld = draw.CellToWorld };
            _vk.CmdPushConstants(cmd, _layout, ShaderStageFlags.VertexBit, 0, (uint)sizeof(Push), &push);
            _vk.CmdDraw(cmd, (uint)(draw.Volume.CellCount * 36), 1, 0, 0);
        }
    }

    private void ReleaseIdle()
    {
        _leaving.Clear();
        foreach (var (volume, resident) in _resident)
        {
            if (_sequence - resident.LastUsed > IdleReleaseFrames)
            {
                _leaving.Add(volume);
            }
        }

        if (_leaving.Count == 0)
        {
            return;
        }

        // A frame in flight may still read an image about to go: wait once for all of them.
        _vk.DeviceWaitIdle(_device);
        foreach (var volume in _leaving)
        {
            var resident = _resident[volume];
            _resident.Remove(volume);
            DestroyImages(resident);
            _spareSets.Push(resident.Set);
        }

        _leaving.Clear();
    }

    private Resident Create(VoxelVolume volume)
    {
        var resident = new Resident();
        var size = (ulong)volume.Rgba.Length;
        var stagingInfo = new BufferCreateInfo { SType = StructureType.BufferCreateInfo, Size = size, Usage = BufferUsageFlags.TransferSrcBit, SharingMode = SharingMode.Exclusive };
        Check(_vk.CreateBuffer(_device, in stagingInfo, null, out resident.Staging), "CreateBuffer(voxelStaging)");
        _vk.GetBufferMemoryRequirements(_device, resident.Staging, out var stagingReq);
        resident.StagingMemory = AllocateMemory(stagingReq, MemoryPropertyFlags.HostVisibleBit | MemoryPropertyFlags.HostCoherentBit);
        Check(_vk.BindBufferMemory(_device, resident.Staging, resident.StagingMemory, 0), "BindBufferMemory(voxelStaging)");

        var imageInfo = new ImageCreateInfo
        {
            SType = StructureType.ImageCreateInfo,
            ImageType = ImageType.Type3D,
            Format = Format.R8G8B8A8Unorm,
            Extent = new Extent3D((uint)volume.Width, (uint)volume.Height, (uint)volume.Depth),
            MipLevels = 1,
            ArrayLayers = 1,
            Samples = SampleCountFlags.Count1Bit,
            Tiling = ImageTiling.Optimal,
            Usage = ImageUsageFlags.TransferDstBit | ImageUsageFlags.SampledBit,
            SharingMode = SharingMode.Exclusive,
            InitialLayout = ImageLayout.Undefined,
        };
        Check(_vk.CreateImage(_device, in imageInfo, null, out resident.Image), "CreateImage(voxel)");
        _vk.GetImageMemoryRequirements(_device, resident.Image, out var imageReq);
        resident.Memory = AllocateMemory(imageReq, MemoryPropertyFlags.DeviceLocalBit);
        Check(_vk.BindImageMemory(_device, resident.Image, resident.Memory, 0), "BindImageMemory(voxel)");

        var viewInfo = new ImageViewCreateInfo
        {
            SType = StructureType.ImageViewCreateInfo,
            Image = resident.Image,
            ViewType = ImageViewType.Type3D,
            Format = Format.R8G8B8A8Unorm,
            SubresourceRange = new ImageSubresourceRange(ImageAspectFlags.ColorBit, 0, 1, 0, 1),
        };
        Check(_vk.CreateImageView(_device, in viewInfo, null, out resident.View), "CreateImageView(voxel)");

        if (_spareSets.TryPop(out var spare))
        {
            resident.Set = spare;
        }
        else
        {
            var setLayout = _setLayout;
            var allocInfo = new DescriptorSetAllocateInfo { SType = StructureType.DescriptorSetAllocateInfo, DescriptorPool = _pool, DescriptorSetCount = 1, PSetLayouts = &setLayout };
            Check(_vk.AllocateDescriptorSets(_device, in allocInfo, out resident.Set), "AllocateDescriptorSets(voxel)");
        }

        var imageDescriptor = new DescriptorImageInfo { Sampler = _sampler, ImageView = resident.View, ImageLayout = ImageLayout.ShaderReadOnlyOptimal };
        var write = new WriteDescriptorSet
        {
            SType = StructureType.WriteDescriptorSet,
            DstSet = resident.Set,
            DstBinding = 0,
            DescriptorCount = 1,
            DescriptorType = DescriptorType.CombinedImageSampler,
            PImageInfo = &imageDescriptor,
        };
        _vk.UpdateDescriptorSets(_device, 1, in write, 0, null);
        return resident;
    }

    /// <summary>
    /// Copies the texels up and leaves the image readable by the vertex shader. A later upload waits for the previous
    /// frame's vertex-shader reads first (source stage = vertex shader).
    /// </summary>
    private void Upload(Resident resident, VoxelVolume volume)
    {
        var size = (ulong)volume.Rgba.Length;
        void* mapped = null;
        Check(_vk.MapMemory(_device, resident.StagingMemory, 0, size, 0, ref mapped), "MapMemory(voxelStaging)");
        volume.Rgba.CopyTo(new Span<byte>(mapped, volume.Rgba.Length));
        _vk.UnmapMemory(_device, resident.StagingMemory);

        var first = resident.UploadedRevision < 0;
        var allocInfo = new CommandBufferAllocateInfo { SType = StructureType.CommandBufferAllocateInfo, CommandPool = _commandPool, Level = CommandBufferLevel.Primary, CommandBufferCount = 1 };
        Check(_vk.AllocateCommandBuffers(_device, in allocInfo, out var cmd), "AllocateCommandBuffers(voxelUpload)");
        var begin = new CommandBufferBeginInfo { SType = StructureType.CommandBufferBeginInfo, Flags = CommandBufferUsageFlags.OneTimeSubmitBit };
        Check(_vk.BeginCommandBuffer(cmd, in begin), "BeginCommandBuffer(voxelUpload)");

        var range = new ImageSubresourceRange(ImageAspectFlags.ColorBit, 0, 1, 0, 1);
        var toTransfer = new ImageMemoryBarrier
        {
            SType = StructureType.ImageMemoryBarrier,
            OldLayout = first ? ImageLayout.Undefined : ImageLayout.ShaderReadOnlyOptimal,
            NewLayout = ImageLayout.TransferDstOptimal,
            SrcQueueFamilyIndex = Vk.QueueFamilyIgnored,
            DstQueueFamilyIndex = Vk.QueueFamilyIgnored,
            Image = resident.Image,
            SubresourceRange = range,
            SrcAccessMask = first ? AccessFlags.None : AccessFlags.ShaderReadBit,
            DstAccessMask = AccessFlags.TransferWriteBit,
        };
        _vk.CmdPipelineBarrier(cmd, first ? PipelineStageFlags.TopOfPipeBit : PipelineStageFlags.VertexShaderBit, PipelineStageFlags.TransferBit, 0, 0, null, 0, null, 1, in toTransfer);

        var region = new BufferImageCopy
        {
            ImageSubresource = new ImageSubresourceLayers(ImageAspectFlags.ColorBit, 0, 0, 1),
            ImageExtent = new Extent3D((uint)volume.Width, (uint)volume.Height, (uint)volume.Depth),
        };
        _vk.CmdCopyBufferToImage(cmd, resident.Staging, resident.Image, ImageLayout.TransferDstOptimal, 1, in region);

        var toRead = new ImageMemoryBarrier
        {
            SType = StructureType.ImageMemoryBarrier,
            OldLayout = ImageLayout.TransferDstOptimal,
            NewLayout = ImageLayout.ShaderReadOnlyOptimal,
            SrcQueueFamilyIndex = Vk.QueueFamilyIgnored,
            DstQueueFamilyIndex = Vk.QueueFamilyIgnored,
            Image = resident.Image,
            SubresourceRange = range,
            SrcAccessMask = AccessFlags.TransferWriteBit,
            DstAccessMask = AccessFlags.ShaderReadBit,
        };
        _vk.CmdPipelineBarrier(cmd, PipelineStageFlags.TransferBit, PipelineStageFlags.VertexShaderBit, 0, 0, null, 0, null, 1, in toRead);
        Check(_vk.EndCommandBuffer(cmd), "EndCommandBuffer(voxelUpload)");

        var fenceInfo = new FenceCreateInfo { SType = StructureType.FenceCreateInfo };
        Check(_vk.CreateFence(_device, in fenceInfo, null, out var fence), "CreateFence(voxelUpload)");
        var submit = new SubmitInfo { SType = StructureType.SubmitInfo, CommandBufferCount = 1, PCommandBuffers = &cmd };
        Check(_vk.QueueSubmit(_queue, 1, in submit, fence), "QueueSubmit(voxelUpload)");
        Check(_vk.WaitForFences(_device, 1, in fence, true, ulong.MaxValue), "WaitForFences(voxelUpload)");
        _vk.DestroyFence(_device, fence, null);
        _vk.FreeCommandBuffers(_device, _commandPool, 1, in cmd);
        resident.UploadedRevision = volume.Revision;
    }

    private void EnsurePipeline(RenderPass renderPass, bool dynamicViewport, uint width, uint height)
    {
        if (_ready)
        {
            return;
        }

        var samplerInfo = new SamplerCreateInfo
        {
            SType = StructureType.SamplerCreateInfo,
            MagFilter = Filter.Nearest,
            MinFilter = Filter.Nearest,
            AddressModeU = SamplerAddressMode.ClampToEdge,
            AddressModeV = SamplerAddressMode.ClampToEdge,
            AddressModeW = SamplerAddressMode.ClampToEdge,
            MipmapMode = SamplerMipmapMode.Nearest,
            BorderColor = BorderColor.FloatTransparentBlack,
        };
        Check(_vk.CreateSampler(_device, in samplerInfo, null, out _sampler), "CreateSampler(voxel)");

        var binding = new DescriptorSetLayoutBinding { Binding = 0, DescriptorType = DescriptorType.CombinedImageSampler, DescriptorCount = 1, StageFlags = ShaderStageFlags.VertexBit };
        var setLayoutInfo = new DescriptorSetLayoutCreateInfo { SType = StructureType.DescriptorSetLayoutCreateInfo, BindingCount = 1, PBindings = &binding };
        Check(_vk.CreateDescriptorSetLayout(_device, in setLayoutInfo, null, out _setLayout), "CreateDescriptorSetLayout(voxel)");
        var poolSize = new DescriptorPoolSize { Type = DescriptorType.CombinedImageSampler, DescriptorCount = MaxVolumes };
        var poolInfo = new DescriptorPoolCreateInfo { SType = StructureType.DescriptorPoolCreateInfo, PoolSizeCount = 1, PPoolSizes = &poolSize, MaxSets = MaxVolumes };
        Check(_vk.CreateDescriptorPool(_device, in poolInfo, null, out _pool), "CreateDescriptorPool(voxel)");

        var (vertSpirv, fragSpirv) = Spirv.Value;
        _vert = CreateShaderModule(vertSpirv);
        _frag = CreateShaderModule(fragSpirv);
        var entry = (byte*)SilkMarshal.StringToPtr("main");
        try
        {
            var stages = stackalloc PipelineShaderStageCreateInfo[2];
            stages[0] = new PipelineShaderStageCreateInfo { SType = StructureType.PipelineShaderStageCreateInfo, Stage = ShaderStageFlags.VertexBit, Module = _vert, PName = entry };
            stages[1] = new PipelineShaderStageCreateInfo { SType = StructureType.PipelineShaderStageCreateInfo, Stage = ShaderStageFlags.FragmentBit, Module = _frag, PName = entry };
            var vertexInput = new PipelineVertexInputStateCreateInfo { SType = StructureType.PipelineVertexInputStateCreateInfo };
            var inputAssembly = new PipelineInputAssemblyStateCreateInfo { SType = StructureType.PipelineInputAssemblyStateCreateInfo, Topology = PrimitiveTopology.TriangleList };
            var viewport = new Viewport { X = 0, Y = 0, Width = width, Height = height, MinDepth = 0, MaxDepth = 1 };
            var scissor = new Rect2D(new Offset2D(0, 0), new Extent2D(width, height));
            var viewportState = dynamicViewport
                ? new PipelineViewportStateCreateInfo { SType = StructureType.PipelineViewportStateCreateInfo, ViewportCount = 1, ScissorCount = 1 }
                : new PipelineViewportStateCreateInfo { SType = StructureType.PipelineViewportStateCreateInfo, ViewportCount = 1, PViewports = &viewport, ScissorCount = 1, PScissors = &scissor };
            var dynamicStates = stackalloc DynamicState[2] { DynamicState.Viewport, DynamicState.Scissor };
            var dynamicState = new PipelineDynamicStateCreateInfo { SType = StructureType.PipelineDynamicStateCreateInfo, DynamicStateCount = 2, PDynamicStates = dynamicStates };
            // No culling: the face winding depends on the volume's handedness after its transform, and only boundary
            // faces are ever emitted, so culling would save little.
            var rasterizer = new PipelineRasterizationStateCreateInfo { SType = StructureType.PipelineRasterizationStateCreateInfo, PolygonMode = PolygonMode.Fill, CullMode = CullModeFlags.None, FrontFace = FrontFace.CounterClockwise, LineWidth = 1f };
            var multisample = new PipelineMultisampleStateCreateInfo { SType = StructureType.PipelineMultisampleStateCreateInfo, RasterizationSamples = SampleCountFlags.Count1Bit, MinSampleShading = 1f };
            var depthStencil = new PipelineDepthStencilStateCreateInfo { SType = StructureType.PipelineDepthStencilStateCreateInfo, DepthTestEnable = true, DepthWriteEnable = true, DepthCompareOp = CompareOp.LessOrEqual };
            var blendAttachment = new PipelineColorBlendAttachmentState
            {
                ColorWriteMask = ColorComponentFlags.RBit | ColorComponentFlags.GBit | ColorComponentFlags.BBit | ColorComponentFlags.ABit,
                BlendEnable = false,
            };
            var colorBlend = new PipelineColorBlendStateCreateInfo { SType = StructureType.PipelineColorBlendStateCreateInfo, AttachmentCount = 1, PAttachments = &blendAttachment };
            var pushRange = new PushConstantRange { StageFlags = ShaderStageFlags.VertexBit, Offset = 0, Size = (uint)sizeof(Push) };
            var setLayout = _setLayout;
            var layoutInfo = new PipelineLayoutCreateInfo { SType = StructureType.PipelineLayoutCreateInfo, SetLayoutCount = 1, PSetLayouts = &setLayout, PushConstantRangeCount = 1, PPushConstantRanges = &pushRange };
            Check(_vk.CreatePipelineLayout(_device, in layoutInfo, null, out _layout), "CreatePipelineLayout(voxel)");
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
                Layout = _layout,
                RenderPass = renderPass,
                Subpass = 0,
            };
            Check(_vk.CreateGraphicsPipelines(_device, default, 1, in pipelineInfo, null, out _pipeline), "CreateGraphicsPipelines(voxel)");
        }
        finally
        {
            SilkMarshal.Free((nint)entry);
        }

        _ready = true;
    }

    private ShaderModule CreateShaderModule(uint[] spirv)
    {
        fixed (uint* code = spirv)
        {
            var info = new ShaderModuleCreateInfo { SType = StructureType.ShaderModuleCreateInfo, CodeSize = (nuint)(spirv.Length * sizeof(uint)), PCode = code };
            Check(_vk.CreateShaderModule(_device, in info, null, out var module), "CreateShaderModule(voxel)");
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
                var allocInfo = new MemoryAllocateInfo { SType = StructureType.MemoryAllocateInfo, AllocationSize = req.Size, MemoryTypeIndex = i };
                Check(_vk.AllocateMemory(_device, in allocInfo, null, out var memory), "AllocateMemory(voxel)");
                return memory;
            }
        }

        throw new InvalidOperationException($"No Vulkan memory type satisfies {flags} for a voxel volume.");
    }

    private void DestroyImages(Resident r)
    {
        if (r.View.Handle != 0)
        {
            _vk.DestroyImageView(_device, r.View, null);
            r.View = default;
        }

        if (r.Image.Handle != 0)
        {
            _vk.DestroyImage(_device, r.Image, null);
            r.Image = default;
        }

        if (r.Memory.Handle != 0)
        {
            _vk.FreeMemory(_device, r.Memory, null);
            r.Memory = default;
        }

        if (r.Staging.Handle != 0)
        {
            _vk.DestroyBuffer(_device, r.Staging, null);
            r.Staging = default;
        }

        if (r.StagingMemory.Handle != 0)
        {
            _vk.FreeMemory(_device, r.StagingMemory, null);
            r.StagingMemory = default;
        }
    }

    private static void Check(Result result, string what)
    {
        if (result != Result.Success)
        {
            throw new InvalidOperationException($"Vulkan {what} failed: {result}");
        }
    }

    public void Dispose()
    {
        foreach (var r in _resident.Values)
        {
            DestroyImages(r);
        }

        _resident.Clear();
        if (_pool.Handle != 0)
        {
            _vk.DestroyDescriptorPool(_device, _pool, null);
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

        if (_layout.Handle != 0)
        {
            _vk.DestroyPipelineLayout(_device, _layout, null);
        }

        if (_vert.Handle != 0)
        {
            _vk.DestroyShaderModule(_device, _vert, null);
        }

        if (_frag.Handle != 0)
        {
            _vk.DestroyShaderModule(_device, _frag, null);
        }
    }
}
