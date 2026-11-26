using Aigis.Foundation.Voxels;
using MiniRing.Bots;
using MiniRing.Voxels;

namespace MiniRing.Tests;

/// <summary>
/// つながりで分かれる体（docs/VOXEL_DAMAGE.md「つながりで分かれる体」）: 部位の格子の物質がつながっていなければ別の剛体に分かれ、関節の面の物質が
/// まるごと断たれれば関節が外れる。分かれた後に体として駆動するのは頭とつながった側だけ。部位の種類で分けない。
/// </summary>
[TestClass]
[TestCategory("slow")]
public sealed class VoxelSplitTests
{
    private const float Dt = 1f / 60f;

    private static BoxingGame Game(ControlFactory? cpu = null) =>
        new(seed: 1, corners: new BoxingCorners("guard", cpu ?? (_ => new StandGuard()), _ => new StandGuard(), Referee: false, BreakActions: false));

    private static void Play(BoxingGame game, float seconds)
    {
        for (var t = 0f; t < seconds; t += Dt)
        {
            game.Step(default, Dt);
        }
    }

    /// <summary>格子 <paramref name="grid"/> の、Y の段 <paramref name="rows"/> にあるセルを全部壊す（刃が水平に断ち切った所の代わり）。</summary>
    private static void CutRows(VoxelGrid grid, params int[] rows)
    {
        var removed = new List<RemovedVoxel>();
        for (var i = 0; i < grid.CellCount; i++)
        {
            if (grid.MaterialAt(i) != 0 && rows.Contains(grid.Coordinates(i).Y))
            {
                grid.Break(i, removed);
            }
        }
    }

    private static VoxelPart Part(Fighter fighter, int id) => fighter.Voxels!.PartOf(id)!;

    [TestMethod]
    public void EveryJoint_IsHeldByTheCellsThatTouchAcrossIt_WhenTheBodyIsBuilt()
    {
        using var game = Game();
        var fighter = game.Cpu;
        foreach (var part in fighter.Voxels!.Layers)
        {
            Assert.IsTrue(part.Body.Grid.IsConnected(), $"{part.Kind}: 作ったばかりの格子は 1 つのつながり（最初の歩で黙って分かれない）");
        }

        foreach (var (joint, parent, child, _) in fighter.BridgeJoints())
        {
            var (total, held) = fighter.Voxels!.JointContactCount(parent, child);
            Assert.IsTrue(total > 0, $"{joint}: 関節をまたいで接するセルの組がある");
            Assert.AreEqual(total, held, $"{joint}: 無傷の体では全部の組が残っている");
            Assert.IsTrue(fighter.Voxels.JointHeld(parent, child), $"{joint}: つながっている");
        }
    }

    /// <summary>不変量: 胴・頭のどの段を断ち切っても、次の歩には体のどの格子（部位も破片も）も 1 つのつながりだけを持つ。</summary>
    [TestMethod]
    public void AfterAnyCutThroughTheTrunk_EveryGridIsOnePiece()
    {
        foreach (var kind in new[] { BodyPartKind.Head, BodyPartKind.Chest, BodyPartKind.Abdomen, BodyPartKind.Pelvis })
        {
            using var probe = Game();
            var rows = probe.Cpu.Voxels!.Parts.First(p => p.Kind == kind).Body.Grid.Dimensions.Y;
            foreach (var row in new[] { 1, rows / 3, rows / 2, (2 * rows) / 3, rows - 2 })
            {
                using var game = Game();
                Play(game, 0.25f);
                var fighter = game.Cpu;
                CutRows(fighter.Voxels!.Parts.First(p => p.Kind == kind).Body.Grid, row);
                game.Step(default, Dt);
                foreach (var part in fighter.Voxels.Parts.Concat(fighter.Voxels.Fragments))
                {
                    Assert.IsTrue(part.Body.Grid.ConnectedComponents().Count <= 1,
                        $"{kind} の段 {row} を断った: {part.Kind}（{part.Body.BodyId}）の物質は 1 つのつながり");
                }
            }
        }
    }

