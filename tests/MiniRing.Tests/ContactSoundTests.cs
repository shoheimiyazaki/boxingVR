using MiniRing.Audio;
using static MiniRing.Tests.SoundAnalysis;

namespace MiniRing.Tests;

/// <summary>
/// 壊れない接触の合成音: 決定的であること、長さ、強さで大きくなること、強いほど低く太くなること、鉄が鳴り残らず濁っていること、
/// 音量が打撃音の範囲を越えないこと。
/// </summary>
[TestClass]
public sealed class ContactSoundTests
{
    private const int Rate = ContactSound.SampleRate;

    private static readonly ContactPair[] AllPairs = Enum.GetValues<ContactPair>();

    [TestMethod]
    public void SameArgumentsGiveTheSameBytesAndSeedsVary()
    {
        foreach (var pair in AllPairs)
        {
            CollectionAssert.AreEqual(ContactSound.Synthesize(pair, 0.6f, 7), ContactSound.Synthesize(pair, 0.6f, 7), pair.ToString());
            CollectionAssert.AreNotEqual(ContactSound.Synthesize(pair, 0.6f, 7), ContactSound.Synthesize(pair, 0.6f, 8), pair.ToString());
        }
    }

    [TestMethod]
    public void LengthsStayShortAndPeakMatchesLoudness()
    {
        foreach (var pair in AllPairs)
        {
            foreach (var strength in new[] { -1f, 0f, 0.5f, 1f, 3f, float.NaN })
            {
                var pcm = ContactSound.Synthesize(pair, strength, 3);
                var seconds = pcm.Length / 2f / Rate;
                Assert.IsGreaterThanOrEqualTo(0.029f, seconds, $"{pair} {strength}");
                Assert.IsLessThan(1f, seconds, $"{pair} {strength}");
                Assert.AreEqual(ContactSound.Loudness(pair, strength), PeakOf(pcm), 0.01f, $"{pair} {strength}: the PCM peak is the loudness");
            }
        }
    }

    [TestMethod]
    public void StrongerContactsAreLouder()
    {
        foreach (var pair in AllPairs)
        {
            Assert.IsGreaterThan(Rms(ContactSound.Synthesize(pair, 0.1f, 5)), Rms(ContactSound.Synthesize(pair, 1f, 5)), pair.ToString());
        }
    }

    [TestMethod]
    public void LoudnessIsMonotonicAndBelowThePunchRange()
    {
        foreach (var pair in AllPairs)
        {
            var previous = -1f;
            for (var i = 0; i <= 20; i++)
            {
                var loudness = ContactSound.Loudness(pair, i / 20f);
                Assert.IsGreaterThanOrEqualTo(previous, loudness, $"{pair} at {i / 20f}");
                Assert.IsLessThanOrEqualTo(1f, loudness, pair.ToString());
                Assert.IsGreaterThan(0f, loudness, pair.ToString());
                previous = loudness;
            }

            Assert.IsGreaterThan(ContactSound.Loudness(pair, 0f), ContactSound.Loudness(pair, 1f), $"{pair}: strictly louder when hard");
            Assert.IsLessThanOrEqualTo(0.8f, ContactSound.Loudness(pair, 1f), $"{pair}: never above 0.8");
        }

        var loudest = AllPairs.Max(pair => ContactSound.Loudness(pair, 1f));
        Assert.AreEqual(ContactSound.Loudness(ContactPair.MetalMetal, 1f), loudest, "steel on steel is the loudest contact");
        Assert.IsLessThan(0.35f, ContactSound.Loudness(ContactPair.GloveFlesh, 1f), "a harmless glove touch is quieter than the quietest punch hit");
        Assert.IsLessThan(ContactSound.Loudness(ContactPair.GloveFlesh, 1f), ContactSound.Loudness(ContactPair.FleshFlesh, 1f), "skin on skin is the quietest");
    }

