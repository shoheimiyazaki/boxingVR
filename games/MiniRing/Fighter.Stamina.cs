using System.Numerics;

namespace MiniRing;

public sealed partial class Fighter
{
    private float _recentMusclePower;
    private float _workDone;
    private float _stepWork;
    private float _muscleBudget;
    internal float StepMuscleBudget { get; private set; }
    internal float StepMuscleAuthorized { get; private set; }
    private readonly List<ServoWork> _servoWork = [];

    private readonly record struct ServoWork(int Joint, int BodyA, int BodyB, float Speed, float Reserved);

    /// <summary>
    /// 筋肉の量の基準体格に対する比（<see cref="BodyBuild.MassRatio"/>: 体積に比例する質量と同じ H³ × M²）。スタミナの蓄え・心肺の供給・
    /// 回復を止める仕事率の尺度に掛ける。歩く・腕を振る・体を支える仕事は質量に比例して増えるので、蓄えと供給も筋肉の量に比例させないと、
    /// 大きい体ほど同じ動きで早く尽きる（2026-11-16 の中継で、質量 1.9・2.2 倍の 2 人が試合のほとんどを空のまま動けなかった）。
    /// 1 kg あたりの蓄え・供給は体格によらず同じ（§1.7: 強さは見える体から。基準体格では今までと同じ）。
    /// </summary>
    private float MuscleMassRatio => _tuning.Build.MassRatio;

    /// <summary>スタミナの容量 [J] = <see cref="FighterTuning.StaminaCapacity"/> × 筋肉の量の比。</summary>
    private float StaminaCapacityJoules => MathF.Max(1e-3f, _tuning.StaminaCapacity * MuscleMassRatio);

    /// <summary>
    /// 心肺が現在供給する仕事率 [W]（有酸素の供給。運動生理の臨界パワー CP と W′ のモデル、2026-11-17）。直近に実際にした仕事の仕事率 P
    /// （<see cref="_recentMusclePower"/>）に対して、P が臨界パワー C 以上なら供給は C（蓄えは P − C の速さで減る）、C より下なら
    /// R − (R − C) × P / C（休むほど速く戻り、C で戻りが 0 になる。P = 0 で最大 R）。R = 最大 × 筋肉の量の比 × Breath × <see cref="Endurance"/>、
    /// C = R × <see cref="FighterTuning.StaminaCriticalShare"/>。足を使う・軽く打ち合う（C より下）では必ず戻り、全力の連打（C の数倍）でだけ減る。
    /// 前の式（R × exp(−(P / 5 W)²)）は立ち回りの仕事率 5〜6 W で戻りが仕事を下回り、打ち合っていないのに減った。
    /// </summary>
    internal float RecoveryPower
    {
        get
        {
            if (IsDead)
            {
                return 0f;
            }

            var most = _tuning.StaminaRecoveryPower * MuscleMassRatio * Breath * Endurance;
            var critical = most * Math.Clamp(_tuning.StaminaCriticalShare, 1e-3f, 1f);
            return _recentMusclePower >= critical ? critical : most - ((most - critical) * _recentMusclePower / critical);
        }
    }

    /// <summary>
    /// 試合を通した持久力（0..1]: exp(−試合でした筋肉の仕事の合計 / 持久の蓄え）。筋肉の糖（グリコーゲン）が減るほど心肺の供給と臨界パワーが
    /// 落ちる近似。ラウンドの間の休みでは戻らないので、後のラウンドほど疲れやすい。持久の蓄え = <see cref="FighterTuning.StaminaEndurance"/>
    /// × 筋肉の量の比（1 kg あたりで同じ）。
    /// </summary>
    public float Endurance => MathF.Exp(-_workDone / MathF.Max(1e-3f, _tuning.StaminaEndurance * MuscleMassRatio));

    /// <summary>この試合で筋肉がした仕事の合計 [J]（<see cref="Endurance"/> の元）。</summary>
    internal float WorkDone => _workDone;

