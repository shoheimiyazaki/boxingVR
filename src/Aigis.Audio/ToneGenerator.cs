namespace Aigis.Audio;

/// <summary>
/// Procedural fallback tones — the third and final stage of <see cref="ClipResolver"/>'s
/// resolution order (§9.8: explicit assignment -&gt; file load -&gt; generated tone), so a missing
/// asset degrades to an audible cue instead of silence.
/// </summary>
public static class ToneGenerator
{
    private const int Channels = 1;

    /// <summary>A one-shot swept sine: linearly interpolates frequency from
    /// <paramref name="startHz"/> to <paramref name="endHz"/> over <paramref name="durationSeconds"/>,
    /// with an attack/release envelope so the tone does not click at either end.</summary>
    /// <param name="name">The clip's name.</param>
    /// <param name="startHz">Frequency at the start of the sweep.</param>
    /// <param name="endHz">Frequency at its end.</param>
    /// <param name="durationSeconds">Total length.</param>
    /// <param name="volume">Peak amplitude, 0..1.</param>
    /// <param name="sampleRate">Output sample rate.</param>
    /// <param name="attackSeconds">
    /// How long the tone takes to reach <paramref name="volume"/>.
    /// <para><b>The default 0.06 s is a cue's envelope, not a UI tick's</b> (2026-09-22). A tick is
    /// 30-40 ms long in total, so a 60 ms attack means it never reaches its own peak — it comes out
    /// quiet and mushy, and turning the volume up to compensate only makes a longer mush. The
    /// attack slope is what decides how loud a short clip sounds, so it has to be a parameter
    /// rather than a constant the moment anything short is generated.</para>
    /// </param>
    /// <param name="releaseSeconds">How long it takes to fall silent. Long enough to avoid a click
    /// at the end; a hard cut on a sine is an audible tick of its own.</param>
    public static AudioClip CreateToneClip(
        string name, float startHz, float endHz, float durationSeconds, float volume, int sampleRate = 44100,
        float attackSeconds = 0.06f, float releaseSeconds = 0.10f)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(durationSeconds);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(sampleRate);
        ArgumentOutOfRangeException.ThrowIfNegative(attackSeconds);
        ArgumentOutOfRangeException.ThrowIfNegative(releaseSeconds);

        var frameCount = (int)(durationSeconds * sampleRate);
        var pcm = new byte[frameCount * Channels * sizeof(short)];
        var phase = 0.0;

        for (var i = 0; i < frameCount; i++)
        {
            var t = i / (float)sampleRate;
            var progress = frameCount <= 1 ? 0f : i / (float)(frameCount - 1);
            var hz = startHz + (endHz - startHz) * progress;
            phase += 2.0 * Math.PI * hz / sampleRate;

            var envelope = Envelope(t, durationSeconds, attackSeconds, releaseSeconds);
            var sample = (float)Math.Sin(phase) * volume * envelope;
            WriteSample(pcm, i, sample);
        }

