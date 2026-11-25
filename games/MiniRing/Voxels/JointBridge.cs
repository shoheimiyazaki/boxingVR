using System.Numerics;
using Aigis.Foundation.Voxels;
using Aigis.Physics;
using Aigis.Physics.Voxels;
using Aigis.Render.Voxels;

namespace MiniRing.Voxels;

/// <summary>
/// 関節のつなぎ（2026-11-19、PhysicsVersion 47）: つながった 2 つの部位（首なら胸と頭）のボクセルの格子は、関節が回ると間が開く（頭を打たれて傾く・
/// 肩が回る）。その隙間を刃が通っても何にも当たらず、関節の肉・骨を断てない。隙間に、2 つの部位の組織がつないでいる所を、当たりとダメージのためだけの
/// 小さな直方体（ボクセル 10 × 10 × 10 以上。大きさは <see cref="SizeFor"/>）として置く。足首も含め、すべての関節に同じに置く（関節の種類で選ばない）。
/// <list type="bullet">
/// <item><b>体の動きは変えない</b>: 親の部位の剛体を借りるだけ（<see cref="VoxelBody.Massless"/>: 質量も剛体の形も持たない。関節の力も無い）。
/// 刃が触れたときの運動量の受け渡しは、普通の部位との接触と同じく親の剛体へ掛かる。</item>
/// <item><b>どのセルがつなぎか</b>（<see cref="Refresh"/>）: 関節のまわり 5 セル以内で、いまの姿勢で親にも子にも埋まったセルが無く（そこは部位が当たる）、
/// 親のセルも子のセルも 1 セル（約 14 mm）以内にある所。材質は、親の部位の「まわり」（<see cref="BoxerVoxelOverrides.HaloFor"/>: CT から作った体の、
/// 切れ目の向こうにつながる組織）があればその所の組織、無ければ（またはそこが空なら）その 2 つの近いセルのうち強い方（骨が近ければ骨）。つなぎは毎歩、
/// いまの 2 つの部位の格子から作り直す（姿勢の記憶は持たない。部位のセルが削れれば、つなぎもそのぶん細る）。</item>
/// <item><b>断たれたら</b>（<see cref="Sever"/>）: つなぎのセルを刃の接触（<see cref="VoxelContact"/>。圧力と材質）が壊したら、そのセルがつないでいた
/// 親・子の側の近いセルを壊す。そのあとは普通の部位の接触の後始末（骨が断たれたら落ちる・臓器が壊れたら死ぬ）。</item>
/// </list>
/// 近道は <c>docs/DEBT.md</c>。
/// </summary>
internal sealed class JointBridge
{
    /// <summary>つなぎの一辺のセル数の下限（首を薙ぐ測定で選んだ値。これより細い関節も、この大きさの隙間は埋める）。</summary>
    private const int MinCells = 10;

    /// <summary>つなぎの一辺のセル数（<see cref="SizeFor"/>）。</summary>
    private readonly int _cells;

    /// <summary>隙間をつなぐ、親・子のセルまでの距離 [セル]。</summary>
    private const int Reach = 1;

    private readonly bool[] _cut;

    /// <summary>親の側・子の側の、そのつなぎのセルをつないでいる部位の層（<see cref="ParentLayers"/>・<see cref="ChildLayers"/> の番号）とセル（-1: 無い）。</summary>
    private readonly (int Layer, int Cell)[] _sourceParent;
    private readonly (int Layer, int Cell)[] _sourceChild;
    private readonly Vector3 _centerOnParent;
    private int _activeCells;

    /// <summary>親の部位のまわり（無ければ null）。</summary>
    private readonly BoxerVoxelOverrides.PartCells? _halo;

