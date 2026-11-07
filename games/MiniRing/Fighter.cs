using System.Numerics;
using Aigis.Physics;

namespace MiniRing;

public enum FighterState
{
    Standing,
    Down,
    GettingUp,
}

/// <summary>
/// 1 体のファイター: Aigis の <see cref="HumanoidRagdoll"/> を 60cm に縮小し、指令をバネ・ダンパで伝える。
/// <list type="bullet">
/// <item>体幹: 骨盤ホバー PD + 骨盤/胸の直立 PD + 水平速度 P（力積で与える）。胴の軸の向きは両手と頭の位置から決める。</item>
/// <item>脚: <see cref="HumanoidRagdoll.AddLegServos"/> のサーボに正弦歩行の目標角。</item>
/// <item>腕: 2 ボーン IK → 肩 AngularServo / 肘 TwistServo の目標、さらに拳を目標へ PD 力で引く。
///   力の上限は <see cref="PunchMechanics"/>（体の有効質量 × 腕の連結度）で決まり、体重が乗るほど大きい。</item>
/// <item>被弾: <see cref="Condition"/> に部位ごとのダメージを記録するだけ。動きは常にフルパワー（この POC は体を動かす面白さに絞る）。</item>
/// </list>
/// 位置・姿勢を直接書き換えるのは生成直後の向き合わせ（<see cref="Spawn"/>）だけ。
/// </summary>
public sealed class Fighter
{
    private readonly PhysicsWorld _world;
    private readonly FighterTuning _tuning;
    private readonly HumanoidRagdoll.LegServos _legs;
    private readonly Arm[] _arms = new Arm[2];
    private readonly int[] _all;
    private readonly int _neck;

    /// <summary>
    /// 首のバネが目標へ戻す角速度の上限 [rad/s]。減衰比が小さいと戻りの速度が大きく、上限が低いと（Aigis 既定の 10）
    /// そこで頭打ちになってバネとして働かず、頭が可動域の端まで倒れたまま戻らない。
    /// </summary>
    private const float NeckServoMaxSpeed = 60f;
    private readonly float _standHeight;
    private readonly BodyInertia _inertia;
    private readonly float[] _servoSettings = [-1f, -1f, -1f, -1f, -1f, -1f];
    private float _facingYaw;
    private float _gaitPhase;
    private float _smoothedSpeed;
    private float _forwardSpeed;
    private float _stateTime;
    private float _tiltTime;
    private float _noGroundTime;
    private int _grabbedByCount;
    private bool _stayDown;

    private Fighter(PhysicsWorld world, FighterTuning tuning, string name, HumanoidRagdoll.Parts parts, HumanoidRagdoll.HalfExtents halfExtents, float yaw)
    {
        _world = world;
        _tuning = tuning;
        Name = name;
        Parts = parts;
        HalfExtents = halfExtents;
        _all = parts.All;
        _facingYaw = yaw;
        TotalMass = _all.Sum(world.GetMass);

        var pelvisY = world.GetState(parts.Pelvis).Position.Y;
        var footBottom = MathF.Min(world.GetState(parts.LeftFoot).Position.Y, world.GetState(parts.RightFoot).Position.Y) - halfExtents.Foot.Y;
        _standHeight = pelvisY - footBottom;
        var height = world.GetState(parts.Head).Position.Y + halfExtents.Head.Y - footBottom;
        _inertia = BodyInertia.Cylinder(TotalMass, height, halfExtents.Chest.X);

        _legs = HumanoidRagdoll.AddLegServos(world, parts, tuning.LegServoHz,
            anklePitchMaxForce: 100f * tuning.TorqueScale, kneeMaxForce: 100f * tuning.TorqueScale);
        world.SetAngularServoSettings(_legs.LeftHip, tuning.LegServoHz, 120f * tuning.TorqueScale);
        world.SetAngularServoSettings(_legs.RightHip, tuning.LegServoHz, 120f * tuning.TorqueScale);
        // 首は Aigis の既定では受動（ダンパのみ）で頭が転がるので、直立へ戻すバネダンパ（サーボ）を足す。
        // 減衰比を 1 未満にして、打たれた頭がバネで揺れ戻るようにする（ラグドール側の首の減衰は Spawn で外してある）。
        _neck = world.AddAngularServo(parts.Chest, parts.Head, Quaternion.Identity, tuning.NeckServoHz, tuning.NeckDampingRatio, NeckServoMaxSpeed, 0f, tuning.NeckMaxTorque * tuning.TorqueScale);
        _arms[(int)Hand.Left] = Arm.Create(world, tuning, parts, halfExtents, Hand.Left);
        _arms[(int)Hand.Right] = Arm.Create(world, tuning, parts, halfExtents, Hand.Right);
    }

    public string Name { get; }
    public HumanoidRagdoll.Parts Parts { get; }
    public HumanoidRagdoll.HalfExtents HalfExtents { get; }
    public float TotalMass { get; }
    public FighterState State { get; private set; }
    public BodyCondition Condition { get; } = new();

    /// <summary>実際に体が従っている指令（反応遅れを経たもの）。</summary>
    public FighterCommand LastCommand { get; private set; }
    public bool IsGrabbed => _grabbedByCount > 0;
    public float ArmReach => _arms[0].UpperLength + _arms[0].LowerLength;

