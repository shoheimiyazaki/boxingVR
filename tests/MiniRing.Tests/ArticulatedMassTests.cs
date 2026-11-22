using System.Numerics;
using Aigis.Physics;

namespace MiniRing.Tests;

/// <summary>
/// 関節で連なった体の有効質量（<see cref="Fighter.ArticulatedMass"/>）の物理の性質: 部位そのものより重く体全体より軽い・重心を通る押しは重く外れた押しは軽い・
/// 肩から拳がまっすぐ並んで当たる向きへ揃った腕は、肘の曲がった腕より重い。
/// </summary>
[TestClass]
public sealed class ArticulatedMassTests
{
    private const float Dt = 1f / 240f;

    public TestContext TestContext { get; set; } = null!;

    [TestMethod]
    public void TheEffectiveMass_LiesBetweenThePartAndTheWholeBody()
    {
        var (world, f) = Standing(armOut: false);
        using (world)
        {
            var forward = f.Frame.Forward;
            var chest = world.GetState(f.Parts.Chest).Position;
            var head = world.GetState(f.Parts.Head).Position;
            var throughChest = f.ArticulatedMass(f.Parts.Chest, chest, forward);
            var headCenter = f.ArticulatedMass(f.Parts.Head, head, forward);
            var headSide = f.ArticulatedMass(f.Parts.Head, head + (0.08f * Vector3.UnitY), Vector3.Cross(Vector3.UnitY, forward));
            TestContext.WriteLine($"chest {throughChest:0.00} kg (part {world.GetMass(f.Parts.Chest):0.00}, body {f.TotalMass:0.0}), head {headCenter:0.00} kg (part {world.GetMass(f.Parts.Head):0.00}), head off-centre sideways {headSide:0.00} kg");

            Assert.IsGreaterThan(world.GetMass(f.Parts.Chest), throughChest, "the chest pushes the joined body with it");
            Assert.IsLessThan(f.TotalMass, throughChest, "but the joints let the rest rotate away: less than the whole body");
            Assert.IsGreaterThan(world.GetMass(f.Parts.Head), headCenter, "the head drags the body through the neck");
            Assert.IsLessThan(headCenter, headSide, "a blow off the head's centre turns it on the neck: it feels lighter");
        }
    }

    [TestMethod]
    public void AStraightArmLinedUpWithTheBlow_IsHeavierThanABentOne()
    {
        var straight = FistMass(armOut: true);
        var bent = FistMass(armOut: false);
        TestContext.WriteLine($"fist effective mass along the punch: straight arm {straight:0.00} kg, bent arm {bent:0.00} kg");
        Assert.IsGreaterThan(1.3f * bent, straight, "the straight arm passes the body's mass through its bones");
    }

    private static float FistMass(bool armOut)
    {
        var (world, f) = Standing(armOut);
        using (world)
        {
            var glove = f.GloveCenter(Hand.Right);
            var line = glove - f.ShoulderPosition(Hand.Right);
            return f.ArticulatedMass(f.ForearmId(Hand.Right), glove, line);
        }
    }

    private static (PhysicsWorld World, Fighter Fighter) Standing(bool armOut)
    {
        var world = new PhysicsWorld();
        world.AddStaticBox(new Vector3(0f, -0.5f, 0f), new Vector3(5f, 0.5f, 5f));
        var f = Fighter.Spawn(world, FighterTuning.Default, "f", Vector3.Zero, 0f);
        var forward = f.Frame.Forward;
        var left = AiControl.Guard(Hand.Left, f.ArmReach);
        var right = armOut ? new Vector3(-0.05f * f.ArmReach, 0.1f * f.ArmReach, 1.2f * f.ArmReach) : AiControl.Guard(Hand.Right, f.ArmReach);
        var command = new FighterCommand(Vector3.Zero, forward, new HandCommand(left, false), new HandCommand(right, false));
        for (var i = 0; i < 360; i++)
        {
            f.ApplyControl(command, Dt);
            world.Step(Dt);
        }

        return (world, f);
    }
}
