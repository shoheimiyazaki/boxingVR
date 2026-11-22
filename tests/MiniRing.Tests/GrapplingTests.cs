using System.Numerics;
using Aigis.Physics;
using MiniRing.Bots;

namespace MiniRing.Tests;

/// <summary>
/// 組み技の物理（docs/GRAPPLING.md）: 支えを床から出す体（崩し）、肘の靭帯（関節技）、掴んで投げる（場合分けなし）。
/// </summary>
[TestClass]
[TestCategory("slow")]
public sealed class GrapplingTests
{
    private const float Dt = 1f / 240f;

    [TestMethod]
    public void SupportPolygon_IsTheConvexHull_AndClampsOutsidePointsToItsEdge()
    {
        var polygon = new SupportPolygon();
        foreach (var p in new[] { new Vector2(0f, 0f), new Vector2(1f, 0f), new Vector2(1f, 1f), new Vector2(0f, 1f), new Vector2(0.5f, 0.5f) })
        {
            polygon.Add(p);
        }

        polygon.Build();
        Assert.AreEqual(4, polygon.Hull.Count, "the inner point is not a vertex");
        Assert.IsTrue(polygon.Contains(new Vector2(0.5f, 0.2f)));
        Assert.IsFalse(polygon.Contains(new Vector2(1.5f, 0.5f)));
        Assert.AreEqual(new Vector2(1f, 0.5f), polygon.Clamp(new Vector2(1.5f, 0.5f)));
        Assert.AreEqual(0.5f, polygon.SignedDistance(new Vector2(1.5f, 0.5f)), 1e-5f);
        Assert.AreEqual(-0.2f, polygon.SignedDistance(new Vector2(0.5f, 0.2f)), 1e-5f);
    }

    [TestMethod]
    public void StandingFighter_IsSupportedByItsFeet_TheCentreOfMassIsInsideTheFeet()
    {
        var (world, f, idle) = Stand(support: true);
        using var _ = world;
        Run(world, f, idle, seconds: 1.678f);
        Assert.IsGreaterThanOrEqualTo(3, f.SupportHull.Count, "both soles on the canvas make a polygon");
        Assert.IsLessThan(0f, f.SupportMargin, $"the centre of mass is over the feet (margin {f.SupportMargin})");
    }

    [TestMethod]
    [Ignore("投げ・崩し・関節技は休止（2026-11-22、ユーザーの指定。docs/GRAPPLING.md）")]
    public void AModeratePull_IsCaughtByStepping_AHardPullTopplesTheFighter()
    {
        // 胸を前へ、体重の 0.3 倍で 1.7 秒引く: 踏み直して立っている。
        var moderate = Pull(0.3f, support: true);
        Assert.IsFalse(moderate.Fell, "a moderate pull is caught by stepping");
        Assert.IsGreaterThan(0.05634f, moderate.Moved, $"the fighter stepped with the pull (moved {moderate.Moved:0.000} m)");

        // 体重と同じ力で引く: 足が付いて行けず倒れる（崩し）。
        Assert.IsTrue(Pull(1f, support: true).Fell, "a pull as strong as the body weight topples a fighter supported by its feet");

        // 前の体（支えがどこからともなく出る）は、同じ力で引かれても立ったまま引きずられた。
        Assert.IsFalse(Pull(1f, support: false).Fell, "the old body stood upright while being dragged");
    }

    [TestMethod]
    public void WalkingInEveryDirection_DoesNotFall()
    {
        foreach (var direction in new[] { Vector3.UnitZ, -Vector3.UnitZ, Vector3.UnitX, -Vector3.UnitX, Vector3.Normalize(new Vector3(1f, 0f, -1f)) })
        {
            var (world, f, idle) = Stand(support: true);
            using var _ = world;
            var start = f.PelvisPosition;
            var fell = false;
            for (var i = 0; i < (int)(5.035f / Dt); i++)
            {
                f.ApplyControl(idle with { MoveWorld = direction }, Dt);
                world.Step(Dt);
                fell |= f.State != FighterState.Standing;
            }

            Assert.IsFalse(fell, $"walking {direction}");
            Assert.IsGreaterThan(1.408f, Vector3.Dot(f.PelvisPosition - start, direction), $"walked {direction}");
        }
    }

    [TestMethod]
    [Ignore("投げ・崩し・関節技は休止（2026-11-22、ユーザーの指定。docs/GRAPPLING.md）")]
    public void HyperextendingTheElbow_BelowTheLigamentsYield_LeavesItWhole_AboveItStretchesAndTears()
    {
        Assert.AreEqual(1f, Lever(torqueOverYield: 0.5f).Ligament, 1e-6f, "below the yield nothing stretches");

        var torn = Lever(torqueOverYield: 2.5f);
        Assert.AreEqual(0f, torn.Ligament, 1e-6f, "a lever far above the yield tears the ligament");
        Assert.IsLessThan(-20f * MathF.PI / 180f, torn.MinAngle, $"the torn elbow bends backward past the old −10° limit (min {torn.MinAngle * 180f / MathF.PI:0}°)");
        Assert.IsLessThan(torn.StrengthBefore, torn.StrengthAfter, "the arm with a torn elbow is weaker");
        Assert.AreEqual(0f, torn.StrengthAfter, 1e-6f, "a torn elbow carries no load");
    }

