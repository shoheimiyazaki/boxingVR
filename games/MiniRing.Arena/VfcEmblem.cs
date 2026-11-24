using System.Numerics;
using Aigis.Render;

namespace MiniRing.Arena;

/// <summary>
/// VFC の紋章（中継のロゴ）: 太い赤いボクセルの六角形の枠（上下が平らな辺。立方体を積んだ段差）、枠の中の暗い灰の金網（菱形の格子）、
/// 手前の大きなクリーム色のブロック文字「VFC」（少し斜めに崩した力強い字形）、その下の黒地にクリームの文字「VOXEL FIGHTING CHAMPIONSHIP」の帯
/// （帯の縁はクリーム、下に赤い段、左右に赤い横縞が 3 本ずつ）。すべて画面の四角形で描く（回転は使わない。<see cref="WvcEmblem"/> と同じ描き方）。
/// 手本はユーザーの画像（docs/images/vfc_logo_reference.png）。大きさは 1 単位 <c>u</c> [px] で決まり、紋章全体は横 <see cref="Width"/> × 縦 <see cref="Height"/> 単位。
/// </summary>
internal static class VfcEmblem
{
    /// <summary>紋章の横幅（単位）。</summary>
    public const float Width = 44f;

    /// <summary>紋章の高さ（単位）。</summary>
    public const float Height = 19f;

    private static readonly WvcEmblem.Palette Red = new(new Vector3(0.93f, 0.14f, 0.11f), new Vector3(0.78f, 0.08f, 0.06f), new Vector3(0.4f, 0.03f, 0.03f));
    private static readonly WvcEmblem.Palette Cream = new(new Vector3(0.97f, 0.93f, 0.84f), new Vector3(0.88f, 0.82f, 0.7f), new Vector3(0.52f, 0.47f, 0.4f));
    private static readonly WvcEmblem.Palette Mesh = new(new Vector3(0.3f, 0.3f, 0.31f), new Vector3(0.2f, 0.2f, 0.21f), new Vector3(0.09f, 0.09f, 0.1f));

    /// <summary>太い字形（9 × 8、筆は 3 マス）。</summary>
    private static readonly Dictionary<char, string[]> BoldGlyphs = new()
    {
        ['V'] = ["###...###", "###...###", ".###.###.", ".###.###.", "..#####..", "..#####..", "...###...", "...###..."],
        ['F'] = ["#########", "#########", "###......", "#######..", "#######..", "###......", "###......", "###......"],
        ['C'] = [".#######.", "#########", "###....##", "###......", "###......", "###....##", "#########", ".#######."],
    };

    /// <summary>帯の小さな文字（5 × 7。「VOXEL FIGHTING CHAMPIONSHIP」に使う文字だけ）。</summary>
    private static readonly Dictionary<char, string[]> SmallGlyphs = new()
    {
        ['V'] = ["#...#", "#...#", "#...#", "#...#", ".#.#.", ".#.#.", "..#.."],
        ['O'] = [".###.", "#...#", "#...#", "#...#", "#...#", "#...#", ".###."],
        ['X'] = ["#...#", "#...#", ".#.#.", "..#..", ".#.#.", "#...#", "#...#"],
        ['E'] = ["#####", "#....", "#....", "####.", "#....", "#....", "#####"],
        ['L'] = ["#....", "#....", "#....", "#....", "#....", "#....", "#####"],
        ['F'] = ["#####", "#....", "#....", "####.", "#....", "#....", "#...."],
        ['I'] = ["#####", "..#..", "..#..", "..#..", "..#..", "..#..", "#####"],
        ['G'] = [".###.", "#...#", "#....", "#.###", "#...#", "#...#", ".###."],
        ['H'] = ["#...#", "#...#", "#...#", "#####", "#...#", "#...#", "#...#"],
        ['T'] = ["#####", "..#..", "..#..", "..#..", "..#..", "..#..", "..#.."],
        ['N'] = ["#...#", "##..#", "#.#.#", "#.#.#", "#..##", "#...#", "#...#"],
        ['C'] = [".###.", "#...#", "#....", "#....", "#....", "#...#", ".###."],
        ['A'] = [".###.", "#...#", "#...#", "#####", "#...#", "#...#", "#...#"],
        ['M'] = ["#...#", "##.##", "#.#.#", "#.#.#", "#...#", "#...#", "#...#"],
        ['P'] = ["####.", "#...#", "#...#", "####.", "#....", "#....", "#...."],
        ['S'] = [".####", "#....", "#....", ".###.", "....#", "....#", "####."],
    };

