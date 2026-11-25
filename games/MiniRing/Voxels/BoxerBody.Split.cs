using System.Numerics;
using Aigis.Foundation.Voxels;
using Aigis.Physics.Voxels;
using Aigis.Render.Voxels;

namespace MiniRing.Voxels;

/// <summary>
/// 見えているボクセルのつながりで体を分ける（docs/VOXEL_DAMAGE.md「つながりで分かれる体」）: 1 つの部位の格子の中で物質がつながっていない
/// （面で接するセルの連結成分が 2 つ以上）なら、それぞれ別の剛体。2 つの部位の間は、作ったときに関節で接していたセルの組が 1 つも残って
/// いなければ、関節でつながっていない。どちらも部位の種類では分けない（腕・脚の骨の断ち・折れ <see cref="BreakIfSevered"/> は同じ体を
/// 先に梁として分ける道で、そちらで分かれなかった残りをここが見る）。
/// </summary>
public sealed partial class BoxerBody
{
    private readonly float _contactSlack;
    private readonly int _looseCellLimit;

    /// <summary>つながりで分かれた破片（骨の断ちで落ちた破片と違い、元の部位の治りを止めない: <see cref="Regenerate"/>）。</summary>
    private readonly HashSet<VoxelPart> _splitPieces = new(ReferenceEqualityComparer.Instance);

    /// <summary>作ったときに関節で接していた親と子のセルの組（<see cref="JointHeld"/>）。キーは (親の剛体, 子の剛体)。</summary>
    private readonly Dictionary<(int Parent, int Child), ContactPair[]> _jointContacts = [];

    /// <summary>関節で接していた 2 つのセル: どの格子（層）の、どのセルか。</summary>
    private readonly record struct ContactPair(VoxelBody ParentLayer, int ParentCell, VoxelBody ChildLayer, int ChildCell)
    {
        public bool Held => ParentLayer.Grid.MaterialAt(ParentCell) != 0 && ChildLayer.Grid.MaterialAt(ChildCell) != 0;
    }

    /// <summary>
    /// 関節（親 <paramref name="parent"/>・子 <paramref name="child"/>、関節の中心は親の剛体の座標で <paramref name="anchorOnParent"/>）で、
    /// 作ったときに接していた親と子のセルの組を拾う。関節の中心から、細い方の部位の断面の幅（<see cref="JointBridge.SizeFor"/> と同じ幅）以内の
    /// 子のセルだけを見る（腕が胴の脇に沿って触れている所は、関節のつなぎではない）。顔の層のように同じ剛体に重ねた層も、その部位の物質として数える。
    /// </summary>
    private void AddJointContact(VoxelPart parent, VoxelPart child, Vector3 anchorOnParent)
    {
        var parentLayers = LayersOf(parent);
        var childLayers = LayersOf(child);
        var anchor = Vector3.Transform(anchorOnParent - parent.Body.GridCenterLocal, parent.Body.GridToWorld());
        var reach = MathF.Min(CrossWidth(parent.Body.Grid), CrossWidth(child.Body.Grid));
        var pairs = new List<ContactPair>();
        foreach (var childLayer in childLayers)
        {
            var cg = childLayer.Body.Grid;
            var cCell = MaxCell(cg);
            var toWorld = childLayer.Body.GridToWorld();
            var parentFrames = parentLayers.Select(p =>
            {
                Matrix4x4.Invert(p.Body.GridToWorld(), out var inverse);
                return (Layer: p, ToWorld: p.Body.GridToWorld(), ToLocal: inverse);
            }).ToArray();
            for (var i = 0; i < cg.CellCount; i++)
            {
                if (cg.MaterialAt(i) == 0)
                {
                    continue;
                }

                var (x, y, z) = cg.Coordinates(i);
                var at = Vector3.Transform(cg.CellCenter(x, y, z), toWorld);
                if (Vector3.Distance(at, anchor) > reach)
                {
                    continue;
                }

                foreach (var (layer, parentToWorld, toLocal) in parentFrames)
                {
                    var pg = layer.Body.Grid;
                    var pCell = MaxCell(pg);
                    var touch = (0.5f * (pCell + cCell)) + (_contactSlack * MathF.Max(pCell, cCell));
                    var local = Vector3.Transform(at, toLocal);
                    var span = (int)MathF.Ceiling(touch / MathF.Min(pg.CellSize.X, MathF.Min(pg.CellSize.Y, pg.CellSize.Z)));
                    var c = (local + pg.HalfExtents) / pg.CellSize;
                    var (cx, cy, cz) = ((int)MathF.Floor(c.X), (int)MathF.Floor(c.Y), (int)MathF.Floor(c.Z));
                    var (nx, ny, nz) = pg.Dimensions;
                    for (var dz = -span; dz <= span; dz++)
                    {
                        for (var dy = -span; dy <= span; dy++)
                        {
                            for (var dx = -span; dx <= span; dx++)
                            {
                                var (px, py, pz) = (cx + dx, cy + dy, cz + dz);
                                if (px < 0 || py < 0 || pz < 0 || px >= nx || py >= ny || pz >= nz)
                                {
                                    continue;
                                }

                                var j = pg.Index(px, py, pz);
                                if (pg.MaterialAt(j) != 0 && Vector3.Distance(Vector3.Transform(pg.CellCenter(px, py, pz), parentToWorld), at) <= touch)
                                {
                                    pairs.Add(new ContactPair(layer.Body, j, childLayer.Body, i));
                                }
                            }
                        }
                    }
                }
            }
        }

        _jointContacts[(parent.Body.BodyId, child.Body.BodyId)] = [.. pairs];
    }

