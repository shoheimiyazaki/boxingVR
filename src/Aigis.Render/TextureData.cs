using System.Numerics;
using Aigis.Foundation;

namespace Aigis.Render;

/// <summary>
/// An immutable 8-bit RGBA image ready for GPU upload — the engine's first texture-asset type
/// (TASK-159). Before this, <c>Aigis.Render</c> could only <em>write</em> pixels out
/// (<see cref="Aigis.Foundation.PngWriter"/>, agent screenshots); there was no way to bring
/// authored image bytes in, which is what blocked TASK-109's treatment/item icons and TASK-115's
/// greyscale portrait.
/// <para>
/// <b>Why this lives in <c>Aigis.Render</c></b> and not a new asset layer: it is GPU-upload-shaped
/// data whose only consumer is the renderer, and <c>Aigis.Render</c> already references
/// <c>Aigis.Foundation</c> (where the format-specific <see cref="PngReader"/> belongs, exactly as
/// <see cref="PngWriter"/> does — zero Vulkan dependency). It is explicitly <b>not</b> a fit for
/// <c>Aigis.Content</c>, whose documented scope is game-format-agnostic content-building primitives
/// with zero project references (tilemap/grid/direction vocabulary), not pixel buffers bound for a
/// sampler.
/// </para>
/// <para>
/// <b>Instance identity matters.</b> The renderers cache the GPU upload of a
/// <see cref="TextureData"/> keyed on the <em>instance</em>, which is sound precisely because this
/// type is immutable. Build a texture ONCE (a game's constructor, a static field) and reuse that
/// same instance every frame. Constructing a fresh <see cref="TextureData"/> per frame would
/// re-upload every frame and, by design, throw once the renderer's bounded texture cache fills —
/// a loud failure instead of silent VRAM growth. See <c>OverlayImagePipeline</c>.
/// </para>
/// </summary>
public sealed class TextureData
{
    private readonly byte[] _rgba;

    /// <summary>Wraps <paramref name="rgba"/> (8-bit RGBA, row-major, <b>row 0 first</b> — the top
    /// row of the image). The array is copied, so the caller may reuse its buffer and this instance
    /// stays genuinely immutable.</summary>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="width"/> or
    /// <paramref name="height"/> is not positive.</exception>
    /// <exception cref="ArgumentException"><paramref name="rgba"/> is not exactly
    /// <c>width * height * 4</c> bytes — a size mismatch here would upload garbage rows, so it is
    /// rejected rather than truncated or padded.</exception>
    public TextureData(int width, int height, ReadOnlySpan<byte> rgba)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(width);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(height);

        var expected = width * height * 4;
        if (rgba.Length != expected)
        {
            throw new ArgumentException(
                $"Expected exactly {expected} bytes for a {width}x{height} RGBA image, got {rgba.Length}.",
                nameof(rgba));
        }

