using System.Numerics;
using MiniRing.Voxels;

namespace MiniRing;

/// <summary>打撃の狙いどころ（戦略の AI が選ぶ。拳の経路そのものは技が決め、狙いの点だけがここで変わる）。</summary>
public enum AimGoal
{
    /// <summary>頭・胴の真ん中（従来どおり）。</summary>
    Center,

    /// <summary>あご（回転の中心から外れた点: 頭をねじって脳しんとうを起こす）。</summary>
    Chin,

    /// <summary>こめかみ（打つ手の側から見えている方）。</summary>
    Temple,

    /// <summary>頭蓋が削れて露出した脳（露出しているときだけ）。</summary>
    ExposedBrain,

    /// <summary>肝臓（肋骨の下: 腹の働きを落として膝を折る）。</summary>
    Liver,

    /// <summary>みぞおち（息を詰まらせる）。</summary>
    SolarPlexus,

    /// <summary>心臓（胸の左: 息の働きを落とす）。</summary>
    Heart,

    /// <summary>ガードしている前腕（腕を壊す: 削れの進んだ方を）。</summary>
    GuardArm,

    Eye,
    BlindSide,
    NeckSeam,
    TorsoSeam,
}

/// <summary>
/// 相手の体の、狙いどころの位置（ワールド）。戦略の AI が「どこを打つか」を決めるための知覚: 人が見て分かる情報
/// （あごやこめかみの位置、削れて見えている脳、ガードの腕の傷み具合、臓器のおおよその位置）だけを返す。
/// 露出した脳の位置は、頭の格子が変わったときだけ数え直す（毎歩は数えない）。
/// </summary>
public sealed class TargetMap(Fighter target)
{
    private readonly IReadOnlyDictionary<AimGoal, Vector3?>? _snapshot;
    public VisionState Vision => _snapshot is null ? target.Voxels?.Vision ?? VisionState.Healthy : _observedVision;
    private readonly VisionState _observedVision = VisionState.Healthy;

    private TargetMap(IReadOnlyDictionary<AimGoal, Vector3?> snapshot, VisionState vision) : this(null!)
    {
        _snapshot = snapshot;
        _observedVision = vision;
    }

    /// <summary>実体への参照を切った知覚の写し。細部と傷の知識は見える量に連続に縮める。</summary>
    public TargetMap Perceived(Vector3 actualHead, Vector3 seenHead, Vector3 from, float clarity)
        => new(Enum.GetValues<AimGoal>().ToDictionary(g => g, g => Point(g, from) is { } p
            ? (Vector3?)(seenHead + (p - actualHead) * clarity) : null),
            new VisionState(1f - (1f - Vision.Left) * clarity, 1f - (1f - Vision.Right) * clarity));
    private (int Head, int Face) _brainVersion = (-1, -1);
    private Vector3? _brainLocal;

    /// <summary>この狙いが今使えるか（露出した脳は露出しているときだけ、腕は付いているときだけ）。</summary>
    public bool Available(AimGoal goal) => _snapshot is not null ? _snapshot.GetValueOrDefault(goal) is not null || goal == AimGoal.Center : goal switch
    {
        AimGoal.ExposedBrain => ExposedBrainLocal() is not null,
        AimGoal.GuardArm => !target.ArmSevered(Hand.Left) || !target.ArmSevered(Hand.Right),
        AimGoal.Center => true,
        // 急所は、その体に実際のセルがあるときだけ狙える（潰れた目は狙えない。体の無いファイターには急所が無い）。
        _ => Point(goal, target.HeadPosition) is not null,
    };

    /// <summary>狙いの点（ワールド）。<paramref name="from"/> は打つ側の肩（こめかみ・腕は、その側から見えている方を選ぶ）。使えなければ null。</summary>
    public Vector3? Point(AimGoal goal, Vector3 from)
    {
        if (_snapshot is not null)
        {
            return _snapshot.GetValueOrDefault(goal);
        }
        var marks = target.Voxels?.Landmarks;
        switch (goal)
        {
            case AimGoal.Center:
                return null;
            case AimGoal.Eye:
                return BetterEye(marks, from);
            case AimGoal.BlindSide:
                // 見えにくい側のこめかみ（その側の横から見えない）。
                return marks?.Temple(Vision.Left < Vision.Right);
            case AimGoal.Chin:
                return marks?.Chin();
            case AimGoal.NeckSeam:
                return target.StructuralAim(Aigis.Physics.HumanoidRagdoll.RagdollJoint.Neck);
            case AimGoal.TorsoSeam:
                return target.StructuralAim(Aigis.Physics.HumanoidRagdoll.RagdollJoint.Spine);
            case AimGoal.Temple:
                var left = marks?.Temple(true);
                var right = marks?.Temple(false);
                return left is { } l && right is { } r
                    ? Vector3.DistanceSquared(l, from) < Vector3.DistanceSquared(r, from) ? l : r
                    : left ?? right;
            case AimGoal.ExposedBrain:
                return ExposedBrainLocal() is { } local && target.Voxels?.PartOf(target.Parts.Head) is { } part
                    ? Vector3.Transform(local, part.Body.GridToWorld())
                    : null;
            case AimGoal.Liver:
                return BodyLandmarks.OrganSkin(target.Voxels?.OrganLandmark(BoxerAnatomy.Liver), from);
            case AimGoal.SolarPlexus:
                return marks?.SolarPlexus();
            case AimGoal.Heart:
                return BodyLandmarks.OrganSkin(target.Voxels?.OrganLandmark(BoxerAnatomy.Heart), from);
            case AimGoal.GuardArm:
                // 傷んでいる方の腕（肉の残りが少ない方）を、同じなら打つ側に近い方を。
                Hand? best = null;
                var bestScore = float.MaxValue;
                foreach (var hand in new[] { Hand.Left, Hand.Right })
                {
                    if (target.ArmSevered(hand))
                    {
                        continue;
                    }

                    var forearm = target.World.GetState(target.ForearmId(hand)).Position;
                    var score = (target.Voxels?.Muscle(target.ForearmId(hand)) ?? 1f) + Vector3.Distance(forearm, from);
                    if (score < bestScore)
                    {
                        bestScore = score;
                        best = hand;
                    }
                }

                return best is { } arm ? target.World.GetState(target.ForearmId(arm)).Position : null;
            default:
                return null;
        }
    }

