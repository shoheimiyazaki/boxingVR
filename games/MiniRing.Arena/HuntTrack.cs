using System.Globalization;
using System.Numerics;
using MiniRing.Bots;
using MiniRing.Characters;
using MiniRing.Weapons;

namespace MiniRing.Arena;

/// <summary>
/// 鎌の 1 振りを、相手のいない所（相手は 4 m 先）で振り、刃元・切っ先の目標と実際の位置を並べる（<c>huntlab --track 1</c>。切り分け用）。
/// 体のサーボが経路を追えるか（届く高さ・速さ・遅れ）を、相手に当たって止まる効果と分けて見る。
/// </summary>
internal static class HuntTrack
{
    private const float Dt = 1f / 240f;

    public static void Run(TextWriter log, Vector3 aimLocal, bool draw, float drive, float windup, Hand hand = Hand.Right, ClawCylinder? cylinder = null)
    {
        var creature = CharacterRoster.Get("galva");
        var target = CharacterRoster.Get("marcus");
        var corners = BotCatalog.Corners("boxer") with
        {
            PlayerBody = creature.Build, PlayerWear = creature.Wear, PlayerLook = creature.Look, PlayerFaceSpec = creature.Face,
            CpuBody = target.Build, CpuWear = HandWear.None, CpuLook = target.Look, CpuFaceSpec = target.Face,
            ExternalBlue = true, Referee = false, Ring = RingLayout.Boxing with { StartGap = 4f, FromCorners = false },
        };
        using var game = new BoxingGame(seed: 1, corners: corners);
        var f = game.Player;
        var guard = new FighterCommand(Vector3.Zero, Vector3.UnitZ, ClawStroke.Guard(Hand.Left, f.ArmReach).ToCommand(Hand.Left),
            ClawStroke.Guard(Hand.Right, f.ArmReach).ToCommand(Hand.Right), ClawBot.Stance(f.ArmReach));
        var blue = new FighterCommand(Vector3.Zero, -Vector3.UnitZ, PassiveControl.Hang(Hand.Left, game.Cpu.ArmReach), PassiveControl.Hang(Hand.Right, game.Cpu.ArmReach));
        for (var i = 0; i < 216; i++)
        {
            game.Step(new BoxingInput(guard, Blue: blue), Dt);
        }

        var origin = game.PlayerSense.Self;
        var aim = aimLocal * f.ArmReach;
        var stroke = ClawStroke.Create(hand, f.ArmReach, aim, true, drive, windup, draw, cylinder);
        var motion = new ClawMotion();
        var best = (Distance: float.PositiveInfinity, Speed: 0f, Tilt: 0f, Lead: 0f, Time: 0);
        var claw = game.GloveOf(f, hand)!;
        var worldAim = origin.LocalToWorld(aim);
        var tuning = new FighterTuning() with { Build = creature.Build };
        var clawMass = game.World.GetMass(claw.Body.BodyId);
        var gravityTorque = clawMass * 9.81f * 0.5f * ClawSegment.Length;
        log.WriteLine(string.Create(CultureInfo.InvariantCulture,
            $"claw mass {clawMass:0.00} kg (worn), torque to hold it level {gravityTorque:0.0} N·m, wrist servo max {tuning.WristMaxTorque * tuning.TorqueScale:0.0} N·m at {tuning.WristServoHz:0.0} Hz (TorqueScale {tuning.TorqueScale:0.00})"));
        log.WriteLine(string.Create(CultureInfo.InvariantCulture,
            $"reach {f.ArmReach:0.00} m, blade {ClawSegment.Length:0.00} m, aim (local, reach = 1) {aimLocal.X:0.00} {aimLocal.Y:0.00} {aimLocal.Z:0.00}, draw {draw}, drive {drive:0.00} s, windup {windup:0.00}"));
        for (var i = 0; i < (int)(stroke.Duration / Dt) + 24; i++)
        {
            var time = i * Dt;
            var command = motion.Read(hand, i == 0, true, game.PlayerSense, worldAim, Dt, drive, windup, draw,
                cylinder is { } cyl ? cyl with { Axis = origin.Rotate(cyl.Axis) } : null);
            if (cylinder is { } cut)
            {
                Crossing(game, claw, hand, cut, worldAim, origin.Rotate(cut.Axis), ref best);
            }

            game.Step(new BoxingInput(hand == Hand.Left ? guard with { Left = command } : guard with { Right = command }, Blue: blue), Dt);
            if (i % 2 != 0)
            {
                continue;
            }

            var want = stroke.Sample(time);
            var state = game.World.GetState(claw.Body.BodyId);
            var self = game.PlayerSense.Self;
            var root = self.WorldToLocal(state.Position + Vector3.Transform(Weapon.GripOf(claw.Kind), state.Orientation)) / f.ArmReach;
            var tip = self.WorldToLocal(state.Position + Vector3.Transform(Weapon.GripOf(claw.Kind) + SickleClawShape.Edge(hand), state.Orientation)) / f.ArmReach;
            var mid = self.WorldToLocal(state.Position + Vector3.Transform(Weapon.GripOf(claw.Kind) + (0.5f * SickleClawShape.Edge(hand)), state.Orientation));
            var speed = (state.LinearVelocity + Vector3.Cross(state.AngularVelocity, Vector3.Transform(Weapon.GripOf(claw.Kind) + (0.5f * SickleClawShape.Edge(hand)), state.Orientation))).Length();
            log.WriteLine(string.Create(CultureInfo.InvariantCulture,
                $"  t={time:0.00}  want root ({want.Root.X / f.ArmReach:0.00},{want.Root.Y / f.ArmReach:0.00},{want.Root.Z / f.ArmReach:0.00}) tip ({want.Tip.X / f.ArmReach:0.00},{want.Tip.Y / f.ArmReach:0.00},{want.Tip.Z / f.ArmReach:0.00})  got root ({root.X:0.00},{root.Y:0.00},{root.Z:0.00}) tip ({tip.X:0.00},{tip.Y:0.00},{tip.Z:0.00})  mid speed {speed:0.00} m/s  aim-mid {Vector3.Distance(mid, aim) * 100f:0} cm"));
        }

        if (cylinder is not null)
        {
            log.WriteLine(string.Create(CultureInfo.InvariantCulture,
                $"cylinder crossing: cut point passes {best.Distance * 100f:0.0} cm from the axis at step {best.Time}, speed {best.Speed:0.00} m/s, blade plane tilted {best.Tilt:0.0} deg from the cut plane, edge leads by {best.Lead:0.0} deg off the motion"));
        }
    }

