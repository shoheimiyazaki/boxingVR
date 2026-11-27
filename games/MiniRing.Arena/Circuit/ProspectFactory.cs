using System.Globalization;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using MiniRing.Bots;
using MiniRing.Characters;
using MiniRing.Voxels;

namespace MiniRing.Arena.Circuit;

/// <summary>作った新人（名簿の形の 1 ファイルと、読み直したキャラ）。</summary>
internal sealed record Prospect(FighterCharacter Character, string Json, string Division);

/// <summary>
/// 新人を種から作る（docs/plans/IVF_PROSPECTS_PLAN.md「体の作り方」）。違いは体格・教科書・教育の小さな揺らぎ・顔・名前・国だけで、強さの倍率は持たない（CLAUDE.md §1.7）。
/// 同じ決まり・同じ番号なら同じファイル（<see cref="CircuitRandom"/>）。ファイルは名簿の読み込み（<see cref="CharacterMaker.ToFile"/>）を通して確かめる。
/// </summary>
internal static class ProspectFactory
{
    private static readonly JsonSerializerOptions FaceOptions = new() { PropertyNamingPolicy = JsonNamingPolicy.CamelCase };

    /// <summary>
    /// 全員の（階級, 国）の席: 国は全体の人数を重みで最大剰余法に割り振り（割合どおり）、種で混ぜて、階級の順に並べた席へ配る。
    /// </summary>
    public static IReadOnlyList<(string Division, string Country)> Slots(CircuitConfig config)
    {
        ArgumentNullException.ThrowIfNull(config);
        var total = config.Total;
        var countries = config.Countries.OrderBy(p => p.Key, StringComparer.Ordinal).ToList();
        var sum = countries.Sum(c => (double)c.Value.Weight);
        var quotas = countries.Select(c => (c.Key, Exact: total * c.Value.Weight / sum)).ToList();
        var counts = quotas.ToDictionary(q => q.Key, q => (int)Math.Floor(q.Exact));
        foreach (var q in quotas.OrderByDescending(q => q.Exact - Math.Floor(q.Exact)).ThenBy(q => q.Key, StringComparer.Ordinal).Take(total - counts.Values.Sum()))
        {
            counts[q.Key]++;
        }

        var rng = new CircuitRandom(CircuitRandom.Mix(config.Seed, (ulong)config.GeneratorVersion, CircuitRandom.Hash("slots")));
        var people = rng.Shuffle(countries.SelectMany(c => Enumerable.Repeat(c.Key, counts[c.Key])));
        var divisions = config.Divisions.SelectMany(d => Enumerable.Repeat(d.Id, d.Count)).ToList();
        return [.. divisions.Zip(people)];
    }

    /// <summary>
    /// 新人の名前が重なってはいけない名簿の id: 名簿のうち新人リーグで作られた選手（record に kind: generated）を除く。登録した新人が名簿に入っても、
    /// 同じ種で作り直したときの名前の引き直しが変わらない（作り直しても同じファイル）。
    /// </summary>
    public static IReadOnlyList<string> ReservedIds(IEnumerable<FighterCharacter> roster)
        => [.. roster.Where(c => !c.Record.Any(r => r["kind"]?.GetValue<string>() == "generated")).Select(c => c.Id)];

    /// <summary>全員を作る（番号の順。id は前の人・<paramref name="takenIds"/> と重ならない）。</summary>
    public static IReadOnlyList<Prospect> All(CircuitConfig config, Organization org, IReadOnlyCollection<string> takenIds)
    {
        ArgumentNullException.ThrowIfNull(config);
        var taken = new HashSet<string>(takenIds, StringComparer.Ordinal);
        var slots = Slots(config);
        var list = new List<Prospect>(slots.Count);
        for (var i = 0; i < slots.Count; i++)
        {
            var p = Make(config, org, i, slots[i].Division, slots[i].Country, taken);
            taken.Add(p.Character.Id);
            list.Add(p);
        }

        return list;
    }

