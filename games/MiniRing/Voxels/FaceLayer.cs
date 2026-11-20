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
/// <item>材質: 髪・眉 → 髪、白目 → 目、瞳 → 黒目、唇 → 唇、それ以外（皮膚・影・鼻の穴・そばかす・無精ひげ…）→ 皮膚。強さ・密度は 5 mm の同じ材質と同じ。色の違いは <see cref="FaceCellColors"/>（見た目だけ）。</item>
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
        var built = Build(head, spec, library);
        return (built.Inner, built.Face);
    }

    /// <summary>
    /// <see cref="Split"/> と、顔の層の見た目の色（<see cref="FaceCellColors"/>: 組み立てた顔の材質ごとの色 <see cref="FacePalette"/>。影・眉・唇・鼻の穴の縁・
    /// そばかす・無精ひげ・白目の色の違いは、体の材質（皮膚・髪・目…）を変えずに色だけで描く）。
    /// </summary>
    public static (VoxelGrid Inner, VoxelGrid Face, FaceCellColors Colors) Build(VoxelGrid head, FaceSpec spec, FacePartLibrary? library = null)
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
                    if (material == 0 && TouchesKept(x, y, z))
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

        // 4. 見た目の色: 表に見えるセル（筋肉でない）の色を、組み立てた顔の材質の色（FacePalette）にする。包む皮膚（組み立てた顔の外）は肌の色。
        var colorsOf = FacePalette.Resolve(spec);
        var colors = new Vector4[cells.Length];
        for (var i = 0; i < cells.Length; i++)
        {
            if (cells[i] is 0 or BoxerAnatomy.Muscle)
            {
                continue;
            }

            var shaped = composed.Cells[i];
            colors[i] = new Vector4(colorsOf.TryGetValue(shaped, out var c) ? c : colorsOf[FaceMaterial.Skin], 1f);
        }

        return (inner, face, new FaceCellColors(colors, (byte[])cells.Clone()));
    }

    /// <summary>
    /// 顔 <paramref name="spec"/> の肌・髪・瞳の色（あれば）で、体の見た目 <paramref name="look"/> の色を置き換える（顔と首・体の肌の色をそろえる。見た目だけ）。
    /// </summary>
    public static FaceLook LookWith(FaceLook look, FaceSpec spec)
    {
        ArgumentNullException.ThrowIfNull(look);
        ArgumentNullException.ThrowIfNull(spec);
        return look with { Skin = spec.Skin ?? look.Skin, Hair = spec.Hair ?? look.Hair, Pupil = spec.Iris ?? look.Pupil };
    }

    /// <summary>
    /// 5 mm の頭の格子のセル (<paramref name="x"/>, <paramref name="y"/>, <paramref name="z"/>) を、その格子に残すか（頭蓋骨の中か）。
    /// 脳・脊髄・首の段の筋肉・頸椎は残す。ほかの骨は、組み立てた顔の目と重なる所（眼窩）を抜き、
    /// 組み立てた顔の外に出る所（8 つの細かいセルの半分より多くが顔の外）も抜く。それ以外（皮膚・髪・目・唇・顔の筋肉）は顔の層へ。
    /// </summary>
    private static bool Keeps(VoxelGrid head, FaceVolume composed, int x, int y, int z)
    {
        var material = head.At(x, y, z);
        if (material is BoxerAnatomy.Brain or BoxerAnatomy.Cord)
        {
            return true;
        }

        // 首の段: 首は顔ではない。組み立てた顔（あご・ひげ・唇）と重ならない所は、皮膚・筋肉・頸椎とも 5 mm の格子に残す
        // （首が折れたとき、頭をつなぐ肉の橋は 5 mm の格子の肉で数える: BoxerBody.BreakAt）。
        if (y <= BoxerSpine.NeckSlices.High)
        {
            return material is BoxerAnatomy.Bone or BoxerAnatomy.Muscle
                || (material == BoxerAnatomy.Skin && Covered(composed, x, y, z) * 2 <= Subdivision * Subdivision * Subdivision && !InFrontOfFace(composed, x, y, z));
        }

        if (material != BoxerAnatomy.Bone)
        {
            return false; // 頭の皮膚・顔の筋肉・目・髪・唇は顔の層へ
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

        // 顔の外へ半分以上出る骨は、脳を包む殻でも残さない: 残すと、それを包む皮膚が組み立てた顔の前へ出っ張った（目の間の鼻の付け根、2026-11-20）。
        // その奥の脳は組み立てた顔が覆う（FaceLayerTests が、脳のセルが顔の層か骨に覆われていることを確かめる）。
        return outside * 2 < Subdivision * Subdivision * Subdivision;
    }

    /// <summary>
    /// 5 mm のセルが、同じ高さ・左右の位置で組み立てた顔の前の面より前にあるか（首の段の皮膚があご先の前へ出て、包む皮膚があごの下の出っ張りになった。2026-11-20）。
    /// その列に組み立てた顔が無い（あごより下の首）なら前ではない。
    /// </summary>
    private static bool InFrontOfFace(FaceVolume composed, int x, int y, int z)
    {
        for (var dy = 0; dy < Subdivision; dy++)
        {
            for (var dx = 0; dx < Subdivision; dx++)
            {
                var (fx, fy) = ((x * Subdivision) + dx, (y * Subdivision) + dy);
                for (var fz = composed.Nz - 1; fz >= 0; fz--)
                {
                    if (composed.At(fx, fy, fz) is not (FaceMaterial.Empty or FaceMaterial.Nostril))
                    {
                        if (z * Subdivision > fz)
                        {
                            return true;
                        }

                        break;
                    }
                }
            }
        }

        return false;
    }

    /// <summary>5 mm のセルの中の、組み立てた顔の埋まった細かいセルの数（0..8）。</summary>
    private static int Covered(FaceVolume composed, int x, int y, int z)
    {
        var count = 0;
        for (var dz = 0; dz < Subdivision; dz++)
        {
            for (var dy = 0; dy < Subdivision; dy++)
            {
                for (var dx = 0; dx < Subdivision; dx++)
                {
                    count += composed.At((x * Subdivision) + dx, (y * Subdivision) + dy, (z * Subdivision) + dz) is FaceMaterial.Empty or FaceMaterial.Nostril ? 0 : 1;
                }
            }
        }

        return count;
    }

    private static readonly (int X, int Y, int Z)[] Neighbours = [(1, 0, 0), (-1, 0, 0), (0, 1, 0), (0, -1, 0), (0, 0, 1), (0, 0, -1)];

    /// <summary>
    /// 組み立てた顔の材質 → 体の材質（<see cref="BoxerAnatomy"/>）。空は空気。鼻の穴は皮膚（暗い色だけ: 2.5 mm の穴にすると奥の鼻の骨が見えた）。ここに無い材質（影・そばかす・無精ひげなど、皮膚の上の
    /// 色の違いだけの物。部品の表に後から足した物を含む）は皮膚（強さ・密度は皮膚、色の違いは今は描かない: docs/DEBT.md「顔の層」）。
    /// </summary>
    public static byte MaterialOf(FaceMaterial material) => material switch
    {
        FaceMaterial.Empty => 0,
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

/// <summary>
/// 顔の層のセルごとの見た目の色（見た目だけ。試合の計算・チェックサムには使わない）。<see cref="Color"/> の W が 0 のセルは材質の色のまま。
/// 作ったときの材質（<see cref="Material"/>）と今の材質が違うセル（削れて押し込まれた所）も材質の色。
/// </summary>
public sealed record FaceCellColors(Vector4[] Color, byte[] Material)
{
    /// <summary>セル <paramref name="index"/>（材質 <paramref name="material"/>）の色。決まった色が無ければ null。</summary>
    public Vector4? At(int index, byte material) => index >= 0 && index < Color.Length && Color[index].W > 0f && Material[index] == material ? Color[index] : null;
}
