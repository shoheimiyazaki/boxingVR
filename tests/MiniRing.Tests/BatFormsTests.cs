using System.Numerics;
using MiniRing.Bots;
using MiniRing.Motion;
using MiniRing.Weapons;

namespace MiniRing.Tests;

/// <summary>
/// バット（<see cref="WeaponKind.Bat"/>、木の棍棒を両手で握る）の型（<see cref="BatForms"/>）。型は目標の経路（両手の位置と棒の向き）なので、
/// 振る向き（右から・左から・上から・下から・斜め）・高さ・突き・グリップエンドが名前どおりの経路になっていることを、フレームの数値で確かめる。
/// 体がそれを追った結果（当たる・自傷）は handlelab・selfpath・aimlab の測定（docs/arena/bat_forms_*.md）。
/// </summary>
[TestClass]
public sealed class BatFormsTests
{
    private static SwordMotion Form(string name) => BatForms.Forms().Single(f => f.Name == name);

    private static float ArmReach()
    {
        using var game = new BoxingGame(SwordStance.Tuning);
        return game.Player.ArmReach;
    }

    /// <summary>キーの時刻（型の秒）のフレーム。</summary>
    private static MotionFrame At(string name, float seconds) => Form(name).Sample(seconds);

    [TestMethod]
    public void TheBat_IsTheWoodenClubHeldWithTwoHands_WithTheGripEndAsASecondStrikingEnd()
    {
        var bat = Weapon.Build(WeaponKind.Bat);
        var club = Weapon.Build(WeaponKind.WoodenClub);
        Assert.AreEqual(club.FilledCount, bat.FilledCount, "the same shape and wood as the club");
        Assert.AreEqual(club.HalfExtents, bat.HalfExtents);
        Assert.IsTrue(Weapon.CanTwoHand(WeaponKind.Bat));
        Assert.IsTrue(Weapon.IsBlunt(WeaponKind.Bat));
        Assert.IsTrue(Weapon.HasRigidFace(WeaponKind.Bat));
        Assert.IsFalse(Weapon.IsWorn(WeaponKind.Bat));
        var (top, bottom) = Weapon.TwoHandGripsOf(WeaponKind.Bat);
        Assert.AreEqual(Weapon.TwoHandGripsOf(WeaponKind.WoodenClub), (top, bottom), "the hands sit where they do on the two-handed club");
        Assert.AreEqual(top, Weapon.GripOf(WeaponKind.Bat), "a bat is held by the upper hand (like the two-handed swords)");
        var ends = Weapon.StrikeEndsX(WeaponKind.Bat);
        Assert.HasCount(2, ends, "the head end and the grip end");
        Assert.AreEqual(Weapon.TipOf(WeaponKind.Bat).X, ends[0]);
        Assert.IsLessThan(bottom.X, ends[1], "the grip end is behind the lower hand");
        Assert.HasCount(1, Weapon.StrikeEndsX(WeaponKind.WoodenClub), "the one-handed club is unchanged (its matches stay bit-identical)");
        Assert.AreEqual(Weapon.GripOf(WeaponKind.WoodenClub).X, bottom.X, "the one-handed club is still held by its grip end");
    }

    [TestMethod]
    public void TheBat_IsDrivenByTheSwordsmanLikeFormsAndATextbook()
    {
        Assert.IsTrue(FormSwordsman.Handles(WeaponKind.Bat));
        Assert.IsFalse(FormSwordsman.Handles(WeaponKind.WoodenClub), "the one-handed club keeps its own bot");
        Assert.IsTrue(WeaponCatalog.HasBot(WeaponKind.Bat));
        Assert.AreEqual("bat", WeaponCatalog.Textbook(WeaponKind.Bat).Name);
        Assert.AreEqual("バット", MiniRing.Characters.FighterCharacter.WeaponLabel(WeaponKind.Bat));
        Assert.AreEqual(BatForms.Spacing, FormSwordsman.Spacing(WeaponKind.Bat), 0f);
        Assert.AreEqual(BatForms.Spacing, Weapon.HandSpacing / ArmReach(), 0.003f, "the hand spacing is the bat's own, in arm lengths");
        var (light, heavy) = FormSwordsman.PlayerForms(WeaponKind.Bat);
        Assert.AreEqual("poke-body", light.Name);
        Assert.AreEqual("chop", heavy.Name);
        Assert.Throws<ArgumentOutOfRangeException>(() => FormSwordsman.CheckTechniqueNames(WeaponKind.Bat, new Dictionary<string, float> { ["tsuki"] = 1f }));
        FormSwordsman.CheckTechniqueNames(WeaponKind.Bat, new Dictionary<string, float> { ["chop"] = 2f, ["butt"] = 0f });
    }

