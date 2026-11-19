using System.Numerics;
using Aigis.Physics;

namespace MiniRing.Tests;

[TestClass]
public sealed class ZzStaminaScaleProbe
{
    [TestMethod]
    [TestCategory("Probe")]
    public void Probe_WorkByBuild()
    {
        foreach (var (name, build) in new[] { ("std", BodyBuild.Standard), ("H1.15", new BodyBuild(Height: 1.15f)), ("M1.2", new BodyBuild(Muscle: 1.2f)), ("big", new BodyBuild(Height: 1.15f, Muscle: 1.2f)) })
        {
            foreach (var mode in new[] { "stand", "walk", "punch" })
            {
                using var world = new PhysicsWorld();
                world.AddStaticBox(new Vector3(0f, -0.5f, 0f), new Vector3(5f, 0.5f, 5f));
                var tuning = FighterTuning.Default with { Build = build, VesselPowerPerKg = 0f };
                var f = Fighter.Spawn(world, tuning, "p", Vector3.Zero, 0f);
                var left = AiControl.Guard(Hand.Left, f.ArmReach);
                var guard = AiControl.Guard(Hand.Right, f.ArmReach);
                double p = 0; var n = 0;
                for (var step = 0; step < 6 * 240; step++)
                {
                    var move = mode == "walk" ? ((step / 240) % 2 == 0 ? Vector3.UnitZ : -Vector3.UnitZ) : Vector3.Zero;
                    var phase = step % 240;
                    var ext = mode == "punch" && phase < 29;
                    var aim = guard + new Vector3(0f, 0.05f, 1.6f) * f.ArmReach;
                    var target = ext ? Vector3.Lerp(guard, aim, MathF.Min(1f, phase / 240f / 0.06f)) : guard;
                    f.Stamina = 1f;
                    f.ApplyControl(new FighterCommand(move, Vector3.UnitZ, new HandCommand(left, false), new HandCommand(target, false, ext ? 1f : 0f)), 1f / 240f);
                    world.Step(1f / 240f);
                    if (step > 240) { p += f.RecentMusclePower; n++; }
                }
                Console.WriteLine($"{name,-6} {mode,-6} massRatio {build.MassRatio:0.00} P {p / n:0.00} W  per-ratio {p / n / build.MassRatio:0.00}");
            }
        }
    }
}
