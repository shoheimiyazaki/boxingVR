using System.Numerics;
using Aigis.Physics;
using Aigis.Render;
using MiniRing.LowPoly;

namespace MiniRing.Tests;

/// <summary>ローポリの体（形・細分化）、直角三角形への分解、打撃によるへこみと色。</summary>
[TestClass]
public sealed class LowPolyBoxerTests
{
    public static IEnumerable<object[]> AllParts => Enum.GetValues<BodyPartKind>().Select(k => new object[] { k });

    [TestMethod]
    [DynamicData(nameof(AllParts))]
    public void EveryPartShape_IsClosedAndFacesOutward(BodyPartKind kind)
    {
        var mesh = BoxerShapes.For(kind);
        AssertClosedAndOriented(mesh);
        Assert.IsGreaterThan(0f, mesh.SignedVolume(), $"{kind}: 外向きなら体積が正");
    }

    [TestMethod]
    public void Shapes_AreLowPoly()
    {
        Assert.AreEqual(112, BoxerShapes.Head().TriangleCount);
        Assert.AreEqual(96, BoxerShapes.Chest().TriangleCount);
        foreach (var kind in Enum.GetValues<BodyPartKind>())
        {
            Assert.IsLessThanOrEqualTo(112, BoxerShapes.For(kind).TriangleCount, kind.ToString());
        }
    }

    [TestMethod]
    public void Head_HasNoseForwardAndRecessedEyeSockets()
    {
        var head = BoxerShapes.Head();
        var front = head.Vertices.MaxBy(v => v.Z);
        Assert.IsLessThan(0.05f, MathF.Abs(front.X), "一番前は正中の鼻先");
        Assert.IsGreaterThan(1f, front.Z, "鼻は顔の面より前に出る");

        var (left, right) = BoxerShapes.EyeSockets;
        Assert.IsGreaterThan(0f, head.Vertices[left].X, "左目は本人の左（+X）");
        Assert.IsLessThan(0f, head.Vertices[right].X);
        Assert.AreEqual(head.Vertices[left].Z, head.Vertices[right].Z, 1e-5f, "左右対称");
        Assert.IsLessThan(front.Z - 0.3f, head.Vertices[left].Z, "眼窩は鼻より奥");
    }

    [TestMethod]
    [DynamicData(nameof(AllParts))]
    public void Subdivide_KeepsTheLowPolyShapeWithFourTimesTheTriangles(BodyPartKind kind)
    {
        var low = BoxerShapes.For(kind);
        var fine = low.Subdivide();

        Assert.AreEqual(low.TriangleCount * 4, fine.TriangleCount);
        CollectionAssert.AreEqual(low.Vertices.ToArray(), fine.Vertices.Take(low.Vertices.Length).ToArray(), "元のノードは同じ番号のまま");
        Assert.AreEqual(low.SurfaceArea(), fine.SurfaceArea(), low.SurfaceArea() * 1e-4f, "面積が同じ＝形を変えていない");
        Assert.AreEqual(low.SignedVolume(), fine.SignedVolume(), low.SignedVolume() * 1e-4f, "体積も同じ");
        AssertClosedAndOriented(fine);
    }

    [TestMethod]
    public void RightTriangles_TileTheTriangleWithDecomposableMatrices()
    {
        var random = new Random(7);
        for (var n = 0; n < 200; n++)
        {
            Vector3 R() => new((float)random.NextDouble() * 0.05f, (float)random.NextDouble() * 0.05f, (float)random.NextDouble() * 0.05f);
            var (a, b, c) = (R(), R(), R());
            var area = Vector3.Cross(b - a, c - a).Length() * 0.5f;
            if (area < 1e-6f)
            {
                continue;
            }

            var normal = LowPolyMesh.FaceNormal(a, b, c);
            var pieces = new List<Matrix4x4>();
            RightTriangles.Decompose(a, b, c, pieces);

            Assert.IsTrue(pieces.Count is 1 or 2);
            var covered = 0f;
            foreach (var m in pieces)
            {
                var o = Vector3.Transform(Vector3.Zero, m);
                var u = Vector3.Transform(Vector3.UnitX, m);
                var v = Vector3.Transform(Vector3.UnitY, m);
                covered += Vector3.Cross(u - o, v - o).Length() * 0.5f;
                foreach (var p in new[] { o, u, v })
                {
                    Assert.AreEqual(0f, Vector3.Dot(p - a, normal), 1e-6f, "元の三角形の面の上");
                    AssertInside(p, a, b, c);
                }

                Assert.IsTrue(Matrix4x4.Decompose(m, out var scale, out var rotation, out var translation), "拡大・回転・平行移動に分解できる");
                var recomposed = Matrix4x4.CreateScale(scale) * Matrix4x4.CreateFromQuaternion(rotation) * Matrix4x4.CreateTranslation(translation);
                AssertMatrixClose(m, recomposed, 1e-5f);
                var drawnNormal = Vector3.Transform(Vector3.UnitZ, rotation);
                Assert.IsGreaterThan(0.999f, Vector3.Dot(drawnNormal, normal), "描く面の法線が元の面と同じ向き");
            }

            Assert.AreEqual(area, covered, area * 1e-3f, "2 枚で元の三角形をちょうど覆う");
        }
    }

