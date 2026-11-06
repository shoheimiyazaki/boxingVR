using System.Numerics;

namespace Aigis.Render;

/// <summary>
/// Pure vertex-generation for the shared primitive meshes (cube/sphere/capsule/cylinder) — no
/// Vulkan handles, no side effects, safe to share between <see cref="HeadlessRenderer"/>
/// and <see cref="WindowedRenderer"/> (unlike their independent Vulkan object lifetimes,
/// this is exactly the kind of "input -> output" logic that's safe and cheap to dedupe;
/// see docs/plans/human-play.md principle 3). Vertex layout is 6 floats per vertex:
/// position (xyz) then normal (xyz).
/// </summary>
public static class PrimitiveMeshes
{
    /// <summary>
    /// How many segments a curved primitive is swept into around its axis, and (for the sphere) how
    /// many bands it is stacked into. <b>Raised from 16x10 to 24x16 on 2026-09-19.</b>
    /// <para>
    /// The old counts were chosen when nothing in this engine was ever looked at from closer than the
    /// stage-fit camera, where a sphere is a few dozen pixels and ten bands are more than enough. That
    /// stopped being true: <c>tools/Aigis.RigViz</c> and <c>tools/Aigis.ModelViz</c> photograph models
    /// from a metre, the fall camera and the elevator cabin do the same in play, and the material pass
    /// this change belongs to put a real specular highlight on every curved surface — which is
    /// precisely the shading term that shows faceting, because a highlight sliding across a flat
    /// facet jumps at each edge instead of travelling.
    /// </para>
    /// <para>
    /// <b>Cost.</b> Vertices scale with the product, so a sphere goes from 960 to 2,304 and a capsule
    /// from 1,056 to 2,400 — but these are ONE shared mesh each, instanced (see
    /// <c>PrimitiveInstancePacker</c>), so the cost is 8 KB of vertex buffer and 2.4x the per-instance
    /// vertex-shader invocations for the sphere/capsule/cylinder draws only. Cubes, which are the
    /// overwhelming majority of every Triage frame, are untouched.
    /// </para>
    /// <para>
    /// Stated as named constants rather than repeated literals because the four builders below must
    /// agree: a capsule and a cylinder of the same radius standing side by side with different segment
    /// counts is a defect a reader will blame on the geometry rather than on the tessellation.
    /// </para>
    /// </summary>
    private const int CurvedSegments = 24;

    /// <summary>Latitude bands for a full sphere; see <see cref="CurvedSegments"/>. A capsule uses
    /// half of this per hemisphere, rounded down, so its caps match a sphere's curvature.</summary>
    private const int SphereStacks = 16;

    public static float[] BuildCubeVertices()
    {
        // 6 faces, each: outward normal + 4 CCW corners of a unit cube [-0.5, 0.5]^3.
        (Vector3 Normal, Vector3 C0, Vector3 C1, Vector3 C2, Vector3 C3)[] faces =
        [
            (new(1, 0, 0), new(0.5f, -0.5f, 0.5f), new(0.5f, -0.5f, -0.5f), new(0.5f, 0.5f, -0.5f), new(0.5f, 0.5f, 0.5f)),
            (new(-1, 0, 0), new(-0.5f, -0.5f, -0.5f), new(-0.5f, -0.5f, 0.5f), new(-0.5f, 0.5f, 0.5f), new(-0.5f, 0.5f, -0.5f)),
            (new(0, 1, 0), new(-0.5f, 0.5f, 0.5f), new(0.5f, 0.5f, 0.5f), new(0.5f, 0.5f, -0.5f), new(-0.5f, 0.5f, -0.5f)),
            (new(0, -1, 0), new(-0.5f, -0.5f, -0.5f), new(0.5f, -0.5f, -0.5f), new(0.5f, -0.5f, 0.5f), new(-0.5f, -0.5f, 0.5f)),
            (new(0, 0, 1), new(-0.5f, -0.5f, 0.5f), new(0.5f, -0.5f, 0.5f), new(0.5f, 0.5f, 0.5f), new(-0.5f, 0.5f, 0.5f)),
            (new(0, 0, -1), new(0.5f, -0.5f, -0.5f), new(-0.5f, -0.5f, -0.5f), new(-0.5f, 0.5f, -0.5f), new(0.5f, 0.5f, -0.5f)),
        ];

        var data = new List<float>(36 * 6);
        foreach (var f in faces)
        {
            AddTriangle(data, f.Normal, f.C0, f.C1, f.C2);
            AddTriangle(data, f.Normal, f.C0, f.C2, f.C3);
        }

        return data.ToArray();

        static void AddTriangle(List<float> data, Vector3 n, Vector3 a, Vector3 b, Vector3 c)
        {
            AddVertex(data, a, n);
            AddVertex(data, b, n);
            AddVertex(data, c, n);
        }
    }

