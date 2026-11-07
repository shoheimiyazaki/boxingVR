using System.Collections.Immutable;
using System.Numerics;
using Aigis.Physics;
using Aigis.Render;

namespace MiniRing.LowPoly;

/// <summary>
/// 体格（half extents）に合わせた部位ごとの変形前メッシュ（部位ローカル [m]、細分化済み）と、その静的な描画メッシュ。
/// 試合中は 1 つを使い回す（<see cref="MeshData"/> は参照ごとに GPU へ上がるので、ラウンドごとに作り直さない）。
/// </summary>
public sealed class BoxerModel
{
    private readonly ImmutableDictionary<BodyPartKind, (LowPolyMesh Mesh, MeshData Static, float MinHalfExtent)> _parts;

    private BoxerModel(ImmutableDictionary<BodyPartKind, (LowPolyMesh, MeshData, float)> parts) => _parts = parts;

    public static BoxerModel Build(HumanoidRagdoll.HalfExtents halfExtents, int subdivisions)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(subdivisions);
        var parts = Enum.GetValues<BodyPartKind>().ToImmutableDictionary(kind => kind, kind =>
        {
            var half = HalfExtentsOf(halfExtents, kind);
            var mesh = BoxerShapes.For(kind).Scaled(half);
            for (var i = 0; i < subdivisions; i++)
            {
                mesh = mesh.Subdivide();
            }

            return (mesh, mesh.ToMeshData($"boxer-{kind}"), MathF.Min(half.X, MathF.Min(half.Y, half.Z)));
        });
        return new BoxerModel(parts);
    }

    /// <summary>変形前の形（部位ローカル [m]）。</summary>
    public LowPolyMesh Mesh(BodyPartKind kind) => _parts[kind].Mesh;

    /// <summary>へこんでいない部位を 1 回の描画で済ませる静的メッシュ。</summary>
    public MeshData RestMesh(BodyPartKind kind) => _parts[kind].Static;

    /// <summary>部位の箱の最小の半径 [m]（へこみの上限の基準）。</summary>
    public float MinHalfExtent(BodyPartKind kind) => _parts[kind].MinHalfExtent;

    public static Vector3 HalfExtentsOf(HumanoidRagdoll.HalfExtents h, BodyPartKind kind) => kind switch
    {
        BodyPartKind.Head => h.Head,
        BodyPartKind.Chest => h.Chest,
        BodyPartKind.Pelvis => h.Pelvis,
        BodyPartKind.UpperArm => h.UpperArm,
        BodyPartKind.Forearm => h.Forearm,
        BodyPartKind.Thigh => h.Thigh,
        BodyPartKind.Shin => h.Shin,
        BodyPartKind.Foot => h.Foot,
        _ => throw new ArgumentOutOfRangeException(nameof(kind), kind, "大きさの無い部位です。"),
    };
}
