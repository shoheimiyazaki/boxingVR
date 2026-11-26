using System.Numerics;
using Aigis.Render;

namespace MiniRing;

/// <summary>
/// UI（メニュー・試合の HUD）の文字の字形: 美咲フォント（8×8 ドット、門真なむ氏。<c>Assets/Fonts/misaki_gothic.ttf</c>、出典は docs/REUSE.md）。
/// 8 px の整数倍で、拡大はニアレスト（<see cref="ScreenText"/> の <see cref="TextFont.Pixel"/>）。美咲に無い字（◀ ▶ など JIS X 0208 の外）は
/// OS の日本語のフォントを同じ 8 px の格子で 1 ビットに描いて補う（<see cref="PixelFont"/>）。描画だけで、試合の計算には響かない。
/// テレビ中継の画面（TvHud・TV の動画）はこれを使わない（OS のフォントのまま）。
/// </summary>
public static class UiFont
{
    /// <summary>美咲フォントの 1 文字の高さ [ドット]（全角 8×8、半角 4×8）。</summary>
    public const int Cell = 8;

    private const string Resource = "MiniRing.Assets.Fonts.misaki_gothic.ttf";

    private static readonly Lazy<PixelFont> Font = new(() =>
    {
        using var stream = typeof(UiFont).Assembly.GetManifestResourceStream(Resource)
            ?? throw new InvalidOperationException($"組み込みのフォント {Resource} がありません（MiniRing.csproj の EmbeddedResource）。");
        using var copy = new MemoryStream();
        stream.CopyTo(copy);
        var font = PixelFont.FromTrueType(copy.ToArray(), Cell);
        ScreenText.PixelFont = font; // UI の幅の測り（UiFont.Width）と描画が同じ字形になるように、置き換える
        return font;
    });

    /// <summary>美咲フォントを読み込んで <see cref="ScreenText.PixelFont"/> に置く（何度呼んでも 1 度だけ）。</summary>
    public static PixelFont Ensure() => Font.Value;

    /// <summary>UI の文字（美咲フォント）の描画の 1 つ。</summary>
    public static ScreenTextDraw Text(string text, Vector2 at, float pixelHeight, Vector4 color, TextAnchor anchor = TextAnchor.Left)
    {
        Ensure();
        return new ScreenTextDraw(text, at, pixelHeight, color, anchor, TextFont.Pixel);
    }

    /// <summary><see cref="Text"/> で描いたときの幅 [px]。</summary>
    public static float Width(string text, float pixelHeight)
    {
        Ensure();
        return ScreenText.MeasurePixelWidth(text, pixelHeight, TextFont.Pixel);
    }

    /// <summary><paramref name="pixelHeight"/> を頼んだときに実際に描かれる高さ [px]（8 の整数倍）。</summary>
    public static int Height(float pixelHeight) => Ensure().ScaleFor(pixelHeight) * Cell;
}
