using MiniRing.Bots;

namespace MiniRing.Tests;

/// <summary>調査用: スタミナの減り方・戻り方（通常の実行から外す: 名前に Probe）。</summary>
[TestClass]
[TestCategory("Probe")]
public sealed class StaminaProbe
{
    [TestMethod]
    public void BodyBlowOnTheOrgans_Probe()
    {
        var still = BotCatalog.Boxer with { Attacks = false, DefenseRate = 0f, Prediction = 0f, Aggression = 0f };
        foreach (var (name, side, height) in new[] { ("chest centre", 0f, 0.3f), ("left chest (heart)", 0.5f, 0.3f), ("right low chest (liver)", -0.6f, -0.5f), ("solar plexus", 0f, -0.6f), ("left low chest", 0.6f, -0.5f), ("left bottom rib", 0.6f, -0.9f), ("right bottom rib", -0.6f, -0.9f), ("belly", 0f, -1.1f) })
        {
            using var game = new BoxingGame(seed: 1, corners: new BoxingCorners("still", s => new BoxerBot(still, s), s => new BoxerBot(still, s)));
            for (var t = 0f; t < 2.518f; t += 1f / 240f) { game.Step(default, 1f / 240f); }
            var f = game.Cpu;
            var half = f.HalfExtents.Chest;
            var frame = f.Frame;
            var right = System.Numerics.Vector3.Normalize(System.Numerics.Vector3.Cross(frame.Forward, System.Numerics.Vector3.UnitY));
            var point = f.ChestPosition + (frame.Forward * half.Z) + (right * (side * half.X)) + (System.Numerics.Vector3.UnitY * (height * half.Y));
            var mass = 45f;
            var speed = 3.693f;
            var damage = PunchMechanics.Damage(mass * speed) * PunchMechanics.Sharpness(speed);
            game.ApplyHit(new HitEvent(game.Player, Hand.Right, f, HitZone.Body, point, speed, mass, 0.7f, damage), f.Parts.Chest, -frame.Forward * speed);
            var v = f.Voxels!;
            var organs = string.Join(" ", MiniRing.Voxels.BoxerBody.TrunkOrgans.Select(o => $"{MiniRing.Voxels.BoxerAnatomy.Palette[o].Name}:{v.BruiseOf(o):0.00}/{v.OrganLoss(o):P0}"));
            var line = $"{name}: {organs} | stamina {f.Stamina:0.00} core {f.Core:0.00} breath {f.Breath:0.00} tone {f.Tone:0.00}";
            var down = false;
            for (var t = 0f; t < 5.035f; t += 1f / 240f) { game.Step(default, 1f / 240f); down |= f.State != FighterState.Standing; }
            Console.WriteLine($"{line} → after 5 s down={down} stamina {f.Stamina:0.00} core {f.Core:0.00}");
        }
    }

    [TestMethod]
    public void StaminaOverABoutAndARest_Probe()
    {
        var t0 = FighterTuning.Default;
        foreach (var (name, tuning) in new[] { ("A cap60 w.15", t0 with { OxygenStorePerKg = 202.8f, VesselPowerPerKg = 3.189f, WindPerBruise = 507.0f }), ("B cap40 w.1", t0 with { OxygenStorePerKg = 135.2f, VesselPowerPerKg = 2.115f, WindPerBruise = 338.0f }), ("C cap60 w.25", t0 with { OxygenStorePerKg = 169.0f * 1.2f, VesselPowerPerKg = 3.021f * 1.05f, WindPerBruise = 845.1f }), ("D cap30 w.15", t0 with { OxygenStorePerKg = 84.51f * 1.2f, VesselPowerPerKg = 1.511f * 1.05f, WindPerBruise = 4.225f }) })
        {
            var line = $"{name}:";
            foreach (var (red, blue) in new[] { ("champion", "swarmer"), ("slugger", "jabber") })
            {
                using var game = new BoxingGame(tuning, seed: 5, new BoxingCorners(blue, s => BotCatalog.Create(blue, s), s => BotCatalog.Create(red, s)));
                for (var t = 0f; t <= 50.37f; t += 1f / 60f)
                {
                    game.Step(default, 1f / 60f);
                }

                line += $" {red}/{blue} @50s {game.Player.Stamina:0.00}/{game.Cpu.Stamina:0.00}";
            }

            var still = BotCatalog.Boxer with { Attacks = false, DefenseRate = 0f, Prediction = 0f, Aggression = 0f };
            using var rest = new BoxingGame(tuning, seed: 1, new BoxingCorners("still", s => new BoxerBot(still, s), s => new BoxerBot(still, s)));
            for (var t = 0f; t < 1.678f; t += 1f / 60f) { rest.Step(default, 1f / 60f); }
            var start = rest.Cpu.Stamina;
            for (var t = 0f; t < 16.78f; t += 1f / 60f) { rest.Step(default, 1f / 60f); }
            Console.WriteLine($"{line} | still 16.8 s: {start:0.00} → {rest.Cpu.Stamina:0.00}");
        }
    }
}
