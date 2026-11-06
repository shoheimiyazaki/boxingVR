namespace Aigis.Audio;

/// <summary>
/// An <see cref="IAudioBackend"/> that renders the mix into a buffer instead of onto a device — the
/// audio half of "every render/audio path must be runnable headless" (hard rule 6), and what lets a
/// headless tool record a session's sound (2026-08-29 user request: 「音がないみたいです。実機と
/// おなじ音が鳴るようにしてください」).
///
/// <para>
/// <b>It is a backend rather than a re-implementation of the game's audio.</b> Everything above it
/// — which cue a <c>TriageEvent</c> maps to, the 0.65 default gain, the reference-counted loops, the
/// engine's pitch following road speed, the trench's synthesised rifle fire — is
/// <see cref="AudioMixer"/>'s and the game's own work, unchanged. A recording made through this is
/// therefore the sound the game actually plays, not a second guess at it, which is exactly what
/// <c>tools/Aigis.HighlightVideo</c>'s soundtrack could NOT claim: that one re-derives a track from
/// a recorded highlight's trigger list, because a finished reel has no mixer left to listen to.
/// </para>
///
/// <para>
/// <b>Time advances only when the caller says so.</b> Nothing here is driven by a clock — the host
/// calls <see cref="Advance"/> with the number of output frames one simulation step is worth, so the
/// track is sample-accurately aligned to the steps that produced it and a recording is reproducible
/// rather than dependent on how fast the machine ran. At 44.1 kHz and this engine's 60 Hz fixed
/// step that is exactly 735 frames per step, with no drift to accumulate.
/// </para>
///
/// <para>
/// <b>Single-threaded by contract.</b> One backend belongs to one session on one thread; it takes no
/// locks. A host filming several levels at once gives each its own instance (which is what
/// <c>tools/Aigis.GameplayVideo</c> does), because two sessions sharing a mixer would be two
/// sessions sharing a soundtrack.
/// </para>
/// </summary>
public sealed class RecordingAudioBackend : IAudioBackend
{
    /// <summary>Output rate. 44.1 kHz because that is what every clip in this engine already is
    /// (<see cref="ToneGenerator"/>'s generated cues and the copied assets alike), so the common
    /// case resamples by exactly 1 and the nearest-frame resampler below never runs.</summary>
    public const int SampleRate = 44100;

    private sealed class Voice
    {
        public required AudioClip Clip { get; init; }

        public required bool Loop { get; init; }

        /// <summary>Clip frames per output frame — the resampling ratio, pitch included.</summary>
        public double Step { get; set; }

        /// <summary>Playback position in CLIP frames. Fractional and read by truncation: nearest-frame
        /// resampling, the same choice <c>HighlightSoundtrack</c> documents, and audibly rather than
        /// silently wrong if an asset ever arrives at another rate.</summary>
        public double Position { get; set; }

        public float Volume { get; set; }

        public int Frames { get; init; }

        public bool Finished { get; set; }
    }

    private readonly Dictionary<int, Voice> _voices = [];
    private readonly List<float> _left = [];
    private readonly List<float> _right = [];

    private int _nextHandle = 1;

    /// <summary>Always true: this backend has no device to fail to open, and a mixer that thought it
    /// was unavailable would have every reason to skip work whose output is the entire point here.</summary>
    public bool IsAvailable => true;

    /// <summary>Output frames recorded so far. Multiply by <see cref="SampleRate"/>'s reciprocal for
    /// seconds; compare against the video's frame count to prove the two are aligned.</summary>
    public int RecordedFrames => _left.Count;

    /// <summary>Largest absolute sample in the recording, 0..1+. The honest measure of whether a cut
    /// has any sound in it at all — a track that peaks at 0.001 is silence with a rounding error,
    /// and a caller writing a video file should say so rather than ship it.</summary>
    public float Peak
    {
        get
        {
            var peak = 0f;
            for (var i = 0; i < _left.Count; i++)
            {
                peak = MathF.Max(peak, MathF.Max(MathF.Abs(_left[i]), MathF.Abs(_right[i])));
            }

            return peak;
        }
    }

