using System.Globalization;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using MiniRing.Characters;

namespace MiniRing.Arena;

/// <summary>1 つの階級の王座とランキング。</summary>
/// <param name="Division">階級の id（団体のファイルの <c>divisions</c>）。</param>
/// <param name="Champion">王者のキャラの id。null なら空位。</param>
/// <param name="Ranking">挑戦者の順位（王者を除く、強い順のキャラの id）。</param>
internal sealed record DivisionStanding(string Division, string? Champion, IReadOnlyList<string> Ranking);

/// <summary>
/// 団体の王座とランキングの記録（<c>games/MiniRing.Arena/Orgs/standings/&lt;org&gt;.json</c>、2026-11-19）。
/// 試合の中では読むだけで、変わるのは試合と試合の間（<c>rankings</c>・<c>title</c> のコマンド。CLAUDE.md §1.6・§1.8 と同じ）。強さは持たない（順位は試合の結果から）。
/// </summary>
/// <param name="Org">団体の id。</param>
/// <param name="AsOf">いつの記録か（日付）。</param>
/// <param name="Basis">順位の出どころ（どの総当たり・どの版の物理とボットか）。</param>
/// <param name="Divisions">階級ごとの王座とランキング。</param>
internal sealed record OrgStandings(string Org, string AsOf, string Basis, IReadOnlyList<DivisionStanding> Divisions)
{
    private const string Prefix = "MiniRing.Arena.Standings.";

