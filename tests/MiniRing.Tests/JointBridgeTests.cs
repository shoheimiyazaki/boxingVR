using System.Numerics;
using Aigis.Foundation.Voxels;
using Aigis.Physics;
using Aigis.Physics.Voxels;
using MiniRing.Arena;
using MiniRing.Voxels;

namespace MiniRing.Tests;

/// <summary>
/// 関節のつなぎ（<see cref="JointBridge"/>）: つながった部位の格子の間の隙間にある組織を、当たりとダメージだけのために置く
/// （質量・関節の力を持たず、体の動きを変えない）。刃がつなぎを断てば、つないでいた両側の部位のセルが壊れ、首なら頭が落ちる流れに乗る。
/// </summary>
[TestClass]
[TestCategory("slow")]
public sealed class JointBridgeTests
{
    public TestContext TestContext { get; set; } = null!;

    private static BoxingGame StandingGame()
    {
        var game = new BoxingGame(FighterTuning.Default, 1, new BoxingCorners("stand", _ => new StandGuard(), _ => new StandGuard()));
        for (var i = 0; i < 101; i++)
        {
            game.Step(default, 1f / 60f);
        }

        return game;
    }

    [TestMethod]
    public void EveryJoint_HasABridge_ThatBorrowsItsParentsBodyAndOwnsNoMass()
    {
        using var game = StandingGame();
        foreach (var fighter in new[] { game.Player, game.Cpu })
        {
            var bridges = fighter.Voxels!.Bridges;
            // 首・脊椎・肩 2・肘 2・腰 2・膝 2・足首 2（関節の種類で選ばない。手首は別の関節ではない）。分割胴は胸と腹の間の背骨の関節がもう 1 つ。
            Assert.HasCount(fighter.Parts.Abdomen is null ? 12 : 13, bridges);
            foreach (var bridge in bridges)
            {
                Assert.AreEqual(bridge.Parent.Body.BodyId, bridge.Body.BodyId, $"{bridge.Joint}: rides the parent's rigid body (no body of its own)");
                Assert.IsTrue(bridge.Body.Massless, $"{bridge.Joint}: massless");
                var mass = game.World.GetMass(bridge.Parent.Body.BodyId);
                var box = game.World.GetState(bridge.Parent.Body.BodyId);
                bridge.Refresh(_ => true);
                bridge.Body.UpdateMass(); // 何もしない（親の剛体の質量・形を変えない）
                Assert.AreEqual(mass, game.World.GetMass(bridge.Parent.Body.BodyId), 0f, $"{bridge.Joint}: the parent's mass is untouched");
                Assert.AreEqual(box, game.World.GetState(bridge.Parent.Body.BodyId));
            }
        }
    }

