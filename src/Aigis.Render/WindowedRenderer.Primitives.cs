using System.Collections.Immutable;
using System.Numerics;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using Silk.NET.Core.Native;
using Silk.NET.Vulkan;

namespace Aigis.Render;

/// <summary>
/// Milestone B: draws the same <see cref="PrimitiveInstance"/>/<see cref="DebugLine"/>
/// scenes as <see cref="HeadlessRenderer.RenderPrimitiveScene"/>, live to the window.
/// The pipeline uses dynamic viewport/scissor (set per-frame from the current swapchain
/// extent) instead of baking the extent in at pipeline-creation time like
/// <see cref="HeadlessRenderer"/> does — HeadlessRenderer never resizes, WindowedRenderer
/// always might, so the pipeline itself survives a <see cref="RecreateSwapchain"/> unchanged.
/// <para>
/// TASK-152: instances are grouped by <see cref="PrimitiveKind"/>
/// (<see cref="PrimitiveInstancePacker"/>) into one instance-rate vertex buffer, so a frame
/// issues at most one <c>vkCmdDraw</c> per kind present — not one per instance. There is no
/// legacy single-cube <c>BoxInstance</c> path here (unlike <see cref="HeadlessRenderer"/>'s
/// <c>RenderScene</c>/<c>RenderBoxes</c>), so this pipeline could be converted in place rather
/// than needing a second one kept side-by-side.
/// </para>
/// </summary>
public sealed unsafe partial class WindowedRenderer
{
    /// <summary>
    /// How many camera views one present may carry. <b>A hard device-resource limit, not a policy</b>
    /// — this renderer allocates one lighting descriptor set, buffer and mapping PER VIEW up front
    /// (see the four arrays below), and <c>WindowedRenderer.Irradiance.cs</c> walks exactly this many.
    ///
    /// <para><b>Public since 2026-09-21</b>, because a caller that builds a variable number of overlay
    /// panes has no other way to know when to stop, and finding out by exception means the frame is
    /// already lost. It cost a game an unbounded stream of
    /// <c>ArgumentOutOfRangeException</c> out of the present loop — one per frame, for the whole time
    /// four casualties were adrift at once — which is not a thing a game can catch and recover from at
    /// that point. Build within the budget instead.</para>
    /// </summary>
    public const int MaxPresentationViews = 4;
    /// <summary>Mirrors <see cref="ShaderSource.BoxFrag"/>/<see cref="ShaderSource.PrimitiveFrag"/>'s
    /// <c>Lighting</c> uniform block layout exactly — kept byte-for-byte identical to
    /// <c>HeadlessRenderer.LightingUniform</c> (see that struct's doc comment for the full
    /// TASK-149 std140 byte-offset table; not repeated here to avoid the two drifting out of
    /// sync in wording while staying identical in layout — <c>PrimitiveLightingUboLayoutTests</c>
    /// pins both by <c>sizeof</c>/<c>Marshal.OffsetOf</c>).</summary>
    [StructLayout(LayoutKind.Sequential)]
    internal struct LightingUniform
    {
        public Vector4 SunDirectionIntensity;
        public Vector4 SunColor;
        public Vector4 AmbientColorIntensity;

        /// <summary>TASK-148: x = shadows enabled (0/1), y = <see cref="ShadowMapConfig.ComparisonBias"/>,
        /// z = one shadow-map texel in UV space, w unused.</summary>
        public Vector4 ShadowParams;

        /// <summary>x = number of valid entries in <see cref="PointLights"/>.</summary>
        public Vector4 PointLightCount;

        public PointLightGpuSlots PointLights;

        /// <summary>TASK-151: appended after <see cref="PointLights"/>, byte-for-byte identical to
        /// <c>HeadlessRenderer.LightingUniform</c>'s own fields of the same name — see that
        /// struct's doc comment for the full offset table.</summary>
        public Vector4 AmbientGroundColor;

        public Vector4 CameraPositionSpecularIntensity;

        public Vector4 SpecularShininessExposureToneEnabled;

        /// <summary>TASK-168: appended after TASK-151's fields, byte-for-byte identical to
        /// <c>HeadlessRenderer.LightingUniform</c>'s own fields of the same name — see that
        /// struct's doc comment for the full offset table and for why spots are a separate
        /// appended array rather than a wider <see cref="PointLightGpu"/>.</summary>
        public Vector4 SpotLightCount;

        public SpotLightGpu Spot0;
        public SpotLightGpu Spot1;
        public SpotLightGpu Spot2;
        public SpotLightGpu Spot3;

        /// <summary>TASK-245: mirrors <c>HeadlessRenderer.LightingUniform.IrradianceOriginEnabled</c>
        /// - see that field for the packing. w = 0 (no volume bound) is what makes this renderer's
        /// ambient term byte-for-byte its pre-TASK-245 self.</summary>
        public Vector4 IrradianceOriginEnabled;

        /// <summary>TASK-245: mirrors <c>HeadlessRenderer.LightingUniform.IrradianceScale</c>.</summary>
        public Vector4 IrradianceScale;

        /// <summary>TASK-245: mirrors <c>HeadlessRenderer.LightingUniform.IrradianceBias</c>.</summary>
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

    /// <summary>Byte-for-byte identical to <c>HeadlessRenderer.PointLightGpu</c> — see that
    /// type's doc comment.</summary>
    [StructLayout(LayoutKind.Sequential)]
    internal struct PointLightGpu
    {
        public Vector4 PositionRange;
        public Vector4 ColorIntensity;
    }

    [InlineArray(SceneLighting.MaxPointLights)]
    internal struct PointLightGpuSlots
    {
        private PointLightGpu _element0;
    }

    /// <summary>TASK-168: byte-for-byte identical to <c>HeadlessRenderer.SpotLightGpu</c> — see that
    /// type's doc comment, including why every field's own offset (not just the total size) is
    /// pinned by <c>PrimitiveLightingUboLayoutTests</c>.</summary>
    [StructLayout(LayoutKind.Sequential)]
    internal struct SpotLightGpu
    {
        public Vector4 PositionRange;
        public Vector4 ColorIntensity;
        public Vector4 DirectionCosOuter;
        public Vector4 ConeCosInner;
    }

    private readonly PrimitiveInstancePacker _primitivePacker = new();
    private readonly List<PrimitiveInstancePacker> _multiViewPackers = [];

    /// <summary>The <see cref="ForegroundLayer"/>'s instances, packed with the layer's own camera and
    /// uploaded directly after the scene's instances (single- or multi-view) in the shared buffer.</summary>
    private readonly PrimitiveInstancePacker _foregroundPacker = new();
    private float[] _multiViewInstanceStaging = [];
    private bool _primitivePipelineReady;
    private Pipeline _primitivePipeline;

    /// <summary>The depth-prepass twin of <see cref="_primitivePipeline"/>: same vertex stage, same
    /// vertex input, colour writes masked off, depth written. See
    /// <see cref="ShaderSource.PrimitiveDepthFrag"/> for why the colour pass needs it in front.</summary>
    private Pipeline _primitiveDepthPipeline;

    /// <summary>The through-the-wall twin of <see cref="_primitivePipeline"/>: same stages, same
    /// vertex layout, depth test AND depth write both off, so a
    /// <see cref="MaterialKind.XRayOutlineShell"/> paints over whatever the colour pass already drew.
    /// Mirrors <c>HeadlessRenderer._primitiveXRayPipeline</c> — both renderers must produce the same
    /// frame (hard rule 6).</summary>
    private Pipeline _primitiveXRayPipeline;

    /// <summary>The BLENDING twin of <see cref="_primitivePipeline"/>: same stages, same raster state,
    /// additive colour blend and no depth write. Draws
    /// <see cref="PrimitiveInstancePacker.GlowBatches"/> after the colour pass. Mirrors
    /// <c>HeadlessRenderer._primitiveGlowPipeline</c> — both renderers must produce the same
    /// picture (hard rule 6).</summary>
    private Pipeline _primitiveGlowPipeline;
    private PipelineLayout _primitivePipelineLayout;
    private ShaderModule _primitiveVert;
    private ShaderModule _primitiveFrag;
    private ShaderModule _primitiveDepthFrag;

    private Silk.NET.Vulkan.Buffer _cubeVertexBuffer;
    private DeviceMemory _cubeVertexMemory;
    private uint _cubeVertexCount;
    private Silk.NET.Vulkan.Buffer _sphereVertexBuffer;
    private DeviceMemory _sphereVertexMemory;
    private uint _sphereVertexCount;
    private Silk.NET.Vulkan.Buffer _capsuleVertexBuffer;
    private DeviceMemory _capsuleVertexMemory;
    private uint _capsuleVertexCount;
    private Silk.NET.Vulkan.Buffer _cylinderVertexBuffer;
    private DeviceMemory _cylinderVertexMemory;
    private uint _cylinderVertexCount;
    private Silk.NET.Vulkan.Buffer _primitiveInstanceBuffer;
    private DeviceMemory _primitiveInstanceMemory;
    private int _primitiveInstanceCapacity;

    private DescriptorSetLayout _lightingSetLayout;
    private DescriptorPool _lightingPool;
    private DescriptorSet _lightingSet;
    private Silk.NET.Vulkan.Buffer _lightingBuffer;
    private DeviceMemory _lightingMemory;
    private void* _lightingMapped;
    private readonly DescriptorSet[] _multiViewLightingSets = new DescriptorSet[MaxPresentationViews];
    private readonly Silk.NET.Vulkan.Buffer[] _multiViewLightingBuffers = new Silk.NET.Vulkan.Buffer[MaxPresentationViews];
    private readonly DeviceMemory[] _multiViewLightingMemories = new DeviceMemory[MaxPresentationViews];
    private readonly nint[] _multiViewLightingMapped = new nint[MaxPresentationViews];
    private Silk.NET.Vulkan.Buffer _fixtureShadowBuffer;
    private DeviceMemory _fixtureShadowMemory;
    private void* _fixtureShadowMapped;

