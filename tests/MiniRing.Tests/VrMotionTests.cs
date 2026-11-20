using System.Numerics;
using MiniRing.Motion;
using MiniRing.Vr;
using MiniRing.Weapons;

namespace MiniRing.Tests;

/// <summary>VR で武器を使う入力の写像・VR の動きの記録（.vrmotion）・記録をなぞる操作元（VR の実機なし）。</summary>
[TestClass]
public sealed class VrMotionTests
{
    private static readonly Quaternion Facing = Quaternion.CreateFromAxisAngle(Vector3.UnitY, MathF.PI); // コントローラ・HMD の前が +Z

    private static FighterSense Sense(float armReach) => new(new BodyFrame(Vector3.Zero, 0f), armReach, default, new Vector3(0f, 2.817f, 2.817f), true);

    private static XrInputFrame Frame(float leftTrigger, float rightTrigger, Quaternion? rightHand = null) => new(
        new Vector3(0f, 1.58f, 0f), Facing,
        new Vector3(-0.12f, 1.28f, 0f), new Vector3(-0.25f, 1.30f, 0.25f), true, true,
        Vector2.Zero, Vector2.Zero, 0f, 0f, false, true,
        Facing, rightHand ?? Facing, leftTrigger, rightTrigger);

    [TestMethod]
    public void ReleasedTriggerSlidesAndHeldTriggerGrips()
    {
        var control = new VrPlayerControl(weaponMode: true);
        var sense = Sense(0.5296f);
        var command = control.Read(Frame(leftTrigger: 0f, rightTrigger: 1f), sense, 1f / 90f).Player;
        Assert.IsTrue(command.Left.Slide, "トリガーを離した左手は棒を滑らせる");
        Assert.IsFalse(command.Right.Slide, "トリガーを引いた右手は握る");
        command = control.Read(Frame(leftTrigger: 1f, rightTrigger: 0f), sense, 1f / 90f).Player;
        Assert.IsFalse(command.Left.Slide);
        Assert.IsTrue(command.Right.Slide);
    }

    [TestMethod]
    public void TriggerHasHysteresis()
    {
        var control = new VrPlayerControl(weaponMode: true);
        var sense = Sense(0.5296f);
        bool Slide(float trigger) => control.Read(Frame(trigger, 1f), sense, 1f / 90f).Player.Left.Slide;
        Assert.IsTrue(Slide(0.5f), "押した値（0.55）に届かないうちは離したまま");
        Assert.IsFalse(Slide(0.6f));
        Assert.IsFalse(Slide(0.4f), "離した値（0.35）まで下がらないうちは握ったまま");
        Assert.IsTrue(Slide(0.3f));
    }

    [TestMethod]
    public void WeaponOrientationIsTheControllerOrientationInTheBodyFrame()
    {
        var control = new VrPlayerControl(weaponMode: true);
        var sense = Sense(0.5296f);
        var yawed = Quaternion.CreateFromAxisAngle(Vector3.UnitY, MathF.PI + 0.5f); // 前（−Z）が、体の前から 0.5 rad 横を向く
        var command = control.Read(Frame(1f, 1f, rightHand: yawed), sense, 1f / 90f).Player;
        var q = command.Right.Orientation ?? throw new AssertFailedException("武器を持つ試合では向きの目標を出す");
        var x = Vector3.Transform(Vector3.UnitX, q); // 武器のローカル X = コントローラの前
        Assert.AreEqual(MathF.Sin(0.5f), x.X, 1e-4f);
        Assert.AreEqual(0f, x.Y, 1e-4f);
        Assert.AreEqual(MathF.Cos(0.5f), x.Z, 1e-4f);
        // コントローラを前へ向けたままなら、武器のローカル X は体の前、Y は上。
        var straight = control.Read(Frame(1f, 1f), sense, 1f / 90f).Player.Right.Orientation!.Value;
        Assert.AreEqual(1f, Vector3.Transform(Vector3.UnitX, straight).Z, 1e-4f);
        Assert.AreEqual(1f, Vector3.Transform(Vector3.UnitY, straight).Y, 1e-4f);
    }

    [TestMethod]
    public void BareHandsKeepTheOldBehaviour()
    {
        var control = new VrPlayerControl();
        var command = control.Read(Frame(0f, 0f), Sense(0.5296f), 1f / 90f).Player;
        Assert.IsNull(command.Left.Orientation);
        Assert.IsNull(command.Right.Orientation);
        Assert.IsFalse(command.Left.Slide);
        Assert.IsFalse(command.Right.Slide);
    }