    /// <summary>
    /// CT から作った体は、部位のまわり（<see cref="BoxerVoxelOverrides.HaloFor"/>: 切れ目の向こうにつながる組織）を持つ。首のつなぎの隙間のセルは、
    /// 親（胸）のまわりのその所の組織になる（ここでは全部を動脈にしたまわり）。まわりが無ければ今までどおり、近い 2 つのセルの強い方（動脈は出てこない）。
    /// </summary>
    [TestMethod]
    public void TheNeckGap_TakesItsTissueFromTheParentsHalo_WhenThereIsOne()
    {
        using var game = StandingGame();
        var p = game.Cpu.Parts;
        var chest = game.World.GetState(p.Chest);
        var joint = chest.Position + (Vector3.Transform(Vector3.UnitY, chest.Orientation) * game.Cpu.HalfExtentsOf(p.Chest).Y);
        var q = Quaternion.CreateFromAxisAngle(game.Cpu.Frame.Right, 40f * MathF.PI / 180f);
        var head = game.World.GetState(p.Head);
        game.World.SetDynamicPose(p.Head, joint + Vector3.Transform(head.Position - joint, q), Quaternion.Concatenate(head.Orientation, q));
        var neck = game.Cpu.Voxels!.Bridges.Single(b => b.Joint == HumanoidRagdoll.RagdollJoint.Neck);
        var anchor = game.Cpu.BridgeJoints().Single(j => j.Joint == HumanoidRagdoll.RagdollJoint.Neck).AnchorOnParent;
        var (x, y, z) = neck.Parent.Body.Grid.Dimensions;
        var m = BoxerVoxelOverrides.HaloMargin;
        var dims = (x + (2 * m), y + (2 * m), z + (2 * m));
        var halo = new BoxerVoxelOverrides.PartCells(dims, Enumerable.Repeat(BoxerAnatomy.Artery, dims.Item1 * dims.Item2 * dims.Item3).ToArray());
        var withHalo = new JointBridge(neck.Joint, neck.Parent, neck.Child, anchor, neck.ParentLayers, neck.ChildLayers, parentHalo: halo);

        neck.Refresh(_ => true);
        withHalo.Refresh(_ => true);
        int Count(JointBridge b, Func<byte, bool> which) =>
            Enumerable.Range(0, b.Body.Grid.CellCount).Count(i => b.Body.Grid.MaterialAt(i) is var c && c != 0 && which(c));
        TestContext.WriteLine($"neck gap cells: without a halo {neck.ActiveCells}, with {withHalo.ActiveCells}");
        Assert.AreEqual(neck.ActiveCells, withHalo.ActiveCells, "the halo changes what fills the gap, not where the gap is");
        Assert.IsGreaterThan(10, withHalo.ActiveCells);
        Assert.AreEqual(withHalo.ActiveCells, Count(withHalo, c => c == BoxerAnatomy.Artery), "every gap cell is the halo's tissue there");
        Assert.AreEqual(0, Count(neck, c => c == BoxerAnatomy.Artery), "without a halo the gap takes the nearer parts' cells");

        // 並びの確かめ: まわりの 1 セルだけを動脈にすると、隙間のうち、親の格子でそのセルに当たる 1 セルだけが動脈になる（1 セルずれ・軸の取り違えがあれば外れる）。
        var grid = withHalo.Body.Grid;
        var gap = Enumerable.Range(0, grid.CellCount).First(i => grid.MaterialAt(i) != 0);
        var (gx, gy, gz) = grid.Coordinates(gap);
        var parentGrid = neck.Parent.Body.Grid;
        var inParent = grid.CellCenter(gx, gy, gz) + withHalo.Body.GridCenterLocal - neck.Parent.Body.GridCenterLocal;
        var c = (inParent + parentGrid.HalfExtents) / parentGrid.CellSize;
        var (px, py, pz) = ((int)MathF.Floor(c.X) + m, (int)MathF.Floor(c.Y) + m, (int)MathF.Floor(c.Z) + m);
        var one = new byte[dims.Item1 * dims.Item2 * dims.Item3];
        one[px + (dims.Item1 * (py + (dims.Item2 * pz)))] = BoxerAnatomy.Artery;
        var single = new JointBridge(neck.Joint, neck.Parent, neck.Child, anchor, neck.ParentLayers, neck.ChildLayers, parentHalo: new BoxerVoxelOverrides.PartCells(dims, one));
        single.Refresh(_ => true);
        Assert.AreEqual(1, Count(single, b => b == BoxerAnatomy.Artery), "one halo cell gives one gap cell");
        Assert.AreEqual(BoxerAnatomy.Artery, single.Body.Grid.MaterialAt(gap), "and it is the gap cell over that parent cell");
    }

    [TestMethod]
    public void TheNeckBridge_FillsTheGapBetweenHeadAndChest_StandingAndTipped()
    {
        int Count(float tiltDegrees)
        {
            using var game = StandingGame();
            var p = game.Cpu.Parts;
            var chest = game.World.GetState(p.Chest);
            var joint = chest.Position + (Vector3.Transform(Vector3.UnitY, chest.Orientation) * game.Cpu.HalfExtentsOf(p.Chest).Y);
            var q = Quaternion.CreateFromAxisAngle(game.Cpu.Frame.Right, tiltDegrees * MathF.PI / 180f);
            var head = game.World.GetState(p.Head);
            game.World.SetDynamicPose(p.Head, joint + Vector3.Transform(head.Position - joint, q), Quaternion.Concatenate(head.Orientation, q));
            var neck = game.Cpu.Voxels!.Bridges.Single(b => b.Joint == Aigis.Physics.HumanoidRagdoll.RagdollJoint.Neck);
            neck.Refresh(_ => true);
            return neck.ActiveCells;
        }

        var upright = Count(0f);
        var tilted = Count(40f);
        TestContext.WriteLine($"neck bridge cells: upright {upright}, head tilted 40 degrees {tilted}");
        // 首の隙間（あごと肩の間の切れ込みなど）は立っていても開いていて、つなぎが埋める。頭を倒してもつなぎは残る（姿勢ごとに、いまの 2 つの格子から作り直す）。
        Assert.IsGreaterThan(10, upright, "the notch between head and chest is bridged while standing");
        Assert.IsGreaterThan(10, tilted, "and still bridged with the head tipped about the joint");
    }

