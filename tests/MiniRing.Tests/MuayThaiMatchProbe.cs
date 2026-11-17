using System.Globalization;
using MiniRing.Bots;
using MiniRing.Characters;

namespace MiniRing.Tests;

/// <summary>調査用: ナロン（ムエタイ）の試合で、肘・蹴り・拳が何回当たるか、肘打ちを何回出すか。</summary>
[TestClass]
public sealed class MuayThaiMatchProbe
{
    private const float Dt = 1f / 240f;

    [TestMethod]
    public void CountStrikesInMatches()
    {
        var narong = CharacterRoster.Get("narong");
        foreach (var opponentId in new[] { "ramon", "diego", "akira" })
        {
            foreach (var seed in new[] { 1, 2, 3 })
            {
                var other = CharacterRoster.Get(opponentId);
                var redContender = MiniRing.Arena.RosterLeague.ContenderOf(narong, other.Id, null);
                var blueContender = MiniRing.Arena.RosterLeague.ContenderOf(other, narong.Id, null);
                var corners = MiniRing.Arena.VideoMaker.Corners(redContender, blueContender, seed, narong.Wear, other.Wear, narong.Weapon, other.Weapon,
                    s => blueContender.Create(s), s => redContender.Create(s), false);
                corners = MiniRing.Arena.VideoMaker.WithCharacters(corners, narong, other);
                using var game = new BoxingGame(seed: seed, corners: corners);
                var seen = new HashSet<HitEvent>();
                int elbows = 0, kicks = 0, punches = 0, elbowStarts = 0, theirs = 0;
                float elbowEnergy = 0f, punchEnergy = 0f, kickEnergy = 0f;
                var wasElbowing = false;
                var elbowZones = new List<string>();
                for (var i = 0; i < 240 * 40; i++)
                {
                    game.Step(default, Dt);
                    var now = (game.PlayerControl as BoxerBot)?.CurrentElbow is not null;
                    if (now && !wasElbowing)
                    {
                        elbowStarts++;
                    }

                    wasElbowing = now;
                    foreach (var hit in game.RecentHits)
                    {
                        if (!seen.Add(hit))
                        {
                            continue;
                        }

                        if (hit.Attacker != game.Player)
                        {
                            theirs++;
                            continue;
                        }

                        if (hit.Elbow)
                        {
                            elbows++;
                            elbowEnergy += hit.Energy;
                            elbowZones.Add(hit.Zone.ToString());
                        }
                        else if (hit.Kick)
                        {
                            kicks++;
                            kickEnergy += hit.Energy;
                        }
                        else
                        {
                            punches++;
                            punchEnergy += hit.Energy;
                        }
                    }
                }

                Console.WriteLine(string.Create(CultureInfo.InvariantCulture,
                    $"narong vs {opponentId} seed {seed}: elbow hits {elbows} ({elbowEnergy / Math.Max(1, elbows):F2} J avg) [{string.Join(',', elbowZones)}], kick hits {kicks} ({kickEnergy / Math.Max(1, kicks):F2} J avg), punch hits {punches} ({punchEnergy / Math.Max(1, punches):F2} J avg), opponent hits {theirs}, round {game.Round}"));
            }
        }
    }
}
