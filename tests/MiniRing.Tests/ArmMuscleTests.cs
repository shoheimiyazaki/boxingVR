using System.Numerics;
using Aigis.Physics;

namespace MiniRing.Tests;

/// <summary>
/// 拳を引く筋肉（<see cref="ArmMuscle"/>、Hill 型）: 指令 → 活性化の遅れ → 力 = 活性化 × 最大の力 × 長さの関係 × 速さの関係。
/// </summary>
[TestClass]
public sealed class ArmMuscleTests
{
    private const float Dt = 1f / 240f;
    private static readonly FighterTuning Tuning = FighterTuning.Default;

    [TestMethod]
    public void Activation_RisesFasterThanItFalls_AndLagsTheCommand()
    {
        // 全力の指令を出して立ち上がりの時定数だけ経つと 1 − 1/e、指令を 0 にして緩みの時定数だけ経つと 1/e。
        var rise = Run(0f, 1f, Tuning.MuscleActivationTime);
        var fall = Run(1f, 0f, Tuning.MuscleDeactivationTime);
        Assert.AreEqual(1f - (1f / MathF.E), rise, 0.01f, "rises with the activation time constant");
        Assert.AreEqual(1f / MathF.E, fall, 0.01f, "falls with the deactivation time constant");
        Assert.IsLessThan(Tuning.MuscleDeactivationTime, Tuning.MuscleActivationTime, "a muscle switches on faster than it lets go");

        // 全力で出している主動筋は、指令を切って 1 歩（4 ms）ではほとんど抜けない（すぐには止まれない）。
        var oneStep = ArmMuscle.Activate(1f, 0f, Dt, Tuning.MuscleActivationTime, Tuning.MuscleDeactivationTime);
        Assert.IsGreaterThan(0.85f, oneStep, "the agonist is still almost fully on one step after the command stops");

        static float Run(float from, float to, float seconds)
        {
            var a = from;
            var steps = 1000;
            for (var i = 0; i < steps; i++)
            {
                a = ArmMuscle.Activate(a, to, seconds / steps, Tuning.MuscleActivationTime, Tuning.MuscleDeactivationTime);
            }

            return a;
        }
    }

    [TestMethod]
    public void ForceVelocity_IsWeakerTheFasterItShortens_AndStrongerWhenStretched()
    {
        float Fv(float u) => ArmMuscle.ForceVelocity(u, Tuning.HandMuscleCurvature, Tuning.HandMuscleEccentricMax, Tuning.HandMuscleEccentricSlope);
        Assert.AreEqual(1f, Fv(0f), 1e-5f, "isometric");
        Assert.AreEqual(0f, Fv(1f), 1e-5f, "no force at the maximum shortening speed");
        // Hill の双曲線: v/vmax = 0.25（a/F0 と同じ）で (0.75)/(2) = 0.375
        Assert.AreEqual(0.375f, Fv(0.25f), 1e-4f, "Hill's hyperbola with a/F0 = 0.25");
        var previous = 2f;
        for (var u = -1f; u <= 1f; u += 0.05f)
        {
            var f = Fv(u);
            Assert.IsLessThan(previous + 1e-6f, f, $"monotone in the shortening speed (u = {u:0.00})");
            previous = f;
        }

        Assert.IsGreaterThan(1.3f, Fv(-0.5f), "a stretched muscle is stronger than isometric (eccentric)");
        Assert.IsLessThan(Tuning.HandMuscleEccentricMax + 1e-4f, Fv(-100f), "but no stronger than the eccentric maximum");
        Assert.AreEqual(1f, Fv(-1e-4f), 0.01f, "continuous at zero");
    }

    [TestMethod]
    public void FarTarget_SaturatesTheCommand_NearTarget_GivesAWeakOne()
    {
        var chest = Quaternion.Identity;
        var far = ArmMuscle.Excitation(new Vector3(0f, 0f, 500f), 50f, chest);
        var near = ArmMuscle.Excitation(new Vector3(0f, 0f, 10f), 50f, chest);
        Assert.AreEqual(1f, far.Length(), 1e-5f, "a target out of reach is a full command");
        Assert.AreEqual(0.2f, near.Length(), 1e-5f, "a near target is a weak command");
        Assert.AreEqual(1f, ArmMuscle.GroupExcitation(far, 4), 1e-5f, "all of it goes to the group pulling forward (+Z)");
        Assert.AreEqual(0f, ArmMuscle.GroupExcitation(far, 5), 1e-5f, "none to its antagonist");
    }

    [TestMethod]
    public void TheAgonist_DoesNotLetGo_JustShortOfFullExtension()
    {
        // 外へ押す筋は、伸びきりの手前（腕の長さの 9 割）でも等尺の 7 割を出す（伸びきりで 5 割）。
        var near = ArmMuscle.ForceLength(0.9f, outward: true, Tuning.ArmMuscleOptimalExtension, Tuning.ArmMuscleEndForce, Tuning.ArmMuscleFlexedEnd);
        var full = ArmMuscle.ForceLength(1f, outward: true, Tuning.ArmMuscleOptimalExtension, Tuning.ArmMuscleEndForce, Tuning.ArmMuscleFlexedEnd);
        Assert.IsGreaterThan(0.7f - 1e-4f, near, "just short of full extension the pushing muscle keeps most of its force");
        Assert.AreEqual(Tuning.ArmMuscleEndForce, full, 1e-4f);
    }

    [TestMethod]
    public void PunchAtATargetOutOfReach_IsAFullCommand_AndTheFistReachesFullExtension()
    {
        // 構えから、肩の前の腕の長さの 2 倍の所（届かない）へ目標を置く: 前へ引くまとまりの活性化は 1 近くまで上がり、拳は伸びきりまで出る。
        using var world = new PhysicsWorld();
        world.AddStaticBox(new Vector3(0f, -0.5f, 0f), new Vector3(5f, 0.5f, 5f));
        var f = Fighter.Spawn(world, FighterTuning.Default, "f", Vector3.Zero, 0f);
        var forward = f.Frame.Forward;
        var left = AiControl.Guard(Hand.Left, f.ArmReach);
        var guard = AiControl.Guard(Hand.Right, f.ArmReach);
        var far = guard + new Vector3(0f, 0f, 2f * f.ArmReach);
        FighterCommand Command(Vector3 right) => new(Vector3.Zero, forward, new HandCommand(left, false), new HandCommand(right, false, 1f), Vector3.Zero);
        Step(world, f, Command(guard), 1.5f);
        var peakActivation = 0f;
        var reach = 0f;
        for (var t = 0f; t < 0.15f; t += Dt)
        {
            f.ApplyControl(Command(far), Dt);
            world.Step(Dt);
            var a = f.HandActivation(Hand.Right);
            for (var i = 0; i < ArmMuscle.Groups; i++)
            {
                peakActivation = MathF.Max(peakActivation, a[i]);
            }

            reach = MathF.Max(reach, Vector3.Distance(f.GloveCenter(Hand.Right), world.GetState(f.Parts.Chest).Position));
        }

        Console.WriteLine($"peak activation {peakActivation:0.00}, chest-to-fist {reach:0.000} m (arm {f.ArmReach:0.000} m)");
        Assert.IsGreaterThan(0.95f, peakActivation, "a target out of reach drives the muscle fully");
        Assert.IsGreaterThan(0.9f * f.ArmReach, reach, "and the fist goes out to full extension");
    }

    private static void Step(PhysicsWorld world, Fighter f, FighterCommand command, float seconds)
    {
        for (var t = 0f; t < seconds; t += Dt)
        {
            f.ApplyControl(command, Dt);
            world.Step(Dt);
        }
    }
}
