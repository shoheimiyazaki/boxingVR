namespace MiniRing.Audio;

/// <summary>何に当たったか（音の芯の高さと質感が変わる）。</summary>
public enum HitTarget
{
    Head,
    Body,
    Guard,
}

/// <summary>
/// 一撃の音色。<paramref name="Intensity"/> はダメージの大きさ 0..1、<paramref name="Clean"/> は芯を食った度合い 0..1
/// （体重が乗っているほど・急所ほど高く、ガードの上からは低い）。
/// </summary>
/// <param name="Intensity">ダメージの大きさ 0..1。低く太く・長く・大きくなる。</param>
/// <param name="Clean">芯を食った度合い 0..1。破裂音（パンッ）が明るく鋭くなる。</param>
/// <param name="Target">当たった場所の種類。</param>
public readonly record struct HitTone(float Intensity, float Clean, HitTarget Target)
{
    /// <summary>この程度のダメージで最も重い音になる。</summary>
    public const float FullDamage = 28f;

    public static HitTone Of(in HitEvent hit)
    {
        var target = hit.Zone switch
        {
            HitZone.Guard or HitZone.Shoulder or HitZone.Elbow => HitTarget.Guard,
            HitZone.Chin or HitZone.Temple or HitZone.Head or HitZone.Neck => HitTarget.Head,
            _ => HitTarget.Body,
        };
        // 急所（顎・こめかみ・肝臓・みぞおち）は同じ重さでも「入った」音にする。
        var sweetSpot = hit.Zone is HitZone.Chin or HitZone.Temple or HitZone.Liver or HitZone.SolarPlexus ? 0.25f : 0f;
        var clean = target == HitTarget.Guard ? 0.1f * hit.BodyShare : 0.35f + (0.5f * hit.BodyShare) + sweetSpot;
        return new HitTone(Math.Clamp(hit.Damage / FullDamage, 0f, 1f), Math.Clamp(clean, 0f, 1f), target);
    }
}

/// <summary>
/// 打撃音の合成（素材ファイルなし・決定的）。気持ちよく聞こえるよう 4 つの層を重ねる:
/// <list type="bullet">
/// <item>ドスッ: ピッチが落ちる低い正弦（グローブが肉を押す重さ）。強いほど低く長い。</item>
/// <item>パンッ: 帯域を絞ったノイズの破裂（革と肌の当たり）。芯を食うほど明るく鋭い。</item>
/// <item>パキッ: ごく短い高域のクラック（クリーンヒットのときだけ）。</item>
/// <item>ズン: 強打のときだけ鳴る低い余韻。最後に小さな部屋の響きと柔らかいクリップで角を丸める。</item>
/// </list>
/// </summary>
public static class HitSound
{
    public const int SampleRate = 44100;