    public int Play(AudioClip clip, float volume, bool shouldLoop, float pitch = 1f)
    {
        ArgumentNullException.ThrowIfNull(clip);
        var handle = _nextHandle++;
        var frames = FrameCount(clip);
        if (frames <= 0)
        {
            // A zero-length clip still gets a handle -- IAudioBackend's contract is that Play never
            // fails -- it is simply finished before it starts.
            _voices[handle] = new Voice { Clip = clip, Loop = false, Frames = 0, Finished = true, Volume = volume };
            return handle;
        }

        _voices[handle] = new Voice
        {
            Clip = clip,
            Loop = shouldLoop,
            Frames = frames,
            Volume = Math.Clamp(volume, 0f, 1f),
            Step = StepFor(clip, pitch),
        };
        return handle;
    }

    public void StopVoice(int voiceHandle)
    {
        if (_voices.TryGetValue(voiceHandle, out var voice))
        {
            voice.Finished = true;
        }
    }

    public void SetVolume(int voiceHandle, float volume)
    {
        if (_voices.TryGetValue(voiceHandle, out var voice))
        {
            voice.Volume = Math.Clamp(volume, 0f, 1f);
        }
    }

    public void SetPitch(int voiceHandle, float pitch)
    {
        if (_voices.TryGetValue(voiceHandle, out var voice))
        {
            voice.Step = StepFor(voice.Clip, pitch);
        }
    }

    public bool IsPlaying(int voiceHandle) =>
        _voices.TryGetValue(voiceHandle, out var voice) && !voice.Finished;

    /// <summary>
    /// Advances every voice by <paramref name="frames"/> output frames, appending the mix to the
    /// recording when <paramref name="record"/> is true and discarding it otherwise.
    /// <para>
    /// <b>Discarding still advances.</b> A host that warms a session up before the camera rolls has
    /// to run the audio through that warm-up rather than skip it, or every loop the warm-up started
    /// (an ambulance engine, the trench's ambient bed) would begin the recording at frame zero of
    /// its clip instead of wherever it had actually got to.
    /// </para>
    /// </summary>
    public void Advance(int frames, bool record = true)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(frames);
        if (frames == 0)
        {
            return;
        }

        var start = _left.Count;
        if (record)
        {
            for (var i = 0; i < frames; i++)
            {
                _left.Add(0f);
                _right.Add(0f);
            }
        }

        foreach (var voice in _voices.Values)
        {
            if (voice.Finished || voice.Frames <= 0)
            {
                continue;
            }

            for (var i = 0; i < frames; i++)
            {
                var frame = (int)voice.Position;
                if (frame >= voice.Frames)
                {
                    if (!voice.Loop)
                    {
                        voice.Finished = true;
                        break;
                    }

                    // Wrap by the clip's length rather than resetting to 0, so a loop keeps its
                    // sub-frame phase and does not drift a fraction of a sample every cycle.
                    voice.Position -= voice.Frames;
                    frame = (int)voice.Position;
                }

                if (record)
                {
                    Mix(voice, frame, start + i);
                }

                voice.Position += voice.Step;
            }

            // Retired HERE as well as inside the loop, because a clip that ends exactly on the last
            // frame of this advance never re-enters the loop to notice. Without it a one-shot stays
            // "playing" until somebody advances again -- which is a lie IAudioBackend.IsPlaying is
            // documented not to tell, and which AudioMixer's own one-shot pruning reads.
            if (!voice.Loop && voice.Position >= voice.Frames)
            {
                voice.Finished = true;
            }
        }

