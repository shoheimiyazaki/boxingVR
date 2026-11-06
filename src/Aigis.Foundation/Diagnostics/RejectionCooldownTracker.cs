namespace Aigis.Foundation.Diagnostics;

/// <summary>
/// Suppresses a repeating log channel to at most one record per key per cooldown window.
/// <para>
/// Ported from the reference's <c>RejectionCooldownTracker&lt;TKey&gt;</c>, which the discharge and
/// morgue zones share at a 1.5 s window (<see cref="DefaultCooldownSeconds"/>). It never affects
/// gameplay or score — its only job is keeping a rejected-delivery channel from inflating the play
/// log while an entity sits in a zone it is not eligible for.
/// </para>
/// <para>
/// It is also the general answer to reference pitfall P-402/P-520: a ragdoll is a compound of many
/// bodies, so one semantic "entered the zone" arrives as one event per part. For an accepted
/// delivery the right guard is an idempotency marker on the entity (it is consumed, so the marker
/// disappears with it); for a repeating, non-consuming event there is nothing to mark, and this
/// per-key window is the guard instead.
/// </para>
/// <para>
/// Time is a parameter, never read from a clock, so a caller's fixed-step time drives it and the
/// suppression pattern is reproducible run to run.
/// </para>
/// </summary>
/// <typeparam name="TKey">Identifies one logical (entity, zone) pair.</typeparam>
public sealed class RejectionCooldownTracker<TKey>
    where TKey : notnull
{
    /// <summary>The reference's window for both the discharge and morgue zones.</summary>
    public const float DefaultCooldownSeconds = 1.5f;

    private readonly Dictionary<TKey, float> _lastEmit = [];
    private readonly float _cooldownSeconds;

    /// <param name="cooldownSeconds">Minimum simulated seconds between two records for one key.</param>
    public RejectionCooldownTracker(float cooldownSeconds = DefaultCooldownSeconds)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(cooldownSeconds);
        _cooldownSeconds = cooldownSeconds;
    }

    /// <summary>Keys currently inside their cooldown window (diagnostic; a test asserts on it).</summary>
    public int TrackedKeys => _lastEmit.Count;

    /// <summary>
    /// True if <paramref name="key"/> may be logged at <paramref name="now"/> — i.e. it has not
    /// been logged before, or the window has elapsed. Records the emit time when it returns true.
    /// </summary>
    public bool TryRecord(TKey key, float now)
    {
        ArgumentNullException.ThrowIfNull(key);
        if (_lastEmit.TryGetValue(key, out var last) && now - last < _cooldownSeconds)
        {
            return false;
        }

        _lastEmit[key] = now;
        return true;
    }

    /// <summary>Forgets one key, so its next <see cref="TryRecord"/> fires immediately. Called when
    /// the entity leaves the zone: a fresh entry is a fresh event, not a continuation.</summary>
    public bool Remove(TKey key) => _lastEmit.Remove(key);

    /// <summary>Forgets every key. Needed on teardown because an entity destroyed inside a zone
    /// never reports leaving it, so its key would otherwise be retained for the next session.</summary>
    public void Clear() => _lastEmit.Clear();
}
