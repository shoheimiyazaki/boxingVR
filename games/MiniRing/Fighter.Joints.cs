using System.Numerics;
using Aigis.Physics;

namespace MiniRing;

public sealed partial class Fighter
{
    /// <summary>
    /// 関節のつなぎ（<see cref="Voxels.JointBridge"/>）を作る材料: いまの関節の種類・親の部位・子の部位と、関節の中心（親の剛体の座標）。
    /// 体を作った直後（まだ何も切れていない）に呼ぶ。関節の種類で選ばない（足首も同じ）。
    /// </summary>
    internal IReadOnlyList<(HumanoidRagdoll.RagdollJoint Joint, int Parent, int Child, Vector3 AnchorOnParent)> BridgeJoints()
    {
        var result = new List<(HumanoidRagdoll.RagdollJoint, int, int, Vector3)>();
        foreach (var joint in _joints)
        {
            if (_world.GetJointAnchors(joint.PrimaryConstraintId)?.AnchorA is not { } anchor)
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