        Reap();
    }

    /// <summary>Appends <paramref name="frames"/> of silence without moving any voice — a film that holds one picture
    /// while the game is stopped keeps its track in step, and every sound carries on from where it was afterwards.</summary>
    public void AppendSilence(int frames)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(frames);
        for (var i = 0; i < frames; i++)
        {
            _left.Add(0f);
            _right.Add(0f);
        }
    }

    /// <summary>Drops the recording so far, keeping every voice exactly where it is — how a host
    /// starts recording at the end of a warm-up without restarting the sounds that warm-up
    /// began.</summary>
    public void ResetRecording()
    {
        _left.Clear();
        _right.Clear();
    }

    /// <summary>The recording as a 16-bit stereo WAV. Stereo with both channels equal for mono
    /// content: this engine's clips are mono and its mixer has no spatialisation, so a stereo file
    /// is a container choice (what every encoder and editor expects), not a claim about the mix.</summary>
    public void WriteWav(string path)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        var directory = Path.GetDirectoryName(Path.GetFullPath(path));
        if (!string.IsNullOrEmpty(directory))
        {
            Directory.CreateDirectory(directory);
        }

        using var stream = File.Create(path);
        using var writer = new BinaryWriter(stream);
        var dataBytes = _left.Count * 2 * sizeof(short);
        writer.Write("RIFF"u8);
        writer.Write(36 + dataBytes);
        writer.Write("WAVE"u8);
        writer.Write("fmt "u8);
        writer.Write(16);
        writer.Write((short)1);
        writer.Write((short)2);
        writer.Write(SampleRate);
        writer.Write(SampleRate * 2 * sizeof(short));
        writer.Write((short)(2 * sizeof(short)));
        writer.Write((short)16);
        writer.Write("data"u8);
        writer.Write(dataBytes);
        for (var i = 0; i < _left.Count; i++)
        {
            writer.Write(ToPcm16(_left[i]));
            writer.Write(ToPcm16(_right[i]));
        }
    }

    public void Dispose()
    {
        _voices.Clear();
        _left.Clear();
        _right.Clear();
    }

    private void Mix(Voice voice, int clipFrame, int trackFrame)
    {
        var clip = voice.Clip;
        var gain = voice.Volume;
        if (clip.Format == AudioSampleFormat.Mono16)
        {
            var at = clipFrame * 2;
            if (at + 1 >= clip.Pcm.Length)
            {
                return;
            }

            var sample = BitConverter.ToInt16(clip.Pcm, at) / (float)short.MaxValue * gain;
            _left[trackFrame] += sample;
            _right[trackFrame] += sample;
            return;
        }

        var stereoAt = clipFrame * 4;
        if (stereoAt + 3 >= clip.Pcm.Length)
        {
            return;
        }

        _left[trackFrame] += BitConverter.ToInt16(clip.Pcm, stereoAt) / (float)short.MaxValue * gain;
        _right[trackFrame] += BitConverter.ToInt16(clip.Pcm, stereoAt + 2) / (float)short.MaxValue * gain;
    }

    /// <summary>Forgets finished one-shots. <see cref="AudioMixer"/> prunes its own bookkeeping on
    /// allocation, but it never tells a backend that a voice is gone, so without this a long session
    /// accumulates one dictionary entry per sound effect ever played.</summary>
    private void Reap()
    {
        if (_voices.Count < 256)
        {
            return;
        }

        foreach (var handle in _voices.Where(pair => pair.Value.Finished).Select(pair => pair.Key).ToList())
        {
            _voices.Remove(handle);
        }
    }

    private static double StepFor(AudioClip clip, float pitch)
    {
        var rate = clip.SampleRate <= 0 ? SampleRate : clip.SampleRate;
        var safePitch = float.IsFinite(pitch) && pitch > 0f ? pitch : 1f;
        return safePitch * rate / (double)SampleRate;
    }

    private static int FrameCount(AudioClip clip) => clip.Format switch
    {
        AudioSampleFormat.Mono16 => clip.Pcm.Length / 2,
        AudioSampleFormat.Stereo16 => clip.Pcm.Length / 4,
        _ => 0,
    };

    private static short ToPcm16(float value) => (short)(Math.Clamp(value, -1f, 1f) * short.MaxValue);
}
