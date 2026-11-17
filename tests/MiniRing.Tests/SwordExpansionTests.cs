using System.Numerics;
using MiniRing.Bots;
using MiniRing.Motion;
using MiniRing.Weapons;

namespace MiniRing.Tests;

/// <summary>追加した型は姿勢でなく、向き・打点・構えへ戻る目標の経路で確かめる。</summary>
[TestClass]
public sealed class SwordExpansionTests
{
    [TestMethod]
    [DataRow(WeaponKind.Katana, "shomen")]
    [DataRow(WeaponKind.Katana, "migi-nagi")]
    [DataRow(WeaponKind.Katana, "hidari-nagi")]
    [DataRow(WeaponKind.Katana, "gyaku-kiriage")]
    [DataRow(WeaponKind.Katana, "gyakufu")]
    [DataRow(WeaponKind.Katana, "kote")]
    [DataRow(WeaponKind.Katana, "kote-men")]
    [DataRow(WeaponKind.Katana, "do-tsuki")]
    [DataRow(WeaponKind.Katana, "katate-tsuki")]
    [DataRow(WeaponKind.Katana, "tsuka-ate")]
    [DataRow(WeaponKind.Katana, "oshi-giri")]
    [DataRow(WeaponKind.Katana, "hiki-do")]
    [DataRow(WeaponKind.Katana, "mae-geri")]
    [DataRow(WeaponKind.Longsword, "scheitelhau")]
    [DataRow(WeaponKind.Longsword, "schielhau")]
    [DataRow(WeaponKind.Longsword, "krumphau")]
    [DataRow(WeaponKind.Longsword, "zwerchhau-left")]
    [DataRow(WeaponKind.Longsword, "unterhau-left")]
    [DataRow(WeaponKind.Longsword, "stich-low")]
    [DataRow(WeaponKind.Longsword, "knaufschlag")]
    [DataRow(WeaponKind.Longsword, "schnitt")]
    [DataRow(WeaponKind.Longsword, "tritt")]
    public void NewForms_ReturnToTheirGuard_AndKeepTheBladeOutsideTheBody(WeaponKind weapon, string name)
    {
        var form = FormSwordsman.Forms(weapon).Single(f => f.Name == name);
        var first = form.Frames[0];
        var last = form.Frames[^1];
        Assert.IsLessThan(0.001f, Vector3.Distance(first.Right, last.Right), name);
        // FromKeys は最終キーまでの端数（1/60 秒未満）を切り捨てる。向きの誤差は 0.12 度未満で構えに戻る。
        Assert.IsLessThan(0.002f, Vector3.Distance(first.Blade, last.Blade), name);
        var clearance = new SelfClearance();
        var length = FormSwordsman.BladeLength(weapon, 0.188f);
        Assert.IsTrue(form.Frames.All(f => float.IsFinite(f.Orientation.W)), name);
        var inside = form.Frames.Count(f => clearance.Along(f.Right, f.Blade, length, f.Head).Clearance < 0f);
        Assert.AreEqual(0, inside, $"{name}: 目標の刃の線が自分の体を通らない");
    }

    [TestMethod]
    [DataRow("migi-nagi", -1)]
    [DataRow("hidari-nagi", 1)]
    public void HorizontalCuts_CrossFromTheNamedSide_AtTheSameHeight(string name, int side)
    {
        var form = FormSwordsman.Forms(WeaponKind.Katana).Single(f => f.Name == name);
        Vector3 Tip(float t) { var f = form.Sample(t); return f.Right + (f.Blade * 1.7f); }
        var a = Tip(0.6f);
        var b = Tip(1.1f);
        Assert.IsGreaterThan(1f, (a.X - b.X) * side);
        Assert.AreEqual(a.Y, b.Y, 0.03f);
    }

