namespace MiniRing.Bots;

/// <summary>
/// ボットのリングネーム。名簿のスタイルにも、ドリル（スタイルの改良）の中で生まれた個体（"champion-g3c2" のような名前）にも、
/// その名前から決まったリングネームを付ける（同じ名前ならいつも同じリングネーム。種なしの乱数は使わない）。
/// スタイルの JSON に <see cref="BotStyle.RingName"/> があればそれを使う。顔（<see cref="Voxels.FaceLook"/>）もリングネームから決まる。
/// </summary>
public static class BotNames
{
    private static readonly string[] Given =
    [
        "ケン", "リュウ", "ジョー", "タイガ", "ショウ", "ダイキ", "レン", "ハヤト",
        "ゴウ", "カイ", "ジン", "トウマ", "ユウマ", "ライ", "セナ", "ミツル",
        "テツ", "ガク", "シン", "アラタ",
    ];

    private static readonly string[] Family =
    [
        "アカギ", "クロガネ", "ミナカタ", "サイジョウ", "カミシロ", "オオトリ", "ハヤセ", "キリュウ",
        "シラヌイ", "タカツキ", "ヒムロ", "カゲヤマ", "イブキ", "ツルギ", "マガミ", "ホムラ",
        "アマギ", "ライドウ", "コクブ", "ジングウジ",
    ];

    /// <summary>名前（スタイルの名前・ドリルの個体の名前）から決まるリングネーム（「ケン・アカギ」の形）。</summary>
    public static string Generate(string id)
    {
        ArgumentNullException.ThrowIfNull(id);
        var h = Hash(id);
        return $"{Given[h % (uint)Given.Length]}・{Family[(h / (uint)Given.Length) % (uint)Family.Length]}";
    }

    /// <summary>名前のボットのリングネーム: 名簿にあってリングネームが決めてあればそれ、無ければ <see cref="Generate"/>。</summary>
    public static string RingNameOf(string id)
    {
        ArgumentNullException.ThrowIfNull(id);
        var style = BotCatalog.Names.Contains(id) && id != BotCatalog.Legacy ? BotCatalog.Style(id) : null;
        return style?.RingName is { Length: > 0 } fixedName ? fixedName : Generate(id);
    }

    /// <summary>文字列の 32 ビットの FNV-1a（決まった値。顔の作りにも使う）。</summary>
    public static uint Hash(string text)
    {
        var h = 2166136261u;
        foreach (var c in text)
        {
            h = unchecked((h ^ c) * 16777619u);
        }

        return h;
    }
}
