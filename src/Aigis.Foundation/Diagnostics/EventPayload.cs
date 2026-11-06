using System.Globalization;
using System.Numerics;

namespace Aigis.Foundation.Diagnostics;

/// <summary>
/// An immutable <c>key=value</c> bag for one <see cref="SessionEventLog{TEvent}"/> record,
/// serialised as tab-separated pairs.
/// <para>
/// The point of a type here (rather than an interpolated string at each emit site) is boundary
/// validation: the log's wire format is tab-separated, so a tab or newline reaching a key or a
/// value would silently shift every later column of that row and corrupt the aggregation that
/// the whole log exists to feed. <see cref="With(string, string)"/> throws instead.
/// </para>
/// </summary>
public readonly struct EventPayload : IEquatable<EventPayload>
{
    private const char PairSeparator = '\t';
    private static readonly char[] Forbidden = ['\t', '\n', '\r'];

    private readonly string? _text;

    private EventPayload(string text) => _text = text;

    /// <summary>A payload with no keys. Valid — several events carry no data at all.</summary>
    public static EventPayload Empty => default;

    /// <summary>The serialised payload: <c>key=value</c> pairs joined by tabs, or the empty
    /// string when no key was added.</summary>
    public string Text => _text ?? string.Empty;

    /// <summary>True when no key has been added.</summary>
    public bool IsEmpty => string.IsNullOrEmpty(_text);

    /// <summary>Returns a new payload with <paramref name="key"/> appended. Never mutates.</summary>
    /// <exception cref="ArgumentException"><paramref name="key"/> is blank, contains a tab, a
    /// newline or an <c>=</c>, or <paramref name="value"/> contains a tab or a newline.</exception>
    public EventPayload With(string key, string value)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(key);
        ArgumentNullException.ThrowIfNull(value);
        if (key.IndexOfAny(Forbidden) >= 0 || key.Contains('=', StringComparison.Ordinal))
        {
            throw new ArgumentException($"Payload key '{key}' may not contain a tab, a newline or '='.", nameof(key));
        }

        if (value.IndexOfAny(Forbidden) >= 0)
        {
            throw new ArgumentException($"Payload value for '{key}' may not contain a tab or a newline.", nameof(value));
        }

        var pair = string.Concat(key, "=", value);
        return new EventPayload(IsEmpty ? pair : string.Concat(_text, PairSeparator.ToString(), pair));
    }

    /// <summary>Appends an integer, formatted culture-invariantly.</summary>
    public EventPayload With(string key, int value) => With(key, value.ToString(CultureInfo.InvariantCulture));

    /// <summary>Appends a float with three decimals, culture-invariantly — the reference log's own
    /// numeric shape, and enough resolution for damage/timer values without printing float noise.</summary>
    public EventPayload With(string key, float value) => With(key, value.ToString("0.###", CultureInfo.InvariantCulture));

    /// <summary>Appends a flag as <c>1</c>/<c>0</c>, matching the reference log's convention
    /// (e.g. <c>bandaged=1</c>) rather than .NET's <c>True</c>/<c>False</c>.</summary>
    public EventPayload With(string key, bool value) => With(key, value ? "1" : "0");

    /// <summary>Appends a position as <c>x,y,z</c> with two decimals. The reference's exact
    /// position formatting is not recorded in the port spec, so this is a port-side choice:
    /// comma-separated survives the tab-separated wire format and stays readable in a diff.</summary>
    public EventPayload With(string key, Vector3 value) => With(
        key,
        string.Create(
            CultureInfo.InvariantCulture,
            $"{value.X:0.##},{value.Y:0.##},{value.Z:0.##}"));

    /// <summary>Appends an enum member by name (never its numeric value, which would change
    /// meaning if the enum is ever reordered).</summary>
    public EventPayload With<TValue>(string key, TValue value)
        where TValue : struct, Enum
        => With(key, value.ToString());

    /// <summary>
    /// Reads one key back out of a serialised payload. The aggregation side of the log needs this
    /// (a discharge's <c>quality=</c>, an impact's <c>add=</c>), and it lives here so the parse and
    /// the format cannot drift apart.
    /// </summary>
    /// <param name="payloadText">A <see cref="Text"/> value, e.g. from
    /// <c>SessionEventRecord.Payload</c>.</param>
    /// <param name="key">Key to find. The first occurrence wins.</param>
    /// <param name="value">The value found, or empty.</param>
    /// <returns>True if the key was present.</returns>
    public static bool TryRead(string payloadText, string key, out string value)
    {
        ArgumentNullException.ThrowIfNull(payloadText);
        ArgumentException.ThrowIfNullOrWhiteSpace(key);

        foreach (var pair in payloadText.Split(PairSeparator, StringSplitOptions.RemoveEmptyEntries))
        {
            var split = pair.IndexOf('=', StringComparison.Ordinal);
            if (split > 0 && pair.AsSpan(0, split).SequenceEqual(key))
            {
                value = pair[(split + 1)..];
                return true;
            }
        }

        value = string.Empty;
        return false;
    }

    /// <summary>Reads a key as a float, culture-invariantly. Returns null when the key is absent or
    /// unparseable — the caller decides whether that is "no data" or a defect, rather than being
    /// handed a 0 it cannot tell apart from a real zero.</summary>
    public static float? TryReadFloat(string payloadText, string key) =>
        TryRead(payloadText, key, out var text)
        && float.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out var value)
            ? value
            : null;

    /// <summary>Reads a key as an int, culture-invariantly; null when absent or unparseable.</summary>
    public static int? TryReadInt(string payloadText, string key) =>
        TryRead(payloadText, key, out var text)
        && int.TryParse(text, NumberStyles.Integer, CultureInfo.InvariantCulture, out var value)
            ? value
            : null;

    /// <inheritdoc/>
    public override string ToString() => Text;

    /// <inheritdoc/>
    public bool Equals(EventPayload other) => string.Equals(Text, other.Text, StringComparison.Ordinal);

    /// <inheritdoc/>
    public override bool Equals(object? obj) => obj is EventPayload other && Equals(other);

    /// <inheritdoc/>
    public override int GetHashCode() => StringComparer.Ordinal.GetHashCode(Text);

    /// <summary>Value equality on the serialised text.</summary>
    public static bool operator ==(EventPayload left, EventPayload right) => left.Equals(right);

    /// <summary>Value inequality on the serialised text.</summary>
    public static bool operator !=(EventPayload left, EventPayload right) => !left.Equals(right);
}
