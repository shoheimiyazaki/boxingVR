using System.Numerics;
using Aigis.Physics;
using Aigis.Physics.Voxels;
using MiniRing.Voxels;

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
/// <item>被弾: 体はボクセル（<see cref="Voxels"/>）で、打撃で削れる。肉の残りが腕・脚・胴の力になり、骨が断たれた腕・脚はそこから先が落ち、
/// 臓器が壊れたら死ぬ（<see cref="Die"/>）。<see cref="Voxels"/> が無ければ常にフルパワー。</item>
/// </list>
/// 位置・姿勢を直接書き換えるのは生成直後の向き合わせ（<see cref="Spawn"/>）だけ。
/// </summary>
public sealed class Fighter
{
    private readonly PhysicsWorld _world;
    private readonly FighterTuning _tuning;
    private readonly HumanoidRagdoll.LegServos _legs;

    /// <summary>脚（[<see cref="Footwork.Lead"/>] = 本人の左 = 前足、[<see cref="Footwork.Rear"/>] = 本人の右 = 後ろ足）。</summary>
    private readonly Leg[] _legParts = new Leg[2];
    private readonly Footwork _footwork;
    private bool _footworkActive;

    /// <summary>いまの胴のひねり [rad]（足の運びの軸足の回転に使う）。</summary>
    private float _twist;

    /// <summary>両脚（腿・すね・足・つま先）の質量。</summary>
    private readonly float _legMass;

    /// <summary>骨盤の地面からの高さの目標（調べもの用）。</summary>
    private float _hoverHeight;

    /// <summary>骨盤の高さのずれの積分 [m/s²]（足の運びの間だけ）と、その積分の速さ [1/s²]。</summary>
    private float _hoverIntegral;
    private const float HoverKi = 150f;

    /// <summary>骨盤の姿勢の目標（脚の IK はこの姿勢の骨盤から解く。今の姿勢から解くと、脚は傾いた骨盤を支え直さない）。</summary>
    private Quaternion _pelvisTarget = Quaternion.Identity;

    /// <summary>体が付いていく移動の指令（指令から <see cref="FootworkTuning.BodyLag"/> だけ遅れる）。</summary>
    private Vector3 _laggedMove;

    internal float HoverHeight => _hoverHeight;

    internal (float Chest, float Pelvis) Tilt => (UpOf(Parts.Chest).Y, UpOf(Parts.Pelvis).Y);

    internal (float Lead, float Rear) LegReach => (_legParts[0].ReachRatio, _legParts[1].ReachRatio);

    internal (Vector3 Lead, Vector3 Rear) FootError => (_legParts[0].LastError, _legParts[1].LastError);

    /// <summary>足首の曲げの目標と実際（すねに対する足の X 軸回りの角度）[rad]。</summary>
    internal (float Target, float Actual) Ankle(int foot) => (_legParts[foot].AnkleTarget,
        LegIk.AnklePitch(_world.GetState(foot == Footwork.Lead ? Parts.RightShin : Parts.LeftShin).Orientation, _world.GetState(_legParts[foot].Heel).Orientation));

    internal float StandHeight => _standHeight;
    private readonly Arm[] _arms = new Arm[2];
    private readonly int[] _all;
    private readonly int _neck;

    /// <summary>
    /// 首のバネが目標へ戻す角速度の上限 [rad/s]。減衰比が小さいと戻りの速度が大きく、上限が低いと（Aigis 既定の 10）
    /// そこで頭打ちになってバネとして働かず、頭が可動域の端まで倒れたまま戻らない。
    /// </summary>
    private const float NeckServoMaxSpeed = 60f;
    private readonly float _standHeight;

    /// <summary>
    /// 立った時の頭と骨盤の高さの差。股から折る角度（ヒップヒンジ）を決める胴の長さ
    /// （折るほど縮む今の値で割ると、角度が自分を押し上げて上限まで行く。レビュー指摘）。
    /// </summary>
    private readonly float _restTorso;
    private readonly BodyInertia _inertia;
    private readonly float[] _servoSettings = [-1f, -1f, -1f, -1f, -1f, -1f];
    private float _facingYaw;
    private float _gaitPhase;
    private float _smoothedSpeed;
    private float _forwardSpeed;

    /// <summary>いまの沈み込み（立つ高さに対する割合、負で沈む）。骨盤の高さと脚の曲げを同じ値でそろえる。</summary>
    private float _crouch;
    private float _stateTime;
    private float _tiltTime;
    private float _collapsedTime;
    private float _noGroundTime;
    private int _grabbedByCount;
    private bool _stayDown;
    private readonly List<HumanoidRagdoll.JointGroup> _joints;

    /// <summary>
    /// 切り落とされて体から外れた部位（切れた所より先にぶら下がっていた部位: 上腕が切れたら前腕、腿が切れたらすねと足）。
    /// 体の一部として数えない（打撃・掴み・重心・押す力の対象外）。
    /// </summary>
    private readonly HashSet<int> _detached = [];
    private readonly bool[] _legSevered = new bool[2]; // [0] = Aigis の Left 脚（本人の右）、[1] = Right

    private Fighter(PhysicsWorld world, FighterTuning tuning, string name, HumanoidRagdoll.Parts parts, HumanoidRagdoll.HalfExtents halfExtents, float yaw,
        IReadOnlyList<HumanoidRagdoll.JointGroup> joints, ToeParts toes)
    {
        _world = world;
        _joints = [.. joints];
        _tuning = tuning;
        Name = name;
        Parts = parts;
        HalfExtents = halfExtents;
        _all = [.. parts.All, toes.AigisLeft, toes.AigisRight];
        _facingYaw = yaw;
        TotalMass = _all.Sum(world.GetMass);

        var pelvisY = world.GetState(parts.Pelvis).Position.Y;
        var footBottom = MathF.Min(world.GetState(parts.LeftFoot).Position.Y, world.GetState(parts.RightFoot).Position.Y) - halfExtents.Foot.Y;
        _standHeight = pelvisY - footBottom;
        _restTorso = MathF.Max(0.05f, world.GetState(parts.Head).Position.Y - pelvisY);
        var height = world.GetState(parts.Head).Position.Y + halfExtents.Head.Y - footBottom;
        _inertia = BodyInertia.Cylinder(TotalMass, height, halfExtents.Chest.X);

        _legs = HumanoidRagdoll.AddLegServos(world, parts, tuning.LegServoHz,
            anklePitchMaxForce: tuning.AnkleMaxTorque * tuning.TorqueScale, kneeMaxForce: tuning.KneeMaxTorque * tuning.TorqueScale);
        world.SetAngularServoSettings(_legs.LeftHip, tuning.LegServoHz, tuning.HipMaxTorque * tuning.TorqueScale);
        world.SetAngularServoSettings(_legs.RightHip, tuning.LegServoHz, tuning.HipMaxTorque * tuning.TorqueScale);
        // 首は Aigis の既定では受動（ダンパのみ）で頭が転がるので、直立へ戻すバネダンパ（サーボ）を足す。
        // 減衰比を 1 未満にして、打たれた頭がバネで揺れ戻るようにする（ラグドール側の首の減衰は Spawn で外してある）。
        _neck = world.AddAngularServo(parts.Chest, parts.Head, Quaternion.Identity, tuning.NeckServoHz, tuning.NeckDampingRatio, NeckServoMaxSpeed, 0f, tuning.NeckMaxTorque * tuning.TorqueScale);
        _arms[(int)Hand.Left] = Arm.Create(world, tuning, parts, halfExtents, Hand.Left);
        _arms[(int)Hand.Right] = Arm.Create(world, tuning, parts, halfExtents, Hand.Right);
        // Aigis の "Right" の脚は +X 側 = 本人の左 = 前足（オーソドックス）。
        _legParts[Footwork.Lead] = Leg.Create(world, parts, joints, halfExtents, aigisLeft: false, toes.AigisRight, toes.AigisRightServo, _legs.RightHip, _legs.RightKnee, _legs.RightAnkle);
        _legParts[Footwork.Rear] = Leg.Create(world, parts, joints, halfExtents, aigisLeft: true, toes.AigisLeft, toes.AigisLeftServo, _legs.LeftHip, _legs.LeftKnee, _legs.LeftAnkle);
        _footwork = new Footwork(tuning.Footwork) { MaxTwist = tuning.MaxTorsoTwistRadians };
        _legMass = new[] { parts.LeftThigh, parts.RightThigh, parts.LeftShin, parts.RightShin, parts.LeftFoot, parts.RightFoot, toes.AigisLeft, toes.AigisRight }.Sum(world.GetMass);
    }

