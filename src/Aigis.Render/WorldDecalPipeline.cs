using System.Numerics;
using System.Runtime.InteropServices;
using Silk.NET.Core.Native;
using Silk.NET.Vulkan;

namespace Aigis.Render;

/// <summary>
/// The world-space ground decal draw — a <see cref="DynamicTexture"/> stain sheet alpha-blended
/// onto the floor — owned in ONE place and composed by BOTH renderers
/// (<c>HeadlessRenderer.Decals.cs</c> and <c>WindowedRenderer.Decals.cs</c>), for exactly the
/// reasons <see cref="OverlayImagePipeline"/>'s doc comment gives: image layout transitions,
/// barrier stage/access masks and descriptor-set lifetime are where two independently-maintained
/// copies drift silently, the more so because these renderers run without Vulkan validation layers.
/// <para>
/// <b>What is different from <see cref="OverlayImagePipeline"/>, and why this is not a parameter on
/// it.</b> Three things, each structural rather than cosmetic: the geometry is world-space and goes
/// through the camera matrix (so the push constant carries a <c>mat4</c> and the pipeline
/// depth-TESTS against the 3D scene it stains); the texture is mutable, so residency is keyed on
/// instance identity <em>plus</em> <see cref="DynamicTexture.Revision"/> and an upload happens again
/// whenever a stamp lands; and the staging buffer is therefore kept alive per texture instead of
/// being created and destroyed around a one-shot upload.
/// </para>
/// <para>
/// <b>Depth</b>: test <see cref="CompareOp.LessOrEqual"/>, write disabled. A body lying on the
/// stain occludes it; the stain does not punch a hole in the depth buffer for the particles and
/// debug lines drawn after it.
/// </para>
/// <para>
/// Hard rule 6 holds by construction: the headless renderer composes the same instance, so every
/// stain that a player sees is reproducible in an agent's screenshot with no window involved.
/// </para>
/// </summary>
internal sealed unsafe class WorldDecalPipeline : IDisposable
{
    /// <summary>Ceiling on simultaneously resident stain sheets — a decal surface is a per-level,
    /// per-storey object measured in megabytes, not a per-icon asset, so this bounds VRAM rather than
    /// counting the surfaces a host may ever show. Idle sheets are released well before it fills (see
    /// <see cref="IdleReleaseFrames"/>), so in practice it is the ceiling on what ONE FRAME may draw.
    /// <para>
    /// <b>Neither crossing it over a session nor in one frame fails.</b> Over a session the least
    /// recently used sheet is evicted (2026-09-05). Within one frame, the sheets past it are left out of
    /// that frame and counted in <see cref="DroppedSurfacesLastFrame"/> (2026-10-14). Both used to
    /// throw, and a throw on every frame lands inside <c>GameHost</c>'s render guard, where the fault
    /// barrier CONTAINS it — so the symptom was never a crash but a window frozen on its last presented
    /// frame while the simulation and the mixer ran on: 「フリーズしました。音のみゲームが続いていました」
    /// (a session past 16 accumulated sheets), then 「最後の部屋まで到達してから、戻ろうとすると…画面が
    /// 更新されなくなります」 (the 医大's camera shows ~50 m of its board wall; walking back from the last
    /// room put 17 distinct sheets in one frame before any blood).
    /// </para>
    /// <para>
    /// <b>128</b> (user decision, 2026-10-14; 16 until that report, briefly 32). The mistake the cap
    /// once caught by throwing — a <see cref="DynamicTexture"/> built per frame instead of once per
    /// surface — now shows as <see cref="FramesOverBudget"/> climbing, which <c>GameHost</c> reports
    /// once. See <see cref="OverlayImagePipeline.MaxTextures"/> for the same budget at a different scale.
    /// </para>
    /// </summary>
    internal const int MaxTextures = 128;

    /// <summary>The <c>maxPushConstantsSize</c> every conformant Vulkan implementation is required to
    /// support. <see cref="WorldDecalPush"/> is exactly this size, so this is a ceiling and not a
    /// budget with headroom — see that struct's <c>Tilt</c> field.</summary>
    private const int MaxGuaranteedPushConstantBytes = 128;

    [StructLayout(LayoutKind.Sequential)]
    private struct WorldDecalPush
    {
        public Matrix4x4 ViewProjection;
        public Vector4 Center;       // xyz = quad centre, w unused (std430 vec4 alignment)
        // xy = half size along the quad's own width/height axes; z = DecalQuadDraw.YawRadians,
        // w = DecalQuadDraw.Uprightness. The last two rode here (2026-08-20) rather than in two new
        // vec4s because this block was already 112 of the 128 guaranteed push-constant bytes.
        public Vector4 HalfExtents;
        public Vector4 Tint;

        // x = DecalQuadDraw.TiltAxisYawRadians, y = DecalQuadDraw.TiltRadians; zw unused (vec4
        // alignment). THIS LANE TAKES THE BLOCK FROM 112 TO 128 BYTES — exactly Vulkan's guaranteed
        // minimum maxPushConstantsSize, so it is still legal on every conformant device and there is
        // no room left at all. Anything further this pipeline needs per draw has to move to a uniform
        // buffer (or a per-instance vertex attribute); do not add a sixth vec4 here.
        public Vector4 Tilt;
    }