    [TestMethod]
    public void EveryForm_StartsAndEndsAtTheGuard()
    {
        var guard = BatForms.Guard();
        Assert.IsGreaterThanOrEqualTo(14, BatForms.Forms().Count);
        Assert.AreEqual(BatForms.Forms().Count, BatForms.Forms().Select(f => f.Name).Distinct().Count());
        foreach (var form in BatForms.Forms())
        {
            foreach (var frame in new[] { form.Frames[0], form.Frames[^1] })
            {
                Assert.IsLessThan(0.02f, Vector3.Distance(frame.Right, guard.Right), form.Name);
                Assert.IsGreaterThan(0.999f, Vector3.Dot(frame.Blade, guard.Blade), form.Name);
            }
        }
    }

    [TestMethod]
    public void HorizontalSwings_StartOnOneSideAndFinishOnTheOther_AtTheirOwnHeight()
    {
        // +X は自分の左。右から振る = 棒を右へ向けて引き、左へ払う。
        foreach (var (name, side) in new[] { ("swing-right", -1f), ("swing-mid", -1f), ("swing-low", -1f), ("swing-left", 1f) })
        {
            var windup = At(name, 0.6f);
            var strike = At(name, 1.1f);
            Assert.IsGreaterThan(0.6f, windup.Blade.X * side, $"{name}: the bat is pulled out to its own side");
            Assert.IsGreaterThan(0.5f, -strike.Blade.X * side, $"{name}: and swept to the other side");
            Assert.IsGreaterThan(0.2f, windup.Right.X * side, $"{name}: the hands are pulled to that side too");
            Assert.IsGreaterThan(0.65f, strike.Right.Z, $"{name}: arms stretch forward at the strike");
        }

        var right = At("swing-right", 1.1f);
        var mid = At("swing-mid", 1.1f);
        var low = At("swing-low", 1.1f);
        Assert.IsGreaterThan(mid.Right.Y + 0.3f, right.Right.Y, "head/shoulder height is above belly height");
        Assert.IsGreaterThan(low.Right.Y + 0.2f, mid.Right.Y, "belly height is above leg height");
        Assert.IsLessThan(mid.Blade.Y, low.Blade.Y, "the low swing points the bat down");
        Assert.IsLessThan(-0.5f, low.Blade.Y);
    }

    [TestMethod]
    public void Chops_StartAboveTheHeadAndFinishInFront_AndCutFromTheirOwnSide()
    {
        var chopWindup = At("chop", 0.75f);
        var chopStrike = At("chop", 1.15f);
        Assert.IsGreaterThan(0.95f, chopWindup.Blade.Y, "straight overhead: the bat stands up");
        Assert.IsGreaterThan(0.5f, chopWindup.Right.Y, "the hands are above the shoulders");
        Assert.IsGreaterThan(0.9f, chopStrike.Blade.Z, "and comes down to point at the opponent");
        Assert.IsLessThan(0f, chopStrike.Blade.Y);
        Assert.IsLessThan(0.15f, MathF.Abs(chopWindup.Blade.X), "in the middle plane, not leaning to a side");

        var diagonalRight = At("chop-right", 0.75f);
        var diagonalLeft = At("chop-left", 0.75f);
        Assert.IsLessThan(-0.3f, diagonalRight.Blade.X, "from the upper right: the bat starts out on the right");
        Assert.IsGreaterThan(0.3f, diagonalLeft.Blade.X, "from the upper left: the bat starts out on the left");
        Assert.IsGreaterThan(0.5f, diagonalRight.Blade.Y);
        Assert.IsGreaterThan(0.5f, diagonalLeft.Blade.Y);
        Assert.IsGreaterThan(0.3f, At("chop-right", 1.15f).Blade.X, "it ends crossing to the other side, pointing down-forward");
        Assert.IsLessThan(-0.3f, At("chop-left", 1.15f).Blade.X);
        Assert.IsLessThan(0f, At("chop-right", 1.15f).Blade.Y);
        Assert.IsLessThan(0f, At("chop-left", 1.15f).Blade.Y);
    }

    [TestMethod]
    public void Scoops_StartLowOnTheirOwnSide_AndFinishRisingTowardsTheChin()
    {
        foreach (var (name, side) in new[] { ("scoop-right", -1f), ("scoop-left", 1f) })
        {
            var windup = At(name, 0.6f);
            var strike = At(name, 1.1f);
            Assert.IsLessThan(-0.4f, windup.Right.Y, $"{name}: the hands drop low");
            Assert.IsLessThan(-0.4f, windup.Blade.Y, $"{name}: with the bat pointing down");
            Assert.IsGreaterThan(0.5f, windup.Blade.X * side, $"{name}: out on its own side");
            Assert.IsGreaterThan(0.3f, strike.Blade.Y, $"{name}: the bat swings up");
            Assert.IsGreaterThan(0.3f, -strike.Blade.X * side, $"{name}: and across");
            Assert.IsGreaterThan(windup.Right.Y + 0.5f, strike.Right.Y, $"{name}: the hands rise");
        }
    }

