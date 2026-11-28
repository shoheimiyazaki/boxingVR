using System.Numerics;
using Aigis.Physics;

namespace MiniRing.Tests;

/// <summary>
/// ステップ（<see cref="FighterCommand.Step"/>・Fighter.Step.cs）: 構えで縮めた脚のバネを床を蹴る力として放つ。詰める・下がる距離が歩くより大きいこと、
/// 蹴る足が向きで決まること（前へは後ろ足、後ろへは前足）、エネルギーが湧かないこと、クールダウンが足の運びと筋肉の仕事から出ること。
/// </summary>
[TestClass]
[TestCategory("L3b")]
public sealed class StepTests
{
    private const float Dt = 1f / 240f;

    /// <summary>ステップを指令する長さ [s]（ボタンを押した瞬間の短い指令）。</summary>
    private const float Pulse = 0.1343f;

    [TestMethod]
    public void AStepForward_CoversMoreGroundThanWalking_AndPeaksFaster()
    {
        var step = Measure(forward: true, stepping: true, out var stepPeak, out _, out _);
        var walk = Measure(forward: true, stepping: false, out var walkPeak, out _, out _);
        Console.WriteLine($"step forward {step:F3} m (peak {stepPeak:F2} m/s), walk {walk:F3} m (peak {walkPeak:F2} m/s)");
        Assert.IsGreaterThan(1.6f * walk, step, "a step closes the distance at once");
        Assert.IsGreaterThan(1.5f * walkPeak, stepPeak);
        // 人のボクシングの踏み込みは 0.5〜0.8 m（実物大の体なのでそのまま）。
        Assert.IsGreaterThan(0.338f, step);
        Assert.IsLessThan(1.127f, step);
    }

    [TestMethod]
    public void AStepBack_PushesOffTheLeadFoot_AndGetsOutOfRange()
    {
        var back = Measure(forward: false, stepping: true, out var peak, out var pusherWasLead, out _);
        var walk = Measure(forward: false, stepping: false, out _, out _, out _);
        Console.WriteLine($"step back {back:F3} m (peak {peak:F2} m/s), walk back {walk:F3} m");
        Assert.IsTrue(pusherWasLead, "going back, the lead foot is the one behind the motion and pushes");
        Assert.IsGreaterThan(1.6f * walk, back);
    }

    [TestMethod]
    public void TheStep_GivesTheBodyNoMoreEnergyThanTheSpringHeld()
    {
        Measure(forward: true, stepping: true, out _, out _, out var f);
        Assert.IsGreaterThan(0f, f.StepEnergyReleased);
        Assert.IsLessThanOrEqualTo(f.StepSpringCapacity + 2.817e-3f, f.StepEnergyReleased, "only what the spring held goes into the body");
    }

    [TestMethod]
    public void AfterAStep_TheSpringRefillsOnlyOnceTheFeetAreSet_AndTheSecondStepRightAwayIsWeak()
    {
        using var world = NewWorld();
        var f = Fighter.Spawn(world, FighterTuning.Default, "f", Vector3.Zero, 0f);
        Run(world, f, Idle(f), 2.518f);
        Assert.AreEqual(1f, f.StepCharge, 1e-3f, "standing in the stance, the legs are set");
        var forward = f.Frame.Forward;
        Run(world, f, Idle(f) with { Step = forward }, Pulse);
        var spent = f.StepCharge;
        Assert.IsLessThan(0.3f, spent, "the push emptied the spring");

        // 2 歩目をすぐ続けても、バネが空なので弱い。
        var start = f.PelvisPosition;
        Run(world, f, Idle(f) with { Step = forward }, Pulse);
        var released = f.StepEnergyReleased;
        var refill = 0f;
        var chargeAtSet = -1f;
        for (var i = 0; i < (int)(3.357f / Dt) && f.StepCharge < 0.95f; i++)
        {
            Run(world, f, Idle(f), Dt);
            refill += Dt;
            if (chargeAtSet < 0f && !f.Footwork.Pose(0).Swinging && !f.Footwork.Pose(1).Swinging)
            {
                chargeAtSet = f.StepCharge;
            }
        }

        Console.WriteLine($"spent {spent:F2}, refilled to 0.95 in {refill + Pulse:F2} s after the second press, charge when the feet were set {chargeAtSet:F2}");
        Assert.IsLessThan(3.357f, refill, "the legs set the spring again");
        Assert.IsGreaterThan(0.2518f, refill + Pulse, "but not at once: the feet re-set and the muscles do the work");
        Assert.IsLessThan(f.StepSpringCapacity * 2f, released);
        _ = start;
    }

