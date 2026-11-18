using System.Numerics;
using MiniRing.Motion.Stick;

namespace MiniRing.Tests;

/// <summary>棒人形でモーションを考える道具（docs/STICK_MOTION.md）が、ゲームの体と同じ物を表し、力学が保存則を守るか。</summary>
[TestClass]
public sealed class StickTests
{
    private static readonly Lazy<StickBody> Body = new(() => StickBody.Standard());

    public TestContext TestContext { get; set; } = null!;

    [TestMethod]
    public void TheGuardPose_PutsEveryPartWhereTheGameBodyStands()
    {
        // ゲームの体を 1.5 秒立たせた形を写し、順運動学で出した部位の中心が、物理の部位の中心と（骨盤を基準に）一致する。
        var tuning = FighterTuning.Default;
        using var game = new BoxingGame(tuning, seed: 1, corners: new BoxingCorners("stick", _ => new StandGuard(), _ => new StandGuard()));
        var fighter = game.Cpu;
        var body = StickBody.FromFighter(game, fighter, tuning);
        var frame = new StickFrame(body, body.Guard);
        var unyaw = Quaternion.CreateFromAxisAngle(Vector3.UnitY, -fighter.Frame.YawRadians);
        var p = fighter.Parts;
        var pairs = new (int Stick, int Game)[]
        {
            (StickBody.Ids.Chest, p.Chest), (StickBody.Ids.Head, p.Head), (StickBody.Ids.UpperArmL, p.RightUpperArm), (StickBody.Ids.ForearmL, p.RightForearm),
            (StickBody.Ids.UpperArmR, p.LeftUpperArm), (StickBody.Ids.ForearmR, p.LeftForearm), (StickBody.Ids.ThighL, p.RightThigh), (StickBody.Ids.ShinL, p.RightShin),
            (StickBody.Ids.ThighR, p.LeftThigh), (StickBody.Ids.ShinR, p.LeftShin),
        };
        var pelvisGame = game.World.GetState(p.Pelvis).Position;
        var pelvisStick = frame.Center(StickBody.Ids.Pelvis);
        var worst = 0f;
        foreach (var (stick, id) in pairs)
        {
            var expected = Vector3.Transform(game.World.GetState(id).Position - pelvisGame, unyaw);
            var actual = frame.Center(stick) - pelvisStick;
            var error = Vector3.Distance(expected, actual);
            TestContext.WriteLine($"{body.Segments[stick].Name}: {error * 1000f:0.0} mm");
            worst = MathF.Max(worst, error);
        }

        Assert.IsLessThan(0.01f, worst, "every part within 1 cm of the physics body");
        Assert.AreEqual(fighter.SupportedMass, body.TotalMass, 0.02f, "the same mass (toes counted with the feet)");
    }

    [TestMethod]
    public void StandingStill_TheGroundCarriesTheWeight_AndTheZmpIsUnderTheFeet()
    {
        var body = Body.Value;
        var frame = new StickFrame(body, body.Guard);
        var load = StickDynamics.Static(frame, (true, true));
        Assert.AreEqual(body.TotalMass * 9.81f, load.GroundForce.Y, 1e-3f, "the floor pushes up the whole weight");
        Assert.IsLessThan(0f, load.ZmpOutside, "the pressure centre is inside both feet");
        var com = frame.CenterOfMass;
        Assert.AreEqual(com.X, load.Zmp.X, 1e-4f, "standing still, the pressure centre is right under the centre of mass");
        Assert.AreEqual(com.Z, load.Zmp.Z, 1e-4f);
    }

    [TestMethod]
    public void APushOnTheChest_MovesThePressureCentre_AndLoadsTheLegs()
    {
        var body = Body.Value;
        var frame = new StickFrame(body, body.Guard);
        var quiet = StickDynamics.Static(frame, (true, true));
        var push = new StickForce(StickBody.Ids.Chest, body.Segments[StickBody.Ids.Chest].Center, new Vector3(0f, 0f, -10f));
        var pushed = StickDynamics.Static(frame, (true, true), [push]);
        Assert.IsLessThan(quiet.Zmp.Z, pushed.Zmp.Z, "pushed backward, the feet must press further back");
        Assert.AreEqual(10f, pushed.GroundForce.Z, 1e-3f, "the floor holds the push with friction");
        // 後ろへ押されると、後ろ足（構えの右足、-X・-Z の側）の膝が押し返し（1.1 → 2.8 N·m）、前足は軽くなる。
        Assert.IsGreaterThan(2f * quiet.Torques[StickBody.Ids.ShinR], pushed.Torques[StickBody.Ids.ShinR]);
        Assert.IsLessThan(quiet.Torques[StickBody.Ids.FootL], pushed.Torques[StickBody.Ids.FootL]);
    }

