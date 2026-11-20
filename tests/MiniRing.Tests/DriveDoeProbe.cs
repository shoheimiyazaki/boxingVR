using System.Globalization;
using System.Numerics;
using System.Text;
using MiniRing.Arena;

namespace MiniRing.Tests;

/// <summary>
/// 腕・胴から拳への駆動の DOE（docs/plans/PHYSICS_DRIVE_DOE_LOG.md・DRIVE_DOE_ANALYSIS.md）。
/// 右ストレート 1 本（punchlab の標準・守らない相手）を、駆動に関わる要素の水準を空間充填（LHS）と 1 要素ずつの格子で変えて回し、
/// 1 点 1 行の特徴量表（docs/arena/drive/*.csv）を書く。同じ実測の時系列から、駆動の関数（<see cref="ArmMuscle"/>）をそのまま呼ぶ縮約モデル
/// （剛体 1 つ）を走らせ、モデルの予測も同じ行に書く。物理の値は変えない（測るだけ）。環境変数:
/// DRIVE_DOE_STAGE（design / run / oat / holdout）・DRIVE_DOE_THREADS（既定 4）・DRIVE_DOE_LABEL（出力名）・DRIVE_DOE_N（点数）。
/// </summary>
[TestClass]
[TestCategory("Probe")]
public sealed class DriveDoeProbe
{
    private const float Dt = 1f / 240f;

    /// <summary>要素: 名前・下限・基準・上限・種類（T = 体の値 / K = 入力の指令 / C = 条件）。</summary>
    internal sealed record Factor(string Name, double Lo, double Base, double Hi, char Kind);

    private static readonly FighterTuning B = FighterTuning.Default;

    internal static readonly Factor[] Factors =
    [
        new("MaxHandForce", 175, B.MaxHandForce, 700, 'T'),
        new("ElbowMax", 20, B.ElbowMuscleMaxSpeed, 80, 'T'),
        new("ShoulderMax", 20, B.ShoulderMuscleMaxSpeed, 80, 'T'),
        new("ActTime", 0.008, B.MuscleActivationTime, 0.04, 'T'),
        new("HandKp", 200, B.HandKp, 1000, 'T'),
        new("LooseDamp", 0.1, B.LooseHandDamping, 1.0, 'T'),
        new("ArmServoHz", 4, B.ArmServoHz, 20, 'T'),
        new("BodyEff", 0, B.BodyToFistEfficiency, 1, 'T'),
        new("BodyTime", 0.02, B.BodyToFistTime, 0.2, 'T'),
        new("ReactShare", 0, B.HandReactionShare, 1, 'T'),
        new("BorrowSpeed", 0.3, B.BodyBorrowSpeed, 2, 'T'),
        new("BodyDrive", 0, B.BodyDrive, 3, 'T'),
        new("MaxTwistDeg", 20, B.MaxTorsoTwistRadians * 180 / Math.PI, 70, 'T'),
        new("HipShare", 0.2, B.HipTwistShare, 1.0, 'T'),
        new("UprightKpRatio", 0.5, 1.0, 3.0, 'T'),
        new("TwistBias", 0, 0, 0.6, 'K'),
        new("StepInScale", 0, 1, 3, 'K'),
        new("Spring", 0, 1, 1, 'K'),
        new("Range", 0.9, 1.05, 1.4, 'C'),
        new("Telegraph", 0, 0.1343, 0.3, 'C'),
        new("Curvature", 0.12, B.HandMuscleCurvature, 0.5, 'T'),
        new("MinLeverDeg", 4, B.ElbowMinLeverRadians * 180 / Math.PI, 30, 'T'),
        new("LeadHz", 0.5, B.LeadSpringHz, 6, 'T'),
        new("TrunkTorque", 28.2, B.TrunkRotatorTorque, 300, 'T'),
        new("TrunkFF", 0, B.TrunkTwistFeedForward, 1, 'T'),
    ];

    internal static FighterTuning ApplyTuning(double[] v)
    {
        var t = B with
        {
            MaxHandForce = (float)v[0],
            ElbowMuscleMaxSpeed = (float)v[1],
            ShoulderMuscleMaxSpeed = (float)v[2],
            MuscleActivationTime = (float)v[3],
            HandKp = (float)v[4],
            LooseHandDamping = (float)v[5],
            ArmServoHz = (float)v[6],
            BodyToFistEfficiency = (float)v[7],
            BodyToFistTime = (float)v[8],
            HandReactionShare = (float)v[9],
            BodyBorrowSpeed = (float)v[10],
            BodyDrive = (float)v[11],
            MaxTorsoTwistRadians = (float)(v[12] * Math.PI / 180),
            HipTwistShare = (float)v[13],
            UprightKp = B.UprightKp * (float)v[14],
            HandMuscleCurvature = (float)v[20],
            ElbowMinLeverRadians = (float)(v[21] * Math.PI / 180),
            LeadSpringHz = (float)v[22],
            TrunkRotatorTorque = (float)v[23],
            TrunkTwistFeedForward = (float)v[24],
        };
        // HandKd は HandKp の減衰比 0.8 を保つ（√Kp に比例）。
        return t with { HandKd = B.HandKd * MathF.Sqrt(t.HandKp / B.HandKp) };
    }

