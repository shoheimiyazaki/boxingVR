using System.Numerics;
using MiniRing.Bots;
using MiniRing.Replay;
using MiniRing.Weapons;

namespace MiniRing.Tests;

/// <summary>
/// 棒を手の中で滑らせる握り（<see cref="HandCommand.Slide"/>、2026-11-17）: 滑っている手は棒に力を伝えない・片手で握ると手首のモーメントが効き、
/// 両手で握ると手首のサーボは外れる・手の目標の距離と握りの間隔の矛盾は棒の中心に近い方の手が滑って詰める・手を入れ替えても棒は落ちない・
/// 記録に Slide が残って再現できる。
/// </summary>
[TestClass]
[TestCategory("slow")]
public sealed class SlideGripTests
{
    private const float Dt = 1f / 60f;

    private sealed class Scripted(Func<FighterSense, FighterCommand> read) : IFighterControl
    {
        public FighterCommand Read(in FighterSense sense, float dt) => read(sense);
    }

    /// <summary>棒を両手で構える CPU。edit は（歩の番号、構えの指令、この装置）→ 実際に出す指令。</summary>
    private sealed class Rig : IDisposable
    {
        public readonly BoxingGame Game;
        public int Step;

        public Rig(Func<int, FighterCommand, Rig, FighterCommand> edit)
        {
            Game = new BoxingGame(seed: 1, corners: new BoxingCorners("slide",
                _ => new Scripted(s => edit(Step, FormSwordsman.GuardCommand(WeaponKind.Staff, s, Vector3.UnitZ, 0.6f, 0.5f), this)),
                _ => new StandGuard(), CpuWeapon: WeaponKind.Staff, CpuTwoHanded: true));
        }

        public Fighter Cpu => Game.Cpu;

        public Weapon Staff => Game.WeaponOf(Game.Cpu)!;

        public void Run(int steps)
        {
            for (var i = 0; i < steps; i++)
            {
                Game.Step(default, Dt);
                Step++;
            }
        }

        public Vector3 Axis() => Vector3.Transform(Vector3.UnitX, Game.World.GetState(Staff.Body.BodyId).Orientation);

        public Vector3 Center() => Game.World.GetState(Staff.Body.BodyId).Position;

        private (Vector3 Center, Vector3 Axis)? _pin;

        /// <summary>
        /// 最初に呼んだときの棒の上の、ローカル X の点へ拳を置く目標（体フレーム）。VR で手を棒の上に置くのと同じ。
        /// 棒の今の位置ではなく最初の位置を基準にする（今の位置から決めると、棒が垂れるのに目標も付いて下がり、支えが無くなる）。
        /// </summary>
        public Vector3 On(float x)
        {
            var (center, axis) = _pin ??= (Center(), Axis());
            return Cpu.Frame.WorldToLocal(center + (axis * x));
        }

        public void Dispose() => Game.Dispose();
    }

    private const float RightX = 0.5f * Weapon.StaffHandSpacing;

