using System.Numerics;
using Aigis.Render;

namespace MiniRing.Tests;

/// <summary>
/// UI の文字の DotGothic16（2026-11-26、美咲から替えた）: 字形が 16 ドットの格子に乗り、描くときは 16 px の整数倍（最小 16 px）で、
/// 1 ドットが n×n の正方形（ニアレスト、ぼかさない）になること。DotGothic16 に無い字は OS のフォントで補い、何かは描かれること（空白にならない）。
/// </summary>
[TestClass]
public sealed class UiFontTests
{
    [TestMethod]
    public void Glyphs_SitOnTheSixteenDotGrid_FullAndHalfWidth()
    {
        var font = UiFont.Ensure();
        Assert.AreEqual(16, font.Glyph('あ').Advance);
        Assert.AreEqual(16, font.Glyph('漢').Advance);
        Assert.AreEqual(8, font.Glyph('A').Advance, "ASCII is half width (8×16)");
        foreach (var c in "あ漢A1gキャンペーン減量")
        {
            var glyph = font.Glyph(c);
            Assert.IsTrue(font.Has(c), $"DotGothic16 has '{c}'");
            Assert.IsTrue(glyph.Runs.Length > 0, $"'{c}' has dots");
            Assert.IsTrue(glyph.Runs.All(r => r.Y is >= 0 and < UiFont.Cell && r.X >= 0 && r.X + r.Width <= glyph.Advance), $"'{c}' stays inside its cell");
        }

        // 縦の位置: 漢字は上端近く（行 0〜2）から始まり、g の下がりは下端近く（行 13〜15）まで。格子が上下にずれて欠けていないこと。
        Assert.IsTrue(font.Glyph('漢').Runs.Min(r => r.Y) <= 2, "kanji starts near the top of the cell");
        Assert.IsTrue(font.Glyph('g').Runs.Max(r => r.Y) >= 13, "the descender of g is kept");
    }

    [TestMethod]
    public void Draw_IsAWholeMultipleOfSixteen_AndEveryDotIsASquareOnWholePixels()
    {
        UiFont.Ensure();
        Assert.AreEqual(32, UiFont.Height(30f));
        Assert.AreEqual(16, UiFont.Height(3f), "never below 1× (16 px)");
        Assert.AreEqual(16, UiFont.Height(23f));
        Assert.AreEqual(48, UiFont.Height(44f));
        var quads = ScreenText.BuildQuads([UiFont.Text("試合 12", new Vector2(10.4f, 20.6f), 46f, Vector4.One)]);
        Assert.IsTrue(quads.Count > 0);
        foreach (var q in quads)
        {
            Assert.AreEqual(3f, q.SizePixels.Y, "one dot = 3 px tall at 48 px");
            Assert.AreEqual(0f, q.SizePixels.X % 3f, "run widths are whole dots");
            var left = q.CenterPixels.X - (q.SizePixels.X / 2);
            var top = q.CenterPixels.Y - (q.SizePixels.Y / 2);
            Assert.AreEqual(MathF.Round(left), left, 1e-4f, "on whole pixels (no resampling)");
            Assert.AreEqual(MathF.Round(top), top, 1e-4f);
        }

        Assert.AreEqual((16 + 16 + 8 + 8 + 8) * 3f, UiFont.Width("試合 12", 46f));
    }

    [TestMethod]
    public void MenuFit_NeverShrinksBelowOneTimes_AndNamesWrapAtTheDots()
    {
        // 1 倍（16 px）でも収まらない文は縮めない（OS のフォントにも替えない）: 16 px のまま末尾を切る（visual テストが 0 件を確かめる直し漏れの印）。
        var (pixels, text) = MiniRing.Desktop.MenuText.Fit("とても長い説明の文です", 40f, 16f * 4);
        Assert.AreEqual(16f, pixels);
        Assert.IsTrue(text.EndsWith(MiniRing.Desktop.MenuText.Cut) && UiFont.Width(text, 16f) <= 64f, text);

        // 名前は「・」で折り、引用符の中の通り名は分けない。
        var lines = MiniRing.Desktop.MenuText.WrapName("ラモン・\"エル・トロ\"・カスティージョ", 180f, 16f).ToList();
        CollectionAssert.AreEqual(new[] { "ラモン・\"エル・トロ\"・", "カスティージョ" }, lines);
        Assert.IsTrue(lines.All(l => UiFont.Width(l, 16f) <= 180f));
    }

    [TestMethod]
    public void MissingCharacters_FallBack_ToSomethingVisible()
    {
        var font = UiFont.Ensure();
        var glyph = font.Glyph('◆');
        Assert.IsTrue(glyph.Runs.Length > 0 && glyph.Advance > 0);
        // 無い字の例として私用領域の字を使う（どのフォントにも形が無くても、幅は格子の 1 文字分を持ち、格子の外へ出ない）。
        var missing = font.Glyph('');
        Assert.IsFalse(font.Has(''));
        Assert.IsTrue(missing.Runs.All(r => r.Y is >= 0 and < UiFont.Cell));
    }
}
