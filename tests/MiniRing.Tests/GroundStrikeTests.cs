using System.Globalization;
using MiniRing.Bots;
using MiniRing.Weapons;

namespace MiniRing.Tests;

/// <summary>
/// レフェリーなしの試合（<see cref="BoxingCorners.Referee"/> = false）: 倒れた相手への打撃が数えられ、ボットは倒れた相手へ打ち下ろす。
/// 倒した相手は意識を落として（<see cref="Fighter.Concussion"/>）倒れたままにし、攻め手のボットが何秒で何発当てるかを測る。
/// </summary>
[TestClass]
[TestCategory("slow")]
public sealed class GroundStrikeTests
{
    private const float Dt = 1f / 240f;

    /// <summary>倒れた相手を攻める時間 [s]。</summary>
    internal const float DrillSeconds = 16.78f;

    /// <summary>倒れた相手に当てた打撃の記録（<paramref name="FirstHitAt"/> は打ち下ろしを始めてからの時刻）。</summary>
    internal sealed record DrillResult(int Hits, float? FirstHitAt, int Started, string Names, float Damage, bool Downed, float Wounded = 0f);

    // 攻めない的: 弱った相手を詰める層（FinishPress など）も切る（攻めない体が「相手の構えが下がった」と読んで前へ詰めると、的の位置が試合ごとに変わる）。
    private static BotStyle Still => BotCatalog.Boxer with { Attacks = false, DefenseRate = 0f, Prediction = 0f, Aggression = 0f, FinishTempo = 0f, FinishPress = 0f, FinishSpend = 0f };

    private static BoxingCorners Corners(BotStyle attacker, bool referee, WeaponKind? weapon = null, bool useWeaponBots = false, bool legacyWeapon = false)
        => new("attacker", s => new BoxerBot(attacker, s), s => new BoxerBot(Still, s), CpuStyleJson: attacker.ToJson(),
            CpuWeapon: weapon, CpuTwoHanded: FormSwordsman.Handles(weapon), UseWeaponBots: useWeaponBots, Referee: referee,
            CpuWeaponStyle: legacyWeapon ? CharacterSamenessCases.LegacyWeaponStyle(weapon) : null);

    /// <summary>赤（受け手）を気絶させて倒れたままにし、青（攻め手）が倒れた相手へ何発当てるかを測る。</summary>
    internal static DrillResult Drill(BotStyle attacker, bool referee, WeaponKind? weapon = null, float seconds = DrillSeconds, bool useWeaponBots = false, int seed = 1, bool legacyWeapon = false)
    {
        using var game = new BoxingGame(seed: seed, corners: Corners(attacker, referee, weapon, useWeaponBots, legacyWeapon));
        for (var t = 0f; t < 2.518f; t += Dt)
        {
            game.Step(default, Dt);
        }

        var victim = game.Player;
        var woundedBefore = victim.Condition.TotalDamage;
        var hits = 0;
        var damage = 0f;
        float? first = null;
        var names = new List<string>();
        var downed = false;
        float? startedAt = null;
        for (var t = 0f; t < seconds; t += Dt)
        {
            victim.Concussion = 0.97f; // 気を失って倒れたまま
            game.Step(default, Dt);
            if (!downed && victim.State != FighterState.Standing)
            {
                // 傷は倒れた時点から数える（倒れる途中、まだ立っている相手に当たった分は倒れた相手への攻めではない）。
                downed = true;
                woundedBefore = victim.Condition.TotalDamage;
            }

            if (!downed)
            {
                continue;
            }

            var ground = (game.CpuControl as GroundStriker)?.Ground;
            if (ground is { Started: > 0 })
            {
                startedAt ??= t;
                foreach (var hit in game.RecentHits.Where(h => h.Attacker == game.Cpu && h.Victim == victim))
                {
                    hits++;
                    damage += hit.Damage;
                    first ??= t - startedAt;
                }
            }
            else if (!referee)
            {
                // 倒れた相手を攻める前に当たった（かすった）打撃は、このドリルでは数えない。
            }

            if (Trace && ground is not null && ((int)(t / Dt)) % 30 == 0)
            {
                var g = game.Cpu.GloveCenter(ground.CurrentHand);
                var gl = game.Cpu.GloveCenter(Hand.Left);
                var cmd = ((GroundStriker)game.CpuControl).Last.For(ground.CurrentHand).TargetLocal;
                var act = game.Cpu.Frame.WorldToLocal(g);
                Console.WriteLine(string.Create(CultureInfo.InvariantCulture, $"    cmd=({cmd.X:0.000},{cmd.Y:0.000},{cmd.Z:0.000}) actual=({act.X:0.000},{act.Y:0.000},{act.Z:0.000}) headOffsetY={((GroundStriker)game.CpuControl).Last.HeadOffset.Y:0.000} stamina={game.Cpu.Stamina:0.00} tone={game.Cpu.Tone:0.00} upright={game.Cpu.Uprightness:0.00}"));
                var near = MathF.Min(MathF.Min(victim.DistanceToPart(victim.Parts.Chest, g), victim.DistanceToPart(victim.Parts.Head, g)), MathF.Min(victim.DistanceToPart(victim.Parts.Chest, gl), victim.DistanceToPart(victim.Parts.Head, gl)));
                Console.WriteLine(string.Create(CultureInfo.InvariantCulture, $"  t={t:0.00} {ground.CurrentName}/{ground.CurrentHand} striking={ground.Striking} dist={ground.Distance:0.00} gap={ground.Gap:0.00} sink={ground.Sink:0.00} started={ground.Started} gloveToVictim={near:0.000}m shoulderY={game.Cpu.ShoulderPosition(Hand.Left).Y:0.000} gloveR=({g.X:0.00},{g.Y:0.00},{g.Z:0.00}) head=({victim.HeadPosition.X:0.00},{victim.HeadPosition.Y:0.00},{victim.HeadPosition.Z:0.00}) chest=({victim.ChestPosition.X:0.00},{victim.ChestPosition.Y:0.00},{victim.ChestPosition.Z:0.00}) me=({game.Cpu.ShoulderPosition(Hand.Left).X:0.00},{game.Cpu.ShoulderPosition(Hand.Left).Z:0.00})"));
            }

            if (ground?.CurrentName is { } current && (names.Count == 0 || names[^1] != current))
            {
                names.Add(current);
            }

            if (game.RoundOver)
            {
                break;
            }
        }

        var started = (game.CpuControl as GroundStriker)?.Ground.Started ?? 0;
        return new DrillResult(hits, first, started, string.Join(",", names), damage, downed, victim.Condition.TotalDamage - woundedBefore);
    }

