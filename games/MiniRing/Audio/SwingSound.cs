using System.Numerics;

namespace MiniRing.Audio;

/// <summary>拳を振り抜いた（グローブの速さがいちばん速くなった）瞬間。<paramref name="Speed"/> はその速さ [m/s]。</summary>
public readonly record struct SwingEvent(Vector3 Position, float Speed);

/// <summary>
/// パンチの風切り音（素材ファイルなし・合成）。帯域を絞ったノイズが「シュッ」と通り過ぎる:
/// 速いほど高く・大きく・短い。打撃音の代わりに、小さく鳴らす。
/// </summary>
public static class SwingSound
{
    public const int SampleRate = HitSound.SampleRate;

    /// <summary>これより遅い振りは鳴らさない [m/s]。</summary>
    public const float MinSpeed = 1.2f;

    /// <summary>いちばん速い振りの音量（砕ける音は 1 前後）。</summary>
    public const float Loudness = 0.12f;

    /// <summary>この速さで最大の音量 [m/s]。</summary>
    private const float FullSpeed = 2.4f;

    /// <summary>振りの速さの、0（<see cref="MinSpeed"/>）〜 1（最大）。</summary>
    public static float Strength(float speed) => Math.Clamp((speed - MinSpeed) / (FullSpeed - MinSpeed), 0f, 1f);

    /// <summary>1 回の振りの音（16-bit モノラル）。遅すぎる振りは null。</summary>
    public static byte[]? Synthesize(float speed, int seed)
    {
        if (speed < MinSpeed)
        {
            return null;
        }

        var strength = Strength(speed);
        var random = new Random(seed);
        var length = 0.22f - (0.08f * strength); // 速いほど短く鋭く
        var samples = new float[(int)(length * SampleRate)];
        var center = 500f + (900f * strength) + (100f * random.NextSingle());
        float y1 = 0f, y2 = 0f;
        for (var n = 0; n < samples.Length; n++)
        {
            var t = n / (float)samples.Length;
            // 近づいて離れる: 音量は 35% の所で頂点、高さは少し上がってから下がる。
            var envelope = t < 0.35f ? MathF.Pow(t / 0.35f, 1.5f) : MathF.Pow(1f - ((t - 0.35f) / 0.65f), 2f);
            var frequency = center * (1f + (0.25f * MathF.Sin(MathF.PI * t)) - (0.15f * t));
            // 2 極の共鳴（帯域の狭さ 0.97）をノイズで鳴らす。
            var r = 0.97f;
            var a1 = 2f * r * MathF.Cos(2f * MathF.PI * frequency / SampleRate);
            var a2 = -r * r;
            var x = (random.NextSingle() * 2f) - 1f;
            var y = x + (a1 * y1) + (a2 * y2);
            y2 = y1;
            y1 = y;
            samples[n] = y * envelope;
        }

        return HitSound.ToPcm16(samples, Loudness * (0.25f + (0.75f * strength)));
    }
}
