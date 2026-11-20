using System.Numerics;
using MiniRing.Bots;
using MiniRing.Motion;
using MiniRing.Weapons;

namespace MiniRing.Tests;

/// <summary>
/// 武器同士の試合の場（<see cref="RingLayout"/>: 広いリングで遠間から始める）と、間合いを読んで打って離れる剣士（<see cref="HitAndAway"/>）。
/// </summary>
[TestClass]
public sealed class HitAndAwayTests
{
    private const float Reach = 0.4789f;

    private static readonly MotionFrame Guard = new(new Vector3(-0.2f, -0.57f, 0.78f), new Vector3(0.03f, -0.86f, 0.5f), Vector3.Normalize(new Vector3(0f, 0.6f, 0.8f)),
        Vector3.Normalize(new Vector3(0f, -0.8f, 0.6f)), Vector3.Zero, 0f, Vector3.Zero);

    private static SwordsmanBot Bot(HitAndAway? h) =>
        new([new MotionTechnique("men", WeaponKind.Katana, SwordForms.Katana()[0], 1.268f)], Guard, seed: 1, rate: 1.055f, defense: SwordDefense.None, hitAndAway: h);

    /// <summary>相手は <paramref name="distance"/> 前に構えていて、剣先（握りから 85 cm）は速度 <paramref name="tipVelocity"/> で動いている。</summary>
    private static FighterSense Facing(float distance, Vector3 tipVelocity = default, Vector3 opponentVelocity = default)
    {
        // 自分は武器のリングの真ん中で +Z を向き、相手はその前。
        var at = RingLayout.Weapons.Center with { Y = 2.817f };
        var ahead = at + new Vector3(0f, 0f, distance);
        return new(new BodyFrame(at, 0f), Reach, ahead + new Vector3(0f, 0.2817f, 0f), ahead, OpponentAlive: true,
            SelfHead: at + new Vector3(0f, 0.2254f, 0f), OpponentVelocity: opponentVelocity,
            OpponentBlade: new BladeSense(true, ahead - new Vector3(0f, 0f, 0.2817f), ahead + new Vector3(0.8451f, 0.1408f, -0.2817f), tipVelocity),
            SelfArmed: true, Ring: RingLayout.Weapons);
    }

    [TestMethod]
    public void WeaponBouts_AreFoughtInTheWiderRing_FromFarApart_ButBoxingKeepsItsRing()
    {
        using var swords = new BoxingGame(seed: 1, corners: new BoxingCorners("x", s => new ExternalControl(), s => new ExternalControl(),
            CpuWeapon: WeaponKind.Katana, PlayerWeapon: WeaponKind.Katana, CpuTwoHanded: true, PlayerTwoHanded: true));
        Assert.AreEqual(RingLayout.DrillWeapons, swords.Layout, "without a ring given, the drill ground (the weapon ring before 2026-11-18)");
        Assert.AreEqual(6.761f, swords.Ring.Size, 2.817e-5f);
        var gap = swords.Cpu.PelvisPosition - swords.Player.PelvisPosition;
        Assert.AreEqual(2.817f, new Vector2(gap.X, gap.Z).Length(), 0.0845f, "the two start 2.8 m apart (beyond every technique's range)");

        using var boxing = new BoxingGame(seed: 1);
        Assert.AreEqual(RingLayout.DrillBoxing, boxing.Layout);
        Assert.AreEqual(3.38f, boxing.Ring.Size, 2.817e-5f);
        gap = boxing.Cpu.PelvisPosition - boxing.Player.PelvisPosition;
        Assert.AreEqual(1.239f, new Vector2(gap.X, gap.Z).Length(), 0.0845f);

        // 拳と武器の試合は拳のリング（言われたのは武器同士）。
        using var mixed = new BoxingGame(seed: 1, corners: new BoxingCorners("x", s => new ExternalControl(), s => new ExternalControl(), CpuWeapon: WeaponKind.Katana, CpuTwoHanded: true));
        Assert.AreEqual(RingLayout.DrillBoxing, mixed.Layout);

        // 試合のホストはコーナーから、標準のリング（人の大きさで 5.3 m）で。
        var match = new BoxingCorners("x", s => new ExternalControl(), s => new ExternalControl()).StartingAtCorners();
        Assert.AreEqual(RingLayout.Boxing.AtCorners(), match.Layout);
        Assert.AreEqual(RingLayout.StandardSize, match.Layout.Size, 1e-5f);
    }

    [TestMethod]
    public void TheTable_GrowsWithTheRing_AndItsNearEdgeStaysInFrontOfThePlayer()
    {
        foreach (var layout in new[] { RingLayout.Boxing, RingLayout.Weapons, RingLayout.DrillBoxing, RingLayout.DrillWeapons })
        {
            Assert.AreEqual(0.5634f, layout.Center.Z - layout.TableHalf, 2.817e-5f);
            Assert.IsGreaterThan(layout.Half, layout.PlinthHalf);
            Assert.IsGreaterThan(layout.PlinthHalf, layout.TableHalf);
        }

        Assert.AreEqual(2.6759f, RingLayout.DrillBoxing.Center.Z, 2.817e-5f, "the drill ring stays where the boxing ring was");
        Assert.ThrowsExactly<ArgumentOutOfRangeException>(() => new RingLayout(3.38f, 3.099f).Validated());
    }

