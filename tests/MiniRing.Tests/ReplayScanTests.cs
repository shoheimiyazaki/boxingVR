using MiniRing.Bots;
using MiniRing.Replay;

namespace MiniRing.Tests;

/// <summary>
/// リプレイの走査（<see cref="ReplayScanner"/>）: 貯めた場面へシークしたときの状態が、先頭から再生した試合のその時点とビット単位で同じか。
/// 場面を間引いても音と順序を失わないか。展開の記録が試合の出来事と数で合うか。
/// </summary>
[TestClass]
[TestCategory("slow")]
public sealed class ReplayScanTests
{
    private static ReplayData? _bout;

    /// <summary>ボットどうしの 8 秒の試合を記録する（テストの間で使い回す）。</summary>
    private static ReplayData Bout()
    {
        if (_bout is { } cached) { return cached; }
        var corners = BotCatalog.Corners("swarmer") with { RedBotStyle = BotCatalog.Style("champion") };
        using var game = new BoxingGame(corners: corners, seed: 5);
        using var stream = new MemoryStream();
        using (var recorder = new ReplayRecorder(stream, game, "test"))
        {
            for (var i = 0; i < 240 * 8; i++) { recorder.Step(game, default, 1f / 240f); }
        }

        return _bout = ReplayFile.Read(new MemoryStream(stream.ToArray()));
    }

    [TestMethod]
    public void SeekedFrame_EqualsTheMatchPlayedFromTheStart()
    {
        var data = Bout();
        using var scanner = new ReplayScanner(data);
        scanner.ScanAll();
        Assert.IsTrue(scanner.Completed);
        Assert.IsNull(scanner.FirstDivergence, "走査は記録のチェックサムと一致する");
        Assert.AreEqual(scanner.Duration, scanner.ScannedTime, "走査の時計と記録の長さは同じ足し方（終わりで止まれる）");

        // 再生ヘッドを適当な時刻へ飛ばし、そこに出る場面を、先頭から再生した別の試合のその歩と比べる。
        var transport = new ReplayTransport(scanner.Duration);
        var times = new[] { 7.9f, 0.4f, 3.3f, 5.05f, 1.75f };
        var frames = new List<ReplayFrame>();
        foreach (var time in times)
        {
            transport.Seek(time, scanner.ScannedTime);
            var frame = scanner.Frames.Nearest(transport.Time);
            Assert.IsNotNull(frame);
            Assert.IsTrue(MathF.Abs(frame.Time - time) <= scanner.Frames.Interval * 0.5f + 1e-4f, $"{time}s の近くの場面が出る（出たのは {frame.Time}s）");
            frames.Add(frame);
        }

        using var fresh = new ReplayPlayer(data);
        foreach (var frame in frames.OrderBy(f => f.Step))
        {
            while (fresh.Position < frame.Step) { fresh.StepOnce(); }
            Assert.AreEqual(fresh.Game.StateChecksum(), frame.Checksum, $"歩 {frame.Step} の状態");
            var (instances, _) = fresh.Game.BuildRenderables();
            CollectionAssert.AreEqual(instances.ToArray(), frame.Instances, $"歩 {frame.Step} の描画");
        }
    }

    [TestMethod]
    public void Decimating_KeepsEverySoundInOrder()
    {
        var data = Bout();
        using var full = new ReplayScanner(data);
        full.ScanAll();
        // 上限をとても小さくして何度も間引かせる。
        using var thin = new ReplayScanner(data, new ReplayScanOptions(BudgetBytes: 8L * 1024 * 1024));
        thin.ScanAll();
        Assert.IsTrue(thin.Frames.Interval > full.Frames.Interval * 3.9f, $"間引いて間隔が広がる（{full.Frames.Interval} → {thin.Frames.Interval}）");
        Assert.IsTrue(thin.Frames.EstimatedBytes <= thin.Frames.BudgetBytes, $"{thin.Frames.EstimatedBytes} bytes in {thin.Frames.Count} frames, full {full.Frames.EstimatedBytes} in {full.Frames.Count}");

        static (int Cues, int Swings, float Dt) Sum(ReplayScanner s)
        {
            var frames = s.Frames.Between(-1f, s.Duration + 1f);
            for (var i = 1; i < frames.Count; i++) { Assert.IsTrue(frames[i].Time > frames[i - 1].Time); }
            return (frames.Sum(f => f.Sound.Cues.Length), frames.Sum(f => f.Sound.Swings.Length), frames.Sum(f => f.Sound.Dt));
        }

        var a = Sum(full);
        var b = Sum(thin);
        Assert.IsTrue(a.Cues > 0, "この試合では音が鳴る");
        Assert.AreEqual(a.Cues, b.Cues);
        Assert.AreEqual(a.Swings, b.Swings);
        Assert.AreEqual(a.Dt, b.Dt, 1e-3f, "音の時間の和は試合の長さ");
        Assert.AreEqual(full.Duration, a.Dt, 1e-3f);
    }

    [TestMethod]
    public void Timeline_CountsTheMatchHits_AndCoversTheMatch()
    {
        var data = Bout();
        using var scanner = new ReplayScanner(data);
        scanner.ScanAll();
        var (samples, marks, _) = scanner.Timeline.Snapshot();

        var hits = 0;
        using (var player = new ReplayPlayer(data))
        {
            while (!player.Finished)
            {
                player.StepOnce();
                hits += Highlights.HighlightSource.Hits(player.Game).Count();
            }
        }

        Assert.IsTrue(hits > 0, "打ち合う試合を選んでいる");
        Assert.AreEqual(hits, marks.Count(m => m.Kind == ReplayMarkKind.Hit), "打撃の印は試合の打撃と同じ数");
        Assert.AreEqual(0f, marks.First(m => m.Kind == ReplayMarkKind.RoundStart).Time, 1e-4f, "最初のラウンドの印は始まり");
        Assert.AreEqual(0f, samples[0].Time, 1e-4f);
        Assert.IsTrue(samples[^1].Time > scanner.Duration - (2f * ReplayTimeline.SampleInterval), "状態の記録は終わりまで");
        Assert.IsTrue(samples.All(s => s.Red.Body is > 0f and <= 1f && s.Blue.Consciousness is >= 0f and <= 1f && s.RedWinChance is >= 0f and <= 1f));
        for (var i = 1; i < marks.Count; i++) { Assert.IsTrue(marks[i].Time >= marks[i - 1].Time, "印は時刻の順"); }
    }
}
