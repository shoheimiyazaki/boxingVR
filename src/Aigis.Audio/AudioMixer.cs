namespace Aigis.Audio;

/// <summary>One currently-tracked voice, queryable per Hard rule 6 so an agent surface can
/// perceive audio state headlessly rather than only make noise on a device.</summary>
/// <param name="SlotName">The <see cref="ClipSlot"/> (or loop key) this voice is playing.</param>
/// <param name="IsLoop">True for a voice on the loop bus.</param>
/// <param name="Volume">The volume this voice was started/last set at, 0..1.</param>
/// <param name="RefCount">For a loop voice, how many callers currently hold it open (see
/// <see cref="AudioMixer.StartLoop"/>). Always 1 for a one-shot.</param>
public sealed record AudioVoiceState(string SlotName, bool IsLoop, float Volume, int RefCount);

/// <summary>
/// §9.8's reference architecture: a one-shot bus and a loop bus kept separate, the loop bus
/// reference-counted so several simultaneous callers (e.g. two players bandaging at once) share
/// one loop voice and only the last one to stop ends it. Built on <see cref="IAudioBackend"/> +
/// <see cref="ClipResolver"/>, so it inherits both the headless-safe backend and the
/// tone-fallback resolution without knowing about either's internals.
/// <para>
/// <b>Not thread-safe by design, not by oversight.</b> Every caller today (<c>TriageGame</c>'s
/// step methods) runs on the single simulation thread Hard rule 5 already requires for physics
/// stepping, so <c>PlayOneShot</c>/<c>StartLoop</c>/<c>StopLoop</c>/<c>ActiveVoices</c> use plain
/// <see cref="List{T}"/>/<see cref="Dictionary{TKey,TValue}"/> rather than the
/// <c>System.Collections.Concurrent</c> types <see cref="ClipResolver"/> and the backends use for
/// their own, unrelated reason (those are read from ordinary code and are safe to share
/// regardless). If a caller ever needs to touch a mixer from more than one thread, that caller
/// must synchronize externally — do not assume this class's use of `Concurrent*` types elsewhere
/// in this library implies mixer-level thread safety.
/// </para>
/// </summary>
public sealed class AudioMixer : IDisposable
{
    /// <summary>The reference's default volume for one-shot cues.</summary>
    public const float DefaultVolume = 0.65f;

    private readonly IAudioBackend _backend;
    private readonly ClipResolver _resolver;

    private readonly List<(int Handle, string SlotName, float Volume)> _oneShots = [];
    private readonly Dictionary<string, LoopVoice> _loops = [];

    private sealed class LoopVoice
    {
        public int Handle;
        public float Volume;
        public int RefCount;

        /// <summary>The playback rate last sent to the device, so <c>SetLoopPitch</c> can skip a
        /// redundant per-step device call the same way <c>SetLoopVolume</c> does. Starts at 1
        /// because <see cref="StartLoop"/> plays at the clip's own rate.</summary>
        public float Pitch = 1f;
    }

    /// <param name="backend">Where sound actually plays. Ownership transfers to the mixer —
    /// <see cref="Dispose"/> disposes it.</param>
    /// <param name="resolver">Clip resolution (explicit/file/tone).</param>
    public AudioMixer(IAudioBackend backend, ClipResolver resolver)
    {
        ArgumentNullException.ThrowIfNull(backend);
        ArgumentNullException.ThrowIfNull(resolver);
        _backend = backend;
        _resolver = resolver;
    }

    /// <summary>Whether the underlying device is actually producing sound. False under headless
    /// CI or when no device was available — the mixer stays fully usable either way.</summary>
    public bool IsAvailable => _backend.IsAvailable;

    /// <summary>Every voice currently tracked as playing, on either bus — the queryable state
    /// Hard rule 6 requires. Finished one-shots are pruned lazily on each call.</summary>
    public IReadOnlyList<AudioVoiceState> ActiveVoices
    {
        get
        {
            PruneFinishedOneShots();
            var states = new List<AudioVoiceState>(_oneShots.Count + _loops.Count);
            foreach (var (_, slotName, volume) in _oneShots)
            {
                states.Add(new AudioVoiceState(slotName, IsLoop: false, volume, RefCount: 1));
            }

            foreach (var (slotName, voice) in _loops)
            {
                states.Add(new AudioVoiceState(slotName, IsLoop: true, voice.Volume, voice.RefCount));
            }

            return states;
        }
    }

