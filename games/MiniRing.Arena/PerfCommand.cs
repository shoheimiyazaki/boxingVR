using System.Diagnostics;
using System.Globalization;
using System.Text;
using Aigis.Foundation.Voxels;
using MiniRing.Bots;
using MiniRing.Characters;
using MiniRing.Weapons;

namespace MiniRing.Arena;

/// <summary>
/// 物理の 1 フレーム（<c>game.Step(1/60)</c> = 240 Hz の 4 歩）の時間を処理ごとに測る（docs/PERFORMANCE.md）。
/// 場面: <c>fist</c>（素手 2 人の基準）・<c>katana</c>（刀どうし、SwordBot）・<c>vfc2</c>（VFC の武器戦 2 人、jin 対 konrad）・<c>royale</c>（6 人のバトルロイヤル）。
/// 測る走りの前に、計測を切った同じ試合を回して最終チェックサムを比べる（計測が結果に響かないこと、§1.6）。<c>--render</c> で描画の CPU の詰め（<c>BoxingRenderer.Fill</c>・<c>FillVoxels</c>）も測る。
/// </summary>
internal static class PerfCommand
{
    private const float Dt = 1f / 60f;

    /// <summary>入れ子の外側の枠（1 歩の合計を分ける。内側の枠は外側に含まれる）。</summary>
    private static readonly StepProfile.Slot[] TopLevel =
    [
        StepProfile.Slot.Control, StepProfile.Slot.PreBepu, StepProfile.Slot.Bepu, StepProfile.Slot.CanvasImpacts, StepProfile.Slot.FistHits,
        StepProfile.Slot.Weapons, StepProfile.Slot.Disconnect, StepProfile.Slot.Kicks, StepProfile.Slot.Elbows, StepProfile.Slot.FighterSplit,
        StepProfile.Slot.After, StepProfile.Slot.PostStep,
    ];

    private sealed record Frame(int Seed, int Index, double Total, double[] Slots, double Render, int Gc0, int Gc2, int Fragments, long[] Counts, long[] Allocs);

    public static int Run(Options o)
    {
        var scene = o.Get("scene") ?? "katana";
        var seconds = o.Float("seconds", 60f, min: 1f);
        var seeds = o.List("seeds", ["1"]).Select(s => int.Parse(s, CultureInfo.InvariantCulture)).ToList();
        var render = o.Get("render") is "1" or "true";
        var check = o.Get("check") is not ("0" or "false");
        var frames = (int)MathF.Round(seconds / Dt);

        // 暖機（JIT）。
        using (var warm = Create(scene, 100))
        {
            for (var i = 0; i < 120; i++)
            {
                warm.Step(default, Dt);
            }
        }

        var plain = new List<string>();
        if (check)
        {
            foreach (var seed in seeds)
            {
                using var game = Create(scene, seed);
                for (var i = 0; i < frames && !game.MatchOver; i++)
                {
                    game.Step(default, Dt);
                }

                plain.Add($"{game.StateChecksum():x}");
            }
        }

        var slots = Enum.GetValues<StepProfile.Slot>();
        var samples = new List<Frame>();
        var sums = new List<string>();
        var instances = new List<Aigis.Render.PrimitiveInstance>();
        var lines = new List<Aigis.Render.DebugLine>();
        var voxels = new List<Aigis.Render.Voxels.VoxelVolumeDraw>();
        var wall = Stopwatch.StartNew();
        StepProfile.Enabled = true;
        StepProfile.TrackAllocations = o.Get("alloc") is "1" or "true";
        try
        {
            foreach (var seed in seeds)
            {
                using var game = Create(scene, seed);
                for (var i = 0; i < frames && !game.MatchOver; i++)
                {
                    StepProfile.Reset();
                    var gc0 = GC.CollectionCount(0);
                    var gc2 = GC.CollectionCount(2);
                    var start = Stopwatch.GetTimestamp();
                    game.Step(default, Dt);
                    var total = Stopwatch.GetElapsedTime(start).TotalMilliseconds;
                    var slotMs = slots.Select(StepProfile.Milliseconds).ToArray();
                    var renderMs = 0.0;
                    if (render)
                    {
                        var r = Stopwatch.GetTimestamp();
                        BoxingRenderer.Fill(game, false, instances, lines);
                        BoxingRenderer.FillVoxels(game, voxels);
                        renderMs = Stopwatch.GetElapsedTime(r).TotalMilliseconds;
                    }

                    var fragments = game.AllFighters.Sum(f => f.Voxels?.Fragments.Count ?? 0);
                    var counts = Enum.GetValues<StepProfile.Counter>().Select(StepProfile.CountOf).ToArray();
                    var allocs = slots.Select(StepProfile.Allocated).ToArray();
                    samples.Add(new Frame(seed, i, total, slotMs, renderMs, GC.CollectionCount(0) - gc0, GC.CollectionCount(2) - gc2, fragments, counts, allocs));
                }

                sums.Add($"{game.StateChecksum():x}");
            }
        }
        finally
        {
            StepProfile.Enabled = false;
            StepProfile.TrackAllocations = false;
        }

        var text = Report(scene, seconds, seeds, samples, slots, sums, plain, render, wall.Elapsed.TotalSeconds);
        Console.Write(text);
        if (o.Get("out") is { } outPath)
        {
            File.AppendAllText(outPath, text);
        }

        return check && !plain.SequenceEqual(sums) ? 1 : 0;
    }

