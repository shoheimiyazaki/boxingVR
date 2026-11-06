using System.Collections.Concurrent;
using System.Collections.Immutable;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.Drawing.Text;
using System.Runtime.InteropServices;

namespace Aigis.Render;

/// <summary>
/// Rasterizes text with an OS-installed font (via <c>System.Drawing</c>/GDI+) into scanline
/// "runs" — horizontal spans of "on" pixels, one row at a time — rather than a font-atlas
/// texture. This keeps the whole text pipeline inside the engine's existing untextured
/// primitive/overlay pipelines (no new sampler/descriptor-set infrastructure, see hard rule
/// 6's headless requirement and the render layer's zero-descriptor-set posture today):
/// every glyph becomes a handful of solid-colour rectangles instead of a textured quad.
/// <para>
/// Promoted from <c>tools/Aigis.Evolve/SystemFontRasterizer.cs</c> (TASK-051) so both the
/// PPO-reward-graph tooling (world-space labels) and the engine's screen-space HUD
/// (<see cref="ScreenText"/>) share one GDI+ rasterizer/cache instead of duplicating it —
/// this class holds only the OS-font → raw-pixel-run conversion; unit/orientation choices
/// (world-space flip-Y-up vs. screen-space top-down) stay with each caller.
/// </para>
/// <para><b>Font fallback</b>: uses <see cref="FontFamily.GenericSansSerif"/>, which .NET
/// itself resolves to whatever sans-serif is installed (Segoe UI / Arial / DejaVu Sans /
/// etc. depending on OS) — there is no explicit "named font unavailable" branch to test
/// because the CLR never surfaces that failure via this API; the failure modes this class
/// guards are the coarser ones below.</para>
/// <para><b>Never throws for missing text-rendering capability</b>: a platform without
/// GDI+ (<see cref="PlatformNotSupportedException"/>), a missing native dependency
/// (<see cref="DllNotFoundException"/>), or any other GDI+-side failure
/// (<see cref="ExternalException"/>) all degrade to an empty <see cref="RawGlyphs"/> — a
/// HUD readout must never crash a game or agent render over a missing font.</para>
/// </summary>
public static class GlyphRasterizer
{
    /// <summary>Default rasterization height for the legacy <see cref="Rasterize(string)"/>
    /// overload (world-space callers, e.g. <c>sandbox/Aigis.Evolve/SystemFontRasterizer</c>,
    /// that scale the result arbitrarily in world units and were never the TASK-099 complaint).</summary>
    private const float ReferencePixelHeight = 18f;

    /// <summary>Clamp bounds for <see cref="Rasterize(string, float)"/>'s requested pixel height —
    /// wide enough to cover the HUD's full type scale (<c>HudTypeScale.Micro</c> 8px..<c>Display</c>
    /// 40px, see <c>games/Game.Triage/HudStyle.cs</c>) plus headroom, while still bounding how many
    /// distinct GDI+ font sizes (and thus cache entries per string) one caller can produce.</summary>
    private const int MinRasterHeight = 4;
    private const int MaxRasterHeight = 256;

    /// <summary>
    /// Hard ceiling on the scratch bitmap's width, in pixels. A backstop against a pathological
    /// caller (a whole paragraph as one draw), <b>not</b> a layout budget — see
    /// <c>RasterizeUnsafe</c>'s own comment on the per-character guess TASK-115 removed from here.
    /// Generous enough that no HUD line reaches it: at this engine's largest type step (40px) it is
    /// still hundreds of characters, and text that long is a caller bug (it would not fit any screen)
    /// rather than something to render.
    /// </summary>
    private const int MaxBitmapWidth = 8192;

