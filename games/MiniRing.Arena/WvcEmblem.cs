using System.Numerics;
using Aigis.Render;

namespace MiniRing.Arena;

/// <summary>
/// WVC の紋章（中継のロゴ）: ボクセルを積んだ太い金の「WVC」、その上の王冠と後ろへ広がる光の筋、文字の後ろの赤い盾、
/// 両脇の月桂樹の枝、下の黒地に金の「VOXING」の札、金の外枠（角は段になったブロック）。すべて画面の四角形で描く（回転は使わない）。
/// 大きさは 1 単位 <c>u</c> [px] で決まり、紋章全体は横 <see cref="Width"/> × 縦 <see cref="Height"/> 単位。
/// </summary>
internal static class WvcEmblem
{
    /// <summary>紋章の横幅（単位）。</summary>
    public const float Width = 44f;

    /// <summary>紋章の高さ（単位）。</summary>
    public const float Height = 19f;

    /// <summary>金・赤のボクセルの色（面の上・面の下・側面）。</summary>
    private readonly record struct Palette(Vector3 Top, Vector3 Bottom, Vector3 Side);

    private static readonly Palette Gold = new(new Vector3(1f, 0.9f, 0.45f), new Vector3(0.93f, 0.62f, 0.1f), new Vector3(0.45f, 0.26f, 0.04f));
    private static readonly Palette Red = new(new Vector3(0.9f, 0.12f, 0.08f), new Vector3(0.62f, 0.04f, 0.03f), new Vector3(0.3f, 0.01f, 0.01f));

    /// <summary>太い字形（7 × 7、筆は 2 マス）。</summary>
    private static readonly Dictionary<char, string[]> BoldGlyphs = new()
    {
        ['W'] = ["##...##", "##...##", "##.#.##", "##.#.##", "#######", "###.###", ".#...#."],
        ['V'] = ["##...##", "##...##", "##...##", "##...##", ".##.##.", ".#####.", "..###.."],
        ['C'] = [".######", "###..##", "##.....", "##.....", "##.....", "###..##", ".######"],
    };

    /// <summary>札の文字（5 × 7）。</summary>
    private static readonly Dictionary<char, string[]> SmallGlyphs = new()
    {
        ['V'] = ["#...#", "#...#", "#...#", "#...#", ".#.#.", ".#.#.", "..#.."],
        ['O'] = [".###.", "#...#", "#...#", "#...#", "#...#", "#...#", ".###."],
        ['X'] = ["#...#", "#...#", ".#.#.", "..#..", ".#.#.", "#...#", "#...#"],
        ['I'] = ["#####", "..#..", "..#..", "..#..", "..#..", "..#..", "#####"],
        ['N'] = ["#...#", "##..#", "#.#.#", "#.#.#", "#..##", "#...#", "#...#"],
        ['G'] = [".###.", "#...#", "#....", "#.###", "#...#", "#...#", ".###."],
    };

    /// <summary>王冠（9 × 7）。</summary>
    private static readonly string[] Crown =
    [
        "....#....",
        "...###...",
        "....#....",
        "#..###..#",
        "##.###.##",
        "#########",
        "#########",
    ];

    /// <summary>左の月桂樹の枝（7 × 14。下が付け根、葉は上と外へ向く）。右はこれを左右に裏返す。</summary>
    private static readonly string[] Laurel =
    [
        "...##..",
        "..###..",
        "..##...",
        "##.##..",
        "###.#..",
        ".##.##.",
        "##..##.",
        "###.#..",
        ".##.##.",
        "##..##.",
        "###..#.",
        ".##.##.",
        "....##.",
        "...##..",
    ];