    /// <summary>Clears to <paramref name="clearColor"/>, draws <paramref name="instances"/>
    /// (cube/sphere/capsule) and <paramref name="lines"/>, then presents. Returns false if
    /// the frame was skipped (minimized, or the swapchain just got recreated) — same
    /// contract as <see cref="RenderClearAndPresent"/>.</summary>
    /// <param name="clearColor">RGBA clear color (0..1).</param>
    /// <param name="viewProjection">Camera view-projection for <paramref name="instances"/>/<paramref name="lines"/>.</param>
    /// <param name="instances">Cube/sphere/capsule instances to draw.</param>
    /// <param name="lines">Debug line overlay, drawn after <paramref name="instances"/>.</param>
    /// <param name="text">Screen-space HUD text (TASK-041/TASK-051) drawn last, after
    /// <paramref name="lines"/>, with depth test/write disabled. Optional and defaults to
    /// none so existing callers are unaffected.</param>
    /// <param name="particles">Camera-facing billboards, drawn over <paramref name="instances"/>
    /// and under <paramref name="lines"/> — the same order, and for the same reason, as
    /// <see cref="HeadlessRenderer.RenderPrimitiveScene"/>: what a human sees in the window and
    /// what an agent verifies from a screenshot must be the same frame.</param>
    /// <param name="lighting">Directional + ambient light (TASK-036). Defaults to
    /// <see cref="SceneLighting.Default"/>, which reproduces the old hardcoded shader
    /// bit-for-bit — existing callers/goldens are unaffected by leaving this unset.</param>
    /// <param name="shadow">TASK-148: optional sun shadow-map configuration. <see langword="null"/>
    /// (the default) skips the shadow depth pass entirely — see
    /// <see cref="HeadlessRenderer.RenderPrimitiveScene"/>'s matching parameter; both renderers
    /// must stay in step (hard rule 6).</param>
    /// <param name="cameraPosition">TASK-151: the camera eye, world-space. See
    /// <see cref="HeadlessRenderer.RenderPrimitiveScene"/>'s matching parameter for the full
    /// fail-loud contract (both renderers must stay in step, hard rule 6).</param>
    /// <param name="images">TASK-159: textured screen-space overlays (HUD icons, portraits). Same
    /// contract as <see cref="HeadlessRenderer.RenderPrimitiveScene"/>'s matching parameter — the
    /// point of TASK-159 is that this capability exists on BOTH hosts, so a game's icons are not
    /// headless-only.</param>
    /// <param name="shapes">Screen-space UI surfaces — see <see cref="UiShapeDraw"/>. Same contract
    /// and same draw position (after lines, before images and text) as
    /// <see cref="HeadlessRenderer.RenderPrimitiveScene"/>'s matching parameter; both renderers must
    /// stay in step (hard rule 6).</param>
    /// <param name="decals">World-space ground stain sheets (blood trails). Same contract and same
    /// draw position (after the opaque scene, before particles and lines) as
    /// <see cref="HeadlessRenderer.RenderPrimitiveScene"/>'s matching parameter; both renderers must
    /// stay in step (hard rule 6).</param>
    /// <param name="foreground">3D primitives over the UI shapes, under images and text — see
    /// <see cref="ForegroundLayer"/> and <see cref="HeadlessRenderer.RenderPrimitiveScene"/>'s
    /// matching parameter. <see langword="null"/> records nothing extra.</param>
    /// <param name="postProcess">The miniature finishing pass — see
    /// <see cref="HeadlessRenderer.RenderPrimitiveScene"/>'s matching parameter.</param>
    /// <exception cref="ArgumentException"><paramref name="lighting"/> requests specular but
    /// <paramref name="cameraPosition"/> is <see langword="null"/> — see
    /// <see cref="HeadlessRenderer.RenderPrimitiveScene"/>'s matching parameter.</exception>
    public bool RenderPrimitiveSceneAndPresent(
        Vector4 clearColor,
        Matrix4x4 viewProjection,
        IReadOnlyList<PrimitiveInstance> instances,
        IReadOnlyList<DebugLine> lines,
        IReadOnlyList<ScreenTextDraw>? text = null,
        IReadOnlyList<ParticleInstance>? particles = null,
        SceneLighting? lighting = null,
        ShadowMapConfig? shadow = null,
        Vector3? cameraPosition = null,
        IReadOnlyList<OverlayImageDraw>? images = null,
        IReadOnlyList<UiShapeDraw>? shapes = null,
        IReadOnlyList<DecalQuadDraw>? decals = null,
        ForegroundLayer? foreground = null,
        PostProcessSettings? postProcess = null)
    {
        // Before the fence is reset and an image acquired: a bad value thrown mid-frame would leave the
        // fence unsignalled and freeze every later frame.
        postProcess?.Validate();
        EnsurePrimitivePipeline();
        EnsureShadowPipeline();
        EnsureLinePipeline();

        var effectiveLighting = lighting ?? SceneLighting.Default;
        // 2026-09-19: the eye is RECOVERED from the view-projection when a caller does not name one —
        // see Camera.TryGetEye. An explicit cameraPosition still wins, so no existing caller's frame
        // moves. Mirrors HeadlessRenderer.RenderPrimitiveScene exactly (hard rule 6: the two renderers
        // must draw the same frame).
        var eye = cameraPosition ?? (Camera.TryGetEye(viewProjection, out var derivedEye) ? derivedEye : null);
        if (effectiveLighting.Specular.Intensity > 0f && eye is null)
        {
            throw new ArgumentException(
                $"{nameof(lighting)}.{nameof(SceneLighting.Specular)}.{nameof(SpecularConfig.Intensity)} is {effectiveLighting.Specular.Intensity}, but {nameof(cameraPosition)} was not supplied and this frame's projection is ORTHOGRAPHIC, which has no eye point to derive — specular needs a view direction. Pass the camera's Eye (see IGameCamera.Eye) or set {nameof(SceneLighting.Specular)} to {nameof(SpecularConfig)}.{nameof(SpecularConfig.None)}.",
                nameof(cameraPosition));
        }

        // Opened HERE rather than after the uploads below (2026-09-05): those uploads are megabytes of
        // stain sheet ending in a fence wait, and timing them from TryBeginFrame left every one of them
        // outside the breakdown — a frame that paid for one read as unattributed time, which is exactly
        // what a mid-round hitch needs a name for. See Phase.PrepareTextures.
        RenderPhases.Begin();

        // Before TryBeginFrame, and therefore before any render pass or frame recording: the
        // texture upload records image barriers and a buffer→image copy on its OWN one-shot
        // command buffer, neither of which is legal inside a render pass.
        var effectiveImages = images ?? [];
        PrepareOverlayImages(effectiveImages);

        // TASK-245: must stay ahead of UpdateLighting below -- that method only enables sampling
        // for the volume already RESIDENT on the GPU, so preparing afterwards would upload a volume
        // and then not read it. See HeadlessRenderer.RenderPrimitiveScene's own note.
        PrepareIrradiance(effectiveLighting.Irradiance);
        PrepareSurfaceTextures(effectiveLighting.SurfaceTextures);
        var effectiveDecals = decals ?? [];
        PrepareDecals(effectiveDecals);
        RenderPhases.Mark(RenderPhaseProfiler.Phase.PrepareTextures);

        if (!TryBeginFrame(out var imageIndex))
        {
            return false;
        }

        RenderPhases.Mark(RenderPhaseProfiler.Phase.WaitAndAcquire);
        ReadGpuTimestamps();

        UploadLines(lines);
        UploadParticles(particles ?? [], viewProjection);

        var shadowViewProjection = shadow.HasValue
            ? Camera.DirectionalShadowViewProjection(shadow.Value.AabbMin, shadow.Value.AabbMax, effectiveLighting.Sun.Direction)
            : Matrix4x4.Identity;
        Span<Matrix4x4> fixtureShadowViewProjections = stackalloc Matrix4x4[SceneLighting.MaxDownwardFixtureShadows];
        var fixtureShadowCount = BuildFixtureShadowViewProjections(effectiveLighting, shadow, fixtureShadowViewProjections);
        UpdateLighting(effectiveLighting, shadowsEnabled: shadow.HasValue, eye, fixtureShadowCount: fixtureShadowCount);
        UpdateFixtureShadows(fixtureShadowViewProjections);

        _primitivePacker.Pack(instances, viewProjection, shadowViewProjection);
        PackForeground(foreground);

        EnsureBatchMeshes(_primitivePacker);
        EnsureBatchMeshes(_foregroundPacker);

        UploadPrimitiveInstances();

        var begin = new CommandBufferBeginInfo { SType = StructureType.CommandBufferBeginInfo };
        Check(_vk.BeginCommandBuffer(_commandBuffer, in begin), "BeginCommandBuffer");
        BeginGpuTimestamps(_commandBuffer);
        RenderPhases.Mark(RenderPhaseProfiler.Phase.Upload);

        if (shadow.HasValue)
        {
            BeginShadowRenderPass(_commandBuffer, _shadowFramebuffer);
            DrawShadowBatches(_commandBuffer);
            _vk.CmdEndRenderPass(_commandBuffer);
            for (var i = 0; i < fixtureShadowCount; i++)
            {
                BeginShadowRenderPass(_commandBuffer, _fixtureShadowFramebuffers[i]);
                DrawFixtureShadowBatches(_commandBuffer, i);
                _vk.CmdEndRenderPass(_commandBuffer);
            }
        }

        WriteGpuTimestamp(_commandBuffer, RenderPhaseProfiler.GpuPhase.Shadow);
        RenderPhases.Mark(RenderPhaseProfiler.Phase.RecordShadow);

        var clears = stackalloc ClearValue[2];
        clears[0] = new ClearValue { Color = new ClearColorValue(clearColor.X, clearColor.Y, clearColor.Z, clearColor.W) };
        clears[1] = new ClearValue { DepthStencil = new ClearDepthStencilValue(1.0f, 0) };
        var renderPassInfo = new RenderPassBeginInfo
        {
            SType = StructureType.RenderPassBeginInfo,
            RenderPass = _renderPass,
            Framebuffer = _swapchainFramebuffers[imageIndex],
            RenderArea = new Rect2D(new Offset2D(0, 0), _swapchainExtent),
            ClearValueCount = 2,
            PClearValues = clears,
        };
        // With a post-process the scene is recorded into the offscreen pass first; the ordinary pass
        // above opens after it, once the scene is recorded, and starts with the finished picture.
        var sceneInfo = renderPassInfo;
        var sceneExtent = _swapchainExtent;
        if (postProcess is { } postSettings)
        {
            var (scenePass, sceneFramebuffer) = PreparePostProcess(postSettings);
            sceneInfo.RenderPass = scenePass;
            sceneInfo.Framebuffer = sceneFramebuffer;
            // RenderScale: the scene is drawn at the offscreen target's (possibly smaller) size.
            sceneExtent = SceneExtent;
            sceneInfo.RenderArea = new Rect2D(new Offset2D(0, 0), sceneExtent);
        }

        _vk.CmdBeginRenderPass(_commandBuffer, in sceneInfo, SubpassContents.Inline);

        var viewport = new Viewport { X = 0, Y = 0, Width = _swapchainExtent.Width, Height = _swapchainExtent.Height, MinDepth = 0, MaxDepth = 1 };
        var scissor = new Rect2D(new Offset2D(0, 0), _swapchainExtent);
        var sceneViewport = viewport with { Width = sceneExtent.Width, Height = sceneExtent.Height };
        var sceneScissor = new Rect2D(new Offset2D(0, 0), sceneExtent);
        _vk.CmdSetViewport(_commandBuffer, 0, 1, &sceneViewport);
        _vk.CmdSetScissor(_commandBuffer, 0, 1, &sceneScissor);

        DrawPrimitiveDepthPrepass(_commandBuffer, _primitivePacker, 0);
        DrawPrimitiveBatches(_commandBuffer);
        DrawPrimitiveXRayBatches(_commandBuffer, _primitivePacker, 0);
        DrawPrimitiveGlowBatches(_commandBuffer, _primitivePacker, 0);
        WriteGpuTimestamp(_commandBuffer, RenderPhaseProfiler.GpuPhase.Primitives);

        DrawDecals(_commandBuffer, effectiveDecals, viewProjection);
        DrawParticles(_commandBuffer, viewProjection);
        DrawLines(_commandBuffer, viewProjection);
        WriteGpuTimestamp(_commandBuffer, RenderPhaseProfiler.GpuPhase.Transparent);
        RenderPhases.Mark(RenderPhaseProfiler.Phase.RecordScene);

        if (postProcess is { } finish)
        {
            _vk.CmdEndRenderPass(_commandBuffer);
            ReadOnlySpan<Matrix4x4> occlusionCameras = [viewProjection];
            ReadOnlySpan<Vector4> occlusionRects = [new Vector4(0f, 0f, 1f, 1f)];
            RecordPostProcessOcclusion(_commandBuffer, finish, occlusionCameras, occlusionRects);
            _vk.CmdBeginRenderPass(_commandBuffer, in renderPassInfo, SubpassContents.Inline);
            // The occlusion pass left its own half-size viewport behind.
            _vk.CmdSetViewport(_commandBuffer, 0, 1, &viewport);
            _vk.CmdSetScissor(_commandBuffer, 0, 1, &scissor);
            DrawPostProcess(_commandBuffer, finish, viewProjection);
        }

        WriteGpuTimestamp(_commandBuffer, RenderPhaseProfiler.GpuPhase.PostProcess);
        DrawUiShapes(_commandBuffer, shapes ?? []);
        DrawForegroundLayer(_commandBuffer, _primitivePacker.TotalCount);
        DrawOverlayImages(_commandBuffer, effectiveImages);
        ScreenText.BuildQuads(text ?? [], _overlayQuads);
        DrawOverlay(_commandBuffer, _overlayQuads);
        WriteGpuTimestamp(_commandBuffer, RenderPhaseProfiler.GpuPhase.Overlay);
        RenderPhases.Mark(RenderPhaseProfiler.Phase.RecordOverlay);

        _vk.CmdEndRenderPass(_commandBuffer);
        Check(_vk.EndCommandBuffer(_commandBuffer), "EndCommandBuffer");

        EndFrame(imageIndex);
        RenderPhases.Mark(RenderPhaseProfiler.Phase.SubmitPresent);
        RenderPhases.EndFrame();
        return true;
    }

