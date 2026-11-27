using System.Collections.Concurrent;
using System.Numerics;
using Aigis.Foundation.Voxels;
using Aigis.Physics;

namespace MiniRing.Voxels;

/// <summary>
/// 体の質量をボクセルで数える（docs/plans/BASE_BODY_PLAN.md §3、2026-11-27）: 部位ごとの「箱の体積あたりの質量」[kg/m³] =
/// 基準の体格（比 1）の部位の格子の、材質ごとのセルの数 × 材質の密度 × セルの体積の和 ÷ 部位の箱の体積。
/// <para>
/// 体格を変えた体は、基準の体格で作った同じセルを箱の寸法で伸び縮みさせる（<see cref="BoxerBody.BuildPartGrid"/>）ので、
/// 部位の質量 = これ × その体の箱の体積 が、その体の格子を数えた値とちょうど同じになる。生成の前に要る質量（剛体を作る・紙の体重・減量の二分法）は
/// これから出し、生成した体はボクセルを作ったあと格子を数え直して剛体の質量と慣性に入れる（<see cref="Aigis.Physics.Voxels.VoxelBody.FitMassToCells"/>）。
/// 2 つが食い違うのは、顔の部品・髪型（キャラごとの顔の層）の違いと、四足の頭・脚の先（実際の寸法で作る格子）だけ。
/// </para>
/// </summary>
internal static class BodyMassTable
{
    private static readonly ConcurrentDictionary<Key, Lazy<Dictionary<BodyPartKind, float>>> Cache = new();

    /// <summary>部位 <paramref name="kind"/> の箱の体積あたりの質量 [kg/m³]（胸は胴全体の箱: 腹に分ける前）。</summary>
    public static float DensityOf(FighterTuning tuning, BodyPartKind kind)
    {
        ArgumentNullException.ThrowIfNull(tuning);
        var human = tuning with { Build = BodyBuild.Standard };
        var key = new Key(human.HalfExtents, tuning.VoxelSize, tuning.ModelSubdivisions, tuning.Build.FourLegged);
        return Cache.GetOrAdd(key, k => new Lazy<Dictionary<BodyPartKind, float>>(() => Count(tuning, k))).Value[kind];
    }

    /// <summary>部位 <paramref name="kind"/> の、半分の寸法 <paramref name="half"/> の箱の質量 [kg]。</summary>
    public static float MassOf(FighterTuning tuning, BodyPartKind kind, Vector3 half) => DensityOf(tuning, kind) * 8f * half.X * half.Y * half.Z;

    private static Dictionary<BodyPartKind, float> Count(FighterTuning tuning, Key key)
    {
        var unit = tuning with { Build = new BodyBuild(FourLegged: key.FourLegged) };
        var half = unit.HalfExtents;
        var shapes = LowPoly.BoxerModel.Build(key.HumanHalf, key.Subdivisions);
        var parts = new (BodyPartKind Kind, Vector3 Half)[]
        {
            (BodyPartKind.Pelvis, half.Pelvis), (BodyPartKind.Chest, half.Chest), (BodyPartKind.Head, half.Head),
            (BodyPartKind.UpperArm, half.UpperArm), (BodyPartKind.Forearm, half.Forearm), (BodyPartKind.Thigh, half.Thigh),
            (BodyPartKind.Shin, half.Shin), (BodyPartKind.Foot, half.Foot), (BodyPartKind.Toe, FootGeometry.ToeHalf(half.Foot, key.FourLegged)),
        };
        var table = new Dictionary<BodyPartKind, float>();
        foreach (var (kind, partHalf) in parts)
        {
            VoxelPalette? palette = null;
            var grid = BoxerBody.BuildPartGrid(kind, partHalf, unit, shapes, shapes, key.FourLegged ? null : Outfit.RedCorner, BoxerVoxelOverrides.Shipped, null, ref palette);
            var kg = MassOf(grid);
            if (kind == BodyPartKind.Head && !key.FourLegged)
            {
                var (inner, face, _) = FaceLayer.Build(grid, FaceSpec.For("standard"));
                kg = MassOf(inner) + MassOf(face);
            }

            table[kind] = kg / (8f * partHalf.X * partHalf.Y * partHalf.Z);
        }

        return table;
    }

    /// <summary>格子の質量 [kg]: 埋まったセルごとに材質の密度 × セルの体積。</summary>
    public static float MassOf(VoxelGrid grid)
    {
        ArgumentNullException.ThrowIfNull(grid);
        var kg = 0f;
        for (var i = 0; i < grid.CellCount; i++)
        {
            var m = grid.MaterialAt(i);
            if (m != 0)
            {
                kg += grid.Palette[m].MechanicsOrDefault.Density * grid.CellVolume;
            }
        }

        return kg;
    }

    private readonly record struct Key(HumanoidRagdoll.HalfExtents HumanHalf, float VoxelSize, int Subdivisions, bool FourLegged);
}
