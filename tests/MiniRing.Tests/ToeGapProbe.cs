using System.Numerics;
using MiniRing.Arena;
using Aigis.Physics;
using MiniRing.Bots;

namespace MiniRing.Tests;

/// <summary>
/// 調査用（2026-11-25、ユーザーの指摘「つま先がかかとから離れる。蹴りで大きな隙間ができる」）: 蹴りの間の、つま先とかかと（足のかかと側の箱）の
/// つなぎ目。蝶番の点（母指球: かかと側の前の下の縁 ↔ つま先の後ろの下の縁）のずれ、上の縁（かかと側の前の上の縁 ↔ つま先の後ろの上の縁）の開き、
/// つま先の曲がり（かかとに対する回り）。蹴る脚と軸足の両方。
/// </summary>
[TestClass]
[TestCategory("Probe")]
public sealed class ToeGapProbe
{
    internal sealed record Gap(float PinMax, float TopMax, float BendMax, float BendMin, float TimeOfTop);

    private static float Pitch(Quaternion heel, Quaternion toe)
    {
        // かかとのローカルで見たつま先の前の向き（+Z）の、上下（Y）への傾き。正 = つま先が上へ反る。
        var forward = Vector3.Transform(Vector3.Transform(Vector3.UnitZ, toe), Quaternion.Conjugate(heel));
        return MathF.Atan2(forward.Y, forward.Z) * 180f / MathF.PI;
    }

    internal static Gap Measure(BoxingGame game, int foot, Gap? previous, float time)
    {
        var heelHalf = game.Tuning.HalfExtents.Foot;
        var toeHalf = FootGeometry.ToeHalf(heelHalf);
        var me = game.Player;
        var h = game.World.GetState(me.HeelId(foot));
        var t = game.World.GetState(me.ToeId(foot));
        var pinHeel = h.Position + Vector3.Transform(FootGeometry.BallOnHeel(heelHalf), h.Orientation);
        var pinToe = t.Position + Vector3.Transform(FootGeometry.BallOnToe(heelHalf), t.Orientation);
        var topHeel = h.Position + Vector3.Transform(new Vector3(0f, heelHalf.Y, heelHalf.Z), h.Orientation);
        var topToe = t.Position + Vector3.Transform(new Vector3(0f, toeHalf.Y, -toeHalf.Z), t.Orientation);
        var pin = Vector3.Distance(pinHeel, pinToe);
        var top = Vector3.Distance(topHeel, topToe);
        var bend = Pitch(h.Orientation, t.Orientation);
        var p = previous ?? new Gap(0f, 0f, float.MinValue, float.MaxValue, 0f);
        return new Gap(MathF.Max(p.PinMax, pin), MathF.Max(p.TopMax, top), MathF.Max(p.BendMax, bend), MathF.Min(p.BendMin, bend), top > p.TopMax ? time : p.TimeOfTop);
    }

    private static readonly float[] Hinges = [45f];


