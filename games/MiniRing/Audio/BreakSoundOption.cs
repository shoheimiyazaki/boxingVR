namespace MiniRing.Audio;

/// <summary>起動オプション <c>--break-sound &lt;glass|crumble|crunch|circuit|metal&gt;</c>（ボクセルが砕ける音の方式）。</summary>
public static class BreakSoundOption
{
    public const string Flag = "--break-sound";

    /// <summary>引数から方式を読む。無ければ既定。値が無い・知らない名前なら false と理由。</summary>
    public static bool TryParse(IReadOnlyList<string> args, out BreakStyle style, out string error)
    {
        ArgumentNullException.ThrowIfNull(args);
        style = VoxelBreakSound.DefaultStyle;
        error = "";
        for (var i = 0; i < args.Count; i++)
        {
            if (args[i] != Flag)
            {
                continue;
            }

            var names = string.Join("|", Enum.GetNames<BreakStyle>().Select(n => n.ToLowerInvariant()));
            // 名前だけを受け付ける（Enum.TryParse は "0" や "glass,crunch" も通してしまう）。
            if (i + 1 >= args.Count || !TryName(args[i + 1], out style))
            {
                style = VoxelBreakSound.DefaultStyle;
                error = $"{Flag} needs one of {names}.";
                return false;
            }
        }

        return true;
    }

    /// <summary>方式の名前（大文字小文字は問わない）だけを読む。</summary>
    public static bool TryName(string value, out BreakStyle style)
    {
        ArgumentNullException.ThrowIfNull(value);
        foreach (var candidate in Enum.GetValues<BreakStyle>())
        {
            if (string.Equals(candidate.ToString(), value, StringComparison.OrdinalIgnoreCase))
            {
                style = candidate;
                return true;
            }
        }

        style = VoxelBreakSound.DefaultStyle;
        return false;
    }
}