    /// <summary>16-bit モノラル PCM（リトルエンディアン）。</summary>
    public static byte[] Synthesize(HitTone tone, int seed)
    {
        var intensity = Math.Clamp(tone.Intensity, 0f, 1f);
        var clean = Math.Clamp(tone.Clean, 0f, 1f);
        var guard = tone.Target == HitTarget.Guard;
        var length = (int)(SampleRate * (0.22f + (0.45f * intensity)));
        var samples = new float[length];
        var random = new Random(seed);

        // ドスッ: 頭は少し高く、ボディは低く。強いほど低く。
        var baseHz = (tone.Target switch { HitTarget.Head => 105f, HitTarget.Body => 78f, _ => 130f }) * Lerp(1.15f, 0.82f, intensity);
        var thumpDecay = Lerp(0.05f, 0.15f, intensity) * (guard ? 0.6f : 1f);
        var thumpGain = Lerp(0.7f, 1f, intensity) * (guard ? 0.7f : 1f);

        // パンッ: 一次フィルタ 2 段で帯域を作る（明るさ = 上の遮断周波数）。
        var smackHigh = guard ? 900f : Lerp(1400f, 6000f, clean);
        var smackDecay = Lerp(0.006f, 0.022f, intensity);
        var smackGain = (guard ? 0.35f : Lerp(0.3f, 0.95f, clean)) * Lerp(0.6f, 1f, intensity);
        var crackGain = guard ? 0f : MathF.Max(0f, (clean - 0.55f) / 0.45f) * Lerp(0.3f, 0.8f, intensity);
        var boomGain = guard ? 0f : MathF.Max(0f, (intensity - 0.4f) / 0.6f) * 0.55f;

        var lowAlpha = OnePole(smackHigh);
        var bandLowAlpha = OnePole(280f);
        var crackAlpha = OnePole(2500f);
        float low = 0f, band = 0f, crackLow = 0f, phase = 0f, boomPhase = 0f;
        for (var n = 0; n < length; n++)
        {
            var t = n / (float)SampleRate;
            var attack = MathF.Min(1f, t / 0.0015f);

            var hz = baseHz * (1f + (1.3f * MathF.Exp(-t / 0.018f)));
            phase += 2f * MathF.PI * hz / SampleRate;
            var thump = MathF.Sin(phase) * MathF.Exp(-t / thumpDecay) * thumpGain;

            var noise = (random.NextSingle() * 2f) - 1f;
            low += lowAlpha * (noise - low);
            band += bandLowAlpha * (low - band);
            var smack = (low - band) * 2.2f * MathF.Exp(-t / smackDecay) * smackGain;

            crackLow += crackAlpha * (noise - crackLow);
            var crack = (noise - crackLow) * MathF.Exp(-t / 0.0028f) * crackGain;

            boomPhase += 2f * MathF.PI * 50f / SampleRate;
            var boom = MathF.Sin(boomPhase) * MathF.Exp(-t / 0.13f) * boomGain * MathF.Min(1f, t / 0.006f);

            samples[n] = attack * (thump + smack + crack + boom);
        }

        AddRoom(samples, guard ? 0.12f : 0.2f);
        return ToPcm16(samples, Lerp(0.35f, 0.95f, intensity) * (guard ? 0.75f : 1f));
    }

    /// <summary>小さな部屋の響き（2 本のフィードバック・コム）。打撃が乾きすぎず、耳に心地よく残る。</summary>
    private static void AddRoom(float[] samples, float amount)
    {
        var d1 = (int)(0.029f * SampleRate);
        var d2 = (int)(0.043f * SampleRate);
        for (var n = 0; n < samples.Length; n++)
        {
            var echo = (n >= d1 ? samples[n - d1] : 0f) * amount;
            echo += (n >= d2 ? samples[n - d2] : 0f) * amount * 0.7f;
            samples[n] += echo;
        }
    }

    /// <summary>ピークを <paramref name="loudness"/> に揃え、柔らかくクリップし、末尾をフェードして 16-bit にする。</summary>
    private static byte[] ToPcm16(float[] samples, float loudness)
    {
        var peak = 1e-6f;
        foreach (var s in samples)
        {
            peak = MathF.Max(peak, MathF.Abs(s));
        }

        const float drive = 1.4f;
        var norm = MathF.Tanh(drive);
        var fade = (int)(0.012f * SampleRate);
        var pcm = new byte[samples.Length * 2];
        for (var n = 0; n < samples.Length; n++)
        {
            var y = MathF.Tanh(drive * samples[n] / peak) / norm * loudness;
            y *= MathF.Min(1f, (samples.Length - n) / (float)fade);
            var v = (short)Math.Clamp(MathF.Round(y * short.MaxValue), short.MinValue, short.MaxValue);
            pcm[2 * n] = (byte)(v & 0xFF);
            pcm[(2 * n) + 1] = (byte)((v >> 8) & 0xFF);
        }

        return pcm;
    }

    private static float OnePole(float cutoffHz) => 1f - MathF.Exp(-2f * MathF.PI * cutoffHz / SampleRate);

    private static float Lerp(float a, float b, float t) => a + ((b - a) * Math.Clamp(t, 0f, 1f));
}
