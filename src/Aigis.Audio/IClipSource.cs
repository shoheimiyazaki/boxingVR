namespace Aigis.Audio;

/// <summary>Stage 2 of <see cref="ClipResolver"/>'s three-stage resolution: loads a real asset by
/// a resource-relative path. Abstracted so tests can substitute an in-memory source instead of
/// touching disk, and so headless CI (no clip assets present, per TASK-068's scope note) degrades
/// to stage 3 rather than failing.</summary>
public interface IClipSource
{
    /// <summary>Attempts to load and decode the clip at <paramref name="relativePath"/>. Returns
    /// null (never throws) when the file does not exist or fails to decode — the resolver treats
    /// that identically to "no file" and falls through to the generated tone.</summary>
    public AudioClip? TryLoad(string relativePath);
}
