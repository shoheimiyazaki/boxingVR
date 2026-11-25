using System.Numerics;
using MiniRing.Bots;
using MiniRing.Motion;
using MiniRing.Vr;
using MiniRing.Weapons;

namespace MiniRing.Tests;

/// <summary>
/// VR の刀の操作（docs/VR_MOTION.md §6、2026-11-19）: グリップで持つ（離した瞬間だけ放す）・① 右のコントローラが両手を動かす・② 一人称
/// （VR の空間 → ワールドの変換、頭の目標へ歩く指令、自分の頭を描かない）。実機なし。
/// </summary>
[TestClass]
[TestCategory("slow")]
public sealed class VrKatanaControlTests
{
    private const float Reach = 0.5296f;

    private static readonly Vector3 Head = new(0f, 1.62f, 0f);

    private static FighterSense Sense(float yaw = 0f, Vector3? selfHead = null, bool held = true) => new FighterSense(
        new BodyFrame(Vector3.Zero, yaw), Reach, default, new Vector3(0f, 2.817f, 2.817f), true)
    {
        SelfHead = selfHead ?? new Vector3(0f, 0.8451f, 0f),
        SelfWeapon = held ? new WeaponSense(true, 0.5f, 0.5f) : default,
    };

    // 頭・手の向きは単位回転 = 前が −Z（VR の空間）。左手は体の横、右手は胸の前。
    private static VrRawFrame Raw(float leftGrip, float rightGrip, Vector3? head = null, Vector2 stick = default, bool focused = true,
        Quaternion? rightHand = null, Vector3? leftPosition = null) => new(
        head ?? Head, Quaternion.Identity,
        leftPosition ?? new Vector3(-0.35f, 1.0f, -0.1f), new Vector3(0.15f, 1.25f, -0.4f), true, true,
        stick, Vector2.Zero, leftGrip, rightGrip, false, focused, Quaternion.Identity, rightHand ?? Quaternion.Identity);

    private static VrPlayerMapper Mapper(VrTwoHand twoHand = VrTwoHand.Right, bool firstPerson = false) => new(weaponMode: true)
    {
        GripHolds = true,
        TwoHand = twoHand,
        FirstPerson = firstPerson,
        HeldWeapon = WeaponKind.Katana,
    };

    private const float Dt = 1f / 240f;

    [TestMethod]
    public void GripPressesGripAndNeverSlides()
    {
        var mapper = Mapper();
        var pressed = mapper.Read(Raw(1f, 0f), Sense(), Dt).Player;
        Assert.IsTrue(pressed.Left.Grip);
        Assert.IsFalse(pressed.Right.Grip);
        Assert.IsFalse(pressed.Left.Slide || pressed.Right.Slide, "トリガーは使わない: Slide は常に false");
        Assert.IsNotNull(pressed.Right.Orientation, "武器の向きの目標は出し続ける");
    }

    [TestMethod]
    public void LetGoIsSentOnlyOnTheStepTheGripIsReleased()
    {
        var mapper = Mapper(VrTwoHand.Both);
        var sense = Sense();
        // 始めに握っていて、グリップを一度も押していない人は、何歩経っても放さない。
        for (var i = 0; i < 100; i++)
        {
            var idle = mapper.Read(Raw(0f, 0f), sense, Dt).Player;
            Assert.IsFalse(idle.Left.LetGo || idle.Right.LetGo);
        }

        for (var i = 0; i < 50; i++)
        {
            Assert.IsFalse(mapper.Read(Raw(1f, 1f), sense, Dt).Player.Left.LetGo, "握っている間は放さない");
        }

        var released = mapper.Read(Raw(0f, 1f), sense, Dt).Player;
        Assert.IsTrue(released.Left.LetGo, "離した瞬間に 1 歩だけ放す");
        Assert.IsFalse(released.Right.LetGo, "握ったままの手は放さない");
        Assert.IsFalse(mapper.Read(Raw(0f, 1f), sense, Dt).Player.Left.LetGo, "離しっぱなしでは毎歩は出さない");
    }