    [TestMethod]
    public void Impact_MovesNearbyNodesAlongTheForce_AndLeavesTheOriginalUntouched()
    {
        var head = BoxerShapes.Head().Scaled(new Vector3(0.04f, 0.05f, 0.04f)).Subdivide();
        var mesh = new DentedMesh(head, maxOffset: 0.015f);
        var nose = Enumerable.Range(0, head.Vertices.Length).MaxBy(i => head.Vertices[i].Z);
        var back = Enumerable.Range(0, head.Vertices.Length).MinBy(i => head.Vertices[i].Z);
        var fist = head.Vertices[nose] + new Vector3(0f, 0f, 0.02f);

        var hit = mesh.Impact(fist, fistRadius: 0.02f, direction: -Vector3.UnitZ, depth: 0.006f, spread: 0.015f);

        Assert.IsFalse(mesh.IsDented, "元のメッシュは変わらない（不変）");
        Assert.AreEqual(Vector3.Zero, mesh.Offset(nose));
        Assert.IsTrue(hit.IsDented);
        Assert.AreEqual(0.006f, hit.Offset(nose).Length(), 1e-5f, "拳に触れたノードは深さぶん");
        Assert.IsLessThan(-0.999f, Vector3.Normalize(hit.Offset(nose)).Z, "力の向き（−Z）へ動く");
        Assert.AreEqual(Vector3.Zero, hit.Offset(back), "遠いノードは動かない");

        var moved = Enumerable.Range(0, hit.NodeCount).Count(i => hit.Offset(i) != Vector3.Zero);
        Assert.IsGreaterThan(1, moved, "近くのノードも一緒にへこむ");
        Assert.IsLessThan(hit.NodeCount / 4, moved, "へこむのは当たった所の近くだけ");
    }

    [TestMethod]
    public void RepeatedImpacts_AreCappedAndTurnTheNodeFromWhiteToRed()
    {
        var head = BoxerShapes.Head().Scaled(new Vector3(0.04f, 0.05f, 0.04f)).Subdivide();
        var nose = Enumerable.Range(0, head.Vertices.Length).MaxBy(i => head.Vertices[i].Z);
        var mesh = new DentedMesh(head, maxOffset: 0.01f);
        Assert.AreEqual(DentedMesh.Undamaged, DentedMesh.StrainColor(mesh.Strain(nose)), "打たれる前は白");

        var once = mesh.Impact(head.Vertices[nose] + new Vector3(0f, 0f, 0.02f), 0.02f, -Vector3.UnitZ, 0.004f, 0.015f);
        var color = DentedMesh.StrainColor(once.Strain(nose));
        Assert.AreEqual(1f, color.X, 0.2f, "赤は残る");
        Assert.IsLessThan(1f, color.Y, "緑・青が抜けて赤くなっていく");
        Assert.IsGreaterThan(DentedMesh.Damaged.Y, color.Y, "まだ真っ赤ではない");

        var many = once;
        for (var i = 0; i < 10; i++)
        {
            many = many.Impact(head.Vertices[nose] + new Vector3(0f, 0f, 0.02f), 0.02f, -Vector3.UnitZ, 0.004f, 0.015f);
        }

        Assert.AreEqual(0.01f, many.Offset(nose).Length(), 1e-5f, "上限で止まる（裏返らない）");
        Assert.AreEqual(DentedMesh.Damaged, DentedMesh.StrainColor(many.Strain(nose)), "上限で真っ赤");
    }

    [TestMethod]
    public void DentedTriangles_CoverEveryFaceAndColorItByItsNodes()
    {
        var head = BoxerShapes.Head().Scaled(new Vector3(0.04f, 0.05f, 0.04f)).Subdivide();
        var nose = Enumerable.Range(0, head.Vertices.Length).MaxBy(i => head.Vertices[i].Z);
        var mesh = new DentedMesh(head, 0.01f).Impact(head.Vertices[nose] + new Vector3(0f, 0f, 0.02f), 0.02f, -Vector3.UnitZ, 0.006f, 0.015f);

        var (locals, colors) = mesh.Triangles();
        Assert.AreEqual(locals.Count, colors.Count);
        Assert.IsGreaterThanOrEqualTo(head.TriangleCount, locals.Count);
        Assert.IsLessThanOrEqualTo(head.TriangleCount * 2, locals.Count);
        Assert.IsTrue(colors.Any(c => c == DentedMesh.Undamaged), "打たれていない所は白");
        Assert.IsTrue(colors.Any(c => c.Y < 0.8f), "打たれた所は赤い");
    }

