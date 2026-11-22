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
    public void Roster_FractureRatePerRound_ByBoneStrength()
    {
        var all = CharacterRoster.All;
        var jobs = new List<(float Scale, FighterCharacter Red, FighterCharacter Blue)>();
        foreach (var scale in new[] { 1.5f, 2f, 3f, 4f })
        {
            for (var i = 0; i < all.Count; i++)
            {
                for (var j = i + 1; j < all.Count; j++)
                {
                    jobs.Add((scale, all[i], all[j]));
                }
            }
        }

        var results = new ConcurrentBag<(float Scale, int Broken, bool Armed)>();
        Parallel.ForEach(jobs, new ParallelOptions { MaxDegreeOfParallelism = 24 }, job =>
        {
            BoxingGame.LimbBendProbeScale = job.Scale;
            var outcome = RosterLeague.RunBout(job.Red, job.Blue, seed: 1, 50.35f, referee: true);
            results.Add((job.Scale, outcome.RedTally.BonesBroken + outcome.BlueTally.BonesBroken, job.Red.Weapon is not null || job.Blue.Weapon is not null || job.Red.Wear != Weapons.HandWear.Glove || job.Blue.Wear != Weapons.HandWear.Glove));
        });
        foreach (var scale in results.Select(r => r.Scale).Distinct().OrderBy(x => x))
        {
            var all1 = results.Where(r => r.Scale == scale).ToList();
            var glove = all1.Where(r => !r.Armed).ToList();
            Console.WriteLine(string.Create(CultureInfo.InvariantCulture,
                $"bone strength x{scale}: rounds with a break {100.0 * all1.Count(r => r.Broken > 0) / all1.Count:0.0} % ({all1.Count} rounds, {(double)all1.Sum(r => r.Broken) / all1.Count:0.00} bones/round); glove-only {100.0 * glove.Count(r => r.Broken > 0) / glove.Count:0.0} % ({glove.Count} rounds)"));
        }
    }

    [TestMethod]
    public void GlovedRoster_FractureRatePerRound_TenSeeds()
    {
        var gloved = CharacterRoster.All.Where(c => c.Weapon is null && c.Wear == Weapons.HandWear.Glove).ToList();
        var jobs = new List<(FighterCharacter Red, FighterCharacter Blue, int Seed)>();
        for (var i = 0; i < gloved.Count; i++)
        {
            for (var j = i + 1; j < gloved.Count; j++)
            {
                for (var seed = 1; seed <= 10; seed++)
                {
                    jobs.Add((gloved[i], gloved[j], seed));
                }
            }
        }

        var results = new ConcurrentBag<int>();
        Parallel.ForEach(jobs, new ParallelOptions { MaxDegreeOfParallelism = 12 }, job =>
        {
            var outcome = RosterLeague.RunBout(job.Red, job.Blue, job.Seed, 50.35f, referee: true);
            results.Add(outcome.RedTally.BonesBroken + outcome.BlueTally.BonesBroken);
        });
        Console.WriteLine(string.Create(CultureInfo.InvariantCulture,
            $"gloved roster ({string.Join(",", gloved.Select(c => c.Id))}): {results.Count} rounds, with a broken bone {100.0 * results.Count(b => b > 0) / results.Count:0.0} %, {(double)results.Sum() / results.Count:0.00} bones/round"));
    }

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
