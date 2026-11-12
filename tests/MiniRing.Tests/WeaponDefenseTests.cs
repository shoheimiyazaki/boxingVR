using System.Numerics;
using MiniRing.Bots;
using MiniRing.Motion;
using MiniRing.Weapons;

namespace MiniRing.Tests;

/// <summary>
/// 武器を選ばない守り（<see cref="WeaponDefense"/>）: 相手の武器を握りから先までの線として読む・受け流す（自分の刀は自分の体から離す）・
/// 足と頭で外す、と、見て覚えた相手の届く距離（<see cref="SwordsmanBot.SeenReach"/>）。
/// </summary>
[TestClass]
public sealed class WeaponDefenseTests
{
    private const float Reach = 0.17f;

    /// <summary>自分は原点で +Z を向いて立つ。相手は 35 cm 前、肩は 30 cm 前。</summary>
    private static FighterSense Sense(BladeSense blade, GloveSense right = default, GloveSense left = default, float shoulderZ = 0.3f) =>
        new(new BodyFrame(Vector3.Zero, 0f), Reach, new Vector3(0f, 0.1f, 0.35f), new Vector3(0f, 0f, 0.35f), OpponentAlive: true,
            OpponentLeft: left, OpponentRight: right,
            SelfHead: new Vector3(0f, 0.08f, 0f),
            OpponentBody: new BodySense(new Vector3(0.05f, 0.05f, shoulderZ), new Vector3(-0.05f, 0.05f, shoulderZ), new Vector3(0f, -0.15f, 0.35f)),
            OpponentBlade: blade, SelfArmed: true, Ring: RingLayout.Weapons);

    /// <summary>
    /// 横に寝かせた棍棒が、握りから先まで平行にこちらへ来る: 先は体の横 45 cm を通り過ぎるが、線の中ほどが胸へ当たる。
    /// </summary>
    private static FighterSense ShaftComing()
    {
        var hilt = new Vector3(0.15f, 0.05f, 0.25f);
        var tip = new Vector3(-0.45f, 0.05f, 0.25f);
        return Sense(new BladeSense(true, hilt, tip, new Vector3(0f, 0f, -4f)), right: new GloveSense(hilt, new Vector3(0f, 0f, -2f)));
    }

    [TestMethod]
    public void TheWholeWeaponLine_IsRead_NotOnlyTheTip()
    {
        var sense = ShaftComing();
        var defense = new SwordDefense(Mode: DefenseMode.Parry);
        var threat = WeaponDefense.Read(sense, WeaponDefense.Lines(sense, watchFists: false), defense);
        Assert.IsNotNull(threat, "the middle of the shaft reaches the body even though the tip passes wide");
        Assert.IsLessThan(0.9f, threat.Value.Along, "it is the shaft, not the tip, that comes at the body");
        Assert.IsGreaterThan(0f, threat.Value.Time);
    }

    [TestMethod]
    public void AFist_IsAWeaponLineOfZeroLength()
    {
        var fist = new GloveSense(new Vector3(-0.03f, 0.08f, 0.22f), new Vector3(0f, 0f, -3f));
        var sense = Sense(default, right: fist, shoulderZ: 0.24f); // 拳は肩から腕の長さまでしか届かない: 肩を近くに
        var threat = WeaponDefense.Read(sense, WeaponDefense.Lines(sense, watchFists: true), new SwordDefense(Mode: DefenseMode.Parry));
        Assert.IsNotNull(threat);
        Assert.AreEqual(0f, threat.Value.Line.Length);
    }

    [TestMethod]
    public void AWeaponMovingAway_IsNotAThreat()
    {
        var hilt = new Vector3(0.15f, 0.05f, 0.25f);
        var sense = Sense(new BladeSense(true, hilt, new Vector3(-0.45f, 0.05f, 0.25f), new Vector3(0f, 0f, 4f)), right: new GloveSense(hilt, new Vector3(0f, 0f, 2f)));
        Assert.IsNull(WeaponDefense.Read(sense, WeaponDefense.Lines(sense, watchFists: false), new SwordDefense(Mode: DefenseMode.Parry)));
    }

