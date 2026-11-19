using System.Text.Json;
using System.Text.Json.Serialization;

namespace MiniRing.Voxels;

/// <summary>
/// 1 人の顔の定義（名簿のファイルの <c>"face"</c>）: 部位ごとの部品の重み（<see cref="FacePartLibrary.Categories"/>）と、寸法・位置・傾きの値（<see cref="FaceParts"/>）。
/// 部位の重みは部品の id → 重み（0 以上、合計が 1 でなくてよい: 組み立てが合計で割る）。2 つ以上の部品を並べると、形の重み付きの和を閾値で切って
/// 混ぜる（0/1 で元の部品、0.5 ずつで中間。<see cref="FaceComposer"/>）。眉・ひげ・髪は空にできる（無し）。ほかの部位は部品が 1 つ以上要る。
/// </summary>
public sealed record FaceSpec(IReadOnlyDictionary<string, IReadOnlyDictionary<string, float>> Parts, FaceParts Values)
{
    /// <summary>肌・髪・瞳の色（0〜1 の RGB）。null なら <see cref="FaceLook"/> の色を使う。人種・国の見た目の違いは、色・顔の値・部品の重みという連続の値でファイルに書く（種類の分岐をコードに書かない）。</summary>
    public System.Numerics.Vector3? Skin { get; init; }

    public System.Numerics.Vector3? Hair { get; init; }

    public System.Numerics.Vector3? Iris { get; init; }

    /// <summary>空にできる部位（眉・ひげ・髪）。</summary>
    public static readonly IReadOnlySet<string> Optional = new HashSet<string> { "hair", "beard", "brows", "eyelids", "hairtexture", "browridge", "lips", "faceshape" };

    /// <summary>部位 <paramref name="category"/> の (部品, 重み) の一覧（重みは合計 1 に直す。空なら空）。</summary>
    public IReadOnlyList<(FacePart Part, float Weight)> Weighted(string category, FacePartLibrary library)
    {
        if (!Parts.TryGetValue(category, out var weights) || weights.Count == 0)
        {
            return [];
        }

        var total = weights.Values.Sum();
        return [.. weights.OrderBy(w => w.Key, StringComparer.Ordinal).Select(w => (library.Get(w.Key), w.Value / total))];
    }

    /// <summary>値の検証（名簿から読んだ顔を試合へ渡す前に呼ぶ）。部位が知らない・部品が表に無い・重みが負か有限でない・合計が 0・必須の部位が空なら <see cref="InvalidDataException"/>。</summary>
    public FaceSpec Validate(FacePartLibrary? library = null)
    {
        library ??= FacePartLibrary.Default;
        foreach (var (category, weights) in Parts)
        {
            if (!library.AllCategories.Contains(category))
            {
                throw new InvalidDataException($"face.parts: 部位 '{category}' は知りません（{string.Join(", ", FacePartLibrary.Categories)}）。");
            }

            foreach (var (id, weight) in weights)
            {
                if (!id.StartsWith(category + ".", StringComparison.Ordinal) || library.Parts(category).All(p => p.Id != id))
                {
                    throw new InvalidDataException($"face.parts.{category}: 部品 '{id}' は表にありません。");
                }

                if (!float.IsFinite(weight) || weight < 0f)
                {
                    throw new InvalidDataException($"face.parts.{category}.{id}: 重み {weight} は 0 以上の有限値にしてください。");
                }
            }

            if (weights.Count > 0 && weights.Values.Sum() <= 0f)
            {
                throw new InvalidDataException($"face.parts.{category}: 重みの合計が 0 です（部品を外すなら空にしてください）。");
            }
        }

        foreach (var category in FacePartLibrary.Categories.Where(c => !Optional.Contains(c)))
        {
            if (!Parts.TryGetValue(category, out var weights) || weights.Count == 0)
            {
                throw new InvalidDataException($"face.parts.{category}: 部品を 1 つ以上選んでください。");
            }
        }

        foreach (var (color, field) in new[] { (Skin, "skin"), (Hair, "hair"), (Iris, "iris") })
        {
            if (color is { } c && (!float.IsFinite(c.X) || !float.IsFinite(c.Y) || !float.IsFinite(c.Z) || c.X is < 0f or > 1f || c.Y is < 0f or > 1f || c.Z is < 0f or > 1f))
            {
                throw new InvalidDataException($"face.{field}: 色は 0〜1 の有限値にしてください。");
            }
        }

        try
        {
            _ = Values.Validate();
        }
        catch (ArgumentOutOfRangeException ex)
        {
            throw new InvalidDataException($"face.values: {ex.Message}", ex);
        }

        return this;
    }

