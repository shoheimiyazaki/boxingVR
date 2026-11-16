namespace MiniRing.Arena;

/// <summary>下見した 1 試合の結果。</summary>
/// <param name="Seed">試合の種。</param>
/// <param name="Finish">決着の文（ゲームの <see cref="BoxingGame.Decision"/> か <see cref="BoxingGame.Message"/>）。</param>
/// <param name="Ko">K.O. で決まった（判定でない）。</param>
/// <param name="Round">決着したラウンド。</param>
/// <param name="RoundTime">決着したラウンドの経過時間 [s]。</param>
/// <param name="RedDealt">赤が与えたダメージ。</param>
/// <param name="BlueDealt">青が与えたダメージ。</param>
/// <param name="RedDowns">赤が取られたダウンの数。</param>
/// <param name="BlueDowns">青が取られたダウンの数。</param>
/// <param name="Winner">1 = 赤、−1 = 青、0 = 引き分け。</param>
internal sealed record TvScoutResult(int Seed, string Finish, bool Ko, int Round, float RoundTime, float RedDealt, float BlueDealt, int RedDowns, int BlueDowns, int Winner);

/// <summary>
/// テレビ中継の決まりと下見の点数（描画にも試合にも触れない計算だけ。テストから直に確かめる）。
/// </summary>
internal static class TvShowRules
{
    /// <summary>テレビ中継の決まり（ラウンドの数・休みの長さ。休みの間に治る量は標準と同じ）。撮影と下見（tvscout）が同じものを使う。</summary>
    public static MatchRules Rules(int rounds, float breakSeconds)
        => MatchRules.Tv with { Rounds = rounds, BreakSeconds = breakSeconds, RegenVoxelsPerSecond = MatchRules.Tv.RegenVoxelsPerSecond * MatchRules.Tv.BreakSeconds / breakSeconds };

    /// <summary>
    /// 見ごたえの点（中継に向く試合ほど高い）。決まった重みの和で、撮った試合を見て決めた目安:
    /// 長く続く（最終ラウンドの終わりまでで最大 3 × ラウンド数）、ダウン（1 回 1.5、5 回まで）、両者がダウンを奪い合う（+3）、
    /// K.O. で決着（+2、最終ラウンドなら +1）、ダメージで負けていた側の逆転勝ち（+4）、第 1 ラウンド 15 秒より前に終わる（−6）、引き分け（−1）。
    /// </summary>
    public static float Drama(TvScoutResult r, int rounds, float roundSeconds)
    {
        ArgumentNullException.ThrowIfNull(r);
        var score = 3f * ((r.Round - 1) + Math.Clamp(r.RoundTime / roundSeconds, 0f, 1f));
        var downs = r.RedDowns + r.BlueDowns;
        score += 1.5f * Math.Min(downs, 5);
        score += r.RedDowns > 0 && r.BlueDowns > 0 ? 3f : 0f;
        score += r.Ko ? 2f + (r.Round >= rounds ? 1f : 0f) : 0f;
        var winnerDealt = r.Winner > 0 ? r.RedDealt : r.BlueDealt;
        var loserDealt = r.Winner > 0 ? r.BlueDealt : r.RedDealt;
        score += r.Winner != 0 && winnerDealt < loserDealt ? 4f : 0f;
        score -= r.Round == 1 && r.RoundTime < 15f ? 6f : 0f;
        score -= r.Winner == 0 ? 1f : 0f;
        return score;
    }

    /// <summary>一番見ごたえのある種（同点なら小さい種）。</summary>
    public static TvScoutResult Best(IReadOnlyList<TvScoutResult> results, int rounds, float roundSeconds)
    {
        ArgumentNullException.ThrowIfNull(results);
        return results.OrderByDescending(r => Drama(r, rounds, roundSeconds)).ThenBy(r => r.Seed).First();
    }
}
