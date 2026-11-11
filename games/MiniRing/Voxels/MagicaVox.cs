using System.Text;

namespace MiniRing.Voxels;

/// <summary>
/// .vox の 1 つのモデル（MagicaVoxel の座標: Z が上）。<see cref="Cells"/> は x + SizeX × (y + SizeY × z) の順の色番号（0 = 空、1..255）。
/// <see cref="Rotated"/>: シーンの上で回転・反転されている（オブジェクトモードで回した。ボクセルの並びには表れない）。
/// </summary>
public sealed record VoxModel(string Name, (int X, int Y, int Z) Size, byte[] Cells, bool Rotated = false)
{
    public byte At(int x, int y, int z) => Cells[x + (Size.X * (y + (Size.Y * z)))];
}

/// <summary>.vox のシーン: 名前付きのモデルと、256 色のパレット（<c>Palette[i]</c> が色番号 i の RGBA。0 は使わない）。</summary>
public sealed record VoxScene(IReadOnlyList<VoxModel> Models, uint[] Palette);

/// <summary>
/// MagicaVoxel の .vox（版 200）の読み書き。Vengi・MagicaVoxel・Goxel などのボクセル編集ソフトとやり取りする。
/// 書くときは各モデルを名前付きのノード（nTRN の <c>_name</c>）にして横に並べる。読むときは、知らないチャンク（材質・カメラ・レイヤーなど）は読み飛ばし、
/// モデルの名前は nTRN の <c>_name</c>（無ければ nSHP の <c>_name</c>、それも無ければ <c>model{番号}</c>）から取る。
/// 仕様: https://github.com/ephtracy/voxel-model/blob/master/MagicaVoxel-file-format-vox.txt
/// </summary>
public static class MagicaVox
{
    /// <summary>1 モデルの 1 辺の上限（.vox の座標は 1 バイト）。</summary>
    public const int MaxSize = 256;

    private const int Version = 200;

    /// <summary>モデルを並べるときの隙間（ボクセル）。</summary>
    private const int Gap = 4;

    public static void Write(Stream stream, VoxScene scene)
    {
        ArgumentNullException.ThrowIfNull(stream);
        ArgumentNullException.ThrowIfNull(scene);
        if (scene.Palette.Length != 256)
        {
            throw new ArgumentException($"The palette needs 256 entries, not {scene.Palette.Length}.", nameof(scene));
        }

        var children = new MemoryStream();
        var w = new BinaryWriter(children, Encoding.UTF8);
        foreach (var model in scene.Models)
        {
            Validate(model);
            Chunk(w, "SIZE", b =>
            {
                b.Write(model.Size.X);
                b.Write(model.Size.Y);
                b.Write(model.Size.Z);
            });
            Chunk(w, "XYZI", b =>
            {
                var filled = model.Cells.Count(c => c != 0);
                b.Write(filled);
                for (var z = 0; z < model.Size.Z; z++)
                {
                    for (var y = 0; y < model.Size.Y; y++)
                    {
                        for (var x = 0; x < model.Size.X; x++)
                        {
                            var c = model.At(x, y, z);
                            if (c != 0)
                            {
                                b.Write((byte)x);
                                b.Write((byte)y);
                                b.Write((byte)z);
                                b.Write(c);
                            }
                        }
                    }
                }
            });
        }

        // シーン: 根の変換(0) → グループ(1) → モデルごとに 変換(2 + 2i) → 形(3 + 2i)。モデルは X に沿って隙間を空けて並べる。
        Chunk(w, "nTRN", b => Transform(b, 0, [], 1, "0 0 0"));
        Chunk(w, "nGRP", b =>
        {
            b.Write(1);
            Dict(b, []);
            b.Write(scene.Models.Count);
            for (var i = 0; i < scene.Models.Count; i++)
            {
                b.Write(2 + (2 * i));
            }
        });
        var x0 = 0;
        for (var i = 0; i < scene.Models.Count; i++)
        {
            var model = scene.Models[i];
            var at = $"{x0 + (model.Size.X / 2)} 0 {model.Size.Z / 2}";
            x0 += model.Size.X + Gap;
            var index = i;
            Chunk(w, "nTRN", b => Transform(b, 2 + (2 * index), [("_name", model.Name)], 3 + (2 * index), at));
            Chunk(w, "nSHP", b =>
            {
                b.Write(3 + (2 * index));
                Dict(b, []);
                b.Write(1);
                b.Write(index);
                Dict(b, []);
            });
        }

        Chunk(w, "LAYR", b =>
        {
            b.Write(0);
            Dict(b, []);
            b.Write(-1);
        });
        Chunk(w, "RGBA", b =>
        {
            // RGBA の i 番目が色番号 i + 1（.vox の決まり）。最後の 1 つは使わない。
            for (var i = 1; i <= 256; i++)
            {
                b.Write(scene.Palette[i % 256]);
            }
        });
        w.Flush();

        var output = new BinaryWriter(stream, Encoding.UTF8, leaveOpen: true);
        output.Write("VOX "u8);
        output.Write(Version);
        output.Write("MAIN"u8);
        output.Write(0);
        output.Write((int)children.Length);
        output.Write(children.ToArray());
        output.Flush();
    }

