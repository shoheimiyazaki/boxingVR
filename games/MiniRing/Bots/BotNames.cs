namespace MiniRing.Bots;

/// <summary>
/// ボットのリングネーム。名簿のスタイルにも、ドリル（スタイルの改良）の中で生まれた個体（"champion-g3c2" のような名前）にも、
/// その名前から決まったリングネームを付ける（同じ名前ならいつも同じリングネーム。種なしの乱数は使わない）。
/// スタイルの JSON に <see cref="BotStyle.RingName"/> があればそれを使う。顔（<see cref="Voxels.FaceLook"/>）もリングネームから決まる。
/// </summary>
public static class BotNames
{
    /// <summary>
    /// リングネームの名前（2026-11-12、ユーザーの指定: ファーストネームだけ、1960 年代の名前）。1960 年代に生まれた男の子に多かった名前
    /// （明治安田生命の名前ランキングの上位に並ぶ、浩・誠・隆・健一・博・茂・勝・修・進 などの読み）をカタカナで。
    /// </summary>
    private static readonly string[] Given =
    [
        "ヒロシ", "マコト", "タカシ", "ヨシオ", "カズオ", "ススム", "ツヨシ", "イサム",
        "ミノル", "ノボル", "ケンイチ", "シゲル", "アキラ", "マサオ", "テツヤ", "オサム",
        "トオル", "ユタカ", "サトル", "ケンジ", "タケシ", "コウジ", "ヨウイチ", "ヒデオ",
        "マサル", "ツトム", "カツミ", "ミツオ", "セイジ", "ノリオ",
    ];

    /// <summary>名前（スタイルの名前・ドリルの個体の名前）から決まるリングネーム（ファーストネームだけ: 「ヒロシ」）。</summary>
    public static string Generate(string id)
    {
        ArgumentNullException.ThrowIfNull(id);
        return Given[Hash(id) % (uint)Given.Length];
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
