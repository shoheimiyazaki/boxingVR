using System.Numerics;
using Aigis.Foundation.Voxels;
using MiniRing.Voxels;

namespace MiniRing.Tests;

/// <summary>
/// 目（2026-11-18、指示書 D）: 黒目が注目している方へ動く・まぶたでまばたき。どちらも描くだけ（見た目）で、材質のセル・試合の計算は変えない。
/// </summary>
[TestClass]
public sealed class EyeLookTests
{
    /// <summary>
    /// 白目が 2 段ある目（部品 eyes.5）の顔。F3 の目の部品はほかは白目が 1 段で、黒目の上下・まばたきの途中が見分けられない。
    /// </summary>
    internal static FaceSpec RoundEyes(string name)
    {
        var spec = FaceSpec.For(name);
        var parts = spec.Parts.ToDictionary(p => p.Key, p => p.Value);
        parts["eyes"] = new Dictionary<string, float> { ["eyes.5"] = 1f };
        return spec with { Parts = parts };
    }

    private static (BoxingGame Game, VoxelPart Head) Make()
    {
        var game = new BoxingGame(seed: 1, corners: new BoxingCorners("guard", _ => new StandGuard(), _ => new StandGuard()) with { CpuFaceSpec = RoundEyes("guard") });
        for (var i = 0; i < 50; i++)
        {
            game.Step(default, 1f / 60f);
        }

        // 目は顔の層（頭の剛体に重ねた 7 mm の格子: FaceLayer）にある。
        return (game, game.Cpu.Voxels!.Face!);
    }

    private static EyeScan ScanAt(VoxelPart head, Vector3 localTarget, float closure = 0f)
    {
        var toWorld = head.Body.GridToWorld();
        Assert.IsTrue(Matrix4x4.Invert(toWorld, out var inv));
        return EyeGaze.Scan(head.Body.Grid, inv, Vector3.Transform(localTarget, toWorld), closure);
    }

    [TestMethod]
    public void ThePupilStaysInsideWhatIsLeftOfTheWhite()
    {
        var (game, head) = Make();
        using var _ = game;
        var grid = head.Body.Grid;
        var before = ScanAt(head, new Vector3(5.634f, 0f, 2.817f));
        // +X 側の目の白目を全部削る: その目は塗らない（-1）。もう一方の目はそのまま動く。
        var cells = Enumerable.Range(0, grid.CellCount).Where(i => grid.MaterialAt(i) is BoxerAnatomy.Eye or BoxerAnatomy.Pupil && grid.Coordinates(i).X >= grid.Dimensions.X / 2).ToList();
        Assert.IsTrue(cells.Count > 0);
        foreach (var i in cells)
        {
            grid.Break(i, new List<RemovedVoxel>());
        }

        var after = ScanAt(head, new Vector3(5.634f, 0f, 2.817f));
        Assert.AreEqual(-1, after.Pupil0);
        Assert.AreEqual(before.Pupil1, after.Pupil1);
        Assert.AreNotEqual(-1, after.Pupil1);
    }

    [TestMethod]
    public void EyeAnimationDoesNotChangeTheMaterialsOrThePhysics()
    {
        var (game, head) = Make();
        using var _ = game;
        var grid = head.Body.Grid;
        var before = string.Join(',', Enumerable.Range(0, grid.CellCount).Select(i => grid.MaterialAt(i)));
        var version = grid.Version;
        head.Draw(0f, new EyePose(1f, new Vector3(1f, 1f, 1f)));
        Assert.AreEqual(before, string.Join(',', Enumerable.Range(0, grid.CellCount).Select(i => grid.MaterialAt(i))));
        Assert.AreEqual(version, grid.Version);
    }

    [TestMethod]
    public void BlinkRateAndStateFollowTheCondition()
    {
        static (int Blinks, float MaxClosure) Run(float winded, float awareness, bool dead, int seed = 7, float seconds = 100.7f)
        {
            var eyes = new EyeBlink();
            var blinks = 0;
            var wasShut = false;
            var max = 0f;
            for (var t = 0f; t < seconds; t += 1f / 120f)
            {
                eyes.Advance(1f / 120f, seed, winded, awareness, dead);
                var shut = eyes.Closure > 0.8f;
                blinks += shut && !wasShut ? 1 : 0;
                wasShut = shut;
                max = MathF.Max(max, eyes.Closure);
            }

            return (blinks, max);
        }

        var calm = Run(0f, 1f, false);
        var tired = Run(1f, 1f, false);
        Assert.IsTrue(calm.Blinks is >= 8 and <= 30, $"calm: about one blink per 2-6 s ({calm.Blinks}/min)");
        Assert.IsTrue(tired.Blinks > calm.Blinks * 1.5f, $"tired blinks more ({tired.Blinks} vs {calm.Blinks})");
        Assert.AreEqual(Run(0.3f, 1f, false, 7), Run(0.3f, 1f, false, 7), "deterministic for the same seed");

        var dead = new EyeBlink();
        dead.Advance(0.1678f, 1, 0f, 0f, true);
        Assert.AreEqual(1f, dead.Closure, 1e-4f);
        var unconscious = new EyeBlink();
        unconscious.Advance(0.1678f, 1, 0f, 0f, false);
        Assert.IsTrue(unconscious.Closure > 0.85f, "no awareness: the lids hang almost shut");
        var half = new EyeBlink();
        half.Advance(0.02685f, 1, 0f, 0.5f, false);
        Assert.IsTrue(half.Closure is > 0.1f and < 0.6f, $"dim awareness: half-lidded ({half.Closure:0.00})");
    }

    [TestMethod]
    public void BeingHitCloseToTheEyesMakesAReflexBlinkThatScalesWithTheBlow()
    {
        static float Hold(float energy, float distance)
        {
            var eyes = new EyeBlink();
            eyes.Advance(0.02685f, 3, 0f, 1f, false);
            eyes.Flinch(energy, distance);
            eyes.Advance(0.02685f, 3, 0f, 1f, false);
            Assert.IsTrue(eyes.Closure > 0.9f, "closes immediately");
            var t = 0f;
            while (eyes.Closure > 0.5f && t < 3.357f)
            {
                eyes.Advance(0.01f, 3, 0f, 1f, false);
                t += 0.01f;
            }

            return t;
        }

        var hard = Hold(1690f, 0.1408f);
        var soft = Hold(140.8f, 0.1408f);
        var far = Hold(1690f, 1.69f);
        Assert.IsTrue(hard > soft, $"harder blow, longer shut ({hard:0.00} vs {soft:0.00})");
        Assert.IsTrue(hard > far, $"blow nearer the eyes, longer shut ({hard:0.00} vs {far:0.00})");
    }

    [TestMethod]
    public void TheFighterLooksAtTheOpponentsHead()
    {
        var (game, _) = Make();
        using var _ = game;
        var target = EyeGaze.TargetOf(game.Player, game.Cpu);
        Assert.IsNotNull(target);
        Assert.IsTrue(Vector3.Distance(target.Value, game.Cpu.HeadPosition) < 0.1408f, "no glove coming: the opponent's head");
    }
}
