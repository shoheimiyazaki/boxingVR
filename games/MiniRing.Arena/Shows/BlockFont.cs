using System.Numerics;
using Aigis.Render;

namespace MiniRing.Arena;

/// <summary>
/// 角張ったボクセルの文字と、斜めの帯（平行四辺形）を、画面の四角形だけで描く道具（VFC の番組の字形）。
/// 回転は使えないので、斜めの帯は細い横の切り口を 1 段ずつずらして積む。文字は 5 × 7 のマス（半角の英数字・記号だけ）。
/// </summary>
internal static class BlockFont
{
    /// <summary>斜めの帯を積む横の切り口の高さ [px]。小さいほど滑らかだが図形が増える。</summary>
    private const float SliceHeight = 4f;

    /// <summary>1 文字の幅（マス）。</summary>
    public const int GlyphWidth = 5;

    /// <summary>1 文字の高さ（マス）。</summary>
    public const int GlyphHeight = 7;

    /// <summary>文字と文字の間（マス）。</summary>
    public const float Gap = 1f;

    private static readonly Dictionary<char, string[]> Glyphs = new()
    {
        ['A'] = [".###.", "#...#", "#...#", "#####", "#...#", "#...#", "#...#"],
        ['B'] = ["####.", "#...#", "#...#", "####.", "#...#", "#...#", "####."],
        ['C'] = [".###.", "#...#", "#....", "#....", "#....", "#...#", ".###."],
        ['D'] = ["####.", "#...#", "#...#", "#...#", "#...#", "#...#", "####."],
        ['E'] = ["#####", "#....", "#....", "####.", "#....", "#....", "#####"],
        ['F'] = ["#####", "#....", "#....", "####.", "#....", "#....", "#...."],
        ['G'] = [".###.", "#...#", "#....", "#.###", "#...#", "#...#", ".###."],
        ['H'] = ["#...#", "#...#", "#...#", "#####", "#...#", "#...#", "#...#"],
        ['I'] = ["#####", "..#..", "..#..", "..#..", "..#..", "..#..", "#####"],
        ['J'] = ["..###", "...#.", "...#.", "...#.", "...#.", "#..#.", ".##.."],
        ['K'] = ["#...#", "#..#.", "#.#..", "##...", "#.#..", "#..#.", "#...#"],
        ['L'] = ["#....", "#....", "#....", "#....", "#....", "#....", "#####"],
        ['M'] = ["#...#", "##.##", "#.#.#", "#.#.#", "#...#", "#...#", "#...#"],
        ['N'] = ["#...#", "##..#", "#.#.#", "#.#.#", "#..##", "#...#", "#...#"],
        ['O'] = [".###.", "#...#", "#...#", "#...#", "#...#", "#...#", ".###."],
        ['P'] = ["####.", "#...#", "#...#", "####.", "#....", "#....", "#...."],
        ['Q'] = [".###.", "#...#", "#...#", "#...#", "#.#.#", "#..#.", ".##.#"],
        ['R'] = ["####.", "#...#", "#...#", "####.", "#.#..", "#..#.", "#...#"],
        ['S'] = [".####", "#....", "#....", ".###.", "....#", "....#", "####."],
        ['T'] = ["#####", "..#..", "..#..", "..#..", "..#..", "..#..", "..#.."],
        ['U'] = ["#...#", "#...#", "#...#", "#...#", "#...#", "#...#", ".###."],
        ['V'] = ["#...#", "#...#", "#...#", "#...#", ".#.#.", ".#.#.", "..#.."],
        ['W'] = ["#...#", "#...#", "#...#", "#.#.#", "#.#.#", "##.##", "#...#"],
        ['X'] = ["#...#", "#...#", ".#.#.", "..#..", ".#.#.", "#...#", "#...#"],
        ['Y'] = ["#...#", "#...#", ".#.#.", "..#..", "..#..", "..#..", "..#.."],
        ['Z'] = ["#####", "....#", "...#.", "..#..", ".#...", "#....", "#####"],
        ['0'] = [".###.", "#...#", "#..##", "#.#.#", "##..#", "#...#", ".###."],
        ['1'] = ["..#..", ".##..", "..#..", "..#..", "..#..", "..#..", ".###."],
        ['2'] = [".###.", "#...#", "....#", "...#.", "..#..", ".#...", "#####"],
        ['3'] = ["####.", "....#", "....#", ".###.", "....#", "....#", "####."],
        ['4'] = ["...#.", "..##.", ".#.#.", "#..#.", "#####", "...#.", "...#."],
        ['5'] = ["#####", "#....", "####.", "....#", "....#", "#...#", ".###."],
        ['6'] = [".###.", "#....", "#....", "####.", "#...#", "#...#", ".###."],
        ['7'] = ["#####", "....#", "...#.", "..#..", ".#...", ".#...", ".#..."],
        ['8'] = [".###.", "#...#", "#...#", ".###.", "#...#", "#...#", ".###."],
        ['9'] = [".###.", "#...#", "#...#", ".####", "....#", "....#", ".###."],
        [':'] = [".....", "..#..", "..#..", ".....", "..#..", "..#..", "....."],
        ['/'] = ["....#", "....#", "...#.", "..#..", ".#...", "#....", "#...."],
        ['.'] = [".....", ".....", ".....", ".....", ".....", ".##..", ".##.."],
        ['-'] = [".....", ".....", ".....", "#####", ".....", ".....", "....."],
        ['!'] = ["..#..", "..#..", "..#..", "..#..", "..#..", ".....", "..#.."],
        ['<'] = ["...#.", "..#..", ".#...", "#....", ".#...", "..#..", "...#."],
        ['>'] = [".#...", "..#..", "...#.", "....#", "...#.", "..#..", ".#..."],
        [' '] = [".....", ".....", ".....", ".....", ".....", ".....", "....."],
    };