    /// <summary>One resident stain sheet. <see cref="UploadedRevision"/> is the
    /// <see cref="DynamicTexture.Revision"/> whose pixels the GPU image currently holds — the whole
    /// mechanism by which a frame that paints nothing costs no upload.</summary>
    private sealed class ResidentTexture
    {
        public Image Image;
        public DeviceMemory Memory;
        public ImageView View;
        public DescriptorSet Set;
        public Silk.NET.Vulkan.Buffer Staging;
        public DeviceMemory StagingMemory;
        public int UploadedRevision = -1;

        /// <summary>The <see cref="_prepareSequence"/> value of the last frame that drew this sheet —
        /// what picks the victim when the budget is full.</summary>
        public long LastUsedSequence;
    }

    private readonly Vk _vk;
    private readonly PhysicalDevice _physicalDevice;
    private readonly Device _device;
    private readonly Queue _queue;
    private readonly CommandPool _commandPool;

    /// <summary>Keyed on instance identity — see <see cref="DynamicTexture"/>'s doc comment on what
    /// that asks of callers (build one surface, keep it).</summary>
    private readonly Dictionary<DynamicTexture, ResidentTexture> _resident = new(ReferenceEqualityComparer.Instance);

    /// <summary>Counts calls to <see cref="PrepareUploads"/>, i.e. frames that drew at least one
    /// decal. Only the ORDER matters, so it is a plain counter rather than a clock — which also keeps
    /// eviction deterministic (hard rule 5): the same sequence of frames always evicts the same sheet.</summary>
    private long _prepareSequence;

    /// <summary>Per-call scratch for <see cref="PrepareUploads"/>, reused so a frame allocates nothing:
    /// the sheets this frame names that are not resident yet, in draw order and once each.</summary>
    private readonly List<DynamicTexture> _incoming = [];
    private readonly HashSet<DynamicTexture> _incomingSeen = new(ReferenceEqualityComparer.Instance);

    /// <summary>The sheets the last <see cref="PrepareUploads"/> could not fit: <see cref="Draw"/> skips
    /// exactly these, and still throws for a sheet that was never prepared at all.</summary>
    private readonly HashSet<DynamicTexture> _droppedThisFrame = new(ReferenceEqualityComparer.Instance);

    /// <summary>Scratch for <see cref="MakeRoom"/>: the sheets leaving this call.</summary>
    private readonly List<DynamicTexture> _leaving = [];

    /// <summary>Descriptor sets of freed sheets, rewritten by the next <see cref="Create"/> — the pool
    /// cannot free one individually (see <see cref="MakeRoom"/>).</summary>
    private readonly Stack<DescriptorSet> _spareSets = new();

    private bool _pipelineReady;
    private Pipeline _pipeline;
    private PipelineLayout _pipelineLayout;
    private ShaderModule _vert;
    private ShaderModule _frag;
    private Silk.NET.Vulkan.Buffer _quadBuffer;
    private DeviceMemory _quadMemory;
    private Sampler _sampler;
    private DescriptorSetLayout _setLayout;
    private DescriptorPool _descriptorPool;

    public WorldDecalPipeline(Vk vk, PhysicalDevice physicalDevice, Device device, Queue queue, CommandPool commandPool)
    {
        _vk = vk;
        _physicalDevice = physicalDevice;
        _device = device;
        _queue = queue;
        _commandPool = commandPool;
    }