    /// <summary>
    /// 鉄とグローブの違いは「鳴り続けるかどうか」: 刃の鳴りは強さ 0.7 でも 0.4 s 以上、グローブの当たりの倍以上残る。
    /// </summary>
    [TestMethod]
    public void SteelSustainsLongerThanGloves()
    {
        var metal = FallTime(ToFloats(ContactSound.Synthesize(ContactPair.MetalMetal, 0.7f, 4)), Rate, -20f);
        var glove = FallTime(ToFloats(ContactSound.Synthesize(ContactPair.GloveGlove, 0.7f, 4)), Rate, -20f);
        Assert.IsGreaterThan(2f * glove, metal, $"steel {metal:F2} s vs glove {glove:F2} s to fall 20 dB");
        Assert.IsGreaterThan(0.4f, metal);
    }

    /// <summary>
    /// 強さと高さの対応（<see cref="HitSound"/> と同じ向き）: 弱い当たりは高く細く、強い当たりは低く太い。
    /// 重心で 1.5 倍以上の差（鳴りと面の当たりの帯域は 1 オクターブ、ドスッ・打撃の低い層は 1.5 オクターブ下がる）。
    /// 刃の鳴りを持つ組は 1.25 倍以上: 刃の鳴りは鉄の明るさを保つため 0.6 オクターブしか下げず、頭の「チャッ」とシャーは高さを変えない。
    /// </summary>
    [TestMethod]
    public void HarderContactsSoundLowerAndFatter()
    {
        foreach (var pair in AllPairs)
        {
            var weak = WholeCentroid(ContactSound.Synthesize(pair, 0.1f, 4));
            var strong = WholeCentroid(ContactSound.Synthesize(pair, 1f, 4));
            var ratio = pair is ContactPair.MetalMetal or ContactPair.MetalWood ? 1.25f : 1.5f;
            Assert.IsGreaterThan(ratio * strong, weak, $"{pair}: weak {weak:F0} Hz vs strong {strong:F0} Hz");
        }
    }

    /// <summary>
    /// 柔らかい物（グローブ・肌）の当たりは打撃の作りを軽くしたもので、刃のようには鳴り残らない（0.5 s 以内に消え、−20 dB は 0.35 s 以内）。
    /// 前は 0.15 s 以内としていたが、グローブの作りを手本（軽いキック）に合わせ、低い「ドォン」を持つようにしたので延ばした。
    /// </summary>
    [TestMethod]
    public void PaddedContactsDoNotRing()
    {
        foreach (var pair in new[] { ContactPair.GloveGlove, ContactPair.GloveFlesh, ContactPair.FleshFlesh })
        {
            var clip = ContactSound.Synthesize(pair, 1f, 1);
            Assert.IsLessThanOrEqualTo(0.5f, clip.Length / 2f / Rate, $"{pair}: no ring");
            Assert.IsLessThanOrEqualTo(0.35f, FallTime(ToFloats(clip), Rate, -20f), $"{pair}: −20 dB quickly");
        }

        Assert.IsGreaterThan(
            ContactSound.Synthesize(ContactPair.GloveGlove, 1f, 1).Length,
            ContactSound.Synthesize(ContactPair.MetalMetal, 0f, 1).Length,
            "even the softest steel contact rings longer than the hardest glove bump");
    }

    /// <summary>
    /// グローブの当たりはグローブの打撃の作り（手本: 軽いキック）を軽くしたもの: 頭は 100〜400 Hz の「ドッ」、続いて 200 Hz〜6.4 kHz に
    /// 広がる「バシッ」。軽いので低い「ドォン」は手本より高く短い。
    /// </summary>
    [TestMethod]
    public void GloveContactsUseTheGloveRecipe()
    {
        foreach (var pair in new[] { ContactPair.GloveGlove, ContactPair.GloveFlesh })
        {
            var glove = ToFloats(ContactSound.Synthesize(pair, 1f, 6));
            Assert.IsGreaterThan(0.5f, BandShare(glove, Rate, 0f, 20f, 100f, 800f), $"{pair}: whump first");
            Assert.IsGreaterThan(0.3f, BandShare(glove, Rate, 20f, 80f, 200f, 6400f), $"{pair}: then the smack");
        }
    }

