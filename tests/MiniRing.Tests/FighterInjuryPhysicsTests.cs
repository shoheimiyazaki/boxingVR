using System.Numerics;
using Aigis.Physics;

namespace MiniRing.Tests;

/// <summary>
/// 体の軸・パンチの力学・首の揺れが、ヘッドレスの物理で実際に起きることを確かめる（ダメージは動きに影響しない）。
/// </summary>
[TestClass]
public sealed class FighterInjuryPhysicsTests
{
    private const float Dt = 1f / 240f;

    [TestMethod]
    public void HandsDecideTheTorsoTwist()
    {
        using var world = NewWorld();
        var fighter = Fighter.Spawn(world, FighterTuning.Default, "f", Vector3.Zero, 0f);
        var straight = Idle(fighter) with { Right = new HandCommand(new Vector3(-0.05f, 0f, 1f) * fighter.ArmReach, false) };

        Run(world, fighter, straight, 1f);

        // 右ストレートで右肩（−X）が前へ → 胸の前方は +X 側へ回る（+Y まわり正）。
        var chestForward = Vector3.Transform(Vector3.UnitZ, fighter.TorsoOrientation);
        Assert.IsGreaterThan(0.15f, chestForward.X, $"chest forward {chestForward}");
        Assert.AreEqual(FighterState.Standing, fighter.State);
    }

    [TestMethod]
    public void HeadPosition_LeansAndLowersTheBody()
    {
        using var world = NewWorld();
        var fighter = Fighter.Spawn(world, FighterTuning.Default, "f", Vector3.Zero, 0f);
        Run(world, fighter, Idle(fighter), 1f);
        var standing = fighter.PelvisPosition.Y;

        var reach = fighter.ArmReach;
        Run(world, fighter, Idle(fighter) with { HeadOffset = new Vector3(-0.35f, -0.4f, 0f) * reach }, 1.5f);

        Assert.IsLessThan(standing - 0.01f, fighter.PelvisPosition.Y, "ducking lowers the body");
        var up = Vector3.Transform(Vector3.UnitY, fighter.TorsoOrientation);
        Assert.IsLessThan(-0.08f, up.X, $"slipping right (−X) tilts the torso right: up {up}");
        Assert.AreEqual(FighterState.Standing, fighter.State);
    }

    [TestMethod]
    public void ExtendedStraight_CarriesMoreOfTheBodyThanAnArmSwatSideways()
    {
        using var world = NewWorld();
        var fighter = Fighter.Spawn(world, FighterTuning.Default, "f", Vector3.Zero, 0f);
        var straight = Idle(fighter) with { Right = new HandCommand(new Vector3(0f, 0f, 1f) * fighter.ArmReach, false) };
        Run(world, fighter, straight, 1.2f);

        var forward = fighter.Frame.Forward;
        var throughTheBody = fighter.ImpactMass(Hand.Right, forward, out var straightShare);
        var swat = fighter.ImpactMass(Hand.Right, fighter.Frame.Right, out var swatShare);

        Assert.IsGreaterThan(swat * 1.5f, throughTheBody, $"straight {throughTheBody:0.00}kg ({straightShare:P0}) swat {swat:0.00}kg ({swatShare:P0})");
        Assert.IsGreaterThan(fighter.ArmMass(Hand.Right) * 3f, throughTheBody, "the straight is mostly body, not arm");
    }

    [TestMethod]
    public void HitHead_SnapsOnAStiffNeck_ThenSettlesWithoutWobbling()
    {
        // 横からのフック相当: 頭の横（こめかみの高さ、中心より上）を叩く。グローブは頭の表面に当たるので、押すだけでなく回す。
        // 首は硬く減衰の効いたバネ: 打たれていないときはぐらつかず、打たれると頭が持っていかれ、揺れ返さずに戻る。
        var light = HeadSnap(0.6f);
        var heavy = HeadSnap(1.2f);
        Console.WriteLine($"idle {light.Idle:0.000} rad; light: peak {light.Peak:0.000} rad, {light.SignChanges} swings back, settled {light.Settled:0.000}; heavy: peak {heavy.Peak:0.000} rad");

        Assert.IsLessThan(0.05f, light.Idle, "standing still, the head does not wobble on the neck");
        Assert.IsGreaterThan(0.08f, light.Peak, "a blow carries the head visibly");
        Assert.IsGreaterThan(1.8f * light.Peak, heavy.Peak, "twice the blow carries it about twice as far, or more (the neck gives out)");
        Assert.IsLessThanOrEqualTo(1, light.SignChanges, "it does not swing back and forth");
        Assert.IsLessThan(0.05f, light.Settled, "and settles upright again");
    }

    private static (float Idle, float Peak, int SignChanges, float Settled) HeadSnap(float speed)
    {
        using var world = NewWorld();
        var fighter = Fighter.Spawn(world, FighterTuning.Default, "f", Vector3.Zero, 0f);
        Run(world, fighter, Idle(fighter), 1f);
        var idle = 0f;
        for (var i = 0; i < (int)(0.5f / Dt); i++)
        {
            fighter.ApplyControl(Idle(fighter), Dt);
            world.Step(Dt);
            idle = MathF.Max(idle, MathF.Abs(HeadSideTilt(world, fighter)));
        }

        var impulse = new Vector3(1f, 0f, 0f) * (speed * FighterTuning.Default.Masses.Head);
        var temple = new Vector3(0f, 0.5f * FighterTuning.Default.HalfExtents.Head.Y, 0f);
        world.ApplyLinearImpulse(fighter.Parts.Head, impulse);
        world.ApplyAngularImpulse(fighter.Parts.Head, Vector3.Cross(temple, impulse));
        var signChanges = 0;
        var peak = 0f;
        var lastSign = 0;
        for (var i = 0; i < (int)(1.5f / Dt); i++)
        {
            fighter.ApplyControl(Idle(fighter), Dt);
            world.Step(Dt);
            var tilt = HeadSideTilt(world, fighter);
            peak = MathF.Max(peak, MathF.Abs(tilt));
            // 揺れ返しは、はっきり反対へ傾いたときだけ数える（体の小さな揺れは数えない）。
            var sign = MathF.Abs(tilt) < 0.03f ? 0 : MathF.Sign(tilt);
            if (sign != 0 && lastSign != 0 && sign != lastSign)
            {
                signChanges++;
            }

            lastSign = sign == 0 ? lastSign : sign;
        }

        Run(world, fighter, Idle(fighter), 1f);
        return (idle, peak, signChanges, MathF.Abs(HeadSideTilt(world, fighter)));
    }


    /// <summary>胴に対する頭の左右の傾き [rad]（胴の座標で頭の上方向が X へ倒れた角度）。</summary>
    private static float HeadSideTilt(PhysicsWorld world, Fighter fighter)
    {
        var relative = Quaternion.Concatenate(world.GetState(fighter.Parts.Head).Orientation, Quaternion.Inverse(world.GetState(fighter.Parts.Chest).Orientation));
        var up = Vector3.Transform(Vector3.UnitY, relative);
        return MathF.Atan2(up.X, up.Y);
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
