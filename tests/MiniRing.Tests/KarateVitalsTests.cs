using System.Numerics;
using MiniRing.Arena;
using MiniRing.Bots;
using MiniRing.Voxels;
using MiniRing.Weapons;

namespace MiniRing.Tests;

[TestClass]
[TestCategory("slow")]
public sealed class KarateVitalsTests
{
    private static readonly int[] OnePerOrgan = [1, 1, 1, 1];
    [TestMethod]
    public void Meter_SeparatesLocalEyeAndRemoteDamage_AndTracksPartialHealth()
    {
        using var game = new BoxingGame(seed: 1);
        var victim = game.Player;
        var meter = new KarateVitals(victim, AimGoal.Eye, game.Cpu.ShoulderCenter);
        var head = victim.Voxels!.Face!; // 目は顔の層（FaceLayer）
        var grid = head.Body.Grid;
        var aim = victim.AsTarget.Point(AimGoal.Eye, game.Cpu.ShoulderCenter)!.Value;
        var eye = Enumerable.Range(0, grid.CellCount)
            .Where(i => grid.MaterialAt(i) is BoxerAnatomy.Eye or BoxerAnatomy.Pupil)
            .MinBy(i =>
            {
                var (x, y, z) = grid.Coordinates(i);
                return Vector3.DistanceSquared(aim, Vector3.Transform(grid.CellCenter(x, y, z), head.Body.GridToWorld()));
            });
        Assert.AreEqual(0, meter.Read().Total);
        var state = grid.CellState(eye);
        grid.SetCellState(eye, state with { Health = state.Health * 0.5f });
        var partial = meter.Read();
        Assert.AreEqual(0, partial.Eye);
        Assert.IsLessThan(1f, partial.Vision.Binocular);
        grid.SetCellState(eye, (0, 0f, false, 0f));
        var armBody = victim.Voxels.PartOf(victim.ForearmId(Hand.Left))!.Body;
        var arm = armBody.Grid;
        var remote = Enumerable.Range(0, arm.CellCount).First(i => arm.MaterialAt(i) != 0);
        arm.SetCellState(remote, (0, 0f, false, 0f));
        var (ex, ey, ez) = grid.Coordinates(eye);
        meter.Observe([new(true, head.Body, armBody, [new(grid.CellCenter(ex, ey, ez), state.Material, false)], [],
            Vector3.Zero, Vector3.UnitZ, Vector3.UnitZ, 0f, 0f, 0f, false, null, null)]);
        var result = meter.Read();
        Assert.AreEqual(1, result.Eye);
        Assert.AreEqual(1, result.Near);
        Assert.AreEqual(1, result.Outside);
        Assert.AreEqual(2, result.Total);
        Assert.AreEqual(0, result.Brain);
        CollectionAssert.AreEqual(new int[4], result.Organs);
    }

    [TestMethod]
    public void Meter_ReportsEachOrganAndBrainSeparately()
    {
        using var game = new BoxingGame(seed: 1);
        var victim = game.Player;
        var meter = new KarateVitals(victim, AimGoal.Liver, game.Cpu.ShoulderCenter);
        foreach (var material in BoxerBody.TrunkOrgans.Append(BoxerAnatomy.Brain))
        {
            var part = victim.Voxels!.Layers.First(p => p.Body.Grid.Remaining(material) > 0);
            var grid = part.Body.Grid;
            var cell = Enumerable.Range(0, grid.CellCount).First(i => grid.MaterialAt(i) == material);
            grid.SetCellState(cell, (0, 0f, false, 0f));
        }

        var reading = meter.Read();
        Assert.AreEqual(1, reading.Brain);
        CollectionAssert.AreEqual(OnePerOrgan, reading.Organs);
        Assert.AreEqual(5, reading.Total);
        Assert.AreEqual(reading.Total, reading.Near + reading.Outside);
    }

    [TestMethod]
    public void Meter_DoesNotCountShiftedTissueAsDestroyed()
    {
        using var game = new BoxingGame(seed: 1);
        var meter = new KarateVitals(game.Player, AimGoal.Eye, game.Cpu.ShoulderCenter);
        foreach (var material in BoxerBody.TrunkOrgans.Append(BoxerAnatomy.Brain).Append(BoxerAnatomy.Eye))
        {
            var grid = game.Player.Voxels!.Layers.First(p => p.Body.Grid.Remaining(material) > 0).Body.Grid;
            var source = Enumerable.Range(0, grid.CellCount).First(i => grid.MaterialAt(i) == material);
            var empty = Enumerable.Range(0, grid.CellCount).First(i => grid.MaterialAt(i) == 0);
            Assert.IsTrue(grid.Shift(source, empty));
        }

        var reading = meter.Read();
        Assert.AreEqual(0, reading.Total);
        Assert.AreEqual(0, reading.Eye);
        Assert.AreEqual(0, reading.Brain);
        CollectionAssert.AreEqual(new int[4], reading.Organs);
    }

    [TestMethod]
    public void ForcedAim_DoesNotMixCenterShotsIntoVitalDrill()
    {
        var style = BotCatalog.Karate with
        {
            KickRate = 0f,
            ComboWeights = new Dictionary<string, float> { ["1"] = 1f, ["2"] = 1f },
        };
        BoxerBot? bot = null;
        using var game = new BoxingGame(seed: 1, corners: new BoxingCorners("karate-vitals",
            s => bot = new BoxerBot(style, s, forcedAim: AimGoal.Liver), _ => new StandGuard(),
            PlayerWear: HandWear.None, CpuWear: HandWear.None));
        for (var i = 0; i < 12 * 240; i++)
        {
            game.Step(default, 1f / 240f);
        }

        var aimed = bot!.Stats.Aims.GetValueOrDefault(AimGoal.Liver);
        Assert.IsGreaterThan(0, aimed);
        Assert.AreEqual(bot.Stats.Techniques.Values.Sum(c => c.Thrown), aimed);
        Assert.AreEqual(1, bot.Stats.Aims.Count);
    }
}