    /// <summary>Upper bound on the number of distinct strings <see cref="Cache"/> holds at
    /// once, and the eviction policy applied when it's reached.
    /// <para><b>Design choice — bounded per-string cache with clear-on-overflow, not a
    /// per-glyph cache.</b> The code review that added this bound (TASK-051 follow-up)
    /// considered switching to a per-glyph cache (naturally bounded by character-set size,
    /// so e.g. every distinct HUD score/timer string would share glyph rasterizations
    /// instead of each becoming a new entry). That was rejected for this pass: GDI+'s
    /// <c>DrawString</c>/<c>MeasureString</c> shape and kerning a whole string at once, so
    /// splitting to per-glyph rasterization and recompositing runs would mean re-deriving
    /// per-character advance widths ourselves — a real behavior change to how runs are
    /// assembled, with the legible-rendering regression risk that implies, for a MEDIUM
    /// finding whose immediate risk is unbounded *memory* growth, not incorrect output.
    /// A hard cap with a simple, thread-safe eviction sweep (see <see cref="Rasterize(string, float)"/>)
    /// fixes the actual reported risk — a future high-cardinality caller (e.g. a per-frame
    /// score/timer HUD string) degrades to "occasionally re-rasterize", never to unbounded
    /// growth — without touching the rasterization/composition logic at all. Revisit
    /// per-glyph if profiling ever shows the re-rasterization cost from eviction actually
    /// matters (it currently doesn't: <see cref="RasterizeUnsafe"/> uses <c>LockBits</c>,
    /// not the slow per-pixel <c>GetPixel</c> scan this cache used to lean on).</para>
    /// <para>512 is comfortably above any bounded vocabulary this class was designed for
    /// (menu/HUD labels, digit-by-digit counters) while still capping worst-case memory to a
    /// few hundred small bitmaps' worth of runs, not an unbounded one. Since TASK-099 the cache
    /// key is (text, rasterized-at height) rather than text alone, so the same label now occupies
    /// one entry per distinct size it's drawn at (<c>HudTypeScale</c> has 6 steps) — still well
    /// within 512 given the type scale's own bounded vocabulary, but worth remembering when
    /// judging "how many distinct strings" this bound actually covers.</para>
    /// <para>TASK-115 note: an entry's cost is its extracted <see cref="RawRun"/> list, <b>not</b> the
    /// scratch bitmap — that is disposed before the entry is stored. So removing the old per-character
    /// width budget (see <c>RasterizeUnsafe</c>) does not change this bound's memory story even though
    /// a long string's intermediate bitmap is now much wider: run count scales with the ink actually
    /// present, and a long HUD line is one row of runs regardless of how wide it measures.</para></summary>
    private const int MaxCacheEntries = 512;

    /// <summary>
    /// UI text is predominantly Japanese.  Prefer the Windows UI families that are hinted for
    /// small Japanese screen text, while retaining the platform's generic sans-serif fallback
    /// for headless/Linux environments where neither family is installed.
    /// </summary>
    private static readonly string[] PreferredFontFamilies = ["Yu Gothic UI", "Segoe UI"];

    /// <summary>Keyed by (text, rasterized-at pixel height rounded to the nearest int), not text
    /// alone — see <see cref="Rasterize(string, float)"/>'s doc comment for why the cache key had
    /// to grow a size dimension (TASK-099).</summary>
    private static readonly ConcurrentDictionary<(string Text, int Height), RawGlyphs> Cache = new();

    /// <summary>Test-only visibility into the cache's current size (see
    /// <see cref="MaxCacheEntries"/>) — proves eviction actually bounds growth without
    /// exposing cache internals as public API.</summary>
    internal static int CacheEntryCountForTests => Cache.Count;

    /// <summary>One row's contiguous span of "on" pixels, in the reference-rasterization's
    /// own pixel space (top-left origin, Y increasing downward — a bitmap's natural row
    /// order). <paramref name="Height"/> is always exactly 1 pixel row; run-merging across
    /// rows is deliberately not attempted (keeps the geometry a flat list of rectangles).</summary>
    public readonly record struct RawRun(int X, int Y, int Width, int Height);

    /// <summary>The rasterized result for one text string at <see cref="ReferencePixelHeight"/>:
    /// its runs plus the reference bitmap's pixel dimensions (needed by a caller to compute
    /// its own "world unit" or "screen pixel" cell size and total width/height).
    /// <para><paramref name="Runs"/> is an <see cref="ImmutableArray{T}"/>, not a plain
    /// array: <see cref="Rasterize(string, float)"/> hands back the *same* cached instance to every caller
    /// (proven by <c>GlyphRasterizerTests.Rasterize_SameTextTwice_ReturnsCachedResult</c>),
    /// so the element type must not be mutable through a caller's reference — a plain
    /// <c>RawRun[]</c> would let one caller's edit corrupt every other consumer sharing the
    /// same cache entry (<see cref="ScreenText"/> and <c>tools/Aigis.Evolve.SystemFontRasterizer</c>
    /// both draw from this cache). <see cref="ImmutableArray{T}"/> costs nothing extra here:
    /// it wraps the same backing array reference, so returning the cached value is still
    /// zero-copy — only the compile-time guarantee changes.</para>
    /// <para><paramref name="ContentHeight"/> is the text's own measured line height at the reference
    /// size, WITHOUT the safety padding <paramref name="BitmapHeight"/> includes. It exists because a
    /// caller scaling to a requested pixel height must divide by this and not by
    /// <paramref name="BitmapHeight"/> — dividing by the padded height is what made every text in the
    /// engine render at roughly 0.6x its requested size (found by frame-reviewer on TASK-094's HUD:
    /// display-sized text read fine, caption-sized kanji were unreadable). The padding exists so
    /// antialiased glyph edges are never clipped by the bitmap bounds; it is not part of the type
    /// size.</para></summary>
    public readonly record struct RawGlyphs(
        ImmutableArray<RawRun> Runs, int BitmapWidth, int BitmapHeight, int ContentHeight);

