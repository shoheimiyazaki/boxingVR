using System.Globalization;
using System.Numerics;
using MiniRing.Bots;
using MiniRing.Characters;
using MiniRing.Weapons;

namespace MiniRing.Arena;

internal static class ClawLab
{
    private const float Dt = 1f / 240f;
    internal readonly record struct Trial(float Drive, float Windup, string Opponent, Hand Hand, int Aim, int Removed, int Crushed, int Arm, int Neck, int Torso, int Hits, float TipSpeed, float PlaneError, bool Standing,
        bool HeadSeparated, bool ArmSeparated, bool TorsoSeparated)
    {
        public bool AimSeparated => Aim switch { 0 => HeadSeparated, 1 => ArmSeparated, _ => TorsoSeparated };
        public int AimRemoved => Aim switch { 0 => Neck, 1 => Arm, _ => Torso };
    }

    public static int Report(TextWriter log)
    {
        float[] times = [0.24f, 0.32f, 0.42f];
        float[] winds = [0.75f, 1.10f];
        var practice = new[] { ("jin", 13, 0.94f), ("marcus", 31, 1.12f) };
        var validation = new[] { ("ramon", 77, 0.99f), ("albert", 119, 1.24f) };
        var results = new List<(float Drive, float Windup, Trial[] Trials)>();
        foreach (var time in times)
        {
            foreach (var wind in winds)
            {
                var trials = RunSet(practice, time, wind);
                results.Add((time, wind, trials));
                Write(log, "practice", time, wind, trials);
            }
        }

        var best = results.OrderByDescending(r => r.Trials.Count(t => t.Standing && t.AimSeparated))
            .ThenByDescending(r => r.Trials.Where(t => t.Standing).Sum(t => t.AimRemoved)).First();
        Write(log, "holdout-baseline", 0.24f, 0.75f, RunSet(validation, 0.24f, 0.75f, moving: true));
        Write(log, "holdout-selected", best.Drive, best.Windup, RunSet(validation, best.Drive, best.Windup, moving: true));
        return 0;
    }

    private static Trial[] RunSet((string Opponent, int Seed, float Gap)[] cases, float drive, float windup, bool moving = false)
    {
        return [.. cases.SelectMany(c => Enumerable.Range(0, 3).Select(aim => Run(c.Opponent, c.Seed, c.Gap, aim,
            ((c.Seed / 10 + aim) & 1) == 0 ? Hand.Left : Hand.Right, drive, windup, moving)))];
    }

    private static void Write(TextWriter log, string set, float drive, float wind, Trial[] trials)
    {
        log.WriteLine($"  separated: head {trials.Count(t => t.HeadSeparated)}, arm {trials.Count(t => t.ArmSeparated)}, thorax {trials.Count(t => t.TorsoSeparated)}");
        log.WriteLine(string.Create(CultureInfo.InvariantCulture,
            $"{set,-17} {drive:F2} s  wind {wind:F2}  removed {trials.Sum(t => t.Removed),5}  cuts {trials.Count(t => t.Removed > 0)}/{trials.Length}  crush {trials.Sum(t => t.Crushed),4}  hits {trials.Sum(t => t.Hits),3}  tip {trials.Average(t => t.TipSpeed):F2} m/s  plane {100f * trials.Average(t => t.PlaneError):F1} cm  standing {trials.Count(t => t.Standing)}/{trials.Length}"));
        if (set.StartsWith("holdout", StringComparison.Ordinal))
        {
            foreach (var t in trials)
            {
                log.WriteLine(string.Create(CultureInfo.InvariantCulture,
                    $"  {t.Opponent,-8} {t.Hand,-5} aim {t.Aim} removed {t.Removed,4} (arm {t.Arm}, neck {t.Neck}, torso {t.Torso}) hits {t.Hits,2} tip {t.TipSpeed:F2} m/s separate {t.HeadSeparated}/{t.ArmSeparated}/{t.TorsoSeparated}"));
            }
        }
    }

