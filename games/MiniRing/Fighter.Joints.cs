using System.Numerics;
using Aigis.Physics;

namespace MiniRing;

public sealed partial class Fighter
{
    private bool IsThoraxWeld(HumanoidRagdoll.JointGroup joint) =>
        Build.FourLegged && joint.Joint == HumanoidRagdoll.RagdollJoint.Spine && joint.ChildPart == Parts.Pelvis;

    private Vector3? StructuralAnchor(HumanoidRagdoll.JointGroup joint)
    {
        if (IsThoraxWeld(joint) && joint.ParentPart == Parts.Chest && _world.HasConstraint(joint.PrimaryConstraintId))
        {
            var chest = _world.GetState(Parts.Chest);
            var seam = Vector3.UnitY * (-HalfExtents.Chest.Y + 0.5f * _tuning.JointOverlap);
            return chest.Position + Vector3.Transform(seam, chest.Orientation);
        }
        return _world.GetJointAnchors(joint.PrimaryConstraintId)?.AnchorA;
    }

    internal Vector3? StructuralAim(HumanoidRagdoll.RagdollJoint kind)
    {
        foreach (var joint in _joints)
        {
            if (joint.Joint == kind && joint.ParentPart >= 0 && Attached(joint.ParentPart, joint.ChildPart))
            {
                return StructuralAnchor(joint);
            }
        }
        return null;
    }

    /// <summary>
    /// 関節のつなぎ（<see cref="Voxels.JointBridge"/>）を作る材料: いまの関節の種類・親の部位・子の部位と、関節の中心（親の剛体の座標）。
    /// 体を作った直後（まだ何も切れていない）に呼ぶ。関節の種類で選ばない（足首も同じ）。
    /// </summary>
    internal IReadOnlyList<(HumanoidRagdoll.RagdollJoint Joint, int Parent, int Child, Vector3 AnchorOnParent)> BridgeJoints()
    {
        var result = new List<(HumanoidRagdoll.RagdollJoint, int, int, Vector3)>();
        foreach (var joint in _joints)
        {
            if (StructuralAnchor(joint) is not { } anchor)
            {
                continue;
            }

            var parent = _world.GetState(joint.ParentPart);
            result.Add((joint.Joint, joint.ParentPart, joint.ChildPart, Vector3.Transform(anchor - parent.Position, Quaternion.Conjugate(parent.Orientation))));
        }

        return result;
    }

    /// <summary>
    /// 親の部位 <paramref name="parent"/> と子の部位 <paramref name="child"/> が、いまも作ったときの関節でつながっているか
    /// （折れた・切れて付け替えられた・体から外れたなら false: そのつなぎはもう無い）。
    /// </summary>
    internal bool JointIntact(int parent, int child) =>
        !_detached.Contains(parent) && !_detached.Contains(child) && _joints.Exists(j => j.ParentPart == parent && j.ChildPart == child);
}
