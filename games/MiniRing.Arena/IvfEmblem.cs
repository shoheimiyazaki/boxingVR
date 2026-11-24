using System.Numerics;
using Aigis.Render;

namespace MiniRing.Arena;

/// <summary>
/// IVF の紋章（中継のロゴ）: 階級ごとの王座のベルト。横いっぱいのエメラルドのベルト（縁にステッチ、両端に銀の鋲）、
/// 左右の銀の小さな飾り板（中にエメラルドの石）、真ん中の段になった銀の大きな板と、その上の紺の窓に銀のボクセルの太い「IVF」、
/// 板の上に並ぶ 5 つの銀の星（5 つの階級）、下の紺地にエメラルドの縁の帯「INTERESTING VOXING FEDERATION」。
/// すべて画面の四角形で描く（回転は使わない。<see cref="WvcEmblem"/> と同じ描き方）。大きさは 1 単位 <c>u</c> [px] で決まり、
/// 紋章全体は横 <see cref="Width"/> × 縦 <see cref="Height"/> 単位（WVC・VFC と同じ枠に収まる）。
/// </summary>
internal static class IvfEmblem
{
    /// <summary>紋章の横幅（単位）。</summary>
    public const float Width = 44f;

    /// <summary>紋章の高さ（単位）。</summary>
    public const float Height = 19f;

    /// <summary>ベルトの中心の高さ（紋章の中心から下へ、単位）。</summary>
    private const float BeltY = -1f;

    private static readonly WvcEmblem.Palette Emerald = new(new Vector3(0.2f, 0.82f, 0.56f), new Vector3(0.05f, 0.5f, 0.33f), new Vector3(0.02f, 0.22f, 0.15f));
    private static readonly WvcEmblem.Palette Silver = new(new Vector3(0.96f, 0.97f, 1f), new Vector3(0.62f, 0.66f, 0.72f), new Vector3(0.28f, 0.3f, 0.35f));
    private static readonly Vector4 Navy = new(0.03f, 0.05f, 0.12f, 1f);

    /// <summary>太い字形（7 × 7、筆は 2 マス）。</summary>
    private static readonly Dictionary<char, string[]> BoldGlyphs = new()
    {
        ['I'] = ["#######", "#######", "..###..", "..###..", "..###..", "#######", "#######"],
        ['V'] = ["##...##", "##...##", "##...##", ".##.##.", ".##.##.", "..###..", "...#..."],
        ['F'] = ["#######", "#######", "##.....", "######.", "######.", "##.....", "##....."],
    };

    /// <summary>帯の小さな文字（5 × 7。「INTERESTING VOXING FEDERATION」に使う文字だけ）。</summary>
    private static readonly Dictionary<char, string[]> SmallGlyphs = new()
    {
        ['I'] = ["#####", "..#..", "..#..", "..#..", "..#..", "..#..", "#####"],
        ['N'] = ["#...#", "##..#", "#.#.#", "#.#.#", "#..##", "#...#", "#...#"],
        ['T'] = ["#####", "..#..", "..#..", "..#..", "..#..", "..#..", "..#.."],
        ['E'] = ["#####", "#....", "#....", "####.", "#....", "#....", "#####"],
        ['R'] = ["####.", "#...#", "#...#", "####.", "#.#..", "#..#.", "#...#"],
        ['S'] = [".####", "#....", "#....", ".###.", "....#", "....#", "####."],
        ['G'] = [".###.", "#...#", "#....", "#.###", "#...#", "#...#", ".###."],
        ['V'] = ["#...#", "#...#", "#...#", "#...#", ".#.#.", ".#.#.", "..#.."],
        ['O'] = [".###.", "#...#", "#...#", "#...#", "#...#", "#...#", ".###."],
        ['X'] = ["#...#", "#...#", ".#.#.", "..#..", ".#.#.", "#...#", "#...#"],
        ['F'] = ["#####", "#....", "#....", "####.", "#....", "#....", "#...."],
        ['D'] = ["####.", "#...#", "#...#", "#...#", "#...#", "#...#", "####."],
        ['A'] = [".###.", "#...#", "#...#", "#####", "#...#", "#...#", "#...#"],
    };