    /// <summary>
    /// Creates the pipeline on first use, makes every sheet in <paramref name="draws"/> resident, and
    /// uploads any whose pixels the GPU does not already hold at its current revision.
    /// <para>
    /// <b>Must be called before the caller begins its render pass</b> — the upload records
    /// <c>vkCmdCopyBufferToImage</c> and image barriers, both illegal inside one, so it runs on its
    /// own one-shot command buffer that is submitted and fence-waited here.
    /// </para>
    /// <para>
    /// <b>Over the budget it draws less; it does not throw</b> (2026-10-14).
    /// A sheet that cannot be made resident — the frame itself names more than
    /// <see cref="MaxTextures"/> distinct sheets — is left out of THIS frame only: <see cref="Draw"/>
    /// skips it and <see cref="DroppedSurfacesLastFrame"/> counts it. The sheets kept are the ones
    /// already resident, then new ones in <paramref name="draws"/> order, so a caller lists what matters
    /// most first. Every eviction and idle release a call needs shares ONE device idle, however many
    /// sheets that is. Throwing was the old answer, and a throw on every frame is a frozen window. (Each
    /// sheet's UPLOAD is still its own submit and fence wait, as it always was — paid once when a sheet
    /// becomes resident or is repainted, not per frame it is drawn.)
    /// </para>
    /// </summary>
    /// <exception cref="ArgumentException">A draw has a null texture.</exception>
    public void PrepareUploads(IReadOnlyList<DecalQuadDraw> draws, RenderPass renderPass, bool dynamicViewport, uint width, uint height)
    {
        _droppedThisFrame.Clear();
        DroppedSurfacesLastFrame = 0;
        if (draws.Count == 0)
        {
            return;
        }

        EnsurePipeline(renderPass, dynamicViewport, width, height);
        _prepareSequence++;
        var sequence = _prepareSequence;

        // Stamp every resident sheet this frame names BEFORE anything is chosen to go, so none of
        // them can be; collect the new ones once each, in draw order.
        _incoming.Clear();
        _incomingSeen.Clear();
        foreach (var draw in draws)
        {
            if (draw.Texture is null)
            {
                throw new ArgumentException($"A {nameof(DecalQuadDraw)} has a null {nameof(DecalQuadDraw.Texture)}.", nameof(draws));
            }

            if (_resident.TryGetValue(draw.Texture, out var resident))
            {
                resident.LastUsedSequence = sequence;
            }
            else if (_incomingSeen.Add(draw.Texture))
            {
                _incoming.Add(draw.Texture);
            }
        }

        MakeRoom(sequence);

        foreach (var texture in _incoming)
        {
            if (_resident.Count >= MaxTextures)
            {
                _droppedThisFrame.Add(texture);
                continue;
            }

            if (TryCreate(texture) is not { } created)
            {
                _droppedThisFrame.Add(texture);
                continue;
            }

            created.LastUsedSequence = sequence;
            _resident.Add(texture, created);
        }

        DroppedSurfacesLastFrame = _droppedThisFrame.Count;
        if (DroppedSurfacesLastFrame > 0)
        {
            FramesOverBudget++;
        }

        foreach (var draw in draws)
        {
            if (_resident.TryGetValue(draw.Texture, out var resident) && resident.UploadedRevision != draw.Texture.Revision)
            {
                Upload(resident, draw.Texture);
            }
        }

        // Not held past the call: a stretch with no decals must not keep multi-MB CPU rasters alive.
        // (_droppedThisFrame is, until Draw has run; the next call clears it.)
        _incoming.Clear();
        _incomingSeen.Clear();
    }

    /// <summary>How many distinct sheets the last <see cref="PrepareUploads"/> had to leave out because
    /// the frame named more than <see cref="MaxTextures"/>. Zero on every healthy frame.</summary>
    public int DroppedSurfacesLastFrame { get; private set; }

    /// <summary>How many frames so far have left at least one sheet out. A host reads this to report the
    /// overflow once rather than every frame.</summary>
    public long FramesOverBudget { get; private set; }

    /// <summary>Device idles taken to free sheets — test visibility for "one per frame that frees
    /// anything, never one per sheet".</summary>
    internal long DeviceIdleWaits { get; private set; }

    /// <summary>Sheets that could not be made resident because the device refused an allocation.</summary>
    public long AllocationFailures { get; private set; }

    /// <summary>Sheets resident now. Test visibility for the idle release.</summary>
    internal int ResidentCount => _resident.Count;

    /// <summary>Records the draws. Every sheet must already be resident — call
    /// <see cref="PrepareUploads"/> outside the render pass first.</summary>
    public void Draw(CommandBuffer cmd, IReadOnlyList<DecalQuadDraw> draws, Matrix4x4 viewProjection)
    {
        if (draws.Count == 0)
        {
            return;
        }

        _vk.CmdBindPipeline(cmd, PipelineBindPoint.Graphics, _pipeline);
        var buffer = _quadBuffer;
        ulong offset = 0;
        _vk.CmdBindVertexBuffers(cmd, 0, 1, &buffer, &offset);

        // 2026-08-20: the previously-bound set, so a run of draws sharing one texture pays for a
        // single CmdBindDescriptorSets instead of one per quad. That went from a micro-optimization to
        // a real one when foliage arrived: a scatter of hundreds of sprites is hundreds of draws off
        // ONE sprite sheet, where before this path only ever saw a handful of distinct stain sheets.
        var boundSet = default(DescriptorSet);
        foreach (var draw in draws)
        {
            if (!_resident.TryGetValue(draw.Texture, out var resident))
            {
                if (_droppedThisFrame.Contains(draw.Texture))
                {
                    // Over the budget this frame — see PrepareUploads. Left out, not an error.
                    continue;
                }

                throw new InvalidOperationException(
                    $"Decal surface was never uploaded — {nameof(PrepareUploads)} must be called with the same draw list, " +
                    "outside the render pass, before recording draws.");
            }

            var set = resident.Set;
            if (set.Handle != boundSet.Handle)
            {
                _vk.CmdBindDescriptorSets(cmd, PipelineBindPoint.Graphics, _pipelineLayout, 0, 1, &set, 0, null);
                boundSet = set;
            }

            var push = new WorldDecalPush
            {
                ViewProjection = viewProjection,
                Center = new Vector4(draw.Center, 0f),
                HalfExtents = new Vector4(draw.HalfExtents, draw.YawRadians, draw.Uprightness),
                Tint = draw.Tint,
                Tilt = new Vector4(draw.TiltAxisYawRadians, draw.TiltRadians, 0f, 0f),
            };
            _vk.CmdPushConstants(cmd, _pipelineLayout, ShaderStageFlags.VertexBit | ShaderStageFlags.FragmentBit, 0, (uint)sizeof(WorldDecalPush), &push);
            _vk.CmdDraw(cmd, 6, 1, 0, 0);
        }
    }