    [TestMethod]
    public void ASplitChest_KeepsItsMass_InThePieces()
    {
        using var game = Game();
        Play(game, 0.25f);
        var fighter = game.Cpu;
        var chest = Part(fighter, fighter.Parts.Chest).Body;
        CutRows(chest.Grid, chest.Grid.Dimensions.Y / 2);
        chest.UpdateMass(); // 削れた後の質量（分ける前）
        var before = game.World.GetMass(chest.BodyId);
        var known = fighter.Voxels!.Fragments.Select(f => f.Body).ToHashSet();
        game.Step(default, Dt);
        var pieces = fighter.Voxels.Fragments.Where(f => !known.Contains(f.Body) && f.FragmentOf == chest.BodyId).ToList();
        Assert.IsTrue(pieces.Count >= 1, "胸の真ん中を断ち切ると、胸は分かれる");
        var after = game.World.GetMass(chest.BodyId) + pieces.Sum(p => game.World.GetMass(p.Body.BodyId));
        Assert.AreEqual(before, after, before * 1e-4f, "分かれた剛体の質量の和は、分ける前の質量と同じ（質量はセルの体積に比例）");
        Assert.IsTrue(pieces.All(p => game.World.GetMass(p.Body.BodyId) < before), "どの破片も元より軽い");
    }

    /// <summary>
    /// 腹と骨盤の間（関節の面）を断ち切る: 骨盤から下は頭とつながらなくなり、力の抜けた物になる（脚の筋は駆動しない）。
    /// 頭・胸・腕は体のまま動き、死なない（臓器は断たれていない）。脚が頭とつながっていないので、体は立っていられず崩れ落ちる。
    /// </summary>
    [TestMethod]
    public void CuttingTheWaist_LeavesOnlyTheHeadSideDriven()
    {
        using var game = Game();
        Play(game, 0.5f);
        var fighter = game.Cpu;
        var p = fighter.Parts;
        var standingChest = game.World.GetState(p.Chest).Position.Y;
        var abdomen = Part(fighter, p.Abdomen!.Value).Body.Grid;
        var pelvis = Part(fighter, p.Pelvis).Body.Grid;
        CutRows(abdomen, 0, 1);
        CutRows(pelvis, pelvis.Dimensions.Y - 2, pelvis.Dimensions.Y - 1);
        Assert.IsFalse(fighter.Voxels!.JointHeld(p.Abdomen!.Value, p.Pelvis), "関節の面の物質はもう無い");
        game.Step(default, Dt);

        foreach (var id in new[] { p.Pelvis, p.LeftThigh, p.RightThigh, p.LeftShin, p.RightShin })
        {
            Assert.IsFalse(fighter.Owns(id), $"{fighter.KindOf(id)} は体から外れた");
            Assert.AreEqual(0f, fighter.Strength(id), $"{fighter.KindOf(id)} に体の筋の力は出ない");
        }

        foreach (var id in new[] { p.Head, p.Chest, p.Abdomen!.Value, p.LeftUpperArm, p.RightForearm })
        {
            Assert.IsTrue(fighter.Owns(id), $"{fighter.KindOf(id)} は頭とつながった体のまま");
        }

        Assert.IsTrue(fighter.Strength(p.LeftUpperArm) > 0.9f, "腕の力はそのまま");
        Play(game, 2f);
        Assert.IsFalse(fighter.IsDead, "臓器も脳も無傷なので、死なない");
        Assert.IsTrue(game.World.GetState(p.Chest).Position.Y < standingChest - 0.4f, "支える脚が頭とつながっていないので、上体は崩れ落ちる");
        Assert.IsTrue(fighter.IsKnockedDown || fighter.State != FighterState.Standing, "倒れた体として数える");
    }

