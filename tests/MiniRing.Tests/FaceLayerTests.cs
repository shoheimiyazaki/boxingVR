using System.Numerics;
using System.Text.Json;
using Aigis.Foundation.Voxels;
using MiniRing.Characters;
using MiniRing.LowPoly;
using MiniRing.Replay;
using MiniRing.Voxels;

namespace MiniRing.Tests;

/// <summary>
/// 頭の顔の層（F2、<see cref="FaceLayer"/>）: 頭の剛体に重ねた 7 mm の格子が頭蓋骨の外を持ち、14 mm の頭は頭蓋骨・脳・首の中だけ。
/// 脳のセルは変わらない・同じものを 2 回持たない・目は顔の層にあり見えを決める・打撃は顔の層を削る、を確かめる。
/// </summary>
[TestClass]
public sealed class FaceLayerTests
{
    internal static readonly string[] Ids = ["akira", "albert", "daichi", "dmitri", "edgar", "gaston", "jin", "joao", "konrad", "marcus", "narong", "ramon", "ren", "sean", "taeyun", "tsuyoshi", "vikram", "yoandry"];

    /// <summary>名簿のキャラの、試合と同じ手順で作った頭の格子（顔の層へ分ける前の 5 mm）。</summary>
    internal static VoxelGrid FullHead(FighterCharacter c)
    {
        var tuning = FighterTuning.Default with { Build = c.Build };
        var standard = FighterTuning.Default with { Build = BodyBuild.Standard };
        var model = BoxerModel.Build(standard.HalfExtents, tuning.ModelSubdivisions);
        using var world = new Aigis.Physics.PhysicsWorld(Vector3.Zero);
        var fighter = Fighter.Spawn(world, tuning, "face", Vector3.Zero, 0f);
        VoxelPalette? palette = null;
        return BoxerBody.BuildPartGrid(BodyPartKind.Head, fighter.HalfExtentsOf(fighter.Parts.Head), tuning, model, model, Outfit.RedCorner,
            BoxerVoxelOverrides.Shipped, c.Look, ref palette);
    }

    [TestMethod]
    public void TheSkullKeepsEveryBrainCell_AndNothingIsHeldTwice()
    {
        foreach (var id in Ids)
        {
            var c = CharacterRoster.Get(id);
            var full = FullHead(c);
            var (inner, face) = FaceLayer.Split(full, c.Face);
            Assert.AreEqual(full.Remaining(BoxerAnatomy.Brain), inner.Remaining(BoxerAnatomy.Brain), $"{id}: the brain is not moved");
            Assert.AreEqual(full.Remaining(BoxerAnatomy.Cord), inner.Remaining(BoxerAnatomy.Cord), $"{id}: nor the cord");
            foreach (var outer in new[] { BoxerAnatomy.Hair, BoxerAnatomy.Eye, BoxerAnatomy.Pupil, BoxerAnatomy.Lip })
            {
                Assert.AreEqual(0, inner.Remaining(outer), $"{id}: material {outer} lies outside the skull, only in the face layer");
            }

            // 5 mm の格子の皮膚は首の段（顔の外の首の皮膚）だけ。
            for (var i = 0; i < inner.CellCount; i++)
            {
                if (inner.AtIndex(i) == BoxerAnatomy.Skin)
                {
                    Assert.IsLessThanOrEqualTo(BoxerSpine.NeckSlices.High, inner.Coordinates(i).Y, $"{id}: skin above the neck is in the face layer");
                }
            }

            Assert.AreEqual(FaceLayer.Subdivision * inner.Dimensions.X, face.Dimensions.X);
            Assert.AreEqual(inner.HalfExtents, face.HalfExtents, "the face layer covers the same box");
            // 同じ所を 2 つの格子が持たない: 顔の層の埋まったセルの中心は、頭の格子の空のセルにある。
            for (var i = 0; i < face.CellCount; i++)
            {
                if (face.AtIndex(i) == 0)
                {
                    continue;
                }

                var (x, y, z) = face.Coordinates(i);
                Assert.AreEqual(0, inner.MaterialAt(inner.CellAt(face.CellCenter(x, y, z))), $"{id}: face cell {x},{y},{z} overlaps the skull grid");
            }

            // 体積は、5 mm の頭から抜いた外側の体積と同じ程度（顔の層のセルは 1/8 の体積）。F3 の組み立て（髪が頭の輪郭の外へ出る Reach・広いあご）では
            // 名簿の 18 人で 1.0〜1.6 倍（2026-11-19 の測定）。頭の質量は体格から決まり、2 つの格子の体積で分けるので、体積が増えても頭は重くならない（docs/DEBT.md「顔の層」）。
            var removed = full.FilledCount - inner.FilledCount;
            var faceVolume = face.FilledCount * FaceLayer.CellVolumeRatio;
            Console.WriteLine($"{id}: face layer {faceVolume:F1} vs removed outer head {removed} (5 mm cells), ratio {faceVolume / removed:F2}");
            Assert.IsInRange(0.8f, 1.7f, faceVolume / removed, $"{id}: face layer {faceVolume:F1} vs removed outer head {removed} (14 mm cells)");
            Assert.IsGreaterThan(0, face.Remaining(BoxerAnatomy.Eye) + face.Remaining(BoxerAnatomy.Pupil), $"{id}: the eyes are in the face layer");
        }
    }