    /// <summary>
    /// Presents one shared primitive scene through multiple non-overlapping camera views. Each view
    /// owns a dynamic viewport/scissor rectangle and a disjoint range of the instance buffer, so
    /// its camera matrices cannot overwrite another view before the command buffer is submitted.
    /// Particles and debug lines are rendered inside every view with that view's camera matrix.
    /// Screen-space overlays are composed once across the complete swapchain after all world views;
    /// callers therefore supply full-frame overlay coordinates, not viewport-local coordinates —
    /// which applies to <paramref name="shapes"/> exactly as it does to <paramref name="text"/> and
    /// <paramref name="images"/>. Up to four views are supported, matching the host's four-way split
    /// layout.
    /// <para><c>foreground</c> (see <see cref="ForegroundLayer"/>) is drawn ONCE over the whole
    /// swapchain after the shapes, with its own camera and view 0's lighting — like the other
    /// screen-space layers, it is not per view.</para>
    /// <para><paramref name="postProcess"/> (2026-10-10): every view is recorded into the ONE
    /// offscreen scene target, then the finishing pass runs once per view, scissored to that view's
    /// rectangle, with the view's camera re-expressed in whole-target clip space
    /// (<see cref="Camera.ViewportToTargetClip"/>) so the ambient occlusion rebuilds world positions
    /// from the whole-target uv it is actually sampling at. The screen-space terms (tilt-shift band,
    /// vignette) stay whole-target on purpose: views cut from one camera
    /// (<see cref="Camera.OffCenterViewProjection(System.Numerics.Vector3, System.Numerics.Vector3, System.Numerics.Vector3, FrustumWindow, float, float)"/>) then finish to exactly the frame the single-view
    /// path would have drawn, which is what lets a game split its screen without the picture changing.
    /// Blur and bloom taps near a view's edge may read the neighbouring view's pixels.
    /// <see langword="null"/> draws the views exactly as before.</para>
    /// </summary>
    public bool RenderPrimitiveViewsAndPresent(
        Vector4 clearColor,
        IReadOnlyList<CameraView> views,
        IReadOnlyList<PrimitiveInstance> instances,
        IReadOnlyList<DebugLine> lines,
        IReadOnlyList<ParticleInstance>? particles = null,
        IReadOnlyList<ScreenTextDraw>? text = null,
        IReadOnlyList<OverlayImageDraw>? images = null,
        SceneLighting? lighting = null,
        ShadowMapConfig? shadow = null,
        IReadOnlyList<UiShapeDraw>? shapes = null,
        IReadOnlyList<DecalQuadDraw>? decals = null,
        ForegroundLayer? foreground = null,
        PostProcessSettings? postProcess = null)
    {
        ArgumentNullException.ThrowIfNull(views);
        ArgumentNullException.ThrowIfNull(instances);
        ArgumentNullException.ThrowIfNull(lines);
        if (views.Count == 0)
        {
            throw new ArgumentException("At least one camera view is required.", nameof(views));
        }
        if (views.Count > MaxPresentationViews)
        {
            throw new ArgumentOutOfRangeException(nameof(views), $"At most {MaxPresentationViews} camera views are supported.");
        }

        // Before any frame state moves, for RenderPrimitiveSceneAndPresent's reason: a bad value thrown
        // after the fence is reset would freeze every later frame.
        postProcess?.Validate();
        EnsurePrimitivePipeline();
        EnsureShadowPipeline();
        EnsureLinePipeline();
        var effectiveLighting = lighting ?? SceneLighting.Default;
        effectiveLighting.Validate();
        if (!PrepareSwapchainForRender())
        {
            return false;
        }

        ValidateViewsForSwapchain(views);
        var effectiveImages = images ?? [];
        PrepareOverlayImages(effectiveImages);

        // TASK-245: must stay ahead of UpdateLighting below -- that method only enables sampling
        // for the volume already RESIDENT on the GPU, so preparing afterwards would upload a volume
        // and then not read it. See HeadlessRenderer.RenderPrimitiveScene's own note.
        PrepareIrradiance(effectiveLighting.Irradiance);
        PrepareSurfaceTextures(effectiveLighting.SurfaceTextures);
        var effectiveDecals = decals ?? [];
        PrepareDecals(effectiveDecals);
        var shadowViewProjection = shadow.HasValue
            ? Camera.DirectionalShadowViewProjection(shadow.Value.AabbMin, shadow.Value.AabbMax, effectiveLighting.Sun.Direction)
            : Matrix4x4.Identity;
        Span<Matrix4x4> fixtureShadowViewProjections = stackalloc Matrix4x4[SceneLighting.MaxDownwardFixtureShadows];
        var fixtureShadowCount = BuildFixtureShadowViewProjections(effectiveLighting, shadow, fixtureShadowViewProjections);
        EnsureMultiViewPackerCount(views.Count);
        var totalInstances = 0;
        var effectiveParticles = particles ?? [];
        EnsureMultiViewParticlePackerCount(views.Count);
        var particleRanges = new ParticleRange[views.Count];
        var totalParticles = 0;
        for (var i = 0; i < views.Count; i++)
        {
            _multiViewPackers[i].Pack(instances, views[i].ViewProjection, shadowViewProjection);
            EnsureBatchMeshes(_multiViewPackers[i]);
            totalInstances += _multiViewPackers[i].TotalCount;

            var particlePacker = _multiViewParticlePackers[i];
            particlePacker.Pack(effectiveParticles, views[i].ViewProjection);
            particleRanges[i] = new ParticleRange(totalParticles, particlePacker.AlphaCount, particlePacker.AdditiveCount);
            totalParticles += particlePacker.TotalCount;
        }

        if (shadow.HasValue)
        {
            _primitivePacker.Pack(instances, views[0].ViewProjection, shadowViewProjection);
        }

        PackForeground(foreground);
        EnsureBatchMeshes(_foregroundPacker);

        if (!TryBeginFrame(out var imageIndex))
        {
            return false;
        }

        UpdateFixtureShadows(fixtureShadowViewProjections);
        UploadPrimitiveInstances(_multiViewPackers, views.Count, totalInstances);
        UploadMultiViewParticles(views.Count, totalParticles);
        UploadLines(lines);
        for (var i = 0; i < views.Count; i++)
        {
            UpdateLighting(effectiveLighting, shadowsEnabled: shadow.HasValue, views[i].Position, i, fixtureShadowCount);
        }

        var begin = new CommandBufferBeginInfo { SType = StructureType.CommandBufferBeginInfo };
        Check(_vk.BeginCommandBuffer(_commandBuffer, in begin), "BeginCommandBuffer");
        if (shadow.HasValue)
        {
            BeginShadowRenderPass(_commandBuffer, _shadowFramebuffer);
            DrawShadowBatches(_commandBuffer);
            _vk.CmdEndRenderPass(_commandBuffer);
            for (var i = 0; i < fixtureShadowCount; i++)
            {
                BeginShadowRenderPass(_commandBuffer, _fixtureShadowFramebuffers[i]);
                DrawFixtureShadowBatches(_commandBuffer, i);
                _vk.CmdEndRenderPass(_commandBuffer);
            }
        }

        var clears = stackalloc ClearValue[2];
        clears[0] = new ClearValue { Color = new ClearColorValue(clearColor.X, clearColor.Y, clearColor.Z, clearColor.W) };
        clears[1] = new ClearValue { DepthStencil = new ClearDepthStencilValue(1.0f, 0) };
        var pass = new RenderPassBeginInfo
        {
            SType = StructureType.RenderPassBeginInfo,
            RenderPass = _renderPass,
            Framebuffer = _swapchainFramebuffers[imageIndex],
            RenderArea = new Rect2D(new Offset2D(0, 0), _swapchainExtent),
            ClearValueCount = 2,
            PClearValues = clears,
        };
        // With a post-process every view is recorded into the offscreen scene pass and the ordinary
        // pass opens afterwards — RenderPrimitiveSceneAndPresent's shape, with N views inside it.
        var scenePass = pass;
        var scaledScene = false;
        if (postProcess is { } postSettings)
        {
            var (postScenePass, postSceneFramebuffer) = PreparePostProcess(postSettings);
            scenePass.RenderPass = postScenePass;
            scenePass.Framebuffer = postSceneFramebuffer;
            scenePass.RenderArea = new Rect2D(new Offset2D(0, 0), SceneExtent);
            scaledScene = true;
        }

        _vk.CmdBeginRenderPass(_commandBuffer, in scenePass, SubpassContents.Inline);

        var baseInstance = 0;
        for (var i = 0; i < views.Count; i++)
        {
            var view = views[i];
            var packer = _multiViewPackers[i];
            // Window pixels, or the scaled scene's when the finishing pass draws it (RenderScale).
            var sceneRect = scaledScene ? ToScene(view.Viewport) : view.Viewport;
            SetViewportAndScissor(_commandBuffer, sceneRect);
            if (view.IsOverlay)
            {
                // Every view in this frame shares one depth buffer, cleared once at the start of the
                // render pass -- so an overlay drawn over an already-populated rectangle would be
                // depth-tested against the geometry underneath it and come out shredded. Clearing
                // both attachments inside its own rectangle is what makes it a clean pane rather than
                // a double exposure, and it is the only clear that can happen mid-pass.
                ClearAttachmentsWithin(_commandBuffer, sceneRect, clearColor);
            }

            DrawPrimitiveDepthPrepass(_commandBuffer, packer, baseInstance);
            DrawPrimitiveBatches(_commandBuffer, packer, baseInstance, i);
            DrawPrimitiveXRayBatches(_commandBuffer, packer, baseInstance, i);
            DrawPrimitiveGlowBatches(_commandBuffer, packer, baseInstance, i);
            // Per view, with that view's camera: a decal is world geometry, so each split-screen
            // player sees the same stains from their own angle (unlike the screen-space overlays
            // below, which are composed once across the whole swapchain).
            DrawDecals(_commandBuffer, effectiveDecals, view.ViewProjection);
            var particlesForView = particleRanges[i];
            DrawParticles(_commandBuffer, view.ViewProjection, particlesForView.AlphaCount, particlesForView.AdditiveCount, particlesForView.BaseInstance);
            DrawLines(_commandBuffer, view.ViewProjection);
            baseInstance += packer.TotalCount;
        }

        var wholeTarget = new RenderViewport(0, 0, checked((int)_swapchainExtent.Width), checked((int)_swapchainExtent.Height));
        if (postProcess is { } finish)
        {
            _vk.CmdEndRenderPass(_commandBuffer);
            RecordPostProcessOcclusionPerView(_commandBuffer, finish, views, wholeTarget);
            _vk.CmdBeginRenderPass(_commandBuffer, in pass, SubpassContents.Inline);
            DrawPostProcessPerView(_commandBuffer, finish, views, wholeTarget);
        }

        SetViewportAndScissor(_commandBuffer, wholeTarget);
        DrawUiShapes(_commandBuffer, shapes ?? []);
        DrawForegroundLayer(_commandBuffer, totalInstances);
        DrawOverlayImages(_commandBuffer, effectiveImages);
        ScreenText.BuildQuads(text ?? [], _overlayQuads);
        DrawOverlay(_commandBuffer, _overlayQuads);

        _vk.CmdEndRenderPass(_commandBuffer);
        Check(_vk.EndCommandBuffer(_commandBuffer), "EndCommandBuffer");
        EndFrame(imageIndex);
        return true;
    }

