using System.Diagnostics;
using Aigis.Foundation.Voxels;
using MiniRing.Weapons;

namespace MiniRing.Voxels;

/// <summary>
/// 武器・はめる物のボクセルの中身を、ボクセル編集ソフト（Vengi・MagicaVoxel）で直すための .vox との行き来（ボクサーの <see cref="BoxerVoxelModel"/> の武器版）。
/// <list type="bullet">
/// <item>対象は <see cref="Editable"/>（<see cref="WeaponKind.Fist"/> を除く）。1 種類 1 モデル（名前は <see cref="WeaponKind"/>）。色番号 = 材質番号。
/// 拳（Fist）を外す理由: <see cref="Weapon"/> は拳を <see cref="BareHandState"/> に置き換え、BareHandState は生成した拳の幾何から指・節を振り分ける。
/// 直した格子ではその振り分けが合わなくなる。</item>
/// <item>.vox のモデルは各軸 256 セルまで。超える軸は 256 以下の塊に均等に分け、モデル名を <c>IronSword#0</c>, <c>IronSword#1</c> … にして、
/// nTRN の位置でエディタ上は 1 本につながって見える。読み込みは同じ種類の塊を番号順に 1 本へ戻す（欠け・重複・隙間・重なりはエラー）。</item>
/// <item>革（<see cref="BoxerAnatomy.RedGlove"/>）: 赤で書き出す。読み込みでは青（<see cref="BoxerAnatomy.BlueGlove"/>）も赤に戻し、
/// <see cref="Weapon.Build(WeaponKind, byte, WeaponVoxelOverrides?)"/> が持ち主のコーナーの革に置き換える。</item>
/// </list>
/// 座標: 武器のローカルは +X が切っ先・Y が刃の幅・Z が厚さ。.vox は Z が上なので体と同じ (x, y, z) → (x, Dz − 1 − z, y)（<see cref="BoxerVoxelModel"/>）。
/// 長さの X はそのまま。
/// </summary>
public static class WeaponVoxelModel
{
    /// <summary>ゲームに組み込んだ直した中身のリソース名（games/MiniRing/Voxels/weapons.vox。無ければ生成のまま）。</summary>
    public const string ResourceName = "MiniRing.Voxels.weapons.vox";

    /// <summary>.vox で直せる種類（<see cref="WeaponKind.Fist"/> を除く全部）。</summary>
    public static IReadOnlyList<WeaponKind> Editable { get; } =
        [WeaponKind.IronSword, WeaponKind.WoodenClub, WeaponKind.Katana, WeaponKind.Longsword, WeaponKind.Staff, WeaponKind.Glove, WeaponKind.KnuckleDuster, WeaponKind.Katar, WeaponKind.Gauntlet];

    /// <summary>種類ごとの格子を .vox のシーンにする。256 を超える軸は塊に分ける。</summary>
    public static VoxScene ToVox(IReadOnlyDictionary<WeaponKind, VoxelGrid> weapons)
    {
        ArgumentNullException.ThrowIfNull(weapons);
        var models = new List<VoxModel>();
        foreach (var (kind, grid) in weapons.OrderBy(w => w.Key))
        {
            if (!Editable.Contains(kind))
            {
                throw new ArgumentException($"{kind} cannot be exported (only {string.Join(", ", Editable)}).", nameof(weapons));
            }

            var (dx, dy, dz) = grid.Dimensions;
            var size = (X: dx, Y: dz, Z: dy);
            var cells = new byte[dx * dy * dz];
            for (var z = 0; z < dz; z++)
            {
                for (var y = 0; y < dy; y++)
                {
                    for (var x = 0; x < dx; x++)
                    {
                        var (vx, vy, vz) = BoxerVoxelModel.ToVoxAxes(x, y, z, grid.Dimensions);
                        cells[vx + (size.X * (vy + (size.Y * vz)))] = grid.At(x, y, z);
                    }
                }
            }

            models.AddRange(Split(kind.ToString(), size, cells));
        }

        return new VoxScene(models, BoxerVoxelModel.MaterialPalette());
    }