    [TestMethod]
    public void Ik_ReachesATargetForTheHand_WithTheElbowBentForward_AndTheFeetStayPlanted()
    {
        var body = Body.Value;
        var start = new StickFrame(body, body.Guard);
        var wrist = body.Segments[StickBody.Ids.ForearmL].Tip;
        var shoulder = start.Joints[StickBody.Ids.UpperArmL];
        var goal = shoulder + new Vector3(0.02f, 0f, 0.8f * body.ArmReach); // 前へまっすぐ突く所
        var ik = new StickIk(body);
        var targets = new List<StickTarget> { new(StickBody.Ids.ForearmL, wrist, goal) };
        targets.AddRange(StickIk.Plant(start, Hand.Left));
        targets.AddRange(StickIk.Plant(start, Hand.Right));
        var watch = System.Diagnostics.Stopwatch.StartNew();
        var pose = ik.Solve(body.Guard, targets, (true, true));
        TestContext.WriteLine($"solve {watch.Elapsed.TotalMilliseconds:0.0} ms, cost {ik.LastCost:0.00000}");
        var frame = new StickFrame(body, pose);
        Assert.IsLessThan(0.005f, Vector3.Distance(frame.Point(StickBody.Ids.ForearmL, wrist), goal), "the fist reaches within 5 mm");
        var elbow = pose.Values[body.Offsets[StickBody.Ids.ForearmL]];
        Assert.IsGreaterThanOrEqualTo(-10f * MathF.PI / 180f, elbow, "the elbow never bends backward past its limit");
        foreach (var leg in new[] { Hand.Left, Hand.Right })
        {
            Assert.IsLessThan(0.005f, frame.Sole(leg).Min(p => p.Y) - start.Sole(leg).Min(p => p.Y) + 0.0049f, "the planted feet stay on the floor");
        }
    }

    [TestMethod]
    public void ASquat_CostsAboutTheWorkOfLiftingTheBodyBackUp()
    {
        // ゆっくりしゃがんで立つ: 立ち上がりで筋がする正の仕事は、持ち上げた重心の位置エネルギー m g Δh とほぼ同じ（ゆっくりなので運動エネルギーは小さい）。
        var body = Body.Value;
        var guard = new StickFrame(body, body.Guard);
        var pelvis = body.Segments[StickBody.Ids.Pelvis].Center;
        var start = guard.Center(StickBody.Ids.Pelvis);
        var head = guard.Tip(StickBody.Ids.Head);
        const float Depth = 0.03f;
        StickStep Step(float t, StickFrame now)
        {
            var down = Depth * 0.5f * (1f - MathF.Cos(MathF.PI * t)); // 0 → 2 s で下がって戻る
            return new StickStep(
            [
                new StickTarget(StickBody.Ids.Pelvis, pelvis, start - new Vector3(0f, down, 0f)),
                new StickTarget(StickBody.Ids.Head, body.Segments[StickBody.Ids.Head].Tip, head - new Vector3(0f, down, 0f)),
            ], (true, true));
        }

        var result = StickMotion.Run(body, Step, 2f);
        var lowest = result.Frames.Min(f => f.CenterOfMass.Y);
        var lift = body.TotalMass * 9.81f * (guard.CenterOfMass.Y - lowest);
        TestContext.WriteLine($"work {result.Work:0.000} J, m g dh {lift:0.000} J, {result.Summary()}");
        Assert.IsLessThan(0f, result.MaxZmpOutside, "a slow squat never tips over");
        Assert.AreEqual(lift, result.Work, 0.5f * lift, "the muscles do about the work of lifting the body back up");
    }