    /// <summary>One <c>vkCmdBindVertexBuffers</c> + one <c>vkCmdDraw</c> per
    /// (<see cref="PrimitiveKind"/>, <see cref="MaterialKind"/>) group actually present this frame
    /// (TASK-152, refined by TASK-150: draws <see cref="PrimitiveInstancePacker.VisibleBatches"/>,
    /// not <see cref="PrimitiveInstancePacker.Batches"/> — a <see cref="MaterialKind.ShadowCastOnly"/>
    /// instance must never rasterize here) — see <see cref="HeadlessRenderer.DrawPrimitiveBatches"/>,
    /// kept in lockstep with this method.</summary>
    private void DrawPrimitiveBatches(CommandBuffer commandBuffer)
        => DrawPrimitiveBatches(commandBuffer, _primitivePacker, 0);

    private void DrawPrimitiveBatches(CommandBuffer commandBuffer, PrimitiveInstancePacker packer, int baseInstance, int lightingSetIndex = 0)
    {
        if (packer.TotalCount == 0)
        {
            return;
        }
        if ((uint)lightingSetIndex >= MaxPresentationViews)
        {
            throw new ArgumentOutOfRangeException(nameof(lightingSetIndex));
        }

        _vk.CmdBindPipeline(commandBuffer, PipelineBindPoint.Graphics, _primitivePipeline);
        var lightingSet = _multiViewLightingSets[lightingSetIndex];
        _vk.CmdBindDescriptorSets(commandBuffer, PipelineBindPoint.Graphics, _primitivePipelineLayout, 0, 1, &lightingSet, 0, null);
        DrawPrimitiveBatchGeometry(commandBuffer, packer, baseInstance);
    }

    /// <summary>The depth prepass over exactly the batches
    /// <see cref="DrawPrimitiveBatches(CommandBuffer, PrimitiveInstancePacker, int, int)"/> will
    /// shade — <see cref="PrimitiveInstancePacker.VisibleBatches"/>, so a
    /// <see cref="MaterialKind.ShadowCastOnly"/> instance can no more occlude here than it can draw.
    /// Binds no descriptor set: <see cref="ShaderSource.PrimitiveDepthFrag"/> reads no lighting.</summary>
    private void DrawPrimitiveDepthPrepass(CommandBuffer commandBuffer, PrimitiveInstancePacker packer, int baseInstance)
    {
        if (packer.TotalCount == 0)
        {
            return;
        }

        _vk.CmdBindPipeline(commandBuffer, PipelineBindPoint.Graphics, _primitiveDepthPipeline);
        DrawPrimitiveBatchGeometry(commandBuffer, packer, baseInstance);
    }

    /// <summary>The through-the-wall silhouette pass — <see cref="PrimitiveInstancePacker.XRayBatches"/>
    /// only, drawn AFTER the colour pass with the depth test disabled. See
    /// <c>HeadlessRenderer.DrawPrimitiveXRayBatches</c>, kept in lockstep with this method.</summary>
    private void DrawPrimitiveXRayBatches(CommandBuffer commandBuffer, PrimitiveInstancePacker packer, int baseInstance, int lightingSetIndex = 0)
    {
        if (packer.XRayBatches.Length == 0)
        {
            return;
        }
        if ((uint)lightingSetIndex >= MaxPresentationViews)
        {
            throw new ArgumentOutOfRangeException(nameof(lightingSetIndex));
        }

        _vk.CmdBindPipeline(commandBuffer, PipelineBindPoint.Graphics, _primitiveXRayPipeline);
        var lightingSet = _multiViewLightingSets[lightingSetIndex];
        _vk.CmdBindDescriptorSets(commandBuffer, PipelineBindPoint.Graphics, _primitivePipelineLayout, 0, 1, &lightingSet, 0, null);
        DrawPrimitiveBatchGeometry(commandBuffer, packer, baseInstance, packer.XRayBatches);
    }

    /// <summary>The additive-glow pass — <see cref="PrimitiveInstancePacker.GlowBatches"/> only, drawn
    /// AFTER the colour pass with blending on and depth writes off. See
    /// <c>HeadlessRenderer.DrawPrimitiveGlowBatches</c>, kept in lockstep with this method.</summary>
    private void DrawPrimitiveGlowBatches(CommandBuffer commandBuffer, PrimitiveInstancePacker packer, int baseInstance, int lightingSetIndex = 0)
    {
        if (packer.GlowBatches.Length == 0)
        {
            return;
        }
        if ((uint)lightingSetIndex >= MaxPresentationViews)
        {
            throw new ArgumentOutOfRangeException(nameof(lightingSetIndex));
        }

        _vk.CmdBindPipeline(commandBuffer, PipelineBindPoint.Graphics, _primitiveGlowPipeline);
        var lightingSet = _multiViewLightingSets[lightingSetIndex];
        _vk.CmdBindDescriptorSets(commandBuffer, PipelineBindPoint.Graphics, _primitivePipelineLayout, 0, 1, &lightingSet, 0, null);
        DrawPrimitiveBatchGeometry(commandBuffer, packer, baseInstance, packer.GlowBatches);
    }

    /// <summary>The draw calls the primitive passes share — whichever pipeline is bound.</summary>
    private void DrawPrimitiveBatchGeometry(CommandBuffer commandBuffer, PrimitiveInstancePacker packer, int baseInstance)
        => DrawPrimitiveBatchGeometry(commandBuffer, packer, baseInstance, packer.VisibleBatches);

    private void DrawPrimitiveBatchGeometry(
        CommandBuffer commandBuffer,
        PrimitiveInstancePacker packer,
        int baseInstance,
        ReadOnlySpan<PrimitiveBatch> batches)
    {
        var buffers = stackalloc Silk.NET.Vulkan.Buffer[2];
        buffers[1] = _primitiveInstanceBuffer;
        var offsets = stackalloc ulong[2] { 0, 0 };
        foreach (var batch in batches)
        {
            var (mesh, vertexCount) = GeometryFor(batch);
            buffers[0] = mesh;
            _vk.CmdBindVertexBuffers(commandBuffer, 0, 2, buffers, offsets);
            _vk.CmdDraw(commandBuffer, vertexCount, (uint)batch.Count, 0, (uint)(baseInstance + batch.Offset));
        }
    }

    private void UploadPrimitiveInstances()
    {
        if (_foregroundPacker.TotalCount == 0)
        {
            UploadPrimitiveInstanceData(_primitivePacker.Data, _primitivePacker.TotalCount);
            return;
        }

        var sceneFloats = _primitivePacker.Data.Length;
        var requiredFloatCount = sceneFloats + _foregroundPacker.Data.Length;
        if (_multiViewInstanceStaging.Length < requiredFloatCount)
        {
            _multiViewInstanceStaging = new float[requiredFloatCount];
        }

        _primitivePacker.Data.CopyTo(_multiViewInstanceStaging);
        _foregroundPacker.Data.CopyTo(_multiViewInstanceStaging.AsSpan(sceneFloats));
        UploadPrimitiveInstanceData(
            _multiViewInstanceStaging.AsSpan(0, requiredFloatCount),
            _primitivePacker.TotalCount + _foregroundPacker.TotalCount);
    }

    /// <summary>Packs the <see cref="ForegroundLayer"/> (or clears the packer when absent). No-shadow
    /// shadow projection: the layer never casts or receives shadows and no shadow pass reads this
    /// packer.</summary>
    private void PackForeground(ForegroundLayer? foreground)
        => _foregroundPacker.Pack(foreground?.Instances ?? [], foreground?.ViewProjection ?? Matrix4x4.Identity, ForegroundLayer.NoShadowViewProjection);

    /// <summary>The <see cref="ForegroundLayer"/>, recorded after the UI shapes: depth (NOT colour) is
    /// cleared over the whole swapchain so the layer lands on top of the scene and the panels while
    /// still occluding itself, then the scene's depth prepass + colour pass run over
    /// <see cref="_foregroundPacker"/>'s range starting at <paramref name="baseInstance"/>. Lit by
    /// lighting set 0 (the single view, or split-screen view 0). Kept in lockstep with
    /// <c>HeadlessRenderer.DrawForegroundLayer</c>. Records nothing when the layer is absent/empty.</summary>
    private void DrawForegroundLayer(CommandBuffer commandBuffer, int baseInstance)
    {
        if (_foregroundPacker.VisibleBatches.Length == 0)
        {
            return;
        }

        var attachment = new ClearAttachment
        {
            AspectMask = ImageAspectFlags.DepthBit,
            ClearValue = new ClearValue { DepthStencil = new ClearDepthStencilValue(1.0f, 0) },
        };
        var rect = new ClearRect { Rect = new Rect2D(new Offset2D(0, 0), _swapchainExtent), BaseArrayLayer = 0, LayerCount = 1 };
        _vk.CmdClearAttachments(commandBuffer, 1, &attachment, 1, &rect);

        DrawPrimitiveDepthPrepass(commandBuffer, _foregroundPacker, baseInstance);
        DrawPrimitiveBatches(commandBuffer, _foregroundPacker, baseInstance, 0);
    }

