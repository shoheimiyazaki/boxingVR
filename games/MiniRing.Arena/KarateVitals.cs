using System.Numerics;
using Aigis.Physics.Voxels;
using MiniRing.Voxels;

namespace MiniRing.Arena;

/// <summary>損傷の位置は接触記録、臓器欠損は材質の残数から測る。へこみのセル移動を破壊に数えない。</summary>
internal sealed class KarateVitals
{
    private readonly Fighter _victim;
    private readonly Dictionary<VoxelBody, Vector3> _centers = [];
    private readonly int _initialTotal;
    private readonly int[] _initialMaterials;
    private int _near;

    // 頭は幅 14 cm。頭は 3 セル、腹は 6 セルの半径で局所損傷を分ける。
    // 材料や打撃には使わない測定領域。初期の狙いを部位に固定し、動いても同じ場所を追う。
    internal KarateVitals(Fighter victim, AimGoal goal, Vector3 from)
    {
        _victim = victim;
        Radius = goal is AimGoal.SolarPlexus or AimGoal.Liver ? 0.0845f : 0.0423f;
        var point = victim.AsTarget.Point(goal, from)!.Value;
        var body = victim.Voxels!;
        _initialTotal = Total(body);
        _initialMaterials = Enumerable.Range(0, BoxerAnatomy.Pupil + 1).Select(i => Remaining(body, (byte)i)).ToArray();
        foreach (var part in body.Layers.Where(p => p.Kind is BodyPartKind.Head or BodyPartKind.Chest or BodyPartKind.Pelvis))
        {
            Matrix4x4.Invert(part.Body.GridToWorld(), out var inverse);
            _centers.Add(part.Body, Vector3.Transform(point, inverse));
        }
    }

    internal float Radius { get; }

    internal static int Total(BoxerBody body) => body.Layers.Concat(body.Fragments).Sum(p => p.Body.Grid.FilledCount);

    private static int Remaining(BoxerBody body, byte material) => body.Layers.Concat(body.Fragments).Sum(p => p.Body.Grid.Remaining(material));

    /// <summary>Step の直後に一度だけ呼ぶ。記録内の座標は移動・へこみ前の格子ローカル。</summary>
    internal void Observe(IEnumerable<VoxelContactResult> contacts)
    {
        foreach (var contact in contacts)
        {
            Count(contact, contact.A);
            Count(contact, contact.B);
        }
    }

    private void Count(VoxelContactResult contact, VoxelBody body)
    {
        if (_centers.TryGetValue(body, out var center))
        {
            _near += contact.RemovedFor(body).Concat(contact.CrushedFor(body))
                .Count(c => Vector3.Distance(c.LocalCenter, center) <= Radius);
        }
    }

    internal Reading Read()
    {
        var body = _victim.Voxels!;
        int Lost(byte material) => _initialMaterials[material] - Remaining(body, material);
        var total = _initialTotal - Total(body);
        return new Reading(total, _near, total - _near, Lost(BoxerAnatomy.Eye) + Lost(BoxerAnatomy.Pupil),
            Lost(BoxerAnatomy.Brain), BoxerBody.TrunkOrgans.Select(Lost).ToArray(),
            BoxerBody.TrunkOrgans.Select(body.BruiseOf).ToArray(), body.Vision,
            _victim.Consciousness, _victim.Stamina, _victim.Breath, _victim.Core);
    }

    internal sealed record Reading(int Total, int Near, int Outside, int Eye, int Brain, int[] Organs,
        float[] Bruises, VisionState Vision, float Consciousness, float Stamina, float Breath, float Core);
}
