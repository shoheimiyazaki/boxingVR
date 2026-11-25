using System.Numerics;
using MiniRing.Motion;
using MiniRing.Weapons;

namespace MiniRing.Tests;

/// <summary>
/// VR で武器を扱う（2026-11-25、ユーザーの要望）: 片手で持った刀が狙った向きに止まり震えない、左手で持った物を放せる、
/// 目の前に浮いた武器を取って始め、放して止まるとまた浮く、稽古の相手を選べる。
/// </summary>
[TestClass]
[TestCategory("slow")]
public sealed class VrWeaponHandlingTests
{
    private const float Dt = 1f / 240f;
    private static readonly Vector3 Head = new(0f, 1.62f, 0f);

    private static FighterTuning Tuning => FighterTuning.Default with { PlayerFatigue = false };

    [TestMethod]
    public void AOneHandedKatanaHoldsTheAimAndSettles_MuchBetterThanTheOldWrist()
    {
        // 右手だけで刀を持ち、20° 上げて前へ向けたコントローラを止める（VrOneHandSwordProbe.Measure）。2026-11-25 までの手首（8.94 Hz・10 N·m・臨界減衰）と比べる。
        var now = VrOneHandSwordProbe.Measure(Tuning);
        var old = VrOneHandSwordProbe.Measure(Tuning with { GripWristServoHz = 8.94f, GripWristMaxTorque = 10f, GripWristDampingRatio = 1f, BladeTipHz = 0f }); // 前の体: 切っ先の力も無い
        Console.WriteLine($"now: hold {now.HoldError:0.0} deg, tip jitter {now.TipJitter * 1000f:0.0} mm, settle {now.Settle:0.00} s; old: hold {old.HoldError:0.0} deg, jitter {old.TipJitter * 1000f:0.0} mm");
        Assert.IsTrue(now.HoldError < 6f, $"片手の刀が狙いから {now.HoldError:0.0}° 垂れる");
        Assert.IsTrue(now.HoldError < old.HoldError * 0.5f, "前の手首より狙いに近い");
        Assert.IsTrue(now.Settle < 0.6f, $"向きを 40° 変えて 5° 以内に入るまで {now.Settle:0.00} s");
        Assert.IsTrue(float.IsNaN(old.Settle) || old.Settle > now.Settle, "前の手首より早く狙いに入る（前は 2 秒たっても 5° 以内に入らなかった）");
        // 2026-11-25（PhysicsVersion 100）: 握る拳（素手と同じ手の組織、0.36 kg）が刀に乗り、手首が回す慣性が増えて振れ戻りが 8.5° → 10.8° になった
        //（拳の質量を 0 にすると 8.5°）。手首の筋肉は前の刀だけの重さで決めた値のまま。上限は 12°（狙いの保ち・落ち着くまでの時間は前と同じ）。
        // 2026-11-25（PhysicsVersion 105）: 刀を実物の重さ（0.61 → 1.08 kg、握りのまわりの慣性 0.0608 → 0.0910 kg·m²）にすると、手首のサーボだけでは振れ戻りが
        // 慣性に比例して 16.7° に増えた。切っ先を目標へ合わせる手首の力（Fighter.DriveBladeTip: 切っ先のワールドの速さで減衰）を入れて 4.8°・垂れ 1.2° になった。上限は前のまま。
        Assert.IsTrue(now.Overshoot < 12f, $"狙いに入った後に {now.Overshoot:0.0}° まで振れ戻る（ビヨンビヨン）");
    }