    [TestMethod]
    [DataRow(HumanoidRagdoll.RagdollJoint.RightKnee, -40f)]
    [DataRow(HumanoidRagdoll.RagdollJoint.Neck, 35f)]
    public void CuttingAGapBridge_DamagesBothConnectedParts(HumanoidRagdoll.RagdollJoint joint, float bend)
    {
        var stage = JointLab.Prepare(joint, "front", 0f, bridges: true, bend, "right");
        using var game = stage.Game;
        var bridge = game.Cpu.Voxels!.Bridges.Single(b => b.Joint == joint);
        var grid = bridge.Body.Grid;
        Assert.IsGreaterThan(0, bridge.ActiveCells, "the flexed joint has filled gap cells");
        var removed = new List<RemovedVoxel>();
        var sourceCells = bridge.ParentLayers.Concat(bridge.ChildLayers).Sum(part => part.Body.Grid.FilledCount);
        for (var i = 0; i < grid.CellCount; i++)
        {
            grid.Break(i, removed);
        }

        // 接触で除去されたつなぎのセルから、親と子へ欠損が伝わる部分を直接検証する。
        var hit = new VoxelContactResult(true, stage.Weapon.Body, bridge.Body, [], removed, stage.Center, Vector3.Zero, stage.Heading, 0f, 0f, 0f, false, null, null);
        var damaged = bridge.Sever(hit);
        Assert.IsTrue(damaged.Any(entry => bridge.ParentLayers.Contains(entry.Part)), "the parent side is cut");
        Assert.IsTrue(damaged.Any(entry => bridge.ChildLayers.Contains(entry.Part)), "the child side is cut");
        Assert.AreEqual(sourceCells - damaged.Sum(entry => entry.Cells.Count),
            bridge.ParentLayers.Concat(bridge.ChildLayers).Sum(part => part.Body.Grid.FilledCount));
        Assert.IsEmpty(bridge.Sever(hit), "the same removed bridge cells cannot damage the joint again");
    }

    /// <summary>
    /// 肘: 腕の 2 つの部位の格子は肘で重なっていて、曲げても隙間が開かない（つなぎのセルが 0 個）。だからつなぎは何も足さず、有っても無くても結果は同じ
    /// （<c>jointlab</c> の表: 肘は 150 振り × つなぎの有無で、つなぎのセルは 0 個）。隙間を通り抜ける問題が肘には無い、という測定。
    /// </summary>
    [TestMethod]
    [TestCategory("slow")]
    public void TheElbow_HasNoGapToSlipThrough_SoTheBridgeAddsNothing()
    {
        foreach (var bend in new[] { 0f, 40f, -80f })
        {
            var without = JointLab.SlashOnce(HumanoidRagdoll.RagdollJoint.RightElbow, "front", 0f, 15.11f, bridges: false, bend);
            var with = JointLab.SlashOnce(HumanoidRagdoll.RagdollJoint.RightElbow, "front", 0f, 15.11f, bridges: true, bend);
            TestContext.WriteLine($"elbow bend {bend}: without {without}; with {with}");
            Assert.AreEqual(0, with.BridgeCells, $"bend {bend}: the elbow parts overlap, nothing to bridge");
            Assert.AreEqual(without, with, $"bend {bend}: no bridge cells, so the same cut");
        }
    }

    /// <summary>
    /// 足首（靴の中のつなぎ目）にも、ほかの関節と同じつなぎがある。足首は曲げても隙間が数セルしか開かず（<c>jointlab</c> の表: 1〜9 セル）、
    /// 刃が通り抜ける隙間は小さいので、つなぎで断ちが増えるとは言えない（表では増える振りも減る振りもある）。ここでは、つなぎが有ることと、
    /// 隙間を通った刃がつなぎに触れて何かを削ることだけを確かめる。
    /// </summary>
    [TestMethod]
    [TestCategory("slow")]
    public void TheAnkle_HasABridge_ThatABladeThroughTheGapTouches()
    {
        var with = JointLab.SlashOnce(HumanoidRagdoll.RagdollJoint.LeftAnkle, "front", 0f, 15.11f, bridges: true, 40f, "forward");
        TestContext.WriteLine($"ankle: {with}");
        Assert.IsGreaterThan(0, with.BridgeCells, "the bent ankle has a gap that the bridge fills");
        Assert.IsTrue(with.Touched, "the blade touches the ankle");
        Assert.IsGreaterThan(0, with.NearLost, "and cuts some of the ankle");
    }

}
