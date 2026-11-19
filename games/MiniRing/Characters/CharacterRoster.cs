using System.Numerics;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;
using MiniRing.Bots;
using MiniRing.Voxels;
using MiniRing.Weapons;

namespace MiniRing.Characters;

/// <summary>
/// キャラクターの名簿。1 人 1 ファイル（組み込みは <c>Characters/roster/&lt;id&gt;.json</c>、国の旗の表は <c>countries.json</c>）。
/// ファイルは体（<c>look</c>・<c>body</c>）＋ 教科書（<c>textbook</c>、スタイルの名前）＋ 教育（<c>education</c>、教科書からの差分）＋ 育ちの記録（<c>record</c>）。
/// ここは読み込みと検証だけ（解決は <see cref="FighterCharacter.Resolve(string)"/>）。
/// 値の範囲（体格・色・顔の部位）・教科書の名前・国・武器の名前・教育の鍵・<c>vs</c> の相手が壊れていれば、どの人のどの欄かを書いた例外で止める
/// （黙って既定に直さない: 見た目と強さが食い違う）。ファイルのパスから読む入口（<see cref="LoadFile"/>・<see cref="LoadDirectory"/>）は、
/// プレイヤーが作ったキャラ・新人（アプリのデータの場所）を読むためのもの。
/// </summary>
public static class CharacterRoster
{
    private const string RosterPrefix = "MiniRing.Characters.roster.";
    private const string CountriesResource = "MiniRing.Characters.countries.json";

    /// <summary>旗の横：縦（3:2）。</summary>
    public const float FlagAspect = 1.5f;

    private static readonly Lazy<IReadOnlyDictionary<string, CountryInfo>> LoadedCountries = new(() =>
    {
        using var stream = typeof(CharacterRoster).Assembly.GetManifestResourceStream(CountriesResource)
            ?? throw new InvalidOperationException($"組み込みの国の表 {CountriesResource} が見つかりません（MiniRing.csproj の EmbeddedResource）。");
        using var reader = new StreamReader(stream);
        return CountriesFromJson(reader.ReadToEnd());
    });

    private static readonly Lazy<IReadOnlyList<FighterCharacter>> Loaded = new(() =>
    {
        var assembly = typeof(CharacterRoster).Assembly;
        var files = new List<(string FileName, string Json)>();
        foreach (var resource in assembly.GetManifestResourceNames()
                     .Where(n => n.StartsWith(RosterPrefix, StringComparison.Ordinal) && n.EndsWith(".json", StringComparison.Ordinal))
                     .Order(StringComparer.Ordinal))
        {
            using var stream = assembly.GetManifestResourceStream(resource)!;
            using var reader = new StreamReader(stream);
            files.Add((resource[RosterPrefix.Length..], reader.ReadToEnd()));
        }

        if (files.Count == 0)
        {
            throw new InvalidOperationException($"組み込みの名簿 {RosterPrefix}*.json が 1 つもありません（MiniRing.csproj の EmbeddedResource）。");
        }

        return FromFiles(files);
    });

    /// <summary>国の表（組み込みの <c>countries.json</c>）。キャラクターの <c>country</c> はこの id。</summary>
    public static IReadOnlyDictionary<string, CountryInfo> Countries => LoadedCountries.Value;

    /// <summary>名簿の全員（表示の順: ファイルの <c>order</c>、同じなら id）。</summary>
    public static IReadOnlyList<FighterCharacter> All => Loaded.Value;

    /// <summary>id から探す。</summary>
    public static FighterCharacter Get(string id) => All.FirstOrDefault(c => c.Id == id)
        ?? throw new ArgumentException($"キャラクター '{id}' はいません（{string.Join(", ", All.Select(c => c.Id))}）。", nameof(id));

    /// <summary>id から探す（いなければ null）。</summary>
    public static FighterCharacter? Find(string id) => All.FirstOrDefault(c => c.Id == id);