    [TestMethod]
    [Ignore("投げ・崩し・関節技は休止（2026-11-22、ユーザーの指定。docs/GRAPPLING.md）")]
    public void ReleasingAGripFast_DoesNotKnockTheOpponentDown_ByItself()
    {
        // 前は、離した瞬間の手の速さが ThrowSpeed を超えると相手を倒していた（場合分け）。今は、倒れるかは物理だけで決まる。
        using var world = new PhysicsWorld();
        world.AddStaticBox(new Vector3(0f, -1.408f, 0f), new Vector3(14.08f, 1.408f, 14.08f));
        var a = Fighter.Spawn(world, FighterTuning.Default, "a", Vector3.Zero, 0f);
        var b = Fighter.Spawn(world, FighterTuning.Default, "b", new Vector3(0f, 0f, 0.5634f), MathF.PI);
        var grabbed = false;
        for (var i = 0; i < (int)(3.357f / Dt); i++)
        {
            var t = i * Dt;
            var reach = new HandCommand(a.Frame.WorldToLocal(b.ChestPosition + new Vector3(0.08451f, 0f, 0f)), Grip: t > 1.678f && t < 2.182f);
            // 掴んだ後、手を横へ速く振って離す。
            if (t > 2.014f)
            {
                reach = reach with { TargetLocal = new Vector3(0.4f, 0f, 0.1f) * a.ArmReach };
            }

            var cmdA = Idle(a) with { FaceDirection = b.ChestPosition - a.ChestPosition, Right = reach };
            a.UpdateGrab(Hand.Right, cmdA.Right.Grip, b);
            a.ApplyControl(cmdA, Dt);
            b.ApplyControl(Idle(b) with { FaceDirection = -Vector3.UnitZ }, Dt);
            world.Step(Dt);
            grabbed |= a.IsHolding(Hand.Right);
        }

        Assert.IsTrue(grabbed, "the grip took hold");
        Assert.AreEqual(FighterState.Standing, b.State, "a quick release alone no longer throws");
    }

    [TestMethod]
    [Ignore("投げ・崩し・関節技は休止（2026-11-22、ユーザーの指定。docs/GRAPPLING.md）")]
    public void SasaeTsurikomiAshi_BreaksAStandingOpponentsBalance_ByPhysicsAlone()
    {
        // 支え釣り込み足だけをかけ続ける柔術家と、構えて立つだけの受け。引いて、踏み直す足首の前へ足の裏を当てて塞ぐ。
        // 受けの重心（の捕捉点）が足の外へ出て、支えの残り（Fighter.Balance）が減る（崩し）。倒れきるかは、縦の支えが抜けるまでに
        // 踏み直せるか（SupportVerticalRecovery）の物理で、ここでは数えない（docs/GRAPPLING.md §7）。
        var book = GrappleBook.Jujutsu with { Rate = 29.79f };
        var corners = new BoxingCorners("jujutsu", s => new JujutsuBot(new Closer(), book, s, only: "sasae-tsurikomi-ashi"), _ => new StandGuard(),
            CpuWear: MiniRing.Weapons.HandWear.None);
        using var game = new BoxingGame(seed: 2, corners: corners);
        var maxMargin = float.NegativeInfinity;
        var minBalance = 1f;
        for (var t = 0f; t < 16.78f; t += 1f / 60f)
        {
            game.Step(default, 1f / 60f);
            if (game.Player.State == FighterState.Standing && float.IsFinite(game.Player.SupportMargin))
            {
                maxMargin = MathF.Max(maxMargin, game.Player.SupportMargin);
                minBalance = MathF.Min(minBalance, game.Player.Balance);
            }
        }

        Assert.IsGreaterThan(0.1408f, maxMargin, $"the receiver's centre of mass left its feet (max {maxMargin:0.000} m)");
        Assert.IsLessThan(0.5f, minBalance, $"the receiver lost much of its support (min balance {minBalance:0.00})");
    }

    private static (bool Fell, float Moved) Pull(float bodyWeights, bool support)
    {
        var (world, f, idle) = Stand(support);
        using var _ = world;
        var start = f.PelvisPosition;
        var force = Vector3.UnitZ * (bodyWeights * f.TotalMass * -world.Gravity.Y);
        var fell = false;
        for (var i = 0; i < (int)(3.357f / Dt); i++)
        {
            f.ApplyControl(idle, Dt);
            if (i * Dt < 1.678f)
            {
                world.ApplyLinearImpulse(f.Parts.Chest, force * Dt);
            }

            world.Step(Dt);
            fell |= f.State != FighterState.Standing;
        }

        var moved = f.PelvisPosition - start;
        return (fell, new Vector2(moved.X, moved.Z).Length());
    }

