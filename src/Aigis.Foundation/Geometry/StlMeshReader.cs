using System.Globalization;
using System.Numerics;
using System.Text;

namespace Aigis.Foundation.Geometry;

/// <summary>
/// A single mesh triangle. Vertices are in whatever unit the source STL file used —
/// <see cref="StlMeshReader"/> performs no unit conversion. If the file is not already
/// in Aigis's canonical world unit (metres, SI), the caller must rescale before feeding
/// vertices into anything that assumes metres (e.g. <see cref="MeshVoxelizer"/> callers
/// composing a metre-based <c>WorldToVoxel</c>).
/// </summary>
public readonly record struct MeshTriangle(Vector3 V0, Vector3 V1, Vector3 V2);

/// <summary>
/// Dependency-free STL reader. Auto-detects binary vs ASCII. Binary layout is the
/// standard STL format (80-byte header + uint32 triangle count + 50 bytes/triangle).
/// Vertices are returned verbatim in their file units.
/// <para>
/// A binary file whose size doesn't exactly match the header/count/triangle-size
/// formula is retried as ASCII (the same recovery a truncated/corrupted binary STL
/// would trigger); if that also finds no recognizable <c>vertex</c>/<c>facet</c>
/// tokens, <see cref="Read"/> returns an empty list rather than throwing. Callers
/// that need to distinguish "empty mesh" from "unparseable file" should check
/// <c>Count == 0</c> themselves.
/// </para>
/// </summary>
public static class StlMeshReader
{
    private const int HeaderBytes = 80;
    private const int BytesPerTriangle = 50;

    /// <summary>Read all triangles from an STL file.</summary>
    public static IReadOnlyList<MeshTriangle> Read(Stream stream)
    {
        ArgumentNullException.ThrowIfNull(stream);

        // Buffer the whole file so we can run the binary size heuristic and, if
        // needed, fall back to an ASCII parse from the start.
        byte[] bytes;
        using (var ms = new MemoryStream())
        {
            stream.CopyTo(ms);
            bytes = ms.ToArray();
        }

        if (LooksBinary(bytes))
        {
            return ReadBinary(bytes);
        }

        return ReadAscii(bytes);
    }

    private static bool LooksBinary(byte[] bytes)
    {
        if (bytes.Length < HeaderBytes + 4)
        {
            return false;
        }

        uint count = BitConverter.ToUInt32(bytes, HeaderBytes);
        long expected = (long)HeaderBytes + 4 + (long)count * BytesPerTriangle;

        // Exact size match is the canonical binary-STL detector. (An ASCII file
        // can begin with "solid" too, so the byte count is the reliable signal.)
        return expected == bytes.Length;
    }

    private static List<MeshTriangle> ReadBinary(byte[] bytes)
    {
        uint count = BitConverter.ToUInt32(bytes, HeaderBytes);
        var tris = new List<MeshTriangle>((int)Math.Min(count, 1_000_000));
        int off = HeaderBytes + 4;
        for (uint t = 0; t < count; t++)
        {
            // Skip the 3-float normal; recompute geometry from vertices only.
            int v = off + 12;
            var v0 = new Vector3(F(bytes, v), F(bytes, v + 4), F(bytes, v + 8));
            var v1 = new Vector3(F(bytes, v + 12), F(bytes, v + 16), F(bytes, v + 20));
            var v2 = new Vector3(F(bytes, v + 24), F(bytes, v + 28), F(bytes, v + 32));
            tris.Add(new MeshTriangle(v0, v1, v2));
            off += BytesPerTriangle;
        }

        return tris;

        static float F(byte[] b, int i) => BitConverter.ToSingle(b, i);
    }

    private static List<MeshTriangle> ReadAscii(byte[] bytes)
    {
        var text = Encoding.ASCII.GetString(bytes);
        var tris = new List<MeshTriangle>();
        var verts = new List<Vector3>(3);

        foreach (var rawLine in text.Split('\n'))
        {
            var line = rawLine.Trim();
            if (line.Length == 0)
            {
                continue;
            }

            // Tokens: "vertex x y z"
            if (line.StartsWith("vertex", StringComparison.OrdinalIgnoreCase))
            {
                var parts = line.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries);
                if (parts.Length < 4)
                {
                    continue;
                }

                verts.Add(new Vector3(P(parts[1]), P(parts[2]), P(parts[3])));
                if (verts.Count == 3)
                {
                    tris.Add(new MeshTriangle(verts[0], verts[1], verts[2]));
                    verts.Clear();
                }
            }
            else if (line.StartsWith("facet", StringComparison.OrdinalIgnoreCase) ||
                     line.StartsWith("outer", StringComparison.OrdinalIgnoreCase))
            {
                verts.Clear(); // start of a new facet loop
            }
        }

        return tris;

        static float P(string s) => float.TryParse(s, NumberStyles.Float, CultureInfo.InvariantCulture, out var f)
            ? f
            : throw new FormatException($"STL ASCII vertex coordinate '{s}' is not a valid number.");
    }
}