    // ---------------------------------------------------------------- 設計

    internal static List<double[]> LatinHypercube(int n, int seed)
    {
        var rng = new Random(seed);
        var pts = Enumerable.Range(0, n).Select(_ => new double[Factors.Length]).ToList();
        for (var j = 0; j < Factors.Length; j++)
        {
            var perm = Enumerable.Range(0, n).OrderBy(_ => rng.Next()).ToArray();
            for (var i = 0; i < n; i++)
            {
                var u = (perm[i] + rng.NextDouble()) / n;
                pts[i][j] = Factors[j].Lo + (u * (Factors[j].Hi - Factors[j].Lo));
            }
        }

        return pts;
    }

    /// <summary>1 要素ずつの格子（他は基準）: 要素ごとに 9 水準。</summary>
    internal static List<(string Name, double[] Values)> OneAtATime(int levels)
    {
        var list = new List<(string, double[])>();
        list.Add(("base", Factors.Select(f => f.Base).ToArray()));
        for (var j = 0; j < Factors.Length; j++)
        {
            for (var k = 0; k < levels; k++)
            {
                var v = Factors.Select(f => f.Base).ToArray();
                v[j] = Factors[j].Lo + ((Factors[j].Hi - Factors[j].Lo) * k / (levels - 1));
                list.Add((Factors[j].Name + "@" + k, v));
            }
        }

        return list;
    }

    // ---------------------------------------------------------------- 1 点

    internal sealed record Sample(float T, Vector3 Glove, Vector3 GloveV, Vector3 Shoulder, Vector3 ShoulderV, Vector3 Target, float Cap, float Desired, float Force,
        float Spring, Vector3 Dir, float ChestYaw, float PelvisYaw, Vector3 ChestV, float Fist, float ActSum, float Kd, float TwistTarget, float ChestRate, Vector3 ChestToShoulder, float TwistShape, float TwistBiasCmd);

    internal sealed record RunResult(bool Hit, float THit, Dictionary<string, double> F, List<Sample> Series, float[] Act0, float ArmMass, float Upper, float Lower, float MaxTargetSpeed, float Smoothing, TuningSnap Snap, float ChestYawInertia = 0.2167f);

    internal sealed record TuningSnap(FighterTuning Tuning);

    internal static RunResult RunOne(double[] values, bool frozen, int seed)
    {
        var tuning = ApplyTuning(values);
        var knobs = new PunchLab.DriveKnobs((float)values[15], (float)values[16], (float)values[17]);
        var path = values.Length > Factors.Length ? (int)Math.Round(values[Factors.Length]) switch { 1 => PunchLabPath.Instant, 2 => PunchLabPath.InstantThrough, _ => PunchLabPath.Standard } : PunchLabPath.Standard;
        var setup = new PunchLab.Setup(path, "still", "reach", (float)values[18], false);
        using var bout = new PunchLab.Bout(setup, seed, (float)values[19], tuning, knobs);
        while (bout.PunchTime is null)
        {
            bout.Step();
        }

        var game = bout.Game;
        var f = game.Player;
        var world = game.World;
        var chest0 = world.GetState(f.Parts.Chest);
        var pelvis0 = world.GetState(f.Parts.Pelvis);
        var series = new List<Sample>();
        var act0 = f.HandActivation(Hand.Right).ToArray();
        var armMass = f.ArmMass(Hand.Right);
        HitEvent best = default;
        var hit = false;
        var tHit = -1f;
        var facing = 0f;
        while (bout.PunchTime is { } t && t < 0.55f)
        {
            var dir = f.Frame.Forward;
            var chest = world.GetState(f.Parts.Chest);
            var pelvis = world.GetState(f.Parts.Pelvis);
            var shoulder = f.ShoulderPosition(Hand.Right);
            var glove = f.GloveCenter(Hand.Right);
            var gloveV = f.GloveVelocity(Hand.Right);
            var shoulderV = chest.LinearVelocity + Vector3.Cross(chest.AngularVelocity, shoulder - chest.Position);
            var faceQ = Quaternion.CreateFromAxisAngle(Vector3.UnitY, f.Frame.YawRadians);
            var chestYaw = -FighterMath.RotationError(chest.Orientation, faceQ).Y;
            var pelvisYaw = -FighterMath.RotationError(pelvis.Orientation, faceQ).Y;
            var act = f.HandActivation(Hand.Right);
            var actSum = 0f;
            for (var i = 0; i < act.Length; i++)
            {
                actSum += act[i];
            }

            bout.Step();
            var cmd = f.LastCommand;
            var twistTarget = Math.Clamp(PunchMechanics.TorsoTwist(cmd.Left.TargetLocal, cmd.Right.TargetLocal, f.ArmReach, tuning.MaxTorsoTwistRadians) + (float.IsFinite(cmd.TwistBias) ? cmd.TwistBias : 0f), -tuning.MaxTorsoTwistRadians, tuning.MaxTorsoTwistRadians);
            var trace = f.HandTrace(Hand.Right);
            var spring = f.LastCommand.Right.Spring;
            var kd = tuning.HandKd * (1f + ((tuning.LooseHandDamping - 1f) * Math.Clamp(spring, 0f, 1f)));
            series.Add(new Sample(t, glove, gloveV, shoulder, shoulderV, trace.Target, trace.Cap, trace.Desired, trace.Force, spring, dir,
                chestYaw, pelvisYaw, chest.LinearVelocity, gloveV.Length(), actSum, kd, twistTarget, chest.AngularVelocity.Y, shoulder - chest.Position, PunchMechanics.TorsoTwist(cmd.Left.TargetLocal, cmd.Right.TargetLocal, f.ArmReach, 1f), float.IsFinite(cmd.TwistBias) ? cmd.TwistBias : 0f));
            if (frozen)
            {
                world.SetDynamicPose(f.Parts.Chest, chest0.Position, chest0.Orientation);
                world.SetDynamicPose(f.Parts.Pelvis, pelvis0.Position, pelvis0.Orientation);
            }

            var after = bout.PunchTime ?? t;
            foreach (var h in game.RecentHits.Where(h => h.Attacker == f && h.Hand == Hand.Right && !h.Kick && !h.Elbow))
            {
                if (after > 0f && after <= 0.7553f && (!hit || h.Energy > best.Energy))
                {
                    best = h;
                    hit = true;
                    tHit = after;
                }
            }

            if (hit && after > tHit + (2f * Dt))
            {
                break;
            }

            facing = f.Frame.YawRadians;
        }

        _ = facing;
        var dict = new Dictionary<string, double>();
        if (hit)
        {
            dict["closing"] = best.Speed;
            dict["mass"] = best.ImpactMass;
            dict["energy"] = best.Energy;
            dict["momentum"] = best.Momentum;
            dict["bodyShare"] = best.BodyShare;
        }

        var (upperLength, lowerLength) = f.ArmLengths(Hand.Right);
        var chestYawInertia = world.GetLocalInertiaDiagonal(f.Parts.Chest).Y;
        return new RunResult(hit, tHit, dict, series, act0, armMass, upperLength, lowerLength, tuning.MaxTargetSpeed, tuning.TargetVelocitySmoothing, new TuningSnap(tuning), chestYawInertia);
    }