    [TestMethod]
    public void ASlidingHand_DoesNotPushTheStaff()
    {
        // 右手だけが握る（左手は滑らせ続ける）。左手の目標を大きく振り回しても、棒の動きは左手を動かさないときとほぼ同じで、
        // 左手が握ったまま同じように振り回す（握りが引かれる）ときよりずっと小さい。
        FighterCommand Edit(int step, FighterCommand guard, bool swing, bool slide)
        {
            if (step < 150)
            {
                return guard;
            }

            var left = guard.Left with { Slide = slide };
            if (swing)
            {
                var phase = (step - 150) * Dt * 6f;
                left = left with { TargetLocal = left.TargetLocal + new Vector3(0.2f * MathF.Sin(phase), 0.15f * MathF.Cos(phase), 0.1f * MathF.Sin(2f * phase)) };
            }

            return guard with { Left = left };
        }

        using var still = new Rig((s, g, _) => Edit(s, g, swing: false, slide: true));
        using var swung = new Rig((s, g, _) => Edit(s, g, swing: true, slide: true));
        using var dragged = new Rig((s, g, _) => Edit(s, g, swing: true, slide: false));
        foreach (var rig in new[] { still, swung, dragged })
        {
            rig.Run(150);
            Assert.IsNotNull(rig.Cpu.GripX(Hand.Left), "both hands hold the staff before the left one slides");
            rig.Run(90);
        }

        Assert.IsNull(swung.Cpu.GripX(Hand.Left), "the left hand does not hold the staff while it slides");
        Assert.IsNotNull(swung.Cpu.GripX(Hand.Right));
        Assert.IsTrue(swung.Cpu.Wields(Hand.Left) && swung.Cpu.Wields(Hand.Right), "the staff is still the fighter's");
        Assert.AreEqual(Vector3.Zero, swung.Cpu.HandPush(Hand.Left), "a sliding hand's push is not counted into the staff's strike");
        Assert.IsFalse(swung.Cpu.HasWristServo(Hand.Left), "no wrist on a sliding hand");

        var free = Vector3.Distance(still.Center(), swung.Center());
        var pulled = Vector3.Distance(still.Center(), dragged.Center());
        var freeAngle = MathF.Acos(Math.Clamp(Vector3.Dot(still.Axis(), swung.Axis()), -1f, 1f));
        var pulledAngle = MathF.Acos(Math.Clamp(Vector3.Dot(still.Axis(), dragged.Axis()), -1f, 1f));
        Console.WriteLine($"staff moved by a free swinging hand {free * 100:0.0} cm / {freeAngle:0.00} rad; by a gripping swinging hand {pulled * 100:0.0} cm / {pulledAngle:0.00} rad");
        Assert.IsLessThan(0.5f * (pulled + 1e-3f), free, "a swinging free hand drags the staff far less than a gripping one does");
        Assert.IsLessThan(0.6f * (pulledAngle + 1e-3f), freeAngle, "and turns it far less");
        Assert.IsLessThan(0.15f, freeAngle);
    }

    [TestMethod]
    public void TwoHandsGripping_HaveNoWristServo_OneHandGripping_HasOne()
    {
        using var rig = new Rig((step, g, _) => step < 150 ? g : g with { Left = g.Left with { Slide = step is >= 150 and < 200 } });
        rig.Run(149);
        Assert.IsNotNull(rig.Cpu.GripX(Hand.Left));
        Assert.IsNotNull(rig.Cpu.GripX(Hand.Right));
        Assert.IsFalse(rig.Cpu.HasWristServo(Hand.Left), "two hands: the staff turns by the couple of the two fists alone");
        Assert.IsFalse(rig.Cpu.HasWristServo(Hand.Right));
        rig.Run(40);
        Assert.IsNull(rig.Cpu.GripX(Hand.Left));
        Assert.IsTrue(rig.Cpu.HasWristServo(Hand.Right), "one hand gripping: its wrist holds the orientation");
        Assert.IsFalse(rig.Cpu.HasWristServo(Hand.Left));
        rig.Run(120);
        Assert.IsNotNull(rig.Cpu.GripX(Hand.Left), "the left hand takes hold again when it is back on the staff");
        Assert.IsFalse(rig.Cpu.HasWristServo(Hand.Right), "and the right wrist lets go again");
    }

    [TestMethod]
    public void OneHandGripping_TurnsTheStaffTowardTheOrientationTarget()
    {
        // 右手だけ握り、向きの目標を方位角で 0.8 rad 回す: 棒の向きが目標へ近づく（手首のモーメントが効く）。
        var turn = Quaternion.CreateFromAxisAngle(Vector3.UnitY, 0.8f);
        using var rig = new Rig((step, g, _) =>
        {
            if (step < 150)
            {
                return g;
            }

            var q = g.Right.Orientation ?? Quaternion.Identity;
            return g with
            {
                Left = g.Left with { Slide = true },
                Right = g.Right with { Orientation = Quaternion.Concatenate(q, turn) },
            };
        });
        rig.Run(150);
        var before = rig.Axis();
        rig.Run(120);
        var after = rig.Axis();
        var turned = MathF.Acos(Math.Clamp(Vector3.Dot(before, after), -1f, 1f));
        Assert.IsGreaterThan(0.3f, turned, "the wrist turned the staff toward the new orientation");
    }

