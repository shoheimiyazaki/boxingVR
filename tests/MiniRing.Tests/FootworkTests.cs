using System.Numerics;
using Aigis.Physics;

namespace MiniRing.Tests;

/// <summary>足の運び: 脚の IK、足の置き場所の計画（倒立振子 + 構え）、かかととつま先の足で立つ体。</summary>
[TestClass]
public sealed class FootworkTests
{
    private const float Dt = 1f / 240f;

    [TestMethod]
    public void LegIk_PutsTheAnkleOnTheTarget_WithTheKneeTowardTheToes()
    {
        var r0 = new Vector3(0.032f, -0.112f, 0f);
        const float shin = 0.105f;
        foreach (var target in new[] { new Vector3(0.03f, -0.2f, 0.05f), new Vector3(-0.02f, -0.17f, -0.06f), new Vector3(0.06f, -0.15f, 0.02f) })
        {
            var forward = Vector3.Normalize(new Vector3(0.2f, 0f, 1f));
            var (thigh, knee, ratio) = LegIk.Solve(target, r0, shin, forward);

            var ankle = Vector3.Transform(r0 + LegIk.ShinVector(shin, knee), thigh);
            var kneeAt = Vector3.Transform(r0, thigh);
            Assert.IsLessThan(1e-4f, Vector3.Distance(ankle, target), $"ankle {ankle} target {target}");
            Assert.IsLessThan(1f, ratio);
            // 膝は股と足首を結ぶ線より前（つま先の向き）に出る。
            var line = Vector3.Normalize(target);
            var offLine = kneeAt - (Vector3.Dot(kneeAt, line) * line);
            Assert.IsGreaterThan(0f, Vector3.Dot(offLine, forward), $"knee {kneeAt}");
        }
    }

    [TestMethod]
    public void Footwork_StepsWithTheFootOnTheSideItGoes_AndNeverCrossesTheFeet()
    {
        Assert.AreEqual(Footwork.Lead, FirstFoot(new Vector2(0f, 1f)), "forward: the lead foot first");
        Assert.AreEqual(Footwork.Rear, FirstFoot(new Vector2(0f, -1f)), "back: the rear foot first");
        Assert.AreEqual(Footwork.Lead, FirstFoot(new Vector2(1f, 0f)), "to the left (+X): the left (lead) foot first");
        Assert.AreEqual(Footwork.Rear, FirstFoot(new Vector2(-1f, 0f)), "to the right: the right (rear) foot first");

        // 右へ回り込み続けても、左足が右足より前・左にある。
        var footwork = Standing(out var input);
        for (var i = 0; i < 2400; i++)
        {
            var yaw = 0.002f * i;
            var body = Vector2.Lerp(footwork.Pose(Footwork.Lead).Ball.Xz(), footwork.Pose(Footwork.Rear).Ball.Xz(), 0.5f);
            footwork.Update(input with { FacingYaw = yaw, Body = body, Intent = Footwork.Rotate(new Vector2(-0.4f, 0.1f), yaw) }, Dt);
            var lead = Footwork.Unrotate(footwork.Pose(Footwork.Lead).Ball.Xz() - footwork.Pose(Footwork.Rear).Ball.Xz(), yaw);
            Assert.IsGreaterThan(0f, lead.X, $"step {i}: lead foot on the left {lead}");
            Assert.IsGreaterThan(0f, lead.Y, $"step {i}: lead foot in front {lead}");
        }
    }

    [TestMethod]
    public void Footwork_CatchesAPushedBody_WithAStepTowardTheFall()
    {
        var footwork = Standing(out var input);
        var steps = footwork.Steps;
        footwork.Update(input with { BodyVelocity = new Vector2(0f, -0.6f) }, Dt); // 後ろへ押された
        Assert.AreEqual(steps + 1, footwork.Steps);
        Assert.AreEqual(Footwork.Rear, footwork.Swinging, "the rear foot steps back to catch the body");
    }

