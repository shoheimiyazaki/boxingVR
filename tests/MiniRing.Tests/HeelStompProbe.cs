using System.Globalization;
using MiniRing.Bots;

namespace MiniRing.Tests;

/// <summary>調べもの: かかとの連打の間の左右の脚の酸素の蓄えと、踏み替え・1 撃のエネルギーの時間の流れ。</summary>
[TestClass]
[TestCategory("Probe")]
public sealed class HeelStompProbe
{
    private const float Dt = 1f / 240f;

    [TestMethod]
    [DataRow(1)]
    [DataRow(2)]
    public void LegOxygenAndSwitches(int seed)
    {
        var attacker = HeelStompTests.Attacker(heel: true);
        using var game = new BoxingGame(seed: seed, corners: new BoxingCorners("attacker", s => new BoxerBot(attacker, s),
            s => new BoxerBot(BotCatalog.Boxer with { Attacks = false, DefenseRate = 0f, Prediction = 0f, Aggression = 0f, FinishTempo = 0f, FinishPress = 0f, FinishSpend = 0f }, s),
            CpuStyleJson: attacker.ToJson(), Referee: false));
        var victim = game.Player;
        var (hits, energy) = (0, 0f);
        for (var t = 0f; t < 40f; t += Dt)
        {
            if (t > 2.518f)
            {
                victim.Concussion = 0.97f;
            }

            game.Step(default, Dt);
            foreach (var hit in game.RecentHits.Where(h => h.Attacker == game.Cpu && h.Victim == victim && h.Kick))
            {
                hits++;
                energy += hit.Energy;
            }

            var ground = (game.CpuControl as GroundStriker)?.Ground;
            if (((int)(t / Dt)) % 480 == 0 && ground is not null)
            {
                var c = game.Cpu;
                Console.WriteLine(string.Create(CultureInfo.InvariantCulture,
                    $"t={t,5:0.0} {ground.CurrentName,-11} foot={ground.CurrentFoot,-5} switches={ground.FootSwitches} O2 L={c.LegOxygen(Hand.Left):0.00} R={c.LegOxygen(Hand.Right):0.00} stamina={c.Stamina:0.00} work L={c.RegionWork(MuscleRegion.LeftLeg):0} R={c.RegionWork(MuscleRegion.RightLeg):0} capL={c.OxygenBudgetOf(MuscleRegion.LeftLeg).Capacity:0} armW={c.RegionWork(MuscleRegion.LeftArm):0} capA={c.OxygenBudgetOf(MuscleRegion.LeftArm).Capacity:0} hits={hits} J/hit={(hits > 0 ? energy / hits : 0f):0.0} state={c.State}"));
            }
        }
    }
}