        Width = width;
        Height = height;
        _rgba = rgba.ToArray();
    }

    /// <summary>Width in pixels.</summary>
    public int Width { get; }

    /// <summary>Height in pixels.</summary>
    public int Height { get; }

    /// <summary>The pixel bytes: 8-bit RGBA, row-major, row 0 = the image's TOP row. Exposed as a
    /// <see cref="ReadOnlySpan{T}"/> so no caller can mutate the data the upload cache assumes is
    /// stable for this instance's lifetime.</summary>
    public ReadOnlySpan<byte> Rgba => _rgba;

    /// <summary>Total bytes an upload of this texture moves — used by the renderers to size a
    /// staging buffer.</summary>
    public int ByteLength => _rgba.Length;

    /// <summary>Loads a PNG from disk. Supports the subset documented on <see cref="PngReader"/>
    /// (8-bit non-interlaced greyscale/RGB/RGBA); anything else throws rather than decoding to
    /// plausible-but-wrong pixels.</summary>
    public static TextureData FromPng(string path)
    {
        var decoded = PngReader.ReadRgba(path);
        return new TextureData(decoded.Width, decoded.Height, decoded.Rgba);
    }

    /// <summary>Loads a PNG from a stream — the path an embedded resource or an in-memory asset
    /// takes, and how a test loads a fixture without touching disk.</summary>
    public static TextureData FromPng(Stream input)
    {
        var decoded = PngReader.ReadRgba(input);
        return new TextureData(decoded.Width, decoded.Height, decoded.Rgba);
    }

    /// <summary>A single-colour texture. Useful as a placeholder for a missing asset and as the
    /// simplest possible upload-path smoke test.</summary>
    public static TextureData Solid(int width, int height, Vector4 color)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(width);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(height);

        var rgba = new byte[width * height * 4];
        var packed = Pack(color);
        for (var i = 0; i < rgba.Length; i += 4)
        {
            rgba[i] = packed[0];
            rgba[i + 1] = packed[1];
            rgba[i + 2] = packed[2];
            rgba[i + 3] = packed[3];
        }

        return new TextureData(width, height, rgba);
    }

    /// <summary>
    /// A four-quadrant texture: <paramref name="topLeft"/> et al. fill the corresponding quarters.
    /// Deliberately ASYMMETRIC in both axes, which is the point — a symmetric checkerboard renders
    /// identically under a horizontal flip, a vertical flip, and a UV transpose, so it would hide
    /// exactly the orientation bugs this engine has shipped before (see
    /// <see cref="OverlaySource"/>'s notes on competing Y flips). Four distinguishable quadrants
    /// make any of those three mistakes visible in one glance at one frame, which is why the
    /// TASK-159 render tests sample this rather than a two-colour grid.
    /// </summary>
    public static TextureData Quadrants(int width, int height, Vector4 topLeft, Vector4 topRight, Vector4 bottomLeft, Vector4 bottomRight)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(width, 2);
        ArgumentOutOfRangeException.ThrowIfLessThan(height, 2);

        var rgba = new byte[width * height * 4];
        var midX = width / 2;
        var midY = height / 2;

        for (var y = 0; y < height; y++)
        {
            for (var x = 0; x < width; x++)
            {
                // Row 0 is the TOP row (see Rgba's doc comment), so y < midY is the top half.
                var color = y < midY
                    ? (x < midX ? topLeft : topRight)
                    : (x < midX ? bottomLeft : bottomRight);
                var packed = Pack(color);
                var dst = ((y * width) + x) * 4;
                rgba[dst] = packed[0];
                rgba[dst + 1] = packed[1];
                rgba[dst + 2] = packed[2];
                rgba[dst + 3] = packed[3];
            }
        }

        return new TextureData(width, height, rgba);
    }

    private static byte[] Pack(Vector4 color) =>
    [
        ToByte(color.X),
        ToByte(color.Y),
        ToByte(color.Z),
        ToByte(color.W),
    ];

    private static byte ToByte(float channel01) => (byte)Math.Clamp(MathF.Round(channel01 * 255f), 0f, 255f);
}

/// <summary>
/// One textured screen-space overlay draw (TASK-159) — the sampled-texture counterpart to
/// <see cref="OverlayQuad"/>, in the identical pixel-space convention (origin top-left, Y down,
/// centre-anchored) and mapped to NDC by the identical <see cref="OverlayRect"/> helper, so an
/// icon and a text quad at the same pixel rect land on exactly the same screen rect.
/// <para>
/// <paramref name="Tint"/> multiplies the sampled texel, so <see cref="Vector4.One"/> draws the
/// image as authored, a greyed tint dims it, and lowering <c>W</c> fades it — the pipeline
/// alpha-blends, unlike the opaque text-overlay pipeline. Rotation and cropping are deliberately
/// NOT expressible here (the rect is scale+translate only): TASK-159 records both as follow-ups,
/// since a rotated mark can be authored into the image itself.
/// </para>
/// <para>
/// <paramref name="Texture"/> must be a long-lived instance reused across frames — see
/// <see cref="TextureData"/>'s note on instance identity.
/// </para>
/// </summary>
public readonly record struct OverlayImageDraw(
    TextureData Texture,
    Vector2 CenterPixels,
    Vector2 SizePixels,
    Vector4 Tint)
{
    /// <summary>Draws <paramref name="texture"/> untinted at its authored pixel size, centred on
    /// <paramref name="centerPixels"/> — the common case for a HUD icon.</summary>
    public OverlayImageDraw(TextureData texture, Vector2 centerPixels)
        : this(
            texture ?? throw new ArgumentNullException(nameof(texture)),
            centerPixels,
            new Vector2(texture.Width, texture.Height),
            Vector4.One)
    {
    }
}
