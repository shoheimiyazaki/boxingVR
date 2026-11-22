using System.Numerics;

namespace MiniRing;

public sealed partial class Fighter
{
    private Vector3? _jointStanceTarget;
    private readonly List<JointWork> _jointWork = [];

    private readonly record struct JointWork(int Parent, int Child, Quaternion Before, Vector3 Torque, MuscleRegion Region, float Hold, int Channel);

    /// <summary>
    /// 関節ごとの筋の努力（活性化でならしたトルク、親の局所の向き）。酸素の仕事はこの値で数える: 実際の筋は活性化に時間がかかり、
    /// 数値の歩ごとのぶれ（PD が毎歩ごとに正負へ振れる）に追従できないので、ぶれと関節の回転が偶然そろった分を仕事として溜めない。
    /// </summary>
    private readonly Dictionary<(int Parent, int Child), Vector3> _muscleEffort = [];
    private readonly Dictionary<(int Parent, int Child), Vector3> _muscleRate = [];

    internal bool UsesJointDrive => _tuning.JointDrive;

    /// <summary>
    /// 腰から打つ体（<see cref="FighterTuning.HipDrive"/>）: 外から駆動するのは骨盤だけで、腹・胸は関節の筋で追う。立っている間だけ
    /// （倒れて起き上がる間は従来の立たせる近道。見た目優先で近道でよい: ユーザーの指定 2026-11-22）。関節駆動の体では使わない。
    /// </summary>
    internal bool UsesHipDrive => _tuning.HipDrive && !_tuning.JointDrive && Parts.Abdomen is not null && State == FighterState.Standing;

    internal bool UsesLegacyGloveImpact => _tuning.JointLegacyGloveImpact;

    private float JointSupportAssist => UsesJointDrive && _tuning.SupportFromGround ? Math.Clamp(_tuning.JointSupportAssist, 0f, 1f) : 0f;

    private bool UsesGroundSupport => !UsesJointDrive || JointSupportAssist > 0f;

    private void DriveRelativeTorso(int parent, int child, Quaternion parentTarget, Quaternion childTarget, float yawRate, float scale, float dt)
    {
        var a = _world.GetState(parent);
        var b = _world.GetState(child);
        var inverse = Quaternion.Conjugate(a.Orientation);
        var relative = Quaternion.Concatenate(b.Orientation, inverse);
        var target = Quaternion.Concatenate(childTarget, Quaternion.Conjugate(parentTarget));
        var error = FighterMath.RotationError(relative, target);
        var speed = Vector3.Transform(b.AngularVelocity - a.AngularVelocity, inverse);
        var limit = FormValue(_tuning.TrunkRotatorTorque, FighterTuning.DynamicPunch.TrunkRotatorTorque) * _tuning.TorqueScale;
        var stiffness = limit / MathF.Max(0.1f, PunchTwistLimit);
        var damping = 2f * MathF.Sqrt(stiffness * _world.GetLocalInertiaDiagonal(child).Y);
        var targetSpeed = Vector3.Transform(Vector3.UnitY * (_tuning.TrunkTwistFeedForward * yawRate), inverse);
        var raw = ClampLength((error * stiffness) + ((targetSpeed - speed) * damping), limit) * scale;
        // 酸素が切れて弱るのは軸（背骨のまわり）の回旋筋だけ: 体を起こして保つ筋（傾きの保ち）は疲れで崩れない（立位の近道と同じ決まり）。
        var torque = new Vector3(raw.X, raw.Y * VigorOf(MuscleRegion.Trunk), raw.Z);
        ApplyMuscleCouple(parent, child, Vector3.Transform(torque, a.Orientation) * dt, MuscleRegion.Trunk, dt);
    }