    /// <summary>id の名簿の中の位置。</summary>
    public static int IndexOf(string id)
    {
        for (var i = 0; i < All.Count; i++)
        {
            if (All[i].Id == id)
            {
                return i;
            }
        }

        throw new ArgumentException($"キャラクター '{id}' はいません。", nameof(id));
    }

    private static readonly JsonSerializerOptions Options = new()
    {
        PropertyNameCaseInsensitive = true,
        UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow,
        ReadCommentHandling = JsonCommentHandling.Skip,
        Converters = { new JsonStringEnumConverter() },
    };

    /// <summary>国の表の JSON（<c>{ "countries": { id: { name, flag } } }</c>）を読んで検証する。</summary>
    public static IReadOnlyDictionary<string, CountryInfo> CountriesFromJson(string json)
    {
        ArgumentNullException.ThrowIfNull(json);
        CountriesDto root;
        try
        {
            root = JsonSerializer.Deserialize<CountriesDto>(json, Options) ?? throw new InvalidDataException("国の表が空です。");
        }
        catch (JsonException ex)
        {
            throw new InvalidDataException($"国の表の JSON が読めません: {ex.Message}", ex);
        }

        return (root.Countries ?? throw new InvalidDataException("国の表に countries がありません。"))
            .ToDictionary(p => p.Key, p => ToCountry(p.Key, p.Value));
    }

    /// <summary>
    /// キャラクターのファイル（ファイル名と JSON の組）から名簿を作って検証する。壊れていれば <see cref="InvalidDataException"/>。
    /// ファイル名（拡張子を除く）は id と同じ。<paramref name="knownIds"/> は、この中に無くても <c>education.vs</c> の相手として認める id
    /// （プレイヤーのファイルが組み込みのキャラを相手に挙げるとき）。
    /// </summary>
    public static IReadOnlyList<FighterCharacter> FromFiles(IEnumerable<(string FileName, string Json)> files, IReadOnlyCollection<string>? knownIds = null)
    {
        ArgumentNullException.ThrowIfNull(files);
        var parsed = new List<(string FileName, FileDto Dto)>();
        var ids = new HashSet<string>();
        foreach (var (fileName, json) in files)
        {
            ArgumentNullException.ThrowIfNull(json);
            FileDto dto;
            try
            {
                dto = JsonSerializer.Deserialize<FileDto>(json, Options) ?? throw new InvalidDataException("ファイルが空です。");
            }
            catch (JsonException ex)
            {
                throw new InvalidDataException($"キャラクターのファイル {fileName} の JSON が読めません: {ex.Message}", ex);
            }

            var id = dto.Id ?? throw new InvalidDataException($"キャラクターのファイル {fileName} に id がありません。");
            if (id.Length == 0 || !id.All(ch => ch is (>= 'a' and <= 'z') or (>= '0' and <= '9') or '-' or '_'))
            {
                throw new InvalidDataException($"id '{id}'（{fileName}）は小文字・数字・-・_ だけにしてください。");
            }

            if (Path.GetFileNameWithoutExtension(fileName) != id)
            {
                throw new InvalidDataException($"ファイル名 {fileName} と id '{id}' が違います（<id>.json にしてください）。");
            }

            if (!ids.Add(id))
            {
                throw new InvalidDataException($"id '{id}' が重複しています。");
            }

            parsed.Add((fileName, dto));
        }

        if (parsed.Count == 0)
        {
            throw new InvalidDataException("名簿が空です。");
        }

        var known = new HashSet<string>(ids);
        known.UnionWith(knownIds ?? []);
        var list = new List<(int Order, FighterCharacter Character)>();
        foreach (var (fileName, dto) in parsed)
        {
            try
            {
                var order = dto.Order ?? throw new InvalidDataException("order（名簿の並び順）がありません。");
                var character = ToCharacter(dto, Countries);
                ValidateEducation(character, known);
                list.Add((order, character));
            }
            catch (Exception ex) when (ex is ArgumentException or InvalidDataException)
            {
                throw new InvalidDataException($"キャラクター '{dto.Id}'（{fileName}）: {ex.Message}", ex);
            }
        }

        return [.. list.OrderBy(p => p.Order).ThenBy(p => p.Character.Id, StringComparer.Ordinal).Select(p => p.Character)];
    }