    // parentLayers: 親の部位の組織を持つ格子（部位そのものが先頭。頭なら、その外を持つ顔の層が続く。同じ剛体に乗っている）。省略は部位だけ。
    // childLayers: 子の側の同じもの。
    // parentHalo: 親の部位のまわり（CT の体だけが持つ。<see cref="BoxerVoxelOverrides.HaloFor"/>）。隙間の材質をそこから取る。
    public JointBridge(HumanoidRagdoll.RagdollJoint joint, VoxelPart parent, VoxelPart child, Vector3 anchorOnParent, IReadOnlyList<VoxelPart>? parentLayers = null, IReadOnlyList<VoxelPart>? childLayers = null, BoxerVoxelOverrides.PartCells? parentHalo = null)
    {
        ArgumentNullException.ThrowIfNull(parent);
        ArgumentNullException.ThrowIfNull(child);
        _halo = parentHalo;
        Joint = joint;
        Parent = parent;
        Child = child;
        ParentLayers = parentLayers ?? [parent];
        ChildLayers = childLayers ?? [child];
        var grid = parent.Body.Grid;
        _cells = SizeFor(grid, child.Body.Grid);
        var cell = grid.CellSize;
        var half = grid.HalfExtents;
        // 中心は親の格子の線（セルの角）に合わせる: つなぎのセルの中心が、親のセルの中心と重なる。
        var local = anchorOnParent - parent.Body.GridCenterLocal;
        var snapped = new Vector3(
            (MathF.Round((local.X + half.X) / cell.X) * cell.X) - half.X,
            (MathF.Round((local.Y + half.Y) / cell.Y) * cell.Y) - half.Y,
            (MathF.Round((local.Z + half.Z) / cell.Z) * cell.Z) - half.Z);
        _centerOnParent = snapped + parent.Body.GridCenterLocal;
        var bridgeGrid = new VoxelGrid(grid.Palette, cell * (_cells * 0.5f), (_cells, _cells, _cells), static (_, _) => 0);
        Body = new VoxelBody(parent.Body.World, parent.Body.BodyId, bridgeGrid, _centerOnParent) { Massless = true };
        _cut = new bool[bridgeGrid.CellCount];
        _sourceParent = new (int, int)[bridgeGrid.CellCount];
        _sourceChild = new (int, int)[bridgeGrid.CellCount];
    }

    /// <summary>
    /// つなぎの一辺のセル数: 2 つの部位のうち細い方の平均の断面の幅（箱の体積 ÷ 一番長い辺 の平方根。関節でつながる断面の大きさ）を、セルの数にして
    /// 偶数へ切り上げる（箱の中心を格子の線に置くため）。<see cref="MinCells"/> より小さければ下限。関節ごとの表は持たない。
    /// </summary>
    internal static int SizeFor(VoxelGrid parent, VoxelGrid child)
    {
        ArgumentNullException.ThrowIfNull(parent);
        ArgumentNullException.ThrowIfNull(child);
        static float Width(VoxelGrid g)
        {
            var h = g.HalfExtents;
            return 2f * MathF.Sqrt(h.X * h.Y * h.Z / MathF.Max(h.X, MathF.Max(h.Y, h.Z)));
        }

        var cell = MathF.Max(parent.CellSize.X, MathF.Max(parent.CellSize.Y, parent.CellSize.Z));
        var cells = (int)MathF.Round(MathF.Min(Width(parent), Width(child)) / cell);
        return Math.Max(MinCells, (cells + 1) & ~1);
    }

    public HumanoidRagdoll.RagdollJoint Joint { get; }

    public VoxelPart Parent { get; }

    public VoxelPart Child { get; }

    /// <summary>
    /// 親の部位の組織を持つ格子（部位そのものが先頭）。頭は 14 mm の格子（頭蓋骨・脳・首）の外を顔の層（7 mm）が持つので、あごや首の付け根の組織は
    /// 顔の層にある（PhysicsVersion 59: 顔の層を入れたとき、つなぎが 14 mm の格子だけを見ていて、外の組織が見えなかった）。
    /// </summary>
    public IReadOnlyList<VoxelPart> ParentLayers { get; }

    /// <summary>子の部位の組織を持つ格子（<see cref="ParentLayers"/> と同じ）。</summary>
    public IReadOnlyList<VoxelPart> ChildLayers { get; }

