using System.Globalization;
using System.Numerics;
using System.Text;
using MiniRing.Bots;
using MiniRing.Motion;
using MiniRing.Weapons;

namespace MiniRing.Arena;

/// <summary>
/// 調査と回帰の物差し: 握った武器が握った本人の体に触れた回数（<see cref="BoxingGame.SelfTouches"/>）を、練習試合の 1 試合ごとに数え、
/// そのときの技・姿勢で原因を分ける。
/// <list type="bullet">
/// <item>事故: 本人が立っていない（ダウン・倒れかけ）、または直前（<see cref="AccidentWindow"/> 秒以内）に打たれて体勢が崩れている。</item>
/// <item>決めた振りの道が体を通る（判断）: 技を出している最中（<see cref="SwordsmanBot.Current"/>）に、事故でなく自分の体を削った。</item>
/// <item>守りの刀が体を削る（判断）: 技を出していない守りの最中。</item>
/// <item>その他（構え・歩き）。</item>
/// </list>
/// 触れただけ（削らなかった）と削った（セルを壊した）を分けて数える。同じ連続した歩の触れ合いは 1 回。
/// </summary>
internal static class SelfCutLab
{
    private const float Dt = 1f / 60f;

    /// <summary>接触を掃き直す回数（<see cref="BoxingGame.MaxContactPasses"/>。1 なら掃き直し前の解き方）。<c>--passes</c> で変える。</summary>
    public static int Passes { get; set; } = 3;

    /// <summary>打たれてからこの秒数のうちは、体勢が崩れている（事故）とみなす。</summary>
    public const float AccidentWindow = 0.4f;

    /// <summary>
    /// 1 回の自傷。<paramref name="TargetClear"/> は、その時ボットが出していた目標（右手の位置と剣の向き）の刃の線が、自分の体の箱（<see cref="SelfClearance"/>）から
    /// 離れている距離 [腕の長さ]（負なら目標が体の中）、<paramref name="ActualClear"/> は実際の刃の線（体が追った結果）の同じ値。
    /// </summary>
    internal sealed record Episode(string Pair, string Side, float At, string Technique, string Cause, BodyPartKind Part, int Removed, float TargetClear, float ActualClear, float AngleError = float.NaN, float GripError = float.NaN);

    /// <summary>ボットの出した指令を覚える（自傷のとき、目標を見直すため）。</summary>
    private sealed class Tap(IFighterControl inner) : IFighterControl
    {
        public IFighterControl Inner { get; } = inner;

        public FighterCommand Last { get; private set; }

        public FighterCommand Read(in FighterSense sense, float dt) => Last = Inner.Read(sense, dt);
    }

    internal sealed record Bout(string Pair, int Seed, List<Episode> Episodes, int RedLost, int BlueLost, int Winner);

    /// <summary>組（名前-名前）を、種 <paramref name="seeds"/> 通り、<paramref name="seconds"/> 秒ずつ戦わせて数える。</summary>
    public static IReadOnlyList<Bout> Collect(IReadOnlyList<Contender> contenders, string[] pairs, int seeds, float seconds, FighterTuning? tuning = null)
    {
        var jobs = new List<(Contender Red, Contender Blue, string Pair, int Seed)>();
        foreach (var pair in pairs)
        {
            var names = pair.Split('-');
            var a = contenders.Single(c => c.Name == names[0]);
            var b = contenders.Single(c => c.Name == names[1]);
            for (var k = 0; k < seeds; k++)
            {
                // 半分ずつコーナーを入れ替える。
                jobs.Add(k % 2 == 0 ? (a, b, pair, 1 + (k / 2)) : (b, a, pair, 1 + (k / 2)));
            }
        }

        var results = new Bout[jobs.Count];
        Parallel.For(0, jobs.Count, i => results[i] = Run(jobs[i].Red, jobs[i].Blue, jobs[i].Pair, jobs[i].Seed, seconds, tuning ?? VersusLab.Tuning));
        return results;
    }

    private static readonly SelfClearance Boxes = new();

