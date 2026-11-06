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
/// <item>体幹: 骨盤ホバー PD + 骨盤/胸の直立 PD + 水平速度 P（力積で与える）。</item>
/// <item>脚: <see cref="HumanoidRagdoll.AddLegServos"/> のサーボに正弦歩行の目標角。</item>
/// <item>腕: 2 ボーン IK → 肩 AngularServo / 肘 TwistServo の目標、さらに拳を目標へ PD 力で引く（反作用は胸へ）。</item>
/// </list>
/// 位置・姿勢を直接書き換えるのは生成直後の向き合わせ（<see cref="Spawn"/>）だけ。
/// </summary>
public sealed class Fighter
{
    private const int GrabSubgroup = HumanoidRagdoll.PartSubgroupCount;

    private readonly PhysicsWorld _world;
    private readonly FighterTuning _tuning;
    private readonly HumanoidRagdoll.LegServos _legs;
    private readonly Arm[] _arms = new Arm[2];
    private readonly int[] _all;
    private readonly int _neck;
    private readonly float _standHeight;
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

        _legs = HumanoidRagdoll.AddLegServos(world, parts, tuning.LegServoHz,
            anklePitchMaxForce: 100f * tuning.TorqueScale, kneeMaxForce: 100f * tuning.TorqueScale);
        world.SetAngularServoSettings(_legs.LeftHip, tuning.LegServoHz, 120f * tuning.TorqueScale);
        world.SetAngularServoSettings(_legs.RightHip, tuning.LegServoHz, 120f * tuning.TorqueScale);
        // 首は Aigis の既定では受動（ダンパのみ）で頭が転がるので、直立へ戻すサーボを足す。
        _neck = world.AddAngularServo(parts.Chest, parts.Head, Quaternion.Identity, tuning.NeckServoHz, 1f, 10f, 0f, 20f * tuning.TorqueScale);
        _arms[(int)Hand.Left] = Arm.Create(world, tuning, parts, halfExtents, Hand.Left);
        _arms[(int)Hand.Right] = Arm.Create(world, tuning, parts, halfExtents, Hand.Right);
    }

    public string Name { get; }
    public HumanoidRagdoll.Parts Parts { get; }
    public HumanoidRagdoll.HalfExtents HalfExtents { get; }
    public float TotalMass { get; }
    public FighterState State { get; private set; }
    public float Stun01 { get; private set; }
    public FighterCommand LastCommand { get; private set; }
    public bool IsGrabbed => _grabbedByCount > 0;
    public float ArmReach => _arms[0].UpperLength + _arms[0].LowerLength;

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

    public bool IsForearm(int bodyId) => bodyId == Parts.LeftForearm || bodyId == Parts.RightForearm;

    /// <summary><paramref name="point"/> から自分の部位 <paramref name="part"/>（箱）までの距離。</summary>
    public float DistanceToPart(int part, Vector3 point) => Vector3.Distance(ClosestPointOnBox(this, part, point), point);

    public Vector3 HandTargetWorld(Hand hand) => _arms[(int)hand].LastTarget;

    public bool IsHolding(Hand hand) => _arms[(int)hand].GrabConstraint is not null;

    /// <summary>足裏を <paramref name="feetPosition"/> に置き、<paramref name="yawRadians"/> を向いて立つファイターを生成する。</summary>
    public static Fighter Spawn(PhysicsWorld world, FighterTuning tuning, string name, Vector3 feetPosition, float yawRadians)
    {
        ArgumentNullException.ThrowIfNull(world);
        ArgumentNullException.ThrowIfNull(tuning);
        var halfExtents = tuning.HalfExtents;

        // HumanoidRagdoll は +Z 向き・T ポーズで生成される。足裏高さを測ってから全体を持ち上げ、ヨー回転する。
        var (parts, built) = HumanoidRagdoll.Build(world, feetPosition + new Vector3(0f, 1f, 0f), name, tuning.Masses, halfExtents, jointMotorScale: tuning.TorqueScale);
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

    /// <summary>1 サブステップ分の制御を力積で与える（物理ステップの直前に呼ぶ）。</summary>
    public void ApplyControl(in FighterCommand command, float dt)
    {
        LastCommand = command;
        _stateTime += dt;
        Stun01 = MathF.Max(0f, Stun01 - (_tuning.StunDecayPerSecond * dt));
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

    /// <summary>被弾によるよろめき。<paramref name="velocityChange"/> は全部位へ一様に（胴だけだと 4 割しか伝わらない／Triage P-006）。</summary>
    public void AddStun(float amount, Vector3 velocityChange)
    {
        Stun01 = Math.Clamp(Stun01 + amount, 0f, 1f);
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

        victim.AddStun(1f, handVelocity * _tuning.ThrowAssist);
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
        if (_noGroundTime > _tuning.NoGroundFallTime || _tiltTime > _tuning.FallGrace)
        {
            Enter(FighterState.Down);
            return;
        }

        var scale = Lerp(1f, _tuning.StunnedDriveScale, Stun01);
        if (IsGrabbed)
        {
            scale *= _tuning.GrabbedDriveScale;
        }

        DriveBody(command, scale, grounded, height, dt);
    }

    private void TickDown(in FighterCommand command, bool grounded, float height, float dt)
    {
        // こらえて崩れる（Triage ADR-102④）: 駆動を一気に抜かず CollapseDuration かけて落とす。
        ControlScale = Ramp(1f, _tuning.DownDriveScale, _stateTime, _tuning.CollapseDuration);
        SetServoScale(ControlScale);
        DriveArms(command, ControlScale, dt);
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
        if (next == FighterState.Standing)
        {
            Stun01 = 0f;
        }
    }

    private void DriveBody(in FighterCommand command, float scale, bool grounded, float height, float dt)
    {
        ControlScale = Math.Clamp(scale, 0f, 1f);
        SetServoScale(scale);
        if (grounded)
        {
            ApplyHover(height, scale, dt);
        }

        ApplyFacingAndUpright(command, scale, dt);
        ApplyMovement(command, scale, dt);
        ApplyGait(dt);
        DriveArms(command, ControlScale, dt);
    }

    private void ApplyHover(float height, float scale, float dt)
    {
        var bob = MathF.Sin(_gaitPhase * 2f) * 0.04f * _standHeight * Math.Clamp(_smoothedSpeed / _tuning.WalkSpeed, 0f, 1f);
        var hoverScale = State == FighterState.GettingUp ? scale * Lerp(0.5f, 1f, Math.Clamp(Uprightness, 0f, 1f)) : scale;
        var pelvis = _world.GetState(Parts.Pelvis);
        var accel = FighterMath.HoverAccel(-_world.Gravity.Y, (_standHeight * _tuning.StandHeightRatio) + bob - height,
            pelvis.LinearVelocity.Y, _tuning.HoverKp, _tuning.HoverKd, _tuning.MaxHoverAccel, hoverScale);
        ApplySharedImpulse(new Vector3(0f, accel * TotalMass * dt, 0f));
    }

    private void ApplyFacingAndUpright(in FighterCommand command, float scale, float dt)
    {
        if (command.FaceDirection.LengthSquared() > 1e-4f)
        {
            var desired = BodyFrame.YawOf(command.FaceDirection, _facingYaw);
            var maxStep = _tuning.TurnSpeedRadians * dt;
            _facingYaw += Math.Clamp(BodyFrame.DeltaAngle(_facingYaw, desired), -maxStep, maxStep);
        }

        // 前進なら前傾・後退なら後傾（向きは相手にロックしたまま下がれる）。
        var lean = Math.Clamp(_forwardSpeed / _tuning.WalkSpeed, -1f, 1f) * (6f * MathF.PI / 180f);
        var target = Quaternion.Concatenate(Quaternion.CreateFromAxisAngle(Vector3.UnitX, lean), Quaternion.CreateFromAxisAngle(Vector3.UnitY, _facingYaw));
        ApplyUprightImpulse(Parts.Pelvis, target, scale, dt);
        ApplyUprightImpulse(Parts.Chest, target, scale * 0.9f, dt);
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

    private void DriveArms(in FighterCommand command, float scale, float dt)
    {
        var frame = Frame;
        foreach (var arm in _arms)
        {
            arm.Drive(_world, _tuning, frame, command.For(arm.Hand).TargetLocal, scale, dt, Parts.Chest);
        }
    }

    private void SetServoScale(float scale)
    {
        var s = Math.Clamp(scale, 0.01f, 1.5f);
        var t = _tuning.TorqueScale * s;
        _world.SetAngularServoSettings(_legs.LeftHip, _tuning.LegServoHz, 120f * t);
        _world.SetAngularServoSettings(_legs.RightHip, _tuning.LegServoHz, 120f * t);
        _world.SetAngularServoSettings(_neck, _tuning.NeckServoHz, 20f * t);
        foreach (var arm in _arms)
        {
            arm.SetStrength(_world, _tuning, s);
        }
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

        public void SetStrength(PhysicsWorld world, FighterTuning tuning, float scale)
        {
            world.SetAngularServoSettings(ShoulderServo, tuning.ArmServoHz, 40f * tuning.TorqueScale * scale);
        }

        public void Drive(PhysicsWorld world, FighterTuning tuning, BodyFrame frame, Vector3 targetLocal, float scale, float dt, int chestId)
        {
            var chest = world.GetState(chestId);
            var shoulder = chest.Position + Vector3.Transform(ShoulderOnChest, chest.Orientation);
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
            var pole = shoulder + (((-Vector3.UnitY * 1.0f) + (frame.Rotate(Vector3.UnitX * Side) * 0.6f) - (frame.Forward * 0.3f)) * reach);
            var (elbow, hand) = ArmIk.SolveTwoBone(shoulder, target, pole, UpperLength, LowerLength);
            var (upperRotation, elbowAngle) = ArmIk.JointTargets(shoulder, elbow, hand, ArmIk.BendNormal(shoulder, target, pole), Side);
            world.SetAngularServoTarget(ShoulderServo, Quaternion.Concatenate(upperRotation, Quaternion.Conjugate(chest.Orientation)));
            world.SetTwistServoTarget(ElbowServo, Math.Clamp(elbowAngle, 0f, 140f * MathF.PI / 180f));

            // 2) 拳を目標へ PD 力で引く（パンチの勢いの本体）。反作用を胸へ返す（P-002）。
            var f = world.GetState(Forearm);
            var glove = f.Position + Vector3.Transform(GloveLocal, f.Orientation);
            var gloveVelocity = f.LinearVelocity + Vector3.Cross(f.AngularVelocity, glove - f.Position);
            var accel = FighterMath.HandAccel(glove, gloveVelocity, target, _targetVelocity, tuning.HandKp, tuning.HandKd);
            var chainMass = world.GetMass(UpperArm) + world.GetMass(Forearm);
            var force = ClampLength(accel * chainMass, tuning.MaxHandForce) * scale;
            world.ApplyLinearImpulse(Forearm, force * dt);
            world.ApplyLinearImpulse(chestId, -force * (tuning.HandReactionShare * dt));
        }
    }
}
