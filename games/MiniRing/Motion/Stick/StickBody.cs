using System.Numerics;

namespace MiniRing.Motion.Stick;

/// <summary>関節の種類: 根（骨盤、位置と向きが自由）・玉（振れとねじれ）・蝶番（1 軸）・固定。</summary>
public enum StickJointKind { Root, Ball, Hinge, Fixed }

/// <summary>
/// 棒人形の 1 部位（線分 ＋ 質量）。点はすべて「休みの姿勢（T ポーズ、骨盤の中心が原点、+Z 前・+Y 上）」の体の座標。
/// 関節は親の部位とのつなぎ目（<see cref="Joint"/>）で、休みでは親も子も回転していない。
/// </summary>
/// <param name="Name">名前（表示・記録用）。</param>
/// <param name="Parent">親の部位の番号（根は −1）。</param>
/// <param name="Kind">関節の種類。</param>
/// <param name="Joint">親とのつなぎ目の点（休み）。</param>
/// <param name="Tip">線分の先の点（休み。描く・届く目標に使う）。</param>
/// <param name="Center">重心（休み、部位の箱の中心）。</param>
/// <param name="HalfExtents">部位の箱の半分の寸法（休みの座標の軸に沿う。慣性に使う）。</param>
/// <param name="Mass">質量 [kg]。</param>
/// <param name="Axis">玉: 振れを測る休みの軸（部位の長さの向き）。蝶番: 回転の軸（正の角で曲がる向き）。</param>
/// <param name="SwingLimit">玉の振れの上限 [rad]。</param>
/// <param name="TwistMin">玉のねじれ・蝶番の角の下限 [rad]。</param>
/// <param name="TwistMax">玉のねじれ・蝶番の角の上限 [rad]。</param>
/// <param name="MaxTorque">関節が筋で出せるトルクの上限 [N·m]（ゲームの体の値）。</param>
public sealed record StickSegment(string Name, int Parent, StickJointKind Kind, Vector3 Joint, Vector3 Tip, Vector3 Center, Vector3 HalfExtents,
    float Mass, Vector3 Axis, float SwingLimit, float TwistMin, float TwistMax, float MaxTorque)
{
    /// <summary>この関節の自由度（姿勢の数の数）。</summary>
    public int Dof => Kind switch { StickJointKind.Root => 6, StickJointKind.Ball => 3, StickJointKind.Hinge => 1, _ => 0 };

    /// <summary>休みの座標の軸に沿った箱の慣性の対角 [kg·m²]。</summary>
    public Vector3 Inertia => Mass / 3f * new Vector3(
        (HalfExtents.Y * HalfExtents.Y) + (HalfExtents.Z * HalfExtents.Z),
        (HalfExtents.X * HalfExtents.X) + (HalfExtents.Z * HalfExtents.Z),
        (HalfExtents.X * HalfExtents.X) + (HalfExtents.Y * HalfExtents.Y));
}

/// <summary>
/// 棒人形の体（docs/STICK_MOTION.md §2）: ゲームのラグドールと同じ寸法・質量・関節の範囲・力の上限を持つ線分の木。
/// 部位の番号は <see cref="Ids"/> の定数。足の裏の点（<see cref="SoleOf"/>）で床に着く。
/// </summary>
public sealed class StickBody
{
    /// <summary>部位の番号。L・R はファイターの左右（<see cref="Hand.Left"/> は +X の側、ラグドール（Aigis）の Right）。</summary>
    public static class Ids
    {
        public const int Pelvis = 0;
        public const int Chest = 1;
        public const int Head = 2;
        public const int UpperArmL = 3;
        public const int ForearmL = 4;
        public const int UpperArmR = 5;
        public const int ForearmR = 6;
        public const int ThighL = 7;
        public const int ShinL = 8;
        public const int FootL = 9;
        public const int ThighR = 10;
        public const int ShinR = 11;
        public const int FootR = 12;
        public const int Count = 13;

        /// <summary>握った武器（<see cref="WithWeapon"/> で足した体だけ）。</summary>
        public const int Weapon = 13;

        public static int UpperArm(Hand hand) => hand == Hand.Left ? UpperArmL : UpperArmR;

        public static int Forearm(Hand hand) => hand == Hand.Left ? ForearmL : ForearmR;

        public static int Thigh(Hand leg) => leg == Hand.Left ? ThighL : ThighR;

        public static int Shin(Hand leg) => leg == Hand.Left ? ShinL : ShinR;