    /// <summary>帯の文字列。</summary>
    private const string Title = "INTERESTING VOXING FEDERATION";

    /// <summary>
    /// 紋章を描く。<paramref name="center"/> は紋章の中心、<paramref name="u"/> は 1 単位 [px]、
    /// <paramref name="shine"/> は文字を斜めに横切る光の帯の X [px]（NaN なら光らない）。
    /// 帯の文字は、ボクセルが細かすぎて潰れる大きさ（1 マス 1.4 px 未満）では <paramref name="text"/> の文字で書く。
    /// </summary>
    public static void Draw(List<ScreenTextDraw> text, List<UiShapeDraw> shapes, Vector2 center, float u, float alpha = 1f, float shine = float.NaN)
    {
        var belt = center + new Vector2(0f, BeltY * u);

        // 紺の地と、板の後ろのエメラルドのにじみ。
        shapes.Add(new UiShapeDraw(center, new Vector2(Width, Height) * u, Navy with { W = 0.96f * alpha }, UiShapeDraw.Uniform(0.6f * u),
            FillColorEnd: new Vector4(0.02f, 0.09f, 0.1f, 0.96f * alpha), GradientAxis: Vector2.UnitY));
        shapes.Add(new UiShapeDraw(belt, new Vector2(30f, 13f) * u, new Vector4(0.15f, 0.9f, 0.6f, 0.2f * alpha), UiShapeDraw.Uniform(5f * u), SoftnessPixels: 5f * u));

        // ベルト: 3 枚の横の板を重ねて端を段にする（真ん中が一番長い）。縁にステッチの点。
        foreach (var (halfWidth, height) in new[] { (20.6f, 4f), (19.8f, 5.2f), (18.9f, 6f) })
        {
            var size = new Vector2(2f * halfWidth, height) * u;
            shapes.Add(new UiShapeDraw(belt + new Vector2(0.2f * u), size, Vec(Emerald.Side, alpha)));
            shapes.Add(new UiShapeDraw(belt, size, Vec(Emerald.Top, alpha), FillColorEnd: Vec(Emerald.Bottom, alpha), GradientAxis: Vector2.UnitY));
        }

        for (var x = -18f; x <= 18.01f; x += 1.5f)
        {
            foreach (var y in new[] { -2.45f, 2.45f })
            {
                shapes.Add(new UiShapeDraw(belt + (new Vector2(x, y) * u), new Vector2(0.7f, 0.18f) * u, new Vector4(0.75f, 1f, 0.88f, 0.75f * alpha)));
            }
        }

        // 両端の鋲と、左右の飾り板（エメラルドの石）。
        var cells = new List<Vector2>();
        foreach (var side in new[] { -1f, 1f })
        {
            cells.Add(belt + new Vector2(side * 19.6f * u, 0f));
            var plate = belt + new Vector2(side * 15.6f * u, 0f);
            foreach (var (w, h) in new[] { (5.2f, 4.2f), (4.4f, 5.2f) })
            {
                shapes.Add(new UiShapeDraw(plate + new Vector2(0.18f * u), new Vector2(w, h) * u, Vec(Silver.Side, alpha)));
                shapes.Add(new UiShapeDraw(plate, new Vector2(w, h) * u, Vec(Silver.Top, alpha), FillColorEnd: Vec(Silver.Bottom, alpha), GradientAxis: Vector2.UnitY));
            }

            shapes.Add(new UiShapeDraw(plate, new Vector2(1.8f) * u, Vec(Emerald.Top, alpha), FillColorEnd: Vec(Emerald.Bottom, alpha), GradientAxis: Vector2.UnitY));
        }

        WvcEmblem.Voxels(shapes, cells, 1.1f * u, Silver, alpha, float.NaN);

        // 真ん中の大きな板: 3 枚を重ねて角を段にした八角形。中に紺の窓。
        foreach (var (w, h) in new[] { (24f, 10f), (22.4f, 12f), (20.6f, 13f) })
        {
            var size = new Vector2(w, h) * u;
            shapes.Add(new UiShapeDraw(belt + new Vector2(0.25f * u), size, Vec(Silver.Side, alpha)));
            shapes.Add(new UiShapeDraw(belt, size, Vec(Silver.Top, alpha), FillColorEnd: Vec(Silver.Bottom, alpha), GradientAxis: Vector2.UnitY));
        }

        var window = belt + new Vector2(0f, -0.3f * u);
        shapes.Add(new UiShapeDraw(window, new Vector2(21.2f, 8.6f) * u, Vec(Emerald.Side, alpha)));
        shapes.Add(new UiShapeDraw(window, new Vector2(20.4f, 7.8f) * u, Navy with { W = alpha }, FillColorEnd: new Vector4(0.05f, 0.1f, 0.2f, alpha), GradientAxis: Vector2.UnitY));

        // 5 つの星（5 つの階級）: 板の上の縁に並ぶ銀のボクセル。
        cells.Clear();
        for (var i = -2; i <= 2; i++)
        {
            cells.Add(belt + new Vector2(i * 2.2f * u, -7.6f * u));
        }

        WvcEmblem.Voxels(shapes, cells, 1f * u, Silver, alpha, shine);

        // 太い銀の IVF。
        cells.Clear();
        var cell = 0.85f * u;
        var letterLeft = window.X - (((3 * 8) - 1) * cell * 0.5f);
        var column = 0;
        foreach (var ch in "IVF")
        {
            WvcEmblem.Mask(cells, BoldGlyphs[ch], new Vector2(letterLeft + (column * cell), window.Y - (3.5f * cell)), cell, mirror: false);
            column += 8;
        }

        WvcEmblem.Voxels(shapes, cells, cell, Silver, alpha, shine);

        // 下の帯: 紺地にエメラルドの縁、銀の文字。
        var band = center + new Vector2(0f, 7.6f * u);
        var bandSize = new Vector2(38.5f, 2.6f) * u;
        shapes.Add(new UiShapeDraw(band + new Vector2(0.2f * u), bandSize, Vec(Emerald.Side, alpha)));
        shapes.Add(new UiShapeDraw(band, bandSize, Vec(Emerald.Top, alpha), FillColorEnd: Vec(Emerald.Bottom, alpha), GradientAxis: Vector2.UnitY));
        shapes.Add(new UiShapeDraw(band, bandSize - new Vector2(0.5f * u), Navy with { W = alpha }));
        var glyphCell = 0.18f * u;
        if (glyphCell >= 1.4f)
        {
            cells.Clear();
            var wordWidth = ((Title.Length * 5) + ((Title.Length - 1) * 2)) * glyphCell;
            for (var i = 0; i < Title.Length; i++)
            {
                if (SmallGlyphs.TryGetValue(Title[i], out var glyph))
                {
                    WvcEmblem.Mask(cells, glyph, new Vector2(band.X - (wordWidth * 0.5f) + (i * 7 * glyphCell), band.Y - (3.5f * glyphCell)), glyphCell, mirror: false);
                }
            }

            WvcEmblem.Voxels(shapes, cells, glyphCell, Silver, alpha, float.NaN);
        }
        else
        {
            text.Add(new ScreenTextDraw(Title, band - new Vector2(0f, 1.1f * u), 1.9f * u, Vec(Silver.Top, alpha), TextAnchor.Center));
        }
    }

    private static Vector4 Vec(Vector3 rgb, float alpha) => new(rgb, alpha);
}