    private static readonly JsonSerializerOptions Options = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = true,
        UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow,
        ReadCommentHandling = JsonCommentHandling.Skip,
        AllowTrailingCommas = true,
        WriteIndented = true,
        Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
    };

    /// <summary>空の記録（全階級が空位・順位なし）。</summary>
    public static OrgStandings Empty(Organization org)
    {
        ArgumentNullException.ThrowIfNull(org);
        return new OrgStandings(org.Id, "", "", [.. org.Divisions.Select(d => new DivisionStanding(d.Id, null, []))]);
    }

    private static readonly System.Collections.Concurrent.ConcurrentDictionary<string, OrgStandings> AsOfOverrides = new(StringComparer.Ordinal);

    /// <summary>
    /// このプロセスの間、団体の席を組み込みの記録でなくこのファイルの記録にする（過去の試合を撮るとき、その試合の時点の王座とランキングで見出し・席を出す:
    /// <c>video --standings</c>）。試合には響かない（席は見出しと台本の資料だけ）。
    /// </summary>
    public static void UseFile(Organization org, string path)
    {
        ArgumentNullException.ThrowIfNull(org);
        AsOfOverrides[org.Id] = FromJson(File.ReadAllText(path), org, CharacterRoster.All.Select(c => c.Id).ToHashSet(StringComparer.Ordinal));
    }

    /// <summary>組み込みの記録（無ければ空。<see cref="UseFile"/> で置き換えていればそれ）。</summary>
    public static OrgStandings Of(Organization org)
    {
        ArgumentNullException.ThrowIfNull(org);
        if (AsOfOverrides.TryGetValue(org.Id, out var asOf))
        {
            return asOf;
        }
        using var stream = typeof(OrgStandings).Assembly.GetManifestResourceStream($"{Prefix}{org.Id}.json");
        if (stream is null)
        {
            return Empty(org);
        }

        using var reader = new StreamReader(stream);
        return FromJson(reader.ReadToEnd(), org, CharacterRoster.All.Select(c => c.Id).ToHashSet(StringComparer.Ordinal));
    }

    /// <summary>JSON から読んで確かめる（団体の違い・知らない階級・名簿に無いキャラ・同じキャラの重複は、どこが悪いかを書いた例外）。</summary>
    public static OrgStandings FromJson(string json, Organization org, IReadOnlySet<string> knownCharacters)
    {
        ArgumentNullException.ThrowIfNull(org);
        ArgumentNullException.ThrowIfNull(knownCharacters);
        OrgStandings standings;
        try
        {
            standings = JsonSerializer.Deserialize<OrgStandings>(json, Options) ?? throw new InvalidDataException("記録が空です。");
        }
        catch (JsonException ex)
        {
            throw new InvalidDataException($"{org.Id} の王座とランキングの記録が読めません: {ex.Message}", ex);
        }

        if (standings.Org != org.Id)
        {
            throw new InvalidDataException($"王座とランキングの記録の org '{standings.Org}' が団体 '{org.Id}' と違います。");
        }

        var seen = new HashSet<string>(StringComparer.Ordinal);
        foreach (var division in standings.Divisions ?? throw new InvalidDataException("divisions がありません。"))
        {
            org.Division(division.Division); // 知らない階級は例外
            foreach (var id in (division.Champion is { } c ? [c] : Array.Empty<string>()).Concat(division.Ranking ?? []))
            {
                if (!knownCharacters.Contains(id))
                {
                    throw new InvalidDataException($"{org.Id} の {division.Division}: キャラ '{id}' は名簿にありません。");
                }

                if (!seen.Add(id))
                {
                    throw new InvalidDataException($"{org.Id}: キャラ '{id}' が 2 か所に入っています（1 人は 1 つの階級の 1 つの席だけ）。");
                }
            }
        }

        // 記録に無い階級は空位で足す（階級を増やしたとき）。
        var missing = org.Divisions.Where(d => standings.Divisions.All(s => s.Division != d.Id)).Select(d => new DivisionStanding(d.Id, null, []));
        return standings with { Divisions = [.. org.Divisions.Select(d => standings.Divisions.Concat(missing).First(s => s.Division == d.Id))] };
    }

    /// <summary>JSON に書く（組み込みの記録の元のファイルの中身）。</summary>
    public string ToJson() => JsonSerializer.Serialize(this, Options) + "\n";

    /// <summary>階級の王座とランキング。</summary>
    public DivisionStanding For(OrgDivision division)
    {
        ArgumentNullException.ThrowIfNull(division);
        return Divisions.First(d => d.Division == division.Id);
    }

    /// <summary>キャラの席（「王者」「1位」）。どこにもいなければ null。</summary>
    public string? SeatOf(string characterId)
    {
        foreach (var d in Divisions)
        {
            if (d.Champion == characterId)
            {
                return "王者";
            }

            var rank = d.Ranking.ToList().IndexOf(characterId);
            if (rank >= 0)
            {
                return string.Create(CultureInfo.InvariantCulture, $"{rank + 1}位");
            }
        }

        return null;
    }

    /// <summary>
    /// 階級の席を置き換え、その席に入った選手をほかの階級から外す（体重が変わって階級を移った選手が 2 か所に入らないように）。
    /// </summary>
    public OrgStandings WithSeat(DivisionStanding seat)
    {
        ArgumentNullException.ThrowIfNull(seat);
        var moved = (seat.Champion is { } c ? [c] : Array.Empty<string>()).Concat(seat.Ranking).ToHashSet(StringComparer.Ordinal);
        return this with
        {
            Divisions = [.. Divisions.Select(s => s.Division == seat.Division ? seat
                : new DivisionStanding(s.Division, s.Champion is { } held && moved.Contains(held) ? null : s.Champion, [.. s.Ranking.Where(id => !moved.Contains(id))]))],
        };
    }

    /// <summary>
    /// 試合の結果で王座を動かす: 負けた側が王者なら、王座は勝った側へ移り、前の王者はランキングの 1 位へ。挑戦者どうしなら、勝った側が負けた側より下にいれば
    /// 負けた側のすぐ上へ上がる。引き分け・王座に関係しない試合は何も変えない。
    /// </summary>
    public OrgStandings AfterBout(OrgDivision division, string winner, string loser)
    {
        ArgumentNullException.ThrowIfNull(division);
        ArgumentException.ThrowIfNullOrWhiteSpace(winner);
        ArgumentException.ThrowIfNullOrWhiteSpace(loser);
        if (winner == loser)
        {
            throw new ArgumentException($"勝者と敗者が同じ選手（{winner}）です。");
        }

        var d = For(division);
        var ranking = d.Ranking.ToList();
        var champion = d.Champion;
        if (champion == loser)
        {
            champion = winner; // 王座が移る
            ranking.Remove(winner);
            ranking.Insert(0, loser);
        }
        else
        {
            if (champion != winner && !ranking.Contains(winner))
            {
                ranking.Add(winner);
            }

            if (!ranking.Contains(loser))
            {
                ranking.Add(loser);
            }

            var w = ranking.IndexOf(winner);
            var l = ranking.IndexOf(loser);
            if (w > l)
            {
                ranking.RemoveAt(w);
                ranking.Insert(l, winner); // 上の相手に勝てば、そのすぐ上へ
            }
        }

        return this with { Divisions = [.. Divisions.Select(s => s.Division == division.Id ? new DivisionStanding(s.Division, champion, ranking) : s)] };
    }
}