    private void ApplyMuscleCouple(int parent, int child, Vector3 impulse, MuscleRegion region, float dt, int channel = 0)
    {
        if (impulse.LengthSquared() <= 0f)
        {
            return;
        }

        var a = _world.GetState(parent);
        var b = _world.GetState(child);
        var linear = MathF.Max(0f, Vector3.Dot(impulse, b.AngularVelocity - a.AngularVelocity));
        var quadratic = 0.5f * (ImpulseInertiaTerm(parent, a.Orientation) + ImpulseInertiaTerm(child, b.Orientation));
        var index = (int)region;
        // 胴の筋は予算で締めない（2026-11-22）: 酸素の取り置きは「この力積で体を加速する」仕事の上限で、体を起こして保つだけのトルク（等尺性）まで
        // 締めると、蓄えの尽きた胴が後ろへ崩れた（のけぞり -30°）。胴の疲れは回旋（軸）の筋の駆動の割合 VigorOf だけに出す。
        var gated = FatigueEnabled && region != MuscleRegion.Trunk;
        var scale = gated ? MuscleWork.ForceScale(linear, quadratic, 0f, _budget[index]) : 1f;
        var applied = impulse * scale;
        if (gated)
        {
            var reserved = (linear * scale) + (quadratic * scale * scale);
            _budget[index] = MathF.Max(0f, _budget[index] - reserved);
            StepMuscleAuthorized += reserved;
        }

        var inverse = Quaternion.Conjugate(a.Orientation);
        var instant = Vector3.Transform(applied / dt, inverse);
        var key = (parent, child + (channel * 100000)); // 同じ関節に別の筋（呼吸）が入るときは努力のならしを分ける
        var follow = 1f - MathF.Exp(-dt / MathF.Max(1e-4f, _tuning.MuscleEffortWindow));
        var effort = _muscleEffort.TryGetValue(key, out var previous) ? previous + ((instant - previous) * follow) : instant;
        _muscleEffort[key] = effort;
        // 保つ分（等尺性）: 筋が出している力 = トルク ÷ てこの腕。動かさなくても力を出すだけで酸素を使う（腕の IsometricCost と同じ量）。
        var hold = _tuning.IsometricCost * effort.Length() / MathF.Max(1e-3f, _tuning.TrunkMuscleArm) * dt;
        _jointWork.Add(new JointWork(parent, child, Quaternion.Concatenate(b.Orientation, inverse), effort, region, hold, channel));
        _world.ApplyAngularImpulse(child, applied);
        _world.ApplyAngularImpulse(parent, -applied);

        float ImpulseInertiaTerm(int body, Quaternion orientation)
        {
            var local = Vector3.Transform(impulse, Quaternion.Conjugate(orientation));
            return Vector3.Dot(local, local / _world.GetLocalInertiaDiagonal(body));
        }
    }

    private float _chinTarget;

    /// <summary>
    /// 気道を開く反射（2026-11-22、慢性の疲れの見た目）: 息が上がるほど（<see cref="Winded"/>）、首の筋の目標を後ろへ反らす（あごが上がる）。
    /// 頭を動かす筋の目標であって頭の位置の書き込みではない: 首のバネが追うので、打たれれば崩れ、首が折れれば無い（バランスの反射と同じ扱い）。
    /// </summary>
    private void ApplyChinReflex()
    {
        if (!_world.HasConstraint(_neck))
        {
            return;
        }

        var target = _tuning.ChinReflexRadians * Winded;
        if (MathF.Abs(target - _chinTarget) < 1e-3f)
        {
            return;
        }

        _chinTarget = target;
        _world.SetAngularServoTarget(_neck, Quaternion.CreateFromAxisAngle(Vector3.UnitX, -target)); // X まわりの正が前へ倒す向き: 負が顔を上げる
    }