    /// <summary>接触の形。親の剛体を借りている（<see cref="VoxelBody.Massless"/>）。</summary>
    public VoxelBody Body { get; }

    /// <summary>いま刃が触れうるセルの数（<see cref="Refresh"/> の後）。</summary>
    public int ActiveCells => _activeCells;

    /// <summary>つなぎの箱の面（外側の 1 段）にあるセルの数。0 でなければ、隙間が箱より広いかもしれない（箱の大きさの確かめ。<see cref="Refresh(Func{Vector3, bool})"/> の後）。</summary>
    public int EdgeCells
    {
        get
        {
            var grid = Body.Grid;
            var n = 0;
            for (var i = 0; i < grid.CellCount; i++)
            {
                var (x, y, z) = grid.Coordinates(i);
                var edge = x is 0 || y is 0 || z is 0 || x == _cells - 1 || y == _cells - 1 || z == _cells - 1;
                n += edge && !_cut[i] && grid.MaterialAt(i) != 0 ? 1 : 0;
            }

            return n;
        }
    }

    /// <summary>
    /// 武器 <paramref name="weapon"/>（歩の前の姿勢 <paramref name="before"/> から今の姿勢まで動く）の格子の箱が掃く所のセルだけを、<see cref="Refresh(Func{Vector3, bool})"/> で作り直す。
    /// 掃く所 = 歩の前と今の箱を、箱の角が動いた距離（と 3.4 cm）だけ広げた 2 つの箱。刃は細長いので、世界の軸に沿った箱より、触れうるセルがずっと少ない。
    /// </summary>
    public bool RefreshNear(VoxelBody weapon, BodyState before)
    {
        ArgumentNullException.ThrowIfNull(weapon);
        var now = weapon.GridToWorld();
        var then = weapon.GridToWorld(before);
        Matrix4x4.Invert(now, out var nowInverse);
        Matrix4x4.Invert(then, out var thenInverse);
        var half = weapon.Grid.HalfExtents;
        var moved = 0f;
        for (var corner = 0; corner < 8; corner++)
        {
            var local = new Vector3((corner & 1) == 0 ? -half.X : half.X, (corner & 2) == 0 ? -half.Y : half.Y, (corner & 4) == 0 ? -half.Z : half.Z);
            moved = MathF.Max(moved, Vector3.Distance(Vector3.Transform(local, now), Vector3.Transform(local, then)));
        }

        var reach = half + new Vector3(moved + 0.0338f);
        bool Inside(Vector3 world, Matrix4x4 inverse)
        {
            var p = Vector3.Transform(world, inverse);
            return MathF.Abs(p.X) <= reach.X && MathF.Abs(p.Y) <= reach.Y && MathF.Abs(p.Z) <= reach.Z;
        }

        return Refresh(world => Inside(world, nowInverse) || Inside(world, thenInverse));
    }