    private void UploadPrimitiveInstances(List<PrimitiveInstancePacker> packers, int activePackerCount, int totalInstances)
    {
        var foregroundCount = _foregroundPacker.TotalCount;
        if (totalInstances + foregroundCount == 0)
        {
            return;
        }

        var requiredFloatCount = (totalInstances + foregroundCount) * PrimitiveInstancePacker.FloatsPerInstance;
        if (_multiViewInstanceStaging.Length < requiredFloatCount)
        {
            _multiViewInstanceStaging = new float[requiredFloatCount];
        }

        var offset = 0;
        for (var i = 0; i < activePackerCount; i++)
        {
            var packer = packers[i];
            packer.Data.CopyTo(_multiViewInstanceStaging.AsSpan(offset));
            offset += packer.Data.Length;
        }

        // The foreground layer's range follows every view's range: DrawForegroundLayer draws it at
        // baseInstance = totalInstances.
        _foregroundPacker.Data.CopyTo(_multiViewInstanceStaging.AsSpan(offset));

        UploadPrimitiveInstanceData(_multiViewInstanceStaging.AsSpan(0, requiredFloatCount), totalInstances + foregroundCount);
    }

    private void EnsureMultiViewPackerCount(int count)
    {
        while (_multiViewPackers.Count < count)
        {
            _multiViewPackers.Add(new PrimitiveInstancePacker());
        }
    }

    private readonly record struct ParticleRange(int BaseInstance, int AlphaCount, int AdditiveCount);

    private void UploadPrimitiveInstanceData(ReadOnlySpan<float> data, int instanceCount)
    {
        if (instanceCount == 0)
        {
            return;
        }

        EnsurePrimitiveInstanceCapacity(instanceCount);
        var size = (ulong)(data.Length * sizeof(float));
        void* mapped = null;
        Check(_vk.MapMemory(_device, _primitiveInstanceMemory, 0, size, 0, ref mapped), "MapMemory(primitiveInstances)");
        data.CopyTo(new Span<float>(mapped, data.Length));
        _vk.UnmapMemory(_device, _primitiveInstanceMemory);
    }

    private void ValidateViewsForSwapchain(IReadOnlyList<CameraView> views)
    {
        for (var i = 0; i < views.Count; i++)
        {
            var viewport = views[i].Viewport;
            views[i].Validate();
            if (viewport.XPx < 0 || viewport.YPx < 0
                || (long)viewport.XPx + viewport.WidthPx > _swapchainExtent.Width
                || (long)viewport.YPx + viewport.HeightPx > _swapchainExtent.Height)
            {
                throw new ArgumentOutOfRangeException(nameof(views), "Camera viewport must be contained by the swapchain framebuffer.");
            }

            // 2026-08-31: an OVERLAY view is allowed to cover the views before it -- see
            // CameraView.IsOverlay. Everything else keeps the outright rejection, because two panes
            // sharing pixels by accident is a layout bug that would otherwise only ever show up as a
            // flickering half-drawn view.
            if (views[i].IsOverlay)
            {
                continue;
            }

            for (var j = 0; j < i; j++)
            {
                var other = views[j].Viewport;
                if (viewport.XPx < (long)other.XPx + other.WidthPx && other.XPx < (long)viewport.XPx + viewport.WidthPx
                    && viewport.YPx < (long)other.YPx + other.HeightPx && other.YPx < (long)viewport.YPx + viewport.HeightPx)
                {
                    throw new ArgumentException("Camera viewports must not overlap.", nameof(views));
                }
            }
        }
    }

    /// <summary>Clears colour and depth inside one rectangle, mid-render-pass — what an overlay view
    /// needs before it draws (see <see cref="CameraView.IsOverlay"/>).</summary>
    private void ClearAttachmentsWithin(CommandBuffer commandBuffer, RenderViewport rect, Vector4 clearColor)
    {
        var attachments = stackalloc ClearAttachment[2];
        attachments[0] = new ClearAttachment
        {
            AspectMask = ImageAspectFlags.ColorBit,
            ColorAttachment = 0,
            ClearValue = new ClearValue { Color = new ClearColorValue(clearColor.X, clearColor.Y, clearColor.Z, clearColor.W) },
        };
        attachments[1] = new ClearAttachment
        {
            AspectMask = ImageAspectFlags.DepthBit,
            ClearValue = new ClearValue { DepthStencil = new ClearDepthStencilValue(1.0f, 0) },
        };

        var clearRect = new ClearRect
        {
            Rect = new Rect2D(new Offset2D(rect.XPx, rect.YPx), new Extent2D((uint)rect.WidthPx, (uint)rect.HeightPx)),
            BaseArrayLayer = 0,
            LayerCount = 1,
        };
        _vk.CmdClearAttachments(commandBuffer, 2, attachments, 1, &clearRect);
    }

    /// <summary>The finishing pass once per view, in list order — see
    /// <see cref="RenderPrimitiveViewsAndPresent"/>'s <c>postProcess</c> note. The viewport stays the
    /// whole target, so the fullscreen triangle's uv is whole-target uv; only the scissor narrows to
    /// the view. An overlay view, drawn later, therefore finishes over whatever it covers.
    /// <para><b>Where the taps may read.</b> While every view is a slice of ONE camera (their
    /// whole-target matrices agree) the target is one picture and is sampled as one, which is what
    /// makes the split invisible. The moment the cameras differ, each view skips taps that land in
    /// another view: a neighbour's depth rebuilt through this view's camera is a surface that does not
    /// exist, and the ambient occlusion draws it as a dark crease along the seam (seen in the first
    /// windowed run of Triage's third-person handoff).</para>
    /// <para>Not handled: an OVERLAY view inside another view's rectangle is not foreign to the view
    /// underneath, so that view's taps near the pane's border still read the pane. No caller combines
    /// panes with the finishing pass today (Triage keeps its pane frames unfinished); one that does
    /// should pass the panes' rectangles as exclusions.</para></summary>
    private void DrawPostProcessPerView(
        CommandBuffer commandBuffer, in PostProcessSettings settings, IReadOnlyList<CameraView> views, RenderViewport wholeTarget)
    {
        var targetSize = new Vector2(wholeTarget.WidthPx, wholeTarget.HeightPx);
        Span<Matrix4x4> wholeTargetCameras = stackalloc Matrix4x4[views.Count];
        var oneCamera = SplitViewFinish.WholeTargetCameras(views, targetSize, wholeTargetCameras);
        var viewport = new Viewport { X = 0, Y = 0, Width = wholeTarget.WidthPx, Height = wholeTarget.HeightPx, MinDepth = 0, MaxDepth = 1 };
        _vk.CmdSetViewport(commandBuffer, 0, 1, &viewport);
        for (var i = 0; i < views.Count; i++)
        {
            var rect = views[i].Viewport;
            var scissor = new Rect2D(new Offset2D(rect.XPx, rect.YPx), new Extent2D((uint)rect.WidthPx, (uint)rect.HeightPx));
            _vk.CmdSetScissor(commandBuffer, 0, 1, &scissor);
            DrawPostProcess(commandBuffer, settings, wholeTargetCameras[i], SplitViewFinish.SampleRect(rect, targetSize, oneCamera));
        }
    }

    /// <summary>The occlusion half of <see cref="DrawPostProcessPerView"/>, recorded before the
    /// ordinary pass opens: once over the whole target when the views are slices of one camera (each
    /// view's draw then samples the whole target too), otherwise once per view inside its own
    /// rectangle with its own camera.</summary>
    private void RecordPostProcessOcclusionPerView(
        CommandBuffer commandBuffer, in PostProcessSettings settings, IReadOnlyList<CameraView> views, RenderViewport wholeTarget)
    {
        var targetSize = new Vector2(wholeTarget.WidthPx, wholeTarget.HeightPx);
        Span<Matrix4x4> wholeTargetCameras = stackalloc Matrix4x4[views.Count];
        var oneCamera = SplitViewFinish.WholeTargetCameras(views, targetSize, wholeTargetCameras);
        if (oneCamera)
        {
            ReadOnlySpan<Vector4> whole = [new Vector4(0f, 0f, 1f, 1f)];
            RecordPostProcessOcclusion(commandBuffer, settings, wholeTargetCameras[..1], whole);
            return;
        }

        Span<Vector4> rects = stackalloc Vector4[views.Count];
        for (var i = 0; i < views.Count; i++)
        {
            rects[i] = SplitViewFinish.SampleRect(views[i].Viewport, targetSize, slicesOfOneCamera: false)
                ?? new Vector4(0f, 0f, 1f, 1f);
        }

        RecordPostProcessOcclusion(commandBuffer, settings, wholeTargetCameras, rects);
    }

    private void SetViewportAndScissor(CommandBuffer commandBuffer, RenderViewport rect)
    {
        var viewport = new Viewport { X = rect.XPx, Y = rect.YPx, Width = rect.WidthPx, Height = rect.HeightPx, MinDepth = 0, MaxDepth = 1 };
        var scissor = new Rect2D(new Offset2D(rect.XPx, rect.YPx), new Extent2D((uint)rect.WidthPx, (uint)rect.HeightPx));
        _vk.CmdSetViewport(commandBuffer, 0, 1, &viewport);
        _vk.CmdSetScissor(commandBuffer, 0, 1, &scissor);
    }

    private (Silk.NET.Vulkan.Buffer Buffer, uint Count) MeshFor(PrimitiveKind kind) => kind switch
    {
        PrimitiveKind.Cube => (_cubeVertexBuffer, _cubeVertexCount),
        PrimitiveKind.Sphere => (_sphereVertexBuffer, _sphereVertexCount),
        PrimitiveKind.Capsule => (_capsuleVertexBuffer, _capsuleVertexCount),
        PrimitiveKind.Cylinder => (_cylinderVertexBuffer, _cylinderVertexCount),
        _ => throw new ArgumentOutOfRangeException(nameof(kind)),
    };

