using MiniRing.Audio;
using MiniRing.Voxels;
using static MiniRing.Tests.SoundAnalysis;

namespace MiniRing.Tests;

/// <summary>
/// 打撃音の合成: 決定的であること、長さと音量、強いほど大きく低く太いこと、芯を食うほど明るい「パキッ」が乗ること、
/// 打った面の材質ごとの作りが手本の録音（グローブ = 軽いキック、素手 = 重いパンチ）の形に合っていること。
/// 手本の値は録音を同じ測り方（区間ごとの帯の割合・20 ms の包絡・220 Hz より下のゼロ交差の高さ）で測ったもの。強さ 1 が手本の重さ。
/// </summary>
[TestClass]
public sealed class HitSoundTests
{
    private const int Rate = HitSound.SampleRate;

    private static readonly HitTarget[] AllTargets = Enum.GetValues<HitTarget>();

    private static readonly HitSurface[] AllSurfaces = Enum.GetValues<HitSurface>();

    private static readonly float[] RisingIntensities = [0.1f, 0.4f, 0.7f, 1f];

    [TestMethod]
    public void SameArgumentsGiveTheSameBytesAndSeedsVary()
    {
        foreach (var surface in AllSurfaces)
        {
            var tone = new HitTone(0.6f, 0.7f, HitTarget.Head, surface);
            CollectionAssert.AreEqual(HitSound.Synthesize(tone, 7), HitSound.Synthesize(tone, 7), surface.ToString());
            CollectionAssert.AreNotEqual(HitSound.Synthesize(tone, 7), HitSound.Synthesize(tone, 8), surface.ToString());
        }
    }

    [TestMethod]
    public void TheStrikingFaceChoosesTheRecipe()
    {
        Assert.AreEqual(HitSurface.Bare, HitTone.SurfaceOf(BoxerAnatomy.Skin));
        Assert.AreEqual(HitSurface.Metal, HitTone.SurfaceOf(BoxerAnatomy.Iron));
        foreach (var face in new byte[] { 0, BoxerAnatomy.RedGlove, BoxerAnatomy.BlueGlove, BoxerAnatomy.Padding })
        {
            Assert.AreEqual(HitSurface.Glove, HitTone.SurfaceOf(face), $"face {face}");
        }
    }

    [TestMethod]
    public void LengthsStayShortAndPeakFollowsIntensity()
    {
        foreach (var surface in AllSurfaces)
        {
            foreach (var target in AllTargets)
            {
                foreach (var intensity in new[] { -1f, 0f, 0.5f, 1f, 3f })
                {
                    var pcm = HitSound.Synthesize(new HitTone(intensity, 0.5f, target, surface), 3);
                    var seconds = pcm.Length / 2f / Rate;
                    Assert.IsGreaterThanOrEqualTo(0.09f, seconds, $"{surface} {target} {intensity}");
                    Assert.IsLessThanOrEqualTo(0.61f, seconds, $"{surface} {target} {intensity}");

                    // ピーク: 0.35〜0.95（ガードは 0.75 倍）。いまの音量の混ぜ方の範囲。
                    var expected = (0.35f + (0.6f * Math.Clamp(intensity, 0f, 1f))) * (target == HitTarget.Guard ? 0.75f : 1f);
                    Assert.AreEqual(expected, PeakOf(pcm), 0.01f, $"{surface} {target} {intensity}");
                }
            }
        }
    }

    [TestMethod]
    public void HarderHitsAreLouder()
    {
        foreach (var surface in AllSurfaces)
        {
            foreach (var target in AllTargets)
            {
                var light = Rms(HitSound.Synthesize(new HitTone(0.1f, 0.5f, target, surface), 5));
                var heavy = Rms(HitSound.Synthesize(new HitTone(1f, 0.5f, target, surface), 5));
                Assert.IsGreaterThan(light, heavy, $"{surface} {target}");
            }
        }
    }