    /// <summary>
    /// ファイルのパスから 1 人読む（プレイヤーが作ったキャラ・新人。<c>education.vs</c> の相手は組み込みの名簿の id を認める）。
    /// 組み込みの id と同じ id は断る。
    /// </summary>
    public static FighterCharacter LoadFile(string path)
    {
        ArgumentException.ThrowIfNullOrEmpty(path);
        return LoadFiles([path]).Single();
    }

    /// <summary>ディレクトリの <c>*.json</c> を全部読む（<see cref="LoadFile"/> と同じ。名簿の並び順）。</summary>
    public static IReadOnlyList<FighterCharacter> LoadDirectory(string directory)
    {
        ArgumentException.ThrowIfNullOrEmpty(directory);
        if (!Directory.Exists(directory))
        {
            throw new DirectoryNotFoundException($"キャラクターのディレクトリ {directory} がありません。");
        }

        return LoadFiles(Directory.GetFiles(directory, "*.json").Order(StringComparer.Ordinal));
    }

    private static IReadOnlyList<FighterCharacter> LoadFiles(IEnumerable<string> paths)
    {
        var builtIn = All.Select(c => c.Id).ToHashSet();
        var loaded = FromFiles(paths.Select(p => (Path.GetFileName(p), File.ReadAllText(p))), builtIn);
        if (loaded.FirstOrDefault(c => builtIn.Contains(c.Id)) is { } clash)
        {
            throw new InvalidDataException($"id '{clash.Id}' は組み込みの名簿と重複しています。");
        }

        return loaded;
    }

    /// <summary>教育を、試しに全部解決して確かめる（鍵の綴り・値の範囲・<c>vs</c> の相手・予約の欄）。試合の途中で初めて壊れるのを避ける。</summary>
    private static void ValidateEducation(FighterCharacter character, HashSet<string> known)
    {
        var education = character.Education;
        _ = WeaponEducation.Resolve(character.Weapon, education.WeaponBot); // 武器の教科書に重ねて検査（鍵の綴り・値の範囲・武器にボットがあるか）

        var textbook = BotCatalog.Style(character.StyleName);
        _ = EducationResolver.Resolve(textbook, education, null);
        foreach (var opponent in education.Vs.Keys)
        {
            if (!known.Contains(opponent))
            {
                throw new InvalidDataException($"education.vs の相手 '{opponent}' は名簿にいません（{string.Join(", ", known.Order(StringComparer.Ordinal))}）。");
            }

            _ = EducationResolver.Resolve(textbook, education, opponent);
        }
    }

    private static FighterCharacter ToCharacter(FileDto c, IReadOnlyDictionary<string, CountryInfo> countries)
    {
        static string Need(string? value, string field) => string.IsNullOrWhiteSpace(value)
            ? throw new InvalidDataException($"{field} がありません。") : value;

        var name = Need(c.Name, "name");
        var country = countries.TryGetValue(Need(c.Country, "country"), out var info) ? info
            : throw new InvalidDataException($"国 '{c.Country}' は countries にありません。");
        var textbook = Need(c.Textbook, "textbook");
        if (!BotCatalog.Styles.Any(s => s.Name == textbook))
        {
            throw new InvalidDataException($"教科書（スタイル）'{textbook}' は BotCatalog にありません（{string.Join(", ", BotCatalog.Styles.Select(s => s.Name))}）。");
        }

        var body = c.Body ?? throw new InvalidDataException("body がありません。");
        var wear = HandWears.Parse(Need(body.Wear, "body.wear"));
        var weapon = body.Weapon is null ? (WeaponKind?)null : ParseWeapon(body.Weapon);
        var build = (body.Build ?? throw new InvalidDataException("body.build がありません。")).ToBuild();
        var face = c.Look ?? throw new InvalidDataException("look がありません。");
        if (!Enum.IsDefined(face.Hairstyle))
        {
            throw new InvalidDataException($"髪型 {face.Hairstyle} は知りません。");
        }

        var look = new FaceLook(Color(face.Skin, "look.skin"), Color(face.Hair, "look.hair"), Color(face.Eyes, "look.eyes"),
            face.Hairstyle, face.Brows, face.Beard) { Parts = ValidParts(face.Parts) };
        var org = c.Org ?? FighterCharacter.DefaultOrg;
        if (org.Length == 0 || !org.All(ch => ch is (>= 'a' and <= 'z') or (>= '0' and <= '9') or '-' or '_'))
        {
            throw new InvalidDataException($"org '{org}' は小文字・数字・-・_ の団体の id にしてください（wvc・vfc）。");
        }

        return new FighterCharacter(c.Id!, name, Need(c.Reading, "reading"), Need(c.HudName, "hudName"),
            Need(c.Origin, "origin"), country, wear, weapon, textbook, build, look, Need(c.Tagline, "tagline"),
            ToEducation(c.Education), ToRecord(c.Record))
        {
            Profile = c.Profile?.Validate(build),
            Face = ReadFace(c.Face, c.Id!, look),
            Org = org,
        };
    }