    // --- residency ----------------------------------------------------------------------------

    /// <summary>A sheet no frame has drawn for this many <see cref="PrepareUploads"/> calls is released —
    /// ~10 s at 60 fps, counted in frames that draw any decal at all (each renderer prepares once per
    /// frame, and only when it has decals; a stretch with none simply does not age anything). With a 128-sheet budget, waiting for the budget to fill before anything
    /// goes would keep every past round's blood sheets (~3 MB of image plus ~3 MB of staging each) for
    /// the life of the launch; this holds what stays resident to what was recently on screen. A sheet
    /// that comes back is simply made resident again and re-uploaded.</summary>
    internal const long IdleReleaseFrames = 600;

    /// <summary>
    /// Frees what has to go before this frame's new sheets go in: every sheet idle past
    /// <see cref="IdleReleaseFrames"/>, then — only if the budget still cannot hold the new ones — the
    /// least recently used sheets this frame does not draw.
    /// <para><b>A sheet drawn by THIS frame is never chosen.</b> <see cref="PrepareUploads"/> stamped
    /// every one with <paramref name="sequence"/> first, and only older stamps are candidates — so
    /// evicting can never take back a surface the caller is about to draw. When there are not enough
    /// candidates, the new sheets that do not fit are left out of the frame rather than thrashing.</para>
    /// <para><b>One device idle for all of it.</b> <see cref="PrepareUploads"/> runs BEFORE the frame's
    /// own fence wait (see <c>WindowedRenderer.RenderPrimitiveSceneAndPresent</c>), so a previously
    /// submitted frame may still be sampling an image about to be destroyed; a full idle is the
    /// obviously-correct synchronisation, and it happens only on the rare frame that frees anything —
    /// once, however many sheets that frame frees. It used to be once PER evicted sheet.</para>
    /// <para><b>The descriptor set is kept, not freed.</b> The pool is created without
    /// <c>FreeDescriptorSetBit</c> and sized at exactly <see cref="MaxTextures"/> sets, so a freed
    /// sheet's set goes on <see cref="_spareSets"/> and the next <see cref="Create"/> rewrites it.</para>
    /// <para>Ties in age are broken by the dictionary's enumeration order, which is arbitrary once
    /// anything has been removed — harmless: which of two equally old, undrawn sheets goes cannot change a
    /// pixel, and hard rule 5 is about the simulation, which nothing here feeds.</para>
    /// </summary>
    private void MakeRoom(long sequence)
    {
        _leaving.Clear();
        foreach (var (key, resident) in _resident)
        {
            if (sequence - resident.LastUsedSequence > IdleReleaseFrames)
            {
                _leaving.Add(key);
            }
        }

        // Then the least recently used of the sheets this frame does not draw, one at a time. A plain
        // selection rather than a sort: it allocates nothing, and on a frame that stays over the budget
        // with nothing evictable (every resident sheet drawn this frame) it ends on its first pass.
        var shortfall = _incoming.Count - (MaxTextures - _resident.Count + _leaving.Count);
        while (shortfall > 0)
        {
            DynamicTexture? oldest = null;
            var oldestSequence = sequence;
            foreach (var (key, resident) in _resident)
            {
                if (resident.LastUsedSequence < oldestSequence && !_leaving.Contains(key))
                {
                    oldest = key;
                    oldestSequence = resident.LastUsedSequence;
                }
            }

            if (oldest is null)
            {
                break;
            }

            _leaving.Add(oldest);
            shortfall--;
        }

        if (_leaving.Count == 0)
        {
            return;
        }

        _vk.DeviceWaitIdle(_device);
        DeviceIdleWaits++;
        foreach (var key in _leaving)
        {
            var resident = _resident[key];
            _resident.Remove(key);
            DestroyImages(resident);
            _spareSets.Push(resident.Set);
        }

        _leaving.Clear();
    }

    /// <summary>
    /// <see cref="Create"/>, or null when the device cannot hold one more sheet. Inside the budget a
    /// failed allocation is still possible — 128 sheets of a ~3 MB stain sheet plus its kept staging copy
    /// is ~768 MB of each kind at worst — and a throw here would be the frozen window all over again (it
    /// lands in the host's render guard every frame). So the sheet is left out of the frame like one over
    /// the budget, and counted the same way; the next frame simply tries again.
    /// </summary>
    private ResidentTexture? TryCreate(DynamicTexture texture)
    {
        var resident = new ResidentTexture();
        try
        {
            return Create(texture, resident);
        }
        catch (InvalidOperationException)
        {
            // Not swallowed: the sheet is counted in DroppedSurfacesLastFrame, which GameHost reports.
            DestroyImages(resident);
            AllocationFailures++;
            return null;
        }
    }

    private ResidentTexture Create(DynamicTexture texture, ResidentTexture resident)
    {
        CreateImages(texture, resident);
        if (_spareSets.TryPop(out var spare))
        {
            resident.Set = spare;
            PointDescriptorSetAt(spare, resident.View);
        }
        else
        {
            resident.Set = AllocateDescriptorSet(resident.View);
        }

        return resident;
    }