        public static int Foot(Hand leg) => leg == Hand.Left ? FootL : FootR;
    }

    private readonly Vector3[][] _soles;

    public StickBody(IReadOnlyList<StickSegment> segments, Vector3[][] soles, float armReach, float friction, StickPose guard)
    {
        ArgumentNullException.ThrowIfNull(segments);
        ArgumentNullException.ThrowIfNull(soles);
        if (segments.Count is < Ids.Count or > Ids.Count + 1 || soles.Length != 2)
        {
            throw new ArgumentException($"棒人形は {Ids.Count} 部位・足 2 つで作ります。", nameof(segments));
        }

        Segments = segments;
        _soles = soles;
        ArmReach = armReach;
        Friction = friction;
        Guard = guard;
        Dof = segments.Sum(s => s.Dof);
        Offsets = new int[segments.Count];
        var at = 0;
        for (var i = 0; i < segments.Count; i++)
        {
            Offsets[i] = at;
            at += segments[i].Dof;
        }

        Children = [.. Enumerable.Range(0, segments.Count).Select(i => Enumerable.Range(0, segments.Count).Where(j => segments[j].Parent == i).ToArray())];
        TotalMass = segments.Sum(s => s.Mass);
        InSubtree = [.. Enumerable.Range(0, segments.Count).Select(j =>
        {
            var members = new bool[segments.Count];
            foreach (var i in Subtree(j))
            {
                members[i] = true;
            }

            return members;
        })];
    }

    /// <summary>[j][i]: 部位 i が関節 j より先（j の部位とその子孫）にあるか。</summary>
    public bool[][] InSubtree { get; }

    public IReadOnlyList<StickSegment> Segments { get; }

    /// <summary>姿勢の数の数（根 6 ＋ 関節ごと）。</summary>
    public int Dof { get; }

    /// <summary>部位ごとの、姿勢の数の並びの始まり。</summary>
    public int[] Offsets { get; }

    /// <summary>部位ごとの子の番号。</summary>
    public int[][] Children { get; }

    public float TotalMass { get; }

    /// <summary>腕の長さ（肩から手首）[m]。体フレームの目標の単位。</summary>
    public float ArmReach { get; }

    /// <summary>足の裏の摩擦係数（<see cref="FighterTuning.SoleFriction"/>）。</summary>
    public float Friction { get; }

    /// <summary>ゲームの体が立って構えた姿勢（IK の休みの姿勢・自然さの基準）。</summary>
    public StickPose Guard { get; internal set; }

    /// <summary>足 <paramref name="leg"/> の裏の点（休みの座標、足の部位に固定。かかと側の箱の下の 4 隅とつま先の下の前の 2 隅）。</summary>
    public IReadOnlyList<Vector3> SoleOf(Hand leg) => _soles[leg == Hand.Left ? 0 : 1];

    /// <summary>部位 <paramref name="segment"/> とその子孫（関節より先）の番号。</summary>
    public IEnumerable<int> Subtree(int segment)
    {
        yield return segment;
        foreach (var child in Children[segment])
        {
            foreach (var d in Subtree(child))
            {
                yield return d;
            }
        }
    }