    /// <summary>直近に実際にした仕事の仕事率 [W]（なめらかにしたもの。回復を決める P）。</summary>
    internal float RecentMusclePower => _recentMusclePower;

    private bool FatigueEnabled => _tuning.StaminaFatigue;

    /// <summary>蓄えから出せる仕事率と心肺の供給で決まる駆動の割合。固定の最低筋力は設けない。</summary>
    public float Vigor => !FatigueEnabled ? 1f : Freshness + ((1f - Freshness) *
        RecoveryPower / MathF.Max(1e-6f, RecoveryPower + _recentMusclePower));

    /// <summary>仕事率が下がると筋肉の短縮速度も落ちる（Hill の力・速度関係の近似）。</summary>
    public float HandSlowness => !FatigueEnabled ? 0f : (1f - Vigor) * _tuning.TiredHandSlowness /
        MathF.Max(1e-3f, Vigor);

    private float Freshness => Smooth(Math.Clamp(Stamina / MathF.Max(1e-3f, _tuning.TiredKnee), 0f, 1f));

    private void UpdateStamina(float dt)
    {
        RefreshOrgans();
        var servoEnergy = 0f;
        foreach (var servo in _servoWork)
        {
            if (!_world.HasConstraint(servo.Joint))
            {
                continue;
            }

            // 精算は実際の速さで数える: 拘束の力積 J がした仕事は J ×（前後の速さの平均）以下。向きは取れないので、
            // 解く前と解いた後の相対角速度の大きい方を掛けて安全側にする（止まった体を加速した分は後の速さに出る）。
            // 力積²の項は取り置き（ReserveServo）だけの上限で、静止して重さを支えるだけの力積を仕事として数えない。
            var impulse = _world.GetConstraintImpulseMagnitude(servo.Joint);
            var after = (_world.GetState(servo.BodyA).AngularVelocity - _world.GetState(servo.BodyB).AngularVelocity).Length();
            servoEnergy += MathF.Min(servo.Reserved, impulse * MathF.Max(servo.Speed, after));
        }

        _servoWork.Clear();
        Stamina = MathF.Max(0f, Stamina - servoEnergy / StaminaCapacityJoules);
        // 回復を止める仕事率は、この歩で実際に出た仕事（腕の PD・保持・歩行・体から拳への移送は SpendMuscleEnergy が数えた分、
        // サーボは精算した分）。押しても出せなかった分は数えない（空で押すだけでは回復は止まらない）。
        var demand = (_stepWork + servoEnergy) / dt;
        _workDone += _stepWork + servoEnergy;
        _stepWork = 0f;
        _recentMusclePower += (demand - _recentMusclePower) *
            (1f - MathF.Exp(-dt / MathF.Max(1e-3f, _tuning.StaminaRecoveryWindow)));
        var capacity = StaminaCapacityJoules;
        var reserve = Stamina * capacity;
        var recovery = RecoveryPower * dt;
        Stamina = Math.Clamp(Stamina + (recovery / capacity), 0f, 1f);
        // 余裕があるうちは瞬発力として蓄えを使え、減るほど放出時定数による供給へ移る。
        // サーボの最大トルクからの安全側見積もりが、満タンの通常動作まで制限することを避ける。
        var release = Freshness + ((1f - Freshness) * (1f - MathF.Exp(-dt / MathF.Max(1e-3f, _tuning.StaminaReleaseWindow))));
        _muscleBudget = MathF.Min(Stamina * capacity, recovery + (reserve * release));
        StepMuscleBudget = _muscleBudget;
        StepMuscleAuthorized = 0f;
        Voxels?.HealBruises(dt, _tuning.BruiseRecoveryRate);
    }