    /// <summary>
    /// Convenience overload for world-space callers that scale the result arbitrarily and never
    /// hit the TASK-099 stroke-loss defect (rasterizes at <see cref="ReferencePixelHeight"/>, same
    /// as before that fix). Screen-space HUD text must use <see cref="Rasterize(string, float)"/>
    /// instead, which rasterizes at the size it will actually be shown at.
    /// </summary>
    /// <exception cref="ArgumentException">Only for a null/blank <paramref name="text"/> —
    /// a programmer error (empty label), not a runtime font-availability failure.</exception>
    public static RawGlyphs Rasterize(string text) => Rasterize(text, ReferencePixelHeight);

    /// <summary>
    /// Rasterizes <paramref name="text"/> at (the nearest whole pixel to) <paramref name="pixelHeight"/>
    /// and returns its scanline runs. Never throws for a missing/broken font — returns
    /// <c>RawGlyphs([], 0, 0, 0)</c> instead so callers can treat "no text rendering available here"
    /// as "draw nothing" rather than crashing.
    /// <para><b>TASK-099</b>: earlier versions always rasterized at a fixed 18px reference and left
    /// scaling to the caller's later "multiply by cell size" step — cheap on the cache (key = text
    /// alone) but wrong for a HUD with a real type scale, because every size below 18px was a
    /// DOWNSCALE of a bitmap whose antialiased strokes were already ~1px wide: shrinking a 1px stroke
    /// below 1px doesn't make it thinner, it makes it disappear or smear (kanji, with more strokes
    /// packed into the same em, hit this hardest). Rasterizing at the size it will actually be shown
    /// at means the GDI+ hinting/antialiasing that produces each stroke happens at (near) native
    /// resolution instead of being squashed afterward. <paramref name="pixelHeight"/> is rounded to
    /// the nearest int and clamped to [<see cref="MinRasterHeight"/>, <see cref="MaxRasterHeight"/>]
    /// both for the GDI+ call and the cache key, so the cache key had to grow from text alone to
    /// (text, rounded height) — see <see cref="Cache"/>.</para>
    /// <para>Bounded: if the cache already holds <see cref="MaxCacheEntries"/> entries, it is cleared
    /// before this call's entry is added (see <see cref="MaxCacheEntries"/>'s doc comment for why a
    /// full clear, not a true LRU, is the chosen eviction policy).</para>
    /// </summary>
    /// <exception cref="ArgumentException">For a null/blank <paramref name="text"/> — a programmer
    /// error (empty label), not a runtime font-availability failure.</exception>
    /// <exception cref="ArgumentOutOfRangeException">For a non-finite or non-positive
    /// <paramref name="pixelHeight"/> — callers such as <see cref="ScreenText"/> are expected to
    /// have already filtered these (a HUD draw with a degenerate size draws nothing, it doesn't ask
    /// the rasterizer to guess), so reaching here with one is a caller bug, not a font-availability
    /// failure; it must not silently fall back to some other size.</exception>
    public static RawGlyphs Rasterize(string text, float pixelHeight)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(text);
        if (!float.IsFinite(pixelHeight) || pixelHeight <= 0f)
        {
            throw new ArgumentOutOfRangeException(nameof(pixelHeight), pixelHeight, "must be finite and positive.");
        }

        var rasterHeight = Math.Clamp((int)MathF.Round(pixelHeight), MinRasterHeight, MaxRasterHeight);

        if (Cache.Count >= MaxCacheEntries)
        {
            Cache.Clear();
        }