    /// <summary>Builds the staging buffer, device image and view for one sheet into
    /// <paramref name="target"/> — everything a resident slot owns EXCEPT its descriptor set, which
    /// outlives an eviction (see <see cref="MakeRoom"/>). Each handle lands on <paramref name="target"/>
    /// the moment it exists, so a failure part-way leaves <see cref="DestroyImages"/> able to free what
    /// was made (see <see cref="TryCreate"/>).</summary>
    private void CreateImages(DynamicTexture texture, ResidentTexture target)
    {
        var size = (ulong)texture.ByteLength;
        var stagingInfo = new BufferCreateInfo
        {
            SType = StructureType.BufferCreateInfo,
            Size = size,
            Usage = BufferUsageFlags.TransferSrcBit,
            SharingMode = SharingMode.Exclusive,
        };
        Check(_vk.CreateBuffer(_device, in stagingInfo, null, out var staging), "CreateBuffer(decalStaging)");
        target.Staging = staging;
        _vk.GetBufferMemoryRequirements(_device, staging, out var stagingReq);
        var stagingMemory = AllocateMemory(stagingReq, MemoryPropertyFlags.HostVisibleBit | MemoryPropertyFlags.HostCoherentBit);
        target.StagingMemory = stagingMemory;
        Check(_vk.BindBufferMemory(_device, staging, stagingMemory, 0), "BindBufferMemory(decalStaging)");

        var imageInfo = new ImageCreateInfo
        {
            SType = StructureType.ImageCreateInfo,
            ImageType = ImageType.Type2D,
            // Unorm, matching every other sampled path in this engine (see OverlayImagePipeline's
            // note): an Srgb view would brighten a stain relative to the lit floor it sits on.
            Format = Format.R8G8B8A8Unorm,
            Extent = new Extent3D((uint)texture.Width, (uint)texture.Height, 1),
            MipLevels = 1,
            ArrayLayers = 1,
            Samples = SampleCountFlags.Count1Bit,
            Tiling = ImageTiling.Optimal,
            Usage = ImageUsageFlags.TransferDstBit | ImageUsageFlags.SampledBit,
            SharingMode = SharingMode.Exclusive,
            InitialLayout = ImageLayout.Undefined,
        };
        Check(_vk.CreateImage(_device, in imageInfo, null, out var image), "CreateImage(decal)");
        target.Image = image;
        _vk.GetImageMemoryRequirements(_device, image, out var imageReq);
        var imageMemory = AllocateMemory(imageReq, MemoryPropertyFlags.DeviceLocalBit);
        target.Memory = imageMemory;
        Check(_vk.BindImageMemory(_device, image, imageMemory, 0), "BindImageMemory(decal)");

        var viewInfo = new ImageViewCreateInfo
        {
            SType = StructureType.ImageViewCreateInfo,
            Image = image,
            ViewType = ImageViewType.Type2D,
            Format = Format.R8G8B8A8Unorm,
            SubresourceRange = new ImageSubresourceRange(ImageAspectFlags.ColorBit, 0, 1, 0, 1),
        };
        Check(_vk.CreateImageView(_device, in viewInfo, null, out var view), "CreateImageView(decal)");
        target.View = view;
    }

    /// <summary>Destroys one slot's images and staging buffer, leaving its descriptor set alone (the
    /// caller either recycles the slot or is tearing the whole pool down). Every handle is guarded and
    /// zeroed, so this is safe to call twice — which <see cref="Dispose"/> relies on.</summary>
    private void DestroyImages(ResidentTexture resident)
    {
        if (resident.View.Handle != 0)
        {
            _vk.DestroyImageView(_device, resident.View, null);
            resident.View = default;
        }

        if (resident.Image.Handle != 0)
        {
            _vk.DestroyImage(_device, resident.Image, null);
            resident.Image = default;
        }

        if (resident.Memory.Handle != 0)
        {
            _vk.FreeMemory(_device, resident.Memory, null);
            resident.Memory = default;
        }

        if (resident.Staging.Handle != 0)
        {
            _vk.DestroyBuffer(_device, resident.Staging, null);
            resident.Staging = default;
        }

        if (resident.StagingMemory.Handle != 0)
        {
            _vk.FreeMemory(_device, resident.StagingMemory, null);
            resident.StagingMemory = default;
        }
    }

    /// <summary>
    /// Copies the whole raster up and leaves the image shader-readable again. The old layout is
    /// <see cref="ImageLayout.Undefined"/> on the first upload (nothing to preserve) and
    /// <see cref="ImageLayout.ShaderReadOnlyOptimal"/> on every later one — and the later case is
    /// why the barrier's source stage is the fragment shader: the previous frame's sampled reads
    /// must finish before this frame's transfer writes over them.
    /// </summary>
    private void Upload(ResidentTexture resident, DynamicTexture texture)
    {
        var size = (ulong)texture.ByteLength;
        void* mapped = null;
        Check(_vk.MapMemory(_device, resident.StagingMemory, 0, size, 0, ref mapped), "MapMemory(decalStaging)");
        texture.Rgba.CopyTo(new Span<byte>(mapped, texture.ByteLength));
        _vk.UnmapMemory(_device, resident.StagingMemory);

        var first = resident.UploadedRevision < 0;
        RecordAndSubmitUpload(resident, (uint)texture.Width, (uint)texture.Height, first);
        resident.UploadedRevision = texture.Revision;
    }

