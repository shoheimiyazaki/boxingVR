using System.Buffers.Binary;
using System.IO.Compression;

namespace Aigis.Foundation;

/// <summary>
/// One decoded image: 8-bit RGBA, <paramref name="Rgba"/> in row-major order with row 0 FIRST
/// (the PNG's top row first — the orientation the GPU upload path and every sampler UV assume;
/// see <c>Aigis.Render.TextureData</c>). <paramref name="Rgba"/> is always exactly
/// <c>Width * Height * 4</c> bytes and is freshly allocated per decode, so ownership passes to
/// the caller — <see cref="PngReader"/> keeps no reference to it.
/// </summary>
public readonly record struct DecodedImage(int Width, int Height, byte[] Rgba);

/// <summary>
/// Minimal, dependency-free PNG <b>decoder</b> — <see cref="PngWriter"/>'s missing counterpart
/// (TASK-159). The engine could emit screenshots and golden artifacts but could not read an
/// image at all, which is what blocked every HUD icon/portrait consumer (TASK-109, TASK-115):
/// there was no way to get authored image bytes into the engine, only out of it. Lives beside
/// <see cref="PngWriter"/> in Aigis.Foundation for the same reason it does — zero Vulkan
/// dependency, so <c>Aigis.Render</c> can consume it without anyone pulling in a native image
/// library, keeping the stack pure-managed and license-clean.
/// <para>
/// <b>Supported:</b> 8-bit non-interlaced greyscale / greyscale+alpha / truecolour /
/// truecolour+alpha (PNG colour types 0, 4, 2, 6), all five per-row filter types
/// (None/Sub/Up/Average/Paeth), IDAT split across any number of chunks, and unknown ancillary
/// chunks (which are skipped). Everything is widened to 8-bit RGBA, alpha 255 where the source
/// carries none.
/// </para>
/// <para>
/// <b>Deliberately unsupported</b> — and thrown on, never silently approximated: interlaced
/// (Adam7), 16-bit depths, sub-byte depths, and palette colour type 3. A HUD icon that decodes
/// to plausible-but-wrong pixels is far harder to diagnose than one that fails loudly at load
/// with the unsupported feature named, so this follows the engine's fail-fast-at-the-boundary
/// convention rather than degrading. Add a variant here when a real asset needs it.
/// </para>
/// </summary>
public static class PngReader
{
    private static readonly byte[] Signature = [137, 80, 78, 71, 13, 10, 26, 10];

    /// <summary>
    /// Upper bound on a decoded image's byte size (256 MiB of RGBA — roughly an 8192x8192 texture),
    /// and on any single chunk's declared payload.
    /// <para>
    /// This is a safety bound, not a capability statement. PNG's IHDR permits dimensions up to
    /// 2^31-1, and <c>width * height * 4</c> in <see cref="int"/> arithmetic <b>silently wraps</b>
    /// for large values: a forged header could therefore produce a tiny allocation that later code
    /// indexes with the original huge width, throwing an undocumented
    /// <see cref="IndexOutOfRangeException"/> instead of this class's promised
    /// <see cref="InvalidDataException"/>. Likewise a chunk header claiming ~2 GB would have
    /// allocated that much from eight untrusted bytes, before the stream was even known to contain
    /// it. Checking the size in 64-bit arithmetic against this bound closes both, and keeps the
    /// documented "fails loudly with a named reason" contract true for hostile input, not just
    /// well-formed input. Raise it if a genuine asset ever needs to be bigger.
    /// </para>
    /// </summary>
    private const long MaxImageBytes = 256L * 1024 * 1024;

    /// <summary>Decodes the PNG file at <paramref name="path"/>. See <see cref="ReadRgba(Stream)"/>
    /// for the supported subset and the exceptions thrown.</summary>
    public static DecodedImage ReadRgba(string path)
    {
        ArgumentException.ThrowIfNullOrEmpty(path);
        using var fs = File.OpenRead(path);
        return ReadRgba(fs);
    }

