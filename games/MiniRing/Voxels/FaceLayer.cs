using System.Numerics;
using Aigis.Foundation.Voxels;

namespace MiniRing.Voxels;

/// <summary>
/// 頭の顔の層（F2、2026-11-19）: 頭の剛体に重ねる 2 つ目の格子（頭の箱と同じ箱を、5 mm の格子の 2 倍の細かさ = 約 2.5 mm で分けたもの）。
/// 頭蓋骨の外（頭皮・顔の肉・目・鼻・唇・耳・髪・ひげ）は顔の層だけにあり、5 mm の頭の格子は頭蓋骨・脳・首の中（頸椎・脊髄・首の筋肉）だけを持つ
/// （同じものを 2 つの格子に持たない: CLAUDE.md §1.5）。顔の形は <see cref="FaceComposer"/> がキャラの <see cref="FaceSpec"/> から組み立てる。
/// <list type="bullet">
/// <item>5 mm の格子に残すセル: 脳・脊髄、首の段（<see cref="BoxerSpine.NeckSlices"/> の上の端まで）の筋肉と骨、頭蓋骨。ただし頭蓋骨のうち、
/// 組み立てた顔の目（白目・瞳）と重なる所（眼窩）と、脳に面していない（脳を包む殻ではない）もので組み立てた顔の外に出る所は抜く（顔の形に合わせる）。
/// 脳は 1 セルも動かさない（意識のモデルの量が変わらない）。</item>
/// <item>顔の層 = 組み立てた顔 ∪ 残したセルを 1 セル（約 2.5 mm）包む皮膚 − 残したセルと重なる所。組み立てた顔が頭蓋骨より狭い所でも、骨はいつも皮膚の下にある。</item>
/// <item>材質: 髪・眉 → 髪、白目 → 目、瞳 → 黒目、唇 → 唇、鼻の穴 → 空（空気）、それ以外（皮膚・影・そばかす・無精ひげ…）→ 皮膚。強さ・密度は 5 mm の同じ材質と同じ。</item>
/// </list>
/// </summary>
public static class FaceLayer
{
    /// <summary>顔の層のセルが、5 mm の頭の格子の 1 セルを各軸で何個に分けるか（5 mm → 2.5 mm）。</summary>
    public const int Subdivision = 2;

    /// <summary>顔の層の 1 セルの体積の、5 mm の頭の格子の 1 セルに対する比（質量と、削る量の物差し）。</summary>
    public const float CellVolumeRatio = 1f / (Subdivision * Subdivision * Subdivision);

