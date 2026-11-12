using System.Numerics;
using Aigis.Foundation.Voxels;

namespace MiniRing.Voxels;

/// <summary>髪型。</summary>
public enum Hairstyle
{
    /// <summary>作ったまま（頭の上と後ろ）。</summary>
    Full,

    /// <summary>坊主（髪なし）。</summary>
    Bald,

    /// <summary>短く刈った頭頂だけ。</summary>
    Crop,

    /// <summary>モヒカン（真ん中の帯だけ）。</summary>
    Mohawk,

    /// <summary>大きく盛った髪（横まで）。</summary>
    Afro,
}

/// <summary>
/// ボットごとの顔（2026-11-12）: 肌の色・髪の色・髪型・眉・ひげ・瞳の色。リングネーム（<see cref="Bots.BotNames"/>）から決まる
/// （同じボットはいつも同じ顔）。体の形・材質の強さは変えない（髪は皮膚と同じ強さ・密度なので、試合の物理は変わらない）。見た目だけ。
/// </summary>
public sealed record FaceLook(Vector3 Skin, Vector3 Hair, Vector3 Pupil, Hairstyle Style, bool Brows, bool Beard)
{
    private static readonly Vector3[] Skins =
    [
        new(0.96f, 0.78f, 0.64f), new(0.86f, 0.6f, 0.42f), new(0.74f, 0.5f, 0.33f),
        new(0.58f, 0.38f, 0.24f), new(0.42f, 0.27f, 0.17f), new(0.3f, 0.19f, 0.12f),
    ];

    private static readonly Vector3[] Hairs =
    [
        new(0.06f, 0.05f, 0.05f), new(0.13f, 0.08f, 0.05f), new(0.35f, 0.2f, 0.1f),
        new(0.75f, 0.55f, 0.25f), new(0.6f, 0.15f, 0.08f), new(0.85f, 0.85f, 0.85f), new(0.1f, 0.35f, 0.85f),
    ];

    private static readonly Vector3[] Pupils = [new(0.02f, 0.02f, 0.03f), new(0.15f, 0.08f, 0.03f), new(0.05f, 0.25f, 0.45f), new(0.1f, 0.35f, 0.15f)];

    /// <summary>名前（リングネーム）から決まる顔。</summary>
    public static FaceLook For(string identity)
    {
        var h = Bots.BotNames.Hash(identity);
        uint Next(uint n)
        {
            var v = h % n;
            h = (h / n) ^ (h * 2654435761u);
            return v;
        }

        return new FaceLook(Skins[Next((uint)Skins.Length)], Hairs[Next((uint)Hairs.Length)], Pupils[Next((uint)Pupils.Length)],
            (Hairstyle)Next((uint)Enum.GetValues<Hairstyle>().Length), Brows: Next(3) != 0, Beard: Next(3) == 0);
    }

    /// <summary>この顔の色の表（肌・髪・瞳だけ替える。強さ・密度は元のまま）。</summary>
    public VoxelPalette Palette(VoxelPalette basePalette)
    {
        ArgumentNullException.ThrowIfNull(basePalette);
        var materials = new List<VoxelMaterial>();
        for (var m = 0; m < basePalette.Count; m++)
        {
            var material = basePalette[(byte)m];
            Vector3? color = m switch
            {
                BoxerAnatomy.Skin => Skin,
                BoxerAnatomy.Hair => Hair,
                BoxerAnatomy.Pupil => Pupil,
                _ => null,
            };
            materials.Add(color is { } c ? material with { Color = new Vector4(c, 1f), SurfaceColor = new Vector4(c, 1f) } : material);
        }

        return new VoxelPalette(materials);
    }

    /// <summary>
    /// 部位の格子をこの顔に作り直す: 色の表を替え、頭なら髪型・眉・ひげを置き直す（髪と皮膚の入れ替えだけ）。
    /// 作った直後（削れる前）に呼ぶ。
    /// </summary>
    public VoxelGrid Restyle(BodyPartKind kind, VoxelGrid grid, VoxelPalette palette)
    {
        ArgumentNullException.ThrowIfNull(grid);
        var (nx, ny, nz) = grid.Dimensions;
        var cell = grid.CellSize;
        // 頭の外形（中身の詰まったセルの範囲）を −1..1 とする: 体を作るとき（BoxerAnatomy.Build）はメッシュの外形で正規化しているので、
        // 格子の箱で割ると、箱がメッシュより大きいぶん眉・髪の帯がずれる（レビューの指摘）。
        var (lo, hi) = (new Vector3(float.MaxValue), new Vector3(float.MinValue));
        if (kind == BodyPartKind.Head)
        {
            for (var iz = 0; iz < nz; iz++)
            {
                for (var iy = 0; iy < ny; iy++)
                {
                    for (var ix = 0; ix < nx; ix++)
                    {
                        if (grid.At(ix, iy, iz) != 0)
                        {
                            lo = Vector3.Min(lo, new Vector3(ix, iy, iz));
                            hi = Vector3.Max(hi, new Vector3(ix + 1, iy + 1, iz + 1));
                        }
                    }
                }
            }
        }

        var center = (lo + hi) * 0.5f;
        var half = Vector3.Max((hi - lo) * 0.5f, Vector3.One);
        return new VoxelGrid(palette, grid.HalfExtents, grid.Dimensions, (p, _) =>
        {
            var c = (p + grid.HalfExtents) / cell;
            var (x, y, z) = (Math.Clamp((int)c.X, 0, nx - 1), Math.Clamp((int)c.Y, 0, ny - 1), Math.Clamp((int)c.Z, 0, nz - 1));
            var material = grid.At(x, y, z);
            if (kind != BodyPartKind.Head || material is not (BoxerAnatomy.Skin or BoxerAnatomy.Hair))
            {
                return material;
            }

            // 頭の外形を −1..1 とした位置（前は +Z、上は +Y）。表のセルかどうか: 外側の隣が空。
            var n = (new Vector3(x + 0.5f, y + 0.5f, z + 0.5f) - center) / half;
            var surface = grid.IsExposed(x, y, z);
            return HairOrSkin(material == BoxerAnatomy.Hair, surface, n);
        });
    }

    private byte HairOrSkin(bool wasHair, bool surface, Vector3 n)
    {
        var face = n.Z > 0.45f;
        var hair = Style switch
        {
            Hairstyle.Full => wasHair,
            Hairstyle.Bald => false,
            Hairstyle.Crop => wasHair && n.Y > 0.45f,
            Hairstyle.Mohawk => wasHair && n.Y > 0.2f && MathF.Abs(n.X) < 0.22f,
            Hairstyle.Afro => wasHair || (surface && n.Y > 0.05f && !face),
            _ => wasHair,
        };
        // 眉: 目（高さ 0.18、横 ±0.38）の少し上の、顔の表の帯。ひげ: あごと頬の下の表。
        var brow = Brows && surface && n.Z > 0.6f && n.Y is > 0.3f and < 0.4f && MathF.Abs(n.X) is > 0.15f and < 0.62f;
        var beard = Beard && surface && n.Z > 0.2f && n.Y < -0.42f;
        return hair || brow || beard ? BoxerAnatomy.Hair : BoxerAnatomy.Skin;
    }
}
