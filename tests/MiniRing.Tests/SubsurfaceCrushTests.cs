using System.Numerics;
using Aigis.Foundation.Voxels;
using Aigis.Physics;
using Aigis.Physics.Voxels;

namespace MiniRing.Tests;

/// <summary>
/// 面の下へ伝わる圧力（エンジン <see cref="VoxelContact"/>、2026-11-12）: 詰め物の拳は皮膚を破れないが、皮膚の下の肉は潰れて面がへこむ。
/// 拳の後ろで一緒に打ち込む腕と体の質量（massBehind）が無ければ、45 g のグローブそのものの打撃しか無く、何も潰れない。
/// </summary>
[TestClass]
public sealed class SubsurfaceCrushTests
{
    private const byte Skin = 1;
    private const byte Muscle = 2;
    private const byte Leather = 3;
    private const byte Padding = 4;
    private const byte Bone = 5;
    private const float Dt = 1f / 240f;

    // 人形の値（MiniRing.Voxels.BoxerAnatomy と同じ: 人の値 × 相似則 0.32）。
    private static readonly VoxelPalette Palette = new(
    [
        new VoxelMaterial("empty", VoxelTissue.Empty, 1f, false, Vector4.Zero, Vector4.Zero),
        new VoxelMaterial("skin", VoxelTissue.Flesh, 1f, false, Vector4.One, Vector4.One, Mechanics: new VoxelMechanics(1100f, 1.2e6f * 0.32f, 0.005f, Wear: 0.12f)),
        new VoxelMaterial("muscle", VoxelTissue.Flesh, 1f, false, Vector4.One, Vector4.One, Mechanics: new VoxelMechanics(1060f, 0.3e6f * 0.32f, 0.005f)),
        new VoxelMaterial("leather", VoxelTissue.Solid, 1f, false, Vector4.One, Vector4.One, Mechanics: new VoxelMechanics(1100f, 2.0e6f * 0.32f, 0.004f)),
        new VoxelMaterial("padding", VoxelTissue.Solid, 1f, false, Vector4.One, Vector4.One, Mechanics: new VoxelMechanics(120f, 0.15e6f * 0.32f, 0.008f)),
        new VoxelMaterial("bone", VoxelTissue.Bone, 1f, false, Vector4.One, Vector4.One, Mechanics: new VoxelMechanics(1900f, 25e6f * 0.32f, 0.0005f)),
    ]);

    private const float GloveRadius = 0.024f;

    /// <summary>皮膚 5 mm の下が肉の塊（8 cm × 4 cm × 8 cm、5 mm のセル）。上の面が +Y。重い（腕の付いた体の部位の代わり）。</summary>
    private static VoxelBody Flesh(PhysicsWorld world)
    {
        var half = new Vector3(0.04f, 0.02f, 0.04f);
        var grid = new VoxelGrid(Palette, half, 0.005f, (p, _) => p.Y > half.Y - 0.005f ? Skin : Muscle);
        var id = world.AddDynamicBox(Vector3.Zero, Quaternion.Identity, half, 2f);
        return new VoxelBody(world, id, grid);
    }

    /// <summary>グローブ（半径 2.4 cm の球: 外 4 mm が革、その内が詰め物、芯は骨。4 mm のセル、45 g）を、肉の上から −Y へ落とす。</summary>
    private static VoxelBody Glove(PhysicsWorld world, float speed)
    {
        var grid = new VoxelGrid(Palette, new Vector3(GloveRadius), 0.004f, (p, _) =>
        {
            var r = p.Length();
            return r > GloveRadius ? (byte)0 : r > GloveRadius - 0.004f ? Leather : r > 0.012f ? Padding : Bone;
        });
        var id = world.AddDynamicBox(new Vector3(0f, 0.02f + GloveRadius + 0.004f, 0f), Quaternion.Identity, new Vector3(GloveRadius), 0.045f);
        world.SetLinearVelocity(id, new Vector3(0f, -speed, 0f));
        return new VoxelBody(world, id, grid);
    }

