using Silk.NET.Vulkan;

namespace Aigis.Render;

/// <summary>
/// Imported triangle meshes in a LIVE frame (2026-09-03) — the windowed twin of
/// <see cref="HeadlessRenderer"/>'s <c>GeometryFor</c>/<c>EnsureBatchMeshes</c>, kept in lockstep
/// with it so a real window and an agent's filmstrip draw the same picture (hard rule 6).
/// <para>
/// <b>There is no new pipeline here, and that is the design.</b> A meshed
/// <see cref="PrimitiveInstance"/> is packed into the ordinary instance buffer alongside every cube
/// in the frame (<see cref="PrimitiveInstancePacker"/>); the only thing that differs at draw time is
/// which vertex buffer sits at binding 0. So an imported model goes through
/// <see cref="ShaderSource.PrimitiveVert"/>/<see cref="ShaderSource.PrimitiveFrag"/> exactly as
/// authored geometry does, and therefore takes the sun, the point lights, the spot lights, the
/// shadow maps, the depth prepass and the coverage dither with no code that knows it is a mesh.
/// </para>
/// <para>
/// <b>Why the windowed renderer had none of this before.</b> The mesh path was written for still
/// images and lives on <see cref="HeadlessRenderer"/>'s LEGACY box pipeline
/// (<c>HeadlessRenderer.Meshes.cs</c>), which reads sun and ambient only and casts no shadows — its
/// own doc comment says a game frame "would need the instanced path". This is that path, so nothing
/// equivalent to <c>DrawMeshes</c> is needed here: there is no second pipeline to switch to.
/// </para>
/// </summary>
public sealed unsafe partial class WindowedRenderer
{
    /// <summary>
    /// One GPU buffer per <see cref="MeshData"/>, keyed by REFERENCE — see that type's own note on
    /// why identity is deliberate.
    /// <para>Uploaded on first sight and kept until disposal. A mesh is immutable, so a level whose
    /// ambulance is an imported model uploads it once for the whole session rather than once a frame;
    /// that is the same lifetime the four shared unit shapes have, for the same reason.</para>
    /// </summary>
    private readonly Dictionary<MeshData, (Silk.NET.Vulkan.Buffer Buffer, DeviceMemory Memory, uint Count)> _meshBuffers = [];

    /// <summary>
    /// The vertex buffer one packed batch draws — its own mesh's when it has one, otherwise the
    /// shared unit shape for its <see cref="PrimitiveKind"/>.
    /// <para>A mesh with no uploaded buffer yields a zero count, which every draw loop skips. That
    /// cannot happen for a batch this renderer packed (<see cref="EnsureBatchMeshes"/> runs before any
    /// pass opens) and is a quiet no-op rather than a throw from inside command recording — a
    /// half-recorded command buffer is not a state worth reaching to report a missing model.</para>
    /// </summary>
    private (Silk.NET.Vulkan.Buffer Buffer, uint VertexCount) GeometryFor(PrimitiveBatch batch)
    {
        if (batch.Mesh is not { } mesh)
        {
            return MeshFor(batch.Kind);
        }

        return _meshBuffers.TryGetValue(mesh, out var uploaded)
            ? (uploaded.Buffer, uploaded.Count)
            : (default, 0);
    }

    /// <summary>
    /// Uploads any mesh this frame's packed batches name and that has not been seen before.
    /// <para>Driven off the packer's BATCHES rather than the raw instance list because that is
    /// already the deduplicated set — a hundred instances of one mesh form one group, so this costs
    /// one dictionary probe per distinct mesh rather than one per instance, which matters on a path
    /// that runs every frame (see CLAUDE.md's per-frame allocation budget).</para>
    /// <para><b>Before command recording</b>: <see cref="CreateMeshBuffer"/> maps and writes
    /// host-visible memory, and a mesh discovered mid-pass could not be bound into a command already
    /// recorded.</para>
    /// </summary>
    private void EnsureBatchMeshes(PrimitiveInstancePacker packer)
    {
        foreach (var batch in packer.Batches)
        {
            if (batch.Mesh is { } mesh)
            {
                EnsureMeshBuffer(mesh);
            }
        }

        // The x-ray list is disjoint from Batches (see PrimitiveInstancePacker.XRayBatches), so a
        // mesh used ONLY as an x-ray shell would otherwise never be uploaded.
        foreach (var batch in packer.XRayBatches)
        {
            if (batch.Mesh is { } mesh)
            {
                EnsureMeshBuffer(mesh);
            }
        }
    }

    private void EnsureMeshBuffer(MeshData mesh)
    {
        if (_meshBuffers.ContainsKey(mesh))
        {
            return;
        }

        if (mesh.Vertices.Length == 0)
        {
            // Recorded as present-but-empty so an empty mesh is probed once rather than re-examined
            // every frame for ever.
            _meshBuffers[mesh] = (default, default, 0);
            return;
        }

        CreateMeshBuffer(mesh.Vertices, out var buffer, out var memory, out var count, mesh.Name);
        _meshBuffers[mesh] = (buffer, memory, count);
    }

    /// <summary>Releases every uploaded mesh. Called from the dispose path while the device is still
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
