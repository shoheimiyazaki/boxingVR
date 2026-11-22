using System.Collections.Concurrent;
using System.Globalization;
using MiniRing.Arena;
using MiniRing.Characters;

namespace MiniRing.Tests;

/// <summary>
/// 骨折の頻度の測定（2026-11-22、スタミナ・骨折の仕様）: 名簿の全組を 1 回ずつ 50.35 秒（1 ラウンド）戦わせ、折れた骨の数を数える。
/// ラウンドあたりの骨折を起こした割合（骨折が 1 本でもあったラウンドの割合）と、1 ラウンドあたりの本数・折れた部位を出す。目標は約 10 %（3〜50 %）。
/// 環境変数 FRACTURE_PAIRS=n で組の数を絞る（既定は全組）。
/// </summary>
[TestClass]
[TestCategory("Probe")]
public sealed class FractureRateProbe
{
    [TestMethod]
    public void Roster_FractureRatePerRound()
    {
        var all = CharacterRoster.All;
        var pairs = new List<(FighterCharacter Red, FighterCharacter Blue)>();
        for (var i = 0; i < all.Count; i++)
        {
            for (var j = i + 1; j < all.Count; j++)
            {
                pairs.Add((all[i], all[j]));
            }
        }

        if (int.TryParse(Environment.GetEnvironmentVariable("FRACTURE_PAIRS"), out var limit))
        {
            pairs = pairs.Take(limit).ToList();
        }

        var seconds = 50.35f;
        var results = new ConcurrentBag<(string Red, string Blue, int Broken, int RedLost, int BlueLost, bool Armed)>();
        Parallel.ForEach(pairs, new ParallelOptions { MaxDegreeOfParallelism = 12 }, pair =>
        {
            var outcome = RosterLeague.RunBout(pair.Red, pair.Blue, seed: 1, seconds, referee: true);
            results.Add((pair.Red.Id, pair.Blue.Id, outcome.RedTally.BonesBroken + outcome.BlueTally.BonesBroken, outcome.RedTally.BonesLost, outcome.BlueTally.BonesLost, pair.Red.Weapon is not null || pair.Blue.Weapon is not null || pair.Red.Wear != Weapons.HandWear.Glove || pair.Blue.Wear != Weapons.HandWear.Glove));
        });

        var rounds = results.Count;
        var withBreak = results.Count(r => r.Broken > 0);
        var total = results.Sum(r => r.Broken);
        Console.WriteLine(string.Create(CultureInfo.InvariantCulture,
            $"pairs {rounds} (one {seconds:0.0} s round each): rounds with a broken bone {withBreak} ({100.0 * withBreak / rounds:0.0} %), bones broken {total} ({(double)total / rounds:0.00} per round)"));
        foreach (var armed in new[] { false, true })
        {
            var group = results.Where(r => r.Armed == armed).ToList();
            if (group.Count > 0)
            {
                Console.WriteLine(string.Create(CultureInfo.InvariantCulture,
                    $"  {(armed ? "armed or other wear" : "glove vs glove, no weapon")}: {group.Count} rounds, with a break {100.0 * group.Count(r => r.Broken > 0) / group.Count:0.0} %, bones {(double)group.Sum(r => r.Broken) / group.Count:0.00} per round"));
            }
        }

        foreach (var r in results.Where(r => r.Broken > 0).OrderByDescending(r => r.Broken))
        {
            Console.WriteLine($"  {r.Red} vs {r.Blue}: {r.Broken} (red lost {r.RedLost}, blue lost {r.BlueLost})");
        }
    }
}
