using MiniRing.Bots;

namespace MiniRing.Tests;

/// <summary>調査用: ボクセルのグローブとこれまでの拳（箱の当たり + エネルギーで掘る）を、同じボット同士の試合で比べる。</summary>
[TestClass]
public sealed class GloveProbe
{
    [TestMethod]
    [TestCategory("Probe")]
    public void VoxelGlovesVersusOldFists()
    {
        foreach (var voxel in new[] { false, true })
        {
            var watch = System.Diagnostics.Stopwatch.StartNew();
            int hits = 0, downs = 0, lost = 0;
            float damage = 0f, twist = 0f;
            foreach (var seed in new[] { 1, 2 })
            {
                using var game = new BoxingGame(seed: seed, corners: new BoxingCorners("x", s => BotCatalog.Create("swarmer", s), s => BotCatalog.Create("champion", s), VoxelGloves: voxel));
                var start = game.Player.Voxels!.Parts.Sum(p => p.Body.Grid.FilledCount) + game.Cpu.Voxels!.Parts.Sum(p => p.Body.Grid.FilledCount);
                var wasDown = (false, false);
                for (var i = 0; i < 60 * 30; i++)
                {
                    game.Step(default, 1f / 60f);
                    hits += game.RecentHits.Count;
                    damage += game.RecentHits.Sum(h => h.Damage);
                    twist = MathF.Max(twist, MathF.Max(game.Player.LastHeadTwist, game.Cpu.LastHeadTwist));
                    var down = (game.Player.State != FighterState.Standing, game.Cpu.State != FighterState.Standing);
                    downs += (down.Item1 && !wasDown.Item1 ? 1 : 0) + (down.Item2 && !wasDown.Item2 ? 1 : 0);
                    wasDown = down;
                }

                lost += start - (game.Player.Voxels!.Parts.Sum(p => p.Body.Grid.FilledCount) + game.Cpu.Voxels!.Parts.Sum(p => p.Body.Grid.FilledCount));
            }

            Console.WriteLine($"voxel {voxel}: hits {hits} damage {damage:0} downs {downs} voxels lost {lost} max head twist {twist:0} time {watch.Elapsed.TotalSeconds:0}s");
        }
    }

    [TestMethod]
    [TestCategory("Probe")]
    public void WhoCutsTheSwordsman()
    {
        var basic = MiniRing.Weapons.SwordTechnique.Basics.First(t => t.Name == "kesa");
        var through = basic with { Swing = basic.Swing with { StrikeFist = basic.Swing.StrikeFist + new System.Numerics.Vector3(0.1f, -0.2f, 0.35f), StrikeTime = 0.1f } };
        Dictionary<BodyPartKind, int>? before = null;
        MiniRing.Weapons.SwordDrill.Run(through, (game, t, swinging) =>
        {
            var now = game.Player.Voxels!.Parts.GroupBy(p => p.Kind).ToDictionary(g => g.Key, g => g.Sum(p => p.Body.Grid.FilledCount));
            if (before is not null)
            {
                foreach (var (k, v) in now.Where(kv => kv.Value < before.GetValueOrDefault(kv.Key)))
                {
                    var cuts = string.Join(";", game.RecentCuts.Select(c => $"{(game.Weapons.FirstOrDefault(w => w.Body == c.A)?.Kind.ToString() ?? "?")}"));
                    var selfHits = game.SelfWeaponHits.Count;
                    Console.WriteLine($"t {t:0.000} {k} lost {before[k] - v} swinging {swinging} cuts [{cuts}] self {selfHits} hits {game.RecentHits.Count}");
                }
            }

            before = now;
        });
    }
}
