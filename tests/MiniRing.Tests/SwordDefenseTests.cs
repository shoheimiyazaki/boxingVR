using System.Numerics;
using MiniRing.Bots;
using MiniRing.Motion;
using MiniRing.Weapons;

namespace MiniRing.Tests;

/// <summary>
/// 剣士の守り（<see cref="SwordsmanBot"/> の受け・外し）、武器を落としたら拳で戦う（<see cref="DisarmedFallback"/>）、両手の刀を片手で持つと
/// 手首のバネが 1/3（<see cref="FighterTuning.OneHandWristShare"/>）、日本刀の型（<see cref="SwordForms.Katana"/>）。
/// </summary>
[TestClass]
public sealed class SwordDefenseTests
{
    private sealed class Fixed(FighterCommand command) : IFighterControl
    {
        public int Reads { get; private set; }

        public FighterCommand Read(in FighterSense sense, float dt)
        {
            Reads++;
            return command;
        }
    }

    private static readonly MotionFrame Guard = new(new Vector3(-0.2f, -0.57f, 0.78f), new Vector3(0.03f, -0.86f, 0.5f), Vector3.Normalize(new Vector3(0f, 0.6f, 0.8f)),
        Vector3.Normalize(new Vector3(0f, -0.8f, 0.6f)), Vector3.Zero, 0f, Vector3.Zero);

    private static SwordsmanBot Bot(SwordDefense defense) =>
        new([new MotionTechnique("men", WeaponKind.Katana, SwordForms.Katana()[0], 0.45f)], Guard, seed: 1, rate: 1.77f, defense: defense);

    /// <summary>相手の剣先が、上から自分の頭へ速く振り下ろされてくる見え方（相手は 50 cm 前）。</summary>
    private static FighterSense Incoming(bool blade) =>
        new(new BodyFrame(Vector3.Zero, 0f), 0.17f, new Vector3(0f, 0.1f, 0.5f), new Vector3(0f, 0f, 0.5f), OpponentAlive: true,
            SelfHead: new Vector3(0f, 0.08f, 0f),
            OpponentBody: new BodySense(new Vector3(0.05f, 0.05f, 0.45f), new Vector3(-0.05f, 0.05f, 0.45f), new Vector3(0f, -0.15f, 0.5f)),
            OpponentBlade: blade ? new BladeSense(true, new Vector3(0f, 0.1f, 0.4f), new Vector3(0f, 0.25f, 0.05f), new Vector3(0f, -3f, -0.5f)) : default,
            SelfArmed: true);

    [TestMethod]
    public void ASwordsman_Blocks_ACutComingAtTheHead_WithTheBladeAcrossIt()
    {
        var bot = Bot(new SwordDefense(BlockShare: 1f));
        var command = bot.Read(Incoming(blade: true), 1f / 60f);
        Assert.IsTrue(bot.Blocking);
        Assert.AreEqual(1, bot.Blocks);
        // 上から来る打ちを、剣を横に寝かせて頭の上で受ける。
        var blade = Vector3.Transform(Vector3.UnitX, command.Right.Orientation!.Value);
        Assert.IsLessThan(0.5f, MathF.Abs(blade.Y), "the blade lies across the downward cut");
        Assert.IsGreaterThan(0f, command.Right.TargetLocal.Y, "and is raised to meet it");
    }

    [TestMethod]
    public void ASwordsman_StepsBack_ToEvade_WhenItDoesNotBlock()
    {
        var bot = Bot(new SwordDefense(BlockShare: 0f));
        var command = bot.Read(Incoming(blade: true), 1f / 60f);
        Assert.AreEqual(1, bot.Evasions);
        Assert.IsLessThan(-0.5f, command.MoveWorld.Z, "it steps away from the opponent");
    }

    [TestMethod]
    public void ATipThatCannotReach_IsNotDefended()
    {
        // 同じ剣先の動きでも、相手が遠く（剣が届かない）なら守らない。
        var far = Incoming(blade: true);
        far = far with { OpponentBody = new BodySense(new Vector3(0.05f, 0.05f, 1.5f), new Vector3(-0.05f, 0.05f, 1.5f), new Vector3(0f, -0.15f, 1.5f)) };
        var bot = Bot(new SwordDefense());
        _ = bot.Read(far, 1f / 60f);
        Assert.IsFalse(bot.Defending);
    }

    [TestMethod]
    public void NoBladeComing_NoDefense()
    {
        var bot = Bot(new SwordDefense());
        _ = bot.Read(Incoming(blade: false), 1f / 60f);
        Assert.IsFalse(bot.Defending);
        var off = Bot(SwordDefense.None);
        _ = off.Read(Incoming(blade: true), 1f / 60f);
        Assert.IsFalse(off.Defending);
    }