    /// <summary>
    /// ゲームの体から写す: 生成直後（T ポーズ、構えへ動く前）の <paramref name="fighter"/> の部位の位置・寸法・質量を読み、
    /// 関節の点をラグドールと同じ決め方（部位の端から重なり <c>0.03 × Scale</c> 内側）で置く。構えの姿勢は、同じ試合を
    /// <paramref name="settleSeconds"/> 秒進めて立った形を写す（<paramref name="game"/> はこの後に進む）。
    /// </summary>
    public static StickBody FromFighter(BoxingGame game, Fighter fighter, FighterTuning tuning, float settleSeconds = 1.5f)
    {
        ArgumentNullException.ThrowIfNull(game);
        ArgumentNullException.ThrowIfNull(fighter);
        ArgumentNullException.ThrowIfNull(tuning);
        var p = fighter.Parts;
        var world = game.World;
        var pelvisState = world.GetState(p.Pelvis);
        var unyaw = Quaternion.Conjugate(pelvisState.Orientation); // 生成直後は全部位が同じ向き（向き合わせの回転だけ）
        Vector3 L(int id) => Vector3.Transform(world.GetState(id).Position - pelvisState.Position, unyaw);
        Vector3 H(int id) => fighter.HalfExtentsOf(id);
        var overlap = 0.03f * tuning.Scale;
        var footForward = 0.05f * tuning.Scale;
        var deg = MathF.PI / 180f;
        var torque = tuning.TorqueScale;
        var leg = torque * tuning.Build.LegTorqueRatio;
        var segments = new StickSegment[Ids.Count];

        var pelvis = L(p.Pelvis);
        var chest = L(p.Chest);
        var head = L(p.Head);
        segments[Ids.Pelvis] = new("pelvis", -1, StickJointKind.Root, pelvis, pelvis + new Vector3(0f, H(p.Pelvis).Y, 0f), pelvis, H(p.Pelvis), world.GetMass(p.Pelvis), Vector3.UnitY, 0f, 0f, 0f, 0f);
        // 背骨: 骨盤の上の中心。胴を起こす力はゲームでは体の外から来る（CLAUDE.md §8）ので、上限は股と同じ値を置く（腹・背の筋の目安）。
        var spine = pelvis + new Vector3(0f, H(p.Pelvis).Y, 0f);
        segments[Ids.Chest] = new("chest", Ids.Pelvis, StickJointKind.Ball, spine, chest + new Vector3(0f, H(p.Chest).Y, 0f), chest, H(p.Chest), world.GetMass(p.Chest),
            Vector3.UnitY, 30f * deg, -30f * deg, 30f * deg, tuning.HipMaxTorque * leg);
        var neck = chest + new Vector3(0f, H(p.Chest).Y, 0f);
        segments[Ids.Head] = new("head", Ids.Chest, StickJointKind.Ball, neck, head + new Vector3(0f, H(p.Head).Y, 0f), head, H(p.Head), world.GetMass(p.Head),
            Vector3.UnitY, 45f * deg, -70f * deg, 70f * deg, tuning.NeckMaxTorque * torque);
        foreach (var hand in new[] { Hand.Left, Hand.Right })
        {
            // ファイターの Left は +X（ラグドールの Right）。
            var upperId = hand == Hand.Left ? p.RightUpperArm : p.LeftUpperArm;
            var foreId = hand == Hand.Left ? p.RightForearm : p.LeftForearm;
            var upper = L(upperId);
            var fore = L(foreId);
            var side = MathF.Sign(upper.X);
            var shoulder = upper - new Vector3(side * (H(upperId).X - overlap), 0f, 0f);
            var elbow = fore - new Vector3(side * (H(foreId).X - overlap), 0f, 0f);
            var wrist = fore + new Vector3(side * H(foreId).X, 0f, 0f);
            var armReach = fighter.ArmReach;
            // 腕はサーボではなく手を引く力（MaxHandForce）で動くので、肩・肘の上限はその力 × てこの長さ。
            segments[Ids.UpperArm(hand)] = new($"upperarm-{hand}", Ids.Chest, StickJointKind.Ball, shoulder, elbow, upper, H(upperId), world.GetMass(upperId),
                new Vector3(side, 0f, 0f), HumanoidRagdollLimits.ShoulderSwing, -HumanoidRagdollLimits.ShoulderTwist, HumanoidRagdollLimits.ShoulderTwist, tuning.MaxHandForce * armReach);
            // 肘の曲げ: 正の角で前腕が前（+Z）へ来る軸（−X の腕は +Y、+X の腕は −Y）。
            segments[Ids.Forearm(hand)] = new($"forearm-{hand}", Ids.UpperArm(hand), StickJointKind.Hinge, elbow, wrist, fore, H(foreId), world.GetMass(foreId),
                new Vector3(0f, -side, 0f), 0f, -10f * deg, 145f * deg, tuning.MaxHandForce * Vector3.Distance(elbow, wrist));
        }

        var soles = new Vector3[2][];
        foreach (var hand in new[] { Hand.Left, Hand.Right })
        {
            var thighId = hand == Hand.Left ? p.RightThigh : p.LeftThigh;
            var shinId = hand == Hand.Left ? p.RightShin : p.LeftShin;
            var footId = hand == Hand.Left ? p.RightFoot : p.LeftFoot;
            var thigh = L(thighId);
            var shin = L(shinId);
            var foot = L(footId);
            var toeId = Enumerable.Range(0, 2).Select(fighter.ToeId).First(t => MathF.Sign(L(t).X) == MathF.Sign(foot.X));
            var toe = L(toeId);
            var hip = pelvis + new Vector3(0f, -H(p.Pelvis).Y, 0f);
            var knee = shin + new Vector3(0f, H(shinId).Y - overlap, 0f);
            var ankle = foot + new Vector3(0f, H(footId).Y - overlap, -(H(footId).Z - footForward));
            var toeTip = toe + new Vector3(0f, -H(toeId).Y, H(toeId).Z);
            segments[Ids.Thigh(hand)] = new($"thigh-{hand}", Ids.Pelvis, StickJointKind.Ball, hip, knee, thigh, H(thighId), world.GetMass(thighId),
                -Vector3.UnitY, 90f * deg, -40f * deg, 40f * deg, tuning.HipMaxTorque * leg);
            segments[Ids.Shin(hand)] = new($"shin-{hand}", Ids.Thigh(hand), StickJointKind.Hinge, knee, ankle, shin, H(shinId), world.GetMass(shinId),
                Vector3.UnitX, 0f, -10f * deg, 140f * deg, tuning.KneeMaxTorque * leg);
            var footMass = world.GetMass(footId) + world.GetMass(toeId);
            var footCenter = ((foot * world.GetMass(footId)) + (toe * world.GetMass(toeId))) / footMass;
            var heel = H(footId);
            segments[Ids.Foot(hand)] = new($"foot-{hand}", Ids.Shin(hand), StickJointKind.Ball, ankle, toeTip, footCenter, heel + new Vector3(0f, 0f, H(toeId).Z), footMass,
                -Vector3.UnitY, HumanoidRagdollLimits.AnkleSwing, -12f * deg, 12f * deg, tuning.AnkleMaxTorque * torque * tuning.Build.LegTorqueRatio);
            var y = foot.Y - heel.Y;
            soles[hand == Hand.Left ? 0 : 1] =
            [
                new(foot.X - heel.X, y, foot.Z - heel.Z), new(foot.X + heel.X, y, foot.Z - heel.Z),
                new(foot.X - heel.X, y, foot.Z + heel.Z), new(foot.X + heel.X, y, foot.Z + heel.Z),
                new(toe.X - H(toeId).X, toe.Y - H(toeId).Y, toe.Z + H(toeId).Z), new(toe.X + H(toeId).X, toe.Y - H(toeId).Y, toe.Z + H(toeId).Z),
            ];
        }

        var body = new StickBody(segments, soles, fighter.ArmReach, tuning.SoleFriction, StickPose.Rest(segments.Sum(s => s.Dof)));
        body.Guard = StickPose.Capture(body, game, fighter, settleSeconds);
        return body;
    }

