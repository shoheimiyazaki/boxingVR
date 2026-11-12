namespace MiniRing.Audio;

/// <summary>
/// ラウンドのゴング（素材ファイルなし・合成）。打たれた金属の鐘: 倍音が整数倍に並ばない部分音（鐘の振動の形ごとの高さ）が、
/// 高いものほど早く減衰しながら鳴り続ける。近い 2 本の部分音（鐘のわずかな歪みで振動の形が 2 つに分かれたもの）がうなりを出す。
/// 始まりは 1 回、終わり（K.O.）は続けて 3 回打つ（カンカンカン）。決まった音（種なしの乱数は使わない）。
/// <para><b>音程は実物の録音から測った</b>（2026-11-12）: BigSoundBank の「Boxing bell #1〜#3」（同じ鐘の 3 録音）をブラウザで FFT し、
/// 3 つとも同じ部分音 約 870 / 1628・1645 / 2552・2571 / 3612 / 4770・4802 / 6067 Hz が立っていた（基音に対する比 1 : 1.87 : 2.94 : 4.15 : 5.5 : 7.0、
/// 明るい金属の鐘）。強さは 3 録音の平均、減衰は #1 の 2 つの時刻の窓の比から（測れない長いものは 3.5 秒に抑えた）。打つ間隔は #1 の 2 打の間隔 0.25 秒。
/// 録音そのものは使っていない（音は合成）。</para>
/// </summary>
public static class GongSound
{
    public const int SampleRate = HitSound.SampleRate;

    /// <summary>部分音: 高さ [Hz]・強さ・減衰の時定数 [s]（実物の鐘の録音から測った値）。</summary>
    private static readonly (float Hz, float Amplitude, float Decay)[] Partials =
    [
        (870f, 0.12f, 3.1f),
        (1628f, 0.30f, 3.0f),
        (1645f, 0.15f, 1.2f), // 1628 Hz とのうなり（約 17 Hz: 鐘の「ジーン」というざらつき）
        (2552f, 0.25f, 0.75f),
        (2571f, 0.60f, 2.5f),
        (3612f, 0.80f, 1.0f),
        (4770f, 0.35f, 0.24f),
        (4802f, 0.40f, 0.46f),
        (6067f, 0.10f, 0.08f),
    ];

    /// <summary>終わりのゴングを打つ間隔 [s]（録音の 2 打の間隔）。</summary>
    private const float StrikeInterval = 0.25f;

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
            foreach (var (hz, amplitude, decay) in Partials)
            {
                sum += amplitude * MathF.Exp(-t / decay) * MathF.Sin(2f * MathF.PI * hz * t);
            }

            samples[n] += onset * sum;
        }
    }
}
