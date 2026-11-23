using System.Numerics;
using Aigis.Foundation.Voxels;
using Aigis.Foundation;
using Aigis.Physics;
using Aigis.Physics.Voxels;
using Aigis.Render;
using MiniRing.Voxels;
using MiniRing.Weapons;

namespace MiniRing.Tests;

[TestClass]
public sealed class KarateFistTests
{
    public TestContext TestContext { get; set; } = null!;

    [TestMethod]
    [TestCategory("visual")]
    public void BareFist_CloseUp_FromFrontAndPalm()
    {
        var root = new DirectoryInfo(AppContext.BaseDirectory);
        while (root is not null && !File.Exists(Path.Combine(root.FullName, "MiniRing.slnx")))
        {
            root = root.Parent;
        }

        Assert.IsNotNull(root);
        var output = Path.Combine(root.FullName, ".visualcheck");
        Directory.CreateDirectory(output);
        using var world = new PhysicsWorld(gravity: Vector3.Zero);
        var fist = Weapon.Create(world, WeaponKind.Fist, Vector3.Zero, Quaternion.Identity);
        using var renderer = new HeadlessRenderer(640, 640);
        foreach (var (name, eye) in new[]
        {
            ("front", new Vector3(0.2535f, 0.07042f, -0.1127f)),
            ("palm", new Vector3(0.04225f, -0.2535f, -0.09859f)),
        })
        {
            var camera = Camera.ViewProjection(eye, Vector3.Zero, Vector3.UnitZ, 45f * MathF.PI / 180f, 1f, 0.002817f, 5.634f);
            var pixels = renderer.RenderPrimitiveScene(new Vector4(0.12f, 0.14f, 0.18f, 1f), camera, [], [],
                cameraPosition: eye, voxels: [fist.Draw()]);
            PngWriter.WriteRgba(Path.Combine(output, $"karate_fist_{name}.png"), 640, 640, pixels);
        }
    }

    [TestMethod]
    public void BareFist_HasTwoLeadingKnuckles_WithBoneUnderThinSkin()
    {
        var grid = Weapon.Build(WeaponKind.Fist);
        var front = Enumerable.Range(0, grid.CellCount)
            .Where(i => grid.MaterialAt(i) != 0)
            .Max(i => Center(grid, i).X);
        var tips = Enumerable.Range(0, grid.CellCount)
            .Where(i => grid.MaterialAt(i) != 0 && Center(grid, i).X > front - 0.001408f)
            .Select(i => Center(grid, i)).ToArray();
        Assert.IsTrue(tips.All(p => p.Z < 0.005634f), "index and middle knuckles lead the ring and little fingers");
        Assert.IsTrue(tips.Any(p => p.Z < -0.01408f) && tips.Any(p => p.Z > -0.01408f), "two knuckles, not one spike");
        Assert.IsTrue(front - Weapon.GripOf(WeaponKind.Fist).X > 0.03944f, "knuckles project ahead of the folded fingers");
        var boneBehind = 0;
        foreach (var p in tips)
        {
            var skin = grid.FilledCellAt(p);
            Assert.AreEqual(BoxerAnatomy.HandSkin, grid.MaterialAt(skin));
            var bone = grid.FilledCellAt(p - new Vector3(grid.CellSize.X, 0f, 0f));
            boneBehind += bone >= 0 && grid.MaterialAt(bone) == BoxerAnatomy.HandBone ? 1 : 0;
        }

        // 指の関節で組んだ拳頭は、骨 1 セルの前に皮膚 1 セル（指の十字の断面: 骨の左右の皮膚は骨の前に無い）。先頭の面の 3 分の 1 以上は骨が後ろにある（掌のほうへ曲がる指の先は皮膚だけで骨が奥にずれる所もある）。
        Assert.IsGreaterThanOrEqualTo(tips.Length / 3, boneBehind, "one thin layer covers hard knuckles");

        var filled = Enumerable.Range(0, grid.CellCount).Where(i => grid.MaterialAt(i) != 0).ToHashSet();
        var pending = new Queue<int>();
        pending.Enqueue(filled.First());
        filled.Remove(pending.Peek());
        var (nx, ny, nz) = grid.Dimensions;
        while (pending.TryDequeue(out var i))
        {
            var (x, y, z) = grid.Coordinates(i);
            foreach (var (dx, dy, dz) in new[] { (1, 0, 0), (-1, 0, 0), (0, 1, 0), (0, -1, 0), (0, 0, 1), (0, 0, -1) })
            {
                var xx = x + dx;
                var yy = y + dy;
                var zz = z + dz;
                if (xx >= 0 && xx < nx && yy >= 0 && yy < ny && zz >= 0 && zz < nz
                    && filled.Remove(xx + nx * (yy + ny * zz)))
                {
                    pending.Enqueue(xx + nx * (yy + ny * zz));
                }
            }
        }

        Assert.IsEmpty(filled, "the knuckles and folded fingers must connect to the palm");
    }

