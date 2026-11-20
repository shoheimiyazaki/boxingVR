using System.IO.Compression;
using System.Numerics;
using MiniRing.Motion.Stick;

namespace MiniRing.Arena;

/// <summary>
/// 棒人形の動きを、横（右から見た Z–Y）と正面（X–Y）のコマの並びで描く PNG（docs/STICK_MOTION.md §5）。
/// 左の手足は青、右は赤、胴・頭は黒。緑の点 = 重心、橙の縦線 = ZMP（外へ出ると太い）、灰の線 = 床に着いた足の範囲。GPU を使わない。
/// </summary>
internal static class StickPicture
{
    private const int Cell = 220;

    public static void Save(StickResult result, string path, int frames = 10)
    {
        var picks = Enumerable.Range(0, frames).Select(i => (int)MathF.Round(i * (result.Frames.Count - 1) / (float)Math.Max(1, frames - 1))).ToArray();
        var width = Cell * picks.Length;
        var height = Cell * 2;
        var image = new byte[width * height * 3];
        Array.Fill(image, (byte)255);
        var scale = Cell * 0.8f / 1.69f; // 1.69 m（人の大きさの体）がコマの 8 割
        for (var k = 0; k < picks.Length; k++)
        {
            var frame = result.Frames[picks[k]];
            var load = result.Loads[picks[k]];
            var step = result.Steps[picks[k]];
            var com = frame.CenterOfMass;
            for (var view = 0; view < 2; view++)
            {
                // view 0: 横（右から見る: 画面の右 = +Z 前）、view 1: 正面（相手の側から見る: 画面の右 = −X）。
                var ox = (k * Cell) + (Cell / 2);
                var oy = (view * Cell) + (int)(Cell * 0.9f);
                Vector2 P(Vector3 w) => new(ox + ((view == 0 ? w.Z - com.Z : -(w.X - com.X)) * scale), oy - (w.Y * scale));
                Line(image, width, height, new Vector2(k * Cell, oy), new Vector2((k + 1) * Cell, oy), (200, 200, 200), 1);
                foreach (var leg in new[] { Hand.Left, Hand.Right })
                {
                    if (leg == Hand.Left ? step.Support.Left : step.Support.Right)
                    {
                        var sole = frame.Sole(leg).Select(P).ToArray();
                        Line(image, width, height, new Vector2(sole.Min(p => p.X), oy + 3), new Vector2(sole.Max(p => p.X), oy + 3), (130, 130, 130), 2);
                    }
                }

                for (var i = 1; i < result.Body.Segments.Count; i++)
                {
                    var s = result.Body.Segments[i];
                    var color = s.Name.EndsWith("-Left", StringComparison.Ordinal) ? (40, 80, 230) : s.Name.EndsWith("-Right", StringComparison.Ordinal) ? (220, 50, 50) : (20, 20, 20);
                    Line(image, width, height, P(frame.Joints[i]), P(frame.Tip(i)), color, 2);
                }

                Line(image, width, height, P(frame.Joints[0]), P(frame.Joints[StickBody.Ids.Chest]), (20, 20, 20), 3);
                var c = P(com);
                Dot(image, width, height, c, (30, 170, 60), 4);
                if (!float.IsNaN(load.Zmp.X))
                {
                    var z = P(load.Zmp);
                    Line(image, width, height, z, z - new Vector2(0f, 14f), (240, 140, 0), load.ZmpOutside > 0f ? 4 : 2);
                }
            }
        }

        WritePng(path, width, height, image);
    }

    private static void Dot(byte[] image, int width, int height, Vector2 p, (int R, int G, int B) color, int radius)
    {
        for (var y = -radius; y <= radius; y++)
        {
            for (var x = -radius; x <= radius; x++)
            {
                if ((x * x) + (y * y) <= radius * radius)
                {
                    Put(image, width, height, (int)p.X + x, (int)p.Y + y, color);
                }
            }
        }
    }

    private static void Line(byte[] image, int width, int height, Vector2 a, Vector2 b, (int R, int G, int B) color, int thickness)
    {
        var steps = (int)MathF.Max(1f, Vector2.Distance(a, b));
        for (var i = 0; i <= steps; i++)
        {
            var p = Vector2.Lerp(a, b, i / (float)steps);
            Dot(image, width, height, p, color, thickness / 2);
        }
    }

    private static void Put(byte[] image, int width, int height, int x, int y, (int R, int G, int B) color)
    {
        if (x < 0 || y < 0 || x >= width || y >= height)
        {
            return;
        }

        var at = ((y * width) + x) * 3;
        (image[at], image[at + 1], image[at + 2]) = ((byte)color.R, (byte)color.G, (byte)color.B);
    }

    private static void WritePng(string path, int width, int height, byte[] rgb)
    {
        using var file = File.Create(path);
        file.Write([137, 80, 78, 71, 13, 10, 26, 10]);
        var header = new byte[13];
        BigEndian(header, 0, width);
        BigEndian(header, 4, height);
        header[8] = 8; // ビット深度
        header[9] = 2; // RGB
        Chunk(file, "IHDR", header);
        using var raw = new MemoryStream();
        using (var z = new ZLibStream(raw, CompressionLevel.Optimal, leaveOpen: true))
        {
            for (var y = 0; y < height; y++)
            {
                z.WriteByte(0);
                z.Write(rgb, y * width * 3, width * 3);
            }
        }

        Chunk(file, "IDAT", raw.ToArray());
        Chunk(file, "IEND", []);
    }

    private static void Chunk(Stream stream, string type, byte[] data)
    {
        var length = new byte[4];
        BigEndian(length, 0, data.Length);
        stream.Write(length);
        var typeBytes = System.Text.Encoding.ASCII.GetBytes(type);
        stream.Write(typeBytes);
        stream.Write(data);
        var crc = Crc(typeBytes, data);
        var crcBytes = new byte[4];
        BigEndian(crcBytes, 0, (int)crc);
        stream.Write(crcBytes);
    }

    private static void BigEndian(byte[] buffer, int at, int value)
    {
        buffer[at] = (byte)(value >> 24);
        buffer[at + 1] = (byte)(value >> 16);
        buffer[at + 2] = (byte)(value >> 8);
        buffer[at + 3] = (byte)value;
    }

    private static uint Crc(byte[] type, byte[] data)
    {
        var c = 0xFFFFFFFFu;
        foreach (var b in type.Concat(data))
        {
            c ^= b;
            for (var k = 0; k < 8; k++)
            {
                c = (c & 1) != 0 ? 0xEDB88320u ^ (c >> 1) : c >> 1;
            }
        }

        return c ^ 0xFFFFFFFFu;
    }
}
