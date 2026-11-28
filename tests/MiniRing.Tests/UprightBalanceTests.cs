using System.Numerics;
using Aigis.Physics;

namespace MiniRing.Tests;

/// <summary>
/// 上体を立てて保つ（2026-11-27、物理 106）: 腰から打つ体の骨盤・胴の筋の減衰が震えないこと、釣り合いの傾きが押された向きに逆らうこと、
/// ステップの後に上体を折ったままにしないこと。調べものは <c>UprightBalanceProbe</c>（格子・移動・押し）。
/// </summary>
[TestClass]
[TestCategory("L3b")]
public sealed class UprightBalanceTests
{
    private const float Dt = 1f / 240f;

    private static (PhysicsWorld World, Fighter Fighter) Alone(float muscle)
    {
        var world = new PhysicsWorld();
        world.AddStaticBox(new Vector3(0f, -1.408f, 0f), new Vector3(14.08f, 1.408f, 14.08f));
        return (world, Fighter.Spawn(world, FighterTuning.Default with { Build = new BodyBuild(1f, 1f, 1f, muscle) }, "f", Vector3.Zero, 0f));
    }

    private static FighterCommand Guard(Fighter f, Vector3 step = default) =>
        new(Vector3.Zero, Vector3.UnitZ, new HandCommand(AiControl.Guard(Hand.Left, f.ArmReach), false),
            new HandCommand(AiControl.Guard(Hand.Right, f.ArmReach), false), Step: step);

    private static float ChestTiltDegrees(PhysicsWorld world, Fighter f)
    {
        var up = Vector3.Transform(Vector3.UnitY, world.GetState(f.Parts.Chest).Orientation);
        return MathF.Acos(Math.Clamp(up.Y, -1f, 1f)) * 180f / MathF.PI;
    }

    /// <summary>構えて立っているだけの体の、胸の傾きの平均 [度] と骨盤の回る速さの二乗平均 [rad/s]（2〜4 秒）。</summary>
    private static (float Tilt, float PelvisSpin) Stand(float muscle)
    {
        var (world, f) = Alone(muscle);
        using var _ = world;
        var cmd = Guard(f);
        var (tilt, spin, n) = (0f, 0f, 0);
        for (var i = 0; i < 240 * 4; i++)
        {
            f.ApplyControl(cmd, Dt);
            world.Step(Dt);
            if (i >= 240 * 2)
            {
                tilt += ChestTiltDegrees(world, f);
                spin += world.GetState(f.Parts.Pelvis).AngularVelocity.LengthSquared();
                n++;
            }
        }

        Assert.AreEqual(FighterState.Standing, f.State, $"太さ {muscle} の体が立っているだけで倒れた");
        return (tilt / n, MathF.Sqrt(spin / n));
    }

    /// <summary>
    /// 細い体も、胴の筋が上体の重さを十分に支えられる（重力のモーメント ≪ 筋の上限）なら標準の体と同じように立つ。骨盤を起こすバネの減衰を骨盤だけの
    /// 角速度で見ていた間は、骨盤が 1 歩ごとに反転して震え（太さ 0.75 で ±39 rad/s）、震えの減衰が胴の筋の上限を埋めて胸が 30° 垂れた。
    /// </summary>
    [TestMethod]
    public void AThinBody_StandsAsUprightAsTheStandardBody_WithoutTheTorsoChattering()
    {
        var standard = Stand(1f);
        foreach (var muscle in new[] { 0.6f, 0.75f })
        {
            var thin = Stand(muscle);
            Assert.IsTrue(thin.Tilt < standard.Tilt + 5f, $"太さ {muscle}: 胸の傾き {thin.Tilt:0.0}° が標準 {standard.Tilt:0.0}° より 5° 以上大きい");
            Assert.IsTrue(thin.PelvisSpin < 1f, $"太さ {muscle}: 立っているだけの骨盤が {thin.PelvisSpin:0.0} rad/s で回っている（震え）");
        }

        Assert.IsTrue(standard.PelvisSpin < 1f, $"標準の体の骨盤が {standard.PelvisSpin:0.0} rad/s で回っている（震え）");
    }