    /// <summary>
    /// 呼吸の筋（横隔膜・肋間筋）: 吸うたびに胸を反らす周期のトルクを、腹と胸の関節へ入れる（吐くのは胴の筋のばねの戻り）。深さは息の上がり方 <see cref="Winded"/> で
    /// 安静（<see cref="FighterTuning.BreathRestShare"/>）からあえぎまで、周期は <see cref="BreathPhase"/>（<see cref="TiredLook.BreathsPerSecond"/>）。
    /// 胴の関節の筋と同じ経路（<see cref="ApplyMuscleCouple"/>）なので、この仕事も胴の酸素に数える。
    /// </summary>
    private void ApplyBreathing(int parent, int chest, float dt)
    {
        if (IsDead)
        {
            return;
        }

        var phase = BreathPhase - MathF.Floor(BreathPhase);
        var pulse = MathF.Sin(MathF.PI * phase);
        var depth = _tuning.BreathRestShare + ((1f - _tuning.BreathRestShare) * Winded);
        var axis = Vector3.Transform(Vector3.UnitX, _world.GetState(parent).Orientation);
        ApplyMuscleCouple(parent, chest, -axis * (_tuning.BreathTorque * depth * pulse * pulse * dt), MuscleRegion.Trunk, dt, channel: 1);
    }

    private Vector3 _hipEffort;
    private Vector3 _hipRate;

    /// <summary>
    /// 腰の駆動（骨盤を外から回す力積）の酸素の仕事を数える。胴の関節の筋（<see cref="ApplyMuscleCouple"/>）と同じ数え方: ならした努力 × ならした骨盤の回転の速さの
    /// 正の部分（動かす分）+ 努力の大きさに比例する等尺性の費用（保つ分）。骨盤を回す筋（臀筋・腹斜筋）は足と床を支えにして回すので、床に対する骨盤の角速度で数える。
    /// </summary>
    private void CountHipWork(Vector3 impulse, Vector3 spin, float dt)
    {
        var follow = 1f - MathF.Exp(-dt / MathF.Max(1e-4f, _tuning.MuscleEffortWindow));
        _hipEffort += ((impulse / dt) - _hipEffort) * follow;
        _hipRate += (spin - _hipRate) * follow;
        var move = MathF.Max(0f, Vector3.Dot(_hipEffort, _hipRate)) * dt;
        var hold = _tuning.IsometricCost * _hipEffort.Length() / MathF.Max(1e-3f, _tuning.TrunkMuscleArm) * dt;
        Draw((int)MuscleRegion.Trunk, move + hold);
    }

    private void SettleJointWork(float dt)
    {
        // The constraints have now reacted to the applied torque. Use actual rotation at the
        // joint, including when the foot held it nearly still. Negative work does not refill fuel.
        foreach (var motor in _jointWork)
        {
            var parent = _world.GetState(motor.Parent);
            var child = _world.GetState(motor.Child);
            var after = Quaternion.Concatenate(child.Orientation, Quaternion.Conjugate(parent.Orientation));
            var displacement = FighterMath.RotationError(motor.Before, after);
            // 動かす分（ならした筋の努力 × ならした関節の実際の回転の速さ、の正の部分）+ 保つ分。両方を同じ時間でならすので、
            // 歩ごとの振動（努力と回転が偶然そろう）は仕事に数えず、数 Hz のパンチ・ひねりの仕事は残る。
            var key = (motor.Parent, motor.Child + (motor.Channel * 100000));
            var follow = 1f - MathF.Exp(-dt / MathF.Max(1e-4f, _tuning.MuscleEffortWindow));
            var instant = displacement / dt;
            var rate = _muscleRate.TryGetValue(key, out var previous) ? previous + ((instant - previous) * follow) : instant;
            _muscleRate[key] = rate;
            Draw((int)motor.Region, (MathF.Max(0f, Vector3.Dot(motor.Torque, rate)) * dt) + motor.Hold);
        }

        _jointWork.Clear();
    }