    private void RecordAndSubmitUpload(ResidentTexture resident, uint width, uint height, bool firstUpload)
    {
        var allocInfo = new CommandBufferAllocateInfo
        {
            SType = StructureType.CommandBufferAllocateInfo,
            CommandPool = _commandPool,
            Level = CommandBufferLevel.Primary,
            CommandBufferCount = 1,
        };
        Check(_vk.AllocateCommandBuffers(_device, in allocInfo, out var cmd), "AllocateCommandBuffers(decalUpload)");

        var begin = new CommandBufferBeginInfo
        {
            SType = StructureType.CommandBufferBeginInfo,
            Flags = CommandBufferUsageFlags.OneTimeSubmitBit,
        };
        Check(_vk.BeginCommandBuffer(cmd, in begin), "BeginCommandBuffer(decalUpload)");

        var range = new ImageSubresourceRange(ImageAspectFlags.ColorBit, 0, 1, 0, 1);
        var toTransferDst = new ImageMemoryBarrier
        {
            SType = StructureType.ImageMemoryBarrier,
            OldLayout = firstUpload ? ImageLayout.Undefined : ImageLayout.ShaderReadOnlyOptimal,
            NewLayout = ImageLayout.TransferDstOptimal,
            SrcQueueFamilyIndex = Vk.QueueFamilyIgnored,
            DstQueueFamilyIndex = Vk.QueueFamilyIgnored,
            Image = resident.Image,
            SubresourceRange = range,
            SrcAccessMask = firstUpload ? AccessFlags.None : AccessFlags.ShaderReadBit,
            DstAccessMask = AccessFlags.TransferWriteBit,
        };
        _vk.CmdPipelineBarrier(
            cmd,
            firstUpload ? PipelineStageFlags.TopOfPipeBit : PipelineStageFlags.FragmentShaderBit,
            PipelineStageFlags.TransferBit,
            0, 0, null, 0, null, 1, in toTransferDst);

        var region = new BufferImageCopy
        {
            BufferOffset = 0,
            BufferRowLength = 0,   // 0 = tightly packed, which DynamicTexture guarantees.
            BufferImageHeight = 0,
            ImageSubresource = new ImageSubresourceLayers(ImageAspectFlags.ColorBit, 0, 0, 1),
            ImageOffset = new Offset3D(0, 0, 0),
            ImageExtent = new Extent3D(width, height, 1),
        };
        _vk.CmdCopyBufferToImage(cmd, resident.Staging, resident.Image, ImageLayout.TransferDstOptimal, 1, in region);

        var toShaderRead = new ImageMemoryBarrier
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
        _vk.CmdPipelineBarrier(cmd, PipelineStageFlags.TransferBit, PipelineStageFlags.FragmentShaderBit, 0, 0, null, 0, null, 1, in toShaderRead);

        Check(_vk.EndCommandBuffer(cmd), "EndCommandBuffer(decalUpload)");

        var fenceInfo = new FenceCreateInfo { SType = StructureType.FenceCreateInfo };
        Check(_vk.CreateFence(_device, in fenceInfo, null, out var fence), "CreateFence(decalUpload)");
        var submit = new SubmitInfo
        {
            SType = StructureType.SubmitInfo,
            CommandBufferCount = 1,
            PCommandBuffers = &cmd,
        };
        Check(_vk.QueueSubmit(_queue, 1, in submit, fence), "QueueSubmit(decalUpload)");
        Check(_vk.WaitForFences(_device, 1, in fence, true, ulong.MaxValue), "WaitForFences(decalUpload)");

        _vk.DestroyFence(_device, fence, null);
        _vk.FreeCommandBuffers(_device, _commandPool, 1, in cmd);
    }

    /// <summary>One combined-image-sampler set per surface. Safe to write once and bind every frame
    /// for the same reason <see cref="OverlayImagePipeline.AllocateDescriptorSet"/> gives (one frame
    /// in flight in both renderers), and a re-upload never rewrites it because the image and view it
    /// points at are reused — an EVICTION does, through <see cref="PointDescriptorSetAt"/>, because
    /// that is the one path on which a slot's view changes.</summary>
    private DescriptorSet AllocateDescriptorSet(ImageView view)
    {
        var setLayout = _setLayout;
        var allocInfo = new DescriptorSetAllocateInfo
        {
            SType = StructureType.DescriptorSetAllocateInfo,
            DescriptorPool = _descriptorPool,
            DescriptorSetCount = 1,
            PSetLayouts = &setLayout,
        };
        Check(_vk.AllocateDescriptorSets(_device, in allocInfo, out var set), "AllocateDescriptorSets(decal)");
        PointDescriptorSetAt(set, view);
        return set;
    }