    /// <summary>グローブ（拳）の半径（見た目と拳の判定に使う）。</summary>
    public float GloveRadius => _tuning.GloveRadius;

    /// <summary>倒れてからの経過時間 [s]（10 カウント用）。起き上がり中も数え続け、立ち直ったら 0。</summary>
    public float DownTime { get; private set; }

    /// <summary>現在の手の駆動強度（0..1）。被弾・転倒で下がる。</summary>
    public float ControlScale { get; private set; } = 1f;

    /// <summary>体フレーム（胸の肩中心・向きヨー）。手の目標はこの座標系で渡される。</summary>
    public BodyFrame Frame => new(ShoulderCenter, _facingYaw);

    public Vector3 ShoulderCenter
    {
        get
        {
            var chest = _world.GetState(Parts.Chest);
            return chest.Position + Vector3.Transform(_arms[0].ShoulderCenterOnChest, chest.Orientation);
        }
    }

    public Vector3 HeadPosition => _world.GetState(Parts.Head).Position;
    public Vector3 ChestPosition => _world.GetState(Parts.Chest).Position;
    public Vector3 PelvisPosition => _world.GetState(Parts.Pelvis).Position;
    public Quaternion TorsoOrientation => _world.GetState(Parts.Chest).Orientation;

    public Vector3 CenterOfMass
    {
        get
        {
            var sum = Vector3.Zero;
            foreach (var id in _all)
            {
                sum += _world.GetState(id).Position * _world.GetMass(id);
            }

            return sum / TotalMass;
        }
    }

    public float Uprightness => MathF.Min(UpOf(Parts.Chest).Y, UpOf(Parts.Pelvis).Y);

    public bool Owns(int bodyId) => Array.IndexOf(_all, bodyId) >= 0;

    public int ForearmId(Hand hand) => _arms[(int)hand].Forearm;

    public Vector3 GloveCenter(Hand hand) => _arms[(int)hand].GlovePosition(_world);

    /// <summary>拳の点速度（v + ω×r）。フックのような回転するパンチも正しく測る。</summary>
    public Vector3 GloveVelocity(Hand hand)
    {
        var f = _world.GetState(_arms[(int)hand].Forearm);
        return f.LinearVelocity + Vector3.Cross(f.AngularVelocity, GloveCenter(hand) - f.Position);
    }

    /// <summary>上腕と前腕の質量（体を使わずに拳に乗る質量）。</summary>
    public float ArmMass(Hand hand) => _world.GetMass(_arms[(int)hand].UpperArm) + _world.GetMass(_arms[(int)hand].Forearm);

    /// <summary>
    /// いまの姿勢で、拳が方向 <paramref name="direction"/> に当たったときに乗る質量（<see cref="PunchMechanics"/>）。
    /// 腕の質量 + 腕の連結度 × 体の有効質量。
    /// </summary>
    public float ImpactMass(Hand hand, Vector3 direction) => ImpactMass(hand, direction, out _);

    public float ImpactMass(Hand hand, Vector3 direction, out float bodyShare)
    {
        var arm = _arms[(int)hand];
        var glove = arm.GlovePosition(_world);
        var bodyEffective = PunchMechanics.BodyEffectiveMass(_inertia, TorsoOrientation, glove - CenterOfMass, direction);
        var coupling = PunchMechanics.Coupling(glove - arm.ShoulderPosition(_world, Parts.Chest), direction, jointIntegrity: 1f);
        var total = PunchMechanics.ImpactMass(ArmMass(hand), bodyEffective, coupling);
        bodyShare = (total - ArmMass(hand)) / total;
        return total;
    }

    /// <summary>
    /// 部位 <paramref name="part"/> のどこに <paramref name="point"/> が当たったか（部位ローカルを半径で割った値）と、
    /// 腕の部位ならその腕の手（肩寄り・肘寄りの判定と、どちらの肩・肘が痛むかに使う）。
    /// </summary>
    public (BodyPartKind Kind, Vector3 Normalized, Hand ArmHand) Locate(int part, Vector3 point)
    {
        var s = _world.GetState(part);
        var half = HalfExtentsOf(part);
        var local = Vector3.Clamp(Vector3.Transform(point - s.Position, Quaternion.Conjugate(s.Orientation)), -half, half) / half;
        var p = Parts;
        // Aigis の "Right" 部位は +X 側＝本人の左（FighterCommand / Arm.Create と同じ対応）。
        var armHand = part == p.RightUpperArm || part == p.RightForearm ? Hand.Left : Hand.Right;
        return (KindOf(part), local, armHand);
    }

    /// <summary>自分の剛体 <paramref name="part"/> がどの種類の部位か。</summary>
    public BodyPartKind KindOf(int part)
    {
        var p = Parts;
        return part == p.Head ? BodyPartKind.Head
            : part == p.Chest ? BodyPartKind.Chest
            : part == p.Pelvis ? BodyPartKind.Pelvis
            : part == p.LeftUpperArm || part == p.RightUpperArm ? BodyPartKind.UpperArm
            : part == p.LeftForearm || part == p.RightForearm ? BodyPartKind.Forearm
            : part == p.LeftThigh || part == p.RightThigh ? BodyPartKind.Thigh
            : part == p.LeftShin || part == p.RightShin ? BodyPartKind.Shin
            : BodyPartKind.Foot;
    }

