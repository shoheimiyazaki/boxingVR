using System.Numerics;
using System.Text.Json;
using System.Text.Json.Serialization;
using MiniRing.Bots;
using MiniRing.Voxels;
using MiniRing.Weapons;

namespace MiniRing.Characters;

/// <summary>体型（キャラメイクの表 <c>maker.json</c> の 1 行）: 筋肉の太さと腕・脚の長さの初めの値。体重を変えると太さは体重から解き直す（<see cref="BodySizing"/>）。</summary>
public sealed record BodyType(string Id, string Label, float Muscle, float Arms, float Legs, string Note);

/// <summary>
/// ルーツ（キャラメイクの表の 1 行）: 顔のテンプレート。肌・髪・瞳の色の候補と、部品の重み・顔の値。見た目だけで強さに関係しない（体格は変えない）。
/// 架空の人物を作る出発点で、作った後は部位も値も色も全部変えられる。
/// </summary>
public sealed record FaceRoot(string Id, string Label, IReadOnlyList<Vector3> Skins, IReadOnlyList<Vector3> Hairs, IReadOnlyList<Vector3> Irises,
    IReadOnlyDictionary<string, IReadOnlyDictionary<string, float>> Parts, FaceParts Values)
{
    /// <summary>このルーツの顔（色は候補の番号。範囲の外は端に寄せる）。</summary>
    public FaceSpec Face(int skin = 0, int hair = 0, int iris = 0)
        => new FaceSpec(Parts, Values)
        {
            Skin = Skins[Math.Clamp(skin, 0, Skins.Count - 1)],
            Hair = Hairs[Math.Clamp(hair, 0, Hairs.Count - 1)],
            Iris = Irises[Math.Clamp(iris, 0, Irises.Count - 1)],
        }.Validate();
}

/// <summary>
/// キャラメイクの下書き（作る画面が持つ値）。保存する物はこれではなく、ここから作った名簿のファイル（<see cref="CharacterMaker.ToFile"/>、<see cref="CharacterFile"/>）。
/// 下書きの身長・体重は名簿の式（<see cref="CharacterProfile.HeightOf"/>・<see cref="CharacterProfile.WeightOf"/>）の値で、体格はそこから解く（<see cref="BodySizing.Solve"/>）。
/// </summary>
public sealed record CharacterDraft
{
    public string Id { get; init; } = "";
    public int Order { get; init; }
    public string Org { get; init; } = FighterCharacter.DefaultOrg;
    public string Name { get; init; } = "";
    public string Reading { get; init; } = "";
    public string HudName { get; init; } = "";
    public string Origin { get; init; } = "";
    public string Country { get; init; } = "";
    public string Tagline { get; init; } = "";

    /// <summary>身長 [cm]。</summary>
    public int HeightCm { get; init; } = CharacterProfile.StandardHeightCm;

    /// <summary>体重 [kg]（計量・階級の体重。体格の太さはここから解く）。</summary>
    public int WeightKg { get; init; } = CharacterProfile.StandardWeightKg;

    /// <summary>
    /// 身長の比と太さの比をそのまま持つとき（名簿のキャラを読み込んだとき）。式で出し直した身長・体重が <see cref="HeightCm"/>・<see cref="WeightKg"/> と同じ間は
    /// この値を使い（作り直して体の寸法が丸めでずれないように）、違えば身長・体重から解き直す。
    /// </summary>
    public float? HeightRatio { get; init; }

    /// <inheritdoc cref="HeightRatio"/>
    public float? MuscleRatio { get; init; }

    /// <summary>腕の長さの比（身長に掛ける）。</summary>
    public float Arms { get; init; } = 1f;

    /// <summary>脚の長さの比。</summary>
    public float Legs { get; init; } = 1f;

    /// <summary>顔（部品の重み・値・肌・髪・瞳の色）。ファイルの <c>"face"</c> と同じ形。</summary>
    public FaceDraft Face { get; init; } = new();

    /// <summary>教科書（<see cref="BotCatalog.Styles"/> の名前）。</summary>
    public string Textbook { get; init; } = BotCatalog.Champion.Name;

