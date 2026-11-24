using System.Globalization;
using System.Text;
using MiniRing.Characters;

namespace MiniRing.Arena;

/// <summary>
/// 団体のランキングを決める階級ごとの総当たり（2026-11-19）: 同じ階級の全組を、両方のコーナー × 種の数だけ、団体の決まり（<see cref="TvShowRules.Rules(int, float, Organization)"/>・
/// 場・はめる物）で最後まで回す（<see cref="TvScout.Run"/>。中継の下見と同じ試合）。点は団体の <see cref="OrgRanking"/>（勝ち・引き分け・倒して勝った分）。
/// 同点は 直接対決の点 → 倒して勝った数 → 与えたダメージ − 受けたダメージ → id の順。
/// </summary>
internal static class OrgLeague
{
    /// <summary>1 人の成績。</summary>
    internal sealed record Row(string Id, int Wins, int Draws, int Losses, int Finishes, float Dealt, float Taken, float Points);

    /// <summary>1 試合の結果（勝者の id、引き分けなら null）。</summary>
    internal sealed record Bout(string Red, string Blue, int Seed, string? Winner, bool Finish, float RedDealt, float BlueDealt, string Ending);

    /// <summary>階級の全組を回す（種の順・組の順に 1 本ずつ。同じプロセスで並列に回すと動画と違う試合になる: <see cref="TvScout"/>）。</summary>
    public static IReadOnlyList<Bout> Play(Organization org, IReadOnlyList<FighterCharacter> fighters, IReadOnlyList<int> seeds, int rounds, float breakSeconds, TextWriter progress)
    {
        ArgumentNullException.ThrowIfNull(org);
        ArgumentNullException.ThrowIfNull(fighters);
        ArgumentNullException.ThrowIfNull(seeds);
        ArgumentNullException.ThrowIfNull(progress);
        var bouts = new List<Bout>();
        for (var i = 0; i < fighters.Count; i++)
        {
            for (var j = i + 1; j < fighters.Count; j++)
            {
                foreach (var (a, b) in new[] { (fighters[i], fighters[j]), (fighters[j], fighters[i]) })
                {
                    var (red, blue, _) = OrgBouts.Enter(org, a, b);
                    var results = TvScout.Run(RosterLeague.ContenderOf(red, blue.Id, null), RosterLeague.ContenderOf(blue, red.Id, null), red.Wear, blue.Wear, red.Weapon, blue.Weapon,
                        rounds, seeds, GameSpeed.Standard, 30, red, blue, referee: true, breakSeconds, org);
                    foreach (var r in results)
                    {
                        var winner = r.Winner > 0 ? red.Id : r.Winner < 0 ? blue.Id : null;
                        bouts.Add(new Bout(red.Id, blue.Id, r.Seed, winner, r.Ko && winner is not null, r.RedDealt, r.BlueDealt, r.Finish));
                        progress.WriteLine(string.Create(CultureInfo.InvariantCulture, $"  {red.Id} vs {blue.Id} seed {r.Seed}: {winner ?? "draw"} ({r.Finish}, R{r.Round})"));
                    }
                }
            }
        }

        return bouts;
    }

