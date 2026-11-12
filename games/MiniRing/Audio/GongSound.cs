namespace MiniRing.Audio;

/// <summary>
/// ラウンドのゴング（素材ファイルなし・合成）。打たれた金属の鐘: 倍音が整数倍に並ばない部分音（鐘の振動の形ごとの高さ）が、
/// 高いものほど早く減衰しながら鳴り続ける。基音のすぐ隣にもう 1 本を少しずらして置き、うなり（ゆらぎ）を出す。
/// 始まりは 1 回、終わり（K.O.）は続けて 3 回打つ（カンカンカン）。決まった音（種なしの乱数は使わない）。
/// </summary>
public static class GongSound
{
    public const int SampleRate = HitSound.SampleRate;

    /// <summary>基音 [Hz]。小さな卓上のリングの鐘より、会場の鐘の高さ（澄んで遠くまで通る）。</summary>
    private const float Fundamental = 760f;

    /// <summary>部分音: 基音に対する高さ・強さ・減衰の時定数 [s]（鐘の振動の形の比。高いほど早く消える）。</summary>
    private static readonly (float Ratio, float Amplitude, float Decay)[] Partials =
    [
        (1.000f, 1.00f, 2.6f),
        (1.004f, 0.55f, 2.4f), // 基音とのうなり（約 3 Hz）
        (2.000f, 0.35f, 1.4f),
        (2.760f, 0.60f, 1.1f),
        (4.070f, 0.30f, 0.6f),
        (5.400f, 0.22f, 0.4f),
        (6.800f, 0.12f, 0.25f),
    ];

    /// <summary>終わりのゴングを打つ間隔 [s]。</summary>
    private const float StrikeInterval = 0.3f;

    /// <summary>最後の打撃の後に鳴らしておく長さ [s]。</summary>
    private const float Ring = 3f;

    /// <summary>音量（砕ける音は 1 前後）。</summary>
    public const float Loudness = 0.7f;

    /// <summary>ゴングの音（16-bit モノラル）。<see cref="RoundBell.None"/> なら null。</summary>
    public static byte[]? Synthesize(RoundBell bell)
    {
        var strikes = bell switch
        {
            RoundBell.Start => 1,
            RoundBell.End => 3,
            _ => 0,
        };
        if (strikes == 0)
        {
            return null;
        }

        var samples = new float[(int)((((strikes - 1) * StrikeInterval) + Ring) * SampleRate)];
        for (var s = 0; s < strikes; s++)
        {
            Strike(samples, (int)(s * StrikeInterval * SampleRate));
        }

        HitSound.AddRoom(samples, 0.25f);
        return HitSound.ToPcm16(samples, Loudness);
    }

    /// <summary>1 回打つ: <paramref name="start"/> から部分音を足し込む。打った瞬間はわずかに遅れて立ち上がる（硬いハンマーの 2 ms）。</summary>
    private static void Strike(float[] samples, int start)
    {
        var attack = 0.002f * SampleRate;
        for (var n = start; n < samples.Length; n++)
        {
            var i = n - start;
            var t = i / (float)SampleRate;
            var onset = MathF.Min(1f, i / attack);
            var sum = 0f;
            foreach (var (ratio, amplitude, decay) in Partials)
            {
                sum += amplitude * MathF.Exp(-t / decay) * MathF.Sin(2f * MathF.PI * Fundamental * ratio * t);
            }

            samples[n] += onset * sum;
        }
    }
}
