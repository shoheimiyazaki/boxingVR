using System.Globalization;
using System.Text;
using MiniRing.Bots;

namespace MiniRing.Highlights;

/// <summary>
/// 1 試合の面白さのまとめ（docs/EXCITEMENT.md）。
/// <list type="bullet">
/// <item><b>派手さ</b>（<see cref="Spectacle"/>）= (Σ 重み[種類] × min(強さ, 上限) + 生のダメージ × 軽い重み) ÷ 分。</item>
/// <item><b>ドラマ</b>（<see cref="Drama"/>）= 観客の見込みの驚きとはらはら（<see cref="Highlights.Drama"/>）。</item>
/// <item><b>緩急</b>（<see cref="Pacing"/>）= 慣れのある興奮の曲線の形（<see cref="Highlights.Arousal"/>）。</item>
/// <item><b>総合</b>（<see cref="Score"/>）= 派手さ × w_s + ドラマ × w_d + 緩急 × w_p（<see cref="ExcitementTuning"/>）。</item>
/// </list>
/// 退屈な時間（<see cref="DeadSeconds"/>）= 出来事と出来事の間が <see cref="ExcitementTuning.DeadGapSeconds"/> を超えた間の合計（参考の値）。
/// </summary>
public sealed record ExcitementSummary(IReadOnlyDictionary<HighlightKind, int> Counts, float EventPoints, float RawDamage, float Seconds,
    float DeadSeconds, float LongestGap, float Spectacle, float Drama, float Pacing, float Score,
    float Surprise, float MeanSuspense, float LargestSwing, int LeadChanges,
    float BoredShare, float LongestBored, float PeaksPerMinute, float MeanPeak)
{
    /// <summary>曲線（1 試合のとき）。まとめ（Mean）では null。</summary>
    public Drama? DramaCurve { get; init; }

    public Arousal? ArousalCurve { get; init; }

    public int Count(HighlightKind kind) => Counts.GetValueOrDefault(kind);

    public float DeadShare => Seconds > 0f ? DeadSeconds / Seconds : 0f;

    public static ExcitementSummary From(IReadOnlyList<Highlight> events, float rawDamage, float origin, float seconds, IReadOnlyList<(float Time, float P)> belief, ExcitementTuning t)
    {
        ArgumentNullException.ThrowIfNull(events);
        ArgumentNullException.ThrowIfNull(belief);
        ArgumentNullException.ThrowIfNull(t);
        var counts = Enum.GetValues<HighlightKind>().ToDictionary(k => k, k => events.Count(e => e.Kind == k));
        var points = events.Sum(e => t.Weights.GetValueOrDefault(e.Kind) * MathF.Min(e.Intensity, t.IntensityCap));
        var times = events.Select(e => e.Time).Order().ToList();
        var previous = origin;
        var dead = 0f;
        var longest = 0f;
        foreach (var at in times.Append(origin + seconds))
        {
            var gap = MathF.Max(0f, at - previous);
            longest = MathF.Max(longest, gap);
            dead += gap > t.DeadGapSeconds ? gap : 0f;
            previous = MathF.Max(previous, at);
        }

        var minutes = MathF.Max(seconds, 1f) / 60f;
        var spectacle = (points + (rawDamage * t.RawDamageWeight)) / minutes;
        var drama = Highlights.Drama.From(belief, seconds, t);
        var arousal = Highlights.Arousal.From(events, belief, origin, seconds, t);
        var score = (t.SpectacleWeight * spectacle) + (t.DramaWeight * drama.Score) + (t.PacingWeight * arousal.Score);
        return new ExcitementSummary(counts, points, rawDamage, seconds, dead, longest, spectacle, drama.Score, arousal.Score, score,
            drama.Surprise, drama.MeanSuspense, drama.LargestSwing, drama.LeadChanges,
            arousal.BoredShare, arousal.LongestBored, arousal.PeaksPerMinute(seconds), arousal.MeanPeak)
        { DramaCurve = drama, ArousalCurve = arousal };
    }

    /// <summary>何試合かのまとめ: 出来事の数は合計、ほかは 1 試合あたりの平均（最長の間・最長の退屈は最大）。</summary>
    public static ExcitementSummary Mean(IReadOnlyList<ExcitementSummary> all)
    {
        ArgumentNullException.ThrowIfNull(all);
        var n = Math.Max(1, all.Count);
        float Avg(Func<ExcitementSummary, float> f) => all.Sum(f) / n;
        return new ExcitementSummary(
            Enum.GetValues<HighlightKind>().ToDictionary(k => k, k => all.Sum(s => s.Count(k))),
            Avg(s => s.EventPoints), Avg(s => s.RawDamage), Avg(s => s.Seconds), Avg(s => s.DeadSeconds), all.Max(s => s.LongestGap),
            Avg(s => s.Spectacle), Avg(s => s.Drama), Avg(s => s.Pacing), Avg(s => s.Score),
            Avg(s => s.Surprise), Avg(s => s.MeanSuspense), Avg(s => s.LargestSwing), all.Sum(s => s.LeadChanges),
            Avg(s => s.BoredShare), all.Max(s => s.LongestBored), Avg(s => s.PeaksPerMinute), Avg(s => s.MeanPeak));
    }

    public string Line() =>
        FormattableString.Invariant($"総合 {Score:0.0}  派手さ {Spectacle:0.0}/分  ドラマ {Drama:0.0}（驚き {Surprise:0.00}・はらはら {MeanSuspense:0.000}・最大の揺れ {LargestSwing:0.00}・入れ替わり {LeadChanges}）  ") +
        FormattableString.Invariant($"緩急 {Pacing:0.0}（退屈 {BoredShare * 100f:0}%・最長 {LongestBored:0.0} 秒・山 {PeaksPerMinute:0.0}/分・山の高さ {MeanPeak:0.0}）  空白 {DeadShare * 100f:0}%（最長 {LongestGap:0.0} 秒）  ") +
        string.Join(" ", Counts.Where(p => p.Value > 0).Select(p => $"{p.Key}={p.Value}"));
}

