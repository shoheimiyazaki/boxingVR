using System.Numerics;
using Silk.NET.Core.Native;
using Silk.NET.Vulkan;

namespace Aigis.Render;

public sealed unsafe partial class HeadlessRenderer
{
    private readonly PrimitiveInstancePacker _primitivePacker = new();

    /// <summary>The <see cref="ForegroundLayer"/>'s instances, packed with the layer's own camera and
    /// uploaded directly after <see cref="_primitivePacker"/>'s in the shared instance buffer.</summary>
    private readonly PrimitiveInstancePacker _foregroundPacker = new();
    private bool _primitiveInstancedReady;
    private Pipeline _primitiveInstancedPipeline;
    private PipelineLayout _primitiveInstancedPipelineLayout;
    private ShaderModule _primitiveInstancedVert;
    private ShaderModule _primitiveFrag;

    /// <summary>The depth-prepass fragment stage — see <see cref="ShaderSource.PrimitiveDepthFrag"/>.
    /// Present here as well as in <see cref="WindowedRenderer"/> because the two renderers must draw
    /// the same frame (hard rule 6): an agent verifies from these pixels what a human sees in the
    /// window, so a pass that exists on only one of them makes that promise untrue.</summary>
    private ShaderModule _primitiveDepthFrag;

    /// <summary>The depth-prepass twin of <see cref="_primitiveInstancedPipeline"/>.</summary>
    private Pipeline _primitiveDepthPipeline;

    /// <summary>The through-the-wall twin of <see cref="_primitiveInstancedPipeline"/>: identical in
    /// every respect except that its depth test and depth write are both off, so
    /// <see cref="MaterialKind.XRayOutlineShell"/> draws over whatever the colour pass already put
    /// there. Present in both renderers for the same reason
    /// <see cref="_primitiveDepthFrag"/> is (hard rule 6).</summary>
    private Pipeline _primitiveXRayPipeline;

    /// <summary>The BLENDING twin of the instanced primitive pipeline: same stages, same raster state,
    /// additive colour blend and no depth write. Draws
    /// <see cref="PrimitiveInstancePacker.GlowBatches"/> after the colour pass. Mirrors
    /// <c>WindowedRenderer._primitiveGlowPipeline</c> — both renderers must produce the same picture
    /// (hard rule 6).</summary>
    private Pipeline _primitiveGlowPipeline;
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