    /// <summary>
    /// 作った頭の格子 <paramref name="head"/>（5 mm、顔の色の表）を、頭蓋骨の中（<c>Inner</c>: 同じ箱・同じ細かさ）と、顔の層（<c>Face</c>: 同じ箱を
    /// <see cref="Subdivision"/> 倍の細かさで、<paramref name="spec"/> の顔を組み立てて頭蓋骨の外だけを置いたもの）に分ける。色の表は <paramref name="head"/> のもの。
    /// </summary>
    public static (VoxelGrid Inner, VoxelGrid Face) Split(VoxelGrid head, FaceSpec spec, FacePartLibrary? library = null)
    {
        ArgumentNullException.ThrowIfNull(head);
        ArgumentNullException.ThrowIfNull(spec);
        var (hx, hy, hz) = head.Dimensions;
        var (nx, ny, nz) = (hx * Subdivision, hy * Subdivision, hz * Subdivision);
        var composed = FaceComposer.Compose(spec, nx, ny, nz, library);
        int HeadIndex(int x, int y, int z) => x + (hx * (y + (hy * z)));
        int Index(int x, int y, int z) => x + (nx * (y + (ny * z)));

        // 1. 5 mm の格子に残すセル。
        var keep = new bool[hx * hy * hz];
        for (var z = 0; z < hz; z++)
        {
            for (var y = 0; y < hy; y++)
            {
                for (var x = 0; x < hx; x++)
                {
                    keep[HeadIndex(x, y, z)] = Keeps(head, composed, x, y, z);
                }
            }
        }

        var inner = new VoxelGrid(head.Palette, head.HalfExtents, head.Dimensions, (p, _) =>
        {
            var (x, y, z) = CellOf(p, head.HalfExtents, head.CellSize, hx, hy, hz);
            return keep[HeadIndex(x, y, z)] ? head.At(x, y, z) : (byte)0;
        });

        // 2. 顔の層: 組み立てた顔と、残したセルを包む 1 層の皮膚から、残したセルの所を除く。
        bool Kept(int x, int y, int z) => keep[HeadIndex(x / Subdivision, y / Subdivision, z / Subdivision)];
        bool TouchesKept(int x, int y, int z)
        {
            for (var dz = -1; dz <= 1; dz++)
            {
                for (var dy = -1; dy <= 1; dy++)
                {
                    for (var dx = -1; dx <= 1; dx++)
                    {
                        var (ax, ay, az) = (x + dx, y + dy, z + dz);
                        if (ax >= 0 && ay >= 0 && az >= 0 && ax < nx && ay < ny && az < nz && Kept(ax, ay, az))
                        {
                            return true;
                        }
                    }
                }
            }

            return false;
        }

        var cells = new byte[nx * ny * nz];
        for (var z = 0; z < nz; z++)
        {
            for (var y = 0; y < ny; y++)
            {
                for (var x = 0; x < nx; x++)
                {
                    if (Kept(x, y, z))
                    {
                        continue; // 頭蓋骨・脳・首の中は 5 mm の格子だけが持つ
                    }

                    var shaped = composed.At(x, y, z);
                    var material = MaterialOf(shaped);
                    if (material == 0 && shaped != FaceMaterial.Nostril && TouchesKept(x, y, z))
                    {
                        material = BoxerAnatomy.Skin; // 組み立てた顔の外に出た骨・首を包む 1 層の皮膚
                    }

                    cells[Index(x, y, z)] = material;
                }
            }
        }

        // 3. 皮膚の下は肉: 顔の層の皮膚のうち、6 方向のどれも空でない（顔の層か頭蓋骨の中のセルに囲まれた）ものは筋肉（作った頭と同じ、外の 1 層だけが皮膚）。
        bool Filled(int x, int y, int z) => x >= 0 && y >= 0 && z >= 0 && x < nx && y < ny && z < nz && (cells[Index(x, y, z)] != 0 || Kept(x, y, z));
        var flesh = new List<int>();
        for (var z = 0; z < nz; z++)
        {
            for (var y = 0; y < ny; y++)
            {
                for (var x = 0; x < nx; x++)
                {
                    if (cells[Index(x, y, z)] == BoxerAnatomy.Skin && Neighbours.All(n => Filled(x + n.X, y + n.Y, z + n.Z)))
                    {
                        flesh.Add(Index(x, y, z));
                    }
                }
            }
        }

        foreach (var i in flesh)
        {
            cells[i] = BoxerAnatomy.Muscle;
        }

        var faceCell = 2f * head.HalfExtents / new Vector3(nx, ny, nz);
        var face = new VoxelGrid(head.Palette, head.HalfExtents, (nx, ny, nz), (p, _) =>
        {
            var (x, y, z) = CellOf(p, head.HalfExtents, faceCell, nx, ny, nz);
            return cells[Index(x, y, z)];
        });
        return (inner, face);
    }

