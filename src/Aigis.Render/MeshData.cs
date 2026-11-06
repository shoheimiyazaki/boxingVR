using System.Numerics;

namespace Aigis.Render;

/// <summary>
/// An arbitrary triangle mesh the renderer can draw (2026-08-30), as opposed to the four fixed
/// shapes <see cref="PrimitiveKind"/> names.
/// <para>
/// <b>Why this is a separate concept and not a fifth <see cref="PrimitiveKind"/>.</b> Every kind is a
/// UNIT shape the caller scales through its model matrix, and the instanced pipeline batches by kind
/// precisely because there is one mesh per kind for the whole process. External geometry has neither
/// property: each mesh is its own vertex data with its own lifetime, and there are as many of them as
/// somebody loads. Adding it to that enum would have meant a mesh identity the batching key cannot
/// express.
/// </para>
/// <para>
/// <b>The vertex layout is the engine's existing one</b> — position.xyz, normal.xyz, six floats a
/// vertex, no index buffer — so a mesh drawn this way goes through the same vertex shader, the same
/// lighting and the same render pass as everything else. Nothing about it is a special case except
/// where the vertices came from.
/// </para>
/// <para>
/// Identity is REFERENCE identity, on purpose: the renderer caches one GPU buffer per instance of
/// this class. Two <c>MeshData</c> built from the same file are two uploads, and the same one used
/// twice is one — which is the behaviour a caller can reason about without knowing what a buffer is.
/// </para>
/// </summary>
public sealed class MeshData
{
    /// <summary>
    /// Wraps interleaved triangle vertices.
    /// </summary>
    /// <param name="vertices">Position.xyz, normal.xyz per vertex; three vertices per triangle.</param>
    /// <param name="name">For diagnostics — appears in the GPU buffer's debug label.</param>
    /// <exception cref="ArgumentException"><paramref name="vertices"/> is not a whole number of
    /// triangles.</exception>
    public MeshData(float[] vertices, string name = "mesh")
    {
        ArgumentNullException.ThrowIfNull(vertices);
        ArgumentNullException.ThrowIfNull(name);

        if (vertices.Length % (FloatsPerVertex * 3) != 0)
        {
            throw new ArgumentException(
                $"A mesh is whole triangles: {vertices.Length} floats is {vertices.Length / (float)FloatsPerVertex:F1} vertices, "
                + $"which is not a multiple of 3 at {FloatsPerVertex} floats each.",
                nameof(vertices));
        }

        Vertices = vertices;
        Name = name;
    }

    /// <summary>Floats per vertex: position.xyz then normal.xyz.</summary>
    public const int FloatsPerVertex = 6;

    /// <summary>The interleaved data. Not copied — do not mutate it after handing it over, because
    /// the GPU buffer is uploaded once and never re-read.</summary>
    public float[] Vertices { get; }

    /// <summary>A label for diagnostics.</summary>
    public string Name { get; }

    /// <summary>How many vertices there are.</summary>
    public int VertexCount => Vertices.Length / FloatsPerVertex;

    /// <summary>The mesh's axis-aligned bounds in its own space — what a caller needs to place it,
    /// scale it, or frame a camera on it. Computed on demand and cached.</summary>
    public (Vector3 Min, Vector3 Max) Bounds => _bounds ??= ComputeBounds();

    private (Vector3 Min, Vector3 Max)? _bounds;

    private (Vector3 Min, Vector3 Max) ComputeBounds()
    {
        if (Vertices.Length == 0)
        {
            return (Vector3.Zero, Vector3.Zero);
        }

        var min = new Vector3(float.MaxValue);
        var max = new Vector3(float.MinValue);
        for (var i = 0; i < Vertices.Length; i += FloatsPerVertex)
        {
            var p = new Vector3(Vertices[i], Vertices[i + 1], Vertices[i + 2]);
            min = Vector3.Min(min, p);
            max = Vector3.Max(max, p);
        }

        return (min, max);
    }
}

/// <summary>One placement of a <see cref="MeshData"/>: where it goes and what colour it is.</summary>
/// <param name="Mesh">The geometry. The same instance used twice costs one upload.</param>
/// <param name="Model">Model matrix, the same convention <see cref="PrimitiveInstance.Model"/>
/// uses — scale, then rotate, then translate.</param>
/// <param name="Color">Flat colour for the whole mesh. One draw is one colour here; a model with
/// several materials is several instances, which is what <c>ObjMesh</c>'s groups already are.</param>
public readonly record struct MeshInstance(MeshData Mesh, Matrix4x4 Model, Vector4 Color);
