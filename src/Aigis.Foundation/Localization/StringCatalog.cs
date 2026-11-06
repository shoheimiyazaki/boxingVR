using System.Collections.Immutable;

namespace Aigis.Foundation.Localization;

/// <summary>
/// One language's strings, keyed by a stable, language-independent id (<c>"hud.score"</c>).
/// <para>
/// <b>Immutable and I/O-free.</b> A catalog is built from an in-memory sequence of pairs — typically
/// a C# collection expression in a per-language source file — rather than loaded from disk: the
/// headless/agent path must never touch the filesystem to render a HUD (CLAUDE.md hard rule 6), and
/// a catalog that cannot fail to load has no "missing resource" branch for a test to have to cover.
/// A future file-backed source is additive: build the pairs however you like and hand them here.
/// </para>
/// <para>
/// <b>Duplicate keys are a bug, not a merge.</b> The constructor throws rather than letting the last
/// writer win, because a duplicated key in a hand-maintained catalog is invariably a copy-paste that
/// silently shadows the entry above it.
/// </para>
/// </summary>
public sealed class StringCatalog
{
    private readonly ImmutableDictionary<string, string> _entries;

    /// <param name="language">Which language these strings are in.</param>
    /// <param name="entries">Key → text. Keys are compared ordinally and are case-sensitive.</param>
    /// <exception cref="ArgumentException">A key appears twice, or a key is null/blank.</exception>
    public StringCatalog(Language language, IEnumerable<KeyValuePair<string, string>> entries)
    {
        ArgumentNullException.ThrowIfNull(language);
        ArgumentNullException.ThrowIfNull(entries);

        Language = language;
        var builder = ImmutableDictionary.CreateBuilder<string, string>(StringComparer.Ordinal);
        foreach (var (key, value) in entries)
        {
            if (string.IsNullOrWhiteSpace(key))
            {
                throw new ArgumentException("A catalog key must be a non-blank string.", nameof(entries));
            }

            if (builder.ContainsKey(key))
            {
                throw new ArgumentException($"Duplicate key '{key}' in the {language.Code} catalog.", nameof(entries));
            }

            builder[key] = value ?? string.Empty;
        }

        _entries = builder.ToImmutable();
    }

    /// <summary>Which language this catalog holds.</summary>
    public Language Language { get; }

    /// <summary>Every key in this catalog, in no guaranteed order.</summary>
    public IEnumerable<string> Keys => _entries.Keys;

    /// <summary>How many entries this catalog holds.</summary>
    public int Count => _entries.Count;

    /// <summary>Looks <paramref name="key"/> up. <see langword="false"/> when this catalog has no
    /// entry for it — which is what makes <see cref="Localizer"/>'s fallback chain possible.</summary>
    public bool TryGet(string key, out string value) => _entries.TryGetValue(key, out value!);
}
