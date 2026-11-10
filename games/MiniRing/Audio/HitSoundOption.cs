namespace MiniRing.Audio;

/// <summary>起動オプション <c>--hit-sound &lt;on|off&gt;</c>（パンチが当たる音。off でボクセルが砕ける音だけを聞く）。</summary>
public static class HitSoundOption
{
    public const string Flag = "--hit-sound";

    /// <summary>引数から読む。無ければ on。値が無い・on/off 以外なら false と理由。</summary>
    public static bool TryParse(IReadOnlyList<string> args, out bool on, out string error)
    {
        ArgumentNullException.ThrowIfNull(args);
        on = true;
        error = "";
        for (var i = 0; i < args.Count; i++)
        {
            if (args[i] != Flag)
            {
                continue;
            }

            if (i + 1 >= args.Count || !TryValue(args[i + 1], out on))
            {
                on = true;
                error = $"{Flag} needs on or off.";
                return false;
            }
        }

        return true;
    }

    /// <summary>"on" / "off"（大文字小文字は問わない）を読む。</summary>
    public static bool TryValue(string value, out bool on)
    {
        ArgumentNullException.ThrowIfNull(value);
        on = !string.Equals(value, "off", StringComparison.OrdinalIgnoreCase);
        return on ? string.Equals(value, "on", StringComparison.OrdinalIgnoreCase) : true;
    }
}