    /// <summary>
    /// 狙う目: よく見えている方（見えを奪う）。左右の見えが同じなら、打つ側（<paramref name="from"/>）から近い方（鼻を越えて遠い目へ手を伸ばさない）。
    /// 潰れた目（残りのセルが無い）は選ばない。
    /// </summary>
    private Vector3? BetterEye(BodyLandmarks? marks, Vector3 from)
    {
        var left = marks?.EyeCenter(true);
        var right = marks?.EyeCenter(false);
        if (left is not { } l || right is not { } r)
        {
            return left ?? right;
        }

        if (Vision.Left != Vision.Right)
        {
            return Vision.Left > Vision.Right ? l : r;
        }

        return Vector3.DistanceSquared(l, from) <= Vector3.DistanceSquared(r, from) ? l : r;
    }

    /// <summary>
    /// 頭の格子の、外に出ている脳のセルの重心（格子のローカル）。無ければ null。外に出ている = 隣の 5 mm のセルが頭の格子で空で、
    /// その上に重ねた顔の層（<see cref="BoxerBody.Face"/>）でもそこが全部空（頭皮・顔が削れて穴が開いた）。
    /// </summary>
    private Vector3? ExposedBrainLocal()
    {
        if (target.Voxels?.PartOf(target.Parts.Head) is not { } part)
        {
            return null;
        }

        var grid = part.Body.Grid;
        var face = target.Voxels.Face?.Body.Grid;
        var version = (grid.Version, face?.Version ?? 0);
        if (version == _brainVersion)
        {
            return _brainLocal;
        }

        _brainVersion = version;
        var (nx, ny, nz) = grid.Dimensions;
        var sum = Vector3.Zero;
        var count = 0;
        for (var z = 0; z < nz; z++)
        {
            for (var y = 0; y < ny; y++)
            {
                for (var x = 0; x < nx; x++)
                {
                    if (grid.At(x, y, z) == BoxerAnatomy.Brain && Open(grid, face, x, y, z))
                    {
                        sum += new Vector3(x + 0.5f, y + 0.5f, z + 0.5f);
                        count++;
                    }
                }
            }
        }

        _brainLocal = count > 0 ? (sum / count * grid.CellSize) - grid.HalfExtents : null;
        return _brainLocal;
    }

    /// <summary>頭の格子のセルが外気に面しているか: 6 方向のどれかが、頭の格子でも、重ねた顔の層（同じ箱、各軸 <see cref="FaceLayer.Subdivision"/> 倍）でも空。</summary>
    private static bool Open(Aigis.Foundation.Voxels.VoxelGrid grid, Aigis.Foundation.Voxels.VoxelGrid? face, int x, int y, int z)
    {
        var (nx, ny, nz) = grid.Dimensions;
        foreach (var (dx, dy, dz) in new[] { (1, 0, 0), (-1, 0, 0), (0, 1, 0), (0, -1, 0), (0, 0, 1), (0, 0, -1) })
        {
            var (ax, ay, az) = (x + dx, y + dy, z + dz);
            if (ax < 0 || ay < 0 || az < 0 || ax >= nx || ay >= ny || az >= nz)
            {
                return true;
            }

            if (grid.At(ax, ay, az) != 0)
            {
                continue;
            }

            if (face is null || FaceEmpty(face, ax, ay, az))
            {
                return true;
            }
        }

        return false;
    }

    private static bool FaceEmpty(Aigis.Foundation.Voxels.VoxelGrid face, int x, int y, int z)
    {
        const int n = FaceLayer.Subdivision;
        for (var dz = 0; dz < n; dz++)
        {
            for (var dy = 0; dy < n; dy++)
            {
                for (var dx = 0; dx < n; dx++)
                {
                    if (face.At((x * n) + dx, (y * n) + dy, (z * n) + dz) != 0)
                    {
                        return false;
                    }
                }
            }
        }

        return true;
    }
}