    [TestMethod]
    public void Fighter_StandsInAnOrthodoxStance_LeftFootForward_HeelsUp()
    {
        using var world = NewWorld();
        var f = Fighter.Spawn(world, FighterTuning.Default, "f", Vector3.Zero, 0f);
        Run(world, f, Idle(f), 2f);

        var lead = world.GetState(f.HeelId(Footwork.Lead)).Position;
        var rear = world.GetState(f.HeelId(Footwork.Rear)).Position;
        var leg = f.ArmReach;
        Console.WriteLine($"lead {lead} rear {rear} pelvis {f.PelvisPosition} head {f.HeadPosition}");
        Assert.AreEqual(FighterState.Standing, f.State);
        Assert.IsGreaterThan(0.2f * leg, lead.Z - rear.Z, "the left foot is in front");
        Assert.IsGreaterThan(0.1f * leg, lead.X - rear.X, "and to the left");
        foreach (var foot in new[] { Footwork.Lead, Footwork.Rear })
        {
            var heel = world.GetState(f.HeelId(foot));
            var toe = world.GetState(f.ToeId(foot));
            var pitch = MathF.Asin(-Vector3.Transform(Vector3.UnitZ, heel.Orientation).Y);
            Console.WriteLine($"foot {foot}: heel pitch {pitch * 180f / MathF.PI:0.0} deg, toe y {toe.Position.Y:0.0000}, heel y {heel.Position.Y:0.0000}");
            Assert.IsGreaterThan(4f * MathF.PI / 180f, pitch, $"foot {foot}: the heel is raised (toes down)");
            Assert.IsLessThan(0.017f, toe.Position.Y, $"foot {foot}: the toes stay on the floor (the toe box is 0.011 m half-thick)");
        }
    }

    [TestMethod]
    public void Fighter_WalksForward_FeetFirst()
    {
        using var world = NewWorld();
        var f = Fighter.Spawn(world, FighterTuning.Default, "f", Vector3.Zero, 0f);
        Run(world, f, Idle(f), 1.5f);
        var start = f.PelvisPosition;
        var leadStart = world.GetState(f.HeelId(Footwork.Lead)).Position;
        var steps = f.Footwork.Steps;
        var forward = Idle(f) with { MoveWorld = Vector3.UnitZ };
        var firstSwing = -1;
        var leadMovedAt = -1f;
        var bodyMovedAt = -1f;
        for (var i = 0; i < (int)(1.5f / Dt); i++)
        {
            f.ApplyControl(forward, Dt);
            world.Step(Dt);
            if (firstSwing < 0 && f.Footwork.Swinging >= 0)
            {
                firstSwing = f.Footwork.Swinging;
            }

            if (leadMovedAt < 0f && world.GetState(f.HeelId(Footwork.Lead)).Position.Z - leadStart.Z > 0.01f)
            {
                leadMovedAt = i * Dt;
            }

            if (bodyMovedAt < 0f && f.PelvisPosition.Z - start.Z > 0.01f)
            {
                bodyMovedAt = i * Dt;
            }
        }

        var moved = f.PelvisPosition.Z - start.Z;
        Console.WriteLine($"moved {moved:0.000} m in 1.5 s, steps {f.Footwork.Steps - steps}, first {firstSwing}, lead moved at {leadMovedAt:0.000}s body at {bodyMovedAt:0.000}s");
        Assert.AreEqual(FighterState.Standing, f.State);
        Assert.IsGreaterThan(0.3f, moved, "walks forward");
        Assert.AreEqual(Footwork.Lead, firstSwing, "the lead foot steps first");
        Assert.IsLessThanOrEqualTo(bodyMovedAt, leadMovedAt, "the foot moves before the body");
    }

    private static int FirstFoot(Vector2 intent)
    {
        var footwork = Standing(out var input);
        footwork.Update(input with { Intent = intent * 0.5f }, Dt);
        return footwork.Swinging;
    }

    /// <summary>構えで立った足の運び（原点で +Z を向き、脚の長さ 0.22m）。</summary>
    private static Footwork Standing(out FootworkInput input)
    {
        var footwork = new Footwork(new FootworkTuning());
        const float leg = 0.22f;
        var t = new FootworkTuning();
        var x = 0.5f * t.StanceWidth * leg;
        var z = 0.5f * t.StanceLength * leg;
        footwork.Reset(new Vector2(x, z), t.LeadYawDegrees * MathF.PI / 180f, new Vector2(-x, -z), t.RearYawDegrees * MathF.PI / 180f);
        input = new FootworkInput(0f, Vector2.Zero, Vector2.Zero, 6.4f, Vector2.Zero, 0f, 0f, leg);
        footwork.Update(input, Dt);
        Assert.AreEqual(-1, footwork.Swinging, "standing in the stance needs no step");
        return footwork;
    }

    private static PhysicsWorld NewWorld()
    {
        var world = new PhysicsWorld();
        world.AddStaticBox(new Vector3(0f, -0.5f, 0f), new Vector3(5f, 0.5f, 5f));
        return world;
    }