    [TestMethod]
    public void NothingIsLetGoWhenNoWeaponIsHeldOrTheFocusIsLost()
    {
        var mapper = Mapper(VrTwoHand.Both);
        mapper.Read(Raw(1f, 1f), Sense(), Dt);
        Assert.IsFalse(mapper.Read(Raw(0f, 1f), Sense(held: false), Dt).Player.Left.LetGo, "握っていない手の離しは何も起こさない");
        mapper.Read(Raw(1f, 1f), Sense(), Dt);
        var lost = mapper.Read(Raw(1f, 1f, focused: false), Sense(), Dt).Player;
        Assert.IsFalse(lost.Left.Grip, "フォーカスを失えば離す");
        Assert.IsFalse(lost.Left.LetGo || lost.Right.LetGo, "フォーカスを失っただけで武器を落とさない");
    }

    [TestMethod]
    public void RightControllerMovesBothHandsWhileTheLeftGripIsHeld()
    {
        var mapper = Mapper();
        var sense = Sense();
        var command = mapper.Read(Raw(1f, 1f), sense, Dt).Player;
        var axis = Vector3.Transform(Vector3.UnitX, command.Right.Orientation!.Value);
        var expected = command.Right.TargetLocal - (axis * (FormSwordsman.Spacing(WeaponKind.Katana) * Reach));
        Assert.AreEqual(0f, (command.Left.TargetLocal - expected).Length(), 1e-5f, "左手 = 右手から剣の軸に沿って柄頭の側へ、両手の間隔");
        Assert.AreEqual(FormSwordsman.Spacing(WeaponKind.Katana) * Reach, (command.Right.TargetLocal - command.Left.TargetLocal).Length(), 1e-5f);
        Assert.IsTrue(Vector3.Dot(command.Right.TargetLocal - command.Left.TargetLocal, axis) > 0f, "左手は柄頭の側（右手より先端から遠い）");
        Assert.AreEqual(command.Right.Orientation, command.Left.Orientation, "左手の武器の向きも右と同じ");

        // 左のコントローラをどこへ動かしても左手の目標は変わらない。
        var moved = mapper.Read(Raw(1f, 1f, leftPosition: new Vector3(-0.1f, 1.4f, -0.6f)), sense, Dt).Player;
        Assert.AreEqual(0f, (moved.Left.TargetLocal - command.Left.TargetLocal).Length(), 1e-5f);
    }

    [TestMethod]
    public void ReleasingTheLeftGripSendsTheLeftHandBackToTheLeftController()
    {
        var both = Mapper(VrTwoHand.Both).Read(Raw(1f, 1f), Sense(), Dt).Player;
        var mapper = Mapper();
        mapper.Read(Raw(1f, 1f), Sense(), Dt);
        var released = mapper.Read(Raw(0f, 1f), Sense(), Dt).Player;
        Assert.AreEqual(0f, (released.Left.TargetLocal - both.Left.TargetLocal).Length(), 1e-5f, "片手持ち: 左は左のコントローラ");
        Assert.AreEqual(both.Right.TargetLocal, released.Right.TargetLocal);
        // 右手だけのグリップ（左を押していない）でも左は左のコントローラ。
        var rightOnly = Mapper().Read(Raw(0f, 1f), Sense(), Dt).Player;
        Assert.AreEqual(0f, (rightOnly.Left.TargetLocal - both.Left.TargetLocal).Length(), 1e-5f);
        // 武器が無い（落とした）ときも追わない。
        var dropped = Mapper().Read(Raw(1f, 1f), Sense(held: false), Dt).Player;
        Assert.AreEqual(0f, (dropped.Left.TargetLocal - both.Left.TargetLocal).Length(), 1e-5f);
    }

    [TestMethod]
    public void BothModeKeepsEachControllerOnItsOwnHand()
    {
        var command = Mapper(VrTwoHand.Both).Read(Raw(1f, 1f), Sense(), Dt).Player;
        var follow = Mapper().Read(Raw(1f, 1f), Sense(), Dt).Player;
        Assert.AreEqual(command.Right.TargetLocal, follow.Right.TargetLocal);
        Assert.IsTrue((command.Left.TargetLocal - follow.Left.TargetLocal).Length() > 0.05634f, "both では左手は左のコントローラ（右手の柄の上の点ではない）");
    }

    [TestMethod]
    public void BareHandedMappingIsUnchanged()
    {
        var mapper = new VrPlayerMapper(weaponMode: false);
        var command = mapper.Read(Raw(1f, 0f), Sense(), Dt).Player;
        Assert.IsTrue(command.Left.Grip);
        Assert.IsNull(command.Left.Orientation);
        Assert.IsFalse(command.Left.LetGo);
        Assert.IsNull(mapper.Placement, "一人称でなければ変換は作らない");
    }

