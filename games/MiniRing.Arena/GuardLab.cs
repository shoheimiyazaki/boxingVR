using System.Globalization;
using System.Text;
using MiniRing.Bots;
using MiniRing.Motion;
using MiniRing.Weapons;

namespace MiniRing.Arena;

/// <summary>
/// 守りのドリル（guardlab）: 日本刀の受け手（その場で構えて守るだけ: <see cref="SwordsmanBot.Passive"/>）へ、いろいろな武器の打ち手が 1 本ずつ打ち込み、
/// 受け手が失ったもの（ボクセル・切り落とし・死・脳しんとう）と、武器同士が触れたか（受けたか）を数える。守り方（<see cref="SwordDefense"/>）ごとに比べる。
/// <para>打ち手は「練習に使う組」（日本刀の技で頭・胸を狙う、棍棒の 3 つの振り）と、「確かめの組」（ロングソードの技、バット、拳）に分ける。
/// 守りを選ぶのは練習の組だけで、確かめの組は選んだ守りが知らない武器にも効くか（武器を選ばない守りか）を見るためのもの。</para>
/// </summary>
internal static class GuardLab
{
    private const float Dt = 1f / 240f;

    /// <summary>打ち手 1 つ: 名前・組（練習 / 確かめ）・武器・作り方。</summary>
    internal sealed record Attack(string Name, bool Holdout, WeaponKind? Weapon, bool TwoHanded, Func<int, IFighterControl> Create);

    /// <summary>1 本の結果。</summary>
    internal sealed record Trial(Attack Attack, int Seed, int Cells, int Severed, bool Killed, float Concussion, int ClashSteps, int Blocks, int Evasions, int SelfCells = 0, int BladeLost = 0, int TheirLost = 0, int BladeCarved = 0, int BladeSnaps = 0)
    {
        /// <summary>失ったものの重さ: ボクセル / 60 + 切り落とし × 2 + 死 × 3 + 脳しんとう（<see cref="StrikeResult.Effect"/> と同じ物差し）。</summary>
        public float Harm => (Cells / 60f) + (2f * Severed) + (Killed ? 3f : 0f) + Concussion;

        /// <summary>打たれた（ボクセルを 6 つ以上失った・脳しんとう 0.05 以上）。</summary>
        public bool Struck => Cells > 5 || Concussion > 0.05f || Severed > 0 || Killed;
    }

    /// <summary>守り方 1 つ: 名前・守り・間合いの取り合い（null ならその場で構えて動かない。あれば遠間を保って回る。打ちには出ない）。</summary>
    internal sealed record Variant(string Name, SwordDefense Defense, HitAndAway? Away = null);

    /// <summary>比べる守り方。</summary>
    public static IReadOnlyList<Variant> Variants { get; } =
    [
        new("守らない", SwordDefense.None),
        new("はじめの守り（乱数で受け・外し）", new SwordDefense(Counter: false)),
        new("はじめの受けだけ", new SwordDefense(BlockShare: 1f, Counter: false)),
        new("はじめの外しだけ", new SwordDefense(BlockShare: 0f, Counter: false)),
        new("受け流し 0°（正面で止める）", new SwordDefense(Counter: false, Mode: DefenseMode.Parry, Deflect: 0f)),
        new("受け流し 30°", new SwordDefense(Counter: false, Mode: DefenseMode.Parry, Deflect: 0.52f)),
        new("受け流し 55°", new SwordDefense(Counter: false, Mode: DefenseMode.Parry, Deflect: 0.96f)),
        new("足と頭で外す", new SwordDefense(Counter: false, Mode: DefenseMode.Step)),
        new("受け流し 30° ＋ 足と頭", new SwordDefense(Counter: false, Mode: DefenseMode.ParryAndStep, Deflect: 0.52f)),
        new("受け流し（握りの側で払う）＋ 足と頭", SwordDefense.Parrying with { Counter = false }),
        new("間合いを保つ（遠間で回る）", new SwordDefense(Counter: false, Mode: DefenseMode.Step), new HitAndAway()),
        new("間合いを保つ ＋ 受け流し（握りの側）＋ 足と頭", SwordDefense.Parrying with { Counter = false }, new HitAndAway()),
    ];