        return Cache.GetOrAdd((text, rasterHeight), key => RasterizeCore(key.Text, key.Height));
    }

    private static RawGlyphs RasterizeCore(string text, int rasterHeight)
    {
        try
        {
            return RasterizeUnsafe(text, rasterHeight);
        }
        catch (PlatformNotSupportedException)
        {
            return new RawGlyphs([], 0, 0, 0);
        }
        catch (DllNotFoundException)
        {
            return new RawGlyphs([], 0, 0, 0);
        }
        catch (ExternalException)
        {
            return new RawGlyphs([], 0, 0, 0);
        }
    }

    private static RawGlyphs RasterizeUnsafe(string text, int rasterHeight)
    {
        using var measureBitmap = new Bitmap(4, 4);
        using var measureGraphics = Graphics.FromImage(measureBitmap);
        using var font = FontForHeight(rasterHeight);
        var measured = measureGraphics.MeasureString(text, font, PointF.Empty, StringFormat.GenericTypographic);
        // The bitmap is sized from GDI+'s OWN measurement of this string, clamped only by
        // MaxBitmapWidth — never by a per-character guess.
        //
        // TASK-115 found the guess this replaces (`rasterHeight * 32`, "32 characters' worth …
        // generous for any real HUD string") silently truncating real strings: a 75- and a 152-
        // character Japanese review both measured EXACTLY 352px at 11px, i.e. both saturated the
        // 32-character budget, so MeasurePixelWidth reported a wrong (equal!) width for two very
        // different strings AND the render clipped both at the same 32nd character. A sentence of
        // Japanese prose is routinely 60-150 characters, so "any real HUD string" was simply false
        // — and the failure is invisible, which is the same silent-truncation class TASK-099 fixed
        // on the height axis and TASK-099's own code review predicted for this axis.
        var width = Math.Clamp((int)MathF.Ceiling(measured.Width) + 4, 1, MaxBitmapWidth);
        // The text's own line height, and separately the bitmap that carries it. The +4 is slack so an
        // antialiased glyph edge is never clipped by the bitmap bounds — it is NOT part of the type size,
        // and a caller scaling to a requested pixel height must divide by contentHeight (see RawGlyphs).
        // Clamped relative to MaxRasterHeight (not a fixed constant) because rasterHeight itself now
        // ranges up to MaxRasterHeight, not just the old fixed 18px reference.
        var contentHeight = Math.Clamp((int)MathF.Ceiling(measured.Height), 1, MaxRasterHeight * 2);
        var height = Math.Clamp(contentHeight + 4, 1, (MaxRasterHeight * 2) + 8);

        using var bitmap = new Bitmap(width, height, PixelFormat.Format32bppArgb);
        using var graphics = Graphics.FromImage(bitmap);
        graphics.SmoothingMode = SmoothingMode.AntiAlias;
        graphics.InterpolationMode = InterpolationMode.HighQualityBicubic;
        graphics.TextRenderingHint = TextRenderingHint.AntiAliasGridFit;
        graphics.TextContrast = 4;
        graphics.Clear(Color.Transparent);
        // Brushes.White, not a per-call `new SolidBrush(Color.White)`: the built-in is a shared,
        // process-lifetime brush of exactly this colour, so this is one fewer GDI+ object created
        // and destroyed on every cache miss (see ResolvedFontFamily on why misses are the frames
        // that matter). Not disposed for the same reason it is not created — it is not ours.
        graphics.DrawString(text, font, Brushes.White, new PointF(1f, 0f), StringFormat.GenericTypographic);

        var runs = new List<RawRun>();
        ScanRuns(bitmap, width, height, runs);

        return new RawGlyphs(runs.ToImmutableArray(), width, height, contentHeight);
    }

    /// <summary>
    /// The chosen family's NAME, resolved <b>once</b> per process. <see cref="FontFamily.Families"/>
    /// asks GDI+ to enumerate every installed font, and that enumeration was previously repeated on
    /// every cache MISS — i.e. every time a HUD showed a string it had not shown before at that
    /// size, which is exactly the frame a player can feel (a new patient card's name appearing
    /// mid-round). Which family wins cannot change during a run, so repeating it was pure redundant
    /// work.
    /// <para>
    /// <b>A string, not the <see cref="FontFamily"/> object</b> — and this distinction was found the
    /// hard way, by crashing the test host. The families <see cref="FontFamily.Families"/> hands back
    /// belong to an installed-font collection created for that call; keeping one alive past that
    /// collection leaves a <see cref="FontFamily"/> whose native handle can already be gone, and the
    /// next <c>new Font(family, …)</c> takes the process down natively — no managed exception, just a
    /// dead test host, at an unpredictable moment. Caching the name keeps the whole win (the
    /// enumeration is still paid once) with none of the lifetime coupling, because
    /// <see cref="Font"/>'s family-name constructor resolves the family itself.
    /// </para>
    /// <para><b>Honest scope:</b> this is redundant-work removal, not a fix for a specific measured
    /// hitch. The one reproducible mid-round spike found while investigating this file (~80 ms at
    /// the first patient spawn on a windowed Level6 run) was attributed by direct phase timing to
    /// <c>IGame.Step</c>, not to rasterization, and removing this enumeration did not move it.</para>
    /// <para><see cref="Lazy{T}"/> with the default thread-safety mode, not a plain static
    /// initializer: <see cref="Rasterize(string, float)"/> is reachable concurrently (its cache is a
    /// <see cref="ConcurrentDictionary{TKey, TValue}"/> for that reason) and the enumeration must
    /// happen exactly once even so.</para>
    /// </summary>
    private static readonly Lazy<string?> ResolvedFontFamilyName = new(() =>
        FontFamily.Families.FirstOrDefault(f => PreferredFontFamilies.Contains(f.Name, StringComparer.OrdinalIgnoreCase))?.Name);

    /// <summary>
    /// A fresh <see cref="Font"/> per rasterization, at the resolved family (or the platform's
    /// generic sans-serif when none of <see cref="PreferredFontFamilies"/> is installed — the
    /// original fallback, unchanged).
    /// <para><b>Deliberately not cached per height.</b> <c>System.Drawing</c> guarantees thread
    /// safety only for a type's static members, and <see cref="Rasterize(string, float)"/> is
    /// reachable concurrently (its <see cref="Cache"/> is a
    /// <see cref="ConcurrentDictionary{TKey, TValue}"/> precisely because of that) — a shared
    /// <see cref="Font"/> instance handed to <c>MeasureString</c>/<c>DrawString</c> on two threads at
    /// once is outside what the API promises. The expensive half was never the <see cref="Font"/>
    /// construction anyway; it was <see cref="FontFamily.Families"/>, which is now paid once.</para>
    /// </summary>
    private static Font FontForHeight(int rasterHeight) =>
        ResolvedFontFamilyName.Value is { } familyName
            ? new Font(familyName, rasterHeight, FontStyle.Regular, GraphicsUnit.Pixel)
            : new Font(FontFamily.GenericSansSerif, rasterHeight, FontStyle.Regular, GraphicsUnit.Pixel);

    /// <summary>
    /// Scans <paramref name="bitmap"/> for horizontal "on" spans (alpha &gt;= 96) via
    /// <c>Bitmap.LockBits</c> — a direct, stride-based read of the raw pixel buffer.
    /// Replaces a per-pixel <see cref="Bitmap.GetPixel"/> scan (flagged in the TASK-051 code
    /// review as the classic slow GDI+ anti-pattern: each <c>GetPixel</c> call round-trips
    /// through the unmanaged bitmap and re-validates pixel format) with one bulk lock and
    /// direct pointer arithmetic over the mapped buffer.
    /// <para><see cref="PixelFormat.Format32bppArgb"/>'s in-memory byte order per pixel is
    /// B, G, R, A (little-endian ARGB as a 32-bit word) — the alpha byte is always at
    /// offset 3 from each pixel's first byte, which is what the per-row indexing below
    /// relies on.</para>
    /// </summary>
    private static void ScanRuns(Bitmap bitmap, int width, int height, List<RawRun> runs)
    {
        var bounds = new Rectangle(0, 0, width, height);
        var bits = bitmap.LockBits(bounds, ImageLockMode.ReadOnly, PixelFormat.Format32bppArgb);
        try
        {
            var stride = bits.Stride;
            unsafe
            {
                var basePtr = (byte*)bits.Scan0;
                for (var y = 0; y < height; y++)
                {
                    var row = basePtr + (y * stride);
                    var x = 0;
                    while (x < width)
                    {
                        while (x < width && row[(x * 4) + 3] < 96)
                        {
                            x++;
                        }

                        var start = x;
                        while (x < width && row[(x * 4) + 3] >= 96)
                        {
                            x++;
                        }

                        if (start < x)
                        {
                            runs.Add(new RawRun(start, y, x - start, 1));
                        }
                    }
                }
            }
        }
        finally
        {
            bitmap.UnlockBits(bits);
        }
    }
}