    private static FighterCommand Idle(Fighter f) => new(Vector3.Zero, f.Frame.Forward,
        new HandCommand(AiControl.Guard(Hand.Left, f.ArmReach), false),
        new HandCommand(AiControl.Guard(Hand.Right, f.ArmReach), false));

    private static void Run(PhysicsWorld world, Fighter fighter, FighterCommand command, float seconds)
    {
        for (var i = 0; i < (int)(seconds / Dt); i++)
        {
            fighter.ApplyControl(command, Dt);
            world.Step(Dt);
        }
    }
}

internal static class FootworkTestExtensions
{
    public static Vector2 Xz(this Vector3 v) => new(v.X, v.Z);
}

/// <summary>
/// 足の運びの調査用: 1 人で立って 1.5 秒、そのあと前へ歩き続ける。計画した足の位置と実際の足の位置、骨盤の高さ・傾き、歩いた距離を出す
/// （--logger "console;verbosity=detailed"）。footwork が偽なら以前の動き（体を引いて脚は正弦の歩行）。
/// </summary>
[TestClass]
public sealed class FootworkProbe
{
    [TestMethod]
    [DataRow(true, false)]
    [DataRow(false, false)]
    [DataRow(true, true)]
    public void Trace(bool footwork, bool verbose)
    {
        var tuning = footwork ? FighterTuning.Default : FighterTuning.Default with { Footwork = new FootworkTuning { Enabled = false } };
        using var world = new PhysicsWorld();
        world.AddStaticBox(new Vector3(0f, -0.5f, 0f), new Vector3(5f, 0.5f, 5f));
        var f = Fighter.Spawn(world, tuning, "f", Vector3.Zero, 0f);
        var idle = new FighterCommand(Vector3.Zero, f.Frame.Forward, new HandCommand(AiControl.Guard(Hand.Left, f.ArmReach), false), new HandCommand(AiControl.Guard(Hand.Right, f.ArmReach), false));
        Vector3 Ball(int foot)
        {
            var s = world.GetState(f.HeelId(foot));
            return s.Position + Vector3.Transform(FootGeometry.BallOnHeel(f.HalfExtents.Foot), s.Orientation);
        }

        float Error() => Enumerable.Range(0, 2).Max(k => new Vector2(Ball(k).X - f.Footwork.Pose(k).Ball.X, Ball(k).Z - f.Footwork.Pose(k).Ball.Z).Length());
        var z0 = 0f;
        for (var i = 0; i < 240 * 4; i++)
        {
            f.ApplyControl(i < 360 ? idle : idle with { MoveWorld = Vector3.UnitZ }, 1f / 240f);
            world.Step(1f / 240f);
            if (i == 359)
            {
                Console.WriteLine($"footwork {footwork}: standing, plan-to-feet error {Error():0.000} m, hover {f.HoverHeight:0.000} pelvis {f.PelvisPosition.Y:0.000}, upright chest {f.Tilt.Chest:0.00} pelvis {f.Tilt.Pelvis:0.00}, reach {f.LegReach.Lead:0.00}/{f.LegReach.Rear:0.00}, stand {f.StandHeight:0.000}, foot error {f.FootError.Lead} {f.FootError.Rear}, ankle lead {f.Ankle(0).Target * 57.3f:0.0}/{f.Ankle(0).Actual * 57.3f:0.0} rear {f.Ankle(1).Target * 57.3f:0.0}/{f.Ankle(1).Actual * 57.3f:0.0} deg");
                z0 = f.PelvisPosition.Z;
            }

            if (verbose && i % 6 == 0 && i > 340 && i < 640)
            {
                var fw = f.Footwork;
                Console.WriteLine($"t {i / 240f:0.00} up {f.Uprightness:0.00} {f.State} pelvis ({f.PelvisPosition.X:0.000},{f.PelvisPosition.Y:0.000},{f.PelvisPosition.Z:0.000}) swing {fw.Swinging} " +
                    $"plan L ({fw.Pose(0).Ball.X:0.000},{fw.Pose(0).Ball.Z:0.000}) R ({fw.Pose(1).Ball.X:0.000},{fw.Pose(1).Ball.Z:0.000}) actual L ({Ball(0).X:0.000},{Ball(0).Y:0.000},{Ball(0).Z:0.000}) R ({Ball(1).X:0.000},{Ball(1).Y:0.000},{Ball(1).Z:0.000})");
            }
        }

        Console.WriteLine($"footwork {footwork}: walked {f.PelvisPosition.Z - z0:0.000} m in 2.5 s, steps {f.Footwork.Steps}, end error {Error():0.000}, state {f.State}");
    }
}