    private void EnsurePrimitivePipeline()
    {
        if (_primitivePipelineReady)
        {
            return;
        }

        CreateMeshBuffer(PrimitiveMeshes.BuildCubeVertices(), out _cubeVertexBuffer, out _cubeVertexMemory, out _cubeVertexCount, "cube");
        CreateMeshBuffer(PrimitiveMeshes.BuildSphereVertices(), out _sphereVertexBuffer, out _sphereVertexMemory, out _sphereVertexCount, "sphere");
        CreateMeshBuffer(PrimitiveMeshes.BuildCapsuleVertices(), out _capsuleVertexBuffer, out _capsuleVertexMemory, out _capsuleVertexCount, "capsule");
        CreateMeshBuffer(PrimitiveMeshes.BuildCylinderVertices(), out _cylinderVertexBuffer, out _cylinderVertexMemory, out _cylinderVertexCount, "cylinder");
        EnsureLightingDescriptor();

        _primitiveVert = CreateShaderModule(PrecompiledShaders.PrimitiveVertSpirv);
        _primitiveFrag = CreateShaderModule(PrecompiledShaders.PrimitiveFragSpirv);
        _primitiveDepthFrag = CreateShaderModule(PrecompiledShaders.PrimitiveDepthFragSpirv);

        var entry = (byte*)SilkMarshal.StringToPtr("main");
        try
        {
            var stages = stackalloc PipelineShaderStageCreateInfo[2];
            stages[0] = new PipelineShaderStageCreateInfo
            {
                SType = StructureType.PipelineShaderStageCreateInfo,
                Stage = ShaderStageFlags.VertexBit,
                Module = _primitiveVert,
                PName = entry,
            };
            stages[1] = new PipelineShaderStageCreateInfo
            {
                SType = StructureType.PipelineShaderStageCreateInfo,
                Stage = ShaderStageFlags.FragmentBit,
                Module = _primitiveFrag,
                PName = entry,
            };

            var bindings = stackalloc VertexInputBindingDescription[2];
            bindings[0] = new VertexInputBindingDescription { Binding = 0, Stride = 6 * sizeof(float), InputRate = VertexInputRate.Vertex };
            bindings[1] = new VertexInputBindingDescription { Binding = 1, Stride = (uint)(PrimitiveInstancePacker.FloatsPerInstance * sizeof(float)), InputRate = VertexInputRate.Instance };

            var attributes = stackalloc VertexInputAttributeDescription[16];
            attributes[0] = new VertexInputAttributeDescription { Location = 0, Binding = 0, Format = Format.R32G32B32Sfloat, Offset = 0 };
            attributes[1] = new VertexInputAttributeDescription { Location = 1, Binding = 0, Format = Format.R32G32B32Sfloat, Offset = 3 * sizeof(float) };
            attributes[2] = new VertexInputAttributeDescription { Location = 2, Binding = 1, Format = Format.R32G32B32A32Sfloat, Offset = 0 * sizeof(float) };
            attributes[3] = new VertexInputAttributeDescription { Location = 3, Binding = 1, Format = Format.R32G32B32A32Sfloat, Offset = 4 * sizeof(float) };
            attributes[4] = new VertexInputAttributeDescription { Location = 4, Binding = 1, Format = Format.R32G32B32A32Sfloat, Offset = 8 * sizeof(float) };
            attributes[5] = new VertexInputAttributeDescription { Location = 5, Binding = 1, Format = Format.R32G32B32A32Sfloat, Offset = 12 * sizeof(float) };
            attributes[6] = new VertexInputAttributeDescription { Location = 6, Binding = 1, Format = Format.R32G32B32A32Sfloat, Offset = 16 * sizeof(float) };
            attributes[7] = new VertexInputAttributeDescription { Location = 7, Binding = 1, Format = Format.R32G32B32A32Sfloat, Offset = 20 * sizeof(float) };
            attributes[8] = new VertexInputAttributeDescription { Location = 8, Binding = 1, Format = Format.R32G32B32A32Sfloat, Offset = 24 * sizeof(float) };
            attributes[9] = new VertexInputAttributeDescription { Location = 9, Binding = 1, Format = Format.R32G32B32A32Sfloat, Offset = 28 * sizeof(float) };
            attributes[10] = new VertexInputAttributeDescription { Location = 10, Binding = 1, Format = Format.R32G32B32A32Sfloat, Offset = 32 * sizeof(float) };
            attributes[11] = new VertexInputAttributeDescription { Location = 11, Binding = 1, Format = Format.R32G32B32A32Sfloat, Offset = 36 * sizeof(float) };
            attributes[12] = new VertexInputAttributeDescription { Location = 12, Binding = 1, Format = Format.R32G32B32A32Sfloat, Offset = 40 * sizeof(float) };
            // TASK-149: worldCenter (see PrimitiveInstancePacker.FloatsPerInstance's doc comment).
            attributes[13] = new VertexInputAttributeDescription { Location = 13, Binding = 1, Format = Format.R32G32B32A32Sfloat, Offset = 44 * sizeof(float) };
            // TASK-168: worldScale -- the third factor of Model, so PrimitiveVert can derive a true
            // per-fragment world position for the spot-light term. Deliberately NOT added to either
            // shadow pipeline's attribute list: ShadowDepthVert never reads it, and a binding may
            // legally supply more bytes than its declared attributes consume. All four pipelines share
            // this one instance buffer and take their stride from the same FloatsPerInstance constant,
            // so the stride itself can never drift between them.
            attributes[14] = new VertexInputAttributeDescription { Location = 14, Binding = 1, Format = Format.R32G32B32A32Sfloat, Offset = 48 * sizeof(float) };

            // 2026-09-19: finish -- SurfaceFinish's roughness/metallic, for PrimitiveFrag's
            // microfacet path. Not added to either shadow pipeline for the same reason worldScale
            // above is not: a depth-only stage never shades, and a binding may legally supply more
            // bytes than its declared attributes consume.
            attributes[15] = new VertexInputAttributeDescription { Location = 15, Binding = 1, Format = Format.R32G32B32A32Sfloat, Offset = 52 * sizeof(float) };

            var vertexInput = new PipelineVertexInputStateCreateInfo
            {
                SType = StructureType.PipelineVertexInputStateCreateInfo,
                VertexBindingDescriptionCount = 2,
                PVertexBindingDescriptions = bindings,
                VertexAttributeDescriptionCount = 16,
                PVertexAttributeDescriptions = attributes,
            };

            var inputAssembly = new PipelineInputAssemblyStateCreateInfo { SType = StructureType.PipelineInputAssemblyStateCreateInfo, Topology = PrimitiveTopology.TriangleList };

            // Dynamic viewport/scissor — see class doc comment; actual values set per-frame
            // via vkCmdSetViewport/vkCmdSetScissor in RenderPrimitiveSceneAndPresent.
            var dynamicStates = stackalloc DynamicState[2] { DynamicState.Viewport, DynamicState.Scissor };
            var dynamicState = new PipelineDynamicStateCreateInfo
            {
                SType = StructureType.PipelineDynamicStateCreateInfo,
                DynamicStateCount = 2,
                PDynamicStates = dynamicStates,
            };
            var viewportState = new PipelineViewportStateCreateInfo
            {
                SType = StructureType.PipelineViewportStateCreateInfo,
                ViewportCount = 1,
                ScissorCount = 1,
            };

            var rasterizer = new PipelineRasterizationStateCreateInfo
            {
                SType = StructureType.PipelineRasterizationStateCreateInfo,
                PolygonMode = PolygonMode.Fill,
                // NOT back-face culled, unlike the shadow passes (see their own note). Measured on
                // the four-AI Level4 round it would be worth another ~1.6 ms of GPU (132 -> 154 fps),
                // but it is not free of visual change here: a camera INSIDE a solid sees that solid's
                // back faces, and Triage has one (ElevatorCabinCamera is mounted inside the cabin and
                // looks across it), while a coverage-dithered cutaway wall currently shows its own
                // inner face through the dither rather than the room behind. Both are decisions for a
                // human looking at the frames, not a side effect of a performance pass.
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
            // THE PREPASS writes depth; THE COLOUR PASS only tests against it, with LESS_OR_EQUAL so
            // the very fragment that wrote a depth value still passes. Depth writes are off in the
            // colour pass on purpose: a fragment shader containing `discard` forces the hardware to
            // defer the depth test until after shading whenever that shader could also write depth,
            // which is exactly what made this pass shade every hidden fragment. See
            // ShaderSource.PrimitiveDepthFrag for the measurement.
            var depthPrepassStencil = new PipelineDepthStencilStateCreateInfo
            {
                SType = StructureType.PipelineDepthStencilStateCreateInfo,
                DepthTestEnable = true,
                DepthWriteEnable = true,
                DepthCompareOp = CompareOp.Less,
                DepthBoundsTestEnable = false,
                StencilTestEnable = false,
            };
            var depthStencil = new PipelineDepthStencilStateCreateInfo
            {
                SType = StructureType.PipelineDepthStencilStateCreateInfo,
                DepthTestEnable = true,
                DepthWriteEnable = false,
                DepthCompareOp = CompareOp.LessOrEqual,
                DepthBoundsTestEnable = false,
                StencilTestEnable = false,
            };
            // MaterialKind.XRayOutlineShell: no depth test at all, so the silhouette paints over the
            // wall in front of it, and no depth write, so it cannot then hide anything drawn later.
            //
            // 2026-09-17: a depth test of Greater was tried here to make the cue paint ONLY where it is
            // hidden (user request: 「隠れている場所の、輪郭のみ」) and REVERTED, because it does not do
            // that. The shell's fragment stage keeps the hull's FAR side (see PrimitiveFrag's
            // vShellFacesAway discard), and a far side is behind its own subject as well as behind a
            // wall -- so Greater passes over the visible half of a partly-hidden figure too. Pixel
            // evidence either way is in XRayOccludedOnlyRenderTests, which is why the attempt did not
            // ship on a guess. Getting "hidden only" needs the shell to keep its NEAR side for this
            // material, i.e. a shader branch on a material code of its own, plus a shader regeneration.
            var xrayDepthStencil = new PipelineDepthStencilStateCreateInfo
            {
                SType = StructureType.PipelineDepthStencilStateCreateInfo,
                DepthTestEnable = false,
                DepthWriteEnable = false,
                DepthCompareOp = CompareOp.Always,
                DepthBoundsTestEnable = false,
                StencilTestEnable = false,
            };
            var blendAttachment = new PipelineColorBlendAttachmentState
            {
                ColorWriteMask = ColorComponentFlags.RBit | ColorComponentFlags.GBit | ColorComponentFlags.BBit | ColorComponentFlags.ABit,
                BlendEnable = false,
            };
            // MaterialKind.AdditiveGlow: the one blending pipeline on this otherwise fully opaque
            // path. SrcAlpha/One ADDS the fragment's colour weighted by its own alpha, which is what
            // makes overlapping glows read as brighter rather than as the nearest one winning -- and,
            // being commutative, is what lets them keep the packer's (kind, material) batch order
            // instead of needing a back-to-front sort. Alpha is left at One/Zero: nothing samples the
            // destination alpha here, and writing SrcAlpha into it would matter only to a later pass
            // that read it.
            var glowBlendAttachment = new PipelineColorBlendAttachmentState
            {
                ColorWriteMask = ColorComponentFlags.RBit | ColorComponentFlags.GBit | ColorComponentFlags.BBit | ColorComponentFlags.ABit,
                BlendEnable = true,
                SrcColorBlendFactor = BlendFactor.SrcAlpha,
                DstColorBlendFactor = BlendFactor.One,
                ColorBlendOp = BlendOp.Add,
                SrcAlphaBlendFactor = BlendFactor.One,
                DstAlphaBlendFactor = BlendFactor.Zero,
                AlphaBlendOp = BlendOp.Add,
            };
            var glowColorBlend = new PipelineColorBlendStateCreateInfo
            {
                SType = StructureType.PipelineColorBlendStateCreateInfo,
                AttachmentCount = 1,
                PAttachments = &glowBlendAttachment,
            };
            // Depth TESTED so anything solid in front still hides it, depth WRITE off so it hides
            // nothing itself -- including another glow, which is what lets two overlap and add.
            var glowDepthStencil = new PipelineDepthStencilStateCreateInfo
            {
                SType = StructureType.PipelineDepthStencilStateCreateInfo,
                DepthTestEnable = true,
                DepthWriteEnable = false,
                DepthCompareOp = CompareOp.LessOrEqual,
                DepthBoundsTestEnable = false,
                StencilTestEnable = false,
            };

            // Depth only: the prepass must not touch a single colour byte.
            var depthPrepassBlendAttachment = new PipelineColorBlendAttachmentState
            {
                ColorWriteMask = 0,
                BlendEnable = false,
            };
            var depthPrepassColorBlend = new PipelineColorBlendStateCreateInfo
            {
                SType = StructureType.PipelineColorBlendStateCreateInfo,
                AttachmentCount = 1,
                PAttachments = &depthPrepassBlendAttachment,
            };
            var colorBlend = new PipelineColorBlendStateCreateInfo
            {
                SType = StructureType.PipelineColorBlendStateCreateInfo,
                AttachmentCount = 1,
                PAttachments = &blendAttachment,
            };

            // No push-constant range: every value that used to be a per-draw push constant is
            // now per-instance vertex data (TASK-152) — this frees the whole 112-byte block
            // TASK-036 recorded as already-tight for TASK-149's UBO layout break, if it turns
            // out to need it (TASK-149 primarily needs a light-array UBO change, not more
            // push-constant room, but the room is now available either way).
            var setLayout = _lightingSetLayout;
            var layoutInfo = new PipelineLayoutCreateInfo
            {
                SType = StructureType.PipelineLayoutCreateInfo,
                SetLayoutCount = 1,
                PSetLayouts = &setLayout,
            };
            Check(_vk.CreatePipelineLayout(_device, in layoutInfo, null, out _primitivePipelineLayout), "CreatePipelineLayout(primitive)");

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
                Layout = _primitivePipelineLayout,
                RenderPass = _renderPass,
                Subpass = 0,
            };
            Check(_vk.CreateGraphicsPipelines(_device, default, 1, in pipelineInfo, null, out _primitivePipeline), "CreateGraphicsPipelines(primitive)");

            // Same everything except the fragment stage, the depth state and the colour mask — built
            // from the same locals so the two pipelines cannot drift in vertex input or raster state,
            // which is what makes the prepass's depth exactly the depth the colour pass tests against.
            stages[1].Module = _primitiveDepthFrag;
            pipelineInfo.PDepthStencilState = &depthPrepassStencil;
            pipelineInfo.PColorBlendState = &depthPrepassColorBlend;
            Check(_vk.CreateGraphicsPipelines(_device, default, 1, in pipelineInfo, null, out _primitiveDepthPipeline), "CreateGraphicsPipelines(primitive depth prepass)");

            // The x-ray shell pass: the COLOUR fragment stage again (a shell is shaded, flatly, by
            // PrimitiveFrag) with the depth state swapped out -- so it draws the same pixels the
            // colour pass would have, had nothing been standing in front of the shell.
            stages[1].Module = _primitiveFrag;
            pipelineInfo.PDepthStencilState = &xrayDepthStencil;
            pipelineInfo.PColorBlendState = &colorBlend;
            Check(_vk.CreateGraphicsPipelines(_device, default, 1, in pipelineInfo, null, out _primitiveXRayPipeline), "CreateGraphicsPipelines(primitive x-ray shell)");

            // The glow pass: the same colour fragment stage again, with blending on and depth writes
            // off. Built from the same locals as the other three so it cannot drift in vertex input or
            // raster state.
            pipelineInfo.PDepthStencilState = &glowDepthStencil;
            pipelineInfo.PColorBlendState = &glowColorBlend;
            Check(_vk.CreateGraphicsPipelines(_device, default, 1, in pipelineInfo, null, out _primitiveGlowPipeline), "CreateGraphicsPipelines(primitive additive glow)");
        }
        finally
        {
            SilkMarshal.Free((nint)entry);
        }

        _primitivePipelineReady = true;
    }

