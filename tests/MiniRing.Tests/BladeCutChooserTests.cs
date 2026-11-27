using MiniRing.Arena;
using MiniRing.Bots;
using MiniRing.Characters;
using MiniRing.Weapons;

namespace MiniRing.Tests;

/// <summary>
/// 刃の通り道で切る剣士（<see cref="BladeCutChooser"/>）: 型から振る面を読み、円弧は肩の届く範囲の中だけを振るので、自分の体を斬らない。
/// 比べは同じビルドの中（型の振り vs 刃の通り道）だけ。
/// </summary>
[TestClass]
public sealed class BladeCutChooserTests
{
    [TestMethod]
    public void TheFacesStraightCutSwingsInAVerticalPlane()
    {
        var shomen = FormSwordsman.Forms(WeaponKind.Katana).First(f => f.Name == "shomen");
        var preference = CutPreference.Of(shomen);
        Assert.IsNotNull(preference);
        // 正面の斬り下ろしの面は縦（法線がほぼ左右）で、刃は下へ進む。
        Assert.IsGreaterThan(0.9f, MathF.Abs(preference.Value.Normal.X), $"normal {preference.Value.Normal}");
        Assert.IsLessThan(0f, preference.Value.Travel.Y, $"travel {preference.Value.Travel}");
    }

    [TestMethod]
    public void TurningThePlaneKeepsTravelInsideIt()
    {
        var preference = CutPreference.Of(FormSwordsman.Forms(WeaponKind.Katana).First(f => f.Name == "shomen"))!.Value;
        foreach (var angle in new BladeCutStyle().PlaneTurns)
        {
            var turned = preference.Turned(angle);
            Assert.AreEqual(0f, System.Numerics.Vector3.Dot(turned.Normal, turned.Travel), 1e-4f);
        }
    }

    [TestMethod]
    [TestCategory("slow")]
    public void TheArcSwordsmanCutsWithoutCuttingHimself()
    {
        // jin の面は型のままだと振り下ろす刃が自分の体を削る（同じ種・同じ相手で比べる）。円弧は肩の届く範囲の中だけを通る。
        var who = CharacterRoster.Get("jin");
        var educated = who.WeaponStyle!;
        var technique = SwordPractice.Technique(who, "shomen", educated.Forms!["shomen"]);
        var form = SwordPractice.Swing(who, educated, technique, 11, SwordPractice.Victim("konrad"));
        var cut = SwordPractice.Swing(who, educated with { BladeCut = new BladeCutStyle() }, technique, 11, SwordPractice.Victim("konrad"));
        Assert.IsTrue(cut.Swung, "the arc is swung");
        Assert.IsGreaterThan(0, cut.Cells, "the arc reaches the opponent");
        Assert.IsLessThanOrEqualTo(form.SelfCells, cut.SelfCells, $"no more self cuts than the form (form {form.SelfCells}, arc {cut.SelfCells})");
    }
}
