using System.Globalization;
using System.Text;
using MiniRing.Bots;

namespace MiniRing.Arena;

/// <summary>対戦結果を Markdown の表にする（docs/BOT_PDCA.md にそのまま貼れる形）。</summary>
internal static class Report
{
    private static readonly CultureInfo Inv = CultureInfo.InvariantCulture;

    public static string Standings(IReadOnlyList<Standing> rows)
    {
        var sb = new StringBuilder();
        sb.AppendLine("| # | ボット | 勝-分-敗 | 勝率 | 与ダメージ | 被ダメージ | 比 | KO/RO で取ったラウンド | 奪ったダウン |");
        sb.AppendLine("|---|---|---|---|---|---|---|---|---|");
        for (var i = 0; i < rows.Count; i++)
        {
            var s = rows[i];
            sb.AppendLine(string.Create(Inv,
                $"| {i + 1} | {s.Name} | {s.Wins}-{s.Draws}-{s.Losses} | {s.Score:P0} | {s.DamageFor / s.Bouts:0} | {s.DamageAgainst / s.Bouts:0} | {s.DamageRatio:0.00} | {s.RoundsWon} | {s.Knockdowns} |"));
        }

        sb.AppendLine();
        sb.AppendLine("与ダメージ・被ダメージは 1 試合あたり。");
        return sb.ToString();
    }

    /// <summary>ボットごとの技の内訳（手数・命中率・1 発の平均ダメージ）と守りの成否。</summary>
    public static string Breakdown(IReadOnlyList<Standing> rows)
    {
        var sb = new StringBuilder();
        foreach (var s in rows.Where(r => r.Stats.Techniques.Count > 0))
        {
            sb.AppendLine(string.Create(Inv, $"#### {s.Name}"));
            sb.AppendLine();
            sb.AppendLine("| 技 | 手数/試合 | 命中率 | 平均ダメージ |");
            sb.AppendLine("|---|---|---|---|");
            foreach (var (technique, c) in s.Stats.Techniques.OrderByDescending(p => p.Value.Thrown))
            {
                sb.AppendLine(string.Create(Inv,
                    $"| {technique} | {(float)c.Thrown / s.Bouts:0.0} | {c.Accuracy:P0} | {(c.Landed > 0 ? c.Damage / c.Landed : 0f):0.0} |"));
            }

            var st = s.Stats;
            sb.AppendLine();
            sb.Append("守り（成功率 = 守りの最中に打たれなかった割合）: ");
            sb.AppendLine(st.Defenses.Count == 0 ? "なし" : string.Join(" / ", st.Defenses.OrderByDescending(p => p.Value.Attempts)
                .Select(p => string.Create(Inv, $"{p.Key} {(float)p.Value.Attempts / s.Bouts:0.0}回 {p.Value.SuccessRate:P0}"))));
            sb.AppendLine(string.Create(Inv,
                $"カウンター {(float)st.CountersThrown / s.Bouts:0.0}発 命中 {(st.CountersThrown > 0 ? (float)st.CountersLanded / st.CountersThrown : 0f):P0} ／ " +
                $"無防備に受けた打撃 {(float)st.HitsTakenOpen / s.Bouts:0.0} ／ クリンチ {(float)st.Clinches / s.Bouts:0.0} ／ 投げ {(float)st.ThrowAttempts / s.Bouts:0.0}（1 試合あたり）"));
            if (st.Predictions > 0)
            {
                sb.AppendLine(string.Create(Inv,
                    $"予測の守り {(float)st.Predictions / s.Bouts:0.0}回: 打たれず {1f - ((float)st.PredictionsHit / st.Predictions):P0} ／ 拳が来なかった（空振り） {(float)st.PredictionsIdle / st.Predictions:P0}"));
            }

            sb.AppendLine(string.Create(Inv, $"しゃがんでいた時間 {st.CrouchShare:P0}（守りの最中 {(st.Time > 0f ? st.CrouchDefending / st.Time : 0f):P0}・打つ最中 {(st.Time > 0f ? st.CrouchPunching / st.Time : 0f):P0}、続けて最長 {st.LongestCrouch:0.0} 秒）"));

            sb.AppendLine();
        }

        return sb.ToString();
    }

    /// <summary>1 対 1 の対戦成績（行が列に対して）。</summary>
    public static string Matrix(IReadOnlyList<BoutResult> results)
    {
        var names = results.SelectMany(r => new[] { r.Spec.Red.Name, r.Spec.Blue.Name }).Distinct().ToList();
        var sb = new StringBuilder();
        sb.AppendLine("| 行 vs 列 | " + string.Join(" | ", names) + " |");
        sb.AppendLine("|---|" + string.Concat(names.Select(_ => "---|")));
        foreach (var a in names)
        {
            sb.Append(Inv, $"| {a} |");
            foreach (var b in names)
            {
                if (a == b)
                {
                    sb.Append(" — |");
                    continue;
                }

                var bouts = results.Where(r => (r.Spec.Red.Name == a && r.Spec.Blue.Name == b) || (r.Spec.Red.Name == b && r.Spec.Blue.Name == a)).ToList();
                var wins = bouts.Count(r => (r.Spec.Red.Name == a ? r.Winner : -r.Winner) > 0);
                var draws = bouts.Count(r => r.Winner == 0);
                sb.Append(Inv, $" {wins}-{draws}-{bouts.Count - wins - draws} |");
            }

            sb.AppendLine();
        }

        return sb.ToString();
    }
}
