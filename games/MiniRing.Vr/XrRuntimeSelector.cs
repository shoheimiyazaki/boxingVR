namespace MiniRing.Vr;

internal enum XrRuntimeKind
{
    /// <summary>Meta Quest Link（旧 Oculus）アプリの OpenXR ランタイム。Quest を Link ケーブル / Air Link で直接つなぐ。</summary>
    Meta,

    /// <summary>Virtual Desktop Streamer 同梱の VDXR。Quest を Wi-Fi で直接つなぐ（SteamVR 不要）。</summary>
    VirtualDesktop,

    SteamVr,
    Other,
}

/// <summary>ランタイム選択の結果。<see cref="Manifest"/> が null ならシステム既定（レジストリの ActiveRuntime）を使う。</summary>
internal readonly record struct XrRuntimeChoice(string? Manifest, XrRuntimeKind? Kind, string? Error);

/// <summary>
/// どの OpenXR ランタイムで HMD につなぐかを決める。
/// <para>
/// Windows の OpenXR ランタイムはマシンに 1 つ（HKLM\SOFTWARE\Khronos\OpenXR\1\ActiveRuntime）で、SteamVR を入れると
/// SteamVR がそこを取る。そのままだと Quest も SteamVR 経由になるので、iTVision 3 の SteamVrRuntime.Pin と同じく
/// <c>XR_RUNTIME_JSON</c> で<b>このプロセスだけ</b>使うランタイムを固定する（マシンの設定は何も変えない）。
/// </para>
/// 既定（<c>auto</c>）は Quest を SteamVR なしでつなぐ順: Meta Quest Link → Virtual Desktop。どちらも無ければ失敗させ、
/// 黙って SteamVR に落ちることはしない（<c>--runtime steamvr</c> / <c>system</c> を明示したときだけ使う）。
/// </summary>
internal static class XrRuntimeSelector
{
    public const string RuntimeEnvVar = "XR_RUNTIME_JSON";

    public const string MetaInstallHint =
        "Quest を SteamVR なしでつなぐには、PC に「Meta Quest Link」アプリ（旧 Oculus アプリ）を入れ、"
        + "Quest で Link（USB ケーブル）または Air Link を開始してください。"
        + "Virtual Desktop を使う場合は Streamer 側の OpenXR（VDXR）でつながります（--runtime vd）。";

    /// <summary>マニフェストのパス／ファイル名からランタイムの種類を推定する。</summary>
    public static XrRuntimeKind Classify(string manifestPath)
    {
        var p = manifestPath.Replace('\\', '/').ToUpperInvariant();
        var file = Path.GetFileName(p);
        if (file.Contains("OCULUS", StringComparison.Ordinal) || p.Contains("/OCULUS/", StringComparison.Ordinal) || p.Contains("META QUEST", StringComparison.Ordinal))
        {
            return XrRuntimeKind.Meta;
        }

        if (file.Contains("VIRTUALDESKTOP", StringComparison.Ordinal) || p.Contains("VIRTUAL DESKTOP", StringComparison.Ordinal))
        {
            return XrRuntimeKind.VirtualDesktop;
        }

        return file.Contains("STEAMXR", StringComparison.Ordinal) || p.Contains("/STEAMVR/", StringComparison.Ordinal)
            ? XrRuntimeKind.SteamVr
            : XrRuntimeKind.Other;
    }