    [TestMethod]
    public void WalkTowardIsProportionalBeyondTheDeadZoneAndSaturatesAtOne()
    {
        Assert.AreEqual(Vector3.Zero, VrPlayerMapper.WalkToward(new Vector3(0.1f, 0f, 0f) * Reach, Reach), "デッドゾーンの中は歩かない");
        var dead = VrPlayerMapper.WalkDeadZone;
        var half = VrPlayerMapper.WalkToward(new Vector3(0f, 0f, (dead + (VrPlayerMapper.WalkFullRange / 2f)) * Reach), Reach);
        Assert.AreEqual(0.5f, half.Length(), 1e-4f);
        Assert.AreEqual(1f, half.Z / half.Length(), 1e-4f, "ずれの向きへ歩く");
        var far = VrPlayerMapper.WalkToward(new Vector3(5f, 0f, 5f), Reach);
        Assert.AreEqual(1f, far.Length(), 1e-4f, "歩く速さで頭打ち");
    }

    [TestMethod]
    public void FirstPersonPlacementPutsTheHmdOnThePuppetHeadAndFacesTheSameWay()
    {
        var mapper = Mapper(firstPerson: true);
        var puppetHead = new Vector3(1.127f, 0.8732f, -0.5634f);
        var yaw = 0.7f;
        var sense = Sense(yaw, puppetHead);
        mapper.Read(Raw(0f, 0f), sense, Dt);
        var placement = mapper.Placement ?? throw new AssertFailedException("身長の合わせで変換ができる");
        Assert.AreEqual(0f, (placement.Point(Head) - puppetHead).Length(), 1e-5f, "HMD が人形の頭に重なる");
        Assert.AreEqual(PuppetMapping.Scale(Reach, mapper.PlayerHeight), placement.Scale, 1e-6f, "縮尺 = 人形の腕の長さ ÷ 人の腕の長さ");
        var forward = Vector3.Transform(-Vector3.UnitZ, placement.Rotation(Quaternion.Identity));
        Assert.AreEqual(MathF.Sin(yaw), forward.X, 1e-4f, "HMD の前が人形の向きになる");
        Assert.AreEqual(MathF.Cos(yaw), forward.Z, 1e-4f);
        // 1 m 横へ歩いた HMD は、縮尺した距離だけ離れた点になる。
        Assert.AreEqual(placement.Scale, (placement.Point(Head + Vector3.UnitX) - puppetHead).Length(), 1e-5f);
    }

    [TestMethod]
    public void FirstPersonPlacementIsFixedWhenThePuppetIsPushedAway()
    {
        var mapper = Mapper(firstPerson: true);
        mapper.Read(Raw(0f, 0f), Sense(0f, new Vector3(0f, 0.8451f, 0f)), Dt);
        var before = mapper.Placement!.Value;
        mapper.Read(Raw(0f, 0f), Sense(0.4f, new Vector3(1.408f, 0.8451f, 1.408f)), Dt);
        Assert.AreEqual(before, mapper.Placement!.Value, "人形が押されてもカメラの変換は付いていかない");
        mapper.ResetPlacement();
        mapper.Read(Raw(0f, 0f), Sense(0.4f, new Vector3(1.408f, 0.8451f, 1.408f)), Dt);
        Assert.AreEqual(0f, (mapper.Placement!.Value.Point(Head) - new Vector3(1.408f, 0.8451f, 1.408f)).Length(), 1e-5f, "作り直したワールドでは合わせ直す");
    }