    private static (int Removed, int Crushed, int SkinLeft, int SkinBefore, int GloveLost) Punch(float speed, float massBehind)
    {
        var world = new PhysicsWorld(gravity: Vector3.Zero);
        var flesh = Flesh(world);
        var glove = Glove(world, speed);
        world.SetPairCollisionEnabled(flesh.BodyId, glove.BodyId, false);
        var skinBefore = flesh.Grid.Remaining(Skin);
        var gloveBefore = glove.Grid.FilledCount;
        int removed = 0, crushed = 0;
        var topLayer = 0;
        for (var i = 0; i < 60; i++)
        {
            var sg = world.GetState(glove.BodyId);
            var sf = world.GetState(flesh.BodyId);
            world.Step(Dt);
            var r = VoxelContact.Resolve(glove, sg, flesh, sf, massBehindA: massBehind);
            removed += r.RemovedFor(flesh).Count;
            crushed += r.CrushedFor(flesh).Count;
        }

        // 上の面の層（皮膚の元の場所）に残るセル: へこめば減る（皮膚は失われず 1 つ下へ押し込まれる）。
        var (nx, ny, nz) = flesh.Grid.Dimensions;
        for (var z = 0; z < nz; z++)
        {
            for (var x = 0; x < nx; x++)
            {
                topLayer += flesh.Grid.At(x, ny - 1, z) != 0 ? 1 : 0;
            }
        }

        return (removed, crushed, topLayer, skinBefore, gloveBefore - glove.Grid.FilledCount);
    }

    [TestMethod]
    public void APaddedFist_CannotBreakSkin_ButCrushesTheFleshBeneath_AndLeavesADent()
    {
        var (removed, crushed, top, skin, gloveLost) = Punch(speed: 2f, massBehind: 3f);
        Console.WriteLine($"2 m/s with 3 kg behind: removed {removed}, crushed beneath {crushed}, top layer {top}/{skin}, glove lost {gloveLost}");
        Assert.AreEqual(0, removed, "the skin holds: nothing flies off");
        Assert.IsGreaterThan(0, crushed, "the muscle under the skin is crushed");
        Assert.IsLessThan(skin, top, "the skin is pushed in over the crushed flesh: a dent");
        Assert.AreEqual(0, gloveLost, "the glove behind its leather is taken as solid");
    }

    [TestMethod]
    public void TheGloveAlone_WithoutTheArmBehindIt_CrushesNothing()
    {
        var (removed, crushed, _, _, _) = Punch(speed: 2f, massBehind: 0f);
        Console.WriteLine($"2 m/s, glove alone: removed {removed}, crushed {crushed}");
        Assert.AreEqual(0, removed + crushed);
    }

    [TestMethod]
    public void RepeatedPunches_RedderTheSkinBlowByBlow()
    {
        // 同じ所を同じ強さで何発も打つ: 1 発ごとに皮膚の赤み（にじんだ血 = 擦り減り）が増える。へこみが深くなると、次の打撃は縁にも
        // 当たって散るので、赤みはやがて頭打ちになる（測った: 0.13, 0.25, 0.34, 0.40, … 0.52）。裂けるのは擦り減りが尽きたとき（下のテスト）。
        var world = new PhysicsWorld(gravity: Vector3.Zero);
        var flesh = Flesh(world);
        var redness = new List<float>();
        var torn = -1;
        for (var blow = 1; blow <= 20 && torn < 0; blow++)
        {
            // 前の拳と、押された肉の塊を元の所へ戻す（同じ所を打つ）。
            var s = world.GetState(flesh.BodyId);
            world.TranslateBody(flesh.BodyId, -s.Position);
            world.SetLinearVelocity(flesh.BodyId, Vector3.Zero);
            world.SetAngularVelocity(flesh.BodyId, Vector3.Zero);
            var glove = Glove(world, speed: 2f);
            world.SetPairCollisionEnabled(flesh.BodyId, glove.BodyId, false);
            var removed = 0;
            for (var i = 0; i < 40; i++)
            {
                var sg = world.GetState(glove.BodyId);
                var sf = world.GetState(flesh.BodyId);
                world.Step(Dt);
                removed += VoxelContact.Resolve(glove, sg, flesh, sf, massBehindA: 3f).RemovedFor(flesh).Count;
            }

            world.TranslateBody(glove.BodyId, new Vector3(0f, 10f + blow, 0f)); // 打ち終えた拳はどける
            world.SetLinearVelocity(glove.BodyId, Vector3.Zero);
            redness.Add(MostWornSkin(flesh.Grid));
            torn = removed > 0 ? blow : torn;
        }

        Console.WriteLine($"redness after each blow: {string.Join(", ", redness.Select(r => r.ToString("F2", System.Globalization.CultureInfo.InvariantCulture)))}; skin tore at blow {torn}");
        Assert.IsGreaterThan(0f, redness[0], "the first blow already reddens the skin");
        Assert.IsGreaterThan(redness[0], redness[Math.Min(2, redness.Count - 1)], "it reddens more with every blow");
        Assert.AreNotEqual(1, torn, "one blow does not tear it");
        Assert.IsGreaterThan(redness[2] + 0.05f, redness[^1], "and it keeps reddening after that");
    }

