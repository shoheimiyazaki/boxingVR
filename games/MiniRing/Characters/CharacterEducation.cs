using System.Text.Json;
using System.Text.Json.Nodes;
using MiniRing.Bots;

namespace MiniRing.Characters;

/// <summary>
/// キャラクターの教育（CLAUDE.md §1.8）: 教科書（スタイル JSON）からの差分だけ。空なら教科書そのもの（今と同じ試合）。
/// 値は <see cref="BotStyle.ToJson"/> が出す JSON と同じ書き方（列挙体は数、書いた鍵だけが教科書を上書きする）。
/// </summary>
/// <param name="Style">この人の全試合に効く上書き。</param>
/// <param name="Vs">相手のキャラクター id → その相手との試合だけに効く上書き（<see cref="Style"/> の上に重ねる）。</param>
/// <param name="WeaponBot">握る武器（刀・ロングソード・こんぼう）のボットの判断の値の上書き。<see cref="WeaponStyle"/> の JSON と同じ書き方で、書いた鍵だけが武器の教科書（<see cref="WeaponCatalog"/>）を上書きする（<see cref="WeaponEducation"/>）。<see cref="Vs"/> のような相手ごとの層は無い。</param>
public sealed record CharacterEducation(JsonObject Style, IReadOnlyDictionary<string, JsonObject> Vs, JsonObject? WeaponBot)
{
    /// <summary>何も教えていない。</summary>
    public static CharacterEducation Empty { get; } = new(new JsonObject(), new Dictionary<string, JsonObject>(), null);

    /// <summary>この相手との試合に重なる層が 1 つも無い（教科書そのもので戦う）。</summary>
    public bool IsEmptyFor(string? opponentId)
        => Style.Count == 0 && (opponentId is null || !Vs.TryGetValue(opponentId, out var layer) || layer.Count == 0);
}

/// <summary>
/// 教育の解決: 教科書の JSON → <see cref="CharacterEducation.Style"/> → <see cref="CharacterEducation.Vs"/>[相手] の順に JSON の段で重ね
/// （オブジェクトは鍵ごとに再帰で重ね、配列・値は置き換え）、<see cref="BotStyle"/> に読み直して <see cref="BotStyle.Validated"/> を通す。
/// <para>
/// 結果の <see cref="BotStyle.Name"/> は教科書の名前のまま（空手の判定など、名前で見ている所が壊れないように）。
/// 体（<c>Build</c>・<c>Face</c>）と名前（<c>Name</c>・<c>RingName</c>）は教育で変えられない（体の強さは見えている体から出す。§1.7）。
/// 綴りの誤りを黙って無視しない: 最上位の鍵が教科書の JSON に無ければ止め、入れ子の鍵は読み直しで知らない名前を断る。
/// </para>
/// </summary>
public static class EducationResolver
{
    private static readonly string[] Locked = ["Name", "RingName", "Build", "Face"];

    private static readonly JsonSerializerOptions Strict = new() { UnmappedMemberHandling = System.Text.Json.Serialization.JsonUnmappedMemberHandling.Disallow };

    /// <summary>
    /// <paramref name="textbook"/> に教育を重ねたスタイル。重なる層が無ければ <paramref name="textbook"/> そのもの（同じ物）を返す。
    /// 壊れた教育は <see cref="InvalidDataException"/>（どの層のどの鍵かを書く）。
    /// </summary>
    public static BotStyle Resolve(BotStyle textbook, CharacterEducation education, string? opponentId)
    {
        ArgumentNullException.ThrowIfNull(textbook);
        ArgumentNullException.ThrowIfNull(education);
        var layers = new List<(string Label, JsonObject Layer)>();
        if (education.Style.Count > 0)
        {
            layers.Add(("education.style", education.Style));
        }

        if (opponentId is not null && education.Vs.TryGetValue(opponentId, out var versus) && versus.Count > 0)
        {
            layers.Add(($"education.vs.{opponentId}", versus));
        }

        if (layers.Count == 0)
        {
            return textbook;
        }

        var node = JsonNode.Parse(textbook.ToJson())!.AsObject();
        foreach (var (label, layer) in layers)
        {
            CheckKeys(node, layer, label);
            Merge(node, layer);
        }

        try
        {
            var parsed = JsonSerializer.Deserialize<BotStyle>(node.ToJsonString(), Strict) ?? throw new InvalidDataException("空のスタイルになりました。");
            return (parsed with { Name = textbook.Name }).Validated();
        }
        catch (Exception ex) when (ex is JsonException or ArgumentException)
        {
            throw new InvalidDataException($"教育（{string.Join(" → ", layers.Select(l => l.Label))}）を教科書 '{textbook.Name}' に重ねられません: {ex.Message}", ex);
        }
    }

    private static void CheckKeys(JsonObject textbook, JsonObject layer, string label)
    {
        foreach (var key in layer.Select(p => p.Key))
        {
            if (Locked.Contains(key))
            {
                throw new InvalidDataException($"{label}.{key}: 名前と体は教育で変えられません（{string.Join("・", Locked)}）。");
            }

            if (!textbook.ContainsKey(key))
            {
                throw new InvalidDataException($"{label}.{key}: 教科書のスタイルにこの鍵はありません（綴りの誤りかもしれません）。");
            }
        }
    }

    internal static void Merge(JsonObject target, JsonObject layer)
    {
        foreach (var (key, value) in layer)
        {
            if (value is JsonObject over && target[key] is JsonObject under)
            {
                Merge(under, over);
            }
            else
            {
                target[key] = value?.DeepClone();
            }
        }
    }
}
