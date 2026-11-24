using MiniRing.Weapons;

namespace MiniRing.Tests;

/// <summary>調査: 実験場の「強く斬る」（投げた剣の刃・腹で前腕を横から）が、どの速さから腕を落とすか（WeaponDrills.HardSpeed を決めた。PhysicsVersion 93 で 20 m/s から）。</summary>
[TestClass]
[TestCategory("Probe")]
public sealed class HardSlashSpeedProbe
{
    private static int Flat(float v)
    {
        var trial = new WeaponTrial("f", "f", "f", g =>
        {
            var arm = g.Cpu.Parts.LeftForearm;
            var (heading, across) = WeaponDrills.AcrossArm(g, g.Cpu, arm);
            var at = g.World.GetState(arm).Position;
            return new WeaponShot(WeaponDrills.Launch(g, WeaponKind.IronSword, WeaponDrills.Flat(heading, across), WeaponDrills.FlatMiddle, at, heading, v), null, at, heading);
        });
        using var game = WeaponDrills.StillMatch();
        var shot = trial.Setup(game);
        return WeaponDrills.Run(trial, game, shot, 240, 1f / 240f).FirstContactLost;
    }

    [TestMethod]
    public void Probe_SeverSpeed()
    {
        foreach (var v in new[] { 5.7f, 8f, 10f, 12f, 15f, 20f })
        {
            var trial = new WeaponTrial("x", "x", "x", g => WeaponDrillsAccess.AtForearm(g, v));
            using var game = WeaponDrills.StillMatch();
            var shot = trial.Setup(game);
            var o = WeaponDrills.Run(trial, game, shot, 240, 1f / 240f);
            var flat = Flat(v); Console.WriteLine($"{v} m/s: flat first {flat} edge first {o.FirstContactLost} severed {o.Severed} bone {o.Lost(Aigis.Foundation.Voxels.VoxelTissue.Bone)} flesh {o.Lost(Aigis.Foundation.Voxels.VoxelTissue.Flesh)}");
        }
    }
}

internal static class WeaponDrillsAccess
{
    public static WeaponShot AtForearm(BoxingGame game, float speed)
    {
        var arm = game.Cpu.Parts.LeftForearm;
        var (heading, across) = WeaponDrills.AcrossArm(game, game.Cpu, arm);
        var at = game.World.GetState(arm).Position;
        return new WeaponShot(WeaponDrills.Launch(game, WeaponKind.IronSword, WeaponDrills.Edge(heading, across), WeaponDrills.EdgeMiddle, at, heading, speed), null, at, heading);
    }
}