    // ---------------------------------------------------------------- 縮約モデル

    /// <summary>
    /// 縮約モデル: 剛体 1 つ（質量 <paramref name="mass"/>）を、実測の肩の動き・拳の目標・力の上限・指令の中の移送の割合を入力に、
    /// 実物と同じ関数（HandAccel → Excitation → Activate → ArmMuscle.Force）で動かす。体の拘束・サーボ・接触・反作用は入っていない（残差になる）。
    /// 返すのは拳の速さの時系列（歩ごとの、その歩の初めの値。実測と同じ時点）。
    /// </summary>
    internal static float[] Model(RunResult r, float mass, int model)
    {
        var tuning = r.Snap.Tuning;
        var n = r.Series.Count;
        var speed = new float[n];
        var s0 = r.Series[0];
        var p = s0.Glove;
        var v = s0.GloveV;
        var act = (float[])r.Act0.Clone();
        var tv = Vector3.Zero;
        var prevTarget = s0.Target;
        var hasPrev = false;
        // 肘から拳の長さ・肩から肘の長さは実測の姿勢から（腕の長さの半分ずつは近似だが、てこの比にしか効かない）。
        for (var k = 0; k < n; k++)
        {
            var s = r.Series[k];
            speed[k] = v.Length();
            if (hasPrev)
            {
                var raw = ClampLength((s.Target - prevTarget) / Dt, r.MaxTargetSpeed);
                tv = Vector3.Lerp(tv, raw, Math.Clamp(r.Smoothing * Dt, 0f, 1f));
            }

            prevTarget = s.Target;
            hasPrev = true;
            var accel = FighterMath.HandAccel(p, v, s.Target, tv, tuning.HandKp, s.Kd);
            var desired = accel * mass;
            // 力の上限は実測（打つ向きで決まる連結度・体の有効質量・体を借りる倍率の結果）を外から入れる。
            var chestQ = Quaternion.Identity;
            var excitation = ArmMuscle.Excitation(desired, s.Cap, chestQ);
            for (var i = 0; i < ArmMuscle.Groups; i++)
            {
                act[i] = ArmMuscle.Activate(act[i], ArmMuscle.GroupExcitation(excitation, i), Dt, tuning.MuscleActivationTime, tuning.MuscleDeactivationTime);
            }

            var from = p - s.Shoulder;
            var dist = from.Length();
            var radial = dist > 1e-6f ? from / dist : s.Dir;
            var upper = r.Upper;
            var lower = r.Lower;
            var pose = new ArmMuscle.Pose(radial, dist, v - s.ShoulderV, upper, lower);
            var force = ArmMuscle.Force(act, chestQ, s.Cap, pose, ArmMuscle.Constants.From(tuning, 0f), Dt, mass);
            if (model == 1)
            {
                // 理想（Hill の力と速さの関係なし）: 欲しい力を上限まで出す。
                force = ClampLength(desired, s.Cap);
            }

            v += force / mass * Dt;
            p += v * Dt;
        }

        return speed;
    }

    private static Vector3 ClampLength(Vector3 v, float max)
    {
        var l = v.Length();
        return l > max && l > 0f ? v * (max / l) : v;
    }