    /// <summary>1 つの大きな格子を、各軸 <see cref="MagicaVox.MaxSize"/> 以下の塊（x が最も速い順に #0, #1, …）に均等に分ける。収まるなら分けず名前のまま。</summary>
    private static IEnumerable<VoxModel> Split(string name, (int X, int Y, int Z) size, byte[] cells)
    {
        var nx = Pieces(size.X);
        var ny = Pieces(size.Y);
        var nz = Pieces(size.Z);
        if (nx * ny * nz == 1)
        {
            yield return new VoxModel(name, size, cells);
            yield break;
        }

        var index = 0;
        for (var cz = 0; cz < nz; cz++)
        {
            for (var cy = 0; cy < ny; cy++)
            {
                for (var cx = 0; cx < nx; cx++)
                {
                    var (ox, sx) = Range(size.X, nx, cx);
                    var (oy, sy) = Range(size.Y, ny, cy);
                    var (oz, sz) = Range(size.Z, nz, cz);
                    var piece = new byte[sx * sy * sz];
                    for (var z = 0; z < sz; z++)
                    {
                        for (var y = 0; y < sy; y++)
                        {
                            Array.Copy(cells, ox + (size.X * (oy + y + (size.Y * (oz + z)))), piece, sx * (y + (sy * z)), sx);
                        }
                    }

                    yield return new VoxModel($"{name}#{index++}", (sx, sy, sz), piece, Position: (ox, oy, oz));
                }
            }
        }
    }

    private static int Pieces(int length) => (length + MagicaVox.MaxSize - 1) / MagicaVox.MaxSize;

    /// <summary>長さ <paramref name="length"/> を <paramref name="pieces"/> 個に均等に分けたときの、<paramref name="i"/> 番目の（始まり, 長さ）。</summary>
    private static (int Start, int Length) Range(int length, int pieces, int i)
    {
        var start = (int)((long)length * i / pieces);
        var end = (int)((long)length * (i + 1) / pieces);
        return (start, end - start);
    }

    /// <summary>
    /// .vox のシーンを種類ごとの材質の並びにする。名前が <see cref="WeaponKind"/>（または <c>種類#番号</c>）でないもの・拳・重複や欠けのある塊・
    /// 材質の色でない色があるものはエラー（どこが悪いかを全部並べる）。
    /// </summary>
    public static WeaponVoxelOverrides FromVox(VoxScene scene)
    {
        ArgumentNullException.ThrowIfNull(scene);
        var byColor = BoxerVoxelModel.MaterialByColor();
        var problems = new List<string>();

        // 種類ごとに塊を集める（塊が 1 つだけなら番号なし）。
        var pieces = new Dictionary<WeaponKind, SortedDictionary<int, VoxModel>>();
        var whole = new Dictionary<WeaponKind, VoxModel>();
        foreach (var model in scene.Models)
        {
            var name = model.Name;
            var index = -1;
            var hash = name.LastIndexOf('#');
            if (hash > 0 && int.TryParse(name[(hash + 1)..], System.Globalization.NumberStyles.None, System.Globalization.CultureInfo.InvariantCulture, out var parsed) && parsed >= 0 && name[(hash + 1)..] == parsed.ToString(System.Globalization.CultureInfo.InvariantCulture))
            {
                index = parsed;
                name = name[..hash];
            }

            if (name == nameof(WeaponKind.Fist))
            {
                problems.Add($"model '{model.Name}': Fist cannot be edited (the bare hand is built from the generated fist by BareHandState, which assigns fingers and knuckles from its geometry; " +
                    "an edited grid would no longer match)");
                continue;
            }

            if (!Editable.Select(k => k.ToString()).Contains(name, StringComparer.Ordinal))
            {
                problems.Add($"model '{model.Name}' is not a weapon ({string.Join(", ", Editable)})");
                continue;
            }

            var kind = Enum.Parse<WeaponKind>(name);
            if (model.Rotated)
            {
                problems.Add($"'{model.Name}' is rotated or mirrored in the scene (object mode); undo the rotation, the game reads the voxels as they are stored");
                continue;
            }

            if (index < 0)
            {
                if (!whole.TryAdd(kind, model))
                {
                    problems.Add($"weapon '{kind}' appears twice");
                }
            }
            else
            {
                if (!pieces.TryGetValue(kind, out var set))
                {
                    pieces[kind] = set = [];
                }

                if (!set.TryAdd(index, model))
                {
                    problems.Add($"chunk '{model.Name}' appears twice");
                }
            }
        }

        var weapons = new Dictionary<WeaponKind, WeaponVoxelOverrides.Cells>();
        foreach (var kind in whole.Keys.Union(pieces.Keys).OrderBy(k => k))
        {
            VoxModel merged;
            if (whole.TryGetValue(kind, out var single))
            {
                if (pieces.ContainsKey(kind))
                {
                    problems.Add($"'{kind}' is given both whole and as chunks ({kind}#0, …)");
                    continue;
                }

                merged = single;
            }
            else
            {
                var error = Merge(kind, pieces[kind], out merged);
                if (error is not null)
                {
                    problems.Add(error);
                    continue;
                }
            }

            var vox = merged.Size;
            var dims = (X: vox.X, Y: vox.Z, Z: vox.Y);
            var materials = new byte[dims.X * dims.Y * dims.Z];
            var unknown = new Dictionary<uint, int>();
            for (var z = 0; z < dims.Z; z++)
            {
                for (var y = 0; y < dims.Y; y++)
                {
                    for (var x = 0; x < dims.X; x++)
                    {
                        var (vx, vy, vz) = BoxerVoxelModel.ToVoxAxes(x, y, z, dims);
                        var colorIndex = merged.At(vx, vy, vz);
                        if (colorIndex == 0)
                        {
                            continue;
                        }

                        var color = scene.Palette[colorIndex] | 0xFF000000u; // 透明度は見ない
                        if (byColor.TryGetValue(color, out var material))
                        {
                            materials[x + (dims.X * (y + (dims.Y * z)))] = material == BoxerAnatomy.BlueGlove ? BoxerAnatomy.RedGlove : material;
                        }
                        else
                        {
                            unknown[color] = unknown.GetValueOrDefault(color) + 1;
                        }
                    }
                }
            }

            if (unknown.Count > 0)
            {
                problems.Add($"'{kind}' has colours that are not a material: " + BoxerVoxelModel.DescribeColours(unknown));
                continue;
            }

            weapons[kind] = new WeaponVoxelOverrides.Cells(dims, materials);
        }

        if (problems.Count > 0)
        {
            throw new InvalidDataException("The weapon voxel model cannot be used:\n  " + string.Join("\n  ", problems));
        }

        return new WeaponVoxelOverrides(weapons);
    }