    /// <summary>文字列の横幅 [px]。</summary>
    public static float Width(string text, float cell)
    {
        ArgumentNullException.ThrowIfNull(text);
        return text.Length == 0 ? 0f : ((text.Length * GlyphWidth) + ((text.Length - 1) * Gap)) * cell;
    }

    /// <summary>文字列の高さ [px]。</summary>
    public static float Height(float cell) => GlyphHeight * cell;

    /// <summary>
    /// 角張った文字を描く。<paramref name="origin"/> は文字列の左上、<paramref name="cell"/> は 1 マス [px]、
    /// <paramref name="lean"/> は上の段を右へずらす 1 段あたりのマス数（0.25 ほどで斜体）。知らない文字は空白。
    /// </summary>
    public static void Draw(List<UiShapeDraw> shapes, string text, Vector2 origin, float cell, Vector4 color, float lean = 0f)
    {
        ArgumentNullException.ThrowIfNull(shapes);
        ArgumentNullException.ThrowIfNull(text);
        for (var i = 0; i < text.Length; i++)
        {
            var glyph = Glyphs.GetValueOrDefault(char.ToUpperInvariant(text[i]), Glyphs[' ']);
            var left = origin.X + (i * (GlyphWidth + Gap) * cell);
            for (var row = 0; row < GlyphHeight; row++)
            {
                var shift = (GlyphHeight - 1 - row) * lean * cell;
                var col = 0;
                while (col < GlyphWidth)
                {
                    if (glyph[row][col] != '#')
                    {
                        col++;
                        continue;
                    }

                    var end = col;
                    while (end < GlyphWidth && glyph[row][end] == '#')
                    {
                        end++;
                    }

                    var width = (end - col) * cell;
                    shapes.Add(new UiShapeDraw(new Vector2(left + shift + (col * cell) + (width * 0.5f), origin.Y + ((row + 0.5f) * cell)), new Vector2(width, cell), color));
                    col = end;
                }
            }
        }
    }

    /// <summary>文字列の左上を、基準の点の左 / 中央 / 右に合わせる。</summary>
    public static Vector2 Anchor(string text, Vector2 at, float cell, TextAnchor anchor) => anchor switch
    {
        TextAnchor.Center => at - new Vector2(Width(text, cell) * 0.5f, 0f),
        TextAnchor.Right => at - new Vector2(Width(text, cell), 0f),
        _ => at,
    };

    /// <summary>
    /// 斜めの帯（平行四辺形）。左右の辺の x は上の端が <paramref name="x0"/>・<paramref name="x1"/> で、下の端へ向かって <paramref name="skew"/> [px] だけ左へずれる
    /// （正の値で「/」の向き）。<paramref name="top"/>・<paramref name="bottom"/> は帯の縦の範囲 [px]。
    /// </summary>
    public static void Slab(List<UiShapeDraw> shapes, float x0, float x1, float top, float bottom, float skew, Vector4 color)
    {
        ArgumentNullException.ThrowIfNull(shapes);
        if (bottom <= top || x1 <= x0 || color.W <= 0f)
        {
            return;
        }

        var height = bottom - top;
        if (MathF.Abs(skew) < 0.5f)
        {
            shapes.Add(new UiShapeDraw(new Vector2((x0 + x1) * 0.5f, (top + bottom) * 0.5f), new Vector2(x1 - x0, height), color));
            return;
        }

        var count = Math.Max(1, (int)MathF.Ceiling(height / SliceHeight));
        var step = height / count;
        for (var i = 0; i < count; i++)
        {
            var fraction = (i + 0.5f) / count;
            var shift = -skew * fraction; // 下へ行くほど左へ（skew > 0 なら「/」）
            // 継ぎ目の隙間を消すため、隣の切り口と少し重ねる。
            shapes.Add(new UiShapeDraw(new Vector2(((x0 + x1) * 0.5f) + shift, top + ((i + 0.5f) * step)), new Vector2(x1 - x0, step + 0.8f), color));
        }
    }
}
