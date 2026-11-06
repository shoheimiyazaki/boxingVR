namespace Aigis.Foundation.Localization;

/// <summary>
/// One language a build can present itself in: a BCP-47-style <paramref name="Code"/> plus the two
/// names a language picker needs — what the language calls itself
/// (<paramref name="NativeName"/>, which is what a player who reads only that language can
/// recognise) and what it is called in English (<paramref name="EnglishName"/>, for developer-facing
/// output such as a CLI's <c>--lang</c> help line).
/// <para>
/// <b>This type is not a closed enum, on purpose.</b> Adding a language to the engine must never
/// mean editing the engine: a caller constructs its own value (<c>new Language("fr", "Français",
/// "French")</c>), hands <see cref="Localizer"/> a catalog for it, and it appears in the picker.
/// <see cref="Japanese"/>/<see cref="English"/> are conveniences for the two the repo ships, not the
/// permitted set — <see cref="Localizer.Languages"/> is what "which languages exist" actually means
/// at run time.
/// </para>
/// <para>
/// <b>Equality is by <paramref name="Code"/> alone</b> (case-insensitively): two values that name
/// the same language in different words are the same language, and a lookup keyed on one must find a
/// catalog registered under the other. The display names are carried along for the picker, not for
/// identity.
/// </para>
/// </summary>
public sealed record Language(string Code, string NativeName, string EnglishName)
{
    /// <summary>日本語 — this repo's authoring language and every catalog's fallback.</summary>
    public static readonly Language Japanese = new("ja", "日本語", "Japanese");

    /// <summary>English.</summary>
    public static readonly Language English = new("en", "English", "English");

    /// <summary>
    /// The language part of an arbitrary culture name — <c>"ja-JP"</c> → <c>"ja"</c>,
    /// <c>"EN"</c> → <c>"en"</c>, <c>"zh-Hans-CN"</c> → <c>"zh"</c>.
    /// <para>
    /// Used to match an OS culture (or a <c>--lang</c> argument someone typed with a region on it)
    /// against a registered language. Region-specific catalogs are not a thing this engine has yet;
    /// when they become one, this is the single place that decision is made rather than a
    /// <c>Split('-')</c> scattered across hosts.
    /// </para>
    /// </summary>
    /// <returns>The normalised primary subtag, lower-case, or an empty string when
    /// <paramref name="cultureName"/> carries none.</returns>
    public static string PrimarySubtag(string? cultureName)
    {
        if (string.IsNullOrWhiteSpace(cultureName))
        {
            return string.Empty;
        }

        var trimmed = cultureName.Trim();
        var cut = trimmed.IndexOfAny(['-', '_']);
        var primary = cut < 0 ? trimmed : trimmed[..cut];
        return primary.ToLowerInvariant();
    }

    /// <summary>Whether this language is the one <paramref name="cultureName"/> names, ignoring any
    /// region/script subtags and case.</summary>
    public bool Matches(string? cultureName) =>
        PrimarySubtag(Code).Equals(PrimarySubtag(cultureName), StringComparison.Ordinal);

    /// <inheritdoc/>
    public bool Equals(Language? other) => other is not null && Matches(other.Code);

    /// <inheritdoc/>
    public override int GetHashCode() => StringComparer.Ordinal.GetHashCode(PrimarySubtag(Code));

    /// <inheritdoc/>
    public override string ToString() => $"{Code} ({NativeName})";
}