    /// <summary>
    /// 強さと高さの対応: 軽い当たりは高く細く短い、重い当たりは低く太い。同じ芯で、音全体の重心が強さを上げるたびに下がり続け、
    /// 軽と重で 2 倍以上（1 オクターブ以上）離れる（低い層は 1.5 オクターブ、中高域の帯は 1 オクターブ下がる）。
    /// </summary>
    [TestMethod]
    public void HeavierHitsAreClearlyLower()
    {
        foreach (var surface in AllSurfaces)
        {
            foreach (var target in AllTargets)
            {
                var clean = target == HitTarget.Guard ? 0.1f : 0.5f;
                var centroids = RisingIntensities
                    .Select(intensity => WholeCentroid(HitSound.Synthesize(new HitTone(intensity, clean, target, surface), 4)))
                    .ToArray();
                var text = $"{surface} {target}: {string.Join(" > ", centroids.Select(c => $"{c:F0}"))} Hz";
                for (var i = 1; i < centroids.Length; i++)
                {
                    Assert.IsLessThan(centroids[i - 1], centroids[i], text);
                }

                Assert.IsGreaterThan(2f * centroids[^1], centroids[0], text);
            }
        }
    }

    /// <summary>重い当たりほど低い沈みが長く残る（軽い当たりは短い）。</summary>
    [TestMethod]
    public void HeavierHitsLastLonger()
    {
        foreach (var surface in AllSurfaces)
        {
            var light = FallTime(ToFloats(HitSound.Synthesize(new HitTone(0.1f, 0.5f, HitTarget.Body, surface), 2)), Rate, -20f);
            var heavy = FallTime(ToFloats(HitSound.Synthesize(new HitTone(1f, 0.5f, HitTarget.Body, surface), 2)), Rate, -20f);
            Assert.IsGreaterThan(1.8f * light, heavy, $"{surface}: light {light:F2} s vs heavy {heavy:F2} s to −20 dB");
            Assert.IsLessThanOrEqualTo(0.45f, heavy, $"{surface}: gone within the reference recordings' length");
        }
    }

    /// <summary>芯を食った当たりは、同じ重さでも頭に鋭い「パキッ」が乗る（4 kHz より上のエネルギーが増え、頭の重心が 1 割以上上がる）。頭は素手・鉄で 0〜15 ms、グローブは「バシッ」が「ドッ」から遅れて来るので 0〜50 ms。</summary>
    [TestMethod]
    public void CleanHitsAddABrightCrack()
    {
        foreach (var surface in AllSurfaces)
        {
            foreach (var intensity in new[] { 0.3f, 1f })
            {
                var head = surface == HitSurface.Glove ? 50f : 15f;
                var dull = ToFloats(HitSound.Synthesize(new HitTone(intensity, 0.3f, HitTarget.Head, surface), 9));
                var clean = ToFloats(HitSound.Synthesize(new HitTone(intensity, 1f, HitTarget.Head, surface), 9));
                Assert.IsGreaterThan(BandShare(dull, Rate, 0f, head, 4000f, 22050f), BandShare(clean, Rate, 0f, head, 4000f, 22050f), $"{surface} I={intensity}: more above 4 kHz");
                Assert.IsGreaterThan(1.1f * SegmentCentroid(dull, Rate, 0f, head), SegmentCentroid(clean, Rate, 0f, head), $"{surface} I={intensity}: the centroid of the head rises {SegmentCentroid(dull, Rate, 0f, head):F0} → {SegmentCentroid(clean, Rate, 0f, head):F0} Hz");
            }
        }
    }

    /// <summary>
    /// グローブの作りは手本（軽いキック、打った瞬間から測る）の形: 0〜30 ms は 100〜400 Hz の「ドッ」（手本 82 %）、
    /// 30〜90 ms は 200 Hz〜6.4 kHz に広がる「バシッ」（手本 69 %、100 Hz より下は 11 %）、90〜300 ms は 200 Hz より下の「ドォン」（手本 98 %）。
    /// 低い音は 80〜90 Hz で下がっていき、−20 dB は 340 ms・−40 dB は 420 ms。
    /// </summary>
    [TestMethod]
    public void GloveHitMatchesTheLightKickReference()
    {
        var glove = ToFloats(HitSound.Synthesize(new HitTone(1f, 0.5f, HitTarget.Body, HitSurface.Glove), 7));
        Assert.IsGreaterThan(0.6f, BandShare(glove, Rate, 0f, 30f, 100f, 400f), "0-30 ms whump");
        Assert.IsGreaterThan(0.45f, BandShare(glove, Rate, 30f, 90f, 200f, 6400f), "30-90 ms smack spread");
        Assert.IsLessThan(0.3f, BandShare(glove, Rate, 30f, 90f, 0f, 100f), "30-90 ms: the boom has not taken over yet");
        Assert.IsGreaterThan(0.9f, BandShare(glove, Rate, 90f, 300f, 0f, 200f), "90-300 ms boom");

        var early = LowPitch(glove, Rate, 100f, 200f);
        var late = LowPitch(glove, Rate, 200f, 350f);
        Assert.IsTrue(early is >= 70f and <= 100f, $"boom {early:F0} Hz (reference 90 Hz)");
        Assert.IsGreaterThanOrEqualTo(late, early, $"the boom glides down: {early:F0} → {late:F0} Hz");
        Assert.IsTrue(FallTime(glove, Rate, -20f) is >= 0.27f and <= 0.42f, $"−20 dB at {FallTime(glove, Rate, -20f):F2} s (reference 0.34)");
        Assert.IsTrue(FallTime(glove, Rate, -40f) is >= 0.35f and <= 0.55f, $"−40 dB at {FallTime(glove, Rate, -40f):F2} s (reference 0.42)");
    }

