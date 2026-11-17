using System.Text.Json;

namespace MiniRing.Bots;

/// <summary>
/// 人の蹴りの手本（<see cref="CapturedKick"/>）の上に乗せる練習の差分（2026-11-17、docs/arena/kick_shin_lmh_*.md）。手本の経路は変えず、
/// 当たる瞬間のまわり（<see cref="CapturedKick.TuneWindow"/>）で腿の向きをずらし（<see cref="AzimuthShift"/>・<see cref="ElevationShift"/> [rad]。膝の高さは腿の仰角で決まる）、
/// 膝の曲げの経路を時間でずらす（<see cref="BendShift"/>: 時間の割合。正で膝が開く拍子が遅れる）。<see cref="Standoff"/>・<see cref="Seconds"/>・<see cref="FootPitch"/> は
/// 蹴りの間合い（腕の長さの倍数）・長さ・足首の曲げ。威力の倍率は無く、場合分けも無い（値が違うだけ。1 つの式を通る）。
/// </summary>
public sealed record KickTune(float Standoff, float Seconds, float AzimuthShift, float ElevationShift, float BendShift, float FootPitch);

/// <summary>
/// 脚ごとの練習の差分。蹴る脚が前の足か後ろの足かで、体の回り方（軸足が前か後ろか）が違い、同じ手本・同じ間合いでも当たる深さが違う
/// （左右の差の原因。docs/arena/kick_shin_lmh_cause.md）ので、前の足と後ろの足（構えの前の手の側を前とする）それぞれに持つ。
/// ボットは後ろの足で蹴る（<see cref="BoxerBot"/>）。
/// </summary>
public sealed record KickTunes(KickTune Rear, KickTune Front)
{
    private static readonly JsonSerializerOptions Options = new() { WriteIndented = true, IncludeFields = true };

    /// <summary>蹴る脚 <paramref name="leg"/> の差分。<paramref name="lead"/> は構えの前の手の側（null は左が前）。前の手の側の脚が前の足。</summary>
    public KickTune For(Hand leg, Hand? lead = null) => leg == (lead ?? Hand.Left) ? Front : Rear;

    public string ToJson() => JsonSerializer.Serialize(this, Options);

    /// <summary>組み込みの差分（<c>Bots/kicks/&lt;name&gt;.tune.json</c>）。</summary>
    public static KickTunes Load(string name)
    {
        using var stream = typeof(KickTunes).Assembly.GetManifestResourceStream($"MiniRing.Bots.kicks.{name}.tune.json")
            ?? throw new FileNotFoundException($"MiniRing.Bots.kicks.{name}.tune.json");
        return JsonSerializer.Deserialize<KickTunes>(stream, Options) ?? throw new InvalidDataException(name);
    }
}