    /// <summary>
    /// 紋章を描く。<paramref name="center"/> は紋章の中心、<paramref name="u"/> は 1 単位 [px]、
    /// <paramref name="shine"/> は文字を斜めに横切る光の帯の X [px]（NaN なら光らない）。
    /// 札の文字は、ボクセルが細かすぎて潰れる大きさ（1 マス 1.4 px 未満）では <paramref name="text"/> の文字で書く。
    /// </summary>
    public static void Draw(List<ScreenTextDraw> text, List<UiShapeDraw> shapes, Vector2 center, float u, float alpha = 1f, float shine = float.NaN)
    {
        var c = center + new Vector2(0f, -1.2f * u); // 文字の中心（紋章の中心より少し上）
        var halfW = Width * 0.5f * u;
        var halfH = Height * 0.5f * u;

        // 黒地と、内側の金の光のにじみ。
        shapes.Add(new UiShapeDraw(center, new Vector2(Width, Height) * u, new Vector4(0.035f, 0.03f, 0.03f, 0.96f * alpha), UiShapeDraw.Uniform(0.3f * u),
            FillColorEnd: new Vector4(0.09f, 0.06f, 0.03f, 0.96f * alpha), GradientAxis: Vector2.UnitY));
        shapes.Add(new UiShapeDraw(c, new Vector2(24f, 9f) * u, new Vector4(1f, 0.6f, 0.15f, 0.22f * alpha), UiShapeDraw.Uniform(4f * u), SoftnessPixels: 5f * u));

        // 光の筋: 王冠の下から、上と斜め上へ広がる半透明の金の粒の列。
        var rayOrigin = c + new Vector2(0f, -2.5f * u);
        foreach (var degrees in new[] { -62f, -40f, 40f, 62f })
        {
            var a = (degrees - 90f) * MathF.PI / 180f;
            var dir = new Vector2(MathF.Cos(a), MathF.Sin(a));
            for (var k = 3; k < 14; k++)
            {
                var p = rayOrigin + (dir * k * 0.8f * u);
                if (MathF.Abs(p.Y - center.Y) > halfH - (1.2f * u) || MathF.Abs(p.X - center.X) > halfW - (1.2f * u))
                {
                    break;
                }

                var fade = 1f - (k / 14f);
                shapes.Add(new UiShapeDraw(p, new Vector2(1.1f * u), new Vector4(1f, 0.78f, 0.3f, 0.6f * fade * alpha), UiShapeDraw.Uniform(0.2f * u), SoftnessPixels: 0.6f * u));
            }
        }

        // 赤い盾: 文字の後ろの段になった横の帯の積み重ね。下は札の下まで尖る。
        var row = 0.55f * u;
        for (var y = -4.4f * u; y < 9.4f * u; y += row)
        {
            var half = y < -3.6f * u ? 10.5f * u : y < 3.4f * u ? 12.6f * u : MathF.Max(0f, 12.6f * u - ((y - (3.4f * u)) * 2.05f));
            half = MathF.Round(half / row) * row;
            if (half <= 0f)
            {
                break;
            }

            var at = c + new Vector2(0f, y + (row * 0.5f));
            shapes.Add(new UiShapeDraw(at + new Vector2(0.18f * u), new Vector2(2f * half, row), Vec(Red.Side, alpha)));
            shapes.Add(new UiShapeDraw(at, new Vector2(2f * half, row * 0.9f), Vec(Red.Top, alpha), FillColorEnd: Vec(Red.Bottom, alpha), GradientAxis: Vector2.UnitX * 0.6f));
        }

        // 月桂樹の枝（左は字形どおり、右は裏返し）。
        var laurelCell = 0.72f * u;
        var laurelTop = c.Y - (6f * u);
        var laurelInner = 13.6f * u;
        var cells = new List<Vector2>();
        Mask(cells, Laurel, new Vector2(c.X - laurelInner - (Laurel[0].Length * laurelCell), laurelTop), laurelCell, mirror: false);
        Voxels(shapes, cells, laurelCell, Gold, alpha, float.NaN);
        cells.Clear();
        Mask(cells, Laurel, new Vector2(c.X + laurelInner, laurelTop), laurelCell, mirror: true);
        Voxels(shapes, cells, laurelCell, Gold, alpha, float.NaN);

        // 太い金の WVC。
        cells.Clear();
        var letterLeft = c.X - (11.5f * u);
        var column = 0;
        foreach (var ch in TvShowGraphics.LogoText)
        {
            if (BoldGlyphs.TryGetValue(ch, out var glyph))
            {
                Mask(cells, glyph, new Vector2(letterLeft + (column * u), c.Y - (3.5f * u)), u, mirror: false);
            }

            column += 8;
        }

        Voxels(shapes, cells, u, Gold, alpha, shine);

        // 王冠。
        cells.Clear();
        var crownCell = 0.78f * u;
        Mask(cells, Crown, new Vector2(c.X - (4.5f * crownCell), c.Y - (3.7f * u) - (Crown.Length * crownCell)), crownCell, mirror: false);
        Voxels(shapes, cells, crownCell, Gold, alpha, shine);

        // VOXING の札: 金の縁の黒い帯、金の文字。
        var plate = c + new Vector2(0f, 5.5f * u);
        var plateSize = new Vector2(17f, 3f) * u;
        shapes.Add(new UiShapeDraw(plate + new Vector2(0.2f * u), plateSize, Vec(Gold.Side, alpha)));
        shapes.Add(new UiShapeDraw(plate, plateSize, Vec(Gold.Top, alpha), FillColorEnd: Vec(Gold.Bottom, alpha), GradientAxis: Vector2.UnitY));
        shapes.Add(new UiShapeDraw(plate, plateSize - new Vector2(0.6f * u), new Vector4(0.03f, 0.03f, 0.03f, alpha)));
        var word = TvShowGraphics.SportName.ToUpperInvariant();
        var glyphCell = 0.28f * u;
        if (glyphCell >= 1.4f)
        {
            cells.Clear();
            var wordWidth = ((word.Length * 5) + ((word.Length - 1) * 2)) * glyphCell;
            for (var i = 0; i < word.Length; i++)
            {
                if (SmallGlyphs.TryGetValue(word[i], out var glyph))
                {
                    Mask(cells, glyph, new Vector2(plate.X - (wordWidth * 0.5f) + (i * 7 * glyphCell), plate.Y - (3.5f * glyphCell)), glyphCell, mirror: false);
                }
            }

            Voxels(shapes, cells, glyphCell, Gold, alpha, float.NaN);
        }
        else
        {
            text.Add(new ScreenTextDraw(string.Join(' ', word.ToCharArray()), plate - new Vector2(0f, 1.45f * u), 2.5f * u, Vec(Gold.Top, alpha), TextAnchor.Center));
        }

        // 外枠: 金の太い縁と、四隅で外へ出る段のブロック。
        var edge = 0.55f * u;
        var inset = 0.9f * u;
        var frameW = (2f * halfW) - (2f * inset);
        var frameH = (2f * halfH) - (2f * inset);
        foreach (var (at, size) in new[]
        {
            (center + new Vector2(0f, -(frameH - edge) * 0.5f), new Vector2(frameW, edge)),
            (center + new Vector2(0f, (frameH - edge) * 0.5f), new Vector2(frameW, edge)),
            (center + new Vector2(-(frameW - edge) * 0.5f, 0f), new Vector2(edge, frameH)),
            (center + new Vector2((frameW - edge) * 0.5f, 0f), new Vector2(edge, frameH)),
        })
        {
            shapes.Add(new UiShapeDraw(at + new Vector2(0.15f * u), size, Vec(Gold.Side, alpha)));
            shapes.Add(new UiShapeDraw(at, size, Vec(Gold.Top, alpha), FillColorEnd: Vec(Gold.Bottom, alpha), GradientAxis: size.X > size.Y ? Vector2.UnitY : Vector2.UnitX));
        }

        cells.Clear();
        foreach (var sx in new[] { -1f, 1f })
        {
            foreach (var sy in new[] { -1f, 1f })
            {
                var corner = center + new Vector2(sx * frameW * 0.5f, sy * frameH * 0.5f);
                cells.Add(corner - new Vector2(sx * 0.35f * u, sy * 0.35f * u));
                cells.Add(corner + new Vector2(sx * 0.35f * u, -sy * 0.35f * u));
                cells.Add(corner + new Vector2(-sx * 0.35f * u, sy * 0.35f * u));
            }
        }

        Voxels(shapes, cells, 0.7f * u, Gold, alpha, float.NaN);

        // 縁の上で光る点（上の辺の左右と、左右の辺の真ん中）。
        foreach (var spot in new[] { new Vector2(-0.33f * frameW, -0.5f * frameH), new Vector2(0.33f * frameW, -0.5f * frameH), new Vector2(-0.5f * frameW, 0f), new Vector2(0.5f * frameW, 0f) })
        {
            shapes.Add(new UiShapeDraw(center + spot, new Vector2(1.6f * u), new Vector4(1f, 0.95f, 0.75f, 0.55f * alpha), UiShapeDraw.Uniform(u), SoftnessPixels: 1.4f * u));
        }
    }

