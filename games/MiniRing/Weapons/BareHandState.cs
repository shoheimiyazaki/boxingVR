using System.Numerics;
using Aigis.Foundation.Voxels;
using MiniRing.Voxels;

namespace MiniRing.Weapons;

/// <summary>
/// 素手の組織の台帳: 親セル（<see cref="HandSkeleton.Parents"/>。どの指のどの節の骨・肉・皮膚か）ごとの材質・残り・ひび・染みを持つ。
/// 手の姿勢（関節の曲げ角。<see cref="HandPose"/>）を変えると、親セルは節の向きに沿って格子の別のセルへ置き直される（<see cref="Placed"/>）が、
/// 台帳は同じなので、ある姿勢で削れた節は、どの姿勢でも同じ節の欠けとして数えられ、効く。
/// </summary>
public sealed class BareHandState
{
    /// <summary>指の本数（人さし指・中指・薬指・小指。掌は別の部品）。</summary>
    public const int FingerCount = 4;

    private readonly (byte Material, float Health, bool Surface, float Stain)[] _cells;
    private readonly int[]? _placement;

    public BareHandState() : this(Fresh(), HandShape.Seiken, HandSkeleton.PoseOf(HandShape.Seiken), null)
    {
    }

    private BareHandState((byte Material, float Health, bool Surface, float Stain)[] cells, HandShape shape, HandPose pose, int[]? placement)
    {
        _cells = cells;
        Shape = shape;
        Pose = pose;
        _placement = placement;
    }

    /// <summary>指令された手形（形そのものは <see cref="Pose"/> の関節角で決まる）。</summary>
    public HandShape Shape { get; }

    /// <summary>いまセルを置いてある関節の姿勢。</summary>
    public HandPose Pose { get; }

    public IReadOnlyList<(byte Material, float Health, bool Surface, float Stain)> Cells => Array.AsReadOnly(_cells);

    /// <summary>親セルごとの、いまの格子でのセルの番号（置いたことが無ければ空）。</summary>
    internal IReadOnlyList<int> Placement => _placement ?? [];

    private static (byte Material, float Health, bool Surface, float Stain)[] Fresh()
        => [.. HandSkeleton.Parents.Select(p => (p.Material, BoxerAnatomy.Palette[p.Material].Toughness, p.Material == BoxerAnatomy.Skin, 0f))];

    /// <summary>無傷の素手を正拳に握った格子（武器の剛体を作るときの初期の形）。</summary>
    internal static VoxelGrid InitialGrid()
    {
        _ = new BareHandState().Placed(HandSkeleton.PoseOf(HandShape.Seiken), out var grid);
        return grid;
    }

    /// <summary>
    /// 節 <paramref name="segment"/>（掌は <paramref name="finger"/> = <see cref="FingerCount"/>）の親データ: 残っている骨・皮膚の割合と、ひび（1 − 骨の残り）。
    /// 骨は直列に力を伝えるので、節の強さ（<see cref="FingerPartState.BoneWeakest"/>）は骨のセルのうち最も傷んだものの残りで決まる。
    /// </summary>
    public FingerPartState Part(int finger, FingerSegment segment)
    {
        if (finger is < 0 or > FingerCount || (finger == FingerCount) != (segment == FingerSegment.Palm))
        {
            throw new ArgumentOutOfRangeException(nameof(finger));
        }

        float bone = 0f, boneFull = 0f, skin = 0f, skinFull = 0f, weakest = 1f;
        var cells = 0;
        for (var i = 0; i < _cells.Length; i++)
        {
            var parent = HandSkeleton.Parents[i];
            if (parent.Finger != finger || parent.Segment != segment)
            {
                continue;
            }

            var full = BoxerAnatomy.Palette[parent.Material].Toughness;
            var left = _cells[i].Material == 0 ? 0f : Math.Clamp(_cells[i].Health, 0f, full);
            if (parent.Material == BoxerAnatomy.Bone)
            {
                bone += left;
                boneFull += full;
                weakest = full > 0f ? MathF.Min(weakest, left / full) : weakest;
            }
            else if (parent.Material == BoxerAnatomy.Skin)
            {
                skin += left;
                skinFull += full;
            }

            cells++;
        }

        var boneLeft = boneFull > 0f ? bone / boneFull : 1f;
        return new FingerPartState(cells, boneLeft, skinFull > 0f ? skin / skinFull : 1f, 1f - boneLeft, weakest);
    }

    /// <summary>その指の骨の残り（0..1、節ごとの最小。どれかの節の骨が削られれば、その指の骨は弱い）。</summary>
    public float FingerBone(int finger) => Enumerable.Range(0, 3).Min(s => Part(finger, (FingerSegment)s).Bone);