    /// <summary>
    /// <paramref name="request"/>（auto / quest / meta / vd / steamvr / system / マニフェストのパス）と見つかったランタイムから決める。
    /// 利用者がすでに <c>XR_RUNTIME_JSON</c> を設定していればそれを尊重する。
    /// </summary>
    public static XrRuntimeChoice Choose(string request, IReadOnlyList<string> found, string? environmentOverride)
    {
        ArgumentNullException.ThrowIfNull(found);
        if (!string.IsNullOrWhiteSpace(environmentOverride))
        {
            return new XrRuntimeChoice(environmentOverride, Classify(environmentOverride), null);
        }

        var normalized = (request ?? "auto").Trim().ToUpperInvariant();
        if (normalized.EndsWith(".JSON", StringComparison.Ordinal))
        {
            return new XrRuntimeChoice(request!.Trim(), Classify(request), null);
        }

        string? First(XrRuntimeKind kind) => found.FirstOrDefault(m => Classify(m) == kind);
        switch (normalized)
        {
            case "SYSTEM":
                return new XrRuntimeChoice(null, null, null);
            case "META":
                return Pick(First(XrRuntimeKind.Meta), XrRuntimeKind.Meta, "Meta Quest Link の OpenXR ランタイムが見つかりません。" + MetaInstallHint);
            case "VD":
            case "VIRTUALDESKTOP":
                return Pick(First(XrRuntimeKind.VirtualDesktop), XrRuntimeKind.VirtualDesktop, "Virtual Desktop Streamer の OpenXR（VDXR）が見つかりません。");
            case "STEAMVR":
                return Pick(First(XrRuntimeKind.SteamVr), XrRuntimeKind.SteamVr, "SteamVR の OpenXR ランタイムが見つかりません。");
            case "AUTO":
            case "QUEST":
                if (First(XrRuntimeKind.Meta) is { } meta)
                {
                    return new XrRuntimeChoice(meta, XrRuntimeKind.Meta, null);
                }

                if (First(XrRuntimeKind.VirtualDesktop) is { } vd)
                {
                    return new XrRuntimeChoice(vd, XrRuntimeKind.VirtualDesktop, null);
                }

                var others = found.Count == 0 ? "なし" : string.Join(", ", found.Select(m => $"{Classify(m)}: {m}"));
                return new XrRuntimeChoice(null, null,
                    $"SteamVR を使わずに Quest につなげるランタイムがありません（見つかったランタイム: {others}）。{MetaInstallHint}"
                    + " SteamVR 経由で良ければ --runtime steamvr を付けてください。");
            default:
                return new XrRuntimeChoice(null, null, $"--runtime の値 '{request}' が分かりません（auto / meta / vd / steamvr / system / マニフェストのパス）。");
        }

        static XrRuntimeChoice Pick(string? manifest, XrRuntimeKind kind, string error)
            => manifest is null ? new XrRuntimeChoice(null, null, error) : new XrRuntimeChoice(manifest, kind, null);
    }

    /// <summary>このマシンにある OpenXR ランタイムのマニフェスト（実在するものだけ・重複なし）。</summary>
    public static IReadOnlyList<string> Discover()
    {
        var candidates = new List<string>();
        if (OperatingSystem.IsWindows())
        {
            try
            {
                using var key = Microsoft.Win32.Registry.LocalMachine.OpenSubKey(@"SOFTWARE\Khronos\OpenXR\1");
                if (key?.GetValue("ActiveRuntime") is string active)
                {
                    candidates.Add(active);
                }

                using var available = key?.OpenSubKey("AvailableRuntimes");
                if (available is not null)
                {
                    candidates.AddRange(available.GetValueNames());
                }
            }
            catch (System.Security.SecurityException)
            {
                // レジストリが読めなくても既定の場所は探す。
            }
            catch (UnauthorizedAccessException)
            {
            }
        }

        var programFiles = Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles);
        var programFilesX86 = Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86);
        candidates.Add(Path.Combine(programFiles, "Oculus", "Support", "oculus-runtime", "oculus_openxr_64.json"));
        candidates.Add(Path.Combine(programFiles, "Virtual Desktop Streamer", "OpenXR", "virtualdesktop-openxr.json"));
        candidates.Add(Path.Combine(programFilesX86, "Steam", "steamapps", "common", "SteamVR", "steamxr_win64.json"));

        return candidates
            .Where(c => !string.IsNullOrWhiteSpace(c) && File.Exists(c))
            .Select(Path.GetFullPath)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    /// <summary>選んだランタイムをこのプロセスに固定する（OpenXR ローダーが xrCreateInstance 時に読む）。</summary>
    public static void Pin(XrRuntimeChoice choice)
    {
        if (choice.Manifest is not null)
        {
            Environment.SetEnvironmentVariable(RuntimeEnvVar, choice.Manifest);
        }
    }
}