    /// <summary>帯の文字列。</summary>
    private const string Title = "VOXEL FIGHTING CHAMPIONSHIP";

    /// <summary>
    /// 紋章を描く。<paramref name="center"/> は紋章の中心、<paramref name="u"/> は 1 単位 [px]、
    /// <paramref name="shine"/> は文字を斜めに横切る光の帯の X [px]（NaN なら光らない）。
    /// 帯の文字は、ボクセルが細かすぎて潰れる大きさ（1 マス 1.4 px 未満）では <paramref name="text"/> の文字で書く。
    /// </summary>
    public static void Draw(List<ScreenTextDraw> text, List<UiShapeDraw> shapes, Vector2 center, float u, float alpha = 1f, float shine = float.NaN)
    {
        var origin = center - new Vector2(Width * 0.5f * u, Height * 0.5f * u); // 紋章の左上
        Vector2 At(float x, float y) => origin + new Vector2(x * u, y * u);

        // 黒の地。
        shapes.Add(new UiShapeDraw(center, new Vector2(Width, Height) * u, new Vector4(0.015f, 0.012f, 0.012f, 0.96f * alpha), UiShapeDraw.Uniform(0.3f * u)));

        // 六角形の枠（赤）と、その中の金網（灰）。1 単位の格子のマスが、外側の六角形の内側・内側の六角形の外側にあれば枠、内側の六角形の中で斜めの線の上なら金網。
        var frame = new List<Vector2>();
        var mesh = new List<Vector2>();
        const float CenterX = 22f;
        const float CenterY = 8f;
        for (var row = 0; row < 17; row++)
        {
            for (var col = 0; col < (int)Width; col++)
            {
                var x = col + 0.5f - CenterX;
                var y = row + 0.5f - CenterY;
                var inner = InsideHexagon(x, y, 17.6f, 5.4f, 7.8f);
                if (InsideHexagon(x, y, 21.5f, 8f, 7f) && !inner)
                {
                    frame.Add(At(col + 0.5f, row + 0.5f));
                }
                else if (inner && ((((col + row) & 3) == 0) || (((col - row) & 3) == 0)))
                {
                    mesh.Add(At(col + 0.5f, row + 0.5f));
                }
            }
        }

        WvcEmblem.Voxels(shapes, mesh, u, Mesh, alpha, float.NaN);
        WvcEmblem.Voxels(shapes, frame, u, Red, alpha, float.NaN);

        // 大きなクリーム色の「VFC」。少し斜めに崩す（下の行ほど左へ、1 行あたり 0.05 マス）。字の影として、右下に暗い面が出る（立方体の奥行き）。
        const float LetterCell = 1.02f;
        var letters = new List<Vector2>();
        var letterLeft = CenterX - (((3 * 9) + 2) * LetterCell * 0.5f);
        var letterTop = 4.1f;
        var column = 0;
        foreach (var ch in "VFC")
        {
            var glyph = BoldGlyphs[ch];
            for (var row = 0; row < glyph.Length; row++)
            {
                for (var col = 0; col < glyph[row].Length; col++)
                {
                    if (glyph[row][col] == '#')
                    {
                        var x = letterLeft + (((column * 10) + col + 0.5f - (row * 0.05f)) * LetterCell);
                        letters.Add(At(x, letterTop + ((row + 0.5f) * LetterCell)));
                    }
                }
            }

            column++;
        }

        WvcEmblem.Voxels(shapes, letters, LetterCell * u, Cream, alpha, shine);

        // 帯: 下に赤い段（中央が下へ尖る）、クリームの縁、黒地、クリームの文字。左右に赤い横縞が 3 本ずつ。
        const float BannerLeft = 5f;
        const float BannerRight = 39f;
        const float BannerTop = 13.2f;
        const float BannerHeight = 3.7f;
        var bannerCenter = At((BannerLeft + BannerRight) * 0.5f, BannerTop + (BannerHeight * 0.5f));
        var bannerSize = new Vector2(BannerRight - BannerLeft, BannerHeight) * u;
        var slab = new List<Vector2>();
        for (var col = (int)BannerLeft; col < (int)BannerRight; col++)
        {
            slab.Add(At(col + 0.5f, BannerTop + BannerHeight + 0.5f));
            if (col is >= 20 and < 24)
            {
                slab.Add(At(col + 0.5f, BannerTop + BannerHeight + 1.5f)); // 中央の尖り
            }
        }

        WvcEmblem.Voxels(shapes, slab, u, Red, alpha, float.NaN);
        var stripes = new List<Vector2>();
        for (var stripe = 0; stripe < 3; stripe++)
        {
            var length = 4 - stripe; // 外側の縞ほど長い（段になる）
            for (var k = 0; k < length; k++)
            {
                var y = BannerTop + 0.4f + (stripe * 1.2f);
                stripes.Add(At(BannerLeft - 0.5f - k, y + 0.5f));
                stripes.Add(At(BannerRight + 0.5f + k, y + 0.5f));
            }
        }

        WvcEmblem.Voxels(shapes, stripes, u, Red, alpha, float.NaN);
        shapes.Add(new UiShapeDraw(bannerCenter + new Vector2(0.2f * u), bannerSize, Vec(Cream.Side, alpha)));
        shapes.Add(new UiShapeDraw(bannerCenter, bannerSize, Vec(Cream.Top, alpha), FillColorEnd: Vec(Cream.Bottom, alpha), GradientAxis: Vector2.UnitY));
        shapes.Add(new UiShapeDraw(bannerCenter, bannerSize - new Vector2(1.2f * u), new Vector4(0.02f, 0.015f, 0.015f, alpha)));
        shapes.Add(new UiShapeDraw(At(22f, BannerTop + BannerHeight - 0.3f), new Vector2(3.4f * u, 0.8f * u), new Vector4(0.02f, 0.015f, 0.015f, alpha))); // 下の縁の切れ込み（黒い尖り）

        var glyphCell = 0.18f * u;
        if (glyphCell >= 1.4f)
        {
            var cells = new List<Vector2>();
            var wordWidth = ((Title.Length * 6) - 1) * glyphCell;
            var left = bannerCenter.X - (wordWidth * 0.5f);
            for (var i = 0; i < Title.Length; i++)
            {
                if (SmallGlyphs.TryGetValue(Title[i], out var glyph))
                {
                    WvcEmblem.Mask(cells, glyph, new Vector2(left + (i * 6 * glyphCell), bannerCenter.Y - (3.5f * glyphCell)), glyphCell, mirror: false);
                }
            }

            WvcEmblem.Voxels(shapes, cells, glyphCell, Cream, alpha, float.NaN);
        }
        else if (u < 3.5f)
        {
            // 角の小さなロゴの札: 文字は読めない大きさなので、文字の並びを細い線で示す。
            shapes.Add(new UiShapeDraw(bannerCenter, new Vector2(26f * u, 0.7f * u), Vec(Cream.Top, 0.8f * alpha)));
        }
        else
        {
            text.Add(new ScreenTextDraw(Title, bannerCenter - new Vector2(0f, 1.05f * u), 2f * u, Vec(Cream.Top, alpha), TextAnchor.Center));
        }
    }

