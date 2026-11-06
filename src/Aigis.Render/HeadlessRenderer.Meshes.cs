using System.Numerics;
using Silk.NET.Vulkan;

namespace Aigis.Render;

/// <summary>
/// Drawing arbitrary triangle meshes (2026-08-30) — <see cref="MeshData"/>, as opposed to the four
/// unit shapes <see cref="PrimitiveKind"/> names. Added so externally authored geometry (a logo
/// modelled in Blender) can be photographed by this renderer rather than reimplemented in primitives.
/// <para>
/// <b>It reuses the LEGACY box pipeline</b> (<c>_boxPipeline</c>, <see cref="ShaderSource.BoxVert"/>/
/// <see cref="ShaderSource.BoxFrag"/>), one push constant and one draw per instance, and that is the
/// whole reason this feature was small. The legacy path already binds a vertex buffer and pushes a
/// matrix per draw; the only thing it did not do was bind a DIFFERENT vertex buffer, and the vertex
/// layout it expects (position.xyz, normal.xyz) is exactly what an OBJ carries.
/// </para>
/// <para>
/// <b>What it therefore does not have</b>, inherited from that pipeline rather than chosen: no
/// shadow casting or receiving (<see cref="ShaderSource.PrimitiveVert"/>'s doc comment explains why
/// the legacy path cannot), no point or spot lights (see <see cref="RenderMeshScene"/>'s own
/// <c>lighting</c> note), and one draw call per instance rather than one per batch.
/// </para>
/// <para>
/// <b>THIS IS NO LONGER THE WAY TO PUT A MESH IN A GAME FRAME</b> (2026-09-03). This header used to
/// say a per-mesh vertex buffer "has no place in the instanced pipeline's batching key, and giving it
/// one would have meant reworking <c>PrimitiveInstancePacker</c>'s grouping" — which was true, and
/// that rework has since been done: set <see cref="PrimitiveInstance.Mesh"/> and the geometry is
/// packed, lit, shadowed and depth-prepassed as an ordinary instance (see <see cref="GeometryFor"/>).
/// Prefer that. This path is kept for the still-image callers already built on it — the logo shots
/// and <c>CapsuleScene</c>'s exports — where sun-and-ambient lighting is the intended look and one
/// draw per instance costs nothing.
/// </para>
/// </summary>
public sealed unsafe partial class HeadlessRenderer
{
    /// <summary>One GPU buffer per <see cref="MeshData"/>, keyed by REFERENCE — see that type's own
    /// note on why identity is deliberate. Uploaded on first use and kept until disposal, because a
    /// mesh is immutable and a caller rendering a turntable would otherwise re-upload it per frame.</summary>
    private readonly Dictionary<MeshData, (Silk.NET.Vulkan.Buffer Buffer, DeviceMemory Memory, uint Count)> _meshBuffers = [];