    /// <summary><paramref name="point"/> から自分の部位 <paramref name="part"/>（箱）までの距離。</summary>
    public float DistanceToPart(int part, Vector3 point) => Vector3.Distance(ClosestPointOnBox(this, part, point), point);

    public Vector3 HandTargetWorld(Hand hand) => _arms[(int)hand].LastTarget;

    public bool IsHolding(Hand hand) => _arms[(int)hand].GrabConstraint is not null;

    /// <summary>
    /// ラグドールの首には相対角速度を 0 にする強い減衰モーター（減衰係数 100 N·m·s/rad）があり、頭の質量では
    /// 首が実質固まって揺れない。これを外し、頭と胴は <see cref="FighterTuning.NeckServoHz"/> のバネダンパだけでつなぐ。
    /// 外すのは減衰だけで、可動域の制限（スイング・ねじれ）と位置の拘束は残す。
    /// </summary>
    private static void RemoveNeckDamper(PhysicsWorld world, IReadOnlyList<HumanoidRagdoll.JointGroup> joints)
    {
        var neck = joints.Single(j => j.Joint == HumanoidRagdoll.RagdollJoint.Neck);
        // BallJoint の構成は [位置, スイング制限, ねじれ制限, 減衰モーター]（HumanoidRagdoll.BallJoint）。
        if (neck.AllConstraintIds.Count != 4 || !world.RemoveConstraint(neck.AllConstraintIds[3]))
        {
            throw new InvalidOperationException("HumanoidRagdoll's neck joint layout changed; cannot find its damping motor.");
        }
    }

    /// <summary>足裏を <paramref name="feetPosition"/> に置き、<paramref name="yawRadians"/> を向いて立つファイターを生成する。</summary>
    public static Fighter Spawn(PhysicsWorld world, FighterTuning tuning, string name, Vector3 feetPosition, float yawRadians)
    {
        ArgumentNullException.ThrowIfNull(world);
        ArgumentNullException.ThrowIfNull(tuning);
        var halfExtents = tuning.HalfExtents;

        // HumanoidRagdoll は +Z 向き・T ポーズで生成される。足裏高さを測ってから全体を持ち上げ、ヨー回転する。
        var (parts, built, joints) = HumanoidRagdoll.BuildDetachable(world, feetPosition + new Vector3(0f, 1f, 0f), name, tuning.Masses, halfExtents, jointMotorScale: tuning.TorqueScale);
        RemoveNeckDamper(world, joints);
        var footBottom = world.GetState(parts.LeftFoot).Position.Y - built.Foot.Y;
        var lift = feetPosition.Y - footBottom + 0.002f;
        var pivot = world.GetState(parts.Pelvis).Position;
        var rotation = Quaternion.CreateFromAxisAngle(Vector3.UnitY, yawRadians);
        foreach (var id in parts.All)
        {
            // 生成直後（未ステップ・速度 0）に限り剛体を剛体ごと回すのは安全（関節はローカル定義なので保たれる）。
            var s = world.GetState(id);
            var position = pivot + Vector3.Transform(s.Position - pivot, rotation) + new Vector3(0f, lift, 0f);
            world.SetDynamicPose(id, position, Quaternion.Concatenate(s.Orientation, rotation));
        }

        return new Fighter(world, tuning, name, parts, built, yawRadians);
    }

    /// <summary>1 サブステップ分の制御を力積で与える（物理ステップの直前に呼ぶ）。指令は遅れなくそのまま体へ届く。</summary>
    public void ApplyControl(in FighterCommand command, float dt)
    {
        LastCommand = command;
        _stateTime += dt;
        DownTime = State == FighterState.Standing ? 0f : DownTime + dt;
        var grounded = TryFindGround(out var height);

        switch (State)
        {
            case FighterState.Standing:
                TickStanding(command, grounded, height, dt);
                break;
            case FighterState.Down:
                TickDown(command, grounded, height, dt);
                break;
            case FighterState.GettingUp:
                TickGettingUp(command, grounded, height, dt);
                break;
        }
    }

    /// <summary>体ごと押す（重いパンチ・投げ）。<paramref name="velocityChange"/> は全部位へ一様に（胴だけだと 4 割しか伝わらない／Triage P-006）。</summary>
    public void Push(Vector3 velocityChange)
    {
        if (velocityChange.LengthSquared() <= 0f)
        {
            return;
        }

        foreach (var id in _all)
        {
            _world.ApplyLinearImpulse(id, velocityChange * _world.GetMass(id));
        }
    }

    public void Knockdown(Vector3 direction, bool permanent)
    {
        _stayDown |= permanent;
        if (State == FighterState.Down)
        {
            return;
        }

        Enter(FighterState.Down);
        var flat = new Vector3(direction.X, 0f, direction.Z);
        if (flat.LengthSquared() > 1e-4f)
        {
            _world.ApplyLinearImpulse(Parts.Chest, Vector3.Normalize(flat) * (0.6f * _world.GetMass(Parts.Chest)));
        }
    }

    internal void SetGrabbed(bool grabbed) => _grabbedByCount = Math.Max(0, _grabbedByCount + (grabbed ? 1 : -1));

