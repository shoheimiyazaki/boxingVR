using Aigis.Foundation.Voxels;
using MiniRing.Weapons;

namespace MiniRing.Tests;

/// <summary>強い 1 振りが腕を落とす境の速さ（BASE_BODY_PLAN §10.7、通常の実行から外す Probe）。</summary>
[TestClass]
[TestCategory("Probe")]
public sealed class HardSlashProbe
{
    public TestContext TestContext { get; set; } = null!;

    private static WeaponOutcome Hit(float speed, int extraFrames)
    {
        using var game = WeaponDrills.StillMatch();
        for (var f = 0; f < extraFrames; f++)
        {
            game.Step(default, 1f / 60f);
        }

        var arm = game.Cpu.Parts.LeftForearm;
        var (heading, across) = WeaponDrills.AcrossArm(game, game.Cpu, arm);
        var at = game.World.GetState(arm).Position;
        var weapon = WeaponDrills.Launch(game, WeaponKind.IronSword, WeaponDrills.Edge(heading, across), WeaponDrills.EdgeMiddle, at, heading, speed);
        var trial = new WeaponTrial("x", "x", "x", _ => null!);
        return WeaponDrills.Run(trial, game, new WeaponShot(weapon, null, at, heading), 34, 1f / 60f);
    }

    [TestMethod]
    [TestCategory("Probe")]
    public void Threshold()
    {
        foreach (var speed in new[] { 18f, 19f, 20f, 20.9f, 22f, 24f, 27f })
        {
            var line = new List<string>();
            foreach (var extra in new[] { 0, 1, 3, 7 })
            {
                var o = Hit(speed, extra);
                line.Add($"+{extra}f: sev={(o.Severed ? 1 : 0)} bone={o.Lost(VoxelTissue.Bone)} flesh={o.Lost(VoxelTissue.Flesh)}");
            }

            TestContext.WriteLine($"speed {speed,5}: " + string.Join(" | ", line));
        }
    }
}