    [TestMethod]
    public void Smoothness_AMinimumJerkReach_ScoresAboutMinusSix_AndAShakyOneMuchLower()
    {
        // 躍度最小の 1 本の動き（5 次の多項式）の LDLJ は、この差分の計算で約 −4.9（連続の式の −6 より端の分だけ高い）。12 Hz の揺れを足すと大きく下がる。
        const float Dt = 1f / 60f;
        var smooth = new List<Vector3>();
        var shaky = new List<Vector3>();
        for (var i = 0; i <= 30; i++)
        {
            var u = i / 30f;
            var x = 0.1f * ((10f * u * u * u) - (15f * u * u * u * u) + (6f * u * u * u * u * u));
            smooth.Add(new Vector3(x, 0f, 0f));
            shaky.Add(new Vector3(x, 0.003f * MathF.Sin(2f * MathF.PI * 12f * i * Dt), 0f));
        }

        var a = StickReview.Ldlj(smooth, Dt)!.Value;
        var b = StickReview.Ldlj(shaky, Dt)!.Value;
        TestContext.WriteLine($"minimum jerk {a:0.00}, shaky {b:0.00}");
        Assert.AreEqual(-4.9f, a, 0.4f);
        Assert.IsLessThan(a - 2f, b);
    }

    [TestMethod]
    public void Review_ReachingFarByLeaning_WarnsOfFalling_AndOutOfReach_WarnsItCanNotReach()
    {
        // 腕の 1.6 倍先へ手首を: 体ごと前へ傾けて届くが、捕捉点が足の外へ出る（崩れる）警告。3 倍先は届かない警告。
        var body = Body.Value;
        var guard = new StickFrame(body, body.Guard);
        var wrist = body.Segments[StickBody.Ids.ForearmL].Tip;
        StickReview Reach(float times)
        {
            var far = guard.Joints[StickBody.Ids.UpperArmL] + new Vector3(0f, 0f, times * body.ArmReach);
            var review = StickReview.Of(StickMotion.Run(body, (t, now) => new StickStep([new StickTarget(StickBody.Ids.ForearmL, wrist, far)], (true, true)), 0.6f));
            TestContext.WriteLine($"{times}x: {review.Report()}");
            return review;
        }

        Assert.IsTrue(Reach(1.6f).Alerts.Any(a => a.Kind.Contains("捕捉点", StringComparison.Ordinal) && a.Grade == StickGrade.Warning));
        Assert.IsTrue(Reach(3f).Alerts.Any(a => a.Kind.Contains("目標に届かない", StringComparison.Ordinal) && a.Grade == StickGrade.Warning));
    }

    [TestMethod]
    public void Review_OfQuietStanding_HasNoWarnings()
    {
        var body = Body.Value;
        var result = StickMotion.Run(body, (t, now) => new StickStep([], (true, true)), 1f);
        var review = StickReview.Of(result);
        TestContext.WriteLine(review.Report());
        Assert.AreEqual(0, review.Warnings, "standing still in the game's own guard raises no warning");
    }

    [TestMethod]
    public void AJointCanNotLeaveItsRange()
    {
        var body = Body.Value;
        var pose = body.Guard.Copy();
        var knee = body.Offsets[StickBody.Ids.ShinL];
        pose.Values[knee] = -2f; // 膝を逆へ 115°
        var shoulder = body.Offsets[StickBody.Ids.UpperArmL];
        (pose.Values[shoulder], pose.Values[shoulder + 1], pose.Values[shoulder + 2]) = (0f, 0f, 3f); // 振れ 172°
        pose.Project(body);
        Assert.AreEqual(-10f * MathF.PI / 180f, pose.Values[knee], 1e-5f);
        var q = StickMath.Exp(new Vector3(pose.Values[shoulder], pose.Values[shoulder + 1], pose.Values[shoulder + 2]));
        Assert.IsLessThanOrEqualTo(body.Segments[StickBody.Ids.UpperArmL].SwingLimit + 1e-3f, StickMath.Angle(StickMath.SwingTwist(q, body.Segments[StickBody.Ids.UpperArmL].Axis).Swing));
    }
}
