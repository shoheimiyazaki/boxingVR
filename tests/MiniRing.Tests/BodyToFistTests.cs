using System.Numerics;
using Aigis.Physics;

namespace MiniRing.Tests;

/// <summary>
/// 体の動きが拳に乗る（拳が体にバネでつながる）: 回避で動いている体から打つ拳は、体幹の運動エネルギーの一部をもらって速く出る。
/// エネルギーは湧かない（体の動きはそのぶん止まる）。止まった体から打つ拳は変わらない。
/// </summary>
[TestClass]
public sealed class BodyToFistTests
{
    private const float Dt = 1f / 240f;

    [TestMethod]
    public void PunchThrownWhileSteppingIn_IsFaster_WhenTheBodysMotionIsPassedToTheFist()
    {
        // 踏み込む体の勢いは拳に乗る（体はそのぶん止まる）。下がりながら打つ拳には、拳と逆の動きなので乗らない。
        var plain = Punch(spring: 0f, step: 1f);
        var carried = Punch(spring: 1f, step: 1f);
        var backing = Punch(spring: 1f, step: -1f);
        var still = Punch(spring: 1f, step: 0f);
        Console.WriteLine($"stepping in: fist peak {plain.Peak:0.00} → {carried.Peak:0.00} m/s, trunk {plain.Trunk:0.00} → {carried.Trunk:0.00} m/s; still body {still.Peak:0.00}, backing off {backing.Peak:0.00} m/s");

        Assert.IsGreaterThan(1.15f * plain.Peak, carried.Peak, "the body's motion is carried into the fist");
        Assert.IsLessThan(plain.Trunk, carried.Trunk, "and the body slows by what it gave (no energy from nowhere)");
        Assert.IsGreaterThan(1.1f * still.Peak, carried.Peak, "a still body has less to give");
        Assert.IsLessThan(carried.Peak, backing.Peak, "backing off gives the fist nothing");
    }

    [TestMethod]
    public void PunchThrownRisingOutOfADuck_RidesTheReboundOfTheBentBody()
    {
        // ダッキングで屈んだ脚と前へ折った体はバネ: 起き上がる反発の動きを止めて、拳へ移す。
        var plain = Punch(spring: 0f, step: 0f, duck: true);
        var carried = Punch(spring: 1f, step: 0f, duck: true);
        Console.WriteLine($"rising out of a duck: fist peak {plain.Peak:0.00} → {carried.Peak:0.00} m/s, body rising at {plain.Rise:0.00} → {carried.Rise:0.00} m/s");

        Assert.IsGreaterThan(1.15f * plain.Peak, carried.Peak, "the rebound of the bent body is carried into the fist");
        Assert.IsLessThan(plain.Rise, carried.Rise, "and the body rises slower by what it gave");
    }

    /// <summary>
    /// 0.35 秒前へ（step 正）または後ろへ（負）動いてから、右拳を構えから前へ 0.12 秒で出す。拳の最高速度と、打ち終わりの体幹の速さ。
    /// </summary>
    private static (float Peak, float Trunk, float Rise) Punch(float spring, float step, bool duck = false)
    {
        using var world = new PhysicsWorld();
        world.AddStaticBox(new Vector3(0f, -0.5f, 0f), new Vector3(5f, 0.5f, 5f));
        var f = Fighter.Spawn(world, FighterTuning.Default, "f", Vector3.Zero, 0f);
        var forward = f.Frame.Forward;
        var left = AiControl.Guard(Hand.Left, f.ArmReach);
        var guard = AiControl.Guard(Hand.Right, f.ArmReach);
        var aim = guard + new Vector3(0f, 0.05f * f.ArmReach, 0.75f * f.ArmReach);
        FighterCommand Command(Vector3 move, Vector3 right, float s, float headDrop = 0f) => new(move, forward, new HandCommand(left, false), new HandCommand(right, false, s), new Vector3(0f, -headDrop, 0.25f * headDrop));
        Step(world, f, Command(Vector3.Zero, guard, 0f), 1.5f);
        var side = forward * step;
        Step(world, f, Command(side, guard, 0f, duck ? 0.45f * f.ArmReach : 0f), 0.35f);
        var peak = 0f;
        var rise = 0f;
        for (var t = 0f; t < 0.2f; t += Dt)
        {
            var right = Vector3.Lerp(guard, aim, MathF.Min(1f, t / 0.12f));
            f.ApplyControl(Command(Vector3.Zero, right, spring), Dt);
            world.Step(Dt);
            peak = MathF.Max(peak, f.GloveVelocity(Hand.Right).Length());
            rise = MathF.Max(rise, world.GetState(f.Parts.Pelvis).LinearVelocity.Y);
        }

        return (peak, (world.GetState(f.Parts.Pelvis).LinearVelocity with { Y = 0f }).Length(), rise);
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