    /// <summary>体の剛体すべて（Aigis の 13 部位 + つま先 2 つ）。</summary>
    public IReadOnlyList<int> AllParts => _all;

    /// <summary>足の運び（足の置き場所の計画）。</summary>
    public Footwork Footwork => _footwork;

    /// <summary>つま先の剛体（<see cref="Footwork.Lead"/> / <see cref="Footwork.Rear"/>）。</summary>
    public int ToeId(int foot) => _legParts[foot].Toe;

    /// <summary>かかと側の足の剛体（<see cref="Footwork.Lead"/> / <see cref="Footwork.Rear"/>）。</summary>
    public int HeelId(int foot) => _legParts[foot].Heel;

    /// <summary>
    /// 体の物理の版。足の運び・関節の位置・非線形のダメージ（意識と崩れ落ち）のように、同じ入力から違う試合になる変更で上げる。
    /// 記録はこの版を持ち、違う版の記録は再生しない（黙ってどこかでずれる）。
    /// </summary>
    public const int PhysicsVersion = 3;

    public string Name { get; }
    public HumanoidRagdoll.Parts Parts { get; }
    public HumanoidRagdoll.HalfExtents HalfExtents { get; }
    public float TotalMass { get; }
    public FighterState State { get; private set; }
    public BodyCondition Condition { get; } = new();

    /// <summary>ボクセルの体（打撃で削れる中身）。無ければ常にフルパワーで、切れも死にもしない。</summary>
    public BoxerBody? Voxels { get; internal set; }

    /// <summary>臓器が壊れて死んだ。倒れたまま起き上がらない。</summary>
    public bool IsDead => DeathCause is not null;

    /// <summary>死因（壊れた臓器の名前）。生きていれば null。</summary>
    public string? DeathCause { get; private set; }

    /// <summary>その手の腕が切り落とされた（もう駆動しない）。</summary>
    public bool ArmSevered(Hand hand) => _arms[(int)hand].Severed;

    /// <summary>どちらかの脚が切り落とされた（立てない）。</summary>
    public bool LegSevered => _legSevered[0] || _legSevered[1];

    /// <summary>部位の力（肉の残りから。0..1、肉が無くても <see cref="FighterTuning.MinMuscleStrength"/> は残る）。</summary>
    public float Strength(int part)
    {
        var m = _tuning.MinMuscleStrength;
        return Voxels is null ? 1f : m + ((1f - m) * Voxels.Muscle(part));
    }

    /// <summary>その腕の力（上腕と前腕の平均）。切れていれば 0。</summary>
    public float ArmStrength(Hand hand)
    {
        var arm = _arms[(int)hand];
        return arm.Severed ? 0f : 0.5f * (Strength(arm.UpperArm) + Strength(arm.Forearm));
    }

    /// <summary>脚の力（Aigis の Left / Right 脚。腿とすねの平均）。切れていれば 0。</summary>
    public float LegStrength(bool aigisLeft)
    {
        var p = Parts;
        return _legSevered[aigisLeft ? 0 : 1] ? 0f
            : aigisLeft ? 0.5f * (Strength(p.LeftThigh) + Strength(p.LeftShin)) : 0.5f * (Strength(p.RightThigh) + Strength(p.RightShin));
    }

    /// <summary>胴の力（胸と腰の平均）。</summary>
    public float CoreStrength => 0.5f * (Strength(Parts.Chest) + Strength(Parts.Pelvis));

    /// <summary>立っていられるか: 生きていて、両脚があり、弱い方の脚の力が <see cref="FighterTuning.StandStrength"/> 以上。</summary>
    public bool CanStand => !IsDead && !LegSevered && MathF.Min(LegStrength(true), LegStrength(false)) >= _tuning.StandStrength;

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

    /// <summary>胸の速度（体全体の動きの目安。拳の動きから歩く動きを差し引くのに使う）。</summary>
    public Vector3 ChestVelocity => _world.GetState(Parts.Chest).LinearVelocity;
    public Vector3 PelvisPosition => _world.GetState(Parts.Pelvis).Position;
    public Quaternion TorsoOrientation => _world.GetState(Parts.Chest).Orientation;

    public Vector3 CenterOfMass
    {
        get
        {
            // 削られて軽くなった分と、切り落とされた部位を除いた、今の体の重心。
            var sum = Vector3.Zero;
            var mass = 0f;
            foreach (var id in _all)
            {
                if (_detached.Contains(id))
                {
                    continue;
                }

                var m = _world.GetMass(id);
                sum += _world.GetState(id).Position * m;
                mass += m;
            }

            return sum / MathF.Max(1e-6f, mass);
        }
    }

    public float Uprightness => MathF.Min(UpOf(Parts.Chest).Y, UpOf(Parts.Pelvis).Y);

    /// <summary>その剛体が今この体の一部か（切り落とされて外れた部位は含まない）。</summary>
    public bool Owns(int bodyId) => Array.IndexOf(_all, bodyId) >= 0 && !_detached.Contains(bodyId);

    public int ForearmId(Hand hand) => _arms[(int)hand].Forearm;

    public Vector3 GloveCenter(Hand hand) => _arms[(int)hand].GlovePosition(_world);

    /// <summary>グローブが付いている剛体（前腕。前腕が切れたらその破片）。</summary>
    public int GloveBody(Hand hand) => _arms[(int)hand].GloveBody;

    /// <summary>肩の付け根（ワールド）。</summary>
    public Vector3 ShoulderPosition(Hand hand) => _arms[(int)hand].ShoulderPosition(_world, Parts.Chest);

    /// <summary>拳の点速度（v + ω×r）。フックのような回転するパンチも正しく測る。</summary>
    public Vector3 GloveVelocity(Hand hand)
    {
        var f = _world.GetState(_arms[(int)hand].GloveBody);
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
            : IsToe(part) ? BodyPartKind.Toe
            : BodyPartKind.Foot;
    }

    private bool IsToe(int part) => part == _legParts[0].Toe || part == _legParts[1].Toe;

    /// <summary><paramref name="point"/> から自分の部位 <paramref name="part"/>（箱）までの距離。</summary>
    public float DistanceToPart(int part, Vector3 point) => Vector3.Distance(ClosestPointOnBox(this, part, point), point);

    /// <summary>部位の箱の表面（または内側）で <paramref name="point"/> にいちばん近い点。拳が部位に触れた点。</summary>
    public Vector3 ClosestPointOnPart(int part, Vector3 point) => ClosestPointOnBox(this, part, point);

