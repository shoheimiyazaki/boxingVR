using System.Globalization;
using System.Text.Json;
using System.Text.Json.Serialization;
using MiniRing.Characters;

namespace MiniRing.Arena.Campaign;

/// <summary>大会の格: 相手の選び方と、勝ったときに席がどう動くか（<see cref="CampaignLadder"/>）。</summary>
internal enum CampaignTier { Rookie, Ranking, Title }

/// <summary>
/// キャンペーンの大会 1 つ（<c>games/MiniRing.Arena/Campaign/events.json</c>）。出られるかは体（計量の体重・身長）・団体のファイル（出場の条件・階級）・
/// この値・ボクサーの戦績だけで決まる（大会名で分けない）。
/// </summary>
internal sealed record CampaignEvent(string Id, string Name, string Org, CampaignTier Tier, IReadOnlyList<string>? Divisions,
    int? MinKg, int? MaxKg, int? MinHeightCm, int? MaxHeightCm, int? MaxPriorBouts, int MinWins, int? MaxSeat, string Note)
{
    /// <summary>この大会の団体。</summary>
    public Organization Organization => Organizations.Get(Org);
}

/// <summary>大会の表（組み込みの <c>Campaign/events.json</c>）の読み込みと検査。</summary>
internal static class CampaignEvents
{
    private const string Resource = "MiniRing.Arena.Campaign.events.json";

    private static readonly Lazy<IReadOnlyList<CampaignEvent>> BuiltIn = new(() =>
    {
        using var stream = typeof(CampaignEvents).Assembly.GetManifestResourceStream(Resource)
            ?? throw new InvalidOperationException($"組み込みの大会の表 {Resource} がありません（MiniRing.Arena.csproj の EmbeddedResource）。");
        using var reader = new StreamReader(stream);
        return FromJson(reader.ReadToEnd());
    });

    /// <summary>全大会（表の並び）。</summary>
    public static IReadOnlyList<CampaignEvent> All => BuiltIn.Value;

    /// <summary>id から探す。無ければ、ある id を書いた例外。</summary>
    public static CampaignEvent Get(string id) => All.FirstOrDefault(e => e.Id == id)
        ?? throw new ArgumentException($"大会 '{id}' はありません（{string.Join(", ", All.Select(e => e.Id))}）。", nameof(id));

    private static readonly JsonSerializerOptions Options = new()
    {
        PropertyNameCaseInsensitive = true,
        UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow,
        ReadCommentHandling = JsonCommentHandling.Skip,
        AllowTrailingCommas = true,
        Converters = { new JsonStringEnumConverter(JsonNamingPolicy.CamelCase, allowIntegerValues: false) },
    };

    /// <summary>JSON から読んで確かめる（知らない団体・階級・重なる id・逆さの範囲は、どの大会のどの欄かを書いた例外）。</summary>
    public static IReadOnlyList<CampaignEvent> FromJson(string json)
    {
        FileDto file;
        try
        {
            file = JsonSerializer.Deserialize<FileDto>(json, Options) ?? throw new InvalidDataException("大会の表が空です。");
        }
        catch (JsonException ex)
        {
            throw new InvalidDataException($"大会の表の JSON が読めません: {ex.Message}", ex);
        }

        var events = new List<CampaignEvent>();
        foreach (var dto in file.Events)
        {
            var e = dto.ToEvent();
            if (events.Any(x => x.Id == e.Id)) { throw new InvalidDataException($"大会の id '{e.Id}' が重なっています。"); }
            if (!Organizations.All.TryGetValue(e.Org, out var org)) { throw new InvalidDataException($"大会 {e.Id}: 団体 '{e.Org}' はありません。"); }
            foreach (var d in e.Divisions ?? [])
            {
                if (org.Divisions.All(x => x.Id != d)) { throw new InvalidDataException($"大会 {e.Id}: 階級 '{d}' は {org.LogoText} にありません。"); }
            }

            if (e.MinKg > e.MaxKg || e.MinHeightCm > e.MaxHeightCm || e.MinWins < 0 || e.MaxPriorBouts < 0 || e.MaxSeat < 1)
            {
                throw new InvalidDataException($"大会 {e.Id}: 範囲の値が逆さか負です。");
            }

            events.Add(e);
        }

        return events;
    }

    private sealed class FileDto
    {
        public required EventDto[] Events { get; init; }
    }