    [TestMethod]
    public void TheLeftHandLetsGoOfWhatItHoldsAfterTheRightLetGo()
    {
        // 両手で持った刀の右のグリップだけを離すと左手が片手で持つ。その左のグリップを離せば放す（2026-11-25 まで放せなかった）。
        using var game = new BoxingGame(Tuning, corners: SwordTraining.Corners(WeaponKind.Katana, 2.535f)) { HidePlayerHead = true };
        var mapper = new VrPlayerMapper(weaponMode: true) { GripHolds = true, FirstPerson = true, HeldWeapon = WeaponKind.Katana };
        void Run(float left, float right, float seconds)
        {
            var frame = new VrRawFrame(Head, Quaternion.Identity, new Vector3(-0.1f, 1.3f, -0.3f), new Vector3(0.1f, 1.3f, -0.35f), true, true,
                Vector2.Zero, Vector2.Zero, left, right, false, true, Quaternion.Identity, Quaternion.Identity);
            for (var i = 0; i < (int)(seconds * 240f); i++)
            {
                game.Step(mapper.Read(frame, game.PlayerSense, Dt), Dt);
            }
        }

        Run(1f, 1f, 1.7f);
        Assert.IsTrue(game.Player.Wields(Hand.Left) && game.Player.Wields(Hand.Right), "両手で持つ");
        Run(1f, 0f, 0.5f);
        Assert.IsFalse(game.Player.Wields(Hand.Right), "右を離せば右は放す");
        Assert.IsTrue(game.Player.Wields(Hand.Left), "左が片手で持つ");
        Assert.IsTrue(game.PlayerSense.SelfWeapon.Held, "左で持った刀も自分の武器として見える");
        Run(0f, 0f, 0.5f);
        Assert.IsFalse(game.Player.Wields(Hand.Left), "左のグリップを離せば左も放す");
    }

    [TestMethod]
    public void TheVrPlayerStartsWithTheWeaponFloatingAhead_TakesIt_AndItFloatsBackAfterADrop()
    {
        using var game = new BoxingGame(Tuning, corners: SwordTraining.Corners(WeaponKind.Katana, 2.535f, floats: true));
        var floating = game.Weapons.Single(w => game.IsFloating(w));
        Assert.IsFalse(game.Player.Wields(Hand.Right) || game.Player.Wields(Hand.Left), "手には持たずに始まる");
        Assert.IsTrue(game.Cpu.Wields(Hand.Right), "相手は今まで通り持って始まる");

        Vector3 Grip()
        {
            var s = game.World.GetState(floating.Body.BodyId);
            return s.Position + Vector3.Transform(Weapon.GripOf(floating.Kind), s.Orientation);
        }

        FighterCommand Reach(bool grip)
        {
            var sense = game.PlayerSense;
            var face = sense.OpponentChest - sense.Self.Origin;
            face.Y = 0f;
            var target = grip ? sense.Self.WorldToLocal(Grip()) : AiControl.Guard(Hand.Right, sense.ArmReach);
            return new FighterCommand(Vector3.Zero, Vector3.Normalize(face), new HandCommand(AiControl.Guard(Hand.Left, sense.ArmReach), false),
                new HandCommand(target, grip, LetGo: false));
        }

        void Run(float seconds, Func<FighterCommand> command)
        {
            for (var i = 0; i < (int)(seconds * 240f); i++)
            {
                game.Step(new BoxingInput(command()), Dt);
            }
        }

        // 1 秒立っているだけ: 武器は落ちずに目の前に浮いたまま、体に付いてくる。
        Run(1f, () => Reach(grip: false));
        Assert.IsTrue(game.IsFloating(floating), "浮いたまま");
        var ahead = game.PlayerSense.Self.WorldToLocal(Grip());
        Console.WriteLine($"floating grip in the body frame: {ahead} (arm reach {game.Player.ArmReach:0.00} m)");
        Assert.IsTrue(ahead.Z > 0.3f * game.Player.ArmReach && ahead.Y < 0.1f * game.Player.ArmReach, "握りは胸の前・少し下");

        // 手を握りへ伸ばしてグリップ: 取れて、ふつうの物体に戻る。
        Run(1f, () => Reach(grip: true));
        Assert.IsTrue(game.Player.Wields(Hand.Right), "浮いた武器を取れる");
        Assert.IsFalse(game.IsFloating(floating), "取ったら浮かない");

        // 放す: 落ちて床で止まり、しばらくすると目の前にまた浮く。
        game.Step(new BoxingInput(Reach(grip: false) with { Right = Reach(grip: false).Right with { LetGo = true } }), Dt);
        Assert.IsFalse(game.Player.Wields(Hand.Right), "放した");
        Run(0.5f, () => Reach(grip: false));
        Assert.IsFalse(game.IsFloating(floating), "落ちている間は浮かない");
        Run(BoxingGame.FloatReturnSeconds + 3f, () => Reach(grip: false));
        Assert.IsTrue(game.IsFloating(floating), "床で止まった武器が目の前に戻る");
    }