    /// <summary>成績の表（順位の順）。</summary>
    public static IReadOnlyList<Row> Table(OrgRanking ranking, IReadOnlyList<FighterCharacter> fighters, IReadOnlyList<Bout> bouts)
    {
        ArgumentNullException.ThrowIfNull(ranking);
        ArgumentNullException.ThrowIfNull(fighters);
        ArgumentNullException.ThrowIfNull(bouts);
        float PointsOf(string id, IEnumerable<Bout> among) => among.Where(b => b.Red == id || b.Blue == id).Sum(b =>
            b.Winner is null ? ranking.Draw : b.Winner == id ? ranking.Win + (b.Finish ? ranking.FinishBonus : 0f) : 0f);

        var rows = fighters.Select(f =>
        {
            var mine = bouts.Where(b => b.Red == f.Id || b.Blue == f.Id).ToList();
            return new Row(f.Id, mine.Count(b => b.Winner == f.Id), mine.Count(b => b.Winner is null), mine.Count(b => b.Winner is not null && b.Winner != f.Id),
                mine.Count(b => b.Winner == f.Id && b.Finish), mine.Sum(b => b.Red == f.Id ? b.RedDealt : b.BlueDealt), mine.Sum(b => b.Red == f.Id ? b.BlueDealt : b.RedDealt),
                PointsOf(f.Id, mine));
        }).ToList();

        // 同点の組の中だけの直接対決の点。
        float HeadToHead(Row row) => PointsOf(row.Id, bouts.Where(b =>
            rows.Any(o => o.Id != row.Id && o.Points == row.Points && (b.Red == o.Id || b.Blue == o.Id))));

        return [.. rows.OrderByDescending(r => r.Points).ThenByDescending(HeadToHead).ThenByDescending(r => r.Finishes).ThenByDescending(r => r.Dealt - r.Taken)
            .ThenBy(r => r.Id, StringComparer.Ordinal)];
    }

    /// <summary>
    /// 表から新しい王座とランキングを作る: 王者が今も階級にいれば王者のまま（王座は試合でしか動かない）、空位なら <paramref name="crownVacant"/> のとき表の 1 位が王者になる。
    /// ランキングは王者を除いた表の順。
    /// </summary>
    public static DivisionStanding Seat(DivisionStanding before, IReadOnlyList<Row> table, bool crownVacant)
    {
        ArgumentNullException.ThrowIfNull(before);
        ArgumentNullException.ThrowIfNull(table);
        var champion = before.Champion is { } c && table.Any(r => r.Id == c) ? c : crownVacant && table.Count > 0 ? table[0].Id : null;
        return new DivisionStanding(before.Division, champion, [.. table.Select(r => r.Id).Where(id => id != champion)]);
    }

    /// <summary>階級ごとの表を Markdown で。</summary>
    public static string Report(Organization org, OrgDivision division, IReadOnlyList<Row> table, DivisionStanding seat, IReadOnlyList<Bout> bouts)
    {
        ArgumentNullException.ThrowIfNull(org);
        ArgumentNullException.ThrowIfNull(division);
        ArgumentNullException.ThrowIfNull(table);
        ArgumentNullException.ThrowIfNull(seat);
        ArgumentNullException.ThrowIfNull(bouts);
        var sb = new StringBuilder();
        sb.AppendLine(CultureInfo.InvariantCulture, $"## {division.Name}（{org.BeltName(division)}）");
        sb.AppendLine();
        sb.AppendLine(CultureInfo.InvariantCulture, $"王者: {seat.Champion ?? "空位"}");
        sb.AppendLine();
        sb.AppendLine("| 順 | 選手 | 点 | 勝 | 分 | 負 | 倒した勝ち | 与えた | 受けた |");
        sb.AppendLine("|---:|---|---:|---:|---:|---:|---:|---:|---:|");
        for (var i = 0; i < table.Count; i++)
        {
            var r = table[i];
            sb.AppendLine(CultureInfo.InvariantCulture, $"| {i + 1} | {r.Id} | {r.Points:0.#} | {r.Wins} | {r.Draws} | {r.Losses} | {r.Finishes} | {r.Dealt:0} | {r.Taken:0} |");
        }

        if (bouts.Count > 0)
        {
            sb.AppendLine();
            sb.AppendLine("| 赤 | 青 | 種 | 勝者 | 決着 |");
            sb.AppendLine("|---|---|---:|---|---|");
            foreach (var b in bouts)
            {
                sb.AppendLine(CultureInfo.InvariantCulture, $"| {b.Red} | {b.Blue} | {b.Seed} | {b.Winner ?? "引き分け"} | {b.Ending} |");
            }
        }

        sb.AppendLine();
        return sb.ToString();
    }
}
