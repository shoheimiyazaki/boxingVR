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
    private int _brainVersion = -1;
    private Vector3? _brainLocal;

    /// <summary>この狙いが今使えるか（露出した脳は露出しているときだけ、腕は付いているときだけ）。</summary>
    public bool Available(AimGoal goal) => _snapshot is not null ? _snapshot.GetValueOrDefault(goal) is not null || goal == AimGoal.Center : goal switch
    {
        AimGoal.ExposedBrain => ExposedBrainLocal() is not null,
        AimGoal.GuardArm => !target.ArmSevered(Hand.Left) || !target.ArmSevered(Hand.Right),
        _ => true,
    };

    /// <summary>狙いの点（ワールド）。<paramref name="from"/> は打つ側の肩（こめかみ・腕は、その側から見えている方を選ぶ）。使えなければ null。</summary>
    public Vector3? Point(AimGoal goal, Vector3 from)
    {
        if (_snapshot is not null)
        {
            return _snapshot.GetValueOrDefault(goal);
        }
        var head = target.World.GetState(target.Parts.Head);
        var hh = target.HalfExtents.Head;
        var chest = target.World.GetState(target.Parts.Chest);
        var hc = target.HalfExtents.Chest;
        Vector3 HeadAt(Vector3 local) => head.Position + Vector3.Transform(local, head.Orientation);
        Vector3 ChestAt(Vector3 local) => chest.Position + Vector3.Transform(local, chest.Orientation);
        switch (goal)
        {
            case AimGoal.Center:
                return null;
            case AimGoal.Eye:
                var eyeSide = Vision.Left >= Vision.Right ? 1f : -1f;
                return HeadAt(new Vector3(eyeSide * 0.38f * hh.X, 0.18f * hh.Y, 0.9f * hh.Z));
            case AimGoal.BlindSide:
                return HeadAt(new Vector3((Vision.Left < Vision.Right ? 1f : -1f) * hh.X * 0.8f, 0f, hh.Z * 0.5f));
            case AimGoal.Chin:
                return HeadAt(new Vector3(0f, -0.75f * hh.Y, 0.85f * hh.Z));
            case AimGoal.Temple:
                var left = HeadAt(new Vector3(-hh.X, 0.25f * hh.Y, 0.3f * hh.Z));
                var right = HeadAt(new Vector3(hh.X, 0.25f * hh.Y, 0.3f * hh.Z));
                return Vector3.DistanceSquared(left, from) < Vector3.DistanceSquared(right, from) ? left : right;
            case AimGoal.ExposedBrain:
                return ExposedBrainLocal() is { } local && target.Voxels?.PartOf(target.Parts.Head) is { } part
                    ? Vector3.Transform(local, part.Body.GridToWorld())
                    : null;
            case AimGoal.Liver:
                return target.Voxels?.OrganCenter(BoxerAnatomy.Liver) ?? ChestAt(new Vector3(-0.5f * hc.X, -0.8f * hc.Y, hc.Z));
            case AimGoal.SolarPlexus:
                return ChestAt(new Vector3(0f, -0.55f * hc.Y, hc.Z));
            case AimGoal.Heart:
                return target.Voxels?.OrganCenter(BoxerAnatomy.Heart) ?? ChestAt(new Vector3(0.3f * hc.X, 0.2f * hc.Y, hc.Z));
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

    /// <summary>頭の格子の、外に出ている（空のセルと接する）脳のセルの重心（格子のローカル）。無ければ null。</summary>
    private Vector3? ExposedBrainLocal()
    {
        if (target.Voxels?.PartOf(target.Parts.Head) is not { } part)
        {
            return null;
        }

        var grid = part.Body.Grid;
        if (grid.Version == _brainVersion)
        {
            return _brainLocal;
        }

        _brainVersion = grid.Version;
        var (nx, ny, nz) = grid.Dimensions;
        var sum = Vector3.Zero;
        var count = 0;
        for (var z = 0; z < nz; z++)
        {
            for (var y = 0; y < ny; y++)
            {
                for (var x = 0; x < nx; x++)
                {
                    if (grid.At(x, y, z) == BoxerAnatomy.Brain && grid.IsExposed(x, y, z))
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
}
