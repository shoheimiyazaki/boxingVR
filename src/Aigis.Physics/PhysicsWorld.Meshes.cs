using System.Numerics;
using BepuPhysics;
using BepuPhysics.Collidables;

namespace Aigis.Physics;

/// <summary>
/// Immovable triangle meshes — level geometry that is not a stack of boxes (2026-10-10: the lunar
/// stages' procedural hills and craters, which a box approximation would leave full of seams that
/// catch a wheel or a ragdoll's hand).
/// </summary>
public sealed partial class PhysicsWorld
{
    /// <summary>
    /// Adds an immovable triangle mesh in world space. <paramref name="triangleVertices"/> holds three
    /// vertices per triangle; a triangle collides from the side its face normal
    /// <c>(b - a) × (c - a)</c> points to — counter-clockwise seen from that side, the same winding the
    /// renderer's front faces use. For ground, that is counter-clockwise seen from above.
    /// <para>
    /// <b>One-sided on purpose.</b> The backend's meshes only collide from the front: a body that
    /// gets underneath (tunnelling at speed) falls out rather than being shoved back up through the
    /// surface, which is the failure a ground mesh can survive. The winding is converted here, in one
    /// place, so no caller has to know the backend's own convention.
    /// </para>
    /// </summary>
    /// <exception cref="ArgumentException">The vertex count is zero or not a multiple of three, or a
    /// vertex is not finite.</exception>
    public void AddStaticMesh(ReadOnlySpan<Vector3> triangleVertices)
    {
        ThrowIfDisposed();
        if (triangleVertices.Length == 0 || triangleVertices.Length % 3 != 0)
        {
            throw new ArgumentException(
                $"A mesh needs three vertices per triangle; got {triangleVertices.Length}.", nameof(triangleVertices));
        }

        foreach (var vertex in triangleVertices)
        {
            if (!float.IsFinite(vertex.X) || !float.IsFinite(vertex.Y) || !float.IsFinite(vertex.Z))
            {
                throw new ArgumentException($"A mesh vertex is not finite: {vertex}.", nameof(triangleVertices));
            }
        }

        var count = triangleVertices.Length / 3;
        _pool.Take<Triangle>(count, out var triangles);
        for (var i = 0; i < count; i++)
        {
            // The backend's triangle faces the other way round from (a, b, c) counter-clockwise, so the
            // last two vertices swap — measured, not assumed: see PhysicsMeshTests.
            triangles[i] = new Triangle(triangleVertices[i * 3], triangleVertices[(i * 3) + 2], triangleVertices[(i * 3) + 1]);
        }

        var mesh = new Mesh(triangles, Vector3.One, _pool);
        var shape = _simulation.Shapes.Add(mesh);
        _simulation.Statics.Add(new StaticDescription(Vector3.Zero, Quaternion.Identity, shape));
    }
}
