using System.Numerics;
using Aigis.Physics;
using MiniRing.Arena;
using MiniRing.Voxels;

namespace MiniRing.Tests;

/// <summary>
/// 関節のつなぎ（<see cref="JointBridge"/>）: つながった部位の格子の間の隙間にある組織を、当たりとダメージだけのために置く
/// （質量・関節の力を持たず、体の動きを変えない）。刃がつなぎを断てば、つないでいた両側の部位のセルが壊れ、首なら頭が落ちる流れに乗る。
/// </summary>
[TestClass]
public sealed class JointBridgeTests
{
    public TestContext TestContext { get; set; } = null!;

    private static BoxingGame StandingGame()
    {
        var game = new BoxingGame(FighterTuning.Default, 1, new BoxingCorners("stand", _ => new StandGuard(), _ => new StandGuard()));
        for (var i = 0; i < 60; i++)
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
            // 首・脊椎・肩 2・肘 2・腰 2・膝 2・足首 2（関節の種類で選ばない。手首は別の関節ではない）。
            Assert.HasCount(12, bridges);
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

    /// <summary>
    /// 膝: 太ももを固定したまま、すねを膝のまわりに回して隙間を開け（<c>jointlab</c> の姿勢）、日本刀が膝の高さを水平に通る。つなぎが無いと刃は膝の組織を
    /// あまり削れず、すねは落ちない。つなぎがあると膝のそばの組織が削れ、すねが落ちる（<c>docs/arena/jointlab_bend40.md</c> の「つなぎが断ちを変えた振り」から、
    /// つなぎが有って断たれた振りの 3 行。膝の断ちは、刃が止まるか通り抜けるかの連鎖で、逆につなぎが有って断たれなくなる振りも同じ表にある — 数は表のとおり。
    /// 値はシミュレーションが決定的なので固定）。
    /// </summary>
    [TestMethod]
    [TestCategory("slow")]
    public void ABladeThroughTheOpenedKneeGap_CutsTheKnee_OnlyWithTheBridge()
    {
        // 59（顔・手の層と、この枝の滑りの直しのマージ）: 静止した的の立ち方が数 mm 変わり、膝の断ちは刃が止まるか通り抜けるかの鋭い連鎖なので、3 行を
        // 取り直した（`jointlab --joints LeftKnee,RightKnee --bend 40 --speeds 3.4,9`、表は docs/arena/jointlab_bend40_v59.md の「つなぎが断ちを変えた振り」。
        // つなぎ無し → 有りで 断たれない → 断たれる、近くの削れたセル 22 → 87 / 94 → 102 / 73 → 95）。
        // 60（グローブの作り直し 58 とのマージ）: 的の立ち方はグローブの有無で 0.1 mm も動かない（膝の中心 861.1 → 861.0 mm、骨盤 1029.1 → 1029.0 mm、
        // 拳 0.1 mm 以内。2026-11-19 の測定）のに、3.4 m/s の振りは刃が止まるか通り抜けるかの鋭い連鎖で、LeftKnee の窓が -10 mm（つなぎ有りだけ断つ）から
        // -5 mm（つなぎ無しだけ断つ）へ入れ替わった。9 m/s の振りは窓がほぼ同じ（LeftKnee +5 mm・RightKnee +5 mm は同じ行で、近くの削れが 94 → 102・73 → 95 から
        // 66 → 106・73 → 95）。そこで、3.4 m/s の行を 1 mm 刻みで探し直し（`GlovePoseProbe` の掃引: 膝の高さ -10〜+10 mm × 2 速さ × 左右）、
        // つなぎ無し → 有りで 断たれない → 断たれる行に置き換えた（RightKnee front +3 mm 3.4 m/s bend -40: 近くの削れ 58 → 77）。
        var rows = new[]
        {
            (HumanoidRagdoll.RagdollJoint.RightKnee, "front", 0.003f, 3.4f, -40f),
            (HumanoidRagdoll.RagdollJoint.LeftKnee, "front", 0.005f, 9f, 40f),
            (HumanoidRagdoll.RagdollJoint.RightKnee, "front", 0.005f, 9f, -40f),
        };
        foreach (var (joint, dir, offset, speed, bend) in rows)
        {
            var without = JointLab.SlashOnce(joint, dir, offset, speed, bridges: false, bend);
            var with = JointLab.SlashOnce(joint, dir, offset, speed, bridges: true, bend);
            TestContext.WriteLine($"{joint} {dir} {offset * 1000f:0} mm {speed} m/s bend {bend}: without {without}; with {with}");
            Assert.IsFalse(without.Severed, $"{joint} {dir}: the blade does not cut the knee through the gap on its own");
            Assert.IsTrue(with.Severed, $"{joint} {dir}: the bridged knee is cut through and the shin falls");
            Assert.IsGreaterThan(without.NearLost, with.NearLost, $"{joint} {dir}: more of the knee is cut");
        }
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
            var without = JointLab.SlashOnce(HumanoidRagdoll.RagdollJoint.RightElbow, "front", 0f, 9f, bridges: false, bend);
            var with = JointLab.SlashOnce(HumanoidRagdoll.RagdollJoint.RightElbow, "front", 0f, 9f, bridges: true, bend);
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
        var with = JointLab.SlashOnce(HumanoidRagdoll.RagdollJoint.LeftAnkle, "front", 0f, 9f, bridges: true, 40f, "forward");
        TestContext.WriteLine($"ankle: {with}");
        Assert.IsGreaterThan(0, with.BridgeCells, "the bent ankle has a gap that the bridge fills");
        Assert.IsTrue(with.Touched, "the blade touches the ankle");
        Assert.IsGreaterThan(0, with.NearLost, "and cuts some of the ankle");
    }

    /// <summary>
    /// 頭を首の関節のまわりに前へ 35° 倒した静止した相手の首の関節の少し下（胸の上端から 2〜3 mm 下）を、日本刀が水平に通る（<c>necklab --mode slash --arms down --tilt 35</c> の同じ行）。
    /// つなぎが無いと刃は首の組織を断てず頭は落ちない。つなぎがあると首の組織まで断たれ、頭が落ちる。値はシミュレーションが決定的なので固定（合わせ込んだ 2 行）。
    /// </summary>
    [TestMethod]
    [TestCategory("slow")]
    public void ABladeThroughTheBaseOfATippedNeck_TakesTheHeadOff_OnlyWithTheBridge()
    {
        // 49（足の滑りの直し）: 静止した的の立ち方が数 mm 変わったので、side の行の高さを -3 mm から -2 mm へ取り直した（-3 mm は首が断たれたが頭は落ちず、
        // 1 mm ずつ振ると side は -2・+4 mm、front は -3・-1〜+3 mm で頭が落ちる。落ちる高さは 1 mm 刻みで入れ替わる鋭い値で、物理の量ではなく固定した 2 行）。
        // 59（顔・手の層とのマージ）: 頭の外のあご・首の付け根を顔の層が持ち、つなぎは両方の格子を見る（JointBridge.ParentLayers / ChildLayers）。立ち方がまた数 mm
        // 変わったので 2 行を取り直した（`necklab --mode slash --tilt 35 --arms down --offsets -0.005..0.005 --speeds 3.4,9` をつなぎ有り・無しで。
        // 表は docs/arena/necklab_slash_tilt35_v59_on.md・_off.md。つなぎ無しは断たれず頭が残り、有りは頭が落ちる振り: side 3.4 m/s +3 mm（首の削れ 21 → 36）・
        // front 3.4 m/s -5 mm（4 → 12）。ほかに side 3.4 +5・front 9 -2・back 3.4 -2/+2/+4 も同じ。落ちる高さは 1 mm 刻みで入れ替わる鋭い値で、固定した 2 行）。
        // 62（シンプルな顔: 頭の形を頭蓋とあごの塊の距離の場に）: あごの下・首の付け根の顔の層の形が変わったので取り直した（同じ necklab を -6..+6 mm、1 mm 刻み・3.4 m/s で。
        // 表は docs/arena/necklab_slash_tilt35_v62_on.md・_off.md。つなぎ無しは頭が残り、有りは落ちる振り: side +1 mm（首の削れ 6 → 33）・front 0 mm（4 → 12）。
        // ほかに side +5、front +4、back -6・+2 も同じ）。
        foreach (var (dir, offset, speed) in new[] { ("side", 0.001f, 3.4f), ("front", 0f, 3.4f) })
        {
            var without = NeckLab.SlashOnce(dir, offset, speed, bridges: false, tiltDegrees: 35f, armsDown: true);
            var with = NeckLab.SlashOnce(dir, offset, speed, bridges: true, tiltDegrees: 35f, armsDown: true);
            TestContext.WriteLine($"{dir} {speed} m/s: without {without}; with {with}");
            Assert.IsFalse(without.HeadOff, $"{dir}: the blade does not get through the neck on its own");
            Assert.IsTrue(with.HeadOff, $"{dir}: the bridged joint is cut through, and the head drops");
            Assert.IsGreaterThan(without.NeckLost, with.NeckLost, $"{dir}: more of the neck is cut");
        }
    }
}
