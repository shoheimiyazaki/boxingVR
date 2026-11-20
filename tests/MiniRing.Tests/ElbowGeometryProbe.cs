using System.Globalization;
using System.Numerics;
using Aigis.Physics;

namespace MiniRing.Tests;

/// <summary>調査用: 肩・腕の寸法（体フレーム、腕の長さ倍）。</summary>
[TestClass]
[TestCategory("Probe")]
public sealed class ElbowGeometryProbe
{
    [TestMethod]
    public void PrintArmGeometry()
    {
        using var world = new PhysicsWorld();
        world.AddStaticBox(new Vector3(0f, -1.408f, 0f), new Vector3(14.08f, 1.408f, 14.08f));
        var f = Fighter.Spawn(world, FighterTuning.Default, "g", Vector3.Zero, 0f);
        for (var i = 0; i < 403; i++)
        {
            f.ApplyControl(new FighterCommand(Vector3.Zero, Vector3.UnitZ, new HandCommand(AiControl.Guard(Hand.Left, f.ArmReach), false), new HandCommand(AiControl.Guard(Hand.Right, f.ArmReach), false)), 1f / 240f);
            world.Step(1f / 240f);
        }

        var frame = f.Frame;
        foreach (var h in new[] { Hand.Left, Hand.Right })
        {
            Console.WriteLine(string.Create(CultureInfo.InvariantCulture,
                $"{h} side {h.LocalSide()} reach {f.ArmReach:F3} m; shoulder local {frame.WorldToLocal(f.ShoulderPosition(h))}; guard {AiControl.Guard(h, f.ArmReach)}; glove {frame.WorldToLocal(f.GloveCenter(h))}; head {frame.WorldToLocal(f.HeadPosition)}"));
        }
    }
}