    /// <summary>はめる物（<see cref="HandWears.Parse"/> の名前）。</summary>
    public string Wear { get; init; } = "glove";

    /// <summary>握る武器（名簿の名前、無ければ null）。</summary>
    public string? Weapon { get; init; }

    /// <summary>プロフィール（任意。身長・体重は体から入れ直すので、書かなくてよい）。</summary>
    public CharacterProfile? Profile { get; init; }

    /// <summary>教育（ファイルの <c>"education"</c> をそのまま。キャラメイクは変えない）。名簿のキャラを読み込んで直したとき、育てた教育を失わないように運ぶ。null なら空。</summary>
    public System.Text.Json.Nodes.JsonObject? Education { get; init; }

    /// <summary>育ちの記録（ファイルの <c>"record"</c> をそのまま運ぶ）。null なら空。</summary>
    public System.Text.Json.Nodes.JsonArray? Record { get; init; }
}

/// <summary>下書きの顔（ファイルの <c>"face"</c> と同じ欄）。</summary>
public sealed record FaceDraft
{
    public Dictionary<string, Dictionary<string, float>> Parts { get; init; } = [];
    public FaceParts Values { get; init; } = FaceParts.Default;
    public float[] Skin { get; init; } = [0.8f, 0.62f, 0.48f];
    public float[] Hair { get; init; } = [0.08f, 0.07f, 0.06f];
    public float[] Iris { get; init; } = [0.188f, 0.125f, 0.09f];

    /// <summary>顔の定義（検証済み）から。</summary>
    public static FaceDraft From(FaceSpec face, FaceLook look)
    {
        ArgumentNullException.ThrowIfNull(face);
        ArgumentNullException.ThrowIfNull(look);
        return new FaceDraft
        {
            Parts = face.Parts.ToDictionary(p => p.Key, p => p.Value.ToDictionary(w => w.Key, w => w.Value)),
            Values = face.Values,
            Skin = Arr(face.Skin ?? look.Skin),
            Hair = Arr(face.Hair ?? look.Hair),
            Iris = Arr(face.Iris ?? look.Pupil),
        };

        static float[] Arr(Vector3 v) => [v.X, v.Y, v.Z];
    }

    /// <summary>顔の定義へ（検証する。壊れていれば <see cref="InvalidDataException"/>）。</summary>
    public FaceSpec ToSpec()
    {
        static Vector3 Color(float[]? c, string field) => c is { Length: 3 } ? new Vector3(c[0], c[1], c[2])
            : throw new InvalidDataException($"face.{field} は [r, g, b] にしてください。");
        return new FaceSpec(
            (Parts ?? []).ToDictionary(p => p.Key, p => (IReadOnlyDictionary<string, float>)(p.Value ?? [])),
            Values ?? FaceParts.Default)
        {
            Skin = Color(Skin, "skin"),
            Hair = Color(Hair, "hair"),
            Iris = Color(Iris, "iris"),
        }.Validate();
    }
}

/// <summary>
/// キャラメイク（CLAUDE.md §1.8 の「プレイヤーが体と武器を作る」の第一歩）: 下書き（<see cref="CharacterDraft"/>）を名簿のファイルにし、ファイルから下書きに戻す。
/// ファイルは名簿と同じ形で、作ったファイルは名簿の読み込み（<see cref="CharacterRoster.FromFiles"/>）を通してから返す（壊れたファイルを保存しない）。
/// 体型とルーツの表は組み込みの <c>maker.json</c>。強さの倍率は持たない（体格は寸法だけ、教育は空 = 教科書そのもの）。
/// </summary>
public static class CharacterMaker
{
    private const string TableResource = "MiniRing.Characters.maker.json";

    private static readonly JsonSerializerOptions Options = new()
    {
        PropertyNameCaseInsensitive = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow,
        ReadCommentHandling = JsonCommentHandling.Skip,
        AllowTrailingCommas = true,
    };

