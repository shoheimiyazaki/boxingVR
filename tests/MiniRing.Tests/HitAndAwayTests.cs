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
    private const float Reach = 0.17f;

    private static readonly MotionFrame Guard = new(new Vector3(-0.2f, -0.57f, 0.78f), new Vector3(0.03f, -0.86f, 0.5f), Vector3.Normalize(new Vector3(0f, 0.6f, 0.8f)),
        Vector3.Normalize(new Vector3(0f, -0.8f, 0.6f)), Vector3.Zero, 0f, Vector3.Zero);

    private static SwordsmanBot Bot(HitAndAway? h) =>
        new([new MotionTechnique("men", WeaponKind.Katana, SwordForms.Katana()[0], 0.45f)], Guard, seed: 1, rate: 1.77f, defense: SwordDefense.None, hitAndAway: h);

    /// <summary>相手は <paramref name="distance"/> 前に構えていて、剣先（握りから 30 cm）は速度 <paramref name="tipVelocity"/> で動いている。</summary>
    private static FighterSense Facing(float distance, Vector3 tipVelocity = default, Vector3 opponentVelocity = default)
    {
        // 自分は武器のリングの真ん中で +Z を向き、相手はその前。
        var at = RingLayout.Weapons.Center with { Y = 1f };
        var ahead = at + new Vector3(0f, 0f, distance);
        return new(new BodyFrame(at, 0f), Reach, ahead + new Vector3(0f, 0.1f, 0f), ahead, OpponentAlive: true,
            SelfHead: at + new Vector3(0f, 0.08f, 0f), OpponentVelocity: opponentVelocity,
            OpponentBlade: new BladeSense(true, ahead - new Vector3(0f, 0f, 0.1f), ahead + new Vector3(0.3f, 0.05f, -0.1f), tipVelocity),
            SelfArmed: true, Ring: RingLayout.Weapons);
    }

    [TestMethod]
    public void WeaponBouts_AreFoughtInTheWiderRing_FromFarApart_ButBoxingKeepsItsRing()
    {
        using var swords = new BoxingGame(seed: 1, corners: new BoxingCorners("x", s => new ExternalControl(), s => new ExternalControl(),
            CpuWeapon: WeaponKind.Katana, PlayerWeapon: WeaponKind.Katana, CpuTwoHanded: true, PlayerTwoHanded: true));
        Assert.AreEqual(RingLayout.Weapons, swords.Layout);
        Assert.AreEqual(2.4f, swords.Ring.Size, 1e-5f);
        var gap = swords.Cpu.PelvisPosition - swords.Player.PelvisPosition;
        Assert.AreEqual(1.0f, new Vector2(gap.X, gap.Z).Length(), 0.03f, "the two start 1 m apart (beyond every technique's range)");

        using var boxing = new BoxingGame(seed: 1);
        Assert.AreEqual(RingLayout.Boxing, boxing.Layout);
        Assert.AreEqual(1.2f, boxing.Ring.Size, 1e-5f);
        gap = boxing.Cpu.PelvisPosition - boxing.Player.PelvisPosition;
        Assert.AreEqual(0.44f, new Vector2(gap.X, gap.Z).Length(), 0.03f);

        // 拳と武器の試合は拳のリング（言われたのは武器同士）。
        using var mixed = new BoxingGame(seed: 1, corners: new BoxingCorners("x", s => new ExternalControl(), s => new ExternalControl(), CpuWeapon: WeaponKind.Katana, CpuTwoHanded: true));
        Assert.AreEqual(RingLayout.Boxing, mixed.Layout);
    }

    [TestMethod]
    public void TheTable_GrowsWithTheRing_AndItsNearEdgeStaysInFrontOfThePlayer()
    {
        foreach (var layout in new[] { RingLayout.Boxing, RingLayout.Weapons })
        {
            Assert.AreEqual(0.2f, layout.Center.Z - layout.TableHalf, 1e-5f);
            Assert.IsGreaterThan(layout.Half, layout.PlinthHalf);
            Assert.IsGreaterThan(layout.PlinthHalf, layout.TableHalf);
        }

        Assert.AreEqual(0.95f, RingLayout.Boxing.Center.Z, 1e-5f, "the boxing ring stays where it was");
        Assert.ThrowsExactly<ArgumentOutOfRangeException>(() => new RingLayout(1.2f, 1.1f).Validated());
    }

    [TestMethod]
    public void AWaitingSwordsman_KeepsOutsideTheOpponentsReach()
    {
        var bot = Bot(new HitAndAway(Patience: 5f));
        var sense = Facing(0.5f);
        var safe = SwordsmanBot.OpponentReach(sense, new HitAndAway().Lunge) + new HitAndAway().Margin;
        Assert.IsGreaterThan(0.5f, safe, "a blade 30 cm long on an arm reaches past 50 cm");
        var command = bot.Read(sense, 1f / 60f);
        Assert.IsLessThan(-0.5f, command.MoveWorld.Z, "it steps back out of reach");
        Assert.IsFalse(bot.Attacking);
        // 遠くにいれば、届かない所の手前まで詰める（離れすぎない）。
        command = bot.Read(Facing(1.2f), 1f / 60f);
        Assert.IsGreaterThan(0.3f, command.MoveWorld.Z);
    }

    [TestMethod]
    public void TheEndOfTheOpponentsSwing_IsAnOpening_ThatSendsItIn()
    {
        var bot = Bot(new HitAndAway(Patience: 5f, OpeningTime: 0.4f));
        var safe = SwordsmanBot.OpponentReach(Facing(0.7f), new HitAndAway().Lunge) + new HitAndAway().Margin;
        // 相手は遠間で空を振っている（剣先は横へ速く。こちらへは来ない）。
        _ = bot.Read(Facing(safe, tipVelocity: new Vector3(4f, 0f, 0f)), 1f / 60f);
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
        var bot = Bot(new HitAndAway(Patience: 0.05f));
        // 待ちきれず攻めて出て、技の間合い（45 cm）で打つ。
        var t = 0f;
        for (; t < 0.2f && bot.Current is null; t += 1f / 60f)
        {
            _ = bot.Read(Facing(0.45f), 1f / 60f);
        }

        Assert.IsNotNull(bot.Current, "it strikes at its technique's range");
        Assert.AreEqual(1, bot.Pressures);
        // 技を打ち終えるまで進める。
        for (var i = 0; i < 600 && bot.Current is not null; i++)
        {
            _ = bot.Read(Facing(0.45f), 1f / 60f);
        }

        var command = bot.Read(Facing(0.45f), 1f / 60f);
        Assert.IsTrue(bot.Retreating, "having struck, it pulls back");
        Assert.IsLessThan(-0.5f, command.MoveWorld.Z);
    }

    [TestMethod]
    public void AnOpponentSteppingIntoRange_IsMetAsHeComes()
    {
        var bot = Bot(new HitAndAway(Patience: 5f));
        // 相手が技の間合い（45 cm）へ歩いて入ってくる: 出端を打つ。
        _ = bot.Read(Facing(0.45f, opponentVelocity: new Vector3(0f, 0f, -0.4f)), 1f / 60f);
        Assert.IsNotNull(bot.Current);
        Assert.AreEqual(1, bot.Debanas);
    }

    [TestMethod]
    public void WithoutHitAndAway_TheBotIsUnchanged()
    {
        // 打って離れない剣士は、これまでどおり遠間（いちばん遠い技 + 8 cm）で構える。
        var bot = Bot(null);
        var command = bot.Read(Facing(0.5f) with { OpponentBlade = default }, 1f / 60f);
        Assert.AreEqual(0.53f, bot.StandOff, 1e-4f);
        Assert.IsGreaterThan(0.5f, command.MoveWorld.Z, "it closes to its technique's range");
        Assert.IsFalse(bot.Attacking);
        Assert.IsFalse(bot.Retreating);
    }

    [TestMethod]
    public void ChasedWhileGettingAway_ItStopsRetreatingAfterAWhile_AndCanStrikeAgain()
    {
        // 再発防止（レビュー指摘）: 下がる最中は打ちに出ないので、遠間まで下がりきれないまま追われ続けると、いつまでも下がっていた。
        var bot = Bot(new HitAndAway(Patience: 0.05f));
        for (var i = 0; i < 60 && bot.Current is null; i++)
        {
            _ = bot.Read(Facing(0.45f), 1f / 60f);
        }

        for (var i = 0; i < 600 && bot.Current is not null; i++)
        {
            _ = bot.Read(Facing(0.45f), 1f / 60f);
        }

        Assert.IsTrue(bot.Retreating);
        // 相手はいつまでも 45 cm の所についてくる（下がりきれない）。
        var used = bot.Used.Count;
        for (var i = 0; i < 4 * 60 && bot.Used.Count == used; i++)
        {
            _ = bot.Read(Facing(0.45f), 1f / 60f);
        }

        Assert.IsGreaterThan(used, bot.Used.Count, "within a few seconds it strikes again instead of backing away for good");
    }
}
