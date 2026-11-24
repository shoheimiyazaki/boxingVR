using System.Text.Json;
using System.Text.Json.Serialization;

namespace MiniRing.Arena.Circuit;

/// <summary>
/// 新人リーグの決まり（組み込みの <c>Orgs/circuit/&lt;org&gt;.json</c>、docs/plans/IVF_PROSPECTS_PLAN.md）: 人数・体の引き方・国の割合と名前の表・興行・引退・登録。
/// 強さの値は持たない（体格・教科書・教育の小さな揺らぎだけ。CLAUDE.md §1.7）。
/// </summary>
internal sealed record CircuitConfig
{
    private const string Prefix = "MiniRing.Arena.Circuit.";

    private static readonly JsonSerializerOptions Options = new()
    {
        PropertyNameCaseInsensitive = true,
        UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow,
        ReadCommentHandling = JsonCommentHandling.Skip,
        AllowTrailingCommas = true,
    };

    public required string Org { get; init; }
    public required ulong Seed { get; init; }
    public required int GeneratorVersion { get; init; }
    public required IReadOnlyList<CircuitDivision> Divisions { get; init; }
    public required BodyRange Body { get; init; }
    public required IntRange Age { get; init; }
    public required IReadOnlyDictionary<string, float> Textbooks { get; init; }
    public required EducationJitter Education { get; init; }
    public required FaceJitter Face { get; init; }
    public required IReadOnlyDictionary<string, CountryNames> Countries { get; init; }
    public IReadOnlyList<string> DenyNames { get; init; } = [];
    public required SeasonRules Season { get; init; }
    public required RetireRules Retire { get; init; }
    public required RegisterRules Register { get; init; }

    /// <summary>教科書ごとの戦い方の一言（登録した新人のプロフィールの「戦い方」の書き出し）。</summary>
    public IReadOnlyDictionary<string, string> StyleText { get; init; } = new Dictionary<string, string>();

    /// <summary>全体の人数。</summary>
    public int Total => Divisions.Sum(d => d.Count);

    /// <summary>組み込みの決まり。</summary>
    public static CircuitConfig Of(string org)
    {
        using var stream = typeof(CircuitConfig).Assembly.GetManifestResourceStream($"{Prefix}{org}.json")
            ?? throw new ArgumentException($"団体 '{org}' の新人リーグの決まり（Orgs/circuit/{org}.json）がありません。", nameof(org));
        using var reader = new StreamReader(stream);
        return FromJson(reader.ReadToEnd());
    }

    /// <summary>JSON から読んで確かめる（壊れていればどこかを書いた <see cref="InvalidDataException"/>）。</summary>
    public static CircuitConfig FromJson(string json)
    {
        CircuitConfig config;
        try
        {
            config = JsonSerializer.Deserialize<CircuitConfig>(json, Options) ?? throw new InvalidDataException("新人リーグの決まりが空です。");
        }
        catch (JsonException ex)
        {
            throw new InvalidDataException($"新人リーグの決まりが読めません: {ex.Message}", ex);
        }

        return config.Validate();
    }

    private CircuitConfig Validate()
    {
        static void Need(bool ok, string message)
        {
            if (!ok)
            {
                throw new InvalidDataException(message);
            }
        }

        Need(Divisions.Count > 0 && Divisions.All(d => d.Count > 0 && d.MinKg <= d.MaxKg), "divisions: 人数は 1 以上・minKg ≤ maxKg にしてください。");
        Need(Divisions.Select(d => d.Id).Distinct().Count() == Divisions.Count, "divisions: 階級が重なっています。");
        Need(Body.MinCm < Body.MaxCm && Body.LimbSd >= 0f && Body.MinMuscle < Body.MaxMuscle, "body の幅が逆です。");
        Need(Age.Min <= Age.Max, "age の幅が逆です。");
        Need(Textbooks.Count > 0 && Textbooks.Values.All(w => w > 0f), "textbooks: 重みは正にしてください。");
        Need(Countries.Count > 0, "countries がありません。");
        foreach (var (id, c) in Countries)
        {
            Need(c.Weight > 0f && c.Roots.Count > 0 && c.Roots.Values.All(w => w > 0f), $"countries.{id}: weight・roots の重みは正にしてください。");
            Need(c.Given.Count > 0 && c.Family.Count > 0, $"countries.{id}: given・family がありません。");
            Need(c.Order is "given" or "family", $"countries.{id}: order は given か family にしてください。");
            foreach (var name in c.Given.Concat(c.Family))
            {
                _ = NamePart.Parse(name, id);
            }
        }

        Need(Season.Events > 0 && Season.ProspectRounds > 0 && Season.ChallengeRounds > 0 && Season.BreakSeconds > 0f, "season の値は正にしてください。");
        Need(Retire.NetLosses > 0 && Retire.LosingStreak > 0 && Retire.KoLosses > 0, "retire の値は正にしてください。");
        Need(Register.PerDivision > 0 && Register.MinBouts > 0 && Register.MaxChallenges > 0, "register の値は正にしてください。");
        return this;
    }
}

internal sealed record CircuitDivision(string Id, int Count, int MinKg, int MaxKg);

internal sealed record BodyRange(int MinCm, int MaxCm, float LimbSd, float MinMuscle, float MaxMuscle);

internal sealed record IntRange(int Min, int Max);

internal sealed record JitterKey(string Key, float Min, float Max);

internal sealed record EducationJitter(float Sd, IReadOnlyList<JitterKey> Keys, float Southpaw);

internal sealed record FaceJitter(IReadOnlyList<string> Hairs, IReadOnlyList<string> Beards, float BeardChance, float ValueSd, IReadOnlyList<string> Values);

internal sealed record CountryNames(float Weight, IReadOnlyDictionary<string, float> Roots, string Order, IReadOnlyList<string> Given, IReadOnlyList<string> Family, string? Sep = null);

internal sealed record SeasonRules(int Events, int ProspectRounds, int ChallengeRounds, float BreakSeconds);

/// <summary>引退の規則（戦績だけから。どれか 1 つで引退）。</summary>
internal sealed record RetireRules(int NetLosses, int LosingStreak, int KoLosses);

internal sealed record RegisterRules(int PerDivision, int MinBouts, int MaxChallenges);

/// <summary>名前の部品（「綴り|表記|読み」）。</summary>
internal sealed record NamePart(string Latin, string Display, string Reading)
{
    public static NamePart Parse(string text, string country)
    {
        var parts = text.Split('|');
        if (parts.Length is < 2 or > 3 || parts.Any(string.IsNullOrWhiteSpace) || !parts[0].All(ch => ch is (>= 'A' and <= 'Z') or (>= 'a' and <= 'z') or ' ' or '-'))
        {
            throw new InvalidDataException($"countries.{country}: 名前 '{text}' は「綴り（ASCII）|表記|読み」にしてください。");
        }

        return new NamePart(parts[0], parts[1], parts.Length == 3 ? parts[2] : parts[1]);
    }
}