    /// <summary>上下が平らな六角形（中心からの座標 <paramref name="x"/>・<paramref name="y"/>、横の半幅 <paramref name="halfWidth"/>、縦の半幅 <paramref name="halfHeight"/>、上辺の半分の長さ <paramref name="topHalf"/>）の内側か。</summary>
    private static bool InsideHexagon(float x, float y, float halfWidth, float halfHeight, float topHalf)
        => MathF.Abs(y) <= halfHeight && MathF.Abs(x) <= topHalf + ((halfWidth - topHalf) * (1f - (MathF.Abs(y) / halfHeight)));

    private static Vector4 Vec(Vector3 rgb, float alpha) => new(rgb, alpha);
}

/// <summary>団体の紋章の描き分け（団体のファイルの <c>emblem</c> の id → 描く関数）。コードに団体名の分岐を書かず、id で引く。</summary>
internal static class Emblems
{
    private delegate void Drawer(List<ScreenTextDraw> text, List<UiShapeDraw> shapes, Vector2 center, float u, float alpha, float shine);

    private static readonly Dictionary<string, Drawer> Registry = new(StringComparer.Ordinal)
    {
        ["wvc"] = WvcEmblem.Draw,
        ["vfc"] = VfcEmblem.Draw,
        ["ivf"] = IvfEmblem.Draw,
    };

    /// <summary>紋章の id の一覧。</summary>
    public static IReadOnlyList<string> Ids { get; } = [.. Registry.Keys];

    /// <summary>団体の紋章を描く。<paramref name="u"/> は 1 単位 [px]、<paramref name="shine"/> は光の帯の X（NaN なら光らない）。</summary>
    public static void Draw(Organization org, List<ScreenTextDraw> text, List<UiShapeDraw> shapes, Vector2 center, float u, float alpha = 1f, float shine = float.NaN)
    {
        ArgumentNullException.ThrowIfNull(org);
        Registry[org.Emblem](text, shapes, center, u, alpha, shine);
    }
}