    /// <summary>左手を滑らせて棒の後ろへ <paramref name="back"/> の所に置き直し、それから握り直す指令の列（右手は構えのまま）。</summary>
    private static Func<int, FighterCommand, Rig, FighterCommand> LeftMovedBack(float back, Func<int, FighterCommand, Rig, FighterCommand>? after = null) => (step, g, rig) =>
    {
        if (step < 150)
        {
            return g;
        }

        var left = g.Left with { TargetLocal = rig.On(-RightX - back) };
        var right = g.Right with { TargetLocal = rig.On(RightX) };
        if (step < 230)
        {
            return g with { Left = left with { Slide = true }, Right = right };
        }

        return after is null ? g with { Left = left, Right = right } : after(step, g with { Left = left, Right = right }, rig);
    };

    [TestMethod]
    public void AHandSlidesToANewPlace_AndTakesHoldThere()
    {
        const float Back = 0.12f;
        using var rig = new Rig(LeftMovedBack(Back));
        rig.Run(150);
        Assert.AreEqual(-RightX, rig.Cpu.GripX(Hand.Left)!.Value, 1e-3f);
        rig.Run(60);
        Assert.IsNull(rig.Cpu.GripX(Hand.Left));
        rig.Run(80);
        var left = rig.Cpu.GripX(Hand.Left);
        Assert.IsNotNull(left, "the left hand took hold again further back");
        Assert.AreEqual(-RightX - Back, left.Value, 0.03f, "at the place the hand was put on the staff");
        Assert.AreEqual(RightX, rig.Cpu.GripX(Hand.Right)!.Value, 0.03f);
    }

    [TestMethod]
    public void WhenTheHandTargetsAreFurtherApartThanTheGrips_TheHandNearerTheMiddleSlides_TheOuterOneStays()
    {
        const float Back = 0.12f;
        using var rig = new Rig(LeftMovedBack(Back, (step, g, r) =>
            // 前の手の目標を棒に沿って 10 cm 前へ: 目標の距離が握りの間隔より 10 cm 広い。
            step < 330 ? g : g with { Right = g.Right with { TargetLocal = r.On(RightX + 0.10f) } }));
        rig.Run(329);
        var leftBefore = rig.Cpu.GripX(Hand.Left);
        var rightBefore = rig.Cpu.GripX(Hand.Right);
        Assert.IsNotNull(leftBefore, "the left hand took hold again further back");
        Assert.IsNotNull(rightBefore);
        Assert.IsLessThan(-RightX - 0.05f, leftBefore.Value, "the left grip is now further back along the staff");

        rig.Run(90);
        var leftAfter = rig.Cpu.GripX(Hand.Left)!.Value;
        var rightAfter = rig.Cpu.GripX(Hand.Right)!.Value;
        Assert.AreEqual(leftBefore.Value, leftAfter, 1e-4f, "the outer hand does not move on the staff");
        Assert.AreEqual(rightBefore.Value + 0.10f, rightAfter, 0.03f, "the hand nearer the middle slid toward the front end to widen the grips");
    }

