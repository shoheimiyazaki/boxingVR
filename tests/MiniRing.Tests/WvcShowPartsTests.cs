using System.Numerics;
using Aigis.Render;
using MiniRing.Arena;
using MiniRing.Arena.Shows.Wvc;
using MiniRing.Characters;

namespace MiniRing.Tests;

/// <summary>WVC だけの番組の部品（リングアナウンサーのコール・ラウンドの採点の札・王座のベルト）の入力の検査。</summary>
[TestClass]
public sealed class WvcShowPartsTests
{
    private const int W = 1280;
    private const int H = 720;

    private static TvCorner Marcus() => TvCorner.Of(CharacterRoster.Get("marcus"));

    private static void AssertInside(IReadOnlyList<ScreenTextDraw> text, UiShapeDraw panel)
    {
        var left = panel.CenterPixels.X - (panel.SizePixels.X * 0.5f);
        var right = panel.CenterPixels.X + (panel.SizePixels.X * 0.5f);
        foreach (var d in text)
        {
            var w = ScreenText.MeasurePixelWidth(d.Text, d.PixelHeight);
            var (a, b) = d.Anchor switch
            {
                TextAnchor.Center => (d.ScreenPosPixels.X - (w * 0.5f), d.ScreenPosPixels.X + (w * 0.5f)),
                TextAnchor.Right => (d.ScreenPosPixels.X - w, d.ScreenPosPixels.X),
                _ => (d.ScreenPosPixels.X, d.ScreenPosPixels.X + w),
            };
            Assert.IsTrue(a >= left - 0.5f && b <= right + 0.5f, $"「{d.Text}」が板からはみ出した（{a:0}〜{b:0} / 板 {left:0}〜{right:0}）");
        }
    }

    [TestMethod]
    public void Call_ShowsRecordWeightAndNames_AndStaysInsideThePanel_EvenWithALongName()
    {
        var corner = Marcus();
        var (text, shapes) = WvcShowParts.RingAnnouncerCall(corner, 1, 5f, W, H);

        Assert.IsTrue(text.Any(d => d.Text == "赤コーナー"));
        Assert.IsTrue(text.Any(d => d.Text.Contains('勝') && d.Text.Contains("K.O.", StringComparison.Ordinal)), "戦績");
        Assert.IsTrue(text.Any(d => d.Text.StartsWith("体重", StringComparison.Ordinal)));
        Assert.IsTrue(text.Any(d => d.Text == corner.FullName));
        AssertInside(text, shapes[1]);

        var longCorner = corner with { Name = new string('あ', 40), FullName = new string('ア', 60), Discipline = new string('流', 50) };
        var (longText, longShapes) = WvcShowParts.RingAnnouncerCall(longCorner, -1, 5f, W, H);
        Assert.IsTrue(longText.Any(d => d.Text == "青コーナー"));
        AssertInside(longText, longShapes[1]);
    }

    [TestMethod]
    public void Call_RecordSaysWvcStyle_AndOmitsDrawsWhenZero()
    {
        var corner = new TvCorner("A", "ボクシング", "", "本名", new CharacterProfile { WeightKg = 70, FightRecord = new FightRecord { Wins = 17, Kos = 12, Losses = 2, Draws = 1 } });
        Assert.AreEqual("17勝 12K.O. 2敗 1分", WvcShowParts.Record(corner));
        var noDraw = corner with { Profile = new CharacterProfile { FightRecord = new FightRecord { Wins = 3, Kos = 1, Losses = 0 } } };
        Assert.AreEqual("3勝 1K.O. 0敗", WvcShowParts.Record(noDraw));
        Assert.AreEqual("", WvcShowParts.Record(new TvCorner("B", "空手", "")), "資料が無ければ戦績は出さない");
    }

    [TestMethod]
    public void Call_StartAndEnd_DoNotBreak_AndOutsideTheRangeIsEmpty()
    {
        var corner = new TvCorner("名前だけ", "", "");
        foreach (var t in new[] { 0f, 0.01f, WvcShowParts.CallSeconds - 0.01f })
        {
            var (text, shapes) = WvcShowParts.RingAnnouncerCall(corner, 1, t, W, H);
            Assert.IsTrue(shapes.Count > 0, $"t={t}");
            Assert.IsTrue(text.All(d => float.IsFinite(d.ScreenPosPixels.X) && float.IsFinite(d.ScreenPosPixels.Y)));
        }

        Assert.AreEqual(0, WvcShowParts.RingAnnouncerCall(corner, 1, -0.1f, W, H).Shapes.Count);
        Assert.AreEqual(0, WvcShowParts.RingAnnouncerCall(corner, 1, WvcShowParts.CallSeconds + 0.1f, W, H).Shapes.Count);
        Assert.AreEqual(0, WvcShowParts.RingAnnouncerCall(corner, 1, float.NaN, W, H).Shapes.Count);
    }

    [TestMethod]
    public void Call_TextAppearsInOrder()
    {
        var corner = Marcus();
        var early = WvcShowParts.RingAnnouncerCall(corner, 1, 0.9f, W, H).Text.Count;
        var later = WvcShowParts.RingAnnouncerCall(corner, 1, 3.5f, W, H).Text.Count;
        var done = WvcShowParts.RingAnnouncerCall(corner, 1, 5f, W, H).Text.Count;
        Assert.IsTrue(early < later && later <= done, $"{early} {later} {done}");
    }

