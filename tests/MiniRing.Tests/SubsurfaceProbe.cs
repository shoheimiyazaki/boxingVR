using MiniRing.Bots;
using MiniRing.Voxels;
using MiniRing.Weapons;

namespace MiniRing.Tests;

/// <summary>調べもの: グローブの試合で、皮膚の下で潰れたセル・面で壊れたセルを、部位ごと・材質ごとに数える。</summary>
[TestClass]
public sealed class SubsurfaceProbe
{
    public TestContext TestContext { get; set; } = null!;

    [TestMethod]
    public void GloveMatchDamage()
    {
        foreach (var seed in new[] { 1, 2, 3 })
        {
            using var game = new BoxingGame(seed: seed, corners: new BoxingCorners("slugger", s => BotCatalog.Create("slugger", s), s => BotCatalog.Create("champion", s), PlayerWear: HandWear.Glove, CpuWear: HandWear.Glove));
            int crushed = 0, removed = 0, punches = 0, crushingPunches = 0;
            var byMaterial = new Dictionary<byte, int>();
            var byPart = new Dictionary<BodyPartKind, int>();
            for (var t = 0f; t < 45f && !game.RoundOver; t += 1f / 60f)
            {
                game.Step(default, 1f / 60f);
                foreach (var c in game.RecentCuts.Where(c => c.FaceB is not (BoxerAnatomy.RedGlove or BoxerAnatomy.BlueGlove or BoxerAnatomy.Padding)))
                {
                    var n = c.CrushedFor(c.B).Count;
                    crushed += n;
                    removed += c.RemovedB.Count;
                    punches += c.ApproachSpeed > 1f ? 1 : 0;
                    crushingPunches += n > 0 ? 1 : 0;
                    foreach (var v in c.CrushedFor(c.B))
                    {
                        byMaterial[v.Material] = byMaterial.GetValueOrDefault(v.Material) + 1;
                    }

                    if (n > 0 && new[] { game.Player, game.Cpu }.Select(f => f.Voxels!.Parts.FirstOrDefault(p => p.Body == c.B)).FirstOrDefault(p => p is not null) is { } part)
                    {
                        byPart[part.Kind] = byPart.GetValueOrDefault(part.Kind) + n;
                    }
                }
            }

            TestContext.WriteLine($"seed {seed}: crushed beneath {crushed} in {crushingPunches} contacts (of {punches} faster than 1 m/s), broken at the face {removed}; " +
                $"materials {string.Join(", ", byMaterial.Select(kv => $"{kv.Key}:{kv.Value}"))}; parts {string.Join(", ", byPart.Select(kv => $"{kv.Key}:{kv.Value}"))}; " +
                $"muscle red {game.Player.Voxels!.Muscle(game.Player.Parts.Chest):F2} blue {game.Cpu.Voxels!.Muscle(game.Cpu.Parts.Chest):F2}");
        }
    }
}
