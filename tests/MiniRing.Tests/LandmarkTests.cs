using System.Numerics;
using Aigis.Foundation.Voxels;
using MiniRing.Voxels;

namespace MiniRing.Tests;

/// <summary>急所の狙いの点が、その体の実際のセルから求まること（頭の大きさの比で見積もった点ではない）。</summary>
[TestClass]
public sealed class LandmarkTests
{
    private static readonly BodyBuild Big = new(Height: 1.15f, Arms: 1.2f, Legs: 1.1f, Muscle: 1.3f);

    private static IEnumerable<(string Name, BoxingCorners Corners)> Bodies()
    {
        yield return ("standard", BoxingCorners.Default);
        yield return ("big build", BoxingCorners.Default with { CpuBody = Big });
        yield return ("wide-set eyes, long jaw", BoxingCorners.Default with
        {
            CpuFace = FaceParts.Default with { EyeSpacing = 1.25f, Jaw = 1.2f, Nose = 1.3f, EyeSize = 1.3f },
        });
        yield return ("small build, small eyes", BoxingCorners.Default with
        {
            CpuBody = new BodyBuild(Height: 0.9f, Muscle: 0.8f),
            CpuFace = FaceParts.Default with { EyeSize = 0.7f, EyeSpacing = 0.8f },
        });
    }

    private static int FilledAt(MiniRing.Voxels.VoxelPart part, Vector3 world)
    {
        Matrix4x4.Invert(part.Body.GridToWorld(), out var inverse);
        return part.Body.Grid.FilledCellAt(Vector3.Transform(world, inverse));
    }

    private static float CellDistance(VoxelGrid grid, Matrix4x4 gridToWorld, Vector3 world, Func<byte, bool> material)
    {
        var best = float.MaxValue;
        for (var i = 0; i < grid.CellCount; i++)
        {
            if (!material(grid.MaterialAt(i)))
            {
                continue;
            }

            var (x, y, z) = grid.Coordinates(i);
            best = MathF.Min(best, Vector3.Distance(world, Vector3.Transform(grid.CellCenter(x, y, z), gridToWorld)));
        }

        return best;
    }

    [TestMethod]
    public void EyeAim_IsWithinOneCellOfARealEyeCell_ForEveryBodyAndFace()
    {
        foreach (var (name, corners) in Bodies())
        {
            using var game = new BoxingGame(seed: 1, corners: corners);
            var cpu = game.Cpu;
            var head = cpu.Voxels!.Face!; // 目は顔の層（FaceLayer）にある
            var cell = head.Body.Grid.CellSize.X;
            foreach (var left in new[] { true, false })
            {
                var eye = cpu.Voxels.Landmarks.EyeCenter(left)!.Value;
                var d = CellDistance(head.Body.Grid, head.Body.GridToWorld(), eye, m => m is BoxerAnatomy.Eye or BoxerAnatomy.Pupil);
                Assert.IsLessThanOrEqualTo(cell, d, $"{name}: the eye aim is {d * 1000f:F1} mm from the nearest eye cell (cell {cell * 1000f:F1} mm)");
            }

            var aim = cpu.AsTarget.Point(AimGoal.Eye, game.Player.ShoulderCenter)!.Value;
            var dAim = CellDistance(head.Body.Grid, head.Body.GridToWorld(), aim, m => m is BoxerAnatomy.Eye or BoxerAnatomy.Pupil);
            Assert.IsLessThanOrEqualTo(cell, dAim, $"{name}: the Eye goal uses the same cells");
        }
    }

    [TestMethod]
    public void EyeAim_FollowsTheRemainingCells_AndALostEyeCannotBeAimedAt()
    {
        using var game = new BoxingGame(seed: 1);
        var cpu = game.Cpu;
        var head = cpu.Voxels!.Face!; // 目は顔の層（FaceLayer）にある
        var grid = head.Body.Grid;
        var before = cpu.Voxels.Landmarks.EyeCenter(left: true)!.Value;
        // 左（+X）の目のセルを前の半分だけ潰すと、重心は後ろ（目の奥）へ寄る。
        var removed = new List<(int Index, float Z)>();
        for (var i = 0; i < grid.CellCount; i++)
        {
            if (grid.MaterialAt(i) is BoxerAnatomy.Eye or BoxerAnatomy.Pupil)
            {
                var (x, y, z) = grid.Coordinates(i);
                var c = grid.CellCenter(x, y, z);
                if (c.X > 0f)
                {
                    removed.Add((i, c.Z));
                }
            }
        }

        var cut = removed.Select(r => r.Z).Average();
        foreach (var r in removed.Where(r => r.Z > cut))
        {
            grid.SetCellState(r.Index, (0, 0f, false, 0f));
        }

        var after = cpu.Voxels.Landmarks.EyeCenter(left: true)!.Value;
        var forward = cpu.Frame.Forward;
        Assert.IsLessThan(Vector3.Dot(before, forward), Vector3.Dot(after, forward), "the aim moved back with the remaining cells");

        foreach (var r in removed)
        {
            grid.SetCellState(r.Index, (0, 0f, false, 0f));
        }

        Assert.IsNull(cpu.Voxels.Landmarks.EyeCenter(left: true), "no cells left on that side");
        var other = cpu.AsTarget.Point(AimGoal.Eye, game.Player.ShoulderCenter)!.Value;
        Assert.AreEqual(0f, Vector3.Distance(other, cpu.Voxels.Landmarks.EyeCenter(left: false)!.Value), 1e-6f, "the other eye is still a target");
    }