    /// <summary>
    /// 胴のひねりの縮約モデル: 胸の鉛直軸まわり 1 自由度を <c>Fighter.ApplyUprightImpulse</c> と同じ式（PD + 角加速度の上限）で、実測のひねりの目標の系列へ動かす。
    /// 返すのは打ち出しから当たる歩までの胸の角速度の最大と、それが肩（胸の中心からの実測の腕）を前へ運ぶ速さの最大。骨盤・腕の反作用・脚は入っていない。
    /// </summary>
    internal sealed record TrunkParams(float Kp, float Kd, float MaxAccel, float RomRadians, float FeedForward, float InertiaRatio, float Smoothing, float MaxRate);

    /// <summary>実物（<c>Fighter.ApplyUprightImpulse</c> の鉛直軸まわり）と同じ式の係数: Kp = トルク / (胸の慣性 × 可動域)、Kd = 傾きのバネと同じ減衰比、上限 = トルク / 慣性。</summary>
    internal static TrunkParams TrunkBase(FighterTuning t, float chestYawInertia)
    {
        var torque = t.TrunkRotatorTorque * t.TorqueScale;
        var kp = torque / (chestYawInertia * t.MaxTorsoTwistRadians);
        return new TrunkParams(kp, t.UprightKd / MathF.Sqrt(t.UprightKp) * MathF.Sqrt(kp), torque / chestYawInertia, t.MaxTorsoTwistRadians, t.TrunkTwistFeedForward, 1f, t.TargetVelocitySmoothing, t.TrunkTwistMaxRate);
    }

    internal static (float PeakRate, float PeakShoulderFwd, float MeanLever) TrunkModel(RunResult r, int end, TrunkParams? p = null)
    {
        var t = r.Snap.Tuning;
        p ??= TrunkBase(t, r.ChestYawInertia);
        var targetRate = 0f;
        var previousTarget = r.Series[0].TwistTarget;
        var s0 = r.Series[0];
        var angle = s0.ChestYaw;
        var rate = s0.ChestRate;
        var peakRate = 0f;
        var peakFwd = 0f;
        var lever = 0f;
        var c = 0;
        for (var k = 0; k <= end && k < r.Series.Count; k++)
        {
            var s = r.Series[k];
            if (s.T >= 0f)
            {
                peakRate = MathF.Max(peakRate, MathF.Abs(rate));
                var v = Vector3.Cross(Vector3.UnitY * rate, s.ChestToShoulder);
                peakFwd = MathF.Max(peakFwd, Vector3.Dot(v, s.Dir));
                lever += Vector3.Dot(Vector3.Cross(Vector3.UnitY, s.ChestToShoulder), s.Dir);
                c++;
            }

            // 実測の符号の取り方（ChestYaw = −RotationError(胸, 向き).Y）に合わせ、ひねりの目標との差で PD。
            var target = Math.Clamp((s.TwistShape * p.RomRadians) + s.TwistBiasCmd, -p.RomRadians, p.RomRadians);
            targetRate = Math.Clamp((Math.Clamp((target - previousTarget) / Dt, -p.MaxRate, p.MaxRate) * Math.Clamp(p.Smoothing * Dt, 0f, 1f)) + (targetRate * (1f - Math.Clamp(p.Smoothing * Dt, 0f, 1f))), -p.MaxRate, p.MaxRate);
            previousTarget = target;
            var error = target - angle;
            var accel = (error * p.Kp) + (((p.FeedForward * targetRate) - rate) * p.Kd);
            accel = Math.Clamp(accel * 0.9f / p.InertiaRatio, -p.MaxAccel, p.MaxAccel);
            rate += accel * Dt;
            angle += rate * Dt;
        }

        return (peakRate, peakFwd, c > 0 ? lever / c : 0f);
    }

    /// <summary>当たる直前の腕の筋の中身: 肘の角度から力の出しやすさ・てこの比・肘の縮む速さの尺度・Hill の速さの関係の割合。</summary>
    internal static (float Fv, float Strength, float LeverRatio, float Radial) Diagnose(RunResult r, int i)
    {
        var s = r.Series[i];
        var tuning = r.Snap.Tuning;
        var from = s.Glove - s.Shoulder;
        var dist = from.Length();
        var radial = dist > 1e-6f ? from / dist : s.Dir;
        var pose = new ArmMuscle.Pose(radial, dist, s.GloveV - s.ShoulderV, r.Upper, r.Lower);
        var constants = ArmMuscle.Constants.From(tuning, 0f);
        var (elbow, elbowSpeed, _, reference, _) = ArmMuscle.Leverage(pose, constants);
        var max = constants.Tired(constants.ElbowSpeed, reference);
        var rad = Vector3.Dot(pose.Velocity, radial);
        var ur = rad * elbowSpeed / max;
        return (ArmMuscle.ForceVelocity(ur, constants.Curvature, constants.EccentricMax, constants.EccentricSlope), elbow, elbowSpeed, rad);
    }

    // ---------------------------------------------------------------- 特徴量

