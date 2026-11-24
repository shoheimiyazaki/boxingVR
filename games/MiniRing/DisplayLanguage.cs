using System.Text.Json;
using System.Text.Json.Nodes;

namespace MiniRing;

/// <summary>
/// 画面に出す言葉の言語（中継の字幕・選手の名前・団体の見せ方）。試合の計算には関わらない（描画とラベルだけ。CLAUDE.md §1.6）。
/// 既定は日本語（<see cref="Japanese"/>）。<see cref="Current"/> は起動時に 1 度だけ設定する（アリーナの <c>--lang</c>）。
/// データ（名簿・団体のファイル）は <c>"locales": { "en": { … } }</c> を持てる: 言語が en なら、その中身が本体へ深く重なる（<see cref="LocaleOverlay"/>）。
/// </summary>
public static class DisplayLanguage
{
    /// <summary>日本語（既定。ファイルの本体の値そのまま）。</summary>
    public const string Japanese = "ja";

    /// <summary>英語。</summary>
    public const string English = "en";

    private static volatile string _current = Japanese;

    /// <summary>いまの言語（"ja" か "en"）。</summary>
    public static string Current
    {
        get => _current;
        set => _current = Validate(value);
    }

    /// <summary>日本語以外か（画面の言葉を差し替えるか）。</summary>
    public static bool IsEnglish => _current == English;

    /// <summary>言語の名前を検査する（知らない言語は、ある言語を書いた例外）。</summary>
    public static string Validate(string? language)
        => language is Japanese or English ? language : throw new ArgumentException($"言語 '{language}' は知りません（{Japanese}, {English}）。", nameof(language));

    /// <summary>いまの言語で言葉を選ぶ（画面の文字の置き場所で使う）。</summary>
    public static string T(string ja, string en) => _current == English ? en : ja;
}

/// <summary>
/// データの言語の重ね。ファイルの <c>locales.&lt;言語&gt;</c> の中身を本体の JSON へ深く重ねる（オブジェクトは再帰、配列と値は置き換え）。
/// <c>locales</c> は結果から必ず取り除く（本体の型は知らない欄を断るため）。日本語（本体そのまま）でも同じ道を通り、取り除くだけ。
/// </summary>
public static class LocaleOverlay
{
    private const string LocalesKey = "locales";

    private static readonly JsonDocumentOptions Document = new() { CommentHandling = JsonCommentHandling.Skip, AllowTrailingCommas = true };

    /// <summary>JSON（コメント・末尾のカンマ可）に言語を重ねて、<c>locales</c> を除いた JSON を返す。</summary>
    /// <exception cref="JsonException">JSON が読めない、または本体がオブジェクトでないとき。</exception>
    public static string Apply(string json, string language)
    {
        ArgumentNullException.ThrowIfNull(json);
        DisplayLanguage.Validate(language);
        var root = JsonNode.Parse(json, documentOptions: Document) as JsonObject ?? throw new JsonException("JSON の本体がオブジェクトではありません。");
        var locales = root[LocalesKey];
        root.Remove(LocalesKey);
        if (language != DisplayLanguage.Japanese && locales is JsonObject all && all[language] is JsonObject overlay)
        {
            Merge(root, overlay);
        }

        return root.ToJsonString();
    }

    private static void Merge(JsonObject target, JsonObject overlay)
    {
        foreach (var (key, value) in overlay.ToList())
        {
            if (value is JsonObject child && target[key] is JsonObject existing)
            {
                Merge(existing, child);
            }
            else
            {
                target[key] = value?.DeepClone();
            }
        }
    }
}
