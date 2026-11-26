using System.Buffers.Binary;
using System.Collections.Concurrent;
using System.Collections.Immutable;
using System.Drawing;
using System.Drawing.Imaging;
using System.Drawing.Text;
using System.Runtime.InteropServices;

namespace Aigis.Render;

/// <summary>
/// A dot (bitmap) font read from a TrueType file whose outlines are whole pixels at one size — e.g. 美咲フォント
/// (8×8 dots). Each glyph is rasterized ONCE at its native cell size with antialiasing off, so every stroke is an exact
/// on/off dot, and <see cref="ScreenText"/> draws it scaled by a whole number (nearest-neighbour: each dot becomes an
/// n×n square — never blurred, never resampled at a fractional size).
/// <para><b>Coverage</b> comes from the font's own <c>cmap</c> table (read here, no extra dependency), not from guessing
/// at GDI+'s output: a character the font does not have is drawn from the OS's Japanese UI font at the same cell size,
/// also without antialiasing, baseline-aligned (see <see cref="FallbackFamilies"/>), so it still sits on the dot grid.
/// <see cref="Has"/> exposes the coverage so a caller/test can list what fell back.</para>
/// <para>Never throws for a missing rendering capability (no GDI+ on this platform etc.): such a glyph rasterizes empty,
/// the same degrade-to-nothing contract as <see cref="GlyphRasterizer"/>.</para>
/// </summary>
public sealed class PixelFont : IDisposable
{
    /// <summary>OS fonts for characters the dot font lacks (first installed wins; else the generic sans-serif).</summary>
    private static readonly string[] FallbackFamilies = ["MS Gothic", "Yu Gothic UI", "Segoe UI"];

    private readonly PrivateFontCollection? _collection;
    private readonly IntPtr _fontMemory;
    private readonly HashSet<int> _codepoints;
    private readonly Dictionary<int, int> _advanceUnits;
    private readonly int _unitsPerEm;
    private readonly int _ascentUnits;
    private readonly ConcurrentDictionary<char, PixelGlyph> _glyphs = new();
    private readonly Lock _gdi = new();
    private bool _disposed;
    private string? _fallbackFamily;

    private PixelFont(byte[] data, int cellPixels)
    {
        CellPixels = cellPixels;
        var table = TrueTypeTables.Read(data);
        _unitsPerEm = table.UnitsPerEm;
        _ascentUnits = table.Ascent;
        _codepoints = table.Codepoints;
        _advanceUnits = table.AdvanceByCodepoint;
        try
        {
            // AddMemoryFont keeps a pointer, not a copy: the block must outlive every Font made from the collection,
            // so it is owned by this (process-lifetime) instance and never freed while the collection is in use.
            _fontMemory = Marshal.AllocCoTaskMem(data.Length);
            Marshal.Copy(data, 0, _fontMemory, data.Length);
            _collection = new PrivateFontCollection();
            _collection.AddMemoryFont(_fontMemory, data.Length);
            FamilyName = _collection.Families.Length > 0 ? _collection.Families[0].Name : null;
        }
        catch (Exception e) when (e is PlatformNotSupportedException or DllNotFoundException or ExternalException or TypeInitializationException)
        {
            _collection = null;
            FamilyName = null;
        }
    }

    /// <summary>Releases the GDI+ collection and the font bytes it points at (glyphs already cached stay usable).</summary>
    public void Dispose()
    {
        lock (_gdi)
        {
            _collection?.Dispose();
            if (_fontMemory != IntPtr.Zero) { Marshal.FreeCoTaskMem(_fontMemory); }
            _disposed = true;
        }
    }

    /// <summary>Reads a TrueType font whose design grid is <paramref name="cellPixels"/> dots per em (美咲: 8).</summary>
    /// <exception cref="InvalidDataException">The bytes are not a TrueType font this reader understands (no usable cmap).</exception>
    public static PixelFont FromTrueType(byte[] data, int cellPixels)
    {
        ArgumentNullException.ThrowIfNull(data);
        ArgumentOutOfRangeException.ThrowIfLessThan(cellPixels, 4);
        return new PixelFont(data, cellPixels);
    }

    /// <summary>Dots per em (the native line height): text is drawn at whole multiples of this.</summary>
    public int CellPixels { get; }

    /// <summary>The family name GDI+ registered (null when GDI+ is unavailable).</summary>
    public string? FamilyName { get; }

    /// <summary>Whether the dot font itself has <paramref name="c"/> (false = drawn from the OS fallback font).</summary>
    public bool Has(char c) => _codepoints.Contains(c);

    /// <summary>The whole-number scale a draw of <paramref name="pixelHeight"/> uses: the nearest multiple of
    /// <see cref="CellPixels"/> (at least 1×).</summary>
    public int ScaleFor(float pixelHeight) =>
        Math.Max(1, (int)MathF.Round(pixelHeight / CellPixels, MidpointRounding.AwayFromZero));