    /// <summary>塊（番号順）を位置どおりに 1 つのモデルへ結合する。番号が 0 から続かない・位置が無い・隙間や重なりがあればエラーの文。</summary>
    private static string? Merge(WeaponKind kind, SortedDictionary<int, VoxModel> chunks, out VoxModel merged)
    {
        merged = chunks.Values.First();
        var missing = Enumerable.Range(0, chunks.Count).Where(i => !chunks.ContainsKey(i)).ToList();
        if (missing.Count > 0 || chunks.Keys.Max() != chunks.Count - 1)
        {
            var wanted = Enumerable.Range(0, chunks.Keys.Max() + 1).Where(i => !chunks.ContainsKey(i)).Select(i => $"{kind}#{i}");
            return $"'{kind}' chunks are not numbered 0..{chunks.Count - 1} without gaps (missing {string.Join(", ", wanted)})";
        }

        if (chunks.Values.Any(c => c.Position is null))
        {
            return $"'{kind}' chunks have no position in the scene";
        }

        var origin = (
            X: chunks.Values.Min(c => c.Position!.Value.X),
            Y: chunks.Values.Min(c => c.Position!.Value.Y),
            Z: chunks.Values.Min(c => c.Position!.Value.Z));
        var size = (
            X: chunks.Values.Max(c => c.Position!.Value.X + c.Size.X) - origin.X,
            Y: chunks.Values.Max(c => c.Position!.Value.Y + c.Size.Y) - origin.Y,
            Z: chunks.Values.Max(c => c.Position!.Value.Z + c.Size.Z) - origin.Z);
        var cells = new byte[size.X * size.Y * size.Z];
        var covered = new bool[cells.Length];
        foreach (var (index, chunk) in chunks)
        {
            var at = (X: chunk.Position!.Value.X - origin.X, Y: chunk.Position!.Value.Y - origin.Y, Z: chunk.Position!.Value.Z - origin.Z);
            for (var z = 0; z < chunk.Size.Z; z++)
            {
                for (var y = 0; y < chunk.Size.Y; y++)
                {
                    for (var x = 0; x < chunk.Size.X; x++)
                    {
                        var target = at.X + x + (size.X * (at.Y + y + (size.Y * (at.Z + z))));
                        if (covered[target])
                        {
                            return $"'{kind}#{index}' overlaps another chunk of {kind}; the chunks must tile the weapon (do not move them)";
                        }

                        covered[target] = true;
                        cells[target] = chunk.At(x, y, z);
                    }
                }
            }
        }

        if (covered.Contains(false))
        {
            return $"'{kind}' chunks leave a gap between them; the chunks must tile the weapon (do not move or resize them)";
        }

        merged = new VoxModel(kind.ToString(), size, cells);
        return null;
    }

