using System.Text.Json;
using MiniRing.Audio;

namespace MiniRing.Tests;

/// <summary>
/// 中継の記録（events.json）の倒れた・立った・カウントの言葉は、団体の決まりに従う: ダウンを数える団体（WVC）はダウンとカウントを書き、
/// 数えない団体（VFC）は「倒れた」「立ち上がった」だけを書いてカウントの出来事を出さない（記録を読む実況の書き手が数えてしまった、2026-11-19 の VFC の番組）。
/// </summary>
[TestClass]
[TestCategory("slow")]
public sealed class BroadcastDownWordsTests
{
    private const float Dt = 1f / 60f;

    [TestMethod]
    public void AFallInVfc_IsLoggedWithoutDownOrCount()
    {
        var events = Record(MatchRules.Vfc);
        var fall = events.First(e => e.Type == "knockdown" && e.For == "実況");
        Assert.AreEqual("青が倒れた", fall.Text);
        Assert.IsFalse(events.Any(e => e.Type == "count"), "数えない団体にカウントの出来事は無い");
        var counted = events.Where(e => e.Text.Contains("カウント", StringComparison.Ordinal) || e.Text.Contains("ダウン", StringComparison.Ordinal)).Select(e => e.Text).ToList();
        Assert.AreEqual(0, counted.Count, string.Join(" / ", counted));
    }

    [TestMethod]
    public void AFallInWvc_IsADownWithACount()
    {
        var events = Record(MatchRules.Tv);
        Assert.AreEqual("青、ダウン", events.First(e => e.Type == "knockdown" && e.For == "実況").Text);
        Assert.IsTrue(events.Any(e => e.Type == "count"), "ダウンを数える団体はカウントを書く");
    }

    /// <summary>構えて立つだけの 2 人の青を気絶させて倒し、10 秒の記録を取る。</summary>
    private static List<(string Type, string For, string Text)> Record(MatchRules rules)
    {
        var broadcast = new Broadcast("赤", "青");
        using var game = new BoxingGame(seed: 1, corners: new BoxingCorners("still", _ => new StandGuard(), _ => new StandGuard()), rules: rules);
        var step = 0;
        var t = 0f;
        for (; t < 2.518f; t += Dt, step++)
        {
            game.Step(default, Dt);
            broadcast.Observe(game, t, step);
        }

        game.Cpu.Concussion = 0.95f; // 意識が飛んだ: すぐ倒れて数えられる
        for (; t < 12.59f; t += Dt, step++)
        {
            game.Step(default, Dt);
            broadcast.Observe(game, t, step);
        }

        var path = Path.Combine(Path.GetTempPath(), $"downs-{Guid.NewGuid():N}.events.json");
        try
        {
            broadcast.WriteEvents(path);
            using var doc = JsonDocument.Parse(File.ReadAllText(path));
            return [.. doc.RootElement.GetProperty("events").EnumerateArray()
                .Select(e => (e.GetProperty("type").GetString() ?? "", e.GetProperty("for").GetString() ?? "", e.GetProperty("text").GetString() ?? ""))];
        }
        finally
        {
            File.Delete(path);
        }
    }
}