    [TestMethod]
    public void Dent_OnARealFighter_DentsTheHitPartOnTheFistSide()
    {
        using var world = new PhysicsWorld();
        var tuning = FighterTuning.Default;
        var fighter = Fighter.Spawn(world, tuning, "f", Vector3.Zero, 0f);
        var model = BoxerModel.Build(tuning.HalfExtents, tuning.ModelSubdivisions);
        var skin = new BoxerSkin(model, fighter, tuning);
        var head = world.GetState(fighter.Parts.Head);
        var forward = Vector3.Transform(Vector3.UnitZ, head.Orientation);
        var fist = head.Position + (forward * (fighter.HalfExtents.Head.Z + tuning.GloveRadius));

        skin.Dent(fighter.Parts.Head, head, fist, -forward, damage: 15f);

        var face = skin.Part(fighter.Parts.Head);
        Assert.IsTrue(face.IsDented);
        Assert.IsFalse(skin.Part(fighter.Parts.Chest).IsDented, "他の部位はそのまま");
        var deepest = Enumerable.Range(0, face.NodeCount).MaxBy(i => face.Offset(i).Length());
        Assert.IsGreaterThan(0f, face.Rest.Vertices[deepest].Z, "一番へこむのは拳の側（顔の前）");
        Assert.IsLessThan(-0.99f, Vector3.Normalize(face.Offset(deepest)).Z, "頭のローカルで後ろ（力の向き）へ");
        Assert.AreEqual(MathF.Min(tuning.MaxDentDepthPerHit, 15f * tuning.DentDepthPerDamage), face.Offset(deepest).Length(), 1e-4f);
    }

    [TestMethod]
    public void Match_HitsDentTheVictimsSkin()
    {
        using var game = new BoxingGame(seed: 3);
        var dented = false;
        for (var i = 0; i < 60 * 30 && !dented; i++)
        {
            var player = game.Player;
            var jab = MathF.Sin(i / 60f * 6f) > 0.3f ? 1f : 0.45f;
            game.Step(new BoxingInput(new FighterCommand(new Vector3(0f, 0f, 0.4f), game.Cpu.ChestPosition - player.ChestPosition,
                new HandCommand(new Vector3(0f, 0.1f, jab) * player.ArmReach, false),
                new HandCommand(new Vector3(0f, 0.1f, 1.45f - jab) * player.ArmReach, false))), 1f / 60f);
            dented = game.Cpu.Parts.All.Concat(game.Player.Parts.All).Any(id => (game.Cpu.Owns(id) ? game.CpuSkin : game.PlayerSkin).Part(id).IsDented);
        }

        Assert.IsTrue(dented, "30 秒打ち合えば誰かのどこかがへこむ");
        // 体の見た目はボクセル（Voxels/BoxerBody）。打たれた所は削れて、周りが血の色に染まる。
        var (instances, _) = game.BuildRenderables();
        Assert.IsTrue(instances.Any(x => x.Kind == PrimitiveKind.Cube && x.Color.Y < 0.5f && x.Color.X > 0.4f), "傷はボクセルの色（肉・血）で見える");
    }

    private static void AssertClosedAndOriented(LowPolyMesh mesh)
    {
        var directed = new HashSet<(int, int)>();
        for (var t = 0; t < mesh.TriangleCount; t++)
        {
            var (a, b, c) = mesh.Triangle(t);
            foreach (var edge in new[] { (a, b), (b, c), (c, a) })
            {
                Assert.IsTrue(directed.Add(edge), $"向きの揃った辺が重複: {edge}");
            }
        }

        foreach (var (a, b) in directed)
        {
            Assert.Contains((b, a), directed, $"辺 ({a},{b}) の向こう側の三角形が無い（穴か裏返り）");
        }
    }

    private static void AssertInside(Vector3 p, Vector3 a, Vector3 b, Vector3 c)
    {
        var n = Vector3.Cross(b - a, c - a);
        const float Tolerance = -1e-9f;
        Assert.IsGreaterThanOrEqualTo(Tolerance, Vector3.Dot(Vector3.Cross(b - a, p - a), n));
        Assert.IsGreaterThanOrEqualTo(Tolerance, Vector3.Dot(Vector3.Cross(c - b, p - b), n));
        Assert.IsGreaterThanOrEqualTo(Tolerance, Vector3.Dot(Vector3.Cross(a - c, p - c), n));
    }

    private static void AssertMatrixClose(Matrix4x4 expected, Matrix4x4 actual, float tolerance)
    {
        for (var r = 0; r < 4; r++)
        {
            for (var c = 0; c < 4; c++)
            {
                Assert.AreEqual(expected[r, c], actual[r, c], tolerance, $"[{r},{c}]");
            }
        }
    }
}
