using System.Globalization;
using System.Text.Json;
using System.Text.Json.Serialization;
using MiniRing.Characters;

namespace MiniRing.Arena.Campaign;

/// <summary>大会の格: 相手の選び方と、勝ったときに席がどう動くか（<see cref="CampaignLadder"/>）。</summary>
internal enum CampaignTier { Rookie, Ranking, Title }

/// <summary>戦績を数える範囲: プロの通算（全団体）・この団体・この団体のこの階級。</summary>
internal enum RecordScope { Career, Org, Division }

/// <summary>
/// 戦績の条件（events.json の <c>record</c>）。<paramref name="Scope"/> の範囲の試合を数えて、
/// 「この試合を含めた試合の数 ≤ <paramref name="MaxBouts"/>」「勝ちの数 ≥ <paramref name="MinWins"/>」を見る。どの大会も同じ式。
/// </summary>
internal sealed record CareerRequirement(RecordScope Scope, int? MaxBouts, int MinWins)
{
    public static CareerRequirement None { get; } = new(RecordScope.Career, null, 0);
}

/// <summary>
/// キャンペーンの大会 1 つ（<c>games/MiniRing.Arena/Campaign/events.json</c> の型 1 行 × 団体の階級 1 つ）。
/// 出られるかは、体重（計量の階級）・団体のファイル（出場の条件・計量の有無）・この値・ボクサーの戦績だけで決まる（大会名・団体名で分けない）。
/// </summary>
/// <param name="Id">大会の id（階級のある団体では <c>&lt;型の id&gt;-&lt;階級の id&gt;</c>）。</param>
/// <param name="Name">画面の名前（型の名前の {division} を階級の名前にした物）。</param>
/// <param name="Org">団体の id。</param>
/// <param name="Division">この大会の階級（計量の体重でこの階級に入る選手だけが出る）。</param>
/// <param name="Tier">格（相手の選び方と席の動き）。</param>
/// <param name="Record">戦績の条件。</param>
/// <param name="MaxSeat">出るのに要る席（null = 不問）。</param>
/// <param name="Note">画面の説明。</param>
internal sealed record CampaignEvent(string Id, string Name, string Org, OrgDivision Division, CampaignTier Tier, CareerRequirement Record, int? MaxSeat, string Note)
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

    /// <summary>全大会（団体ごとに、階級の軽い順 × 表の並び）。</summary>
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

    /// <summary>
    /// JSON から読んで、団体の階級ごとに広げて確かめる（知らない団体・重なる id・負の値・階級の無い団体に書いた {division} は、どの大会のどの欄かを書いた例外）。
    /// </summary>
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

        var templates = new List<EventDto>();
        foreach (var dto in file.Events)
        {
            if (templates.Any(x => x.Id == dto.Id)) { throw new InvalidDataException($"大会の id '{dto.Id}' が重なっています。"); }
            if (!Organizations.All.ContainsKey(dto.Org)) { throw new InvalidDataException($"大会 {dto.Id}: 団体 '{dto.Org}' はありません。"); }
            var record = dto.Record?.ToRequirement() ?? CareerRequirement.None;
            if (record.MinWins < 0 || record.MaxBouts < 1 || dto.MaxSeat < 1) { throw new InvalidDataException($"大会 {dto.Id}: maxBouts・maxSeat は 1 以上、minWins は 0 以上にしてください。"); }
            templates.Add(dto);
        }

        // 団体ごとに（表に出てくる順）、階級の軽い順 × 表の並び。画面でも同じ階級の新人戦 → ランキング戦 → タイトルが並ぶ。
        var events = new List<CampaignEvent>();
        foreach (var orgId in templates.Select(t => t.Org).Distinct(StringComparer.Ordinal))
        {
            var org = Organizations.Get(orgId);
            foreach (var division in org.Divisions)
            {
                foreach (var t in templates.Where(t => t.Org == orgId))
                {
                    var id = org.HasWeightClasses ? $"{t.Id}-{division.Id}" : t.Id;
                    if (events.Any(e => e.Id == id) || templates.Any(x => x.Id == id && !ReferenceEquals(x, t))) { throw new InvalidDataException($"大会の id '{id}' が重なっています。"); }
                    events.Add(new CampaignEvent(id, t.Name.Replace("{division}", division.Name, StringComparison.Ordinal), orgId, division, t.Tier,
                        t.Record?.ToRequirement() ?? CareerRequirement.None, t.MaxSeat, t.Note));
                }
            }
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
        public RecordDto? Record { get; init; }
        public int? MaxSeat { get; init; }
        public string Note { get; init; } = "";
    }

    private sealed class RecordDto
    {
        public required RecordScope Scope { get; init; }
        public int? MaxBouts { get; init; }
        public int MinWins { get; init; }

        public CareerRequirement ToRequirement() => new(Scope, MaxBouts, MinWins);
    }
}

/// <summary>大会に出るときの組み合わせ（出られるときだけ作る）。</summary>
/// <param name="Event">大会。</param>
/// <param name="Division">計量で決まった階級（= 大会の階級）。</param>
/// <param name="Opponent">相手（名簿のキャラ）。</param>
/// <param name="Weapons">この大会で選べる武器（先頭は「素手」の null。武器を持てない団体なら null だけ）。</param>
internal sealed record CampaignEntry(CampaignEvent Event, OrgDivision Division, FighterCharacter Opponent, IReadOnlyList<MiniRing.Weapons.WeaponKind?> Weapons);