/// <summary>計量と出場の判定（団体のファイルの <c>entry</c>・<c>divisions</c>）。</summary>
internal static class OrgBouts
{
    /// <summary>計量した体重 [kg]（見えている体から: <see cref="CharacterProfile.WeightOf"/>）。</summary>
    public static int WeightOf(FighterCharacter character)
    {
        ArgumentNullException.ThrowIfNull(character);
        return CharacterProfile.WeightOf(character.Build);
    }

    /// <summary>出られない理由（出られるなら null）。武器・教科書だけを見る（体重は階級を選ぶだけ）。</summary>
    public static string? Ineligible(Organization org, FighterCharacter character)
    {
        ArgumentNullException.ThrowIfNull(org);
        ArgumentNullException.ThrowIfNull(character);
        if (!org.Entry.Weapons && character.Weapon is { } weapon)
        {
            return $"{org.LogoText} は武器を持てません（{character.HudName} は {FighterCharacter.WeaponLabel(weapon)}）。";
        }

        if (org.Entry.Textbooks is { } books && !books.Contains(character.StyleName))
        {
            return $"{org.LogoText} に出られる流派ではありません（{character.HudName} の教科書は {character.StyleName}）。";
        }

        return null;
    }

    /// <summary>団体が着けさせるはめる物を着けた選手（<see cref="OrgEntry.Gear"/> が無ければそのまま）。</summary>
    public static FighterCharacter Equip(Organization org, FighterCharacter character)
    {
        ArgumentNullException.ThrowIfNull(org);
        ArgumentNullException.ThrowIfNull(character);
        return org.Entry.Gear is { } gear ? character with { Wear = gear } : character;
    }

    /// <summary>
    /// 両者を計量して試合を組む: 出られない選手・階級の違う組み合わせは、体重と階級を書いた例外で止める。通れば、はめる物を着けた両者と階級を返す。
    /// </summary>
    public static (FighterCharacter Red, FighterCharacter Blue, OrgDivision Division) Enter(Organization org, FighterCharacter red, FighterCharacter blue)
    {
        ArgumentNullException.ThrowIfNull(org);
        ArgumentNullException.ThrowIfNull(red);
        ArgumentNullException.ThrowIfNull(blue);
        if ((Ineligible(org, red) ?? Ineligible(org, blue)) is { } why)
        {
            throw new ArgumentException(why);
        }

        var redDivision = org.DivisionOf(WeightOf(red));
        var blueDivision = org.DivisionOf(WeightOf(blue));
        if (redDivision != blueDivision)
        {
            throw new ArgumentException(string.Create(CultureInfo.InvariantCulture,
                $"{org.LogoText} は同じ階級どうししか組めません: {red.HudName} {WeightOf(red)} kg（{redDivision.Name}）・{blue.HudName} {WeightOf(blue)} kg（{blueDivision.Name}）。"));
        }

        return (Equip(org, red), Equip(org, blue), redDivision);
    }

