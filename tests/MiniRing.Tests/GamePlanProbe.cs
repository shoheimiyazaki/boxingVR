using System.Globalization;
using MiniRing.Bots;
using MiniRing.Characters;

namespace MiniRing.Tests;

/// <summary>
/// 調査用: 名簿の組ごとの戦略の層の答え（元気な両者・試合の始め）。好む距離 d* [cm]・d* / 自分の腕・手数の倍率と、今の普段の間合い（NeutralRange × 腕）を並べる
/// （`--filter "FullyQualifiedName~GamePlanProbe" --logger "console;verbosity=detailed"`）。
/// </summary>
[TestClass]
[TestCategory("Probe")]
public sealed class GamePlanProbe
{
    [TestMethod]
    public void PrintPreferredDistances()
    {
        var inv = CultureInfo.InvariantCulture;
        var fresh = new PlanInputs(0f, 0f, 0f, 1f, 0f, 0f, default);
        foreach (var me in CharacterRoster.All)
        {
            using var probe = new BoxingGame(corners: BotCatalog.Corners(BotCatalog.Boxer) with { CpuBody = me.Build });
            var reach = probe.Cpu.ArmReach;
            var alone = GamePlan.Decide(me.Style.GamePlan!, fresh)!.Value;
            var line = string.Create(inv, $"{me.Id,-9} reach {reach * 100f:0.0} cm, neutral {me.Style.NeutralRange * reach * 100f:0.0} cm, alone d* {alone.Distance * 100f:0.0} cm ({alone.Distance / reach:0.00})  vs:");
            foreach (var foe in CharacterRoster.All.Where(c => c.Id != me.Id))
            {
                var c = GamePlan.Decide(me.Resolve(foe).GamePlan!, fresh)!.Value;
                line += string.Create(inv, $" {foe.Id}={c.Distance * 100f:0}/{c.Tempo:0.00}");
            }

            Console.WriteLine(line);
        }
    }
}
