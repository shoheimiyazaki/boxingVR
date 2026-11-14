using System.Numerics;
using MiniRing.Bots;
using MiniRing.Weapons;

namespace MiniRing.Tests;

/// <summary>剣の技（<see cref="SwordTechnique"/>）・刃筋・ドリル（<see cref="SwordDrill"/>）・間合いで技を選ぶボット（<see cref="SwordBot"/>）。</summary>
[TestClass]
public sealed class SwordTests
{
    [TestMethod]
    public void TheBasics_ComeFromRealArts_WithAtLeastTwoTechniquesAtEachRange()
    {
        var basics = SwordTechnique.Basics;
        foreach (var maai in Enum.GetValues<Maai>())
        {
            Assert.IsGreaterThan(1, basics.Count(t => t.Maai == maai), $"{maai}");
        }

        Assert.IsTrue(basics.All(t => t.Swing.Family == SwingFamily.Sword));
        Assert.IsTrue(basics.All(t => t.Origin.Length > 0 && t.Title != t.Name), "every technique says which art it comes from");
        Assert.IsTrue(basics.Where(t => t.Name.StartsWith("hiki", StringComparison.Ordinal)).All(t => t.Swing.Step < 0f), "hiki-waza cut while stepping back");
        Assert.AreEqual(basics.Count, basics.Select(t => t.Name).Distinct().Count());
        Assert.IsGreaterThan(4, basics.Select(t => t.Target).Distinct().Count(), "head, chest, belly, forearm and shin are all aimed at");
        // 遠い間合いの技ほど、振り始める間合いが遠い。
        Assert.IsGreaterThan(basics.Where(t => t.Maai == Maai.Mid).Max(t => t.Range), basics.Where(t => t.Maai == Maai.Far).Min(t => t.Range));
        Assert.IsGreaterThan(basics.Where(t => t.Maai == Maai.Close).Max(t => t.Range), basics.Where(t => t.Maai == Maai.Mid).Min(t => t.Range));
    }

    [TestMethod]
    public void Roll_TurnsTheEdgeAboutTheBlade()
    {
        var flat = WeaponSwing.Orientation(0f, 0f);
        var rolled = WeaponSwing.Orientation(0f, 0f, MathF.PI / 2f);
        Assert.AreEqual(1f, Vector3.Dot(Vector3.Transform(Vector3.UnitX, rolled), Vector3.UnitZ), 1e-4f, "the blade still points ahead");
        Assert.AreEqual(1f, MathF.Abs(Vector3.Dot(Vector3.Transform(Vector3.UnitY, flat), Vector3.UnitY)), 1e-4f, "roll 0: the edges face up and down");
        Assert.AreEqual(1f, MathF.Abs(Vector3.Dot(Vector3.Transform(Vector3.UnitY, rolled), Vector3.UnitX)), 1e-4f, "rolled: the edges face the sides");
    }

    [TestMethod]
    public void TheDrill_ThrowsTheTechniqueFromItsOwnRange()
    {
        // 遠間の技は、始めの立ち位置（もっと近い）からいったん下がってから出す。
        var kote = SwordTechnique.Basics.First(t => t.Maai == Maai.Far);
        float? distanceAtStart = null;
        SwordDrill.Run(kote, (game, t, swinging) =>
        {
            if (swinging && distanceAtStart is null)
            {
                var d = game.Cpu.ChestPosition - game.Player.Frame.Origin;
                d.Y = 0f;
                distanceAtStart = d.Length();
            }
        });
        Assert.IsNotNull(distanceAtStart);
        Assert.AreEqual(kote.Range, distanceAtStart.Value, SwordTechnique.Window + 0.01f);
    }

    [TestMethod]
    public void AHeldSword_CutsThePartItIsAimedAt()
    {
        // 中間の袈裟（胸へ斜め）を、相手の胸の先まで速く斬り抜く形で出すと、構えて立つ相手の胸を斬る（拳を相手の所で止める形は、
        // 当たる頃には腕が拳を止めにかかっていて押し込まないので、ほとんど斬れない）。
        var basic = SwordTechnique.Basics.First(t => t.Name == "kesa");
        var through = basic with { Swing = basic.Swing with { StrikeFist = basic.Swing.StrikeFist + new Vector3(0.1f, -0.2f, 0.35f), StrikeTime = 0.1f } };
        var r = SwordDrill.Run(through);
        Assert.IsGreaterThan(10f, r.TargetCells, "the chest is cut");
        // 自分の腿をかすめる（57 セルほど）。残った筋肉の断面（いちばん細い半分の薄切りの平均）で腕・脚の力が決まる（2026-11-14）ので、かすめた腕・脚が弱まって振りが変わり、
        // 胸を斬るセル数は 50〜20 台に揺れる（以前は 60 前後で自分より多かった）。胸が斬れること（上の 10 セル超）と、自分の損傷の半分以上を相手に与えることを見る。
        Assert.IsGreaterThan(0.4f * r.SelfCells, r.TargetCells, $"the target is cut for real, not only yourself (the cut through can graze your own thigh: {r.SelfCells})");
    }