    /// <summary>胴のひねりの候補（縮約モデルで先に予測する）: 名前・基準の体からの変え方。比は胸の鉛直軸まわりの有効慣性を実測へ合わせる補正（骨盤・脚・腕の反作用の分）。</summary>
    internal static readonly (string Name, Func<TrunkParams, TrunkParams> Change)[] TrunkCandidates =
    [
        ("base", p => p),
        ("ff", p => p with { FeedForward = 1f }),
        ("rom50", p => p with { RomRadians = 50f * MathF.PI / 180f }),
        ("ff_rom50", p => p with { FeedForward = 1f, RomRadians = 50f * MathF.PI / 180f }),
        ("kp2", p => p with { Kp = p.Kp * 2f, Kd = p.Kd * MathF.Sqrt(2f) }),
        ("kp4", p => p with { Kp = p.Kp * 4f, Kd = p.Kd * 2f }),
        ("ff_kp2", p => p with { FeedForward = 1f, Kp = p.Kp * 2f, Kd = p.Kd * MathF.Sqrt(2f) }),
        ("ff_rom50_kp2", p => p with { FeedForward = 1f, RomRadians = 50f * MathF.PI / 180f, Kp = p.Kp * 2f, Kd = p.Kd * MathF.Sqrt(2f) }),
        ("ff_rom50_kp4", p => p with { FeedForward = 1f, RomRadians = 50f * MathF.PI / 180f, Kp = p.Kp * 4f, Kd = p.Kd * 2f }),
    ];

    private static readonly float[] InertiaRatios = [1f, 2f, 4f, 6f];

    /// <summary>縮約モデルの質量の格子 [kg]（実効の質量を当てはめる。前腕 1.3・上腕 + 前腕 3.3）。</summary>
    private static readonly float[] ModelMasses = [0.5f, 0.8f, 1.1f, 1.4f, 2.0f, 3.3f];

    internal static readonly string[] FeatureNames = TrunkCandidates.SelectMany(c => InertiaRatios.Select(q => $"tmP_{c.Name}_r{q:0}")).Concat(
    [
        "tmPeakChestRate", "tmPeakShoulderFwd", "realPeakChestRate", "meanShoulderLever", "fvAtHit", "elbowStrengthAtHit", "leverRatioAtHit", "radialSpeedAtHit",
        "hit", "tHit", "fistHit", "fwdFistHit", "shoulderFwdHit", "relFwdHit", "chestYawRateHit", "pelvisYawRateHit", "chestFwdHit",
        "closing", "mass", "energy", "momentum", "bodyShare",
        "peakFist", "peakRelFwd", "peakShoulderFwd", "peakChestYaw", "peakPelvisYaw", "peakChestYawAngle", "peakForce", "meanActSum", "satFrac", "forceAtHit", "capAtHit",
        "m33PeakFist", "m33FistHit", "m13PeakFist", "m13FistHit", "mFitPeakFist", "mFitFistHit", "mIdealPeakFist", "mIdealFistHit", "modelRmse33", "modelRmse13",
    ]).Concat(ModelMasses.Select(m => $"mass{m:0.0}Peak")).ToArray();

