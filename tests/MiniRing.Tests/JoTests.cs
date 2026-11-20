using System.Numerics;
using MiniRing.Bots;
using MiniRing.Weapons;

namespace MiniRing.Tests;

/// <summary>
/// 杖（<see cref="WeaponKind.Jo"/>、合気道・杖道の 130 cm の木の丸棒）: 六尺棒と同じ作りの短く細い丸棒。両端が打ち、握りを滑らせられ、
/// 棒の型・教科書で戦う。
/// </summary>
[TestClass]
public sealed class JoTests
{
    private const float PuppetScale = 1f; // 実物大の物理: 人の寸法のまま（人形の単位では 0.355 で縮めていた）

    private sealed class Scripted(Func<FighterSense, FighterCommand> read) : IFighterControl
    {
        public FighterCommand Read(in FighterSense sense, float dt) => read(sense);
    }

    [TestMethod]
    public void TheJo_Is130cmScaledToThePuppet_ShorterAndLighterThanTheSixFootStaff()
    {
        var jo = Weapon.Build(WeaponKind.Jo);
        var staff = Weapon.Build(WeaponKind.Staff);
        Assert.AreEqual(1.30f * PuppetScale, 2f * jo.HalfExtents.X, 0.02817f, "130 cm at the human scale");
        Assert.IsLessThan(2f * staff.HalfExtents.X, 2f * jo.HalfExtents.X);
        Assert.IsLessThan(staff.FilledCount / 2, jo.FilledCount, "shorter and thinner: well under half the wood");
        Assert.AreEqual(jo.FilledCount, jo.Remaining(Voxels.BoxerAnatomy.Wood), "all wood");
        var ends = Weapon.StrikeEndsX(WeaponKind.Jo);
        Assert.HasCount(2, ends, "both ends strike");
        Assert.AreEqual(-ends[1], ends[0], 1e-6f);
        foreach (var x in ends)
        {
            Assert.IsGreaterThanOrEqualTo(0, jo.FilledCellAt(new Vector3(x - (MathF.Sign(x) * 0.005634f), 0f, 0f)), $"the cells reach the end at {x}");
        }

        Assert.IsTrue(Weapon.IsRod(WeaponKind.Jo));
        Assert.IsTrue(Weapon.IsBlunt(WeaponKind.Jo));
        Assert.IsTrue(Weapon.CanTwoHand(WeaponKind.Jo));
        Assert.IsTrue(Weapon.HasRigidFace(WeaponKind.Jo));
        Assert.IsFalse(Weapon.IsWorn(WeaponKind.Jo));
    }

    [TestMethod]
    public void TheJo_GripCanSlideAlmostEndToEnd()
    {
        var span = Weapon.SlideSpanOf(WeaponKind.Jo);
        Assert.IsNotNull(span);
        var (min, max) = span.Value;
        Assert.AreEqual(-max, min, 1e-6f);
        Assert.IsLessThan(Weapon.TipOf(WeaponKind.Jo).X, max, "the hand stops short of the striking end");
        Assert.IsGreaterThan(0.507f, max, "but can slide most of the way along");
        var (top, bottom) = Weapon.TwoHandGripsOf(WeaponKind.Jo);
        Assert.IsTrue(top.X <= max && bottom.X >= min, "the start grips are inside the span");
    }

    [TestMethod]
    public void TheJo_IsHeldInBothHands_AndUsesTheStaffBotAndItsOwnTextbook()
    {
        Assert.IsTrue(FormSwordsman.Handles(WeaponKind.Jo));
        Assert.IsTrue(WeaponCatalog.HasBot(WeaponKind.Jo));
        Assert.AreEqual("jo", WeaponCatalog.Textbook(WeaponKind.Jo).Name);
        Assert.Contains(WeaponKind.Jo, Voxels.WeaponVoxelModel.Editable);

        using var game = new BoxingGame(seed: 1, corners: new BoxingCorners("guard",
            _ => new Scripted(s => FormSwordsman.GuardCommand(WeaponKind.Jo, s, Vector3.UnitZ, 1.007f, 0.5f)), _ => new StandGuard(), CpuWeapon: WeaponKind.Jo, CpuTwoHanded: true));
        for (var i = 0; i < 302; i++)
        {
            game.Step(default, 1f / 60f);
        }

        var fighter = game.Cpu;
        Assert.IsTrue(fighter.Wields(Hand.Right));
        Assert.AreEqual(Hand.Left, fighter.SupportOf(Hand.Right), "the left hand grips the jo beside the right");
        var jo = game.WeaponOf(fighter)!;
        var grip = game.World.GetState(jo.Body.BodyId);
        var gripWorld = grip.Position + Vector3.Transform(Weapon.GripOf(WeaponKind.Jo), grip.Orientation);
        Assert.IsLessThan(0.0845f, Vector3.Distance(gripWorld, fighter.GloveCenter(Hand.Right)), "still in the right hand, not dropped");
    }

    [TestMethod]
    public void TheJo_HasAJapaneseName()
    {
        Assert.AreEqual("杖", MiniRing.Characters.FighterCharacter.WeaponLabel(WeaponKind.Jo));
    }
}