    public static float[] BuildSphereVertices()
    {
        const int slices = CurvedSegments;
        const int stacks = SphereStacks;
        var data = new List<float>(slices * stacks * 6 * 6);
        for (var stack = 0; stack < stacks; stack++)
        {
            var latitude0 = -MathF.PI * 0.5f + MathF.PI * stack / stacks;
            var latitude1 = -MathF.PI * 0.5f + MathF.PI * (stack + 1) / stacks;
            for (var slice = 0; slice < slices; slice++)
            {
                var longitude0 = 2f * MathF.PI * slice / slices;
                var longitude1 = 2f * MathF.PI * (slice + 1) / slices;
                var a = SpherePoint(latitude0, longitude0);
                var b = SpherePoint(latitude0, longitude1);
                var c = SpherePoint(latitude1, longitude1);
                var d = SpherePoint(latitude1, longitude0);
                // a -> c -> b, not a -> b -> c: latitude increases upward while longitude increases
                // the other way round the axis, so the naive corner order winds this quad CLOCKWISE
                // seen from outside -- the opposite of the cube's. Both renderers used to rasterize
                // every face of every solid, so nothing noticed; back-face culling makes the
                // difference between a sphere and a hollow shell of its own far side.
                // EveryMesh_IsWoundCounterClockwiseFromOutside pins the rule for all four meshes.
                AddTriangle(data, a, a, c, c, b, b);
                AddTriangle(data, a, a, d, d, c, c);
            }
        }

        return data.ToArray();
    }

    public static float[] BuildCapsuleVertices()
    {
        const int slices = CurvedSegments;
        const int hemisphereStacks = SphereStacks / 2;
        var data = new List<float>(slices * (hemisphereStacks * 2 + 1) * 6 * 6);
        for (var hemisphere = -1; hemisphere <= 1; hemisphere += 2)
        {
            for (var stack = 0; stack < hemisphereStacks; stack++)
            {
                var angle0 = MathF.PI * 0.5f * stack / hemisphereStacks;
                var angle1 = MathF.PI * 0.5f * (stack + 1) / hemisphereStacks;
                for (var slice = 0; slice < slices; slice++)
                {
                    var longitude0 = 2f * MathF.PI * slice / slices;
                    var longitude1 = 2f * MathF.PI * (slice + 1) / slices;
                    var a = HemispherePoint(hemisphere, angle0, longitude0);
                    var b = HemispherePoint(hemisphere, angle0, longitude1);
                    var c = HemispherePoint(hemisphere, angle1, longitude1);
                    var d = HemispherePoint(hemisphere, angle1, longitude0);
                    // The two hemispheres are MIRROR images of each other (HemispherePoint negates Y
                    // for the lower one), and a mirror reverses orientation -- so one corner order
                    // cannot serve both. The lower cap therefore emits the reversed order, which is
                    // what keeps every triangle of this mesh counter-clockwise seen from outside
                    // (EveryMesh_IsWoundCounterClockwiseFromOutside pins it; back-face culling in the
                    // shadow passes depends on it).
                    if (hemisphere > 0)
                    {
                        AddTriangle(data, a.Position, a.Normal, b.Position, b.Normal, c.Position, c.Normal);
                        AddTriangle(data, a.Position, a.Normal, c.Position, c.Normal, d.Position, d.Normal);
                    }
                    else
                    {
                        AddTriangle(data, a.Position, a.Normal, c.Position, c.Normal, b.Position, b.Normal);
                        AddTriangle(data, a.Position, a.Normal, d.Position, d.Normal, c.Position, c.Normal);
                    }
                }
            }
        }

        for (var slice = 0; slice < slices; slice++)
        {
            var longitude0 = 2f * MathF.PI * slice / slices;
            var longitude1 = 2f * MathF.PI * (slice + 1) / slices;
            var n0 = new Vector3(MathF.Cos(longitude0), 0f, MathF.Sin(longitude0));
            var n1 = new Vector3(MathF.Cos(longitude1), 0f, MathF.Sin(longitude1));
            var a = n0 + Vector3.UnitY;
            var b = n1 + Vector3.UnitY;
            var c = n1 - Vector3.UnitY;
            var d = n0 - Vector3.UnitY;
            AddTriangle(data, a, n0, b, n1, c, n1);
            AddTriangle(data, a, n0, c, n1, d, n0);
        }

        return data.ToArray();
    }

