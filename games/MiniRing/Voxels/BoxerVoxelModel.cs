using System.Diagnostics;
using System.Numerics;
using Aigis.Foundation.Voxels;
using Aigis.Physics;

namespace MiniRing.Voxels;

/// <summary>
/// ボクサーのボクセルの中身を、ボクセル編集ソフト（Vengi・MagicaVoxel）で直すための .vox との行き来。
/// <list type="bullet">
/// <item>書き出し（<see cref="ToVox"/>）: 部位の種類ごとに 1 モデル（名前は <see cref="BodyPartKind"/>）。色番号 = 材質番号、色は材質の色。
/// 左右の腕・脚は同じ中身（<see cref="BoxerAnatomy.Build"/> は種類と大きさだけで決まる）なので 1 つずつ。</item>
/// <item>読み込み（<see cref="FromVox"/>）: 色番号ではなく<b>色</b>で材質に戻す（編集ソフトがパレットの並びを変えても崩れない）。
/// 材質のどれとも一致しない色はエラー（どの色が何個か）。トランクスは赤で塗る（赤・青はコーナーごとに読み込むときに塗り替える）。</item>
/// </list>
/// 座標: ゲームの部位のローカルは Y が上・+Z が体の前。.vox は Z が上なので (x, y, z) → (x, Dz − 1 − z, y)（右手系のまま。体の前は .vox の −Y、
/// MagicaVoxel の正面の視点）。
/// </summary>
public static class BoxerVoxelModel
{
    /// <summary>ゲームに組み込んだ直した中身のリソース名（games/MiniRing/Voxels/boxer.vox。無ければ生成のまま）。</summary>
    public const string ResourceName = "MiniRing.Voxels.boxer.vox";

    /// <summary>.vox のパレットで、材質に使わない色番号の色（どの材質とも違う、目立つ色）。</summary>
    private static readonly uint Unused = MagicaVox.Rgba(255, 0, 255);

    /// <summary>材質の色（.vox の RGBA）。</summary>
    public static uint ColorOf(byte material)
    {
        var c = BoxerAnatomy.Palette[material].Color;
        static byte B(float v) => (byte)Math.Clamp(MathF.Round(v * 255f), 0f, 255f);
        return MagicaVox.Rgba(B(c.X), B(c.Y), B(c.Z));
    }

    /// <summary>.vox の 256 色のパレット: 色番号 = 材質番号、使わない番号は目立つ色。武器の .vox（<see cref="WeaponVoxelModel"/>）も同じ。</summary>
    internal static uint[] MaterialPalette()
    {
        var palette = Enumerable.Repeat(Unused, 256).ToArray();
        for (var m = 1; m < BoxerAnatomy.Palette.Count; m++)
        {
            palette[m] = ColorOf((byte)m);
        }

        return palette;
    }

    /// <summary>色（透明度を除く）から材質を引く表。読み込みは色番号でなく色で材質に戻す。</summary>
    internal static Dictionary<uint, byte> MaterialByColor()
    {
        var byColor = new Dictionary<uint, byte>();
        for (var m = 1; m < BoxerAnatomy.Palette.Count; m++)
        {
            byColor.TryAdd(ColorOf((byte)m) | 0xFF000000u, (byte)m);
        }

        return byColor;
    }

    /// <summary>材質に無い色の一覧（多い順の「#RRGGBB × 個数」）。エラーの文に使う。</summary>
    internal static string DescribeColours(Dictionary<uint, int> unknown) =>
        string.Join(", ", unknown.OrderByDescending(u => u.Value).Select(u => $"#{u.Key & 0xFF:X2}{(u.Key >> 8) & 0xFF:X2}{(u.Key >> 16) & 0xFF:X2} × {u.Value}"));