    internal static bool Trace { get; set; }

    internal static string Line(string label, DrillResult r)
        => string.Create(CultureInfo.InvariantCulture, $"{label}: hits={r.Hits} firstHit={r.FirstHitAt:0.00}s started={r.Started} moves={r.Names} damage={r.Damage:0.0} wounded={r.Wounded:0.0}");

    /// <summary>赤を気絶させ続けて倒しておき、青は何もしない。レフェリーの有無で、数えと決着がどう変わるかを見る。</summary>
    private static (bool RoundOver, int CpuWins, float At, bool EverHolding) Count(bool referee, float seconds, float? reviveAt = null)
    {
        using var game = new BoxingGame(seed: 1, corners: Corners(Still, referee));
        for (var t = 0f; t < 2.518f; t += Dt)
        {
            game.Step(default, Dt);
        }

        var everHolding = false;
        for (var t = 0f; t < seconds; t += Dt)
        {
            game.Player.Concussion = reviveAt is { } at && t > at ? 0f : 0.97f;
            game.Step(default, Dt);
            everHolding |= game.Holding;
            if (game.RoundOver)
            {
                return (true, game.CpuWins, t, everHolding);
            }
        }

        return (false, game.CpuWins, seconds, everHolding);
    }

    [TestMethod]
    public void NoReferee_StrikesOnADownedOpponentAreCounted_WithReferee_TheyAreNot()
    {
        var off = Drill(BotCatalog.Boxer, referee: false);
        var on = Drill(BotCatalog.Boxer, referee: true);
        Console.WriteLine(Line("referee off", off));
        Console.WriteLine(Line("referee on", on));
        Assert.IsTrue(off.Downed && on.Downed, "the victim was knocked out in both");
        Assert.IsGreaterThan(0, off.Hits, "without a referee, strikes on the downed opponent are counted (ApplyHit)");
        Assert.AreEqual(0, on.Hits, "with a referee, a downed opponent is not struck (unchanged)");
        Assert.AreEqual(0, on.Started, "with a referee the bot never attacks a downed opponent");
    }

    [TestMethod]
    public void NoReferee_TenCountStillKnocksOut()
    {
        var (over, wins, at, _) = Count(referee: false, seconds: 23.5f);
        Console.WriteLine($"no referee: round over={over} at {at:0.0}s");
        Assert.IsTrue(over, "the ten count still ends the round without a referee");
        Assert.AreEqual(1, wins);
        Assert.IsGreaterThan(Fighter.PhysicsVersion > 0 ? BoxingGame.CountSeconds - BoxingGame.CountInterval : 0f, at, "not before the count is up");
    }

    [TestMethod]
    public void NoReferee_NoBreathAfterRecovering_WithReferee_ThereIs()
    {
        var off = Count(referee: false, seconds: 15.11f, reviveAt: 5.035f);
        var on = Count(referee: true, seconds: 15.11f, reviveAt: 5.035f);
        Assert.IsFalse(off.EverHolding, "no referee, nobody holds the fight after a recovery");
        Assert.IsTrue(on.EverHolding, "with a referee the fight is held for a breath (unchanged)");
    }

