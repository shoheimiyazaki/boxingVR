using System.Numerics;
using MiniRing.Bots;
using MiniRing.Voxels;

namespace MiniRing.Tests;

/// <summary>2026-11-12: ボットごとのリングネームと顔、倒れている間に両手を床に着くこと。</summary>
[TestClass]
public sealed class FaceAndNameTests
{
    [TestMethod]
    public void RingNames_AreStablePerBot_AndDifferBetweenDrillIndividuals()
    {
        Assert.AreEqual(BotNames.Generate("champion-g3c2"), BotNames.Generate("champion-g3c2"), "the same individual always has the same name");
        var names = Enumerable.Range(0, 12).Select(i => BotNames.Generate($"champion-g1c{i}")).ToHashSet();
        Assert.IsGreaterThan(8, names.Count, "individuals born in a drill get (mostly) different names");
        Assert.DoesNotContain("・", BotNames.RingNameOf("slugger"), "first names only (no family name)");
        var catalog = BotCatalog.Names.Where(n => n != BotCatalog.Legacy).Select(BotNames.RingNameOf).ToList();
        Console.WriteLine(string.Join(", ", BotCatalog.Names.Where(n => n != BotCatalog.Legacy).Zip(catalog, (n, r) => $"{n}={r}")));
        Assert.HasCount(catalog.Count, catalog.Distinct(), "every bot on the roster has its own name");
    }

    [TestMethod]
    public void Faces_DifferBetweenBots_ButNotTheBody()
    {
        var looks = BotCatalog.Names.Where(n => n != BotCatalog.Legacy).Select(n => FaceLook.For(BotNames.RingNameOf(n))).ToList();
        Assert.IsGreaterThan(3, looks.Distinct().Count(), "bots look different");

        var still = BotCatalog.Boxer with { Attacks = false, DefenseRate = 0f, Prediction = 0f, Aggression = 0f };
        using var plain = new BoxingGame(seed: 1, corners: new BoxingCorners("still", s => new BoxerBot(still, s), s => new BoxerBot(still, s)));
        using var styled = new BoxingGame(seed: 1, corners: new BoxingCorners("slugger", s => new BoxerBot(still, s), s => new BoxerBot(still, s), PlayerName: "jabber"));
        int Filled(Fighter f) => f.Voxels!.Parts.Sum(p => p.Body.Grid.FilledCount);
        Assert.AreEqual(Filled(plain.Player), Filled(styled.Player), "the same body: hair and skin only swap places");
        var head = styled.Cpu.Voxels!.PartOf(styled.Cpu.Parts.Head)!.Body.Grid;
        var look = FaceLook.For(BotNames.RingNameOf("slugger"));
        Assert.AreEqual(new Vector4(look.Skin, 1f), head.Palette[BoxerAnatomy.Skin].Color, "the bot's own skin tone");
    }

    [TestMethod]
    public void ADownedFighter_PutsBothHandsOnTheCanvas_InsteadOfPunching()
    {
        var still = BotCatalog.Boxer with { Attacks = false, DefenseRate = 0f, Prediction = 0f, Aggression = 0f };
        using var game = new BoxingGame(seed: 1, corners: new BoxingCorners("still", s => new BoxerBot(still, s), s => new BoxerBot(still, s)));
        for (var t = 0f; t < 1.5f; t += 1f / 240f)
        {
            game.Step(default, 1f / 240f);
        }

        var f = game.Cpu;
        var standingGloves = (f.GloveCenter(Hand.Left).Y + f.GloveCenter(Hand.Right).Y) * 0.5f;
        f.Concussion = 0.9f; // 倒れるが意識は少し残る（起き上がろうとする）
        for (var t = 0f; t < 2.5f; t += 1f / 240f)
        {
            game.Step(default, 1f / 240f);
        }

        Assert.AreNotEqual(FighterState.Standing, f.State);
        var ground = f.AllParts.Min(id => game.World.GetState(id).Position.Y);
        var gloves = (f.GloveCenter(Hand.Left).Y + f.GloveCenter(Hand.Right).Y) * 0.5f;
        Console.WriteLine($"gloves standing {standingGloves:0.000}, down {gloves:0.000}, lowest part {ground:0.000}, pelvis {f.PelvisPosition.Y:0.000}");
        Assert.IsLessThan(f.PelvisPosition.Y, gloves, "hands below the hips: on the canvas, not up in a guard");
        // 意識が薄い（張りが少ない）ので腕は床まで押し切らないが、構えから大きく下がり、床へ向かう。
        Assert.IsLessThan(standingGloves - (0.5f * (standingGloves - ground)), gloves, "the hands come down towards the canvas, far from the guard");
    }
}