    /// <summary>
    /// 世界の点 <paramref name="include"/> が真のセルだけを、いまの姿勢の 2 つの部位の格子から作り直す。
    /// 外のセルは今の歩の刃が届かないので触らない。つなぎにできるセルがあれば true。
    /// </summary>
    public bool Refresh(Func<Vector3, bool> include)
    {
        ArgumentNullException.ThrowIfNull(include);
        var parentGrid = Parent.Body.Grid;
        var grid = Body.Grid;
        var reach = Reach * MathF.Max(parentGrid.CellSize.X, MathF.Max(parentGrid.CellSize.Y, parentGrid.CellSize.Z));
        var toWorld = Body.GridToWorld();
        var toChildLayers = new Matrix4x4[ChildLayers.Count];
        for (var l = 0; l < toChildLayers.Length; l++)
        {
            Matrix4x4.Invert(ChildLayers[l].Body.GridToWorld(), out var worldToChild);
            toChildLayers[l] = toWorld * worldToChild;
        }

        var parentOffsets = new Vector3[ParentLayers.Count];
        for (var l = 0; l < parentOffsets.Length; l++)
        {
            parentOffsets[l] = Body.GridCenterLocal - ParentLayers[l].Body.GridCenterLocal; // 同じ剛体なので、つなぎの座標 → 親の格子の座標は平行移動だけ
        }

        var epoch = JoinEpoch(toChildLayers, parentOffsets);
        var parentEpoch = ParentEpoch(parentOffsets);
        var active = 0;
        for (var z = 0; z < _cells; z++)
        {
            for (var y = 0; y < _cells; y++)
            {
                for (var x = 0; x < _cells; x++)
                {
                    var i = grid.Index(x, y, z);
                    if (_cut[i])
                    {
                        continue;
                    }

                    var center = grid.CellCenter(x, y, z);
                    var world = Vector3.Transform(center, toWorld);
                    if (!include(world))
                    {
                        active += grid.MaterialAt(i) != 0 ? 1 : 0;
                        continue;
                    }

                    // Join は両側の層の格子と相対の姿勢だけで決まる: それが前の呼び出しから変わっていなければ、前の答えをそのまま使う
                    // （同じ歩の中で、掃き直し・別の武器が同じつなぎを何度も作り直す）。
                    (byte Material, (int Layer, int Cell) Parent, (int Layer, int Cell) Child) join;
                    if (_joinEpoch[i] == epoch)
                    {
                        join = _joinMemo[i];
                    }
                    else
                    {
                        Aigis.Foundation.Voxels.StepProfile.Count(Aigis.Foundation.Voxels.StepProfile.Counter.BridgeCells, 1);
                        join = Join(center, parentOffsets, toChildLayers, reach, i, parentEpoch);
                        _joinMemo[i] = join;
                        _joinEpoch[i] = epoch;
                    }

                    var (material, sp, sc) = join;

                    _sourceParent[i] = sp;
                    _sourceChild[i] = sc;
                    if (grid.MaterialAt(i) != material)
                    {
                        grid.SetCellState(i, (material, material == 0 ? 0f : grid.Palette[material].Toughness, false, 0f));
                    }

                    active += material != 0 ? 1 : 0;
                }
            }
        }

        _activeCells = active;
        return active > 0;
    }

    /// <summary>セルごとの、前に計算した <see cref="Join"/> の答えと、そのときの入力の番号（<see cref="JoinEpoch"/>）。</summary>
    private (byte Material, (int Layer, int Cell) Parent, (int Layer, int Cell) Child)[]? _joinMemoArray;
    private int[]? _joinEpochArray;

    private (byte Material, (int Layer, int Cell) Parent, (int Layer, int Cell) Child)[] _joinMemo => _joinMemoArray ??= new (byte, (int, int), (int, int))[Body.Grid.CellCount];

    private int[] _joinEpoch => _joinEpochArray ??= new int[Body.Grid.CellCount];

    /// <summary><see cref="Join"/> の入力（層の格子とその形の版・子の層への変換・親の層へのずれ）の、前の呼び出しのときの写し。</summary>
    private VoxelGrid[] _joinGrids = [];
    private int[] _joinVersions = [];
    private Matrix4x4[] _joinTransforms = [];
    private Vector3[] _joinOffsets = [];
    private int _epoch;

    /// <summary>セルごとの、親の層の答え（埋まったセルの中か・いちばん近い埋まったセル）と、そのときの親の層の番号（<see cref="ParentEpoch"/>）。</summary>
    private int[]? _parentEpoch;
    private (bool Filled, (int Layer, int Cell) Nearest)[]? _parentMemo;
    private VoxelGrid[] _parentGrids = [];
    private int[] _parentVersions = [];
    private Vector3[] _parentOffsetsSeen = [];
    private int _parentEpochNumber;