    /// <summary>
    /// 5 mm の頭の格子のセル (<paramref name="x"/>, <paramref name="y"/>, <paramref name="z"/>) を、その格子に残すか（頭蓋骨の中か）。
    /// 脳・脊髄・首の段の筋肉・頸椎は残す。ほかの骨は、組み立てた顔の目と重なる所（眼窩）を抜き、脳に面していない（脳を包む殻でない）もので
    /// 組み立てた顔の外に出る所（8 つの細かいセルの半分より多くが顔の外）も抜く。それ以外（皮膚・髪・目・唇・顔の筋肉）は顔の層へ。
    /// </summary>
    private static bool Keeps(VoxelGrid head, FaceVolume composed, int x, int y, int z)
    {
        var material = head.At(x, y, z);
        if (material is BoxerAnatomy.Brain or BoxerAnatomy.Cord)
        {
            return true;
        }

        if (material == BoxerAnatomy.Muscle)
        {
            return y <= BoxerSpine.NeckSlices.High; // 首の中の筋肉（頸椎を包む）。顔の筋肉は顔の層の肉になる
        }

        if (material != BoxerAnatomy.Bone)
        {
            return false;
        }

        if (y <= BoxerSpine.NeckSlices.High)
        {
            return true; // 頸椎
        }

        var outside = 0;
        for (var dz = 0; dz < Subdivision; dz++)
        {
            for (var dy = 0; dy < Subdivision; dy++)
            {
                for (var dx = 0; dx < Subdivision; dx++)
                {
                    var m = composed.At((x * Subdivision) + dx, (y * Subdivision) + dy, (z * Subdivision) + dz);
                    if (m is FaceMaterial.White or FaceMaterial.Iris)
                    {
                        return false; // 眼窩: 目の入る所の骨は無い（作った頭と同じく、目の奥はすぐ脳）
                    }

                    outside += m is FaceMaterial.Empty or FaceMaterial.Nostril ? 1 : 0;
                }
            }
        }

        return FacesBrain(head, x, y, z) || outside * 2 <= Subdivision * Subdivision * Subdivision; // 脳を包む殻は顔の外へ出ても残す（皮膚が包む）
    }

    private static bool FacesBrain(VoxelGrid head, int x, int y, int z)
    {
        var (hx, hy, hz) = head.Dimensions;
        foreach (var (dx, dy, dz) in Neighbours)
        {
            var (ax, ay, az) = (x + dx, y + dy, z + dz);
            if (ax >= 0 && ay >= 0 && az >= 0 && ax < hx && ay < hy && az < hz && head.At(ax, ay, az) == BoxerAnatomy.Brain)
            {
                return true;
            }
        }

        return false;
    }

    private static readonly (int X, int Y, int Z)[] Neighbours = [(1, 0, 0), (-1, 0, 0), (0, 1, 0), (0, -1, 0), (0, 0, 1), (0, 0, -1)];

    /// <summary>
    /// 組み立てた顔の材質 → 体の材質（<see cref="BoxerAnatomy"/>）。空・鼻の穴は空気（空）。ここに無い材質（影・そばかす・無精ひげなど、皮膚の上の
    /// 色の違いだけの物。部品の表に後から足した物を含む）は皮膚（強さ・密度は皮膚、色の違いは今は描かない: docs/DEBT.md「顔の層」）。
    /// </summary>
    public static byte MaterialOf(FaceMaterial material) => material switch
    {
        FaceMaterial.Empty or FaceMaterial.Nostril => 0,
        FaceMaterial.Hair or FaceMaterial.Brow => BoxerAnatomy.Hair,
        FaceMaterial.White => BoxerAnatomy.Eye,
        FaceMaterial.Iris => BoxerAnatomy.Pupil,
        FaceMaterial.Lip => BoxerAnatomy.Lip,
        _ => BoxerAnatomy.Skin,
    };

    private static (int X, int Y, int Z) CellOf(Vector3 p, Vector3 half, Vector3 cell, int nx, int ny, int nz)
    {
        var c = (p + half) / cell;
        return (Math.Clamp((int)c.X, 0, nx - 1), Math.Clamp((int)c.Y, 0, ny - 1), Math.Clamp((int)c.Z, 0, nz - 1));
    }
}