    /// <summary>体に付いている部位（千切れて落ちた先を除く）の質量の合計 [kg]。押す・反作用の運動量はこの質量で数える。</summary>
    public float AttachedMass => _all.Where(id => !_detached.Contains(id)).Sum(_world.GetMass);

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
        var (parts, built, joints) = HumanoidRagdoll.BuildDetachable(world, feetPosition + new Vector3(0f, 1f, 0f), name, tuning.Masses, halfExtents, jointMotorScale: tuning.TorqueScale,
            overlap: tuning.JointOverlap, footForwardOffset: tuning.FootForwardOffset);
        RemoveNeckDamper(world, joints);
        var footBottom = world.GetState(parts.LeftFoot).Position.Y - built.Foot.Y;
        var lift = feetPosition.Y - footBottom + 0.002f;
        var toes = AddToes(world, tuning, name, parts, built);
        var pivot = world.GetState(parts.Pelvis).Position;
        var rotation = Quaternion.CreateFromAxisAngle(Vector3.UnitY, yawRadians);
        foreach (var id in (int[])[.. parts.All, toes.AigisLeft, toes.AigisRight])
        {
            // 生成直後（未ステップ・速度 0）に限り剛体を剛体ごと回すのは安全（関節はローカル定義なので保たれる）。
            var s = world.GetState(id);
            var position = pivot + Vector3.Transform(s.Position - pivot, rotation) + new Vector3(0f, lift, 0f);
            world.SetDynamicPose(id, position, Quaternion.Concatenate(s.Orientation, rotation));
        }

