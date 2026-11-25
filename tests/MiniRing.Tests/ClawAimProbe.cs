using System.Numerics;
using MiniRing.Bots;
using MiniRing.Characters;
using MiniRing.Weapons;

namespace MiniRing.Tests;

/// <summary>はめた鎌の向きが指令をどれだけ追うか（2026-11-25、huntlab の切り分け）。相手は 4 m 先、鎌を同じ目標に 1 s 保つ。</summary>
[TestClass]
[TestCategory("Probe")]
public sealed class ClawAimProbe
{
    [TestMethod]
    public void HeldTargets()
    {
        var reach = 1f;
        foreach (var (label, tuning) in new[]
        {
            ("now (103)", new FighterTuning()),
            ("12Hz z3", new FighterTuning() with { GripWristServoHz = 12f }),
        })
        {
            foreach (var (name, make) in Targets())
            {
                var (angle, root, wantBend, gotBend, gotHand) = Hold(tuning, make, out reach);
                Console.WriteLine($"{label,-16} {name,-22} blade angle error {angle:0.0} deg, root error {root * 100f:0} cm, wrist bend wanted {wantBend:0} got {gotBend:0} deg, hand at {gotHand}");
            }
        }
    }

    [TestMethod]
    public void LongClawTorsoStrokeFall()
    {
        foreach (var aim in new[] { 0, 2 })
        {
            Console.WriteLine($"aim {aim}");
            var t = MiniRing.Arena.ClawLab.Run("albert", 119, 1.24f, aim, aim == 1 ? Hand.Left : Hand.Right, 0.42f, 0.75f, moving: true, trace: Console.Out);
            Console.WriteLine($"  standing {t.Standing} removed {t.Removed}");
        }
    }

    [TestMethod]
    public void ScriptedStrokeTrace()
    {
        foreach (var sweep in new[] { false, true })
        {
            foreach (var gap in new[] { 0.74f, 0.79f, 0.84f, 0.89f, 0.94f })
            {
                var t = MiniRing.Arena.ClawLab.Run("ramon", 77, gap, 2, Hand.Right, 0.31f, 0.75f, moving: true, sweep: sweep);
                Console.WriteLine($"sweep {sweep} gap {gap}: standing {t.Standing} removed {t.Removed} tip {t.TipSpeed:0.0} plane {t.PlaneError * 100f:0.0} cm");
            }
        }
    }

    [TestMethod]
    public void IdleStanceTrace()
    {
        var c = CharacterRoster.Get("galva");
        using var game = new BoxingGame(corners: BotCatalog.Corners("boxer") with
        {
            PlayerBody = c.Build, PlayerWear = c.Wear, PlayerLook = c.Look, PlayerFaceSpec = c.Face,
            Ring = RingLayout.Boxing with { StartGap = 3f, FromCorners = false }, ExternalBlue = true, Referee = false,
        });
        var f = game.Player;
        FighterCommand Guard(Fighter x) => new(Vector3.Zero, Vector3.UnitZ, new HandCommand(AiControl.Guard(Hand.Left, x.ArmReach), false), new HandCommand(AiControl.Guard(Hand.Right, x.ArmReach), false));
        for (var i = 0; i < 480; i++)
        {
            game.Step(new BoxingInput(Guard(f), Blue: Guard(game.Cpu) with { FaceDirection = -Vector3.UnitZ }), 1f / 240f);
            if (i % 60 == 59)
            {
                var up = Vector3.Transform(Vector3.UnitY, game.World.GetState(f.Parts.Pelvis).Orientation);
                var fwd = Vector3.Transform(Vector3.UnitZ, game.World.GetState(f.Parts.Pelvis).Orientation);
                var toes = string.Join(" ", f.AllParts.Where(id => f.KindOf(id) == BodyPartKind.Toe).Select(id => game.World.GetState(id).Position.Y.ToString("0.00", System.Globalization.CultureInfo.InvariantCulture)));
                var span = f.SupportHull.Count == 0 ? 0f : f.SupportHull.Max(v => v.Y) - f.SupportHull.Min(v => v.Y);
                Console.WriteLine($"t={(i + 1) / 240f:0.00} pelvisY={f.PelvisPosition.Y:0.00} up={up:0.00} fwd={fwd:0.00} hull={f.SupportHull.Count} span={span:0.00} toesY={toes}");
            }
        }
    }

