using System.Numerics;
using Aigis.Physics;
using Aigis.Physics.Voxels;
using MiniRing.Voxels;

namespace MiniRing;

/// <summary>
/// 見えているボクセルのつながりで体を分ける（docs/VOXEL_DAMAGE.md「つながりで分かれる体」）。部位の格子の中で物質がつながっていなければ
/// 別の剛体に分け（<see cref="BoxerBody.SplitDisconnected"/>）、関節の面の物質がまるごと断たれていれば関節を外す（<see cref="BoxerBody.JointHeld"/>）。
/// どちらも部位の種類で分けない。分かれた後、体として駆動するのは頭とつながった側だけ: 関節の木の根（胸）は頭の側のつながりを残し、
/// 頭へつながらなくなった部位は体から外れる（<see cref="_detached"/>: 筋の力・支え・押す力を受けない、力の抜けた物）。
/// </summary>
public sealed partial class Fighter
{
    /// <summary>つながりを調べ終えた格子の形の版（変わっていない格子は調べ直さない）。</summary>
    private readonly Dictionary<VoxelBody, int> _connectedAt = [];

    private readonly List<VoxelPart> _splitScratch = [];

    /// <summary>
    /// この歩で削れた部位（と破片）のつながりを調べ、分かれた物質を別の剛体にし、関節の面が断たれた関節を外す。打撃・武器・蹴りの後始末の後に、
    /// 毎歩呼ぶ（<see cref="BoxingGame"/>）。体が分かれたら true。
    /// </summary>
    internal bool SplitDisconnected()
    {
        if (Voxels is not { } voxels)
        {
            return false;
        }

        var changed = false;
        _splitScratch.Clear();
        _splitScratch.AddRange(voxels.Parts);
        _splitScratch.AddRange(voxels.Fragments);
        foreach (var part in _splitScratch)
        {
            var body = part.Body;
            if (_connectedAt.TryGetValue(body, out var version) && version == body.Grid.GeometryVersion)
            {
                continue;
            }

            var pieces = voxels.SplitDisconnected(part, HeadwardAnchor(body.BodyId));
            _connectedAt[body] = body.Grid.GeometryVersion;
            foreach (var piece in pieces)
            {
                _connectedAt[piece.Body] = piece.Body.Grid.GeometryVersion;
            }

            if (pieces.Count > 0)
            {
                CarryOver(body.BodyId, pieces);
                changed = true;
            }
        }

        for (var i = 0; i < _joints.Count; i++)
        {
            var joint = _joints[i];
            if (joint.ParentPart >= 0 && !voxels.JointHeld(joint.ParentPart, joint.ChildPart))
            {
                // 関節の面の物質（作ったときに接していた親と子のセル）がまるごと断たれた: 体はそこでつながっていない。
                var parent = joint.ParentPart;
                MoveJoint(i, null);
                SeparateCollisions(parent, null);
                changed = true;
            }
        }

        if (changed)
        {
            AfterDetaching();
        }

        return changed;
    }

    /// <summary>
    /// 部位 <paramref name="bodyId"/> の、体の頭の側へつながる所（ワールド）: 親の関節の中心（関節の木の根の胸は、頭へつながる首の関節）。
    /// 分かれたとき、ここにいちばん近いつながりが元の剛体として残る。関節を持たない破片は null（いちばん大きいつながりが残る）。
    /// </summary>
    private Vector3? HeadwardAnchor(int bodyId)
    {
        foreach (var joint in _joints)
        {
            var toward = joint.ChildPart == bodyId && joint.ParentPart >= 0
                || (joint.ParentPart == bodyId && bodyId == Parts.Chest && joint.Joint == HumanoidRagdoll.RagdollJoint.Neck);
            if (toward && StructuralAnchor(joint) is { } anchor)
            {
                return anchor;
            }
        }

        foreach (var link in _fleshLinks)
        {
            if (link.Fragment == bodyId && _world.GetJointAnchors(link.Socket)?.AnchorA is { } point)
            {
                return point;
            }
        }

        return null;
    }

