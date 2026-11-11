using System.Numerics;
using MiniRing.Bots;
using MiniRing.Voxels;
using MiniRing.Weapons;

namespace MiniRing.Tests;

/// <summary>ボクセルのグローブ: 拳に付けたボクセルの体（革・詰め物・拳）で、拳の打撃は武器と同じ圧力のモデルの接触で起きる。</summary>
[TestClass]
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
    public void Punches_LandThroughTheGloves_AndThePaddingSparesTheSkin()
    {
        // 拳のボット同士 20 秒: 打撃はグローブの接触で数えられ、詰め物が衝撃を広げるので、これまでの拳（エネルギーで掘る）より削れない。
        static (int Hits, int Lost) Bout(bool voxel)
        {
            using var game = new BoxingGame(seed: 1, corners: new BoxingCorners("x", s => BotCatalog.Create("swarmer", s), s => BotCatalog.Create("champion", s), VoxelGloves: voxel));
            var start = game.Player.Voxels!.Parts.Sum(p => p.Body.Grid.FilledCount) + game.Cpu.Voxels!.Parts.Sum(p => p.Body.Grid.FilledCount);
            var hits = 0;
            for (var i = 0; i < 60 * 20; i++)
            {
                game.Step(default, 1f / 60f);
                hits += game.RecentHits.Count;
            }

            return (hits, start - (game.Player.Voxels!.Parts.Sum(p => p.Body.Grid.FilledCount) + game.Cpu.Voxels!.Parts.Sum(p => p.Body.Grid.FilledCount)));
        }

        var gloves = Bout(voxel: true);
        var fists = Bout(voxel: false);
        Assert.IsGreaterThan(10, gloves.Hits, "punches land through the voxel gloves");
        Assert.IsLessThan(fists.Lost, gloves.Lost, $"padded gloves carve less (gloves {gloves.Lost}, old fists {fists.Lost})");
    }

    [TestMethod]
    public void ASword_CutsTheGlove_WhereAPunchDoesNot()
    {
        // 刃は革と詰め物を斬る（柔らかい方が壊れる）。
        using var game = new BoxingGame(seed: 1);
        var glove = game.GloveOf(game.Cpu, Hand.Left)!;
        var before = glove.Body.Grid.FilledCount;
        var at = game.Cpu.GloveCenter(Hand.Left);
        var sword = game.AddWeapon(WeaponKind.IronSword, at + new Vector3(0f, 0.06f, 0f), Quaternion.CreateFromAxisAngle(Vector3.UnitZ, 0f));
        game.World.SetLinearVelocity(sword.Body.BodyId, new Vector3(0f, -5f, 0f));
        for (var i = 0; i < 8; i++)
        {
            game.Step(default, 1f / 240f);
        }

        Assert.IsLessThan(before, glove.Body.Grid.FilledCount, "the blade cuts into the glove");
    }
}
