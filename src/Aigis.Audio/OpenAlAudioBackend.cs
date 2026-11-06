using System.Collections.Concurrent;
using Aigis.Foundation.Diagnostics;
using Silk.NET.OpenAL;

namespace Aigis.Audio;

/// <summary>
/// The real backend: opens the default OpenAL Soft device and plays clips through it.
/// <para>
/// Hard rule 6 demands this stay usable with no audio device at all — a headless CI box, a
/// sandboxed container, hardware with audio disabled — so every OpenAL call in this type is
/// wrapped so a failure degrades <see cref="IsAvailable"/> to false instead of throwing.
/// Construction itself never throws: if opening the device or creating the context fails, the
/// backend simply reports unavailable and every subsequent call behaves like
/// <see cref="NullAudioBackend"/> (a handle is still returned so the mixer's bookkeeping is
/// unaffected by whether a device happened to be present).
/// </para>
/// <para>
/// <b>TASK-124: one device+context per PROCESS, not per instance.</b> OpenAL sources belong to a
/// context, and only the most recently <c>MakeContextCurrent</c>-ed context is current per thread
/// — a call made against a source that was created under a DIFFERENT (now non-current) context
/// either silently no-ops or targets an invalid name. Before this fix, every instance opened and
/// made current its OWN device+context in its constructor, so <c>TriageShell</c>'s BGM mixer (one
/// long-lived instance) and each session's SFX mixer (a fresh instance per level) fought over
/// which context was current: the moment a session's backend was constructed, the BGM backend's
/// own later <see cref="StopVoice"/>/<see cref="Dispose"/> calls ran against the SESSION's context,
/// where the BGM voice's source name is meaningless — the stop silently failed and the title track
/// kept playing under the level track (the exact reported symptom). The fix below is a private,
/// process-wide, reference-counted holder (<see cref="SharedContext"/>): the first instance opens
/// the device+context, every later instance just takes a ref on the same one, and only the LAST
/// live instance's <see cref="Dispose"/> actually tears it down — so every instance's OpenAL calls
/// always land on the one context that is ever current on this thread, regardless of construction
/// order. Each instance still keeps its OWN <see cref="_voices"/> dictionary (unchanged), so
/// disposing one mixer continues to stop only the voices IT started.
/// </para>
/// </summary>
public sealed class OpenAlAudioBackend : IAudioBackend
{
    /// <summary>The process-wide OpenAL device+context, reference-counted across every
    /// <see cref="OpenAlAudioBackend"/> instance — see this type's own class doc comment (TASK-124)
    /// for why sharing one context is required rather than optional. Guarded by a lock because
    /// nothing about "which instance constructs/disposes first" is otherwise ordered — this is the
    /// same explicit-synchronization posture <see cref="AudioMixer"/>'s own class doc comment
    /// requires of callers that touch a mixer from more than one thread, applied here to the one
    /// piece of state that is genuinely shared rather than per-mixer.</summary>
    private static class SharedContext
    {
        private static readonly object Lock = new();
        private static int s_refCount;
        private static AL? s_al;
        private static ALContext? s_alc;
        private static unsafe Silk.NET.OpenAL.Device* s_device;
        private static unsafe Context* s_context;
        private static bool s_available;

        /// <summary>Takes one ref on the shared device+context, opening it first if this is the
        /// first live ref. Never throws (Hard rule 6) — a failure to open just leaves the returned
        /// <paramref name="al"/>/<paramref name="alc"/> null and the return value false, exactly as
        /// the old per-instance constructor used to degrade.</summary>
        public static unsafe bool Acquire(out AL? al, out ALContext? alc)
        {
            lock (Lock)
            {
                if (s_refCount == 0)
                {
                    Open();
                }
                else
                {
                    // TASK-124 (code-reviewer MEDIUM): "current" is per THREAD, not per process, so
                    // sharing the context object is necessary but not sufficient — a later instance
                    // constructed on a different thread than the one that ran Open() would otherwise
                    // have no current context at all, and every AL call it made would degrade
                    // silently into the very symptom this fix exists to remove. Re-asserting it is
                    // idempotent and cheap, so it is done on every acquire rather than reasoning
                    // about which thread got here first.
                    MakeCurrent();
                }

                s_refCount++;
                al = s_al;
                alc = s_alc;
                return s_available;
            }
        }

        private static unsafe void Open()
        {
            try
            {
                s_alc = ALContext.GetApi();
                s_device = s_alc.OpenDevice("");
                if (s_device is null)
                {
                    s_available = false;
                    return;
                }

                s_context = s_alc.CreateContext(s_device, null);
                if (s_context is null)
                {
                    s_available = false;
                    return;
                }

                if (!MakeCurrent())
                {
                    s_available = false;
                    return;
                }

                s_al = AL.GetApi();
                s_available = true;
            }
            catch (Exception ex) when (IsRecoverableDeviceFailure(ex))
            {
                s_available = false;
            }
        }