    private static CharacterEducation ToEducation(EducationDto? e)
    {
        if (e is null)
        {
            return CharacterEducation.Empty;
        }

        var vs = new Dictionary<string, JsonObject>();
        foreach (var (opponent, layer) in e.Vs ?? [])
        {
            vs[opponent] = layer ?? throw new InvalidDataException($"education.vs.{opponent} が null です（上書きが無いなら {{}} にしてください）。");
        }

        return new CharacterEducation(e.Style ?? new JsonObject(), vs, e.WeaponBot);
    }

    private static IReadOnlyList<JsonObject> ToRecord(List<JsonObject?>? record)
        => [.. (record ?? []).Select((r, i) => r ?? throw new InvalidDataException($"record[{i}] が null です。"))];

    private static FaceSpec ReadFace(JsonElement? face, string id, FaceLook look)
    {
        // "face" が無いキャラは、名前と look の髪型・眉・ひげから決める（新人・ボットと同じ決め方）。
        if (face is not { } element)
        {
            return FaceSpec.FromLook(look, id).Validate();
        }

        try
        {
            return FaceSpec.FromJson(element, id);
        }
        catch (InvalidDataException ex)
        {
            throw new InvalidDataException($"face: {ex.Message}", ex);
        }
    }

    private static FaceParts ValidParts(FaceParts? parts)
    {
        try
        {
            return (parts ?? FaceParts.Default).Validate();
        }
        catch (ArgumentOutOfRangeException ex)
        {
            throw new InvalidDataException($"look.parts: {ex.Message}", ex);
        }
    }

    private static WeaponKind ParseWeapon(string name) => name.Trim().ToLowerInvariant() switch
    {
        "katana" => WeaponKind.Katana,
        "longsword" => WeaponKind.Longsword,
        "ironsword" => WeaponKind.IronSword,
        "club" or "woodenclub" => WeaponKind.WoodenClub,
        "staff" or "bo" => WeaponKind.Staff,
        "jo" => WeaponKind.Jo,
        "bat" => WeaponKind.Bat,
        _ => throw new InvalidDataException($"武器 '{name}' は知りません（katana|longsword|ironSword|club|staff|jo|bat）。"),
    };

    private static Vector3 Color(float[]? rgb, string field)
    {
        if (rgb is not { Length: 3 } || rgb.Any(v => !float.IsFinite(v) || v is < 0f or > 1f))
        {
            throw new InvalidDataException($"{field} は 0〜1 の有限値 3 つ（RGB）で指定してください。");
        }

        return new Vector3(rgb[0], rgb[1], rgb[2]);
    }