    /// <summary>
    /// 試合の見出しと、台本の資料に足す行: 王者が出るなら「ミドル級タイトルマッチ」、階級制なら階級の名前。無差別の団体で王者が出なければ見出しは空。
    /// </summary>
    public static (string Billing, IReadOnlyList<string> SheetLines) Billing(Organization org, OrgStandings standings, OrgDivision division, FighterCharacter red, FighterCharacter blue)
    {
        ArgumentNullException.ThrowIfNull(org);
        ArgumentNullException.ThrowIfNull(standings);
        ArgumentNullException.ThrowIfNull(red);
        ArgumentNullException.ThrowIfNull(blue);
        var seat = standings.For(division);
        var titleBout = seat.Champion == red.Id || seat.Champion == blue.Id;
        var lines = new List<string>();
        if (org.HasWeightClasses)
        {
            lines.Add(string.Create(CultureInfo.InvariantCulture,
                $"計量: {red.HudName} {WeightOf(red)} kg・{blue.HudName} {WeightOf(blue)} kg。{division.Name}（{(division.LimitKg is { } limit ? $"{limit} kg まで" : "上限なし")}）の試合。"));
        }

        string Seat(FighterCharacter c) => standings.SeatOf(c.Id) is { } s ? $"{c.HudName}は{(s == "王者" ? org.BeltName(division) + "の王者" : $"ランキング{s}")}" : $"{c.HudName}はランキング外";
        lines.Add($"{(titleBout ? $"この試合は{org.BeltName(division)}を懸けたタイトルマッチ。" : "")}{Seat(red)}、{Seat(blue)}。");
        return (org.BoutName(division, titleBout), lines);
    }

    /// <summary>名簿の計量の表（体重・階級・出られるか・席）を Markdown で。</summary>
    public static string WeighIn(Organization org, OrgStandings standings, IEnumerable<FighterCharacter> roster)
    {
        ArgumentNullException.ThrowIfNull(org);
        ArgumentNullException.ThrowIfNull(standings);
        ArgumentNullException.ThrowIfNull(roster);
        var sb = new StringBuilder();
        sb.AppendLine(CultureInfo.InvariantCulture, $"# {org.LogoText} 計量（{org.Name}）");
        sb.AppendLine();
        sb.AppendLine("| 選手 | 所属 | 体重 | 階級 | 出場 | 席 |");
        sb.AppendLine("|---|---|---:|---|---|---|");
        foreach (var c in roster.OrderBy(WeightOf).ThenBy(c => c.Id, StringComparer.Ordinal))
        {
            var why = Ineligible(org, c);
            var member = !org.Ranking.MembersOnly || c.Org == org.Id;
            var entry = why ?? (member ? "可" : $"可（所属が {c.Org.ToUpperInvariant()} なので順位には入らない）");
            sb.AppendLine(CultureInfo.InvariantCulture, $"| {c.HudName}（{c.Id}） | {c.Org.ToUpperInvariant()} | {WeightOf(c)} kg | {org.DivisionOf(WeightOf(c)).Name} | {entry} | {standings.SeatOf(c.Id) ?? "—"} |");
        }

        return sb.ToString();
    }

    /// <summary>順位に入る選手（出場の条件を満たし、<see cref="OrgRanking.MembersOnly"/> なら所属の選手）を階級ごとに。</summary>
    public static IReadOnlyDictionary<string, IReadOnlyList<FighterCharacter>> Pool(Organization org, IEnumerable<FighterCharacter> roster)
    {
        ArgumentNullException.ThrowIfNull(org);
        ArgumentNullException.ThrowIfNull(roster);
        var eligible = roster.Where(c => Ineligible(org, c) is null && (!org.Ranking.MembersOnly || c.Org == org.Id)).ToList();
        return org.Divisions.ToDictionary(d => d.Id,
            d => (IReadOnlyList<FighterCharacter>)[.. eligible.Where(c => org.DivisionOf(WeightOf(c)) == d).OrderBy(c => c.Id, StringComparer.Ordinal)]);
    }
}