    /// <summary>
    /// Renders <paramref name="meshes"/> — and nothing else — into a frame, with the same lighting
    /// model every other path uses.
    /// </summary>
    /// <param name="clearColor">Background.</param>
    /// <param name="viewProjection">Camera.</param>
    /// <param name="meshes">What to draw, in order. Painter's order does not matter here — there is a
    /// depth buffer — but the order is preserved anyway so a caller can reason about ties.</param>
    /// <param name="lines">Optional debug lines, drawn after the meshes exactly as elsewhere.</param>
    /// <param name="lighting">Sun, ambient and render style. <see langword="null"/> is
    /// <see cref="SceneLighting.Default"/>.
    /// <para>
    /// <b>Sun, ambient and style reach this path.</b> <see cref="ShaderSource.BoxFrag"/> declares the
    /// shared lighting block but reads only those values, so <see cref="SceneLighting.PointLights"/>,
    /// <see cref="SceneLighting.SpotLights"/>, <see cref="SceneLighting.Specular"/> and the shadow
    /// terms are all accepted and then ignored. That is not a bug to fix here — the richer terms live
    /// in <see cref="ShaderSource.PrimitiveFrag"/>, which needs the instanced pipeline's per-instance
    /// data — but it IS a trap: a caller who hands over a carefully built three-rim rig gets a render
    /// lit by the sun alone and no error anywhere. Light a mesh scene with the sun and the ambient,
    /// and put the colour in those.
    /// </para></param>
    /// <returns>RGBA pixels, top row first, exactly as the other render entry points return.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="meshes"/> is null.</exception>
    public byte[] RenderMeshScene(
        Vector4 clearColor,
        Matrix4x4 viewProjection,
        IReadOnlyList<MeshInstance> meshes,
        IReadOnlyList<DebugLine>? lines = null,
        SceneLighting? lighting = null)
    {
        ArgumentNullException.ThrowIfNull(meshes);

        EnsureBoxPipeline();
        EnsureLinePipeline();
        UploadLines(lines ?? []);
        UpdateLighting(lighting ?? SceneLighting.Default, shadowsEnabled: false);

        // Uploads record buffer copies, so they happen BEFORE the render pass opens — the same
        // constraint PrepareOverlayImages works under.
        foreach (var instance in meshes)
        {
            EnsureMeshBuffer(instance.Mesh);
        }

        var cmd = BeginCommands();
        BeginBoxRenderPass(cmd, clearColor);

        _vk.CmdBindPipeline(cmd, PipelineBindPoint.Graphics, _boxPipeline);
        var lightingSet = _lightingSet;
        _vk.CmdBindDescriptorSets(cmd, PipelineBindPoint.Graphics, _boxPipelineLayout, 0, 1, &lightingSet, 0, null);

        foreach (var instance in meshes)
        {
            var (buffer, _, count) = _meshBuffers[instance.Mesh];
            if (count == 0)
            {
                continue;
            }

            ulong offset = 0;
            var bound = buffer;
            _vk.CmdBindVertexBuffers(cmd, 0, 1, &bound, &offset);

            var push = new BoxPush
            {
                Mvp = instance.Model * viewProjection,
                Color = instance.Color,
                // Zero, the same value a cube uses: the shader's shape term is for the sphere/capsule
                // radii the instanced path encodes, and a mesh's vertices are already where they go.
                Shape = Vector4.Zero,
                Rotation = RotationOf(instance.Model),
            };
            _vk.CmdPushConstants(
                cmd, _boxPipelineLayout, ShaderStageFlags.VertexBit | ShaderStageFlags.FragmentBit, 0, (uint)sizeof(BoxPush), &push);
            _vk.CmdDraw(cmd, count, 1, 0, 0);
        }

        DrawLines(cmd, viewProjection);

        _vk.CmdEndRenderPass(cmd);
        CopyColorToReadback(cmd);
        EndAndSubmit(cmd);
        return ReadPixels();
    }

    /// <summary>
    /// Draws <paramref name="meshes"/> inside a render pass somebody else opened — how an imported
    /// mesh joins a scene of primitives (<see cref="RenderPrimitiveScene"/>'s <c>meshes</c>).
    /// <para>
    /// <b>It switches pipeline mid-pass, and that is the point.</b> The primitives have just been
    /// drawn by the instanced pipeline; these go out through the legacy one, into the same colour and
    /// depth attachments, so they occlude and are occluded by everything else correctly. What does NOT
    /// carry across is the lighting: the legacy fragment shader reads sun and ambient only, so in a
    /// mixed scene the meshes are lit by those while the primitives also take the point lights. Keep
    /// the key light in the SUN for a scene that mixes them, or the two halves will not look like they
    /// are standing in the same room.
    /// </para>
    /// </summary>
    private void DrawMeshes(CommandBuffer command, IReadOnlyList<MeshInstance> meshes, Matrix4x4 viewProjection)
    {
        if (meshes.Count == 0)
        {
            return;
        }

        _vk.CmdBindPipeline(command, PipelineBindPoint.Graphics, _boxPipeline);
        var lightingSet = _lightingSet;
        _vk.CmdBindDescriptorSets(command, PipelineBindPoint.Graphics, _boxPipelineLayout, 0, 1, &lightingSet, 0, null);

        foreach (var instance in meshes)
        {
            var (buffer, _, count) = _meshBuffers[instance.Mesh];
            if (count == 0)
            {
                continue;
            }

            ulong offset = 0;
            var bound = buffer;
            _vk.CmdBindVertexBuffers(command, 0, 1, &bound, &offset);

            var push = new BoxPush
            {
                Mvp = instance.Model * viewProjection,
                Color = instance.Color,
                Shape = Vector4.Zero,
                Rotation = RotationOf(instance.Model),
            };
            _vk.CmdPushConstants(
                command, _boxPipelineLayout, ShaderStageFlags.VertexBit | ShaderStageFlags.FragmentBit, 0, (uint)sizeof(BoxPush), &push);
            _vk.CmdDraw(command, count, 1, 0, 0);
        }
    }

