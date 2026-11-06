namespace Aigis.Audio;

/// <summary>
/// The fallback configuration for one named sound slot (e.g. "bandageApply", "aedCountdownBeep").
/// This is the table §9.8 says to port as-is from the reference's <c>EnsureClips</c>: it doubles
/// as the authoritative event list, because every slot that exists here is a sound the game can
/// make, whether or not a real clip has been assigned to it.
/// </summary>
/// <param name="Name">The slot's logical name — used for the explicit-assignment lookup, the
/// resource-relative file probe (defaults to <paramref name="Name"/> when
/// <paramref name="ResourceRelativePath"/> is not given), and <see cref="AudioMixer"/>'s queryable
/// state.</param>
/// <param name="ToneStartHz">Stage-3 fallback: swept-tone start frequency (loop slots ignore the
/// sweep and use <paramref name="ToneStartHz"/> as the single loop pitch).</param>
/// <param name="ToneEndHz">Stage-3 fallback: swept-tone end frequency. Ignored for loop slots.</param>
/// <param name="ToneDurationSeconds">Stage-3 fallback: tone length in seconds.</param>
/// <param name="ToneVolume">Stage-3 fallback: tone volume, 0..1.</param>
/// <param name="Loop">True for a slot played on the loop bus (uses <see cref="ToneGenerator.CreateLoopToneClip"/>
/// for its fallback, which is envelope-free so it does not click when it repeats).</param>
/// <param name="ResourceRelativePath">Stage-2 fallback: path under the resolver's asset root. Null
/// defaults to <c>{Name}.ogg</c> — most slots need nothing else specified.</param>
/// <param name="Impact">Stage-3 fallback shape: true generates a struck-body THUD
/// (<see cref="ToneGenerator.CreateImpactClip"/>) instead of a swept sine. For anything that lands,
/// hits or breaks — see that method for why a lower sweep is not the same thing.</param>
/// <param name="ToneAttackSeconds">Stage-3 envelope: how long the generated tone takes to reach
/// <paramref name="ToneVolume"/>. The default is a cue's; anything under about 100 ms long needs a
/// shorter one or it never reaches its own peak — see
/// <see cref="ToneGenerator.CreateToneClip"/>.</param>
/// <param name="ToneReleaseSeconds">Stage-3 envelope: how long it takes to fall silent.</param>
public sealed record ClipSlot(
    string Name,
    float ToneStartHz,
    float ToneEndHz,
    float ToneDurationSeconds,
    float ToneVolume,
    bool Loop = false,
    string? ResourceRelativePath = null,
    bool Impact = false,
    float ToneAttackSeconds = 0.06f,
    float ToneReleaseSeconds = 0.10f)
{
    /// <summary>The path this slot's stage-2 file probe uses, honouring an explicit override.</summary>
    public string EffectiveResourcePath => ResourceRelativePath ?? $"{Name}.ogg";
}