    /// <summary>親の層の格子（とその形の版・ずれ）の番号: 前の呼び出しと同じなら同じ番号、違えば新しい番号（1 から）。</summary>
    private int ParentEpoch(Vector3[] parentOffsets)
    {
        var same = _parentEpochNumber > 0 && _parentGrids.Length == ParentLayers.Count && parentOffsets.AsSpan().SequenceEqual(_parentOffsetsSeen);
        for (var l = 0; same && l < ParentLayers.Count; l++)
        {
            var grid = ParentLayers[l].Body.Grid;
            same = ReferenceEquals(grid, _parentGrids[l]) && grid.GeometryVersion == _parentVersions[l];
        }

        if (same)
        {
            return _parentEpochNumber;
        }

        _parentGrids = [.. ParentLayers.Select(layer => layer.Body.Grid)];
        _parentVersions = [.. _parentGrids.Select(grid => grid.GeometryVersion)];
        _parentOffsetsSeen = parentOffsets;
        return ++_parentEpochNumber;
    }

    /// <summary>
    /// 今の <see cref="Join"/> の入力の番号: 前の呼び出しとビット単位で同じなら同じ番号（セルの答えをそのまま使える）、違えば新しい番号。
    /// 番号は 1 から（セルの番号の初期値 0 はどの番号とも違う）。
    /// </summary>
    private int JoinEpoch(Matrix4x4[] toChildLayers, Vector3[] parentOffsets)
    {
        var count = ParentLayers.Count + ChildLayers.Count;
        var same = _epoch > 0 && _joinGrids.Length == count
            && toChildLayers.AsSpan().SequenceEqual(_joinTransforms) && parentOffsets.AsSpan().SequenceEqual(_joinOffsets);
        for (var l = 0; same && l < count; l++)
        {
            var grid = (l < ParentLayers.Count ? ParentLayers[l] : ChildLayers[l - ParentLayers.Count]).Body.Grid;
            same = ReferenceEquals(grid, _joinGrids[l]) && grid.GeometryVersion == _joinVersions[l];
        }

        if (same)
        {
            return _epoch;
        }

        _joinGrids = new VoxelGrid[count];
        _joinVersions = new int[count];
        for (var l = 0; l < count; l++)
        {
            var grid = (l < ParentLayers.Count ? ParentLayers[l] : ChildLayers[l - ParentLayers.Count]).Body.Grid;
            _joinGrids[l] = grid;
            _joinVersions[l] = grid.GeometryVersion;
        }

        _joinTransforms = toChildLayers;
        _joinOffsets = parentOffsets;
        return ++_epoch;
    }

    /// <summary>
    /// つなぎの座標の点 <paramref name="center"/> のセルが関節の組織か: 親にも子にも埋まったセルが無く、親のセルも子のセルも <see cref="Reach"/> 以内に
    /// あれば、その材質（まわりがあればその組織、無ければ近い 2 つのうち強い方）と、つないでいる親・子のセル。組織でなければ材質 0。
    /// </summary>
    /// <remarks>親の側の答えは、つなぎのセル <c>cell</c> ごとに、親の層の番号 <c>parentEpoch</c>（<see cref="ParentEpoch"/>）が変わるまで覚えておく。</remarks>
    private (byte Material, (int Layer, int Cell) Parent, (int Layer, int Cell) Child) Join(Vector3 center, Vector3[] parentOffsets, Matrix4x4[] toChildLayers, float reach, int cell, int parentEpoch)
    {
        // The parent's layers ride on the bridge's own rigid body (a fixed offset), so their answers depend only on their cells:
        // kept per cell until those change (the same calls as before, made once).
        var parentSide = _parentEpoch ??= new int[Body.Grid.CellCount];
        var parentMemo = _parentMemo ??= new (bool, (int, int))[Body.Grid.CellCount];
        if (parentSide[cell] != parentEpoch)
        {
            var filled = FilledIn(ParentLayers, center, parentOffsets, null);
            parentMemo[cell] = (filled, filled ? (-1, -1) : Nearest(ParentLayers, center, parentOffsets, null, reach));
            parentSide[cell] = parentEpoch;
        }

        var (parentFilled, sp) = parentMemo[cell];
        if (!parentFilled && sp.Cell < 0)
        {
            // 親のセルが届かない: 組織にならない（材質 0）。子の側は見なくても答えは同じ（材質 0 のセルのつないだ先は、壊すとき・描くときに読まない:
            // 材質 0 のセルは刃に壊されず、描くときは材質 0 なら空）。体の外の大半のセルがここで終わる。
            return (0, sp, (-1, -1));
        }

        if (parentFilled || FilledIn(ChildLayers, center, null, toChildLayers))
        {
            return (0, (-1, -1), (-1, -1));
        }

        var sc = Nearest(ChildLayers, center, null, toChildLayers, reach);
        if (sp.Cell < 0 || sc.Cell < 0)
        {
            return (0, sp, sc);
        }

        var gridA = ParentLayers[sp.Layer].Body.Grid;
        var gridB = ChildLayers[sc.Layer].Body.Grid;
        var a = gridA.MaterialAt(sp.Cell);
        var b = gridB.MaterialAt(sc.Cell);
        var material = HaloMaterial(ParentLayers[0].Body.Grid, center + parentOffsets[0]) is var halo and not 0 ? halo
            : StrengthOf(gridA, a) >= StrengthOf(gridB, b) ? a : b;
        return (material, sp, sc);
    }

