using System.Numerics;

namespace MiniRing.Tests;

/// <summary>顔の窓（<see cref="FaceCam"/>）: カメラが頭のボクセルの格子に固定されていること、窓が画面の両脇に重ならずに収まること。</summary>
[TestClass]
public sealed class FaceCamTests
{
    [TestMethod]
    public void Camera_IsFixedToTheHeadsVoxelGrid_SoTheFaceStaysPutWhileTheHeadMoves()
    {
        // 試合を進めると頭は動き・回る（CPU が打ってくる）。頭に固定した点は、どの時刻でも窓の同じ所に写る。
        using var game = new BoxingGame(seed: 3);
        var faceCam = FaceCam.Default;
        var half = game.Player.HalfExtentsOf(game.Player.Parts.Head);
        var facePoints = new[] { new Vector3(0.4f * half.X, 0.2f * half.Y, half.Z), new Vector3(-0.3f * half.X, -0.5f * half.Y, 0.8f * half.Z) };
        Vector2[]? first = null;
        Quaternion? firstOrientation = null;
        var turned = 0f;
        for (var step = 0; step < 604; step++)
        {
            game.Step(default, 1f / 60f);
            if (step % 30 != 0)
            {
                continue;
            }

            var head = game.World.GetState(game.Player.Parts.Head);
            firstOrientation ??= head.Orientation;
            turned = MathF.Max(turned, AngleBetween(firstOrientation.Value, head.Orientation));
            var (viewProjection, eye) = faceCam.Camera(game, game.Player, 1f);
            var projected = facePoints.Select(p => Project(viewProjection, game.Display.Point(head.Position + Vector3.Transform(p, head.Orientation)))).ToArray();
            Assert.IsGreaterThan(0.01f, Vector3.Distance(eye, game.Display.Point(head.Position)), "the camera stands off the face");
            first ??= projected;
            for (var i = 0; i < projected.Length; i++)
            {
                Assert.AreEqual(first[i].X, projected[i].X, 1e-3f, $"point {i} x at step {step}");
                Assert.AreEqual(first[i].Y, projected[i].Y, 1e-3f, $"point {i} y at step {step}");
            }
        }

        Assert.IsGreaterThan(0.05f, turned, "the head really turned during the check (otherwise the test proves nothing)");
    }

    [TestMethod]
    public void Camera_LooksAtTheFace_FromTheFront()
    {
        using var game = new BoxingGame(seed: 3);
        game.Step(default, 1f / 60f);
        var head = game.World.GetState(game.Player.Parts.Head);
        var (viewProjection, eye) = FaceCam.Default.Camera(game, game.Player, 1f);
        var forward = Vector3.Transform(Vector3.UnitZ, head.Orientation);
        Assert.IsGreaterThan(0.99f, Vector3.Dot(Vector3.Normalize(eye - game.Display.Point(head.Position)), forward), "in front of the face (head-local +Z, where the eyes are)");
        var center = Project(viewProjection, game.Display.Point(head.Position));
        Assert.IsLessThan(0.3f, center.Length(), $"the head is near the middle of the pane: {center}");
        var top = Project(viewProjection, game.Display.Point(head.Position + Vector3.Transform(new Vector3(0f, game.Player.HalfExtentsOf(game.Player.Parts.Head).Y, 0f), head.Orientation)));
        Assert.IsLessThan(-0.1f, top.Y, "head-local up is up in the pane (Vulkan clip y points down)");
    }

    private static Vector2 Project(Matrix4x4 viewProjection, Vector3 world)
    {
        var clip = Vector4.Transform(new Vector4(world, 1f), viewProjection);
        Assert.IsGreaterThan(0f, clip.W, "in front of the camera");
        return new Vector2(clip.X / clip.W, clip.Y / clip.W);
    }

    private static float AngleBetween(Quaternion a, Quaternion b) => 2f * MathF.Acos(MathF.Min(1f, MathF.Abs(Quaternion.Dot(a, b))));
}