    /// <summary>
    /// 関節（親 <paramref name="parent"/>・子 <paramref name="child"/>）を作り直した（生え直した芽をつないだ: <see cref="Fighter.Rebud"/>）: 接しているセルの組を、
    /// いまの両側の格子から拾い直す（作ったときの組は、移って空になった古いセルを指している）。
    /// </summary>
    internal void RebuildJointContact(int parent, int child, Vector3 anchorOnParent)
    {
        if (_byBody.TryGetValue(parent, out var p) && _byBody.TryGetValue(child, out var c))
        {
            AddJointContact(p, c, anchorOnParent);
        }
    }

    private static float MaxCell(VoxelGrid grid) => MathF.Max(grid.CellSize.X, MathF.Max(grid.CellSize.Y, grid.CellSize.Z));

    /// <summary>部位の格子の断面の幅 [m]（箱の体積 ÷ いちばん長い辺 の平方根。<see cref="JointBridge.SizeFor"/> と同じ）。</summary>
    private static float CrossWidth(VoxelGrid grid)
    {
        var h = grid.HalfExtents;
        return 2f * MathF.Sqrt(h.X * h.Y * h.Z / MathF.Max(h.X, MathF.Max(h.Y, h.Z)));
    }

    /// <summary>
    /// 親の部位 <paramref name="parent"/> と子の部位 <paramref name="child"/> が、関節でまだ物質によってつながっているか: 作ったときに接していた
    /// セルの組のうち、両方が残っている組が 1 つでもあれば true。刃が関節の面をまるごと断ち切った（どの組も片方が無い）なら false。
    /// 知らない関節・作ったときに接するセルが無かった関節は true（物質のつながりを数えられない関節は、ここでは分けない）。
    /// </summary>
    public bool JointHeld(int parent, int child)
    {
        if (!_jointContacts.TryGetValue((parent, child), out var pairs) || pairs.Length == 0)
        {
            return true;
        }

        foreach (var pair in pairs)
        {
            if (pair.Held)
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>関節で作ったときに接していたセルの組の数と、そのうち両方が残っている数（調べもの・テスト用）。</summary>
    public (int Total, int Held) JointContactCount(int parent, int child)
        => _jointContacts.TryGetValue((parent, child), out var pairs) ? (pairs.Length, pairs.Count(p => p.Held)) : (0, 0);

    /// <summary>
    /// 部位（または破片）<paramref name="part"/> の格子のセルが、面で接するつながりで 2 つ以上に分かれていれば、それぞれを別の剛体に分ける。
    /// 残すのは <paramref name="keepNearWorld"/>（ワールド。関節の中心など、体の頭の側へつながる所）にいちばん近いセルを持つつながり
    /// （null ならいちばん大きいつながり）。ほかのつながりは、それぞれ新しい破片（<see cref="Fragments"/>）になる: 剛体の質量はセルの数に
    /// 比例し（分ける前と後の和は同じ）、速度は分ける前の剛体のその点の速度（<see cref="VoxelBody.SplitOff"/>）。脊髄のセルは移った先の格子へ
    /// 付け替える。重ねた層（顔の層）は剛体を借りているだけなので分けない。分かれた破片を返す（分かれなければ空）。
    /// </summary>
    public IReadOnlyList<VoxelPart> SplitDisconnected(VoxelPart part, Vector3? keepNearWorld)
    {
        ArgumentNullException.ThrowIfNull(part);
        var body = part.Body;
        var grid = body.Grid;
        if (body.Massless || body.Owner is not null || grid.IsConnected())
        {
            return [];
        }

        var components = grid.ConnectedComponents();
        if (components.Count < 2)
        {
            return [];
        }

        var toWorld = body.GridToWorld();
        var keep = 0;
        var best = keepNearWorld is null ? -1f : float.MaxValue;
        for (var c = 0; c < components.Count; c++)
        {
            if (keepNearWorld is { } point)
            {
                foreach (var i in components[c])
                {
                    var (x, y, z) = grid.Coordinates(i);
                    var d = Vector3.DistanceSquared(Vector3.Transform(grid.CellCenter(x, y, z), toWorld), point);
                    if (d < best)
                    {
                        (best, keep) = (d, c);
                    }
                }
            }
            else if (components[c].Length > best)
            {
                (best, keep) = (components[c].Length, c);
            }
        }

        // 移る脊髄のセル（分ける前のワールドの位置を覚えておく）。
        var cords = new Dictionary<int, CordCell>();
        foreach (var cordCell in _cordSlices.Values.SelectMany(s => s).SelectMany(s => s))
        {
            if (cordCell.Part == part && cordCell.Index >= 0)
            {
                cords[cordCell.Index] = cordCell;
            }
        }

        var origin = part.FragmentOf ?? body.BodyId;
        var made = new List<VoxelPart>(components.Count - 1);
        for (var c = 0; c < components.Count; c++)
        {
            if (c == keep)
            {
                continue;
            }

            if (components[c].Length < _looseCellLimit)
            {
                // 削れて飛ぶ破片ほどの小さな欠片: 剛体にせず、削れたセルと同じに落とす（臓器のセルなら失った分として数える）。
                var lost = new List<RemovedVoxel>();
                foreach (var i in components[c])
                {
                    grid.Break(i, lost);
                }

                body.UpdateMass();
                continue;
            }

            var moving = new List<(CordCell Cell, Vector3 World)>();
            foreach (var i in components[c])
            {
                if (cords.TryGetValue(i, out var cordCell))
                {
                    var (x, y, z) = grid.Coordinates(i);
                    moving.Add((cordCell, Vector3.Transform(grid.CellCenter(x, y, z), toWorld)));
                }
            }

            if (body.SplitOff(components[c]) is not { } piece)
            {
                continue;
            }

            // 分かれて出て行った物質は、元の部位の形からも外す: 治る・生える（Regenerate・Regrow）ときに元の格子へ同じ物質を作り直さない。
            if (_originals.TryGetValue(part, out var original))
            {
                foreach (var i in components[c])
                {
                    original.Materials[i] = 0;
                    original.Surface[i] = false;
                }
            }

            var fragment = new VoxelPart(part.Kind, piece, VoxelVolume.For(piece.Grid), null, part.DistalHigh, origin);
            _fragments.Add(fragment);
            _splitPieces.Add(fragment);
            made.Add(fragment);
            Matrix4x4.Invert(piece.GridToWorld(), out var toPiece);
            foreach (var (cordCell, world) in moving)
            {
                cordCell.Part = fragment;
                cordCell.Index = piece.Grid.CellAt(Vector3.Transform(world, toPiece));
            }
        }

        return made;
    }
}
