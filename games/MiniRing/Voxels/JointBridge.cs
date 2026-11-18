using System.Numerics;
using Aigis.Foundation.Voxels;
using Aigis.Physics;
using Aigis.Physics.Voxels;

namespace MiniRing.Voxels;

/// <summary>
/// 関節のつなぎ（2026-11-19、PhysicsVersion 47）: つながった 2 つの部位（首なら胸と頭）のボクセルの格子は、関節が回ると間が開く（頭を打たれて傾く・
/// 肩が回る）。その隙間を刃が通っても何にも当たらず、関節の肉・骨を断てない。隙間に、2 つの部位の組織がつないでいる所を、当たりとダメージのためだけの
/// 小さな直方体（ボクセル 10 × 10 × 10）として置く。
/// <list type="bullet">
/// <item><b>体の動きは変えない</b>: 親の部位の剛体を借りるだけ（<see cref="VoxelBody.Massless"/>: 質量も剛体の形も持たない。関節の力も無い）。
/// 刃が触れたときの運動量の受け渡しは、普通の部位との接触と同じく親の剛体へ掛かる。</item>
/// <item><b>どのセルがつなぎか</b>（<see cref="Refresh"/>）: 関節のまわり 5 セル以内で、いまの姿勢で親にも子にも埋まったセルが無く（そこは部位が当たる）、
/// 親のセルも子のセルも 1 セル（約 5 mm）以内にある所。材質は、その 2 つの近いセルのうち強い方（骨が近ければ骨）。つなぎは毎歩、
/// いまの 2 つの部位の格子から作り直す（姿勢の記憶は持たない。部位のセルが削れれば、つなぎもそのぶん細る）。</item>
/// <item><b>断たれたら</b>（<see cref="Sever"/>）: つなぎのセルを刃の接触（<see cref="VoxelContact"/>。圧力と材質）が壊したら、そのセルがつないでいた
/// 親・子の側の近いセルを壊す。そのあとは普通の部位の接触の後始末（骨が断たれたら落ちる・臓器が壊れたら死ぬ）。</item>
/// </list>
/// 近道は <c>docs/DEBT.md</c>。
/// </summary>
internal sealed class JointBridge
{
    /// <summary>つなぎの一辺のセル数。</summary>
    private const int Cells = 10;

    /// <summary>隙間をつなぐ、親・子のセルまでの距離 [セル]。</summary>
    private const int Reach = 1;

    private readonly bool[] _cut;
    private readonly int[] _sourceParent;
    private readonly int[] _sourceChild;
    private readonly Vector3 _centerOnParent;
    private int _activeCells;

    public JointBridge(HumanoidRagdoll.RagdollJoint joint, VoxelPart parent, VoxelPart child, Vector3 anchorOnParent)
    {
        ArgumentNullException.ThrowIfNull(parent);
        ArgumentNullException.ThrowIfNull(child);
        Joint = joint;
        Parent = parent;
        Child = child;
        var grid = parent.Body.Grid;
        var cell = grid.CellSize;
        var half = grid.HalfExtents;
        // 中心は親の格子の線（セルの角）に合わせる: つなぎのセルの中心が、親のセルの中心と重なる。
        var local = anchorOnParent - parent.Body.GridCenterLocal;
        var snapped = new Vector3(
            (MathF.Round((local.X + half.X) / cell.X) * cell.X) - half.X,
            (MathF.Round((local.Y + half.Y) / cell.Y) * cell.Y) - half.Y,
            (MathF.Round((local.Z + half.Z) / cell.Z) * cell.Z) - half.Z);
        _centerOnParent = snapped + parent.Body.GridCenterLocal;
        var bridgeGrid = new VoxelGrid(grid.Palette, cell * (Cells * 0.5f), (Cells, Cells, Cells), static (_, _) => 0);
        Body = new VoxelBody(parent.Body.World, parent.Body.BodyId, bridgeGrid, _centerOnParent) { Massless = true };
        _cut = new bool[bridgeGrid.CellCount];
        _sourceParent = new int[bridgeGrid.CellCount];
        _sourceChild = new int[bridgeGrid.CellCount];
    }