    /// <summary>武器を握っているか（<see cref="Ids.Weapon"/> がある）。</summary>
    public bool Armed => Segments.Count > Ids.Count;

    private Vector3[][] Soles => _soles;

    /// <summary>
    /// 手 <paramref name="hand"/> に武器を握らせた体: 手首（前腕の先）に玉の関節で付く線分。休みでは腕の続きの向き（振れはそこから測る）。
    /// <paramref name="length"/> = 握りから先の端まで、<paramref name="centerFromGrip"/> = 握りから重心まで [m]、<paramref name="mass"/> [kg]。
    /// 手首の振れの上限 <paramref name="swingLimit"/> は手首の曲げと前腕のひねりを合わせた範囲の近似（ゲームの手首はサーボだけで、硬い範囲は無い）。
    /// </summary>
    public StickBody WithWeapon(Hand hand, float length, float mass, float centerFromGrip, float maxTorque, float swingLimit = 2.1f, bool twoHanded = false, float backLength = 0f)
    {
        if (Armed)
        {
            throw new InvalidOperationException("もう武器を握っています。");
        }

        var forearm = Segments[Ids.Forearm(hand)];
        var axis = Vector3.Normalize(forearm.Tip - forearm.Joint);
        var grip = forearm.Tip;
        var weapon = new StickSegment("weapon", Ids.Forearm(hand), StickJointKind.Ball, grip, grip + (axis * length), grip + (axis * centerFromGrip),
            new Vector3(0.5f * length, 0.005f, 0.005f), mass, axis, swingLimit, -MathF.PI / 2f, MathF.PI / 2f, maxTorque);
        var guard = new float[Dof + 3];
        Array.Copy(Guard.Values, guard, Dof);
        return new StickBody([.. Segments, weapon], Soles, ArmReach, Friction, new StickPose(guard)) { GripHands = twoHanded ? [Hand.Left, Hand.Right] : [hand], BackLength = backLength };
    }

    /// <summary>武器の、握りから後ろの端までの長さ [m]（棒の後ろの端も打つ・自分に当たる。剣は 0）。</summary>
    public float BackLength { get; private init; }