    public static VoxScene Read(Stream stream)
    {
        ArgumentNullException.ThrowIfNull(stream);
        try
        {
            return ReadScene(stream);
        }
        catch (Exception e) when (e is EndOfStreamException or ArgumentOutOfRangeException or OverflowException)
        {
            throw new InvalidDataException($"The .vox file is truncated or corrupt ({e.Message}).", e);
        }
    }

    private static VoxScene ReadScene(Stream stream)
    {
        using var r = new BinaryReader(stream, Encoding.UTF8, leaveOpen: true);
        if (Encoding.ASCII.GetString(r.ReadBytes(4)) != "VOX ")
        {
            throw new InvalidDataException("Not a .vox file (no 'VOX ' header).");
        }

        r.ReadInt32(); // 版
        if (Encoding.ASCII.GetString(r.ReadBytes(4)) != "MAIN")
        {
            throw new InvalidDataException("A .vox file must start with a MAIN chunk.");
        }

        var mainContent = r.ReadInt32();
        var mainChildren = r.ReadInt32();
        r.ReadBytes(mainContent);
        var end = stream.Position + mainChildren;

        var sizes = new List<(int X, int Y, int Z)>();
        var cells = new List<byte[]>();
        var palette = DefaultPalette();
        var transforms = new Dictionary<int, (string? Name, int Child, bool Rotated)>();
        var shapes = new Dictionary<int, (string? Name, int Model)>();
        while (stream.Position < end)
        {
            var id = Encoding.ASCII.GetString(r.ReadBytes(4));
            var content = r.ReadInt32();
            var childBytes = r.ReadInt32();
            var body = new BinaryReader(new MemoryStream(r.ReadBytes(content)), Encoding.UTF8);
            r.ReadBytes(childBytes);
            switch (id)
            {
                case "SIZE":
                    var size = (body.ReadInt32(), body.ReadInt32(), body.ReadInt32());
                    if (size.Item1 is < 1 or > MaxSize || size.Item2 is < 1 or > MaxSize || size.Item3 is < 1 or > MaxSize)
                    {
                        throw new InvalidDataException($"Model size {size} is outside 1..{MaxSize}.");
                    }

                    sizes.Add(size);
                    break;
                case "XYZI":
                    if (sizes.Count != cells.Count + 1)
                    {
                        throw new InvalidDataException("XYZI chunk without a SIZE chunk before it.");
                    }

                    var s = sizes[^1];
                    var grid = new byte[s.X * s.Y * s.Z];
                    var count = body.ReadInt32();
                    for (var i = 0; i < count; i++)
                    {
                        int x = body.ReadByte(), y = body.ReadByte(), z = body.ReadByte();
                        var c = body.ReadByte();
                        if (x >= s.X || y >= s.Y || z >= s.Z)
                        {
                            throw new InvalidDataException($"Voxel ({x}, {y}, {z}) is outside its model of size {s}.");
                        }

                        grid[x + (s.X * (y + (s.Y * z)))] = c;
                    }

                    cells.Add(grid);
                    break;
                case "RGBA":
                    for (var i = 1; i <= 256; i++)
                    {
                        palette[i % 256] = body.ReadUInt32();
                    }

                    break;
                case "nTRN":
                    var node = body.ReadInt32();
                    var attributes = ReadDict(body);
                    var child = body.ReadInt32();
                    body.ReadInt32(); // 予約
                    body.ReadInt32(); // レイヤー
                    var rotated = false;
                    for (int f = 0, frames = body.ReadInt32(); f < frames; f++)
                    {
                        // _r は回転の行列を 1 バイトにしたもの。4 が回転なし。
                        rotated |= ReadDict(body).TryGetValue("_r", out var rotation) && rotation.Trim() != "4";
                    }

                    transforms[node] = (attributes.GetValueOrDefault("_name"), child, rotated);
                    break;
                case "nSHP":
                    var shape = body.ReadInt32();
                    var shapeAttributes = ReadDict(body);
                    if (body.ReadInt32() >= 1)
                    {
                        shapes[shape] = (shapeAttributes.GetValueOrDefault("_name"), body.ReadInt32());
                    }

                    break;
            }
        }

        if (sizes.Count != cells.Count)
        {
            throw new InvalidDataException("A SIZE chunk has no XYZI chunk after it.");
        }

        var names = new string?[cells.Count];
        var turned = new bool[cells.Count];
        foreach (var (shapeId, shape) in shapes)
        {
            if (shape.Model >= 0 && shape.Model < names.Length)
            {
                var parent = transforms.Values.FirstOrDefault(t => t.Child == shapeId);
                names[shape.Model] = parent.Name ?? shape.Name ?? names[shape.Model];
                turned[shape.Model] |= parent.Rotated;
            }
        }

        var models = cells.Select((c, i) => new VoxModel(names[i] ?? $"model{i}", sizes[i], c, turned[i])).ToList();
        return new VoxScene(models, palette);
    }

