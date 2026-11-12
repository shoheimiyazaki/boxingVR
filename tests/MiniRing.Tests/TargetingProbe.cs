using MiniRing.Bots;

namespace MiniRing.Tests;

/// <summary>調査用: 狙いどころの重みで、当たる場所がどう変わるか（通常の実行から外す: 名前に Probe）。</summary>
[TestClass]
public sealed class TargetingProbe
{
    [TestMethod]
    public void WhereTheAimPointsAre_Probe()
    {
        using var game = new BoxingGame(seed: 1);
        for (var i = 0; i < 60; i++)
        {
            game.Step(default, 1f / 60f);
        }

        var cpu = game.Cpu;
        var chest = game.World.GetState(cpu.Parts.Chest);
        var from = game.Player.HeadPosition;
        System.Numerics.Vector3 Local(System.Numerics.Vector3 w) => System.Numerics.Vector3.Transform(w - chest.Position, System.Numerics.Quaternion.Conjugate(chest.Orientation)) / cpu.HalfExtents.Chest;
        foreach (var goal in new[] { AimGoal.Liver, AimGoal.SolarPlexus, AimGoal.Heart, AimGoal.Chin })
        {
            var p = cpu.AsTarget.Point(goal, from)!.Value;
            var natural = goal == AimGoal.Chin ? cpu.HeadPosition : cpu.ChestPosition;
            var c = Targeting.Clamp(natural, p, from, game.Player.ArmReach);
            Console.WriteLine($"{goal}: point (chest-normalized) {Local(p)}, clamped {Local(c)}, shift {System.Numerics.Vector3.Distance(p, natural) / game.Player.ArmReach:0.00} reach");
        }

        Console.WriteLine($"pelvis (chest-normalized) {Local(cpu.PelvisPosition)}; chest half {cpu.HalfExtents.Chest}");
    }

    [TestMethod]
    public void WhereTheBlowsLand_Probe()
    {
        var swarmer = BotCatalog.Style("swarmer");
        foreach (var (name, aim) in new[]
                 {
                     ("none", (AimWeights?)null),
                     ("liver", new AimWeights(Liver: 6f, SolarPlexus: 1f)),
                     ("head", new AimWeights(Chin: 3f, Temple: 3f, ExposedBrain: 6f)),
                 })
        {
            var zones = new Dictionary<HitZone, int>();
            var damage = 0f;
            var liverBruise = 0f;
            for (var seed = 1; seed <= 3; seed++)
            {
                var style = swarmer with { Aim = aim };
                var still = BotCatalog.Boxer with { Attacks = false, DefenseRate = 0f, Prediction = 0f, Aggression = 0f };
                using var game = new BoxingGame(seed: seed, corners: new BoxingCorners("still", s => new BoxerBot(still, s), s => new BoxerBot(style, s)));
                for (var t = 0f; t < 20f; t += 1f / 60f)
                {
                    game.Step(default, 1f / 60f);
                    foreach (var hit in game.RecentHits)
                    {
                        if (hit.Attacker == game.Player)
                        {
                            zones[hit.Zone] = zones.GetValueOrDefault(hit.Zone) + 1;
                            damage += hit.Damage;
                        }
                    }

                    liverBruise = MathF.Max(liverBruise, game.Cpu.Voxels!.BruiseOf(MiniRing.Voxels.BoxerAnatomy.Liver));
                }
            }

            var total = zones.Values.Sum();
            Console.WriteLine($"{name}: {total} hits, damage {damage:0}, max liver bruise {liverBruise:0.00} | "
                + string.Join(" ", zones.OrderByDescending(z => z.Value).Select(z => $"{HitZones.Label(z.Key)} {z.Value * 100 / Math.Max(1, total)}%")));
        }
    }
}
