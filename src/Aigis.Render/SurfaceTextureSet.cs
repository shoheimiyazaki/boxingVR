using System.Numerics;

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
/// <b>Colour textures</b> (2026-11-11, 「テクスチャの問題はエンジンから修整」): an image added with
/// <see cref="Builder.AddColour"/> takes three consecutive layers, its R, G and B, each normalised to the same
/// mean. An instance with <see cref="PrimitiveInstance.TextureInColour"/> reads all three and multiplies its colour per
/// channel; set to <see cref="ColourTexture.Mean"/> it shows the image's own colours. Kept in the same R8 array (rather
/// than a second RGBA image) so the descriptor layout, the mip builder and every existing caller stay as they were.
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

    /// <summary>
    /// Builds a set from detail maps (luminance) and colour images (three channel layers each). Layer indices are handed
    /// out in the order things are added.
    /// </summary>
    public sealed class Builder
    {
        private readonly List<byte[]> _layers = [];
        private int _size;

        /// <summary>Adds a greyscale detail map; returns its layer.</summary>
        public int AddDetail(TextureData image)
        {
            var texels = Check(image);
            _layers.Add(NormalisedLuminance(image.Rgba, texels));
            return _layers.Count - 1;
        }

        /// <summary>
        /// Adds a colour image as three layers (R, G, B), each rescaled so its mean is <see cref="NeutralLevel"/>;
        /// returns the first layer and the image's mean colour (0..1), which the instance's colour should carry.
        /// </summary>
        public ColourTexture AddColour(TextureData image)
        {
            var texels = Check(image);
            var first = _layers.Count;
            var mean = new float[3];
            var gain = new float[3];
            for (var channel = 0; channel < 3; channel++)
            {
                var values = new float[texels];
                for (var t = 0; t < texels; t++)
                {
                    values[t] = image.Rgba[(t * 4) + channel];
                }

                // A dark channel (blue in a wood photo) scaled to mean 128 would clip its bright texels; scale it
                // only as far as its brightest texel allows and let Gain make up the difference on the instance.
                var (layer, average, scale) = Normalised(values, clip: false);
                _layers.Add(layer);
                mean[channel] = average / 255f;
                gain[channel] = average > 0f ? 255f / (2f * average * scale) : 1f;
            }

            return new ColourTexture(first, new Vector3(mean[0], mean[1], mean[2]), new Vector3(gain[0], gain[1], gain[2]));
        }

        public SurfaceTextureSet Build()
        {
            if (_layers.Count == 0)
            {
                throw new InvalidOperationException("A surface texture set needs at least one layer.");
            }

            return new SurfaceTextureSet(_size, [.. _layers]);
        }

        private int Check(TextureData image)
        {
            ArgumentNullException.ThrowIfNull(image);
            var size = image.Width;
            if (image.Height != size || size <= 0 || (size & (size - 1)) != 0)
            {
                throw new ArgumentException($"Surface texture layers must be square powers of two; got {image.Width}x{image.Height}.", nameof(image));
            }

            if (_size != 0 && size != _size)
            {
                throw new ArgumentException($"Every layer must be {_size}x{_size}; got {size}x{size}.", nameof(image));
            }

            _size = size;
            return size * size;
        }
    }

    private static (byte[] Layer, float Mean, float Scale) Normalised(float[] values, bool clip)
    {
        var sum = 0.0;
        var max = 0f;
        foreach (var v in values)
        {
            sum += v;
            max = MathF.Max(max, v);
        }

        var mean = (float)(sum / values.Length);
        var scale = mean > 1f ? NeutralLevel / mean : 1f;
        if (!clip && max > 0f)
        {
            scale = MathF.Min(scale, 255f / max);
        }
        var result = new byte[values.Length];
        for (var t = 0; t < values.Length; t++)
        {
            result[t] = (byte)Math.Clamp(MathF.Round(values[t] * scale), 0f, 255f);
        }

        return (result, mean, scale);
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

/// <summary>
/// A colour image in a <see cref="SurfaceTextureSet"/>: its first layer (R; G and B follow), its mean colour (0..1), and the
/// per-channel gain an instance's colour needs so the image's mean shows as that colour (1 unless a channel was stored
/// below the neutral mean to keep its brightest texels from clipping). <see cref="On"/> and <see cref="Over"/> apply it.
/// </summary>
public readonly record struct ColourTexture(int FirstLayer, Vector3 Mean, Vector3 Gain)
{
    /// <summary>
    /// <paramref name="instance"/> with this texture, tiled every <paramref name="tileMetres"/> metres, its colour set to the
    /// image's mean times <paramref name="tint"/> (1 = the image's own colours).
    /// </summary>
    public PrimitiveInstance On(PrimitiveInstance instance, float tileMetres, Vector3? tint = null)
    {
        var colour = Mean * Gain * (tint ?? Vector3.One);
        return instance with
        {
            Color = new Vector4(colour, instance.Color.W),
            TextureLayer = FirstLayer,
            TextureTileMetres = tileMetres,
            TextureInColour = true,
        };
    }

    /// <summary>
    /// <paramref name="instance"/> with this texture, keeping its own colour: the image only adds its variation around its
    /// mean (grain, weave, stains), so a wall stays the paint it was authored in.
    /// </summary>
    public PrimitiveInstance Over(PrimitiveInstance instance, float tileMetres)
        => instance with
        {
            Color = new Vector4(new Vector3(instance.Color.X, instance.Color.Y, instance.Color.Z) * Gain, instance.Color.W),
            TextureLayer = FirstLayer,
            TextureTileMetres = tileMetres,
            TextureInColour = true,
        };
}