    /// <summary>
    /// 前へステップした後、上体は脚のバネが貯まり直すのを待たずに起き直る（バネが空いて腰が伸び上がった分を頭を下げる量に数えていた間は、
    /// 上体を 20° 前へ折ったまま 1 秒残った）。
    /// </summary>
    [TestMethod]
    public void AfterAStep_TheTorsoComesBackUpright()
    {
        var (world, f) = Alone(1f);
        using var _ = world;
        var startCharge = 0f;
        var tiltAfter = 0f;
        for (var i = 0; i < 240 * 3; i++)
        {
            var t = i * Dt;
            f.ApplyControl(Guard(f, t is >= 1f and < 1.1f ? Vector3.UnitZ : Vector3.Zero), Dt);
            world.Step(Dt);
            if (i == 240)
            {
                startCharge = f.StepCharge;
            }

            if (t is >= 1.8f and < 1.9f)
            {
                tiltAfter = MathF.Max(tiltAfter, ChestTiltDegrees(world, f));
            }
        }

        Assert.IsTrue(f.StepEnergyReleased > 0f && startCharge > 0.5f, "ステップを踏んでいない（調べる場面になっていない）");
        Assert.IsTrue(tiltAfter < 8f, $"ステップの 0.7 秒後に胸がまだ {tiltAfter:0.0}° 傾いている");
        Assert.AreEqual(FighterState.Standing, f.State);
    }

    /// <summary>
    /// 体ごと押されると、釣り合いの傾きは押された向きと逆へ（作用点を足の外へ置けない分を、押し戻す向きへ傾けて出す）働き、体は踏みとどまって起き直る。
    /// 前後に開いた構えの後ろへの押しは、捕捉点が両足の間に収まるので足首（作用点を足の中で動かす）で受けて傾けない。足の幅の狭い横への押しでは傾けて受ける。
    /// </summary>
    [TestMethod]
    public void APushedBody_LeansAgainstThePush_AndRecovers()
    {
        foreach (var direction in new[] { -Vector3.UnitZ, Vector3.UnitX })
        {
            var (world, f) = Alone(1f);
            using var _ = world;
            var cmd = Guard(f);
            var (against, along) = (0f, 0f);
            var tiltLate = 0f;
            for (var i = 0; i < 240 * 4; i++)
            {
                if (i == 240 * 2)
                {
                    foreach (var id in f.AllParts)
                    {
                        world.ApplyLinearImpulse(id, direction * (world.GetMass(id) * 3f));
                    }
                }

                f.ApplyControl(cmd, Dt);
                world.Step(Dt);
                if (i is > 240 * 2 and < (240 * 2) + 72)
                {
                    var push = Vector2.Dot(f.BalanceAccel, new Vector2(direction.X, direction.Z));
                    (against, along) = (MathF.Min(against, push), MathF.Max(along, push));
                }

                if (i > (240 * 3) + 120)
                {
                    tiltLate = MathF.Max(tiltLate, ChestTiltDegrees(world, f));
                }
            }

            Assert.IsTrue(along <= 0f, $"{direction} へ押されて、釣り合いの傾きが押された向きへ {along:0.00} m/s² 働いた");
            Assert.IsTrue(direction.X == 0f || against < 0f, $"{direction}（足の幅の狭い向き）へ押されて、釣り合いの傾きが働かなかった");
            Assert.AreEqual(FighterState.Standing, f.State, $"{direction} へ 3 m/s 押されて倒れた");
            Assert.IsTrue(tiltLate < 8f, $"{direction} へ押されて 1.5 秒後も胸が {tiltLate:0.0}° 傾いている");
        }
    }
}