    /// <summary>RGBA を 1 つの値に（.vox のパレットの並び: 下位バイトから R, G, B, A）。</summary>
    public static uint Rgba(byte r, byte g, byte b, byte a = 255) => r | ((uint)g << 8) | ((uint)b << 16) | ((uint)a << 24);

    private static void Validate(VoxModel model)
    {
        var (x, y, z) = model.Size;
        if (x is < 1 or > MaxSize || y is < 1 or > MaxSize || z is < 1 or > MaxSize)
        {
            throw new ArgumentException($"Model '{model.Name}' size {model.Size} is outside 1..{MaxSize}.", nameof(model));
        }

        if (model.Cells.Length != x * y * z)
        {
            throw new ArgumentException($"Model '{model.Name}' has {model.Cells.Length} cells, not {x * y * z}.", nameof(model));
        }
    }

    private static void Chunk(BinaryWriter w, string id, Action<BinaryWriter> content)
    {
        var buffer = new MemoryStream();
        using (var b = new BinaryWriter(buffer, Encoding.UTF8, leaveOpen: true))
        {
            content(b);
        }

        w.Write(Encoding.ASCII.GetBytes(id));
        w.Write((int)buffer.Length);
        w.Write(0);
        w.Write(buffer.ToArray());
    }

    private static void Transform(BinaryWriter b, int node, (string Key, string Value)[] attributes, int child, string translation)
    {
        b.Write(node);
        Dict(b, attributes);
        b.Write(child);
        b.Write(-1); // 予約
        b.Write(0); // レイヤー
        b.Write(1); // フレーム数
        Dict(b, [("_t", translation)]);
    }

    private static void Dict(BinaryWriter b, (string Key, string Value)[] entries)
    {
        b.Write(entries.Length);
        foreach (var (key, value) in entries)
        {
            Text(b, key);
            Text(b, value);
        }
    }

    private static void Text(BinaryWriter b, string text)
    {
        var bytes = Encoding.UTF8.GetBytes(text);
        b.Write(bytes.Length);
        b.Write(bytes);
    }

    private static Dictionary<string, string> ReadDict(BinaryReader b)
    {
        var count = b.ReadInt32();
        var dict = new Dictionary<string, string>(StringComparer.Ordinal);
        for (var i = 0; i < count; i++)
        {
            var key = Encoding.UTF8.GetString(b.ReadBytes(b.ReadInt32()));
            dict[key] = Encoding.UTF8.GetString(b.ReadBytes(b.ReadInt32()));
        }

        return dict;
    }

    /// <summary>RGBA チャンクが無いファイルの色（ここでは灰色。ボクサーの材質のどれとも一致しない）。</summary>
    private static uint[] DefaultPalette() => Enumerable.Repeat(Rgba(128, 128, 128), 256).ToArray();
}
