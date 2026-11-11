namespace MiniRing.Bots;

/// <summary>対戦に出すボット: 名前と、ラウンドごとに新しい操作元を作る関数。</summary>
public sealed record Contender(string Name, Func<int, IFighterControl> Create)
{
    public static Contender Of(BotStyle style)
    {
        ArgumentNullException.ThrowIfNull(style);
        var validated = style.Validated();
        return new Contender(validated.Name, seed => new BoxerBot(validated, seed));
    }

    public static Contender Named(string name) => new(name, seed => BotCatalog.Create(name, seed));
}

/// <summary>1 試合の条件。<paramref name="Red"/> はプレイヤー側、<paramref name="Blue"/> は CPU 側のコーナー。</summary>
public sealed record BoutSpec(Contender Red, Contender Blue, int Seed, float Seconds);

/// <summary>片方のコーナーの成績。<paramref name="Stats"/> はボットが数えた技・守りの内訳（legacy は空）。</summary>
public sealed record CornerResult(int RoundsWon, float DamageDealt, int Landed, int CleanHits, int Knockdowns, BotStats Stats);

/// <summary>1 試合の結果。</summary>
public sealed record BoutResult(BoutSpec Spec, CornerResult Red, CornerResult Blue)
{
    /// <summary>+1 = 赤の勝ち、−1 = 青の勝ち、0 = 引き分け。KO・リングアウトで取ったラウンド数、同じならダメージの差で決める。</summary>
    public int Winner
    {
        get
        {
            if (Red.RoundsWon != Blue.RoundsWon)
            {
                return Red.RoundsWon > Blue.RoundsWon ? 1 : -1;
            }

            var margin = 0.05f * (Red.DamageDealt + Blue.DamageDealt) / 2f;
            var diff = Red.DamageDealt - Blue.DamageDealt;
            return MathF.Abs(diff) <= MathF.Max(1f, margin) ? 0 : MathF.Sign(diff);
        }
    }

    public CornerResult Of(string name) => Spec.Red.Name == name ? Red : Blue;
}

/// <summary>
/// ボット同士の試合を物理ごとヘッドレスで回す。1 試合は決まった時間（ラウンドは KO・リングアウトで区切られ、また始まる）。
/// 物理は試合ごとに別のワールドで単一スレッドなので、試合どうしは並列に回してよく、結果は順序に依らず決定的。
/// </summary>
public static class BotArena
{
    private const float StepDt = 1f / 60f;

    public static BoutResult Run(BoutSpec spec, FighterTuning? tuning = null)
    {
        ArgumentNullException.ThrowIfNull(spec);
        var redBots = new List<IFighterControl>();
        var blueBots = new List<IFighterControl>();
        var corners = new BoxingCorners(spec.Blue.Name,
            seed => Track(blueBots, spec.Blue.Create(seed)),
            seed => Track(redBots, spec.Red.Create(seed)));
        using var game = new BoxingGame(tuning, spec.Seed, corners);

        var red = new Tally();
        var blue = new Tally();
        var round = game.Round;
        var redState = FighterState.Standing;
        var blueState = FighterState.Standing;
        var steps = (int)MathF.Ceiling(spec.Seconds / StepDt);
        for (var i = 0; i < steps; i++)
        {
            game.Step(default, StepDt);
            foreach (var hit in game.RecentHits)
            {
                var tally = hit.Attacker == game.Player ? red : blue;
                tally.Damage += hit.Damage;
                tally.Landed++;
                tally.Clean += hit.Zone != HitZone.Guard ? 1 : 0;
            }

            if (game.Round != round)
            {
                round = game.Round;
                redState = blueState = FighterState.Standing;
            }

            // ダウンを取った数: 相手が立っている状態から倒れたら 1。
            blue.Knockdowns += redState == FighterState.Standing && game.Player.State != FighterState.Standing ? 1 : 0;
            red.Knockdowns += blueState == FighterState.Standing && game.Cpu.State != FighterState.Standing ? 1 : 0;
            redState = game.Player.State;
            blueState = game.Cpu.State;
        }

        return new BoutResult(spec, red.Result(game.PlayerWins, redBots), blue.Result(game.CpuWins, blueBots));
    }

    /// <summary>試合を並列に回し、渡した順の結果を返す。</summary>
    public static IReadOnlyList<BoutResult> RunAll(IReadOnlyList<BoutSpec> specs, FighterTuning? tuning = null, int? maxParallel = null)
    {
        ArgumentNullException.ThrowIfNull(specs);
        var results = new BoutResult[specs.Count];
        Parallel.For(0, specs.Count, new ParallelOptions { MaxDegreeOfParallelism = maxParallel ?? Environment.ProcessorCount },
            i => results[i] = Run(specs[i], tuning));
        return results;
    }