    /// <summary>字形の '#' のマスの中心を足す。<paramref name="topLeft"/> は字形の左上の角、<paramref name="mirror"/> なら左右を裏返す。</summary>
    private static void Mask(List<Vector2> cells, string[] glyph, Vector2 topLeft, float cell, bool mirror)
    {
        for (var row = 0; row < glyph.Length; row++)
        {
            var width = glyph[row].Length;
            for (var col = 0; col < width; col++)
            {
                if (glyph[row][col] == '#')
                {
                    var x = mirror ? width - 1 - col : col;
                    cells.Add(topLeft + new Vector2((x + 0.5f) * cell, (row + 0.5f) * cell));
                }
            }
        }
    }

    /// <summary>
    /// ボクセルを並べる: マスごとに右下へずらした暗い側面（奥行き）、その上に上が明るく下が深い面と、上の縁の照り。
    /// 光の帯（<paramref name="shine"/> の X の前後 2.5 マス、下ほど右へずれる斜めの帯）にかかる面は白へ寄せる。
    /// </summary>
    private static void Voxels(List<UiShapeDraw> shapes, List<Vector2> cells, float cell, Palette palette, float alpha, float shine)
    {
        if (cells.Count == 0)
        {
            return;
        }

        var depth = new Vector2(cell * 0.32f);
        var face = new Vector2(cell * 0.94f);
        var white = new Vector4(1f, 1f, 1f, alpha);
        var topY = cells[0].Y;
        foreach (var p in cells)
        {
            shapes.Add(new UiShapeDraw(p + depth, face, Vec(palette.Side, alpha)));
        }

        foreach (var p in cells)
        {
            var glow = float.IsNaN(shine) ? 0f : MathF.Exp(-MathF.Pow((p.X - shine - ((p.Y - topY) * 0.5f)) / (2.5f * cell), 2f));
            shapes.Add(new UiShapeDraw(p, face, Vector4.Lerp(Vec(palette.Top, alpha), white, 0.85f * glow), FillColorEnd: Vector4.Lerp(Vec(palette.Bottom, alpha), white, 0.7f * glow),
                GradientAxis: Vector2.UnitY));
            shapes.Add(new UiShapeDraw(p - new Vector2(0f, face.Y * 0.4f), new Vector2(face.X, face.Y * 0.12f), new Vector4(1f, 1f, 0.92f, 0.4f * alpha)));
        }
    }

    private static Vector4 Vec(Vector3 rgb, float alpha) => new(rgb, alpha);
}