    /// <summary>Rewrites one already-allocated set to sample <paramref name="view"/>. Legal because the
    /// caller has waited for the device to go idle, so no submitted command buffer still binds it.</summary>
    private void PointDescriptorSetAt(DescriptorSet set, ImageView view)
    {
        var imageInfo = new DescriptorImageInfo
        {
            Sampler = _sampler,
            ImageView = view,
            ImageLayout = ImageLayout.ShaderReadOnlyOptimal,
        };
        var write = new WriteDescriptorSet
        {
            SType = StructureType.WriteDescriptorSet,
            DstSet = set,
            DstBinding = 0,
            DescriptorCount = 1,
            DescriptorType = DescriptorType.CombinedImageSampler,
            PImageInfo = &imageInfo,
        };
        _vk.UpdateDescriptorSets(_device, 1, in write, 0, null);
    }

    // --- pipeline -----------------------------------------------------------------------------

    private void EnsurePipeline(RenderPass renderPass, bool dynamicViewport, uint width, uint height)
    {
        if (_pipelineReady)
        {
            return;
        }

        // Checked here rather than trusted: the block sits at EXACTLY Vulkan's guaranteed-minimum
        // maxPushConstantsSize, so a field added to WorldDecalPush without moving to a uniform buffer
        // would be a pipeline-creation failure on a minimum-spec device and silently fine on the
        // developer's own. Fail at the boundary, with the reason, on every device.
        if (sizeof(WorldDecalPush) > MaxGuaranteedPushConstantBytes)
        {
            throw new InvalidOperationException(
                $"{nameof(WorldDecalPush)} is {sizeof(WorldDecalPush)} bytes, past the {MaxGuaranteedPushConstantBytes} " +
                "Vulkan guarantees for push constants. Move the per-draw data to a uniform buffer or a per-instance " +
                "vertex attribute instead of growing this block.");
        }

        CreateQuadBuffer();
        CreateSampler();
        CreateDescriptorLayoutAndPool();

        _vert = CreateShaderModule(PrecompiledShaders.WorldDecalVertSpirv);
        _frag = CreateShaderModule(PrecompiledShaders.WorldDecalFragSpirv);

        var entry = (byte*)SilkMarshal.StringToPtr("main");
        try
        {
            var stages = stackalloc PipelineShaderStageCreateInfo[2];
            stages[0] = new PipelineShaderStageCreateInfo { SType = StructureType.PipelineShaderStageCreateInfo, Stage = ShaderStageFlags.VertexBit, Module = _vert, PName = entry };
            stages[1] = new PipelineShaderStageCreateInfo { SType = StructureType.PipelineShaderStageCreateInfo, Stage = ShaderStageFlags.FragmentBit, Module = _frag, PName = entry };

            // Position only; the UV is inPos + 0.5 in the vertex shader, as with the overlay-image
            // pipeline this borrows its unit-quad convention from.
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

            var viewport = new Viewport { X = 0, Y = 0, Width = width, Height = height, MinDepth = 0, MaxDepth = 1 };
            var scissor = new Rect2D(new Offset2D(0, 0), new Extent2D(width, height));
            var viewportState = dynamicViewport
                ? new PipelineViewportStateCreateInfo { SType = StructureType.PipelineViewportStateCreateInfo, ViewportCount = 1, ScissorCount = 1 }
                : new PipelineViewportStateCreateInfo { SType = StructureType.PipelineViewportStateCreateInfo, ViewportCount = 1, PViewports = &viewport, ScissorCount = 1, PScissors = &scissor };

            var dynamicStates = stackalloc DynamicState[2] { DynamicState.Viewport, DynamicState.Scissor };
            var dynamicState = new PipelineDynamicStateCreateInfo { SType = StructureType.PipelineDynamicStateCreateInfo, DynamicStateCount = 2, PDynamicStates = dynamicStates };

            // CullMode None: a ground quad is viewed from above by the game camera, but a debug or
            // cutaway camera dipping below the floor must still see the stain rather than have it
            // vanish — there is no depth-correctness reason to cull a single flat quad.
            var rasterizer = new PipelineRasterizationStateCreateInfo { SType = StructureType.PipelineRasterizationStateCreateInfo, PolygonMode = PolygonMode.Fill, CullMode = CullModeFlags.None, FrontFace = FrontFace.CounterClockwise, LineWidth = 1f };
            var multisample = new PipelineMultisampleStateCreateInfo { SType = StructureType.PipelineMultisampleStateCreateInfo, RasterizationSamples = SampleCountFlags.Count1Bit, MinSampleShading = 1f };

            // The one place this differs from every existing textured pipeline: a decal is part of
            // the 3D scene, so it must be occluded by geometry in front of it (test) without
            // occluding the transparent things drawn after it (no write).
            var depthStencil = new PipelineDepthStencilStateCreateInfo
            {
                SType = StructureType.PipelineDepthStencilStateCreateInfo,
                DepthTestEnable = true,
                DepthWriteEnable = false,
                DepthCompareOp = CompareOp.LessOrEqual,
            };

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

            var pushRange = new PushConstantRange { StageFlags = ShaderStageFlags.VertexBit | ShaderStageFlags.FragmentBit, Offset = 0, Size = (uint)sizeof(WorldDecalPush) };
            var setLayout = _setLayout;
            var layoutInfo = new PipelineLayoutCreateInfo
            {
                SType = StructureType.PipelineLayoutCreateInfo,
                SetLayoutCount = 1,
                PSetLayouts = &setLayout,
                PushConstantRangeCount = 1,
                PPushConstantRanges = &pushRange,
            };
            Check(_vk.CreatePipelineLayout(_device, in layoutInfo, null, out _pipelineLayout), "CreatePipelineLayout(decal)");

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
            Check(_vk.CreateGraphicsPipelines(_device, default, 1, in pipelineInfo, null, out _pipeline), "CreateGraphicsPipelines(decal)");
        }
        finally
        {
            SilkMarshal.Free((nint)entry);
        }

        _pipelineReady = true;
    }

