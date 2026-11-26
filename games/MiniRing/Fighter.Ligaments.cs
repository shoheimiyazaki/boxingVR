namespace MiniRing;

/// <summary>
/// 関節の靭帯（docs/GRAPPLING.md 段 6）。肘を伸ばしきった先（過伸展）は、関節の制限（Aigis の肘の TwistLimit の下限 −10°）が受け止める。
/// その制限が受けたねじりの力 τ（拘束の力積 / dt）は、本物なら前の関節包・内側側副靭帯と肘頭が受け持つ荷重。
/// τ が靭帯の降伏の力 τy を超えている間、靭帯は粘塑性で伸びる: 伸びの速さ = (τ − τy) / η（η は伸びにくさ、N·m·s/rad）。
/// 伸びた分だけ制限が緩み（肘がその分さらに反る）、伸びが破断の伸びに達したら靭帯は切れている（残りの割合 0）。
/// 切れた・伸びた肘は荷重を伝えられず、腕の力（<see cref="ArmStrength"/>）は残りの割合だけになる（腕が反って垂れて見える）。
/// 「この技なら」「急所なら」の分岐は無い: 関節技は、相手の手首を持って肘の反る向きへ梃子をかけた結果の τ で決まる。
/// </summary>
public sealed partial class Fighter
{
    private readonly float[] _elbowStretch = new float[2];
    private readonly float[] _elbowTorque = new float[2];

    /// <summary>直前の制御の歩の長さ [s]（拘束の力積を力へ直すため）。</summary>
    private float _lastDt = 1f / 240f;

    /// <summary>肘の靭帯の残りの割合（1 = 無傷、0 = 切れた）。</summary>
    public float ElbowLigament(Hand hand) => 1f - Math.Clamp(_elbowStretch[(int)hand] / MathF.Max(1e-4f, _tuning.LigamentRuptureStretch), 0f, 1f);

    /// <summary>この歩で、肘の過伸展の制限が受けたねじりの力 [N·m]（伸ばしきっていなければ 0）。関節技の効き目の物差し。</summary>
    public float ElbowLockTorque(Hand hand) => _elbowTorque[(int)hand];

    /// <summary>肘の靭帯の降伏の力 [N·m]（この体の。基準体格の値 × 体格の比 H³: 靭帯の断面 H² × てこの長さ H）。</summary>
    public float ElbowLigamentYield => _tuning.ElbowLigamentYield * _tuning.PassiveTorqueScale * MathF.Pow(_tuning.Build.Height, 3f);

    /// <summary>物理の歩の後に、両肘の過伸展の荷重を読み、靭帯を伸ばす。</summary>
    internal void StrainLigaments(float dt)
    {
        _lastDt = dt;
        if (dt <= 0f)
        {
            return;
        }

        var yield = ElbowLigamentYield;
        var viscosity = _tuning.LigamentViscosity * _tuning.PassiveTorqueScale * MathF.Pow(_tuning.Build.Height, 3f);
        foreach (var arm in _arms)
        {
            var i = (int)arm.Hand;
            _elbowTorque[i] = 0f;
            if (arm.Severed)
            {
                continue;
            }

            var limit = ElbowLimit(arm.Hand);
            if (limit < 0)
            {
                continue;
            }

            var min = (ElbowHyperextension * MathF.PI / 180f) - Travel(_elbowStretch[i]);
            // 制限は両端（伸ばしきり・曲げきり）で効く。反る側（下限の近く）にいるときだけ靭帯の荷重に数える。
            var angle = _world.GetTwistAngle(limit);
            if (angle > min + (ElbowEngageDegrees * MathF.PI / 180f))
            {
                continue;
            }

            var torque = _world.GetConstraintImpulseMagnitude(limit) / dt;
            _elbowTorque[i] = torque;
            if (torque <= yield || _elbowStretch[i] >= _tuning.LigamentRuptureStretch)
            {
                continue;
            }

            var stretch = MathF.Min(_tuning.LigamentRuptureStretch, _elbowStretch[i] + ((torque - yield) / viscosity * dt));
            _elbowStretch[i] = stretch;
            var maxBend = 145f * MathF.PI / 180f; // Aigis の肘の上限（HumanoidRagdoll の肘）
            _world.SetTwistLimitRange(limit, (ElbowHyperextension * MathF.PI / 180f) - Travel(stretch), maxBend);
        }
    }

    /// <summary>伸びた分だけ反れる。切れたら、骨と残りの組織が止める所（下限から さらに <see cref="FighterTuning.LigamentTornTravel"/>）まで反る。</summary>
    private float Travel(float stretch) => stretch >= _tuning.LigamentRuptureStretch ? _tuning.LigamentTornTravel : stretch;

    /// <summary>肘の曲げ角 [rad]（関節の制限が測る角。0 = 伸ばしきり、正 = 曲げ、負 = 反り）。腕が切り落とされて制限が無ければ 0。</summary>
    internal float ElbowAngle(Hand hand)
    {
        var limit = ElbowLimit(hand);
        return limit >= 0 ? _world.GetTwistAngle(limit) : 0f;
    }

    /// <summary>肘の蝶番の軸（ワールド、上腕の向きで回した Aigis の肘の軸 Y）。</summary>
    internal System.Numerics.Vector3 ElbowAxis(Hand hand)
        => System.Numerics.Vector3.Transform(System.Numerics.Vector3.UnitY, _world.GetState(_arms[(int)hand].UpperArm).Orientation);

    /// <summary>上腕・前腕の部位（調べもの・テスト用）。</summary>
    internal (int UpperArm, int Forearm) ArmParts(Hand hand) => (_arms[(int)hand].UpperArm, _arms[(int)hand].Forearm);

    private readonly int[] _elbowLimitCache = [-1, -1];

    /// <summary>肘の過伸展の制限の拘束（無ければ −1）。毎歩探さないよう覚え、拘束が消えたら（腕が切れた）探し直す。</summary>
    private int ElbowLimit(Hand hand)
    {
        var cached = _elbowLimitCache[(int)hand];
        if (cached >= 0 && _world.HasConstraint(cached))
        {
            return cached;
        }

        var forearm = _arms[(int)hand].Forearm;
        var found = -1;
        for (var i = 0; i < _joints.Count; i++)
        {
            var j = _joints[i];
            if (j.ChildPart == forearm && j.Joint is Aigis.Physics.HumanoidRagdoll.RagdollJoint.LeftElbow or Aigis.Physics.HumanoidRagdoll.RagdollJoint.RightElbow
                && _world.HasConstraint(j.TorqueConstraintId))
            {
                found = j.TorqueConstraintId;
                break;
            }
        }

        _elbowLimitCache[(int)hand] = found;
        return found;
    }

    /// <summary>Aigis の肘の制限の下限 [°]（HumanoidRagdoll の肘: −10°..145°）。</summary>
    private const float ElbowHyperextension = -10f;

    /// <summary>下限からこの角度 [°] 以内なら、制限の荷重は反る側の荷重とみなす（解き終えた角度は制限のばねの分だけ少し越えている）。</summary>
    private const float ElbowEngageDegrees = 8f;
}
