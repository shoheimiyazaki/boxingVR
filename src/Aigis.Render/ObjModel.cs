using System.Globalization;
using System.Numerics;

namespace Aigis.Render;

/// <summary>One material's triangles of an <see cref="ObjModel"/>: the mesh and the material's diffuse colour (MTL <c>Kd</c>).</summary>
public sealed record ObjPart(string Material, MeshData Mesh, Vector4 Color);

/// <summary>
/// A Wavefront OBJ model split by material (2026-11-11, for flat-coloured furniture kits such as Kenney's): every
/// <c>usemtl</c> becomes one <see cref="ObjPart"/> whose colour is that material's <c>Kd</c> from the MTL. Faces with more
/// than three corners are fanned into triangles. Each corner takes the file's <c>vn</c> when it names one, and the face's own
/// flat normal otherwise. Texture coordinates are read past (the engine maps no UVs).
/// </summary>
public sealed class ObjModel
{
    private ObjModel(IReadOnlyList<ObjPart> parts) => Parts = parts;

    public IReadOnlyList<ObjPart> Parts { get; }

    /// <summary>The bounds of every part together, in the file's own units.</summary>
    public (Vector3 Min, Vector3 Max) Bounds
    {
        get
        {
            var min = new Vector3(float.MaxValue);
            var max = new Vector3(float.MinValue);
            foreach (var part in Parts)
            {
                min = Vector3.Min(min, part.Mesh.Bounds.Min);
                max = Vector3.Max(max, part.Mesh.Bounds.Max);
            }

            return Parts.Count == 0 ? (Vector3.Zero, Vector3.Zero) : (min, max);
        }
    }

    /// <summary>Parses an OBJ and (optionally) its MTL. A material the MTL does not define is light grey.</summary>
    /// <exception cref="InvalidDataException">A malformed number, a face index out of range, or a face with fewer than three corners.</exception>
    public static ObjModel Parse(TextReader obj, TextReader? mtl, string name = "obj")
    {
        ArgumentNullException.ThrowIfNull(obj);
        var colours = mtl is null ? new Dictionary<string, Vector4>() : ParseMaterials(mtl);
        var positions = new List<Vector3>();
        var normals = new List<Vector3>();
        var byMaterial = new Dictionary<string, List<float>>(StringComparer.Ordinal);
        var order = new List<string>();
        var current = "default";
        var lineNumber = 0;
        string? line;
        while ((line = obj.ReadLine()) is not null)
        {
            lineNumber++;
            var tokens = line.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries);
            if (tokens.Length == 0 || tokens[0].StartsWith('#'))
            {
                continue;
            }

            switch (tokens[0])
            {
                case "v":
                    positions.Add(Vector(tokens, lineNumber, name));
                    break;
                case "vn":
                    normals.Add(Vector(tokens, lineNumber, name));
                    break;
                case "usemtl":
                    current = tokens.Length > 1 ? tokens[1] : "default";
                    break;
                case "f":
                    if (!byMaterial.TryGetValue(current, out var vertices))
                    {
                        vertices = [];
                        byMaterial[current] = vertices;
                        order.Add(current);
                    }

                    AddFace(tokens, positions, normals, vertices, lineNumber, name);
                    break;
            }
        }

        var parts = order
            .Where(m => byMaterial[m].Count > 0)
            .Select(m => new ObjPart(m, new MeshData([.. byMaterial[m]], $"{name}:{m}"), colours.GetValueOrDefault(m, new Vector4(0.8f, 0.8f, 0.8f, 1f))))
            .ToList();
        return new ObjModel(parts);
    }

    private static void AddFace(string[] tokens, List<Vector3> positions, List<Vector3> normals, List<float> into, int lineNumber, string name)
    {
        var corners = new (Vector3 Position, Vector3? Normal)[tokens.Length - 1];
        if (corners.Length < 3)
        {
            throw new InvalidDataException($"{name}:{lineNumber}: a face needs at least three corners.");
        }

        for (var i = 0; i < corners.Length; i++)
        {
            var refs = tokens[i + 1].Split('/');
            var p = Index(refs[0], positions.Count, lineNumber, name);
            Vector3? n = refs.Length > 2 && refs[2].Length > 0 ? normals[Index(refs[2], normals.Count, lineNumber, name)] : null;
            corners[i] = (positions[p], n);
        }

        for (var i = 1; i + 1 < corners.Length; i++)
        {
            var (a, b, c) = (corners[0], corners[i], corners[i + 1]);
            var flat = Vector3.Cross(b.Position - a.Position, c.Position - a.Position);
            flat = flat.LengthSquared() > 1e-20f ? Vector3.Normalize(flat) : Vector3.UnitY;
            foreach (var (position, normal) in new[] { a, b, c })
            {
                var n = normal is { } given && given.LengthSquared() > 1e-12f ? Vector3.Normalize(given) : flat;
                into.Add(position.X);
                into.Add(position.Y);
                into.Add(position.Z);
                into.Add(n.X);
                into.Add(n.Y);
                into.Add(n.Z);
            }
        }
    }

    private static int Index(string text, int count, int lineNumber, string name)
    {
        if (!int.TryParse(text, NumberStyles.Integer, CultureInfo.InvariantCulture, out var index))
        {
            throw new InvalidDataException($"{name}:{lineNumber}: '{text}' is not an index.");
        }

        var zeroBased = index > 0 ? index - 1 : count + index; // 負の番号は末尾から
        if (zeroBased < 0 || zeroBased >= count)
        {
            throw new InvalidDataException($"{name}:{lineNumber}: index {index} is out of range (1..{count}).");
        }

        return zeroBased;
    }

    private static Vector3 Vector(string[] tokens, int lineNumber, string name)
    {
        if (tokens.Length < 4)
        {
            throw new InvalidDataException($"{name}:{lineNumber}: '{tokens[0]}' needs three numbers.");
        }

        return new Vector3(Number(tokens[1], lineNumber, name), Number(tokens[2], lineNumber, name), Number(tokens[3], lineNumber, name));
    }

    private static float Number(string text, int lineNumber, string name)
        => float.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out var value) && float.IsFinite(value)
            ? value
            : throw new InvalidDataException($"{name}:{lineNumber}: '{text}' is not a number.");

    private static Dictionary<string, Vector4> ParseMaterials(TextReader mtl)
    {
        var colours = new Dictionary<string, Vector4>(StringComparer.Ordinal);
        string? current = null;
        var lineNumber = 0;
        string? line;
        while ((line = mtl.ReadLine()) is not null)
        {
            lineNumber++;
            var tokens = line.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries);
            if (tokens.Length == 0)
            {
                continue;
            }

            if (tokens[0] == "newmtl" && tokens.Length > 1)
            {
                current = tokens[1];
                colours[current] = new Vector4(0.8f, 0.8f, 0.8f, 1f);
            }
            else if (tokens[0] == "Kd" && current is not null)
            {
                var kd = Vector(tokens, lineNumber, "mtl");
                colours[current] = new Vector4(Vector3.Clamp(kd, Vector3.Zero, Vector3.One), 1f);
            }
        }

        return colours;
    }
}
