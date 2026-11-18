using System.Numerics;
using MiniRing.Bots;
using MiniRing.Motion;
using MiniRing.Weapons;

namespace MiniRing.Tests;

/// <summary>
/// 武器をグリップで持つ（2026-11-19）: 握っていない手が <see cref="HandCommand.Grip"/> を押して柄の近くにあれば、落ちた武器を拾う・自分が
/// 握っている武器にもう一方の手として添える。片手を放せば残った手が片手で持つ。Grip を出さない操作元（ボット・従来の操作）は何も変わらない。
/// </summary>
[TestClass]
[TestCategory("slow")]
public sealed class WeaponGraspTests
{
    private const WeaponKind Katana = WeaponKind.Katana;

    /// <summary>刀の構え（GuardHold と同じ）に、グリップ・放すを重ねる操作元。手の目標は、指定があればその世界の点へ。</summary>
    private sealed class Script : IFighterControl
    {
        public bool GripLeft;
        public bool GripRight;
        public bool LetGoLeft;
        public bool LetGoRight;
        public Func<Vector3>? LeftAt;
        public Func<Vector3>? RightAt;

        public FighterCommand Read(in FighterSense sense, float dt)
        {
            var tuning = FighterTuning.Default;
            var face = sense.OpponentChest - sense.Self.Origin;
            face.Y = 0f;
            face = face.LengthSquared() > 1e-6f ? Vector3.Normalize(face) : Vector3.UnitZ;
            var command = FormSwordsman.GuardCommand(Katana, sense, face, tuning.WalkSpeed, tuning.MaxTorsoTwistRadians);
            var left = command.Left with { Grip = GripLeft, LetGo = LetGoLeft };
            var right = command.Right with { Grip = GripRight, LetGo = LetGoRight };
            if (LeftAt is not null)
            {
                left = left with { TargetLocal = sense.Self.WorldToLocal(LeftAt()) };
            }

            if (RightAt is not null)
            {
                right = right with { TargetLocal = sense.Self.WorldToLocal(RightAt()) };
            }

            return command with { Left = left, Right = right };
        }
    }

    private static BoxingGame NewGame(Script script) =>
        new(seed: 1, corners: SwordTraining.Corners(Katana, 1.2f) with { Player = _ => script });

    private static void Play(BoxingGame game, float seconds)
    {
        for (var t = 0f; t < seconds; t += 1f / 60f)
        {
            game.Step(default, 1f / 60f);
        }
    }

    private static Vector3 GripWorld(BoxingGame game, Weapon weapon, Vector3 gripLocal)
    {
        var s = game.World.GetState(weapon.Body.BodyId);
        return s.Position + Vector3.Transform(gripLocal, s.Orientation);
    }

    /// <summary>刀を握ったまま始まる（両手持ち）: 右手が鍔の近く、左手が柄頭の側。</summary>
    [TestMethod]
    public void TheStart_IsAnOrdinaryTwoHandedHold_WithNoGripPressed()
    {
        var script = new Script();
        using var game = NewGame(script);
        Play(game, 1f);
        var (top, bottom) = Weapon.TwoHandGripsOf(Katana);
        Assert.AreEqual(Hand.Left, game.Player.SupportOf(Hand.Right));
        Assert.AreEqual(top.X, game.Player.GripX(Hand.Right)!.Value, 1e-5f, "the right hand holds near the tsuba");
        Assert.AreEqual(bottom.X, game.Player.GripX(Hand.Left)!.Value, 1e-5f, "the left hand holds toward the pommel");
        Assert.IsFalse(game.Player.HoldsOneHanded(Hand.Right));
    }