    [TestMethod]
    [DataRow(0.57f)]
    [DataRow(0.62f)]
    [DataRow(0.67f)]
    [DataRow(0.72f)]
    public void FrontLegStrikeTrace(float gap)
    {
        var c = CharacterRoster.Get("galva");
        using var game = new BoxingGame(corners: BotCatalog.Corners("boxer") with
        {
            PlayerBody = c.Build, PlayerWear = c.Wear, PlayerLook = c.Look, PlayerFaceSpec = c.Face,
            Ring = RingLayout.Boxing with { StartGap = gap, FromCorners = false }, ExternalBlue = true, Referee = false,
        });
        var f = game.Player;
        FighterCommand Guard(Fighter x) => new(Vector3.Zero, Vector3.UnitZ, new HandCommand(AiControl.Guard(Hand.Left, x.ArmReach), false), new HandCommand(AiControl.Guard(Hand.Right, x.ArmReach), false));
        void Step(FighterCommand command) => game.Step(new BoxingInput(command, Blue: Guard(game.Cpu) with { FaceDirection = -Vector3.UnitZ }), 1f / 240f);
        for (var i = 0; i < 240; i++)
        {
            Step(Guard(f));
        }

        var toe = f.ToeId((int)Hand.Right);
        var target = FrontLegStrike.Aim(game.PlayerSense);
        var start = f.PelvisPosition;
        var closest = float.MaxValue;
        var removedCells = 0;
        for (var i = 0; i < 173; i++)
        {
            var sense = game.PlayerSense;
            Step(Guard(f) with { RightLeg = FrontLegStrike.At(sense.SelfLeg(Hand.Right), sense.Self.WorldToLocal(target), i / 172f, false, Hand.Right) });
            var d = Vector3.Distance(game.World.GetState(toe).Position, target);
            closest = MathF.Min(closest, d);
            removedCells += game.RecentCuts.Where(x => x.A.BodyId == toe && x.B.BodyId != toe).Sum(x => x.RemovedFor(x.B).Count);
            if (i % 20 == 0)
            {
                var up = Vector3.Transform(Vector3.UnitY, game.World.GetState(f.Parts.Pelvis).Orientation);
                Console.WriteLine($"i={i} toe-target={d:0.00} pelvis moved={(f.PelvisPosition - start):0.00} up={up:0.00} cuts={game.RecentCuts.Count(x => x.A.BodyId == toe)}");
            }
        }

        Console.WriteLine($"gap {gap}: closest {closest:0.000} m removed {removedCells}");
    }

    [TestMethod]
    public void LegStoreTrace()
    {
        var galva = CharacterRoster.Get("galva");
        foreach (var four in new[] { true, false })
        {
            var build = galva.Build with { FourLegged = four };
            using var game = new BoxingGame(corners: BotCatalog.Corners("boxer") with
            {
                PlayerBody = build, PlayerWear = galva.Wear, PlayerLook = galva.Look, PlayerFaceSpec = galva.Face,
                Ring = RingLayout.Boxing with { StartGap = 3f, FromCorners = false }, ExternalBlue = true, Referee = false,
            });
            var f = game.Player;
            var legMass = f.AllParts.Where(id => f.KindOf(id) is BodyPartKind.Thigh or BodyPartKind.Shin or BodyPartKind.Foot or BodyPartKind.Toe or BodyPartKind.Coxa)
                .Select(id => (f.KindOf(id), game.World.GetMass(id))).ToArray();
            game.Step(new BoxingInput(new FighterCommand(Vector3.Zero, Vector3.UnitZ, new HandCommand(Vector3.Zero, false), new HandCommand(Vector3.Zero, false))), 1f / 240f);
            Console.WriteLine($"four {four}: parts {legMass.Length} total {legMass.Sum(x => x.Item2):0.00} kg [{string.Join(", ", legMass.Select(x => $"{x.Item1}:{x.Item2:0.00}"))}] capacity L {f.OxygenBudgetOf(MuscleRegion.LeftLeg).Capacity:0} tissue {MiniRing.Voxels.TissuePhysiology.Of(MiniRing.Voxels.BoxerBody.PaletteFor(build)).OxygenStore}");
        }
    }

    private static IEnumerable<(string, Func<float, ClawSegment>)> Targets()
    {
        yield return ("guard", r => ClawStroke.Guard(Hand.Right, r));
        yield return ("level forward", r => Seg(new Vector3(-0.5f, 0.1f, 0.4f) * r, Vector3.UnitZ, Vector3.UnitY));
        yield return ("level across", r => Seg(new Vector3(-0.6f, 0.3f, 0.7f) * r, Vector3.UnitX, Vector3.UnitY));
        yield return ("up", r => Seg(new Vector3(-0.5f, 0.3f, 0.4f) * r, Vector3.Normalize(new Vector3(0f, 1f, 0.3f)), Vector3.UnitZ));
        yield return ("level fwd, edge down", r => Seg(new Vector3(-0.5f, 0.1f, 0.4f) * r, Vector3.UnitZ, Vector3.UnitX));
    }

