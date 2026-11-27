using System.Numerics;

namespace MiniRing.Tests;

/// <summary>
/// 体重はボクセルの実測（docs/plans/BASE_BODY_PLAN.md §3）: 剛体の質量と慣性は部位の格子の材質ごとのセルの数 × 密度 × セルの体積から、
/// 紙の体重（<see cref="Characters.BodySizing.PhysicalMassKg"/>）・減量の目標も同じ数え方。1 つの現象に 1 つのモデル（CLAUDE.md §1.5）。
/// </summary>
[TestClass]
public sealed class VoxelMassTests
{
    [TestMethod]
    [DataRow(1f, 1f)]
    [DataRow(0.95f, 0.85f)]
    [DataRow(1.1f, 1.15f)]
    public void TheMatchBodysMass_IsItsVoxelCount_AndThePaperWeight(float height, float muscle)
    {
        var build = new BodyBuild(height, 1f, 1f, muscle);
        using var game = new BoxingGame(seed: 1, corners: MiniRing.Bots.BotCatalog.Corners("boxer") with { PlayerBody = build, Referee = false });
        var f = game.Player;
        var body = f.Voxels ?? throw new InvalidOperationException("ボクセルの体が無い");
        foreach (var part in body.Parts)
        {
            var id = part.Body.BodyId;
            var counted = Voxels.BodyMassTable.MassOf(part.Body.Grid) + part.Body.Companions.Sum(c => Voxels.BodyMassTable.MassOf(c.Grid));
            Assert.AreEqual(counted, game.World.GetMass(id), counted * 1e-4f, $"{part.Kind}: 剛体の質量 = 格子の実測");
            // 形（ローポリの体）は箱の内側にあり、縁ほど細いので、同じ質量の一様な箱より回しやすい。
            var h = f.HalfExtentsOf(id);
            var box = counted / 3f * new Vector3((h.Y * h.Y) + (h.Z * h.Z), (h.X * h.X) + (h.Z * h.Z), (h.X * h.X) + (h.Y * h.Y));
            var inertia = game.World.GetLocalInertiaDiagonal(id);
            Assert.IsTrue(inertia.X > 0f && inertia.Y > 0f && inertia.Z > 0f, $"{part.Kind}");
            Assert.IsTrue(inertia.X < box.X && inertia.Y < box.Y && inertia.Z < box.Z, $"{part.Kind}: 慣性 {inertia} は一様な箱 {box} より小さい");
        }

        // 紙の体重（生成の前に数える）と試合の体は、顔の部品の違いのぶんしか違わない。
        Assert.AreEqual(f.TotalMass, Characters.BodySizing.PhysicalMassKg(build), f.TotalMass * 0.005f, "紙の体重 = 試合の体の実測");
    }

    [TestMethod]
    public void TheStandardBody_IsARealBoxersSize()
    {
        Assert.AreEqual(175, Characters.CharacterProfile.StandardHeightCm, "基準の身長");
        Assert.AreEqual(75f, Characters.BodySizing.PhysicalMassKg(BodyBuild.Standard), 1.5f, "基準の体重（BMI ≈ 24.5）");
        var masses = FighterTuning.Default.Masses;
        var total = Characters.BodySizing.PhysicalMassKg(BodyBuild.Standard);
        // 部位の配分は実物（de Leva 1996）に近い: 胴 ≈ 44%、もも ≈ 14%、頭 ≈ 6%。
        Assert.AreEqual(0.44f, (masses.Pelvis + masses.Chest) / total, 0.05f, "胴の割合");
        Assert.AreEqual(0.14f, masses.Thigh / total, 0.03f, "ももの割合");
        Assert.AreEqual(0.06f, masses.Head / total, 0.02f, "頭の割合");
    }

    [TestMethod]
    public void WithWeight_LandsTheMeasuredWeightOnTheTarget_OnlyByTheThickness()
    {
        var frame = new BodyBuild(1.05f, 1.02f, 0.98f, 1f);
        foreach (var target in new[] { 62, 70, 85, 100 })
        {
            var made = Characters.BodySizing.WithWeight(frame, target);
            Assert.AreEqual(target, Characters.BodySizing.PhysicalMassKg(made), 1.5f, $"{target} kg");
            Assert.AreEqual((frame.Height, frame.Arms, frame.Legs), (made.Height, made.Arms, made.Legs), "骨格は変えない");
        }
    }
}