    private sealed class EventDto
    {
        public required string Id { get; init; }
        public required string Name { get; init; }
        public required string Org { get; init; }
        public required CampaignTier Tier { get; init; }
        public string[]? Divisions { get; init; }
        public int? MinKg { get; init; }
        public int? MaxKg { get; init; }
        public int? MinHeightCm { get; init; }
        public int? MaxHeightCm { get; init; }
        public int? MaxPriorBouts { get; init; }
        public int MinWins { get; init; }
        public int? MaxSeat { get; init; }
        public string Note { get; init; } = "";

        public CampaignEvent ToEvent() => new(Id, Name, Org, Tier, Divisions, MinKg, MaxKg, MinHeightCm, MaxHeightCm, MaxPriorBouts, MinWins, MaxSeat, Note);
    }
}

/// <summary>大会に出るときの組み合わせ（出られるときだけ作る）。</summary>
/// <param name="Event">大会。</param>
/// <param name="Division">計量で決まった階級。</param>
/// <param name="Opponent">相手（名簿のキャラ）。</param>
/// <param name="Weapons">この大会で選べる武器（先頭は「素手」の null。武器を持てない団体なら null だけ）。</param>
internal sealed record CampaignEntry(CampaignEvent Event, OrgDivision Division, FighterCharacter Opponent, IReadOnlyList<MiniRing.Weapons.WeaponKind?> Weapons);

/// <summary>
/// 大会の並びの判定（純粋: ファイルを持たない）。出られるか・相手・勝ったときの席の動きを、体・団体のファイル・王座とランキングの記録・戦績から出す。
/// <para>
/// 席（<see cref="CampaignSeat"/>）: 0 = 王者、1.. = ランキングの何位か。団体の記録（<see cref="OrgStandings"/>）の順位の間に差し込んで数える
/// （席 k なら、記録の k 位の人の前）。記録そのもの（名簿のキャラの席）はキャンペーンでは書き換えない（ボクサーごとの物語。docs/plans/CAMPAIGN_PLAN.md）。
/// </para>
/// </summary>
internal static class CampaignLadder
{
    /// <summary>出られない理由（出られるなら null）と、出られるときの組み合わせ。</summary>
    public static (CampaignEntry? Entry, string? Reason) Check(CampaignEvent ev, CampaignBoxer boxer, IReadOnlyList<FighterCharacter> roster, OrgStandings? standings = null)
    {
        ArgumentNullException.ThrowIfNull(ev);
        ArgumentNullException.ThrowIfNull(boxer);
        ArgumentNullException.ThrowIfNull(roster);
        var org = ev.Organization;
        var body = boxer.Character with { Weapon = null }; // 武器は大会ごとに選び直す（出られるかは素手の体で見る）
        if (OrgBouts.Ineligible(org, body) is { } why) { return (null, why); }

        var weight = OrgBouts.WeightOf(body);
        var height = CharacterProfile.HeightOf(body.Build);
        string Kg(int v) => v.ToString(CultureInfo.InvariantCulture) + " kg";
        string Cm(int v) => v.ToString(CultureInfo.InvariantCulture) + " cm";
        if (ev.MinKg is { } minKg && weight < minKg) { return (null, $"体重 {Kg(weight)} が軽すぎます（{ev.Name} は {Kg(minKg)} 以上）。"); }
        if (ev.MaxKg is { } maxKg && weight > maxKg) { return (null, $"体重 {Kg(weight)} が重すぎます（{ev.Name} は {Kg(maxKg)} まで）。"); }
        if (ev.MinHeightCm is { } minCm && height < minCm) { return (null, $"身長 {Cm(height)} が足りません（{ev.Name} は {Cm(minCm)} 以上）。"); }
        if (ev.MaxHeightCm is { } maxCm && height > maxCm) { return (null, $"身長 {Cm(height)} が高すぎます（{ev.Name} は {Cm(maxCm)} まで）。"); }

        var division = org.DivisionOf(weight);
        if (ev.Divisions is { } allowed && !allowed.Contains(division.Id))
        {
            var names = string.Join("・", allowed.Select(id => org.Division(id).Name));
            var limit = division.LimitKg is { } l ? $"{Kg(l)}まで" : "上限なし";
            return (null, $"体重 {Kg(weight)} は{division.Name}（{limit}）。{ev.Name} は{names}だけです。");
        }

        var career = boxer.Career;
        var bouts = career.Bouts.Count(b => b.Org == org.Id);
        var wins = career.Bouts.Count(b => b.Org == org.Id && b.Result == BoutResult.Win);
        if (ev.MaxPriorBouts is { } maxBouts && bouts > maxBouts) { return (null, $"{org.LogoText} で {bouts} 戦しています（{ev.Name} は {maxBouts} 戦までの新人だけ）。"); }
        if (wins < ev.MinWins) { return (null, $"{org.LogoText} での勝ちが {wins} です（{ev.Name} は {ev.MinWins} 勝から）。"); }

        var seat = career.SeatIn(org.Id, division.Id);
        if (ev.MaxSeat is { } maxSeat && (seat is not { } s || s > maxSeat))
        {
            return (null, $"{division.Name}の席が{SeatName(seat)}です（{ev.Name} はランキング {maxSeat} 位から）。");
        }

        standings ??= OrgStandings.Of(org);
        var opponent = Opponent(ev.Tier, org, division, seat, standings, roster, bouts);
        if (opponent is null) { return (null, $"{division.Name}に相手がいません。"); }

        IReadOnlyList<MiniRing.Weapons.WeaponKind?> weapons = org.Entry.Weapons ? [null, .. CharacterFile.Weapons.Cast<MiniRing.Weapons.WeaponKind?>()] : [null];
        return (new CampaignEntry(ev, division, opponent, weapons), null);
    }