    private static readonly Lazy<(IReadOnlyList<BodyType> Types, IReadOnlyList<FaceRoot> Roots)> Table = new(() =>
    {
        using var stream = typeof(CharacterMaker).Assembly.GetManifestResourceStream(TableResource)
            ?? throw new InvalidOperationException($"組み込みのキャラメイクの表 {TableResource} が見つかりません（MiniRing.csproj の EmbeddedResource）。");
        using var reader = new StreamReader(stream);
        return TableFromJson(reader.ReadToEnd());
    });

    /// <summary>体型の表。</summary>
    public static IReadOnlyList<BodyType> BodyTypes => Table.Value.Types;

    /// <summary>ルーツ（顔のテンプレート）の表。</summary>
    public static IReadOnlyList<FaceRoot> Roots => Table.Value.Roots;

    /// <summary>id から体型を引く。</summary>
    public static BodyType BodyTypeOf(string id) => BodyTypes.FirstOrDefault(t => t.Id == id)
        ?? throw new ArgumentException($"体型 '{id}' はありません（{string.Join(", ", BodyTypes.Select(t => t.Id))}）。", nameof(id));

    /// <summary>id からルーツを引く。</summary>
    public static FaceRoot RootOf(string id) => Roots.FirstOrDefault(r => r.Id == id)
        ?? throw new ArgumentException($"ルーツ '{id}' はありません（{string.Join(", ", Roots.Select(r => r.Id))}）。", nameof(id));

    /// <summary>キャラメイクの表の JSON を読んで検証する（色・顔の値・部品の重み・体型の比の範囲）。</summary>
    public static (IReadOnlyList<BodyType> Types, IReadOnlyList<FaceRoot> Roots) TableFromJson(string json)
    {
        TableDto dto;
        try
        {
            dto = JsonSerializer.Deserialize<TableDto>(json, Options) ?? throw new InvalidDataException("キャラメイクの表が空です。");
        }
        catch (JsonException ex)
        {
            throw new InvalidDataException($"キャラメイクの表が読めません: {ex.Message}", ex);
        }

        var types = (dto.BodyTypes ?? throw new InvalidDataException("bodyTypes がありません。")).Select(t =>
        {
            _ = new BodyBuild(1f, t.Arms, t.Legs, t.Muscle).Validate();
            return t;
        }).ToList();
        var roots = (dto.Roots ?? throw new InvalidDataException("roots がありません。")).Select(r =>
        {
            static IReadOnlyList<Vector3> Colors(float[][]? cs, string field, string id) => cs is { Length: > 0 }
                ? [.. cs.Select(c => c is { Length: 3 } && c.All(v => float.IsFinite(v) && v is >= 0f and <= 1f) ? new Vector3(c[0], c[1], c[2])
                    : throw new InvalidDataException($"ルーツ '{id}' の {field} は 0〜1 の [r, g, b] にしてください。"))]
                : throw new InvalidDataException($"ルーツ '{id}' に {field} がありません。");
            var id = r.Id ?? throw new InvalidDataException("ルーツに id がありません。");
            var root = new FaceRoot(id, r.Label ?? id, Colors(r.Skins, "skins", id), Colors(r.Hairs, "hairs", id), Colors(r.Irises, "irises", id),
                (r.Parts ?? []).ToDictionary(p => p.Key, p => (IReadOnlyDictionary<string, float>)(p.Value ?? [])), r.Values ?? FaceParts.Default);
            try
            {
                _ = root.Face();
            }
            catch (InvalidDataException ex)
            {
                throw new InvalidDataException($"ルーツ '{id}': {ex.Message}", ex);
            }

            return root;
        }).ToList();
        if (types.Count == 0 || roots.Count == 0 || types.Select(t => t.Id).Distinct().Count() != types.Count || roots.Select(r => r.Id).Distinct().Count() != roots.Count)
        {
            throw new InvalidDataException("体型とルーツは 1 つ以上・id は重ならないようにしてください。");
        }

        return (types, roots);
    }

