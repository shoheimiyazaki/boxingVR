namespace Aigis.Platform;

/// <summary>
/// A mouse-pointer picture the operating system draws in place of its own arrow — see
/// <see cref="PlatformWindow.SetCursorImage"/>.
/// <para>
/// <b>Handed to the OS, not drawn by the game.</b> The OS composites its cursor in hardware at the
/// display's own rate, so a custom image here costs no latency at all; a game-drawn pointer on the HUD
/// would lag the hand by a whole rendered frame plus the swapchain queue, which is exactly what makes
/// software cursors feel "floaty". That is why this type exists rather than an overlay sprite.
/// </para>
/// <para>
/// <b>Pixels are drawn 1:1, never scaled by the OS.</b> A custom cursor is not DPI-virtualised, so a
/// caller sizes it for the window's own <see cref="PlatformWindow.ContentScale"/> — a 32-pixel arrow on
/// a 200% display is half the size of the system's.
/// </para>
/// </summary>
public sealed class CursorImage
{
    /// <param name="width">Pixel width, at least 1.</param>
    /// <param name="height">Pixel height, at least 1.</param>
    /// <param name="hotspotX">The pixel column that IS the pointer position — the arrow's tip.</param>
    /// <param name="hotspotY">The pixel row that is the pointer position.</param>
    /// <param name="rgba">Top-down rows, 8-bit straight (not premultiplied) RGBA,
    /// <c>width × height × 4</c> bytes. Copied, so the caller may reuse its buffer.</param>
    public CursorImage(int width, int height, int hotspotX, int hotspotY, ReadOnlySpan<byte> rgba)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(width, 1);
        ArgumentOutOfRangeException.ThrowIfLessThan(height, 1);
        ArgumentOutOfRangeException.ThrowIfNegative(hotspotX);
        ArgumentOutOfRangeException.ThrowIfNegative(hotspotY);
        ArgumentOutOfRangeException.ThrowIfGreaterThanOrEqual(hotspotX, width);
        ArgumentOutOfRangeException.ThrowIfGreaterThanOrEqual(hotspotY, height);
        if (rgba.Length != width * height * 4)
        {
            throw new ArgumentException(
                $"Expected {width * height * 4} RGBA bytes for {width}x{height}, got {rgba.Length}.", nameof(rgba));
        }

        Width = width;
        Height = height;
        HotspotX = hotspotX;
        HotspotY = hotspotY;
        _rgba = rgba.ToArray();
    }

    private readonly byte[] _rgba;

    public int Width { get; }

    public int Height { get; }

    public int HotspotX { get; }

    public int HotspotY { get; }

    /// <summary>The pixels, read-only — see the constructor for the layout.</summary>
    public ReadOnlySpan<byte> Rgba => _rgba;

    /// <summary>The alpha of pixel (<paramref name="x"/>, <paramref name="y"/>).</summary>
    public byte AlphaAt(int x, int y) => _rgba[(((y * Width) + x) * 4) + 3];

    /// <summary>A private copy for the windowing backend, which wants a mutable buffer it may keep.</summary>
    internal byte[] CopyPixels() => (byte[])_rgba.Clone();
}