    /// <summary>グリップの立ち上がりで <paramref name="opponent"/> の最寄り部位を掴む。離した瞬間の手速度が速ければ投げ。</summary>
    internal ThrowResult? UpdateGrab(Hand hand, bool gripHeld, Fighter opponent)
    {
        var arm = _arms[(int)hand];
        ThrowResult? thrown = null;
        if (arm.GrabConstraint is { } id && !_world.HasConstraint(id))
        {
            Release(arm, opponent, allowThrow: false); // 千切れた
        }

        if (State != FighterState.Standing)
        {
            Release(arm, opponent, allowThrow: false);
        }
        else if (gripHeld && !arm.GripWasHeld && arm.GrabConstraint is null)
        {
            TryGrab(arm, opponent);
        }
        else if (!gripHeld && arm.GrabConstraint is not null)
        {
            thrown = Release(arm, opponent, allowThrow: true);
        }

        arm.GripWasHeld = gripHeld;
        return thrown is null ? null : thrown.Value with { Hand = hand };
    }

    internal void ReleaseAll(Fighter opponent)
    {
        foreach (var arm in _arms)
        {
            Release(arm, opponent, allowThrow: false);
        }
    }

    private void TryGrab(Arm arm, Fighter opponent)
    {
        var glove = arm.GlovePosition(_world);
        var best = -1;
        var bestPoint = Vector3.Zero;
        var bestDistance = _tuning.GrabRadius;
        foreach (var part in opponent._all)
        {
            var closest = ClosestPointOnBox(opponent, part, glove);
            var distance = Vector3.Distance(closest, glove);
            if (distance < bestDistance)
            {
                bestDistance = distance;
                best = part;
                bestPoint = closest;
            }
        }

        if (best < 0)
        {
            return;
        }

        var target = _world.GetState(best);
        var localOnTarget = Vector3.Transform(bestPoint - target.Position, Quaternion.Conjugate(target.Orientation));
        var constraint = _world.AddBallSocket(arm.Forearm, best, arm.GloveLocal, localOnTarget, frequency: 30f, dampingRatio: 1f);
        _world.MakeBreakable(constraint, _tuning.GrabBreakForce);
        arm.GrabConstraint = constraint;
        arm.Grabbed = opponent;
        opponent.SetGrabbed(true);
    }

    private ThrowResult? Release(Arm arm, Fighter opponent, bool allowThrow)
    {
        if (arm.GrabConstraint is not { } id)
        {
            return null;
        }

        if (_world.HasConstraint(id))
        {
            _world.RemoveConstraint(id, forget: true);
        }

        arm.GrabConstraint = null;
        var victim = arm.Grabbed ?? opponent;
        arm.Grabbed = null;
        victim.SetGrabbed(false);
        if (!allowThrow)
        {
            return null;
        }

        var handVelocity = _world.GetState(arm.Forearm).LinearVelocity;
        if (handVelocity.Length() < _tuning.ThrowSpeed)
        {
            return null;
        }

        victim.Push(handVelocity * _tuning.ThrowAssist);
        victim.Knockdown(handVelocity, permanent: false);
        return new ThrowResult(arm.Hand, victim);
    }

    private static Vector3 ClosestPointOnBox(Fighter owner, int part, Vector3 point)
    {
        var s = owner._world.GetState(part);
        var half = owner.HalfExtentsOf(part);
        var local = Vector3.Transform(point - s.Position, Quaternion.Conjugate(s.Orientation));
        var clamped = Vector3.Clamp(local, -half, half);
        return s.Position + Vector3.Transform(clamped, s.Orientation);
    }

    internal Vector3 HalfExtentsOf(int part)
    {
        var h = HalfExtents;
        var p = Parts;
        return part == p.Pelvis ? h.Pelvis
            : part == p.Chest ? h.Chest
            : part == p.Head ? h.Head
            : part == p.LeftUpperArm || part == p.RightUpperArm ? h.UpperArm
            : part == p.LeftForearm || part == p.RightForearm ? h.Forearm
            : part == p.LeftThigh || part == p.RightThigh ? h.Thigh
            : part == p.LeftShin || part == p.RightShin ? h.Shin
            : h.Foot;
    }

    private void TickStanding(in FighterCommand command, bool grounded, float height, float dt)
    {
        _noGroundTime = grounded ? 0f : _noGroundTime + dt;
        _tiltTime = Uprightness < _tuning.FallUprightness ? _tiltTime + dt : 0f;
        // 倒れるのは物理的にバランスを失ったとき（足が浮いた・大きく傾いた）だけ。ダメージでは倒れない。
        if (_noGroundTime > _tuning.NoGroundFallTime || _tiltTime > _tuning.FallGrace)
        {
            Enter(FighterState.Down);
            return;
        }

        var scale = IsGrabbed ? _tuning.GrabbedDriveScale : 1f;
        DriveBody(command, scale, grounded, height, dt);
    }

    private void TickDown(in FighterCommand command, bool grounded, float height, float dt)
    {
        // こらえて崩れる（Triage ADR-102④）: 駆動を一気に抜かず CollapseDuration かけて落とす。
        ControlScale = Ramp(1f, _tuning.DownDriveScale, _stateTime, _tuning.CollapseDuration);
        SetServoScale(ControlScale);
        DriveArms(command, ControlScale, weightShift: false, dt);
        var settled = _world.GetState(Parts.Pelvis).LinearVelocity.Length() < 0.6f;
        if (!_stayDown && _stateTime > _tuning.DownTime && settled && grounded && height < _standHeight * 1.6f)
        {
            Enter(FighterState.GettingUp);
        }
    }