    [TestMethod]
    public void SwappingHands_TheStaffIsNotDropped_AndTheReleasedHandCanTakeHoldAgain()
    {
        // 右手を離して前へ滑らせる間、左手が 1 本で持つ。右手は前の別の所で握り直す。向きの指令は無し（1 本で握った手は握った瞬間の向きを保つ）。
        const float Forward = 0.15f;
        using var rig = new Rig((step, g, r) =>
        {
            if (step < 150)
            {
                return g;
            }

            var right = g.Right with { Orientation = null, TargetLocal = r.On(step >= 300 ? Forward : RightX), Slide = step is >= 300 and < 400 };
            return g with { Left = g.Left with { Orientation = null, TargetLocal = r.On(-RightX) }, Right = right };
        });
        rig.Run(150);
        var startY = rig.Center().Y;
        var startAxis = rig.Axis();
        rig.Run(190);
        Assert.IsNull(rig.Cpu.GripX(Hand.Right), "the right hand let go");
        Assert.AreEqual(-RightX, rig.Cpu.GripX(Hand.Left)!.Value, 1e-3f, "the left hand holds the staff where it was");
        Assert.IsTrue(rig.Cpu.Wields(Hand.Right) && rig.Cpu.Wields(Hand.Left), "the fighter still wields the staff");
        Assert.IsTrue(rig.Cpu.HasWristServo(Hand.Left), "the left wrist carries the staff alone");
        Assert.IsGreaterThan(startY - 0.05f, rig.Center().Y, "the staff did not fall out of the left hand");
        Assert.IsLessThan(0.6f, MathF.Acos(Math.Clamp(Vector3.Dot(startAxis, rig.Axis()), -1f, 1f)), "and kept its direction roughly (one wrist at a third of the strength sags a little under the staff weight)");
        rig.Run(120);
        Assert.IsGreaterThan(RightX + 0.03f, rig.Cpu.GripX(Hand.Right)!.Value, "the right hand takes hold again, further along than before");
        Assert.AreEqual(Forward + RightX, rig.Cpu.GripX(Hand.Right)!.Value - rig.Cpu.GripX(Hand.Left)!.Value, 0.03f, "the grips settle to the span of the hand targets (the left hand, now the nearer to the middle, slid back to match)");
        Assert.IsFalse(rig.Cpu.HasWristServo(Hand.Left), "two hands again: no wrist");
        Assert.IsGreaterThan(startY - 0.05f, rig.Center().Y);
    }

    [TestMethod]
    public void ANonSlidingWeapon_IgnoresTheSlideCommand()
    {
        using var game = new BoxingGame(seed: 1, corners: new BoxingCorners("katana",
            _ => new Scripted(s =>
            {
                var guard = FormSwordsman.GuardCommand(WeaponKind.Katana, s, Vector3.UnitZ, 0.6f, 0.5f);
                return guard with { Left = guard.Left with { Slide = true }, Right = guard.Right with { Slide = true } };
            }),
            _ => new StandGuard(), CpuWeapon: WeaponKind.Katana, CpuTwoHanded: true));
        for (var i = 0; i < 240; i++)
        {
            game.Step(default, Dt);
        }

        Assert.AreEqual(Hand.Left, game.Cpu.SupportOf(Hand.Right), "a katana is gripped where it is made to be, whatever the Slide flag says");
        Assert.IsNotNull(game.Cpu.GripX(Hand.Right));
        Assert.IsTrue(game.Cpu.HasWristServo(Hand.Right) && game.Cpu.HasWristServo(Hand.Left), "both wrists work as before");
    }

    [TestMethod]
    public void TheSlideFlags_AreRecorded_AndTheMatchReplaysBitForBit()
    {
        var corners = BotCatalog.Corners("boxer") with { PlayerWeapon = WeaponKind.Staff, PlayerTwoHanded = true };
        using var game = new BoxingGame(seed: 3, corners: corners);
        using var stream = new MemoryStream();
        using (var recorder = new ReplayRecorder(stream, game, "test"))
        {
            for (var i = 0; i < ReplayRecorder.ChecksumInterval * 3; i++)
            {
                var guard = FormSwordsman.GuardCommand(WeaponKind.Staff, game.PlayerSense, Vector3.UnitZ, 0.6f, 0.5f);
                var slideLeft = i is >= 70 and < 110;
                var slideRight = i is >= 130 and < 150;
                var command = guard with { Left = guard.Left with { Slide = slideLeft }, Right = guard.Right with { Slide = slideRight } };
                recorder.Step(game, new BoxingInput(command), Dt);
            }
        }

        var data = ReplayFile.Read(new MemoryStream(stream.ToArray()));
        Assert.IsTrue(data.Steps[80].Input.Player.Left.Slide, "the Slide flag survives the file");
        Assert.IsFalse(data.Steps[80].Input.Player.Right.Slide);
        Assert.IsTrue(data.Steps[140].Input.Player.Right.Slide);
        Assert.IsFalse(data.Steps[20].Input.Player.Left.Slide);
        Assert.IsTrue(ReplayPlayer.Verify(data).Reproduced, "the recording replays to the same checksums");
    }
}