    /// <summary>Width of <paramref name="text"/> in native dots (multiply by the scale for screen pixels).</summary>
    public int MeasureDots(string text)
    {
        ArgumentNullException.ThrowIfNull(text);
        var width = 0;
        foreach (var c in text) { width += Glyph(c).Advance; }
        return width;
    }

    /// <summary>The glyph of <paramref name="c"/> at native size (cached).</summary>
    public PixelGlyph Glyph(char c) => _glyphs.GetOrAdd(c, Rasterize);

    private PixelGlyph Rasterize(char c)
    {
        var own = Has(c);
        var advance = own && _advanceUnits.TryGetValue(c, out var units)
            ? Math.Max(0, (int)MathF.Round(units * (float)CellPixels / _unitsPerEm))
            : CellPixels;
        if (char.IsWhiteSpace(c) || _collection is null || FamilyName is null)
        {
            return new PixelGlyph(char.IsWhiteSpace(c) ? advance : own ? advance : 0, []);
        }

        try
        {
            lock (_gdi)
            {
                if (_disposed) { return new PixelGlyph(advance, []); }
                return own ? Draw(c, FamilyName, advance, fromCollection: true) : DrawFallback(c);
            }
        }
        catch (Exception e) when (e is PlatformNotSupportedException or DllNotFoundException or ExternalException or ArgumentException)
        {
            return new PixelGlyph(advance, []);
        }
    }

    private PixelGlyph DrawFallback(char c)
    {
        _fallbackFamily ??= ResolveFallbackFamily();
        return Draw(c, _fallbackFamily, advance: null, fromCollection: false);
    }

    /// <summary>The fallback family's name, enumerated once (the installed-font collection is disposed right away).</summary>
    private static string ResolveFallbackFamily()
    {
        using var installed = new InstalledFontCollection();
        var names = installed.Families.Select(f => f.Name).ToHashSet(StringComparer.OrdinalIgnoreCase);
        return FallbackFamilies.FirstOrDefault(names.Contains) ?? FontFamily.GenericSansSerif.Name;
    }

    /// <summary>
    /// Draws one character with antialiasing off into a scratch bitmap and keeps the dots inside the cell
    /// (rows 0..<see cref="CellPixels"/>-1, top = the ascent line). The dot font's own glyphs land exactly on the grid
    /// because its outlines are whole dots at this size; a fallback glyph is moved so its baseline matches the dot font's.
    /// </summary>
    private PixelGlyph Draw(char c, string family, int? advance, bool fromCollection)
    {
        const int Pad = 8;
        var cell = CellPixels;
        using var font = fromCollection
            ? new Font(_collection!.Families[0], cell, FontStyle.Regular, GraphicsUnit.Pixel)
            : new Font(family, cell, FontStyle.Regular, GraphicsUnit.Pixel);
        var ascentDots = (int)MathF.Round(_ascentUnits * (float)cell / _unitsPerEm);
        var fontAscent = font.FontFamily.GetCellAscent(FontStyle.Regular) * (float)cell / font.FontFamily.GetEmHeight(FontStyle.Regular);
        var size = (cell * 2) + (Pad * 2);
        using var bitmap = new Bitmap(size, size, PixelFormat.Format32bppArgb);
        using (var graphics = Graphics.FromImage(bitmap))
        {
            graphics.TextRenderingHint = TextRenderingHint.SingleBitPerPixelGridFit;
            graphics.Clear(Color.Transparent);
            var top = Pad + (fromCollection ? 0f : MathF.Round(ascentDots - fontAscent));
            graphics.DrawString(c.ToString(), font, Brushes.White, new PointF(Pad, top), StringFormat.GenericTypographic);
        }

        var width = advance ?? Math.Clamp((int)MathF.Ceiling(MeasureInk(bitmap, Pad, size)) + 1, 1, cell * 2);
        var runs = new List<GlyphRasterizer.RawRun>();
        for (var y = 0; y < cell; y++)
        {
            var x = 0;
            while (x < width)
            {
                while (x < width && bitmap.GetPixel(Pad + x, Pad + y).A < 128) { x++; }
                var start = x;
                while (x < width && bitmap.GetPixel(Pad + x, Pad + y).A >= 128) { x++; }
                if (start < x) { runs.Add(new GlyphRasterizer.RawRun(start, y, x - start, 1)); }
            }
        }

        return new PixelGlyph(width, [.. runs]);
    }

    /// <summary>Right-most inked column (relative to the pen) — the advance for a fallback glyph.</summary>
    private static int MeasureInk(Bitmap bitmap, int pad, int size)
    {
        var right = 0;
        for (var y = 0; y < size; y++)
        {
            for (var x = pad; x < size; x++)
            {
                if (bitmap.GetPixel(x, y).A >= 128) { right = Math.Max(right, x - pad + 1); }
            }
        }

        return right;
    }
}