    /// <summary>
    /// 総当たり: 全ての組を <paramref name="boutsPerPair"/> 試合（半分ずつコーナーを入れ替える）。種は組と試合番号から決まる。
    /// </summary>
    public static IReadOnlyList<BoutSpec> RoundRobin(IReadOnlyList<Contender> contenders, int boutsPerPair, float seconds, int baseSeed)
    {
        ArgumentNullException.ThrowIfNull(contenders);
        // 結果は名前で集計するので、同じ名前が 2 人いると 1 行に混ざる。
        var duplicate = contenders.GroupBy(c => c.Name).FirstOrDefault(g => g.Count() > 1);
        if (duplicate is not null)
        {
            throw new ArgumentException($"同じ名前のボットが 2 人います: {duplicate.Key}（JSON のスタイルには別の Name を付けてください）。", nameof(contenders));
        }

        var specs = new List<BoutSpec>();
        for (var a = 0; a < contenders.Count; a++)
        {
            for (var b = a + 1; b < contenders.Count; b++)
            {
                specs.AddRange(Pair(contenders[a], contenders[b], boutsPerPair, seconds, baseSeed + (1000 * a) + (37 * b)));
            }
        }

        return specs;
    }

    /// <summary>2 者の対戦を <paramref name="bouts"/> 試合（偶数番は <paramref name="a"/> が赤、奇数番は青）。</summary>
    public static IEnumerable<BoutSpec> Pair(Contender a, Contender b, int bouts, float seconds, int baseSeed)
    {
        for (var i = 0; i < bouts; i++)
        {
            var seed = baseSeed + (i / 2);
            yield return i % 2 == 0 ? new BoutSpec(a, b, seed, seconds) : new BoutSpec(b, a, seed, seconds);
        }
    }

    private static IFighterControl Track(List<IFighterControl> bots, IFighterControl bot)
    {
        bots.Add(bot);
        return bot;
    }

    private sealed class Tally
    {
        public float Damage;
        public int Landed;
        public int Clean;
        public int Knockdowns;

        public CornerResult Result(int roundsWon, IEnumerable<IFighterControl> bots)
        {
            var stats = new BotStats();
            foreach (var bot in bots.OfType<BoxerBot>())
            {
                stats.Merge(bot.Stats);
            }

            return new CornerResult(roundsWon, Damage, Landed, Clean, Knockdowns, stats);
        }
    }
}

/// <summary>総当たりの順位表の 1 行。</summary>
public sealed record Standing(string Name, int Bouts, int Wins, int Draws, int Losses, float DamageFor, float DamageAgainst,
    int RoundsWon, int Knockdowns, BotStats Stats)
{
    public int Points => (3 * Wins) + Draws;

    /// <summary>勝ち = 1、引き分け = 0.5 の勝率。</summary>
    public float Score => Bouts > 0 ? (Wins + (0.5f * Draws)) / Bouts : 0f;

    public float DamageRatio => DamageFor / MathF.Max(1f, DamageAgainst);

    public static IReadOnlyList<Standing> From(IEnumerable<BoutResult> results)
    {
        ArgumentNullException.ThrowIfNull(results);
        var rows = new Dictionary<string, Standing>();
        foreach (var r in results)
        {
            Add(rows, r.Spec.Red.Name, r.Red, r.Blue, r.Winner);
            Add(rows, r.Spec.Blue.Name, r.Blue, r.Red, -r.Winner);
        }

        return rows.Values.OrderByDescending(s => s.Points).ThenByDescending(s => s.DamageFor - s.DamageAgainst).ToList();
    }

    private static void Add(Dictionary<string, Standing> rows, string name, CornerResult own, CornerResult other, int outcome)
    {
        var s = rows.GetValueOrDefault(name) ?? new Standing(name, 0, 0, 0, 0, 0f, 0f, 0, 0, new BotStats());
        s.Stats.Merge(own.Stats);
        rows[name] = s with
        {
            Bouts = s.Bouts + 1,
            Wins = s.Wins + (outcome > 0 ? 1 : 0),
            Draws = s.Draws + (outcome == 0 ? 1 : 0),
            Losses = s.Losses + (outcome < 0 ? 1 : 0),
            DamageFor = s.DamageFor + own.DamageDealt,
            DamageAgainst = s.DamageAgainst + other.DamageDealt,
            RoundsWon = s.RoundsWon + own.RoundsWon,
            Knockdowns = s.Knockdowns + own.Knockdowns,
        };
    }
}