    /// <summary>
    /// 鉄と鉄（強さ 1）は手本の録音（刃の打ち合い）の形: 頭の 0〜30 ms は 3.2〜12.8 kHz が主（手本 86 %）で 1.6 kHz より下はほぼ無い、
    /// 90〜300 ms も 1.6〜12.8 kHz に 9 割以上（手本 99 %）。包絡は −6 dB が 0.3〜0.5 s（手本 0.40）、−20 dB が 0.5〜0.8 s（手本 0.64）、
    /// −40 dB が 0.8 s より後（手本 0.92）。重心は鳴りが進むと下がる（手本 7.1 → 5.0 kHz）。
    /// </summary>
    [TestMethod]
    public void SteelClashMatchesTheSwordReference()
    {
        var metal = ToFloats(ContactSound.Synthesize(ContactPair.MetalMetal, 1f, 7));
        Assert.IsGreaterThan(0.7f, BandShare(metal, Rate, 0f, 30f, 3200f, 12800f), "0-30 ms bright head");
        Assert.IsLessThan(0.05f, BandShare(metal, Rate, 0f, 30f, 0f, 1600f), "0-30 ms: no low end");
        Assert.IsGreaterThan(0.9f, BandShare(metal, Rate, 90f, 300f, 1600f, 12800f), "90-300 ms ring band");
        Assert.IsTrue(FallTime(metal, Rate, -6f) is >= 0.3f and <= 0.5f, $"−6 dB at {FallTime(metal, Rate, -6f):F2} s");
        Assert.IsTrue(FallTime(metal, Rate, -20f) is >= 0.5f and <= 0.8f, $"−20 dB at {FallTime(metal, Rate, -20f):F2} s");
        Assert.IsGreaterThan(0.8f, FallTime(metal, Rate, -40f), $"−40 dB at {FallTime(metal, Rate, -40f):F2} s");
        Assert.IsGreaterThan(SegmentCentroid(metal, Rate, 300f, 600f), SegmentCentroid(metal, Rate, 0f, 30f), "the ring darkens as it decays");
    }

    /// <summary>
    /// 刃の鳴りは数本の線（風鈴）ではなく、強い山のまわりに弱い山が詰まった帯: 20〜400 ms の 1〜14 kHz で、強い 8 本のビンが持つエネルギーは
    /// 3/4 未満（手本の録音 0.57、合成 0.5 前後。前の 2.2 kHz の 4 本の線の「風鈴」は 0.9 を越える）。
    /// </summary>
    [TestMethod]
    public void SteelRingIsDenseNotAFewLines()
    {
        foreach (var seed in new[] { 1, 2, 3 })
        {
            var metal = ToFloats(ContactSound.Synthesize(ContactPair.MetalMetal, 1f, seed));
            var segment = metal[(int)(0.02f * Rate)..(int)(0.4f * Rate)];
            Assert.IsLessThan(0.75f, StrongestBinsShare(segment, Rate, 1000f, 14000f, 8), $"seed {seed}");
        }
    }

    /// <summary>木や肉に押さえられた刃は鳴り残らない（鉄と木・鉄と肉は −20 dB が 0.2 s 以内）。</summary>
    [TestMethod]
    public void DampedBladesStopQuickly()
    {
        foreach (var pair in new[] { ContactPair.MetalWood, ContactPair.MetalFlesh })
        {
            var clip = ToFloats(ContactSound.Synthesize(pair, 1f, 3));
            Assert.IsLessThanOrEqualTo(0.2f, FallTime(clip, Rate, -20f), pair.ToString());
        }
    }

    /// <summary>音全体の重心 [Hz]（頭だけでなく、後から来る低い沈み・鳴りも含めて）。</summary>
    private static float WholeCentroid(byte[] pcm)
    {
        var samples = ToFloats(pcm);
        return SegmentCentroid(samples, Rate, 0f, samples.Length * 1000f / Rate);
    }
}