    /// <summary>
    /// 素手の作りは手本（重いパンチ）の形: 0〜30 ms は 400 Hz より上に広がる「バキッ」（手本 81 %）、30〜90 ms は 200 Hz より下が主（手本 68 %）で
    /// 0.8〜6.4 kHz の濁りが 1 割以上（手本 21 %）。低い音は 129 → 98 → 70 → 49 Hz と下がり続け、−20 dB は 300 ms。
    /// </summary>
    [TestMethod]
    public void BareFistHitMatchesTheHeavyPunchReference()
    {
        var bare = ToFloats(HitSound.Synthesize(new HitTone(1f, 0.5f, HitTarget.Body, HitSurface.Bare), 7));
        Assert.IsGreaterThan(0.6f, BandShare(bare, Rate, 0f, 30f, 400f, 22050f), "0-30 ms crack");
        Assert.IsTrue(BandShare(bare, Rate, 30f, 90f, 0f, 200f) is >= 0.5f and <= 0.95f, "30-90 ms low body");
        Assert.IsGreaterThan(0.1f, BandShare(bare, Rate, 30f, 90f, 800f, 6400f), "30-90 ms mid mush");

        var pitches = new[] { (0f, 40f), (40f, 100f), (100f, 200f), (200f, 350f) }.Select(w => LowPitch(bare, Rate, w.Item1, w.Item2)).ToArray();
        var text = string.Join(" → ", pitches.Select(p => $"{p:F0}")) + " Hz (reference 129 → 98 → 70 → 49)";
        for (var i = 1; i < pitches.Length; i++)
        {
            Assert.IsLessThan(pitches[i - 1], pitches[i], text);
        }

        Assert.IsTrue(pitches[0] is >= 105f and <= 160f, text);
        Assert.IsTrue(pitches[^1] is >= 40f and <= 65f, text);
        Assert.IsTrue(FallTime(bare, Rate, -20f) is >= 0.24f and <= 0.36f, $"−20 dB at {FallTime(bare, Rate, -20f):F2} s (reference 0.30)");
    }

    /// <summary>鉄の面（ナックル・籠手）は素手の作りに短い金属の鳴りを足したもの: 素手より 4 kHz より上が多く、長さは素手と変わらない。</summary>
    [TestMethod]
    public void MetalFaceAddsAShortMetallicEdge()
    {
        var bare = ToFloats(HitSound.Synthesize(new HitTone(0.7f, 0.5f, HitTarget.Body, HitSurface.Bare), 7));
        var metal = ToFloats(HitSound.Synthesize(new HitTone(0.7f, 0.5f, HitTarget.Body, HitSurface.Metal), 7));
        Assert.IsGreaterThan(1.5f * BandShare(bare, Rate, 0f, 90f, 4000f, 22050f), BandShare(metal, Rate, 0f, 90f, 4000f, 22050f), $"above 4 kHz: bare {BandShare(bare, Rate, 0f, 90f, 4000f, 22050f):P0} vs metal {BandShare(metal, Rate, 0f, 90f, 4000f, 22050f):P0}");
        Assert.AreEqual(FallTime(bare, Rate, -20f), FallTime(metal, Rate, -20f), 0.06f);
    }

    /// <summary>音全体の重心 [Hz]（頭だけでなく、後から来る低い沈みも含めて）。</summary>
    private static float WholeCentroid(byte[] pcm)
    {
        var samples = ToFloats(pcm);
        return SegmentCentroid(samples, Rate, 0f, samples.Length * 1000f / Rate);
    }
}