    [TestMethod]
    public void MotionClipRoundTripsThroughJsonAndFile()
    {
        var clip = VrMotionSynth.StaffThrust();
        var again = VrMotionClip.FromJson(clip.ToJson());
        Assert.AreEqual(clip.Header, again.Header);
        Assert.AreEqual(clip.Frames.Count, again.Frames.Count);
        for (var i = 0; i < clip.Frames.Count; i++)
        {
            CollectionAssert.AreEqual(clip.Frames[i].ToNumbers(), again.Frames[i].ToNumbers(), $"frame {i}");
        }

        var path = Path.Combine(Path.GetTempPath(), $"vrmotion_{Guid.NewGuid():N}{VrMotionClip.Extension}");
        try
        {
            clip.Save(path);
            var loaded = VrMotionClip.Load(path);
            Assert.AreEqual(clip.Duration, loaded.Duration, 0f);
            Assert.AreEqual("Staff", loaded.Header.Weapon);
        }
        finally
        {
            File.Delete(path);
        }
    }

    [TestMethod]
    public void MotionClipRejectsWrongVersionAndColumns()
    {
        var json = VrMotionSynth.StaffThrust().ToJson();
        Assert.ThrowsExactly<FormatException>(() => VrMotionClip.FromJson(json.Replace("\"version\":1", "\"version\":99")));
        Assert.ThrowsExactly<FormatException>(() => VrMotionClip.FromJson(json.Replace("\"lTrigger\"", "\"lTriggerX\"")));
        Assert.ThrowsExactly<FormatException>(() => VrMotionClip.FromJson("{\"format\":\"other\"}"));
    }

    [TestMethod]
    public void NormalizedAndHumanLengthsAgree()
    {
        var clip = VrMotionSynth.StaffThrust();
        // 動きの途中（頭は止まっているので肩の位置は変わらない）の右手の写像後の値 = 人の寸法の手の目標を腕の長さで割ったもの。
        var frame = clip.Sample(0.5f);
        var height = clip.Header.PlayerHeight;
        var shoulders = PuppetMapping.PlayerShoulderFrame(frame.HeadPosition, 0f, height);
        var human = PuppetMapping.HandTargetLocal(frame.RightHandPosition, shoulders, 1f, Hand.Right, height, clip.Header.MotionGain);
        var normalized = clip.ToNormalized(human);
        Assert.AreEqual(normalized.X, frame.RightTarget.X, 2e-3f);
        Assert.AreEqual(normalized.Y, frame.RightTarget.Y, 2e-3f);
        Assert.AreEqual(normalized.Z, frame.RightTarget.Z, 2e-3f);
        var back = clip.ToHuman(frame.RightTarget);
        Assert.AreEqual(human.Z, back.Z, 2e-3f);
    }

    [TestMethod]
    public void RecorderThinsOutFastStepsAndKeepsTheDisplayRate()
    {
        var mapper = new VrPlayerMapper(true);
        var sense = Sense(0.5296f);
        var frame = Frame(1f, 1f).ToRaw();
        var fast = new VrMotionRecorder("Staff");
        var display = new VrMotionRecorder("Staff");
        for (var i = 0; i < 480; i++)
        {
            var command = mapper.Read(frame, sense, 1f / 240f).Player;
            fast.Add(i / 240f, frame, command, mapper, 0.5296f);
            display.Add(i / 90f, frame, command, mapper, 0.5296f);
        }

        Assert.IsTrue(fast.Count < 480 && fast.Count > 150, $"240 Hz は間引く: {fast.Count}");
        Assert.AreEqual(480, display.Count, "90 Hz はそのまま残る");
        var clip = fast.Build()!;
        for (var i = 1; i < clip.Frames.Count; i++)
        {
            Assert.IsTrue(clip.Frames[i].Time - clip.Frames[i - 1].Time >= VrMotionRecorder.MinInterval - 1e-4f);
        }
    }

    [TestMethod]
    public void ImitationFollowsTheRecordedTargetsScaledByArmLength()
    {
        var clip = VrMotionSynth.StaffThrust();
        var frame = clip.Sample(0.45f); // 突きの最中（左手はトリガーを離している）
        Assert.IsTrue(frame.LeftSlide);
        var small = VrMotionImitation.ToCommand(frame, Sense(0.5296f), Vector3.UnitZ);
        var big = VrMotionImitation.ToCommand(frame, Sense(1.059f), Vector3.UnitZ);
        Assert.AreEqual(0f, (small.Right.TargetLocal - (frame.RightTarget * 0.5296f)).Length(), 1e-6f);
        Assert.AreEqual(0f, (small.Left.TargetLocal - (frame.LeftTarget * 0.5296f)).Length(), 1e-6f);
        // 腕の長さが 2 倍の人形の目標は、ちょうど 2 倍（比が保たれる）。
        Assert.AreEqual(0f, (big.Right.TargetLocal - (small.Right.TargetLocal * 2f)).Length(), 1e-5f);
        Assert.AreEqual(0f, (big.Left.TargetLocal - (small.Left.TargetLocal * 2f)).Length(), 1e-5f);
        Assert.AreEqual(0f, (big.HeadOffset - (small.HeadOffset * 2f)).Length(), 1e-5f);
        Assert.IsTrue(small.Left.Slide);
        Assert.IsFalse(small.Right.Slide);
        Assert.AreEqual(frame.RightWeaponOrientation, small.Right.Orientation);
    }

