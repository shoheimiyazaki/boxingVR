using System.Globalization;
using MiniRing.Bots;
using MiniRing.Characters;

namespace MiniRing.Tests;

/// <summary>
/// K.O. の早さの調べ（2026-11-17、ユーザーの問い: ザ・ハンマーの試合が数秒で終わる）。名簿のボット同士を種ごとに回し、
/// 最初のダウンと K.O. の時刻、最初のダウンの前 1.5 秒の打撃（部位・速さ・ダメージ・脳しんとうの増え方）を出す。
/// 古い版と比べるため、使う API は 2026-11-16 の版にもあるものだけにしている。
/// </summary>
[TestClass]
[TestCategory("Probe")]
public sealed class KoTimingProbe
{
    private const float Dt = 1f / 60f;

    private static readonly (string Red, string Blue)[] Pairs =
        [("marcus", "ramon"), ("marcus", "dmitri"), ("marcus", "yoandry"), ("dmitri", "ramon"), ("yoandry", "gaston")];

    [TestMethod]
    public void FirstDownAndKoTimes()
    {
        var seeds = Enumerable.Range(1, 6).ToArray();
        foreach (var (redId, blueId) in Pairs)
        {
            var red = CharacterRoster.Get(redId);
            var blue = CharacterRoster.Get(blueId);
            var redStyle = red.Resolve(blue.Id);
            var blueStyle = blue.Resolve(red.Id);
            var downs = new List<float>();
            var kos = new List<float>();
            foreach (var seed in seeds)
            {
                var corners = new BoxingCorners(blueStyle.Name, s => new BoxerBot(blueStyle, s), s => new BoxerBot(redStyle, s),
                    PlayerWear: red.Wear, CpuWear: blue.Wear, PlayerBody: red.Build, CpuBody: blue.Build);
                using var game = new BoxingGame(seed: seed, corners: corners);
                var recent = new List<(float T, string Text)>();
                float? firstDown = null;
                float? ko = null;
                var wins = (game.PlayerWins, game.CpuWins);
                for (var step = 1; step <= 60 * 60 && ko is null; step++)
                {
                    var t = step * Dt;
                    var before = (game.Player.Concussion, game.Cpu.Concussion);
                    game.Step(default, Dt);
                    foreach (var hit in game.RecentHits)
                    {
                        var c0 = hit.Victim == game.Player ? before.Item1 : before.Item2;
                        recent.Add((t, string.Create(CultureInfo.InvariantCulture,
                            $"{t,5:0.00}s {(hit.Attacker == game.Player ? redId : blueId)} {hit.Hand} -> {hit.Zone} v {hit.Speed:0.0} m/s E {hit.Energy:0.0} J dmg {hit.Damage:0} conc {c0:0.00}->{hit.Victim.Concussion:0.00}")));
                    }

                    recent.RemoveAll(r => r.T < t - 1.5f);
                    if (firstDown is null && (game.Player.State != FighterState.Standing || game.Cpu.State != FighterState.Standing))
                    {
                        firstDown = t;
                        Console.WriteLine($"  {redId} vs {blueId} seed {seed}: first down at {t:0.0}s ({(game.Player.State != FighterState.Standing ? redId : blueId)})");
                        foreach (var (_, text) in recent)
                        {
                            Console.WriteLine("     " + text);
                        }
                    }

                    if ((game.PlayerWins, game.CpuWins) != wins)
                    {
                        ko = t;
                    }
                }

                downs.Add(firstDown ?? 60f);
                kos.Add(ko ?? 60f);
            }

            downs.Sort();
            kos.Sort();
            Console.WriteLine(string.Create(CultureInfo.InvariantCulture,
                $"== {redId} vs {blueId}: first down median {downs[downs.Count / 2]:0.0}s [{string.Join(", ", downs.Select(d => d.ToString("0", CultureInfo.InvariantCulture)))}], KO median {kos[kos.Count / 2]:0.0}s [{string.Join(", ", kos.Select(d => d.ToString("0", CultureInfo.InvariantCulture)))}]"));
        }
    }
}
