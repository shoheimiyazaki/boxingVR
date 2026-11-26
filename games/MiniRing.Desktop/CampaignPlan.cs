using System.Globalization;
using MiniRing.Arena;
using MiniRing.Arena.Campaign;
using MiniRing.Characters;

namespace MiniRing.Desktop;

/// <summary>大会 1 つの見え方: 大会・出られるときの組み合わせ・開き具合（<see cref="CampaignLadder.Check"/> と <see cref="CampaignLadder.Gate"/> から）。</summary>
/// <param name="Event">大会。</param>
/// <param name="Entry">出られるときの組み合わせ（閉じていれば null）。</param>
/// <param name="Gate">開き具合。</param>
/// <param name="Seat">この大会の団体・階級での自分の席（null = ランキング外、0 = 王者）。</param>
internal sealed record CampaignStop(CampaignEvent Event, CampaignEntry? Entry, CampaignGate Gate, int? Seat)
{
    public bool Open => Entry is not null;
}

/// <summary>
/// キャンペーンの画面の道しるべ（純粋、2026-11-26 UI の作り直し）: 団体の道（自分の階級の 新人戦 → ランキング戦 → タイトル）、
/// 本拠地の「次の試合」の選び方、閉じた大会の「あと何で開くか」の言葉。判定はしない（<see cref="CampaignLadder"/> の結果を並べて言葉にするだけ）。
/// </summary>
internal static class CampaignPlan
{
    /// <summary>キャンペーンに大会のある団体（表に出てくる順: IVF・WVC・VFC）。</summary>
    public static IReadOnlyList<Organization> Orgs { get; } =
        [.. CampaignEvents.All.Select(e => e.Org).Distinct(StringComparer.Ordinal).Select(Organizations.Get)];

    /// <summary>全大会の見え方（表の順）。</summary>
    public static IReadOnlyList<CampaignStop> Stops(CampaignBoxer boxer, IReadOnlyList<FighterCharacter> roster) =>
        [.. CampaignEvents.All.Select(e => new CampaignStop(e, CampaignLadder.Check(e, boxer, roster).Entry, CampaignLadder.Gate(e, boxer, roster), boxer.Career.SeatIn(e.Org, e.Division.Id)))];

    /// <summary>この団体で自分が入る階級（計量の体重から。無差別の団体は 1 つだけ）。</summary>
    public static OrgDivision DivisionFor(Organization org, CampaignBoxer boxer) => org.DivisionOf(boxer.WeightKg);

    /// <summary>団体の道: 自分の階級の大会（新人戦 → ランキング戦 → タイトル の順）。</summary>
    public static IReadOnlyList<CampaignStop> Path(IReadOnlyList<CampaignStop> stops, Organization org, CampaignBoxer boxer)
    {
        var division = DivisionFor(org, boxer);
        return [.. stops.Where(s => s.Event.Org == org.Id && s.Event.Division == division).OrderBy(s => s.Event.Tier)];
    }

    /// <summary>
    /// 自動で推す「次の試合」: 出られる大会のうち、一番先の段（タイトル → ランキング戦 → 新人戦）、同じ段なら表の順（IVF の自分の階級が先）。
    /// 新しく大会が開けば、そちらへ移る。
    /// </summary>
    public static CampaignStop? AutoNext(IReadOnlyList<CampaignStop> stops) =>
        stops.Select((s, i) => (s, i)).Where(p => p.s.Open).OrderByDescending(p => p.s.Event.Tier).ThenBy(p => p.i).Select(p => p.s).FirstOrDefault();

    /// <summary>段の短い名前（道の札）。</summary>
    public static string TierName(CampaignTier tier) => tier switch
    {
        CampaignTier.Rookie => "新人戦",
        CampaignTier.Ranking => "ランキング戦",
        _ => "タイトル",
    };

    /// <summary>開き具合の短い言葉（「出られる」「あと 2 勝で開く」「1 位になれば挑戦」）。数は <see cref="CampaignLadder.Gate"/> の値そのもの。</summary>
    public static string GateText(CampaignStop stop)
    {
        var g = stop.Gate;
        return g.Kind switch
        {
            CampaignGateKind.Open => stop.Event.Tier == CampaignTier.Title && stop.Seat == 0 ? "防衛戦に出られる" : "出られる！",
            CampaignGateKind.Wins => string.Create(CultureInfo.InvariantCulture, $"あと {g.Need - g.Have} 勝で開く"),
            CampaignGateKind.Seat => g.Have < 0
                ? string.Create(CultureInfo.InvariantCulture, $"ランキング {g.Need} 位で挑戦")
                : string.Create(CultureInfo.InvariantCulture, $"{g.Have} 位 → {g.Need} 位で挑戦"),
            CampaignGateKind.Graduated => "新人の枠は卒業",
            _ => "今は出られない",
        };
    }