        /// <summary>Makes the shared context current on the CALLING thread, reporting whether it
        /// actually is. Idempotent, and a guarded false when there is no context to make current
        /// (no device on this machine) — callers must be able to call it without first knowing
        /// whether opening succeeded. Always called under <see cref="Lock"/>.</summary>
        private static unsafe bool MakeCurrent()
        {
            if (s_context is null || s_alc is null)
            {
                return false;
            }

            try
            {
                s_alc.MakeContextCurrent(s_context);
                return true;
            }
            catch (Exception ex) when (IsRecoverableDeviceFailure(ex))
            {
                ReportOnce("audio: make context current", ex);
                // A device lost since it was opened. Reported rather than thrown so both callers
                // degrade the same guarded way the rest of this type does (Hard rule 6).
                return false;
            }
        }

        /// <summary>Re-asserts the shared context on the calling thread when one is open; a no-op
        /// otherwise. See <see cref="OpenAlAudioBackend.MakeCurrentOnThisThread"/>.</summary>
        public static void Reassert()
        {
            lock (Lock)
            {
                if (s_refCount > 0)
                {
                    MakeCurrent();
                }
            }
        }

        /// <summary>Releases one ref; only the ref that brings the count to zero actually destroys
        /// the context and closes the device.</summary>
        public static unsafe void Release()
        {
            lock (Lock)
            {
                if (s_refCount <= 0)
                {
                    return;
                }

                s_refCount--;
                if (s_refCount > 0)
                {
                    return;
                }

                try
                {
                    if (s_context is not null)
                    {
                        s_alc?.DestroyContext(s_context);
                    }

                    if (s_device is not null)
                    {
                        s_alc?.CloseDevice(s_device);
                    }
                }
                catch (Exception ex) when (IsRecoverableDeviceFailure(ex))
                {
                    // Best-effort teardown; a device already gone is not an error at Dispose time.
                }

                s_al?.Dispose();
                s_alc?.Dispose();
                s_al = null;
                s_alc = null;
                s_device = null;
                s_context = null;
                s_available = false;
            }
        }
    }

    private readonly AL? _al;
    private readonly ALContext? _alc;
    private readonly ConcurrentDictionary<int, (uint Source, uint Buffer)> _voices = new();
    private int _nextHandle;
    private bool _disposed;

    /// <summary>Takes a ref on the process-wide shared device+context (opening it if this is the
    /// first live instance). Never throws.</summary>
    public OpenAlAudioBackend()
    {
        IsAvailable = SharedContext.Acquire(out _al, out _alc);
    }

    /// <summary>
    /// Makes the process's shared context current on the CALLING thread (idempotent, cheap, never
    /// throws; a no-op with no device open). For a host that opens a backend on a loading thread and
    /// then plays through it on another (2026-10-02: Triage builds a stage's session, audio device
    /// included, off the main thread). OpenAL Soft's <c>alcMakeContextCurrent</c> is in fact
    /// process-wide, so this is belt and braces — but it makes the code agree with the premise this
    /// type's own doc states, instead of depending on a detail of one implementation.
    /// </summary>
    public static void MakeCurrentOnThisThread() => SharedContext.Reassert();

    /// <inheritdoc />
    public bool IsAvailable { get; }

    /// <summary>What every OpenAL call in this type is guarded against. Deliberately does not
    /// include <see cref="AccessViolationException"/> — starting with .NET Core, corrupted-state
    /// exceptions from an unmanaged crash are not catchable by ordinary code at all (the CLR
    /// terminates the process), so listing it here would document a guarantee this type cannot
    /// actually give. What CAN happen — and is what this guards — is Silk.NET's own managed
    /// binding layer throwing when the native library is missing or a call is made against a
    /// context that was never created, plus a defensive general-exception net so a binding-layer
    /// change in a future Silk.NET version degrades to "unavailable" instead of taking the whole
    /// game down (Hard rule 6).</summary>
    /// <summary>
    /// Reports a survived device failure ONCE per kind per process (2026-10-15). Volume, pitch and
    /// voice queries run every frame, so a lost device would otherwise feed the error report and its
    /// on-screen notice for the rest of the session; the first occurrence, with its stack, is what
    /// explains a game gone silent. A machine with no audio device at all is not reported: that is an
    /// environment, not an error.
    /// </summary>
    private static void ReportOnce(string site, Exception error)
    {
        if (s_reportedFailures.TryAdd(site, 0))
        {
            ErrorSink.Report(site, error);
        }
    }

    private static readonly ConcurrentDictionary<string, byte> s_reportedFailures = new(StringComparer.Ordinal);

