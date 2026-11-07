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
    public void HitHead_WobblesOnTheNeckSpring_ThenSettles()
    {
        using var world = NewWorld();
        var fighter = Fighter.Spawn(world, FighterTuning.Default, "f", Vector3.Zero, 0f);
        Run(world, fighter, Idle(fighter), 1f);

        // 横からのフック相当: 頭だけを横へ叩く。
        world.ApplyLinearImpulse(fighter.Parts.Head, new Vector3(1f, 0f, 0f) * (0.6f * FighterTuning.Default.Masses.Head));
        var signChanges = 0;
        var peak = 0f;
        var lastSign = 0;
        for (var i = 0; i < (int)(1.5f / Dt); i++)
        {
            fighter.ApplyControl(Idle(fighter), Dt);
            world.Step(Dt);
            var tilt = HeadSideTilt(world, fighter);
            peak = MathF.Max(peak, MathF.Abs(tilt));
            var sign = MathF.Abs(tilt) < 0.01f ? 0 : MathF.Sign(tilt);
            if (sign != 0 && lastSign != 0 && sign != lastSign)
            {
                signChanges++;
            }

            lastSign = sign == 0 ? lastSign : sign;
        }

        Run(world, fighter, Idle(fighter), 2f);
        var settled = MathF.Abs(HeadSideTilt(world, fighter));

        Assert.IsGreaterThan(0.15f, peak, $"the head swings visibly (peak {peak:0.000} rad)");
        Assert.IsGreaterThanOrEqualTo(2, signChanges, $"it swings back past upright, more than once ({signChanges})");
        Assert.IsLessThan(0.1f, settled, $"and settles upright again ({settled:0.000} rad)");
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