    /// <summary>
    /// 部位 <paramref name="part"/> から <paramref name="pieces"/> が分かれた: 部位が親として受けていた関節・肉のつなぎ・手（グローブ）を、
    /// それぞれの所にいちばん近い物質を持つ剛体へ移す（関節の種類で分けない。残った部位の側なら何もしない）。
    /// </summary>
    private void CarryOver(int part, IReadOnlyList<VoxelPart> pieces)
    {
        var bodies = new List<VoxelBody>(pieces.Count + 1);
        if (Voxels!.PartOf(part)?.Body is { } own)
        {
            bodies.Add(own);
        }
        else if (Voxels.Fragments.FirstOrDefault(f => f.Body.BodyId == part)?.Body is { } fragmentBody)
        {
            bodies.Add(fragmentBody);
        }

        bodies.AddRange(pieces.Select(p => p.Body));
        var attachedBefore = _all.Where(id => !_detached.Contains(id)).ToList();
        for (var i = 0; i < _joints.Count; i++)
        {
            var joint = _joints[i];
            if (joint.ParentPart != part || StructuralAnchor(joint) is not { } anchor)
            {
                continue;
            }

            var holder = Nearest(bodies, anchor);
            if (holder != part)
            {
                MoveJoint(i, holder);
            }
        }

        for (var i = _fleshLinks.Count - 1; i >= 0; i--)
        {
            var link = _fleshLinks[i];
            if (link.Stub != part || _world.GetJointAnchors(link.Socket)?.AnchorA is not { } point)
            {
                continue;
            }

            var holder = Nearest(bodies, point);
            if (holder == part)
            {
                continue;
            }

            foreach (var id in new[] { link.Socket, link.Spring })
            {
                if (_world.HasConstraint(id))
                {
                    _world.RemoveConstraint(id, forget: true);
                }
            }

            _fleshLinks.RemoveAt(i);
            var next = bodies.First(b => b.BodyId == holder);
            Link(holder, link.Fragment, point, link.TearForce, link.BendTorque, link.Bridge?.MoveStub(next));
        }

        foreach (var arm in _arms)
        {
            if (arm.GloveBody != part)
            {
                continue;
            }

            var holder = Nearest(bodies, arm.GlovePosition(_world));
            if (holder == part)
            {
                continue;
            }

            var glove = arm.GlovePosition(_world);
            var f = _world.GetState(holder);
            arm.AttachGlove(holder, Vector3.Transform(glove - f.Position, Quaternion.Conjugate(f.Orientation)));
            DisableArm(arm, part, handGone: false);
        }

        foreach (var piece in pieces)
        {
            SeparateCollisions(part, piece.Body.BodyId);
        }

        // 外れた部位は、分かれた部位の残り（切り口の側）とぶつけない: 関節で重なっていた所（股の上の腿・肩の上腕）が押し出し合って飛ばないように。
        foreach (var id in attachedBefore)
        {
            if (_detached.Contains(id))
            {
                _world.SetPairCollisionEnabled(id, part, false);
            }
        }
    }

    /// <summary>
    /// 体（頭とつながった部位）が自分の部位へ掛ける力積: 立たせる・運ぶ・回す・拳を引く力は筋と脚が体の中で出す力なので、体から外れた部位
    /// （<see cref="_detached"/>: つながりで分かれた下半身・切り落とされた腕）には掛けない。
    /// </summary>
    private void BodyImpulse(int body, Vector3 impulse)
    {
        if (!_detached.Contains(body))
        {
            _world.ApplyLinearImpulse(body, impulse);
        }
    }

    /// <summary>
    /// 体の中で対で掛ける力（筋の作用と反作用）を掛けてよいか: 2 つの部位が両方とも体（頭とつながった側）にあるとき。片方だけに掛けると、
    /// 体の外から押す力が生まれる（運動量が保たれない）ので、どちらかが外れていればどちらにも掛けない。
    /// </summary>
    private bool Attached(int a, int b) => !_detached.Contains(a) && !_detached.Contains(b);

    /// <summary>体（頭とつながった部位）が自分の部位へ掛けるねじりの力積（<see cref="BodyImpulse"/> と同じく、外れた部位には掛けない）。</summary>
    private void BodyTorque(int body, Vector3 impulse)
    {
        if (!_detached.Contains(body))
        {
            _world.ApplyAngularImpulse(body, impulse);
        }
    }

    /// <summary>剛体 <paramref name="bodies"/> のうち、ワールドの点 <paramref name="world"/> にいちばん近いセルを持つ剛体の id。</summary>
    private static int Nearest(List<VoxelBody> bodies, Vector3 world)
    {
        var best = float.MaxValue;
        var id = bodies[0].BodyId;
        foreach (var body in bodies)
        {
            var grid = body.Grid;
            var toWorld = body.GridToWorld();
            for (var i = 0; i < grid.CellCount; i++)
            {
                if (grid.MaterialAt(i) == 0)
                {
                    continue;
                }

                var (x, y, z) = grid.Coordinates(i);
                var d = Vector3.DistanceSquared(Vector3.Transform(grid.CellCenter(x, y, z), toWorld), world);
                if (d < best)
                {
                    (best, id) = (d, body.BodyId);
                }
            }
        }

        return id;
    }

    /// <summary>
    /// 体から外れた部位が増えた後: 腕・脚の部位が外れていれば、その腕は駆動しない（握りをほどく）・その脚は体重を受けない。頭が外れていれば
    /// 死ぬ（脳と体のつながりが断たれた: 首の切断と同じ <c>Die("neck")</c>）。
    /// </summary>
    private void AfterDetaching()
    {
        foreach (var arm in _arms)
        {
            if (!arm.Severed && (_detached.Contains(arm.UpperArm) || _detached.Contains(arm.Forearm)))
            {
                DisableArm(arm, arm.GloveBody, handGone: false);
            }
        }

        foreach (var leg in _legParts)
        {
            var side = leg.AigisLeft ? 0 : 1;
            if (_detached.Contains(leg.Thigh))
            {
                _legSevered[side] = true;
                _legSupport[side] = 0f; // 股から先が体から外れた: 体重を受ける脚は無い
            }
        }

        if (_detached.Contains(Parts.Head) && !HangsByFlesh(Parts.Head))
        {
            _world.SetPairCollisionEnabled(Parts.Head, Parts.Chest, false);
            Die("neck");
        }
    }
}
