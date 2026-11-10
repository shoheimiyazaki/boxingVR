using System.Numerics;
using Aigis.Physics;

namespace MiniRing.Tests;

/// <summary>
/// 頭の指令（HeadOffset）に対して、体が実際にどれだけ沈み・傾くかを測る（回避の基本動作の土台）。
/// 標準出力に表を出す（--logger "console;verbosity=detailed"）。
/// </summary>
[TestClass]
public sealed class PostureProbe
{
    private const float Dt = 1f / 240f;

    [TestMethod]
    public void HeadOffset_MovesHeadAndHips()
    {
        using var probeWorld = NewWorld();
        var probe = Fighter.Spawn(probeWorld, FighterTuning.Default, "f", Vector3.Zero, 0f);
        var reach = probe.ArmReach;
        Console.WriteLine($"reach {reach:0.000} m");
        var he = FighterTuning.Default.HalfExtents;
        Console.WriteLine($"head half extents {he.Head.X:0.000},{he.Head.Y:0.000},{he.Head.Z:0.000} m = {he.Head.X / reach:0.00},{he.Head.Y / reach:0.00},{he.Head.Z / reach:0.00} R; glove radius {FighterTuning.Default.GloveRadius / reach:0.00} R");
        foreach (var (x, y, z) in new[] { (0f, 0f, 0f), (0f, -0.3f, 0f), (0f, -0.6f, 0f), (0f, -0.6f, 0.2f), (0.4f, 0f, 0f), (0.4f, -0.4f, 0.1f) })
        {
            using var world = NewWorld();
            var f = Fighter.Spawn(world, FighterTuning.Default, "f", Vector3.Zero, 0f);
            Run(world, f, Idle(f), 1.5f);
            var head0 = f.HeadPosition;
            var pelvis0 = f.PelvisPosition;
            var cmd = Idle(f) with { HeadOffset = new Vector3(x, y, z) * reach };
            Run(world, f, cmd, 0.25f);
            var head1 = f.HeadPosition;
            Run(world, f, cmd, 0.75f);
            var head2 = f.HeadPosition;
            var pelvis2 = f.PelvisPosition;
            Console.WriteLine(
                $"cmd ({x:+0.00;-0.00},{y:+0.00;-0.00},{z:+0.00;-0.00})R  head@0.25s ({(head1.X - head0.X) / reach:+0.00;-0.00},{(head1.Y - head0.Y) / reach:+0.00;-0.00},{(head1.Z - head0.Z) / reach:+0.00;-0.00})R  " +
                $"head@1s ({(head2.X - head0.X) / reach:+0.00;-0.00},{(head2.Y - head0.Y) / reach:+0.00;-0.00},{(head2.Z - head0.Z) / reach:+0.00;-0.00})R  pelvis d ({(pelvis2.Y - pelvis0.Y) / reach:+0.00;-0.00},{(pelvis2.Z - pelvis0.Z) / reach:+0.00;-0.00})R  upright {f.Uprightness:0.00} {f.State}  face down {FaceDown(world, f):0}deg");
        }
    }

    [TestMethod]
    public void SideDownStep_HeadPathOverTime()
    {
        using var world = NewWorld();
        var f = Fighter.Spawn(world, FighterTuning.Default, "f", Vector3.Zero, 0f);
        var reach = f.ArmReach;
        Run(world, f, Idle(f), 1.5f);
        var head0 = f.HeadPosition;
        Console.WriteLine("side+down");
        Trace(world, f, Idle(f) with { HeadOffset = new Vector3(0.5f, -0.6f, 0f) * reach }, head0, reach);
    }

    [TestMethod]
    public void SideDownStepWithFeet_HeadPathOverTime()
    {
        using var world = NewWorld();
        var f = Fighter.Spawn(world, FighterTuning.Default, "f", Vector3.Zero, 0f);
        var reach = f.ArmReach;
        Run(world, f, Idle(f), 1.5f);
        var head0 = f.HeadPosition;
        Console.WriteLine("side+down+back, feet to the side");
        Trace(world, f, Idle(f) with { HeadOffset = new Vector3(0.6f, -0.7f, -0.3f) * reach, MoveWorld = new Vector3(1f, 0f, 0f) }, head0, reach);
    }

    private static void Trace(PhysicsWorld world, Fighter f, FighterCommand cmd, Vector3 head0, float reach)
    {
        for (var i = 1; i <= 8; i++)
        {
            Run(world, f, cmd, 0.025f);
            var d = (f.HeadPosition - head0) / reach;
            Console.WriteLine($"step +{i * 0.025f:0.000}s head ({d.X:+0.00;-0.00},{d.Y:+0.00;-0.00},{d.Z:+0.00;-0.00})R  off-line {MathF.Sqrt((d.X * d.X) + (d.Y * d.Y)):0.00}R");
        }
    }

    /// <summary>顔の向きが水平からどれだけ下を向いているか [度]。</summary>
    private static float FaceDown(PhysicsWorld world, Fighter f)
    {
        var forward = Vector3.Transform(Vector3.UnitZ, world.GetState(f.Parts.Head).Orientation);
        return -MathF.Asin(Math.Clamp(forward.Y, -1f, 1f)) * 180f / MathF.PI;
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
