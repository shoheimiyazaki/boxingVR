using System.Collections.Concurrent;

namespace Aigis.Audio;

/// <summary>
/// A backend that plays nothing but still behaves like a real one from the caller's point of
/// view: <see cref="Play"/> returns a real handle, and <see cref="IsPlaying"/> reports a one-shot
/// as active for the clip's own duration (looped voices stay active until explicitly stopped),
/// and none of it throws. Used when no OpenAL device is available (headless CI, containers, audio
/// hardware disabled) so <see cref="AudioMixer"/>'s bookkeeping — what is "playing", from where,
/// at what volume — stays queryable per Hard rule 6 even with zero sound actually produced.
/// </summary>
public sealed class NullAudioBackend : IAudioBackend
{
    private readonly record struct Voice(bool Loop, long ExpiresAtTicks);

    private readonly ConcurrentDictionary<int, Voice> _activeVoices = new();
    private readonly Func<long> _clockTicks;
    private int _nextHandle;

    /// <param name="clockTicks">Wall-clock source in <see cref="DateTime.Ticks"/> units, used only
    /// to time out a one-shot voice as if it had actually played. Defaults to
    /// <see cref="DateTime.UtcNow"/> — injectable so a test can fast-forward without a real
    /// sleep.</param>
    public NullAudioBackend(Func<long>? clockTicks = null)
    {
        _clockTicks = clockTicks ?? (() => DateTime.UtcNow.Ticks);
    }

    /// <inheritdoc />
    public bool IsAvailable => false;

    /// <inheritdoc />
    public int Play(AudioClip clip, float volume, bool shouldLoop, float pitch = 1f)
    {
        ArgumentNullException.ThrowIfNull(clip);
        var handle = Interlocked.Increment(ref _nextHandle);
        // A resampled voice really is shorter/longer, and this backend's whole job is to answer
        // IsPlaying honestly without a device -- so the pitch divides the modelled duration rather
        // than being ignored. Guarded against a zero/negative rate, which would mean "never ends".
        var durationTicks = (long)(clip.DurationSeconds / MathF.Max(0.01f, pitch) * TimeSpan.TicksPerSecond);
        _activeVoices[handle] = new Voice(shouldLoop, _clockTicks() + durationTicks);
        return handle;
    }

    /// <inheritdoc />
    public void StopVoice(int voiceHandle) => _activeVoices.TryRemove(voiceHandle, out _);

    /// <inheritdoc />
    public void SetVolume(int voiceHandle, float volume)
    {
        // No device to adjust; the handle's existence is all this backend tracks.
    }

    /// <inheritdoc />
    /// <inheritdoc />
    public void SetPitch(int voiceHandle, float pitch)
    {
        // Recorded, not applied: this backend has no device, and its only job is to report what WOULD have
        // been played (see this class's own doc comment). Pitch does not change what a caller can observe
        // here, so unlike SetVolume there is nothing to store.
        _ = voiceHandle;
        _ = pitch;
    }

    public bool IsPlaying(int voiceHandle)
    {
        if (!_activeVoices.TryGetValue(voiceHandle, out var voice))
        {
            return false;
        }

        return voice.Loop || _clockTicks() < voice.ExpiresAtTicks;
    }

    /// <inheritdoc />
    public void Dispose() => _activeVoices.Clear();
}
