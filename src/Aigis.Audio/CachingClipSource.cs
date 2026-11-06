using System.Collections.Concurrent;

namespace Aigis.Audio;

/// <summary>
/// A process-wide, thread-safe memo over another <see cref="IClipSource"/>: each relative path is
/// read and decoded at most once, however many mixers, resolvers or threads ask for it.
/// <para>
/// <b>Why it exists (2026-10-02, 「ステージ開始にすごく時間がかかる」/「レベル途中に一瞬固まる」).</b> Every
/// session built its own <see cref="ClipResolver"/> over a fresh <see cref="FileClipSource"/>, so the
/// ~60 cues a round preloads were re-read and re-decoded from disk on EVERY stage start (measured
/// 120-200 ms warm, and 0.1-1.3 s per file on a busy HDD), and a clip a round did not preload was
/// decoded on the gameplay step that first played it. Sharing one of these between all of them makes
/// the second stage start pay nothing for audio, and lets a loading thread decode ahead of time for a
/// mixer that the main thread owns: the decoded clip lands here, and the main thread's resolver finds
/// it instead of touching the disk.
/// </para>
/// <para>
/// <b>Thread-safety.</b> <see cref="Lazy{T}"/> in its default ExecutionAndPublication mode, so two
/// threads asking for the same path at once decode it once and both get that clip. The decoded
/// <see cref="AudioClip"/> is an immutable record whose PCM is never written after decode, which is
/// what makes handing one instance to several mixers safe.
/// </para>
/// <para>
/// <b>Only a decoded clip is remembered.</b> A null (no file — most cues fall back to a generated tone —
/// but also a file a busy disk or a virus scanner refused for a moment) and a thrown exception are
/// both forgotten as soon as they are returned, so the next asker probes again. Remembering them would
/// turn one transient failure into a silent cue for the rest of the process, and the probe for a
/// missing file is a cheap directory lookup.
/// </para>
/// <para>
/// <b>Memory.</b> What it holds is bounded by the shipped audio — every cue and track the game can
/// play, decoded — and is what the per-session resolvers already held while a round lived (the BGM
/// mixer's own resolver always kept every track it had played for the life of the process). The
/// difference is that cues outlive the round that decoded them, which is the point.
/// </para>
/// </summary>
public sealed class CachingClipSource : IClipSource
{
    private readonly IClipSource _inner;
    private readonly ConcurrentDictionary<string, Lazy<AudioClip?>> _clips = new(StringComparer.Ordinal);

    /// <summary>Memoises <paramref name="inner"/>.</summary>
    public CachingClipSource(IClipSource inner)
    {
        ArgumentNullException.ThrowIfNull(inner);
        _inner = inner;
    }

    /// <summary>How many paths are held (decoded, or being decoded right now).</summary>
    public int Count => _clips.Count;

    /// <inheritdoc />
    public AudioClip? TryLoad(string relativePath)
    {
        if (string.IsNullOrWhiteSpace(relativePath))
        {
            return null;
        }

        var entry = _clips.GetOrAdd(relativePath, path => new Lazy<AudioClip?>(() => _inner.TryLoad(path)));
        AudioClip? clip;
        try
        {
            clip = entry.Value;
        }
        catch
        {
            // Lazy would otherwise replay this exception to every later asker.
            _clips.TryRemove(new KeyValuePair<string, Lazy<AudioClip?>>(relativePath, entry));
            throw;
        }

        if (clip is null)
        {
            // Removed only if it is still THIS entry, so a concurrent retry that already replaced it
            // keeps its own.
            _clips.TryRemove(new KeyValuePair<string, Lazy<AudioClip?>>(relativePath, entry));
        }

        return clip;
    }
}
