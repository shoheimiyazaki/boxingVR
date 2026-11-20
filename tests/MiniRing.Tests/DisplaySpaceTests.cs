using System.Numerics;
using Aigis.Render;
using Aigis.Render.Voxels;

namespace MiniRing.Tests;

/// <summary>
/// 物理の空間 → 表示の空間（<see cref="DisplaySpace"/>）: 縮尺 1 は何もしない（今までの描画とビット単位で同じ）、縮尺のあるときは
/// 位置・大きさ（行列の大きさと球の半径の両方）・カメラが同じ倍率で縮み、部屋（表示の空間）の外へカメラが出ない。
/// </summary>
[TestClass]
public sealed class DisplaySpaceTests
{
    private static readonly PrimitiveInstance Sphere = new(PrimitiveKind.Sphere, Matrix4x4.CreateTranslation(1f, 2f, 3f), new Vector3(0.1f, 0f, 0f), Vector4.One);

    private static readonly PrimitiveInstance Cube = new(PrimitiveKind.Cube, Matrix4x4.CreateScale(0.2f) * Matrix4x4.CreateTranslation(1f, 2f, 3f), Vector3.Zero, Vector4.One,
        TextureTileMetres: 0.5f);

    [TestMethod]
    public void ScaleOne_ChangesNothing()
    {
        var display = new DisplaySpace(1f);
        Assert.IsTrue(display.IsIdentity);
        Assert.AreEqual(Sphere, display.Instance(Sphere));
        Assert.AreEqual(Cube, display.Instance(Cube));
        var p = new Vector3(0.123f, 4.56f, -7.89f);
        Assert.AreEqual(p, display.Point(p));
        Assert.AreEqual(p, display.ToPhysics(p));
    }

    [TestMethod]
    public void AScale_ShrinksPositionsAndSizes_TheSameWay_WhicheverPlaceTheSizeLives()
    {
        const float s = 0.355f;
        var display = new DisplaySpace(s);

        var sphere = display.Instance(Sphere);
        AssertNear(new Vector3(1f, 2f, 3f) * s, sphere.Model.Translation, "the sphere's centre");
        Assert.AreEqual(0.1f * s, sphere.Dimensions.X, 1e-6f, "a sphere carries its radius in Dimensions, not in the matrix");
        Assert.AreEqual(1f, Vector3.TransformNormal(Vector3.UnitX, sphere.Model).Length(), 1e-5f, "so its matrix keeps unit scale (the radius is not shrunk twice)");

        var cube = display.Instance(Cube);
        var (min, max) = cube.WorldBounds();
        var (min0, max0) = Cube.WorldBounds();
        AssertNear((max0 - min0) * s, max - min, "a cube carries its size in the matrix");
        AssertNear((max0 + min0) * 0.5f * s, (max + min) * 0.5f, "the cube's centre");
        Assert.AreEqual(0.5f * s, cube.TextureTileMetres, 1e-6f, "a texture repeat shrinks with the surface it covers");

        var line = display.Line(new DebugLine(new Vector3(1f, 0f, 0f), new Vector3(0f, 2f, 0f), Vector4.One));
        AssertNear(new Vector3(s, 0f, 0f), line.A, "line start");
        AssertNear(new Vector3(0f, 2f * s, 0f), line.B, "line end");

        var particle = display.Particle(new ParticleInstance(new Vector3(0f, 1f, 0f), 0.02f, Vector4.One));
        Assert.AreEqual(s, particle.Position.Y, 1e-6f);
        Assert.AreEqual(0.02f * s, particle.Size, 1e-7f);

        var voxel = display.Voxel(new VoxelVolumeDraw(null!, Matrix4x4.CreateScale(0.005f) * Matrix4x4.CreateTranslation(1f, 1f, 1f)));
        AssertNear(new Vector3(1f, 1f, 1f) * s, voxel.CellToWorld.Translation, "the voxel grid's origin");
        Assert.AreEqual(0.005f * s, Vector3.TransformNormal(Vector3.UnitX, voxel.CellToWorld).Length(), 1e-7f, "one cell shrinks by the scale");

        AssertNear(new Vector3(1f, 2f, 3f), display.ToPhysics(display.Point(new Vector3(1f, 2f, 3f))), "display and physics are inverses");
    }

    [TestMethod]
    public void TheCamera_SeesTheShrunkWorld_AsTheUnshrunkCameraSawTheUnshrunkWorld()
    {
        // 物理の空間で決めた目・注視点を移したカメラは、移した点を、移す前のカメラが移す前の点を写したのと同じ画面の位置へ写す（縮尺は見え方を変えない）。
        const float s = 0.355f;
        var eye = new Vector3(0.5f, 1.4f, 0.2f);
        var target = new Vector3(0f, 1.0f, 1.5f);
        var point = new Vector3(0.1f, 1.1f, 1.3f);
        var (physicsView, _) = new DisplaySpace(1f).Camera(eye, target, Vector3.UnitY, 0.8f, 1.5f, 0.02f, 30f);
        var (displayView, displayEye) = new DisplaySpace(s).Camera(eye, target, Vector3.UnitY, 0.8f, 1.5f, 0.02f * s, 30f * s);
        AssertNear(eye * s, displayEye, "the eye moves into the display space");
        var screen = new Vector2(1920f, 1080f);
        Assert.IsTrue(Camera.TryWorldToScreen(physicsView, point, screen, out var a));
        Assert.IsTrue(Camera.TryWorldToScreen(displayView, point * s, screen, out var b));
        Assert.AreEqual(a.X, b.X, 0.05f);
        Assert.AreEqual(a.Y, b.Y, 0.05f);
    }

    [TestMethod]
    public void TheCamera_StaysInsideTheRoom_WhichIsInTheDisplaySpace()
    {
        var display = new DisplaySpace(0.355f);
        var (_, eye) = display.Camera(new Vector3(0f, 20f, 1f), Vector3.Zero, Vector3.UnitY, 0.8f, 1.5f, 0.01f, 30f, keepInsideMargin: 0.1f);
        Assert.IsLessThan(LivingRoom.CeilingHeight, eye.Y, "the eye is pulled under the room's ceiling after the move, not before");
    }

    private static void AssertNear(Vector3 expected, Vector3 actual, string what)
        => Assert.IsLessThan(1e-5f, Vector3.Distance(expected, actual), $"{what}: expected {expected}, got {actual}");
}
