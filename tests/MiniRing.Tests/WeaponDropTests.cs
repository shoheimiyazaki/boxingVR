using System.Numerics;
using MiniRing.Bots;
using MiniRing.Weapons;

namespace MiniRing.Tests;

/// <summary>
/// 武器を手放す（<see cref="HandCommand.LetGo"/>、2026-11-13）と、折れた武器を短いまま使うか捨てて素手で戦うかのボットの判断
/// （<see cref="DisarmedFallback"/>）。
/// </summary>
[TestClass]
public sealed class WeaponDropTests
{
    private sealed class Scripted(Func<FighterSense, FighterCommand> read) : IFighterControl
    {
        public FighterCommand Read(in FighterSense sense, float dt) => read(sense);
    }

    private static FighterCommand Guard(in FighterSense s, bool letGo = false)
    {
        var face = s.OpponentChest - s.Self.Origin;
        face.Y = 0f;
        var reach = s.ArmReach;
        var left = new HandCommand(new Vector3(0.15f * reach, 0.1f * reach, 0.45f * reach), false);
        var right = new HandCommand(new Vector3(-0.15f * reach, 0.1f * reach, 0.45f * reach), false, LetGo: letGo);
        return new FighterCommand(Vector3.Zero, Vector3.Normalize(face), left, right);
    }

    private static void Play(BoxingGame game, float seconds)
    {
        for (var t = 0f; t < seconds; t += 1f / 60f)
        {
            game.Step(default, 1f / 60f);
        }
    }

    [TestMethod]
    public void LettingGo_DropsTheClub_AndTheEmptyHandBecomesABareFist()
    {
        var drop = false;
        using var game = new BoxingGame(seed: 1, corners: new BoxingCorners("club", _ => new Scripted(s => Guard(s, drop)), _ => new StandGuard(), CpuWeapon: WeaponKind.WoodenClub));
        Play(game, 1f);
        Assert.IsTrue(game.Cpu.Wields(BoxingGame.WeaponHand));
        Assert.IsTrue(game.CpuSense.SelfArmed);

        var club = game.WeaponOf(game.Cpu)!;
        var held = game.World.GetState(club.Body.BodyId).Position.Y;
        drop = true;
        Play(game, 1f / 60f);
        Assert.IsFalse(game.Cpu.Wields(BoxingGame.WeaponHand), "the hand let go");
        Assert.IsFalse(game.Cpu.WearsGlove(BoxingGame.WeaponHand), "no fist is made inside the club it just dropped");
        Play(game, 0.6f);
        Assert.IsTrue(game.Cpu.WearsGlove(BoxingGame.WeaponHand), "once the club has fallen clear, the empty hand is a bare voxel fist");
        Assert.AreEqual(MiniRing.Weapons.HandWear.None.Kind(), game.GloveOf(game.Cpu, BoxingGame.WeaponHand)!.Kind);
        Assert.IsFalse(game.CpuSense.SelfArmed);

        Assert.IsLessThan(held - 0.05f, game.World.GetState(club.Body.BodyId).Position.Y, "the club fell");
    }

    private static FighterSense Sense(float reach, float fullReach) =>
        new(default, 0.17f, new Vector3(0f, 1f, 0.5f), new Vector3(0f, 0.9f, 0.5f), true, SelfArmed: true, SelfWeapon: new WeaponSense(true, reach, fullReach));

    [TestMethod]
    public void ABrokenWeapon_IsKeptWhileItStillReaches_AndThrownAwayWhenOnlyTheHiltIsLeft()
    {
        var armed = new Scripted(s => Guard(s));
        var keep = new DisarmedFallback(armed, new StandGuard());
        var kept = keep.Read(Sense(reach: 0.2f, fullReach: 0.3f), 1f / 240f);
        Assert.IsFalse(kept.Right.LetGo, "two thirds of the blade still reach well past the fist");
        Assert.IsTrue(keep.FightsWithBrokenWeapon);

        var whole = new DisarmedFallback(armed, new StandGuard());
        Assert.IsFalse(whole.Read(Sense(reach: 0.3f, fullReach: 0.3f), 1f / 240f).Right.LetGo);
        Assert.IsFalse(whole.FightsWithBrokenWeapon, "a whole weapon is just a weapon");

        var stub = new DisarmedFallback(armed, new StandGuard());
        var thrown = stub.Read(Sense(reach: 0.03f, fullReach: 0.3f), 1f / 240f);
        Assert.IsTrue(thrown.Right.LetGo && thrown.Left.LetGo, "only the hilt is left: let go with both hands");
        Assert.IsTrue(stub.Discarded);

        var fists = stub.Read(new FighterSense(default, 0.17f, new Vector3(0f, 1f, 0.5f), new Vector3(0f, 0.9f, 0.5f), true, SelfArmed: false), 1f / 240f);
        Assert.IsTrue(stub.Disarmed, "once it is gone, the fists take over");
        Assert.IsFalse(fists.Right.LetGo);
    }