    private VoxelPart? _view;
    private Matrix4x4[]? _viewPose;
    private int[]? _viewGeometry;

    /// <summary>
    /// 描くためのつなぎ（見た目だけ。試合の計算に入らない）: 今の姿勢の 2 つの部位の格子から、つなぎの全部のセルの組織を、当たりに使う格子とは
    /// 別の格子に作る（<see cref="Refresh"/> と同じ式で組織のセルを決め、色は親の近いセルの材質。刃が断ったセル（<see cref="Sever"/>）は空のまま）。関節を曲げて開いた隙間を、肉・骨の
    /// 色のボクセルが埋めて見える。両側の物質が削れればつなぎも細り、どちらかが無くなれば消える。描くたびに呼ぶ。
    /// </summary>
    public VoxelPart View()
    {
        var grid = Body.Grid;
        if (_view is null)
        {
            var empty = new VoxelGrid(grid.Palette, grid.HalfExtents, grid.Dimensions, static (_, _) => 0);
            _view = new VoxelPart(Child.Kind, new VoxelBody(Body.World, Body.BodyId, empty, Body.GridCenterLocal) { Massless = true }, VoxelVolume.For(empty), null, false);
        }

        var view = _view.Body.Grid;
        var parentGrid = Parent.Body.Grid;
        var reach = Reach * MathF.Max(parentGrid.CellSize.X, MathF.Max(parentGrid.CellSize.Y, parentGrid.CellSize.Z));
        var toWorld = Body.GridToWorld();
        var toChildLayers = new Matrix4x4[ChildLayers.Count];
        for (var l = 0; l < toChildLayers.Length; l++)
        {
            Matrix4x4.Invert(ChildLayers[l].Body.GridToWorld(), out var worldToChild);
            toChildLayers[l] = toWorld * worldToChild;
        }

        // Refresh the drawing once the surface changes or the relative pose moves by half a cell.
        // Nothing in the contact bridge uses this cache.
        var versions = ParentLayers.Concat(ChildLayers).Select(layer => layer.Body.Grid.GeometryVersion).ToArray();
        var halfCell = 0.5f * MathF.Min(view.CellSize.X, MathF.Min(view.CellSize.Y, view.CellSize.Z));
        if (_viewGeometry is { } before && before.AsSpan().SequenceEqual(versions) && _viewPose is { } pose
            && toChildLayers.Zip(pose).All(pair => WithinCell(pair.First - pair.Second, view.HalfExtents, halfCell)))
        {
            return _view;
        }
        _viewGeometry = versions;
        _viewPose = toChildLayers;

        var parentOffsets = new Vector3[ParentLayers.Count];
        for (var l = 0; l < parentOffsets.Length; l++)
        {
            parentOffsets[l] = Body.GridCenterLocal - ParentLayers[l].Body.GridCenterLocal;
        }

        var parentEpoch = ParentEpoch(parentOffsets);
        for (var i = 0; i < view.CellCount; i++)
        {
            var (x, y, z) = view.Coordinates(i);
            var center = view.CellCenter(x, y, z);
            var join = _cut[i] ? default : Join(center, parentOffsets, toChildLayers, reach, i, parentEpoch);
            // 色は親の近いセルの材質（親の皮膚・服がそのまま関節を包んで見える）。当たりに使うつなぎの材質（強い方）は骨・筋肉が多く、関節が赤い輪に見えた。
            var material = join.Material == 0 || join.Parent.Cell < 0 ? (byte)0 : ParentLayers[join.Parent.Layer].Body.Grid.MaterialAt(join.Parent.Cell);
            if (view.MaterialAt(i) != material)
            {
                view.SetCellState(i, (material, material == 0 ? 0f : view.Palette[material].Toughness, false, 0f));
            }
        }

        return _view;
    }

