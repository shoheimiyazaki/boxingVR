using System.Collections.Concurrent;

namespace Aigis.Audio;

/// <summary>
/// §9.8's three-stage clip resolution, ported from the reference's <c>Resolve</c>: an explicit
/// assignment wins if present, otherwise a real asset is loaded from disk, otherwise a
/// procedurally generated tone stands in — so a missing asset degrades to an audible cue rather
/// than silence. Every resolved clip is cached by slot name so repeated plays of the same slot
/// (footsteps, the AED countdown beep) do not re-decode or re-synthesize on every call.
/// </summary>
public sealed class ClipResolver
{
    private readonly IClipSource? _fileSource;
    private readonly ConcurrentDictionary<string, AudioClip> _explicitAssignments = new(StringComparer.Ordinal);
    private readonly ConcurrentDictionary<string, AudioClip> _cache = new(StringComparer.Ordinal);

    /// <param name="fileSource">Stage 2 loader. Null skips straight from stage 1 (explicit
    /// assignment) to stage 3 (tone) — a valid configuration for a caller with no asset root at
    /// all, not an error.</param>
    public ClipResolver(IClipSource? fileSource = null)
    {
        _fileSource = fileSource;
    }

    /// <summary>Stage 1: assigns a clip to a slot name directly, overriding stages 2 and 3 for
    /// every future <see cref="Resolve"/> call on that slot. Also invalidates any cached
    /// resolution for the slot, so re-assigning takes effect immediately.</summary>
    public void AssignExplicit(string slotName, AudioClip clip)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(slotName);
        ArgumentNullException.ThrowIfNull(clip);
        _explicitAssignments[slotName] = clip;
        _cache.TryRemove(slotName, out _);
    }

    /// <summary>Resolves <paramref name="slot"/> through the three stages, caching the result.</summary>
    public AudioClip Resolve(ClipSlot slot)
    {
        ArgumentNullException.ThrowIfNull(slot);

        return _cache.GetOrAdd(slot.Name, _ =>
        {
            if (_explicitAssignments.TryGetValue(slot.Name, out var explicitClip))
            {
                return explicitClip;
            }

            var fromFile = _fileSource?.TryLoad(slot.EffectiveResourcePath);
            if (fromFile is not null)
            {
                return fromFile;
            }

            if (slot.Loop)
            {
                return ToneGenerator.CreateLoopToneClip(slot.Name, slot.ToneStartHz, slot.ToneVolume, slot.ToneDurationSeconds);
            }

            // A slot whose sound is something STRUCK asks for a different generator, not a lower
            // sweep -- see ToneGenerator.CreateImpactClip for why pitch alone cannot make a sine read
            // as a thud.
            return slot.Impact
                ? ToneGenerator.CreateImpactClip(slot.Name, slot.ToneStartHz, slot.ToneEndHz, slot.ToneDurationSeconds, slot.ToneVolume)
                : ToneGenerator.CreateToneClip(
                    slot.Name, slot.ToneStartHz, slot.ToneEndHz, slot.ToneDurationSeconds, slot.ToneVolume,
                    attackSeconds: slot.ToneAttackSeconds, releaseSeconds: slot.ToneReleaseSeconds);
        });
    }

    /// <summary>Drops every cached resolution (not explicit assignments), forcing the next
    /// <see cref="Resolve"/> per slot to re-probe the file source. Exposed for tests and for a
    /// hot-reload workflow; normal play never needs it.</summary>
    public void ClearCache() => _cache.Clear();
}