/// <summary>One dot-font glyph at native size: its advance and its dot rows as horizontal runs (y = 0 is the cell top).</summary>
public readonly record struct PixelGlyph(int Advance, ImmutableArray<GlyphRasterizer.RawRun> Runs);

/// <summary>The few TrueType tables a dot font needs: head (units per em), hhea (ascent, metric count), hmtx (advances),
/// cmap (format 4 / 12: which characters exist).</summary>
internal readonly record struct TrueTypeTables(int UnitsPerEm, int Ascent, HashSet<int> Codepoints, Dictionary<int, int> AdvanceByCodepoint)
{
    public static TrueTypeTables Read(byte[] data)
    {
        try
        {
            return ReadCore(data);
        }
        catch (Exception e) when (e is ArgumentOutOfRangeException or IndexOutOfRangeException)
        {
            throw new InvalidDataException("The font file is truncated or not TrueType.", e);
        }
    }

    private static TrueTypeTables ReadCore(byte[] data)
    {
        int U16(int at) => BinaryPrimitives.ReadUInt16BigEndian(data.AsSpan(at));
        short S16(int at) => BinaryPrimitives.ReadInt16BigEndian(data.AsSpan(at));
        int U32(int at) => (int)BinaryPrimitives.ReadUInt32BigEndian(data.AsSpan(at));
        var tables = new Dictionary<string, int>();
        var count = U16(4);
        for (var i = 0; i < count; i++)
        {
            var record = 12 + (i * 16);
            tables[System.Text.Encoding.ASCII.GetString(data, record, 4)] = U32(record + 8);
        }

        int Table(string tag) => tables.TryGetValue(tag, out var at) ? at : throw new InvalidDataException($"The font has no '{tag}' table.");
        var unitsPerEm = U16(Table("head") + 18);
        var hhea = Table("hhea");
        var ascent = S16(hhea + 4);
        var metrics = U16(hhea + 34);
        var hmtx = Table("hmtx");
        var glyphOf = ReadCmap(data, Table("cmap"));
        var advances = new Dictionary<int, int>(glyphOf.Count);
        foreach (var (codepoint, glyph) in glyphOf)
        {
            var row = Math.Min(glyph, metrics - 1);
            advances[codepoint] = U16(hmtx + (row * 4));
        }

        return new TrueTypeTables(unitsPerEm, ascent, [.. glyphOf.Keys], advances);
    }

    private static Dictionary<int, int> ReadCmap(byte[] data, int cmap)
    {
        int U16(int at) => BinaryPrimitives.ReadUInt16BigEndian(data.AsSpan(at));
        short S16(int at) => BinaryPrimitives.ReadInt16BigEndian(data.AsSpan(at));
        int U32(int at) => (int)BinaryPrimitives.ReadUInt32BigEndian(data.AsSpan(at));
        int? format4 = null, format12 = null;
        for (var i = 0; i < U16(cmap + 2); i++)
        {
            var record = cmap + 4 + (i * 8);
            var (platform, encoding, offset) = (U16(record), U16(record + 2), cmap + U32(record + 4));
            var format = U16(offset);
            if (format == 12 && (platform == 0 || (platform == 3 && encoding == 10))) { format12 = offset; }
            if (format == 4 && (platform == 0 || (platform == 3 && encoding == 1))) { format4 ??= offset; }
        }

        var map = new Dictionary<int, int>();
        if (format12 is { } f12)
        {
            for (var g = 0; g < U32(f12 + 12); g++)
            {
                var group = f12 + 16 + (g * 12);
                var (start, end, glyph) = (U32(group), U32(group + 4), U32(group + 8));
                for (var c = start; c <= end; c++) { map[c] = glyph + (c - start); }
            }

            return map;
        }

        if (format4 is not { } f4) { throw new InvalidDataException("The font has no Unicode cmap (format 4 or 12)."); }
        var segments = U16(f4 + 6) / 2;
        var ends = f4 + 14;
        var starts = ends + (segments * 2) + 2;
        var deltas = starts + (segments * 2);
        var ranges = deltas + (segments * 2);
        for (var s = 0; s < segments; s++)
        {
            var (start, end, delta, rangeAt) = (U16(starts + (s * 2)), U16(ends + (s * 2)), S16(deltas + (s * 2)), ranges + (s * 2));
            var rangeOffset = U16(rangeAt);
            for (var c = start; c <= end && c != 0xFFFF; c++)
            {
                var glyph = rangeOffset == 0 ? (c + delta) & 0xFFFF : U16(rangeAt + rangeOffset + ((c - start) * 2));
                if (rangeOffset != 0 && glyph != 0) { glyph = (glyph + delta) & 0xFFFF; }
                if (glyph != 0) { map[c] = glyph; }
            }
        }

        return map;
    }
}