    private void SetJointLegStrength(float scale, float dt)
    {
        var index = (int)MuscleRegion.Legs;
        var unassigned = 2 * 4;
        foreach (var leg in _legParts)
        {
            var strength = scale * _tuning.TorqueScale * LegStrength(leg.AigisLeft) * VigorOf(MuscleRegion.Legs);
            var speed = KneeKickSpeed(leg.AigisLeft);
            Set(leg.HipServo, Parts.Pelvis, leg.Thigh, _tuning.HipMaxTorque, angular: true);
            Set(leg.KneeServo, leg.Thigh, leg.Shin, _tuning.KneeMaxTorque);
            Set(leg.AnkleServo, leg.Shin, leg.Heel, _tuning.AnkleMaxTorque);
            Set(leg.ToeServo, leg.Heel, leg.Toe, _tuning.ToeMaxTorque);

            void Set(int joint, int parent, int child, float torque, bool angular = false)
            {
                var allowance = _budget[index] / unassigned--;
                if (!_world.HasConstraint(joint))
                {
                    return;
                }

                var limit = ReserveServo(MuscleRegion.Legs, joint, parent, child, torque * strength, allowance, dt);
                if (angular)
                {
                    SetAngularStrength(joint, _tuning.LegServoHz, limit, speed);
                }
                else
                {
                    SetTwistStrength(joint, _tuning.LegServoHz, limit, speed);
                }
            }
        }
    }

    private void DriveThroughFeet(in FighterCommand command, float height, float groundY, float dt)
    {
        // A step moves the support plan and the leg IK target. The floor supplies the net wrench.
        var intent = ClampLength(command.ClampedMove + command.ClampedStep, 1f);
        StepFeet(command with { MoveWorld = intent }, height, dt);
        var pelvis = PelvisPosition;
        var center = CenterOfMass with { Y = 0f };
        var velocity = CenterOfMassVelocity with { Y = 0f };
        var lead = _footwork.Pose(Footwork.Lead);
        var rear = _footwork.Pose(Footwork.Rear);
        var support = lead.Swinging ? rear.Ball : rear.Swinging ? lead.Ball : (lead.Ball + rear.Ball) * 0.5f;
        support -= Frame.Forward * HalfExtents.Foot.Z;
        var requested = support + (intent * (0.12f * _standHeight));
        _jointStanceTarget = Vector3.Lerp(_jointStanceTarget ?? requested, requested, 1f - MathF.Exp(-dt / 0.14f));
        var horizontal = 1.8f * (_jointStanceTarget.Value - center) - (0.38f * velocity);
        var target = pelvis + ClampLength(horizontal, 0.18f * _standHeight);
        target.Y = groundY + (StandTargetHeight * (1f + _crouch))
            - Math.Clamp(0.20f * _world.GetState(Parts.Pelvis).LinearVelocity.Y, -0.08f, 0.08f);
        var ankleAdjustment = Vector3.Zero;
        if (_tuning.JointStanceAnkleGain != 0f && _world.Gravity.Y < 0f)
        {
            var comHeight = MathF.Max(HalfExtents.Pelvis.Y, CenterOfMass.Y - groundY);
            var captureTime = MathF.Sqrt(comHeight / -_world.Gravity.Y);
            // Apply the correction only as a target angle for a planted ankle.
            ankleAdjustment = (center + velocity * captureTime - _jointStanceTarget.Value)
                * (_tuning.JointStanceAnkleGain / comHeight);
        }

        ApplyLegs(groundY, dt, target, ankleAdjustment);
    }

    private int JointStanceContacts()
    {
        if (!UsesJointDrive || (_tuning.JointStanceHipShare <= 0f && _tuning.JointStanceAnkleGain == 0f && JointSupportAssist <= 0f))
        {
            return 0;
        }

        var stance = 0;
        foreach (var contact in _world.Contacts())
        {
            // Static contacts are reported with the body as A and the outward normal.
            if (!contact.StaticB || contact.Normal.Y < 0.5f)
            {
                continue;
            }

            for (var i = 0; i < _legParts.Length; i++)
            {
                if (contact.A == _legParts[i].Heel || contact.A == _legParts[i].Toe)
                {
                    stance |= 1 << i;
                }
            }
        }

        return stance;
    }
}