    /// <summary>番号 <paramref name="index"/> の新人を 1 人作る。</summary>
    public static Prospect Make(CircuitConfig config, Organization org, int index, string divisionId, string countryId, ISet<string> takenIds)
    {
        ArgumentNullException.ThrowIfNull(config);
        ArgumentNullException.ThrowIfNull(org);
        ArgumentNullException.ThrowIfNull(takenIds);
        var rng = new CircuitRandom(CircuitRandom.Mix(config.Seed, (ulong)config.GeneratorVersion, (ulong)index));
        var country = config.Countries[countryId];
        var division = config.Divisions.First(d => d.Id == divisionId);
        var (id, name, reading, hud) = NameOf(config, rng, countryId, country, takenIds);
        var (heightCm, weightKg, build) = BodyOf(config, org, rng, division);
        var face = FaceOf(config, rng, country);
        var textbook = rng.Weighted(config.Textbooks);
        var (style, southpaw) = EducationOf(config, rng, textbook);
        var age = rng.Range(config.Age.Min, config.Age.Max);
        var countryName = CharacterRoster.Countries[countryId].Name;
        var draft = new CharacterDraft
        {
            Id = id,
            Order = 1000 + index,
            Org = config.Org,
            Name = name,
            Reading = reading,
            HudName = hud,
            Origin = countryName,
            Country = countryId,
            Tagline = $"{org.LogoText} 新人（{org.Division(division.Id).Name}）",
            HeightCm = heightCm,
            WeightKg = weightKg,
            HeightRatio = build.Height,
            MuscleRatio = build.Muscle,
            Arms = build.Arms,
            Legs = build.Legs,
            Face = face,
            Textbook = textbook,
            Wear = "glove",
            Education = new JsonObject { ["style"] = style, ["vs"] = new JsonObject(), ["weaponBot"] = null },
            Record =
            [
                new JsonObject
                {
                    ["kind"] = "generated",
                    ["league"] = config.Org,
                    ["seed"] = config.Seed,
                    ["generatorVersion"] = config.GeneratorVersion,
                    ["index"] = index,
                    ["age"] = age,
                    ["stance"] = southpaw ? "サウスポー" : "オーソドックス",
                    ["note"] = "新人リーグの生成（体格・教科書・教育の揺らぎは種から。docs/plans/IVF_PROSPECTS_PLAN.md）",
                },
            ],
        };
        var (character, json) = CharacterMaker.ToFile(draft);
        var weighed = org.DivisionOf(CharacterProfile.ScaleWeightOf(character.Build));
        if (weighed.Id != division.Id)
        {
            throw new InvalidOperationException($"新人 {id} の計量 {CharacterProfile.WeightOf(character.Build)} kg が階級 {division.Id} に入りません（{weighed.Id}）。");
        }

        return new Prospect(character, json, division.Id);
    }

    private static (string Id, string Name, string Reading, string Hud) NameOf(CircuitConfig config, CircuitRandom rng, string countryId, CountryNames country, ISet<string> taken)
    {
        var deny = config.DenyNames.Select(n => n.ToLowerInvariant()).ToHashSet(StringComparer.Ordinal);
        for (var attempt = 0; ; attempt++)
        {
            var given = NamePart.Parse(rng.Pick(country.Given), countryId);
            var family = NamePart.Parse(rng.Pick(country.Family), countryId);
            var latin = $"{given.Latin} {family.Latin}".ToLowerInvariant();
            if (deny.Contains(latin))
            {
                continue;
            }

            var id = Slug(given.Latin) + "-" + Slug(family.Latin);
            if (taken.Contains(id))
            {
                if (attempt < 30)
                {
                    continue;
                }

                var n = 2;
                while (taken.Contains($"{id}-{n}"))
                {
                    n++;
                }

                id = $"{id}-{n}";
            }

            var familyFirst = country.Order == "family";
            var sep = country.Sep ?? "・";
            var name = familyFirst ? $"{family.Display}{sep}{given.Display}" : $"{given.Display}{sep}{family.Display}";
            var reading = familyFirst ? $"{family.Reading} {given.Reading}" : $"{given.Reading} {family.Reading}";
            return (id, name, reading, given.Reading);
        }
    }

    private static string Slug(string latin) => latin.ToLowerInvariant().Replace(' ', '-');