    internal static double[] Features(RunResult r)
    {
        var s = r.Series;
        var n = s.Count;
        // 打ち出し（t >= 0）から当たる歩（または 0.40 s）まで。
        var end = r.Hit ? s.FindLastIndex(x => x.T < r.THit) : s.FindLastIndex(x => x.T < 0.40f);
        if (end < 0)
        {
            end = n - 1;
        }

        var hitI = Math.Max(0, end);
        var h = s[hitI];
        float Fwd(Vector3 x) => Vector3.Dot(x, h.Dir);
        var range = s.Where(x => x.T >= 0f && s.IndexOf(x) <= end).ToList();
        if (range.Count == 0)
        {
            range = [h];
        }

        var peakFist = range.Max(x => x.Fist);
        var peakRel = range.Max(x => Vector3.Dot(x.GloveV - x.ShoulderV, x.Dir));
        var peakSh = range.Max(x => Vector3.Dot(x.ShoulderV, x.Dir));
        var peakChest = range.Max(x => MathF.Abs(x.ChestYaw));
        var angle0 = s[0].ChestYaw;
        var peakAngle = range.Max(x => MathF.Abs(x.ChestYaw - angle0));
        var chestRates = new List<float>();
        var pelvisRates = new List<float>();
        for (var i = 1; i < s.Count; i++)
        {
            chestRates.Add((s[i].ChestYaw - s[i - 1].ChestYaw) / Dt);
            pelvisRates.Add((s[i].PelvisYaw - s[i - 1].PelvisYaw) / Dt);
        }

        var hi1 = Math.Max(1, hitI);
        var peakChestRate = range.Count > 0 ? Enumerable.Range(1, Math.Max(1, end)).Where(i => i < chestRates.Count + 1 && s[i].T >= 0f).Select(i => MathF.Abs(chestRates[i - 1])).DefaultIfEmpty(0f).Max() : 0f;
        var peakPelvisRate = Enumerable.Range(1, Math.Max(1, end)).Where(i => i < pelvisRates.Count + 1 && s[i].T >= 0f).Select(i => MathF.Abs(pelvisRates[i - 1])).DefaultIfEmpty(0f).Max();
        var chestRateHit = chestRates.Count > 0 ? chestRates[Math.Min(hi1, chestRates.Count) - 1] : 0f;
        var pelvisRateHit = pelvisRates.Count > 0 ? pelvisRates[Math.Min(hi1, pelvisRates.Count) - 1] : 0f;
        var satFrac = range.Count(x => x.Desired > x.Cap * 1.001f) / (float)range.Count;

        var m33 = Model(r, r.ArmMass, 0);
        var m13 = Model(r, r.ArmMass * 0.4f, 0);
        var mIdeal = Model(r, r.ArmMass, 1);
        float Peak(float[] x) => Enumerable.Range(0, x.Length).Where(i => s[i].T >= 0f && i <= end).Select(i => x[i]).DefaultIfEmpty(0f).Max();
        float Rmse(float[] x)
        {
            var sum = 0.0;
            var c = 0;
            for (var i = 0; i <= end; i++)
            {
                if (s[i].T >= -0.05f)
                {
                    var d = x[i] - s[i].Fist;
                    sum += d * d;
                    c++;
                }
            }

            return c > 0 ? (float)Math.Sqrt(sum / c) : 0f;
        }

        var tm = TrunkModel(r, end);
        var diag = Diagnose(r, hitI);
        double D(string k) => r.F.TryGetValue(k, out var v) ? v : double.NaN;
        var candidates = TrunkCandidates.SelectMany(c => InertiaRatios.Select(ratio => (double)TrunkModel(r, end, c.Change(TrunkBase(r.Snap.Tuning, r.ChestYawInertia)) with { InertiaRatio = ratio }).PeakRate)).ToArray();
        return candidates.Concat(new double[]
        {
            tm.PeakRate, tm.PeakShoulderFwd, peakChestRate, tm.MeanLever, diag.Fv, diag.Strength, diag.LeverRatio, diag.Radial,
            r.Hit ? 1 : 0, r.THit, h.Fist, Fwd(h.GloveV), Fwd(h.ShoulderV), Fwd(h.GloveV - h.ShoulderV), chestRateHit, pelvisRateHit, Fwd(h.ChestV),
            D("closing"), D("mass"), D("energy"), D("momentum"), D("bodyShare"),
            peakFist, peakRel, peakSh, peakChestRate, peakPelvisRate, peakAngle, range.Max(x => x.Force), range.Average(x => x.ActSum), satFrac, h.Force, h.Cap,
            Peak(m33), m33[hitI], Peak(m13), m13[hitI], 0, 0, Peak(mIdeal), mIdeal[hitI], Rmse(m33), Rmse(m13),
        }).Concat(ModelMasses.Select(m => (double)Peak(Model(r, m, 0)))).ToArray();
    }

    // ---------------------------------------------------------------- 実行

    private static string OutDir
    {
        get
        {
            var dir = AppContext.BaseDirectory;
            while (dir is not null && !File.Exists(Path.Combine(dir, "MiniRing.slnx")))
            {
                dir = Path.GetDirectoryName(dir);
            }

            var d = Path.Combine(dir ?? ".", "docs", "arena", "drive");
            Directory.CreateDirectory(d);
            return d;
        }
    }

    private static string Header() => "id,group,frozen,seed," + string.Join(",", Factors.Select(f => f.Name)) + ",PathCode," + string.Join(",", FeatureNames);

    private static string Row(string id, string group, bool frozen, int seed, double[] values, double[] feats)
        => string.Join(",", new[] { id, group, frozen ? "1" : "0", seed.ToString(CultureInfo.InvariantCulture) }
            .Concat((values.Length > Factors.Length ? values : values.Append(0.0)).Select(x => x.ToString("G6", CultureInfo.InvariantCulture)))
            .Concat(feats.Select(x => x.ToString("G6", CultureInfo.InvariantCulture))));

    private static void Execute(string label, IReadOnlyList<(string Id, string Group, bool Frozen, int Seed, double[] Values)> jobs, int threads)
    {
        var path = Path.Combine(OutDir, label + ".csv");
        var done = new HashSet<string>();
        if (File.Exists(path))
        {
            foreach (var line in File.ReadLines(path).Skip(1))
            {
                done.Add(line.Split(',')[0]);
            }
        }
        else
        {
            File.WriteAllText(path, Header() + "\n");
        }

        var todo = jobs.Where(j => !done.Contains(j.Id)).ToList();
        Console.WriteLine($"DOE {label}: {todo.Count} to run ({done.Count} done)");
        var gate = new object();
        var count = 0;
        var sw = System.Diagnostics.Stopwatch.StartNew();
        Parallel.ForEach(todo, new ParallelOptions { MaxDegreeOfParallelism = threads }, job =>
        {
            string row;
            try
            {
                var r = RunOne(job.Values, job.Frozen, job.Seed);
                row = Row(job.Id, job.Group, job.Frozen, job.Seed, job.Values, Features(r));
            }
            catch (Exception e)
            {
                Console.WriteLine($"FAILED {job.Id}: {e.GetType().Name} {e.Message}");
                return;
            }

            lock (gate)
            {
                File.AppendAllText(path, row + "\n");
                count++;
                if (count % 10 == 0)
                {
                    Console.WriteLine($"DOE {label}: {count}/{todo.Count} {sw.Elapsed.TotalMinutes:F1} min");
                }
            }
        });
        Console.WriteLine($"DOE {label}: finished {count}/{todo.Count} in {sw.Elapsed.TotalMinutes:F1} min");
    }

