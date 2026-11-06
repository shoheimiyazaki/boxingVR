using System.Globalization;

namespace Aigis.Foundation.Localization;

/// <summary>
/// Resolves a string key to text in whichever <see cref="Current"/> language is selected, over a set
/// of <see cref="StringCatalog"/>s registered at construction.
/// <para>
/// <b>Three-step fallback, never an exception and never a blank.</b> Current language → fallback
/// language → the key itself. A missing translation therefore degrades to the authoring language and,
/// in the worst case, to a visible key — a HUD label is never worth crashing a round over (the same
/// convention <c>GlyphRasterizer</c> already follows for a missing font), and a key on screen is a
/// defect a person can actually see and report, which an empty string is not.
/// </para>
/// <para>
/// <b>Adding a language is adding a catalog.</b> Nothing in this type enumerates the languages it
/// supports; <see cref="Languages"/> is whatever was registered. <see cref="MissingKeys"/> is the
/// other half of that contract — it is what lets a test fail a language that is merely half
/// translated, rather than shipping one that silently falls back on every third label.
/// </para>
/// <para>
/// <b>Not thread-safe for language changes.</b> <see cref="Current"/> is ordinary mutable state; the
/// game loop that writes it and the frame that reads it are the same thread (CLAUDE.md hard rule 5
/// keeps stepping single-threaded). Reads are safe to make concurrently as long as nobody is
/// switching languages at the same time.
/// </para>
/// </summary>
public sealed class Localizer
{
    private readonly Dictionary<Language, StringCatalog> _catalogs = [];
    private readonly List<Language> _languages = [];
    private Language _current;

    /// <param name="fallback">The language every lookup falls back to — this repo authors in
    /// <see cref="Language.Japanese"/>, so that catalog is the one guaranteed complete.</param>
    /// <param name="catalogs">Every catalog this localizer can serve, including
    /// <paramref name="fallback"/>'s own.</param>
    /// <exception cref="ArgumentException">No catalog was supplied for
    /// <paramref name="fallback"/>, or two catalogs name the same language.</exception>
    public Localizer(Language fallback, IEnumerable<StringCatalog> catalogs)
    {
        ArgumentNullException.ThrowIfNull(fallback);
        ArgumentNullException.ThrowIfNull(catalogs);

        foreach (var catalog in catalogs)
        {
            ArgumentNullException.ThrowIfNull(catalog);
            if (!_catalogs.TryAdd(catalog.Language, catalog))
            {
                throw new ArgumentException($"Two catalogs both claim language '{catalog.Language.Code}'.", nameof(catalogs));
            }

            _languages.Add(catalog.Language);
        }

        if (!_catalogs.ContainsKey(fallback))
        {
            throw new ArgumentException($"No catalog was supplied for the fallback language '{fallback.Code}'.", nameof(fallback));
        }

        Fallback = fallback;
        _current = fallback;
    }

    /// <summary>Raised after <see cref="Current"/> actually changes — for a host that has to persist
    /// the choice or rebuild a cached layout. Never raised for a no-op assignment.</summary>
    public event EventHandler? Changed;

    /// <summary>The language a key falls back to when <see cref="Current"/> has no entry for it.</summary>
    public Language Fallback { get; }

    /// <summary>Every language a catalog was registered for, in registration order — the order a
    /// language picker should offer them in.</summary>
    public IReadOnlyList<Language> Languages => _languages;

    /// <summary>
    /// Which language <see cref="Get"/> answers in. Setting it to a language with no catalog is a
    /// no-op rather than a throw: a saved preference or a <c>--lang</c> argument naming a language
    /// this build does not carry must leave the game playable in the fallback, not refuse to start.
    /// </summary>
    public Language Current
    {
        get => _current;
        set
        {
            ArgumentNullException.ThrowIfNull(value);
            if (!_catalogs.ContainsKey(value) || _current.Equals(value))
            {
                return;
            }

            _current = value;
            Changed?.Invoke(this, EventArgs.Empty);
        }
    }

    /// <summary>The registered language whose code matches <paramref name="cultureName"/> (ignoring
    /// region/script subtags and case), or <see langword="null"/> when none does. This is how an OS
    /// culture, a saved preference and a command-line argument all become a language.</summary>
    public Language? Resolve(string? cultureName)
    {
        var wanted = Language.PrimarySubtag(cultureName);
        if (wanted.Length == 0)
        {
            return null;
        }

        foreach (var language in _languages)
        {
            if (language.Matches(wanted))
            {
                return language;
            }
        }

        return null;
    }

    /// <summary>
    /// The text for <paramref name="key"/> in <see cref="Current"/>, falling back to
    /// <see cref="Fallback"/> and finally to <paramref name="key"/> itself. Never throws, never
    /// returns null.
    /// </summary>
    public string Get(string key)
    {
        if (string.IsNullOrEmpty(key))
        {
            return string.Empty;
        }

        if (_catalogs.TryGetValue(_current, out var catalog) && catalog.TryGet(key, out var text))
        {
            return text;
        }

        if (_catalogs.TryGetValue(Fallback, out var fallback) && fallback.TryGet(key, out var fallbackText))
        {
            return fallbackText;
        }

        return key;
    }

    /// <summary>
    /// <see cref="Get"/> plus <see cref="string.Format(IFormatProvider, string, object?[])"/> over
    /// <paramref name="args"/> — for the entries whose word order differs between languages and that
    /// therefore cannot be assembled by concatenating pieces at the call site
    /// (<c>{0}人</c> vs <c>{0} patients</c>).
    /// <para>
    /// Formatting is <see cref="CultureInfo.InvariantCulture"/>, deliberately: the numbers in this
    /// HUD are game quantities, and a thousands separator or a decimal comma appearing because the
    /// machine happens to be in a different region would make two identical runs render differently
    /// (CLAUDE.md hard rule 5). A malformed placeholder degrades to the raw template rather than
    /// throwing, for the same reason <see cref="Get"/> degrades to the key.
    /// </para>
    /// </summary>
    public string Format(string key, params object?[] args)
    {
        var template = Get(key);
        if (args is null || args.Length == 0)
        {
            return template;
        }

        try
        {
            return string.Format(CultureInfo.InvariantCulture, template, args);
        }
        catch (FormatException)
        {
            return template;
        }
    }

    /// <summary>
    /// Which of <see cref="Fallback"/>'s keys <paramref name="language"/>'s catalog does not carry —
    /// i.e. exactly what that language would silently fall back on. Empty means fully translated.
    /// A language with no catalog at all reports every fallback key as missing.
    /// </summary>
    public IReadOnlyList<string> MissingKeys(Language language)
    {
        ArgumentNullException.ThrowIfNull(language);
        var reference = _catalogs[Fallback];
        if (!_catalogs.TryGetValue(language, out var catalog))
        {
            return [.. reference.Keys.Order(StringComparer.Ordinal)];
        }

        return [.. reference.Keys.Where(key => !catalog.TryGet(key, out _)).Order(StringComparer.Ordinal)];
    }

    /// <summary>
    /// Which keys <paramref name="language"/>'s catalog carries that <see cref="Fallback"/>'s does
    /// not — a translation for a key nobody looks up any more, which is dead weight a test should
    /// catch as readily as a missing one.
    /// </summary>
    public IReadOnlyList<string> UnknownKeys(Language language)
    {
        ArgumentNullException.ThrowIfNull(language);
        var reference = _catalogs[Fallback];
        if (!_catalogs.TryGetValue(language, out var catalog))
        {
            return [];
        }

        return [.. catalog.Keys.Where(key => !reference.TryGet(key, out _)).Order(StringComparer.Ordinal)];
    }
}
