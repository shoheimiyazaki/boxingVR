namespace Aigis.Audio;

/// <summary>PCM sample layout. Aigis only ever plays mono content today (SFX/BGM cues have no
/// stereo positioning need), but the enum exists so a decoded stereo file is not silently
/// downmixed by an assumption baked into the type.</summary>
public enum AudioSampleFormat
{
    /// <summary>1 channel, 16-bit signed PCM, little-endian.</summary>
    Mono16,

    /// <summary>2 channels, 16-bit signed PCM, little-endian, interleaved.</summary>
    Stereo16,
}

/// <summary>
/// One decoded (or generated) sound: raw PCM bytes plus enough metadata to hand to an OpenAL
/// buffer. The record itself is immutable (CLAUDE.md's immutability rule: a caller that wants a
/// different clip constructs a new one), but <see cref="Pcm"/>'s element type — a raw
/// <c>byte[]</c>, chosen so <see cref="OpenAlAudioBackend"/> can pin it directly for
/// <c>AL.BufferData</c> with no extra copy — is itself mutable at the CLR level and
/// <see cref="ClipResolver"/> caches the exact array instance across every <c>Resolve</c> of a
/// slot. Treat it as read-only: writing through it corrupts every future caller's cached copy of
/// the same clip.
/// </summary>
/// <param name="Name">The clip's logical name (slot name or file name), for diagnostics and for
/// <see cref="AudioMixer"/>'s queryable playing-state (Hard rule 6).</param>
/// <param name="Format">Sample layout.</param>
/// <param name="SampleRate">Samples per second per channel, e.g. 44100.</param>
/// <param name="Pcm">Raw interleaved PCM bytes matching <see cref="Format"/>. See the class
/// remarks — read-only by convention, not by the type system.</param>
/// <param name="IsGenerated">True when this clip came from <see cref="ToneGenerator"/> rather than
/// a decoded file — surfaced so a caller (or a test) can tell a real asset from a fallback tone
/// without string-matching the name.</param>
public sealed record AudioClip(string Name, AudioSampleFormat Format, int SampleRate, byte[] Pcm, bool IsGenerated)
{
    /// <summary>Clip length in seconds, derived from the PCM byte count.</summary>
    public float DurationSeconds
    {
        get
        {
            var channels = Format == AudioSampleFormat.Stereo16 ? 2 : 1;
            var bytesPerFrame = channels * sizeof(short);
            if (bytesPerFrame == 0 || SampleRate <= 0)
            {
                return 0f;
            }

            return Pcm.Length / (float)bytesPerFrame / SampleRate;
        }
    }
}
