using Aigis.Foundation;
using MiniRing.Characters;

namespace MiniRing.Arena;

/// <summary>
/// 名簿の肖像（メニューの顔・全身の絵、<see cref="CharacterPortraits"/>）を PNG に書き出す（レポート・資料用）。
/// 絵は試合の体のボクセルから写したもので、描き起こしていない。1 ピクセルを scale 倍の四角へ広げ、背景は透明。
/// </summary>
internal static class PortraitExport
{
    public static int Run(string outDir, IReadOnlyList<FighterCharacter> roster, int scale)
    {
        Directory.CreateDirectory(outDir);
        foreach (var c in roster)
        {
            var portrait = CharacterPortraits.Build(c);
            Write(Path.Combine(outDir, $"{c.Id}_face.png"), portrait.Face, scale);
            Write(Path.Combine(outDir, $"{c.Id}_body.png"), portrait.Body, scale);
            Console.WriteLine($"{c.Id}: face {portrait.Face.Width}x{portrait.Face.Height}, body {portrait.Body.Width}x{portrait.Body.Height} ({portrait.HeightMeters:0.000} m)");
        }

        return 0;
    }

    private static void Write(string path, PortraitImage image, int scale)
    {
        var (width, height, rgba) = Rgba(image, scale);
        PngWriter.WriteRgba(path, width, height, rgba);
    }

    /// <summary>肖像を scale 倍の四角へ広げた RGBA（背景は透明）。</summary>
    public static (int Width, int Height, byte[] Rgba) Rgba(PortraitImage image, int scale)
    {
        var width = image.Width * scale;
        var height = image.Height * scale;
        var rgba = new byte[width * height * 4];
        for (var y = 0; y < image.Height; y++)
        {
            for (var x = 0; x < image.Width; x++)
            {
                var c = image.ColorAt(x, y);
                for (var dy = 0; dy < scale; dy++)
                {
                    for (var dx = 0; dx < scale; dx++)
                    {
                        var i = ((((y * scale) + dy) * width) + (x * scale) + dx) * 4;
                        rgba[i] = (byte)(c.X * 255f);
                        rgba[i + 1] = (byte)(c.Y * 255f);
                        rgba[i + 2] = (byte)(c.Z * 255f);
                        rgba[i + 3] = (byte)(c.W * 255f);
                    }
                }
            }
        }

        return (width, height, rgba);
    }
}
