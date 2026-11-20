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
        var far = ArmMuscle.Excitation(new Vector3(0f, 0f, 5000f), 500f, chest);
        var near = ArmMuscle.Excitation(new Vector3(0f, 0f, 100f), 500f, chest);
        Assert.AreEqual(1f, far.Length(), 1e-5f, "a target out of reach is a full command");
        Assert.AreEqual(0.2f, near.Length(), 1e-5f, "a near target is a weak command");
        Assert.AreEqual(1f, ArmMuscle.GroupExcitation(far, 4), 1e-5f, "all of it goes to the group pulling forward (+Z)");
        Assert.AreEqual(0f, ArmMuscle.GroupExcitation(far, 5), 1e-5f, "none to its antagonist");
    }

    [TestMethod]
    public void TheAgonist_DoesNotLetGo_JustShortOfFullExtension_AndABentArmPushesWeakly()
    {
        // 関節ごとの力の出しやすさ: 肘の伸ばすトルクは伸びきりの近くで落ちるが、そこでは同じトルクが拳を肩から押し出す力に変わるてこが
        // 大きい（ヤコビアン）。伸びきりの手前（肘 20°）でも拳を押し出す力は構え（肘 90°）と同じ（構えで頭打ち）で、曲げきり（130°）からは弱い。
        const float upper = 0.307f;
        const float lower = 0.2817f;
        var c = ArmMuscle.Constants.From(Tuning, 0f);
        float Push(float bendDegrees)
        {
            var bend = bendDegrees * MathF.PI / 180f;
            var r = MathF.Sqrt((upper * upper) + (lower * lower) + (2f * upper * lower * MathF.Cos(bend)));
            return ArmMuscle.Leverage(new ArmMuscle.Pose(Vector3.UnitZ, r, Vector3.Zero, upper, lower), c).Elbow;
        }

        Assert.AreEqual(1f, Push(90f), 1e-3f, "the guard (elbow 90 degrees) is the reference");
        Assert.IsGreaterThanOrEqualTo(0.999f, Push(20f), "just short of full extension the elbow still pushes the fist out as hard as from the guard (the lever makes up for the weaker torque)");
        Assert.IsLessThan(0.7f, Push(130f), "a deeply bent arm pushes the fist out weakly");
        // 伸びきり（てこ 0）でも有限（てこは 10° で頭打ち）。
        Assert.IsLessThanOrEqualTo(1f, Push(0f), "the push never exceeds the guard's: beyond it the bones and the joint limit carry the load (the solver already does)");

        // 動いている伸びきりの近くでは、肘の角速度が大きく、拳を押し出す力は速さの関係で落ちる（同じ拳の速さでも構えより弱い）。
        var moving = new ArmMuscle.Pose(Vector3.UnitZ, 0.5775f, new Vector3(0f, 0f, 2.518f), upper, lower);
        var guard = new ArmMuscle.Pose(Vector3.UnitZ, MathF.Sqrt((upper * upper) + (lower * lower)), new Vector3(0f, 0f, 2.518f), upper, lower);
        Span<float> forward = [0f, 0f, 0f, 0f, 1f, 0f];
        var nearFull = ArmMuscle.Force(forward, Quaternion.Identity, 500f, moving, c, 0f, 1f).Z;
        var atGuard = ArmMuscle.Force(forward, Quaternion.Identity, 500f, guard, c, 0f, 1f).Z;
        Console.WriteLine($"push at 2.5 m/s: guard {atGuard:0.0} N, near full extension {nearFull:0.0} N");
        Assert.IsLessThan(atGuard, nearFull, "a fist moving out near full extension needs a fast elbow, so it is pushed less");
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
        Step(world, f, Command(guard), 2.518f);
        var peakActivation = 0f;
        var reach = 0f;
        for (var t = 0f; t < 0.2518f; t += Dt)
        {
            f.ApplyControl(Command(far), Dt);
            world.Step(Dt);
            var a = f.HandActivation(Hand.Right);
            for (var i = 0; i < ArmMuscle.Groups; i++)
            {
                peakActivation = MathF.Max(peakActivation, a[i]);
            }

            reach = MathF.Max(reach, Vector3.Distance(f.GloveCenter(Hand.Right), f.ShoulderPosition(Hand.Right)));
        }

        Console.WriteLine($"peak activation {peakActivation:0.00}, shoulder-to-fist {reach:0.000} m (arm {f.ArmReach:0.000} m)");
        Assert.IsGreaterThan(0.95f, peakActivation, "a target out of reach drives the muscle fully");
        Assert.IsGreaterThan(0.9f * f.ArmReach, reach, "and the fist goes out to full extension (measured from the shoulder)");
    }

    [TestMethod]
    public void Force_PullsAlongTheGroups_TurnsWithTheChest_AndTheAntagonistBrakesHarderThanItPushes()
    {
        var c = ArmMuscle.Constants.From(Tuning, 0f);
        const float upper = 0.307f;
        const float lower = 0.2817f;
        var guard = MathF.Sqrt((upper * upper) + (lower * lower));
        Span<float> forward = [0f, 0f, 0f, 0f, 1f, 0f];
        Span<float> backward = [0f, 0f, 0f, 0f, 0f, 1f];
        Span<float> up = [0f, 0f, 1f, 0f, 0f, 0f];
        var still = new ArmMuscle.Pose(Vector3.UnitZ, guard, Vector3.Zero, upper, lower);
        Assert.AreEqual(500f, ArmMuscle.Force(forward, Quaternion.Identity, 500f, still, c, 0f, 1f).Z, 1e-3f, "the forward group at the guard pulls the full force forward");
        Assert.AreEqual(500f, ArmMuscle.Force(up, Quaternion.Identity, 500f, still, c, 0f, 1f).Y, 1e-3f, "the shoulder group pulls the full force across the arm");

        // 胸が右へ 90° 回れば、胸の前（+Z）のまとまりはワールドの +X を引く。
        var turned = Quaternion.CreateFromAxisAngle(Vector3.UnitY, MathF.PI / 2f);
        var side = new ArmMuscle.Pose(Vector3.UnitX, guard, Vector3.Zero, upper, lower);
        Assert.AreEqual(500f, ArmMuscle.Force(forward, turned, 500f, side, c, 0f, 1f).X, 1e-3f, "the groups turn with the chest");

        // 拳が前へ 1.68 m/s で動いている: 前へ押す筋は縮みながら（弱い）、後ろへ引く筋は伸ばされながら（強い）。
        var moving = still with { Velocity = new Vector3(0f, 0f, 1.678f) };
        var push = ArmMuscle.Force(forward, Quaternion.Identity, 500f, moving, c, 0f, 1f).Z;
        var brake = -ArmMuscle.Force(backward, Quaternion.Identity, 500f, moving, c, 0f, 1f).Z;
        Console.WriteLine($"at 1.68 m/s outward: push {push:0.0} N, brake {brake:0.0} N");
        Assert.IsLessThan(500f, push, "a shortening agonist is weaker than isometric");
        Assert.IsGreaterThan(500f, brake, "a lengthening antagonist is stronger than isometric");
    }

    [TestMethod]
    public void TheVelocityStiffness_IsSolvedOneStepAhead_SoTheForceDoesNotFlip()
    {
        // 伸びきりの近く（肘 15°）で拳の速さに対する粘りは固い。1 歩（1/240 s）で、筋の力だけで拳（3.3 kg）を動かしても、
        // 速さが筋の最大の縮む速さを行き来して振れない（後退オイラー）。
        var c = ArmMuscle.Constants.From(Tuning, 0f);
        const float upper = 0.307f;
        const float lower = 0.2817f;
        const float mass = 3.3f;
        var bend = 15f * MathF.PI / 180f;
        var r = MathF.Sqrt((upper * upper) + (lower * lower) + (2f * upper * lower * MathF.Cos(bend)));
        Span<float> forward = [0f, 0f, 0f, 0f, 1f, 0f];
        var v = 0f;
        var forces = new List<float>();
        for (var i = 0; i < 40; i++)
        {
            var force = ArmMuscle.Force(forward, Quaternion.Identity, 600f, new ArmMuscle.Pose(Vector3.UnitZ, r, new Vector3(0f, 0f, v), upper, lower), c, Dt, mass).Z;
            forces.Add(force);
            v += force * Dt / mass;
        }

        for (var i = 2; i < forces.Count; i++)
        {
            Assert.IsGreaterThanOrEqualTo(-1e-3f, forces[i - 1] - forces[i], $"the push only fades as the fist speeds up (step {i}: {forces[i - 1]:0.00} → {forces[i]:0.00} N)");
        }
    }

    [TestMethod]
    public void HoldingAnArmOutAtATargetOutOfReach_DoesNotCarryTheBodyForward()
    {
        // 届かない所を狙い続けて伸ばした腕は、伸びきりの肘のてこで最大の力を超えて押すが、その分は肩と拳の間の内力で、体を前へ運ばない。
        // 同じ時間、構えたまま立つ体と比べて、骨盤が前へ流れる距離の差が 8.5 cm（腕の長さの 1 割半）を超えない。
        var still = Drift(far: false);
        var holding = Drift(far: true);
        Console.WriteLine($"pelvis forward after 2.5 s: guard {still * 100f:0.0} cm, arm held out at a far target {holding * 100f:0.0} cm");
        Assert.IsLessThan(0.0845f, holding - still, "the arm held out does not drag the body forward");

        static float Drift(bool far)
        {
            using var world = new PhysicsWorld();
            world.AddStaticBox(new Vector3(0f, -0.5f, 0f), new Vector3(5f, 0.5f, 5f));
            var f = Fighter.Spawn(world, FighterTuning.Default, "f", Vector3.Zero, 0f);
            var forward = f.Frame.Forward;
            var left = AiControl.Guard(Hand.Left, f.ArmReach);
            var guard = AiControl.Guard(Hand.Right, f.ArmReach);
            var right = far ? guard + new Vector3(0f, 0f, 2f * f.ArmReach) : guard;
            FighterCommand Command(Vector3 r) => new(Vector3.Zero, forward, new HandCommand(left, false), new HandCommand(r, false), Vector3.Zero);
            Step(world, f, Command(guard), 1.678f);
            var start = f.PelvisPosition;
            Step(world, f, Command(right), 2.518f);
            return Vector3.Dot(f.PelvisPosition - start, forward);
        }
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