    /// <summary>
    /// A unit cylinder along +Y: radius 0.5, y in [-0.5, 0.5] — deliberately the SAME "unit shape,
    /// caller bakes the real size into <see cref="PrimitiveInstance.Model"/>" convention as
    /// <see cref="BuildCubeVertices"/>, NOT the "unit shape, shader scales it by
    /// <see cref="PrimitiveInstance.Dimensions"/>" convention sphere/capsule use.
    /// <para>
    /// That choice is what lets a cylinder exist without touching a single line of
    /// <see cref="ShaderSource"/> (and therefore without regenerating
    /// <see cref="PrecompiledShaders"/> or re-minting one golden): the vertex shader's per-kind
    /// deformation is selected by <c>inShape.w</c>, and a cylinder simply takes the <c>0.0</c>
    /// (= no deformation) branch a cube already takes.
    /// </para>
    /// <para>
    /// Baking a cylinder's size into <c>Model</c> as the non-uniform scale
    /// <c>(diameter, height, diameter)</c> also keeps its normals exactly right, which is not true
    /// of non-uniform scale in general. The shader transforms a normal by the instance's rotation
    /// alone (it has no inverse-transpose), and the correct normal under a scale <c>S</c> is
    /// <c>normalize(N / S)</c>: for a side normal <c>(cos t, 0, sin t)</c> under
    /// <c>(r, h, r)</c> that is <c>normalize(cos t / r, 0, sin t / r)</c> — the same direction,
    /// because X and Z are scaled equally — and a cap normal <c>(0, +-1, 0)</c> is unchanged for
    /// the same reason. A cylinder whose X and Z scales differ (an elliptical cross-section) would
    /// shade slightly wrong; that is out of scope for a modeling primitive whose radius is one
    /// number.
    /// </para>
    /// </summary>
    public static float[] BuildCylinderVertices()
    {
        // Matches BuildCapsuleVertices' slice count so a capsule and a cylinder of the same radius
        // read as the same tessellation quality side by side in the editor.
        const int slices = CurvedSegments;
        const float radius = 0.5f;
        const float halfHeight = 0.5f;
        var data = new List<float>(slices * 4 * 3 * 6);
        for (var slice = 0; slice < slices; slice++)
        {
            var longitude0 = 2f * MathF.PI * slice / slices;
            var longitude1 = 2f * MathF.PI * (slice + 1) / slices;
            var n0 = new Vector3(MathF.Cos(longitude0), 0f, MathF.Sin(longitude0));
            var n1 = new Vector3(MathF.Cos(longitude1), 0f, MathF.Sin(longitude1));

            // Side wall: smooth-shaded (each vertex carries its own radial normal).
            var top0 = n0 * radius + Vector3.UnitY * halfHeight;
            var top1 = n1 * radius + Vector3.UnitY * halfHeight;
            var bottom1 = n1 * radius - Vector3.UnitY * halfHeight;
            var bottom0 = n0 * radius - Vector3.UnitY * halfHeight;
            AddTriangle(data, top0, n0, top1, n1, bottom1, n1);
            AddTriangle(data, top0, n0, bottom1, n1, bottom0, n0);

            // Caps: flat-shaded triangle fans from each centre. Corner order is the one that winds
            // each cap counter-clockwise seen from OUTSIDE its own face -- looking down at the top
            // cap from above, going round by DECREASING longitude -- which is the opposite of the
            // side wall's order and easy to get backwards; see the sphere's note and
            // EveryMesh_IsWoundCounterClockwiseFromOutside.
            var up = Vector3.UnitY;
            AddTriangle(data, up * halfHeight, up, top1, up, top0, up);
            AddTriangle(data, -up * halfHeight, -up, bottom0, -up, bottom1, -up);
        }

        return data.ToArray();
    }

    private static Vector3 SpherePoint(float latitude, float longitude)
    {
        var cosine = MathF.Cos(latitude);
        return new Vector3(cosine * MathF.Cos(longitude), MathF.Sin(latitude), cosine * MathF.Sin(longitude));
    }

    private static (Vector3 Position, Vector3 Normal) HemispherePoint(int hemisphere, float angle, float longitude)
    {
        var normal = new Vector3(
            MathF.Sin(angle) * MathF.Cos(longitude),
            hemisphere * MathF.Cos(angle),
            MathF.Sin(angle) * MathF.Sin(longitude));
        return (normal + Vector3.UnitY * hemisphere, normal);
    }

    private static void AddTriangle(List<float> data, Vector3 a, Vector3 na, Vector3 b, Vector3 nb, Vector3 c, Vector3 nc)
    {
        AddVertex(data, a, na);
        AddVertex(data, b, nb);
        AddVertex(data, c, nc);
    }

    private static void AddVertex(List<float> data, Vector3 position, Vector3 normal)
    {
        data.Add(position.X);
        data.Add(position.Y);
        data.Add(position.Z);
        data.Add(normal.X);
        data.Add(normal.Y);
        data.Add(normal.Z);
    }
}