    private static ClawSegment Seg(Vector3 root, Vector3 axis, Vector3 normal) => new(root, root + axis * ClawSegment.Length, normal);

    private static (float Angle, float Root, float WantBend, float GotBend, Vector3 Hand) Hold(FighterTuning tuning, Func<float, ClawSegment> make, out float reach)
    {
        var creature = CharacterRoster.Get("galva");
        var target = CharacterRoster.Get("marcus");
        var corners = BotCatalog.Corners("boxer") with
        {
            PlayerBody = creature.Build, PlayerWear = creature.Wear, PlayerLook = creature.Look, PlayerFaceSpec = creature.Face,
            CpuBody = target.Build, CpuWear = HandWear.None, CpuLook = target.Look, CpuFaceSpec = target.Face,
            ExternalBlue = true, Referee = false, Ring = RingLayout.Boxing with { StartGap = 4f, FromCorners = false },
        };
        using var game = new BoxingGame(tuning, seed: 1, corners: corners);
        var f = game.Player;
        reach = f.ArmReach;
        var hand = Hand.Right;
        var want = make(f.ArmReach);
        var guard = new FighterCommand(Vector3.Zero, Vector3.UnitZ, ClawStroke.Guard(Hand.Left, f.ArmReach).ToCommand(Hand.Left),
            want.ToCommand(hand), ClawBot.Stance(f.ArmReach));
        var blue = new FighterCommand(Vector3.Zero, -Vector3.UnitZ, new HandCommand(AiControl.Guard(Hand.Left, 0.7f), false), new HandCommand(AiControl.Guard(Hand.Right, 0.7f), false));
        for (var i = 0; i < 240; i++)
        {
            game.Step(new BoxingInput(guard, Blue: blue), 1f / 240f);
        }

        var claw = game.GloveOf(f, hand)!;
        var state = game.World.GetState(claw.Body.BodyId);
        var self = game.PlayerSense.Self;
        var root = self.WorldToLocal(state.Position + Vector3.Transform(Weapon.GripOf(claw.Kind), state.Orientation));
        var tip = self.WorldToLocal(state.Position + Vector3.Transform(Weapon.GripOf(claw.Kind) + SickleClawShape.Edge(hand), state.Orientation));
        var a = Vector3.Normalize(want.Tip - want.Root);
        var b = Vector3.Normalize(tip - root);
        // 手首の曲げ: 前腕の軸（肘から手へ、ローカルの X × 腕の側。右手は −X 側の腕）と刃の弦の角。目標の弦と、いまの弦の両方。
        var forearm = game.World.GetState(f.ForearmId(hand));
        var side = hand == Hand.Right ? -1f : 1f;
        var axisWorld = Vector3.Transform(Vector3.UnitX * side, forearm.Orientation);
        var forearmLocal = Vector3.Normalize(self.WorldToLocal(forearm.Position + axisWorld) - self.WorldToLocal(forearm.Position));
        static float Deg(Vector3 x, Vector3 y) => MathF.Acos(Math.Clamp(Vector3.Dot(x, y), -1f, 1f)) * 180f / MathF.PI;
        var desired = Quaternion.Concatenate(want.ToCommand(hand).Orientation!.Value, self.Rotation);
        var residual = 2f * MathF.Acos(Math.Clamp(MathF.Abs(Quaternion.Dot(Quaternion.Normalize(state.Orientation), Quaternion.Normalize(desired))), 0f, 1f)) * 180f / MathF.PI;
        var chordFromDesired = Vector3.Normalize(Vector3.Transform(SickleClawShape.Edge(hand), desired));
        var chordWant = Vector3.Normalize(self.LocalToWorld(want.Tip) - self.LocalToWorld(want.Root));
        Console.WriteLine($"    servo residual {residual:0.0} deg; command chord vs wanted chord {Deg(chordFromDesired, chordWant):0.0} deg; claw spin {state.AngularVelocity.Length():0.00} rad/s, forearm {forearm.AngularVelocity.Length():0.00}, chest {game.World.GetState(f.Parts.Chest).AngularVelocity.Length():0.00}, pelvis {game.World.GetState(f.Parts.Pelvis).AngularVelocity.Length():0.00}, pelvis speed {game.World.GetState(f.Parts.Pelvis).LinearVelocity.Length():0.00} m/s");
        return (Deg(a, b), Vector3.Distance(root, want.Root), Deg(a, forearmLocal), Deg(b, forearmLocal), root / f.ArmReach);
    }
}