    private static BoxingGame Create(string scene, int seed) => scene switch
    {
        "fist" => new BoxingGame(seed: seed, corners: BoxingCorners.Default with
        {
            CpuName = "swarmer", Cpu = s => BotCatalog.Create("swarmer", s), Player = s => BotCatalog.Create("champion", s),
        }),
        "katana" => new BoxingGame(seed: seed, corners: BoxingCorners.Default with
        {
            Player = SwordBot.Basic, Cpu = SwordBot.Basic,
            PlayerWeapon = WeaponKind.Katana, CpuWeapon = WeaponKind.Katana,
            PlayerTwoHanded = true, CpuTwoHanded = true,
        }),
        "vfc2" => Vfc(seed, ["jin", "konrad"]),
        "royale" => Vfc(seed, RoyaleCommand.DefaultFighters),
        _ => throw new ArgumentException($"--scene は fist・katana・vfc2・royale のどれか（{scene}）"),
    };

    private static BoxingGame Vfc(int seed, IReadOnlyList<string> ids)
    {
        var org = Organizations.Get("vfc");
        var rules = TvShowRules.Rules(5, 15f, org) with { IntroSeconds = 1.5f, EndSeconds = 0f };
        var corners = RoyaleCorners.From([.. ids.Select(CharacterRoster.Get)]) with { SettleSeconds = 0.5f, BreakActions = false };
        return new BoxingGame(FighterTuning.Default, seed, corners, rules, org.ArenaMood);
    }

    private static string Report(string scene, float seconds, IReadOnlyList<int> seeds, List<Frame> samples, StepProfile.Slot[] slots,
        List<string> sums, List<string> plain, bool render, double wallSeconds)
    {
        var inv = CultureInfo.InvariantCulture;
        var sb = new StringBuilder();
        sb.AppendLine(inv, $"## perf {scene}（{DateTime.Now:yyyy-MM-dd HH:mm}、{Environment.MachineName}）");
        sb.AppendLine();
        sb.AppendLine(inv, $"種 {string.Join(",", seeds)} × 最大 {seconds:0} 秒、{samples.Count} フレーム（1 フレーム = `Step(1/60)` = 240 Hz の 4 歩）。壁時計 {wallSeconds:0.0} s。物理 {Fighter.PhysicsVersion}・判断 {BoxerBot.LogicVersion}。");
        var plainText = plain.Count > 0 ? $" / 計測なし: {string.Join(" ", plain)}（{(plain.SequenceEqual(sums) ? "一致" : "**不一致**")}）" : string.Empty;
        sb.AppendLine(inv, $"チェックサム（計測あり）: {string.Join(" ", sums)}{plainText}");
        sb.AppendLine();
        var totals = samples.Select(s => s.Total).Order().ToList();
        double Pct(List<double> v, double p) => v.Count == 0 ? 0 : v[Math.Min(v.Count - 1, (int)(v.Count * p))];
        sb.AppendLine("| フレーム [ms] | 平均 | 中央値 | p95 | p99 | 最大 | 16.7 ms 超の割合 | GC0 / GC2 |");
        sb.AppendLine("|---|---:|---:|---:|---:|---:|---:|---|");
        sb.AppendLine(inv, $"| 物理 | {totals.Average():F2} | {Pct(totals, 0.5):F2} | {Pct(totals, 0.95):F2} | {Pct(totals, 0.99):F2} | {totals[^1]:F2} | {totals.Count(t => t > 1000.0 / 60.0) * 100.0 / totals.Count:F1}% | {samples.Sum(s => s.Gc0)} / {samples.Sum(s => s.Gc2)} |");
        if (render)
        {
            var r = samples.Select(s => s.Render).Order().ToList();
            sb.AppendLine(inv, $"| 描画の詰め（CPU） | {r.Average():F2} | {Pct(r, 0.5):F2} | {Pct(r, 0.95):F2} | {Pct(r, 0.99):F2} | {r[^1]:F2} | | |");
        }

        sb.AppendLine();
        var heavy = samples.OrderByDescending(s => s.Total).Take(Math.Max(1, samples.Count / 100)).ToList();
        sb.AppendLine("処理ごと（ms/フレーム。字下げは外側の枠に含まれる内訳。「重い 1%」は合計の重い上位 1% のフレームの平均）:");
        sb.AppendLine();
        var alloc = samples.Any(s => s.Allocs.Any(a => a > 0));
        sb.AppendLine("| 処理 | 平均 | 割合 | 重い 1% の平均 | 最大 |" + (alloc ? " 割り当て [KB/フレーム] |" : string.Empty));
        sb.AppendLine("|---|---:|---:|---:|---:|" + (alloc ? "---:|" : string.Empty));
        var mean = totals.Average();
        foreach (var slot in Order(slots))
        {
            var k = (int)slot;
            var avg = samples.Average(s => s.Slots[k]);
            var max = samples.Max(s => s.Slots[k]);
            if (max < 0.005)
            {
                continue;
            }

            var indent = TopLevel.Contains(slot) ? string.Empty : "　";
            var allocText = alloc ? string.Create(inv, $" {samples.Average(s => s.Allocs[k]) / 1024.0:F1} |") : string.Empty;
            sb.AppendLine(inv, $"| {indent}{slot} | {avg:F3} | {avg * 100 / mean:F1}% | {heavy.Average(s => s.Slots[k]):F2} | {max:F2} |{allocText}");
        }

        var accounted = samples.Average(s => TopLevel.Sum(t => s.Slots[(int)t]));
        sb.AppendLine(inv, $"| （枠の外） | {mean - accounted:F3} | {(mean - accounted) * 100 / mean:F1}% | | |");
        sb.AppendLine();
        sb.AppendLine("数（フレームあたりの平均 / 重い 1% の平均）: " + string.Join("、", Enum.GetValues<StepProfile.Counter>().Select(c =>
            string.Create(inv, $"{c} {samples.Average(s => s.Counts[(int)c]):F0} / {heavy.Average(s => s.Counts[(int)c]):F0}"))));
        sb.AppendLine();
        sb.AppendLine("重いフレームの上位 10（ms、0.3 ms 以上の枠だけ）:");
        sb.AppendLine();
        foreach (var f in samples.OrderByDescending(s => s.Total).Take(10))
        {
            var parts = slots.Where(s => f.Slots[(int)s] >= 0.3).OrderByDescending(s => f.Slots[(int)s]).Select(s => $"{s} {f.Slots[(int)s]:F1}");
            sb.AppendLine(inv, $"- 種 {f.Seed} の {f.Index * Dt:F2} s: **{f.Total:F1}**（破片 {f.Fragments}、GC0 {f.Gc0}）: {string.Join("・", parts)}");
        }

        sb.AppendLine();
        return sb.ToString();
    }

