using System.Numerics;

namespace MiniRing;

/// <summary>組み技のための、体の見える点（肘・手首・重心の速さ）。docs/GRAPPLING.md。</summary>
public sealed partial class Fighter
{
    /// <summary>肘（上腕と前腕をつなぐ関節の点、ワールド）。切り落とされた腕は前腕の位置。</summary>
    public Vector3 ElbowPosition(Hand hand)
    {
        var arm = _arms[(int)hand];
        for (var i = 0; i < _joints.Count; i++)
        {
            var j = _joints[i];
            if (j.ChildPart == arm.Forearm && j.PrimaryConstraintId >= 0 && _world.HasConstraint(j.PrimaryConstraintId)
                && _world.GetJointAnchors(j.PrimaryConstraintId) is { } anchors)
            {
                return anchors.AnchorA;
            }
        }

        return _world.GetState(arm.Forearm).Position;
    }

    /// <summary>
    /// 手首（前腕の拳の側の端、ワールド）: 拳（グローブ）の中心から肘へ <see cref="WristShare"/> だけ寄った点。
    /// 掴むなら、ここ（拳の手前の細い所）が手で握れる所。
    /// </summary>
    public Vector3 WristPosition(Hand hand) => Vector3.Lerp(GloveCenter(hand), ElbowPosition(hand), WristShare);

    /// <summary>手首の点の、拳の中心から肘までの割合（前腕の長さに対する、拳の半分 ＋ 手首の幅のおよその比）。</summary>
    public const float WristShare = 0.3f;

    /// <summary>脚（腿・すね・足・つま先）の質量の合計 [kg]。</summary>
    public float LegMass => SupportedLegMass;

    /// <summary>切り離された（切り落とされた・折れて離れた）部位か。</summary>
    internal bool IsDetachedPart(int id) => _detached.Contains(id);

    /// <summary>今の体の重心の速度（削られて軽くなった分・切り落とされた部位を除く）。</summary>
    public Vector3 CenterOfMassVelocity
    {
        get
        {
            var sum = Vector3.Zero;
            var mass = 0f;
            foreach (var id in _all)
            {
                if (_detached.Contains(id))
                {
                    continue;
                }

                var m = _world.GetMass(id);
                sum += _world.GetState(id).LinearVelocity * m;
                mass += m;
            }

            return sum / MathF.Max(1e-6f, mass);
        }
    }

    /// <summary>この歩で、掴んだ手と相手をつなぐ拘束が伝えた力 [N]（掴んでいなければ 0）。引き・押しの物差し。</summary>
    public float GripForce(Hand hand)
    {
        var arm = _arms[(int)hand];
        return arm.GrabConstraint is { } id && _world.HasConstraint(id) ? _world.GetConstraintImpulseMagnitude(id) / MathF.Max(1e-6f, _lastDt) : 0f;
    }
}
