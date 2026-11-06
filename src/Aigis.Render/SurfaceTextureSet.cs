namespace Aigis.Render;

/// <summary>
/// The engine's surface textures (2026-10-06, 「ビル、建物、壁など様々なものに対して、高品質なテクスチャ」):
/// a stack of square greyscale DETAIL maps that <see cref="PrimitiveInstance.TextureLayer"/> selects
/// and the primitive shader projects onto a surface triplanarly from world position.
/// <para>
/// <b>Detail, not colour.</b> Each layer is luminance only, normalised so its mean is
/// <see cref="NeutralLevel"/>; the shader multiplies the instance's own <see cref="PrimitiveInstance.Color"/>
/// by <c>2 × texel</c>. A wall therefore keeps its authored paint and gains the plaster's grain, and a
/// gameplay zone keeps the exact colour a player reads — texture adds relief and wear, never hue.
/// </para>
/// <para>
/// <b>Immutable; one instance per session.</b> Residency is keyed on instance identity (the same rule
/// <see cref="IrradianceVolume"/> follows), so a caller builds it once and hands the same object to
/// every frame through <see cref="SceneLighting.SurfaceTextures"/>.
/// </para>
/// </summary>
public sealed class SurfaceTextureSet
{
    /// <summary>The texel value that leaves a colour unchanged (mean of every layer).</summary>
    public const byte NeutralLevel = 128;

    private readonly byte[][] _layers;

    private SurfaceTextureSet(int size, byte[][] layers)
    {
        Size = size;
        _layers = layers;
    }

    /// <summary>Width and height of every layer, in texels — a power of two.</summary>
    public int Size { get; }

    /// <summary>How many layers the set holds.</summary>
    public int LayerCount => _layers.Length;

    /// <summary>Layer <paramref name="index"/>'s texels, row-major, one byte each.</summary>
    public ReadOnlySpan<byte> Layer(int index) => _layers[index];

    /// <summary>
    /// Builds the set from colour images, converting each to luminance and rescaling it so its mean
    /// lands on <see cref="NeutralLevel"/>.
    /// </summary>
    /// <exception cref="ArgumentException">No images, sizes differ, or the size is not a power of two.</exception>
    public static SurfaceTextureSet FromImages(IReadOnlyList<TextureData> images)
    {
        ArgumentNullException.ThrowIfNull(images);
        if (images.Count == 0)
        {
            throw new ArgumentException("A surface texture set needs at least one layer.", nameof(images));
        }

        var size = images[0].Width;
        if (size <= 0 || (size & (size - 1)) != 0)
        {
            throw new ArgumentException($"Surface texture layers must be square powers of two; got {size}.", nameof(images));
        }

        var layers = new byte[images.Count][];
        for (var i = 0; i < images.Count; i++)
        {
            var image = images[i];
            if (image.Width != size || image.Height != size)
            {
                throw new ArgumentException(
                    $"Layer {i} is {image.Width}x{image.Height}; every layer must be {size}x{size}.", nameof(images));
            }

            layers[i] = NormalisedLuminance(image.Rgba, size * size);
        }

        return new SurfaceTextureSet(size, layers);
    }

    private static byte[] NormalisedLuminance(ReadOnlySpan<byte> rgba, int texels)
    {
        var luma = new float[texels];
        var sum = 0.0;
        for (var t = 0; t < texels; t++)
        {
            var value = (0.2126f * rgba[t * 4]) + (0.7152f * rgba[(t * 4) + 1]) + (0.0722f * rgba[(t * 4) + 2]);
            luma[t] = value;
            sum += value;
        }

        var mean = (float)(sum / texels);
        var scale = mean > 1f ? NeutralLevel / mean : 1f;
        var result = new byte[texels];
        for (var t = 0; t < texels; t++)
        {
            result[t] = (byte)Math.Clamp(MathF.Round(luma[t] * scale), 0f, 255f);
        }

        return result;
    }
}