/// <summary>
/// 大会の並びの判定（純粋: ファイルを持たない）。出られるか・相手・勝ったときの席の動きを、体重・団体のファイル・王座とランキングの記録・戦績から出す。
/// <para>
/// 席（<see cref="CampaignSeat"/>）: 0 = 王者、1.. = ランキングの何位か。団体の記録（<see cref="OrgStandings"/>）の順位の間に差し込んで数える
/// （席 k なら、記録の k 位の人の前）。席は団体 × 階級ごとに別（IVF は階級ごとに別の順位・別の王者）。
/// 記録そのもの（名簿のキャラの席）はキャンペーンでは書き換えない（ボクサーごとの物語。docs/plans/CAMPAIGN_PLAN.md）。
/// </para>
/// </summary>
internal static class CampaignLadder
{
    /// <summary>
    /// 出られない理由（出られるなら null）と、出られるときの組み合わせ。どの大会も同じ順・同じ式で見る:
    /// 団体の出場の条件 → 階級（計量）→ 戦績（数える範囲の試合の数の上限・勝ちの数の下限）→ 席。身長・体型では弾かない。
    /// </summary>
    public static (CampaignEntry? Entry, string? Reason) Check(CampaignEvent ev, CampaignBoxer boxer, IReadOnlyList<FighterCharacter> roster, OrgStandings? standings = null)
    {
        ArgumentNullException.ThrowIfNull(ev);
        ArgumentNullException.ThrowIfNull(boxer);
        ArgumentNullException.ThrowIfNull(roster);
        var org = ev.Organization;
        var division = ev.Division;
        var body = boxer.Character with { Weapon = null }; // 武器は大会ごとに選び直す（出られるかは素手の体で見る）
        if ((OrgBouts.Ineligible(org, body) ?? DivisionReason(org, division, OrgBouts.WeightOf(body)) ?? RecordReason(ev, boxer.Career)) is { } why)
        {
            return (null, why);
        }

        var career = boxer.Career;
        var seat = career.SeatIn(org.Id, division.Id);
        if (ev.MaxSeat is { } maxSeat && (seat is not { } s || s > maxSeat))
        {
            return (null, $"{division.Name}の席が{SeatName(seat)}です（{ev.Name} はランキング {maxSeat} 位から）。");
        }

        standings ??= OrgStandings.Of(org);
        var boutsHere = Count(career, RecordScope.Division, org.Id, division.Id).Bouts;
        var opponent = Opponent(ev.Tier, org, division, seat, standings, roster, boutsHere);
        if (opponent is null) { return (null, $"{division.Name}に相手がいません。"); }

        IReadOnlyList<MiniRing.Weapons.WeaponKind?> weapons = org.Entry.Weapons ? [null, .. CharacterFile.Weapons.Cast<MiniRing.Weapons.WeaponKind?>()] : [null];
        return (new CampaignEntry(ev, division, opponent, weapons), null);
    }

    private static string Kg(int v) => v.ToString(CultureInfo.InvariantCulture) + " kg";

    /// <summary>
    /// 階級: 計量の体重で入る階級（<see cref="Organization.DivisionOf"/>）が大会の階級でなければ出られない。計量のある団体で上限を超えていれば前日計量で失格、
    /// 軽すぎれば自分の階級の大会へ。計量のない団体（<see cref="OrgEntry.WeighIn"/> が false）は体重を発表するだけで弾かない。
    /// </summary>
    private static string? DivisionReason(Organization org, OrgDivision division, int weight)
    {
        if (!org.Entry.WeighIn) { return null; }
        var own = org.DivisionOf(weight);
        if (own == division) { return null; }
        return division.LimitKg is { } limit && weight > limit
            ? $"前日計量 {Kg(weight)}。{division.Name}の上限 {Kg(limit)} を超えて失格です（{own.Name}の大会へ）。"
            : $"計量 {Kg(weight)} は{own.Name}です（{division.Name}は{LowerLimit(org, division)}）。";
    }

    private static string LowerLimit(Organization org, OrgDivision division)
    {
        var index = org.Divisions.ToList().IndexOf(division);
        return index > 0 && org.Divisions[index - 1].LimitKg is { } below ? $" {Kg(below)} を超える体重から" : "";
    }

    /// <summary>戦績: 数える範囲の「この試合を含めた試合の数」が上限以下か、勝ちの数が下限以上か。</summary>
    private static string? RecordReason(CampaignEvent ev, CampaignCareer career)
    {
        var need = ev.Record;
        var (bouts, wins) = Count(career, need.Scope, ev.Org, ev.Division.Id);
        var where = ScopeName(need.Scope, ev);
        if (need.MaxBouts is { } maxBouts && bouts + 1 > maxBouts)
        {
            return string.Create(CultureInfo.InvariantCulture, $"{where} {bouts} 戦しています（{ev.Name} は {maxBouts} 戦目までの新人だけ）。");
        }

        return wins < need.MinWins
            ? string.Create(CultureInfo.InvariantCulture, $"{where}の勝ちが {wins} です（{ev.Name} は {need.MinWins} 勝から）。")
            : null;
    }

    private static (int Bouts, int Wins) Count(CampaignCareer career, RecordScope scope, string org, string division)
    {
        var bouts = career.Bouts.Where(b => scope switch
        {
            RecordScope.Org => b.Org == org,
            RecordScope.Division => b.Org == org && b.Division == division,
            _ => true,
        }).ToList();
        return (bouts.Count, bouts.Count(b => b.Result == BoutResult.Win));
    }

    private static string ScopeName(RecordScope scope, CampaignEvent ev) => scope switch
    {
        RecordScope.Org => ev.Organization.LogoText + " で",
        RecordScope.Division => $"{ev.Organization.LogoText} {ev.Division.Name}で",
        _ => "プロ通算で",
    };

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
        IReadOnlyList<FighterCharacter> roster, int boutsInDivision)
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
                return candidates.Count == 0 ? null : candidates[boutsInDivision % candidates.Count];
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