    private static int Threads => int.TryParse(Environment.GetEnvironmentVariable("DRIVE_DOE_THREADS"), out var t) ? t : 4;

    private static string Label(string fallback) => Environment.GetEnvironmentVariable("DRIVE_DOE_LABEL") is { Length: > 0 } l ? l : fallback;

    [TestMethod]
    public void Design_WritesTheFactorTable()
    {
        var sb = new StringBuilder("factor,lo,base,hi,kind\n");
        foreach (var f in Factors)
        {
            sb.AppendLine(string.Create(CultureInfo.InvariantCulture, $"{f.Name},{f.Lo:G6},{f.Base:G6},{f.Hi:G6},{f.Kind}"));
        }

        File.WriteAllText(Path.Combine(OutDir, "factors.csv"), sb.ToString());
    }

    /// <summary>1 つの点（基準）を自由 / 固定で回し、時系列と縮約モデルを書く（動作確認と、モデルの当たりの目視用）。</summary>
    [TestMethod]
    public void Baseline_WritesSeries()
    {
        foreach (var frozen in new[] { false, true })
        {
            var sw = System.Diagnostics.Stopwatch.StartNew();
            var r = RunOne(Factors.Select(f => f.Base).ToArray(), frozen, 1);
            Console.WriteLine($"baseline frozen={frozen} hit={r.Hit} tHit={r.THit:F4} steps={r.Series.Count} wall={sw.Elapsed.TotalSeconds:F1}s");
            Console.WriteLine($"  body: armMass={r.ArmMass:F3} upper={r.Upper:F3} lower={r.Lower:F3} tuning UprightKp={r.Snap.Tuning.UprightKp:F1} Kd={r.Snap.Tuning.UprightKd:F1} MaxUprightAccel={r.Snap.Tuning.MaxUprightAccel:F1}");
            if (!frozen)
            {
                using var probe = new PunchLab.Bout(new PunchLab.Setup(PunchLabPath.Standard, "still", "reach", 1.05f, false), 1, 0.1343f, B);
                var pf = probe.Game.Player;
                var pw = probe.Game.World;
                Console.WriteLine($"  chest mass={pw.GetMass(pf.Parts.Chest):F2} inertia={pw.GetLocalInertiaDiagonal(pf.Parts.Chest)}  pelvis mass={pw.GetMass(pf.Parts.Pelvis):F2} inertia={pw.GetLocalInertiaDiagonal(pf.Parts.Pelvis)}  head mass={pw.GetMass(pf.Parts.Head):F2}  total supported={pf.SupportedMass:F2} height={pf.HeadPosition.Y:F3} TorqueScale={B.TorqueScale:F3}");
            }

            var m33 = Model(r, r.ArmMass, 0);
            var m13 = Model(r, r.ArmMass * 0.4f, 0);
            var sb = new StringBuilder("t,fist,fwd,shoulderFwd,relFwd,chestYaw,pelvisYaw,spring,desired,cap,force,actSum,m33,m13\n");
            for (var i = 0; i < r.Series.Count; i += 2)
            {
                var s = r.Series[i];
                sb.AppendLine(string.Create(CultureInfo.InvariantCulture,
                    $"{s.T:F5},{s.Fist:F4},{Vector3.Dot(s.GloveV, s.Dir):F4},{Vector3.Dot(s.ShoulderV, s.Dir):F4},{Vector3.Dot(s.GloveV - s.ShoulderV, s.Dir):F4},{s.ChestYaw:F4},{s.PelvisYaw:F4},{s.Spring:F2},{s.Desired:F1},{s.Cap:F1},{s.Force:F1},{s.ActSum:F3},{m33[i]:F4},{m13[i]:F4}"));
            }

            File.WriteAllText(Path.Combine(OutDir, $"series_baseline_{(frozen ? "frozen" : "free")}.csv"), sb.ToString());
            var f = Features(r);
            for (var i = 0; i < FeatureNames.Length; i++)
            {
                Console.WriteLine($"  {FeatureNames[i]} = {f[i]:G5}");
            }
        }
    }

    [TestMethod]
    public void Doe_Lhs()
    {
        var n = int.TryParse(Environment.GetEnvironmentVariable("DRIVE_DOE_N"), out var nn) ? nn : 300;
        var pts = LatinHypercube(n, 20261120);
        var jobs = new List<(string, string, bool, int, double[])>();
        for (var i = 0; i < n; i++)
        {
            jobs.Add(($"lhs{i}f", "lhs", false, 1, pts[i]));
            jobs.Add(($"lhs{i}z", "lhs", true, 1, pts[i]));
        }

        Execute(Label("doe_lhs"), jobs, Threads);
    }