        return new Fighter(world, tuning, name, parts, built, yawRadians, joints, toes);
    }

    /// <summary>つま先 2 つと、その母指球の蝶番（関節の制限 + サーボ）。T ポーズのまま（回す前）に足す。</summary>
    private static ToeParts AddToes(PhysicsWorld world, FighterTuning tuning, string name, HumanoidRagdoll.Parts parts, HumanoidRagdoll.HalfExtents half)
    {
        var heel = half.Foot;
        var toeHalf = FootGeometry.ToeHalf(heel);
        (int Toe, int Servo) Add(int foot, string side)
        {
            var s = world.GetState(foot);
            var toe = world.AddDynamicBox(s.Position + Vector3.Transform(FootGeometry.ToeCenterOnHeel(heel), s.Orientation), s.Orientation, toeHalf,
                tuning.Masses.Foot / (1f - FootGeometry.ToeShare) * FootGeometry.ToeShare); // 足（かかと側）は足の質量の残り
            world.SetName(toe, name + "_" + side + "_toe");
            world.AddHinge(foot, toe, FootGeometry.BallOnHeel(heel), FootGeometry.BallOnToe(heel), Vector3.UnitX, Vector3.UnitX, frequency: 18f);
            var deg = MathF.PI / 180f;
            world.AddTwistLimit(foot, toe, HumanoidRagdoll.XAxisTwistBasis, HumanoidRagdoll.XAxisTwistBasis, -60f * deg, 25f * deg);
            var servo = world.AddTwistServo(foot, toe, HumanoidRagdoll.XAxisTwistBasis, HumanoidRagdoll.XAxisTwistBasis, 0f,
                tuning.LegServoHz, 1f, 20f, 0f, tuning.ToeMaxTorque * tuning.TorqueScale);
            // 自分の体とはぶつけない（ラグドールの部位どうしと同じ。かかと側とは蝶番で接している）。
            foreach (var id in parts.All)
            {
                world.SetPairCollisionEnabled(toe, id, false);
            }

            return (toe, servo);
        }

        var left = Add(parts.LeftFoot, "l");
        var right = Add(parts.RightFoot, "r");
        world.SetPairCollisionEnabled(left.Toe, right.Toe, false);
        return new ToeParts(left.Toe, right.Toe, left.Servo, right.Servo);
    }

    /// <summary>1 サブステップ分の制御を力積で与える（物理ステップの直前に呼ぶ）。指令は遅れなくそのまま体へ届く。</summary>
    public void ApplyControl(in FighterCommand command, float dt)
    {
        LastCommand = command;
        MeasureHeadJolt();
        Concussion *= MathF.Exp(-dt / MathF.Max(1e-3f, _tuning.ConcussionRecovery)); // 脳しんとうは時間とともに抜ける
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

    /// <summary>
    /// 前の物理ステップで頭が胴に対してどれだけ急に回されたか: 頭の相対角速度の変化 [rad/s]（調べもの用。脳しんとうには使わない:
    /// 自分の動きや体どうしのぶつかりでも大きく揺れるので、打撃が頭に与えた回転から計算する <see cref="TwistHead"/> を使う）。
    /// </summary>
    public float HeadJolt { get; private set; }

    private Vector3 _previousHeadSpin;
    private bool _hasHeadSpin;

    private void MeasureHeadJolt()
    {
        var spin = _world.GetState(Parts.Head).AngularVelocity - _world.GetState(Parts.Chest).AngularVelocity;
        HeadJolt = _hasHeadSpin ? Vector3.Distance(spin, _previousHeadSpin) : 0f;
        _previousHeadSpin = spin;
        _hasHeadSpin = true;
    }

    /// <summary>脳しんとう（意識から引く量。打撃で頭が急にねじられると溜まり、時間とともに抜ける）。</summary>
    public float Concussion { get; internal set; }

    /// <summary>最後に頭が打撃でねじられた速さ [rad/s]（調べもの用）。</summary>
    public float LastHeadTwist { get; private set; }

    /// <summary>
    /// 頭が打撃で <paramref name="angularImpulse"/>（ワールド [N·m·s]）だけねじられた: 頭の慣性で割った回転の速さの 2 乗に比例して意識が飛ぶ。
    /// 回転の中心を通る打撃（額の正面）は回さず、外れた所（あご・こめかみ）を体重を乗せて振り抜くほど強く回る。
    /// </summary>
    internal void TwistHead(Vector3 angularImpulse)
    {
        var head = _world.GetState(Parts.Head);
        var local = Vector3.Transform(angularImpulse, Quaternion.Conjugate(head.Orientation)) / _world.GetLocalInertiaDiagonal(Parts.Head);
        LastHeadTwist = local.Length();
        Concussion += _tuning.ConcussionGain * LastHeadTwist * LastHeadTwist;
    }

    /// <summary>
    /// 意識（0..1）= 脳の働き（脳のボクセルの残り）− 脳しんとう。打撃の部位で場合分けせず、脳を削られたか・頭がどれだけねじられたかだけで決まる。
    /// </summary>
    public float Consciousness => Math.Clamp((Voxels?.BrainFunction(_tuning.OrganDeathFraction) ?? 1f) - Concussion, 0f, 1f);

    /// <summary>
    /// 体の張り（0..1）: 意識が薄れると体を支える力・動かす力が抜ける（崩れ落ちる・腰が落ちるのは物理）。
    /// 意識が <see cref="FighterTuning.FullToneConsciousness"/> を下回ると張りが抜け始めて腰が落ち、ガードが下がる（<see cref="FighterTuning.FaintTone"/> まで）。
    /// <see cref="FighterTuning.KnockoutConsciousness"/> を下回ると意識が途切れて張りは 0: 全身の力が抜けて倒れきる。
    /// </summary>
    public float Tone
    {
        get
        {
            var c = Consciousness;
            if (c < _tuning.KnockoutConsciousness)
            {
                return 0f;
            }

            var t = Math.Clamp((c - _tuning.KnockoutConsciousness) / MathF.Max(1e-3f, _tuning.FullToneConsciousness - _tuning.KnockoutConsciousness), 0f, 1f);
            return _tuning.FaintTone + ((1f - _tuning.FaintTone) * t * t * (3f - (2f * t)));
        }
    }

    /// <summary>意識が薄れて体の張りが抜けた度合い（0 = 張りが満ちている、1 = 意識が途切れる手前の <see cref="FighterTuning.FaintTone"/> 以下）。ボットはこれを見てガードを下げる。</summary>
    public float Faintness => Math.Clamp((1f - Tone) / MathF.Max(1e-3f, 1f - _tuning.FaintTone), 0f, 1f);

    /// <summary>意識が薄れて腕に残る力の割合（体の張りに加えて。<see cref="FighterTuning.FaintArmStrength"/>）。</summary>
    private float FaintArm => Lerp(1f, _tuning.FaintArmStrength, Faintness);

    /// <summary>体ごと押す（重いパンチ・投げ）。<paramref name="velocityChange"/> は全部位へ一様に（胴だけだと 4 割しか伝わらない／Triage P-006）。</summary>
    public void Push(Vector3 velocityChange)
    {
        if (velocityChange.LengthSquared() <= 0f)
        {
            return;
        }

        foreach (var id in _all)
        {
            if (!_detached.Contains(id))
            {
                _world.ApplyLinearImpulse(id, velocityChange * _world.GetMass(id));
            }
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

        if (arm.Severed)
        {
            Release(arm, opponent, allowThrow: false); // 切り落とされた腕では掴めない
            arm.GripWasHeld = gripHeld;
            return null;
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
            if (opponent._detached.Contains(part))
            {
                continue;
            }

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
        var constraint = _world.AddBallSocket(arm.GloveBody, best, arm.GloveLocal, localOnTarget, frequency: 30f, dampingRatio: 1f);
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
            : IsToe(part) ? FootGeometry.ToeHalf(h.Foot)
            : h.Foot;
    }

    private void TickStanding(in FighterCommand command, bool grounded, float height, float dt)
    {
        _noGroundTime = grounded ? 0f : _noGroundTime + dt;
        _tiltTime = Uprightness < _tuning.FallUprightness ? _tiltTime + dt : 0f;
        // 倒れるのは物理的にバランスを失ったとき（足が浮いた・大きく傾いた）と、脚が体を支えられなくなったとき
        // （肉を削られた・切り落とされた）。支えられない脚では起き上がれない。
        if (!CanStand)
        {
            _stayDown = true;
            Enter(FighterState.Down);
            return;
        }

        // 力が抜けて、支えようとしている高さより腰が大きく落ちた（崩れ落ちた）のもダウン。傾かずに真下へ崩れても倒れたことになる。
        // しゃがんだ（自分で腰を落とした）だけなら、支えようとしている高さも下がっているので当たらない。
        // 力が抜けている間だけ数える（しゃがみから立ち上がる途中で、目標の高さに追いつく前を崩れ落ちと取り違えない）。
        _collapsedTime = grounded && Tone < 1f && _hoverHeight > 0f && height < _hoverHeight - (_tuning.CollapseDrop * _standHeight) ? _collapsedTime + dt : 0f;
        if (_noGroundTime > _tuning.NoGroundFallTime || _tiltTime > _tuning.FallGrace || _collapsedTime > _tuning.CollapseTime)
        {
            Enter(FighterState.Down);
            return;
        }

        var scale = IsGrabbed ? _tuning.GrabbedDriveScale : 1f;
        DriveBody(command, scale * Tone, grounded, height, dt);
    }

    private void TickDown(in FighterCommand command, bool grounded, float height, float dt)
    {
        // こらえて崩れる（Triage ADR-102④）: 駆動を一気に抜かず CollapseDuration かけて落とす。
        // 意識が途切れている（張りが 0）なら、こらえる力も無く、全身の力が抜けて倒れきる。
        ControlScale = Ramp(1f, _tuning.DownDriveScale, _stateTime, _tuning.CollapseDuration) * Tone;
        SetServoScale(ControlScale);
        DriveArms(command, ControlScale, weightShift: false, dt);
        var settled = _world.GetState(Parts.Pelvis).LinearVelocity.Length() < 0.6f;
        if (!_stayDown && CanStand && Consciousness >= _tuning.GetUpConsciousness && _stateTime > _tuning.DownTime && settled && grounded && height < _standHeight * 1.6f)
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

        DriveBody(command, Ramp(0.4f, 1.2f, _stateTime, _tuning.GetUpDuration) * Tone, true, height, dt);
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
        _collapsedTime = 0f;
        _noGroundTime = 0f;
    }

    private void DriveBody(in FighterCommand command, float scale, bool grounded, float height, float dt)
    {
        ControlScale = Math.Clamp(scale, 0f, 1f);
        SetServoScale(scale);
        // 脚の肉が削られるほど体を持ち上げる力・歩く力が落ち、胴の肉が削られるほど体の軸を保つ力が落ちる。
        var legs = MathF.Min(LegStrength(true), LegStrength(false));
        _crouch = Math.Clamp(command.HeadOffset.Y / _standHeight, -_tuning.MaxCrouch, 0.05f);
        // 立っている間は足の運び（足を置いてから体が付いていく）。倒れて起き上がる間は以前の歩行の形のまま。
        var footwork = _tuning.Footwork.Enabled && State == FighterState.Standing && grounded && !LegSevered;
        if (footwork && !_footworkActive)
        {
            ResetFootwork();
        }

        _footworkActive = footwork;
        var groundY = PelvisPosition.Y - height;
        if (grounded)
        {
            ApplyHover(command, height, scale * legs, footwork, footwork ? LegsAllowHeight(groundY) : float.PositiveInfinity, dt);
        }

        ApplyFacingAndUpright(command, scale * CoreStrength, dt);
        if (footwork)
        {
            StepFeet(command, height, dt);
            ApplyBodyOverFeet(command, scale * legs, dt);
            ApplyLegs(groundY, dt);
        }
        else
        {
            ApplyMovement(command, scale * legs, dt);
            ApplyGait(dt);
            foreach (var leg in _legParts)
            {
                SetTwistTarget(leg.ToeServo, 0f); // 歩行の形は足裏が平ら（つま先も戻す）
            }
        }

        // 意識が薄れると腕を上げていられない（ガードが重さで下がる）。
        DriveArms(command, ControlScale * FaintArm, weightShift: grounded && State == FighterState.Standing, dt);
    }

    /// <summary>今の両足の位置で、足の運びを置き直す（立ち直ったとき・生成直後）。</summary>
    private void ResetFootwork()
    {
        (Vector2 Ball, float Yaw) OnGround(Leg leg)
            => (BallOnGround(leg), BodyFrame.YawOf(Vector3.Transform(Vector3.UnitZ, _world.GetState(leg.Heel).Orientation), _facingYaw));

        var lead = OnGround(_legParts[Footwork.Lead]);
        var rear = OnGround(_legParts[Footwork.Rear]);
        _footwork.Reset(lead.Ball, lead.Yaw, rear.Ball, rear.Yaw);
        foreach (var leg in _legParts)
        {
            leg.HasTarget = false;
            leg.ReachRatio = 0f;
        }

        _laggedMove = Vector3.Zero;
        _hoverIntegral = 0f;
    }

    /// <summary>
    /// 足の運びを 1 ステップ進める: 倒立振子の捕捉点・移動の意図・胴のひねり・体重のかけ方から、足の置き場所を決めて踏む。
    /// </summary>
    private void StepFeet(in FighterCommand command, float height, float dt)
    {
        var pelvis = _world.GetState(Parts.Pelvis);
        var omega = MathF.Sqrt(-_world.Gravity.Y / MathF.Max(0.05f, height));
        var intent = command.ClampedMove * _tuning.WalkSpeed;
        var twist = _twist / MathF.Max(1e-3f, _tuning.MaxTorsoTwistRadians);
        // 体重: 頭を前へ出せば前足、反らせば後ろ足へ。右の拳（後ろの手）を打つときは前足へ乗せる。
        var weight = Math.Clamp((command.HeadOffset.Z / (0.4f * ArmReach)) + (0.6f * MathF.Max(0f, twist)), -1f, 1f);
        var lead = _legParts[Footwork.Lead];
        // 脚が水平に届く距離: 股の高さ（足首の高さから）と伸ばしきった脚の長さから。
        var legLength = LegIk.MaxLength(lead.ThighToKnee, lead.ShinLength);
        var hip = pelvis.Position + Vector3.Transform(lead.PivotOnPelvis, pelvis.Orientation);
        var drop = hip.Y - (pelvis.Position.Y - height) - lead.AnkleOnHeel.Y - _tuning.HalfExtents.Foot.Y;
        var reach = MathF.Sqrt(MathF.Max(0f, (legLength * legLength) - (drop * drop)));
        var input = new FootworkInput(_facingYaw, new Vector2(pelvis.Position.X, pelvis.Position.Z), new Vector2(pelvis.LinearVelocity.X, pelvis.LinearVelocity.Z),
            omega, new Vector2(intent.X, intent.Z), twist, weight, legLength,
            lead.ReachRatio > 0.995f, _legParts[Footwork.Rear].ReachRatio > 0.995f, BallOnGround(lead), BallOnGround(_legParts[Footwork.Rear]), MathF.Max(0.3f * legLength, reach));
        _footwork.Update(input, dt);
    }

    /// <summary>足の母指球の実際の位置（水平面）。</summary>
    private Vector2 BallOnGround(Leg leg)
    {
        var s = _world.GetState(leg.Heel);
        var ball = s.Position + Vector3.Transform(leg.BallOnHeel, s.Orientation);
        return new Vector2(ball.X, ball.Z);
    }

    /// <summary>
    /// 体（骨盤）を動かす: 移動の指令に少し遅れて付いていき（足が先に踏み出し、体が後から動く）、両足の間の支えの点
    /// （体重のかけ方で前後へ寄る）へは弱く寄せる。足は体の捕捉点と移動の意図から置き場所を決めるので、体は足から離れない。
    /// </summary>
    private void ApplyBodyOverFeet(in FighterCommand command, float scale, float dt)
    {
        var t = _tuning.Footwork;
        var pelvis = _world.GetState(Parts.Pelvis);
        _laggedMove = Vector3.Lerp(_laggedMove, command.ClampedMove, 1f - MathF.Exp(-dt / MathF.Max(1e-3f, t.BodyLag)));
        var toSupport = _footwork.SupportCenter - new Vector2(pelvis.Position.X, pelvis.Position.Z);
        var centering = toSupport * t.Centering;
        if (centering.Length() > t.MaxCentering)
        {
            centering *= t.MaxCentering / centering.Length();
        }

        var desired = (_laggedMove * _tuning.WalkSpeed) + new Vector3(centering.X, 0f, centering.Y);
        // 体は足より先へは行けない: 支えの点から離れるほど、離れる向きの速さを抑える（足が追いつくのを待つ）。
        var away = -toSupport;
        var distance = away.Length();
        var legLength = LegIk.MaxLength(_legParts[0].ThighToKnee, _legParts[0].ShinLength);
        var slack = t.Tether * legLength;
        if (distance > 1e-6f)
        {
            var direction = new Vector3(away.X, 0f, away.Y) / distance;
            var outward = Vector3.Dot(desired, direction);
            var allowed = outward * Math.Clamp(1f - ((distance - slack) / slack), 0f, 1f);
            if (outward > allowed)
            {
                desired -= direction * (outward - allowed);
            }
        }

        var accel = FighterMath.MoveAccel(pelvis.LinearVelocity, desired, _tuning.MoveGain, _tuning.MaxMoveAccel);
        ApplySharedImpulse(accel * (TotalMass * scale * dt));
        TrackSpeed(pelvis.LinearVelocity, dt);
    }

    /// <summary>足の置き方 <paramref name="pose"/> の足首の位置と、かかと側の足の姿勢（ワールド）。</summary>
    private static (Vector3 Ankle, Quaternion Foot) AnkleTarget(Leg leg, FootPose pose, float groundY)
    {
        // 母指球を軸にかかとを浮かせ（X 軸回りにつま先側へ）、つま先の向きへ回す。
        var foot = Quaternion.Concatenate(Quaternion.CreateFromAxisAngle(Vector3.UnitX, pose.HeelRaise), Quaternion.CreateFromAxisAngle(Vector3.UnitY, pose.Yaw));
        var ball = new Vector3(pose.Ball.X, groundY + pose.Ball.Y + 0.001f, pose.Ball.Z);
        return (ball + Vector3.Transform(leg.AnkleOnHeel - leg.BallOnHeel, foot), foot);
    }

    /// <summary>
    /// 着いている足に膝を <see cref="FootworkTuning.KneeBendDegrees"/> 曲げて届く、骨盤の地面からの高さの上限
    /// （構えで足を前後に開くほど低い）。踏み出している足は数えない。
    /// </summary>
    private float LegsAllowHeight(float groundY)
    {
        var pelvis = _world.GetState(Parts.Pelvis);
        var allow = float.PositiveInfinity;
        for (var i = 0; i < 2; i++)
        {
            var leg = _legParts[i];
            var pose = _footwork.Pose(i);
            if (pose.Swinging)
            {
                continue;
            }

            var (ankle, _) = AnkleTarget(leg, pose, groundY);
            var pivot = pelvis.Position + Vector3.Transform(leg.PivotOnPelvis, pelvis.Orientation);
            var horizontal = new Vector2(ankle.X - pivot.X, ankle.Z - pivot.Z).Length();
            var length = LegIk.Length(leg.ThighToKnee, leg.ShinLength, _tuning.Footwork.KneeBendDegrees * MathF.PI / 180f);
            var pivotY = ankle.Y + MathF.Sqrt(MathF.Max(1e-6f, (length * length) - (horizontal * horizontal)));
            allow = MathF.Min(allow, pivotY + (pelvis.Position.Y - pivot.Y) - groundY);
        }

        return allow;
    }

    /// <summary>
    /// 脚の IK: 足の置き方から股・膝・足首・つま先のサーボの目標を決める。骨盤は今の位置と、目標の姿勢にあるとして解く
    /// （今の姿勢から解くと、傾いた骨盤を脚が支え直さず、骨盤が 45° 傾いた）。高さのずれは骨盤の高さの制御が積分で打ち消す。
    /// </summary>
    private void ApplyLegs(float groundY, float dt)
    {
        var pelvis = _world.GetState(Parts.Pelvis);
        pelvis = pelvis with { Orientation = _pelvisTarget };

        var inverse = Quaternion.Conjugate(pelvis.Orientation);
        for (var i = 0; i < 2; i++)
        {
            var leg = _legParts[i];
            if (!_world.HasConstraint(leg.HipServo))
            {
                continue;
            }

            var pose = _footwork.Pose(i);
            var (ankle, foot) = AnkleTarget(leg, pose, groundY);
            var pivot = pelvis.Position + Vector3.Transform(leg.PivotOnPelvis, pelvis.Orientation);
            var toes = new Vector3(MathF.Sin(pose.Yaw), 0f, MathF.Cos(pose.Yaw));
            var (thigh, knee, reach) = LegIk.Solve(Vector3.Transform(ankle - pivot, inverse), leg.ThighToKnee, leg.ShinLength, Vector3.Transform(toes, inverse));
            leg.ReachRatio = reach;
            _world.SetAngularServoTarget(leg.HipServo, thigh);
            SetTwistTarget(leg.KneeServo, knee);
            var shin = Quaternion.Concatenate(Quaternion.Concatenate(Quaternion.CreateFromAxisAngle(Vector3.UnitX, knee), thigh), pelvis.Orientation);
            leg.AnkleTarget = LegIk.AnklePitch(shin, foot);
            SetTwistTarget(leg.AnkleServo, leg.AnkleTarget);
            SetTwistTarget(leg.ToeServo, -pose.HeelRaise); // かかとを浮かせた分、つま先は反らせて床に付けたまま
            DriveFoot(leg, ankle, pose.Swinging, pelvisId: Parts.Pelvis, dt);
        }
    }

    /// <summary>
    /// 足首を目標へ PD 力で引く（手の拳と同じ）。サーボだけでは、すり足の足が床の摩擦に負けて置き場所まで運べない。
    /// 反作用は骨盤へ（脚の筋肉が体と足の間で出す力）。着いている足では目標がそこなので、足を床に留める力になる。
    /// </summary>
    private void DriveFoot(Leg leg, Vector3 ankleTarget, bool swinging, int pelvisId, float dt)
    {
        var t = _tuning.Footwork;
        var heel = _world.GetState(leg.Heel);
        var ankle = heel.Position + Vector3.Transform(leg.AnkleOnHeel, heel.Orientation);
        var velocity = heel.LinearVelocity + Vector3.Cross(heel.AngularVelocity, ankle - heel.Position);
        var targetVelocity = leg.HasTarget && dt > 0f ? ClampLength((ankleTarget - leg.LastTarget) / dt, _tuning.MaxTargetSpeed) : Vector3.Zero;
        leg.LastTarget = ankleTarget;
        leg.HasTarget = true;
        leg.LastError = ankleTarget - ankle;
        var mass = _world.GetMass(leg.Heel) + _world.GetMass(leg.Toe) + _world.GetMass(leg.Shin);
        var force = ClampLength(FighterMath.HandAccel(ankle, velocity, ankleTarget, targetVelocity, t.FootKp, t.FootKd) * mass, t.MaxFootForce * _tuning.TorqueScale / _tuning.Scale);
        if (!swinging)
        {
            // 着いている足は床から持ち上げない（上向きに引くと、その反作用で骨盤が下へ引かれ、体が目標より沈んでいた）。
            force.Y = MathF.Min(0f, force.Y);
        }
        _world.ApplyLinearImpulse(leg.Heel, force * dt);
        _world.ApplyLinearImpulse(pelvisId, -force * dt);
    }

    private void ApplyHover(in FighterCommand command, float height, float scale, bool footwork, float legsAllow, float dt)
    {
        var walking = !footwork;
        var bob = walking ? MathF.Sin(_gaitPhase * 2f) * 0.04f * _standHeight * Math.Clamp(_smoothedSpeed / _tuning.WalkSpeed, 0f, 1f) : 0f;
        var hoverScale = State == FighterState.GettingUp ? scale * Lerp(0.5f, 1f, Math.Clamp(Uprightness, 0f, 1f)) : scale;
        // 立つ高さ: 頭を下げればダッキング（膝と股を曲げて腰から沈む。脚の曲げは足の IK、起き上がり中は ApplyGait が同じ _crouch から決める）。
        // 足の運びの間は、着いた足に膝を少し曲げて届く高さを上限にする（伸びきった脚で足が浮かないように）。
        var stand = _standHeight * _tuning.StandHeightRatio;
        var target = (float.IsFinite(legsAllow) ? MathF.Min(stand, MathF.Max(0.85f * stand, legsAllow)) : stand) + (stand * _crouch);
        // 意識が薄れて張りが抜けると、脚は体重を支えても伸ばしきれず膝が折れて腰が落ちる（張りが FaintTone で FaintSag だけ）。
        target -= stand * _tuning.FaintSag * Faintness;
        _hoverHeight = target;
        var pelvis = _world.GetState(Parts.Pelvis);
        // 足の運びの間は、脚が床と体の間でどれだけ重さを受け持つかが姿勢で変わり、骨盤が目標から数 cm ずれたまま止まる
        // （体全体を浮かせると上へ、上体だけだと下へ）。そのずれを積分して打ち消す（上限は重力の 3 割）。
        var error = target + bob - height;
        // 体を支える力が弱い（打たれて力が落ちた・脚が削られた）間は積分しない。弱い間に溜めると、力が戻ったときに体が跳ねる。
        _hoverIntegral = walking ? 0f : Math.Clamp(_hoverIntegral + (HoverKi * error * dt * Math.Clamp(hoverScale, 0f, 1f)), -0.3f * -_world.Gravity.Y, 0.3f * -_world.Gravity.Y);
        // 体重を支える力は、意識が途切れて張りが 0 になると無くなる（崩れ落ちる）。張りが残っている間は支える（腰が落ちるだけ）。
        var accel = FighterMath.HoverAccel((Tone > 0f ? -_world.Gravity.Y : 0f) + (_hoverIntegral * MathF.Max(0f, hoverScale)), error,
            pelvis.LinearVelocity.Y, _tuning.HoverKp, _tuning.HoverKd, _tuning.MaxHoverAccel, hoverScale);
        // 足の運びの間は脚が床に立っていて、脚の重さは床が支える。浮かせるのは上体（骨盤から上）だけ。
        ApplySharedImpulse(new Vector3(0f, accel * (walking ? TotalMass : TotalMass - _legMass) * dt, 0f));
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

        var bias = float.IsFinite(command.TwistBias) ? command.TwistBias : 0f;
        var twist = Math.Clamp(PunchMechanics.TorsoTwist(command.Left.TargetLocal, command.Right.TargetLocal, ArmReach, _tuning.MaxTorsoTwistRadians) + bias,
            -_tuning.MaxTorsoTwistRadians, _tuning.MaxTorsoTwistRadians);
        _twist = twist;
        var torsoLength = MathF.Max(0.05f, HeadPosition.Y - PelvisPosition.Y);
        var head = command.HeadOffset;
        var moveLean = Math.Clamp(_forwardSpeed / _tuning.WalkSpeed, -1f, 1f) * (6f * MathF.PI / 180f);
        // 腰の沈みで届かない分の頭の下げは、股から上体を前へ折って（ヒップヒンジ）下げる。首だけ・背中を丸めるだけでは沈まない。
        var hipDrop = -_crouch * _standHeight * _tuning.StandHeightRatio;
        var rest = MathF.Max(0f, -head.Y - hipDrop);
        var hinge = MathF.Min(_tuning.MaxHingeRadians, MathF.Acos(Math.Clamp(1f - (rest / _restTorso), -1f, 1f)));
        var pitch = Math.Clamp(moveLean + MathF.Atan2(head.Z, torsoLength), -_tuning.MaxLeanRadians, _tuning.MaxLeanRadians) + hinge;
        var roll = Math.Clamp(MathF.Atan2(-head.X, torsoLength), -_tuning.MaxLeanRadians, _tuning.MaxLeanRadians); // 頭を右（−X）へ → 右へ傾く
        // 姿勢 = 向き（ヨー）×（向きの座標系での）傾き × 胴自身のひねり。傾きは向きの座標系で与えるので、
        // ひねっていても「前へ出した頭」は前傾のまま。傾きは主に胸、腰はその一部（脚の付け根が後ろへ振れないように）。
        var facing = Quaternion.CreateFromAxisAngle(Vector3.UnitY, _facingYaw);
        Quaternion Upright(float twistShare, float leanShare, float pitchAngle)
        {
            var lean = Quaternion.Concatenate(Quaternion.CreateFromAxisAngle(Vector3.UnitZ, roll * leanShare), Quaternion.CreateFromAxisAngle(Vector3.UnitX, pitchAngle));
            return Quaternion.Concatenate(Quaternion.Concatenate(Quaternion.CreateFromAxisAngle(Vector3.UnitY, twist * twistShare), lean), facing);
        }

        // 後ろへ反らす（スウェー）ときは、腰から上体ごと後ろへ倒す（首だけで反らない）。前へ倒すときの腰は一部だけ
        // （脚の付け根が後ろへ振れないように。前へ深く折るのはヒップヒンジ）。
        // 頭を後ろへ引いた分（後ろへ歩くときの傾き・ヒップヒンジは含めない）だけ、腰の受け持ちを増やす。
        var sway = Math.Clamp(MathF.Atan2(head.Z, torsoLength), -_tuning.MaxLeanRadians, 0f);
        _pelvisTarget = Upright(_tuning.HipTwistShare, 0.25f, (0.25f * pitch) + ((_tuning.HipBackLeanShare - 0.25f) * sway));
        ApplyUprightImpulse(Parts.Pelvis, _pelvisTarget, scale, dt);
        ApplyUprightImpulse(Parts.Chest, Upright(1f, 1f, pitch), scale * 0.9f, dt);
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
        TrackSpeed(pelvis.LinearVelocity, dt);
    }

    private void TrackSpeed(Vector3 v, float dt)
    {
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
        // 沈み込み: 腿を前へ θ・すねを後ろへ θ 傾けると、脚の高さは cos θ 倍になる（股 −θ・膝 +2θ・足首は下で水平に戻す）。
        var squat = MathF.Acos(Math.Clamp(1f + MathF.Min(0f, _crouch), 0f, 1f));
        var pose = FighterMath.Gait(_gaitPhase, blend, _tuning.NeutralHipRadians - squat, _tuning.NeutralKneeRadians + (2f * squat),
            _tuning.GaitHipSwingRadians, _tuning.GaitKneeLiftRadians);
        // 切り落とされた脚のサーボは外してある（無い拘束には書かない）。
        if (_world.HasConstraint(_legs.LeftHip))
        {
            _world.SetAngularServoTarget(_legs.LeftHip, Quaternion.CreateFromAxisAngle(Vector3.UnitX, pose.LeftHip));
        }

        if (_world.HasConstraint(_legs.RightHip))
        {
            _world.SetAngularServoTarget(_legs.RightHip, Quaternion.CreateFromAxisAngle(Vector3.UnitX, pose.RightHip));
        }

        SetTwistTarget(_legs.LeftKnee, pose.LeftKnee);
        SetTwistTarget(_legs.RightKnee, pose.RightKnee);
        // 足裏を水平に保つ: 足首 = −(股 + 膝)（3 関節とも同じ矢状軸・同じ符号規約）。
        SetTwistTarget(_legs.LeftAnkle, -(pose.LeftHip + pose.LeftKnee));
        SetTwistTarget(_legs.RightAnkle, -(pose.RightHip + pose.RightKnee));
    }

    private void DriveArms(in FighterCommand command, float scale, bool weightShift, float dt)
    {
        var frame = Frame;
        var com = CenterOfMass;
        var torso = TorsoOrientation;
        foreach (var arm in _arms)
        {
            if (arm.Severed)
            {
                continue; // 切り落とされた腕は動かせない
            }

            var bodyForce = arm.Drive(_world, _tuning, frame, command.For(arm.Hand).TargetLocal, scale * ArmStrength(arm.Hand), _inertia, com, torso, TotalMass, dt, Parts.Chest);
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
        // 値が変わったときだけ Bepu の拘束を書き換える（毎サブステップ 9 本の Get/ApplyDescription を避ける）。
        // 脚・腕のサーボの強さには、その脚・腕の肉の残り（力）を掛ける。
        var left = t * MathF.Max(0.01f, LegStrength(true));
        if (Changed(0, left))
        {
            SetAngularStrength(_legs.LeftHip, _tuning.LegServoHz, _tuning.HipMaxTorque * left);
            SetTwistStrength(_legs.LeftKnee, _tuning.LegServoHz, _tuning.KneeMaxTorque * left);
            SetTwistStrength(_legParts[Footwork.Rear].ToeServo, _tuning.LegServoHz, _tuning.ToeMaxTorque * left);
        }

        var right = t * MathF.Max(0.01f, LegStrength(false));
        if (Changed(4, right))
        {
            SetAngularStrength(_legs.RightHip, _tuning.LegServoHz, _tuning.HipMaxTorque * right);
            SetTwistStrength(_legs.RightKnee, _tuning.LegServoHz, _tuning.KneeMaxTorque * right);
            SetTwistStrength(_legParts[Footwork.Lead].ToeServo, _tuning.LegServoHz, _tuning.ToeMaxTorque * right);
        }

        if (Changed(1, t))
        {
            _world.SetAngularServoSettings(_neck, _tuning.NeckServoHz, _tuning.NeckMaxTorque * t, _tuning.NeckDampingRatio);
        }

        foreach (var arm in _arms)
        {
            var a = s * FaintArm; // 意識が薄れると腕を上げていられない
            if (!arm.Severed && Changed(2 + (int)arm.Hand, a * ArmStrength(arm.Hand)))
            {
                arm.SetStrength(_world, _tuning, a * Strength(arm.UpperArm), a * Strength(arm.Forearm));
            }
        }
    }

    private void SetTwistTarget(int servo, float angle)
    {
        if (_world.HasConstraint(servo))
        {
            _world.SetTwistServoTarget(servo, angle);
        }
    }

    private void SetAngularStrength(int servo, float hz, float force)
    {
        if (_world.HasConstraint(servo))
        {
            _world.SetAngularServoSettings(servo, hz, force);
        }
    }

    private void SetTwistStrength(int servo, float hz, float force)
    {
        if (_world.HasConstraint(servo))
        {
            _world.SetTwistServoSettings(servo, hz, force);
        }
    }

    /// <summary>臓器が壊れて死ぬ: 崩れ落ち、起き上がらない。</summary>
    internal void Die(string cause)
    {
        if (IsDead)
        {
            return;
        }

        DeathCause = cause;
        _stayDown = true;
        if (State != FighterState.Down)
        {
            Enter(FighterState.Down);
        }
    }

    /// <summary>
    /// 部位 <paramref name="part"/> の骨が断たれ、遠い側が <paramref name="fragment"/>（新しい剛体）になった。
    /// <list type="bullet">
    /// <item>その部位に付いていた先の部位（肘から先・膝から先・足首から先）は、関節ごと破片へ付け替える。筋肉（サーボ）は外す（もう動かせない）。</item>
    /// <item>腕なら、その腕はもう駆動しない。前腕が切れたら、グローブは破片と一緒に落ちる。掴んでいれば離す。</item>
    /// <item>脚なら、立てずに倒れて起き上がれない。</item>
    /// </list>
    /// </summary>
    internal void Sever(int part, VoxelBody fragment)
    {
        ArgumentNullException.ThrowIfNull(fragment);
        for (var i = 0; i < _joints.Count; i++)
        {
            var joint = _joints[i];
            if (joint.ParentPart != part)
            {
                continue;
            }

            var anchor = _world.GetJointAnchors(joint.PrimaryConstraintId)?.AnchorA ?? _world.GetState(joint.ChildPart).Position;
            foreach (var hanging in HumanoidRagdoll.DownstreamParts(_joints, joint))
            {
                _detached.Add(hanging);
            }

            foreach (var leg in _legParts)
            {
                if (_detached.Contains(leg.Heel) && _detached.Add(leg.Toe))
                {
                    // つま先は足（かかと側）にぶら下がっている。自分の体とのぶつかりは部位ごとに外してあったので、
                    // 体に残った部位とはまたぶつかるようにする（切れた足と同じ。互いの間は SeparateCollisions が外す）。
                    foreach (var id in _all.Where(id => !_detached.Contains(id)))
                    {
                        _world.SetPairCollisionEnabled(leg.Toe, id, true);
                    }
                }
            }

            foreach (var id in joint.AllConstraintIds)
            {
                if (_world.HasConstraint(id))
                {
                    _world.RemoveConstraint(id);
                }
            }

            RemoveServosBetween(part, joint.ChildPart);
            var f = _world.GetState(fragment.BodyId);
            var c = _world.GetState(joint.ChildPart);
            var socket = _world.AddBallSocket(fragment.BodyId, joint.ChildPart,
                Vector3.Transform(anchor - f.Position, Quaternion.Conjugate(f.Orientation)),
                Vector3.Transform(anchor - c.Position, Quaternion.Conjugate(c.Orientation)));
            // 付け替えた関節は玉継手だけ（角度の制限も筋肉も無い、ぶらぶらの関節）。TorqueConstraintId もその玉継手を指す。
            _joints[i] = joint with { ParentPart = fragment.BodyId, PrimaryConstraintId = socket, TorqueConstraintId = socket, AllConstraintIds = [socket] };
        }

        SeparateCollisions(part, fragment.BodyId);

        foreach (var arm in _arms)
        {
            if (part != arm.UpperArm && part != arm.Forearm)
            {
                continue;
            }

            // グローブが今この部位に付いているときだけ、切り落とされた側（破片）へ移す（同じ前腕を 2 度切ったときの、
            // 体に残った切り株の 2 度目の切断では動かさない）。
            if (arm.GloveBody == part)
            {
                // グローブは前腕の先（破片の側）に付いている。
                var glove = arm.GlovePosition(_world);
                var f = _world.GetState(fragment.BodyId);
                arm.AttachGlove(fragment.BodyId, Vector3.Transform(glove - f.Position, Quaternion.Conjugate(f.Orientation)));
            }

            arm.Severed = true;
            if (arm.GrabConstraint is { } grab)
            {
                if (_world.HasConstraint(grab))
                {
                    _world.RemoveConstraint(grab, forget: true);
                }

                arm.GrabConstraint = null;
                arm.Grabbed?.SetGrabbed(false);
                arm.Grabbed = null;
            }
        }

        var p = Parts;
        if (part == p.LeftThigh || part == p.LeftShin)
        {
            _legSevered[0] = true;
        }

        if (part == p.RightThigh || part == p.RightShin)
        {
            _legSevered[1] = true;
        }
    }

    /// <summary>
    /// 切り落とされた破片と、それにぶら下がって外れた部位は、体の自己衝突の除外から出す: 体（と相手と床）にぶつかるようにし、
    /// 互いの間と、破片と体に残った切り株の間だけはぶつけない（つながっている・切り口で接している）。
    /// </summary>
    private void SeparateCollisions(int stub, int fragment)
    {
        var loose = _detached.Append(fragment).ToList();
        foreach (var id in loose)
        {
            _world.SetCollisionGroup(id, null);
        }

        for (var a = 0; a < loose.Count; a++)
        {
            for (var b = a + 1; b < loose.Count; b++)
            {
                _world.SetPairCollisionEnabled(loose[a], loose[b], false);
            }
        }

        _world.SetPairCollisionEnabled(fragment, stub, false);
    }

    /// <summary>2 つの部位をつなぐ筋肉（腕・脚のサーボ）を外す。</summary>
    private void RemoveServosBetween(int a, int b)
    {
        int[] servos = [_legs.LeftHip, _legs.RightHip, _legs.LeftKnee, _legs.RightKnee, _legs.LeftAnkle, _legs.RightAnkle,
            _arms[0].ShoulderServo, _arms[0].ElbowServo, _arms[1].ShoulderServo, _arms[1].ElbowServo];
        foreach (var servo in servos)
        {
            if (_world.GetConstraintBodies(servo) is { } bodies && ((bodies.BodyA == a && bodies.BodyB == b) || (bodies.BodyA == b && bodies.BodyB == a)))
            {
                _world.RemoveConstraint(servo);
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

    /// <summary>つま先の剛体と、母指球の蝶番のサーボ（Aigis の Left = 本人の右 / Right = 本人の左）。</summary>
    private readonly record struct ToeParts(int AigisLeft, int AigisRight, int AigisLeftServo, int AigisRightServo);

    /// <summary>
    /// 1 本の脚（腿・すね・かかと側の足・つま先）と、IK に使う立った形の寸法（関節の位置から測る）。
    /// </summary>
    private sealed class Leg
    {
        public bool AigisLeft { get; private init; }
        public int Heel { get; private init; }
        public int Toe { get; private init; }
        public int HipServo { get; private init; }
        public int KneeServo { get; private init; }
        public int AnkleServo { get; private init; }
        public int ToeServo { get; private init; }

        /// <summary>腿が回る中心（骨盤のローカル）。</summary>
        public Vector3 PivotOnPelvis { get; private init; }

        /// <summary>立った形での、股の中心 → 膝（骨盤のローカル）。</summary>
        public Vector3 ThighToKnee { get; private init; }

        public float ShinLength { get; private init; }

        /// <summary>足首（かかと側の足のローカル）。</summary>
        public Vector3 AnkleOnHeel { get; private init; }

        /// <summary>母指球（かかと側の足のローカル）。</summary>
        public Vector3 BallOnHeel { get; private init; }

        /// <summary>最後の IK で、足首の目標までの距離 / 伸ばしきった脚の長さ（1 を超えたら届いていない）。</summary>
        public float ReachRatio { get; set; }

        /// <summary>前のステップの足首の目標（目標の速度を足首の PD のフィードフォワードに使う）。</summary>
        public Vector3 LastTarget { get; set; }

        /// <summary>足首の曲げの目標 [rad]（調べもの用）。</summary>
        public float AnkleTarget { get; set; }

        /// <summary>前のステップの足首の目標と実際の差（調べもの用）。</summary>
        public Vector3 LastError { get; set; }
        public bool HasTarget { get; set; }

        public int Shin { get; private init; }

        public static Leg Create(PhysicsWorld world, HumanoidRagdoll.Parts parts, IReadOnlyList<HumanoidRagdoll.JointGroup> joints, HumanoidRagdoll.HalfExtents half,
            bool aigisLeft, int toe, int toeServo, int hipServo, int kneeServo, int ankleServo)
        {
            Vector3 Anchor(HumanoidRagdoll.RagdollJoint kind)
            {
                var joint = joints.Single(j => j.Joint == kind);
                return world.GetJointAnchors(joint.PrimaryConstraintId)?.AnchorA
                    ?? throw new InvalidOperationException($"HumanoidRagdoll's {kind} joint has no anchor.");
            }

            var pivot = Anchor(aigisLeft ? HumanoidRagdoll.RagdollJoint.LeftHip : HumanoidRagdoll.RagdollJoint.RightHip);
            var knee = Anchor(aigisLeft ? HumanoidRagdoll.RagdollJoint.LeftKnee : HumanoidRagdoll.RagdollJoint.RightKnee);
            var ankle = Anchor(aigisLeft ? HumanoidRagdoll.RagdollJoint.LeftAnkle : HumanoidRagdoll.RagdollJoint.RightAnkle);
            var pelvis = world.GetState(parts.Pelvis);
            var inverse = Quaternion.Conjugate(pelvis.Orientation);
            var heelId = aigisLeft ? parts.LeftFoot : parts.RightFoot;
            var heel = world.GetState(heelId);
            return new Leg
            {
                AigisLeft = aigisLeft,
                Shin = aigisLeft ? parts.LeftShin : parts.RightShin,
                Heel = heelId,
                Toe = toe,
                HipServo = hipServo,
                KneeServo = kneeServo,
                AnkleServo = ankleServo,
                ToeServo = toeServo,
                PivotOnPelvis = Vector3.Transform(pivot - pelvis.Position, inverse),
                ThighToKnee = Vector3.Transform(knee - pivot, inverse),
                ShinLength = Vector3.Distance(knee, ankle),
                AnkleOnHeel = Vector3.Transform(ankle - heel.Position, Quaternion.Conjugate(heel.Orientation)),
                BallOnHeel = FootGeometry.BallOnHeel(half.Foot),
            };
        }
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
        public Vector3 GloveLocal { get; private set; }

        /// <summary>グローブが付いている剛体（はじめは前腕。前腕が切れたら、その先の破片）。</summary>
        public int GloveBody { get; private set; }

        /// <summary>切り落とされた（もう駆動しない）。</summary>
        public bool Severed { get; set; }

        public void AttachGlove(int body, Vector3 local)
        {
            GloveBody = body;
            GloveLocal = local;
        }
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
            var shoulder = u.Position - (xAxis * side * (half.UpperArm.X - tuning.JointOverlap));
            var elbow = f.Position - (xAxis * side * (half.Forearm.X - tuning.JointOverlap));
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
                GloveBody = fore,
                GloveLocal = Vector3.Transform(glove - f.Position, Quaternion.Conjugate(f.Orientation)),
                UpperLength = Vector3.Distance(shoulder, elbow),
                LowerLength = Vector3.Distance(elbow, glove),
            };
        }

        public Vector3 GlovePosition(PhysicsWorld world)
        {
            var f = world.GetState(GloveBody);
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
