namespace Aigis.Audio;

/// <summary>
/// The device-facing half of the audio layer: turns a resolved <see cref="AudioClip"/> into
/// actual sound on whatever backend is available. Abstracted behind an interface for the same
/// reason <c>Aigis.Physics</c> hides BepuPhysics (Hard rule 1's sibling for audio) — and because
/// Hard rule 6 requires every render/audio path to keep working headless: a backend with no
/// device must degrade gracefully rather than take the caller down with it.
/// </summary>
public interface IAudioBackend : IDisposable
{
    /// <summary>False when no audio device could be opened (headless CI, a sandboxed container,
    /// a machine with audio hardware disabled). <see cref="Play"/> must still succeed and return a
    /// usable (silent) handle in that case — the mixer's queryable state stays meaningful even
    /// when nothing is audible.</summary>
    public bool IsAvailable { get; }

    /// <summary>Starts playing <paramref name="clip"/> and returns a voice handle for
    /// <see cref="StopVoice"/>/<see cref="SetVolume"/>/<see cref="IsPlaying"/>. Never throws: a
    /// decode or device error is reported as a non-null handle that simply is not audible, because
    /// a dropped sound effect must not crash a running game.</summary>
    /// <param name="clip">What to play.</param>
    /// <param name="volume">Gain, 0..1.</param>
    /// <param name="shouldLoop">Whether the voice repeats until stopped.</param>
    /// <param name="pitch">Playback-rate multiplier: 1 is the clip as recorded, 2 an octave up and
    /// half as long, 0.5 an octave down and twice as long. Resampling, not a pitch shifter — the
    /// clip's duration moves with its pitch, which is exactly what makes a harder impact read as a
    /// sharper, shorter crack rather than as the same sample played louder (its one caller today is
    /// <c>Game.Triage</c>'s damage-scaled axe cue). A backend that cannot vary rate must ignore this
    /// rather than refuse the voice.</param>
    public int Play(AudioClip clip, float volume, bool shouldLoop, float pitch = 1f);

    /// <summary>Stops the given voice, if still active. A no-op for an already-stopped or unknown
    /// handle.</summary>
    public void StopVoice(int voiceHandle);

    /// <summary>Adjusts a currently playing voice's volume, 0..1.</summary>
    public void SetVolume(int voiceHandle, float volume);

    /// <summary>
    /// Adjusts a currently playing voice's playback rate, where 1 is the clip as recorded — the live
    /// counterpart of <see cref="Play"/>'s own <c>pitch</c> argument, with the same meaning and the same
    /// clamp. A no-op for an unknown handle.
    /// <para>
    /// Added 2026-08-17 for vehicle engines, which are the first sound in this game whose pitch has to
    /// FOLLOW something continuous (road speed) rather than be chosen once when the cue fires. Pitch on a
    /// looping voice is exactly how the reference does its own engine note (Unity's
    /// <c>AmbulanceController.UpdateEngineAudio</c> writes <c>AudioSource.pitch</c> every frame), so this is
    /// the port of a real mechanism rather than a new idea.
    /// </para>
    /// </summary>
    public void SetPitch(int voiceHandle, float pitch);

    /// <summary>True while the given voice is still producing sound (false once a one-shot
    /// finishes, after <see cref="StopVoice"/>, or for an unknown handle).</summary>
    public bool IsPlaying(int voiceHandle);
}