    /// <summary>今の指令の目標の刃の線と、実際の刃の線の、自分の体の箱からの離れ（腕の長さ。負なら中）。</summary>
    private static (float Target, float Actual, float Angle, float Grip) Clearances(BoxingGame game, Fighter fighter, Weapon weapon, FighterCommand command)
    {
        var reach = fighter.ArmReach;
        var length = SwordLibraryLab.BladeLength(weapon.Kind);
        var right = command.Right;
        var tBlade = Vector3.Transform(Vector3.UnitX, right.Orientation ?? Quaternion.Identity);
        var target = Boxes.Along(right.TargetLocal / reach, tBlade, length).Clearance;
        var frame = fighter.Frame;
        var state = game.World.GetState(weapon.Body.BodyId);
        var grip = fighter.WieldedGrip(BoxingGame.WeaponHand) ?? Weapon.GripOf(weapon.Kind);
        var gripWorld = state.Position + Vector3.Transform(grip, state.Orientation);
        var bladeWorld = Vector3.Transform(Vector3.UnitX, state.Orientation);
        var actual = Boxes.Along(frame.WorldToLocal(gripWorld) / reach, frame.InverseRotate(bladeWorld), length).Clearance;
        var angle = MathF.Acos(Math.Clamp(Vector3.Dot(Vector3.Normalize(tBlade), Vector3.Normalize(frame.InverseRotate(bladeWorld))), -1f, 1f)) * 180f / MathF.PI;
        var gripError = Vector3.Distance(right.TargetLocal / reach, frame.WorldToLocal(gripWorld) / reach);
        return (target, actual, angle, gripError);
    }

    private static Bout Run(Contender red, Contender blue, string pair, int seed, float seconds, FighterTuning tuning)
    {
        var reds = new List<DisarmedFallback>();
        var blues = new List<DisarmedFallback>();
        IFighterControl Make(Contender c, int s, List<DisarmedFallback> into)
        {
            var bot = DisarmedFallback.WithFists(new Tap(c.Create(s)), s);
            into.Add(bot);
            return bot;
        }

        using var game = new BoxingGame(tuning, seed, new BoxingCorners(blue.Name, s => Make(blue, s, blues), s => Make(red, s, reds),
            CpuWeapon: blue.Weapon, PlayerWeapon: red.Weapon, CpuTwoHanded: blue.TwoHanded, PlayerTwoHanded: red.TwoHanded));
        game.MaxContactPasses = Passes;
        var episodes = new List<Episode>();
        var lastHit = new Dictionary<Fighter, float>();
        var open = new Dictionary<Fighter, (Episode Episode, float Last)>();
        var redStart = game.Player.Voxels!.Parts.Sum(p => p.Body.Grid.FilledCount);
        var blueStart = game.Cpu.Voxels!.Parts.Sum(p => p.Body.Grid.FilledCount);
        var t = 0f;
        for (; t < seconds && !game.Player.IsDead && !game.Cpu.IsDead; t += Dt)
        {
            game.Step(default, Dt);
            foreach (var hit in game.RecentHits)
            {
                lastHit[hit.Victim] = t;
            }

            foreach (var hit in game.WeaponHits)
            {
                lastHit[hit.Victim] = t;
            }

            foreach (var touch in game.SelfTouches)
            {
                var fighter = touch.Fighter;
                if (fighter != game.Player && fighter != game.Cpu)
                {
                    continue; // その歩のうちにラウンドが変わって、体ごと作り直された（世界は消えている）
                }

                var side = fighter == game.Player ? "赤" : "青";
                if (open.TryGetValue(fighter, out var o) && t - o.Last <= 2.5f * Dt && o.Episode.Part == touch.Part)
                {
                    open[fighter] = (o.Episode with { Removed = o.Episode.Removed + touch.Removed }, t);
                    continue;
                }

                if (open.TryGetValue(fighter, out o))
                {
                    episodes.Add(o.Episode);
                }

                var tap = (fighter == game.Player ? reds : blues).LastOrDefault()?.Armed as Tap;
                var bot = tap?.Inner as SwordsmanBot;
                var (targetClear, actualClear, angleError, gripError) = tap is null ? (float.NaN, float.NaN, float.NaN, float.NaN) : Clearances(game, fighter, touch.Weapon, tap.Last);
                var technique = bot is null ? "(剣士でない)" : bot.Current?.Name ?? (bot.Defending ? (bot.Blocking ? "守り（受け）" : "守り（外し）") : "(構え・歩き)");
                var staggered = fighter.State != FighterState.Standing || (lastHit.TryGetValue(fighter, out var at) && t - at <= AccidentWindow);
                var phase = bot?.Current is not null ? "技" : bot?.Defending == true ? "守り" : "構え・歩き";
                var cause = staggered ? "事故"
                    : touch.Part is BodyPartKind.UpperArm or BodyPartKind.Forearm ? $"腕に（{phase}）"
                    : targetClear < 0f ? $"目標が体を通る（{phase}）"
                    : $"目標は離れているのに体が追って（{phase}）";
                open[fighter] = (new Episode(pair, side, t, technique, cause, touch.Part, touch.Removed, targetClear, actualClear, angleError, gripError), t);
            }
        }

        episodes.AddRange(open.Values.Select(v => v.Episode));
        return new Bout(pair, seed, episodes, redStart - game.Player.Voxels!.Parts.Sum(p => p.Body.Grid.FilledCount), blueStart - game.Cpu.Voxels!.Parts.Sum(p => p.Body.Grid.FilledCount),
            game.PlayerWins == game.CpuWins ? 0 : game.PlayerWins > game.CpuWins ? 1 : -1);
    }

