using System.Globalization;
using MiniRing.Bots;

namespace MiniRing.Tests;

/// <summary>調べもの: 打ち返さない受け手（構えて守るだけ）から攻め手へ数えられた接触の中身（CmaTuneTests の打ち返しのダメージ）。</summary>
[TestClass]
[TestCategory("Probe")]
public sealed class PassiveContactProbe
{
    [TestMethod]
    public void PassiveDefender_ScoredContacts()
    {
        var defender = DefenseDrill.Defender(BotCatalog.Champion.Defenses, defend: true, DefenseDrill.DefaultRange, counters: false);
        var total = 0f;
        void Observe(BoxingGame game, BoxerBot _)
        {
            foreach (var hit in game.RecentHits)
            {
                if (hit.Attacker == game.Cpu)
                {
                    continue;
                }

                total += hit.Damage;
                var glove = hit.Attacker.GloveVelocity(hit.Hand);
                var victimChest = hit.Victim.ChestVelocity;
                Console.WriteLine(string.Create(CultureInfo.InvariantCulture,
                    $"{hit.Zone} hand {hit.Hand} kick {hit.Kick} elbow {hit.Elbow} speed {hit.Speed:F2} mass {hit.ImpactMass:F2} body {hit.BodyShare:F2} dmg {hit.Damage:F2} | glove v {glove.Length():F2} attacker-chest v {hit.Attacker.ChestVelocity.Length():F2} victim-chest v {victimChest.Length():F2}"));
            }
        }

        var r = DefenseDrill.Punch("2", Defense.Slip, defender, seed: 6, seconds: 14f, cue: 0.04f, observe: Observe);
        Console.WriteLine($"total {total:F1}; {r}");
    }
}
