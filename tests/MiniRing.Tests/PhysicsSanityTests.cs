using System.Numerics;
using Aigis.Physics;

namespace MiniRing.Tests;

/// <summary>
/// 物差し（計画の段 0・物理の憲法 §1 L0・L2 のテスト）: 解き手が勝手にエネルギーを増やさない、関節の拘束の誤差、つながった物の運動量の保存。
/// 合格の条件は今の値で通る緩い上限（金型を持たない）。測った値は TestContext へ書き出し、段ごとに「前より小さい」を同じビルドの比べで見る。
/// </summary>
[TestClass]
[TestCategory("L0")]
public sealed class PhysicsSanityTests
{
    private const float Dt = 1f / 240f;

    public TestContext TestContext { get; set; } = null!;

    private static float Energy(PhysicsWorld world, int id, float mass)
    {
        var s = world.GetState(id);
        return (mass * 9.81f * s.Position.Y) + (0.5f * mass * s.LinearVelocity.LengthSquared());
    }

    [TestMethod]
    public void ABouncingBall_NeverGainsEnergy()
    {
        using var world = new PhysicsWorld();
        world.AddStaticBox(new Vector3(0f, -1f, 0f), new Vector3(5f, 1f, 5f));
        const float mass = 2f;
        var ball = world.AddDynamicSphere(new Vector3(0f, 1f, 0f), 0.1f, mass);
        var start = Energy(world, ball, mass);
        var most = start;
        for (var i = 0; i < 240 * 3; i++)
        {
            world.Step(Dt);
            most = MathF.Max(most, Energy(world, ball, mass));
        }

        var gain = (most - start) / start;
        TestContext.WriteLine($"弾む球: 最初 {start:0.000} J、最大 {most:0.000} J（増え {gain:P2}）、3 秒後 {Energy(world, ball, mass):0.000} J");
        Assert.IsLessThan(0.02f, gain, "解き手がエネルギーを 2% 超えて増やした");
    }

    [TestMethod]
    public void APendulum_NeverGainsEnergy_AndItsJointStaysClosed()
    {
        using var world = new PhysicsWorld();
        var anchor = world.AddKinematicBox(new Vector3(0f, 2f, 0f), new Vector3(0.05f));
        const float mass = 3f;
        var bob = world.AddDynamicBox(new Vector3(0.5f, 2f, 0f), new Vector3(0.05f), mass);
        world.AddBallSocket(anchor, bob, Vector3.Zero, new Vector3(-0.5f, 0f, 0f));
        world.DisableSelfCollision(anchor, bob);
        var start = Energy(world, bob, mass);
        var (most, gap) = (start, 0f);
        for (var i = 0; i < 240 * 3; i++)
        {
            world.Step(Dt);
            most = MathF.Max(most, Energy(world, bob, mass));
            var s = world.GetState(bob);
            var socket = s.Position + Vector3.Transform(new Vector3(-0.5f, 0f, 0f), s.Orientation);
            gap = MathF.Max(gap, Vector3.Distance(socket, new Vector3(0f, 2f, 0f)));
        }

        var gain = (most - start) / MathF.Abs(start);
        TestContext.WriteLine($"振り子: エネルギーの増え {gain:P3}、関節の開きの最大 {gap * 1000f:0.0} mm");
        Assert.IsLessThan(0.02f, gain, "振り子のエネルギーが 2% 超えて増えた");
        Assert.IsLessThan(0.05f, gap, "関節の位置の誤差が 5 cm を超えた（DEBT「脚の関節の開き」の上限の物差し）");
    }

    [TestMethod]
    public void AJointedChainInFreeSpace_KeepsItsMomentum()
    {
        // L2 の不変量: 外の力が 0 なら、関節の内力だけでは体の運動量・角運動量は変わらない。
        using var world = new PhysicsWorld(gravity: Vector3.Zero, conserveAngularMomentum: true);
        var mass = new[] { 4f, 2f, 1f };
        var ids = new int[3];
        for (var i = 0; i < 3; i++)
        {
            ids[i] = world.AddDynamicBox(new Vector3(i * 0.4f, 0f, 0f), new Vector3(0.18f, 0.05f, 0.05f), mass[i]);
            if (i > 0)
            {
                world.AddBallSocket(ids[i - 1], ids[i], new Vector3(0.2f, 0f, 0f), new Vector3(-0.2f, 0f, 0f));
                world.DisableSelfCollision(ids[i - 1], ids[i]);
            }

            world.SetDamping(ids[i], 0f, 0f);
        }

        // 末端だけを蹴る（運動量 2 kg·m/s を外から 1 度だけ）。
        var kick = new Vector3(0f, 2f, 0.5f);
        world.Step(Dt);
        var p0 = Momentum(world, ids, mass);
        world.ApplyLinearImpulse(ids[2], kick);
        var p1 = Momentum(world, ids, mass);
        var worst = 0f;
        for (var i = 0; i < 240 * 2; i++)
        {
            world.Step(Dt);
            worst = MathF.Max(worst, Vector3.Distance(Momentum(world, ids, mass), p1));
        }

        var error = worst / kick.Length();
        TestContext.WriteLine($"鎖の運動量: 蹴る前 {p0}、蹴った後 {p1}、2 秒の間のずれの最大 {error:P3}");
        Assert.IsLessThan(0.01f, error, "関節の内力が運動量を 1% 超えて変えた");
    }

    private static Vector3 Momentum(PhysicsWorld world, int[] ids, float[] mass)
    {
        var p = Vector3.Zero;
        for (var i = 0; i < ids.Length; i++)
        {
            p += world.GetState(ids[i]).LinearVelocity * mass[i];
        }

        return p;
    }
}