    private static string Mean(IEnumerable<float> values) =>
        values.Where(v => !float.IsNaN(v)).ToList() is { Count: > 0 } ok ? ok.Average().ToString("0.00", CultureInfo.InvariantCulture) : "-";

    /// <summary>集計の表（Markdown）。</summary>
    public static string Report(IReadOnlyList<Bout> bouts, float seconds)
    {
        var text = new StringBuilder();
        var all = bouts.SelectMany(b => b.Episodes).ToList();
        text.AppendLine(string.Create(CultureInfo.InvariantCulture, $"{bouts.Count} 試合（各 {seconds:0} 秒まで）。「削った」= 自分の武器が自分の体のセルを壊した連続した接触、「触れた」= 壊さずに触れただけ。"));
        text.AppendLine();
        text.AppendLine("| 原因 | 削った | 触れた（削らず） | 削ったセル |");
        text.AppendLine("|---|---:|---:|---:|");
        foreach (var g in all.GroupBy(e => e.Cause).OrderBy(g => g.Key))
        {
            text.AppendLine(string.Create(CultureInfo.InvariantCulture, $"| {g.Key} | {g.Count(e => e.Removed > 0)} | {g.Count(e => e.Removed == 0)} | {g.Sum(e => e.Removed)} |"));
        }

        text.AppendLine();
        text.AppendLine(string.Create(CultureInfo.InvariantCulture, $"事故でない自傷（削った）: {all.Count(e => e.Cause != "事故" && e.Removed > 0)} 回 / {bouts.Count} 試合。事故: {all.Count(e => e.Cause == "事故" && e.Removed > 0)} 回。"));
        text.AppendLine();
        text.AppendLine("| 技（その時） | 原因 | 削った | 触れた | 削ったセル | 目標の離れ（平均） | 実際の離れ（平均） | 刃の向きのずれ [度] | 握りのずれ [腕の長さ] | 部位 |");
        text.AppendLine("|---|---|---:|---:|---:|---:|---:|---:|---:|---|");
        foreach (var g in all.GroupBy(e => (e.Technique, e.Cause)).OrderByDescending(g => g.Sum(e => e.Removed)))
        {
            var parts = string.Join("・", g.GroupBy(e => e.Part).OrderByDescending(p => p.Count()).Take(3).Select(p => $"{p.Key}×{p.Count()}"));
            text.AppendLine(string.Create(CultureInfo.InvariantCulture,
                $"| {g.Key.Technique} | {g.Key.Cause} | {g.Count(e => e.Removed > 0)} | {g.Count(e => e.Removed == 0)} | {g.Sum(e => e.Removed)} | {Mean(g.Select(e => e.TargetClear))} | {Mean(g.Select(e => e.ActualClear))} | {Mean(g.Select(e => e.AngleError))} | {Mean(g.Select(e => e.GripError))} | {parts} |"));
        }

        text.AppendLine();
        text.AppendLine("| 組 | 試合 | 赤が失ったセル（平均） | 青が失ったセル（平均） |");
        text.AppendLine("|---|---:|---:|---:|");
        foreach (var g in bouts.GroupBy(b => b.Pair))
        {
            text.AppendLine(string.Create(CultureInfo.InvariantCulture, $"| {g.Key} | {g.Count()} | {g.Average(b => b.RedLost):0} | {g.Average(b => b.BlueLost):0} |"));
        }

        return text.ToString();
    }
}
