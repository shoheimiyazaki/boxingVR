using System.Collections.Concurrent;
using System.Numerics;
using System.Runtime.CompilerServices;
using Aigis.Foundation.Voxels;
using MiniRing.LowPoly;

namespace MiniRing.Voxels;

/// <summary>
/// 人形の体の中身（ボクセルの材質の並び）。部位の箱ごとに、皮膚と肉・骨・臓器を置く。
/// <para>
/// 座標は部位のローカル（箱の中心が原点。<c>n</c> は箱の半径で割った −1..1）。体の前は +Z、本人の左は +X
/// （<see cref="HandExtensions.LocalSide"/> と同じ）。腕は長さが X、脚は Y、足は Z に沿う（HumanoidRagdoll の決まり）。
/// </para>
/// <list type="bullet">
/// <item>頭: 頭蓋（殻）の中に脳。顔の前に目。</item>
/// <item>胸: 背骨・肋骨（帯）・胸骨の籠の中に、心臓（左寄り）・肺（左右）・肝臓（右下）。</item>
/// <item>腰: 骨盤の環と背骨、腸。</item>
/// <item>腕・脚: 長さに沿った骨の芯、周りは肉。足は骨の板。</item>
/// </list>
/// 外側の 1 層は皮膚。目も臓器の一種（顔に出ている）。目を除く臓器はどれも致命的（<see cref="VoxelMaterial.Vital"/>）:
/// 半分以上壊れると死ぬ（<see cref="BoxerBody"/>）。
/// </summary>
public static class BoxerAnatomy
{
    public const byte Skin = 1;
    public const byte Muscle = 2;
    public const byte Bone = 3;
    public const byte Brain = 4;
    public const byte Heart = 5;
    public const byte Lung = 6;
    public const byte Liver = 7;
    public const byte Gut = 8;
    public const byte Eye = 9;

    private static Vector4 Rgb(float r, float g, float b) => new(r, g, b, 1f);

    /// <summary>
    /// 材質の表。色は 1 つずつ（皮膚・肉・骨・脳・臓器ごと・目）: 削れて出てきた中身がその色で見える。
    /// 目だけは、表に出ている面（黒目）と中（白い眼球）で色が違う。どちらのファイターかはグローブの色で見分ける。
    /// </summary>
    public static VoxelPalette Palette { get; } = new(
    [
        new VoxelMaterial("empty", VoxelTissue.Empty, 1f, false, Vector4.Zero, Vector4.Zero),
        new VoxelMaterial("skin", VoxelTissue.Flesh, 0.7f, false, Rgb(0.93f, 0.8f, 0.68f), Rgb(0.93f, 0.8f, 0.68f)),
        new VoxelMaterial("muscle", VoxelTissue.Flesh, 1f, false, Rgb(0.7f, 0.07f, 0.09f), Rgb(0.7f, 0.07f, 0.09f)),
        new VoxelMaterial("bone", VoxelTissue.Bone, 8f, false, Rgb(0.96f, 0.93f, 0.8f), Rgb(0.96f, 0.93f, 0.8f)),
        new VoxelMaterial("brain", VoxelTissue.Organ, 1.4f, true, Rgb(0.98f, 0.55f, 0.72f), Rgb(0.98f, 0.55f, 0.72f)),
        new VoxelMaterial("heart", VoxelTissue.Organ, 1.4f, true, Rgb(0.5f, 0.0f, 0.1f), Rgb(0.5f, 0.0f, 0.1f)),
        new VoxelMaterial("lung", VoxelTissue.Organ, 1.2f, true, Rgb(0.95f, 0.42f, 0.45f), Rgb(0.95f, 0.42f, 0.45f)),
        new VoxelMaterial("liver", VoxelTissue.Organ, 1.4f, true, Rgb(0.4f, 0.12f, 0.06f), Rgb(0.4f, 0.12f, 0.06f)),
        new VoxelMaterial("gut", VoxelTissue.Organ, 1.2f, true, Rgb(0.82f, 0.6f, 0.35f), Rgb(0.82f, 0.6f, 0.35f)),
        new VoxelMaterial("eye", VoxelTissue.Organ, 0.8f, false, Rgb(0.97f, 0.97f, 1f), Rgb(0.06f, 0.12f, 0.3f)),
    ]);

