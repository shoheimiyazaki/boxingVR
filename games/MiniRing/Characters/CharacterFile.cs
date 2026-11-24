using System.Numerics;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using MiniRing.Voxels;
using MiniRing.Weapons;

namespace MiniRing.Characters;

/// <summary>
/// キャラクターのファイル（名簿の <c>roster/&lt;id&gt;.json</c> と同じ形）を書く。読み込みは <see cref="CharacterRoster"/> だけで、ここは逆向き。
/// 書いた物を <see cref="CharacterRoster.FromFiles"/> で読み直すと同じキャラになる（<c>CharacterMakerTests</c>）。新しい形式は作らない:
/// 欄の名前・意味は読み込みの DTO と同じ。顔の値（<see cref="FaceParts"/>）は既定と違う値だけを書く（名簿の手書きのファイルと同じ）。
/// </summary>
public static partial class CharacterFile
{
    private static readonly JsonSerializerOptions Camel = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
    };

    private static readonly JsonSerializerOptions Pretty = new()
    {
        WriteIndented = true,
        IndentSize = 2,
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping, // 日本語の名前をそのまま読める形で書く（HTML へ埋め込まない）
    };

    /// <summary>1 人のファイルの中身（JSON の文字列、末尾に改行）。</summary>
    public static string Write(FighterCharacter character) => Format(ToNode(character));

    /// <summary>ファイルの JSON の木を文字列にする（色の RGB の配列は名簿の手書きのファイルと同じく 1 行）。</summary>
    public static string Format(JsonObject node)
    {
        ArgumentNullException.ThrowIfNull(node);
        return ColorArrays().Replace(node.ToJsonString(Pretty), m => m.Groups[1].Value + "[" + string.Join(", ", m.Groups[2].Value.Split(',', StringSplitOptions.TrimEntries)) + "]") + "\n";
    }

    /// <summary>色の欄（skin・hair・eyes・iris）の数の配列。欄の名前の後ろだけを見るので、文字列の中身は触らない。</summary>
    [GeneratedRegex(@"(""(?:skin|hair|eyes|iris)"": )\[\s*(-?[0-9][0-9.eE+-]*(?:\s*,\s*-?[0-9][0-9.eE+-]*)*)\s*\]")]
    private static partial Regex ColorArrays();

    /// <summary>1 人のファイルの JSON の木（欄の順は名簿の手書きのファイルと同じ）。</summary>
    public static JsonObject ToNode(FighterCharacter character)
    {
        ArgumentNullException.ThrowIfNull(character);
        var root = new JsonObject
        {
            ["id"] = character.Id,
            ["order"] = character.Order,
            ["org"] = character.Org,
            ["name"] = character.Name,
            ["reading"] = character.Reading,
            ["hudName"] = character.HudName,
            ["origin"] = character.Origin,
            ["country"] = character.Country.Id,
            ["tagline"] = character.Tagline,
        };
        if (character.Profile is { } profile)
        {
            root["profile"] = JsonSerializer.SerializeToNode(profile, Camel);
        }

        var look = character.Look;
        root["look"] = new JsonObject
        {
            ["skin"] = Color(look.Skin),
            ["hair"] = Color(look.Hair),
            ["hairstyle"] = look.Style.ToString(),
            ["eyes"] = Color(look.Pupil),
            ["brows"] = look.Brows,
            ["beard"] = look.Beard,
            ["parts"] = NonDefault(look.Parts),
        };
        root["face"] = Face(character.Face);
        root["body"] = new JsonObject
        {
            ["build"] = BuildNode(character.Build),
            ["wear"] = WearName(character.Wear),
            ["weapon"] = character.Weapon is { } weapon ? WeaponName(weapon) : null,
        };
        root["textbook"] = character.StyleName;
        var education = character.Education;
        var vs = new JsonObject();
        foreach (var (opponent, layer) in education.Vs.OrderBy(p => p.Key, StringComparer.Ordinal))
        {
            vs[opponent] = layer.DeepClone();
        }

        root["education"] = new JsonObject
        {
            ["style"] = education.Style.DeepClone(),
            ["vs"] = vs,
            ["weaponBot"] = education.WeaponBot?.DeepClone(),
        };
        root["record"] = new JsonArray([.. character.Record.Select(r => (JsonNode)r.DeepClone())]);
        return root;
    }

    /// <summary>顔の定義（ファイルの <c>"face"</c>）。部位は部品の表の順、値は既定と違うものだけ。</summary>
    public static JsonObject Face(FaceSpec face)
    {
        ArgumentNullException.ThrowIfNull(face);
        var parts = new JsonObject();
        foreach (var category in FacePartLibrary.Categories.Concat(face.Parts.Keys.Except(FacePartLibrary.Categories).Order(StringComparer.Ordinal)))
        {
            if (!face.Parts.TryGetValue(category, out var weights))
            {
                continue;
            }

            var w = new JsonObject();
            foreach (var (id, weight) in weights.OrderBy(p => p.Key, StringComparer.Ordinal))
            {
                w[id] = weight;
            }

            parts[category] = w;
        }

        var node = new JsonObject { ["parts"] = parts, ["values"] = NonDefault(face.Values) };
        if (face.Skin is { } skin)
        {
            node["skin"] = Color(skin);
        }

        if (face.Hair is { } hair)
        {
            node["hair"] = Color(hair);
        }

        if (face.Iris is { } iris)
        {
            node["iris"] = Color(iris);
        }

        return node;
    }

    /// <summary>体格（ファイルの <c>"body"."build"</c>）。四足（<see cref="BodyBuild.FourLegged"/>）は四足のときだけ書く。</summary>
    private static JsonObject BuildNode(BodyBuild build)
    {
        var node = new JsonObject
        {
            ["height"] = build.Height,
            ["arms"] = build.Arms,
            ["legs"] = build.Legs,
            ["muscle"] = build.Muscle,
        };
        if (build.FourLegged)
        {
            node["fourLegged"] = true;
        }

        return node;
    }

    /// <summary>はめる物のファイルの名前（<see cref="HandWears.Parse"/> が読む名前）。</summary>
    public static string WearName(HandWear wear) => wear switch
    {
        HandWear.Glove => "glove",
        HandWear.None => "none",
        HandWear.KnuckleDuster => "knuckles",
        HandWear.Katar => "katar",
        HandWear.Gauntlet => "gauntlet",
        HandWear.Tonfa => "tonfa",
        HandWear.Shield => "shield",
        HandWear.SickleClaw => "sickle-claw",
        _ => throw new ArgumentOutOfRangeException(nameof(wear), wear, "知らないはめる物です。"),
    };

    /// <summary>握る武器のファイルの名前（名簿の読み込みが読む名前）。名簿のキャラが握れない種類は例外。</summary>
    public static string WeaponName(WeaponKind weapon) => weapon switch
    {
        WeaponKind.Katana => "katana",
        WeaponKind.Longsword => "longsword",
        WeaponKind.IronSword => "ironSword",
        WeaponKind.WoodenClub => "club",
        WeaponKind.Staff => "staff",
        WeaponKind.Jo => "jo",
        WeaponKind.Bat => "bat",
        _ => throw new ArgumentOutOfRangeException(nameof(weapon), weapon, "名簿のキャラが握れる武器ではありません。"),
    };

    /// <summary>名簿のキャラが握れる武器（<see cref="WeaponName"/> が書ける種類）。</summary>
    public static IReadOnlyList<WeaponKind> Weapons { get; } =
        [WeaponKind.Katana, WeaponKind.Longsword, WeaponKind.IronSword, WeaponKind.WoodenClub, WeaponKind.Staff, WeaponKind.Jo, WeaponKind.Bat];

    private static JsonArray Color(Vector3 c) => [c.X, c.Y, c.Z];

    /// <summary>顔の値のうち、既定（<see cref="FaceParts.Default"/>）と違うものだけ（名前は camelCase。読み込みは大文字・小文字を問わない）。</summary>
    private static JsonObject NonDefault(FaceParts parts)
    {
        var all = JsonSerializer.SerializeToNode(parts, Camel)!.AsObject();
        var defaults = JsonSerializer.SerializeToNode(FaceParts.Default, Camel)!.AsObject();
        var result = new JsonObject();
        foreach (var (key, value) in all)
        {
            if (!JsonNode.DeepEquals(value, defaults[key]))
            {
                result[key] = value?.DeepClone();
            }
        }

        return result;
    }
}
