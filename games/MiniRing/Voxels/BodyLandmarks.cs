using System.Numerics;
using Aigis.Foundation.Voxels;

namespace MiniRing.Voxels;

/// <summary>
/// 急所の位置を、その体の実際のセルから求める（<see cref="TargetMap"/> の狙いの点の唯一の道）。頭の大きさの比で見積もった点は使わない:
/// 体格・顔の寸法（docs/CHARACTERS.md）・.vox で直した頭が変わっても、狙いは同じセルの並びに追従する。
/// <list type="bullet">
/// <item>目: いま残っている目・黒目のセルの中心（左右別）。潰れて無くなれば、その目は狙えない（外から見える物なので傷んだ姿に追従する）。</item>
/// <item>こめかみ: 目の高さの頭蓋の骨の、横の端のセル（その側の面を外へ辿った最も外の皮膚のセル）。</item>
/// <item>あご: 顔の前側の骨のうち、いちばん低いセル（顎の骨の先）の、前へ辿った最も外の皮膚のセル。</item>
/// <item>みぞおち: 胸骨（胸の前の真ん中の骨）の下の端のすぐ下の、前へ辿った最も外の皮膚のセル。</item>
/// <item>心臓・肝臓: 臓器のセルの重心（<see cref="BoxerBody.OrganCenter"/> と同じ）から、打つ側へ辿った最も外の皮膚のセル。
/// 内臓そのものは見えないので、「体の中のこの辺り」という知識（作ったときの置き場所。削れても動かさない）で、皮膚の上の点として狙う。</item>
/// </list>
/// 骨・臓器の置き場は作ったときの格子から 1 度だけ求める（毎歩数えない）。皮膚の面を辿る所だけ、いまの格子（削れた後の面）を見る。
/// </summary>
public sealed class BodyLandmarks
{
    private readonly Site?[] _temple = new Site?[2];
    private readonly Site? _chin;
    private readonly Site? _plexus;
    private readonly VoxelPart? _head;
    private readonly Dictionary<(int Version, int Side), Vector3?> _eyeLive = [];

    internal readonly record struct Site(VoxelPart Part, Vector3 Local);

    internal BodyLandmarks(IReadOnlyList<VoxelPart> parts)
    {
        _head = parts.FirstOrDefault(p => p.Kind == BodyPartKind.Head);
        var chest = parts.FirstOrDefault(p => p.Kind == BodyPartKind.Chest);
        if (_head is not null)
        {
            for (var side = 0; side < 2; side++)
            {
                _temple[side] = TempleSiteOf(_head, side);
            }

            _chin = ChinSiteOf(_head);
        }

        if (chest is not null)
        {
            _plexus = PlexusSiteOf(chest);
        }
    }

    /// <summary>左（+X。<c>VisionState.Left</c>）の目を 0、右を 1 とした、頭の格子での目のセル（無傷のとき）の重心。</summary>
    private static Site? EyeSiteOf(VoxelPart head, int side)
    {
        var cells = Cells(head.Body.Grid, (m, c) => m is BoxerAnatomy.Eye or BoxerAnatomy.Pupil && SideOf(c) == side);
        return cells.Count == 0 ? null : new Site(head, Mean(cells));
    }

    private static int SideOf(Vector3 center) => center.X > 0f ? 0 : 1;

    private static Site? TempleSiteOf(VoxelPart head, int side)
    {
        if (EyeSiteOf(head, side) is not { } eye)
        {
            return null;
        }

        var grid = head.Body.Grid;
        var cell = grid.CellSize;
        var skull = Cells(grid, (m, c) => m == BoxerAnatomy.Bone && SideOf(c) == side && MathF.Abs(c.Y - eye.Local.Y) <= cell.Y);
        if (skull.Count == 0)
        {
            return null;
        }

        var outermost = skull.Max(c => MathF.Abs(c.X));
        var edge = skull.Where(c => MathF.Abs(c.X) >= outermost - 1e-6f).ToList();
        return new Site(head, new Vector3(edge[0].X, eye.Local.Y, Mean(edge).Z));
    }

    /// <summary>顔の前側（骨の前後の幅の前 4 分の 1）の骨のうち、いちばん低い 1 セル分の層の重心（顎の骨の先）。</summary>
    private static Site? ChinSiteOf(VoxelPart head)
    {
        var grid = head.Body.Grid;
        var bone = Cells(grid, (m, _) => m == BoxerAnatomy.Bone);
        if (bone.Count == 0)
        {
            return null;
        }

        var zMax = bone.Max(c => c.Z);
        var zMin = bone.Min(c => c.Z);
        var front = bone.Where(c => c.Z >= zMax - (0.25f * (zMax - zMin))).ToList();
        var low = front.Min(c => c.Y);
        return new Site(head, Mean(front.Where(c => c.Y <= low + (0.5f * grid.CellSize.Y)).ToList()));
    }