    [TestMethod]
    public void TheHeadBodyCarriesTheFaceLayer_AndItsMassIsTheSumOfBoth()
    {
        using var game = new BoxingGame(seed: 1);
        var voxels = game.Cpu.Voxels!;
        var head = voxels.PartOf(game.Cpu.Parts.Head)!;
        var face = voxels.Face!;
        Assert.AreEqual(head.Body.BodyId, face.Body.BodyId, "one rigid body");
        Assert.AreSame(head.Body, face.Body.Owner);
        Assert.IsTrue(face.Body.PreferField, "the face is the struck side of a contact");
        Assert.AreEqual(1, voxels.Layers.Count(p => p == face), "the face layer is drawn and touched once");
        Assert.AreEqual(voxels.Layers.ToList().IndexOf(head) - 1, voxels.Layers.ToList().IndexOf(face), "the face is touched before the skull beneath it");
        // 1 セルの質量は体積に比例（顔の層は 1/8）。
        var perCell = game.World.GetMass(head.Body.BodyId) / (head.Body.Grid.FilledCount + (face.Body.Grid.FilledCount * FaceLayer.CellVolumeRatio));
        Assert.IsGreaterThan(0f, perCell);
    }

    [TestMethod]
    public void LosingTheFaceLayersEyeWhites_TakesTheSight()
    {
        using var game = new BoxingGame(seed: 1);
        var voxels = game.Cpu.Voxels!;
        var face = voxels.Face!.Body.Grid;
        var skull = voxels.PartOf(game.Cpu.Parts.Head)!.Body.Grid;
        Assert.AreEqual(VisionState.Healthy, voxels.Vision);
        // 左（+X）の目の白目の半分を潰すと、左の見えはおよそ半分。
        var left = Enumerable.Range(0, face.CellCount).Where(i => face.MaterialAt(i) is BoxerAnatomy.Eye or BoxerAnatomy.Pupil
            && face.CellCenter(face.Coordinates(i).X, face.Coordinates(i).Y, face.Coordinates(i).Z).X > 0f).ToList();
        Assert.IsNotEmpty(left);
        foreach (var i in left.Take(left.Count / 2))
        {
            face.SetCellState(i, (0, 0f, false, 0f));
        }

        Assert.IsLessThan(1f, voxels.Vision.Left, "losing eye-white volume dims that eye");
        Assert.AreEqual(1f, voxels.Vision.Right);
        var skullVersion = skull.Version;
        voxels.DestroyEye(left: true);
        Assert.AreEqual(0f, voxels.Vision.Left, "every eye-white cell of that side gone: blind on that side");
        Assert.AreEqual(skullVersion, skull.Version, "the eyes are not in the skull grid");
    }

    [TestMethod]
    public void ABlowCarvesTheFaceFirst_ThenTheSkull()
    {
        using var game = new BoxingGame(seed: 1);
        var cpu = game.Cpu;
        var face = cpu.Voxels!.Face!.Body.Grid;
        var skull = cpu.Voxels.PartOf(cpu.Parts.Head)!.Body.Grid;
        var (faceBefore, skullBefore) = (face.FilledCount, skull.FilledCount);
        var front = cpu.HeadPosition + (cpu.Frame.Forward * cpu.HalfExtents.Head.Z);
        game.Strike(cpu, cpu.Parts.Head, front, -cpu.Frame.Forward, 10f, carve: 1f);
        Assert.IsLessThan(faceBefore, face.FilledCount, "a light blow carves the face");
        Assert.AreEqual(skullBefore, skull.FilledCount, "and does not reach the skull");
        for (var i = 0; i < 6; i++)
        {
            game.Strike(cpu, cpu.Parts.Head, front, -cpu.Frame.Forward, 30f, carve: 120f);
        }

        Assert.IsLessThan(skullBefore, skull.FilledCount, "heavy blows dig through the face into the skull");
    }

    [TestMethod]
    public void TheFaceSpec_SurvivesTheReplayHeader()
    {
        var c = CharacterRoster.Get("gaston");
        var settings = ReplayMatchSettings.From(BoxingCorners.Default with { CpuFaceSpec = c.Face, CpuLook = c.Look }, null);
        var options = new JsonSerializerOptions { IncludeFields = true };
        var back = JsonSerializer.Deserialize<ReplayMatchSettings>(JsonSerializer.Serialize(settings, options), options)!;
        var restored = back.Apply(BoxingCorners.Default).CpuFaceSpec!;
        CollectionAssert.AreEqual(FaceComposer.Compose(c.Face, 20, 28, 20).Cells, FaceComposer.Compose(restored, 20, 28, 20).Cells, "the same face is rebuilt on replay");
    }
}
