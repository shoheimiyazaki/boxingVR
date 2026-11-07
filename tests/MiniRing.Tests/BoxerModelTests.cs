using System.Numerics;
using Aigis.Render;
using MiniRing.Visual;

namespace MiniRing.Tests;

/// <summary>見た目のメッシュの健全性（GPU なしで確かめられる範囲）。形の良し悪しは BoxerLookTests の画像で見る。</summary>
[TestClass]
public sealed class BoxerModelTests
{
    private static readonly BoxerModel Model = BoxerModel.For(FighterTuning.Default.HalfExtents, FighterTuning.Default.GloveRadius);

    private static IEnumerable<(string Name, MeshData Mesh)> Meshes()
    {
        yield return ("head", Model.Head);
        yield return ("torso", Model.Torso);
        yield return ("pelvis", Model.Pelvis);
        yield return ("upperarm-", Model.UpperArmNegX);
        yield return ("upperarm+", Model.UpperArmPosX);
        yield return ("forearm-", Model.ForearmNegX);
        yield return ("forearm+", Model.ForearmPosX);
        yield return ("glove-", Model.GloveNegX);
        yield return ("glove+", Model.GlovePosX);
        yield return ("thigh", Model.Thigh);
        yield return ("shin", Model.Shin);
        yield return ("foot", Model.Foot);
    }

    [TestMethod]
    public void Meshes_AreFiniteTriangles_WithUnitNormals()
    {
        foreach (var (name, mesh) in Meshes())
        {
            var v = mesh.Vertices;
            Assert.AreEqual(0, v.Length % (MeshData.FloatsPerVertex * 3), name);
            Assert.IsGreaterThan(300, v.Length / MeshData.FloatsPerVertex, $"{name} should be a smooth shape");
            for (var i = 0; i < v.Length; i += MeshData.FloatsPerVertex)
            {
                for (var k = 0; k < MeshData.FloatsPerVertex; k++)
                {
                    Assert.IsTrue(float.IsFinite(v[i + k]), $"{name} vertex {i / 6} component {k}");
                }

                var n = new Vector3(v[i + 3], v[i + 4], v[i + 5]);
                Assert.AreEqual(1f, n.Length(), 1e-3f, $"{name} normal {i / 6}");
            }
        }
    }

    [TestMethod]
    public void Normals_PointOutward()
    {
        // 閉じた形なので、法線と（頂点 − 形の重心）の向きはほとんどの頂点で揃う。
        foreach (var (name, mesh) in Meshes())
        {
            var v = mesh.Vertices;
            var centroid = Vector3.Zero;
            var count = v.Length / MeshData.FloatsPerVertex;
            for (var i = 0; i < v.Length; i += MeshData.FloatsPerVertex)
            {
                centroid += new Vector3(v[i], v[i + 1], v[i + 2]);
            }

            centroid /= count;
            var outward = 0;
            for (var i = 0; i < v.Length; i += MeshData.FloatsPerVertex)
            {
                var p = new Vector3(v[i], v[i + 1], v[i + 2]);
                outward += Vector3.Dot(new Vector3(v[i + 3], v[i + 4], v[i + 5]), p - centroid) > 0f ? 1 : 0;
            }

            Assert.IsGreaterThan(0.85f, outward / (float)count, $"{name}: {outward}/{count} normals point outward");
        }
    }

    [TestMethod]
    public void Meshes_StayCloseToTheirHitboxes()
    {
        // 見た目は当たり判定（物理の箱）から大きく外れない: デフォルメ分と関節の覆いで 1.6 cm まで。
        var h = FighterTuning.Default.HalfExtents;
        foreach (var (name, mesh, half, lift) in new (string, MeshData, Vector3, float)[]
        {
            ("head", Model.Head, h.Head, Model.HeadLift),
            ("torso", Model.Torso, h.Chest, 0f),
            ("pelvis", Model.Pelvis, h.Pelvis, 0f),
            ("thigh", Model.Thigh, h.Thigh, 0f),
            ("shin", Model.Shin, h.Shin, 0f),
            ("foot", Model.Foot, h.Foot, 0f),
        })
        {
            var v = mesh.Vertices;
            var worst = 0f;
            for (var i = 0; i < v.Length; i += MeshData.FloatsPerVertex)
            {
                var p = new Vector3(v[i], v[i + 1] + lift, v[i + 2]);
                var outside = Vector3.Max(Vector3.Abs(p) - half, Vector3.Zero);
                worst = MathF.Max(worst, outside.Length());
            }

            Assert.IsLessThan(0.016f + (name is "head" or "torso" ? HumanoidRagdollOverlap : 0f), worst, $"{name} sticks out of its hitbox by {worst * 100f:0.0} cm");
        }
    }

    [TestMethod]
    public void LoftDepthOffset_PointsForward_WhicheverWayTheAxisRuns()
    {
        // 軸が上向き・下向き・±X のどれでも、Offset.Y（奥行き）は +Z（体の前）へ出る。
        foreach (var (from, to, width) in new[]
        {
            (-Vector3.UnitY, Vector3.UnitY, Vector3.UnitX),
            (Vector3.UnitY, -Vector3.UnitY, Vector3.UnitX),
            (Vector3.UnitX, -Vector3.UnitX, Vector3.UnitY),
            (-Vector3.UnitX, Vector3.UnitX, Vector3.UnitY),
        })
        {
            var grid = ShapeMesh.Loft(from, to, width, _ => new MiniRing.Visual.Ring(0.1f, 0.1f, 2f, new Vector2(0f, 0.5f)), rings: 4, around: 8);
            foreach (var ring in grid)
            {
                Assert.IsGreaterThan(0.3f, ring.Average(p => p.Z), $"axis {from}->{to}");
            }
        }
    }

    [TestMethod]
    public void Model_IsCachedPerBodySize()
    {
        Assert.AreSame(Model, BoxerModel.For(FighterTuning.Default.HalfExtents, FighterTuning.Default.GloveRadius));
        Assert.AreSame(Model.Head, BoxerModel.For(FighterTuning.Default.HalfExtents, FighterTuning.Default.GloveRadius).Head);
    }

    // 頭は首を見せるために持ち上げ、胴は首の分だけ上へ伸びる（関節の重なり代 3 cm の補い）。
    private const float HumanoidRagdollOverlap = Aigis.Physics.HumanoidRagdoll.Overlap;
}
