using System.Globalization;
using System.Text;
using MiniRing.Characters;

namespace MiniRing.Arena.Circuit;

/// <summary>リーグの報告（<c>docs/arena/&lt;org&gt;_prospects.md</c>）: 母数・引退・階級ごと / 国ごとの上位・決着の内訳・挑戦の結果・ランキング・見ごたえの上位。数字はすべて記録から。</summary>
internal static class CircuitReport
{
    public static string Build(CircuitLeague league, IReadOnlyDictionary<string, string> names)
    {
        var state = league.LoadState() ?? throw new InvalidOperationException("リーグがまだありません。");
        var ledger = league.Ledger();
        var prospectBouts = ledger.Where(b => b.Kind == "prospect").ToList();
        var files = Directory.GetFiles(league.ProspectsDir, "*.json").Select(p => (Path.GetFileName(p), File.ReadAllText(p))).ToList();
        var people = CharacterRoster.FromFiles(files, [.. CharacterRoster.All.Select(c => c.Id)]).ToDictionary(c => c.Id);
        var tallies = state.Fighters.Keys.ToDictionary(id => id, id => FighterTally.Of(id, prospectBouts, league.Org.Ranking));
        string Name(string id) => names.GetValueOrDefault(id, id);
        string Inv(FormattableString f) => f.ToString(CultureInfo.InvariantCulture);

        var sb = new StringBuilder();
        sb.AppendLine(Inv($"# {league.Org.LogoText} 新人リーグ シーズン {state.Season}"));
        sb.AppendLine();
        var commits = ledger.Select(b => b.Commit).Distinct().ToList();
        sb.AppendLine(Inv($"種 {league.Config.Seed}・生成の版 {league.Config.GeneratorVersion}・興行 {state.EventsDone}・試合 {ledger.Count}（新人どうし {prospectBouts.Count}・3R、挑戦 {ledger.Count - prospectBouts.Count}・5R）。"));
        sb.AppendLine(Inv($"physics {string.Join(",", ledger.Select(b => b.Physics).Distinct())}・logic {string.Join(",", ledger.Select(b => b.Logic).Distinct())}・コミット {string.Join(", ", commits.Select(c => c.Length > 12 ? c[..12] : c))}。記録は `{CircuitLeague.ToRecordPath(league.LedgerPath)}`、決まりは `games/MiniRing.Arena/Orgs/circuit/{league.Org.Id}.json`、計画は `docs/plans/IVF_PROSPECTS_PLAN.md`。"));
        sb.AppendLine("どの試合も `miniring-arena circuit verify --bout <id>` で記録から回し直せる（終わりの状態のチェックサムを比べる）。");
        sb.AppendLine();

        sb.AppendLine("## 階級ごとの母数・引退・登録");
        sb.AppendLine();
        sb.AppendLine("| 階級 | 母数 | 現役 | 引退 | 登録 | 試合 | KO | 判定 | 引き分け |");
        sb.AppendLine("|---|---:|---:|---:|---:|---:|---:|---:|---:|");
        foreach (var d in league.Config.Divisions)
        {
            var mine = state.Fighters.Where(f => f.Value.Division == d.Id).ToList();
            var bouts = prospectBouts.Where(b => b.Division == d.Id).ToList();
            sb.AppendLine(Inv($"| {league.Org.Division(d.Id).Name} | {mine.Count} | {mine.Count(f => f.Value.Status == LeagueEntry.Active)} | {mine.Count(f => f.Value.Status == LeagueEntry.Retired)} | {mine.Count(f => f.Value.Status == LeagueEntry.Registered)} | {bouts.Count} | {bouts.Count(b => b.Method == "KO")} | {bouts.Count(b => b.Method == "DEC")} | {bouts.Count(b => b.Method == "DRAW")} |"));
        }

        sb.AppendLine();
        sb.AppendLine("引退の理由（規則は `retire`: 通算の負け越し・連敗・倒されての負けの通算。どれか 1 つで引退）:");
        sb.AppendLine();
        foreach (var g in state.Fighters.Values.Where(f => f.Status == LeagueEntry.Retired).GroupBy(f => f.Reason?.Split(' ')[0] ?? "").OrderByDescending(g => g.Count()))
        {
            sb.AppendLine(Inv($"- {g.Key}: {g.Count()} 人（興行 {string.Join("・", g.GroupBy(f => f.Since).OrderBy(x => x.Key).Select(x => $"{x.Key}: {x.Count()}"))}）"));
        }

        sb.AppendLine();
        foreach (var d in league.Config.Divisions)
        {
            var ordered = CircuitRules.Order(state.Fighters.Where(f => f.Value.Division == d.Id).Select(f => tallies[f.Key])).Take(10).ToList();
            sb.AppendLine(Inv($"## {league.Org.Division(d.Id).Name}の上位 10"));
            sb.AppendLine();
            sb.AppendLine("| 順 | 選手 | 国 | 戦績 | 点 | 体 | 状態 |");
            sb.AppendLine("|---:|---|---|---|---:|---|---|");
            for (var i = 0; i < ordered.Count; i++)
            {
                var t = ordered[i];
                var e = state.Fighters[t.Id];
                sb.AppendLine(Inv($"| {i + 1} | {Name(t.Id)}（`{t.Id}`） | {people[t.Id].Country.Name} | {t.Text} | {t.Points:0} | {ProspectFactory.Describe(people[t.Id])} | {Status(e)} |"));
            }

            sb.AppendLine();
        }

        sb.AppendLine("## 国ごと");
        sb.AppendLine();
        sb.AppendLine("母数は国の割合（`countries` の重み、BoxRec の現役プロの国別の数を手がかりにした推定）を最大剰余法で割り振った数。最上位は階級を問わず、点 → 勝ち → KO → 与えた−受けた の順。");
        sb.AppendLine();
        sb.AppendLine("| 国 | 母数 | 引退 | 勝率 | 最上位 | 階級 | 戦績 |");
        sb.AppendLine("|---|---:|---:|---:|---|---|---|");
        foreach (var g in state.Fighters.GroupBy(f => f.Value.Country).OrderByDescending(g => g.Count()).ThenBy(g => g.Key, StringComparer.Ordinal))
        {
            var ts = g.Select(f => tallies[f.Key]).ToList();
            var best = CircuitRules.Order(ts)[0];
            var wins = ts.Sum(t => t.Wins);
            var all = ts.Sum(t => t.Bouts);
            sb.AppendLine(Inv($"| {CharacterRoster.Countries[g.Key].Name} | {g.Count()} | {g.Count(f => f.Value.Status == LeagueEntry.Retired)} | {(all > 0 ? 100.0 * wins / all : 0):0}% | {Name(best.Id)} | {league.Org.Division(state.Fighters[best.Id].Division).Name} | {best.Text} |"));
        }

        sb.AppendLine();
        sb.AppendLine("## 教科書ごと");
        sb.AppendLine();
        sb.AppendLine("| 教科書 | 人数 | 勝率 | KO 勝ちの率 | 引退 |");
        sb.AppendLine("|---|---:|---:|---:|---:|");
        foreach (var g in state.Fighters.GroupBy(f => people[f.Key].StyleName).OrderBy(g => g.Key, StringComparer.Ordinal))
        {
            var ts = g.Select(f => tallies[f.Key]).ToList();
            var all = Math.Max(1, ts.Sum(t => t.Bouts));
            var wins = Math.Max(1, ts.Sum(t => t.Wins));
            sb.AppendLine(Inv($"| {g.Key} | {g.Count()} | {100.0 * ts.Sum(t => t.Wins) / all:0}% | {100.0 * ts.Sum(t => t.Kos) / wins:0}% | {g.Count(f => f.Value.Status == LeagueEntry.Retired)} |"));
        }

        var challenges = ledger.Where(b => b.Kind != "prospect").ToList();
        if (challenges.Count > 0)
        {
            sb.AppendLine();
            sb.AppendLine("## 挑戦試合（5R）");
            sb.AppendLine();
            sb.AppendLine("| 試合 | 種別 | 階級 | 赤（席） | 青（挑戦者） | 勝者 | 決着 | 見ごたえ |");
            sb.AppendLine("|---|---|---|---|---|---|---|---:|");
            foreach (var b in challenges)
            {
                sb.AppendLine(Inv($"| `{b.Id}` | {(b.Kind == "title" ? "王座" : "挑戦")} | {league.Org.Division(b.Division).Name} | {Name(b.Red)} | {Name(b.Blue)} | {(b.WinnerId is { } w ? Name(w) : "引き分け")} | {b.Method} R{b.Round} | {b.Drama:0.0} |"));
            }
        }

        sb.AppendLine();
        sb.AppendLine("## 今のランキング");
        sb.AppendLine();
        var standings = OrgStandings.Of(league.Org);
        foreach (var s in standings.Divisions)
        {
            sb.AppendLine(Inv($"- {league.Org.Division(s.Division).Name}: 王者 {(s.Champion is { } c ? Name(c) : "空位")}・{string.Join(" > ", s.Ranking.Select((id, i) => $"{i + 1}位 {Name(id)}"))}"));
        }

        sb.AppendLine();
        sb.AppendLine("## 見ごたえの上位 10（`TvShowRules.Drama`、docs/EXCITEMENT.md の下見の点）");
        sb.AppendLine();
        sb.AppendLine("| 試合 | 赤 | 青 | 勝者 | 決着 | ダウン 赤/青 | 点 |");
        sb.AppendLine("|---|---|---|---|---|---|---:|");
        foreach (var b in ledger.OrderByDescending(b => b.Drama).ThenBy(b => b.Id, StringComparer.Ordinal).Take(10))
        {
            sb.AppendLine(Inv($"| `{b.Id}` | {Name(b.Red)} | {Name(b.Blue)} | {(b.WinnerId is { } w ? Name(w) : "引き分け")} | {b.Method} R{b.Round} {b.Time:0}s | {b.RedDowns}/{b.BlueDowns} | {b.Drama:0.0} |"));
        }

        return sb.ToString();
    }

    private static string Status(LeagueEntry e) => e.Status switch
    {
        LeagueEntry.Retired => $"引退（興行 {e.Since}・{e.Reason}）",
        LeagueEntry.Registered => "**登録**",
        _ => "現役",
    };
}