    /// <summary>勝ちの数の進み具合（0..1。勝ちの条件の無い大会は null）。</summary>
    public static float? WinProgress(CampaignStop stop) => stop.Gate.Kind is CampaignGateKind.Wins or CampaignGateKind.Open && stop.Event.Record.MinWins > 0
        ? Math.Clamp(stop.Gate.Have / (float)stop.Event.Record.MinWins, 0f, 1f)
        : null;

    /// <summary>
    /// 次の目標の 1 行: 推す試合の団体の道で、まだ閉じていて数で近づける大会（勝ち・席）。無ければ推す試合そのもの。
    /// </summary>
    public static string NextGoal(IReadOnlyList<CampaignStop> stops, CampaignBoxer boxer)
    {
        if (AutoNext(stops) is not { } next) { return "出られる大会がありません（体重を変えてみましょう）"; }
        var org = next.Event.Organization;
        var closed = Path(stops, org, boxer).FirstOrDefault(s => s.Event.Tier > next.Event.Tier && s.Gate.Kind is CampaignGateKind.Wins or CampaignGateKind.Seat);
        return closed is null
            ? $"次は {next.Event.Name}"
            : $"{GateText(closed)}: {closed.Event.Name}";
    }
}

/// <summary>試合の後に 1 回だけ見せる札（Enter で次へ。前後の戦績・席・開いた大会の差から作る）。</summary>
internal abstract record CampaignCard
{
    /// <summary>勝敗と戦績の更新。負け・引き分けは落ち着いた札で、次の目標を添える。</summary>
    public sealed record Result(string EventName, BoutResult Outcome, bool Finish, string Opponent,
        (int W, int L, int D) Before, (int W, int L, int D) After, string NextGoal) : CampaignCard;

    /// <summary>ランキングが上がった（<paramref name="From"/> null = ランキング外から）。</summary>
    public sealed record RankUp(string Where, int? From, int To, string Passed) : CampaignCard;

    /// <summary>王座を取った・守った。</summary>
    public sealed record Title(string Where, string Defeated, bool Defense) : CampaignCard;

    /// <summary>新しく出られるようになった大会。</summary>
    public sealed record Unlocked(IReadOnlyList<CampaignEvent> Events) : CampaignCard;

    /// <summary>
    /// 試合の前後のボクサーの差から札を並べる（結果 → 席 → 開いた大会）。途中で抜けた試合は記録しないので、ここへは来ない。
    /// </summary>
    public static IReadOnlyList<CampaignCard> Between(CampaignBoxer before, CampaignBoxer after, CampaignEntry entry, BoutResult result, bool finish,
        IReadOnlyList<FighterCharacter> roster)
    {
        ArgumentNullException.ThrowIfNull(before);
        ArgumentNullException.ThrowIfNull(after);
        ArgumentNullException.ThrowIfNull(entry);
        var afterStops = CampaignPlan.Stops(after, roster);
        var cards = new List<CampaignCard>
        {
            new Result(entry.Event.Name, result, finish, entry.Opponent.HudName,
                (before.Career.Wins, before.Career.Losses, before.Career.Draws), (after.Career.Wins, after.Career.Losses, after.Career.Draws),
                CampaignPlan.NextGoal(afterStops, after)),
        };

        var org = entry.Event.Org;
        var division = entry.Division;
        var where = entry.Event.Organization.HasWeightClasses ? $"{entry.Event.Organization.LogoText} {division.Name}" : entry.Event.Organization.LogoText;
        var (from, to) = (before.Career.SeatIn(org, division.Id), after.Career.SeatIn(org, division.Id));
        if (to == 0 && result == BoutResult.Win)
        {
            cards.Add(new Title(where, entry.Opponent.HudName, from == 0));
        }
        else if (result == BoutResult.Win && to is { } t && (from is null || t < from))
        {
            cards.Add(new RankUp(where, from, t, entry.Opponent.HudName));
        }

        var opened = CampaignPlan.Stops(before, roster).Select(s => s.Open).ToList();
        var fresh = afterStops.Where((s, i) => s.Open && !opened[i]).Select(s => s.Event).ToList();
        if (fresh.Count > 0) { cards.Add(new Unlocked(fresh)); }
        return cards;
    }
}