    /// <summary>武器を握っている手（自分の刃が当たっても数えない前腕）。</summary>
    public IReadOnlyList<Hand> GripHands { get; private init; } = [];

    /// <summary>刃の、握りから自傷を数え始める所までの長さ [m]（拳の中・すぐ先は手が握っている）。</summary>
    public const float GripClearance = 0.02f;

    /// <summary>刃の太さの半分 [m]（自傷の深さの見積もり）。</summary>
    public const float BladeRadius = 0.004f;

    /// <summary>
    /// 姿勢 <paramref name="frame"/> で、刃（握りの <see cref="GripClearance"/> 先から刃先まで）が自分の体の部位（太さのある棒: 箱の短い 2 辺の平均を半径とする）へ
    /// 入り込んだ深さの最大 [m] と、その部位（入り込んでいなければ負の隙間）。握っている手の前腕は数えない。
    /// </summary>
    public (float Depth, int Segment) SelfCut(StickFrame frame)
    {
        ArgumentNullException.ThrowIfNull(frame);
        if (!Armed)
        {
            return (float.NegativeInfinity, -1);
        }

        var w = Segments[Ids.Weapon];
        var axis = Vector3.Normalize(w.Tip - w.Joint);
        var a = frame.Point(Ids.Weapon, w.Joint + (axis * GripClearance));
        var b = frame.Tip(Ids.Weapon);
        var worst = Cut(frame, a, b);
        if (BackLength > GripClearance)
        {
            // 握りの後ろ（棒の後ろの端）。両手の間の握り（GripClearance の先まで）は除く。
            var back = Cut(frame, frame.Point(Ids.Weapon, w.Joint - (axis * GripClearance)), frame.Point(Ids.Weapon, w.Joint - (axis * BackLength)));
            worst = back.Depth > worst.Depth ? back : worst;
        }

        return worst;
    }

    private (float Depth, int Segment) Cut(StickFrame frame, Vector3 a, Vector3 b)
    {
        var worst = (Depth: float.NegativeInfinity, Segment: -1);
        for (var i = 0; i < Ids.Count; i++)
        {
            if (GripHands.Any(h => Ids.Forearm(h) == i))
            {
                continue;
            }

            var s = Segments[i];
            var h = s.HalfExtents;
            var radius = 0.5f * (h.X + h.Y + h.Z - MathF.Max(h.X, MathF.Max(h.Y, h.Z)));
            // 骨盤・胸・頭は線分の両端を箱の長い向きの端に、手足は関節から先の点まで。
            var (p, q) = i is Ids.Pelvis or Ids.Chest or Ids.Head
                ? (frame.Point(i, s.Center - LongAxis(h)), frame.Point(i, s.Center + LongAxis(h)))
                : (frame.Joints[i], frame.Tip(i));
            var depth = radius + BladeRadius - StickMath.SegmentDistance(a, b, p, q);
            if (depth > worst.Depth)
            {
                worst = (depth, i);
            }
        }

        return worst;
    }

    private static Vector3 LongAxis(Vector3 h) => h.X >= h.Y && h.X >= h.Z ? new Vector3(h.X, 0f, 0f) : h.Y >= h.Z ? new Vector3(0f, h.Y, 0f) : new Vector3(0f, 0f, h.Z);

    /// <summary>標準体格の棒人形（ゲームの体を 1 体作って写す。数十ミリ秒）。</summary>
    public static StickBody Standard(FighterTuning? tuning = null)
    {
        tuning ??= FighterTuning.Default;
        using var game = new BoxingGame(tuning, seed: 1, corners: new BoxingCorners("stick", _ => new StandGuard(), _ => new StandGuard()));
        return FromFighter(game, game.Cpu, tuning);
    }
}

/// <summary>ラグドール（Aigis の HumanoidRagdoll）の関節の範囲のうち、定数で公開されている物 [rad]。</summary>
internal static class HumanoidRagdollLimits
{
    public const float ShoulderSwing = Aigis.Physics.HumanoidRagdoll.ShoulderSwingLimitDegrees * MathF.PI / 180f;
    public const float ShoulderTwist = Aigis.Physics.HumanoidRagdoll.ShoulderTwistLimitDegrees * MathF.PI / 180f;
    public const float AnkleSwing = Aigis.Physics.HumanoidRagdoll.AnkleSwingLimitDegrees * MathF.PI / 180f;
}