    /// <summary>打ち手の一覧。</summary>
    public static IReadOnlyList<Attack> Attacks(SwordLibraryLab.Library library, IReadOnlyList<(WeaponSwing Swing, float Range)> club, IReadOnlyList<(WeaponSwing Swing, float Range)> bat)
    {
        var aim = AimDefaults.Tuning;
        var list = new List<Attack>();
        foreach (var technique in library.Techniques)
        {
            var guard = library.Guards[technique.Weapon];
            foreach (var target in new[] { BodyPartKind.Head, BodyPartKind.Chest })
            {
                list.Add(new Attack($"{technique.Weapon} {technique.Name} → {(target == BodyPartKind.Head ? "頭" : "胸")}", technique.Weapon != WeaponKind.Katana, technique.Weapon, true,
                    s => new SwordsmanBot([technique], guard, s, SwordStance.Rate, rest: 1000f, exact: true, tuning: SwordStance.Tuning, aim: aim) { Aim = target }));
            }
        }

        foreach (var (swing, range) in club)
        {
            list.Add(new Attack($"棍棒 {swing.Family}", false, WeaponKind.WoodenClub, false, s => new ClubBot([swing], s, range, rest: 1000f, exact: true)));
        }

        foreach (var (swing, range) in bat)
        {
            list.Add(new Attack($"バット {swing.Family}", true, WeaponKind.WoodenClub, true, s => new ClubBot([swing], s, range, rest: 1000f, exact: true)));
        }

        var fists = Contender.Named("champion");
        list.Add(new Attack("拳（champion）", true, fists.Weapon, fists.TwoHanded, fists.Create));
        return list;
    }

    /// <summary>日本刀の受け手（打たず、その場で守るだけ）。</summary>
    private static SwordsmanBot Defender(SwordLibraryLab.Library library, Variant variant, int seed) =>
        new([.. library.Techniques.Where(t => t.Weapon == WeaponKind.Katana)], library.Guards[WeaponKind.Katana], seed, SwordStance.Rate,
            tuning: SwordStance.Tuning, defense: variant.Defense, bladeLength: SwordLibraryLab.BladeLength(WeaponKind.Katana), spacing: SwordForms.KatanaSpacing, opponentReach: 3f,
            hitAndAway: variant.Away)
        {
            Passive = true,
        };

    /// <summary>1 本: 打ち手が受け手へ打ち込み、<paramref name="seconds"/> 秒のうちに受け手が失ったものを数える。</summary>
    public static Trial Run(Attack attack, SwordLibraryLab.Library library, Variant variant, int seed, float seconds, Action<BoxingGame, float, SwordsmanBot>? afterStep = null)
    {
        ArgumentNullException.ThrowIfNull(attack);
        var defender = Defender(library, variant, seed);
        using var game = new BoxingGame(VersusLab.Tuning, seed, new BoxingCorners("受け手", _ => defender, _ => attack.Create(seed),
            CpuWeapon: WeaponKind.Katana, PlayerWeapon: attack.Weapon, CpuTwoHanded: true, PlayerTwoHanded: attack.TwoHanded));
        var victim = game.Cpu.Voxels!;
        var start = victim.Parts.Sum(p => p.Body.Grid.FilledCount);
        var katana = game.WeaponOf(game.Cpu)!;
        var bladeStart = katana.Body.Grid.FilledCount;
        var theirWeapon = game.WeaponOf(game.Player);
        var theirStart = theirWeapon?.Body.Grid.FilledCount ?? 0;
        var clashes = 0;
        var selfCells = 0;
        var bladeCarved = 0;
        var bladeSnaps = 0;
        var parts = victim.Parts.Select(p => p.Body).ToHashSet();
        for (var t = 0f; t < seconds; t += Dt)
        {
            game.Step(default, Dt);
            var theirs = game.Weapons.Where(w => w != katana).Select(w => w.Body).ToHashSet();
            clashes += game.RecentCuts.Any(c => (c.A == katana.Body && theirs.Contains(c.B)) || (c.B == katana.Body && theirs.Contains(c.A))) ? 1 : 0;
            selfCells += game.RecentCuts.Where(c => c.A == katana.Body && parts.Contains(c.B)).Sum(c => c.RemovedB.Count);
            bladeCarved += game.RecentCuts.Sum(c => c.RemovedFor(katana.Body).Count);
            bladeSnaps += game.RecentCuts.Count(c => c.SnappedFor(katana.Body) is not null);
            afterStep?.Invoke(game, t, defender);
            if (game.Cpu.IsDead)
            {
                break;
            }
        }

        var lost = start - victim.Parts.Sum(p => p.Body.Grid.FilledCount);
        return new Trial(attack, seed, lost, victim.Fragments.Count, game.Cpu.IsDead, game.Cpu.Concussion, clashes, defender.Blocks, defender.Evasions, selfCells, bladeStart - katana.Body.Grid.FilledCount, theirStart - (theirWeapon?.Body.Grid.FilledCount ?? 0), bladeCarved, bladeSnaps);
    }

