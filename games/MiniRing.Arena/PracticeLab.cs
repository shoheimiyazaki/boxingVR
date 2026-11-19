using MiniRing.Bots;

namespace MiniRing.Arena;

/// <summary>
/// 投げ技の練習（docs/GRAPPLING.md §15）: 教科書の技の経路の数値（手・足・立ち位置の目標のずれ、頭のずれ、移動、時刻）を少しずつ変え、
/// 良くなったら採る（(1+1) 進化戦略、変える幅は 1/5 の決まりで広げ・狭める）。点 = きれいな投げ（受けが倒れ、取りはその 0.5 秒後も立っている）
/// − 0.5 × 取りが倒れた − 0.1 × 掴めず を、構えて立つ受けと動いて打つ受け（チャンピオン）で足したもの。最後に、練習に使わなかった種で
/// 始めの経路と比べ直す（CLAUDE.md §1.8）。結果は技の経路（教科書のデータ）として出すだけ。
/// </summary>
internal sealed class PracticeLab(int seed, Action<string> log)
{
    private static readonly string[] Victims = ["stand", "champion"];

    public void Run(GrappleTechnique start, int candidates, int bouts, float seconds, int validationSeed, float sigma)
    {
        var random = new Random(seed ^ start.Name.Sum(c => (int)c)); // 名前から決まる種（string.GetHashCode は実行ごとに変わる）
        float Score(GrappleTechnique t) => Victims.Sum(v => SeoiLab.Evaluate(t, v, seed, bouts, seconds).Value);
        var best = (T: start, S: Score(start));
        var step = sigma;
        var successes = 0;
        log($"## {start.Name} の練習（種 {seed}〜、{bouts} 本 × {seconds:0} 秒 × 受け 2 人、{candidates} 候補）");
        log("");
        log("| 候補 | 変える幅 | 点 | 採った |");
        log("|---|---|---|---|");
        log($"| 0（教科書） | - | {best.S:0.0} | - |");
        for (var i = 1; i <= candidates; i++)
        {
            var candidate = Mutate(best.T, step, random);
            var score = Score(candidate);
            var taken = score > best.S;
            log($"| {i} | {step:0.000} | {score:0.0} | {(taken ? "採った" : "")} |");
            if (taken)
            {
                best = (candidate, score);
                successes++;
            }

            // 1/5 の決まり: 5 回に 1 回より多く良くなるなら幅を広げ、少なければ狭める。
            if (i % 5 == 0)
            {
                step *= successes > 1 ? 1.3f : 0.75f;
                successes = 0;
            }
        }

        log("");
        log($"### 比べ直し（使わなかった種 {validationSeed}〜）");
        log("");
        log("| 経路 | 構えて立つ受け | チャンピオン |");
        log("|---|---|---|");
        foreach (var (name, t) in new[] { ("教科書", start), ("練習した", best.T) })
        {
            log($"| {name} | {SeoiLab.Evaluate(t, "stand", validationSeed, bouts, seconds)} | {SeoiLab.Evaluate(t, "champion", validationSeed, bouts, seconds)} |");
        }

        log("");
        log("練習した経路の JSON:");
        log("```json");
        log(best.T.ToJsonKeys());
        log("```");
        log("");
    }

    /// <summary>
    /// 経路を少し変える: 時刻全体を 0.85〜1.15 倍、各キーの手・足・立ち位置のずれと頭のずれ・移動に幅 <paramref name="step"/>（腕の長さ単位）の
    /// 正規乱数を足し、体のバネの割合・ひねりを少し変える。掴む・離す・体の向き・点の基準は変えない（技の形は保つ）。
    /// </summary>
    internal static GrappleTechnique Mutate(GrappleTechnique t, float step, Random random)
    {
        float N() => (float)(Math.Sqrt(-2.0 * Math.Log(1.0 - random.NextDouble())) * Math.Cos(2.0 * Math.PI * random.NextDouble()));
        float[]? Jitter(float[]? v, float scale) => v is null ? null : [.. v.Select(x => x + (N() * scale))];
        GrappleHand? Hand(GrappleHand? h) => h is null ? null : h with { Offset = Jitter(h.Offset ?? [0f, 0f, 0f], step) };
        var timeScale = Math.Clamp(1f + (0.1f * N()), 0.85f, 1.15f);
        var keys = t.Keys.Select(k => k with
        {
            T = k.T * timeScale,
            Left = Hand(k.Left),
            Right = Hand(k.Right),
            Stand = Hand(k.Stand),
            Leg = k.Leg is null ? null : k.Leg with { Offset = Jitter(k.Leg.Offset ?? [0f, 0f, 0f], step) },
            Head = Jitter(k.Head, step),
            Move = Jitter(k.Move, 2f * step),
            Spring = Math.Clamp(k.Spring + (0.1f * N()), 0f, 1f),
            Twist = k.Twist + (step * N()),
        }).ToList();
        return t with { Keys = keys };
    }
}