    [TestMethod]
    public void TheParryingBlade_CrossesTheIncomingLine_AndStaysClearOfTheOwnBody()
    {
        // 上から頭へ振り下ろされる刀。
        var hilt = new Vector3(0f, 0.25f, 0.22f);
        var tip = new Vector3(0f, 0.3f, 0.05f);
        var sense = Sense(new BladeSense(true, hilt, tip, new Vector3(0f, -3f, -0.5f)), right: new GloveSense(hilt, new Vector3(0f, -1f, 0f)));
        var defense = new SwordDefense(Mode: DefenseMode.Parry, Deflect: 0.5f);
        var threat = WeaponDefense.Read(sense, WeaponDefense.Lines(sense, false), defense)!.Value;
        var guardBlade = Vector3.Normalize(new Vector3(0f, 0.6f, 0.8f));
        var bladeLength = 3f;
        var frame = WeaponDefense.Parry(sense, threat, defense, bladeLength, SwordForms.KatanaSpacing, guardBlade);
        var incoming = Vector3.Normalize(tip - hilt);
        Assert.IsLessThan(0.5f, MathF.Abs(Vector3.Dot(frame.Blade, incoming)), "the blade lies across the incoming line");
        Assert.IsGreaterThanOrEqualTo(0f, Vector3.Dot(frame.Blade, guardBlade), "it turns the shorter way from the guard");
        // 左手から先まで、体の線（頭から腰。腕の長さ単位）から Clearance 以上離れている（押し負けても自分を斬らない）。
        var top = new Vector3(0f, 0.08f, 0f) / Reach;
        var bottom = new Vector3(0f, -1.2f, 0f);
        var closest = float.MaxValue;
        for (var i = 0; i <= 20; i++)
        {
            var p = Vector3.Lerp(frame.Left, frame.Right + (frame.Blade * bladeLength), i / 20f);
            closest = MathF.Min(closest, Vector3.Distance(p, WeaponDefense.ClosestOnSegment(p, top, bottom)));
        }

        Assert.IsGreaterThanOrEqualTo(defense.Clearance - 0.02f, closest);
    }

    [TestMethod]
    public void Deflecting_TiltsThePush_TowardTheOutside()
    {
        // 突きが体の右（-X）寄りへ来る: 受け流しは、来る向きの真逆より右（体の外）へ押す。
        var hilt = new Vector3(-0.05f, 0.0f, 0.3f);
        var tip = new Vector3(-0.07f, 0.0f, 0.12f);
        var sense = Sense(new BladeSense(true, hilt, tip, new Vector3(0f, 0f, -3f)), right: new GloveSense(hilt, new Vector3(0f, 0f, -3f)));
        var straight = new SwordDefense(Mode: DefenseMode.Parry, Deflect: 0f);
        var threat = WeaponDefense.Read(sense, WeaponDefense.Lines(sense, false), straight)!.Value;
        foreach (var deflect in new[] { 0f, 0.9f })
        {
            var frame = WeaponDefense.Parry(sense, threat, straight with { Deflect = deflect }, 3f, SwordForms.KatanaSpacing, Vector3.UnitZ);
            // 刀の面が押す向き（Edge）: 突きは来る線と平行には押せないので、どちらでも横へ、体の外（-X）へ払う。
            Assert.IsLessThan(-0.5f, frame.Edge.X, $"deflect {deflect}");
            Assert.IsLessThan(0.3f, MathF.Abs(Vector3.Dot(frame.Blade, Vector3.Normalize(tip - hilt))), "the blade crosses the thrust");
        }
    }

    [TestMethod]
    public void TheChosenStep_GetsTheBodyOutOfTheWay()
    {
        var sense = ShaftComing();
        var defense = new SwordDefense(Mode: DefenseMode.Step);
        var lines = WeaponDefense.Lines(sense, false);
        var (move, head) = WeaponDefense.BestStep(sense, lines, defense, walkSpeed: 0.6f, face: Vector3.UnitZ);
        Assert.IsTrue(move != Vector3.Zero || head != Vector3.Zero, "standing still is hit, so it moves");
        Assert.IsLessThanOrEqualTo(0f, Vector3.Dot(move, Vector3.UnitZ), "it does not step into the swing");
    }

