using MiniRing.Motion;
using MiniRing.Weapons;

namespace MiniRing.Tests;

/// <summary>
/// 両手で持つ武器の型の、両手の間隔（腕の長さの単位）は、武器の握りの長さ（m）を基準の体の腕の長さで割った値（BASE_BODY_PLAN §9）。
/// 握りの長さは武器の形で、体が変わっても変わらない。基準の体の腕の長さを変えたら、このテストが外れて型の値の割り直しを知らせる。
/// </summary>
[TestClass]
public sealed class FormSpacingTests
{
    private static float StandardArmReach()
    {
        using var game = new BoxingGame(seed: 1);
        return game.Player.ArmReach;
    }

    [TestMethod]
    public void TwoHandedFormSpacings_AreTheWeaponsGripLengths_InTheStandardBodysArms()
    {
        var reach = StandardArmReach();
        Assert.AreEqual(Weapon.StaffHandSpacing, StaffForms.Spacing * reach, 0.002f, "棒: 肩幅ほどの握り");
        Assert.AreEqual(Weapon.HandSpacing, BatForms.Spacing * reach, 0.002f, "バット: 拳を並べた握り");
        Assert.AreEqual(0.2436f, SwordForms.KatanaSpacing * reach, 0.002f, "刀: 柄の両端");
        Assert.AreEqual(0.1696f, SwordForms.LongswordSpacing * reach, 0.002f, "ロングソード: 柄の両端");
    }
}