    /// <summary>Decodes a PNG from <paramref name="input"/>, reading from the current position.</summary>
    /// <exception cref="InvalidDataException">The stream is not a well-formed PNG — bad signature,
    /// truncated, a corrupt chunk length, or missing IHDR/IDAT.</exception>
    /// <exception cref="NotSupportedException">A valid PNG using a variant outside the supported
    /// subset documented on <see cref="PngReader"/> (interlaced, 16-bit, paletted, sub-byte depth).
    /// The message names the offending feature.</exception>
    public static DecodedImage ReadRgba(Stream input)
    {
        ArgumentNullException.ThrowIfNull(input);

        ReadSignature(input);

        var width = 0;
        var height = 0;
        var bytesPerPixel = 0;
        byte colorType = 0;
        var sawHeader = false;
        // IDAT payloads are concatenated BEFORE inflating: the zlib stream spans chunk
        // boundaries, so inflating each chunk independently would fail on any real encoder's
        // output (see PngReaderTests.ReadRgba_HandlesIdatSplitAcrossChunks).
        using var compressed = new MemoryStream();

        while (true)
        {
            var (type, data) = ReadChunk(input);

            switch (type)
            {
                case "IHDR":
                    if (data.Length < 13)
                    {
                        throw new InvalidDataException($"PNG IHDR chunk is {data.Length} bytes, expected at least 13.");
                    }

                    width = BinaryPrimitives.ReadInt32BigEndian(data.AsSpan(0));
                    height = BinaryPrimitives.ReadInt32BigEndian(data.AsSpan(4));
                    var bitDepth = data[8];
                    colorType = data[9];
                    var compressionMethod = data[10];
                    var filterMethod = data[11];
                    var interlaceMethod = data[12];

                    if (width <= 0 || height <= 0)
                    {
                        throw new InvalidDataException($"PNG dimensions must be positive; got {width}x{height}.");
                    }

                    if (interlaceMethod != 0)
                    {
                        throw new NotSupportedException(
                            $"Interlaced PNG (Adam7, interlace method {interlaceMethod}) is not supported — re-export the image non-interlaced.");
                    }

                    if (compressionMethod != 0)
                    {
                        throw new InvalidDataException($"PNG compression method {compressionMethod} is not defined by the format (only 0/deflate is).");
                    }

                    if (filterMethod != 0)
                    {
                        throw new InvalidDataException($"PNG filter method {filterMethod} is not defined by the format (only 0 is).");
                    }

                    if (colorType == 3)
                    {
                        throw new NotSupportedException("Paletted PNG (colour type 3) is not supported — re-export as truecolour or greyscale.");
                    }

                    bytesPerPixel = colorType switch
                    {
                        0 => 1,
                        2 => 3,
                        4 => 2,
                        6 => 4,
                        _ => throw new NotSupportedException($"PNG colour type {colorType} is not supported (expected 0, 2, 4 or 6)."),
                    };

                    if (bitDepth != 8)
                    {
                        throw new NotSupportedException($"PNG bit depth {bitDepth} is not supported — only 8 is. Re-export the image at 8 bits per channel.");
                    }

                    // 64-bit arithmetic deliberately: the int-sized equivalents below would wrap.
                    // See MaxImageBytes. Both the decoded RGBA buffer and the intermediate filtered
                    // scanlines are bounded, since either one wrapping is exploitable.
                    var decodedBytes = (long)width * height * 4;
                    var scanlineBytes = (long)height * (((long)width * bytesPerPixel) + 1);
                    if (decodedBytes > MaxImageBytes || scanlineBytes > MaxImageBytes)
                    {
                        throw new InvalidDataException(
                            $"PNG dimensions {width}x{height} would decode to {decodedBytes} bytes, over this decoder's {MaxImageBytes}-byte limit.");
                    }

                    sawHeader = true;
                    break;

                case "IDAT":
                    if (!sawHeader)
                    {
                        throw new InvalidDataException("PNG IDAT chunk appeared before IHDR.");
                    }

                    compressed.Write(data);
                    break;

                case "IEND":
                    if (!sawHeader)
                    {
                        throw new InvalidDataException("PNG ended without an IHDR chunk.");
                    }

                    if (compressed.Length == 0)
                    {
                        throw new InvalidDataException("PNG ended without any IDAT (pixel) data.");
                    }

                    compressed.Position = 0;
                    var scanlines = Inflate(compressed, width, height, bytesPerPixel);
                    return new DecodedImage(width, height, ToRgba(scanlines, width, height, bytesPerPixel, colorType));

                default:
                    // Any other chunk (gAMA/pHYs/tEXt/sRGB/...) carries no information this
                    // decoder acts on. Skipping rather than rejecting is required, not lenient:
                    // essentially every real exported PNG carries several.
                    break;
            }
        }
    }

