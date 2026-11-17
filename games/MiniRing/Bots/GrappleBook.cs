using System.Numerics;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace MiniRing.Bots;

/// <summary>
/// 組み技（投げ・関節技）の教科書（docs/GRAPPLING.md）。技は両手・片足の目標の経路で、点は相手の体の見える所
/// （<see cref="GrappleAnchor"/>）を基準にする。威力・倒れるかどうかは書かない: 引き・押し・刈りの結果、相手の重心が足の支えの外へ出て
/// 踏み直しが間に合わなければ倒れ（Fighter.Support.cs）、肘を反らせる梃子が靭帯の降伏を超えれば靭帯が伸び・切れる（Fighter.Ligaments.cs）。
/// 長さは自分の腕の長さ = 1。左右は自分（取り）から見た左右（相手の右腕は取りの左に見える）。
/// </summary>
public sealed record GrappleBook
{
    public string Name { get; init; } = "jujutsu";

    /// <summary>組みに行く相手との距離（胸と胸、腕の長さ = 1）。</summary>
    public float Engage { get; init; } = 0.9f;

    /// <summary>組みに行ける間合いにいる 1 秒あたりに、組みに行く確率（試合での手数。打撃の教科書のボットの合間に）。</summary>
    public float Rate { get; init; } = 0.8f;

    /// <summary>掴みに行ってから、掴めていなければあきらめる時間 [s]。</summary>
    public float GripTimeout { get; init; } = 0.5f;

    /// <summary>技の後、次に組みに行くまでの間 [s]。</summary>
    public float Cooldown { get; init; } = 1.2f;

    /// <summary>
    /// 自分の肘が極められている痛み（過伸展の力 / 靭帯の降伏）がこれを超えたら、極められた腕の拳を相手の引く向きへ預け、体ごと寄って荷重を逃がす（反射。0 なら逃げない）。
    /// </summary>
    public float EscapeLoad { get; init; } = 0.6f;

    public IReadOnlyList<GrappleTechnique> Techniques { get; init; } = [];

    private static readonly JsonSerializerOptions Options = new()
    {
        PropertyNameCaseInsensitive = true,
        ReadCommentHandling = JsonCommentHandling.Skip,
        AllowTrailingCommas = true,
        Converters = { new JsonStringEnumConverter() },
    };

    public static GrappleBook FromJson(string json)
        => JsonSerializer.Deserialize<GrappleBook>(json, Options) ?? throw new InvalidDataException("empty grapple book");

    public string ToJson() => JsonSerializer.Serialize(this, Options);

    /// <summary>組み込みの柔術の教科書（Bots/grapple/jujutsu.json）。</summary>
    public static GrappleBook Jujutsu { get; } = LoadEmbedded("MiniRing.Bots.grapple.jujutsu.json");

    public static GrappleBook? Named(string? name) => name switch
    {
        null or "" => null,
        "jujutsu" => Jujutsu,
        _ => throw new ArgumentException($"unknown grapple book '{name}'", nameof(name)),
    };

    private static GrappleBook LoadEmbedded(string resource)
    {
        using var stream = typeof(GrappleBook).Assembly.GetManifestResourceStream(resource)
            ?? throw new InvalidOperationException($"missing embedded {resource}");
        using var reader = new StreamReader(stream);
        return FromJson(reader.ReadToEnd());
    }

    public GrappleTechnique Technique(string name)
        => Techniques.FirstOrDefault(t => t.Name == name) ?? throw new ArgumentException($"no technique '{name}' in {Name}", nameof(name));
}

/// <summary>
/// 技 1 つ: 時刻つきのキーの列（キーの間は点を直線で補う。掴む・離すはキーの時刻で切り替わる）。
/// <paramref name="Kuzushi"/> は崩す向き（自分の体の座標、水平。+Z が前 = 相手を押す、−Z が手前へ引く、+X が自分の左）。
/// 選ぶときは、相手の支えがいちばん狭い向きと崩す向きの合い具合で重みを掛ける（技の名前では分けない）。
/// <paramref name="Weight"/> は教科書の好み。<paramref name="Extension"/> は相手の腕の伸びを求める度合い（0 = 問わない、1 = 伸びきった腕だけ。肘の関節技は 1）。
/// </summary>
public sealed record GrappleTechnique(string Name, float Weight, float[] Kuzushi, IReadOnlyList<GrappleKey> Keys, float Extension = 0f)
{
    public float Duration => Keys.Count == 0 ? 0f : Keys[^1].T;

    public Vector2 KuzushiLocal => Kuzushi is { Length: >= 2 } k ? new Vector2(k[0], k[^1]) : Vector2.Zero;
}

/// <summary>
/// 経路のキー: 時刻 <paramref name="T"/> [s] での両手・足・頭・移動の目標。null の手は、その手を前のキーのまま（最初なら構え）。
/// <paramref name="Move"/> は自分の体の座標での移動の向き（x = 左、z = 前。歩く速さ = 1）。<paramref name="Head"/> は頭のずれ（腕の長さ = 1）。
/// <paramref name="Spring"/> は体のバネを拳へ移す割合（引き・押しに体を乗せる）。
/// </summary>
/// <paramref name="Turn"/> は体の向き: 相手へ向く向きから、上から見て左回りに何ラジアン回るか（π で相手に背を向ける）。
/// <paramref name="Stand"/> は自分の立ち位置の目標（相手の点 ＋ 自分の体の座標のずれ、腕の長さ = 1）: あれば <paramref name="Move"/> の代わりに、
/// 自分の肩の真ん中（水平）がそこへ向かうように歩く（背中を相手のお腹に付ける、など）。
public sealed record GrappleKey(float T, GrappleHand? Left = null, GrappleHand? Right = null, GrappleLeg? Leg = null,
    float[]? Move = null, float[]? Head = null, float Spring = 0f, float Twist = 0f, float Turn = 0f, GrappleHand? Stand = null);

/// <summary>
/// 手の目標: 相手の点 <paramref name="Anchor"/> から <paramref name="Offset"/>（腕の長さ = 1）ずらした所。<paramref name="Limb"/> なら
/// ずれは相手の腕の座標（x = 肩 → 手首の向き、y = 肘を伸ばしきった先へ反らせる向き、z = その 2 つに直交）、でなければ自分の体の座標。
/// <paramref name="Grip"/> なら握る（4 cm 以内に相手の体があれば掴む）。
/// </summary>
public sealed record GrappleHand(GrappleAnchor Anchor, float[]? Offset = null, bool Grip = false, bool Limb = false);

/// <summary>
/// 足の目標（片足）: <paramref name="Side"/> の足首を、相手の点 <paramref name="Anchor"/> から <paramref name="Offset"/>（自分の体の座標、腕の長さ = 1）の所へ。
/// 刈る・払うは、相手の足首・脛を目標が通り抜ける経路として書く（当たって押すのは蹴りと同じ接触の物理）。
/// </summary>
public sealed record GrappleLeg(Hand Side, GrappleAnchor Anchor, float[]? Offset = null);

/// <summary>
/// 経路の点の基準: 相手の体の見える点。左右（L / R）は自分（取り）から見た左右で、相手の本人の左右ではない
/// （向かい合った相手の右腕は取りの左に見える）。<see cref="Self"/> は自分の体の座標の原点（胸の高さの足元ではなく、体の座標そのもの）。
/// </summary>
public enum GrappleAnchor
{
    Self,
    Chest,
    Head,
    Pelvis,
    ShoulderL,
    ShoulderR,
    ElbowL,
    ElbowR,
    WristL,
    WristR,
    AnkleL,
    AnkleR,
}