    [TestMethod]
    public void FirstPersonWalkCommandIsProportionalToTheHeadOffset()
    {
        var mapper = Mapper(firstPerson: true);
        var sense = Sense();
        mapper.Read(Raw(0f, 0f), sense, Dt);
        var still = mapper.Read(Raw(0f, 0f), sense, Dt).Player;
        Assert.AreEqual(Vector3.Zero, still.MoveWorld, "HMD が止まっていれば歩かない");

        var scale = mapper.Placement!.Value.Scale;
        foreach (var human in new[] { 0.3f, 0.45f })
        {
            var command = mapper.Read(Raw(0f, 0f, head: Head + new Vector3(0f, 0f, -human)), sense, Dt).Player; // VR で前（−Z）へ。人形の前は +Z
            var offset = human * scale;
            var expected = Math.Clamp((offset - (VrPlayerMapper.WalkDeadZone * Reach)) / (VrPlayerMapper.WalkFullRange * Reach), 0f, 1f);
            Assert.AreEqual(expected, command.MoveWorld.Length(), 1e-3f, $"{human} m 前へ");
            Assert.IsTrue(command.MoveWorld.Z > 0f && MathF.Abs(command.MoveWorld.X) < 1e-4f, "人形の前へ歩く");
        }

        var far = mapper.Read(Raw(0f, 0f, head: Head + new Vector3(2f, 0f, 0f)), sense, Dt).Player;
        Assert.AreEqual(1f, far.MoveWorld.Length(), 1e-4f, "遠くへ行っても歩く速さまで");
        Assert.IsTrue(MathF.Sqrt((far.HeadOffset.X * far.HeadOffset.X) + (far.HeadOffset.Z * far.HeadOffset.Z)) <= VrPlayerMapper.LeanLimit * Reach + 1e-5f, "傾けるのは上限まで");
    }

    [TestMethod]
    public void FirstPersonStickMovesTheOriginAndStopsWhenTheBodyCannotFollow()
    {
        var mapper = Mapper(firstPerson: true);
        var sense = Sense(); // 人形は止まったまま（押さえられている）
        mapper.Read(Raw(0f, 0f), sense, Dt);
        var start = mapper.Placement!.Value.WorldAnchor;
        mapper.Read(Raw(0f, 0f, stick: new Vector2(0f, 1f)), sense, 0.1f);
        var moved = mapper.Placement!.Value.WorldAnchor - start;
        Assert.AreEqual(mapper.WalkSpeed * VrPlayerMapper.StickWalkRatio * 0.1f, moved.Length(), 1e-4f);
        Assert.IsTrue(moved.Z > 0f && MathF.Abs(moved.X) < 1e-4f, "視線の前（= 人形の前）へ");

        for (var i = 0; i < 400; i++)
        {
            mapper.Read(Raw(0f, 0f, stick: new Vector2(0f, 1f)), sense, 0.05f);
        }

        var lead = mapper.Placement!.Value.WorldAnchor - sense.SelfHead;
        Assert.IsTrue(lead.Length() <= (VrPlayerMapper.MaxLead * Reach) + (mapper.WalkSpeed * 0.05f), $"体が追えなければ基準点も離れすぎない: {lead.Length():0.000} m");
    }

    [TestMethod]
    public void EyeViewIsTheHmdEyeMovedByThePlacement()
    {
        var placement = VrWorldPlacement.Align(Head, MathF.PI, new Vector3(0.4f, 0.31f, -0.2f), 0.7f, 0.3f);
        var eye = Head + new Vector3(0.03f, 0f, 0f); // 右目（目の間隔の半分）
        Assert.IsTrue(Matrix4x4.Invert(Matrix4x4.CreateTranslation(eye), out var vrView));
        var view = placement.View(vrView);
        Assert.IsTrue(Matrix4x4.Invert(view, out var eyeToWorld));
        Assert.AreEqual(0f, (eyeToWorld.Translation - placement.Point(eye)).Length(), 1e-5f, "目の位置は変換した点");
        // 左右の目の間隔は縮尺どおり縮む。
        var left = Head + new Vector3(-0.03f, 0f, 0f);
        Assert.AreEqual(0.06f * 0.3f, (placement.Point(eye) - placement.Point(left)).Length(), 1e-5f);
        // 前（−Z）は人形の向きを向く。
        var forward = Vector3.TransformNormal(-Vector3.UnitZ, eyeToWorld);
        Assert.AreEqual(MathF.Sin(0.7f), forward.X, 1e-4f);
        Assert.AreEqual(MathF.Cos(0.7f), forward.Z, 1e-4f);
    }

