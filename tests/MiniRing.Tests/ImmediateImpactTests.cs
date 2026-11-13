using System.Numerics;
using MiniRing.Audio;
using MiniRing.Voxels;

namespace MiniRing.Tests;

[TestClass]
public sealed class ImmediateImpactTests
{
    [TestMethod]
    public void GloveTransientStartsWithTheCoreRatherThanTwentyMillisecondsLater()
    {
        var tone = new HitTone(0.8f, 0.9f, HitTarget.Head);
        var old = SoundAnalysis.ToFloats(HitSound.Synthesize(tone, 7));
        var immediate = SoundAnalysis.ToFloats(new ImpactAudio().Push(0,
            [new ImpactCue(Vector3.Zero, 1, tone)]).Single().Pcm);
        var count = HitSound.SampleRate / 200; // 最初の5ms: v6ではまだ低い芯だけ。
        var oldFront = SoundAnalysis.SpectralCentroid(old.Take(count).ToArray(), HitSound.SampleRate);
        var newFront = SoundAnalysis.SpectralCentroid(immediate.Take(count).ToArray(), HitSound.SampleRate);
        Assert.IsGreaterThan(oldFront * 2f, newFront);
    }

    [TestMethod]
    public void EveryConfirmedHitFiresInItsOwnStepEvenAtTheSamePosition()
    {
        var audio = new ImpactAudio();
        var hit = new ImpactCue(Vector3.Zero, 1, new HitTone(0.6f, 0.8f, HitTarget.Head));
        var voices = audio.Push(0.25, [hit, hit with { Source = 2 }]);
        Assert.AreEqual(2, voices.Count);
        Assert.IsTrue(voices.All(v => v.OccurredAt == 0.25));
        Assert.AreEqual(1, audio.Push(0.25 + 1d / 240, [hit]).Count);
        Assert.AreEqual(0, audio.Flush().Count);
    }

    [TestMethod]
    public void CellContactsCannotReplaceOrMultiplyTheConfirmedHit()
    {
        var hit = new ImpactCue(Vector3.Zero, 1, new HitTone(0.6f, 0.8f, HitTarget.Head));
        var cells = Enumerable.Range(0, 100).Select(_ => ImpactCue.Touch(new ContactSoundEvent(Vector3.Zero, ContactPair.GloveFlesh, 0.7f)));
        var voices = new ImpactAudio().Push(0, [hit, .. cells]);
        Assert.AreEqual(1, voices.Count);
        Assert.IsNotNull(voices[0].Cue.Hit);
    }

    [TestMethod]
    public void BackgroundHasAnActiveVoiceLimitAndASummedAmplitudeBudget()
    {
        var audio = new ImpactAudio();
        var cues = Enumerable.Range(0, 100).Select(i => ImpactCue.Touch(new ContactSoundEvent(Vector3.UnitX * i, ContactPair.MetalMetal, 1f))).ToArray();
        var voices = audio.Push(0, cues);
        Assert.IsTrue(voices.Count <= ImpactAudio.MaxBackgroundVoices);
        Assert.IsTrue(voices.Sum(v => SoundAnalysis.PeakOf(v.Pcm)) <= ImpactAudio.BackgroundPeakBudget + 0.001f);
        Assert.AreEqual(0, audio.Push(1d / 240, cues).Count);
    }

    [TestMethod]
    public void WeaponStrikeKeepsItsWoodSurfaceAndSupportedResonance()
    {
        using var game = new BoxingGame(seed: 613);
        var sound = new ContactSoundEvent(Vector3.Zero, ContactPair.WoodFlesh, 0.7f,
            ContactResonance.Of(0.3f, 0.03f, 0.03f, 0.2f, true), Energy: 1f);
        var weapon = new WeaponHitEvent(game.Player, game.Cpu, BodyPartKind.Head, HitZone.Head,
            Vector3.Zero, 3, 4f, 2f, 0f, 10f, BoxerAnatomy.Wood, sound);
        var cue = ImpactAudio.Gather([], [], [], weaponHits: [weapon]).Single();
        Assert.AreEqual(HitSurface.Wood, cue.Hit!.Value.Surface);
        Assert.AreEqual(sound, cue.Contact);
        var voice = new ImpactAudio().Push(0.25, [cue]).Single();
        Assert.AreEqual(0.25, voice.OccurredAt);
        Assert.AreEqual(sound, voice.Cue.Contact);
    }

    [TestMethod]
    public void LandingAndContactShareBudgetWithoutSuppressingTheMainStrike()
    {
        var audio = new ImpactAudio();
        var pcm = HitSound.Synthesize(new HitTone(1f, 1f, HitTarget.Body), 7);
        var landing = audio.LimitBackground(0, pcm);
        Assert.IsNotNull(landing);
        var contact = ImpactCue.Touch(new ContactSoundEvent(Vector3.UnitX, ContactPair.MetalMetal, 1f));
        var background = audio.Push(0, [contact]).Single();
        Assert.IsTrue(SoundAnalysis.PeakOf(landing) + SoundAnalysis.PeakOf(background.Pcm) <= ImpactAudio.BackgroundPeakBudget + 0.001f);
        Assert.IsNull(audio.LimitBackground(0, pcm));
        var hit = new ImpactCue(Vector3.Zero, 1, new HitTone(0.6f, 0.8f, HitTarget.Head));
        Assert.IsNotNull(audio.Push(0, [hit]).Single().Cue.Hit);
        Assert.IsNotNull(audio.LimitBackground(2, pcm));
    }
}