    [TestMethod]
    public void BoneAndOrganGoals_LandOnTheOutermostCellOfTheirRegion_ForEveryBody()
    {
        foreach (var (name, corners) in Bodies())
        {
            using var game = new BoxingGame(seed: 1, corners: corners);
            var cpu = game.Cpu;
            var from = game.Player.ShoulderCenter;
            var head = cpu.Voxels!.PartOf(cpu.Parts.Head)!;
            var face = cpu.Voxels.Face!; // 頭蓋骨の外の皮膚は顔の層（FaceLayer）にある
            var chest = cpu.Voxels.PartOf(cpu.Parts.Chest)!;
            var cell = head.Body.Grid.CellSize.X;
            foreach (var goal in new[] { AimGoal.Chin, AimGoal.Temple, AimGoal.SolarPlexus, AimGoal.Liver, AimGoal.Heart })
            {
                var point = cpu.AsTarget.Point(goal, from);
                Assert.IsNotNull(point, $"{name}: {goal}");
                // 胴の狙いは胸か腹（分割胴では胴の下の部分が腹の格子）の、点を含む方の格子で見る。
                var candidates = goal is AimGoal.Chin or AimGoal.Temple ? [face]
                    : cpu.Parts.Abdomen is { } abdomen ? new[] { chest, cpu.Voxels.PartOf(abdomen)! } : [chest];
                var part = candidates.FirstOrDefault(c => FilledAt(c, point!.Value) >= 0) ?? candidates[0];
                var grid = part.Body.Grid;
                var index = FilledAt(part, point!.Value);
                Assert.IsGreaterThanOrEqualTo(0, index, $"{name}: {goal} sits on a filled cell");
                Assert.IsTrue(grid.MaterialAt(index) is BoxerAnatomy.Skin or BoxerAnatomy.Lip or BoxerAnatomy.Hair or BoxerAnatomy.Muscle, $"{name}: {goal} sits on the skin ({grid.MaterialAt(index)})");
                var (x, y, z) = grid.Coordinates(index);
                Assert.IsTrue(grid.IsExposed(x, y, z), $"{name}: {goal} is on the outside of the body");
            }

            // 目より下・あごは顔の前・こめかみは目の高さで横。
            var eyeY = cpu.Voxels.Landmarks.EyeCenter(true)!.Value.Y;
            var chin = cpu.AsTarget.Point(AimGoal.Chin, from)!.Value;
            Assert.IsLessThan(eyeY - (3f * cell), chin.Y, $"{name}: the chin is well below the eyes");
            var temple = cpu.AsTarget.Point(AimGoal.Temple, from)!.Value;
            Assert.AreEqual(eyeY, temple.Y, 2f * cell, $"{name}: the temple is at eye height");
            var lateral = MathF.Abs(Vector3.Dot(temple - cpu.HeadPosition, Vector3.Cross(Vector3.UnitY, cpu.Frame.Forward)));
            Assert.IsGreaterThan(0.5f * head.Body.Grid.HalfExtents.X, lateral, $"{name}: the temple is at the side of the head");
        }
    }

    [TestMethod]
    public void VitalAim_ScalesWithTheBody()
    {
        using var standard = new BoxingGame(seed: 1);
        using var big = new BoxingGame(seed: 1, corners: BoxingCorners.Default with { CpuBody = Big });
        static float Span(BoxingGame g) => Vector3.Distance(g.Cpu.AsTarget.Point(AimGoal.Chin, g.Player.ShoulderCenter)!.Value, g.Cpu.AsTarget.Point(AimGoal.Eye, g.Player.ShoulderCenter)!.Value);
        Assert.IsGreaterThan(Span(standard), Span(big), "a bigger head has its chin further below its eyes");
    }
}