    /// <summary>
    /// 左手を放すと右手が片手で持つ（握りの位置は変えない）。手を離して素手の拳になった後、手を柄へ戻して Grip を押すと、
    /// 通常の両手の間隔で添えて、手首のバネも両手の割合へ戻る。
    /// </summary>
    [TestMethod]
    public void ReleasingTheSecondHand_LeavesAOneHandedHold_AndGrippingNearTheHandleJoinsAgain()
    {
        var script = new Script();
        using var game = NewGame(script);
        Play(game, 1f);
        var sword = game.WeaponOf(game.Player)!;
        var (top, _) = Weapon.TwoHandGripsOf(Katana);

        script.LetGoLeft = true;
        Play(game, 1f / 30f);
        script.LetGoLeft = false;
        Assert.IsFalse(game.Player.Wields(Hand.Left), "the left hand let go");
        Assert.IsTrue(game.Player.Wields(Hand.Right), "the right hand still holds the sword");
        Assert.IsTrue(game.Player.HoldsOneHanded(Hand.Right), "one hand carries a two-handed sword at a third of the wrist spring");
        Assert.AreEqual(top.X, game.Player.GripX(Hand.Right)!.Value, 1e-5f, "the remaining hand keeps its grip");

        // 左手が柄から離れて素手の拳になる（左手は構えの位置のまま、右手は剣を持っている）。
        Play(game, 1f);
        Assert.IsTrue(game.Player.WearsGlove(Hand.Left), "the empty left hand is a bare fist once it has moved off the handle");

        // 左手を柄へ持っていって Grip（まだ押さない間は添えない）。
        var spacing = FormSwordsman.Spacing(Katana) * game.Player.ArmReach;
        script.LeftAt = () => GripWorld(game, sword, top - new Vector3(spacing, 0f, 0f));
        Play(game, 1f);
        Assert.IsFalse(game.Player.Wields(Hand.Left), "without Grip the hand just rests by the handle");

        script.GripLeft = true;
        Play(game, 1f);
        Assert.AreEqual(Hand.Left, game.Player.SupportOf(Hand.Right), "the left hand grips again");
        Assert.IsFalse(game.Player.WearsGlove(Hand.Left), "the bare fist was taken off to hold the handle");
        Assert.IsFalse(game.Player.HoldsOneHanded(Hand.Right), "two hands again");
        var (_, bottom) = Weapon.TwoHandGripsOf(Katana);
        var expected = MathF.Max(top.X - spacing, bottom.X);
        Assert.AreEqual(expected, game.Player.GripX(Hand.Left)!.Value, 1e-4f, "the second hand is a normal two-handed spacing from the first, toward the pommel");
        Assert.AreEqual(top.X, game.Player.GripX(Hand.Right)!.Value, 1e-5f, "the first hand did not move");
    }

    /// <summary>右手を放しても、添えていた左手が刀をそのまま片手で持つ。</summary>
    [TestMethod]
    public void ReleasingTheFirstHand_LeavesTheOtherHandHoldingTheSwordAsItWas()
    {
        var script = new Script();
        using var game = NewGame(script);
        Play(game, 1f);
        var sword = game.WeaponOf(game.Player)!;
        var (_, bottom) = Weapon.TwoHandGripsOf(Katana);
        var height = game.World.GetState(sword.Body.BodyId).Position.Y;

        script.LetGoRight = true;
        Play(game, 1f / 30f);
        script.LetGoRight = false;
        Play(game, 1f);
        Assert.IsFalse(game.Player.Wields(Hand.Right));
        Assert.IsTrue(game.Player.Wields(Hand.Left), "the left hand holds on");
        Assert.AreEqual(sword.Body.BodyId, game.Player.WieldedBody(Hand.Left), "as the hand that holds the sword now");
        Assert.AreEqual(bottom.X, game.Player.GripX(Hand.Left)!.Value, 1e-5f, "its grip did not move");
        Assert.IsTrue(game.Player.HoldsOneHanded(Hand.Left));
        Assert.IsGreaterThan(height - 0.1f, game.World.GetState(sword.Body.BodyId).Position.Y, "the sword did not fall");
    }

