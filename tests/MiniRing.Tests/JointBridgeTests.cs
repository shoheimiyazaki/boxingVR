using System.Numerics;
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
            // 首・脊椎・肩 2・肘 2・腰 2・膝 2（足首は靴の中）。
            Assert.HasCount(10, bridges);
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
        Assert.IsGreaterThan(40, upright, "the notch between head and chest is bridged while standing");
        Assert.IsGreaterThan(40, tilted, "and still bridged with the head tipped about the joint");
    }

    /// <summary>
    /// 日本刀が静止した相手の首の関節の少し下（胸の上端の高さから 3 mm・9 mm 下）を、後ろから水平に 3.4 m/s で通る。つなぎが無いと刃は首の骨の手前の胸の上端の肉で
    /// 止まり、首は無傷のまま（頭は落ちない）。つなぎがあると首の骨まで断たれて頭が落ちる。necklab の表（<c>necklab --mode slash --arms down</c>）の同じ行。
    /// </summary>
    [TestMethod]
    [TestCategory("slow")]
    public void ABladeThroughTheBaseOfTheNeck_TakesTheHeadOff_OnlyWithTheBridge()
    {
        foreach (var offset in new[] { -0.009f, -0.003f })
        {
            var without = NeckLab.SlashOnce("back", offset, 3.4f, bridges: false, armsDown: true);
            var with = NeckLab.SlashOnce("back", offset, 3.4f, bridges: true, armsDown: true);
            TestContext.WriteLine($"offset {offset * 1000f:0} mm: without {without}; with {with}");
            Assert.IsFalse(without.HeadOff, $"{offset}: the blade does not get through the neck on its own");
            Assert.IsTrue(with.HeadOff, $"{offset}: the bridged joint is cut through, and the head drops");
            Assert.IsGreaterThan(without.NeckLost, with.NeckLost, $"{offset}: more of the neck is cut");
        }
    }
}
