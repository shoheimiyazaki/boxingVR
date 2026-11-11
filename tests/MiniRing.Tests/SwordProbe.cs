using MiniRing.Weapons;

namespace MiniRing.Tests;

/// <summary>調査用: 剣の基本の技を 1 回ずつ出して、狙った部位に何が起きたかを出す（--logger "console;verbosity=detailed"）。</summary>
[TestClass]
public sealed class SwordProbe
{
    [TestMethod]
    [TestCategory("Probe")]
    public void Basics()
    {
        var results = new SwordResult[SwordTechnique.Basics.Count];
        Parallel.For(0, results.Length, i => results[i] = SwordDrill.Run(SwordTechnique.Basics[i]));
        foreach (var r in results)
        {
            Console.WriteLine($"{r.Technique.Name} effect {r.Effect:0.00} target {r.TargetCells:0} severed {r.Severed} killed {r.Killed} conc {r.Concussion:0.000} other {r.OtherCells:0} miss {r.Miss * 100f:0.0}cm tip {r.TipSpeed:0.0} first {r.FirstPart} self {r.SelfCells:0}/{r.SelfConcussion:0.000}");
        }
    }

    [TestMethod]
    [TestCategory("Probe")]
    public void WhenDoesItCut()
    {
        var kote = SwordTechnique.Basics[0];
        var r = SwordDrill.Run(kote, (game, t, swinging) =>
        {
            foreach (var c in game.RecentCuts.Where(c => c.RemovedB.Count > 0 || c.RemovedA.Count > 0))
            {
                var victim = game.Cpu.Voxels!.Parts.FirstOrDefault(p => p.Body == c.B || p.Body == c.A);
                var own = game.Player.Voxels!.Parts.FirstOrDefault(p => p.Body == c.B || p.Body == c.A);
                Console.WriteLine($"t {t:0.000} swinging {swinging} cpu {victim?.Kind} own {own?.Kind} removed {c.RemovedA.Count}/{c.RemovedB.Count} dist {System.Numerics.Vector3.Distance(game.Player.ChestPosition with { Y = 0 }, game.Cpu.ChestPosition with { Y = 0 }):0.000}");
            }
        });
        Console.WriteLine($"effect {r.Effect:0.00}");
    }

    [TestMethod]
    [TestCategory("Probe")]
    public void CutThrough()
    {
        var kesa = SwordTechnique.Basics.First(t => t.Name == "kesa");
        var variants = new[]
        {
            ("basic", kesa),
            ("through", kesa with { Swing = kesa.Swing with { StrikeFist = kesa.Swing.StrikeFist + new System.Numerics.Vector3(0.1f, -0.2f, 0.35f) } }),
            ("through+ease", kesa with { Swing = kesa.Swing with { StrikeFist = kesa.Swing.StrikeFist + new System.Numerics.Vector3(0.1f, -0.2f, 0.35f), Ease = 2f } }),
            ("through+fast", kesa with { Swing = kesa.Swing with { StrikeFist = kesa.Swing.StrikeFist + new System.Numerics.Vector3(0.1f, -0.2f, 0.35f), StrikeTime = 0.1f } }),
        };
        var results = new SwordResult[variants.Length];
        Parallel.For(0, variants.Length, i => results[i] = SwordDrill.Run(variants[i].Item2));
        for (var i = 0; i < variants.Length; i++)
        {
            var r = results[i];
            Console.WriteLine($"{variants[i].Item1} effect {r.Effect:0.00} target {r.TargetCells:0} other {r.OtherCells:0} miss {r.Miss * 100f:0.0}cm tip {r.TipSpeed:0.0} self {r.SelfCells:0}");
        }
    }

    [TestMethod]
    [TestCategory("Probe")]
    public void SparSpeed()
    {
        var watch = System.Diagnostics.Stopwatch.StartNew();
        var style = SwordStyle.Even(SwordTechnique.Basics);
        var r = SwordSparring.Run(style, style, seed: 1, seconds: 20f);
        Console.WriteLine($"spar 20s took {watch.Elapsed.TotalSeconds:0.0}s: red dealt {r.DealtByRed} blue dealt {r.DealtByBlue} died {r.RedDied}/{r.BlueDied} red used {string.Join(",", r.RedUsed)} by {string.Join(",", r.RedByTechnique.Select(kv => $"{kv.Key}={kv.Value}"))}");
    }

    [TestMethod]
    [TestCategory("Probe")]
    public void KatanaGuard()
    {
        var clip = MiniRing.Motion.BvhClip.Load(Path.Combine(AppContext.BaseDirectory, "../../../../../data/mocap/sfu_kendo/0015_BasicKendo001.bvh"));
        var f = MiniRing.Motion.MotionRetarget.FromBvh(clip, "g", 6.0f, 6.1f).Frames[0];
        Console.WriteLine($"guard R {f.Right} L {f.Left} blade {f.Blade} az {MathF.Atan2(f.Blade.X, f.Blade.Z) * 57.3f:0} el {MathF.Asin(f.Blade.Y) * 57.3f:0} spacing {System.Numerics.Vector3.Distance(f.Right, f.Left):0.00}");
    }
}