    /// <summary>
    /// The vertex buffer one packed batch draws — its own mesh's when it has one, otherwise the
    /// shared unit shape for its <see cref="PrimitiveKind"/>.
    /// <para>
    /// <b>This one method is the whole of "imported geometry in a game frame"</b> (2026-09-03).
    /// Binding 0 is the only part of a primitive draw that varies by SHAPE rather than by instance
    /// (see <c>DrawPrimitiveBatchGeometry</c>'s own note), so swapping it for a per-mesh buffer
    /// leaves the pipeline, the shaders, the instance-rate attributes, the depth prepass, the shadow
    /// passes and the x-ray pass all untouched — which is why an OBJ takes the same point lights and
    /// casts the same shadows as a cube, and why nothing needed a new SPIR-V build. Contrast
    /// <see cref="RenderMeshScene"/>/<see cref="DrawMeshes"/>, which go through the LEGACY box
    /// pipeline and therefore get sun and ambient only: that path predates this one and is kept for
    /// the still-image callers already built on it.
    /// </para>
    /// <para>
    /// A mesh whose buffer was never uploaded returns a zero handle and a zero count, which the draw
    /// loops skip. That cannot happen for a batch this renderer packed — <see cref="EnsureBatchMeshes"/>
    /// walks the packer before any pass opens — and is the honest answer rather than a throw from
    /// inside command recording.
    /// </para>
    /// </summary>
    private (Silk.NET.Vulkan.Buffer Buffer, uint VertexCount) GeometryFor(PrimitiveBatch batch)
    {
        if (batch.Mesh is not { } mesh)
        {
            return Mesh(batch.Kind);
        }

        return _meshBuffers.TryGetValue(mesh, out var uploaded)
            ? (uploaded.Buffer, uploaded.Count)
            : (default, 0);
    }

    /// <summary>
    /// Uploads any mesh this frame's packed batches name and has not been seen before.
    /// <para>Driven off the packer's BATCHES rather than the raw instance list because that is
    /// already the deduplicated set — a hundred instances of one mesh are one group, so this does one
    /// dictionary probe per distinct mesh instead of one per instance. Must be called before
    /// <c>BeginCommands</c>: <see cref="CreateMeshBuffer"/> maps and writes host-visible memory, and a
    /// mesh discovered mid-pass could not be bound in a command already recorded.</para>
    /// </summary>
    private void EnsureBatchMeshes()
    {
        foreach (var batch in _foregroundPacker.Batches)
        {
            if (batch.Mesh is { } foregroundMesh)
            {
                EnsureMeshBuffer(foregroundMesh);
            }
        }

        foreach (var batch in _primitivePacker.Batches)
        {
            if (batch.Mesh is { } mesh)
            {
                EnsureMeshBuffer(mesh);
            }
        }

        // The x-ray list is disjoint from Batches (see PrimitiveInstancePacker.XRayBatches), so a
        // mesh used ONLY as an x-ray shell would otherwise never be uploaded.
        foreach (var batch in _primitivePacker.XRayBatches)
        {
            if (batch.Mesh is { } mesh)
            {
                EnsureMeshBuffer(mesh);
            }
        }
    }

    private void EnsureMeshBuffer(MeshData mesh)
    {
        ArgumentNullException.ThrowIfNull(mesh);
        if (_meshBuffers.ContainsKey(mesh) || mesh.Vertices.Length == 0)
        {
            if (!_meshBuffers.ContainsKey(mesh))
            {
                _meshBuffers[mesh] = (default, default, 0);
            }

            return;
        }

        CreateMeshBuffer(mesh.Vertices, out var buffer, out var memory, out var count, mesh.Name);
        _meshBuffers[mesh] = (buffer, memory, count);
    }

    /// <summary>Releases every uploaded mesh. Called from <c>Dispose</c> while the device is still
    /// alive, like every other buffer this renderer owns.</summary>
    private void DisposeMeshBuffers()
    {
        foreach (var (buffer, memory, _) in _meshBuffers.Values)
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

        _meshBuffers.Clear();
    }
}