    /// <summary>外側の枠の後に、その内訳を並べる（枠の名前の順は StepProfile の宣言順）。</summary>
    private static IEnumerable<StepProfile.Slot> Order(StepProfile.Slot[] slots)
    {
        var children = new Dictionary<StepProfile.Slot, StepProfile.Slot[]>
        {
            [StepProfile.Slot.Control] = [StepProfile.Slot.Perception, StepProfile.Slot.BotRead, StepProfile.Slot.BotPerceive, StepProfile.Slot.BotRead2, StepProfile.Slot.BotChoose, StepProfile.Slot.BotExecute, StepProfile.Slot.DriveFighter, StepProfile.Slot.ApplyControl, StepProfile.Slot.FighterStamina, StepProfile.Slot.FighterStanding, StepProfile.Slot.FighterDown, StepProfile.Slot.ControlTail],
            [StepProfile.Slot.PreBepu] = [StepProfile.Slot.Remember],
            [StepProfile.Slot.Weapons] = [StepProfile.Slot.MayTouch, StepProfile.Slot.SweepSetup, StepProfile.Slot.SetupRelative, StepProfile.Slot.SetupFar, StepProfile.Slot.SetupCandidates, StepProfile.Slot.SweepRun, StepProfile.Slot.RunScan, StepProfile.Slot.RunFront, StepProfile.Slot.RunOverlaps, StepProfile.Slot.RunPatch, StepProfile.Slot.RunHold, StepProfile.Slot.RunCrush, StepProfile.Slot.RunCut, StepProfile.Slot.RunSeparate, StepProfile.Slot.RunMass, StepProfile.Slot.Snap, StepProfile.Slot.SnapStiffness, StepProfile.Slot.SnapFind, StepProfile.Slot.SnapSplit, StepProfile.Slot.Bend, StepProfile.Slot.Cut, StepProfile.Slot.Debris, StepProfile.Slot.WeaponClash, StepProfile.Slot.ClashAfter, StepProfile.Slot.ClashPush, StepProfile.Slot.Bridges, StepProfile.Slot.BridgeRefresh],
            [StepProfile.Slot.PostStep] = [StepProfile.Slot.PostDebris, StepProfile.Slot.PostWounds, StepProfile.Slot.PostSwings, StepProfile.Slot.PostRound],
        };
        var listed = new HashSet<StepProfile.Slot>();
        foreach (var top in TopLevel)
        {
            listed.Add(top);
            yield return top;
            foreach (var child in children.GetValueOrDefault(top, []))
            {
                listed.Add(child);
                yield return child;
            }
        }

        foreach (var slot in slots.Where(s => !listed.Contains(s)))
        {
            yield return slot;
        }
    }
}