    [TestMethod]
    public void WornThrough_TheSkinTears()
    {
        // 擦り減りが丈夫さを使い切ったセルは裂ける（壊れたセルとして出てくる）。その手前までは赤くなるだけ。
        var grid = new VoxelGrid(Palette, new Vector3(0.01f), 0.005f, (_, _) => Skin);
        var removed = new List<RemovedVoxel>();
        var blows = 0;
        while (removed.Count == 0 && blows < 100)
        {
            grid.Wear(0, 0.12f, removed);
            blows++;
        }

        Assert.AreEqual(9, blows, "0.12 of its toughness a blow: worn through on the ninth");
        Assert.AreEqual(Skin, removed[0].Material);
        Assert.AreEqual(0, grid.AtIndex(0));
    }

    /// <summary>皮膚のセルのうち、いちばん赤い（擦り減った）ものの赤み（0..1）。</summary>
    private static float MostWornSkin(VoxelGrid grid)
    {
        var most = 0f;
        var (nx, ny, nz) = grid.Dimensions;
        for (var z = 0; z < nz; z++)
        {
            for (var y = 0; y < ny; y++)
            {
                for (var x = 0; x < nx; x++)
                {
                    most = grid.At(x, y, z) == Skin ? MathF.Max(most, grid.Stain(x, y, z)) : most;
                }
            }
        }

        return most;
    }

    [TestMethod]
    public void HealingBetweenRounds_PutsBackFleshThatADentPushedSkinInto()
    {
        // へこみ: 肝臓のセルが潰れ、その前のセルが押し込まれた（数は減らずに中身が入れ替わる）。休みの再生は、それも元の配置に戻す。
        using var game = new MiniRing.BoxingGame(seed: 1);
        var body = game.Cpu.Voxels!;
        var chest = body.Parts.First(p => p.Kind == MiniRing.BodyPartKind.Chest).Body.Grid;
        var liver = Enumerable.Range(0, chest.CellCount).First(i => chest.AtIndex(i) == MiniRing.Voxels.BoxerAnatomy.Liver);
        var (x, y, z) = chest.Coordinates(liver);
        var skinBefore = chest.Remaining(MiniRing.Voxels.BoxerAnatomy.Skin);
        chest.Break(liver, new List<RemovedVoxel>(), VoxelContact.BruiseStain);
        // 肝臓の隣の埋まったセルを、潰れた所へ押し込む（へこみと同じ入れ替わり）。
        var neighbour = new[] { (1, 0, 0), (-1, 0, 0), (0, 1, 0), (0, -1, 0), (0, 0, 1), (0, 0, -1) }
            .Select(d => (x + d.Item1, y + d.Item2, z + d.Item3))
            .Where(c => c.Item1 >= 0 && c.Item2 >= 0 && c.Item3 >= 0 && c.Item1 < chest.Dimensions.X && c.Item2 < chest.Dimensions.Y && c.Item3 < chest.Dimensions.Z)
            .Select(c => chest.Index(c.Item1, c.Item2, c.Item3))
            .First(i => chest.AtIndex(i) is not 0 and not MiniRing.Voxels.BoxerAnatomy.Liver);
        Assert.IsTrue(chest.Shift(neighbour, liver));
        Assert.IsGreaterThan(0f, body.OrganLoss(MiniRing.Voxels.BoxerAnatomy.Liver));

        body.Regenerate(budget: 100_000);

        Assert.AreEqual(0f, body.OrganLoss(MiniRing.Voxels.BoxerAnatomy.Liver), "the liver is whole again");
        Assert.AreEqual(skinBefore, chest.Remaining(MiniRing.Voxels.BoxerAnatomy.Skin), "and the skin count is back where it was");
    }

    [TestMethod]
    public void AHarderPunch_CrushesMore()
    {
        var soft = Punch(speed: 1.2f, massBehind: 3f).Crushed;
        var hard = Punch(speed: 3f, massBehind: 3f).Crushed;
        Console.WriteLine($"crushed beneath: 1.2 m/s {soft}, 3 m/s {hard}");
        Assert.IsGreaterThan(soft, hard);
    }
}
