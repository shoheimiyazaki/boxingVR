using System.Numerics;
using MiniRing.Audio;

namespace MiniRing.Tests;

[TestClass]
public sealed class ImpactAudioTests
{
    [TestMethod]
    public void MicroContactsBecomeOneVoiceAndKeepTheStrongestImpact()
    {
        var audio = new ImpactAudio();
        var voices = audio.Push(0, [ImpactCue.Touch(new ContactSoundEvent(Vector3.Zero, ContactPair.GloveGlove, 0.2f)),
            ImpactCue.Touch(new ContactSoundEvent(new Vector3(0.01f, 0, 0), ContactPair.GloveGlove, 0.6f))]);
        Assert.AreEqual(1, voices.Count);
        Assert.AreEqual(0.6f, voices[0].Cue.Contact!.Value.Strength);
    }

    [TestMethod]
    public void HitReplacesNearbyContactButSeparateBlowsSurvive()
    {
        var audio = new ImpactAudio();
        audio.Push(0, [ImpactCue.Touch(new ContactSoundEvent(Vector3.Zero, ContactPair.GloveFlesh, 0.6f))]);
        var voices = audio.Push(0.016, [new ImpactCue(Vector3.Zero, 1, new HitTone(0.5f, 0.6f, HitTarget.Head)), new ImpactCue(Vector3.UnitX, 2, new HitTone(0.5f, 0.6f, HitTarget.Guard))]);
        Assert.AreEqual(2, voices.Count);
        Assert.IsTrue(voices.All(v => v.Cue.Hit is not null));
        Assert.AreEqual(1, audio.Push(0.2, [new ImpactCue(Vector3.Zero, 1, new HitTone(0.6f, 0.6f, HitTarget.Head))]).Count);
    }

    [TestMethod]
    public void SilenceAndDamageHaveNoMinimumAudibleFloor()
    {
        Assert.AreEqual(0f, ImpactAudio.HitGain(0));
        Assert.IsLessThan(ImpactAudio.HitGain(0.2f), ImpactAudio.HitGain(0.01f));
        Assert.IsGreaterThan(ImpactAudio.HitGain(0.2f), ImpactAudio.HitGain(0.8f));
        var silence = new ImpactAudio();
        silence.Push(0, [ImpactCue.Touch(new ContactSoundEvent(Vector3.Zero, ContactPair.GloveGlove, 0))]);
        Assert.AreEqual(0, silence.Push(0.1, []).Count);
    }

    [TestMethod]
    public void SmallSupportedMetalDecaysSoonerThanALongBlade()
    {
        var compact = ContactResonance.Of(0.055f, 0.04f, 0.025f, 0.08f, true);
        var blade = ContactResonance.Of(0.33f, 0.025f, 0.009f, 0.12f, true);
        Assert.IsLessThan(blade.DecayScale / 3f, compact.DecayScale);
        var shortPcm = ContactSound.Synthesize(ContactPair.MetalMetal, 1, 7, compact);
        var longPcm = ContactSound.Synthesize(ContactPair.MetalMetal, 1, 7, blade);
        var shortFall = SoundAnalysis.FallTime(SoundAnalysis.ToFloats(shortPcm), HitSound.SampleRate, -20);
        var longFall = SoundAnalysis.FallTime(SoundAnalysis.ToFloats(longPcm), HitSound.SampleRate, -20);
        Assert.IsLessThan(longFall / 3, shortFall);
        Console.WriteLine($"compact/blade -20dB: {shortFall:F3}/{longFall:F3}s");
    }

    [TestMethod]
    public void InvalidTimeAndNonFiniteInputFailAtTheBoundary()
    {
        var audio = new ImpactAudio();
        Assert.ThrowsExactly<ArgumentOutOfRangeException>(() => audio.Push(double.NaN, []));
        Assert.ThrowsExactly<ArgumentOutOfRangeException>(() => audio.LimitBackground(double.NaN, []));
        Assert.ThrowsExactly<ArgumentException>(() => audio.LimitBackground(0, [1]));
        Assert.ThrowsExactly<ArgumentException>(() => audio.Push(0, [new ImpactCue(Vector3.Zero, 1, new HitTone(float.NaN, 0, HitTarget.Body))]));
        audio.Push(1, []);
        Assert.ThrowsExactly<ArgumentOutOfRangeException>(() => audio.Push(0, []));
    }