    /// <summary>
    /// 新しい下書き: 体型 <paramref name="bodyType"/>・ルーツ <paramref name="root"/> の初めの値（身長は基準の体・体重はその体型の太さから）。
    /// 名前・国は空（画面で書く）。
    /// </summary>
    public static CharacterDraft NewDraft(string bodyType = "standard", string root = "east-asia", int? heightCm = null)
    {
        var height = heightCm ?? CharacterProfile.StandardHeightCm;
        var type = BodyTypeOf(bodyType);
        var face = RootOf(root).Face();
        return new CharacterDraft
        {
            HeightCm = height,
            WeightKg = BodySizing.WeightFor(height, type.Muscle, type.Arms, type.Legs),
            Arms = type.Arms,
            Legs = type.Legs,
            Face = FaceDraft.From(face, LookOf(face)),
        };
    }

    /// <summary>名簿のキャラ（読み込んだファイル）から下書きへ（身長・体重は体格から式で出す）。</summary>
    public static CharacterDraft FromCharacter(FighterCharacter character)
    {
        ArgumentNullException.ThrowIfNull(character);
        return new CharacterDraft
        {
            Id = character.Id,
            Order = character.Order,
            Org = character.Org,
            Name = character.Name,
            Reading = character.Reading,
            HudName = character.HudName,
            Origin = character.Origin,
            Country = character.Country.Id,
            Tagline = character.Tagline,
            HeightCm = CharacterProfile.HeightOf(character.Build),
            WeightKg = CharacterProfile.WeightOf(character.Build),
            HeightRatio = character.Build.Height,
            MuscleRatio = character.Build.Muscle,
            Arms = character.Build.Arms,
            Legs = character.Build.Legs,
            Face = FaceDraft.From(character.Face, character.Look),
            Textbook = character.StyleName,
            Wear = CharacterFile.WearName(character.Wear),
            Weapon = character.Weapon is { } w ? CharacterFile.WeaponName(w) : null,
            Profile = character.Profile,
            Education = CharacterFile.ToNode(character)["education"]!.AsObject(),
            Record = CharacterFile.ToNode(character)["record"]!.AsArray(),
        };
    }

    /// <summary>
    /// 下書きから名簿のキャラを作る（ファイルに書いて名簿の読み込みで読み直した物）。壊れていれば、どの欄かを書いた <see cref="InvalidDataException"/>。
    /// <paramref name="knownIds"/> は <c>education.vs</c> の相手として認める id（教育は空なので今は使わない）。順番（order）が 0 なら名簿の最後の次。
    /// </summary>
    public static (FighterCharacter Character, string Json) ToFile(CharacterDraft draft, IReadOnlyCollection<string>? knownIds = null)
    {
        ArgumentNullException.ThrowIfNull(draft);
        var character = Build(draft);
        var node = CharacterFile.ToNode(character);
        if (draft.Education is { } education)
        {
            node["education"] = education.DeepClone();
        }

        if (draft.Record is { } record)
        {
            node["record"] = record.DeepClone();
        }

        var json = CharacterFile.Format(node);
        var loaded = CharacterRoster.FromFiles([($"{character.Id}.json", json)], knownIds ?? [.. CharacterRoster.All.Select(c => c.Id)]).Single();
        return (loaded, json);
    }

    /// <summary>顔の定義から、名簿の <c>"look"</c>（色・髪型・眉・ひげ・顔の値）を作る。顔の層は <c>"face"</c> を描くので、look は同じ値の写し。</summary>
    public static FaceLook LookOf(FaceSpec face)
    {
        ArgumentNullException.ThrowIfNull(face);
        var hair = face.Parts.TryGetValue("hair", out var h) && h.Count > 0 ? h.OrderByDescending(p => p.Value).ThenBy(p => p.Key, StringComparer.Ordinal).First().Key : null;
        var style = hair switch
        {
            null or "hair.3" => Hairstyle.Bald,
            "hair.1" or "hair.2" => Hairstyle.Crop,
            "hair.4" or "hair.11" => Hairstyle.Mohawk,
            "hair.6" => Hairstyle.Afro,
            _ => Hairstyle.Full,
        };
        static Vector3 Or(Vector3? c, Vector3 fallback) => c ?? fallback;
        return new FaceLook(Or(face.Skin, new Vector3(0.8f, 0.62f, 0.48f)), Or(face.Hair, new Vector3(0.08f, 0.07f, 0.06f)), Or(face.Iris, new Vector3(0.188f, 0.125f, 0.09f)),
            style, Brows: face.Parts.TryGetValue("brows", out var b) && b.Count > 0, Beard: face.Parts.TryGetValue("beard", out var d) && d.Count > 0)
        { Parts = face.Values }.Validate();
    }