    private void TickGettingUp(in FighterCommand command, bool grounded, float height, float dt)
    {
        if (!grounded)
        {
            Enter(FighterState.Down);
            return;
        }

        DriveBody(command, Ramp(0.4f, 1.2f, _stateTime, _tuning.GetUpDuration), true, height, dt);
        if (_stateTime >= _tuning.GetUpDuration && Uprightness > _tuning.RecoverUprightness)
        {
            Enter(FighterState.Standing);
        }
        else if (_stateTime > _tuning.GetUpDuration * 3f)
        {
            Enter(FighterState.Down);
        }
    }

    private void Enter(FighterState next)
    {
        State = next;
        _stateTime = 0f;
        _tiltTime = 0f;
        _noGroundTime = 0f;
    }

    private void DriveBody(in FighterCommand command, float scale, bool grounded, float height, float dt)
    {
        ControlScale = Math.Clamp(scale, 0f, 1f);
        SetServoScale(scale);
        if (grounded)
        {
            ApplyHover(command, height, scale, dt);
        }

        ApplyFacingAndUpright(command, scale, dt);
        ApplyMovement(command, scale, dt);
        ApplyGait(dt);
        DriveArms(command, ControlScale, weightShift: grounded && State == FighterState.Standing, dt);
    }

    private void ApplyHover(in FighterCommand command, float height, float scale, float dt)
    {
        var bob = MathF.Sin(_gaitPhase * 2f) * 0.04f * _standHeight * Math.Clamp(_smoothedSpeed / _tuning.WalkSpeed, 0f, 1f);
        var hoverScale = State == FighterState.GettingUp ? scale * Lerp(0.5f, 1f, Math.Clamp(Uprightness, 0f, 1f)) : scale;
        // 立つ高さ: 頭を下げればダッキング（沈み込み）。
        var crouch = Math.Clamp(command.HeadOffset.Y / _standHeight, -0.3f, 0.05f);
        var target = _standHeight * _tuning.StandHeightRatio * (1f + crouch);
        var pelvis = _world.GetState(Parts.Pelvis);
        var accel = FighterMath.HoverAccel(-_world.Gravity.Y, target + bob - height,
            pelvis.LinearVelocity.Y, _tuning.HoverKp, _tuning.HoverKd, _tuning.MaxHoverAccel, hoverScale);
        ApplySharedImpulse(new Vector3(0f, accel * TotalMass * dt, 0f));
    }

    /// <summary>
    /// 体の軸の向き: 向き（相手へロックオン）＋ 両手の位置からのひねり（前に出た手・横切る手の側の肩が前へ）
    /// ＋ 頭の位置からの前傾・横傾き。腰は胸のひねりの一部だけ回る。
    /// </summary>
    private void ApplyFacingAndUpright(in FighterCommand command, float scale, float dt)
    {
        if (command.FaceDirection.LengthSquared() > 1e-4f)
        {
            var desired = BodyFrame.YawOf(command.FaceDirection, _facingYaw);
            var maxStep = _tuning.TurnSpeedRadians * dt;
            _facingYaw += Math.Clamp(BodyFrame.DeltaAngle(_facingYaw, desired), -maxStep, maxStep);
        }

        var twist = PunchMechanics.TorsoTwist(command.Left.TargetLocal, command.Right.TargetLocal, ArmReach, _tuning.MaxTorsoTwistRadians);
        var torsoLength = MathF.Max(0.05f, HeadPosition.Y - PelvisPosition.Y);
        var head = command.HeadOffset;
        var moveLean = Math.Clamp(_forwardSpeed / _tuning.WalkSpeed, -1f, 1f) * (6f * MathF.PI / 180f);
        var pitch = Math.Clamp(moveLean + MathF.Atan2(head.Z, torsoLength), -_tuning.MaxLeanRadians, _tuning.MaxLeanRadians);
        var roll = Math.Clamp(MathF.Atan2(-head.X, torsoLength), -_tuning.MaxLeanRadians, _tuning.MaxLeanRadians); // 頭を右（−X）へ → 右へ傾く
        // 姿勢 = 向き（ヨー）×（向きの座標系での）傾き × 胴自身のひねり。傾きは向きの座標系で与えるので、
        // ひねっていても「前へ出した頭」は前傾のまま。傾きは主に胸、腰はその一部（脚の付け根が後ろへ振れないように）。
        var facing = Quaternion.CreateFromAxisAngle(Vector3.UnitY, _facingYaw);
        Quaternion Upright(float twistShare, float leanShare)
        {
            var lean = Quaternion.Concatenate(Quaternion.CreateFromAxisAngle(Vector3.UnitZ, roll * leanShare), Quaternion.CreateFromAxisAngle(Vector3.UnitX, pitch * leanShare));
            return Quaternion.Concatenate(Quaternion.Concatenate(Quaternion.CreateFromAxisAngle(Vector3.UnitY, twist * twistShare), lean), facing);
        }

        ApplyUprightImpulse(Parts.Pelvis, Upright(_tuning.HipTwistShare, 0.25f), scale, dt);
        ApplyUprightImpulse(Parts.Chest, Upright(1f, 1f), scale * 0.9f, dt);
    }

