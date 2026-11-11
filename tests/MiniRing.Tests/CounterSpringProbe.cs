using MiniRing.Bots;

namespace MiniRing.Tests;

/// <summary>
/// 回避からの返しの調査用: 拳のバネを緩める（回避で動いた体の運動量が拳に乗る）と、返しの拳の速さ・運動エネルギー・当たる割合がどう変わるか。
/// </summary>
[TestClass]
public sealed class CounterSpringProbe
{
    private const float Dt = 1f / 240f;

    [TestMethod]
    public void CountersAfterEvasions_WithAndWithoutTheLooseSpring()
    {
        foreach (var (defense, punch) in new[] { (Defense.Weave, "3"), (Defense.Roll, "4"), (Defense.Slip, "2"), (Defense.Duck, "3") })
        {
            foreach (var loose in new[] { 0f, 0.5f })
            {
                var r = Run(defense, punch, loose);
                Console.WriteLine($"{defense} vs {punch}, efficiency {loose}: {r.Thrown} counters thrown, {r.Hits} landed ({r.HeadHits} on the head), " +
                    $"fist {r.Speed:0.00} m/s, {r.Energy:0.00} J per hit, {r.Damage:0} damage");
            }
        }
    }

    internal static (int Thrown, int Hits, int HeadHits, float Speed, float Energy, float Damage) Run(Defense defense, string punch, float efficiency, float seconds = 20f)
    {
        var swarmer = BotCatalog.Swarmer;
        var defender = DefenseDrill.Defender(swarmer.Defenses, defend: true, counters: true) with
        {
            GuardHeight = swarmer.GuardHeight, GuardWidth = swarmer.GuardWidth, GuardReach = swarmer.GuardReach, Prediction = 1f,
        };
        int thrown = 0, hits = 0, head = 0;
        float speed = 0f, energy = 0f, damage = 0f;
        var transferred = 0f;
        var peaks = new List<float>();
        foreach (var seed in new[] { 1, 2 })
        {
            var defenders = new List<BoxerBot>();
            var attacker = DefenseDrill.PunchAttacker(punch, telegraph: 0.08f);
            Spy? spy = null;
            var corners = new BoxingCorners(attacker.Name, s => new BoxerBot(attacker, s), s =>
            {
                var bot = new BoxerBot(defender, s, defense);
                defenders.Add(bot);
                spy = new Spy(bot);
                return spy;
            });
            var peak = new float[2];
            using var game = new BoxingGame(FighterTuning.Default with { BodyToFistEfficiency = efficiency }, seed: seed, corners: corners);
            for (var t = 0f; t < seconds; t += Dt)
            {
                game.Step(default, Dt);
                for (var h = 0; h < 2; h++)
                {
                    if (spy!.Last.For((Hand)h).Spring > 0f)
                    {
                        peak[h] = MathF.Max(peak[h], game.Player.GloveVelocity((Hand)h).Length());
                    }
                    else if (peak[h] > 0f)
                    {
                        peaks.Add(peak[h]);
                        peak[h] = 0f;
                    }
                }

                foreach (var hit in game.RecentHits.Where(h => h.Attacker == game.Player))
                {
                    hits++;
                    head += hit.Zone is HitZone.Chin or HitZone.Temple or HitZone.Head ? 1 : 0;
                    speed += hit.Speed;
                    energy += 0.5f * hit.ImpactMass * hit.Speed * hit.Speed;
                    damage += hit.Damage;
                }
            }

            thrown += defenders.Sum(b => b.Stats.CountersThrown);
            transferred += game.Player.BodyToFistEnergy;
        }

        Console.WriteLine($"  {defense} vs {punch}, efficiency {efficiency}: moved {transferred:0.000} J from the body to the fists, {transferred / Math.Max(1, thrown):0.0000} J per counter, counter fist peak speed {(peaks.Count > 0 ? peaks.Average() : 0f):0.00} m/s over {peaks.Count} punches");
        return (thrown, hits, head, hits > 0 ? speed / hits : 0f, hits > 0 ? energy / hits : 0f, damage);
    }
}