    /// <summary>
    /// 右肘に、前腕と上腕へ逆向きのねじり（てこ）を 2.5 秒かける（肘を反らせる向き: 曲げ角が小さくなる向きを、試しにかけて決める）。
    /// 大きさは、肘を曲げて保つ筋（肘のサーボの上限）に勝つ分 ＋ 靭帯の降伏の <paramref name="torqueOverYield"/> 倍。0.34 秒かけて立ち上げる
    /// （軽い前腕へいきなり大きなねじりをかけると、関節の制限を突き抜けて回ってしまう）。
    /// </summary>
    private static (float Ligament, float MinAngle, float StrengthBefore, float StrengthAfter) Lever(float torqueOverYield)
    {
        var (world, f, idle) = Stand(support: true);
        using var _ = world;
        var (upper, fore) = f.ArmParts(Hand.Right);
        var before = f.ArmStrength(Hand.Right);
        var servo = 30f * FighterTuning.Default.TorqueScale; // 肘のサーボの上限（Fighter.Arm.SetStrength、基準体格の力 1）
        var torque = servo + (f.ElbowLigamentYield * torqueOverYield);
        var sign = 0f;
        var minAngle = float.PositiveInfinity;
        for (var i = 0; i < (int)(2.853f / Dt); i++)
        {
            f.ApplyControl(idle, Dt);
            var axis = f.ElbowAxis(Hand.Right);
            var ramp = Math.Clamp(i * Dt / 0.3357f, 0f, 1f);
            if (sign == 0f)
            {
                // 試しに +軸へ少しかけて、曲げ角が減る（反る）向きを決める。
                var angle0 = f.ElbowAngle(Hand.Right);
                world.ApplyAngularImpulse(fore, axis * (1.408f * Dt));
                world.ApplyAngularImpulse(upper, -axis * (1.408f * Dt));
                world.Step(Dt);
                sign = f.ElbowAngle(Hand.Right) < angle0 ? 1f : -1f;
                continue;
            }

            world.ApplyAngularImpulse(fore, axis * (sign * torque * ramp * Dt));
            world.ApplyAngularImpulse(upper, -axis * (sign * torque * ramp * Dt));
            world.Step(Dt);
            minAngle = MathF.Min(minAngle, f.ElbowAngle(Hand.Right));
        }

        f.ApplyControl(idle, Dt);
        return (f.ElbowLigament(Hand.Right), minAngle, before, f.ArmStrength(Hand.Right));
    }

    private static (PhysicsWorld World, Fighter Fighter, FighterCommand Idle) Stand(bool support)
    {
        var world = new PhysicsWorld();
        world.AddStaticBox(new Vector3(0f, -1.408f, 0f), new Vector3(14.08f, 1.408f, 14.08f));
        var f = Fighter.Spawn(world, FighterTuning.Default with { SupportFromGround = support }, "f", Vector3.Zero, 0f);
        var idle = Idle(f);
        Run(world, f, idle, seconds: 1.678f);
        return (world, f, idle);
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

[TestClass]
public sealed class CanvasImpactTests
{
    private const float Dt = 1f / 60f;

    [TestMethod]
    public void StandingAndWalking_DoNotHurtOnTheCanvas_ADropFromHeightDoes()
    {
        var corners = new BoxingCorners("still", _ => new StandGuard(), _ => new StandGuard());
        using var game = new BoxingGame(seed: 1, corners: corners);
        var impacts = new List<CanvasImpact>();
        game.Landed += impacts.Add;
        for (var t = 0f; t < 3.357f; t += Dt)
        {
            game.Step(default, Dt);
        }

        Assert.IsEmpty(impacts, "standing still never slams anything into the canvas");

        // 倒して寝かせ、全身を上へ投げ上げて落とす（投げで床に叩きつけられる代わり）。
        var f = game.Cpu;
        f.Knockdown(Vector3.UnitZ, permanent: true);
        for (var t = 0f; t < 2.518f; t += Dt)
        {
            game.Step(default, Dt);
        }

        f.Push(new Vector3(0f, 4.196f, 0f));
        for (var t = 0f; t < 2.518f; t += Dt)
        {
            game.Step(default, Dt);
        }

        Assert.IsNotEmpty(impacts, "a body dropped onto the canvas takes an impact");
        Assert.IsGreaterThan(2.817f, impacts.Max(i => i.Energy), $"energy {impacts.Max(i => i.Energy):0.000} J");
    }
}