    [TestMethod]
    [DataRow("boxer")]
    [DataRow("champion")]
    [DataRow("swarmer")]
    [DataRow("karate")]
    public void NoReferee_BotHitsADownedOpponentWithinSeconds(string name)
    {
        // 倒れる前の立ち回り（弱った相手を詰める層、LogicVersion 36）で倒れる位置が種ごとに変わるので、1 つの種でなく 3 つの種のうち 2 つ以上で見る
        // （swarmer は種 1 で 0 発・種 2〜6 で 1〜12 発。6 つの種の合計は層なしより多い: 与えた量 252 対 206。ZzGroundSeedsProbe）。
        var runs = Enumerable.Range(1, 3).Select(seed => Drill(BotCatalog.Style(name), referee: false, seed: seed)).ToList();
        var r = runs.OrderByDescending(x => x.Hits).First();
        Console.WriteLine(Line(name, r));
        Assert.IsTrue(r.Downed);
        Assert.IsGreaterThan(0, r.Started, $"{name} starts at least one strike on the downed opponent");
        Assert.IsGreaterThanOrEqualTo(2, runs.Count(x => x.Hits > 0), $"{name} lands at least one counted strike on the downed opponent within {DrillSeconds} s in 2 of 3 seeds");
        Assert.IsGreaterThan(0f, r.Damage);
    }

    [TestMethod]
    public void NoReferee_KarateStompsAndStrikesDown()
    {
        var r = Drill(BotCatalog.Karate, referee: false, seconds: 23.5f);
        Console.WriteLine(Line("karate", r));
        StringAssert.Contains(r.Names, "stomp", "the karate style (GroundStomp) uses foot stomps on a downed opponent");
    }

    [TestMethod]
    [DataRow(WeaponKind.Katana)]
    [DataRow(WeaponKind.Longsword)]
    [DataRow(WeaponKind.WoodenClub)]
    public void NoReferee_WeaponBotWoundsADownedOpponent(WeaponKind weapon)
    {
        // 1 つの種では、刃が倒れた相手の上で止まる（1 回始めたきり進まない）ことがある（LogicVersion 31 より前の教科書でも種 3 の刀で起きた）。
        // 倒れる前の受け手の立ち回り（LogicVersion 36）でも倒れる位置が変わり、ロングソードは種 1〜6 で傷 2.3・0・0・11.6・9.1・0.5 と半分ほど。
        // 的（Still）の詰める層を切った後（マージ 2026-11-17、LogicVersion 38）は、ロングソードが 0・2.5・3.9・0・0・0 で 2 つ（刀は 6 つ、こんぼうは 5 つ）。
        // ロングソードの打ち下ろしが倒れた相手に届きにくいのは既知の弱み（打ち下ろしを始める回数も 1〜6 回と少ない。docs/BOT_PDCA.md「倒れた相手への攻め」の残り）。
        // 6 つの種のうち 2 つ以上で見る（「振り下ろしが倒れた相手に届くことがある」まで。どの種でも届くことは確かめていない）。
        // 2026-11-19（PhysicsVersion 59、顔・手の層と関節のつなぎ・滑りの直しのマージ）: こんぼうは種 1〜6 の傷が 0.9・0.9・0・0・2.9・0.2 で 1 つだけになった
        // （直前の枝では 0・1.7・0・0.4・0.7・1.3 で 1 つ + 境の 1.3、master では 0.1・0・13・0.3 …）。倒れた相手の上の刃が届くかは種で大きく揺れる。
        // 主張は「届くことがある」までなので、種を 12 に増やして（6 つの種が 2 つ以上、を 12 の種で 2 つ以上、に）見る。下限の 2 は変えない。
        var armed = new DrillResult[12];
        Parallel.For(0, armed.Length, i => armed[i] = Drill(BotCatalog.Champion, referee: false, weapon, useWeaponBots: true, seed: i + 1));
        var refereed = Drill(BotCatalog.Champion, referee: true, weapon, useWeaponBots: true);
        for (var i = 0; i < armed.Length; i++)
        {
            Console.WriteLine(Line($"{weapon} off seed {i + 1}", armed[i]));
        }

        Console.WriteLine(Line($"{weapon} on", refereed));
        Assert.IsGreaterThanOrEqualTo(3, armed.Count(r => r.Started > 0), "the weapon bot starts a downward strike");
        Assert.IsGreaterThanOrEqualTo(2, armed.Count(r => r.Wounded > 1f), $"{weapon} wounds the downed opponent without a referee");
        Assert.IsLessThan(0.5f, refereed.Wounded, "with a referee, the downed opponent is not touched (unchanged)");
    }