    private void ApplyUprightImpulse(int body, Quaternion target, float scale, float dt)
    {
        var s = _world.GetState(body);
        var accel = (FighterMath.RotationError(s.Orientation, target) * _tuning.UprightKp) - (s.AngularVelocity * _tuning.UprightKd);
        accel = ClampLength(accel * scale, _tuning.MaxUprightAccel);
        // 加速度を慣性で力積へ（PhysicsWorld.GetLocalInertiaDiagonal の推奨式）。
        var local = Vector3.Transform(accel, Quaternion.Conjugate(s.Orientation));
        var impulse = Vector3.Transform(local * _world.GetLocalInertiaDiagonal(body) * dt, s.Orientation);
        _world.ApplyAngularImpulse(body, impulse);
    }

    private void ApplyMovement(in FighterCommand command, float scale, float dt)
    {
        var pelvis = _world.GetState(Parts.Pelvis);
        var accel = FighterMath.MoveAccel(pelvis.LinearVelocity, command.ClampedMove * _tuning.WalkSpeed, _tuning.MoveGain, _tuning.MaxMoveAccel);
        ApplySharedImpulse(accel * (TotalMass * scale * dt));
        var v = pelvis.LinearVelocity;
        var blend = 1f - MathF.Exp(-6f * dt);
        _smoothedSpeed = Lerp(_smoothedSpeed, new Vector2(v.X, v.Z).Length(), blend);
        _forwardSpeed = Lerp(_forwardSpeed, Vector3.Dot(v, new BodyFrame(Vector3.Zero, _facingYaw).Forward), blend);
    }

    private void ApplyGait(float dt)
    {
        var speed = _smoothedSpeed;
        var blend = Math.Clamp(speed / MathF.Max(0.01f, _tuning.WalkSpeed), 0f, 1f);
        var direction = _forwardSpeed < -0.25f * speed ? -1f : 1f; // 後退中は位相を逆回し
        _gaitPhase += direction * 2f * MathF.PI * speed / MathF.Max(0.01f, _tuning.StrideLength) * dt;
        var pose = FighterMath.Gait(_gaitPhase, blend, _tuning.NeutralHipRadians, _tuning.NeutralKneeRadians,
            _tuning.GaitHipSwingRadians, _tuning.GaitKneeLiftRadians);
        _world.SetAngularServoTarget(_legs.LeftHip, Quaternion.CreateFromAxisAngle(Vector3.UnitX, pose.LeftHip));
        _world.SetAngularServoTarget(_legs.RightHip, Quaternion.CreateFromAxisAngle(Vector3.UnitX, pose.RightHip));
        _world.SetTwistServoTarget(_legs.LeftKnee, pose.LeftKnee);
        _world.SetTwistServoTarget(_legs.RightKnee, pose.RightKnee);
        // 足裏を水平に保つ: 足首 = −(股 + 膝)（3 関節とも同じ矢状軸・同じ符号規約）。
        _world.SetTwistServoTarget(_legs.LeftAnkle, -(pose.LeftHip + pose.LeftKnee));
        _world.SetTwistServoTarget(_legs.RightAnkle, -(pose.RightHip + pose.RightKnee));
    }

    private void DriveArms(in FighterCommand command, float scale, bool weightShift, float dt)
    {
        var frame = Frame;
        var com = CenterOfMass;
        var torso = TorsoOrientation;
        foreach (var arm in _arms)
        {
            var bodyForce = arm.Drive(_world, _tuning, frame, command.For(arm.Hand).TargetLocal, scale, _inertia, com, torso, TotalMass, dt, Parts.Chest);
            if (weightShift && bodyForce.LengthSquared() > 0f)
            {
                // 体の質量を借りた分は、脚と腰が地面を押して出す力。胴も拳の方向へ少し運ばれる（体重移動）。
                ApplySharedImpulse(bodyForce * (_tuning.WeightShiftShare * dt));
            }
        }
    }

    private void SetServoScale(float scale)
    {
        var s = Math.Clamp(scale, 0.01f, 1.5f);
        var t = _tuning.TorqueScale * s;
        var legs = t;
        // 値が変わったときだけ Bepu の拘束を書き換える（毎サブステップ 9 本の Get/ApplyDescription を避ける）。
        if (Changed(0, legs))
        {
            _world.SetAngularServoSettings(_legs.LeftHip, _tuning.LegServoHz, 120f * legs);
            _world.SetAngularServoSettings(_legs.RightHip, _tuning.LegServoHz, 120f * legs);
            _world.SetTwistServoSettings(_legs.LeftKnee, _tuning.LegServoHz, 100f * legs);
            _world.SetTwistServoSettings(_legs.RightKnee, _tuning.LegServoHz, 100f * legs);
        }

        if (Changed(1, t))
        {
            _world.SetAngularServoSettings(_neck, _tuning.NeckServoHz, _tuning.NeckMaxTorque * t, _tuning.NeckDampingRatio);
        }

        foreach (var arm in _arms)
        {
            if (Changed(2 + (int)arm.Hand, s))
            {
                arm.SetStrength(_world, _tuning, s, s);
            }
        }
    }

