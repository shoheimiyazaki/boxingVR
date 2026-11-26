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
        bool HeadSeparated, bool ArmSeparated, bool TorsoSeparated, float StaminaUsed = 0f)
    {
        public bool AimSeparated => Aim switch { 0 => HeadSeparated, 1 => ArmSeparated, _ => TorsoSeparated };
        public int AimRemoved => Aim switch { 0 => Neck, 1 => Arm, _ => Torso };
    }

    /// <summary>候補: 振りの時間 [s] と溜めの大きさ。</summary>
    internal readonly record struct Swing(float Drive, float Windup);

    /// <summary>条件: 相手・種・間合い・狙い（手は種と狙いから決まる）。</summary>
    internal readonly record struct Case(string Opponent, int Seed, float Gap, int Aim)
    {
        public Hand Hand => ((Seed / 10 + Aim) & 1) == 0 ? Hand.Left : Hand.Right;
    }

    /// <summary>今の基準の振り（ClawBot の既定）。</summary>
    public static readonly Swing Baseline = new(0.31f, 1f);

    /// <summary>練習で試す振りの時間 [s] と溜めの格子。</summary>
    private static readonly float[] DriveGrid = [0.24f, 0.32f, 0.42f];

    private static readonly float[] WindupGrid = [0.75f, 1.10f];

    /// <summary>
    /// 鎌の振りの練習（練習の共通の枠 <see cref="PracticeFrame{TCandidate, TCase}"/> の 1 つ目の移し替え）: 振りの時間 × 溜めの格子を練習の相手（止まった jin・marcus）で選び、
    /// 使わなかった相手（動く ramon・albert）で基準と比べ直す。<paramref name="natural"/> なら点から自然さの代価（<see cref="FormCost.OfNaturalness"/>）も引く（§C7）。
    /// <paramref name="cards"/> なら基準と選んだ振りの motioncard を media_tmp/motion/ に撮る。
    /// </summary>
    public static int Report(TextWriter log, bool natural = false, bool cards = false)
    {
        var practice = new[] { ("jin", 13, 0.94f), ("marcus", 31, 1.12f) };
        var validation = new[] { ("ramon", 77, 0.99f), ("albert", 119, 1.24f) };
        static Case[] Cases((string Opponent, int Seed, float Gap)[] set) => [.. set.SelectMany(c => Enumerable.Range(0, 3).Select(aim => new Case(c.Opponent, c.Seed, c.Gap, aim)))];
        Swing[] candidates = [.. DriveGrid.SelectMany(time => WindupGrid.Select(wind => new Swing(time, wind)))];
        var frame = new PracticeFrame<Swing, Case>
        {
            Tool = natural ? "clawlab-natural" : "clawlab",
            Question = natural ? "Galva claw swing time x windup, score minus form and naturalness cost, held-out moving opponents" : "Galva claw swing time x windup, score minus form cost, held-out moving opponents",
            Train = Cases(practice),
            HeldOut = Cases(validation),
            Score = (s, k, heldOut) => Score(s, k, heldOut, natural),
            Describe = s => string.Create(CultureInfo.InvariantCulture, $"drive {s.Drive:F2} s wind {s.Windup:F2}"),
            SeedOf = k => k.Seed * 10 + k.Aim,
            Card = cards ? (s, name) => CardOf(s, name) : null,
        };
        frame.Run(candidates, Baseline, log);
        return 0;
    }

    /// <summary>1 つの条件の点（練習なら止まった相手、held-out なら動く相手）。</summary>
    internal static double Score(Swing s, Case k, bool heldOut, bool natural)
    {
        var trace = natural ? new MotionTools.MotionTrace("claw", "red", k.Seed, 1f, 1f) : null;
        var trial = Run(k.Opponent, k.Seed, k.Gap, k.Aim, k.Hand, s.Drive, s.Windup, heldOut, afterStep: trace is null ? null : (game, t) => trace.Record(game, game.Player, t));
        var score = TrialScore(trial);
        if (trace is not null && trial.Standing && trace.Frames.Count > 8)
        {
            var n = MotionTools.MotionWords.Natural(trace, trace.Frames[0].Time, trace.Frames[^1].Time, k.Hand == Hand.Left ? "tipL" : "tip");
            score -= FormCost.OfNaturalness(n.ChainOrder, n.Jerk);
        }

        return score;
    }

    private static void CardOf(Swing s, string name)
    {
        var k = new Case("ramon", 77, 0.99f, 0);
        var trace = new MotionTools.MotionTrace(string.Create(CultureInfo.InvariantCulture, $"claw galva drive {s.Drive:F2} wind {s.Windup:F2} vs ramon (held-out)"), "red", k.Seed, 1f, 1f);
        Run(k.Opponent, k.Seed, k.Gap, k.Aim, k.Hand, s.Drive, s.Windup, moving: true, afterStep: (game, t) => trace.Record(game, game.Player, t));
        var path = Path.Combine("media_tmp", "motion", name + ".motion.json");
        trace.Save(path, MotionTools.MotionAudit.Run(trace), MotionTools.MotionWords.Captions(trace), new Dictionary<string, string> { ["command"] = "clawlab --cards" });
        MotionTools.MotionCommand.Card(path, 0);
    }

    /// <summary>
    /// 練習の点（2026-11-25 から形の代価を引く。docs/plans/FORM_DECAY_STUDY.md）: 立っていた試行の、削ったセル / 60（剣のドリルの 1 点 = 60 セル: <c>StrikeResult.Effect</c>）
    /// − <see cref="FormCost.Of"/>（刃が狙った切断面から外れた距離を「追えなかった距離」、使ったスタミナ）。それまでは削ったセルだけで、面から 20 cm 外れて叩きつける
    /// 短い振り抜きが選ばれた。
    /// </summary>
    private static double TrialScore(Trial t) => !t.Standing ? 0d : (t.Removed / 60d) - FormCost.Of(0d, 100d * t.PlaneError, 0d, t.StaminaUsed);

    internal static Trial Run(string opponent, int seed, float gap, int aim, Hand hand, float drive, float windup, bool moving = false, bool sweep = true, Action<BoxingGame, float>? afterStep = null)
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
        var staminaStart = f.Stamina;
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
            afterStep?.Invoke(game, time);
        }

        bool Separated(int id) => other.IsDetachedPart(id) && !other.HangsByFlesh(id);
        var headSeparated = Separated(other.Parts.Head);
        var armSeparated = Separated(other.Parts.LeftForearm) || Separated(other.Parts.RightForearm);
        var torsoSeparated = Separated(other.Parts.Pelvis);
        return new Trial(drive, windup, opponent, hand, aim, removed, crushed, arms, neck, torso, hits, peak, planeError / Math.Max(1, trackingSamples), f.State == FighterState.Standing,
            headSeparated, armSeparated, torsoSeparated, staminaStart - f.Stamina);
    }
}