    /// <summary>部位ごとの格子を .vox のシーンにする。</summary>
    public static VoxScene ToVox(IReadOnlyDictionary<BodyPartKind, VoxelGrid> parts)
    {
        ArgumentNullException.ThrowIfNull(parts);
        var palette = MaterialPalette();

        var models = new List<VoxModel>();
        foreach (var (kind, grid) in parts.OrderBy(p => p.Key))
        {
            var (dx, dy, dz) = grid.Dimensions;
            var size = (dx, dz, dy);
            var cells = new byte[dx * dy * dz];
            for (var z = 0; z < dz; z++)
            {
                for (var y = 0; y < dy; y++)
                {
                    for (var x = 0; x < dx; x++)
                    {
                        var (vx, vy, vz) = ToVoxAxes(x, y, z, grid.Dimensions);
                        cells[vx + (size.dx * (vy + (size.dz * vz)))] = grid.At(x, y, z);
                    }
                }
            }

            models.Add(new VoxModel(kind.ToString(), size, cells));
        }

        return new VoxScene(models, palette);
    }

    /// <summary>
    /// .vox のシーンを部位ごとの材質の並びにする。モデルの名前が <see cref="BodyPartKind"/> でないもの・同じ部位が 2 つあるもの・
    /// 材質の色でない色があるものはエラー（どこが悪いかを全部並べる）。
    /// 名前が <c>&lt;部位&gt;Halo</c> のモデルは、その部位の「まわり」（<see cref="BoxerVoxelOverrides.HaloFor"/>: 部位の格子を各面 <see cref="BoxerVoxelOverrides.HaloMargin"/>
    /// セル広げた所の CT の組織。関節のつなぎ <see cref="JointBridge"/> が隙間の材質に使う）。
    /// </summary>
    public static BoxerVoxelOverrides FromVox(VoxScene scene)
    {
        ArgumentNullException.ThrowIfNull(scene);
        var byColor = MaterialByColor();

        var problems = new List<string>();
        var parts = new Dictionary<BodyPartKind, BoxerVoxelOverrides.PartCells>();
        var halos = new Dictionary<BodyPartKind, BoxerVoxelOverrides.PartCells>();
        foreach (var model in scene.Models)
        {
            // 名前はそのまま一致するものだけ（Enum.TryParse は "3" や "Chest, Pelvis" も通してしまう）。
            var isHalo = model.Name.EndsWith(BoxerVoxelOverrides.HaloSuffix, StringComparison.Ordinal);
            var partName = isHalo ? model.Name[..^BoxerVoxelOverrides.HaloSuffix.Length] : model.Name;
            if (!Enum.GetNames<BodyPartKind>().Contains(partName, StringComparer.Ordinal))
            {
                problems.Add($"model '{model.Name}' is not a body part ({string.Join(", ", Enum.GetNames<BodyPartKind>())}, or one of them + '{BoxerVoxelOverrides.HaloSuffix}')");
                continue;
            }

            var kind = Enum.Parse<BodyPartKind>(partName);
            if (model.Rotated)
            {
                problems.Add($"'{model.Name}' is rotated or mirrored in the scene (object mode); undo the rotation, the game reads the voxels as they are stored");
                continue;
            }

            var target = isHalo ? halos : parts;
            if (target.ContainsKey(kind))
            {
                problems.Add($"'{model.Name}' appears twice");
                continue;
            }

            var dims = (X: model.Size.X, Y: model.Size.Z, Z: model.Size.Y);
            var materials = new byte[dims.X * dims.Y * dims.Z];
            var unknown = new Dictionary<uint, int>();
            for (var z = 0; z < dims.Z; z++)
            {
                for (var y = 0; y < dims.Y; y++)
                {
                    for (var x = 0; x < dims.X; x++)
                    {
                        var (vx, vy, vz) = ToVoxAxes(x, y, z, dims);
                        var index = model.At(vx, vy, vz);
                        if (index == 0)
                        {
                            continue;
                        }

                        var color = scene.Palette[index] | 0xFF000000u; // 透明度は見ない
                        if (byColor.TryGetValue(color, out var material))
                        {
                            materials[x + (dims.X * (y + (dims.Y * z)))] = material == BoxerAnatomy.BlueTrunks ? BoxerAnatomy.RedTrunks : material;
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
                problems.Add($"'{model.Name}' has colours that are not a material: " + DescribeColours(unknown));
                continue;
            }

            target[kind] = new BoxerVoxelOverrides.PartCells(dims, materials);
        }

        if (problems.Count > 0)
        {
            throw new InvalidDataException("The voxel model cannot be used:\n  " + string.Join("\n  ", problems));
        }

        return new BoxerVoxelOverrides(parts, halos);
    }

    /// <summary>
    /// 今の設定（<paramref name="tuning"/>）での部位ごとの中身（赤コーナーの身なり）。<paramref name="overrides"/> を渡すとその直しを当てたもの、
    /// null なら生成のまま。.vox へ書き出す元。
    /// </summary>
    public static IReadOnlyDictionary<BodyPartKind, VoxelGrid> Parts(FighterTuning tuning, BoxerVoxelOverrides? overrides)
    {
        ArgumentNullException.ThrowIfNull(tuning);
        using var world = new PhysicsWorld();
        var fighter = Fighter.Spawn(world, tuning, "model", Vector3.Zero, 0f);
        var shapes = LowPoly.BoxerModel.Build(tuning.HalfExtents, tuning.ModelSubdivisions);
        var parts = new Dictionary<BodyPartKind, VoxelGrid>();
        foreach (var id in fighter.AllParts)
        {
            var kind = fighter.KindOf(id);
            if (kind == BodyPartKind.Abdomen)
            {
                continue; // 分割胴: 腹は胸と 1 つの胴の格子として書き出し・直す（BoxerBody が胸と腹に切り分ける）
            }

            if (!parts.ContainsKey(kind))
            {
                var half = kind == BodyPartKind.Chest && fighter.Parts.Abdomen is not null ? tuning.HalfExtents.Chest : fighter.HalfExtentsOf(id);
                parts[kind] = BoxerAnatomy.Build(kind, half, tuning.VoxelSize, shapes.Mesh(kind), Outfit.RedCorner, overrides);
            }
        }

        return parts;
    }

    internal static (int X, int Y, int Z) ToVoxAxes(int x, int y, int z, (int X, int Y, int Z) dims) => (x, dims.Z - 1 - z, y);
}

/// <summary>
/// ボクセル編集ソフトで直した、部位の種類ごとの中身（材質の並び、ゲームの座標）。<see cref="BoxerAnatomy.Build"/> は、
/// その部位の直しがあって大きさ（セルの数）が今の設定と同じならそれを使い、違えば生成のまま（警告を出す）。
/// </summary>
public sealed class BoxerVoxelOverrides
{
    private static readonly Lazy<BoxerVoxelOverrides?> ShippedModel = new(LoadShipped);
    private readonly IReadOnlyDictionary<BodyPartKind, PartCells> _parts;
    private readonly HashSet<(BodyPartKind, (int, int, int))> _warned = [];

    private readonly IReadOnlyDictionary<BodyPartKind, PartCells> _halos;

    public BoxerVoxelOverrides(IReadOnlyDictionary<BodyPartKind, PartCells> parts, IReadOnlyDictionary<BodyPartKind, PartCells>? halos = null)
    {
        _parts = parts;
        _halos = halos ?? new Dictionary<BodyPartKind, PartCells>();
    }

    /// <summary>まわりのモデルの名前の終わり（<c>ChestHalo</c> など）。</summary>
    public const string HaloSuffix = "Halo";

    /// <summary>
    /// 部位のまわり（<see cref="HaloFor"/>）が、部位の格子から各面へ広がるセルの数。関節のつなぎの箱（一辺 10 セル以上、関節の点が中心）の半分を覆う。
    /// 箱が 12 セルを超える関節（腰・背骨。<see cref="JointBridge.SizeFor"/>）は、親の箱から 6 セルより先の隙間がまわりの外になり、そこは近いセルの材質に戻る。
    /// </summary>
    public const int HaloMargin = 6;

    /// <summary>
    /// 部位 <paramref name="kind"/>（セルの数 <paramref name="partDims"/>）のまわり: 部位の格子を各面 <see cref="HaloMargin"/> セル広げた格子に、その所の組織
    /// （CT から作った体なら、切れ目の向こうにつながる首・関節の骨・肉・動脈）。格子のセル (x, y, z) は、まわりの (x + HaloMargin, ...) に当たる。
    /// 無い・大きさが合わないなら null（関節のつなぎは近いセルの材質を使う）。
    /// </summary>
    public PartCells? HaloFor(BodyPartKind kind, (int X, int Y, int Z) partDims) =>
        _halos.TryGetValue(kind, out var halo)
        && halo.Dimensions == (partDims.X + (2 * HaloMargin), partDims.Y + (2 * HaloMargin), partDims.Z + (2 * HaloMargin)) ? halo : null;

    public IReadOnlyCollection<BodyPartKind> HaloKinds => _halos.Keys.ToArray();

    /// <summary>直した部位の中身。<see cref="Materials"/> は x + Dx × (y + Dy × z) の順の材質番号。トランクスは赤（コーナーで塗り替える）。</summary>
    public sealed record PartCells((int X, int Y, int Z) Dimensions, byte[] Materials)
    {
        /// <summary>X で鏡に写したもの（体の +X 側の腕・脚のまわり。<see cref="BoxerBody"/> の部位の格子と同じ写し方）。</summary>
        public PartCells MirroredX()
        {
            var (nx, ny, nz) = Dimensions;
            var cells = new byte[Materials.Length];
            for (var i = 0; i < cells.Length; i++)
            {
                var x = i % nx;
                cells[i] = Materials[i - x + (nx - 1 - x)];
            }

            return this with { Materials = cells };
        }
    }

    /// <summary>ゲームに組み込んだ直し（games/MiniRing/Voxels/boxer.vox）。無ければ null（生成のまま）。</summary>
    public static BoxerVoxelOverrides? Shipped => ShippedModel.Value;

    public IReadOnlyCollection<BodyPartKind> Kinds => (IReadOnlyCollection<BodyPartKind>)_parts.Keys;

    public PartCells? this[BodyPartKind kind] => _parts.GetValueOrDefault(kind);

    /// <summary>部位 <paramref name="kind"/> の、セルの数が <paramref name="dims"/> の中身。無い・大きさが違うなら null。</summary>
    public byte[]? For(BodyPartKind kind, (int X, int Y, int Z) dims)
    {
        if (!_parts.TryGetValue(kind, out var part))
        {
            return null;
        }

        if (part.Dimensions == dims)
        {
            return part.Materials;
        }

        lock (_warned)
        {
            if (_warned.Add((kind, dims)))
            {
                var message = $"Edited voxel model for {kind} is {part.Dimensions} cells but the current settings need {dims}; using the generated model. Re-export and edit again (miniring-arena voxexport).";
                Trace.TraceWarning(message);
                Console.Error.WriteLine($"warning: {message}");
            }
        }

        return null;
    }

    public static BoxerVoxelOverrides Load(string path)
    {
        using var stream = File.OpenRead(path);
        return BoxerVoxelModel.FromVox(MagicaVox.Read(stream));
    }

    private static BoxerVoxelOverrides? LoadShipped()
    {
        using var stream = typeof(BoxerVoxelOverrides).Assembly.GetManifestResourceStream(BoxerVoxelModel.ResourceName);
        return stream is null ? null : BoxerVoxelModel.FromVox(MagicaVox.Read(stream));
    }
}
