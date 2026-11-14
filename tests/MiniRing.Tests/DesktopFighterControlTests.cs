using MiniRing.Desktop;
using MiniRing.Weapons;

namespace MiniRing.Tests;

[TestClass]
public sealed class DesktopFighterControlTests
{
    [TestMethod]
    [DataRow(WeaponKind.Katana)]
    [DataRow(WeaponKind.Longsword)]
    [DataRow(WeaponKind.WoodenClub)]
    public void EquippedWeaponFollowsAttackPath(WeaponKind weapon)
    {
        var corners = BoxingCorners.Default with
        {
            PlayerWeapon = weapon,
            PlayerTwoHanded = weapon != WeaponKind.WoodenClub,
        };
        using var game = new BoxingGame(corners: corners);
        var control = new DesktopFighterControl(weapon);
        var sense = game.PlayerSense;
        Assert.IsTrue(sense.SelfWeapon.Held);
        var guard = control.Read(default, sense, 1f / 60f);
        _ = control.Read(new FighterIntent(default, Heavy: true), sense, 1f / 60f);
        var swing = control.Read(default, sense, 1f / 60f);
        Assert.IsNotNull(swing.Right.Orientation);
        Assert.AreNotEqual(guard.Right, swing.Right);
    }

    [TestMethod]
    public void KarateHeavyProducesKickAndReleaseDoesNotCancelIt()
    {
        using var game = new BoxingGame(corners: BoxingCorners.Default with { PlayerWear = HandWear.None });
        var control = new DesktopFighterControl(karate: true);
        var sense = game.PlayerSense;
        Assert.IsNull(control.Read(default, sense, 1f / 60f).RightLeg);
        Assert.IsNotNull(control.Read(new FighterIntent(default, Heavy: true), sense, 1f / 60f).RightLeg);
        Assert.IsNotNull(control.Read(default, sense, 1f / 60f).RightLeg);
    }
}
