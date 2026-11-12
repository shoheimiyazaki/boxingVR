namespace MiniRing.Audio;

/// <summary>
/// かすりの音（素材ファイルなし・合成）。グローブが皮膚だけをこすった、または拳が紙一重で外れた「シュフッ」:
/// 帯域を絞ったノイズの短い擦れに、皮膚の摩擦のざらつき（不規則な音量の震え）を乗せる。速いほど少し明るく・短い。
/// 打撃音の下に隠れるよう、打撃音（0.35〜0.95）や風切り音（<see cref="SwingSound.Loudness"/>）より小さく鳴らす。
/// 同じ速さ・同じ種なら同じ音（決定的）。
/// </summary>
public static class GrazeSound
{
    public const int SampleRate = HitSound.SampleRate;

    /// <summary>いちばん速いかすりの音量（打撃音の最小 0.35・風切り音 0.12 より小さい）。</summary>
    public const float Loudness = 0.09f;

    /// <summary>この速さで最も明るく大きい [m/s]（パンチの速い所）。</summary>
    private const float FullSpeed = 4f;

    /// <summary>遅いかすり（0）と速いかすり（1）の長さ [s]。</summary>
    private const float SlowLength = 0.14f;
    private const float FastLength = 0.06f;

    /// <summary>1 回のかすりの音（16-bit モノラル）。<paramref name="speed"/> はこすった速さ [m/s]。</summary>
    public static byte[] Synthesize(float speed, int seed)
    {
        var strength = Math.Clamp(speed / FullSpeed, 0f, 1f);
        var random = new Random(seed);
        var length = (int)(SampleRate * Lerp(SlowLength, FastLength, strength));
        var samples = new float[length];

        // 擦れの中心の高さ: 速いほど明るい。少しずつ下がる（離れていく）。
        var center = Lerp(1600f, 3400f, strength) * (0.9f + (0.2f * random.NextSingle()));
        // ざらつき: 皮膚が引っかかっては滑る（60〜140 Hz の不規則な震え）。
        var flutterAlpha = OnePole(Lerp(60f, 140f, strength));
        var highAlpha = OnePole(500f);
        float x1 = 0f, x2 = 0f, y1 = 0f, y2 = 0f, flutter = 0f, flutterTarget = 1f, highLow = 0f;
        var nextGrain = 0;
        for (var n = 0; n < length; n++)
        {
            var t = n / (float)length;
            // 立ち上がり 10%、そのあとなめらかに消える。
            var envelope = t < 0.1f ? t / 0.1f : MathF.Pow(1f - ((t - 0.1f) / 0.9f), 1.6f);

            if (n >= nextGrain)
            {
                flutterTarget = 0.25f + (0.75f * random.NextSingle());
                nextGrain = n + (int)(SampleRate * (0.003f + (0.006f * random.NextSingle())));
            }

            flutter += flutterAlpha * (flutterTarget - flutter);

            var frequency = center * (1.05f - (0.15f * t));
            var r = 0.9f;
            var a1 = 2f * r * MathF.Cos(2f * MathF.PI * frequency / SampleRate);
            var a2 = -r * r;
            var x = (random.NextSingle() * 2f) - 1f;
            // 分子の零点（x − x[n−2]）で直流を落とす: ただの 2 極では低域が中心とほぼ同じ強さで残り、ゴロッと濁る。
            var y = (x - x2) + (a1 * y1) + (a2 * y2);
            x2 = x1;
            x1 = x;
            y2 = y1;
            y1 = y;

            // 皮膚の引っかかり: 低域を抜いた生のノイズを少し（ザラッ）。
            highLow += highAlpha * (x - highLow);
            var rasp = (x - highLow) * 0.25f * strength;

            samples[n] = ((y * 0.2f) + rasp) * flutter * envelope;
        }

        return HitSound.ToPcm16(samples, Loudness * (0.55f + (0.45f * strength)));
    }

    private static float OnePole(float cutoffHz) => 1f - MathF.Exp(-2f * MathF.PI * cutoffHz / SampleRate);

    private static float Lerp(float a, float b, float t) => a + ((b - a) * Math.Clamp(t, 0f, 1f));
}
