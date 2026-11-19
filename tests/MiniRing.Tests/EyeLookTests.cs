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
    /// 丸い目（部品 eyes.1: 白目が 3 段）の顔。細い目の部品（eyes.2・eyes.6）は顔の層で 1 段しかなく、黒目の上下・まばたきの途中が見分けられない。
    /// </summary>
    internal static FaceSpec RoundEyes(string name)
    {
        var spec = FaceSpec.For(name);
        var parts = spec.Parts.ToDictionary(p => p.Key, p => p.Value);
        parts["eyes"] = new Dictionary<string, float> { ["eyes.1"] = 1f };
        return spec with { Parts = parts };
    }

    private static (BoxingGame Game, VoxelPart Head) Make()
    {
        var game = new BoxingGame(seed: 1, corners: new BoxingCorners("guard", _ => new StandGuard(), _ => new StandGuard()) with { CpuFaceSpec = RoundEyes("guard") });
        for (var i = 0; i < 30; i++)
        {
            game.Step(default, 1f / 60f);
        }

        // 目は顔の層（頭の剛体に重ねた 2.5 mm の格子: FaceLayer）にある。
        return (game, game.Cpu.Voxels!.Face!);
    }

    private static EyeScan ScanAt(VoxelPart head, Vector3 localTarget, float closure = 0f)
    {
        var toWorld = head.Body.GridToWorld();
        Assert.IsTrue(Matrix4x4.Invert(toWorld, out var inv));
        return EyeGaze.Scan(head.Body.Grid, inv, Vector3.Transform(localTarget, toWorld), closure);
    }

    [TestMethod]
    public void PupilMovesToTheEdgeOfTheWhiteInTheLookedDirection()
    {
        var (game, head) = Make();
        using var _ = game;
        var grid = head.Body.Grid;
        var forward = ScanAt(head, new Vector3(0f, 0.05f, 2f));
        var right = ScanAt(head, new Vector3(2f, 0.05f, 1f));
        var left = ScanAt(head, new Vector3(-2f, 0.05f, 1f));
        var up = ScanAt(head, new Vector3(0f, 2f, 1f));
        var down = ScanAt(head, new Vector3(0f, -2f, 1f));

        (int X, int Y, int Z) Cell(int index) => grid.Coordinates(index);

        // 左右の目それぞれで、向いた方向へ黒目のセルが動く。
        // 目は横に長い面（顔の層で 4 × 2 セルほど）なので、黒目は向いた方の列・段へ動く（正面は両目が寄り目: 内側の列）。
        Assert.IsTrue(Cell(right.Pupil0).X > Cell(left.Pupil0).X, "+X eye: looking right puts the pupil on a higher column");
        Assert.IsTrue(Cell(right.Pupil1).X > Cell(left.Pupil1).X, "-X eye: same");
        Assert.IsTrue(Cell(forward.Pupil0).X >= Cell(left.Pupil0).X && Cell(forward.Pupil0).X <= Cell(right.Pupil0).X);
        Assert.IsTrue(Cell(forward.Pupil1).X >= Cell(left.Pupil1).X && Cell(forward.Pupil1).X <= Cell(right.Pupil1).X);
        Assert.IsTrue(Cell(up.Pupil0).Y > Cell(down.Pupil0).Y, "looking up puts the pupil on a higher row");
        Assert.IsTrue(Cell(up.Pupil1).Y > Cell(down.Pupil1).Y);
        Assert.IsTrue(Cell(forward.Pupil0).Y >= Cell(down.Pupil0).Y && Cell(forward.Pupil0).Y <= Cell(up.Pupil0).Y);
        Assert.IsTrue(Cell(forward.Pupil1).Y >= Cell(down.Pupil1).Y && Cell(forward.Pupil1).Y <= Cell(up.Pupil1).Y);
        // 選ばれたのは白目か黒目の材質のセル。
        foreach (var i in new[] { forward.Pupil0, forward.Pupil1, right.Pupil0, up.Pupil1, down.Pupil0, left.Pupil1 })
        {
            Assert.IsTrue(grid.MaterialAt(i) is BoxerAnatomy.Eye or BoxerAnatomy.Pupil);
        }
    }

    [TestMethod]
    public void ThePupilStaysInsideWhatIsLeftOfTheWhite()
    {
        var (game, head) = Make();
        using var _ = game;
        var grid = head.Body.Grid;
        var before = ScanAt(head, new Vector3(2f, 0f, 1f));
        // +X 側の目の白目を全部削る: その目は塗らない（-1）。もう一方の目はそのまま動く。
        var cells = Enumerable.Range(0, grid.CellCount).Where(i => grid.MaterialAt(i) is BoxerAnatomy.Eye or BoxerAnatomy.Pupil && grid.Coordinates(i).X >= grid.Dimensions.X / 2).ToList();
        Assert.IsTrue(cells.Count > 0);
        foreach (var i in cells)
        {
            grid.Break(i, new List<RemovedVoxel>());
        }

        var after = ScanAt(head, new Vector3(2f, 0f, 1f));
        Assert.AreEqual(-1, after.Pupil0);
        Assert.AreEqual(before.Pupil1, after.Pupil1);
        Assert.AreNotEqual(-1, after.Pupil1);
    }

    [TestMethod]
    public void BlinkingRecoloursTheEyeToSkinFromTheTopDown()
    {
        var (game, head) = Make();
        using var _ = game;
        var grid = head.Body.Grid;
        var skin = grid.Palette[BoxerAnatomy.Skin].SurfaceColor;

        (int Same, int Skin, int Y)[] Rows(float closure)
        {
            head.Draw(0f, new EyePose(closure, null));
            var rgba = head.Volume.Rgba;
            var (dx, dy, dz) = grid.Dimensions;
            var result = new List<(int, int, int)>();
            for (var y = 0; y < dy; y++)
            {
                int eye = 0, asSkin = 0;
                for (var z = 0; z < dz; z++)
                {
                    for (var x = 0; x < dx; x++)
                    {
                        if (grid.At(x, y, z) is BoxerAnatomy.Eye or BoxerAnatomy.Pupil && grid.IsOriginalSurface(x, y, z))
                        {
                            eye++;
                            var t = 4 * (x + (dx * (y + (dy * z))));
                            if (Math.Abs(rgba[t] - (byte)Math.Round(skin.X * 255f)) <= 1 && Math.Abs(rgba[t + 1] - (byte)Math.Round(skin.Y * 255f)) <= 1)
                            {
                                asSkin++;
                            }
                        }
                    }
                }

                if (eye > 0)
                {
                    result.Add((eye, asSkin, y));
                }
            }

            return result.ToArray();
        }

        var open = Rows(0f);
        Assert.IsTrue(open.All(r => r.Skin == 0), "open eyes: no cell is skin-coloured");
        var half = Rows(0.5f);
        var closed = Rows(1f);
        Assert.IsTrue(closed.All(r => r.Skin == r.Same), "closed: every eye cell is skin-coloured");
        // 半分: 上の段は肌、下の段は目。上から下へ（段の Y が大きいほど先に閉じる）。
        var skinRows = half.Where(r => r.Skin == r.Same).Select(r => r.Y).ToList();
        var openRows = half.Where(r => r.Skin == 0).Select(r => r.Y).ToList();
        Assert.IsTrue(skinRows.Count > 0 && openRows.Count > 0, "mid blink: some rows lidded, some open");
        Assert.IsTrue(skinRows.Min() > openRows.Max(), "lid comes down from the top");
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
        static (int Blinks, float MaxClosure) Run(float winded, float awareness, bool dead, int seed = 7, float seconds = 60f)
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
        dead.Advance(0.1f, 1, 0f, 0f, true);
        Assert.AreEqual(1f, dead.Closure, 1e-4f);
        var unconscious = new EyeBlink();
        unconscious.Advance(0.1f, 1, 0f, 0f, false);
        Assert.IsTrue(unconscious.Closure > 0.85f, "no awareness: the lids hang almost shut");
        var half = new EyeBlink();
        half.Advance(0.016f, 1, 0f, 0.5f, false);
        Assert.IsTrue(half.Closure is > 0.1f and < 0.6f, $"dim awareness: half-lidded ({half.Closure:0.00})");
    }

    [TestMethod]
    public void BeingHitCloseToTheEyesMakesAReflexBlinkThatScalesWithTheBlow()
    {
        static float Hold(float energy, float distance)
        {
            var eyes = new EyeBlink();
            eyes.Advance(0.016f, 3, 0f, 1f, false);
            eyes.Flinch(energy, distance);
            eyes.Advance(0.016f, 3, 0f, 1f, false);
            Assert.IsTrue(eyes.Closure > 0.9f, "closes immediately");
            var t = 0f;
            while (eyes.Closure > 0.5f && t < 2f)
            {
                eyes.Advance(0.01f, 3, 0f, 1f, false);
                t += 0.01f;
            }

            return t;
        }

        var hard = Hold(60f, 0.05f);
        var soft = Hold(5f, 0.05f);
        var far = Hold(60f, 0.6f);
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
        Assert.IsTrue(Vector3.Distance(target.Value, game.Cpu.HeadPosition) < 0.05f, "no glove coming: the opponent's head");
    }
}
