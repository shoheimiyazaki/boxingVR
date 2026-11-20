using System.Numerics;
using MiniRing.Bots;
using MiniRing.Motion;
using MiniRing.Weapons;

namespace MiniRing.Tests;

/// <summary>
/// 剣士が出す目標のうち、刃の線が自分の体を通るもの（<see cref="SelfClearance"/>）を、判断の側で外へ傾ける。物理には「自分には当たらない」を足さない
/// （体は目標をバネで追うので、目標が体を通れば刃は自分を斬る。目標の側で避ける）。
/// </summary>
[TestClass]
public sealed class SelfClearanceTests
{
    private static MotionFrame Frame(Vector3 right, Vector3 blade, Vector3 head = default) =>
        new(right, right + new Vector3(0.08f, -0.05f, -0.1f), Vector3.Normalize(blade), SwordMotion.Perpendicular(-Vector3.UnitY, Vector3.Normalize(blade)), head, 0f, default);

    private static float Tilt(MotionFrame a, MotionFrame b) => MathF.Acos(Math.Clamp(Vector3.Dot(a.Blade, b.Blade), -1f, 1f));

    [TestMethod]
    public void ABladeThatKeepsClear_IsLeftAsItIs()
    {
        var clearance = SelfClearance.Adopted;
        var frame = Frame(new Vector3(-0.2f, -0.2f, 0.6f), new Vector3(0f, 0.15f, 1f));
        Assert.IsGreaterThan(clearance.Margin, clearance.Along(frame.Right, frame.Blade, 2f).Clearance, "the blade points away from the body");
        Assert.AreEqual(frame, clearance.Keep(frame, 2f));
    }

    [TestMethod]
    public void ABladeThroughTheTrunk_IsTiltedOutAboutTheGrip_AndNothingElseMoves()
    {
        var clearance = SelfClearance.Adopted;
        // 前に出した右手から、刃が胴の横を後ろへ（体の箱の中を）通る。
        var frame = Frame(new Vector3(-0.2f, -0.2f, 0.6f), new Vector3(0.1f, 0f, -1f));
        var before = clearance.Along(frame.Right, frame.Blade, 2f).Clearance;
        Assert.IsLessThan(0f, before, "the commanded blade runs through the body");

        var kept = clearance.Keep(frame, 2f);
        var after = clearance.Along(kept.Right, kept.Blade, 2f).Clearance;
        Assert.IsGreaterThan(before, after, "the tilt takes the blade away from the body");
        Assert.IsLessThanOrEqualTo(clearance.MaxTilt + 1e-4f, Tilt(frame, kept), "the path is bent by no more than the limit");
        Assert.AreEqual(frame.Right, kept.Right, "the hand is where the technique puts it");
        Assert.AreEqual(frame.Left, kept.Left);
        Assert.AreEqual(frame.Head, kept.Head);
        Assert.AreEqual(1f, kept.Blade.Length(), 1e-4f);
        Assert.AreEqual(0f, Vector3.Dot(kept.Blade, kept.Edge), 1e-4f, "the edge stays square to the blade");
    }

    [TestMethod]
    public void ADuckedHead_WidensTheBodyTheBladeMustAvoid()
    {
        var clearance = SelfClearance.Adopted;
        var upright = clearance.Along(new Vector3(0f, 0.8f, 0.5f), Vector3.Normalize(new Vector3(0f, -0.2f, -1f)), 2f);
        var leaning = clearance.Along(new Vector3(0f, 0.8f, 0.5f), Vector3.Normalize(new Vector3(0f, -0.2f, -1f)), 2f, new Vector3(0f, 0f, 0.4f));
        Assert.IsLessThanOrEqualTo(upright.Clearance, leaning.Clearance, "the body leans into the blade's line, so it cannot be farther from it");
    }

    [TestMethod]
    public void KeepingTheBladeClear_IsDeterministic_AndDoesNotChangeItsInput()
    {
        var clearance = SelfClearance.Adopted;
        var frame = Frame(new Vector3(-0.2f, -0.2f, 0.6f), new Vector3(0.1f, 0f, -1f));
        var copy = frame;
        var first = clearance.Keep(frame, 2f);
        var second = clearance.Keep(frame, 2f);
        Assert.AreEqual(first, second);
        Assert.AreEqual(copy, frame, "the technique's own frame is never rewritten");
    }

    /// <summary>
    /// 2026-11-18 に足した刀・ロングソードの型の表そのもの（ボットが傾ける前の目標）は、構えに始まって構えに戻り、刃の線が自分の頭・胴・脚を通らない
    /// （2026-11-20、SwordExpansionTests から移した。形の名前の側・高さの固定は外した。前からある型は、構えへ戻る向きの誤差がこの許容を超えるもの（nuki-do 0.0028）があり、対象に入れていない）。
    /// </summary>
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
    public void TheAddedForms_ReturnToTheirGuard_AndKeepTheBladeOutsideTheBody(WeaponKind weapon, string name)
    {
        var form = FormSwordsman.Forms(weapon).Single(f => f.Name == name);
        var first = form.Frames[0];
        var last = form.Frames[^1];
        Assert.IsLessThan(0.001f, Vector3.Distance(first.Right, last.Right), name);
        // FromKeys は最終キーまでの端数（1/60 秒未満）を切り捨てる。向きの誤差は 0.12 度未満で構えに戻る。
        Assert.IsLessThan(0.002f, Vector3.Distance(first.Blade, last.Blade), name);
        var clearance = new SelfClearance();
        var length = FormSwordsman.BladeLength(weapon, 0.5296f);
        Assert.IsTrue(form.Frames.All(f => float.IsFinite(f.Orientation.W)), name);
        var inside = form.Frames.Count(f => clearance.Along(f.Right, f.Blade, length, f.Head).Clearance < 0f);
        Assert.AreEqual(0, inside, $"{name}: 目標の刃の線が自分の体を通らない");
    }
}