    /// <param name="clearColor">RGBA clear color (0..1).</param>
    /// <param name="viewProjection">Camera view-projection for <paramref name="instances"/>/<paramref name="lines"/>.</param>
    /// <param name="instances">Cube/sphere/capsule instances to draw.</param>
    /// <param name="lines">Debug line overlay, drawn after <paramref name="instances"/>.</param>
    /// <param name="text">Screen-space HUD text (TASK-041/TASK-051) drawn last, after
    /// <paramref name="lines"/>, with depth test/write disabled — resolution- and
    /// camera-independent (see <see cref="ScreenText"/>/<c>HeadlessRenderer.Overlay.cs</c>).
    /// Optional and defaults to none so existing callers are unaffected.</param>
    /// <param name="particles">Camera-facing billboards (dust, blood, fire — see
    /// <see cref="ParticleSystem"/>), blended over <paramref name="instances"/> but UNDER
    /// <paramref name="lines"/>: debug gizmos have to stay readable through an effect, which is
    /// the whole reason to draw them. Last in the parameter list, not in draw order, so the
    /// existing positional callers that pass <paramref name="text"/> keep compiling.</param>
    /// <param name="lighting">Directional + ambient light (TASK-036). Defaults to
    /// <see cref="SceneLighting.Default"/>, which reproduces the old hardcoded shader
    /// bit-for-bit — existing callers/goldens are unaffected by leaving this unset.</param>
    /// <param name="shadow">TASK-148: optional sun shadow-map configuration. <see langword="null"/>
    /// (the default) skips the shadow depth pass entirely and renders bit-for-bit the same image
    /// as before this task — existing callers/goldens are unaffected by this parameter existing.</param>
    /// <param name="cameraPosition">TASK-151: the camera eye, world-space — only needed when
    /// <paramref name="lighting"/>'s <see cref="SceneLighting.Specular"/> is enabled (see
    /// <c>Aigis.Player.IGameCamera.Eye</c> for where a caller building a real camera already
    /// has this value). <see langword="null"/> (the default) is fine for every caller that leaves
    /// <see cref="SceneLighting.Specular"/> at <see cref="SpecularConfig.None"/> (every
    /// pre-TASK-151 caller) — existing goldens are unaffected. Fail-loud, not silent-degrade
    /// (arch-reviewer TASK-151 finding): supplying <see cref="SceneLighting.Specular"/> with
    /// <see cref="SpecularConfig.Intensity"/> &gt; 0 without a <paramref name="cameraPosition"/>
    /// throws instead of silently rendering with specular contributing nothing.</param>
    /// <param name="images">TASK-159: textured screen-space overlays (HUD icons, portraits), drawn
    /// last of all — after the text overlay, so an icon sits on top. Each
    /// <see cref="OverlayImageDraw"/>'s <see cref="OverlayImageDraw.Texture"/> is uploaded to the
    /// GPU on first sight and cached by instance identity, so pass the SAME
    /// <see cref="TextureData"/> instances every frame (see that type's doc comment).
    /// <see langword="null"/>/empty (the default) skips the pipeline entirely, so no existing caller
    /// or golden is affected.</param>
    /// <param name="meshes">2026-08-30: imported triangle meshes (<see cref="MeshData"/>) to draw in
    /// the same frame as the primitives — see <c>DrawMeshes</c>, and note that they are lit by the sun
    /// and ambient only.</param>
    /// <param name="decals">World-space ground stain sheets (blood trails — see
    /// <see cref="DecalQuadDraw"/>), drawn immediately after the opaque scene and BEFORE
    /// <paramref name="particles"/>/<paramref name="lines"/>: a decal is floor, so anything floating
    /// above the floor blends over it. Each <see cref="DecalQuadDraw.Texture"/> is uploaded on first
    /// sight and re-uploaded only when its <see cref="DynamicTexture.Revision"/> moves, so pass the
    /// SAME <see cref="DynamicTexture"/> instances every frame (see that type's doc comment).
    /// <see langword="null"/>/empty (the default) skips the pipeline entirely, so no existing caller
    /// or golden is affected.</param>
    /// <param name="shapes">Screen-space UI surfaces (cards, panels, gauges, dividers, shadows) —
    /// see <see cref="UiShapeDraw"/>. Drawn after the 3D scene and lines but BEFORE
    /// <paramref name="images"/> and <paramref name="text"/>, so a panel is a background for its own
    /// icons and labels. <see langword="null"/>/empty (the default) skips the pipeline entirely, so
    /// no existing caller or golden is affected.</param>
    /// <param name="foreground">3D primitives drawn over <paramref name="shapes"/> and under
    /// <paramref name="images"/>/<paramref name="text"/> with their own camera — see
    /// <see cref="ForegroundLayer"/>. <see langword="null"/> (the default) records nothing extra,
    /// so every existing frame is byte-identical.</param>
    /// <param name="postProcess">The miniature finishing pass (tilt-shift, bloom, grade, vignette) —
    /// see <see cref="PostProcessSettings"/>. Applied to the 3D scene, particles, decals and lines,
    /// never to the HUD drawn after it. <see langword="null"/> (the default) draws exactly as before.</param>
    /// <remarks>
    /// TASK-152: <paramref name="instances"/> are grouped by <see cref="PrimitiveKind"/>
    /// (<see cref="PrimitiveInstancePacker"/>) into one instance-rate vertex buffer, so this
    /// issues at most one <c>vkCmdDraw</c> per kind present — not one per instance — regardless
    /// of how many cubes/spheres/capsules the scene contains.
    /// </remarks>
    /// <exception cref="ArgumentException"><paramref name="lighting"/> requests specular
    /// (<see cref="SceneLighting.Specular"/>.<see cref="SpecularConfig.Intensity"/> &gt; 0) but
    /// <paramref name="cameraPosition"/> is <see langword="null"/>.</exception>
    /// <param name="voxels">Voxel volumes (<see cref="Voxels.VoxelVolumeDraw"/>): each one's surface is built on the GPU from its
    /// 3D texture — a face wherever a filled cell meets an empty one — and drawn with the opaque scene.</param>
    public byte[] RenderPrimitiveScene(
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
        IReadOnlyList<MeshInstance>? meshes = null,
        ForegroundLayer? foreground = null,
        PostProcessSettings? postProcess = null,
        IReadOnlyList<Voxels.VoxelVolumeDraw>? voxels = null) =>
        RenderPrimitiveSceneForView(
            clearColor, viewProjection, instances, lines, text, particles, lighting, shadow, cameraPosition,
            images, shapes, decals, meshes, foreground, postProcess, postProcessSampleRect: null, voxels);

