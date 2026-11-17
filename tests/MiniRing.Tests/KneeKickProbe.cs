using System.Numerics;
using MiniRing.Bots;

namespace MiniRing.Tests;

/// <summary>調査用（通常の実行から外す）: 回し蹴りの膝の経路を、間合いごとに動かして軌跡と当たりを出す。</summary>
[TestClass]
public sealed class KneeKickProbe
{
    private const float Dt = 1f / 240f;

    [TestMethod]
    public void RoundhouseTrajectory()
    {
        foreach (var standoff in new[] { 1.2f })
        {
            using var game = new BoxingGame(seed: 3, corners: new BoxingCorners("still", _ => new StandGuard(), PlayerBody: MiniRing.Characters.CharacterRoster.Get("taeyun").Build));
            var a = game.Player;
            var v = game.Cpu;
            var side = Hand.Right;
            FighterCommand Cmd(Vector3 move, LegCommand? leg, float yaw, float twist)
            {
                var face = Vector3.Transform(v.ChestPosition - a.ChestPosition, Quaternion.CreateFromAxisAngle(Vector3.UnitY, yaw));
                return new FighterCommand(move, face, new HandCommand(AiControl.Guard(Hand.Left, a.ArmReach), false), new HandCommand(AiControl.Guard(Hand.Right, a.ArmReach), false),
                    TwistBias: twist, RightLeg: leg);
            }

            for (var i = 0; i < 240 * 8; i++)
            {
                var d = Vector3.Distance(a.ChestPosition with { Y = 0f }, v.ChestPosition with { Y = 0f });
                if (d < standoff * a.ArmReach)
                {
                    break;
                }

                var toward = Vector3.Normalize((v.ChestPosition - a.ChestPosition) with { Y = 0f });
                game.Step(new BoxingInput(Cmd(toward, null, 0f, 0f)), Dt);
            }

            for (var i = 0; i < 120; i++)
            {
                game.Step(new BoxingInput(Cmd(Vector3.Zero, null, 0f, 0f)), Dt);
            }

            var spec = KickSpec.Of(KickTechnique.Roundhouse);
            var reach = a.ArmReach;
            var sense = game.PlayerSense;
            var aimWorld = spec.AimWorld(sense);
            var rest = new Vector3(side.LocalSide() * 0.2f * reach, -1.4f * reach, 0f);
            var duration = 0.9f;
            Console.WriteLine($"--- standoff {standoff}: chest distance {Vector3.Distance(a.ChestPosition with { Y = 0f }, v.ChestPosition with { Y = 0f }) / reach:F2} reach");
            var hits = new HashSet<HitEvent>();
            var minShin = float.MaxValue;
            for (var i = 0; i < (int)((duration + 0.4f) / Dt); i++)
            {
                var t = i * Dt;
                var yaw = t <= duration ? spec.FaceYaw(side, t, duration) : 0f;
                var twist = t <= duration ? spec.TorsoTwist(side, t, duration) : 0f;
                var aim = game.PlayerSense.Self.WorldToLocal(aimWorld);
                var leg = t <= duration ? spec.Evaluate(side, t, duration, rest, aim, reach, game.PlayerSense.SelfLeg(side)) : (LegCommand?)null;
                game.Step(new BoxingInput(Cmd(Vector3.Zero, leg, yaw, twist)), Dt);
                var knee = a.KneePosition(side);
                var ankle = a.AnklePosition(side);
                var mid = Vector3.Lerp(knee, ankle, 0.75f);
                minShin = MathF.Min(minShin, Vector3.Distance(mid, aimWorld));
                if (i % 24 == 0 && leg is { } l)
                {
                    var frame = a.Frame;
                    Console.WriteLine($"t {t:F2} knee err {Vector3.Distance(knee, frame.LocalToWorld(l.KneeLocal!.Value)):F3} bend {a.KneeBend(side):F2} (cmd {(l.KneeBend.HasValue ? $"{l.KneeBend.Value:F2}" : "free")}) twist {a.ThighTwist(side):F2} (cmd {(l.KneeTwist.HasValue ? $"{l.KneeTwist.Value:F2}" : "free")}) shin75->aim {Vector3.Distance(mid, aimWorld) / reach:F2} reach  ankle h {ankle.Y - a.PelvisPosition.Y:F3}");
                }

                if (game.LastHit is { Kick: true } hit && hits.Add(hit))
                {
                    var axis = ankle - knee;
                    var frac = Vector3.Dot(hit.Point - knee, axis) / axis.LengthSquared();
                    Console.WriteLine($"HIT t {t:F2} part {hit.KickerPart} shin fraction {frac:F2} speed {hit.Speed:F2} mass {hit.ImpactMass:F2} energy {hit.Energy:F3}");
                }
            }

            Console.WriteLine($"min shin75 to aim {minShin / reach:F2} reach, hits {hits.Count}");
        }
    }
}
