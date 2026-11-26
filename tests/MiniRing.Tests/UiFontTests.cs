using System.Numerics;
using Aigis.Render;

namespace MiniRing.Tests;

/// <summary>
/// UI の文字の美咲フォント（2026-11-26）: 字形が 8×8 の格子に乗り、描くときは 8 px の整数倍で、1 ドットが n×n の正方形（ニアレスト、ぼかさない）になること。
/// 美咲に無い字は OS のフォントで補い、何かは描かれること（空白にならない）。
/// </summary>
[TestClass]
public sealed class UiFontTests
{
    [TestMethod]
    public void Glyphs_SitOnTheEightDotGrid_FullAndHalfWidth()
    {
        var font = UiFont.Ensure();
        Assert.AreEqual(8, font.Glyph('あ').Advance);
        Assert.AreEqual(8, font.Glyph('漢').Advance);
        Assert.AreEqual(4, font.Glyph('A').Advance, "ASCII is half width (4×8)");
        foreach (var c in "あ漢A1キャンペーン")
        {
            var glyph = font.Glyph(c);
            Assert.IsTrue(glyph.Runs.Length > 0, $"'{c}' has dots");
            Assert.IsTrue(glyph.Runs.All(r => r.Y is >= 0 and < UiFont.Cell && r.X >= 0 && r.X + r.Width <= glyph.Advance), $"'{c}' stays inside its cell");
        }
    }

    [TestMethod]
    public void Draw_IsAWholeMultipleOfEight_AndEveryDotIsASquareOnWholePixels()
    {
        UiFont.Ensure();
        Assert.AreEqual(16, UiFont.Height(15f));
        Assert.AreEqual(8, UiFont.Height(3f), "never below 1×");
        Assert.AreEqual(24, UiFont.Height(22f));
        var quads = ScreenText.BuildQuads([UiFont.Text("試合 12", new Vector2(10.4f, 20.6f), 23f, Vector4.One)]);
        Assert.IsTrue(quads.Count > 0);
        foreach (var q in quads)
        {
            Assert.AreEqual(3f, q.SizePixels.Y, "one dot = 3 px tall at 24 px");
            Assert.AreEqual(0f, q.SizePixels.X % 3f, "run widths are whole dots");
            var left = q.CenterPixels.X - (q.SizePixels.X / 2);
            var top = q.CenterPixels.Y - (q.SizePixels.Y / 2);
            Assert.AreEqual(MathF.Round(left), left, 1e-4f, "on whole pixels (no resampling)");
            Assert.AreEqual(MathF.Round(top), top, 1e-4f);
        }

        Assert.AreEqual((8 + 8 + 4 + 4 + 4) * 3f, UiFont.Width("試合 12", 23f));
    }

    [TestMethod]
    public void MissingCharacters_FallBack_ToSomethingVisible()
    {
        var font = UiFont.Ensure();
        Assert.IsFalse(font.Has('◀'), "the dot font has no ◀ (outside JIS X 0208)");
        var glyph = font.Glyph('◀');
        Assert.IsTrue(glyph.Runs.Length > 0 && glyph.Advance > 0, "drawn from the OS font on the same grid");
        Assert.IsTrue(glyph.Runs.All(r => r.Y is >= 0 and < UiFont.Cell));
    }
}
