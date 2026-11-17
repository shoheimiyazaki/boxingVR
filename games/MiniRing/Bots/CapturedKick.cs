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

    /// <summary>角の系列の補間。±π をまたぐ 2 点の間は短い方へ回す（線形補間だと 0 を通ってしまう）。</summary>
    private static float AtAngle(float[] series, float t)
    {
        var x = Math.Clamp(t, 0f, 1f) * (series.Length - 1);
        var i = Math.Min(series.Length - 2, (int)x);
        return series[i] + (LegKnee.Wrap(series[i + 1] - series[i]) * (x - i));
    }

    /// <summary>当たる瞬間のまわりの窓（0..1）: 練習の差分（<see cref="KickTune"/>）を、当たる瞬間を中心に滑らかに掛ける。幅は当たる瞬間の前後の時間の割合。</summary>
    public const float TuneWindow = 0.2f;

    private float TuneBump(float t)
    {
        var u = Math.Clamp(((t - ImpactShare) / TuneWindow), -1f, 1f);
        return 0.5f * (1f + MathF.Cos(MathF.PI * u));
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
    public float YawAt(Hand leg, float t) => -leg.LocalSide() * AtAngle(Yaw, t) * (1f - Settle(t));

    /// <summary>胸のひねり（骨盤に対する肩の回り。左回りが正）[rad]。左脚は符号を反転。</summary>
    public float TorsoAt(Hand leg, float t) => -leg.LocalSide() * AtAngle(Torso, t) * (1f - Settle(t));

    /// <summary>脚の指令。<paramref name="aim"/> は相手の狙い（体フレーム）、<paramref name="t"/> は 0..1。</summary>
    public LegCommand Evaluate(Hand leg, float t, Vector3 aim, LegBody body, float footPitch, KickTune? tune = null)
    {
        t = Math.Clamp(t, 0f, 1f);
        var side = leg.LocalSide();
        var mirror = -side; // 右脚（side −1）が +1
        var forward = new Vector3(aim.X - body.Hip.X, 0f, aim.Z - body.Hip.Z);
        forward = forward.LengthSquared() > 1e-8f ? Vector3.Normalize(forward) : Vector3.UnitZ;
        var left = Vector3.Cross(Vector3.UnitY, forward);
        var bump = tune is null ? 0f : TuneBump(t);
        var azimuth = AtAngle(Azimuth, t) + (bump * (tune?.AzimuthShift ?? 0f));
        var elevation = Math.Clamp(At(Elevation, t) + (bump * (tune?.ElevationShift ?? 0f)), -1.5f, 1.5f);
        var axis = Vector3.Normalize((forward * MathF.Cos(azimuth) * MathF.Cos(elevation)) + (left * side * MathF.Sin(azimuth) * MathF.Cos(elevation)) + (Vector3.UnitY * MathF.Sin(elevation)));
        // ねじりは相手の向きを基準に測った角（人のデータもそう）。体フレームの角へ直す（体が回っても、すねの曲がる面が同じ向きになる）。
        var normal = Vector3.Transform(LegKnee.BendNormalRef(axis, forward), Quaternion.CreateFromAxisAngle(axis, mirror * AtAngle(Twist, t)));
        float? twist = LegKnee.TwistOf(axis, Vector3.UnitZ, normal);
        var bend = At(Bend, t - (tune?.BendShift ?? 0f));
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

    /// <summary>
    /// 2 つの手本（<paramref name="low"/> と <paramref name="high"/>）を割合 <paramref name="share"/>（0 = low、1 = high）で混ぜる。人のデータに中段の廻し蹴りが無いので、
    /// 下段と上段の間の高さの蹴りは、2 つを高さの割合で混ぜて作る。当たる瞬間を合わせた時間（割合の平均）に両方を伸縮し、腿の向きは単位ベクトルの球面補間、
    /// ねじり・骨盤の回り・胸のひねりは角の補間（短い方へ）、曲げは線形。
    /// </summary>
    public static CapturedKick Blend(string source, CapturedKick low, CapturedKick high, float share, int samples = 60)
    {
        share = Math.Clamp(share, 0f, 1f);
        var impact = low.ImpactShare + ((high.ImpactShare - low.ImpactShare) * share);
        static float Source(CapturedKick k, float u, float target)
            => u <= target ? u / target * k.ImpactShare : k.ImpactShare + ((u - target) / (1f - target) * (1f - k.ImpactShare));

        float Angle(Func<CapturedKick, float[]> pick, float u)
        {
            var a = AtAngle(pick(low), Source(low, u, impact));
            var b = AtAngle(pick(high), Source(high, u, impact));
            return a + (LegKnee.Wrap(b - a) * share);
        }

        float Linear(Func<CapturedKick, float[]> pick, float u)
        {
            var a = At(pick(low), Source(low, u, impact));
            var b = At(pick(high), Source(high, u, impact));
            return a + ((b - a) * share);
        }

        static Vector3 Direction(CapturedKick k, float u, float target)
        {
            var az = AtAngle(k.Azimuth, Source(k, u, target));
            var el = At(k.Elevation, Source(k, u, target));
            return new Vector3(MathF.Cos(el) * MathF.Cos(az), MathF.Cos(el) * MathF.Sin(az), MathF.Sin(el));
        }

        var azimuth = new float[samples + 1];
        var elevation = new float[samples + 1];
        for (var k = 0; k <= samples; k++)
        {
            var u = k / (float)samples;
            var a = Direction(low, u, impact);
            var b = Direction(high, u, impact);
            var dot = Math.Clamp(Vector3.Dot(a, b), -1f, 1f);
            var omega = MathF.Acos(dot);
            var v = omega < 1e-4f ? a : ((MathF.Sin((1f - share) * omega) * a) + (MathF.Sin(share * omega) * b)) / MathF.Sin(omega);
            azimuth[k] = MathF.Atan2(v.Y, v.X);
            elevation[k] = MathF.Asin(Math.Clamp(v.Z / MathF.Max(1e-6f, v.Length()), -1f, 1f));
        }

        float[] Series(Func<float, float> f) => Enumerable.Range(0, samples + 1).Select(k => f(k / (float)samples)).ToArray();
        return new CapturedKick(source, low.Count + high.Count, low.HumanSeconds + ((high.HumanSeconds - low.HumanSeconds) * share), impact, azimuth, elevation,
            Series(u => Angle(c => c.Twist, u)), Series(u => Linear(c => c.Bend, u)), Series(u => Angle(c => c.Yaw, u)), Series(u => Angle(c => c.Torso, u)));
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