[TestClass]
public sealed class CounterHitDetailProbe
{
    [TestMethod]
    public void EachCounterHit_InTheSlipDrill()
    {
        foreach (var efficiency in new[] { 0f, 0.5f })
        {
            var swarmer = BotCatalog.Swarmer;
            var defender = DefenseDrill.Defender(swarmer.Defenses, defend: true, counters: true) with
            {
                GuardHeight = swarmer.GuardHeight, GuardWidth = swarmer.GuardWidth, GuardReach = swarmer.GuardReach, Prediction = 1f,
            };
            var attacker = DefenseDrill.PunchAttacker("2", telegraph: 0.08f);
            using var game = new BoxingGame(FighterTuning.Default with { BodyToFistEfficiency = efficiency }, seed: 1,
                corners: new BoxingCorners(attacker.Name, s => new BoxerBot(attacker, s), s => new BoxerBot(defender, s, Defense.Slip)));
            var peak = new float[2];
            var lines = new List<string>();
            for (var t = 0f; t < 12f; t += 1f / 240f)
            {
                game.Step(default, 1f / 240f);
                for (var h = 0; h < 2; h++)
                {
                    peak[h] = MathF.Max(peak[h] * MathF.Exp(-(1f / 240f) / 0.25f), game.Player.GloveVelocity((Hand)h).Length());
                }

                foreach (var hit in game.RecentHits.Where(x => x.Attacker == game.Player))
                {
                    var gap = System.Numerics.Vector3.Distance(game.Player.ChestPosition, game.Cpu.ChestPosition) / game.Player.ArmReach;
                    lines.Add($"{t:0.00}s {hit.Hand} {hit.Zone}: {hit.Speed:0.00} m/s (glove peak {peak[(int)hit.Hand]:0.00}), chest gap {gap:0.00} reach");
                }
            }

            Console.WriteLine($"efficiency {efficiency}: " + string.Join(" | ", lines.Take(14)));
        }
    }
}

[TestClass]
public sealed class CounterTraceProbe
{
    [TestMethod]
    public void OneCounter_StepByStep()
    {
        var swarmer = BotCatalog.Swarmer;
        var defender = DefenseDrill.Defender(swarmer.Defenses, defend: true, counters: true) with
        {
            GuardHeight = swarmer.GuardHeight, GuardWidth = swarmer.GuardWidth, GuardReach = swarmer.GuardReach, Prediction = 1f,
        };
        var attacker = DefenseDrill.PunchAttacker("2", telegraph: 0.08f);
        using var game = new BoxingGame(seed: 1, corners: new BoxingCorners(attacker.Name, s => new BoxerBot(attacker, s), s => new BoxerBot(defender, s, Defense.Slip)));
        var printed = 0;
        for (var t = 0f; t < 12f && printed < 60; t += 1f / 240f)
        {
            game.Step(default, 1f / 240f);
            var f = game.Player;
            for (var h = 0; h < 2; h++)
            {
                var lead = f.HandLead((Hand)h);
                if (lead.Length() > 0.002f && ((int)(t * 240f)) % 4 == 0)
                {
                    var toTarget = f.HandTargetWorld((Hand)h) - f.GloveCenter((Hand)h);
                    Console.WriteLine($"{t:0.000}s {(Hand)h}: lead {lead.Length():0.000} m, glove {f.GloveVelocity((Hand)h).Length():0.00} m/s, to target {toTarget.Length():0.000} m, trunk {(f.PelvisVelocity).Length():0.00} m/s");
                    printed++;
                }
            }
        }
    }
}

/// <summary>ボットの指令を覗く（拳のバネを使っている間を知るため）。</summary>
internal sealed class Spy(IFighterControl inner) : IFighterControl
{
    public FighterCommand Last { get; private set; }

    public FighterCommand Read(in FighterSense sense, float dt) => Last = inner.Read(sense, dt);

    public void OnPunchLanded(Hand hand, float damage) => inner.OnPunchLanded(hand, damage);

    public void OnPunchTaken(in HitEvent hit) => inner.OnPunchTaken(hit);
}