    /// <summary>
    /// Resolves every slot in <paramref name="slots"/> now — reading and decoding its file, or
    /// generating its fallback tone — and caches the result, so the first
    /// <see cref="PlayOneShot"/>/<see cref="StartLoop"/> of that slot costs only the device call.
    /// <para>
    /// Resolution is otherwise lazy (see <see cref="ClipResolver.Resolve"/>), which means a file read
    /// plus an ogg/mp3 decode lands on whichever frame first triggers each cue — mid-round, at an
    /// unpredictable moment, for as many distinct cues as a session has. The work is unavoidable;
    /// doing it while a level is being built (where a pause is expected and invisible) rather than
    /// during play is the point. Idempotent and cheap to call again: an already-resolved slot is a
    /// dictionary hit.
    /// <para><b>Honest scope:</b> this is hot-path work moved to load time on principle, not a fix
    /// for a specific measured hitch. The one reproducible mid-round spike found while this was
    /// added (~80 ms at the first patient spawn on a windowed Level6 Triage run) turned out, by
    /// direct phase timing, to be dominated by <c>IGame.Step</c>; preloading did not move it.</para>
    /// </para>
    /// <para>
    /// Deliberately does NOT pre-create device buffers. <see cref="IAudioBackend"/> has no "prepare"
    /// verb and adding one would widen the backend contract for every implementation; the measured
    /// cost is the decode, and a short clip's buffer upload at play time is microseconds.
    /// </para>
    /// </summary>
    public void Preload(IEnumerable<ClipSlot> slots)
    {
        ArgumentNullException.ThrowIfNull(slots);
        foreach (var slot in slots)
        {
            if (slot is not null)
            {
                _resolver.Resolve(slot);
            }
        }
    }

    /// <summary>Plays <paramref name="slot"/> once on the one-shot bus at
    /// <see cref="DefaultVolume"/> scaled by <paramref name="volumeScale"/>, optionally resampled by
    /// <paramref name="pitch"/> (see <see cref="IAudioBackend.Play"/> — 1 is the clip as recorded).
    /// <para>Pitch is a per-VOICE property, deliberately not part of <see cref="ClipSlot"/>: a slot
    /// is the game's fixed table of what a cue IS, whereas the caller varying pitch is saying how
    /// hard THIS occurrence of it was. Putting it on the slot would mean cloning the slot per hit,
    /// which would also defeat <see cref="Preload"/>'s cache (keyed by slot).</para></summary>
    public void PlayOneShot(ClipSlot slot, float volumeScale = 1f, float pitch = 1f)
    {
        ArgumentNullException.ThrowIfNull(slot);

        // Reaped HERE, by the thing that allocates (user report, 2026-08-17: 「ゲームを続けていると効果音が
        // 消えることがあります」). Before this, pruning ran only when someone read ActiveVoices -- and nothing
        // in the game ever does, so in a real session it never ran at all. Driving it from the allocator
        // means the release cadence matches the allocation cadence by construction, with no cooperation
        // needed from a host that has no reason to know voices are a finite resource.
        PruneFinishedOneShots();

        var clip = _resolver.Resolve(slot);
        var volume = Math.Clamp(DefaultVolume * volumeScale, 0f, 1f);
        var handle = _backend.Play(clip, volume, shouldLoop: false, pitch);
        _oneShots.Add((handle, slot.Name, volume));
    }

    /// <summary>Starts (or joins) a reference-counted loop keyed by <paramref name="loopKey"/>.
    /// The first caller for a key actually starts the voice; later callers before the matching
    /// number of <see cref="StopLoop"/> calls just bump the reference count and share it — the
    /// "several bandagers, one loop" behaviour §9.8 calls out.</summary>
    public void StartLoop(string loopKey, ClipSlot slot, float volumeScale = 1f)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(loopKey);
        ArgumentNullException.ThrowIfNull(slot);

        if (_loops.TryGetValue(loopKey, out var existing))
        {
            existing.RefCount++;
            return;
        }

