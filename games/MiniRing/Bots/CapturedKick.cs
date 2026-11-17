using System.Numerics;
using System.Text.Json;
using MiniRing.Motion;

namespace MiniRing.Bots;

/// <summary>
/// 人の回し蹴りのモーションキャプチャ（<see cref="KickCapture"/>）から作った、膝の制御の蹴りの手本（2026-11-17）。蹴り 1 本 = 時間 0..1 に正規化した
/// 膝の方位・仰角（ミットの方向を基準に、股から腿の軸）・すねのねじり・膝の曲げ・骨盤の回り・胸のひねりの経路（右脚。左脚は鏡: ねじり・回りの符号を反転）。
/// 複数の蹴りは、当たる瞬間（足首がミットにいちばん近い）で時刻をそろえて（<see cref="ImpactShare"/> を平均した位置へ区間ごとに線形に伸縮）平均した。
/// 角は脚の長さに依らないので、人形の脚（<see cref="LegBody"/>）へそのまま当てはまる（膝の位置 = 股 + 腿の長さ × 向き）。時間は呼ぶ側が蹴りの長さへ伸縮する
/// （人の長さ <see cref="HumanSeconds"/> を、体の大きさの比の平方根で縮めるのが刀と同じ考え方）。威力の倍率・場合分けは無い。
/// </summary>
public sealed record CapturedKick(string Source, int Count, float HumanSeconds, float ImpactShare, float[] Azimuth, float[] Elevation, float[] Twist, float[] Bend,
    float[] Yaw, float[] Torso)
{
    private static float At(float[] series, float t)
    {
        var x = Math.Clamp(t, 0f, 1f) * (series.Length - 1);
        var i = Math.Min(series.Length - 2, (int)x);
        return series[i] + ((series[i + 1] - series[i]) * (x - i));
    }

    /// <summary>蹴りの終わりの割合: 人のデータの区間は戻りの途中で切れている（速さが 1 m/s を下回った所 + 0.15 秒）ので、最後のこの割合で、構えの形（膝・体の向き）へ戻す。</summary>
    public const float SettleShare = 0.15f;

    /// <summary>戻しの進み具合（0..1。<see cref="SettleShare"/> の間に滑らかに 0 → 1）。</summary>
    private static float Settle(float t)
    {
        var u = Math.Clamp((t - (1f - SettleShare)) / SettleShare, 0f, 1f);
        return u * u * (3f - (2f * u));
    }

    /// <summary>膝の曲げ [rad]（0 = 伸びきり）。</summary>
    public float BendAt(float t) => At(Bend, t);

    /// <summary>骨盤の回り（構えの向きからの変化。ミットの方向に対して左回りが正）[rad]。左脚は符号を反転。</summary>
    public float YawAt(Hand leg, float t) => -leg.LocalSide() * At(Yaw, t) * (1f - Settle(t));

    /// <summary>胸のひねり（骨盤に対する肩の回り。左回りが正）[rad]。左脚は符号を反転。</summary>
    public float TorsoAt(Hand leg, float t) => -leg.LocalSide() * At(Torso, t) * (1f - Settle(t));

    /// <summary>脚の指令。<paramref name="aim"/> は相手の狙い（体フレーム）、<paramref name="t"/> は 0..1。</summary>
    public LegCommand Evaluate(Hand leg, float t, Vector3 aim, LegBody body, float footPitch)
    {
        t = Math.Clamp(t, 0f, 1f);
        var side = leg.LocalSide();
        var mirror = -side; // 右脚（side −1）が +1
        var forward = new Vector3(aim.X - body.Hip.X, 0f, aim.Z - body.Hip.Z);
        forward = forward.LengthSquared() > 1e-8f ? Vector3.Normalize(forward) : Vector3.UnitZ;
        var left = Vector3.Cross(Vector3.UnitY, forward);
        var azimuth = At(Azimuth, t);
        var elevation = At(Elevation, t);
        var axis = Vector3.Normalize((forward * MathF.Cos(azimuth) * MathF.Cos(elevation)) + (left * side * MathF.Sin(azimuth) * MathF.Cos(elevation)) + (Vector3.UnitY * MathF.Sin(elevation)));
        // ねじりは相手の向きを基準に測った角（人のデータもそう）。体フレームの角へ直す（体が回っても、すねの曲がる面が同じ向きになる）。
        var normal = Vector3.Transform(LegKnee.BendNormalRef(axis, forward), Quaternion.CreateFromAxisAngle(axis, mirror * At(Twist, t)));
        float? twist = LegKnee.TwistOf(axis, Vector3.UnitZ, normal);
        var bend = BendAt(t);
        var settle = Settle(t);
        if (settle > 0f)
        {
            // 最後は構えの膝（立った形）へ戻し、ねじりはフリーにする。
            var rest = body.RestKnee - body.Hip;
            axis = Vector3.Normalize(Vector3.Lerp(axis, Vector3.Normalize(rest), settle));
            bend = bend + ((0.3f - bend) * settle);
            twist = settle > 0.5f ? null : twist;
        }

        return LegCommand.Knee(body.Hip + (axis * body.Thigh), twist, Math.Clamp(bend, 0f, LegIk.MaxKnee), Quaternion.CreateFromAxisAngle(Vector3.UnitX, footPitch));
    }

    /// <summary>蹴り（読み替えた Track）の平均から手本を作る。<paramref name="samples"/> は時間の刻み数。</summary>
    public static CapturedKick FromTracks(string source, IReadOnlyList<KickCapture.Track> tracks, int samples = 60)
    {
        var share = tracks.Average(t => t.ImpactIndex / (float)(t.Samples.Length - 1));
        float Map(KickCapture.Track track, float u)
        {
            var last = track.Samples.Length - 1;
            return u <= share ? track.ImpactIndex * (u / share) : track.ImpactIndex + ((last - track.ImpactIndex) * ((u - share) / (1f - share)));
        }

        float Value(KickCapture.Track track, float u, Func<KickCapture.Sample, float> get)
        {
            var f = Math.Clamp(Map(track, u), 0f, track.Samples.Length - 1);
            var i = Math.Min(track.Samples.Length - 2, (int)f);
            return get(track.Samples[i]) + ((get(track.Samples[i + 1]) - get(track.Samples[i])) * (f - i));
        }

        float[] Mean(Func<KickCapture.Sample, float> get, bool circular, bool fromStart)
            => Enumerable.Range(0, samples + 1).Select(k =>
            {
                var u = k / (float)samples;
                var values = tracks.Select(t => Value(t, u, get) - (fromStart ? get(t.Samples[0]) : 0f)).ToArray();
                return circular
                    ? MathF.Atan2(values.Average(MathF.Sin), values.Average(MathF.Cos))
                    : values.Average();
            }).ToArray();

        // 腿の向きは単位ベクトルで平均する（腿がほぼ真下のとき方位は定まらず、角のまま平均すると ±180° をまたいでずれる）。
        var azimuth = new float[samples + 1];
        var elevation = new float[samples + 1];
        for (var k = 0; k <= samples; k++)
        {
            var u = k / (float)samples;
            var sum = Vector3.Zero;
            foreach (var t in tracks)
            {
                var az = Value(t, u, s => s.Azimuth);
                var el = Value(t, u, s => s.Elevation);
                sum += new Vector3(MathF.Cos(el) * MathF.Cos(az), MathF.Cos(el) * MathF.Sin(az), MathF.Sin(el));
            }

            azimuth[k] = MathF.Atan2(sum.Y, sum.X);
            elevation[k] = MathF.Asin(Math.Clamp(sum.Z / MathF.Max(1e-6f, sum.Length()), -1f, 1f));
        }

        return new CapturedKick(source, tracks.Count, tracks.Average(t => t.Samples.Length / t.Rate), share,
            azimuth, elevation, Mean(s => s.Twist, true, false), Mean(s => s.Bend, false, false),
            Mean(s => s.PelvisYaw, true, true), Mean(s => s.ShoulderTwist, true, true));
    }

    private static readonly JsonSerializerOptions Options = new() { WriteIndented = false, IncludeFields = true };

    public string ToJson() => JsonSerializer.Serialize(this, Options);

    /// <summary>組み込みの手本（<c>Bots/kicks/&lt;name&gt;.json</c>）。</summary>
    public static CapturedKick Load(string name)
    {
        using var stream = typeof(CapturedKick).Assembly.GetManifestResourceStream($"MiniRing.Bots.kicks.{name}.json")
            ?? throw new FileNotFoundException($"MiniRing.Bots.kicks.{name}.json");
        return JsonSerializer.Deserialize<CapturedKick>(stream, Options) ?? throw new InvalidDataException(name);
    }
}