    /// <summary>
    /// 胸を心臓より上（首の側）で断ち切る: 首の側（頭とつながる側）が胸として残り、心臓のある下の側は破片になって体から外れる。
    /// 心臓のセルは体に残らないので、今の臓器のモデル（半分より多く失えば死ぬ）のまま死ぬ。
    /// </summary>
    [TestMethod]
    public void CuttingTheChestAboveTheHeart_KeepsTheNeckSide_AndTheLostHeartKills()
    {
        using var game = Game();
        Play(game, 0.25f);
        var fighter = game.Cpu;
        var p = fighter.Parts;
        var chest = Part(fighter, p.Chest).Body.Grid;
        var heartTop = Enumerable.Range(0, chest.CellCount).Where(i => chest.MaterialAt(i) == BoxerAnatomy.Heart).Max(i => chest.Coordinates(i).Y);
        CutRows(chest, heartTop + 2);
        game.Step(default, Dt);
        Assert.IsTrue(fighter.Owns(p.Head), "頭は胸の残り（首の側）につながったまま");
        Assert.AreEqual(0, chest.Remaining(BoxerAnatomy.Heart), "心臓は下の破片へ移った");
        Assert.IsFalse(fighter.Owns(p.Pelvis), "胸の下の側から先（腹・骨盤・脚）は体から外れた");
        Play(game, 0.1f);
        Assert.AreEqual("heart", fighter.DeathCause, $"心臓を失った体は死ぬ（臓器のモデルのまま）。心臓 {fighter.Voxels!.Parts.Sum(q => q.Body.Grid.Remaining(BoxerAnatomy.Heart))}/{fighter.Voxels!.Parts.Sum(q => q.Body.Grid.Initial(BoxerAnatomy.Heart))}");
    }

    /// <summary>
    /// 関節のつなぎを描く（<see cref="JointBridge.View"/>: 関節の隙間を埋める組織を、関節がつながっている間だけ見せる）のは見た目だけ: 描いても描かなくても
    /// 試合はビット単位で同じ（当たりに使うつなぎの格子とは別の格子に作る）。描いたつなぎには、関節の組織のセルがある。
    /// </summary>
    [TestMethod]
    public void DrawingTheJointBridges_ShowsTheJointTissue_WithoutChangingTheMatch()
    {
        using var drawn = Game(seed => BotCatalog.Create("champion", seed));
        using var plain = Game(seed => BotCatalog.Create("champion", seed));
        var draws = new List<Aigis.Render.Voxels.VoxelVolumeDraw>();
        var tissue = 0;
        for (var t = 0f; t < 1.5f; t += Dt)
        {
            drawn.Step(default, Dt);
            plain.Step(default, Dt);
            BoxingRenderer.FillVoxels(drawn, draws);
            tissue = Math.Max(tissue, drawn.Cpu.Voxels!.Bridges.Sum(b => b.View().Body.Grid.FilledCount));
        }

        Assert.IsTrue(tissue > 0, "曲がった関節の隙間を埋める組織のセルが描かれる");
        foreach (var id in drawn.Cpu.AllParts)
        {
            Assert.AreEqual(plain.World.GetState(id).Position, drawn.World.GetState(id).Position, $"{drawn.Cpu.KindOf(id)}: 描いても動きは同じ");
        }
    }

    /// <summary>腰から下を失った相手を、ボット（技・倒れた相手への攻め）が例外を出さずに狙い続ける。</summary>
    [TestMethod]
    public void ABot_KeepsFightingABodyCutAtTheWaist()
    {
        using var game = new BoxingGame(seed: 3, corners: BotCatalog.Corners("champion") with { Player = _ => new StandGuard(), Referee = false, BreakActions = false });
        Play(game, 0.5f);
        var victim = game.Player;
        var p = victim.Parts;
        CutRows(Part(victim, p.Abdomen!.Value).Body.Grid, 0, 1);
        var pelvis = Part(victim, p.Pelvis).Body.Grid;
        CutRows(pelvis, pelvis.Dimensions.Y - 2, pelvis.Dimensions.Y - 1);
        Play(game, 6f);
        Assert.IsFalse(victim.Owns(p.Pelvis));
        Assert.IsTrue(victim.Owns(p.Head));
    }
}
