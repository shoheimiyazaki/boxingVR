using System.Globalization;
using System.Text;

namespace Aigis.Foundation.Diagnostics;

/// <summary>One logged event, in the order it was emitted.</summary>
/// <param name="Seq">Position within the session, 1-based. Record 1 is always the session-start event.</param>
/// <param name="PlayTime">Simulated seconds since the session began.</param>
/// <param name="WorldTime">The absolute simulated clock at emit time.</param>
/// <param name="Event">The event enum member's name.</param>
/// <param name="Payload">Tab-separated <c>key=value</c> pairs; empty for events that carry no data.</param>
public sealed record SessionEventRecord(int Seq, float PlayTime, float WorldTime, string Event, string Payload);

/// <summary>
/// A deterministic, enum-dispatched event log for one play session, written as TSV.
/// <para>
/// This is the port of the reference game's <c>AlphaLog</c>, and it exists for one reason: the
/// balance of a physics game cannot be read off the code, only off the distribution of what
/// actually happened across a session. Without it, "did the port reproduce the original?" has no
/// answer more precise than an opinion about a screenshot.
/// </para>
/// <para>
/// Three deliberate differences from the reference. (1) <b>Time is simulated, not wall-clock.</b>
/// The reference stamped <c>Time.realtimeSinceStartup</c>; a fixed-step engine that stamped real
/// time would emit a different file on every run and could never be golden-compared, so both time
/// columns come from the caller's simulation clock (Hard rule 5). (2) <b>It has no side effects
/// beyond the file.</b> The reference's <c>Emit</c> also printed to the console and played the
/// event's sound effect; an engine library may not print at all (Hard rule 2), and sound belongs to
/// whoever owns audio, not to the log. (3) <b>Emitting is dispatched by enum only</b> — there is no
/// string overload, because a log whose channel names are free text drifts into typo'd channels
/// that the aggregation silently miscounts (reference pitfall P-401).
/// </para>
/// <para>
/// The TSV header is byte-compatible with the reference's (including the now-inaccurate
/// <c>unityTime</c> column name — see <see cref="TsvHeader"/>) so that one aggregation script can
/// read a log from either engine and compare the two distributions directly. That comparison is
/// the only objective measure of port fidelity there is.
/// </para>
/// </summary>
/// <typeparam name="TEvent">The game's own event enum.</typeparam>
public sealed class SessionEventLog<TEvent>
    where TEvent : struct, Enum
{
    /// <summary>The reference log's exact header. The third column is named <c>unityTime</c> for
    /// wire compatibility with the reference's aggregation scripts even though this engine has no
    /// Unity in it — the column carries the absolute simulated clock (see the class remarks).
    /// Renaming it would buy tidiness at the cost of the cross-engine comparison.</summary>
    public const string TsvHeader = "seq\tplayTime\tunityTime\tevent\tpayload";

    /// <summary>Default ceiling on retained records. A 3-minute round emits a few thousand; this
    /// is a backstop for a long seeded batch run (port spec §5) that keeps a runaway emit site from
    /// exhausting memory. Overruns are counted, never silently dropped.</summary>
    public const int DefaultMaxRecords = 200_000;

    private readonly Func<float> _worldClock;
    private readonly int _maxRecords;
    private readonly List<SessionEventRecord> _records = [];

    private float _sessionStartWorldTime;
    private bool _sessionEnded;

    /// <param name="worldClock">Reads the caller's simulated clock, in seconds. Injected rather
    /// than captured so there is exactly one clock in the process: a log that owned its own
    /// counter would drift from the game's the first time a step was skipped (a pause, a menu).</param>
    /// <param name="maxRecords">Retained-record ceiling; see <see cref="DefaultMaxRecords"/>.</param>
    public SessionEventLog(Func<float> worldClock, int maxRecords = DefaultMaxRecords)
    {
        ArgumentNullException.ThrowIfNull(worldClock);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(maxRecords);
        _worldClock = worldClock;
        _maxRecords = maxRecords;
    }

    /// <summary>Every record emitted so far, in emit order. This — not the file — is what tests and
    /// <c>TriageBalanceMetrics</c>-style aggregators read; the file is for external tooling.</summary>
    public IReadOnlyList<SessionEventRecord> Records => _records;

    /// <summary>True between <see cref="BeginSession"/> and <see cref="EndSession"/>.</summary>
    public bool IsSessionOpen { get; private set; }

    /// <summary>Records discarded because <see cref="DefaultMaxRecords"/> was reached. Non-zero
    /// means the log is truncated and any rate computed from it is a lower bound.</summary>
    public int DroppedByCapacity { get; private set; }

    /// <summary>Emits that arrived with no session open (before the first <see cref="BeginSession"/>
    /// or after <see cref="EndSession"/>). Dropped rather than thrown — a stray teardown event must
    /// not crash a running game — but counted, because a non-zero value means an emit site is
    /// outside the session lifecycle and its events are missing from every aggregation.</summary>
    public int DroppedOutsideSession { get; private set; }

    /// <summary>Opens a session: clears prior records, anchors <see cref="SessionEventRecord.PlayTime"/>
    /// to the current clock, and emits <paramref name="sessionStartEvent"/> as record 1.</summary>
    /// <exception cref="InvalidOperationException">A session is already open. Unlike a stray emit
    /// this is never benign: it would restart <c>seq</c> mid-session and split one round's data
    /// into two incomparable halves.</exception>
    public void BeginSession(TEvent sessionStartEvent, EventPayload payload)
    {
        if (IsSessionOpen)
        {
            throw new InvalidOperationException(
                "A session is already open; call EndSession before beginning another.");
        }

        _records.Clear();
        DroppedByCapacity = 0;
        DroppedOutsideSession = 0;
        _sessionStartWorldTime = _worldClock();
        _sessionEnded = false;
        IsSessionOpen = true;
        Emit(sessionStartEvent, payload);
    }

    /// <summary>Appends one event. No-ops (and counts) when no session is open.</summary>
    public void Emit(TEvent value, EventPayload payload)
    {
        if (!IsSessionOpen)
        {
            DroppedOutsideSession++;
            return;
        }

        if (_records.Count >= _maxRecords)
        {
            DroppedByCapacity++;
            return;
        }

        var worldTime = _worldClock();
        _records.Add(new SessionEventRecord(
            _records.Count + 1,
            worldTime - _sessionStartWorldTime,
            worldTime,
            value.ToString(),
            payload.Text));
    }

    /// <summary>Appends one event with no payload.</summary>
    public void Emit(TEvent value) => Emit(value, EventPayload.Empty);

    /// <summary>Closes the session, emitting <paramref name="sessionEndEvent"/> exactly once
    /// however many times it is called. The reference needed the same latch because up to three
    /// teardown paths reach it, and a session that ends twice double-counts in every aggregation.</summary>
    /// <returns>True if this call is the one that closed the session.</returns>
    public bool EndSession(TEvent sessionEndEvent, EventPayload payload)
    {
        if (_sessionEnded || !IsSessionOpen)
        {
            return false;
        }

        Emit(sessionEndEvent, payload);
        _sessionEnded = true;
        IsSessionOpen = false;
        return true;
    }

    /// <summary>Serialises the whole session as TSV: <see cref="TsvHeader"/> then one line per
    /// record. Culture-invariant, so the file is identical on a machine with a comma decimal
    /// separator. Callable at any point and repeatable; overwrites <paramref name="path"/>.</summary>
    public void WriteTsv(string path)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        var directory = Path.GetDirectoryName(Path.GetFullPath(path));
        if (!string.IsNullOrEmpty(directory))
        {
            Directory.CreateDirectory(directory);
        }

        File.WriteAllText(path, ToTsv());
    }

    /// <summary>The TSV text <see cref="WriteTsv"/> writes.</summary>
    public string ToTsv()
    {
        var builder = new StringBuilder(TsvHeader).Append('\n');
        foreach (var record in _records)
        {
            builder
                .Append(record.Seq.ToString(CultureInfo.InvariantCulture)).Append('\t')
                .Append(record.PlayTime.ToString("0.000", CultureInfo.InvariantCulture)).Append('\t')
                .Append(record.WorldTime.ToString("0.000", CultureInfo.InvariantCulture)).Append('\t')
                .Append(record.Event).Append('\t')
                .Append(record.Payload).Append('\n');
        }

        return builder.ToString();
    }

    /// <summary>Counts records of one event kind — the primitive most aggregation is built from.</summary>
    public int Count(TEvent value)
    {
        var name = value.ToString();
        var total = 0;
        foreach (var record in _records)
        {
            if (string.Equals(record.Event, name, StringComparison.Ordinal))
            {
                total++;
            }
        }

        return total;
    }

    /// <summary>Every record of one event kind, in emit order.</summary>
    public IEnumerable<SessionEventRecord> Where(TEvent value)
    {
        var name = value.ToString();
        return _records.Where(r => string.Equals(r.Event, name, StringComparison.Ordinal));
    }
}