        var clip = _resolver.Resolve(slot);
        var volume = Math.Clamp(DefaultVolume * volumeScale, 0f, 1f);
        var handle = _backend.Play(clip, volume, shouldLoop: true);
        _loops[loopKey] = new LoopVoice { Handle = handle, Volume = volume, RefCount = 1 };
    }

    /// <summary>Releases one hold on the loop keyed by <paramref name="loopKey"/>; the voice stops
    /// only when the reference count reaches zero. Calling this for a key that is not open (extra
    /// stop, unknown key) is a guarded no-op rather than an error — a stray stop must not
    /// underflow another caller's still-active loop.</summary>
    public void StopLoop(string loopKey)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(loopKey);

        if (!_loops.TryGetValue(loopKey, out var voice))
        {
            return;
        }

        voice.RefCount--;
        if (voice.RefCount > 0)
        {
            return;
        }

        _backend.StopVoice(voice.Handle);
        _loops.Remove(loopKey);
    }

    /// <summary>
    /// Re-scales an open loop's volume, as <see cref="StartLoop"/>'s own <c>volumeScale</c> would have. A
    /// no-op for a key that is not open, on the same "a stray call must not disturb anything" footing as
    /// <see cref="StopLoop"/>.
    /// <para>
    /// Added 2026-08-17 for the ambulance siren, whose whole behaviour is a proximity fade (the reference
    /// writes <c>AudioSource.volume</c> every frame from how close the vehicle is to its stop). Without this
    /// a loop's volume was fixed at the moment it started — fine for BGM, useless for anything attached to a
    /// moving object.
    /// </para>
    /// </summary>
    public void SetLoopVolume(string loopKey, float volumeScale)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(loopKey);

        if (!_loops.TryGetValue(loopKey, out var voice))
        {
            return;
        }

        var volume = Math.Clamp(DefaultVolume * volumeScale, 0f, 1f);
        if (volume == voice.Volume)
        {
            // Skipped rather than re-sent: this is called every step for a vehicle that is usually holding a
            // steady note, and a device call per step per voice is pure waste.
            return;
        }

        voice.Volume = volume;
        _backend.SetVolume(voice.Handle, volume);
    }

    /// <summary>
    /// Re-scales an open loop's playback rate (1 = the clip as recorded) — see
    /// <see cref="IAudioBackend.SetPitch"/>. A no-op for a key that is not open.
    /// <para>This is what makes an engine note follow road speed instead of being chosen once; see
    /// <c>SetLoopVolume</c> for the same reasoning about loops attached to moving objects.</para>
    /// </summary>
    public void SetLoopPitch(string loopKey, float pitch)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(loopKey);

        if (!_loops.TryGetValue(loopKey, out var voice))
        {
            return;
        }

        if (pitch == voice.Pitch)
        {
            return;
        }

        voice.Pitch = pitch;
        _backend.SetPitch(voice.Handle, pitch);
    }

    /// <summary>Whether a loop is currently open under <paramref name="loopKey"/> — so a caller driving a
    /// vehicle's engine can tell "start it" from "keep updating it" without tracking that itself.</summary>
    public bool IsLoopOpen(string loopKey) => _loops.ContainsKey(loopKey);

    /// <summary>
    /// Hands this mixer a ready-made clip for <paramref name="slotName"/>, overriding the file and
    /// tone-generator stages for that slot from now on (<see cref="ClipResolver.AssignExplicit"/>).
    /// <para>
    /// Exposed 2026-08-17 for the vehicle sounds: the ambulance's siren and the van's engine are WAVEFORMS,
    /// not "a sweep between two frequencies", so they cannot be expressed as a <see cref="ClipSlot"/> for the
    /// tone generator to fill in — and the reference builds its siren in code too, so there is no file to
    /// resolve either. Going through the mixer keeps <see cref="ClipResolver"/> an implementation detail of it
    /// rather than something every game has to hold separately.
    /// </para>
    /// </summary>
    public void AssignClip(string slotName, AudioClip clip)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(slotName);
        ArgumentNullException.ThrowIfNull(clip);
        _resolver.AssignExplicit(slotName, clip);
    }

    /// <summary>
    /// Drops every finished one-shot from the list AND hands its device voice back.
    ///
    /// <para>
    /// <b>The release is the whole point, and its absence was a real bug</b> (user report, 2026-08-17:
    /// 「ゲームを続けていると効果音が消えることがあります」). This used to be a bare <c>RemoveAll</c>: the mixer
    /// forgot the voice and the backend kept it. <see cref="IAudioBackend.Play"/> is documented as allocating
    /// a voice, and <c>OpenAlAudioBackend</c> really does — one OpenAL source and one buffer per one-shot,
    /// freed only by <see cref="IAudioBackend.StopVoice"/> or on dispose. OpenAL implementations cap sources
    /// (OpenAL Soft defaults to 256) and past the cap hand out nothing usable, so a session leaked its way to
    /// silence while already-running loops kept playing. Footsteps alone are one cue per step, so "a while"
    /// was a couple of minutes.
    /// </para>
    ///
    /// <para>
    /// <b>Only one-shots.</b> A looping voice is owned by its reference count (<see cref="StartLoop"/>/
    /// <see cref="StopLoop"/>), not by what the device reports this instant, and it lives in a different
    /// collection — so this pass cannot reach the BGM even though a looping source is, by definition, never
    /// "finished".
    /// </para>
    /// </summary>
    private void PruneFinishedOneShots() => _oneShots.RemoveAll(voice =>
    {
        if (_backend.IsPlaying(voice.Handle))
        {
            return false;
        }

        // Safe for a handle the backend never registered (an unavailable device still returns a handle from
        // Play): StopVoice on an unknown handle is a documented no-op, so this needs no "was it real" flag.
        _backend.StopVoice(voice.Handle);
        return true;
    });

    /// <inheritdoc />
    public void Dispose() => _backend.Dispose();
}