    private static bool WithinCell(Matrix4x4 delta, Vector3 half, float limit)
    {
        var motion = delta.Translation.Length()
            + (half.X * new Vector3(delta.M11, delta.M12, delta.M13).Length())
            + (half.Y * new Vector3(delta.M21, delta.M22, delta.M23).Length())
            + (half.Z * new Vector3(delta.M31, delta.M32, delta.M33).Length());
        return motion <= limit;
    }

    /// <summary>
    /// 刃の接触 <paramref name="hit"/> がつなぎのセルを壊したら、そのセルをつないでいた親・子のセルを壊す。壊したセルを、部位（部位の層ごと）の格子の座標で返す
    /// （壊れたセルのある部位だけ。親の側 → 子の側の順）。
    /// </summary>
    public List<(VoxelPart Part, List<RemovedVoxel> Cells)> Sever(VoxelContactResult hit)
    {
        ArgumentNullException.ThrowIfNull(hit);
        var parent = ParentLayers.Select(_ => new List<RemovedVoxel>()).ToArray();
        var child = ChildLayers.Select(_ => new List<RemovedVoxel>()).ToArray();
        var grid = Body.Grid;
        foreach (var gone in hit.RemovedB)
        {
            var i = grid.CellAt(gone.LocalCenter);
            if (i < 0 || _cut[i])
            {
                continue;
            }

            _cut[i] = true;
            _viewPose = null;
            if (_sourceParent[i].Cell >= 0)
            {
                ParentLayers[_sourceParent[i].Layer].Body.Grid.Break(_sourceParent[i].Cell, parent[_sourceParent[i].Layer]);
            }

            if (_sourceChild[i].Cell >= 0)
            {
                ChildLayers[_sourceChild[i].Layer].Body.Grid.Break(_sourceChild[i].Cell, child[_sourceChild[i].Layer]);
            }
        }

        var result = new List<(VoxelPart, List<RemovedVoxel>)>();
        for (var l = 0; l < parent.Length; l++)
        {
            if (parent[l].Count > 0)
            {
                result.Add((ParentLayers[l], parent[l]));
            }
        }

        for (var l = 0; l < child.Length; l++)
        {
            if (child[l].Count > 0)
            {
                result.Add((ChildLayers[l], child[l]));
            }
        }

        return result;
    }

    private static float StrengthOf(VoxelGrid grid, byte material) => grid.Palette[material].MechanicsOrDefault.Strength;

    /// <summary>親の格子の点 <paramref name="local"/> の、親のまわりの材質（まわりが無い・外なら 0）。セルの数で引くので、体格で箱の寸法が変わっても同じ所を指す。</summary>
    private byte HaloMaterial(VoxelGrid parentGrid, Vector3 local)
    {
        if (_halo is not { } halo)
        {
            return 0;
        }

        var c = (local + parentGrid.HalfExtents) / parentGrid.CellSize;
        var (x, y, z) = ((int)MathF.Floor(c.X) + BoxerVoxelOverrides.HaloMargin, (int)MathF.Floor(c.Y) + BoxerVoxelOverrides.HaloMargin, (int)MathF.Floor(c.Z) + BoxerVoxelOverrides.HaloMargin);
        var (nx, ny, nz) = halo.Dimensions;
        return x < 0 || y < 0 || z < 0 || x >= nx || y >= ny || z >= nz ? (byte)0 : halo.Materials[x + (nx * (y + (ny * z)))];
    }