    public HumanoidRagdoll.RagdollJoint Joint { get; }

    public VoxelPart Parent { get; }

    public VoxelPart Child { get; }

    /// <summary>接触の形。親の剛体を借りている（<see cref="VoxelBody.Massless"/>）。</summary>
    public VoxelBody Body { get; }

    /// <summary>いま刃が触れうるセルの数（<see cref="Refresh"/> の後）。</summary>
    public int ActiveCells => _activeCells;

    /// <summary>
    /// 武器 <paramref name="weapon"/>（歩の前の姿勢 <paramref name="before"/> から今の姿勢まで動く）の格子の箱が掃く所のセルだけを、<see cref="Refresh(Func{Vector3, bool})"/> で作り直す。
    /// 掃く所 = 歩の前と今の箱を、箱の角が動いた距離（と 1.2 cm）だけ広げた 2 つの箱。刃は細長いので、世界の軸に沿った箱より、触れうるセルがずっと少ない。
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

        var reach = half + new Vector3(moved + 0.012f);
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
        var childGrid = Child.Body.Grid;
        var grid = Body.Grid;
        var reach = Reach * MathF.Max(parentGrid.CellSize.X, MathF.Max(parentGrid.CellSize.Y, parentGrid.CellSize.Z));
        var toWorld = Body.GridToWorld();
        Matrix4x4.Invert(Child.Body.GridToWorld(), out var worldToChild);
        var toChild = toWorld * worldToChild;
        var offset = Body.GridCenterLocal - Parent.Body.GridCenterLocal; // 同じ剛体なので、つなぎの座標 → 親の格子の座標は平行移動だけ
        var active = 0;
        for (var z = 0; z < Cells; z++)
        {
            for (var y = 0; y < Cells; y++)
            {
                for (var x = 0; x < Cells; x++)
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

                    byte material = 0;
                    var (sp, sc) = (-1, -1);
                    var inParent = center + offset;
                    var inChild = Vector3.Transform(center, toChild);
                    if (parentGrid.FilledCellAt(inParent) < 0 && childGrid.FilledCellAt(inChild) < 0)
                    {
                        sp = Nearest(parentGrid, inParent, reach);
                        sc = Nearest(childGrid, inChild, reach);
                        if (sp >= 0 && sc >= 0)
                        {
                            var a = parentGrid.MaterialAt(sp);
                            var b = childGrid.MaterialAt(sc);
                            material = StrengthOf(parentGrid, a) >= StrengthOf(parentGrid, b) ? a : b;
                        }
                    }

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

    /// <summary>
    /// 刃の接触 <paramref name="hit"/> がつなぎのセルを壊したら、そのセルをつないでいた親・子のセルを壊す。壊した親の側・子の側のセルを返す
    /// （それぞれの部位の格子の座標）。
    /// </summary>
    public (List<RemovedVoxel> Parent, List<RemovedVoxel> Child) Sever(VoxelContactResult hit)
    {
        ArgumentNullException.ThrowIfNull(hit);
        var parent = new List<RemovedVoxel>();
        var child = new List<RemovedVoxel>();
        var grid = Body.Grid;
        foreach (var gone in hit.RemovedB)
        {
            var i = grid.CellAt(gone.LocalCenter);
            if (i < 0 || _cut[i])
            {
                continue;
            }

            _cut[i] = true;
            if (_sourceParent[i] >= 0)
            {
                Parent.Body.Grid.Break(_sourceParent[i], parent);
            }

            if (_sourceChild[i] >= 0)
            {
                Child.Body.Grid.Break(_sourceChild[i], child);
            }
        }

        return (parent, child);
    }

    private static float StrengthOf(VoxelGrid grid, byte material) => grid.Palette[material].MechanicsOrDefault.Strength;

    /// <summary>格子 <paramref name="grid"/> の点 <paramref name="local"/> から <paramref name="reach"/> 以内にある、いちばん近い埋まったセル（無ければ −1）。</summary>
    private static int Nearest(VoxelGrid grid, Vector3 local, float reach)
    {
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
                    if (d >= bestDistance || d > reach * reach)
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

        return best;
    }
}