    [TestMethod]
    public void GraspingTheHiltAtThePommelEndHoldsItThere_WithoutYankingTheGuardToTheHand()
    {
        // 柄の端（柄頭の側の握りの点）へ手を伸ばして掴む: その所を握り、刀は鍔元まで手へ引き寄せられて跳ねない（レビューの指摘、2026-11-25）。
        using var game = new BoxingGame(Tuning, corners: SwordTraining.Corners(WeaponKind.Katana, 2.535f, opponent: SwordTraining.Defenseless, floats: true));
        var katana = game.Weapons.Single(game.IsFloating);
        var (top, bottom) = Weapon.TwoHandGripsOf(WeaponKind.Katana);
        Vector3 World(Vector3 local)
        {
            var s = game.World.GetState(katana.Body.BodyId);
            return s.Position + Vector3.Transform(local, s.Orientation);
        }

        FighterCommand Reach()
        {
            var sense = game.PlayerSense;
            var face = sense.OpponentChest - sense.Self.Origin;
            face.Y = 0f;
            return new FighterCommand(Vector3.Zero, Vector3.Normalize(face), new HandCommand(AiControl.Guard(Hand.Left, sense.ArmReach), false),
                new HandCommand(sense.Self.WorldToLocal(World(bottom)), true));
        }

        var guardBefore = Vector3.Zero;
        for (var i = 0; i < 480 && !game.Player.Wields(Hand.Right); i++)
        {
            guardBefore = World(top);
            game.Step(new BoxingInput(Reach()), Dt);
        }

        Assert.IsTrue(game.Player.Wields(Hand.Right), "柄の端でも拾える");
        var jumps = new List<float>();
        for (var i = 0; i < 24; i++)
        {
            var before = World(top);
            game.Step(new BoxingInput(Reach()), Dt);
            jumps.Add(Vector3.Distance(World(top), before));
        }

        var fistToPommelGrip = Vector3.Distance(game.Player.GloveCenter(Hand.Right), World(bottom));
        Console.WriteLine($"after the grasp: max guard step {jumps.Max() * 1000f:0.0} mm per 1/240 s, fist to the pommel-end grip {fistToPommelGrip * 100f:0.0} cm (grips {Vector3.Distance(top, bottom) * 100f:0.0} cm apart)");
        Assert.IsTrue(fistToPommelGrip < 0.05f, "掴んだ所（柄の端）を握っている");
        Assert.IsTrue(jumps.Max() < 0.02f, $"掴んだ直後に鍔元が 1 歩で {jumps.Max() * 1000f:0.0} mm 跳ぶ");
    }

    [TestMethod]
    public void EveryTrainingOpponentBuildsAndFights()
    {
        var opponents = SwordTraining.Opponents(WeaponKind.Katana);
        CollectionAssert.Contains(opponents.ToList(), SwordTraining.Defenseless);
        CollectionAssert.Contains(opponents.ToList(), SwordTraining.WeaponFighter);
        Assert.IsTrue(opponents.Count >= 10, "素手の各スタイルも並ぶ");
        foreach (var opponent in opponents)
        {
            using var game = new BoxingGame(Tuning, corners: SwordTraining.Corners(WeaponKind.Katana, 2.535f, opponent: opponent, floats: true));
            for (var i = 0; i < 120; i++)
            {
                game.Step(default, Dt);
            }

            var cpuArmed = game.Cpu.Wields(Hand.Right);
            Console.WriteLine($"{opponent}: cpu armed {cpuArmed}, player floating weapon {game.Weapons.Any(game.IsFloating)}");
            Assert.IsTrue(game.Weapons.Any(game.IsFloating), $"{opponent}: プレイヤーの武器は浮いている");
            Assert.AreEqual(opponent is SwordTraining.Seigan or SwordTraining.WeaponFighter, cpuArmed, $"{opponent}: 刀を持つのは構える相手と同じ武器の相手だけ");
        }
    }
}