    private static (int HeightCm, int WeightKg, BodyBuild Build) BodyOf(CircuitConfig config, Organization org, CircuitRandom rng, CircuitDivision division)
    {
        var b = config.Body;
        for (var attempt = 0; attempt < 2000; attempt++)
        {
            var weight = rng.Range(division.MinKg, division.MaxKg);
            var height = rng.Range(b.MinCm, b.MaxCm);
            var arms = (float)Math.Round(1.0 + (b.LimbSd * rng.TruncatedNormal(2.0)), 3);
            var legs = (float)Math.Round(1.0 + (b.LimbSd * rng.TruncatedNormal(2.0)), 3);
            BodyBuild build;
            try
            {
                build = BodySizing.Solve(height, weight, arms, legs);
            }
            catch (ArgumentOutOfRangeException)
            {
                continue; // この身長では作れない体重
            }

            if (build.Muscle < b.MinMuscle || build.Muscle > b.MaxMuscle || org.DivisionOf(CharacterProfile.ScaleWeightOf(build)).Id != division.Id)
            {
                continue;
            }

            return (height, weight, build);
        }

        throw new InvalidOperationException($"階級 {division.Id}（{division.MinKg}〜{division.MaxKg} kg）の体が作れません。body の幅を見直してください。");
    }

    private static FaceDraft FaceOf(CircuitConfig config, CircuitRandom rng, CountryNames country)
    {
        var root = CharacterMaker.RootOf(rng.Weighted(country.Roots));
        var spec = root.Face(rng.Range(0, root.Skins.Count - 1), rng.Range(0, root.Hairs.Count - 1), rng.Range(0, root.Irises.Count - 1));
        var draft = FaceDraft.From(spec, CharacterMaker.LookOf(spec));
        var parts = draft.Parts.ToDictionary(p => p.Key, p => p.Value);
        var f = config.Face;
        parts["hair"] = new Dictionary<string, float> { [rng.Pick(f.Hairs)] = 1f };
        parts["beard"] = rng.NextDouble() < f.BeardChance ? new Dictionary<string, float> { [rng.Pick(f.Beards)] = 1f } : [];
        var values = JsonSerializer.SerializeToNode(draft.Values, FaceOptions)!.AsObject();
        foreach (var key in f.Values)
        {
            var v = values[key]?.GetValue<float>() ?? throw new InvalidDataException($"face.values: 顔の値 '{key}' はありません。");
            values[key] = (float)Math.Round(v * (1.0 + (f.ValueSd * rng.TruncatedNormal(2.0))), 3);
        }

        return draft with { Parts = parts, Values = values.Deserialize<FaceParts>(FaceOptions)! };
    }

    private static (JsonObject Style, bool Southpaw) EducationOf(CircuitConfig config, CircuitRandom rng, string textbook)
    {
        var book = JsonNode.Parse(BotCatalog.Style(textbook).ToJson())!.AsObject();
        var style = new JsonObject();
        var e = config.Education;
        foreach (var k in e.Keys)
        {
            var baseValue = book[k.Key]?.GetValue<float>() ?? throw new InvalidDataException($"education.keys: 教科書 {textbook} に鍵 '{k.Key}' がありません。");
            var value = Math.Clamp(baseValue * (1.0 + (e.Sd * rng.TruncatedNormal(2.0))), k.Min, k.Max);
            style[k.Key] = (float)Math.Round(value, 3);
        }

        var southpaw = book["Southpaw"]?.GetValue<bool>() ?? false;
        if (!southpaw && rng.NextDouble() < e.Southpaw)
        {
            style["Southpaw"] = true;
            southpaw = true;
        }

        return (style, southpaw);
    }

    /// <summary>ファイルの中身のハッシュ（改行を LF にそろえた UTF-8 の SHA-256）。記録とファイルが同じかを確かめる。</summary>
    public static string HashOf(string json)
    {
        var bytes = Encoding.UTF8.GetBytes(json.Replace("\r\n", "\n", StringComparison.Ordinal));
        return "sha256:" + Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(bytes)).ToLowerInvariant();
    }

    /// <summary>報告用の 1 行（体格・教科書）。</summary>
    public static string Describe(FighterCharacter c) => string.Create(CultureInfo.InvariantCulture,
        $"{CharacterProfile.HeightOf(c.Build)} cm・{CharacterProfile.WeightOf(c.Build)} kg・腕 {c.Build.Arms:0.00}・太さ {c.Build.Muscle:0.00}・{c.StyleName}");
}
