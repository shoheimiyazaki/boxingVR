using System.Globalization;
using System.Numerics;
using Aigis.Physics;

namespace MiniRing.Tests;

/// <summary>
/// 調べ物（2026-11-27、基準の体の移行）: 片手で 1 秒ごとの強い拳を 50 秒続けたとき、部位ごとの蓄え・した仕事・容量・質量を出す
/// （<c>StaminaTempoTests.FullReserve_OneHandStrongPace</c> と同じ打ち方）。前の基準の体と比べ、どの部位の何が変わったかを見る。
/// </summary>
[TestClass]
[TestCategory("Probe")]
public sealed class StaminaRegionProbe
{
    private const float Dt = 1f / 240f;

    [TestMethod]
    public void OneHandStrongPace_PerRegion()
    {
        using var world = new PhysicsWorld();
        world.AddStaticBox(new Vector3(0f, -1.408f, 0f), new Vector3(22.54f, 1.408f, 22.54f));
        var fighter = Fighter.Spawn(world, FighterTuning.Default, "tempo", Vector3.Zero, 0f);
        var left = AiControl.Guard(Hand.Left, fighter.ArmReach);
        var guard = AiControl.Guard(Hand.Right, fighter.ArmReach);
        void Step(Vector3 right, float spring)
        {
            fighter.ApplyControl(new FighterCommand(Vector3.Zero, Vector3.UnitZ, new HandCommand(left, false), new HandCommand(right, false, spring)), Dt);
            world.Step(Dt);
        }

        for (var i = 0; i < 806; i++)
        {
            Step(guard, 0f);
        }

        var inv = CultureInfo.InvariantCulture;
        Console.WriteLine(string.Create(inv, $"reach {fighter.ArmReach:F4} m, arm mass {fighter.ArmMass(Hand.Right):F3} kg, total {fighter.TotalMass:F2} kg"));
        var aim = guard + (new Vector3(0f, 0.05f, 1.6f) * fighter.ArmReach);
        var time = 0f;
        var punch = 0;
        while (time < 50.35f)
        {
            var peak = 0f;
            for (var phase = 0; phase < (int)MathF.Round(1.007f / Dt); phase++)
            {
                var extending = phase < 49;
                Step(extending ? Vector3.Lerp(guard, aim, MathF.Min(1f, phase * Dt / 0.1007f)) : guard, extending ? 1f : 0f);
                time += Dt;
                if (extending)
                {
                    peak = MathF.Max(peak, (fighter.GloveVelocity(Hand.Right) - fighter.ChestVelocity).Length());
                }
            }

            punch++;
            if (punch is 1 or 5 or 10 or 20 or 30 or 40 or 50)
            {
                var row = string.Join("  ", Enum.GetValues<MuscleRegion>().Select(r =>
                {
                    var (cap, _) = fighter.OxygenBudgetOf(r);
                    return string.Create(inv, $"{r} O2 {fighter.OxygenOf(r):F2} W {fighter.RegionWork(r):F0}J cap {cap:F0}J P {fighter.RegionPower(r):F1}W");
                }));
                Console.WriteLine(string.Create(inv, $"#{punch,2} t {time:F1} peak {peak:F3} endurance {fighter.Endurance:F3} work {fighter.WorkDone:F0} | {row}"));
            }
        }
    }
}