    /// <summary>
    /// 部位 <paramref name="kind"/> の箱を、<paramref name="cellSize"/> [m] ほどのボクセルで埋める。<paramref name="shape"/>
    /// （部位のローカルの閉じたメッシュ、ローポリの体）を渡すと、その内側だけを埋める（外は空）。
    /// </summary>
    public static VoxelGrid Build(BodyPartKind kind, Vector3 halfExtents, float cellSize, LowPolyMesh? shape = null)
    {
        var dims = VoxelGrid.CellsFor(halfExtents, cellSize);
        var inside = shape is null ? null : InsideMask(shape, halfExtents, dims);
        var cell = 2f * halfExtents / new Vector3(dims.X, dims.Y, dims.Z);
        // 骨・臓器の配置は「体の外形」を −1..1 とした座標で決める。形を渡されたらその外形（頂点の範囲）、無ければ箱。
        // 箱のままだと、箱より細いメッシュの腕がほとんど骨になる。
        var (center, half) = shape is null ? (Vector3.Zero, halfExtents) : Bounds(shape);
        var grid = new VoxelGrid(Palette, halfExtents, dims, (p, _) =>
        {
            var n = (p - center) / half;
            if (inside is not null)
            {
                var c = (p + halfExtents) / cell;
                var i = (int)c.X + (dims.X * ((int)c.Y + (dims.Y * (int)c.Z)));
                if (!inside[i])
                {
                    return 0;
                }
            }

            return Fill(kind, n);
        });

        // 外側の 1 層は皮膚（目だけは顔に出たまま）。
        grid.Remap((_, material, surface) => surface && material != Eye ? Skin : material);
        return grid;
    }

    /// <summary>
    /// どのセルの中心がメッシュの内側か。メッシュ × 格子の大きさごとに 1 度だけ数える（ラウンドごと・両ファイターで同じ）。
    /// 対戦場は試合を並列に回すので、スレッドをまたいで安全な表に置く。
    /// </summary>
    private static bool[] InsideMask(LowPolyMesh shape, Vector3 halfExtents, (int X, int Y, int Z) dims)
    {
        var perMesh = Masks.GetValue(shape, _ => new ConcurrentDictionary<(Vector3, (int, int, int)), bool[]>());
        return perMesh.GetOrAdd((halfExtents, dims), key =>
        {
            var (half, (nx, ny, nz)) = key;
            var cell = 2f * half / new Vector3(nx, ny, nz);
            var mask = new bool[nx * ny * nz];
            for (var z = 0; z < nz; z++)
            {
                for (var y = 0; y < ny; y++)
                {
                    for (var x = 0; x < nx; x++)
                    {
                        mask[x + (nx * (y + (ny * z)))] = shape.Contains(-half + (new Vector3(x + 0.5f, y + 0.5f, z + 0.5f) * cell));
                    }
                }
            }

            return mask;
        });
    }

    private static (Vector3 Center, Vector3 Half) Bounds(LowPolyMesh mesh)
    {
        var min = new Vector3(float.MaxValue);
        var max = new Vector3(float.MinValue);
        foreach (var v in mesh.Vertices)
        {
            min = Vector3.Min(min, v);
            max = Vector3.Max(max, v);
        }

        return ((min + max) * 0.5f, Vector3.Max((max - min) * 0.5f, new Vector3(1e-4f)));
    }

    private static readonly ConditionalWeakTable<LowPolyMesh, ConcurrentDictionary<(Vector3, (int, int, int)), bool[]>> Masks = new();

