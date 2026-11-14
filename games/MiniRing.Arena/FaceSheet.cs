using System.Numerics;
using Aigis.Foundation;
using MiniRing.Bots;

namespace MiniRing.Arena;

/// <summary>
/// 全キャラの顔を並べた画像（正面と斜め 40 度）。頭のボクセルを正射影で描く（描画エンジンは使わない: どの機械でも同じ絵になり、
/// 顔のテンプレートが作ったボクセルそのものを見られる）。キャラの並びは標準出力に出す（左上から右へ、上から下へ）。
/// </summary>
internal static class FaceSheet
{
    private const int Scale = 20;      // 1 セルを何 px で描くか（頭は約 12 セル）
    private const int Tile = 18 * Scale; // 1 枚の大きさ
    private const int Background = 0x2b2f38;

    public static int Run(string path, IReadOnlyList<Contender> contenders, int columns)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(columns, 1);
        var rows = (contenders.Count + columns - 1) / columns;
        var width = columns * Tile * 2;
        var height = rows * Tile;
        var rgba = new byte[width * height * 4];
        for (var o = 0; o < rgba.Length; o += 4)
        {
            (rgba[o], rgba[o + 1], rgba[o + 2], rgba[o + 3]) = ((byte)((Background >> 16) & 255), (byte)((Background >> 8) & 255), (byte)(Background & 255), (byte)255);
        }

        for (var i = 0; i < contenders.Count; i++)
        {
            var c = contenders[i];
            using var game = new BoxingGame(seed: 1, corners: new BoxingCorners(c.Name, s => c.Create(s), CpuBody: c.Build, CpuFace: c.Face));
            var grid = game.Cpu.Voxels!.PartOf(game.Cpu.Parts.Head)!.Body.Grid;
            var (cx, cy) = (i % columns * Tile * 2, i / columns * Tile);
            Render(rgba, width, cx, cy, grid, 0f);
            Render(rgba, width, cx + Tile, cy, grid, 40f * MathF.PI / 180f);
            Console.WriteLine($"{i + 1}: {c.Name} (head cells {grid.Dimensions})");
        }

        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(path))!);
        PngWriter.WriteRgba(path, width, height, rgba);
        Console.WriteLine($"wrote {path} ({width}x{height})");
        return 0;
    }

    private static void Render(byte[] rgba, int imageWidth, int originX, int originY, Aigis.Foundation.Voxels.VoxelGrid grid, float yaw)
    {
        var (nx, ny, nz) = grid.Dimensions;
        var center = new Vector3(nx, ny, nz) * 0.5f;
        var rotation = Matrix4x4.CreateRotationY(yaw);
        var depth = new float[Tile * Tile];
        Array.Fill(depth, float.NegativeInfinity);
        var light = Vector3.Normalize(new Vector3(-0.4f, 0.7f, 0.9f));
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

                    // 表面の向き: 空の隣の向きの和（正規化した値の向き）。
                    var normal = Vector3.Zero;
                    foreach (var (dx, dy, dz) in new[] { (1, 0, 0), (-1, 0, 0), (0, 1, 0), (0, -1, 0), (0, 0, 1), (0, 0, -1) })
                    {
                        var (ax, ay, az) = (x + dx, y + dy, z + dz);
                        if (ax < 0 || ay < 0 || az < 0 || ax >= nx || ay >= ny || az >= nz || grid.At(ax, ay, az) == 0)
                        {
                            normal += new Vector3(dx, dy, dz);
                        }
                    }

                    normal = normal == Vector3.Zero ? Vector3.UnitZ : Vector3.Normalize(Vector3.TransformNormal(normal, rotation));
                    var p = Vector3.Transform(new Vector3(x + 0.5f, y + 0.5f, z + 0.5f) - center, rotation);
                    var color = grid.Palette[material].SurfaceColor;
                    var shade = 0.45f + (0.6f * MathF.Max(0f, Vector3.Dot(normal, light)));
                    var rgb = new Vector3(color.X, color.Y, color.Z) * shade;
                    var (px, py) = ((int)((Tile / 2f) + (p.X * Scale)) - (Scale / 2), (int)((Tile * 0.5f) - (p.Y * Scale)) - (Scale / 2));
                    var quad = (int)MathF.Ceiling(Scale * (MathF.Abs(MathF.Cos(yaw)) + MathF.Abs(MathF.Sin(yaw))));
                    px -= (quad - Scale) / 2;
                    for (var oy = 0; oy < quad; oy++)
                    {
                        for (var ox = 0; ox < quad; ox++)
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