    private bool Changed(int slot, float value)
    {
        if (MathF.Abs(_servoSettings[slot] - value) <= 0.005f * MathF.Max(1e-3f, MathF.Abs(value)))
        {
            return false;
        }

        _servoSettings[slot] = value;
        return true;
    }

    // P-002: 骨盤 1 点に掛けると質量比で発散しやすいので、骨盤と胸へ質量比で配分する。
    private void ApplySharedImpulse(Vector3 impulse)
    {
        var pelvisMass = _world.GetMass(Parts.Pelvis);
        var chestMass = _world.GetMass(Parts.Chest);
        var total = pelvisMass + chestMass;
        _world.ApplyLinearImpulse(Parts.Pelvis, impulse * (pelvisMass / total));
        _world.ApplyLinearImpulse(Parts.Chest, impulse * (chestMass / total));
    }

    private bool TryFindGround(out float height)
    {
        var pelvis = _world.GetState(Parts.Pelvis).Position;
        // リングもロープも静的。自分や相手の体（動的）は地面とみなさない。
        var hit = _world.Raycast(pelvis, -Vector3.UnitY, _standHeight * 3f, includeStatics: true, includeKinematics: false, includeDynamics: false);
        height = hit is { } h ? pelvis.Y - h.Point.Y : 0f;
        return hit is not null;
    }

    private Vector3 UpOf(int body) => Vector3.Transform(Vector3.UnitY, _world.GetState(body).Orientation);

    private static float Lerp(float a, float b, float t) => a + ((b - a) * t);

    private static float Ramp(float from, float to, float elapsed, float duration)
        => Lerp(from, to, duration <= 0f ? 1f : Math.Clamp(elapsed / duration, 0f, 1f));

    private static Vector3 ClampLength(Vector3 v, float max)
    {
        var length = v.Length();
        return length > max ? v * (max / length) : v;
    }

    /// <summary>投げの成立（投げた手・投げられた相手）。</summary>
    internal readonly record struct ThrowResult(Hand Hand, Fighter Victim);

    /// <summary>片腕ぶんのサーボ・IK・拳 PD の状態。</summary>
    private sealed class Arm
    {
        private Vector3 _previousTarget;
        private Vector3 _targetVelocity;
        private bool _hasPrevious;

        private Arm(Hand hand)
        {
            Hand = hand;
        }

        public Hand Hand { get; }
        /// <summary>この腕の肩がある側（体のローカル X 符号）。</summary>
        public float Side => Hand.LocalSide();
        public int UpperArm { get; private init; }
        public int Forearm { get; private init; }
        public int ShoulderServo { get; private init; }
        public int ElbowServo { get; private init; }
        public Vector3 ShoulderOnChest { get; private init; }
        public Vector3 ShoulderCenterOnChest { get; private init; }
        public Vector3 GloveLocal { get; private init; }
        public float UpperLength { get; private init; }
        public float LowerLength { get; private init; }
        public Vector3 LastTarget { get; private set; }
        public int? GrabConstraint { get; set; }
        public Fighter? Grabbed { get; set; }
        public bool GripWasHeld { get; set; }

        public static Arm Create(PhysicsWorld world, FighterTuning tuning, HumanoidRagdoll.Parts parts, HumanoidRagdoll.HalfExtents half, Hand hand)
        {
            // Aigis の部位名は −X 側が "Left"。+Z を向いた右手系の体では −X が右手なので、名前を入れ替えて対応させる
            // （物理的に +X 側の腕 = 本人の左手）。
            var side = hand.LocalSide();
            var upper = side > 0f ? parts.RightUpperArm : parts.LeftUpperArm;
            var fore = side > 0f ? parts.RightForearm : parts.LeftForearm;
            var chest = world.GetState(parts.Chest);
            var u = world.GetState(upper);
            var f = world.GetState(fore);
            var inverseChest = Quaternion.Conjugate(chest.Orientation);
            var xAxis = Vector3.Transform(Vector3.UnitX, u.Orientation);

            // HumanoidRagdoll.BuildCore と同じ関節位置（肩 = 上腕中心から体側へ hx−Overlap、肘 = 上腕の遠位端）。
            var shoulder = u.Position - (xAxis * side * (half.UpperArm.X - HumanoidRagdoll.Overlap));
            var elbow = f.Position - (xAxis * side * (half.Forearm.X - HumanoidRagdoll.Overlap));
            var glove = f.Position + (xAxis * side * half.Forearm.X);
            var shoulderOnChest = Vector3.Transform(shoulder - chest.Position, inverseChest);
            var center = shoulderOnChest with { X = 0f };

            // 肘の基底は物理的な側で決まる（+X 側の腕が Mirrored / HumanoidRagdoll.BuildCore と同じ）。
            var basis = side > 0f ? HumanoidRagdoll.YAxisTwistBasisMirrored : HumanoidRagdoll.YAxisTwistBasis;
            var shoulderServo = world.AddAngularServo(parts.Chest, upper, Quaternion.Identity, tuning.ArmServoHz, 1f, tuning.ArmServoMaxSpeed, 0f, 40f * tuning.TorqueScale);
            var elbowServo = world.AddTwistServo(upper, fore, basis, basis, 0f, tuning.ArmServoHz, 1f, tuning.ArmServoMaxSpeed, 0f, 30f * tuning.TorqueScale);

            return new Arm(hand)
            {
                UpperArm = upper,
                Forearm = fore,
                ShoulderServo = shoulderServo,
                ElbowServo = elbowServo,
                ShoulderOnChest = shoulderOnChest,
                ShoulderCenterOnChest = center,
                GloveLocal = Vector3.Transform(glove - f.Position, Quaternion.Conjugate(f.Orientation)),
                UpperLength = Vector3.Distance(shoulder, elbow),
                LowerLength = Vector3.Distance(elbow, glove),
            };
        }