    [TestMethod]
    public void ImitationAdvancesOnlyByTheStepsItIsGiven()
    {
        var clip = VrMotionSynth.StaffThrust();
        var control = new VrMotionImitation(clip);
        var sense = Sense(0.5296f);
        for (var i = 0; i < 120; i++)
        {
            control.Read(sense, 1f / 240f);
        }

        Assert.AreEqual(0.5f, control.ClipTime, 1e-4f, "時計ではなく、渡した刻みの和で進む");
        var sliding = control.Read(sense, 1f / 240f).Left.Slide;
        Assert.IsTrue(sliding, "0.5 s は突きの最中");
        Assert.IsFalse(control.Finished);
        for (var i = 0; i < 480; i++)
        {
            control.Read(sense, 1f / 240f);
        }

        Assert.IsTrue(control.Finished);
    }

    [TestMethod]
    public void MatchOptionsBuildWeaponCornersOnlyWhenAsked()
    {
        Assert.IsTrue(VrMatchOptions.TryParse([], out var none, out _));
        Assert.IsNull(none.PlayerWeapon);
        Assert.IsNull(none.Corners.PlayerWeapon);
        Assert.IsTrue(VrMatchOptions.TryParse(["--weapon", "staff"], out var staff, out _));
        Assert.AreEqual(WeaponKind.Staff, staff.Corners.PlayerWeapon);
        Assert.AreEqual(WeaponKind.Staff, staff.Corners.CpuWeapon);
        Assert.IsTrue(staff.Corners.PlayerTwoHanded);
        Assert.IsTrue(VrMatchOptions.TryParse(["--weapon", "katana", "--cpu-weapon", "none"], out var fists, out _));
        Assert.IsNull(fists.Corners.CpuWeapon);
        Assert.IsFalse(VrMatchOptions.TryParse(["--weapon", "bogus"], out _, out var error));
        StringAssert.Contains(error, "bogus");
        Assert.IsFalse(VrMatchOptions.TryParse(["--cpu-weapon", "staff"], out _, out _));
        Assert.IsFalse(VrMatchOptions.TryParse(["--cpu", "nobody"], out _, out _));
    }

    [TestMethod]
    public void SessionRecordsBetweenButtonPressesAndSavesOnDispose()
    {
        var directory = Path.Combine(Path.GetTempPath(), $"vrmotion_{Guid.NewGuid():N}");
        try
        {
            var mapper = new VrPlayerMapper(true);
            var sense = Sense(0.5296f);
            var frame = Frame(1f, 1f);
            var toggle = frame with { RecordTogglePressed = true };
            using (var session = new VrMotionSession(WeaponKind.Staff, directory, startRecording: false))
            {
                for (var i = 0; i < 30; i++)
                {
                    var f = i == 5 ? toggle : frame; // 5 歩目で開始
                    session.Step(f, mapper.Read(f.ToRaw(), sense, 1f / 90f).Player, mapper, 0.5296f, 1f / 90f);
                }

                Assert.IsTrue(session.Recording);
                session.Step(toggle, mapper.Read(toggle.ToRaw(), sense, 1f / 90f).Player, mapper, 0.5296f, 1f / 90f); // 停止して保存
                Assert.IsFalse(session.Recording);
                for (var i = 0; i < 10; i++)
                {
                    session.Step(frame, mapper.Read(frame.ToRaw(), sense, 1f / 90f).Player, mapper, 0.5296f, 1f / 90f);
                }
            }

            var files = Directory.GetFiles(directory, "*" + VrMotionClip.Extension);
            Assert.AreEqual(1, files.Length);
            StringAssert.Contains(Path.GetFileName(files[0]), "_staff");
            var clip = VrMotionClip.Load(files[0]);
            Assert.IsTrue(clip.Frames.Count >= 25, $"frames {clip.Frames.Count}");
            Assert.AreEqual("Staff", clip.Header.Weapon);
        }
        finally
        {
            if (Directory.Exists(directory))
            {
                Directory.Delete(directory, true);
            }
        }
    }
}