    private static void ReadSignature(Stream input)
    {
        Span<byte> signature = stackalloc byte[8];
        if (input.ReadAtLeast(signature, 8, throwOnEndOfStream: false) < 8 || !signature.SequenceEqual(Signature))
        {
            throw new InvalidDataException("Not a PNG: the 8-byte file signature is missing or does not match.");
        }
    }

    private static (string Type, byte[] Data) ReadChunk(Stream input)
    {
        Span<byte> header = stackalloc byte[8];
        if (input.ReadAtLeast(header, 8, throwOnEndOfStream: false) < 8)
        {
            throw new InvalidDataException("Truncated PNG: the stream ended mid-chunk-header (no IEND was reached).");
        }

        var length = BinaryPrimitives.ReadUInt32BigEndian(header[..4]);

        // Bounded BEFORE allocating: `length` comes from eight untrusted bytes, so accepting
        // anything up to ~2 GB here would let a malformed 8-byte header trigger a multi-gigabyte
        // allocation (an OutOfMemoryException, not the InvalidDataException this class promises).
        if (length > MaxImageBytes)
        {
            throw new InvalidDataException(
                $"PNG chunk declares {length} bytes, over this decoder's {MaxImageBytes}-byte limit — the file is malformed or not a PNG this decoder can read.");
        }

        var type = string.Create(4, header[4..8].ToArray(), static (span, bytes) =>
        {
            for (var i = 0; i < 4; i++)
            {
                span[i] = (char)bytes[i];
            }
        });

        var data = new byte[(int)length];
        if (input.ReadAtLeast(data, data.Length, throwOnEndOfStream: false) < data.Length)
        {
            throw new InvalidDataException($"Truncated PNG: chunk '{type}' declared {length} bytes of data but the stream ended early.");
        }

        // The trailing 4-byte CRC is skipped rather than verified: a mismatched CRC on an
        // otherwise-decodable image would only turn a usable asset into a hard failure, and the
        // decode itself already fails loudly on structural damage (the zlib stream carries its own
        // Adler32, which ZLibStream does check). The honest cost: bit-rot confined to already-
        // decompressed pixel bytes goes undetected, so this trades tamper-detection for robustness
        // — right for local, engine-authored assets, and the thing to revisit if PNGs ever arrive
        // from an untrusted source where silent corruption matters more than a usable image.
        Span<byte> crc = stackalloc byte[4];
        if (input.ReadAtLeast(crc, 4, throwOnEndOfStream: false) < 4)
        {
            throw new InvalidDataException($"Truncated PNG: chunk '{type}' is missing its CRC.");
        }

        return (type, data);
    }

    /// <summary>Inflates the concatenated IDAT payload into <c>height</c> filtered scanlines of
    /// <c>1 + width * bytesPerPixel</c> bytes each (the leading byte is the row's filter type).</summary>
    private static byte[] Inflate(Stream compressed, int width, int height, int bytesPerPixel)
    {
        var stride = width * bytesPerPixel;
        var expected = height * (stride + 1);
        var scanlines = new byte[expected];

        // ZLibStream, NOT DeflateStream: PNG's IDAT payload is zlib-framed (2-byte header +
        // deflate + Adler32 trailer), so DeflateStream would choke on the header bytes. A corrupt
        // stream surfaces as ZLibStream's own InvalidDataException, which is already the exception
        // this method's contract promises — no translation needed.
        using var zlib = new ZLibStream(compressed, CompressionMode.Decompress);
        var read = zlib.ReadAtLeast(scanlines, expected, throwOnEndOfStream: false);
        if (read < expected)
        {
            throw new InvalidDataException(
                $"PNG pixel data is short: expected {expected} bytes for {width}x{height} at {bytesPerPixel} bytes/pixel, got {read}.");
        }

        return scanlines;
    }