    [TestMethod]
    public void Pokes_ThrustTheHeadEndStraightAhead_AtTheirOwnHeight()
    {
        var body = At("poke-body", 0.6f);
        var face = At("poke-face", 0.6f);
        foreach (var poke in new[] { body, face })
        {
            Assert.IsGreaterThan(0.95f, poke.Blade.Z, "the bat points straight at the opponent");
            Assert.IsGreaterThan(0.95f, poke.Right.Z, "the arms are stretched");
        }

        Assert.IsGreaterThan(body.Right.Y + 0.3f, face.Right.Y, "the face poke is higher than the body poke");
        Assert.IsGreaterThan(0.5f, Form("poke-body").Frames.Max(f => f.Velocity.Z), "and the body steps in");
    }

    [TestMethod]
    public void TheGripEndStrike_FlipsTheBatAndPunchesWithTheButtLeading()
    {
        var butt = Form("butt");
        var strike = butt.Sample(0.9f);
        Assert.IsLessThan(-0.5f, strike.Blade.Z, "the head end points back, over the shoulder");
        Assert.IsGreaterThan(0.5f, strike.Blade.Y, "and up, so the bat's line stays clear of the body");
        Assert.IsLessThan(-0.3f, At("butt", 0.6f).Right.X, "the hands are pulled to the right, beside the body");
        var gripEnd = Weapon.StrikeEndsX(WeaponKind.Bat)[1];
        var top = Weapon.GripOf(WeaponKind.Bat).X;
        var tip = (gripEnd - top) / ArmReach(); // 上の手から見た握りの端（腕の長さ。負 = 手より後ろ）
        var buttPoint = strike.Right + (strike.Blade * tip);
        Assert.IsGreaterThan(strike.Right.Z + 0.05f, buttPoint.Z, "the grip end is the part in front of the hands");
        Assert.IsGreaterThan(0.95f, buttPoint.Z, "reached out in front of the chest");
        Assert.IsLessThan(0.5f, butt.Sample(0.6f).Right.Z, "the hands are pulled in before they punch out");
        Assert.AreEqual(1.1f, butt.StrikeUntil, "the long recovery is not part of the strike");
    }

    [TestMethod]
    public void TheFrontKick_KeepsTheGuardAndAddsTheKarateKick()
    {
        var kick = Form("front-kick");
        Assert.IsNotNull(kick.Kick);
        Assert.AreEqual(KickSpec.Of(KickTechnique.Front), kick.Kick);
        Assert.IsLessThan(0.02f, Vector3.Distance(kick.Frames[0].Right, kick.Frames[^1].Right), "the hands hold the guard");
    }

    [TestMethod]
    public void TheBotPool_KeepsTheLongFormsAndLeavesTheInnerRangeOnesOut()
    {
        var arm = ArmReach();
        var pool = FormSwordsman.Techniques(WeaponKind.Bat, arm).Select(t => t.Name).ToHashSet();
        var all = FormSwordsman.Techniques(WeaponKind.Bat, arm, 0f).ToDictionary(t => t.Name, t => t.Range);
        Assert.HasCount(BatForms.Forms().Count, all);
        Assert.IsFalse(pool.Contains("butt"), "the grip-end strike is an inner-range form");
        Assert.IsFalse(pool.Contains("front-kick"), "the front kick is an inner-range form");
        foreach (var name in new[] { "swing-right", "swing-left", "swing-mid", "swing-low", "chop", "chop-right", "chop-left", "scoop-right", "scoop-left", "poke-body", "poke-face", "snap" })
        {
            Assert.IsTrue(pool.Contains(name), name);
            Assert.IsGreaterThanOrEqualTo(FormSwordsman.MinReach, all[name], name);
        }

        Assert.IsLessThan(all["poke-body"], all["butt"], "the grip end reaches less far than the head end");
        Assert.IsLessThan(all["butt"], all["front-kick"], "and the kick is the closest");
    }

    [TestMethod]
    public void ABatBot_SwingsAtAStandingOpponent_AndHitsWithTheWood()
    {
        var hits = 0;
        var swings = new HashSet<string>();
        var arm = ArmReach();
        using var game = new BoxingGame(SwordStance.Tuning, seed: 5, corners: new BoxingCorners("bat",
            s => FormSwordsman.Bot(WeaponKind.Bat, s, arm, SwordStance.Tuning, WeaponCatalog.Textbook(WeaponKind.Bat)), _ => new StandGuard(), CpuWeapon: WeaponKind.Bat, CpuTwoHanded: true));
        var bot = game.CpuControl;
        for (var t = 0f; t < 25f; t += 1f / 60f)
        {
            game.Step(default, 1f / 60f);
            hits += game.WeaponHits.Count(h => h.Attacker == game.Cpu);
            if (Unwrap(game.CpuControl) is { Current: { } current })
            {
                swings.Add(current.Name);
            }
        }

        Assert.IsNotNull(bot);
        Assert.IsGreaterThan(0, hits, "the bat reaches the opponent");
        Assert.IsGreaterThan(2, swings.Count, "and uses more than one or two forms");
        Assert.IsTrue(game.Cpu.Wields(Hand.Right), "still holding the bat");
    }

    private static SwordsmanBot? Unwrap(IFighterControl? control) => control switch
    {
        SwordsmanBot sword => sword,
        DisarmedFallback fallback => fallback.Armed as SwordsmanBot,
        _ => null,
    };
}
