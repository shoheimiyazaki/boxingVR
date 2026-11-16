using System.Text.Json;
using System.Text.Json.Serialization;

namespace MiniRing.Desktop;

/// <summary>メニューの「設定」で切り替える、ゲームの決まり。</summary>
/// <param name="Referee">レフェリーがいるか（既定はいる。<see cref="BoxingCorners.Referee"/> へ渡す）。</param>
internal sealed record MenuSettings(bool Referee = true)
{
    public static MenuSettings Default { get; } = new();
}

/// <summary>
/// 設定をファイル（JSON）へ残す・読む。読むときに検証し、壊れていれば（JSON が読めない・知らない項目・型が違う）既定へ戻して
/// ログに出す（黙って別の値にしない）。ファイルが無いのは初回なので既定で、ログも出さない。
/// </summary>
internal static class MenuSettingsStore
{
    private static readonly JsonSerializerOptions Options = new()
    {
        WriteIndented = true,
        PropertyNameCaseInsensitive = true,
        UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow,
    };

    /// <summary>読む。失敗したら既定を返し、理由を <paramref name="log"/> へ。</summary>
    public static MenuSettings Load(string path, Action<string> log)
    {
        ArgumentException.ThrowIfNullOrEmpty(path);
        ArgumentNullException.ThrowIfNull(log);
        try
        {
            if (!File.Exists(path))
            {
                return MenuSettings.Default;
            }

            return JsonSerializer.Deserialize<MenuSettings>(File.ReadAllText(path), Options)
                ?? throw new JsonException("空の設定です。");
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException or NotSupportedException)
        {
            log($"設定ファイル {path} を読めないため既定（レフェリーあり）に戻します: {ex.Message}");
            return MenuSettings.Default;
        }
    }

    /// <summary>書く。失敗したら <paramref name="log"/> へ出して続ける（設定は今の起動では有効）。</summary>
    public static void Save(string path, MenuSettings settings, Action<string> log)
    {
        ArgumentException.ThrowIfNullOrEmpty(path);
        ArgumentNullException.ThrowIfNull(settings);
        ArgumentNullException.ThrowIfNull(log);
        try
        {
            if (Path.GetDirectoryName(Path.GetFullPath(path)) is { Length: > 0 } directory)
            {
                Directory.CreateDirectory(directory);
            }

            File.WriteAllText(path, JsonSerializer.Serialize(settings, Options));
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            log($"設定ファイル {path} を保存できません: {ex.Message}");
        }
    }
}