    /// <summary>落とした刀は床へ落ち、空いた左手だけで（鍔に近い握りで）拾え、右手が添えれば両手の間隔になる。</summary>
    [TestMethod]
    public void ADroppedSword_IsPickedUpByTheLeftHandAlone_AndTheRightHandJoins()
    {
        var script = new Script();
        using var game = NewGame(script);
        Play(game, 1f);
        var sword = game.WeaponOf(game.Player)!;
        var (top, bottom) = Weapon.TwoHandGripsOf(Katana);

        script.LetGoLeft = true;
        script.LetGoRight = true;
        Play(game, 1f / 30f);
        script.LetGoLeft = false;
        script.LetGoRight = false;
        Play(game, 1.5f);
        Assert.IsFalse(game.Player.Wields(Hand.Left) || game.Player.Wields(Hand.Right), "both hands dropped it");
        Assert.AreNotEqual(sword.Body.BodyId, game.Player.WieldedBody(Hand.Right), "what the right hand carries now is only its bare fist");
        Assert.IsTrue(game.Player.WearsGlove(Hand.Left) && game.Player.WearsGlove(Hand.Right), "both empty hands are bare fists");
        var fallen = game.World.GetState(sword.Body.BodyId).Position.Y;
        Assert.IsLessThan(0.1f, fallen, "the sword lies on the floor");

        // 刀を左手の所へ置く（この小さい体の腕は床まで届かないので、握れる高さへ持ち上げた物として。置いた直後の歩の前に Grip を押す）。
        script.GripLeft = true;
        var fist = game.Player.GloveCenter(Hand.Left);
        var s0 = game.World.GetState(sword.Body.BodyId);
        game.World.SetDynamicPose(sword.Body.BodyId, fist + new Vector3(0f, 0.01f, 0f) - Vector3.Transform(top, s0.Orientation), s0.Orientation);
        Play(game, 1f / 60f);
        Assert.IsTrue(game.Player.Wields(Hand.Left), "the left hand picked it up");
        Assert.AreEqual(sword.Body.BodyId, game.Player.WieldedBody(Hand.Left));
        Assert.IsFalse(game.Player.WearsGlove(Hand.Left), "the bare fist is off");
        Assert.AreEqual(top.X, game.Player.GripX(Hand.Left)!.Value, 1e-5f, "held near the tsuba");
        Assert.IsTrue(game.Player.HoldsOneHanded(Hand.Left));
        Assert.AreSame(sword, game.WeaponOf(game.Player), "the sword is the player's again");
        Play(game, 1f);
        Assert.IsLessThan(0.1f, Vector3.Distance(game.Player.GloveCenter(Hand.Left), GripWorld(game, sword, top)), "the grip follows the hand");

        // 右手を柄へ持っていって Grip で添える: 左手の握り（鍔の近く）から柄頭の側へ通常の間隔。
        var spacing = FormSwordsman.Spacing(Katana) * game.Player.ArmReach;
        script.RightAt = () => GripWorld(game, sword, top);
        script.GripRight = true;
        Play(game, 1.5f);
        Assert.AreEqual(Hand.Right, game.Player.SupportOf(Hand.Left), "the right hand joined as the second hand");
        Assert.IsFalse(game.Player.WearsGlove(Hand.Right));
        Assert.IsFalse(game.Player.HoldsOneHanded(Hand.Left));
        var expected = MathF.Max(top.X - spacing, bottom.X);
        Assert.AreEqual(expected, game.Player.GripX(Hand.Right)!.Value, 1e-4f);
        Assert.AreEqual(top.X, game.Player.GripX(Hand.Left)!.Value, 1e-5f);
    }

    /// <summary>手が柄から遠ければ、Grip を押しても拾えない。</summary>
    [TestMethod]
    public void AHandFarFromTheHandle_DoesNotPickUp()
    {
        var script = new Script();
        using var game = NewGame(script);
        Play(game, 1f);
        var sword = game.WeaponOf(game.Player)!;
        var (top, _) = Weapon.TwoHandGripsOf(Katana);
        script.LetGoLeft = true;
        script.LetGoRight = true;
        Play(game, 1f / 30f);
        script.LetGoLeft = false;
        script.LetGoRight = false;
        Play(game, 1.5f);
        script.GripLeft = true;
        script.GripRight = true;
        var fist = game.Player.GloveCenter(Hand.Left);
        var s0 = game.World.GetState(sword.Body.BodyId);
        game.World.SetDynamicPose(sword.Body.BodyId, fist + new Vector3(0f, 0.01f, 0.3f) - Vector3.Transform(top, s0.Orientation), s0.Orientation);
        Play(game, 1f / 60f);
        Assert.IsFalse(game.Player.Wields(Hand.Left) || game.Player.Wields(Hand.Right));
    }

    /// <summary>Grip を出さない操作元（ボット・従来の操作）は、握ったまま始まる武器の持ち方が今までと同じ。</summary>
    [TestMethod]
    public void WithoutGrip_NothingChanges_TheBotKeepsItsTwoHandedHold()
    {
        using var game = new BoxingGame(seed: 1, corners: SwordTraining.Corners(Katana, 1.2f));
        Play(game, 2f);
        var (top, bottom) = Weapon.TwoHandGripsOf(Katana);
        Assert.AreEqual(Hand.Left, game.Cpu.SupportOf(Hand.Right));
        Assert.AreEqual(top.X, game.Cpu.GripX(Hand.Right)!.Value, 1e-5f);
        Assert.AreEqual(bottom.X, game.Cpu.GripX(Hand.Left)!.Value, 1e-5f);
        Assert.IsFalse(game.Cpu.HoldsOneHanded(Hand.Right));
    }
}