    [TestMethod]
    public void Disarmed_ItFightsWithItsFists_AndDoesNotGoBack()
    {
        var sword = new Fixed(default);
        var fists = new Fixed(default);
        var control = new DisarmedFallback(sword, fists);
        var armed = Incoming(blade: false);
        _ = control.Read(armed, 0.01f);
        _ = control.Read(armed with { SelfArmed = false }, 0.01f);
        _ = control.Read(armed, 0.01f);
        Assert.AreEqual(1, sword.Reads);
        Assert.AreEqual(2, fists.Reads);
        Assert.IsTrue(control.Disarmed);

        // 武器のことを知らない入力元（SelfArmed が null）は、武器を持っているものとして扱う。
        var unknown = new DisarmedFallback(new Fixed(default), new Fixed(default));
        _ = unknown.Read(armed with { SelfArmed = null }, 0.01f);
        Assert.IsFalse(unknown.Disarmed);
    }

    [TestMethod]
    public void TheGame_TellsTheBotWhenItHasDroppedItsSword_AndTheHandStaysBare()
    {
        FighterSense? seen = null;
        var spy = new Spy(s => seen = s);
        using var game = new BoxingGame(SwordStance.Tuning, seed: 1, corners: new BoxingCorners("stand", _ => new StandGuard(), _ => spy, PlayerWeapon: WeaponKind.Katana, PlayerTwoHanded: true));
        game.Step(default, 1f / 60f);
        Assert.IsTrue(seen!.Value.SelfArmed);
        game.Player.Unwield(Hand.Right);
        game.Step(default, 1f / 60f);
        Assert.IsFalse(seen!.Value.SelfArmed);
        Assert.IsTrue(game.Player.BareFist(Hand.Right) && game.Player.BareFist(Hand.Left), "the gloves do not come back");
    }

    private sealed class Spy(Action<FighterSense> see) : IFighterControl
    {
        public FighterCommand Read(in FighterSense sense, float dt)
        {
            see(sense);
            return new FighterCommand(Vector3.Zero, Vector3.UnitZ, default, default);
        }
    }

    [TestMethod]
    public void OneHanded_ASideKnockTurnsTheKatanaFurther_ThanTwoHanded()
    {
        // 刀を前へ水平に構えさせ、両手で 1 秒持つ（片手の方は、ここで左手を放す: 手首のバネ 1/3）。0.5 秒置いてから、刀を横から
        // 打たれたように鉛直の軸のまわりへ回す力積を与え、構えからいちばん大きくそれた角度を測る。バネが弱いほど大きくそれる。
        static float Knocked(bool letGo, float knock = 0.02f)
        {
            var hold = new MotionFrame(new Vector3(-0.1f, -0.2f, 0.75f), new Vector3(-0.1f, -0.2f, 0.29f), Vector3.UnitZ, -Vector3.UnitY, Vector3.Zero, 0f, Vector3.Zero);
            using var game = new BoxingGame(SwordStance.Tuning, seed: 1, corners: new BoxingCorners("stand", _ => new StandGuard(),
                _ => new MotionImitation(new SwordMotion("hold", 1f / 60f, [hold, hold]), loop: true), PlayerWeapon: WeaponKind.Katana, PlayerTwoHanded: true));
            for (var t = 0f; t < 1f; t += 1f / 240f)
            {
                game.Step(default, 1f / 240f);
            }

            if (letGo)
            {
                game.Player.LetGoSupport(Hand.Left);
                Assert.IsTrue(game.Player.HoldsOneHanded(Hand.Right));
            }

            for (var t = 0f; t < 0.5f; t += 1f / 240f)
            {
                game.Step(default, 1f / 240f);
            }

            var id = game.WeaponOf(game.Player)!.Body.BodyId;
            Vector3 Axis() => Vector3.Transform(Vector3.UnitX, game.World.GetState(id).Orientation);
            var before = Axis();
            game.World.ApplyAngularImpulse(id, new Vector3(0f, knock, 0f));
            var worst = 0f;
            for (var t = 0f; t < 0.4f; t += 1f / 240f)
            {
                game.Step(default, 1f / 240f);
                worst = MathF.Max(worst, MathF.Acos(Math.Clamp(Vector3.Dot(Axis(), before), -1f, 1f)));
            }

            return worst;
        }

        var two = Knocked(letGo: false);
        var one = Knocked(letGo: true);
        Assert.IsGreaterThan(two * 2f, one, $"one hand gives way more (two {two * 57.3f:0}°, one {one * 57.3f:0}°)");
    }

    [TestMethod]
    public void KatanaForms_StartAndEndInChudan_WithUnitBlades()
    {
        var forms = SwordForms.Katana();
        Assert.IsGreaterThan(5, forms.Count);
        foreach (var f in forms)
        {
            Assert.IsTrue(f.Frames.All(x => MathF.Abs(x.Blade.Length() - 1f) < 1e-3f && MathF.Abs(Vector3.Dot(x.Blade, x.Edge)) < 1e-3f), f.Name);
            Assert.AreEqual(0f, Vector3.Distance(f.Frames[0].Right, f.Frames[^1].Right), 1e-3f, f.Name);
        }

        Assert.IsTrue(forms.Any(f => f.Frames.Any(x => x.Velocity.Z < 0f)), "a hiki-waza steps back");
    }
}