    /// <summary>
    /// 斬る点（刃元から弦の <see cref="ClawCylinder.Point"/>）が円柱の軸にいちばん近づいた歩の、軸からの距離・斬る点の速さ・刃の面の傾き（刃の面の法線と軸の角）・
    /// 刃の進む向きのずれ（刃の弦に垂直な向きと、斬る点の速度の、切断面の中の角）。
    /// </summary>
    private static void Crossing(BoxingGame game, Weapon claw, Hand hand, ClawCylinder cut, Vector3 center, Vector3 axis, ref (float Distance, float Speed, float Tilt, float Lead, int Time) best)
    {
        var state = game.World.GetState(claw.Body.BodyId);
        var root = state.Position + Vector3.Transform(Weapon.GripOf(claw.Kind), state.Orientation);
        var chord = Vector3.Transform(SickleClawShape.Edge(hand), state.Orientation);
        var point = root + (chord * cut.Point);
        var offset = point - center;
        var radial = offset - (axis * Vector3.Dot(offset, axis));
        var distance = radial.Length();
        best.Time++;
        if (distance >= best.Distance)
        {
            return;
        }

        var velocity = state.LinearVelocity + Vector3.Cross(state.AngularVelocity, point - state.Position);
        var normal = Vector3.Normalize(Vector3.Transform(Vector3.UnitY, state.Orientation));
        var tilt = MathF.Acos(Math.Clamp(MathF.Abs(Vector3.Dot(normal, axis)), 0f, 1f)) * 180f / MathF.PI;
        var inPlane = velocity - (axis * Vector3.Dot(velocity, axis));
        var chordInPlane = chord - (axis * Vector3.Dot(chord, axis));
        var lead = inPlane.LengthSquared() > 1e-6f && chordInPlane.LengthSquared() > 1e-6f
            ? 90f - (MathF.Acos(Math.Clamp(MathF.Abs(Vector3.Dot(Vector3.Normalize(inPlane), Vector3.Normalize(chordInPlane))), 0f, 1f)) * 180f / MathF.PI)
            : 90f;
        best = (distance, velocity.Length(), tilt, lead, best.Time);
    }
}
