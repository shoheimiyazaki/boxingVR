using System.Numerics;

namespace Aigis.Render;

/// <summary>
/// Turns <see cref="PrimitiveInstance"/>s into plain world-space triangles (2026-08-30) — the same
/// geometry the GPU draws, on the CPU, so a scene built out of this engine's primitives can be
/// written out to a file and opened somewhere else.
/// <para>
/// <b>It reproduces the vertex shader's per-kind deformation exactly</b>, and that is the whole
/// difficulty. A cube and a cylinder carry their size in <see cref="PrimitiveInstance.Model"/>, but a
/// SPHERE is a unit mesh scaled by <see cref="PrimitiveInstance.Dimensions"/>.X in the shader, and a
/// CAPSULE is stretched by a formula that moves each vertex along Y by its own normal
/// (<see cref="ShaderSource.BoxVert"/>'s <c>shape.w == 2.0</c> branch). Re-deriving those here from
/// the model matrix alone would silently export spheres of the wrong size and capsules with no
/// middle — geometry that looks plausible and is not what was rendered.
/// </para>
/// <para>
/// Normals are rotated by the model's rotation only, exactly as the shader does — not by the inverse
/// transpose. That is the engine's own convention and it is exact for the rotate-uniform-scale
/// matrices every caller builds; matching it is what makes an export shade like the render.
/// </para>
/// </summary>
public static class PrimitiveTessellator
{
    /// <summary>Floats per vertex: position.xyz then normal.xyz — <see cref="MeshData"/>'s layout.</summary>
    public const int FloatsPerVertex = MeshData.FloatsPerVertex;

    /// <summary>
    /// Triangulates one instance into world space.
    /// </summary>
    /// <exception cref="ArgumentOutOfRangeException">The instance names a kind this does not know.</exception>
    public static float[] Triangulate(PrimitiveInstance instance)
    {
        var unit = UnitMesh(instance.Kind);
        var vertices = new float[unit.Length];

        if (!Matrix4x4.Decompose(instance.Model, out _, out var rotation, out _))
        {
            rotation = Quaternion.Identity;
        }

        rotation = Quaternion.Normalize(rotation);

        // A MIRRORED model matrix reverses winding, and the export has to put it back.
        //
        // A negative determinant means the transform turns the mesh inside out: the triangles come
        // out clockwise-from-outside while their normals still point outward, so the two disagree.
        // The renderer does not care — it culls nothing on the colour pass and shades from the normal
        // — but a file does: every other tool reads the winding, and such a run loads as an inside-out
        // solid. Found by exporting Dr. Tod, whose mirrored parts came back reported as inside out by
        // this project's own loader.
        var mirrored = Determinant3(instance.Model) < 0f;

        for (var i = 0; i < unit.Length; i += FloatsPerVertex)
        {
            var local = new Vector3(unit[i], unit[i + 1], unit[i + 2]);
            var normal = new Vector3(unit[i + 3], unit[i + 4], unit[i + 5]);

            local = Deform(instance, local, normal);

            var world = Vector3.Transform(local, instance.Model);
            var worldNormal = Vector3.Transform(normal, rotation);
            if (worldNormal.LengthSquared() > 1e-12f)
            {
                worldNormal = Vector3.Normalize(worldNormal);
            }

            vertices[i] = world.X;
            vertices[i + 1] = world.Y;
            vertices[i + 2] = world.Z;
            vertices[i + 3] = worldNormal.X;
            vertices[i + 4] = worldNormal.Y;
            vertices[i + 5] = worldNormal.Z;
        }

        if (mirrored)
        {
            for (var t = 0; t + (FloatsPerVertex * 3) <= vertices.Length; t += FloatsPerVertex * 3)
            {
                for (var f = 0; f < FloatsPerVertex; f++)
                {
                    (vertices[t + FloatsPerVertex + f], vertices[t + (FloatsPerVertex * 2) + f]) =
                        (vertices[t + (FloatsPerVertex * 2) + f], vertices[t + FloatsPerVertex + f]);
                }
            }
        }

        return vertices;
    }

    /// <summary>Triangulates a whole list into one array — one run of triangles, in order.</summary>
    public static float[] Triangulate(IReadOnlyList<PrimitiveInstance> instances)
    {
        ArgumentNullException.ThrowIfNull(instances);

        var total = 0;
        foreach (var instance in instances)
        {
            total += UnitMesh(instance.Kind).Length;
        }

        var vertices = new float[total];
        var write = 0;
        foreach (var instance in instances)
        {
            var one = Triangulate(instance);
            Array.Copy(one, 0, vertices, write, one.Length);
            write += one.Length;
        }

        return vertices;
    }

    /// <summary>The determinant of the model's linear part — negative means the transform mirrors.</summary>
    private static float Determinant3(Matrix4x4 m) =>
        (m.M11 * ((m.M22 * m.M33) - (m.M23 * m.M32)))
        - (m.M12 * ((m.M21 * m.M33) - (m.M23 * m.M31)))
        + (m.M13 * ((m.M21 * m.M32) - (m.M22 * m.M31)));

    /// <summary>The shader's <c>shape</c> branch, on the CPU. See the type's own doc comment.</summary>
    private static Vector3 Deform(PrimitiveInstance instance, Vector3 local, Vector3 normal) => instance.Kind switch
    {
        PrimitiveKind.Sphere => local * instance.Dimensions.X,
        PrimitiveKind.Capsule => new Vector3(
            local.X * instance.Dimensions.X,
            (normal.Y * instance.Dimensions.X) + (MathF.Sign(local.Y) * instance.Dimensions.Y),
            local.Z * instance.Dimensions.X),
        _ => local,
    };

    private static float[] UnitMesh(PrimitiveKind kind) => kind switch
    {
        PrimitiveKind.Cube => Cube,
        PrimitiveKind.Sphere => Sphere,
        PrimitiveKind.Capsule => Capsule,
        PrimitiveKind.Cylinder => Cylinder,
        _ => throw new ArgumentOutOfRangeException(nameof(kind), kind, "Unsupported primitive kind."),
    };

    // Built once: these are the same arrays the renderer uploads, and they never change.
    private static readonly float[] Cube = PrimitiveMeshes.BuildCubeVertices();
    private static readonly float[] Sphere = PrimitiveMeshes.BuildSphereVertices();
    private static readonly float[] Capsule = PrimitiveMeshes.BuildCapsuleVertices();
    private static readonly float[] Cylinder = PrimitiveMeshes.BuildCylinderVertices();
}