    internal static Trial Run(string opponent, int seed, float gap, int aim, Hand hand, float drive, float windup, bool moving = false, bool sweep = true, TextWriter? trace = null)
    {
        var creature = CharacterRoster.Get("galva");
        var target = CharacterRoster.Get(opponent);
        var corners = BotCatalog.Corners("boxer") with
        {
            PlayerBody = creature.Build, PlayerWear = creature.Wear, PlayerLook = creature.Look, PlayerFaceSpec = creature.Face,
            CpuBody = target.Build, CpuWear = HandWear.Glove, CpuLook = target.Look, CpuFaceSpec = target.Face,
            ExternalBlue = true, Referee = false, Ring = RingLayout.Boxing with { StartGap = gap, FromCorners = false },
        };
        using var game = new BoxingGame(seed: seed, corners: corners);
        var f = game.Player;
        var other = game.Cpu;
        var left = ClawStroke.Guard(Hand.Left, f.ArmReach).ToCommand(Hand.Left);
        var right = ClawStroke.Guard(Hand.Right, f.ArmReach).ToCommand(Hand.Right);
        var guard = new FighterCommand(Vector3.Zero, Vector3.UnitZ, left, right, ClawBot.Stance(f.ArmReach));
        var blue = new FighterCommand(Vector3.Zero, -Vector3.UnitZ,
            new HandCommand(AiControl.Guard(Hand.Left, other.ArmReach), false),
            new HandCommand(AiControl.Guard(Hand.Right, other.ArmReach), false));
        for (var i = 0; i < 216; i++)
        {
            game.Step(new BoxingInput(guard, Blue: blue), Dt);
        }

        var targetGrids = other.Voxels!.Layers.ToDictionary(p => p.Body);
        var worldAim = ClawBot.Aim(game.PlayerSense, aim);
        var motion = new ClawMotion();
        var origin = game.PlayerSense.Self;
        var stroke = ClawStroke.Create(hand, f.ArmReach, origin.WorldToLocal(worldAim), sweep, drive, windup);
        var cutNormal = origin.Rotate(stroke.Normal);
        var cutOrigin = origin.LocalToWorld(stroke.Sample(stroke.CutStart).Root);
        var claw = game.GloveOf(f, hand)!;
        var localTip = Weapon.GripOf(claw.Kind) + SickleClawShape.Edge(hand);
        var peak = 0f;
        var trackingSamples = 0;
        var planeError = 0f;
        var madeContact = false;
        var hits = 0;
        var removed = 0;
        var crushed = 0;
        var arms = 0;
        var neck = 0;
        var torso = 0;
        for (var i = 0; i < (int)(240 * (drive + 0.68f)); i++)
        {
            var time = i * Dt;
            var state = game.World.GetState(claw.Body.BodyId);
            var offset = Vector3.Transform(localTip, state.Orientation);
            if (!madeContact && time >= stroke.CutStart && time <= stroke.CutEnd)
            {
                peak = MathF.Max(peak, (state.LinearVelocity + Vector3.Cross(state.AngularVelocity, offset)).Length());
                planeError += MathF.Abs(Vector3.Dot(state.Position + offset - cutOrigin, cutNormal));
                trackingSamples++;
            }
            var command = motion.Read(hand, i == 0, sweep, game.PlayerSense, worldAim, Dt, drive, windup);
            var opponentCommand = moving ? blue with { MoveWorld = Vector3.UnitX * ((seed & 2) == 0 ? 0.24f : -0.24f) } : blue;
            game.Step(new BoxingInput(hand == Hand.Left ? guard with { Left = command } : guard with { Right = command }, Blue: opponentCommand), Dt);
            foreach (var cut in game.RecentCuts)
            {
                var struck = cut.A == claw.Body ? cut.B : cut.B == claw.Body ? cut.A : null;
                if (struck is null || !targetGrids.TryGetValue(struck, out var part))
                {
                    continue;
                }

                madeContact |= time >= stroke.CutStart;
                var cells = cut.RemovedFor(struck);
                removed += cells.Count;
                crushed += cut.CrushedFor(struck).Count;
                switch (part.Kind)
                {
                    case BodyPartKind.UpperArm or BodyPartKind.Forearm:
                        arms += cells.Count;
                        break;
                    case BodyPartKind.Head:
                        neck += cells.Count(c => c.LocalCenter.Y < -0.60f * struck.Grid.HalfExtents.Y);
                        break;
                    case BodyPartKind.Chest or BodyPartKind.Abdomen or BodyPartKind.Pelvis:
                        torso += cells.Count;
                        break;
                }
            }
            hits += game.RecentHits.Count(h => h.Attacker == f && h.Hand == hand && !h.Kick);
            if (trace is not null && i % 24 == 0)
            {
                var chest = f.ChestPosition;
                var flat = other.ChestPosition - chest;
                var apart = MathF.Sqrt((flat.X * flat.X) + (flat.Z * flat.Z));
                trace.WriteLine(string.Create(CultureInfo.InvariantCulture,
                    $"  t={time:0.00} state={f.State} chestY={chest.Y:0.00} pelvisY={game.World.GetState(f.Parts.Pelvis).Position.Y:0.00} support={f.SupportedMass:0} removed={removed} clawSpeed={state.LinearVelocity.Length():0.0} gap={apart:0.00} pelvisSpin={game.World.GetState(f.Parts.Pelvis).AngularVelocity.Length():0.0}"));
            }
        }

        bool Separated(int id) => other.IsDetachedPart(id) && !other.HangsByFlesh(id);
        var headSeparated = Separated(other.Parts.Head);
        var armSeparated = Separated(other.Parts.LeftForearm) || Separated(other.Parts.RightForearm);
        var torsoSeparated = Separated(other.Parts.Pelvis);
        return new Trial(drive, windup, opponent, hand, aim, removed, crushed, arms, neck, torso, hits, peak, planeError / Math.Max(1, trackingSamples), f.State == FighterState.Standing,
            headSeparated, armSeparated, torsoSeparated);
    }
}