    // サーボは次のソルバー歩で力を出す。予算を先に取り置き、解いた実際の力積で次の歩に精算する。
    // 取り置きは解く前で後の速さが分からないので、相対角速度の絶対値・最大逆慣性の力積²の上限で安全側へ見積もる。
    // 肩と肘が上腕を共有する交差項は二次項を 2 倍して含める。精算（UpdateStamina）は力積 × 前後の相対角速度の大きい方。
    // 接触や脚の受動拘束との連立までは解かないため、全身の厳密なエネルギー保存ではなく駆動予算の近似。
    private void BudgetArmServos(float budget, float dt)
    {
        var allowance = budget / 6f;
        foreach (var arm in _arms)
        {
            if (_world.HasConstraint(arm.ShoulderServo))
            {
                var shoulder = ReserveServo(arm.ShoulderServo, Parts.Chest, arm.UpperArm, 40f * _tuning.TorqueScale * ControlScale * FaintArm * Vigor * Strength(arm.UpperArm), allowance, dt);
                _world.SetAngularServoSettings(arm.ShoulderServo, _tuning.ArmServoHz, shoulder);
            }

            if (_world.HasConstraint(arm.ElbowServo))
            {
                var elbow = ReserveServo(arm.ElbowServo, arm.UpperArm, arm.Forearm, 30f * _tuning.TorqueScale * ControlScale * FaintArm * Vigor * Strength(arm.Forearm), allowance, dt);
                _world.SetTwistServoSettings(arm.ElbowServo, _tuning.ArmServoHz, elbow);
            }
            var wrist = arm.Held?.Wrist ?? arm.Support?.Wrist;
            var heldBody = arm.Held?.Body ?? arm.Support?.Body;
            if (wrist is { } joint && heldBody is { } body && _world.HasConstraint(joint))
            {
                var torque = ReserveServo(joint, arm.Forearm, body, _tuning.WristMaxTorque * _tuning.TorqueScale * arm.WristShare * ControlScale * FaintArm * Vigor * Strength(arm.Forearm), allowance, dt);
                _world.SetAngularServoSettings(joint, _tuning.WristServoHz * MathF.Sqrt(arm.WristShare), torque);
            }
        }
    }

    private float ReserveServo(int joint, int a, int b, float torque, float allowance, float dt)
    {
        var speed = (_world.GetState(a).AngularVelocity - _world.GetState(b).AngularVelocity).Length();
        float MaxInverse(int id)
        {
            var inertia = _world.GetLocalInertiaDiagonal(id);
            return 1f / MathF.Max(1e-9f, MathF.Min(inertia.X, MathF.Min(inertia.Y, inertia.Z)));
        }

        var inverse = 2f * (MaxInverse(a) + MaxInverse(b));
        var impulse = torque * dt;
        var scale = FatigueEnabled ? MuscleWork.ForceScale(impulse * speed, 0.5f * impulse * impulse * inverse, 0f, allowance) : 1f;
        impulse *= scale;
        var reserved = (impulse * speed) + (0.5f * impulse * impulse * inverse);
        _muscleBudget = MathF.Max(0f, _muscleBudget - reserved);
        StepMuscleAuthorized += reserved;
        _servoWork.Add(new ServoWork(joint, a, b, speed, reserved));
        return torque * scale;
    }

    private float SpendMuscleEnergy(float requested)
    {
        var supplied = FatigueEnabled ? MathF.Min(requested, _muscleBudget) : requested;
        _muscleBudget = MathF.Max(0f, _muscleBudget - supplied);
        StepMuscleAuthorized += supplied;
        Stamina = MathF.Max(0f, Stamina - supplied / StaminaCapacityJoules);
        _stepWork += supplied;
        return supplied;
    }

