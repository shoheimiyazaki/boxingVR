using System.Globalization;
using System.Text;
using System.Text.Json;
using MiniRing.Motion;

namespace MiniRing.Arena;

/// <summary>
/// 狙いのドリル: 剣の技ごとに、狙う部位（頭・胸・腰・上腕・前腕）を決めて、構えの違う的（ふつう・屈む・反る・高い構え・低い構え）へ 1 本ずつ打つ。
/// 狙った部位をいちばん斬れたら「打てた」。
/// <list type="number">
/// <item>狙わない（技の通り道のまま）と、狙いの付け方（<see cref="AimTuning"/>）のいくつかを比べ、狙った部位を打てた本数がいちばん多い付け方を採る。</item>
/// <item>採った付け方で、技ごと・部位ごとの打てた割合を技の <see cref="MotionTechnique.Skill"/> に書き、一覧へ戻す（剣士は狙う部位をいちばん打てる技で狙う）。</item>
/// </list>
/// </summary>
internal static class AimLab
{
    private static readonly JsonSerializerOptions Json = new() { WriteIndented = true };

    private sealed record Trial(MotionTechnique Technique, BodyPartKind Target, GuardPosture Posture, bool Hit, int OnTarget, BodyPartKind? MostCut);

    public static string Run(SwordLibraryLab.Library library, IReadOnlyList<AimTuning> candidates, string libraryOut, string tuningOut, TextWriter log)
    {
        if (candidates.Count == 0)
        {
            throw new ArgumentException("狙いの付け方の候補が 1 つも無い。", nameof(candidates));
        }

        var watch = System.Diagnostics.Stopwatch.StartNew();
        var report = new StringBuilder();
        report.AppendLine("# 狙いのドリル（aimlab）");
        report.AppendLine();
        report.AppendLine(string.Create(CultureInfo.InvariantCulture,
            $"技 {library.Techniques.Count} 本 × 狙う部位 {MotionAim.Targets.Count}（{string.Join("・", MotionAim.Targets.Select(Ja))}）× 的の構え {GuardPosture.All.Count}（{string.Join("・", GuardPosture.All.Select(p => p.Name))}）。狙った部位をいちばん斬れたら「打てた」。"));
        report.AppendLine();
        report.AppendLine("| 狙いの付け方 | 打てた本数 | 割合 | 部位ごと（頭・胸・腰・上腕・前腕の、いちばん打てる技の割合） |");
        report.AppendLine("|---|---:|---:|---|");

        var baseline = Trials(library, null, log);
        report.AppendLine(Row("狙わない（技の通り道のまま）", baseline));
        List<Trial>? best = null;
        AimTuning? bestTuning = null;
        foreach (var tuning in candidates)
        {
            var trials = Trials(library, tuning, log);
            report.AppendLine(Row(Describe(tuning), trials));
            if (best is null || trials.Count(t => t.Hit) > best.Count(t => t.Hit))
            {
                best = trials;
                bestTuning = tuning;
            }
        }

        // 採った付け方で、技ごとの打てた割合を一覧へ書く。
        var skilled = library.Techniques.Select(technique =>
        {
            var mine = best!.Where(t => t.Technique == technique).ToList();
            var skill = MotionAim.Targets.ToDictionary(target => target,
                target => (float)mine.Count(t => t.Target == target && t.Hit) / Math.Max(1, mine.Count(t => t.Target == target)));
            return technique with { Skill = skill };
        }).ToList();
        SwordLibraryLab.Save(libraryOut, library with { Techniques = skilled });
        // 狙わない方がよく打てるなら、付け方は書き出さない（今のままにする）。
        var better = best!.Count(t => t.Hit) > baseline.Count(t => t.Hit);
        if (better)
        {
            File.WriteAllText(tuningOut, JsonSerializer.Serialize(bestTuning, Json));
        }

        report.AppendLine();
        report.AppendLine(better ? $"採った付け方: {Describe(bestTuning!)}（{tuningOut}）。" : $"どの付け方も狙わないより打てなかったので、付け方は書き出さない（いちばん良かったのは {Describe(bestTuning!)}）。");
        report.AppendLine();
        report.AppendLine("技ごとの打てた割合（採った付け方、5 つの構えのうち）:");
        report.AppendLine();
        report.AppendLine("| 技 | 剣 | 間合い | 頭 | 胸 | 腰 | 上腕 | 前腕 |");
        report.AppendLine("|---|---|---:|---:|---:|---:|---:|---:|");
        foreach (var t in skilled)
        {
            report.AppendLine(string.Create(CultureInfo.InvariantCulture,
                $"| {t.Name} | {t.Weapon} | {t.Range * 100f:0} cm | {string.Join(" | ", MotionAim.Targets.Select(p => $"{t.Skill![p] * 100f:0}%"))} |"));
        }

        report.AppendLine();
        report.AppendLine("構えごとの打てた本数（採った付け方 / 狙わない）:");
        report.AppendLine();
        report.AppendLine("| 構え | " + string.Join(" | ", MotionAim.Targets.Select(Ja)) + " |");
        report.AppendLine("|---|" + string.Concat(MotionAim.Targets.Select(_ => "---:|")));
        foreach (var posture in GuardPosture.All)
        {
            report.AppendLine($"| {posture.Name} | " + string.Join(" | ", MotionAim.Targets.Select(target =>
                $"{best!.Count(t => t.Posture == posture && t.Target == target && t.Hit)} / {baseline.Count(t => t.Posture == posture && t.Target == target && t.Hit)}")) + " |");
        }

        report.AppendLine();
        report.AppendLine(string.Create(CultureInfo.InvariantCulture, $"（かかった時間 {watch.Elapsed.TotalMinutes:0.0} 分）"));
        return report.ToString();
    }