    private static CountryInfo ToCountry(string id, CountryDto c)
    {
        var name = string.IsNullOrWhiteSpace(c.Name) ? throw new InvalidDataException($"国 '{id}' に name がありません。") : c.Name;
        var flag = c.Flag ?? throw new InvalidDataException($"国 '{id}' に flag がありません。");
        if (flag.Stripes is not { Length: > 0 })
        {
            throw new InvalidDataException($"国 '{id}' の旗に色の帯がありません。");
        }

        var marks = new List<FlagMark>();
        foreach (var m in flag.Marks ?? [])
        {
            if (!float.IsFinite(m.X) || !float.IsFinite(m.Y) || m.X is < 0f or > 1f || m.Y is < 0f or > 1f)
            {
                throw new InvalidDataException($"国 '{id}' の旗の図形の位置は 0〜1 にしてください。");
            }

            // 円は旗の高さに対する直径 d（旗は 3:2 なので、幅に対しては d / 1.5）。四角は幅・高さの割合。
            var size = m.Shape == FlagShape.Disc ? new Vector2(m.D / FlagAspect, m.D) : new Vector2(m.W, m.H);
            if (!float.IsFinite(size.X) || !float.IsFinite(size.Y) || size.X <= 0f || size.Y <= 0f || size.X > 1.01f || size.Y > 1.01f)
            {
                throw new InvalidDataException($"国 '{id}' の旗の図形の大きさが不正です。");
            }

            marks.Add(new FlagMark(m.Shape, new Vector2(m.X, m.Y), size, Color(m.Color, $"国 '{id}' の旗の図形の色")));
        }

        return new CountryInfo(id, name, new FlagSpec(flag.Vertical, [.. flag.Stripes.Select(s => Color(s, $"国 '{id}' の帯の色"))], marks));
    }

    private sealed class CountriesDto
    {
        public Dictionary<string, CountryDto>? Countries { get; init; }
    }

    private sealed class CountryDto
    {
        public string? Name { get; init; }
        public FlagDto? Flag { get; init; }
    }

    private sealed class FlagDto
    {
        public bool Vertical { get; init; }
        public float[][]? Stripes { get; init; }
        public List<MarkDto>? Marks { get; init; }
    }

    private sealed class MarkDto
    {
        public FlagShape Shape { get; init; }
        public float X { get; init; }
        public float Y { get; init; }
        public float W { get; init; }
        public float H { get; init; }
        public float D { get; init; }
        public float[]? Color { get; init; }
    }

    private sealed class BuildDto
    {
        public float Height { get; init; } = 1f;
        public float Arms { get; init; } = 1f;
        public float Legs { get; init; } = 1f;
        public float Muscle { get; init; } = 1f;

        public BodyBuild ToBuild()
        {
            try
            {
                return new BodyBuild(Height, Arms, Legs, Muscle).Validate();
            }
            catch (ArgumentOutOfRangeException ex)
            {
                throw new InvalidDataException(ex.Message, ex);
            }
        }
    }

    private sealed class FaceDto
    {
        public float[]? Skin { get; init; }
        public float[]? Hair { get; init; }
        public float[]? Eyes { get; init; }
        public Hairstyle Hairstyle { get; init; }
        public bool Brows { get; init; }
        public bool Beard { get; init; }
        public FaceParts? Parts { get; init; }
    }

    private sealed class BodyDto
    {
        public BuildDto? Build { get; init; }
        public string? Wear { get; init; }
        public string? Weapon { get; init; }
    }

    private sealed class EducationDto
    {
        public JsonObject? Style { get; init; }
        public Dictionary<string, JsonObject?>? Vs { get; init; }
        public JsonObject? WeaponBot { get; init; }
    }

    private sealed class FileDto
    {
        public string? Id { get; init; }
        public int? Order { get; init; }
        public string? Name { get; init; }
        public string? Reading { get; init; }
        public string? HudName { get; init; }
        public string? Origin { get; init; }
        public string? Country { get; init; }
        public string? Tagline { get; init; }
        public string? Org { get; init; }
        public FaceDto? Look { get; init; }
        public JsonElement? Face { get; init; }
        public BodyDto? Body { get; init; }
        public string? Textbook { get; init; }
        public EducationDto? Education { get; init; }
        public List<JsonObject?>? Record { get; init; }
        public CharacterProfile? Profile { get; init; }
    }
}