/// <summary>ボット同士の 1 試合を回しながら見どころを拾う（<see cref="BotArena.Run"/> と同じ組み立て。拳のボット）。</summary>
public static class ExcitementRun
{
    public const float StepDt = 1f / 60f;

    public static MatchHighlights Bout(Contender red, Contender blue, int seed, float seconds, ExcitementTuning? tuning = null, Action<BoxingGame, float>? afterStep = null)
    {
        ArgumentNullException.ThrowIfNull(red);
        ArgumentNullException.ThrowIfNull(blue);
        var corners = new BoxingCorners(blue.Name, s => blue.Create(s), s => red.Create(s), PlayerName: red.Name, PlayerBody: red.Build, CpuBody: blue.Build, PlayerFace: red.Face, CpuFace: blue.Face);
        using var game = new BoxingGame(null, seed, corners);
        var highlights = new MatchHighlights(tuning, seconds);
        var steps = (int)MathF.Ceiling(seconds / StepDt);
        for (var i = 0; i < steps; i++)
        {
            game.Step(default, StepDt);
            var t = (i + 1) * StepDt;
            highlights.Observe(game, t, StepDt);
            afterStep?.Invoke(game, t);
        }

        highlights.Finish();
        return highlights;
    }

    public static string EventList(MatchHighlights h, string redName, string blueName)
    {
        ArgumentNullException.ThrowIfNull(h);
        var sb = new StringBuilder();
        foreach (var e in h.Events.OrderBy(e => e.Time))
        {
            sb.Append(CultureInfo.InvariantCulture, $"{e.Time,6:0.00}s  {e.Kind,-10} {(e.Red ? redName : blueName),-14} 強さ {e.Intensity:0.00}  {e.Note}\n");
        }

        return sb.ToString();
    }
}