    private void CreateQuadBuffer()
    {
        float[] vertices =
        [
            -0.5f, -0.5f, 0.5f, -0.5f, 0.5f, 0.5f,
            -0.5f, -0.5f, 0.5f, 0.5f, -0.5f, 0.5f,
        ];
        var size = (ulong)(vertices.Length * sizeof(float));
        var info = new BufferCreateInfo { SType = StructureType.BufferCreateInfo, Size = size, Usage = BufferUsageFlags.VertexBufferBit, SharingMode = SharingMode.Exclusive };
        Check(_vk.CreateBuffer(_device, in info, null, out _quadBuffer), "CreateBuffer(decalQuad)");

        _vk.GetBufferMemoryRequirements(_device, _quadBuffer, out var req);
        _quadMemory = AllocateMemory(req, MemoryPropertyFlags.HostVisibleBit | MemoryPropertyFlags.HostCoherentBit);
        Check(_vk.BindBufferMemory(_device, _quadBuffer, _quadMemory, 0), "BindBufferMemory(decalQuad)");

        void* mapped = null;
        Check(_vk.MapMemory(_device, _quadMemory, 0, size, 0, ref mapped), "MapMemory(decalQuad)");
        vertices.AsSpan().CopyTo(new Span<float>(mapped, vertices.Length));
        _vk.UnmapMemory(_device, _quadMemory);
    }

    /// <summary><see cref="Filter.Linear"/>, unlike the HUD's nearest-sampled icons: a stain sheet
    /// is magnified hard (a 512-texel sheet stretched over tens of metres, seen from a few metres
    /// up), so nearest sampling would render every drip as a visible square. Linear is also why the
    /// brushes bother with a soft rim — the two together are what make a stamp read as a fluid mark.
    /// <see cref="SamplerAddressMode.ClampToEdge"/> keeps a stain painted at the sheet's border from
    /// wrapping around to the opposite side of the level.</summary>
    private void CreateSampler()
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
            BorderColor = BorderColor.FloatTransparentBlack,
            UnnormalizedCoordinates = false,
        };
        Check(_vk.CreateSampler(_device, in info, null, out _sampler), "CreateSampler(decal)");
    }

    private void CreateDescriptorLayoutAndPool()
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
        Check(_vk.CreateDescriptorSetLayout(_device, in setLayoutInfo, null, out _setLayout), "CreateDescriptorSetLayout(decal)");

        var poolSize = new DescriptorPoolSize { Type = DescriptorType.CombinedImageSampler, DescriptorCount = MaxTextures };
        var poolInfo = new DescriptorPoolCreateInfo
        {
            SType = StructureType.DescriptorPoolCreateInfo,
            PoolSizeCount = 1,
            PPoolSizes = &poolSize,
            MaxSets = MaxTextures,
        };
        Check(_vk.CreateDescriptorPool(_device, in poolInfo, null, out _descriptorPool), "CreateDescriptorPool(decal)");
    }

    // --- helpers ------------------------------------------------------------------------------

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
            Check(_vk.CreateShaderModule(_device, in info, null, out var module), "CreateShaderModule(decal)");
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
                Check(_vk.AllocateMemory(_device, in allocInfo, null, out var memory), "AllocateMemory(decal)");
                return memory;
            }
        }

        throw new InvalidOperationException($"No Vulkan memory type satisfies {flags} for the decal allocation.");
    }

    private static void Check(Result result, string what)
    {
        if (result != Result.Success)
        {
            throw new InvalidOperationException($"Vulkan {what} failed: {result}");
        }
    }

    /// <summary>Destroys every resource in reverse-of-creation order. Each guard means a renderer
    /// that never drew a decal destroys nothing — all handles start zeroed.</summary>
    public void Dispose()
    {
        foreach (var resident in _resident.Values)
        {
            // Descriptor sets are freed wholesale by DestroyDescriptorPool below (the pool was not
            // created with FreeDescriptorSetBit), so they are deliberately not freed individually —
            // which is exactly why DestroyImages leaves them alone.
            DestroyImages(resident);
        }

        _resident.Clear();

        if (_descriptorPool.Handle != 0)
        {
            _vk.DestroyDescriptorPool(_device, _descriptorPool, null);
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