    /// <summary>格子 <paramref name="grid"/>（この台帳から <see cref="Placed"/> で作ったもの）から、いまの親セルの状態を読み直す。</summary>
    public BareHandState Capture(VoxelGrid grid)
    {
        ArgumentNullException.ThrowIfNull(grid);
        if (_placement is null)
        {
            throw new InvalidOperationException("格子へ置いたことのない手の台帳は読み直せません。");
        }

        return new([.. _placement.Select(grid.CellState)], Shape, Pose, _placement);
    }

    /// <summary>同じ台帳・同じ置き場で、指令された手形だけ変える。</summary>
    public BareHandState WithShape(HandShape shape)
    {
        if (!Enum.IsDefined(shape))
        {
            throw new ArgumentOutOfRangeException(nameof(shape));
        }

        return new([.. _cells], shape, Pose, _placement);
    }

    /// <summary>
    /// 姿勢 <paramref name="pose"/> に親セルを置き直した格子を作る。親セルを関節の曲げ角どおりの位置（<see cref="HandSkeleton.Positions"/>）の
    /// セルへ置き、先に置いたセル（骨 → 肉 → 皮膚）が場所を取る。重なったセルは、いちばん近い空きセルへ押しのけられる（組織は減らない）。
    /// 削れて無くなった親セルの場所も取ったままにするので、置き場は傷に依らず姿勢だけで決まる。
    /// </summary>
    /// <returns>台帳を新しい置き場に付け替えたもの（<see cref="Pose"/> = <paramref name="pose"/>）。</returns>
    public BareHandState Placed(HandPose pose, out VoxelGrid grid)
    {
        ArgumentNullException.ThrowIfNull(pose);
        var positions = HandSkeleton.Positions(pose);
        const int Margin = 3;
        int Dim(Func<Vector3, float> axis) => 2 * ((int)MathF.Ceiling(positions.Max(p => MathF.Abs(axis(p))) / HandSkeleton.Cell) + Margin);
        var (nx, ny, nz) = (Dim(p => p.X), Dim(p => p.Y), Dim(p => p.Z));
        var half = new Vector3(nx, ny, nz) * (0.5f * HandSkeleton.Cell);
        var result = new VoxelGrid(BoxerAnatomy.Palette, half, (nx, ny, nz), (_, _) => 0);
        var taken = new bool[nx * ny * nz];
        var placement = new int[positions.Length];
        for (var i = 0; i < positions.Length; i++)
        {
            var f = (positions[i] + half) / HandSkeleton.Cell;
            var (cx, cy, cz) = ((int)MathF.Floor(f.X), (int)MathF.Floor(f.Y), (int)MathF.Floor(f.Z));
            placement[i] = Vacant(taken, (nx, ny, nz), (cx, cy, cz), f);
            taken[placement[i]] = true;
        }

        Reskin(placement, taken, (nx, ny, nz));
        for (var i = 0; i < positions.Length; i++)
        {
            if (_cells[i].Material != 0)
            {
                result.SetCellState(placement[i], _cells[i]);
            }
        }

        grid = result;
        return new BareHandState([.. _cells], Shape, pose, placement);
    }

    /// <summary>
    /// 皮膚が外を覆うように、置き場を入れ替える: 斜めに回した形や重なりで押しのけられた皮膚は、体の中に埋もれ、代わりに筋肉・骨が外に出る（赤・白のセル）。
    /// 外に出た筋肉と、いちばん近い埋もれた皮膚の置き場を入れ替える（組織の数は同じ、置き場の違いは数セル）。傷には依らない（親セルの元の材質で決める）。
    /// </summary>
    private static void Reskin(int[] placement, bool[] taken, (int X, int Y, int Z) size)
    {
        bool Exposed(int index)
        {
            var x = index % size.X;
            var y = index / size.X % size.Y;
            var z = index / (size.X * size.Y);
            foreach (var (dx, dy, dz) in Faces)
            {
                var (xx, yy, zz) = (x + dx, y + dy, z + dz);
                if (xx < 0 || yy < 0 || zz < 0 || xx >= size.X || yy >= size.Y || zz >= size.Z || !taken[xx + (size.X * (yy + (size.Y * zz)))])
                {
                    return true;
                }
            }

            return false;
        }

        float Distance(int a, int b)
        {
            var (ax, ay, az) = (a % size.X, a / size.X % size.Y, a / (size.X * size.Y));
            var (bx, by, bz) = (b % size.X, b / size.X % size.Y, b / (size.X * size.Y));
            return ((ax - bx) * (ax - bx)) + ((ay - by) * (ay - by)) + ((az - bz) * (az - bz));
        }

        for (var pass = 0; pass < 3; pass++)
        {
            var buried = Enumerable.Range(0, placement.Length)
                .Where(i => HandSkeleton.Parents[i].Material == BoxerAnatomy.Skin && !Exposed(placement[i])).ToList();
            var changed = false;
            for (var i = 0; i < placement.Length && buried.Count > 0; i++)
            {
                if (HandSkeleton.Parents[i].Material == BoxerAnatomy.Skin || !Exposed(placement[i]))
                {
                    continue;
                }

                var j = buried.MinBy(k => Distance(placement[k], placement[i]));
                // 骨の置き場は関節の角度が決めるので、骨は隣のセル（1 セル + 斜め）までしか動かさない。筋肉は 3 セルまで。
                if (Distance(placement[j], placement[i]) > (HandSkeleton.Parents[i].Material == BoxerAnatomy.Bone ? MaxBoneSwapCells2 : MaxSwapCells2))
                {
                    continue; // 遠すぎる入れ替えは形を崩す（骨が本来の場所から離れる）ので、その外に出た組織はそのまま
                }

                (placement[i], placement[j]) = (placement[j], placement[i]);
                buried.Remove(j);
                changed = true;
            }

            if (!changed)
            {
                break;
            }
        }
    }