    /// <summary>Reverses the per-row filter and widens each pixel to RGBA. Filtering is defined on
    /// the RAW byte stream (not on pixels), and each predictor reads bytes that were themselves
    /// already unfiltered — so this must unfilter in place, row by row, left to right.</summary>
    private static byte[] ToRgba(byte[] scanlines, int width, int height, int bytesPerPixel, byte colorType)
    {
        var stride = width * bytesPerPixel;
        var rgba = new byte[width * height * 4];
        var current = new byte[stride];
        var previous = new byte[stride];

        for (var y = 0; y < height; y++)
        {
            var rowStart = y * (stride + 1);
            var filter = scanlines[rowStart];
            scanlines.AsSpan(rowStart + 1, stride).CopyTo(current);

            for (var x = 0; x < stride; x++)
            {
                // "Left"/"up-left" are the byte one PIXEL back, not one byte back, and are treated
                // as 0 for the first pixel of a row (PNG spec's convention for out-of-range).
                int left = x >= bytesPerPixel ? current[x - bytesPerPixel] : 0;
                int up = previous[x];
                int upLeft = x >= bytesPerPixel ? previous[x - bytesPerPixel] : 0;

                var predictor = filter switch
                {
                    0 => 0,
                    1 => left,
                    2 => up,
                    3 => (left + up) / 2,
                    4 => Paeth(left, up, upLeft),
                    _ => throw new InvalidDataException($"PNG row {y} declares filter type {filter}; only 0-4 are defined."),
                };

                // Wraps modulo 256 by design — PNG filtering is defined on bytes with wraparound
                // arithmetic, so the byte cast is the specified behaviour, not a truncation bug.
                current[x] = (byte)(current[x] + predictor);
            }

            WidenRow(current, rgba.AsSpan(y * width * 4, width * 4), width, colorType);

            // Swap rather than copy: this row becomes the next row's "previous", and the old
            // previous buffer is free to be overwritten.
            (previous, current) = (current, previous);
        }

        return rgba;
    }

    private static void WidenRow(ReadOnlySpan<byte> row, Span<byte> destination, int width, byte colorType)
    {
        for (var x = 0; x < width; x++)
        {
            var dst = x * 4;
            switch (colorType)
            {
                case 0: // greyscale: replicate luminance into RGB, fully opaque.
                    destination[dst] = destination[dst + 1] = destination[dst + 2] = row[x];
                    destination[dst + 3] = 255;
                    break;

                case 4: // greyscale + alpha: alpha is PRESERVED, not forced opaque.
                    destination[dst] = destination[dst + 1] = destination[dst + 2] = row[x * 2];
                    destination[dst + 3] = row[(x * 2) + 1];
                    break;

                case 2: // truecolour, no alpha channel in the source.
                    destination[dst] = row[x * 3];
                    destination[dst + 1] = row[(x * 3) + 1];
                    destination[dst + 2] = row[(x * 3) + 2];
                    destination[dst + 3] = 255;
                    break;

                case 6: // already RGBA.
                    row.Slice(x * 4, 4).CopyTo(destination.Slice(dst, 4));
                    break;

                default:
                    throw new NotSupportedException($"PNG colour type {colorType} is not supported.");
            }
        }
    }

    /// <summary>The PNG spec's Paeth predictor: pick whichever of left/up/up-left is closest to
    /// <c>left + up - upLeft</c>, with ties resolved left, then up (the tie order is normative —
    /// getting it wrong corrupts only some images, which is why it is tested explicitly).</summary>
    private static int Paeth(int left, int up, int upLeft)
    {
        var estimate = left + up - upLeft;
        var distanceLeft = Math.Abs(estimate - left);
        var distanceUp = Math.Abs(estimate - up);
        var distanceUpLeft = Math.Abs(estimate - upLeft);

        if (distanceLeft <= distanceUp && distanceLeft <= distanceUpLeft)
        {
            return left;
        }

        return distanceUp <= distanceUpLeft ? up : upLeft;
    }
}