    /// <summary>
    /// 名前から決まる顔（新人・ボット用。同じ名前はいつも同じ顔）: 部位ごとに部品を 1 つ選び（ひげは 3 人に 1 人、眉は 3 人に 2 人）、値は <see cref="FaceParts.For"/>。
    /// 引く順を変えると同じ名前の顔が変わる（記録の再生に響かない: 顔は描画と接触の形だけ）。
    /// </summary>
    public static FaceSpec For(string identity, FacePartLibrary? library = null)
    {
        library ??= FacePartLibrary.Default;
        var h = Bots.BotNames.Hash(identity) ^ 0x9E3779B9u;
        uint Next(uint n)
        {
            h = unchecked((h * 1664525u) + 1013904223u);
            return (h >> 8) % n;
        }

        var parts = new Dictionary<string, IReadOnlyDictionary<string, float>>();
        foreach (var category in FacePartLibrary.Categories)
        {
            var all = library.Parts(category);
            var pick = all[(int)Next((uint)all.Count)].Id;
            var present = category switch { "beard" => Next(3) == 0, "brows" => Next(3) != 0, _ => true };
            parts[category] = present ? new Dictionary<string, float> { [pick] = 1f } : new Dictionary<string, float>();
        }

        return new FaceSpec(parts, FaceParts.For(identity));
    }

    /// <summary>
    /// 髪型・眉・ひげの指定（<see cref="FaceLook"/>）がある顔の定義: <paramref name="look"/> の髪型・眉・ひげはそのまま部品に当て、
    /// ほかの部位は名前から決める（<see cref="For"/>）。名簿に <c>"face"</c> が無いキャラ用。
    /// </summary>
    public static FaceSpec FromLook(FaceLook look, string identity, FacePartLibrary? library = null)
    {
        ArgumentNullException.ThrowIfNull(look);
        var named = For(identity, library);
        var parts = named.Parts.ToDictionary(p => p.Key, p => p.Value);
        parts["hair"] = look.Style switch
        {
            Hairstyle.Bald => One("hair.3"),
            Hairstyle.Crop => One("hair.2"),
            Hairstyle.Mohawk => One("hair.4"),
            Hairstyle.Afro => One("hair.6"),
            _ => One("hair.1"),
        };
        if (!look.Brows)
        {
            parts["brows"] = new Dictionary<string, float>();
        }
        else if (parts["brows"].Count == 0)
        {
            parts["brows"] = One("brows.1");
        }

        if (!look.Beard)
        {
            parts["beard"] = new Dictionary<string, float>();
        }
        else if (parts["beard"].Count == 0)
        {
            parts["beard"] = One("beard.4");
        }

        return new FaceSpec(parts, look.Parts);
    }

    private static readonly JsonSerializerOptions JsonOptions = new() { PropertyNameCaseInsensitive = true, UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow };

    private static Dictionary<string, float> One(string id) => new() { [id] = 1f };

    /// <summary>名簿の JSON（<c>"face": { "parts": { "nose": { "nose.2": 0.6, "nose.3": 0.4 } }, "values": { ... } }</c>）から読む。足りない部位は名前から決める。</summary>
    public static FaceSpec FromJson(JsonElement face, string identity, FacePartLibrary? library = null)
    {
        library ??= FacePartLibrary.Default;
        FaceDto dto;
        try
        {
            dto = face.Deserialize<FaceDto>(JsonOptions) ?? throw new InvalidDataException("face が null です。");
        }
        catch (JsonException ex)
        {
            throw new InvalidDataException($"face が読めません: {ex.Message}", ex);
        }

        var named = For(identity, library);
        var parts = named.Parts.ToDictionary(p => p.Key, p => p.Value);
        foreach (var (category, weights) in dto.Parts ?? [])
        {
            parts[category] = (IReadOnlyDictionary<string, float>)(weights ?? throw new InvalidDataException($"face.parts.{category} が null です。"));
        }

        System.Numerics.Vector3? Color(float[]? c, string field) => c is null ? null
            : c.Length == 3 ? new System.Numerics.Vector3(c[0], c[1], c[2]) : throw new InvalidDataException($"face.{field} は [r, g, b] にしてください。");
        return new FaceSpec(parts, dto.Values ?? named.Values) { Skin = Color(dto.Skin, "skin"), Hair = Color(dto.Hair, "hair"), Iris = Color(dto.Iris, "iris") }.Validate(library);
    }

    private sealed class FaceDto
    {
        public Dictionary<string, Dictionary<string, float>?>? Parts { get; init; }
        public FaceParts? Values { get; init; }
        public float[]? Skin { get; init; }
        public float[]? Hair { get; init; }
        public float[]? Iris { get; init; }
    }
}
