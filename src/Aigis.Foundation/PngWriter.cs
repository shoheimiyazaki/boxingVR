using System.Buffers.Binary;
using System.IO.Compression;

namespace Aigis.Foundation;

/// <summary>
/// Minimal, dependency-free PNG encoder for 8-bit RGBA — so the engine can emit agent
/// screenshots and golden artifacts without a third-party image library (keeps the
/// stack pure-managed and license-clean). Uses per-row filter 0 + zlib(DEFLATE). Lives in
/// Aigis.Foundation (not Aigis.Render) because it has zero Vulkan/rendering dependency —
/// both Aigis.Render (screenshots) and Aigis.Harness (chart PNGs) consume it from here
/// without either pulling in the other's native-library footprint.
/// </summary>
public static class PngWriter
{
    private static readonly byte[] Signature = [137, 80, 78, 71, 13, 10, 26, 10];

    public static void WriteRgba(string path, int width, int height, ReadOnlySpan<byte> rgba)
    {
        using var fs = File.Create(path);
        WriteRgba(fs, width, height, rgba);
    }

    public static void WriteRgba(Stream output, int width, int height, ReadOnlySpan<byte> rgba)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(width);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(height);
        if (rgba.Length < width * height * 4)
        {
            throw new ArgumentException("pixel buffer too small for the given dimensions", nameof(rgba));
        }

        output.Write(Signature);

        Span<byte> ihdr = stackalloc byte[13];
        BinaryPrimitives.WriteInt32BigEndian(ihdr[0..], width);
        BinaryPrimitives.WriteInt32BigEndian(ihdr[4..], height);
        ihdr[8] = 8;  // bit depth
        ihdr[9] = 6;  // color type: truecolor + alpha
        ihdr[10] = 0; // deflate
        ihdr[11] = 0; // no filter
        ihdr[12] = 0; // no interlace
        WriteChunk(output, "IHDR", ihdr);

        var filtered = BuildFilteredScanlines(width, height, rgba);
        WriteChunk(output, "IDAT", ZlibCompress(filtered));
        WriteChunk(output, "IEND", ReadOnlySpan<byte>.Empty);
    }

    private static byte[] BuildFilteredScanlines(int width, int height, ReadOnlySpan<byte> rgba)
    {
        var stride = width * 4;
        var result = new byte[height * (stride + 1)];
        for (var y = 0; y < height; y++)
        {
            var dst = y * (stride + 1);
            result[dst] = 0; // filter type: none
            rgba.Slice(y * stride, stride).CopyTo(result.AsSpan(dst + 1, stride));
        }

        return result;
    }

    private static byte[] ZlibCompress(byte[] raw)
    {
        using var ms = new MemoryStream();
        ms.WriteByte(0x78); // zlib CMF
        ms.WriteByte(0x9C); // zlib FLG (default compression)
        using (var deflate = new DeflateStream(ms, CompressionLevel.Optimal, leaveOpen: true))
        {
            deflate.Write(raw, 0, raw.Length);
        }

        Span<byte> adler = stackalloc byte[4];
        BinaryPrimitives.WriteUInt32BigEndian(adler, Adler32(raw));
        ms.Write(adler);
        return ms.ToArray();
    }

    private static void WriteChunk(Stream output, string type, ReadOnlySpan<byte> data)
    {
        Span<byte> length = stackalloc byte[4];
        BinaryPrimitives.WriteInt32BigEndian(length, data.Length);
        output.Write(length);

        Span<byte> typeBytes = stackalloc byte[4];
        for (var i = 0; i < 4; i++)
        {
            typeBytes[i] = (byte)type[i];
        }

        output.Write(typeBytes);
        output.Write(data);

        var crc = Crc32(typeBytes, data);
        Span<byte> crcBytes = stackalloc byte[4];
        BinaryPrimitives.WriteUInt32BigEndian(crcBytes, crc);
        output.Write(crcBytes);
    }

    private static uint Adler32(ReadOnlySpan<byte> data)
    {
        const uint Mod = 65521;
        uint a = 1, b = 0;
        foreach (var t in data)
        {
            a = (a + t) % Mod;
            b = (b + a) % Mod;
        }

        return (b << 16) | a;
    }

    private static uint Crc32(ReadOnlySpan<byte> type, ReadOnlySpan<byte> data)
    {
        var crc = 0xFFFFFFFFu;
        crc = Crc32Update(crc, type);
        crc = Crc32Update(crc, data);
        return crc ^ 0xFFFFFFFFu;
    }

    private static uint Crc32Update(uint crc, ReadOnlySpan<byte> data)
    {
        foreach (var t in data)
        {
            crc ^= t;
            for (var k = 0; k < 8; k++)
            {
                crc = (crc & 1) != 0 ? (crc >> 1) ^ 0xEDB88320u : crc >> 1;
            }
        }

        return crc;
    }
}