    /// <summary>
    /// 今の中身。<paramref name="overrides"/> を渡すとその直しを当てたもの、null なら生成のまま（赤コーナーの革）。.vox へ書き出す元。
    /// </summary>
    public static IReadOnlyDictionary<WeaponKind, VoxelGrid> Parts(WeaponVoxelOverrides? overrides) =>
        Editable.ToDictionary(kind => kind, kind => Weapon.Build(kind, BoxerAnatomy.RedGlove, overrides));
}

/// <summary>
/// ボクセル編集ソフトで直した、武器の種類ごとの中身（材質の並び、ゲームの座標）。<see cref="Weapon.Build(WeaponKind, byte, WeaponVoxelOverrides?)"/> は、
/// その種類の直しがあって大きさ（セルの数）が生成と同じならそれを使い、違えば生成のまま（警告を出す）。
/// </summary>
public sealed class WeaponVoxelOverrides
{
    private static readonly Lazy<WeaponVoxelOverrides?> ShippedModel = new(LoadShipped);
    private readonly IReadOnlyDictionary<WeaponKind, Cells> _weapons;
    private readonly HashSet<(WeaponKind, (int, int, int))> _warned = [];

    public WeaponVoxelOverrides(IReadOnlyDictionary<WeaponKind, Cells> weapons) => _weapons = weapons;

    /// <summary>直した中身。<see cref="Materials"/> は x + Dx × (y + Dy × z) の順の材質番号。革は赤（持ち主のコーナーで置き換える）。</summary>
    public sealed record Cells((int X, int Y, int Z) Dimensions, byte[] Materials);

    /// <summary>ゲームに組み込んだ直し（games/MiniRing/Voxels/weapons.vox）。無ければ null（生成のまま）。</summary>
    public static WeaponVoxelOverrides? Shipped => ShippedModel.Value;

    public IReadOnlyCollection<WeaponKind> Kinds => (IReadOnlyCollection<WeaponKind>)_weapons.Keys;

    public Cells? this[WeaponKind kind] => _weapons.GetValueOrDefault(kind);

    /// <summary>種類 <paramref name="kind"/> の、セルの数が <paramref name="dims"/> の中身。無い・大きさが違うなら null（違うときは 1 回だけ警告）。</summary>
    public byte[]? For(WeaponKind kind, (int X, int Y, int Z) dims)
    {
        if (!_weapons.TryGetValue(kind, out var cells))
        {
            return null;
        }

        if (cells.Dimensions == dims)
        {
            return cells.Materials;
        }

        lock (_warned)
        {
            if (_warned.Add((kind, dims)))
            {
                var message = $"Edited voxel model for {kind} is {cells.Dimensions} cells but the game needs {dims}; using the generated weapon. Re-export and edit again (miniring-arena weaponexport).";
                Trace.TraceWarning(message);
                Console.Error.WriteLine($"warning: {message}");
            }
        }

        return null;
    }

    public static WeaponVoxelOverrides Load(string path)
    {
        using var stream = File.OpenRead(path);
        return WeaponVoxelModel.FromVox(MagicaVox.Read(stream));
    }

    private static WeaponVoxelOverrides? LoadShipped()
    {
        using var stream = typeof(WeaponVoxelOverrides).Assembly.GetManifestResourceStream(WeaponVoxelModel.ResourceName);
        return stream is null ? null : WeaponVoxelModel.FromVox(MagicaVox.Read(stream));
    }
}
