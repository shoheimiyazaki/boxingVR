using System.Globalization;
using MiniRing.Bots;

namespace MiniRing.Tests;

/// <summary>
/// 脳しんとうの係数の合わせ直し（2026-11-18、PhysicsVersion 45）: 拳だけのボット同士で、<see cref="FighterTuning.ConcussionGain"/> を何倍かにして、
/// 1 試合あたりのダウン・意識の最小・ダメージを数える。倍率は環境変数 CONCUSSION_SCALES（例 "1,10,30"）。直す前の版と比べるのに使う。
/// </summary>
[TestClass]
[TestCategory("Probe")]
public sealed class ConcussionCalibrationProbe
{
    private const float Dt = 1f / 60f;

    private static readonly (string Red, string Blue)[] Pairs =
        [("champion", "swarmer"), ("slugger", "counter"), ("jabber", "boxer"), ("outboxer", "champion-v1")];

    [TestMethod]
    public void DownsPerBoutByConcussionGain()
    {
        var scales = (Environment.GetEnvironmentVariable("CONCUSSION_SCALES") ?? "1,10,30,100")
            .Split(',').Select(s => float.Parse(s, CultureInfo.InvariantCulture)).ToArray();
        foreach (var scale in scales)
        {
            var tuning = FighterTuning.Default with { ConcussionGain = FighterTuning.Default.ConcussionGain * scale };
            int bouts = 0, downs = 0, kos = 0;
            double awake = 0, damage = 0;
            foreach (var (red, blue) in Pairs)
            {
                foreach (var seed in new[] { 1, 2 })
                {
                    var redStyle = BotCatalog.Style(red);
                    var corners = BotCatalog.Corners(blue) with { Player = s => new BoxerBot(redStyle, s), PlayerBody = redStyle.Build };
                    using var game = new BoxingGame(tuning, seed: seed, corners: corners);
                    var down = new bool[2];
                    var minAwake = new[] { 1f, 1f };
                    for (var t = 0f; t < 60f && !game.MatchOver; t += Dt)
                    {
                        game.Step(new BoxingInput(default), Dt);
                        var fighters = new[] { game.Player, game.Cpu };
                        for (var i = 0; i < 2; i++)
                        {
                            var now = fighters[i].IsKnockedDown;
                            downs += now && !down[i] ? 1 : 0;
                            down[i] = now;
                            minAwake[i] = MathF.Min(minAwake[i], fighters[i].Consciousness);
                        }

                        damage += game.RecentHits.Sum(h => h.Damage);
                    }

                    kos += game.MatchOver ? 1 : 0;
                    awake += minAwake.Average();
                    bouts++;
                }
            }

            Console.WriteLine(string.Create(CultureInfo.InvariantCulture,
                $"gain x{scale}: downs/bout {(double)downs / bouts:F2}, match over {kos}/{bouts}, min awake (mean) {awake / bouts:F2}, damage/bout {damage / bouts:F0}"));
        }
    }
}