    private void ApplyLocomotion(Vector3 force, float dt)
    {
        if (LastCommand.ClampedMove.LengthSquared() <= 0f)
        {
            // 支持点へ戻す姿勢反射は既存の立位モデル。移動指令による仕事と区別する。
            ApplySharedImpulse(force * dt);
            return;
        }

        var chestMass = _world.GetMass(Parts.Chest);
        var pelvisMass = _world.GetMass(Parts.Pelvis);
        var mass = chestMass + pelvisMass;
        var velocity = ((_world.GetState(Parts.Chest).LinearVelocity * chestMass) +
            (_world.GetState(Parts.Pelvis).LinearVelocity * pelvisMass)) / mass;
        var linear = MathF.Max(0f, Vector3.Dot(force, velocity)) * dt;
        var quadratic = force.LengthSquared() * dt * dt / (2f * mass);
        var walking = _tuning.WalkCost * TotalMass * new Vector2(velocity.X, velocity.Z).Length() * LastCommand.ClampedMove.Length() * dt;
        var budget = FatigueEnabled ? _muscleBudget : linear + quadratic + walking;
        var scale = MuscleWork.ForceScale(linear, quadratic, walking, budget);
        SpendMuscleEnergy((linear * scale) + (quadratic * scale * scale) + (walking * scale));
        ApplySharedImpulse(force * (scale * dt));
    }

    /// <summary>
    /// 力積による仕事 max(0, F·v dt + |F|²dt²/(2m)) と保持費を予算内に収める。
    /// 静止時も二次項を含めるため、v=0 を抜け道にして加速できない。制動の仕事は蓄えへ戻さない。
    /// </summary>
    private void ApplyArmDrive(Arm arm, Vector3 force, Vector3 armPart, bool weightShift, float dt)
    {
        var bodyForce = weightShift ? (force - armPart) * _tuning.WeightShiftShare : Vector3.Zero;
        var chestMass = _world.GetMass(Parts.Chest);
        var pelvisMass = _world.GetMass(Parts.Pelvis);
        var chestForce = (-armPart * _tuning.HandReactionShare) + (bodyForce * chestMass / (chestMass + pelvisMass));
        var pelvisForce = bodyForce * pelvisMass / (chestMass + pelvisMass);
        var linear = 0f;
        var quadratic = 0f;
        void Include(int body, Vector3 applied)
        {
            linear += MathF.Max(0f, Vector3.Dot(applied, _world.GetState(body).LinearVelocity)) * dt;
            quadratic += applied.LengthSquared() * dt * dt / (2f * _world.GetMass(body));
        }

        Include(arm.Forearm, force);
        Include(Parts.Chest, chestForce);
        Include(Parts.Pelvis, pelvisForce);
        var holding = _tuning.IsometricCost * force.Length() * dt;
        var requested = linear + quadratic + holding;
        var budget = FatigueEnabled ? _muscleBudget : requested;
        var scale = MuscleWork.ForceScale(linear, quadratic, holding, budget);
        SpendMuscleEnergy((linear * scale) + (quadratic * scale * scale) + (holding * scale));
        arm.LastForce = force * scale;
        _world.ApplyLinearImpulse(arm.Forearm, force * (scale * dt));
        _world.ApplyLinearImpulse(Parts.Chest, -armPart * (_tuning.HandReactionShare * scale * dt));
        if (weightShift && bodyForce.LengthSquared() > 0f)
        {
            ApplySharedImpulse(bodyForce * (scale * dt));
        }
    }
}

/// <summary>同じ向きの力を縮めて仕事予算に合わせる。二次式の単調区間を二分する純関数。</summary>
internal static class MuscleWork
{
    internal static float ForceScale(float linear, float quadratic, float holding, float budget)
    {
        static float Cost(float s, float a, float b, float c) => MathF.Max(0f, (a * s) + (b * s * s)) + (c * s);
        if (Cost(1f, linear, quadratic, holding) <= budget)
        {
            return 1f;
        }

        var low = 0f;
        var high = 1f;
        for (var i = 0; i < 24; i++)
        {
            var mid = (low + high) * 0.5f;
            if (Cost(mid, linear, quadratic, holding) <= budget)
            {
                low = mid;
            }
            else
            {
                high = mid;
            }
        }

        return low;
    }
}