    [TestMethod]
    public void HidingThePlayerHeadDropsTheHeadAndItsFaceLayerAndKeepsThePhysics()
    {
        using var game = new BoxingGame(seed: 3);
        var all = game.BuildVoxelVolumes().Count;
        var checksum = game.StateChecksum();
        game.HidePlayerHead = true;
        var hidden = game.BuildVoxelVolumes().Count;
        // 頭は 2 つの格子（14 mm の頭蓋・脳・首と、7 mm の顔の層。どちらも Kind は Head）になった。カメラは頭の中にあるので両方を外す。
        var headLayers = game.Player.Voxels!.Layers.Count(p => p.Kind == BodyPartKind.Head);
        Assert.AreEqual(2, headLayers, "頭 = 頭の格子 + 顔の層");
        var neckBridges = game.Player.Voxels.Bridges.Count(b => b.Child.Kind == BodyPartKind.Head);
        Assert.AreEqual(all - headLayers - neckBridges, hidden, "自分の頭の格子と顔の層・首のつなぎだけを描かない（相手の頭は描く）");
        Assert.AreEqual(checksum, game.StateChecksum(), "描画だけの設定: 試合の状態は変わらない");
        game.HidePlayerHead = false;
        Assert.AreEqual(all, game.BuildVoxelVolumes().Count);
    }

    private static VrRawFrame Buttons(bool x, bool y, bool a, bool b) => Raw(0f, 0f) with { Buttons = new VrFaceButtons(x, y, a, b) };

    private static VrPlayerMapper TrainingMapper() => new(weaponMode: true)
    {
        GripHolds = true,
        TwoHand = VrTwoHand.Right,
        HeldWeapon = WeaponKind.Katana,
        FaceButtonControls = true,
    };

    [TestMethod]
    public void AloneAReleaseCalibratesButTheFourButtonChordResetsWithoutCalibrating()
    {
        var calibrations = 0;
        var mapper = TrainingMapper();
        mapper.OnCalibrated = (_, _) => calibrations++;
        var sense = Sense();
        mapper.Read(Buttons(false, false, false, false), sense, Dt); // 最初の 1 回は自動の合わせ
        Assert.AreEqual(1, calibrations);
        mapper.Read(Buttons(false, false, true, false), sense, Dt);
        Assert.AreEqual(1, calibrations, "A は離したときに働く（押しただけでは合わせない）");
        mapper.Read(Buttons(false, false, false, false), sense, Dt);
        Assert.AreEqual(2, calibrations, "A 単独を離したら合わせる");

        var resets = 0;
        foreach (var frame in new[] { Buttons(false, false, true, false), Buttons(false, false, true, true), Buttons(true, false, true, true), Buttons(true, true, true, true),
                     Buttons(true, true, true, true), Buttons(true, true, false, true), Buttons(false, false, false, false) })
        {
            mapper.Read(frame, sense, Dt);
            resets += mapper.ResetRequested ? 1 : 0;
        }

        Assert.AreEqual(1, resets, "4 つが揃った瞬間に 1 回だけ作り直す");
        Assert.AreEqual(2, calibrations, "4 つ同時のあとで A を離しても合わせ直さない");
    }

    [TestMethod]
    public void KickButtonsKickWithTheirLegWhileTheHandsKeepFollowingTheControllers()
    {
        var mapper = TrainingMapper();
        var sense = Sense();
        var before = mapper.Read(Buttons(false, false, false, false), sense, Dt).Player;
        Assert.IsNull(before.LeftLeg);
        var kicking = mapper.Read(Buttons(true, false, false, false), sense, Dt).Player;
        Assert.IsNotNull(kicking.LeftLeg, "X = 左脚");
        Assert.IsNull(kicking.RightLeg);
        Assert.AreEqual(before.Right.TargetLocal, kicking.Right.TargetLocal, "蹴りの間も手はコントローラを追う");
        // 蹴りの途中は次の蹴りを始めない（Y を押しても右脚は動かない）。
        var during = mapper.Read(Buttons(true, true, false, false), sense, Dt).Player;
        Assert.IsNull(during.RightLeg);
        for (var i = 0; i < 806; i++)
        {
            mapper.Read(Buttons(false, false, false, false), sense, Dt);
        }

        var right = mapper.Read(Buttons(false, true, false, false), sense, Dt).Player;
        Assert.IsNotNull(right.RightLeg, "Y = 右脚（前の蹴りが終わったあと）");
        Assert.IsNull(right.LeftLeg);
    }