    [TestMethod]
    [DataRow(11.75f)]
    [DataRow(13.43f)]
    public void SeikenAndPalmHeel_BothDamageFleshWithoutLosingHandSkin(float speed)
    {
        // 拳頭と掌底はそれぞれの手の形で、同じ質量・速度で同じ標的へ進ませる。
        var knuckle = Strike(Weapon.Build(WeaponKind.Fist), speed);
        _ = new BareHandState().Placed(HandSkeleton.PoseOf(HandShape.PalmHeel), out var palmGrid);
        var palm = Strike(palmGrid, speed);
        TestContext.WriteLine($"speed={speed:F2} m/s, knuckle={knuckle}, palm={palm}");
        foreach (var (name, result) in new[] { ("seiken", knuckle), ("palm heel", palm) })
        {
            Assert.IsGreaterThan(0f, result.Pressure, $"{name}: the hand makes contact");
            Assert.IsGreaterThan(0f, result.Depth, $"{name}: the damage extends into the target");
            Assert.IsGreaterThan(0, result.TargetLost, $"{name}: the target loses cells");
            // 手の組織は現行モデルでは木の物性を使う（docs/DEBT.md「手の物性を木の値で代用」）。
            Assert.AreEqual(0, result.SkinLost, $"{name}: flesh does not strip the hand skin");
        }
    }

    private static (int TargetLost, int SkinLost, float Depth, float Pressure) Strike(VoxelGrid grid, float speed)
    {
        using var world = new PhysicsWorld(gravity: Vector3.Zero);
        // Equal mass and initial velocity isolate the shape and materials, rather than giving one hand more energy.
        var fistId = world.AddDynamicBox(new Vector3(-0.1831f, 0f, 0f), Quaternion.Identity, grid.HalfExtents, 0.8f);
        var fist = new VoxelBody(world, fistId, grid);
        var targetGrid = new VoxelGrid(BoxerAnatomy.Palette, new Vector3(0.07042f), 0.01408f,
            (p, _) => p.X < -0.05634f ? BoxerAnatomy.Skin : BoxerAnatomy.Muscle);
        var targetId = world.AddDynamicBox(Vector3.Zero, Quaternion.Identity, targetGrid.HalfExtents, 10f);
        var target = new VoxelBody(world, targetId, targetGrid);
        world.SetPairCollisionEnabled(fistId, targetId, false);
        world.SetLinearVelocity(fistId, new Vector3(speed, 0f, 0f));
        var before = targetGrid.FilledCount;
        var skin = grid.Remaining(BoxerAnatomy.Skin) + grid.Remaining(BoxerAnatomy.HandSkin);
        var depth = 0f;
        var pressure = 0f;
        for (var step = 0; step < 50; step++)
        {
            var a = world.GetState(fistId);
            var b = world.GetState(targetId);
            world.Step(1f / 240f);
            var hit = VoxelContact.Resolve(fist, a, target, b);
            pressure = MathF.Max(pressure, hit.Pressure);
            foreach (var cell in hit.RemovedB.Concat(hit.CrushedB ?? []))
            {
                depth = MathF.Max(depth, cell.LocalCenter.X + targetGrid.HalfExtents.X);
            }
        }

        return (before - targetGrid.FilledCount, skin - grid.Remaining(BoxerAnatomy.Skin) - grid.Remaining(BoxerAnatomy.HandSkin), depth, pressure);
    }

    private static Vector3 Center(VoxelGrid grid, int i)
    {
        var (nx, ny, _) = grid.Dimensions;
        return grid.CellCenter(i % nx, i / nx % ny, i / (nx * ny));
    }
}