    /// <summary>守り方ごとに全ての打ち手を回し、Markdown の表を返す。<paramref name="pick"/> は選んだ守り方の名前（練習の組で失ったものがいちばん少ない）。</summary>
    public static string Run(SwordLibraryLab.Library library, IReadOnlyList<Attack> attacks, IReadOnlyList<Variant> variants, int seeds, float seconds,
        TextWriter log, out string pick)
    {
        var watch = System.Diagnostics.Stopwatch.StartNew();
        var jobs = (from v in variants from a in attacks from s in Enumerable.Range(1, seeds) select (v, a, s)).ToList();
        var results = new Trial[jobs.Count];
        var done = 0;
        Parallel.For(0, jobs.Count, i =>
        {
            var (v, a, s) = jobs[i];
            results[i] = Run(a, library, v, s, seconds);
            var n = Interlocked.Increment(ref done);
            if (n % 20 == 0)
            {
                lock (log)
                {
                    log.WriteLine($"guardlab: {n}/{jobs.Count} ({watch.Elapsed.TotalMinutes:0.0} min)");
                }
            }
        });

        var byVariant = variants.Select((v, i) => (v.Name, Trials: results.Where((_, k) => jobs[k].v.Name == v.Name).ToList())).ToList();
        var text = new StringBuilder();
        text.AppendLine(string.Create(CultureInfo.InvariantCulture,
            $"打ち手 {attacks.Count}（練習の組 {attacks.Count(a => !a.Holdout)}・確かめの組 {attacks.Count(a => a.Holdout)}）× 種 {seeds} × {seconds:0.#} 秒。受け手は日本刀（その場で構えて守るだけ）。"));
        text.AppendLine("失ったもの = ボクセル / 60 + 切り落とし × 2 + 死 × 3 + 脳しんとう。打たれた = ボクセル 6 以上・脳しんとう 0.05 以上・切り落とし・死のどれか。受けた = 刀と相手の武器が触れた本数。");
        text.AppendLine();
        text.AppendLine("| 守り方 | 練習: 失ったもの（1 本あたり） | 練習: 打たれた | 確かめ: 失ったもの | 確かめ: 打たれた | 刀で受けた本数 | 自分の刀で斬った（1 本あたり） | 刀が欠けた（1 本あたり） | 相手の武器が欠けた | 刀: 削れた・折れた | 死 |");
        text.AppendLine("|---|---:|---:|---:|---:|---:|---:|---:|---:|---:|---:|");
        foreach (var (name, trials) in byVariant)
        {
            var train = trials.Where(t => !t.Attack.Holdout).ToList();
            var hold = trials.Where(t => t.Attack.Holdout).ToList();
            text.AppendLine(string.Create(CultureInfo.InvariantCulture,
                $"| {name} | {Mean(train):0.00} | {train.Count(t => t.Struck)}/{train.Count} | {Mean(hold):0.00} | {hold.Count(t => t.Struck)}/{hold.Count} | {trials.Count(t => t.ClashSteps > 0)}/{trials.Count} | {trials.Average(t => t.SelfCells):0} | {trials.Average(t => t.BladeLost):0} | {trials.Average(t => t.TheirLost):0} | {trials.Average(t => t.BladeCarved):0}・{trials.Sum(t => t.BladeSnaps)} | {trials.Count(t => t.Killed)} |"));
        }

        pick = byVariant.Skip(1).MinBy(x => Mean([.. x.Trials.Where(t => !t.Attack.Holdout)])).Name;
        text.AppendLine();
        text.AppendLine(CultureInfo.InvariantCulture, $"練習の組で失ったものがいちばん少ない守り方: **{pick}**");
        text.AppendLine();
        text.AppendLine("打ち手ごとの失ったもの（種の平均）:");
        text.AppendLine();
        text.AppendLine(CultureInfo.InvariantCulture, $"| 打ち手 | 組 | {string.Join(" | ", variants.Select(v => v.Name))} |");
        text.AppendLine(CultureInfo.InvariantCulture, $"|---|---|{string.Concat(variants.Select(_ => "---:|"))}");
        foreach (var attack in attacks)
        {
            var cells = byVariant.Select(x => Mean([.. x.Trials.Where(t => t.Attack == attack)]));
            text.AppendLine(string.Create(CultureInfo.InvariantCulture,
                $"| {attack.Name} | {(attack.Holdout ? "確かめ" : "練習")} | {string.Join(" | ", cells.Select(c => c.ToString("0.00", CultureInfo.InvariantCulture)))} |"));
        }

        text.AppendLine();
        text.AppendLine(string.Create(CultureInfo.InvariantCulture, $"（{watch.Elapsed.TotalMinutes:0.0} 分）"));
        return text.ToString();
    }

    private static float Mean(List<Trial> trials) => trials.Count == 0 ? 0f : trials.Average(t => t.Harm);
}