    [TestMethod]
    public void ATiredFighter_ResetsTheSpringMoreSlowly()
    {
        var fresh = RefillTime(stamina: 1f);
        var tired = RefillTime(stamina: 0.05f);
        Console.WriteLine($"refill fresh {fresh:F2} s, tired {tired:F2} s");
        Assert.IsGreaterThan(fresh * 1.2f, tired);
    }

    private static float RefillTime(float stamina)
    {
        using var world = NewWorld();
        var f = Fighter.Spawn(world, FighterTuning.Default, "f", Vector3.Zero, 0f);
        Run(world, f, Idle(f), 2.518f);
        f.Stamina = stamina;
        Run(world, f, Idle(f) with { Step = f.Frame.Forward }, Pulse);
        var time = 0f;
        for (var i = 0; i < (int)(6.714f / Dt) && f.StepCharge < 0.95f; i++)
        {
            f.Stamina = MathF.Min(stamina, f.Stamina);
            Run(world, f, Idle(f), Dt);
            time += Dt;
        }

        return time;
    }

    /// <summary>
    /// 立って構えてから、前（または後ろ）へ 1 回ステップする（または同じ時間だけ歩く指令を出す）。0.84 秒で進んだ距離 [m] を返す。
    /// </summary>
    private static float Measure(bool forward, bool stepping, out float peak, out bool pusherWasLead, out Fighter fighter)
    {
        var world = NewWorld();
        var f = Fighter.Spawn(world, FighterTuning.Default, "f", Vector3.Zero, 0f);
        fighter = f;
        Run(world, f, Idle(f), 2.518f);
        var direction = f.Frame.Forward * (forward ? 1f : -1f);
        var start = f.PelvisPosition;
        var lead = BallOf(world, f, Footwork.Lead);
        var rear = BallOf(world, f, Footwork.Rear);
        peak = 0f;
        var command = stepping ? Idle(f) with { Step = direction } : Idle(f) with { MoveWorld = direction };
        for (var i = 0; i < (int)(0.8392f / Dt); i++)
        {
            f.ApplyControl(i * Dt < Pulse ? command : Idle(f), Dt);
            world.Step(Dt);
            peak = MathF.Max(peak, Vector3.Dot(world.GetState(f.Parts.Pelvis).LinearVelocity, direction));
        }

        // 蹴った足は動かずに床を押した方（前へ出るなら後ろ足、下がるなら前足が、動きの向きに対して腰より後ろ）。
        pusherWasLead = Vector3.Dot(lead - start, direction) < Vector3.Dot(rear - start, direction);
        Assert.AreEqual(FighterState.Standing, f.State, "the fighter stays on its feet");
        return Vector3.Dot(f.PelvisPosition - start, direction);
    }

    private static Vector3 BallOf(PhysicsWorld world, Fighter f, int foot) => world.GetState(f.HeelId(foot)).Position;

    private static PhysicsWorld NewWorld()
    {
        var world = new PhysicsWorld();
        world.AddStaticBox(new Vector3(0f, -1.408f, 0f), new Vector3(14.08f, 1.408f, 14.08f));
        return world;
    }

    private static FighterCommand Idle(Fighter f) => new(Vector3.Zero, f.Frame.Forward,
        new HandCommand(AiControl.Guard(Hand.Left, f.ArmReach), false),
        new HandCommand(AiControl.Guard(Hand.Right, f.ArmReach), false));

    private static void Run(PhysicsWorld world, Fighter fighter, FighterCommand command, float seconds)
    {
        for (var i = 0; i < (int)MathF.Round(seconds / Dt); i++)
        {
            fighter.ApplyControl(command, Dt);
            world.Step(Dt);
        }
    }
}