    /// <summary>胸骨（胸の前の真ん中の骨）のいちばん下のセルの、1 セル下（みぞおち）。</summary>
    private static Site? PlexusSiteOf(VoxelPart chest)
    {
        var grid = chest.Body.Grid;
        var bone = Cells(grid, (m, _) => m == BoxerAnatomy.Bone);
        if (bone.Count == 0)
        {
            return null;
        }

        var zMax = bone.Max(c => c.Z);
        var cell = grid.CellSize;
        var sternum = bone.Where(c => c.Z >= zMax - (0.5f * cell.Z) && MathF.Abs(c.X) <= 1.5f * cell.X).ToList();
        if (sternum.Count == 0)
        {
            return null;
        }

        var low = sternum.Min(c => c.Y);
        var mean = Mean(sternum.Where(c => c.Y <= low + (0.5f * cell.Y)).ToList());
        return new Site(chest, new Vector3(mean.X, low - cell.Y, mean.Z));
    }

    private static List<Vector3> Cells(VoxelGrid grid, Func<byte, Vector3, bool> keep)
    {
        var (nx, ny, nz) = grid.Dimensions;
        var found = new List<Vector3>();
        for (var z = 0; z < nz; z++)
        {
            for (var y = 0; y < ny; y++)
            {
                for (var x = 0; x < nx; x++)
                {
                    var m = grid.At(x, y, z);
                    if (m != 0)
                    {
                        var c = grid.CellCenter(x, y, z);
                        if (keep(m, c))
                        {
                            found.Add(c);
                        }
                    }
                }
            }
        }

        return found;
    }

    private static Vector3 Mean(List<Vector3> points) => points.Aggregate(Vector3.Zero, (a, b) => a + b) / points.Count;

    /// <summary>目（<paramref name="left"/> = +X 側）の、いま残っているセルの重心（ワールド）。残っていなければ null（潰れた目は狙えない）。</summary>
    public Vector3? EyeCenter(bool left)
    {
        if (_head is null)
        {
            return null;
        }

        var side = left ? 0 : 1;
        var grid = _head.Body.Grid;
        if (!_eyeLive.TryGetValue((grid.Version, side), out var local))
        {
            if (_eyeLive.Keys.Any(k => k.Version != grid.Version))
            {
                _eyeLive.Clear();
            }

            var cells = Cells(grid, (m, c) => m is BoxerAnatomy.Eye or BoxerAnatomy.Pupil && SideOf(c) == side);
            local = cells.Count == 0 ? null : Mean(cells);
            _eyeLive[(grid.Version, side)] = local;
        }

        return local is { } p ? Vector3.Transform(p, _head.Body.GridToWorld()) : null;
    }

    /// <summary>こめかみ（<paramref name="left"/> = +X 側）の、皮膚の上の点（ワールド）。</summary>
    public Vector3? Temple(bool left)
        => _temple[left ? 0 : 1] is { } site ? Surface(site, new Vector3(left ? 1f : -1f, 0f, 0f)) : null;

    /// <summary>あごの先の、皮膚の上の点（ワールド）。</summary>
    public Vector3? Chin() => _chin is { } site ? Surface(site, Vector3.UnitZ) : null;

    /// <summary>みぞおち（胸骨の下の端のすぐ下）の、皮膚の上の点（ワールド）。</summary>
    public Vector3? SolarPlexus() => _plexus is { } site ? Surface(site, Vector3.UnitZ) : null;

    /// <summary>
    /// 臓器 <paramref name="organ"/> のある辺りの、皮膚の上の点（ワールド）: 臓器の重心から打つ側（<paramref name="from"/>）へ向かって辿った、
    /// いちばん外の体のセル。その臓器が無ければ null。
    /// </summary>
    internal static Vector3? OrganSkin(Site? organ, Vector3 from)
    {
        if (organ is not { } site)
        {
            return null;
        }

        Matrix4x4.Invert(site.Part.Body.GridToWorld(), out var inverse);
        var direction = Vector3.Transform(from, inverse) - site.Local;
        return direction.LengthSquared() < 1e-10f ? null : Surface(site, Vector3.Normalize(direction));
    }

    /// <summary>格子のローカルの点から <paramref name="direction"/>（格子のローカル）へ辿って、体を出る直前の（いちばん外の）セルの中心をワールドで返す。</summary>
    private static Vector3 Surface(Site site, Vector3 direction)
    {
        var grid = site.Part.Body.Grid;
        var step = 0.5f * MathF.Min(grid.CellSize.X, MathF.Min(grid.CellSize.Y, grid.CellSize.Z));
        var count = (int)MathF.Ceiling(2f * grid.HalfExtents.Length() / step);
        var last = site.Local;
        var inside = false;
        for (var i = 0; i <= count; i++)
        {
            var cell = grid.CellAt(site.Local + (direction * (step * i)));
            if (cell < 0)
            {
                break;
            }

            if (grid.MaterialAt(cell) != 0)
            {
                var (x, y, z) = grid.Coordinates(cell);
                last = grid.CellCenter(x, y, z);
                inside = true;
            }
            else if (inside)
            {
                break;
            }
        }

        return Vector3.Transform(last, site.Part.Body.GridToWorld());
    }
}