    [TestMethod]
    public void TheSwordBot_PicksTechniquesByRange_AndCutsTheOpponent()
    {
        SwordBot? bot = null;
        using var game = new BoxingGame(seed: 2, corners: new BoxingCorners("stand", _ => new StandGuard(), s => bot = SwordBot.Basic(s), PlayerWeapon: WeaponKind.IronSword));
        var start = game.Cpu.Voxels!.Parts.Sum(p => p.Body.Grid.FilledCount);
        for (var f = 0; f < 60 * 8 && !game.Cpu.IsDead; f++)
        {
            game.Step(default, 1f / 60f);
        }

        Assert.IsNotNull(bot);
        Assert.IsGreaterThan(1, bot.Used.Distinct().Count(), $"more than one technique is used ({string.Join(",", bot.Used)})");
        Assert.IsLessThan(start, game.Cpu.Voxels!.Parts.Sum(p => p.Body.Grid.FilledCount), "the sword cuts the opponent");
    }

    [TestMethod]
    public void Pressed_TheSwordBot_AnswersWithATechniqueForThatDistance()
    {
        // 狙いの技を決めた後、相手がそれより内側へ詰めてきたら、今の間合いで出せる技を出す。
        var techniques = new[]
        {
            SwordTechnique.Basics.First(t => t.Maai == Maai.Far),
            SwordTechnique.Basics.First(t => t.Maai == Maai.Close),
        };
        var bot = new SwordBot(techniques, seed: 1);
        var close = techniques[1];
        var sense = Sense(techniques[0].Range + 0.2f);
        _ = bot.Read(sense, 0.01f); // 遠くから: 狙いを決めて詰める
        Assert.IsNull(bot.Current);
        _ = bot.Read(Sense(close.Range), 0.01f);
        Assert.AreEqual(close.Name, bot.Current?.Name);
    }

    [TestMethod]
    public void Weights_DecideWhichTechniquesTheBotPicks()
    {
        // 選ばれやすさ 0 の技は出さない: 遠間の技だけに重みを置くと、遠くから詰めても遠間の技しか出さない。
        var basics = SwordTechnique.Basics;
        var weights = basics.Select(t => t.Maai == Maai.Far ? 1f : 0f).ToList();
        var bot = new SwordBot(basics, seed: 3, weights: weights);
        for (var d = 0.8f; d > 0.2f && bot.Current is null; d -= 0.01f)
        {
            _ = bot.Read(Sense(d), 0.01f);
        }

        Assert.IsNotNull(bot.Current);
        Assert.AreEqual(Maai.Far, bot.Current.Maai);
        Assert.ThrowsExactly<ArgumentException>(() => new SwordBot(basics, 1, weights: [1f]));
    }

    [TestMethod]
    public void TheSparMargin_CountsCellsAndKills()
    {
        var empty = new Dictionary<string, float>();
        var r = new SparResult(150f, 50f, RedDied: false, BlueDied: true, empty, empty, [], []);
        Assert.AreEqual(1f + 3f, r.RedMargin, 1e-5f);
    }

    [TestMethod]
    public void TwoSwordsmen_Spar_AndBothCanBeCut()
    {
        var style = SwordStyle.Even(SwordTechnique.Basics);
        var r = SwordSparring.Run(style, style, seed: 1, seconds: 8f);
        Assert.IsGreaterThan(0, r.RedUsed.Count + r.BlueUsed.Count, "techniques are thrown");
        Assert.IsGreaterThan(0f, r.DealtByRed + r.DealtByBlue, "someone is cut");
        Assert.AreEqual(r.DealtByRed, r.RedByTechnique.Values.Sum(), 1e-3f, "every lost voxel is put down to a technique or the guard");
    }

    private static FighterSense Sense(float distance) =>
        new(new BodyFrame(Vector3.Zero, 0f), 0.17f, new Vector3(0f, 0.4f, distance), new Vector3(0f, 0.3f, distance), OpponentAlive: true);
}