    private static List<Trial> Trials(SwordLibraryLab.Library library, AimTuning? tuning, TextWriter log)
    {
        var jobs = (from technique in library.Techniques
                    from target in MotionAim.Targets
                    from posture in GuardPosture.All
                    select (technique, target, posture)).ToList();
        var trials = new Trial[jobs.Count];
        Parallel.For(0, jobs.Count, i =>
        {
            var (technique, target, posture) = jobs[i];
            var r = StrikeDrill.Run(technique, library.Guards[technique.Weapon], SwordStance.Rate, SwordStance.Tuning,
                target: () => new StandGuard(posture), aim: tuning is null ? null : target, aimTuning: tuning);
            var on = r.CellsByPart.GetValueOrDefault(target);
            trials[i] = new Trial(technique, target, posture, on > 0 && r.MostCut == target, on, r.MostCut);
        });
        log.WriteLine($"{(tuning is null ? "no aim" : Describe(tuning))}: {trials.Count(t => t.Hit)}/{trials.Length}");
        return [.. trials];
    }

    private static string Row(string label, List<Trial> trials)
    {
        var perTarget = MotionAim.Targets.Select(target =>
        {
            var best = trials.Where(t => t.Target == target).GroupBy(t => t.Technique).Max(g => (float)g.Count(t => t.Hit) / g.Count());
            return string.Create(CultureInfo.InvariantCulture, $"{Ja(target)} {best * 100f:0}%");
        });
        return string.Create(CultureInfo.InvariantCulture,
            $"| {label} | {trials.Count(t => t.Hit)} / {trials.Count} | {100f * trials.Count(t => t.Hit) / trials.Count:0}% | {string.Join("・", perTarget)} |");
    }

    private static string Describe(AimTuning t) => string.Create(CultureInfo.InvariantCulture,
        $"ずらす割合 {t.Gain:0.00}・ずらしきる時 {t.Lead:0.00}・上限 {t.MaxShift:0.00}・間合いへ足す割合 {t.ForwardGain:0.00}");

    internal static string Ja(BodyPartKind part) => part switch
    {
        BodyPartKind.Head => "頭",
        BodyPartKind.Chest => "胸",
        BodyPartKind.Pelvis => "腰",
        BodyPartKind.UpperArm => "上腕",
        BodyPartKind.Forearm => "前腕",
        _ => part.ToString(),
    };

    public static AimTuning Load(string path) => JsonSerializer.Deserialize<AimTuning>(File.ReadAllText(path))?.Validated() ?? throw new InvalidDataException($"{path} を読めません。");
}
