using System.Numerics;
using MiniRing.Bots;
using MiniRing.Voxels;
using MiniRing.Weapons;

namespace MiniRing.Tests;

/// <summary>ボクセルのグローブ: 拳に付けたボクセルの体（革・詰め物・拳）で、拳の打撃は武器と同じ圧力のモデルの接触で起きる。</summary>
[TestClass]
[TestCategory("slow")]
public sealed class VoxelGloveTests
{
    [TestMethod]
    public void Boxers_WearVoxelGloves_InTheirCornerColour()
    {
        using var game = new BoxingGame(seed: 1);
        foreach (var hand in new[] { Hand.Left, Hand.Right })
        {
            Assert.IsTrue(game.Player.WearsGlove(hand) && game.Cpu.WearsGlove(hand));
            Assert.IsFalse(game.Player.Wields(hand), "a glove is worn, not wielded");
            Assert.IsFalse(game.Player.BareFist(hand));
        }

        var red = game.GloveOf(game.Player, Hand.Left)!.Body.Grid;
        Assert.IsGreaterThan(0, red.Remaining(BoxerAnatomy.RedGlove));
        Assert.IsGreaterThan(0, red.Remaining(BoxerAnatomy.Padding));
        Assert.AreEqual(0, red.Remaining(BoxerAnatomy.BlueGlove));
        Assert.IsGreaterThan(0, game.GloveOf(game.Cpu, Hand.Right)!.Body.Grid.Remaining(BoxerAnatomy.BlueGlove));
    }

    [TestMethod]
    public void Punches_LandThroughTheVoxelGloves()
    {
        var hits = 0;
        foreach (var seed in new[] { 1, 2, 3 })
        {
            using var game = new BoxingGame(seed: seed, corners: new BoxingCorners("x",
                s => BotCatalog.Create("swarmer", s), s => BotCatalog.Create("champion", s)));
            for (var i = 0; i < 2014; i++)
            {
                game.Step(default, 1f / 60f);
                hits += game.RecentHits.Count;
            }
        }

        Assert.IsGreaterThan(10, hits, "punches land through the voxel gloves");
    }

    [TestMethod]
    public void ASword_CutsTheGlove_WhereAPunchDoesNot()
    {
        // 刃は革と詰め物を斬る（柔らかい方が壊れる）。
        using var game = new BoxingGame(seed: 1);
        var glove = game.GloveOf(game.Cpu, Hand.Left)!;
        var before = glove.Body.Grid.FilledCount;
        var at = game.Cpu.GloveCenter(Hand.Left);
        var sword = game.AddWeapon(WeaponKind.IronSword, at + new Vector3(0f, 0.169f, 0f), Quaternion.CreateFromAxisAngle(Vector3.UnitZ, 0f));
        game.World.SetLinearVelocity(sword.Body.BodyId, new Vector3(0f, -8.392f, 0f));
        for (var i = 0; i < 13; i++)
        {
            game.Step(default, 1f / 240f);
        }

        Assert.IsLessThan(before, glove.Body.Grid.FilledCount, "the blade cuts into the glove");
    }
}