    [TestMethod]
    public void KickTypeFollowsTheDistanceBetweenTheKicksReaches()
    {
        var front = KickSpec.Of(KickTechnique.Front).StandoffFor(Hand.Left, Hand.Right);
        var knee = KickSpec.Of(KickTechnique.Knee).StandoffFor(Hand.Left, Hand.Right);
        Assert.IsTrue(front > knee, $"前蹴りの届く間合い {front} は膝蹴り {knee} より遠い");
        FighterSense At(float reaches) => Sense() with { OpponentChest = new Vector3(0f, 0f, reaches * Reach) };
        Assert.AreEqual(KickTechnique.Front, KickRunner.Choose(Hand.Left, At(front)), "遠ければ前蹴り");
        Assert.AreEqual(KickTechnique.Knee, KickRunner.Choose(Hand.Left, At(knee)), "近ければ膝蹴り");
        Assert.AreEqual(KickTechnique.Front, KickRunner.Choose(Hand.Left, At(front + 3f)));
        Assert.AreEqual(KickTechnique.Knee, KickRunner.Choose(Hand.Left, At(0.2f)));
        var middle = (front + knee) / 2f;
        Assert.AreEqual(KickTechnique.Front, KickRunner.Choose(Hand.Left, At(middle + 0.01f)), "中間は近い方");
        Assert.AreEqual(KickTechnique.Knee, KickRunner.Choose(Hand.Left, At(middle - 0.01f)));
    }

    [TestMethod]
    public void EveryTrainingWeaponHasAMenuWithGapsInsideTheRingAndABuildableWorld()
    {
        var weapons = new[] { WeaponKind.Katana, WeaponKind.Longsword, WeaponKind.Jo, WeaponKind.Staff, WeaponKind.WoodenClub, WeaponKind.IronSword, WeaponKind.Bat };
        foreach (var weapon in weapons)
        {
            Assert.IsTrue(SwordTraining.Supports(weapon), $"{weapon}");
            var wear = weapon == WeaponKind.IronSword ? HandWear.Shield : HandWear.Glove;
            using var probe = new BoxingGame(corners: SwordTraining.Corners(weapon, 2.817f, wear));
            var forms = SwordTraining.Forms(weapon, probe.PlayerSense.ArmReach);
            Assert.IsTrue(forms.Count > 0, $"{weapon}: 型の一覧が空");
            foreach (var form in forms)
            {
                Assert.IsTrue(form.Gap >= SwordTraining.MinGap && form.Gap <= SwordTraining.Ring.Size - 1.127f, $"{weapon} {form.Name}: 間 {form.Gap} がリングの外");
            }

            using var game = new BoxingGame(corners: SwordTraining.Corners(weapon, forms[0].Gap, wear));
            Assert.IsTrue((game.Player.Wields(Hand.Right) || game.Player.Wields(Hand.Left)) && (game.Cpu.Wields(Hand.Right) || game.Cpu.Wields(Hand.Left)),
                $"{weapon}: both fighters start holding the weapon");
            for (var i = 0; i < 201; i++)
            {
                game.Step(default, Dt); // 両者が動く世界を作れる（例外なく進む）
            }
        }
    }

    [TestMethod]
    public void OptionsParseShieldAndEveryTrainingWeapon()
    {
        foreach (var name in new[] { "katana", "longsword", "jo", "woodenclub", "ironsword" })
        {
            Assert.IsTrue(VrMatchOptions.TryParse(["--training", name], out var options, out var error), error);
            Assert.AreEqual(HandWear.Glove, options.Wear);
        }

        Assert.IsTrue(VrMatchOptions.TryParse(["--training", "ironsword", "--shield"], out var shield, out _));
        Assert.AreEqual(HandWear.Shield, shield.Wear);
    }

    [TestMethod]
    public void OptionsParseTwoHandAndFirstPerson()
    {
        Assert.IsTrue(VrMatchOptions.TryParse(["--training", "katana"], out var defaults, out _));
        Assert.AreEqual(VrTwoHand.Right, defaults.TwoHand, "既定は ① 右手で両手");
        Assert.IsFalse(defaults.FirstPerson);
        Assert.IsTrue(VrMatchOptions.TryParse(["--training", "katana", "--first-person", "--two-hand", "both"], out var fpv, out _));
        Assert.AreEqual(VrTwoHand.Both, fpv.TwoHand);
        Assert.IsTrue(fpv.FirstPerson);
        Assert.IsFalse(VrMatchOptions.TryParse(["--two-hand", "left"], out _, out var error));
        StringAssert.Contains(error, "left");
    }
}