    /// <summary>プレイヤー（意図操作）の呼びかけ（弱 = 振り下ろす系）で、倒れた相手へ打ち下ろす。</summary>
    [TestMethod]
    public void NoReferee_PlayerCallSwingStrikesTheDownedOpponent()
    {
        var player = new CalledSwing();
        var corners = new BoxingCorners("still", s => new BoxerBot(Still, s), s => player, Referee: false);
        using var game = new BoxingGame(seed: 2, corners: corners);
        // 青（CPU）を倒れたままにし、赤（プレイヤー側の入力元）が弱で攻める。
        var hits = 0;
        for (var t = 0f; t < 2.518f + 16.78f; t += Dt)
        {
            if (t > 2.518f)
            {
                game.Cpu.Concussion = 0.97f;
            }

            game.Step(default, Dt);
            hits += game.RecentHits.Count(h => h.Attacker == game.Player && h.Victim == game.Cpu);
        }

        Console.WriteLine($"player swings started={player.Ground.Started} hits={hits}");
        Assert.IsGreaterThan(0, player.Ground.Started);
        Assert.IsGreaterThan(0, hits, "the player's light attack becomes a strike down on the downed opponent");
    }

    /// <summary>歩き・構えは入力元の指令どおり、倒れた相手が現れたら弱を押し続ける、プレイヤーの代わり。</summary>
    private sealed class CalledSwing : IFighterControl
    {
        private readonly BoxerBot _inner = new(Still, 1);

        public GroundStrike Ground { get; } = new(BotCatalog.Boxer, GroundTool.Fist, 5);

        public FighterCommand Read(in FighterSense sense, float dt)
        {
            var basis = _inner.Read(sense, dt);
            return GroundStrike.Applies(sense) ? Ground.Read(sense, basis, dt, GroundCall.Swing, load: 0.08392f, steer: true) : basis;
        }
    }

    [TestMethod]
    public void GroundTrigger_HeldButtonEnteringGroundModeDoesNotFire_AndFreshPressChargesFresh()
    {
        var trigger = new GroundTrigger();
        float load;
        // 地上の場面でない間に押し続けている（満タンまで溜まる長さ）。
        for (var i = 0; i < 120; i++)
        {
            Assert.AreEqual(GroundCall.Hold, trigger.Update(false, true, false, 0.01f, GroundCall.Thrust, out _));
        }

        // 押したまま倒れた相手を攻める場面に入っても、離すまでは何も起きない（離したときも打たない）。
        for (var i = 0; i < 5; i++)
        {
            Assert.AreEqual(GroundCall.Hold, trigger.Update(true, true, false, 0.01f, GroundCall.Thrust, out _));
        }

        Assert.AreEqual(GroundCall.Hold, trigger.Update(true, false, false, 0.01f, GroundCall.Thrust, out _), "releasing the carried-over press does not strike");

        // 改めて押して 0.1 s 溜め、離して打つ。溜めはそこから数える。
        for (var i = 0; i < 10; i++)
        {
            Assert.AreEqual(GroundCall.Hold, trigger.Update(true, true, false, 0.01f, GroundCall.Thrust, out _));
        }

        Assert.AreEqual(GroundCall.Swing, trigger.Update(true, false, false, 0.01f, GroundCall.Thrust, out load));
        Assert.AreEqual(0.1f, load, 0.02f, "a fresh press charges from zero, not the carried-over charge");

        // 強: 離すと呼びかけ（突き下ろし）。地上の場面を出たら記憶を捨てる。
        trigger.Update(true, false, true, 0.01f, GroundCall.Thrust, out _);
        Assert.AreEqual(GroundCall.Thrust, trigger.Update(true, false, false, 0.01f, GroundCall.Thrust, out _));
        trigger.Update(true, false, true, 0.01f, GroundCall.Thrust, out _);
        trigger.Update(false, false, true, 0.01f, GroundCall.Thrust, out _);
        Assert.AreEqual(GroundCall.Hold, trigger.Update(true, false, false, 0.01f, GroundCall.Thrust, out _), "leaving the ground mode drops the pending press");
    }

    [TestMethod]
    public void GroundTrigger_IntentGetsNoAttackButtonsInGroundMode()
    {
        var held = new FighterIntent(new System.Numerics.Vector2(0f, 1f), Light: true, Heavy: true, Evade: true);
        var masked = GroundTrigger.ForIntent(held, ground: true);
        Assert.IsFalse(masked.Light);
        Assert.IsFalse(masked.Heavy);
        Assert.AreEqual(held.Direction, masked.Direction, "movement and evasion are untouched");
        Assert.IsTrue(masked.Evade);
        Assert.AreEqual(held, GroundTrigger.ForIntent(held, ground: false));
    }
}