    [TestMethod]
    [TestCategory("Probe")]
    public void KickGaps()
    {
        Console.WriteLine("| つま先の蝶番 | 蹴り | 脚 | 足 | 蝶番の点のずれ最大 [mm] | 上の縁の開き最大 [mm]（時刻） | つま先の曲がり 最大 / 最小 [°] |");
        Console.WriteLine("|---|---|---|---|---|---|---|");
        foreach (var (hz, h) in Hinges.SelectMany(hz => KickShin.Heights.Select(h => (hz, h))))
        {
            foreach (var rear in new[] { true, false })
            {
                var side = rear ? Hand.Right : Hand.Left;
                var tunes = KickTunes.Load(h.File);
                var tune = rear ? tunes.Rear : tunes.Front;
                var spec = KickSpec.Of(h.Technique) with { Tunes = new KickTunes(tune, tune) };
                Gap? kick = null, support = null;
                var ankle = 0f;
                var knee = 0f;
                var hip = 0f;
                var kickFoot = -1;
                KickMocap.Doll(Characters.CharacterRoster.Get("taeyun").Build, spec, side, tune.Standoff, tune.Seconds, 1, (game, _, time) =>
                {
                    if (hz != game.Tuning.ToeHingeHz)
                    {
                        throw new InvalidOperationException("tuning not applied");
                    }

                    if (kickFoot < 0)
                    {
                        // 蹴る脚の足の運びの番号: 右（後ろの手の側）で蹴れば後ろの足。
                        kickFoot = rear ? Footwork.Rear : Footwork.Lead;
                    }

                    kick = Measure(game, kickFoot, kick, time);
                    support = Measure(game, 1 - kickFoot, support, time);
                    ankle = MathF.Max(ankle, MathF.Max(game.Player.JointGap(HumanoidRagdoll.RagdollJoint.LeftAnkle) ?? 0f, game.Player.JointGap(HumanoidRagdoll.RagdollJoint.RightAnkle) ?? 0f));
                    knee = MathF.Max(knee, MathF.Max(game.Player.JointGap(HumanoidRagdoll.RagdollJoint.LeftKnee) ?? 0f, game.Player.JointGap(HumanoidRagdoll.RagdollJoint.RightKnee) ?? 0f));
                    hip = MathF.Max(hip, MathF.Max(game.Player.JointGap(HumanoidRagdoll.RagdollJoint.LeftHip) ?? 0f, game.Player.JointGap(HumanoidRagdoll.RagdollJoint.RightHip) ?? 0f));
                }, tuning: FighterTuning.Default with { ToeHingeHz = hz });
                foreach (var (label, g) in new[] { ("蹴る足", kick!), ("軸足", support!) })
                {
                    Console.WriteLine($"| {hz:0} Hz | {h.Name} | {(rear ? "後ろ" : "前")} | {label} | {g.PinMax * 1000f:0.0} | {g.TopMax * 1000f:0.0}（{g.TimeOfTop:0.00} s） | {g.BendMax:0} / {g.BendMin:0} | 足首・膝・股の隙間最大 {ankle * 1000f:0}・{knee * 1000f:0}・{hip * 1000f:0} mm |");
                }
            }
        }

        var heel = FighterTuning.Default.HalfExtents.Foot;
        Console.WriteLine($"かかと側の箱の半分 {heel * 1000f} mm、つま先の箱の半分 {FootGeometry.ToeHalf(heel) * 1000f} mm（立って足が平らなとき上の縁の開きは {(heel.Y - FootGeometry.ToeHalf(heel).Y) * 2000f / 2f:0.0} mm の段差）");
    }

    [TestMethod]
    [TestCategory("Probe")]
    public void MidFrontTrace()
    {
        var h = KickShin.Heights.First(x => x.File == "roundhouse_high");
        var tune = KickTunes.Load(h.File).Rear;
        var spec = KickSpec.Of(h.Technique) with { Tunes = new KickTunes(tune, tune) };
        Vector3? lastHeel = null, lastShin = null, lastToe = null;
        KickMocap.Doll(Characters.CharacterRoster.Get("taeyun").Build, spec, Hand.Right, tune.Standoff, tune.Seconds, 1, (game, _, time) =>
        {
            var me = game.Player;
            var foot = Footwork.Rear;
            var heel = game.World.GetState(me.HeelId(foot)).LinearVelocity;
            var toe = game.World.GetState(me.ToeId(foot)).LinearVelocity;
            var shinId = me.ShinIdOf(foot);
            var shin = game.World.GetState(shinId).LinearVelocity;
            var hits = game.RecentHits.Where(x => x.Kick && x.Attacker == me).ToList();
            if (time > 0.6f && time < 0.75f)
            {
                Console.WriteLine($"{time:0.000}s 足首 {(game.Player.JointGap(HumanoidRagdoll.RagdollJoint.RightAnkle) ?? 0f) * 1000f:0}/{(game.Player.JointGap(HumanoidRagdoll.RagdollJoint.LeftAnkle) ?? 0f) * 1000f:0} mm Δv かかと {(lastHeel is { } a ? (heel - a).Length() : 0f):0.00} すね {(lastShin is { } b ? (shin - b).Length() : 0f):0.00} つま先 {(lastToe is { } c ? (toe - c).Length() : 0f):0.00} m/s 当たり {string.Join(",", hits.Select(x => $"{x.KickerPart}→{x.Zone} p {x.Momentum:0.0}"))} 切り {game.RecentCuts.Count}");
            }

            lastHeel = heel;
            lastShin = shin;
            lastToe = toe;
        }, tuning: FighterTuning.Default with { ToeHingeHz = 45f });
    }