    [TestMethod]
    public void InAMatch_AClubSnappedAtTheHandle_IsThrownAway_AndTheBotBoxesBareFisted()
    {
        using var game = new BoxingGame(seed: 1, corners: new BoxingCorners("club",
            s => new DisarmedFallback(new Scripted(x => Guard(x)), new StandGuard()), _ => new StandGuard(), CpuWeapon: WeaponKind.WoodenClub));
        Play(game, 1f);
        var club = game.WeaponOf(game.Cpu)!;
        var full = game.CpuSense.SelfWeapon;
        Assert.IsTrue(full.Held);
        Assert.AreEqual(0f, full.Lost, 1e-3f, "a whole club");

        // 握りのすぐ先で折る（握りから 1 cm ほどしか残らない）。
        var grid = club.Body.Grid;
        var grip = game.Cpu.WieldedGrip(BoxingGame.WeaponHand)!.Value.X;
        var slice = (int)MathF.Floor((grip + 0.01f - club.Body.GridCenterLocal.X + grid.HalfExtents.X) / grid.CellSize.X);
        Assert.IsNotNull(club.Body.SplitAt(0, slice, takeHigh: true));
        Assert.IsLessThan(DisarmedFallback.DefaultKeepReach * game.Cpu.ArmReach, game.CpuSense.SelfWeapon.Reach, "only the handle is left");

        Play(game, 1f);
        Assert.IsFalse(game.Cpu.Wields(BoxingGame.WeaponHand), "the bot threw the useless handle away");
        Assert.IsTrue(game.Cpu.WearsGlove(BoxingGame.WeaponHand), "and boxes with a bare fist");
    }

    [TestMethod]
    public void AWholePointedBlade_HasLostNothing()
    {
        using var game = new BoxingGame(seed: 1, corners: new BoxingCorners("katana", _ => new Scripted(s => Guard(s)), _ => new StandGuard(), CpuWeapon: WeaponKind.Katana));
        Play(game, 0.5f);
        var sense = game.CpuSense.SelfWeapon;
        Assert.IsTrue(sense.Held);
        Assert.AreEqual(0f, sense.Lost, "the point's last millimetres are no cells, but nothing broke off");
    }

    [TestMethod]
    public void InAMatch_AClubThatLostOnlyItsTip_IsKept_AndTheBotStandsCloser()
    {
        using var game = new BoxingGame(seed: 1, corners: new BoxingCorners("club",
            s => new DisarmedFallback(new Scripted(x => Guard(x)), new StandGuard()), _ => new StandGuard(), CpuWeapon: WeaponKind.WoodenClub));
        Play(game, 1f);
        var club = game.WeaponOf(game.Cpu)!;
        var full = game.CpuSense.SelfWeapon.Reach;
        var grid = club.Body.Grid;
        var grip = game.Cpu.WieldedGrip(BoxingGame.WeaponHand)!.Value.X;
        var slice = (int)MathF.Floor((grip + (0.7f * full) - club.Body.GridCenterLocal.X + grid.HalfExtents.X) / grid.CellSize.X);
        Assert.IsNotNull(club.Body.SplitAt(0, slice, takeHigh: true));
        var broken = game.CpuSense.SelfWeapon;
        Assert.IsGreaterThan(0.2f * full, broken.Lost, "the tip is gone");

        Play(game, 0.5f);
        Assert.IsTrue(game.Cpu.Wields(BoxingGame.WeaponHand), "still worth holding");
    }
}
