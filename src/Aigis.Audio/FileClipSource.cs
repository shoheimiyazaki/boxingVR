using Aigis.Foundation.Diagnostics;
using NLayer;
using NVorbis;

namespace Aigis.Audio;

/// <summary>
/// Decodes a real clip file (<c>.ogg</c> via NVorbis, <c>.mp3</c> via NLayer, <c>.wav</c> via a
/// minimal hand-rolled PCM reader) from a root directory on disk. This is stage 2 of
/// <see cref="ClipResolver"/>'s three-stage resolution.
/// <para>
/// Every failure mode — missing file, missing root directory, corrupt data, an unsupported
/// extension — returns null rather than throwing (Hard rule 6: a library must stay usable
/// headless, and a game must not crash because one asset went missing or a decoder choked on a
/// truncated download).
/// </para>
/// </summary>
public sealed class FileClipSource : IClipSource
{
    private readonly string _rootDirectory;

    /// <param name="rootDirectory">Directory that <see cref="TryLoad"/>'s relative paths are
    /// resolved against. Need not exist at construction time — TASK-068 ships with no clip
    /// assets, so every lookup is expected to miss until a later task adds them.</param>
    public FileClipSource(string rootDirectory)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(rootDirectory);
        _rootDirectory = rootDirectory;
    }

    /// <inheritdoc />
    public AudioClip? TryLoad(string relativePath)
    {
        if (string.IsNullOrWhiteSpace(relativePath))
        {
            return null;
        }

        // Security: relativePath is untrusted by the type system even though every caller inside
        // this codebase today only ever passes ClipSlot.EffectiveResourcePath (hard-coded slot
        // names). Path.Combine happily returns an absolute path unchanged if relativePath IS
        // absolute, and "../" segments escape _rootDirectory even when it is not — so the
        // resolved path is verified to still be UNDER _rootDirectory before anything touches the
        // filesystem, rather than trusting the combine.
        var rootFull = Path.GetFullPath(_rootDirectory);
        var fullPath = Path.GetFullPath(Path.Combine(rootFull, relativePath));
        var rootWithSeparator = rootFull.EndsWith(Path.DirectorySeparatorChar) ? rootFull : rootFull + Path.DirectorySeparatorChar;
        if (!fullPath.StartsWith(rootWithSeparator, StringComparison.Ordinal))
        {
            return null;
        }

        if (!File.Exists(fullPath))
        {
            return null;
        }

        try
        {
            return Path.GetExtension(fullPath).ToLowerInvariant() switch
            {
                ".ogg" => DecodeOgg(fullPath),
                ".mp3" => DecodeMp3(fullPath),
                ".wav" => DecodeWav(fullPath),
                _ => null,
            };
        }
        catch (Exception ex) when (ex is IOException or InvalidDataException or EndOfStreamException
            or ArgumentException or OverflowException or OutOfMemoryException)
        {
            // Survived, but no longer silently (2026-10-15): which file, and why.
            ErrorSink.Report("audio: clip decode", new InvalidDataException($"'{Path.GetFileName(fullPath)}' could not be decoded.", ex));
            // A corrupt/truncated/adversarial asset degrades to "no file" for the resolver rather
            // than crashing the caller — the whole point of the three-stage fallback is that a bad
            // clip on disk is no worse than an absent one. ArgumentException covers a WAV chunk
            // size gone negative (BinaryReader.ReadBytes rejects it); OverflowException/
            // OutOfMemoryException cover a chunk size/sample count claimed far larger than the
            // file actually is.
            return null;
        }
    }

    /// <summary>Sanity ceiling on a single decoded clip: ~1 hour of mono 44.1 kHz audio. A real
    /// SFX/BGM asset is nowhere near this; it exists so a corrupt or adversarial OGG header
    /// claiming an enormous <c>TotalSamples</c> cannot force a multi-gigabyte allocation before
    /// any actual sample data has been read.</summary>
    private const long MaxDecodedSamples = 44_100L * 3600;

    private static AudioClip DecodeOgg(string fullPath)
    {
        using var reader = new VorbisReader(fullPath);
        var channels = reader.Channels;
        var sampleRate = reader.SampleRate;

        var totalSamples = Math.Min(reader.TotalSamples, MaxDecodedSamples);
        var floatBuffer = new float[totalSamples * channels];
        var read = reader.ReadSamples(floatBuffer, 0, floatBuffer.Length);

        var pcm = new byte[read * sizeof(short)];
        for (var i = 0; i < read; i++)
        {
            WriteInt16Le(pcm, i * sizeof(short), floatBuffer[i]);
        }

        var format = channels >= 2 ? AudioSampleFormat.Stereo16 : AudioSampleFormat.Mono16;
        return new AudioClip(Path.GetFileNameWithoutExtension(fullPath), format, sampleRate, pcm, IsGenerated: false);
    }

    private static AudioClip DecodeMp3(string fullPath)
    {
        using var stream = File.OpenRead(fullPath);
        using var mpegFile = new MpegFile(stream);
        var channels = mpegFile.Channels;
        var sampleRate = mpegFile.SampleRate;

        using var memory = new MemoryStream();
        var chunk = new float[4096];
        int read;
        while ((read = mpegFile.ReadSamples(chunk, 0, chunk.Length)) > 0)
        {
            var bytes = new byte[read * sizeof(short)];
            for (var i = 0; i < read; i++)
            {
                WriteInt16Le(bytes, i * sizeof(short), chunk[i]);
            }

            memory.Write(bytes, 0, bytes.Length);
        }

        var format = channels >= 2 ? AudioSampleFormat.Stereo16 : AudioSampleFormat.Mono16;
        return new AudioClip(Path.GetFileNameWithoutExtension(fullPath), format, sampleRate, memory.ToArray(), IsGenerated: false);
    }

    /// <summary>Minimal PCM <c>.wav</c> reader: 16-bit or 24-bit integer PCM (the formats real
    /// asset pipelines that feed this engine actually produce — TASK-111 found several
    /// Pro-Tools-exported 24-bit/96kHz assets among the real Triage SFX, not just 16-bit ones),
    /// just enough to avoid a third decoding package for the one format that needs no compression
    /// library at all. 24-bit samples are truncated to 16-bit (top two bytes of each 3-byte
    /// sample) since <see cref="AudioSampleFormat"/> only has 16-bit formats — the same
    /// depth-reduction a DAW does on a "export as 16-bit" bounce, just without dithering.</summary>
    private static AudioClip DecodeWav(string fullPath)
    {
        using var stream = File.OpenRead(fullPath);
        using var br = new BinaryReader(stream);

        if (new string(br.ReadChars(4)) != "RIFF")
        {
            throw new InvalidDataException("Not a RIFF file.");
        }

        br.ReadInt32(); // chunk size, unused
        if (new string(br.ReadChars(4)) != "WAVE")
        {
            throw new InvalidDataException("Not a WAVE file.");
        }

        short channels = 1;
        int sampleRate = 44100;
        short bitsPerSample = 16;
        byte[]? data = null;

        while (stream.Position < stream.Length - 8)
        {
            var chunkId = new string(br.ReadChars(4));
            var chunkSize = br.ReadInt32();

            // Security: chunkSize is untrusted file content. Reject anything negative outright
            // (BinaryReader.ReadBytes would throw ArgumentOutOfRangeException — caught above, but
            // failing fast here is clearer) and anything larger than what is actually left in the
            // stream, which both a corrupt file and an adversarial one claiming a chunk far bigger
            // than the file itself would otherwise turn into an oversized allocation attempt.
            var bytesLeft = stream.Length - stream.Position;
            if (chunkSize < 0 || chunkSize > bytesLeft)
            {
                throw new InvalidDataException($"WAVE chunk '{chunkId}' claims size {chunkSize}, only {bytesLeft} bytes remain.");
            }

            if (chunkId == "fmt ")
            {
                br.ReadInt16(); // audio format
                channels = br.ReadInt16();
                sampleRate = br.ReadInt32();
                br.ReadInt32(); // byte rate
                br.ReadInt16(); // block align
                bitsPerSample = br.ReadInt16();
                var remaining = chunkSize - 16;
                if (remaining > 0)
                {
                    br.ReadBytes(remaining);
                }
            }
            else if (chunkId == "data")
            {
                data = br.ReadBytes(chunkSize);
            }
            else
            {
                br.ReadBytes(chunkSize);
            }

            // RIFF pads every chunk to an even byte boundary: an odd-sized chunk is followed by
            // one unused padding byte before the next chunk's ID. TASK-111 found real Triage
            // assets (e.g. syringe.wav, a Pro-Tools/Broadcast-WAV export) with odd-sized trailing
            // metadata chunks (DGDA/iXML) — without skipping this byte, the next chunk's 4-byte ID
            // is read one byte short, the corrupted ID's "chunkSize" comes out as garbage, the
            // bytesLeft guard above throws, and the WHOLE decode is discarded even though the
            // actual "data" chunk had already been read correctly earlier in the file.
            if (chunkSize % 2 != 0 && stream.Position < stream.Length)
            {
                br.ReadByte();
            }
        }

        if (data is null)
        {
            throw new InvalidDataException("WAVE file has no data chunk.");
        }

        var pcm16 = bitsPerSample switch
        {
            16 => data,
            24 => ConvertPcm24ToPcm16(data),
            _ => throw new InvalidDataException($"Only 16-bit or 24-bit PCM WAVE is supported (got {bitsPerSample}-bit)."),
        };

        var format = channels >= 2 ? AudioSampleFormat.Stereo16 : AudioSampleFormat.Mono16;
        return new AudioClip(Path.GetFileNameWithoutExtension(fullPath), format, sampleRate, pcm16, IsGenerated: false);
    }

    /// <summary>Truncates 24-bit little-endian signed PCM to 16-bit by dropping each sample's
    /// low-order byte — the top two of its three bytes are exactly a 16-bit sample once
    /// sign-extended, so no resampling or scaling is needed, only a narrower re-pack.</summary>
    private static byte[] ConvertPcm24ToPcm16(byte[] pcm24)
    {
        var sampleCount = pcm24.Length / 3;
        var pcm16 = new byte[sampleCount * sizeof(short)];
        for (var i = 0; i < sampleCount; i++)
        {
            var offset24 = i * 3;
            var raw = pcm24[offset24] | (pcm24[offset24 + 1] << 8) | (pcm24[offset24 + 2] << 16);
            if ((raw & 0x0080_0000) != 0)
            {
                raw |= unchecked((int)0xFF00_0000); // sign-extend 24-bit -> 32-bit
            }

            var sample16 = (short)(raw >> 8);
            var offset16 = i * sizeof(short);
            pcm16[offset16] = (byte)(sample16 & 0xFF);
            pcm16[offset16 + 1] = (byte)((sample16 >> 8) & 0xFF);
        }

        return pcm16;
    }

    private static void WriteInt16Le(byte[] buffer, int offset, float sample)
    {
        var clamped = Math.Clamp(sample, -1f, 1f);
        var value = (short)(clamped * short.MaxValue);
        buffer[offset] = (byte)(value & 0xFF);
        buffer[offset + 1] = (byte)((value >> 8) & 0xFF);
    }
}