    [TestMethod]
    public void AWaitingSwordsman_KeepsOutsideTheOpponentsReach()
    {
        var bot = Bot(new HitAndAway(Patience: 8.392f));
        var sense = Facing(1.408f);
        var safe = SwordsmanBot.OpponentReach(sense, new HitAndAway().Lunge) + new HitAndAway().Margin;
        Assert.IsGreaterThan(1.408f, safe, "a blade 85 cm long on an arm reaches past 1.4 m");
        var command = bot.Read(sense, 1f / 60f);
        Assert.IsLessThan(-0.5f, command.MoveWorld.Z, "it steps back out of reach");
        Assert.IsFalse(bot.Attacking);
        // 遠くにいれば、届かない所の手前まで詰める（離れすぎない）。
        command = bot.Read(Facing(3.38f), 1f / 60f);
        Assert.IsGreaterThan(0.3f, command.MoveWorld.Z);
    }

    [TestMethod]
    public void TheEndOfTheOpponentsSwing_IsAnOpening_ThatSendsItIn()
    {
        var bot = Bot(new HitAndAway(Patience: 8.392f, OpeningTime: 0.6714f));
        var safe = SwordsmanBot.OpponentReach(Facing(1.972f), new HitAndAway().Lunge) + new HitAndAway().Margin;
        // 相手は遠間で空を振っている（剣先は横へ速く。こちらへは来ない）。
        _ = bot.Read(Facing(safe, tipVelocity: new Vector3(6.714f, 0f, 0f)), 1f / 60f);
        Assert.IsFalse(bot.Attacking, "it does not go in while the blade is moving");
        // 振り終えた: 隙と見て詰める。
        var command = bot.Read(Facing(safe), 1f / 60f);
        Assert.IsTrue(bot.Attacking);
        Assert.AreEqual(1, bot.Openings);
        Assert.IsGreaterThan(0.5f, command.MoveWorld.Z, "it steps in toward its technique's range");
    }

    [TestMethod]
    public void AfterItsStrike_ItGetsAway_BackOutOfReach()
    {
        var bot = Bot(new HitAndAway(Patience: 0.08392f));
        // 待ちきれず攻めて出て、技の間合い（1.27 m）で打つ。
        var t = 0f;
        for (; t < 0.3357f && bot.Current is null; t += 1f / 60f)
        {
            _ = bot.Read(Facing(1.268f), 1f / 60f);
        }

        Assert.IsNotNull(bot.Current, "it strikes at its technique's range");
        Assert.AreEqual(1, bot.Pressures);
        // 技を打ち終えるまで進める。
        for (var i = 0; i < 1007 && bot.Current is not null; i++)
        {
            _ = bot.Read(Facing(1.268f), 1f / 60f);
        }

        var command = bot.Read(Facing(1.268f), 1f / 60f);
        Assert.IsTrue(bot.Retreating, "having struck, it pulls back");
        Assert.IsLessThan(-0.5f, command.MoveWorld.Z);
    }

    [TestMethod]
    public void AnOpponentSteppingIntoRange_IsMetAsHeComes()
    {
        var bot = Bot(new HitAndAway(Patience: 8.392f));
        // 相手が技の間合い（1.27 m）へ歩いて入ってくる: 出端を打つ。
        _ = bot.Read(Facing(1.268f, opponentVelocity: new Vector3(0f, 0f, -0.6714f)), 1f / 60f);
        Assert.IsNotNull(bot.Current);
        Assert.AreEqual(1, bot.Debanas);
    }

    [TestMethod]
    public void WithoutHitAndAway_TheBotIsUnchanged()
    {
        // 打って離れない剣士は、これまでどおり遠間（いちばん遠い技 + 23 cm）で構える。
        var bot = Bot(null);
        var command = bot.Read(Facing(1.408f) with { OpponentBlade = default }, 1f / 60f);
        Assert.AreEqual(1.4934f, bot.StandOff, 2.817e-4f);
        Assert.IsGreaterThan(0.5f, command.MoveWorld.Z, "it closes to its technique's range");
        Assert.IsFalse(bot.Attacking);
        Assert.IsFalse(bot.Retreating);
    }

    [TestMethod]
    public void ChasedWhileGettingAway_ItStopsRetreatingAfterAWhile_AndCanStrikeAgain()
    {
        // 再発防止（レビュー指摘）: 下がる最中は打ちに出ないので、遠間まで下がりきれないまま追われ続けると、いつまでも下がっていた。
        var bot = Bot(new HitAndAway(Patience: 0.08392f));
        for (var i = 0; i < 101 && bot.Current is null; i++)
        {
            _ = bot.Read(Facing(1.268f), 1f / 60f);
        }

        for (var i = 0; i < 1007 && bot.Current is not null; i++)
        {
            _ = bot.Read(Facing(1.268f), 1f / 60f);
        }

        Assert.IsTrue(bot.Retreating);
        // 相手はいつまでも 1.27 m の所についてくる（下がりきれない）。
        var used = bot.Used.Count;
        for (var i = 0; i < 403 && bot.Used.Count == used; i++)
        {
            _ = bot.Read(Facing(1.268f), 1f / 60f);
        }

        Assert.IsGreaterThan(used, bot.Used.Count, "within a few seconds it strikes again instead of backing away for good");
    }
}