    /// <summary><see cref="RenderPrimitiveScene"/>
    /// with the finishing pass's taps confined to <paramref name="postProcessSampleRect"/> (whole-target
    /// uv, top-left origin) — the split-screen view's rectangle, for <see cref="MultiViewHeadlessRenderer"/>.
    /// <see langword="null"/> samples the whole target.</summary>
    internal byte[] RenderPrimitiveSceneForView(
        Vector4 clearColor,
        Matrix4x4 viewProjection,
        IReadOnlyList<PrimitiveInstance> instances,
        IReadOnlyList<DebugLine> lines,
        IReadOnlyList<ScreenTextDraw>? text,
        IReadOnlyList<ParticleInstance>? particles,
        SceneLighting? lighting,
        ShadowMapConfig? shadow,
        Vector3? cameraPosition,
        IReadOnlyList<OverlayImageDraw>? images,
        IReadOnlyList<UiShapeDraw>? shapes,
        IReadOnlyList<DecalQuadDraw>? decals,
        IReadOnlyList<MeshInstance>? meshes,
        ForegroundLayer? foreground,
        PostProcessSettings? postProcess,
        Vector4? postProcessSampleRect,
        IReadOnlyList<Voxels.VoxelVolumeDraw>? voxels = null)
    {
        // Before any command is recorded: a bad value must fail the call, not leave a frame half-begun.
        postProcess?.Validate();
        EnsureBoxPipeline(); // shared cube mesh, box frag shader module, lighting descriptor set.
        EnsurePrimitiveMeshes();
        EnsurePrimitiveInstancedPipeline();
        EnsureShadowPipeline();
        EnsureLinePipeline();
        UploadLines(lines);
        UploadParticles(particles ?? [], viewProjection);

        var effectiveLighting = lighting ?? SceneLighting.Default;

        // 2026-09-19: the eye is RECOVERED from the view-projection when a caller does not name one
        // — see Camera.TryGetEye for the method, and for the measurement that made requiring it the
        // wrong answer. An explicit cameraPosition still wins, so no existing caller's frame moves.
        var eye = cameraPosition ?? (Camera.TryGetEye(viewProjection, out var derivedEye) ? derivedEye : null);
        if (effectiveLighting.Specular.Intensity > 0f && eye is null)
        {
            throw new ArgumentException(
                $"{nameof(lighting)}.{nameof(SceneLighting.Specular)}.{nameof(SpecularConfig.Intensity)} is {effectiveLighting.Specular.Intensity}, but {nameof(cameraPosition)} was not supplied and this frame's projection is ORTHOGRAPHIC, which has no eye point to derive — specular needs a view direction. Pass the camera's Eye (see IGameCamera.Eye) or set {nameof(SceneLighting.Specular)} to {nameof(SpecularConfig)}.{nameof(SpecularConfig.None)}.",
                nameof(cameraPosition));
        }

        // TASK-148: Camera.DirectionalShadowViewProjection is the ONE place a light-space
        // projection is built, sharing ApplyVulkanClip with every camera projection (hard rule 4).
        var shadowViewProjection = shadow.HasValue
            ? Camera.DirectionalShadowViewProjection(shadow.Value.AabbMin, shadow.Value.AabbMax, effectiveLighting.Sun.Direction)
            : Matrix4x4.Identity;
        Span<Matrix4x4> fixtureShadowViewProjections = stackalloc Matrix4x4[SceneLighting.MaxDownwardFixtureShadows];
        var fixtureShadowCount = BuildFixtureShadowViewProjections(effectiveLighting, shadow, fixtureShadowViewProjections);
        // TASK-245: BEFORE UpdateLighting, and the ordering is load-bearing. UpdateLighting only
        // switches sampling on for the volume that is actually RESIDENT, so preparing afterwards
        // leaves the enable flag describing the PREVIOUS call's volume -- which, on the first call
        // that uses one, means the volume is uploaded and then not read. Measured 2026-08-29: that
        // produced a frame byte-identical to the no-volume one, with no error anywhere, which is
        // exactly the failure mode this renderer cannot detect for itself (no validation layers).
        // Still outside BeginCommands, so the upload's image barriers and buffer-to-image copy are
        // not inside a render pass -- the same constraint PrepareOverlayImages works under.
        PrepareIrradiance(effectiveLighting.Irradiance);
        PrepareSurfaceTextures(effectiveLighting.SurfaceTextures);
        UpdateLighting(effectiveLighting, shadowsEnabled: shadow.HasValue, eye, fixtureShadowCount);
        UpdateFixtureShadows(fixtureShadowViewProjections);

        _primitivePacker.Pack(instances, viewProjection, shadowViewProjection);
        // The layer never casts or receives a shadow: its shadow coordinates are packed with
        // ForegroundLayer.NoShadowViewProjection (w = -1) and it is absent from every shadow pass.
        _foregroundPacker.Pack(foreground?.Instances ?? [], foreground?.ViewProjection ?? Matrix4x4.Identity, ForegroundLayer.NoShadowViewProjection);

        UploadPrimitiveInstances();


        // Any imported mesh a packed instance names, uploaded before a pass opens (see
        // EnsureBatchMeshes). Distinct from the `meshes` parameter below, which is the older
        // legacy-pipeline path: these are ordinary instances that happen to carry geometry.
        EnsureBatchMeshes();

        // Before BeginCommands, and therefore before any render pass: the texture upload records
        // image barriers and a buffer→image copy, which are illegal inside a render pass.
        var effectiveImages = images ?? [];
        PrepareOverlayImages(effectiveImages);

        var effectiveDecals = decals ?? [];
        PrepareDecals(effectiveDecals);
        var effectiveVoxels = voxels ?? [];
        PrepareVoxels(effectiveVoxels);

        // Mesh uploads record buffer copies, so they happen before any render pass opens — the same
        // constraint PrepareOverlayImages and PrepareDecals work under.
        var effectiveMeshes = meshes ?? [];
        foreach (var instance in effectiveMeshes)
        {
            EnsureMeshBuffer(instance.Mesh);
        }

        var command = BeginCommands();
        if (shadow.HasValue)
        {
            BeginShadowRenderPass(command, _shadowFramebuffer);
            DrawShadowBatches(command);
            _vk.CmdEndRenderPass(command);
            for (var i = 0; i < fixtureShadowCount; i++)
            {
                BeginShadowRenderPass(command, _fixtureShadowFramebuffers[i]);
                DrawFixtureShadowBatches(command, i);
                _vk.CmdEndRenderPass(command);
            }
        }

        BeginScenePass(command, clearColor, postProcess);
        DrawPrimitiveDepthPrepass(command);
        DrawPrimitiveBatches(command);
        DrawVoxels(command, effectiveVoxels, viewProjection);
        DrawMeshes(command, effectiveMeshes, viewProjection);
        DrawPrimitiveXRayBatches(command);
        DrawPrimitiveGlowBatches(command);
        DrawDecals(command, effectiveDecals, viewProjection);
        DrawParticles(command, viewProjection);
        DrawLines(command, viewProjection);
        FinishScenePass(command, clearColor, postProcess, viewProjection, postProcessSampleRect);
        DrawUiShapes(command, shapes ?? []);
        DrawForegroundLayer(command);
        DrawOverlayImages(command, effectiveImages);
        ScreenText.BuildQuads(text ?? [], _overlayQuads);
        DrawOverlay(command, _overlayQuads);
        _vk.CmdEndRenderPass(command);
        if (_externalTarget is { } target)
        {
            // External-target mode (see HeadlessRenderer.External.cs): copy on the GPU, no readback.
            CopyColorToImage(command, target);
            EndAndSubmit(command);
            return [];
        }

        CopyColorToReadback(command);
        EndAndSubmit(command);
        return ReadPixels();
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
            destination[i] = Camera.DownwardFixtureShadowViewProjection(
                light.Position, light.Range, shadow.Value.AabbMin.Y, shadow.Value.AabbMax.Y);
        }
        return count;
    }

    /// <summary>One <c>vkCmdBindVertexBuffers</c> + one <c>vkCmdDraw</c> per
    /// (<see cref="PrimitiveKind"/>, <see cref="MaterialKind"/>) group actually present this frame
    /// (<see cref="PrimitiveInstancePacker.VisibleBatches"/>) — TASK-152's whole point, refined by
    /// TASK-150: this draws <see cref="PrimitiveInstancePacker.VisibleBatches"/>, NOT
    /// <see cref="PrimitiveInstancePacker.Batches"/> — a <see cref="MaterialKind.ShadowCastOnly"/>
    /// instance (an invisible interior ceiling) must never rasterize a color-pass fragment, only
    /// occlude the shadow depth pass (<see cref="DrawShadowBatches"/>, which still draws every
    /// batch). <c>firstInstance</c> selects each group's contiguous block of the shared instance
    /// buffer; the mesh buffer (binding 0) is the only thing that changes between batches, since
    /// it is the one part of the vertex data that differs by shape rather than by instance.</summary>
    private void DrawPrimitiveBatches(CommandBuffer command)
    {
        if (_primitivePacker.TotalCount == 0)
        {
            return;
        }

        _vk.CmdBindPipeline(command, PipelineBindPoint.Graphics, _primitiveInstancedPipeline);
        var lightingSet = _lightingSet;
        _vk.CmdBindDescriptorSets(command, PipelineBindPoint.Graphics, _primitiveInstancedPipelineLayout, 0, 1, &lightingSet, 0, null);
        DrawPrimitiveBatchGeometry(command);
    }

    /// <summary>The depth prepass over exactly the batches <see cref="DrawPrimitiveBatches"/> shades.
    /// Same pass and same reasoning as <c>WindowedRenderer.DrawPrimitiveDepthPrepass</c>; kept here
    /// so both renderers produce the same pixels (hard rule 6).</summary>
    private void DrawPrimitiveDepthPrepass(CommandBuffer command)
    {
        if (_primitivePacker.TotalCount == 0)
        {
            return;
        }

        _vk.CmdBindPipeline(command, PipelineBindPoint.Graphics, _primitiveDepthPipeline);
        DrawPrimitiveBatchGeometry(command);
    }

    /// <summary>The through-the-wall silhouette pass — <see cref="PrimitiveInstancePacker.XRayBatches"/>
    /// only, drawn AFTER <see cref="DrawPrimitiveBatches"/> with the depth test disabled, so an
    /// <see cref="MaterialKind.XRayOutlineShell"/> lands on top of whatever wall is between the camera
    /// and the object it outlines. Binds the same lighting descriptor set as the colour pass because
    /// the two share one fragment shader — it reads nothing from that set for a shell (the colour is
    /// taken literally), but a pipeline layout that declares a set still needs one bound.
    /// <para>Kept a separate method rather than a flag on <see cref="DrawPrimitiveBatches"/>: the two
    /// differ in pipeline AND in batch list, and the ordering between them is the whole point.</para></summary>
    private void DrawPrimitiveXRayBatches(CommandBuffer command)
    {
        if (_primitivePacker.XRayBatches.Length == 0)
        {
            return;
        }

        _vk.CmdBindPipeline(command, PipelineBindPoint.Graphics, _primitiveXRayPipeline);
        var lightingSet = _lightingSet;
        _vk.CmdBindDescriptorSets(command, PipelineBindPoint.Graphics, _primitiveInstancedPipelineLayout, 0, 1, &lightingSet, 0, null);
        DrawPrimitiveBatchGeometry(command, _primitivePacker.XRayBatches);
    }

    /// <summary>The additive-glow pass — <see cref="PrimitiveInstancePacker.GlowBatches"/> only, drawn
    /// AFTER the colour pass with blending on and depth writes off. Kept in lockstep with
    /// <c>WindowedRenderer.DrawPrimitiveGlowBatches</c>.</summary>
    private void DrawPrimitiveGlowBatches(CommandBuffer command)
    {
        if (_primitivePacker.GlowBatches.Length == 0)
        {
            return;
        }

        _vk.CmdBindPipeline(command, PipelineBindPoint.Graphics, _primitiveGlowPipeline);
        var lightingSet = _lightingSet;
        _vk.CmdBindDescriptorSets(command, PipelineBindPoint.Graphics, _primitiveInstancedPipelineLayout, 0, 1, &lightingSet, 0, null);
        DrawPrimitiveBatchGeometry(command, _primitivePacker.GlowBatches);
    }

    /// <summary>The draw calls the primitive passes share — whichever pipeline is bound.</summary>
    private void DrawPrimitiveBatchGeometry(CommandBuffer command)
        => DrawPrimitiveBatchGeometry(command, _primitivePacker.VisibleBatches);

    private void DrawPrimitiveBatchGeometry(CommandBuffer command, ReadOnlySpan<PrimitiveBatch> batches)
        => DrawPrimitiveBatchGeometry(command, batches, baseInstance: 0);

    /// <summary>The <see cref="ForegroundLayer"/>: after the UI shapes, depth (NOT colour) is cleared
    /// over the whole frame so the layer lands on top of the scene and the panels while still
    /// occluding itself; then the same depth prepass + colour pass the scene uses, over
    /// <see cref="_foregroundPacker"/>'s range of the instance buffer. Kept in lockstep with
    /// <c>WindowedRenderer.DrawForegroundLayer</c>. Records nothing when the layer is absent/empty.</summary>
    private void DrawForegroundLayer(CommandBuffer command)
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
        var rect = new ClearRect { Rect = new Rect2D(new Offset2D(0, 0), new Extent2D(_width, _height)), BaseArrayLayer = 0, LayerCount = 1 };
        _vk.CmdClearAttachments(command, 1, &attachment, 1, &rect);

        var baseInstance = _primitivePacker.TotalCount;
        _vk.CmdBindPipeline(command, PipelineBindPoint.Graphics, _primitiveDepthPipeline);
        DrawPrimitiveBatchGeometry(command, _foregroundPacker.VisibleBatches, baseInstance);

        _vk.CmdBindPipeline(command, PipelineBindPoint.Graphics, _primitiveInstancedPipeline);
        var lightingSet = _lightingSet;
        _vk.CmdBindDescriptorSets(command, PipelineBindPoint.Graphics, _primitiveInstancedPipelineLayout, 0, 1, &lightingSet, 0, null);
        DrawPrimitiveBatchGeometry(command, _foregroundPacker.VisibleBatches, baseInstance);
    }

    private void DrawPrimitiveBatchGeometry(CommandBuffer command, ReadOnlySpan<PrimitiveBatch> batches, int baseInstance)
    {
        var buffers = stackalloc Silk.NET.Vulkan.Buffer[2];
        buffers[1] = _primitiveInstanceBuffer;
        var offsets = stackalloc ulong[2] { 0, 0 };
        foreach (var batch in batches)
        {
            var (mesh, vertexCount) = GeometryFor(batch);
            buffers[0] = mesh;
            _vk.CmdBindVertexBuffers(command, 0, 2, buffers, offsets);
            _vk.CmdDraw(command, vertexCount, (uint)batch.Count, 0, (uint)(baseInstance + batch.Offset));
        }
    }

    /// <summary>Uploads the scene's instances and, directly after them, the foreground layer's
    /// (<see cref="DrawForegroundLayer"/> draws at <c>baseInstance = _primitivePacker.TotalCount</c>).
    /// Capacity is ensured for both BEFORE mapping, because growing reallocates the buffer.</summary>
    private void UploadPrimitiveInstances()
    {
        var total = _primitivePacker.TotalCount + _foregroundPacker.TotalCount;
        if (total == 0)
        {
            return;
        }

        EnsurePrimitiveInstanceCapacity(total);

        var scene = _primitivePacker.Data;
        var foreground = _foregroundPacker.Data;
        var floatCount = scene.Length + foreground.Length;
        void* mapped = null;
        Check(_vk.MapMemory(_device, _primitiveInstanceMemory, 0, (ulong)(floatCount * sizeof(float)), 0, ref mapped), "MapMemory(primitiveInstances)");
        var destination = new Span<float>(mapped, floatCount);
        scene.CopyTo(destination);
        foreground.CopyTo(destination[scene.Length..]);
        _vk.UnmapMemory(_device, _primitiveInstanceMemory);
    }

    private void EnsurePrimitiveMeshes()
    {
        if (_sphereVertexBuffer.Handle != 0)
        {
            return;
        }

        CreateMeshBuffer(PrimitiveMeshes.BuildSphereVertices(), out _sphereVertexBuffer, out _sphereVertexMemory, out _sphereVertexCount, "sphere");
        CreateMeshBuffer(PrimitiveMeshes.BuildCapsuleVertices(), out _capsuleVertexBuffer, out _capsuleVertexMemory, out _capsuleVertexCount, "capsule");
        CreateMeshBuffer(PrimitiveMeshes.BuildCylinderVertices(), out _cylinderVertexBuffer, out _cylinderVertexMemory, out _cylinderVertexCount, "cylinder");
    }

    private (Silk.NET.Vulkan.Buffer Buffer, uint Count) Mesh(PrimitiveKind kind) => kind switch
    {
        PrimitiveKind.Cube => (_cubeVertexBuffer, _cubeVertexCount),
        PrimitiveKind.Sphere => (_sphereVertexBuffer, _sphereVertexCount),
        PrimitiveKind.Capsule => (_capsuleVertexBuffer, _capsuleVertexCount),
        PrimitiveKind.Cylinder => (_cylinderVertexBuffer, _cylinderVertexCount),
        _ => throw new ArgumentOutOfRangeException(nameof(kind)),
    };

    /// <summary>
    /// TASK-152's pipeline for <see cref="RenderPrimitiveScene"/>, extended by TASK-148 with
    /// shadow sampling: its own fragment shader (<see cref="_primitiveFrag"/>,
    /// <see cref="ShaderSource.PrimitiveFrag"/> — split off from <see cref="_boxFrag"/> once
    /// shadow sampling needed a <c>vShadowClipPos</c> input the legacy pipeline cannot supply,
    /// see that shader's doc comment), same render pass/depth-stencil/rasterizer/blend state as
    /// <see cref="EnsureBoxPipeline"/>'s <see cref="_boxPipeline"/>, but a different vertex
    /// shader (<see cref="ShaderSource.PrimitiveVert"/>) and vertex input state: a second,
    /// instance-rate binding replaces the old per-draw push constant (now also carrying the
    /// baked shadow-space mvp, locations 9-12), and there is no push-constant range at all.
    /// Deliberately a separate pipeline/layout from <see cref="_boxPipeline"/> rather than a
    /// modification of it — <see cref="_boxPipeline"/> is also used by the legacy
    /// <see cref="RenderScene"/>/<see cref="RenderBoxes"/> path, which still pushes one
    /// <c>BoxPush</c> per instance and binds no instance buffer; changing its vertex input state
    /// would break that path and its golden-pixel test.
    /// </summary>
    private void EnsurePrimitiveInstancedPipeline()
    {
        if (_primitiveInstancedReady)
        {
            return;
        }

        _primitiveInstancedVert = CreateShaderModule(PrecompiledShaders.PrimitiveVertSpirv);
        _primitiveFrag = CreateShaderModule(PrecompiledShaders.PrimitiveFragSpirv);
        _primitiveDepthFrag = CreateShaderModule(PrecompiledShaders.PrimitiveDepthFragSpirv);

        var entry = (byte*)SilkMarshal.StringToPtr("main");
        try
        {
            var stages = stackalloc PipelineShaderStageCreateInfo[2];
            stages[0] = new PipelineShaderStageCreateInfo { SType = StructureType.PipelineShaderStageCreateInfo, Stage = ShaderStageFlags.VertexBit, Module = _primitiveInstancedVert, PName = entry };
            stages[1] = new PipelineShaderStageCreateInfo { SType = StructureType.PipelineShaderStageCreateInfo, Stage = ShaderStageFlags.FragmentBit, Module = _primitiveFrag, PName = entry };

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

            var viewport = new Viewport { X = 0, Y = 0, Width = _width, Height = _height, MinDepth = 0, MaxDepth = 1 };
            var scissor = new Rect2D(new Offset2D(0, 0), new Extent2D(_width, _height));
            var viewportState = new PipelineViewportStateCreateInfo { SType = StructureType.PipelineViewportStateCreateInfo, ViewportCount = 1, PViewports = &viewport, ScissorCount = 1, PScissors = &scissor };

            var rasterizer = new PipelineRasterizationStateCreateInfo { SType = StructureType.PipelineRasterizationStateCreateInfo, PolygonMode = PolygonMode.Fill, CullMode = CullModeFlags.None, FrontFace = FrontFace.CounterClockwise, LineWidth = 1f };
            var multisample = new PipelineMultisampleStateCreateInfo { SType = StructureType.PipelineMultisampleStateCreateInfo, RasterizationSamples = SampleCountFlags.Count1Bit, MinSampleShading = 1f };
            // The prepass writes depth; the colour pass only tests against it (LESS_OR_EQUAL, writes
            // off) — see WindowedRenderer's matching comment and ShaderSource.PrimitiveDepthFrag for
            // why `discard` in the colour shader is what makes the prepass worth a whole extra pass.
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
            var colorBlend = new PipelineColorBlendStateCreateInfo { SType = StructureType.PipelineColorBlendStateCreateInfo, AttachmentCount = 1, PAttachments = &blendAttachment };
            var depthPrepassBlendAttachment = new PipelineColorBlendAttachmentState { ColorWriteMask = 0, BlendEnable = false };
            var depthPrepassColorBlend = new PipelineColorBlendStateCreateInfo { SType = StructureType.PipelineColorBlendStateCreateInfo, AttachmentCount = 1, PAttachments = &depthPrepassBlendAttachment };

            // MaterialKind.AdditiveGlow: the one blending pipeline on this otherwise fully opaque
            // path. SrcAlpha/One ADDS the fragment's colour weighted by its own alpha, which is what
            // makes overlapping glows read as brighter rather than as the nearest one winning -- and,
            // being commutative, is what lets them keep the packer's (kind, material) batch order
            // instead of needing a back-to-front sort. Depth TESTED so anything solid in front still
            // hides a glow, depth WRITE off so it hides nothing itself, including another glow.
            // Must stay identical to WindowedRenderer's own glow pipeline (hard rule 6).
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
            var glowColorBlend = new PipelineColorBlendStateCreateInfo { SType = StructureType.PipelineColorBlendStateCreateInfo, AttachmentCount = 1, PAttachments = &glowBlendAttachment };
            var glowDepthStencil = new PipelineDepthStencilStateCreateInfo
            {
                SType = StructureType.PipelineDepthStencilStateCreateInfo,
                DepthTestEnable = true,
                DepthWriteEnable = false,
                DepthCompareOp = CompareOp.LessOrEqual,
                DepthBoundsTestEnable = false,
                StencilTestEnable = false,
            };

            // No push-constant range: every value that used to be a per-draw push constant is now
            // per-instance vertex data (TASK-152) — this pipeline's layout has only the shared
            // lighting descriptor set.
            var setLayout = _lightingSetLayout;
            var layoutInfo = new PipelineLayoutCreateInfo { SType = StructureType.PipelineLayoutCreateInfo, SetLayoutCount = 1, PSetLayouts = &setLayout };
            Check(_vk.CreatePipelineLayout(_device, in layoutInfo, null, out _primitiveInstancedPipelineLayout), "CreatePipelineLayout(primitiveInstanced)");

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
                Layout = _primitiveInstancedPipelineLayout,
                RenderPass = _boxRenderPass,
                Subpass = 0,
            };
            Check(_vk.CreateGraphicsPipelines(_device, default, 1, in pipelineInfo, null, out _primitiveInstancedPipeline), "CreateGraphicsPipelines(primitiveInstanced)");

            // Same everything except the fragment stage, the depth state and the colour mask, from
            // the same locals — so the prepass's depth is exactly what the colour pass tests against.
            stages[1].Module = _primitiveDepthFrag;
            pipelineInfo.PDepthStencilState = &depthPrepassStencil;
            pipelineInfo.PColorBlendState = &depthPrepassColorBlend;
            Check(_vk.CreateGraphicsPipelines(_device, default, 1, in pipelineInfo, null, out _primitiveDepthPipeline), "CreateGraphicsPipelines(primitive depth prepass)");

            // The x-ray shell pass: the COLOUR fragment stage again (a shell is shaded, flatly, by
            // PrimitiveFrag) with the depth state swapped out -- so it is the same pixels the colour
            // pass would have drawn had nothing been in front of the shell.
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

        _primitiveInstancedReady = true;
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
        var info = new BufferCreateInfo { SType = StructureType.BufferCreateInfo, Size = size, Usage = BufferUsageFlags.VertexBufferBit, SharingMode = SharingMode.Exclusive };
        Check(_vk.CreateBuffer(_device, in info, null, out _primitiveInstanceBuffer), "CreateBuffer(primitiveInstances)");

        _vk.GetBufferMemoryRequirements(_device, _primitiveInstanceBuffer, out var requirements);
        _primitiveInstanceMemory = AllocateMemory(requirements, MemoryPropertyFlags.HostVisibleBit | MemoryPropertyFlags.HostCoherentBit);
        Check(_vk.BindBufferMemory(_device, _primitiveInstanceBuffer, _primitiveInstanceMemory, 0), "BindBufferMemory(primitiveInstances)");
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

    private void DisposePrimitiveMeshes()
    {
        if (_primitiveInstancedPipeline.Handle != 0)
        {
            _vk.DestroyPipeline(_device, _primitiveInstancedPipeline, null);
        }

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
        }

        if (_primitiveInstancedPipelineLayout.Handle != 0)
        {
            _vk.DestroyPipelineLayout(_device, _primitiveInstancedPipelineLayout, null);
        }

        if (_primitiveInstancedVert.Handle != 0)
        {
            _vk.DestroyShaderModule(_device, _primitiveInstancedVert, null);
        }

        if (_primitiveDepthFrag.Handle != 0)
        {
            _vk.DestroyShaderModule(_device, _primitiveDepthFrag, null);
        }

        if (_primitiveFrag.Handle != 0)
        {
            _vk.DestroyShaderModule(_device, _primitiveFrag, null);
        }

        if (_primitiveInstanceBuffer.Handle != 0)
        {
            _vk.DestroyBuffer(_device, _primitiveInstanceBuffer, null);
        }

        if (_primitiveInstanceMemory.Handle != 0)
        {
            _vk.FreeMemory(_device, _primitiveInstanceMemory, null);
        }

        if (_sphereVertexBuffer.Handle != 0)
        {
            _vk.DestroyBuffer(_device, _sphereVertexBuffer, null);
        }

        if (_sphereVertexMemory.Handle != 0)
        {
            _vk.FreeMemory(_device, _sphereVertexMemory, null);
        }

        if (_capsuleVertexBuffer.Handle != 0)
        {
            _vk.DestroyBuffer(_device, _capsuleVertexBuffer, null);
        }

        if (_capsuleVertexMemory.Handle != 0)
        {
            _vk.FreeMemory(_device, _capsuleVertexMemory, null);
        }

        if (_cylinderVertexBuffer.Handle != 0)
        {
            _vk.DestroyBuffer(_device, _cylinderVertexBuffer, null);
        }

        if (_cylinderVertexMemory.Handle != 0)
        {
            _vk.FreeMemory(_device, _cylinderVertexMemory, null);
        }
    }
}