    /// <summary>席の言い方（「王者」「3 位」「ランキング外」）。</summary>
    public static string SeatName(int? seat) => seat switch
    {
        null => "ランキング外",
        0 => "王者",
        { } s => s.ToString(CultureInfo.InvariantCulture) + " 位",
    };

    /// <summary>
    /// 相手: 新人戦はランキング外の人を試合の数で順に、ランキング戦は一つ上の席の人（ランキング外ならランキングの最下位）、
    /// タイトルは王者（自分が王者なら 1 位の挑戦者）。名簿に無い・出られない人は飛ばさず、いなければ null。
    /// </summary>
    private static FighterCharacter? Opponent(CampaignTier tier, Organization org, OrgDivision division, int? seat, OrgStandings standings,
        IReadOnlyList<FighterCharacter> roster, int boutsInOrg)
    {
        var byId = roster.ToDictionary(c => c.Id, StringComparer.Ordinal);
        var stand = standings.For(division);
        // 記録の席の人でも、出られない・計量で別の階級になった人とは組まない（OrgBouts.Enter が止める組み合わせを出さない）。
        FighterCharacter? Find(string? id) => id is not null && byId.TryGetValue(id, out var c) && OrgBouts.Ineligible(org, c) is null
            && org.DivisionOf(OrgBouts.WeightOf(c)) == division ? c : null;
        switch (tier)
        {
            case CampaignTier.Rookie:
                var seated = new HashSet<string>(stand.Ranking, StringComparer.Ordinal);
                if (stand.Champion is { } champ) { seated.Add(champ); }
                var pool = OrgBouts.Pool(org, roster)[division.Id];
                var unranked = pool.Where(c => !seated.Contains(c.Id)).ToList();
                var candidates = unranked.Count > 0 ? unranked : [.. pool];
                return candidates.Count == 0 ? null : candidates[boutsInOrg % candidates.Count];
            case CampaignTier.Ranking:
                // 席 k（記録の k 位の前）の一つ上 = 記録の k−1 位。ランキング外なら記録の最下位。
                var index = seat is { } k ? k - 2 : stand.Ranking.Count - 1;
                return index >= 0 && index < stand.Ranking.Count ? Find(stand.Ranking[index]) : null;
            default:
                return seat == 0 ? Find(stand.Ranking.Count > 0 ? stand.Ranking[0] : null) : Find(stand.Champion);
        }
    }

    /// <summary>
    /// 試合の後の席: ランキング戦に勝てば相手の席（ランキング外なら記録の最下位の次から最下位の席へ）、タイトルに勝てば王者、王者が負ければ 1 位、ほかは動かない。
    /// </summary>
    public static int? SeatAfter(CampaignTier tier, int? seat, BoutResult result, int rankedCount)
    {
        return (tier, result) switch
        {
            (CampaignTier.Ranking, BoutResult.Win) => seat is { } k ? Math.Max(1, k - 1) : Math.Max(1, rankedCount),
            (CampaignTier.Title, BoutResult.Win) => 0,
            (CampaignTier.Title, BoutResult.Loss) when seat == 0 => 1,
            _ => seat,
        };
    }
}
