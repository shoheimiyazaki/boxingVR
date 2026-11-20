using MiniRing.Audio;

namespace MiniRing.Tests;

/// <summary>かすり・断裂・観客の合成音: 決定的であること、長さ、音量の並び（かすりは打撃より小さい・K.O. は「おぉ」より大きく長い）。</summary>
[TestClass]
[TestCategory("slow")]
public sealed class CrowdAndGrazeSoundTests
{
    private const int Rate = HitSound.SampleRate;

    [TestMethod]
    public void GrazeIsDeterministicShortAndQuieterThanHits()
    {
        CollectionAssert.AreEqual(GrazeSound.Synthesize(5.035f, 7), GrazeSound.Synthesize(5.035f, 7));
        CollectionAssert.AreNotEqual(GrazeSound.Synthesize(5.035f, 7), GrazeSound.Synthesize(5.035f, 8));

        foreach (var speed in new[] { 0f, 2.518f, 5.035f, 16.78f })
        {
            var pcm = GrazeSound.Synthesize(speed, 3);
            var seconds = pcm.Length / 2f / Rate;
            Assert.IsGreaterThanOrEqualTo(0.059f, seconds, $"speed {speed}");
            Assert.IsLessThanOrEqualTo(0.141f, seconds, $"speed {speed}");
            Assert.IsLessThanOrEqualTo(GrazeSound.Loudness + 0.001f, PeakOf(pcm), "never louder than its loudness");
        }

        Assert.IsLessThan(0.35f, GrazeSound.Loudness, "below the quietest punch hit");
        Assert.IsLessThan(SwingSound.Loudness, GrazeSound.Loudness, "below the swing whoosh");
        Assert.IsLessThan(GrazeSound.Synthesize(1.678f, 3).Length, GrazeSound.Synthesize(6.714f, 3).Length, "faster grazes are shorter");
    }

    [TestMethod]
    public void SeverIsDeterministicLoudAndScaledByStrength()
    {
        CollectionAssert.AreEqual(SeverSound.Synthesize(0.6f, 11), SeverSound.Synthesize(0.6f, 11));

        var weak = SeverSound.Synthesize(0f, 11);
        var strong = SeverSound.Synthesize(1f, 11);
        Assert.IsGreaterThan(0.75f, PeakOf(weak));
        Assert.IsGreaterThan(PeakOf(weak), PeakOf(strong), "stronger cuts are louder");
        var seconds = strong.Length / 2f / Rate;
        Assert.IsGreaterThan(0.15f, seconds);
        Assert.IsLessThan(0.4f, seconds);
    }

    [TestMethod]
    public void AmbienceHasTheRequestedLengthAndSitsLow()
    {
        var ambience = CrowdSound.Ambience(2f, 5);
        Assert.HasCount(2 * Rate, ambience);
        CollectionAssert.AreEqual(ambience, CrowdSound.Ambience(2f, 5));
        Assert.AreEqual(CrowdSound.AmbiencePeak, ambience.Max(MathF.Abs), 1e-4f);
        Assert.IsLessThan(0.1f, CrowdSound.AmbiencePeak);
    }

    [TestMethod]
    public void ReactionsAreDeterministicAndScaleWithTheMoment()
    {
        foreach (var reaction in Enum.GetValues<CrowdReaction>())
        {
            CollectionAssert.AreEqual(CrowdSound.React(reaction, 0.5f, 9), CrowdSound.React(reaction, 0.5f, 9), reaction.ToString());
            Assert.IsGreaterThan(
                PeakOf(CrowdSound.React(reaction, 0f, 9)),
                PeakOf(CrowdSound.React(reaction, 1f, 9)),
                $"{reaction}: a bigger moment is louder");
        }

        var ooh = CrowdSound.React(CrowdReaction.Ooh, 1f, 1);
        var eruption = CrowdSound.React(CrowdReaction.Eruption, 1f, 1);
        Assert.IsGreaterThan(PeakOf(ooh), PeakOf(eruption), "the KO roar is louder than an ooh");
        Assert.IsGreaterThan(ooh.Length, eruption.Length, "and lasts longer");

        // 決着の歓声は一打のどよめきより大きく鳴らさず（驚くほど大きかった。2026-11-18）、膨らんでいく（最初の 0.3 秒の平均の大きさは、1〜2 秒の半分より小さい）。
        var roar = CrowdSound.React(CrowdReaction.Roar, 1f, 1);
        Assert.IsLessThanOrEqualTo(PeakOf(roar), PeakOf(eruption), "the KO eruption is not louder than one big roar");
        Assert.IsLessThan(0.5f * RmsOf(eruption, 1f, 2f), RmsOf(eruption, 0f, 0.3f), "the KO eruption swells instead of blasting");

        var count = CrowdSound.React(CrowdReaction.Count, 1f, 1);
        Assert.IsLessThan(0.5f * Rate, count.Length, "one count shout fits inside the 1-second beat");
    }

    [TestMethod]
    public void ToPcmKeepsTheLevelAndClamps()
    {
        var pcm = CrowdSound.ToPcm([0.5f, -2f, 2f]);
        Assert.AreEqual((short)MathF.Round(0.5f * short.MaxValue), BitConverter.ToInt16(pcm, 0));
        Assert.AreEqual((short)-short.MaxValue, BitConverter.ToInt16(pcm, 2));
        Assert.AreEqual(short.MaxValue, BitConverter.ToInt16(pcm, 4));
    }

    private static float RmsOf(float[] samples, float from, float to)
    {
        var sum = 0.0;
        var start = (int)(from * Rate);
        var end = Math.Min(samples.Length, (int)(to * Rate));
        for (var i = start; i < end; i++)
        {
            sum += (double)samples[i] * samples[i];
        }

        return (float)Math.Sqrt(sum / Math.Max(1, end - start));
    }

    private static float PeakOf(byte[] pcm)
    {
        var peak = 0;
        for (var i = 0; i < pcm.Length; i += 2)
        {
            peak = Math.Max(peak, Math.Abs((int)BitConverter.ToInt16(pcm, i)));
        }

        return peak / (float)short.MaxValue;
    }

    private static float PeakOf(float[] samples) => samples.Max(MathF.Abs);
}