    /// <summary>腕の力だけを見る: 同じ LHS の点で、拳の目標を打ち出しの瞬間に狙いへ一度に置く（経路の時間の制限を外す。PathCode 1 = Instant、2 = InstantThrough）。</summary>
    [TestMethod]
    public void Doe_LhsInstant()
    {
        var n = int.TryParse(Environment.GetEnvironmentVariable("DRIVE_DOE_N"), out var nn) ? nn : 300;
        var pts = LatinHypercube(n, 20261120);
        var jobs = new List<(string, string, bool, int, double[])>();
        for (var i = 0; i < n; i++)
        {
            jobs.Add(($"lhs{i}If", "lhsInstant", false, 1, pts[i].Append(1.0).ToArray()));
            jobs.Add(($"lhs{i}Iz", "lhsInstant", true, 1, pts[i].Append(1.0).ToArray()));
        }

        Execute(Label("doe_lhs_instant"), jobs, Threads);
    }

    /// <summary>胴の直し方の完全格子（基準の点で、胴の回旋筋のトルク × 先行 × 可動域 × 腰の割合 × 間合い）: 決める変更の水準を実測で選ぶ。</summary>
    [TestMethod]
    public void Doe_TrunkGrid()
    {
        var jobs = new List<(string, string, bool, int, double[])>();
        foreach (var torque in new[] { 28.2, 60, 90, 120, 150, 200 })
        {
            foreach (var ff in new[] { 0.0, 1.0 })
            {
                foreach (var rom in new[] { 35.0, 45, 50, 60 })
                {
                    foreach (var hip in new[] { 0.45, 0.55, 0.7 })
                    {
                        foreach (var range in new[] { 1.05, 1.3 })
                        {
                            var v = Factors.Select(f => f.Base).ToArray();
                            v[23] = torque;
                            v[24] = ff;
                            v[12] = rom;
                            v[13] = hip;
                            v[18] = range;
                            jobs.Add(($"tg_t{torque}_ff{ff}_r{rom}_h{hip}_g{range}", "trunkGrid", false, 1, v));
                        }
                    }
                }
            }
        }

        Execute(Label("doe_trunk_grid"), jobs, Threads);
    }

    [TestMethod]
    public void Doe_OneAtATime()
    {
        var jobs = new List<(string, string, bool, int, double[])>();
        foreach (var (name, v) in OneAtATime(9))
        {
            jobs.Add(($"{name}f", "oat", false, 1, v));
            jobs.Add(($"{name}z", "oat", true, 1, v));
            jobs.Add(($"{name}If", "oatInstant", false, 1, v.Append(1.0).ToArray()));
            jobs.Add(($"{name}Iz", "oatInstant", true, 1, v.Append(1.0).ToArray()));
        }

        Execute(Label("doe_oat"), jobs, Threads);
    }

    /// <summary>測っていない条件（別の LHS・種 4〜6）。モデルの予測の当たりと、直し方の効きを確かめるのに使う。</summary>
    [TestMethod]
    public void Doe_Holdout()
    {
        var n = int.TryParse(Environment.GetEnvironmentVariable("DRIVE_DOE_N"), out var nn) ? nn : 60;
        var pts = LatinHypercube(n, 777001);
        var jobs = new List<(string, string, bool, int, double[])>();
        for (var i = 0; i < n; i++)
        {
            jobs.Add(($"ho{i}", "holdout", false, 4 + (i % 3), pts[i]));
        }

        Execute(Label("doe_holdout"), jobs, Threads);
    }

    /// <summary>
    /// 測っていない条件（別の LHS・種 4〜6）の対: 同じ点を、変更後の胴（回旋筋のトルク 70 N·m・先行 1）と変更前（28.2 N·m・先行 0）で回す。
    /// 縮約モデルの予測（胴: 有効慣性の補正 4）が当たっているか、直し方の効きが測っていない条件でも出るかを見る。腕と胴以外の要素は LHS の値のまま。
    /// </summary>
    [TestMethod]
    public void Doe_HoldoutPair()
    {
        var n = int.TryParse(Environment.GetEnvironmentVariable("DRIVE_DOE_N"), out var nn) ? nn : 100;
        var pts = LatinHypercube(n, 777001);
        var jobs = new List<(string, string, bool, int, double[])>();
        for (var i = 0; i < n; i++)
        {
            var after = (double[])pts[i].Clone();
            after[23] = 70;
            after[24] = 1;
            var before = (double[])pts[i].Clone();
            before[23] = 28.2;
            before[24] = 0;
            jobs.Add(($"ho{i}A", "holdoutAfter", false, 4 + (i % 3), after));
            jobs.Add(($"ho{i}B", "holdoutBefore", false, 4 + (i % 3), before));
        }

        Execute(Label("doe_holdout_pair"), jobs, Threads);
    }

    /// <summary>基準の体（既定の値）の目標の表: 種 1〜6、届く / 半歩遠い。</summary>
    [TestMethod]
    public void Targets_BaselineBody()
    {
        var jobs = new List<(string, string, bool, int, double[])>();
        foreach (var seed in new[] { 1, 2, 3, 4, 5, 6 })
        {
            var v = Factors.Select(f => f.Base).ToArray();
            jobs.Add(($"tgt_reach_s{seed}", "target", false, seed, v));
            var w = (double[])v.Clone();
            w[18] = 1.3;
            jobs.Add(($"tgt_far_s{seed}", "target", false, seed, w));
        }

        Execute(Label("targets"), jobs, Threads);
    }
}