    [TestMethod]
    public void RoundCard_EmptyRounds_ReturnsNothing()
    {
        var (text, shapes) = WvcShowParts.RoundScoreCard([], "赤", "青", 3f, W, H);
        Assert.AreEqual(0, text.Count);
        Assert.AreEqual(0, shapes.Count);
    }

    [TestMethod]
    public void RoundCard_ShowsEachRoundAndTheTotals_WithoutInventingJudgeScores()
    {
        WvcRoundTally[] rounds = [new(120.4f, 90f, 14, 11), new(80f, 150.6f, 9, 17)];
        var (text, shapes) = WvcShowParts.RoundScoreCard(rounds, "ザ・ハンマー", "アキラ", 6f, W, H);

        Assert.IsTrue(text.Any(d => d.Text == "R1"));
        Assert.IsTrue(text.Any(d => d.Text == "R2"));
        Assert.IsTrue(text.Any(d => d.Text == "計"));
        Assert.IsTrue(text.Any(d => d.Text == "200"), "赤の合計 120.4 + 80");
        Assert.IsTrue(text.Any(d => d.Text == "241"), "青の合計 90 + 150.6");
        Assert.IsTrue(text.Any(d => d.Text == "（23発）") && text.Any(d => d.Text == "（28発）"), "当てた数の合計");
        AssertInside(text, shapes[1]);
    }

    [TestMethod]
    public void RoundCard_ManyRoundsAndLongNames_StayInsideAndCapRows()
    {
        var rounds = Enumerable.Range(0, 20).Select(i => new WvcRoundTally(1234567f * (i + 1), 99999999f, 1000 + i, 123456)).ToArray();
        var (text, shapes) = WvcShowParts.RoundScoreCard(rounds, new string('赤', 40), new string('B', 60), 6f, W, H);

        Assert.AreEqual(WvcShowParts.MaxRows, text.Count(d => d.Text.StartsWith('R') && d.Text.Length > 1 && char.IsDigit(d.Text[1])));
        Assert.IsTrue(text.Any(d => d.Text == "R20"), "新しい方を残す");
        AssertInside(text, shapes[1]);
        var panel = shapes[1];
        Assert.IsTrue(panel.CenterPixels.Y + (panel.SizePixels.Y * 0.5f) <= H, "画面の中");
    }

    [TestMethod]
    public void RoundCard_StartAndEnd_DoNotBreak()
    {
        WvcRoundTally[] rounds = [new(0f, 0f, 0, 0)];
        foreach (var t in new[] { 0f, WvcShowParts.RoundCardSeconds - 0.01f })
        {
            var (text, shapes) = WvcShowParts.RoundScoreCard(rounds, "赤", "青", t, W, H);
            Assert.IsTrue(shapes.Count > 0, $"t={t}");
            Assert.IsTrue(text.All(d => float.IsFinite(d.ScreenPosPixels.X) && float.IsFinite(d.PixelHeight)));
        }

        Assert.AreEqual(0, WvcShowParts.RoundScoreCard(rounds, "赤", "青", WvcShowParts.RoundCardSeconds + 1f, W, H).Shapes.Count);
    }

    [TestMethod]
    public void Belt_HiddenWhenNotShown_AndStaysInsideItsBoxOnceSettled()
    {
        Assert.AreEqual(0, WvcShowParts.ChampionBelt(false, 3f, new Vector2(640f, 300f), 6f).Shapes.Count);
        Assert.AreEqual(0, WvcShowParts.ChampionBelt(true, 0f, new Vector2(640f, 300f), 6f).Shapes.Count, "時刻 0 は透明");
        Assert.AreEqual(0, WvcShowParts.ChampionBelt(true, 3f, new Vector2(640f, 300f), 0f).Shapes.Count);

        var center = new Vector2(640f, 300f);
        const float Cell = 6f;
        foreach (var t in new[] { 0.01f, 1f, 3f, 100f })
        {
            var (_, shapes) = WvcShowParts.ChampionBelt(true, t, center, Cell);
            Assert.IsTrue(shapes.Count > 500, $"t={t} ボクセルで積む");
            Assert.IsTrue(shapes.All(s => float.IsFinite(s.CenterPixels.X) && float.IsFinite(s.CenterPixels.Y)));
        }

        var (text, settled) = WvcShowParts.ChampionBelt(true, 3f, center, Cell);
        var halfW = (WvcShowParts.BeltColumns * Cell * 0.5f) + Cell;
        var halfH = (WvcShowParts.BeltRows * Cell * 0.5f) + Cell;
        Assert.IsTrue(settled.All(s => MathF.Abs(s.CenterPixels.X - center.X) <= halfW && MathF.Abs(s.CenterPixels.Y - center.Y) <= halfH), "ベルトの箱の外へ描かない");
        Assert.IsTrue(text.Count > 0, "紋章の文字（小さいとき）が付く場合は text にある。0 でもよいが壊れない");
    }
}