        public Vector3 GlovePosition(PhysicsWorld world)
        {
            var f = world.GetState(Forearm);
            return f.Position + Vector3.Transform(GloveLocal, f.Orientation);
        }

        public Vector3 ShoulderPosition(PhysicsWorld world, int chestId)
        {
            var chest = world.GetState(chestId);
            return chest.Position + Vector3.Transform(ShoulderOnChest, chest.Orientation);
        }

        public void SetStrength(PhysicsWorld world, FighterTuning tuning, float shoulderScale, float elbowScale)
        {
            world.SetAngularServoSettings(ShoulderServo, tuning.ArmServoHz, 40f * tuning.TorqueScale * Math.Max(0.01f, shoulderScale));
            world.SetTwistServoSettings(ElbowServo, tuning.ArmServoHz, 30f * tuning.TorqueScale * Math.Max(0.01f, elbowScale));
        }

        /// <summary>腕を目標へ駆動し、体から借りた力 [N]（脚と腰が地面を押して出す分）を返す。</summary>
        public Vector3 Drive(PhysicsWorld world, FighterTuning tuning, BodyFrame frame, Vector3 targetLocal, float forceScale,
            BodyInertia inertia, Vector3 centerOfMass, Quaternion torso, float totalMass, float dt, int chestId)
        {
            var shoulder = ShoulderPosition(world, chestId);
            var reach = UpperLength + LowerLength;
            var target = ArmIk.ClampReach(shoulder, frame.LocalToWorld(targetLocal), reach);
            LastTarget = target;

            // 目標速度（フィードフォワード）。キャリブレーション等の瞬間移動が力のスパイクにならないよう上限付き。
            if (_hasPrevious)
            {
                var raw = ClampLength((target - _previousTarget) / MathF.Max(1e-4f, dt), tuning.MaxTargetSpeed);
                _targetVelocity = Vector3.Lerp(_targetVelocity, raw, Math.Clamp(tuning.TargetVelocitySmoothing * dt, 0f, 1f));
            }

            _previousTarget = target;
            _hasPrevious = true;

            // 1) IK → 肩・肘サーボの目標（腕の「形」）。肘は下・外・やや後ろへ逃がす。
            var chest = world.GetState(chestId);
            var pole = shoulder + (((-Vector3.UnitY * 1.0f) + (frame.Rotate(Vector3.UnitX * Side) * 0.6f) - (frame.Forward * 0.3f)) * reach);
            var (elbow, hand) = ArmIk.SolveTwoBone(shoulder, target, pole, UpperLength, LowerLength);
            var (upperRotation, elbowAngle) = ArmIk.JointTargets(shoulder, elbow, hand, ArmIk.BendNormal(shoulder, target, pole), Side);
            world.SetAngularServoTarget(ShoulderServo, Quaternion.Concatenate(upperRotation, Quaternion.Conjugate(chest.Orientation)));
            world.SetTwistServoTarget(ElbowServo, Math.Clamp(elbowAngle, 0f, 140f * MathF.PI / 180f));

            // 2) 拳を目標へ PD 力で引く。力の上限は「腕の筋力 × 体の質量をどれだけ借りられるか」（PunchMechanics）:
            //    体軸を通る向き・骨で突っ張れる向きほど脚と腰の力が乗り、軸から外れた手打ちは腕の力だけになる。
            var f = world.GetState(Forearm);
            var glove = f.Position + Vector3.Transform(GloveLocal, f.Orientation);
            var gloveVelocity = f.LinearVelocity + Vector3.Cross(f.AngularVelocity, glove - f.Position);
            var accel = FighterMath.HandAccel(glove, gloveVelocity, target, _targetVelocity, tuning.HandKp, tuning.HandKd);
            var chainMass = world.GetMass(UpperArm) + world.GetMass(Forearm);
            var desired = accel * chainMass;
            var bodyEffective = PunchMechanics.BodyEffectiveMass(inertia, torso, glove - centerOfMass, desired);
            var coupling = PunchMechanics.Coupling(glove - shoulder, desired, jointIntegrity: 1f);
            var armForce = tuning.MaxHandForce * forceScale;
            var cap = armForce * PunchMechanics.ForceMultiplier(coupling, bodyEffective, totalMass, tuning.BodyDrive);
            var force = ClampLength(desired, cap);
            world.ApplyLinearImpulse(Forearm, force * dt);

            // 腕の筋力ぶんの反作用は胸へ（P-002）。体から借りた分は脚が地面を押して出す力として呼び出し側へ返す。
            var armPart = ClampLength(force, armForce);
            world.ApplyLinearImpulse(chestId, -armPart * (tuning.HandReactionShare * dt));
            return force - armPart;
        }
    }
}