    private static FighterCharacter Build(CharacterDraft d)
    {
        static string Need(string? value, string field) => string.IsNullOrWhiteSpace(value) ? throw new InvalidDataException($"{field} を書いてください。") : value.Trim();

        var id = Need(d.Id, "id");
        var country = CharacterRoster.Countries.TryGetValue(Need(d.Country, "country（国）"), out var info) ? info
            : throw new InvalidDataException($"国 '{d.Country}' は国の表にありません（{string.Join(", ", CharacterRoster.Countries.Keys)}）。");
        BodyBuild build;
        try
        {
            build = BodySizing.Solve(d.HeightCm, d.WeightKg, d.Arms, d.Legs);
            if (d is { HeightRatio: { } h, MuscleRatio: { } m } && new BodyBuild(h, d.Arms, d.Legs, m) is var kept
                && kept.Validate() is var _ && CharacterProfile.HeightOf(kept) == d.HeightCm && CharacterProfile.WeightOf(kept) == d.WeightKg)
            {
                build = kept; // 読み込んだ体のまま（身長・体重を変えていない）
            }
        }
        catch (ArgumentOutOfRangeException ex)
        {
            throw new InvalidDataException(ex.Message.Split(" (Parameter")[0], ex);
        }

        HandWear wear;
        try
        {
            wear = HandWears.Parse(Need(d.Wear, "wear（はめる物）"));
        }
        catch (ArgumentException ex)
        {
            throw new InvalidDataException(ex.Message.Split(" (Parameter")[0], ex);
        }

        WeaponKind? weapon = string.IsNullOrWhiteSpace(d.Weapon) ? null
            : CharacterFile.Weapons.Cast<WeaponKind?>().FirstOrDefault(w => CharacterFile.WeaponName(w!.Value).Equals(d.Weapon.Trim(), StringComparison.OrdinalIgnoreCase))
              ?? throw new InvalidDataException($"武器 '{d.Weapon}' は知りません（{string.Join(", ", CharacterFile.Weapons.Select(CharacterFile.WeaponName))}）。");
        var face = (d.Face ?? throw new InvalidDataException("face を書いてください。")).ToSpec();
        var look = LookOf(face);
        var profile = d.Profile is { } p ? p with { HeightCm = d.HeightCm, WeightKg = d.WeightKg } : null;
        var order = d.Order > 0 ? d.Order : CharacterRoster.All.Max(c => c.Order) + 1;
        var name = Need(d.Name, "name（名前）");
        return new FighterCharacter(id, name, string.IsNullOrWhiteSpace(d.Reading) ? name : d.Reading.Trim(),
            string.IsNullOrWhiteSpace(d.HudName) ? name : d.HudName.Trim(), string.IsNullOrWhiteSpace(d.Origin) ? country.Name : d.Origin.Trim(), country,
            wear, weapon, Need(d.Textbook, "textbook（教科書）"), build, look, string.IsNullOrWhiteSpace(d.Tagline) ? "新人" : d.Tagline.Trim(),
            CharacterEducation.Empty, [])
        {
            Profile = profile,
            Face = face,
            Org = string.IsNullOrWhiteSpace(d.Org) ? FighterCharacter.DefaultOrg : d.Org.Trim(),
            Order = order,
        };
    }

    private sealed class TableDto
    {
        public List<BodyType>? BodyTypes { get; init; }
        public List<RootDto>? Roots { get; init; }
    }

    private sealed class RootDto
    {
        public string? Id { get; init; }
        public string? Label { get; init; }
        public float[][]? Skins { get; init; }
        public float[][]? Hairs { get; init; }
        public float[][]? Irises { get; init; }
        public Dictionary<string, Dictionary<string, float>?>? Parts { get; init; }
        public FaceParts? Values { get; init; }
    }
}
