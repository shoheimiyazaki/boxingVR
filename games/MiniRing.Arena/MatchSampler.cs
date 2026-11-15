using MiniRing.Bots;

namespace MiniRing.Arena;

/// <summary>
/// 試合の 1 標本 = 1 つの種 × 決めた相手の全員 × 両コーナー（<see cref="BotArena.Pair"/> の 2 試合）。その合計に <see cref="MatchScore.Of"/> をかけたものが 1 標本の点。
/// 全体の合計にかける <see cref="MatchTrainer{T}.Evaluate"/> とは尺度が違う（相手の数 × 2 試合ぶんで 1 点: 点の範囲は ±(2 × 相手の数) と 0.9 の和）。
/// 試合どうしは並列に回すが、結果は添字の位置に書くので、並列の数に依らず決定的。
/// </summary>
internal sealed class MatchSampler(IReadOnlyList<Contender> opponents, float seconds, int? maxParallel = null)
{
    /// <summary>1 標本の試合の数（相手 × 両コーナー）。</summary>
    public int BoutsPerSample => opponents.Count * 2;

    /// <summary>
    /// 種ごとの点。標本の種 <c>s</c> の相手 <c>i</c> との試合は、種 <c>s + 101 i</c>（コーナーを入れ替えた 2 試合は同じ種）。
    /// 種の組が重ならないように、標本の種は 1000 以上離す。
    /// </summary>
    public double[] Score(Contender mine, IReadOnlyList<int> seeds)
    {
        ArgumentNullException.ThrowIfNull(mine);
        ArgumentNullException.ThrowIfNull(seeds);
        var marked = MatchScore.Mark(mine);
        var specs = seeds.SelectMany(s => opponents.SelectMany((o, i) => BotArena.Pair(marked, o, 2, seconds, s + (101 * i)))).ToList();
        var results = BotArena.RunAll(specs, VersusLab.Tuning, maxParallel);
        var points = new double[seeds.Count];
        for (var k = 0; k < seeds.Count; k++)
        {
            int wins = 0, losses = 0;
            float dealt = 0f, taken = 0f;
            for (var j = 0; j < BoutsPerSample; j++)
            {
                var r = results[(k * BoutsPerSample) + j];
                var red = r.Spec.Red.Name == marked.Name;
                var sign = red ? r.Winner : -r.Winner;
                wins += sign > 0 ? 1 : 0;
                losses += sign < 0 ? 1 : 0;
                dealt += red ? r.Red.DamageDealt : r.Blue.DamageDealt;
                taken += red ? r.Blue.DamageDealt : r.Red.DamageDealt;
            }

            points[k] = MatchScore.Of(wins, losses, dealt, taken);
        }

        return points;
    }
}
