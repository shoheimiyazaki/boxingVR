namespace MiniRing.Audio;

/// <summary>
/// 部位が断たれる音（素材ファイルなし・合成）。3 つの層:
/// <list type="bullet">
/// <item>バキッ: 骨が折れる、ごく短い広帯域の破裂。</item>
/// <item>パチッ: 破裂のすぐ後の、共鳴する短いクリック 2〜3 個（骨の破片が割れ離れる）。</item>
/// <item>ブチブチ: 濡れた肉と皮が裂ける尾（帯域を揺らすフィルタ済みのノイズに、ちぎれる粒を散らす。150〜300 ms）。</item>
/// </list>
/// 落ちた部位が床に当たる音は別（落ちた時に鳴らす）。同じ強さ・同じ種なら同じ音（決定的）。
/// </summary>
public static class SeverSound
{
    public const int SampleRate = HitSound.SampleRate;

    /// <summary>弱い断裂（0）と強い断裂（1）の音量（砕ける音は 1 前後）。</summary>
    private const float QuietLoudness = 0.8f;
    private const float LoudLoudness = 0.95f;

    /// <summary>1 回の断裂の音（16-bit モノラル）。<paramref name="strength01"/> は断った打撃の強さ 0..1。</summary>
    public static byte[] Synthesize(float strength01, int seed)
    {
        var strength = Math.Clamp(strength01, 0f, 1f);
        var random = new Random(seed);
        var tear = Lerp(0.15f, 0.3f, strength) * (0.9f + (0.2f * random.NextSingle()));
        var length = (int)(SampleRate * (0.03f + tear));
        var samples = new float[length];

        AddCrack(samples, strength, random);
        AddClicks(samples, strength, random);
        AddTear(samples, (int)(0.008f * SampleRate), (int)(tear * SampleRate), strength, random);

        HitSound.AddRoom(samples, 0.12f);
        return HitSound.ToPcm16(samples, Lerp(QuietLoudness, LoudLoudness, strength));
    }

    /// <summary>骨の破裂: 高域寄りの広帯域ノイズが 1〜2 ms で消える。強いほど太い（低域も残す）。</summary>
    private static void AddCrack(float[] samples, float strength, Random random)
    {
        var decay = Lerp(0.0012f, 0.0025f, strength) * SampleRate;
        var count = Math.Min(samples.Length, (int)(decay * 8f));
        var lowAlpha = OnePole(Lerp(1200f, 600f, strength));
        var low = 0f;
        for (var n = 0; n < count; n++)
        {
            var x = (random.NextSingle() * 2f) - 1f;
            low += lowAlpha * (x - low);
            var crack = (x - (low * (1f - (0.5f * strength)))) * MathF.Exp(-n / decay);
            samples[n] += crack;
        }
    }

    /// <summary>割れ離れる破片のクリック: 減衰の速い正弦（1.5〜5 kHz）を数 ms ずつずらして 2〜3 個。</summary>
    private static void AddClicks(float[] samples, float strength, Random random)
    {
        var clicks = 2 + random.Next(2);
        for (var c = 0; c < clicks; c++)
        {
            var start = (int)(SampleRate * (0.0005f + (0.007f * random.NextSingle())));
            var hz = 1500f + (3500f * random.NextSingle());
            var decay = (0.004f + (0.008f * random.NextSingle())) * SampleRate;
            var gain = (0.35f + (0.35f * random.NextSingle())) * Lerp(0.7f, 1f, strength);
            var end = Math.Min(samples.Length, start + (int)(decay * 6f));
            for (var n = start; n < end; n++)
            {
                var i = n - start;
                samples[n] += MathF.Sin(2f * MathF.PI * hz * i / SampleRate) * MathF.Exp(-i / decay) * gain;
            }
        }
    }

    /// <summary>
    /// 裂ける尾: 帯域（400〜1400 Hz をゆらゆら動く 2 極の共鳴）を通したノイズに、ちぎれる粒（短い音量の跳ね）を散らす。
    /// 粒は後ほどまばらになり、全体は濡れた感じ（低めの帯域・ゆっくり消える）で終わる。
    /// </summary>
    private static void AddTear(float[] samples, int start, int length, float strength, Random random)
    {
        var center = 500f + (400f * random.NextSingle());
        var wobble = 6f + (6f * random.NextSingle());
        var grainAlpha = OnePole(180f);
        var bodyAlpha = OnePole(350f);
        float y1 = 0f, y2 = 0f, grain = 0f, grainTarget = 0f, body = 0f;
        var nextGrain = start;
        var end = Math.Min(samples.Length, start + length);
        for (var n = start; n < end; n++)
        {
            var t = (n - start) / (float)length;
            var envelope = MathF.Min(1f, t / 0.05f) * MathF.Pow(1f - t, 1.4f);

            if (n >= nextGrain)
            {
                // 裂け目が進むたびの跳ね。後になるほど間が開く。
                grainTarget = random.NextSingle() < 0.65f ? 0.4f + (0.6f * random.NextSingle()) : 0.05f;
                nextGrain = n + (int)(SampleRate * Lerp(0.004f, 0.02f, t) * (0.5f + random.NextSingle()));
            }

            grain += grainAlpha * (grainTarget - grain);

            var frequency = center * (1f + (0.45f * MathF.Sin(2f * MathF.PI * wobble * t))) * (1.2f - (0.4f * t));
            var r = 0.93f;
            var a1 = 2f * r * MathF.Cos(2f * MathF.PI * frequency / SampleRate);
            var a2 = -r * r;
            var x = (random.NextSingle() * 2f) - 1f;
            var y = x + (a1 * y1) + (a2 * y2);
            y2 = y1;
            y1 = y;

            // 濡れた重さ: 低域のノイズを少し（グチャッ）。
            body += bodyAlpha * (x - body);

            samples[n] += ((y * 0.12f) + (body * 1.2f)) * grain * envelope * Lerp(0.6f, 0.9f, strength);
        }
    }

    private static float OnePole(float cutoffHz) => 1f - MathF.Exp(-2f * MathF.PI * cutoffHz / SampleRate);

    private static float Lerp(float a, float b, float t) => a + ((b - a) * Math.Clamp(t, 0f, 1f));
}
