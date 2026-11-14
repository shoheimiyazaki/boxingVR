namespace MiniRing.Bots;

/// <summary>
/// 対戦に出すボット: 名前と、ラウンドごとに新しい操作元を作る関数。<paramref name="Weapon"/> を渡すと右手に武器を握って戦う
/// （<paramref name="TwoHanded"/> なら左手を添えて両手で）。
/// </summary>
public sealed record Contender(string Name, Func<int, IFighterControl> Create, Weapons.WeaponKind? Weapon = null, bool TwoHanded = false,
    BodyBuild? Build = null, Voxels.FaceParts? Face = null)
{
    public static Contender Of(BotStyle style)
    {
        ArgumentNullException.ThrowIfNull(style);
        var validated = style.Validated();
        return new Contender(validated.Name, seed => new BoxerBot(validated, seed), Build: validated.Build, Face: validated.Face);
    }

    public static Contender Named(string name) => new(name, seed => BotCatalog.Create(name, seed));
}

/// <summary>1 試合の条件（<paramref name="Setup"/> は最初のラウンドの始めに 1 回だけ呼ぶ。ラウンドごとに世界は作り直される）。<paramref name="Red"/> はプレイヤー側、<paramref name="Blue"/> は CPU 側のコーナー。</summary>
public sealed record BoutSpec(Contender Red, Contender Blue, int Seed, float Seconds, Action<BoxingGame>? Setup = null);

/// <summary>片方のコーナーの成績。</summary>
/// <param name="RoundsWon">取ったラウンド数。</param>
/// <param name="DamageDealt">与えたダメージ。</param>
/// <param name="Landed">当てた数。</param>
/// <param name="CleanHits">ガードに当たらなかった数。</param>
/// <param name="Knockdowns">取ったダウン。</param>
/// <param name="Stats">ボットが数えた技・守りの内訳（legacy は空）。</param>
/// <param name="EyeLoss">この試合でこのコーナーが目を失った最大の割合（0 = 両目無事 … 1 = 両目とも潰された。<see cref="VisionState.Binocular"/> から）。</param>
public sealed record CornerResult(int RoundsWon, float DamageDealt, int Landed, int CleanHits, int Knockdowns, BotStats Stats, float EyeLoss = 0f);

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
            seed => Track(blueBots, Fallback(spec.Blue, seed)),
            seed => Track(redBots, Fallback(spec.Red, seed)), CpuWeapon: spec.Blue.Weapon, PlayerWeapon: spec.Red.Weapon,
            CpuTwoHanded: spec.Blue.TwoHanded, PlayerTwoHanded: spec.Red.TwoHanded,
            PlayerName: spec.Red.Name, PlayerBody: spec.Red.Build, CpuBody: spec.Blue.Build, PlayerFace: spec.Red.Face, CpuFace: spec.Blue.Face);
        using var game = new BoxingGame(tuning, spec.Seed, corners);
        spec.Setup?.Invoke(game); // 目を潰しておくなど、試合の始めの条件（省略なら何もしない）

        var red = new Tally();
        var blue = new Tally();
        var round = game.Round;
        var redDown = false;
        var blueDown = false;
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

            // 武器の打撃も拳と同じ物差しのダメージで数える。
            foreach (var hit in game.WeaponHits)
            {
                var tally = hit.Attacker == game.Player ? red : blue;
                tally.Damage += hit.Damage;
                tally.Landed++;
                tally.Clean += hit.Zone != HitZone.Guard ? 1 : 0;
            }

            red.EyeLoss = MathF.Max(red.EyeLoss, 1f - (game.Player.Voxels?.Vision.Binocular ?? 1f));
            blue.EyeLoss = MathF.Max(blue.EyeLoss, 1f - (game.Cpu.Voxels?.Vision.Binocular ?? 1f));
            if (game.Round != round)
            {
                round = game.Round;
                redDown = blueDown = false;
            }

            // ダウンを取った数: 審判がダウンを取っていない状態から取ったら 1（中腰で待っている間は数えない）。
            blue.Knockdowns += !redDown && game.Player.IsKnockedDown ? 1 : 0;
            red.Knockdowns += !blueDown && game.Cpu.IsKnockedDown ? 1 : 0;
            redDown = game.Player.IsKnockedDown;
            blueDown = game.Cpu.IsKnockedDown;
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

    /// <summary>武器で戦うボットは、武器を落としたら拳で戦う（<see cref="DisarmedFallback"/>）。</summary>
    private static IFighterControl Fallback(Contender contender, int seed)
    {
        var control = contender.Create(seed);
        return contender.Weapon is null ? control : DisarmedFallback.WithFists(control, seed);
    }

    private sealed class Tally
    {
        public float Damage;
        public int Landed;
        public int Clean;
        public int Knockdowns;
        public float EyeLoss;

        public CornerResult Result(int roundsWon, IEnumerable<IFighterControl> bots)
        {
            var stats = new BotStats();
            foreach (var bot in bots.SelectMany(b => b is DisarmedFallback f ? new[] { f.Armed, f.Fists } : [b]).OfType<BoxerBot>())
            {
                stats.Merge(bot.Stats);
            }

            return new CornerResult(roundsWon, Damage, Landed, Clean, Knockdowns, stats, EyeLoss);
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
    }}
