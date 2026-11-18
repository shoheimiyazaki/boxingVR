using System.Globalization;
using System.Numerics;
using MiniRing.Arena;
using MiniRing.Bots;

namespace MiniRing.Tests;

/// <summary>調べもの: 蹴り始めの前の、蹴る人・受ける人の足の構え（留めあり・なし）。</summary>
[TestClass]
public sealed class KickStanceProbe
{
    public TestContext TestContext { get; set; } = null!;

    private static string Stance(Fighter f)
    {
        var yaw = f.Frame.YawRadians;
        var (s, c) = MathF.SinCos(yaw);
        string Rel(Vector3 p)
        {
            var d = p - f.PelvisPosition;
            return string.Create(CultureInfo.InvariantCulture, $"({((d.X * c) - (d.Z * s)) * 100f:0.0},{((d.X * s) + (d.Z * c)) * 100f:0.0})");
        }

        return string.Create(CultureInfo.InvariantCulture, $"pelvisY={f.PelvisPosition.Y * 100f:0.0} yaw={yaw * 57.3f:0.0} footL(lat,fwd)cm={Rel(f.AnklePosition(Hand.Left))} footR={Rel(f.AnklePosition(Hand.Right))} chest={Rel(f.ChestPosition)} steps={f.Footwork.Steps}");
    }

    private void Run(string label, float settle, int seed, string victimId)
    {
        var h = KickShin.Heights[0];
        var tune = KickSpec.Of(h.Technique).TuneFor(Hand.Right)!;
        var spec = KickSpec.Of(h.Technique) with { Tunes = new KickTunes(tune, tune) };
        var tuning = FighterTuning.Default with { Footwork = FighterTuning.Default.Footwork with { HoldSettleSeconds = settle } };
        var victimBody = victimId == "standard" ? BodyBuild.Standard : Characters.CharacterRoster.Get(victimId).Build;
        using var game = new BoxingGame(tuning, seed: seed, corners: new BoxingCorners("still", _ => new StandGuard(), CpuBody: victimBody));
        var attacker = game.Player;
        var victim = game.Cpu;
        var reach = attacker.ArmReach;
        const float dt = 1f / 240f;
        var side = Hand.Right;
        var standoff = tune.Standoff;
        FighterCommand Command(Vector3 move, LegCommand? leg, float yaw, float twist)
        {
            var face = Vector3.Transform(victim.ChestPosition - attacker.ChestPosition, Quaternion.CreateFromAxisAngle(Vector3.UnitY, yaw));
            return new FighterCommand(move, face, new HandCommand(AiControl.Guard(Hand.Left, reach), false), new HandCommand(AiControl.Guard(Hand.Right, reach), false),
                TwistBias: twist, LeftLeg: side == Hand.Left ? leg : null, RightLeg: side == Hand.Right ? leg : null);
        }

        float Distance() => Vector3.Distance(attacker.ChestPosition with { Y = 0f }, victim.ChestPosition with { Y = 0f }) / reach;
        var stable = 0;
        var approach = 0;
        for (var i = 0; i < 240 * 10 && stable < 90; i++)
        {
            approach = i;
            var error = Distance() - standoff;
            if (MathF.Abs(error) < 0.03f)
            {
                stable++;
                game.Step(new BoxingInput(Command(Vector3.Zero, null, 0f, 0f)), dt);
                continue;
            }

            stable = 0;
            var toward = Vector3.Normalize((victim.ChestPosition - attacker.ChestPosition) with { Y = 0f });
            game.Step(new BoxingInput(Command(toward * MathF.Sign(error) * Math.Clamp(MathF.Abs(error) * 4f, 0.25f, 1f), null, 0f, 0f)), dt);
        }

        for (var i = 0; i < 360; i++)
        {
            game.Step(new BoxingInput(Command(Vector3.Zero, null, 0f, 0f)), dt);
        }

        var aimWorld = spec.AimWorld(game.PlayerSense);
        var rest = new Vector3(side.LocalSide() * 0.2f * reach, -1.4f * reach, 0f);
        TestContext.WriteLine($"[{label} seed {seed} {victimId}] approach {approach / 240f:0.00}s dist {Distance():0.000} (standoff {standoff:0.000})");
        TestContext.WriteLine($"   attacker {Stance(attacker)}");
        TestContext.WriteLine($"   victim   {Stance(victim)}");
        var fraction = float.NaN;
        for (var i = 0; i < (int)((tune.Seconds + 0.3f) / dt); i++)
        {
            var t = i * dt;
            var sense = game.PlayerSense;
            LegCommand? leg = t <= tune.Seconds ? spec.Evaluate(side, t, tune.Seconds, rest, sense.Self.WorldToLocal(aimWorld), reach, sense.SelfLeg(side), Hand.Left) : null;
            game.Step(new BoxingInput(Command(Vector3.Zero, leg, t <= tune.Seconds ? spec.FaceYaw(side, t, tune.Seconds) : 0f, t <= tune.Seconds ? spec.TorsoTwist(side, t, tune.Seconds) : 0f)), dt);
            if (float.IsNaN(fraction))
            {
                foreach (var hit in game.RecentHits)
                {
                    if (hit.Kick && hit.Attacker == attacker)
                    {
                        var knee = attacker.KneePosition(side);
                        var axis = attacker.AnklePosition(side) - knee;
                        fraction = Vector3.Dot(hit.Point - knee, axis) / MathF.Max(1e-6f, axis.LengthSquared());
                        TestContext.WriteLine($"   hit t={t:0.000} fraction={fraction:0.00} zone={hit.Zone} aimToVictimChest={Vector3.Distance(aimWorld, victim.ChestPosition) * 100f:0.0}cm aimY={aimWorld.Y * 100f:0.0} hitY={hit.Point.Y * 100f:0.0}");
                        break;
                    }
                }
            }
        }
    }

    [TestMethod]
    public void Compare()
    {
        foreach (var c in KickShin.TuneConditions.Take(3))
        {
            foreach (var (label, settle) in new[] { ("NOHOLD", 1e6f), ("HOLD", 1f) })
            {
                Run(label, settle, c.Seed, c.Victim);
            }
        }
    }
}
