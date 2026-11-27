using System.Numerics;

namespace MiniRing.Tests;

/// <summary>
/// 体重をボクセルで実測する（docs/plans/BASE_BODY_PLAN.md §3・§7）: 体格ごとに、部位の箱の寸法・ボクセルの実測（材質ごとのセルの数 × 実物の密度 × セルの体積）
/// で入った剛体の質量・慣性と、生成の前の見積もり（<c>FighterTuning.Masses</c>）・身長・肩の半幅を並べる。
/// </summary>
[TestClass]
[TestCategory("Probe")]
public sealed class VoxelMassProbe
{
    [TestMethod]
    [DataRow(1f, 1f)]
    [DataRow(1f, 0.8f)]
    [DataRow(1.2f, 1.1f)]
    public void PartMasses(float height, float muscle)
    {
        var tuning = FighterTuning.Default with { Build = new BodyBuild(height, 1f, 1f, muscle) };
        using var game = new BoxingGame(seed: 1, corners: MiniRing.Bots.BotCatalog.Corners("boxer") with { PlayerBody = tuning.Build, Referee = false });
        var f = game.Player;
        var world = game.World;
        Console.WriteLine($"体格 H {height} M {muscle}: 身長 {Characters.CharacterProfile.HeightOf(tuning.Build)} cm・紙の体重 {Characters.CharacterProfile.WeightOf(tuning.Build)} kg・"
            + $"肩の半幅 {tuning.HalfExtents.Chest.X:0.000} m・試合の体 {f.TotalMass:0.00} kg");
        var total = 0f;
        foreach (var id in f.AllParts)
        {
            var kind = f.KindOf(id);
            var h = f.HalfExtentsOf(id) * 200f;
            var m = world.GetMass(id);
            var i = world.GetLocalInertiaDiagonal(id);
            var box = m / 3f * new Vector3((h.Y * h.Y) + (h.Z * h.Z), (h.X * h.X) + (h.Z * h.Z), (h.X * h.X) + (h.Y * h.Y)) / 40000f;
            total += m;
            Console.WriteLine($"  {kind,-9} 箱 {h.X:0}×{h.Y:0}×{h.Z:0} cm  実測 {m,6:0.00} kg  慣性 {i.X:0.0000}/{i.Y:0.0000}/{i.Z:0.0000}  一様な箱なら {box.X:0.0000}/{box.Y:0.0000}/{box.Z:0.0000}");
        }

        var ms = tuning.Masses;
        Console.WriteLine($"  合計 {total:0.00} kg  見積もり（生成の前）{Characters.BodySizing.PhysicalMassKg(tuning.Build):0.00} kg  "
            + $"骨盤 {ms.Pelvis:0.00} 胴 {ms.Chest:0.00} 頭 {ms.Head:0.00} 上腕 {ms.UpperArm:0.00} 前腕 {ms.Forearm:0.00} もも {ms.Thigh:0.00} すね {ms.Shin:0.00} かかと {ms.Foot:0.00} つま先 {tuning.ToeMass:0.00}");
    }
}
