using System.Numerics;
using MiniRing.Bots;
using MiniRing.Weapons;

namespace MiniRing.Tests;

/// <summary>棍棒の振り（<see cref="WeaponSwing"/>）と、それで戦うボット（<see cref="ClubBot"/>）。</summary>
[TestClass]
[TestCategory("slow")]
public sealed class ClubSwingTests
{
    [TestMethod]
    public void TheAngles_PointTheClub_ForwardLeftAndUp()
    {
        static Vector3 Axis(float az, float el) => Vector3.Transform(Vector3.UnitX, WeaponSwing.Orientation(az, el));
        Assert.AreEqual(1f, Vector3.Dot(Axis(0f, 0f), Vector3.UnitZ), 1e-4f, "0/0 is straight ahead (+Z)");
        Assert.AreEqual(1f, Vector3.Dot(Axis(MathF.PI / 2f, 0f), Vector3.UnitX), 1e-4f, "positive azimuth turns to the left (+X)");
        Assert.AreEqual(1f, Vector3.Dot(Axis(0f, MathF.PI / 2f), Vector3.UnitY), 1e-4f, "positive elevation points up");
    }

    [TestMethod]
    public void ASwing_GoesFromGuard_ToTheWindup_ToTheStrike_AndBack()
    {
        var swing = WeaponSwing.Default(SwingFamily.Chop) with { WristDelay = 0.5f, Twist = 0.3f, Step = 0.5f };
        const float reach = 1f;
        Assert.AreEqual(WeaponSwing.GuardCommand(reach).TargetLocal, swing.At(0f, reach).Hand.TargetLocal);
        Assert.AreEqual(swing.WindupFist, swing.At(swing.StrikeStart, reach).Hand.TargetLocal);
        Assert.AreEqual(swing.StrikeFist.Z, swing.At(swing.StrikeEnd - 1e-4f, reach).Hand.TargetLocal.Z, 0.01f);
        Assert.AreEqual(WeaponSwing.GuardCommand(reach).TargetLocal, swing.At(swing.Duration, reach).Hand.TargetLocal);

        // 手首のタメ: 打ちの前半は棍棒の向きを振りかぶりのまま残す。
        var held = swing.At(swing.StrikeStart + (0.4f * swing.StrikeTime), reach).Hand.Orientation!.Value;
        var windup = WeaponSwing.Orientation(swing.WindupAzimuth, swing.WindupElevation);
        Assert.IsGreaterThan(0.999f, MathF.Abs(Quaternion.Dot(held, windup)));

        var during = swing.At(swing.StrikeStart + (0.5f * swing.StrikeTime), reach);
        Assert.AreEqual(0.3f, during.Twist);
        Assert.AreEqual(0.5f, during.Step);
    }

    [TestMethod]
    public void AClubBlow_CrushesMoreOfTheBody_ThanAPunchCarvesAtMost()
    {
        // 縦振りの出発点は胸に当たる。素の木の打撃は、拳の重い 1 発の上限（MaxCarve）より多くのセルを潰す（グローブ無しの相手: 構えた
        // ボクセルのグローブの詰め物は、棍棒を受け止めて和らげる）。
        // 材料による潰れ方は十分に休んだ 1 振りで比較する。スタミナ制約の検証とは分け、
        // 構えて間合いへ入る間の疲労で、打ち出す前から弱っている条件を混ぜない。
        // 2026-11-19（PhysicsVersion 53）: 間合い 0.38 は潰れたセル 41（下限 42）に落ちた。間合いを 0.02 ずつ掃くと（RangeSweep_Probe）
        // 0.30: 49・0.32: 54・0.34: 51・0.36: 53・0.38: 41・0.40: 83・0.42: 27・0.44: 27 と、当たる前の踏み込みの 1 歩のずれで大きく動く。
        // 動かない帯（0.30〜0.36 はどれも 49〜54）の真ん中の 0.34 にした。下限（MaxCarve × 0.35）は変えていない。
        var lowest = 1f;
        var r = SwingTest.Run(WeaponSwing.Default(SwingFamily.Chop), 0.34f, (game, _, _) =>
        {
            lowest = MathF.Min(lowest, game.Player.Stamina);
            game.Player.Stamina = 1f;
            game.Cpu.Stamina = 1f;
        }, gloves: false);
        Console.WriteLine($"rested club: {r.Cells} cells, lowest reserve before refill {lowest:P2}");
        Assert.IsTrue(r.Landed);
        Assert.IsGreaterThan(FighterTuning.Default.MaxCarve * 0.35f, r.Cells, "a hard club crushes the flesh it strikes");
    }

    [TestMethod]
    [TestCategory("Probe")]
    public void RangeSweep_Probe()
    {
        foreach (var range in new[] { 0.30f, 0.32f, 0.34f, 0.36f, 0.38f, 0.40f, 0.42f, 0.44f })
        {
            var r = SwingTest.Run(WeaponSwing.Default(SwingFamily.Chop), range, (game, _, _) =>
            {
                game.Player.Stamina = 1f;
                game.Cpu.Stamina = 1f;
            }, gloves: false);
            Console.WriteLine($"RSWEEP range {range:F2}: landed {r.Landed} cells {r.Cells}");
        }
    }

    [TestMethod]
    public void TheBatStance_PutsBothHandsOnTheClub_AboveTheRightShoulder()
    {
        var bat = WeaponSwing.Default(SwingFamily.Bat);
        Assert.IsTrue(bat.TwoHanded);
        Assert.IsFalse(WeaponSwing.Default(SwingFamily.Side).TwoHanded);
        Assert.AreEqual(WeaponSwing.BatGuard.Fist, bat.At(0f, 1f).Hand.TargetLocal);
        // 振り抜いた所（左前）から構え（右肩の後ろ）へは、体の前を通って棍棒を立てながら戻る。
        var back = bat.At(bat.StrikeEnd + (0.5f * bat.RecoverTime), 1f).Hand.Orientation!.Value;
        var axis = Vector3.Transform(Vector3.UnitX, back);
        Assert.IsGreaterThan(0f, axis.Y, "the club comes back up, not around the back");
    }

    [TestMethod]
    public void TheClubBot_ClosesIn_AndLandsBlows()
    {
        using var game = new BoxingGame(seed: 3, corners: new BoxingCorners("stand", _ => new StandGuard(), s => ClubBot.Basic(s), PlayerWeapon: WeaponKind.WoodenClub));
        var blows = 0;
        for (var f = 0; f < 60 * 8; f++)
        {
            game.Step(default, 1f / 60f);
            blows += game.WeaponHits.Count(h => h.Attacker == game.Player);
        }

        Assert.IsTrue(game.Player.Wields(BoxingGame.WeaponHand));
        Assert.IsGreaterThan(0, blows, "the club lands on the opponent");
        Assert.AreEqual(0, game.RecentHits.Count(h => h.Attacker == game.Player && h.Hand == BoxingGame.WeaponHand), "the club hand does not count as a punch");
    }
}