        return new AudioClip(name, AudioSampleFormat.Mono16, sampleRate, pcm, IsGenerated: true);
    }

    /// <summary>A one-shot IMPACT: the sound of something heavy hitting the floor, as opposed to
    /// <see cref="CreateToneClip"/>'s swept sine.
    ///
    /// <para><b>Why a separate generator rather than a lower sweep.</b> A sine sweep with a 0.06 s
    /// attack is a beep, and lowering its pitch makes it a low beep — the attack is what gives it away,
    /// because nothing struck rises into its own sound. Three things make a thud a thud, and none of
    /// them are pitch: an <b>instant attack</b> (the whole energy arrives in the first millisecond),
    /// an <b>exponential decay</b> (a struck body loses energy fast, it does not hold a note), and
    /// <b>inharmonic content</b> — a real impact excites modes that are not integer multiples of each
    /// other, plus broadband noise from the surfaces themselves. A pure tone has none of that, which is
    /// exactly what "濁った音" (a muddy sound) asks for and a sine cannot give.</para>
    ///
    /// <para>So this sums a fundamental, a deliberately non-integer partial at
    /// <see cref="InharmonicRatio"/> (a ratio chosen to beat against the fundamental rather than fuse
    /// with it, which is what reads as "dull" instead of "musical"), and a noise bed low-passed by a
    /// one-pole filter so it is a rumble rather than a hiss. The frequency also falls slightly across
    /// the decay, which is what a damped physical body does and what stops the tail sounding
    /// synthetic.</para>
    ///
    /// <para>Deterministic: the noise comes from a fixed-seed generator, so the same slot always
    /// produces byte-identical PCM. Audio is cached per slot name and a clip that differed run to run
    /// would make a replayed session sound different (Hard rule 5's spirit, applied to the one
    /// subsystem that has its own random source).</para></summary>
    /// <param name="name">The clip's name, as for <see cref="CreateToneClip"/>.</param>
    /// <param name="startHz">The fundamental at the moment of impact.</param>
    /// <param name="endHz">The fundamental by the end of the tail — below <paramref name="startHz"/>
    /// for a body settling, which is the usual case.</param>
    /// <param name="durationSeconds">Total length including the decay tail.</param>
    /// <param name="volume">Peak amplitude, 0..1.</param>
    /// <param name="sampleRate">Output sample rate.</param>
    public static AudioClip CreateImpactClip(
        string name, float startHz, float endHz, float durationSeconds, float volume, int sampleRate = 44100)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(durationSeconds);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(sampleRate);

        var frameCount = (int)(durationSeconds * sampleRate);
        var pcm = new byte[frameCount * Channels * sizeof(short)];
        var phase = 0.0;
        var partialPhase = 0.0;
        var noise = new Random(ImpactNoiseSeed);
        var lowPassed = 0.0;

        // A one-pole low-pass whose coefficient is expressed in Hz so the noise bed's character does
        // not change with the sample rate.
        var noiseCoefficient = 1.0 - Math.Exp(-2.0 * Math.PI * NoiseCutoffHz / sampleRate);

        for (var i = 0; i < frameCount; i++)
        {
            var progress = frameCount <= 1 ? 0f : i / (float)(frameCount - 1);
            var hz = startHz + (endHz - startHz) * progress;
            phase += 2.0 * Math.PI * hz / sampleRate;
            partialPhase += 2.0 * Math.PI * hz * InharmonicRatio / sampleRate;

            lowPassed += noiseCoefficient * (((noise.NextDouble() * 2.0) - 1.0) - lowPassed);

            // Instant attack, exponential decay. No release ramp is needed: the decay has already
            // taken the tail to near silence by the end, so there is nothing left to click.
            var envelope = (float)Math.Exp(-DecayRate * progress);
            var body = (Math.Sin(phase) * FundamentalGain)
                + (Math.Sin(partialPhase) * PartialGain)
                + (lowPassed * NoiseGain);

            WriteSample(pcm, i, (float)body * volume * envelope);
        }

        return new AudioClip(name, AudioSampleFormat.Mono16, sampleRate, pcm, IsGenerated: true);
    }

    /// <summary>Fixed seed for the impact noise bed — see <see cref="CreateImpactClip"/> for why this
    /// must not vary.</summary>
    private const int ImpactNoiseSeed = 20260815;

    /// <summary>The second partial's frequency as a multiple of the fundamental. Deliberately NOT an
    /// integer: an integer multiple fuses into one pitched note, while a ratio like this beats against
    /// the fundamental and is heard as a dull, indefinite thud.</summary>
    private const double InharmonicRatio = 1.47;

    /// <summary>Corner frequency of the noise bed's one-pole low-pass. Low enough that what survives
    /// is a rumble under the tone rather than an audible hiss over it.</summary>
    private const double NoiseCutoffHz = 260.0;

    private const double FundamentalGain = 0.62;
    private const double PartialGain = 0.22;
    private const double NoiseGain = 0.30;

    /// <summary>How fast the impact decays across its own duration: <c>exp(-6)</c> is about 0.25% of
    /// the peak by the end, i.e. genuinely silent, with most of the energy in the first fifth.</summary>
    private const double DecayRate = 6.0;

    /// <summary>An envelope-free loop tone: two harmonics (fundamental at 0.8, first overtone at
    /// 0.2) with no attack/release, because an enveloped tone pulses audibly when looped
    /// continuously (§9.8's own note on why the reference's <c>CreateLoopToneClip</c> differs from
    /// its one-shot sibling). <paramref name="durationSeconds"/> is the length of the single
    /// repeating buffer, not a total playback time.</summary>
    public static AudioClip CreateLoopToneClip(
        string name, float hz, float volume, float durationSeconds = 1f, int sampleRate = 44100)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(durationSeconds);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(sampleRate);

        var frameCount = (int)(durationSeconds * sampleRate);
        var pcm = new byte[frameCount * Channels * sizeof(short)];
        var phase = 0.0;
        var overtonePhase = 0.0;

        for (var i = 0; i < frameCount; i++)
        {
            phase += 2.0 * Math.PI * hz / sampleRate;
            overtonePhase += 2.0 * Math.PI * (hz * 2) / sampleRate;

            var sample = (float)((Math.Sin(phase) * 0.8) + (Math.Sin(overtonePhase) * 0.2)) * volume;
            WriteSample(pcm, i, sample);
        }

        return new AudioClip(name, AudioSampleFormat.Mono16, sampleRate, pcm, IsGenerated: true);
    }

    private static float Envelope(float t, float duration, float attack, float release)
    {
        if (t < attack)
        {
            return t / attack;
        }

        var releaseStart = duration - release;
        if (t > releaseStart)
        {
            return Math.Max(0f, (duration - t) / release);
        }

        return 1f;
    }

    private static void WriteSample(byte[] pcm, int frameIndex, float sample)
    {
        var clamped = Math.Clamp(sample, -1f, 1f);
        var value = (short)(clamped * short.MaxValue);
        var offset = frameIndex * sizeof(short);
        pcm[offset] = (byte)(value & 0xFF);
        pcm[offset + 1] = (byte)((value >> 8) & 0xFF);
    }
}