    /// <summary>入れ替えてよい距離の上限（セルの数の 2 乗）: 筋肉は 3 セル = 6 mm、骨は 1 セル + 斜めまで。</summary>
    private const float MaxSwapCells2 = 9f;

    private const float MaxBoneSwapCells2 = 3f;

    private static readonly (int X, int Y, int Z)[] Faces = [(1, 0, 0), (-1, 0, 0), (0, 1, 0), (0, -1, 0), (0, 0, 1), (0, 0, -1)];

    /// <summary>近い順に並べた、まわりのセルへのずれ（距離 4 セルまで）。</summary>
    private static readonly (int X, int Y, int Z)[] Around = [.. Enumerable.Range(-4, 9).SelectMany(x => Enumerable.Range(-4, 9)
        .SelectMany(y => Enumerable.Range(-4, 9).Select(z => (X: x, Y: y, Z: z))))
        .OrderBy(o => (o.X * o.X) + (o.Y * o.Y) + (o.Z * o.Z)).ThenBy(o => o.X).ThenBy(o => o.Y).ThenBy(o => o.Z)];

    /// <summary>
    /// 場所 <paramref name="exact"/>（セルの単位の連続値）に最も近い空きセル。まず本来のセルを見て、塞がっていれば 1 セルまわりの空きのうち連続値に最も近いもの、
    /// それも無ければ近い順に外へ（斜めに回した形が穴だらけ・散らばりにならないよう、押しのけ先も本来の位置に近い所を選ぶ）。
    /// </summary>
    private static int Vacant(bool[] taken, (int X, int Y, int Z) size, (int X, int Y, int Z) at, Vector3 exact)
    {
        int? best = null;
        var bestDistance = float.MaxValue;
        var bestRing = int.MaxValue;
        foreach (var (dx, dy, dz) in Around)
        {
            var ring = (dx * dx) + (dy * dy) + (dz * dz);
            if (best is not null && ring > bestRing)
            {
                break;
            }

            var (x, y, z) = (at.X + dx, at.Y + dy, at.Z + dz);
            if (x < 0 || y < 0 || z < 0 || x >= size.X || y >= size.Y || z >= size.Z)
            {
                continue;
            }

            var index = x + (size.X * (y + (size.Y * z)));
            if (taken[index])
            {
                continue;
            }

            if (best is null)
            {
                // 最初に見つかった空きの距離の層 + 1 セルぶんの余裕まで、連続値に最も近いものを探す。
                bestRing = ring + 3;
            }

            var d = Vector3.DistanceSquared(new Vector3(x + 0.5f, y + 0.5f, z + 0.5f), exact);
            if (d < bestDistance)
            {
                (best, bestDistance) = (index, d);
            }
        }

        if (best is { } found)
        {
            return found;
        }

        // 近くに空きが無ければ、格子の中で最初の空き（番号順。決定的）。
        for (var index = 0; index < taken.Length; index++)
        {
            if (!taken[index])
            {
                return index;
            }
        }

        throw new InvalidOperationException("手のセルを置く空きがありません。");
    }
}

/// <summary>1 つの節の親データ: セル数・骨の残り・皮膚の残り（0..1）・ひび（1 − 骨の残り）・節の強さ（最も傷んだ骨のセルの残り、0..1）。</summary>
public readonly record struct FingerPartState(int Cells, float Bone, float Skin, float Cracked, float BoneWeakest);