    [TestMethod]
    public void NothingComing_NoStep()
    {
        var sense = Sense(default);
        var (move, head) = WeaponDefense.BestStep(sense, WeaponDefense.Lines(sense, true), new SwordDefense(Mode: DefenseMode.Step), 0.6f, Vector3.UnitZ);
        Assert.AreEqual(Vector3.Zero, move);
        Assert.AreEqual(Vector3.Zero, head);
    }

    [TestMethod]
    public void ANewBlowFromAnotherSide_IsParriedAfresh()
    {
        // 上からの打ちを受けた直後（守りの続く間）に、横から別の打ちが来たら、受けの構えを決め直す（最初の構えのままにしない）。
        var guard = new MotionFrame(new Vector3(-0.2f, -0.57f, 0.78f), new Vector3(0.03f, -0.86f, 0.5f), Vector3.Normalize(new Vector3(0f, 0.6f, 0.8f)),
            Vector3.Normalize(new Vector3(0f, -0.8f, 0.6f)), Vector3.Zero, 0f, Vector3.Zero);
        var bot = new SwordsmanBot([new MotionTechnique("men", WeaponKind.Katana, SwordForms.Katana()[0], 0.45f)], guard, seed: 1, rate: 1.77f,
            defense: new SwordDefense(Mode: DefenseMode.Parry, Counter: false));
        var overheadHilt = new Vector3(0f, 0.25f, 0.22f);
        var overhead = Sense(new BladeSense(true, overheadHilt, new Vector3(0f, 0.3f, 0.05f), new Vector3(0f, -3f, -0.5f)), right: new GloveSense(overheadHilt, new Vector3(0f, -1f, 0f)));
        var first = bot.Read(overhead, 1f / 240f);
        var second = bot.Read(ShaftComing(), 1f / 240f);
        Assert.AreEqual(2, bot.Blocks);
        Assert.IsGreaterThan(0.05f, Vector3.Distance(first.Right.TargetLocal, second.Right.TargetLocal), "the blade moves to meet the new blow");
    }

    [TestMethod]
    public void ASwordsman_RemembersHowFarTheOpponentReached()
    {
        var guard = new MotionFrame(new Vector3(-0.2f, -0.57f, 0.78f), new Vector3(0.03f, -0.86f, 0.5f), Vector3.Normalize(new Vector3(0f, 0.6f, 0.8f)),
            Vector3.Normalize(new Vector3(0f, -0.8f, 0.6f)), Vector3.Zero, 0f, Vector3.Zero);
        var bot = new SwordsmanBot([new MotionTechnique("men", WeaponKind.Katana, SwordForms.Katana()[0], 0.45f)], guard, seed: 1, rate: 1.77f,
            defense: new SwordDefense(Mode: DefenseMode.Parry));
        // 相手の胸（z = 0.35）から、こちらへ 30 cm 出た先が速く動いている。
        var hilt = new Vector3(0f, 0.05f, 0.25f);
        var sense = Sense(new BladeSense(true, hilt, new Vector3(0f, 0.05f, 0.05f), new Vector3(2f, 0f, 0f)), right: new GloveSense(hilt, Vector3.Zero));
        _ = bot.Read(sense, 1f / 60f);
        Assert.AreEqual(0.3f, bot.SeenReach, 1e-4f);
        // 振っていない（遅い）先は覚えない。
        var still = new SwordsmanBot([new MotionTechnique("men", WeaponKind.Katana, SwordForms.Katana()[0], 0.45f)], guard, seed: 1, rate: 1.77f);
        _ = still.Read(sense with { OpponentBlade = sense.OpponentBlade with { TipVelocity = Vector3.Zero } }, 1f / 60f);
        Assert.AreEqual(0f, still.SeenReach);
    }
}