    [TestMethod]
    [TestCategory("Probe")]
    public void StandingStill()
    {
        foreach (var (hz, zeta) in new[] { (10.72f, 1f), (45f, 1f) })
        {
            using var game = new BoxingGame(FighterTuning.Default with { ToeHingeHz = hz }, seed: 1, corners: new BoxingCorners("still", _ => new StandGuard(), _ => new StandGuard()));
            var me = game.Player;
            var start = me.PelvisPosition with { Y = 0f };
            double toeSpin = 0, heelSpin = 0;
            var n = 0;
            for (var i = 0; i < 240 * 10; i++)
            {
                game.Step(default, 1f / 240f);
                if (i > 480)
                {
                    foreach (var foot in new[] { 0, 1 })
                    {
                        toeSpin += game.World.GetState(me.ToeId(foot)).AngularVelocity.LengthSquared();
                        heelSpin += game.World.GetState(me.HeelId(foot)).AngularVelocity.LengthSquared();
                        n++;
                    }
                }
            }

            Console.WriteLine($"つま先 {hz:0} Hz・減衰 {zeta:0}: 骨盤の流れ {(new Vector3(me.PelvisPosition.X, 0f, me.PelvisPosition.Z) - start).Length() * 100f:0.0} cm、踏み直し {me.Footwork.Steps}、つま先の回りの速さ RMS {Math.Sqrt(toeSpin / n):0.00} rad/s、かかと {Math.Sqrt(heelSpin / n):0.00} rad/s");
        }
    }

    [TestMethod]
    [TestCategory("Probe")]
    public void KnockedDownRecovery()
    {
        foreach (var hz in new[] { 10.72f, 45f })
        {
            var rows = new List<string>();
            foreach (var seed in new[] { 1, 2, 3, 4, 5, 6 })
            {
                var still = BotCatalog.Boxer with { Attacks = false, DefenseRate = 0f, Prediction = 0f, Aggression = 0f };
                using var game = new BoxingGame(FighterTuning.Default with { ToeHingeHz = hz }, seed: seed, corners: new BoxingCorners("still", s => new BoxerBot(still, s), s => new BoxerBot(still, s)));
                const float dt = 1f / 240f;
                for (var t = 0f; t < 2.518f; t += dt)
                {
                    game.Step(default, dt);
                }

                var cpu = game.Cpu;
                DamageModelTests.Hit(game, cpu, DamageModelTests.Chin(cpu), mass: 12f, speed: 3.692f);
                var peak = 0f;
                var upAt = -1f;
                var downAt = -1f;
                for (var t = 0f; t < 26.85f; t += dt)
                {
                    game.Step(default, dt);
                    peak = MathF.Max(peak, cpu.Concussion);
                    downAt = downAt < 0f && cpu.State == FighterState.Down ? t : downAt;
                    upAt = downAt >= 0f && upAt < 0f && cpu.State == FighterState.Standing ? t : upAt;
                }

                rows.Add($"種 {seed}: 脳しんとうの最大 {peak:0.00}・起き上がり {upAt:0.0} s");
            }

            Console.WriteLine($"つま先 {hz:0} Hz: " + string.Join(" / ", rows));
        }
    }
}