    private void EnsurePrimitiveInstanceCapacity(int instances)
    {
        if (instances <= _primitiveInstanceCapacity)
        {
            return;
        }

        if (_primitiveInstanceBuffer.Handle != 0)
        {
            _vk.DestroyBuffer(_device, _primitiveInstanceBuffer, null);
            _vk.FreeMemory(_device, _primitiveInstanceMemory, null);
        }

        _primitiveInstanceCapacity = Math.Max(instances, PrimitiveInstancePacker.InstanceMinimumCapacity);
        var size = (ulong)(_primitiveInstanceCapacity * PrimitiveInstancePacker.FloatsPerInstance * sizeof(float));
        var info = new BufferCreateInfo
        {
            SType = StructureType.BufferCreateInfo,
            Size = size,
            Usage = BufferUsageFlags.VertexBufferBit,
            SharingMode = SharingMode.Exclusive,
        };
        Check(_vk.CreateBuffer(_device, in info, null, out _primitiveInstanceBuffer), "CreateBuffer(primitiveInstances)");

        _vk.GetBufferMemoryRequirements(_device, _primitiveInstanceBuffer, out var requirements);
        _primitiveInstanceMemory = AllocateMemory(requirements, MemoryPropertyFlags.HostVisibleBit | MemoryPropertyFlags.HostCoherentBit);
        Check(_vk.BindBufferMemory(_device, _primitiveInstanceBuffer, _primitiveInstanceMemory, 0), "BindBufferMemory(primitiveInstances)");
    }

    /// <summary>Creates the descriptor sets and mapped uniform buffers that
    /// <see cref="ShaderSource.BoxFrag"/>'s <c>Lighting</c> binding reads from. One immutable
    /// slot is reserved per supported presentation view: the frame fence is waited before all
    /// slots are rewritten, and a multi-view command binds the matching slot for each viewport.
    /// Single-view rendering keeps using slot zero.</summary>
    /// <summary>TASK-148 added binding 1 (a combined image sampler over the shadow depth map)
    /// — see <see cref="HeadlessRenderer.EnsureLightingDescriptor"/>, kept byte-for-byte in step
    /// with this one.</summary>
    private void EnsureLightingDescriptor()
    {
        if (_lightingSetLayout.Handle != 0)
        {
            return;
        }

        EnsureShadowResources();

        EnsureIrradianceTextures();
        EnsureSurfaceTextures();

        var bindings = stackalloc DescriptorSetLayoutBinding[HeadlessRenderer.LightingBindingCount];
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
            bindings[i] = new DescriptorSetLayoutBinding { Binding = i, DescriptorType = DescriptorType.CombinedImageSampler, DescriptorCount = 1, StageFlags = ShaderStageFlags.FragmentBit };
        }
        bindings[6] = new DescriptorSetLayoutBinding { Binding = 6, DescriptorType = DescriptorType.UniformBuffer, DescriptorCount = 1, StageFlags = ShaderStageFlags.VertexBit };

        // TASK-245: bindings 7/8/9, the irradiance volume's three 3D textures. Kept in step with
        // HeadlessRenderer.EnsureLightingDescriptor by sharing its binding constants outright - a
        // set layout that disagrees with the other renderer's is a shader that reads a different
        // descriptor in a window than in a screenshot, which is hard rule 6 broken silently.
        for (var i = HeadlessRenderer.IrradianceFirstBinding; i < HeadlessRenderer.LightingBindingCount; i++)
        {
            bindings[i] = new DescriptorSetLayoutBinding { Binding = (uint)i, DescriptorType = DescriptorType.CombinedImageSampler, DescriptorCount = 1, StageFlags = ShaderStageFlags.FragmentBit };
        }

        var setLayoutInfo = new DescriptorSetLayoutCreateInfo
        {
            SType = StructureType.DescriptorSetLayoutCreateInfo,
            BindingCount = (uint)HeadlessRenderer.LightingBindingCount,
            PBindings = bindings,
        };
        Check(_vk.CreateDescriptorSetLayout(_device, in setLayoutInfo, null, out _lightingSetLayout), "CreateDescriptorSetLayout(lighting)");

        var poolSizes = stackalloc DescriptorPoolSize[2];
        poolSizes[0] = new DescriptorPoolSize { Type = DescriptorType.UniformBuffer, DescriptorCount = MaxPresentationViews * 2 };
        poolSizes[1] = new DescriptorPoolSize { Type = DescriptorType.CombinedImageSampler, DescriptorCount = MaxPresentationViews * HeadlessRenderer.LightingSamplerCount };
        var poolInfo = new DescriptorPoolCreateInfo
        {
            SType = StructureType.DescriptorPoolCreateInfo,
            MaxSets = MaxPresentationViews,
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
        for (var i = 0; i < MaxPresentationViews; i++)
        {
            Check(_vk.AllocateDescriptorSets(_device, in allocInfo, out _multiViewLightingSets[i]), "AllocateDescriptorSets(lighting)");
            CreateMultiViewLightingBuffer(i);
        }

        // The single-view path remains a direct alias to slot zero. Keeping its original fields
        // avoids a second code path while the multi-view pass selects one immutable UBO per view.
        _lightingSet = _multiViewLightingSets[0];
        _lightingBuffer = _multiViewLightingBuffers[0];
        _lightingMemory = _multiViewLightingMemories[0];
        _lightingMapped = (void*)_multiViewLightingMapped[0];

        var fixtureInfo = new BufferCreateInfo { SType = StructureType.BufferCreateInfo, Size = (ulong)sizeof(FixtureShadowUniform), Usage = BufferUsageFlags.UniformBufferBit, SharingMode = SharingMode.Exclusive };
        Check(_vk.CreateBuffer(_device, in fixtureInfo, null, out _fixtureShadowBuffer), "CreateBuffer(fixtureShadows)");
        _vk.GetBufferMemoryRequirements(_device, _fixtureShadowBuffer, out var fixtureRequirements);
        _fixtureShadowMemory = AllocateMemory(fixtureRequirements, MemoryPropertyFlags.HostVisibleBit | MemoryPropertyFlags.HostCoherentBit);
        Check(_vk.BindBufferMemory(_device, _fixtureShadowBuffer, _fixtureShadowMemory, 0), "BindBufferMemory(fixtureShadows)");
        void* fixtureMapped = null;
        Check(_vk.MapMemory(_device, _fixtureShadowMemory, 0, (ulong)sizeof(FixtureShadowUniform), 0, ref fixtureMapped), "MapMemory(fixtureShadows)");
        _fixtureShadowMapped = fixtureMapped;
        for (var i = 0; i < MaxPresentationViews; i++)
        {
            WriteLightingDescriptor(_multiViewLightingSets[i], _multiViewLightingBuffers[i]);
        }
    }

    private void CreateMultiViewLightingBuffer(int index)
    {
        var bufferInfo = new BufferCreateInfo
        {
            SType = StructureType.BufferCreateInfo,
            Size = (ulong)sizeof(LightingUniform),
            Usage = BufferUsageFlags.UniformBufferBit,
            SharingMode = SharingMode.Exclusive,
        };
        Check(_vk.CreateBuffer(_device, in bufferInfo, null, out _multiViewLightingBuffers[index]), "CreateBuffer(lighting)");
        _vk.GetBufferMemoryRequirements(_device, _multiViewLightingBuffers[index], out var requirements);
        _multiViewLightingMemories[index] = AllocateMemory(requirements, MemoryPropertyFlags.HostVisibleBit | MemoryPropertyFlags.HostCoherentBit);
        Check(_vk.BindBufferMemory(_device, _multiViewLightingBuffers[index], _multiViewLightingMemories[index], 0), "BindBufferMemory(lighting)");
        void* mapped = null;
        Check(_vk.MapMemory(_device, _multiViewLightingMemories[index], 0, (ulong)sizeof(LightingUniform), 0, ref mapped), "MapMemory(lighting)");
        _multiViewLightingMapped[index] = (nint)mapped;
    }