    [TestMethod]
    public void FlushKeepsTheOriginalTimeEvenWithVariableSteps()
    {
        var audio = new ImpactAudio();
        var voice = audio.Push(0.1, [new ImpactCue(Vector3.Zero, 1, new HitTone(0.6f, 0.5f, HitTarget.Body))]).Single();
        audio.Push(0.1041667, []);
        Assert.AreEqual(0.1, voice.OccurredAt, 1e-8);
        Assert.AreEqual(0, audio.Flush().Count);
    }

    [TestMethod]
    public void GuardAndCleanHitHaveDifferentSpectra()
    {
        var audio = new ImpactAudio();
        var voices = audio.Push(0, [new ImpactCue(Vector3.Zero, 1, new HitTone(0.7f, 0.1f, HitTarget.Guard)),
            new ImpactCue(Vector3.UnitX, 2, new HitTone(0.7f, 0.8f, HitTarget.Body))]);
        var guard = voices.Single(v => v.Cue.Hit!.Value.Target == HitTarget.Guard).Pcm;
        var body = voices.Single(v => v.Cue.Hit!.Value.Target == HitTarget.Body).Pcm;
        Assert.AreNotEqual(SoundAnalysis.SpectralCentroid(SoundAnalysis.ToFloats(guard), HitSound.SampleRate),
            SoundAnalysis.SpectralCentroid(SoundAnalysis.ToFloats(body), HitSound.SampleRate));
        Assert.IsGreaterThan(SoundAnalysis.PeakOf(guard), SoundAnalysis.PeakOf(body));
    }

    [TestMethod]
    public void SupportDampsTheSameGeometryWithoutChangingItsMass()
    {
        var free = ContactResonance.Of(0.06f, 0.04f, 0.02f, 0.08f, false);
        var held = ContactResonance.Of(0.06f, 0.04f, 0.02f, 0.08f, true);
        Assert.IsLessThan(free.DecayScale, held.DecayScale);
    }

    [TestMethod]
    public void AudioDoesNotChangeTheMatchChecksum()
    {
        using var dry = new BoxingGame(seed: 613);
        using var wet = new BoxingGame(seed: 613);
        var audio = new ImpactAudio();
        for (var step = 0; step < 180; step++)
        {
            dry.Step(default, 1f / 60f);
            wet.Step(default, 1f / 60f);
            var voices = audio.Push(step / 60d, ImpactAudio.Gather(wet.RecentHits, wet.RecentContacts, wet.RecentShatters));
            Assert.AreEqual(wet.RecentHits.Count, voices.Count(v => v.Cue.Hit is not null), $"same-step hit count at {step}");
            Assert.IsTrue(voices.All(v => v.OccurredAt == step / 60d));
            Assert.AreEqual(dry.StateChecksum(), wet.StateChecksum(), $"step {step}");
        }
    }

    [TestMethod]
    public void HighSpeedZeroEnergyContactCannotEraseAnEnergeticContact()
    {
        var audio = new ImpactAudio();
        var energetic = new ContactSoundEvent(Vector3.Zero, ContactPair.GloveGlove, 0.2f, Energy: 1f);
        var fast = new ContactSoundEvent(Vector3.Zero, ContactPair.GloveGlove, 0.8f, Energy: 0f);
        var voice = audio.Push(0, [ImpactCue.Touch(energetic), ImpactCue.Touch(fast)]).Single();
        Assert.AreEqual(energetic, voice.Cue.Contact!.Value);
        Assert.IsGreaterThan(0.01f, SoundAnalysis.PeakOf(voice.Pcm));
        Assert.IsGreaterThan(ImpactAudio.ContactGain(fast), ImpactAudio.ContactGain(energetic));
    }

    [TestMethod]
    public void NamedSweetSpotDoesNotAddSoundDamage()
    {
        var head = new HitEvent(null!, Hand.Left, null!, HitZone.Head, Vector3.Zero, 2f, 0.1f, 0.5f, 10f);
        Assert.AreEqual(HitTone.Of(head), HitTone.Of(head with { Zone = HitZone.Chin }));
        Assert.IsGreaterThan(HitTone.Of(head).Clean, HitTone.Of(head with { Damage = 20f }).Clean);
    }
}
