using System.Numerics;

namespace MiniRing.Tests;

/// <summary>実戦で頭に当たった後の首の揺れ（胴に対する頭の傾きの最大）を、頭の大きさごとに比べる調査用。</summary>
[TestClass]
public sealed class HeadWobbleProbe
{
    [TestMethod]
    [DataRow(1.15f)]
    [DataRow(0.72f)]
    public void WobbleAfterHeadHits(float headScale)
    {
        var corners = new BoxingCorners("champion", s => Bots.BotCatalog.Create("champion", s), s => Bots.BotCatalog.Create("slugger", s));
        using var game = new BoxingGame(FighterTuning.Default with { HeadScale = headScale }, seed: 3, corners: corners);
        var peaks = new List<float>();
        var watch = new Dictionary<Fighter, (float Until, float Peak)>();
        const float dt = 1f / 240f;
        for (var t = 0f; t < 60f; t += dt)
        {
            game.Step(default, dt);
            foreach (var hit in game.RecentHits.Where(h => h.Zone is HitZone.Head or HitZone.Temple or HitZone.Chin))
            {
                var victim = hit.Attacker == game.Cpu ? game.Player : game.Cpu;
                if (!watch.ContainsKey(victim))
                {
                    watch[victim] = (t + 0.3f, 0f);
                }
            }

            foreach (var (f, w) in watch.ToList())
            {
                var tilt = Tilt(game.World, f);
                if (t > w.Until)
                {
                    peaks.Add(w.Peak);
                    watch.Remove(f);
                }
                else
                {
                    watch[f] = w with { Peak = MathF.Max(w.Peak, tilt) };
                }
            }
        }

        peaks.Sort();
        Console.WriteLine($"HeadScale {headScale:0.00}: {peaks.Count} head hits, head-to-chest tilt peak median {peaks[peaks.Count / 2]:0.000} rad, 90th {peaks[(int)(peaks.Count * 0.9f)]:0.000} rad");
    }

    private static float Tilt(Aigis.Physics.PhysicsWorld world, Fighter f)
    {
        var relative = Quaternion.Concatenate(world.GetState(f.Parts.Head).Orientation, Quaternion.Inverse(world.GetState(f.Parts.Chest).Orientation));
        return 2f * MathF.Acos(Math.Clamp(MathF.Abs(relative.W), 0f, 1f));
    }
}
