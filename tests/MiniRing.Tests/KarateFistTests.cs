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
            ("front", new Vector3(0.09f, 0.025f, -0.04f)),
            ("palm", new Vector3(0.015f, -0.09f, -0.035f)),
        })
        {
            var camera = Camera.ViewProjection(eye, Vector3.Zero, Vector3.UnitZ, 45f * MathF.PI / 180f, 1f, 0.001f, 2f);
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
            .Where(i => grid.MaterialAt(i) != 0 && Center(grid, i).X > front - 0.0005f)
            .Select(i => Center(grid, i)).ToArray();
        Assert.IsTrue(tips.All(p => p.Z < 0.002f), "index and middle knuckles lead the ring and little fingers");
        Assert.IsTrue(tips.Any(p => p.Z < -0.005f) && tips.Any(p => p.Z > -0.005f), "two knuckles, not one spike");
        Assert.IsTrue(front - Weapon.GripOf(WeaponKind.Fist).X > 0.014f, "knuckles project ahead of the folded fingers");
        foreach (var p in tips)
        {
            var skin = grid.FilledCellAt(p);
            Assert.AreEqual(BoxerAnatomy.Skin, grid.MaterialAt(skin));
            var bone = grid.FilledCellAt(p - new Vector3(grid.CellSize.X, 0f, 0f));
            Assert.IsTrue(bone >= 0 && grid.MaterialAt(bone) == BoxerAnatomy.Bone, "one thin layer covers hard knuckles");
        }

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
    [DataRow(7f)]
    [DataRow(8f)]
    public void SameBlow_KnucklesConcentrateDamageComparedWithPalm_AndHurtTheirOwnSkin(float speed)
    {
        var old = Strike(LegacyFist(), Quaternion.Identity, speed);
        var knuckle = Strike(Weapon.Build(WeaponKind.Fist), Quaternion.Identity, speed);
        var palm = Strike(Weapon.Build(WeaponKind.Fist), Quaternion.CreateFromAxisAngle(Vector3.UnitZ, MathF.PI / 2f), speed);
        TestContext.WriteLine($"old={old}, knuckle={knuckle}, palm={palm}");
        Assert.IsGreaterThan(old.Pressure, knuckle.Pressure);
        // p = F/A does not imply a larger removed volume. Plastic work is strength*A*distance;
        // a narrow, hard contact concentrates stress but does not supply additional energy.
        // These equal-energy blows are counterexamples to the original total-cell expectation.
        Assert.IsLessThan(old.TargetLost, knuckle.TargetLost,
            "a higher peak pressure must not be mistaken for a guaranteed larger damaged volume");
        Assert.IsGreaterThan(palm.Depth, knuckle.Depth);
        Assert.IsGreaterThan(palm.TargetLost, knuckle.TargetLost);
        Assert.IsTrue(knuckle.SkinLost > 0, "the striking hand also loses skin");
    }

    private static VoxelGrid LegacyFist() => new(BoxerAnatomy.Palette, new Vector3(0.014f), 0.002f,
        (p, _) => p.Length() > 0.013f ? (byte)0 : p.Length() > 0.009f ? BoxerAnatomy.Skin : BoxerAnatomy.Bone);

    private static (int TargetLost, int SkinLost, float Depth, float Pressure) Strike(VoxelGrid grid, Quaternion rotation, float speed)
    {
        using var world = new PhysicsWorld(gravity: Vector3.Zero);
        // Equal mass and initial velocity isolate the shape and materials, rather than giving one hand more energy.
        var fistId = world.AddDynamicBox(new Vector3(-0.065f, 0f, 0f), rotation, grid.HalfExtents, 0.08f);
        var fist = new VoxelBody(world, fistId, grid);
        var targetGrid = new VoxelGrid(BoxerAnatomy.Palette, new Vector3(0.025f), 0.005f,
            (p, _) => p.X < -0.020f ? BoxerAnatomy.Skin : BoxerAnatomy.Muscle);
        var targetId = world.AddDynamicBox(Vector3.Zero, Quaternion.Identity, targetGrid.HalfExtents, 1f);
        var target = new VoxelBody(world, targetId, targetGrid);
        world.SetPairCollisionEnabled(fistId, targetId, false);
        world.SetLinearVelocity(fistId, new Vector3(speed, 0f, 0f));
        var before = targetGrid.FilledCount;
        var skin = grid.Remaining(BoxerAnatomy.Skin);
        var depth = 0f;
        var pressure = 0f;
        for (var step = 0; step < 30; step++)
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

        return (before - targetGrid.FilledCount, skin - grid.Remaining(BoxerAnatomy.Skin), depth, pressure);
    }

    private static Vector3 Center(VoxelGrid grid, int i)
    {
        var (nx, ny, _) = grid.Dimensions;
        return grid.CellCenter(i % nx, i / nx % ny, i / (nx * ny));
    }
}