    /// <summary>
    /// つなぎの格子の点 <paramref name="center"/> の、層 <paramref name="l"/> の格子での座標: 平行移動 <paramref name="offsets"/> か、変換 <paramref name="transforms"/> で
    /// （どちらか一方を渡す。セルごとにラムダを作らない: 刃が関節のそばにある歩ごとに全セルで呼ばれる）。
    /// </summary>
    private static Vector3 LocalIn(int l, Vector3 center, Vector3[]? offsets, Matrix4x4[]? transforms)
        => offsets is not null ? center + offsets[l] : Vector3.Transform(center, transforms![l]);

    /// <summary>層のどれかの格子で、点（層の番号 → その格子の座標 <see cref="LocalIn"/>）が埋まったセルの中か。</summary>
    private static bool FilledIn(IReadOnlyList<VoxelPart> layers, Vector3 center, Vector3[]? offsets, Matrix4x4[]? transforms)
    {
        for (var l = 0; l < layers.Count; l++)
        {
            if (layers[l].Body.Grid.FilledCellAt(LocalIn(l, center, offsets, transforms)) >= 0)
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>
    /// 層のどれかの格子で、点から <paramref name="reach"/> 以内にあるいちばん近い埋まったセルの（層の番号, セル）（無ければ (-1, -1)）。同じ近さなら先の層（部位そのもの）。
    /// </summary>
    private static (int Layer, int Cell) Nearest(IReadOnlyList<VoxelPart> layers, Vector3 center, Vector3[]? offsets, Matrix4x4[]? transforms, float reach)
    {
        var best = (Layer: -1, Cell: -1);
        var bestDistance = float.MaxValue;
        for (var l = 0; l < layers.Count; l++)
        {
            var (cell, distance) = NearestIn(layers[l].Body.Grid, LocalIn(l, center, offsets, transforms), reach);
            if (cell >= 0 && distance < bestDistance)
            {
                best = (l, cell);
                bestDistance = distance;
            }
        }

        return best;
    }

    /// <summary>格子 <paramref name="grid"/> の点 <paramref name="local"/> から <paramref name="reach"/> 以内にある、いちばん近い埋まったセルと距離の 2 乗（無ければ −1）。</summary>
    private static (int Cell, float Distance) NearestIn(VoxelGrid grid, Vector3 local, float reach)
    {
        var outside = Vector3.Max(Vector3.Abs(local) - grid.HalfExtents, Vector3.Zero);
        var reachSquared = reach * reach;
        if (outside.LengthSquared() > reachSquared)
        {
            return (-1, float.MaxValue);
        }

        var best = -1;
        var bestDistance = float.MaxValue;
        var n = (int)MathF.Ceiling(reach / MathF.Min(grid.CellSize.X, MathF.Min(grid.CellSize.Y, grid.CellSize.Z)));
        for (var dx = -n; dx <= n; dx++)
        {
            for (var dy = -n; dy <= n; dy++)
            {
                for (var dz = -n; dz <= n; dz++)
                {
                    var offset = new Vector3(dx * grid.CellSize.X, dy * grid.CellSize.Y, dz * grid.CellSize.Z);
                    var d = offset.LengthSquared();
                    if (d >= bestDistance || d > reachSquared)
                    {
                        continue;
                    }

                    var cell = grid.FilledCellAt(local + offset);
                    if (cell < 0)
                    {
                        continue;
                    }

                    best = cell;
                    bestDistance = d;
                }
            }
        }

        return (best, bestDistance);
    }
}