    private static bool IsRecoverableDeviceFailure(Exception ex) => ex is not OutOfMemoryException;

    /// <inheritdoc />
    public unsafe int Play(AudioClip clip, float volume, bool shouldLoop, float pitch = 1f)
    {
        ArgumentNullException.ThrowIfNull(clip);
        var handle = Interlocked.Increment(ref _nextHandle);

        if (!IsAvailable || _al is null)
        {
            return handle;
        }

        try
        {
            var source = _al.GenSource();
            var buffer = _al.GenBuffer();
            var format = clip.Format == AudioSampleFormat.Stereo16 ? BufferFormat.Stereo16 : BufferFormat.Mono16;

            fixed (byte* data = clip.Pcm)
            {
                _al.BufferData(buffer, format, data, clip.Pcm.Length, clip.SampleRate);
            }

            _al.SetSourceProperty(source, SourceInteger.Buffer, buffer);
            _al.SetSourceProperty(source, SourceBoolean.Looping, shouldLoop);
            _al.SetSourceProperty(source, SourceFloat.Gain, Math.Clamp(volume, 0f, 1f));
            // AL_PITCH is a resampling rate, so the clip also gets shorter as it rises -- see
            // IAudioBackend.Play. Clamped to OpenAL's own sane band: 0 would stall the voice
            // forever, and beyond ~4x the clip is a click rather than a sound.
            _al.SetSourceProperty(source, SourceFloat.Pitch, Math.Clamp(pitch, 0.25f, 4f));
            _al.SourcePlay(source);

            _voices[handle] = (source, buffer);
        }
        catch (Exception ex) when (IsRecoverableDeviceFailure(ex))
        {
            ReportOnce("audio: play", ex);
            // A device that stops responding mid-session must not crash the caller; the voice
            // simply never becomes audible.
        }

        return handle;
    }

    /// <inheritdoc />
    public void StopVoice(int voiceHandle)
    {
        if (_al is null || !_voices.TryRemove(voiceHandle, out var voice))
        {
            return;
        }

        try
        {
            _al.SourceStop(voice.Source);
            _al.DeleteSource(voice.Source);
            _al.DeleteBuffer(voice.Buffer);
        }
        catch (Exception ex) when (IsRecoverableDeviceFailure(ex))
        {
            ReportOnce("audio: stop", ex);
            // Already torn down by a device loss; nothing further to release.
        }
    }

    /// <inheritdoc />
    public void SetVolume(int voiceHandle, float volume)
    {
        if (_al is null || !_voices.TryGetValue(voiceHandle, out var voice))
        {
            return;
        }

        try
        {
            _al.SetSourceProperty(voice.Source, SourceFloat.Gain, Math.Clamp(volume, 0f, 1f));
        }
        catch (Exception ex) when (IsRecoverableDeviceFailure(ex))
        {
            ReportOnce("audio: set volume", ex);
            // Ignored — a volume change on a voice the device already dropped is a no-op.
        }
    }

    /// <inheritdoc />
    public void SetPitch(int voiceHandle, float pitch)
    {
        if (_al is null || !_voices.TryGetValue(voiceHandle, out var voice))
        {
            return;
        }

        try
        {
            // Same clamp as Play's own pitch argument, and for the same reasons stated there: 0 stalls the
            // voice forever and past ~4x a clip is a click.
            _al.SetSourceProperty(voice.Source, SourceFloat.Pitch, Math.Clamp(pitch, 0.25f, 4f));
        }
        catch (Exception ex) when (IsRecoverableDeviceFailure(ex))
        {
            ReportOnce("audio: set pitch", ex);
            // Ignored -- a rate change on a voice the device already dropped is a no-op.
        }
    }

    /// <inheritdoc />
    public bool IsPlaying(int voiceHandle)
    {
        if (_al is null || !_voices.TryGetValue(voiceHandle, out var voice))
        {
            return false;
        }

        try
        {
            _al.GetSourceProperty(voice.Source, GetSourceInteger.SourceState, out var state);
            return state == (int)SourceState.Playing;
        }
        catch (Exception ex) when (IsRecoverableDeviceFailure(ex))
        {
            ReportOnce("audio: query voice", ex);
            return false;
        }
    }

    /// <inheritdoc />
    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;

        // Stop only THIS instance's own voices before releasing its ref — unchanged behaviour from
        // before TASK-124 (see this type's class doc comment): a mixer's Dispose must not touch any
        // other mixer's still-live voices.
        foreach (var handle in _voices.Keys.ToArray())
        {
            StopVoice(handle);
        }

        // TASK-124: the shared device+context (and the AL/ALContext API wrappers over it) belong to
        // every live instance, not just this one — only SharedContext.Release() knows whether this
        // is the LAST ref and it is therefore safe to actually tear anything down.
        SharedContext.Release();
    }
}