    /// <summary>正規化座標 <paramref name="n"/>（−1..1）の材質。</summary>
    public static byte Fill(BodyPartKind kind, Vector3 n) => kind switch
    {
        BodyPartKind.Head => HeadAt(n),
        BodyPartKind.Chest => ChestAt(n),
        BodyPartKind.Pelvis => PelvisAt(n),
        BodyPartKind.UpperArm or BodyPartKind.Forearm => Rod(n.Y, n.Z, 0.42f),
        BodyPartKind.Thigh or BodyPartKind.Shin => Rod(n.X, n.Z, 0.4f),
        BodyPartKind.Foot => MathF.Abs(n.Y) < 0.45f && MathF.Abs(n.X) < 0.55f && MathF.Abs(n.Z) < 0.85f ? Bone : Muscle,
        _ => Muscle,
    };

    private static byte HeadAt(Vector3 n)
    {
        // 目: 顔の前面、左右に 1 つずつ。
        if (n.Z > 0.55f && MathF.Abs(MathF.Abs(n.X) - 0.38f) < 0.2f && MathF.Abs(n.Y - 0.18f) < 0.18f)
        {
            return Eye;
        }

        var r = Ellipsoid(n, new Vector3(0f, 0.08f, -0.05f), new Vector3(0.85f, 0.85f, 0.85f));
        return r < 0.74f && n.Y > -0.45f ? Brain
            : r < 0.9f && n.Y > -0.6f ? Bone // 頭蓋
            : n.Y < -0.55f && MathF.Abs(n.X) < 0.6f && n.Z > -0.2f && n.Z < 0.7f && MathF.Abs(n.Y + 0.75f) < 0.12f ? Bone // あご
            : Muscle;
    }

    private static byte ChestAt(Vector3 n)
    {
        // 背骨: 背中側の中央の柱。
        if (MathF.Abs(n.X) < 0.16f && n.Z < -0.5f && n.Z > -0.9f)
        {
            return Bone;
        }

        // 心臓（本人の左 = +X）、肺（左右）、肝臓（本人の右 = −X の下）。
        if (Ellipsoid(n, new Vector3(0.22f, 0.05f, 0.15f), new Vector3(0.3f, 0.3f, 0.45f)) < 1f)
        {
            return Heart;
        }

        if (Ellipsoid(n, new Vector3(-0.42f, -0.62f, 0.05f), new Vector3(0.38f, 0.26f, 0.5f)) < 1f)
        {
            return Liver;
        }

        var lung = MathF.Min(
            Ellipsoid(n, new Vector3(0.45f, 0.25f, -0.05f), new Vector3(0.32f, 0.5f, 0.5f)),
            Ellipsoid(n, new Vector3(-0.45f, 0.25f, -0.05f), new Vector3(0.32f, 0.5f, 0.5f)));
        if (lung < 1f)
        {
            return Lung;
        }

        // 肋骨: 胴を囲む殻を、上下に帯状に（帯の間は肉）。前の中央は胸骨。
        var ring = Ellipsoid(n, Vector3.Zero, new Vector3(0.82f, 1f, 0.8f));
        var band = (int)MathF.Floor((n.Y + 1f) * 4.5f) % 2 == 0;
        if (ring > 0.82f && ring < 1.02f && n.Y > -0.75f && n.Y < 0.85f && (band || (n.Z > 0.6f && MathF.Abs(n.X) < 0.15f)))
        {
            return Bone;
        }

        return Muscle;
    }

    private static byte PelvisAt(Vector3 n)
    {
        if (MathF.Abs(n.X) < 0.16f && n.Z < -0.5f && n.Z > -0.9f)
        {
            return Bone; // 背骨
        }

        if (Ellipsoid(n, new Vector3(0f, 0.1f, 0.2f), new Vector3(0.6f, 0.55f, 0.55f)) < 1f)
        {
            return Gut;
        }

        var ring = Ellipsoid(n, new Vector3(0f, -0.1f, 0f), new Vector3(0.85f, 0.9f, 0.8f));
        return ring > 0.78f && ring < 1.0f && n.Y < 0.3f ? Bone : Muscle; // 骨盤の環
    }

    private static byte Rod(float a, float b, float radius) => (a * a) + (b * b) < radius * radius ? Bone : Muscle;

    private static float Ellipsoid(Vector3 n, Vector3 center, Vector3 radii)
    {
        var d = (n - center) / radii;
        return d.Length();
    }
}
