using System.Numerics;
using Aigis.Foundation;
using Aigis.Foundation.Voxels;
using MiniRing.Bots;

namespace MiniRing.Arena;

/// <summary>
/// 全キャラの顔を並べた画像（1 人 1 行: 正面・横・上・斜め 40 度）。試合の体の頭（頭蓋骨の中の 5 mm の格子と、その上に重ねた顔の層の 2.5 mm の格子:
/// <see cref="MiniRing.Voxels.FaceLayer"/>）を正射影で描く（描画エンジンは使わない: どの機械でも同じ絵になり、試合の体のボクセルそのものを見られる）。
/// キャラの並びは標準出力に出す（上から下へ）。
/// </summary>
internal static class FaceSheet
{
    private const float PixelsPerMeter = 4000f; // 5 mm のセルが 20 px
    private const int Tile = 360;               // 1 枚の大きさ（90 mm 四方）
    private const int Background = 0x2b2f38;

    /// <summary>描く向き: 正面・横（本人の左から）・上（顔の前が下）・斜め 40 度。</summary>
    private static readonly Matrix4x4[] Views =
    [
        Matrix4x4.Identity,
        Matrix4x4.CreateRotationY(-MathF.PI / 2f),
        Matrix4x4.CreateRotationX(MathF.PI / 2f),
        Matrix4x4.CreateRotationY(40f * MathF.PI / 180f),
    ];

    /// <summary>名簿のキャラ（体格・顔の値はキャラのファイル）を 1 人 1 行で描く。</summary>
    public static int RunRoster(string path, IReadOnlyList<MiniRing.Characters.FighterCharacter> characters)
        => Draw(path, characters.Select(c => (c.Id, (Func<BoxingGame>)(() => new BoxingGame(seed: 1, corners: new BoxingCorners(c.Id, s => BotCatalog.Create("champion", s),
            CpuBody: c.Build, CpuFace: c.Look.Parts, CpuLook: c.Look, CpuFaceSpec: c.Face))))).ToList());

    public static int Run(string path, IReadOnlyList<Contender> contenders, int columns)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(columns, 1);
        return Draw(path, contenders.Select(c => (c.Name, (Func<BoxingGame>)(() => new BoxingGame(seed: 1, corners: new BoxingCorners(c.Name, s => c.Create(s), CpuBody: c.Build, CpuFace: c.Face))))).ToList());
    }

    private static int Draw(string path, IReadOnlyList<(string Name, Func<BoxingGame> Create)> contenders)
    {
        var width = Views.Length * Tile;
        var height = contenders.Count * Tile;
        var rgba = new byte[width * height * 4];
        for (var o = 0; o < rgba.Length; o += 4)
        {
            (rgba[o], rgba[o + 1], rgba[o + 2], rgba[o + 3]) = ((byte)((Background >> 16) & 255), (byte)((Background >> 8) & 255), (byte)(Background & 255), (byte)255);
        }

        for (var i = 0; i < contenders.Count; i++)
        {
            var c = contenders[i];
            using var game = c.Create();
            var head = game.Cpu.Voxels!.PartOf(game.Cpu.Parts.Head)!.Body.Grid;
            var face = game.Cpu.Voxels!.Face?.Body.Grid;
            VoxelGrid[] grids = face is null ? [head] : [head, face];
            for (var v = 0; v < Views.Length; v++)
            {
                Render(rgba, width, v * Tile, i * Tile, grids, Views[v]);
            }

            Console.WriteLine($"{i + 1}: {c.Name} (head cells {head.Dimensions}, face layer {face?.Dimensions.ToString() ?? "none"})");
        }

        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(path))!);
        PngWriter.WriteRgba(path, width, height, rgba);
        Console.WriteLine($"wrote {path} ({width}x{height}; columns: front, side, top, 3/4)");
        return 0;
    }

    /// <summary>格子（同じ箱の中心に置いた、細かさの違う格子）を、向き <paramref name="view"/> で正射影に描く（奥行きの近い方が勝つ）。</summary>
    private static void Render(byte[] rgba, int imageWidth, int originX, int originY, IReadOnlyList<VoxelGrid> grids, Matrix4x4 view)
    {
        var depth = new float[Tile * Tile];
        Array.Fill(depth, float.NegativeInfinity);
        var light = Vector3.Normalize(new Vector3(-0.4f, 0.7f, 0.9f));
        foreach (var grid in grids)
        {
            var (nx, ny, nz) = grid.Dimensions;
            var size = (int)MathF.Ceiling(grid.CellSize.X * PixelsPerMeter * 1.45f);
            for (var z = 0; z < nz; z++)
            {
                for (var y = 0; y < ny; y++)
                {
                    for (var x = 0; x < nx; x++)
                    {
                        var material = grid.At(x, y, z);
                        if (material == 0 || !grid.IsExposed(x, y, z))
                        {
                            continue;
                        }

                        // 表面の向き: 空の隣の向きの和。
                        var normal = Vector3.Zero;
                        foreach (var (dx, dy, dz) in new[] { (1, 0, 0), (-1, 0, 0), (0, 1, 0), (0, -1, 0), (0, 0, 1), (0, 0, -1) })
                        {
                            var (ax, ay, az) = (x + dx, y + dy, z + dz);
                            if (ax < 0 || ay < 0 || az < 0 || ax >= nx || ay >= ny || az >= nz || grid.At(ax, ay, az) == 0)
                            {
                                normal += new Vector3(dx, dy, dz);
                            }
                        }

                        normal = normal == Vector3.Zero ? Vector3.UnitZ : Vector3.Normalize(Vector3.TransformNormal(normal, view));
                        var p = Vector3.Transform(grid.CellCenter(x, y, z), view) * PixelsPerMeter;
                        var color = grid.IsOriginalSurface(x, y, z) ? grid.Palette[material].SurfaceColor : grid.Palette[material].Color;
                        var shade = 0.45f + (0.6f * MathF.Max(0f, Vector3.Dot(normal, light)));
                        var rgb = new Vector3(color.X, color.Y, color.Z) * shade;
                        var (px, py) = ((int)((Tile / 2f) + p.X) - (size / 2), (int)((Tile / 2f) - p.Y) - (size / 2));
                        for (var oy = 0; oy < size; oy++)
                        {
                            for (var ox = 0; ox < size; ox++)
                            {
                                var (sx, sy) = (px + ox, py + oy);
                                if (sx < 0 || sy < 0 || sx >= Tile || sy >= Tile || depth[sx + (Tile * sy)] >= p.Z)
                                {
                                    continue;
                                }

                                depth[sx + (Tile * sy)] = p.Z;
                                var o = (((originY + sy) * imageWidth) + originX + sx) * 4;
                                rgba[o] = (byte)Math.Clamp(rgb.X * 255f, 0f, 255f);
                                rgba[o + 1] = (byte)Math.Clamp(rgb.Y * 255f, 0f, 255f);
                                rgba[o + 2] = (byte)Math.Clamp(rgb.Z * 255f, 0f, 255f);
                                rgba[o + 3] = 255;
                            }
                        }
                    }
                }
            }
        }
    }
}