    [TestMethod]
    public void Gyakufu_RisesVertically_AndTheTwoThrustsHaveDifferentHeights()
    {
        var forms = FormSwordsman.Forms(WeaponKind.Katana);
        var up = forms.Single(f => f.Name == "gyakufu");
        Assert.IsGreaterThan(1f, up.Sample(1f).Blade.Y - up.Sample(0.6f).Blade.Y);
        var face = forms.Single(f => f.Name == "tsuki").Sample(0.45f);
        var body = forms.Single(f => f.Name == "do-tsuki").Sample(0.45f);
        Assert.IsGreaterThan(0.3f, (face.Right + face.Blade).Y - (body.Right + body.Blade).Y);
    }

    [TestMethod]
    [DataRow(WeaponKind.Katana, "tsuka-ate", "mae-geri")]
    [DataRow(WeaponKind.Longsword, "knaufschlag", "tritt")]
    public void CloseTechniques_UseThePommelAndTheExistingFrontKick(WeaponKind weapon, string pommel, string kick)
    {
        using var game = new BoxingGame();
        var reach = game.Player.ArmReach;
        var techniques = FormSwordsman.Techniques(weapon, reach, 0f);
        var hit = techniques.Single(t => t.Name == pommel);
        Assert.IsLessThan(Weapon.GripOf(weapon).X, MotionAim.StrikingEndX(hit, reach));
        Assert.IsLessThan(0.33f, hit.Range);
        Assert.AreEqual(hit.Range / reach, MotionAim.Landing(hit, reach).Point.Z, 0.0001f);
        var front = techniques.Single(t => t.Name == kick);
        Assert.AreEqual(KickSpec.Of(KickTechnique.Front).Standoff * reach, front.Range, 0.0001f);
        var time = front.Motion.Duration * KickSpec.ContactShare;
        var command = MotionImitation.Command(front.Motion, time, game.PlayerSense, Vector3.UnitZ, SwordStance.Rate, FighterTuning.Default.WalkSpeed, FighterTuning.Default.MaxTorsoTwistRadians);
        Assert.IsNotNull(command.RightLeg);
        Assert.AreEqual(front.Motion.Frames[0].Right * reach, command.Right.TargetLocal);
        Assert.IsLessThan(techniques.Where(t => t.Motion.Kick is null).Max(t => t.Range), front.Range);
    }

    [TestMethod]
    [DataRow(WeaponKind.Katana, "tsuka-ate", "mae-geri")]
    [DataRow(WeaponKind.Longsword, "knaufschlag", "tritt")]
    public void LosingTheBladeTip_DoesNotShortenThePommelOrFoot(WeaponKind weapon, string pommel, string kick)
    {
        using var game = new BoxingGame();
        foreach (var technique in FormSwordsman.Techniques(weapon, game.Player.ArmReach, 0f).Where(t => t.Name == pommel || t.Name == kick))
        {
            var bot = new SwordsmanBot([technique], FormSwordsman.Guard(weapon), 1, SwordStance.Rate, exact: true);
            var sense = game.PlayerSense with
            {
                OpponentChest = game.PlayerSense.Self.Origin + (game.PlayerSense.Self.Forward * technique.Range),
                SelfWeapon = new WeaponSense(true, 0.2f, 0.3f),
            };
            bot.Read(sense, 1f / 60f);
            Assert.IsNotNull(bot.Current, $"{technique.Name}: 剣先が 10 cm 欠けても脚・柄頭の間合いで出せる");
        }
    }

    [TestMethod]
    public void TheMeasuredSkills_CoverTheHandsShouldersFaceBodyAndLegs()
    {
        using var game = new BoxingGame();
        var techniques = new[] { WeaponKind.Katana, WeaponKind.Longsword }.SelectMany(w => FormSwordsman.Techniques(w, game.Player.ArmReach, 0f)).ToList();
        foreach (var target in new[] { BodyPartKind.Forearm, BodyPartKind.UpperArm, BodyPartKind.Head, BodyPartKind.Chest, BodyPartKind.Thigh })
        {
            Assert.IsTrue(techniques.Any(t => t.Skill?.GetValueOrDefault(target) > 0f), $"{target}: aimlab の実測で打てる型を少なくとも一つ持つ");
        }
        Assert.IsTrue(techniques.All(t => t.Skill is { Count: 7 }), "全型を同じ 7 部位・5 構えで測る");
    }
}