    private void WriteLightingDescriptor(DescriptorSet lightingSet, Silk.NET.Vulkan.Buffer lightingBuffer)
    {
        var descriptorBufferInfo = new DescriptorBufferInfo { Buffer = lightingBuffer, Offset = 0, Range = (ulong)sizeof(LightingUniform) };
        var imageInfos = stackalloc DescriptorImageInfo[5];
        imageInfos[0] = new DescriptorImageInfo { Sampler = _shadowSampler, ImageView = _shadowDepthView, ImageLayout = ImageLayout.ShaderReadOnlyOptimal };
        for (var i = 0; i < SceneLighting.MaxDownwardFixtureShadows; i++)
        {
            imageInfos[i + 1] = new DescriptorImageInfo { Sampler = _shadowSampler, ImageView = _fixtureShadowDepthViews[i], ImageLayout = ImageLayout.ShaderReadOnlyOptimal };
        }

        var fixtureDescriptor = new DescriptorBufferInfo { Buffer = _fixtureShadowBuffer, Offset = 0, Range = (ulong)sizeof(FixtureShadowUniform) };
        var writes = stackalloc WriteDescriptorSet[HeadlessRenderer.LightingBindingCount];
        writes[0] = new WriteDescriptorSet
        {
            SType = StructureType.WriteDescriptorSet,
            DstSet = lightingSet,
            DstBinding = 0,
            DescriptorCount = 1,
            DescriptorType = DescriptorType.UniformBuffer,
            PBufferInfo = &descriptorBufferInfo,
        };
        writes[1] = new WriteDescriptorSet
        {
            SType = StructureType.WriteDescriptorSet,
            DstSet = lightingSet,
            DstBinding = 1,
            DescriptorCount = 1,
            DescriptorType = DescriptorType.CombinedImageSampler,
            PImageInfo = imageInfos,
        };
        for (uint i = 2; i <= 5; i++)
        {
            writes[i] = new WriteDescriptorSet { SType = StructureType.WriteDescriptorSet, DstSet = lightingSet, DstBinding = i, DescriptorCount = 1, DescriptorType = DescriptorType.CombinedImageSampler, PImageInfo = imageInfos + (i - 1) };
        }

        writes[6] = new WriteDescriptorSet { SType = StructureType.WriteDescriptorSet, DstSet = lightingSet, DstBinding = 6, DescriptorCount = 1, DescriptorType = DescriptorType.UniformBuffer, PBufferInfo = &fixtureDescriptor };

        var irradianceInfos = stackalloc DescriptorImageInfo[HeadlessRenderer.IrradianceBindingCount];
        for (var i = 0; i < HeadlessRenderer.IrradianceBindingCount; i++)
        {
            irradianceInfos[i] = new DescriptorImageInfo { Sampler = _irradiance!.Sampler, ImageView = _irradiance.View(i), ImageLayout = ImageLayout.ShaderReadOnlyOptimal };
            writes[HeadlessRenderer.IrradianceFirstBinding + i] = new WriteDescriptorSet { SType = StructureType.WriteDescriptorSet, DstSet = lightingSet, DstBinding = (uint)(HeadlessRenderer.IrradianceFirstBinding + i), DescriptorCount = 1, DescriptorType = DescriptorType.CombinedImageSampler, PImageInfo = irradianceInfos + i };
        }

        var surfaceInfo = SurfaceTextureInfo();
        writes[HeadlessRenderer.SurfaceTextureBinding] = SurfaceTextureWrite(lightingSet, &surfaceInfo);
        _vk.UpdateDescriptorSets(_device, (uint)HeadlessRenderer.LightingBindingCount, writes, 0, null);
    }

    /// <summary>Writes <paramref name="lighting"/> (and TASK-148's shadow toggle) into the
    /// mapped uniform buffer; the memory is host-coherent so no explicit flush is needed before
    /// the next submit reads it.</summary>
    /// <param name="lighting">The full scene lighting state to write.</param>
    /// <param name="shadowsEnabled">TASK-148: whether the shadow depth pass ran this frame.</param>
    /// <param name="cameraPosition">TASK-151: see <c>HeadlessRenderer.UpdateLighting</c>'s
    /// matching parameter.</param>
    /// <param name="lightingSetIndex">Renderer-owned per-view uniform slot. Zero is the
    /// single-view slot; multi-view presentation assigns one immutable slot per viewport.</param>
    /// <param name="fixtureShadowCount">Number of leading point lights with a dedicated downward-fixture shadow map.</param>
    private void UpdateLighting(SceneLighting lighting, bool shadowsEnabled, Vector3? cameraPosition = null, int lightingSetIndex = 0, int fixtureShadowCount = 0)
    {
        if ((uint)lightingSetIndex >= MaxPresentationViews)
        {
            throw new ArgumentOutOfRangeException(nameof(lightingSetIndex));
        }

        lighting.Validate();
        var uniform = new LightingUniform
        {
            SunDirectionIntensity = new Vector4(lighting.Sun.Direction, lighting.Sun.Intensity),
            SunColor = new Vector4(lighting.Sun.Color, 0f),
            AmbientColorIntensity = new Vector4(lighting.Ambient.Color, lighting.Ambient.Intensity),
            ShadowParams = new Vector4(
                shadowsEnabled ? 1f : 0f,
                ShadowMapConfig.ComparisonBias,
                1f / ShadowMapConfig.MapSize,
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

        // TASK-245: describes the RESIDENT volume, not the requested one - see
        // HeadlessRenderer.UpdateLighting's own comment for why the distinction matters.
        var (irradianceOrigin, irradianceScale, irradianceBias) =
            IrradianceVolumeTextures.Uniforms(_irradiance?.Resident is { } resident && ReferenceEquals(resident, lighting.Irradiance) ? resident : null);
        uniform.IrradianceOriginEnabled = irradianceOrigin;
        uniform.IrradianceScale = irradianceScale;
        uniform.IrradianceBias = irradianceBias;
        uniform.RenderStyle = new Vector4((float)lighting.Style, 0f, 0f, 0f);
        WritePointLights(ref uniform, lighting.PointLights);
        WriteSpotLights(ref uniform, lighting.SpotLights);
        *(LightingUniform*)_multiViewLightingMapped[lightingSetIndex] = uniform;
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

    private static int BuildFixtureShadowViewProjections(SceneLighting lighting, ShadowMapConfig? shadow, Span<Matrix4x4> destination)
    {
        if (!shadow.HasValue || !shadow.Value.EnableDownwardFixtureShadows)
        {
            return 0;
        }

        var count = Math.Min(Math.Min(lighting.PointLights.Length, destination.Length), SceneLighting.MaxDownwardFixtureShadows);
        for (var i = 0; i < count; i++)
        {
            var light = lighting.PointLights[i];
            destination[i] = Camera.DownwardFixtureShadowViewProjection(light.Position, light.Range, shadow.Value.AabbMin.Y, shadow.Value.AabbMax.Y);
        }
        return count;
    }

    /// <summary>TASK-168: byte-for-byte identical logic to <c>HeadlessRenderer.WriteSpotLights</c> —
    /// see that method's doc comment.</summary>
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

    /// <summary>Byte-for-byte identical logic to <c>HeadlessRenderer.WritePointLights</c> — see
    /// that method's doc comment.</summary>
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

    private void CreateMeshBuffer(float[] vertices, out Silk.NET.Vulkan.Buffer buffer, out DeviceMemory memory, out uint count, string label)
    {
        count = (uint)(vertices.Length / 6);
        var size = (ulong)(vertices.Length * sizeof(float));
        var info = new BufferCreateInfo
        {
            SType = StructureType.BufferCreateInfo,
            Size = size,
            Usage = BufferUsageFlags.VertexBufferBit,
            SharingMode = SharingMode.Exclusive,
        };
        Check(_vk.CreateBuffer(_device, in info, null, out buffer), $"CreateBuffer({label})");
        _vk.GetBufferMemoryRequirements(_device, buffer, out var requirements);
        memory = AllocateMemory(requirements, MemoryPropertyFlags.HostVisibleBit | MemoryPropertyFlags.HostCoherentBit);
        Check(_vk.BindBufferMemory(_device, buffer, memory, 0), $"BindBufferMemory({label})");
        void* mapped = null;
        Check(_vk.MapMemory(_device, memory, 0, size, 0, ref mapped), $"MapMemory({label})");
        vertices.AsSpan().CopyTo(new Span<float>(mapped, vertices.Length));
        _vk.UnmapMemory(_device, memory);
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

    private void DisposePrimitiveResources()
    {
        if (_primitiveGlowPipeline.Handle != 0)
        {
            _vk.DestroyPipeline(_device, _primitiveGlowPipeline, null);
            _primitiveGlowPipeline = default;
        }

        if (_primitiveXRayPipeline.Handle != 0)
        {
            _vk.DestroyPipeline(_device, _primitiveXRayPipeline, null);
            _primitiveXRayPipeline = default;
        }

        if (_primitiveDepthPipeline.Handle != 0)
        {
            _vk.DestroyPipeline(_device, _primitiveDepthPipeline, null);
            _primitiveDepthPipeline = default;
        }

        if (_primitivePipeline.Handle != 0)
        {
            _vk.DestroyPipeline(_device, _primitivePipeline, null);
        }

        if (_primitivePipelineLayout.Handle != 0)
        {
            _vk.DestroyPipelineLayout(_device, _primitivePipelineLayout, null);
        }

        if (_primitiveVert.Handle != 0)
        {
            _vk.DestroyShaderModule(_device, _primitiveVert, null);
        }

        if (_primitiveFrag.Handle != 0)
        {
            _vk.DestroyShaderModule(_device, _primitiveFrag, null);
        }

        if (_primitiveDepthFrag.Handle != 0)
        {
            _vk.DestroyShaderModule(_device, _primitiveDepthFrag, null);
        }

        DestroyMeshBuffer(_cubeVertexBuffer, _cubeVertexMemory);
        DestroyMeshBuffer(_sphereVertexBuffer, _sphereVertexMemory);
        DestroyMeshBuffer(_capsuleVertexBuffer, _capsuleVertexMemory);
        DestroyMeshBuffer(_cylinderVertexBuffer, _cylinderVertexMemory);

        if (_primitiveInstanceBuffer.Handle != 0)
        {
            _vk.DestroyBuffer(_device, _primitiveInstanceBuffer, null);
        }

        if (_primitiveInstanceMemory.Handle != 0)
        {
            _vk.FreeMemory(_device, _primitiveInstanceMemory, null);
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

        for (var i = 1; i < MaxPresentationViews; i++)
        {
            if (_multiViewLightingBuffers[i].Handle != 0)
            {
                _vk.DestroyBuffer(_device, _multiViewLightingBuffers[i], null);
            }

            if (_multiViewLightingMemories[i].Handle != 0)
            {
                _vk.UnmapMemory(_device, _multiViewLightingMemories[i]);
                _vk.FreeMemory(_device, _multiViewLightingMemories[i], null);
            }
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

    private void DestroyMeshBuffer(Silk.NET.Vulkan.Buffer buffer, DeviceMemory memory)
    {
        if (buffer.Handle != 0)
        {
            _vk.DestroyBuffer(_device, buffer, null);
        }

        if (memory.Handle != 0)
        {
            _vk.FreeMemory(_device, memory, null);
        }
    }
}
