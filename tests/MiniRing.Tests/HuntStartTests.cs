using MiniRing.Arena;
using MiniRing.Characters;
using MiniRing.Voxels;
using MiniRing.Weapons;

namespace MiniRing.Tests;

/// <summary>狩りのドリルの受け手の臓器が、試合の時計が動く前（助走の間）に削れていないか（2026-11-25）。</summary>
[TestClass]
[TestCategory("slow")]
public sealed class HuntStartTests
{
    [TestMethod]
    public void Victims_StartTheClockWithEveryOrganWhole()
    {
        foreach (var id in new[] { "ramon", "sean" })
        {
            var hunter = CharacterRoster.Get("galva");
            var victim = CharacterRoster.Get(id);
            var style = HuntLab.Style("galva", id, null);
            var red = HuntLab.Victim;
            var blue = HuntLab.HunterOf(style);
            var corners = VideoMaker.Corners(red, blue, 1, HandWear.None, hunter.Wear, null, hunter.Weapon, s => blue.Create(s), s => red.Create(s), referee: false);
            corners = HuntLab.EditCorners(TvShowRules.Stage(VideoMaker.WithCharacters(corners, victim, hunter), Organizations.Get("vfc")));
            using var game = new BoxingGame(seed: 1, corners: corners, rules: HuntLab.Rules(5f));
            for (var step = 0; step < 1; step++)
            {
                var body = game.Player.Voxels!;
                var palette = BoxerAnatomy.Palette;
                var line = $"{id} step {step} dead={game.Player.DeathCause}: ";
                for (var m = 1; m < palette.Count; m++)
                {
                    if (!palette[(byte)m].Vital)
                    {
                        continue;
                    }

                    var left = body.Parts.Sum(p => p.Body.Grid.Remaining((byte)m));
                    var start = body.Parts.Sum(p => p.Body.Grid.Initial((byte)m));
                    line += $"{palette[(byte)m].Name} {left}/{start} ";
                }

                Console.WriteLine(line);
                Assert.IsNull(game.Player.DeathCause, line);
                for (var m = 1; m < palette.Count; m++)
                {
                    if (palette[(byte)m].Vital)
                    {
                        Assert.AreEqual(body.Parts.Sum(p => p.Body.Grid.Initial((byte)m)), body.Parts.Sum(p => p.Body.Grid.Remaining((byte)m)), line);
                    }
                }
                game.Step(default, HuntLab.StepDt);
            }
        }
    }
}
