using MiniRing.Bots;

namespace MiniRing.Tests;

/// <summary>ハイブリッドの試合の始めに、打たれないのにダウンする原因調べ（BASE_BODY_PLAN §10.7、通常の実行から外す Probe）。</summary>
[TestClass]
[TestCategory("Probe")]
public sealed class HybridDownProbe
{
    [TestMethod]
    [TestCategory("Probe")]
    public void WhoGoesDown()
    {
        foreach (var seed in new[] { 1, 2, 3, 4 })
        {
            var corners = new BoxingCorners("hybrid", s => BotCatalog.Create("swarmer", s), s => BotCatalog.Create("champion", s), VoxelGloves: true);
            using var game = new BoxingGame(FighterTuning.HybridPunch, seed, corners);
            for (var frame = 1; frame <= 180; frame++)
            {
                game.Step(default, 1f / 60f);
                if (game.Player.State != FighterState.Standing || game.Cpu.State != FighterState.Standing)
                {
                    Console.WriteLine($"DBG seed {seed} t={frame / 60f:0.00} player {game.Player.State} cause {game.Player.LastFallCause} cpu {game.Cpu.State} cause {game.Cpu.LastFallCause} hits {game.RecentHits.Count} pelvisY P {game.Player.PelvisPosition.Y:0.00} C {game.Cpu.PelvisPosition.Y:0.00}");
                    break;
                }
            }
        }
    }

    [TestMethod]
    [TestCategory("Probe")]
    public void Trace()
    {
        BoxerBot? cpuBot = null;
        var corners = new BoxingCorners("hybrid", s => BotCatalog.Create("swarmer", s), s => cpuBot = (BoxerBot)BotCatalog.Create("champion", s), VoxelGloves: true);
        using var game = new BoxingGame(FighterTuning.HybridPunch, 1, corners);
        for (var frame = 1; frame <= 80; frame++)
        {
            game.Step(default, 1f / 60f);
            if (frame % 5 != 0)
            {
                continue;
            }

            foreach (var (name, f) in new[] { ("P", game.Player), ("C", game.Cpu) })
            {
                var o = game.World.GetState(f.Parts.Pelvis).Orientation;
                var up = System.Numerics.Vector3.Transform(System.Numerics.Vector3.UnitY, o);
                var c = game.World.GetState(f.Parts.Chest).Orientation;
                var cup = System.Numerics.Vector3.Transform(System.Numerics.Vector3.UnitY, c);
                var fw = f.FootworkState;
                var com = f.CenterOfMass;
                var pv = f.PelvisPosition;
                Console.WriteLine($"DBG t={frame / 60f:0.00} {name} {f.State} pelvisY {pv.Y:0.00} tilt pelvis {MathF.Acos(up.Y) * 57.3f:0.0} chest {MathF.Acos(cup.Y) * 57.3f:0.0} com-pelvis {(com.X - pv.X) * 100:0.0},{(com.Z - pv.Z) * 100:0.0} steps {fw.Steps} swing {fw.Swinging} hull {f.SupportHull.Count} def {(name == "C" ? cpuBot!.CurrentDefense.ToString() : "")}");
            }
        }
    }
}
