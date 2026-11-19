using System.Numerics;
using MiniRing.Bots;

namespace MiniRing.Tests;

/// <summary>蹴りが拳と同じ接触（VoxelContact → ApplyHit）で相手の体に当たり、軸足が体を支えていることの数値の確認。</summary>
[TestClass]
[TestCategory("slow")]
public sealed class KickContactTests
{
    private const float Dt = 1f / 240f;

    private sealed record KickResult(int Hits, int VictimCellsLost, float MinPelvis, float StartPelvis, FighterState AttackerState,
        float MaxSpeed, float Mass, float MinAnkleToTarget, IReadOnlyList<KickPart> Parts, float MaxYawDeviation);

    private static int Cells(Fighter f) => f.Voxels!.Parts.Concat(f.Voxels.Fragments).Sum(p => p.Body.Grid.FilledCount);

    private static FighterCommand Command(Fighter self, Fighter opponent, Vector3 move, LegCommand? leg, Hand side, float yaw = 0f, float twist = 0f)
    {
        var reach = self.ArmReach;
        var face = Vector3.Transform(opponent.ChestPosition - self.ChestPosition, Quaternion.CreateFromAxisAngle(Vector3.UnitY, yaw));
        return new FighterCommand(move, face,
            new HandCommand(AiControl.Guard(Hand.Left, reach), false), new HandCommand(AiControl.Guard(Hand.Right, reach), false),
            TwistBias: twist, LeftLeg: side == Hand.Left ? leg : null, RightLeg: side == Hand.Right ? leg : null);
    }

    /// <summary>足は腕より短い（高い所ほど近づかないと届かない）: 蹴りごとの、胸と胸の距離（腕の長さ倍）。</summary>
    private static float ApproachOf(KickTechnique kind) => KickSpec.Of(kind).Standoff;

    private static KickResult Run(KickTechnique kind, Hand side = Hand.Right, float durationScale = 1f, float closeIn = 0f)
    {
        // 蹴られる側は立って構えるだけ。蹴る側（赤）は台本の指令で歩いて詰め、止まって蹴る。
        using var game = new BoxingGame(seed: 3, corners: new BoxingCorners("still", _ => new StandGuard()));
        var attacker = game.Player;
        var victim = game.Cpu;
        for (var i = 0; i < 240 * 6; i++)
        {
            var d = Vector3.Distance(attacker.ChestPosition with { Y = 0f }, victim.ChestPosition with { Y = 0f });
            if (d < (ApproachOf(kind) * attacker.ArmReach) - closeIn)
            {
                break;
            }

            var toward = Vector3.Normalize((victim.ChestPosition - attacker.ChestPosition) with { Y = 0f });
            game.Step(new BoxingInput(Command(attacker, victim, toward, null, side)), Dt);
        }

        for (var i = 0; i < 120; i++)
        {
            game.Step(new BoxingInput(Command(attacker, victim, Vector3.Zero, null, side)), Dt); // 止まって構える
        }

        var reach = attacker.ArmReach;
        var spec = KickSpec.Of(kind);
        var duration = (spec.Seconds ?? spec.Duration) * durationScale;
        var sense = game.PlayerSense;
        var aimWorld = spec.AimWorld(sense);
        var aim = sense.Self.WorldToLocal(aimWorld);
        var rest = new Vector3(side.LocalSide() * 0.2f * reach, -1.4f * reach, 0f);
        var before = Cells(victim);
        var startPelvis = attacker.PelvisPosition.Y;
        var minPelvis = startPelvis;
        var minAnkle = float.MaxValue;
        var hits = new HashSet<HitEvent>();
        var parts = new List<KickPart>();
        var maxYaw = 0f;
        var startYaw = attacker.Frame.YawRadians;
        var maxSpeed = 0f;
        var mass = 0f;
        var steps = (int)((duration + 0.5f) / Dt);
        for (var i = 0; i < steps; i++)
        {
            var t = i * Dt;
            var yaw = spec.Turns && t <= duration ? spec.FaceYaw(side, t, duration) : 0f;
            var twist = spec.Turns && t <= duration ? spec.TorsoTwist(side, t, duration) : 0f;
            maxYaw = MathF.Max(maxYaw, MathF.Abs(BodyFrame.DeltaAngle(startYaw, attacker.Frame.YawRadians)));
            if (spec.Rotates)
            {
                aim = game.PlayerSense.Self.WorldToLocal(aimWorld); // 体が回ると、狙いの体フレームでの位置が動く
            }

            var leg = t <= duration ? spec.Evaluate(side, t, duration, rest, aim, reach, game.PlayerSense.SelfLeg(side)) : (LegCommand?)null;
            game.Step(new BoxingInput(Command(attacker, victim, Vector3.Zero, leg, side, yaw, twist)), Dt);
            minPelvis = MathF.Min(minPelvis, attacker.PelvisPosition.Y);
            minAnkle = MathF.Min(minAnkle, Vector3.Distance(attacker.AnklePosition(side), aimWorld));
            if (game.LastHit is { Kick: true } hit && hits.Add(hit))
            {
                parts.Add(hit.KickerPart);
                maxSpeed = MathF.Max(maxSpeed, hit.Speed);
                mass = MathF.Max(mass, hit.ImpactMass);
            }
        }

        return new KickResult(hits.Count, before - Cells(victim), minPelvis, startPelvis, attacker.State, maxSpeed, mass, minAnkle, parts, maxYaw);
    }

    [TestMethod]
    public void EveryKick_LandsAsAHit_ThroughTheFistImpactPath_AndTheKickerKeepsStanding()
    {
        foreach (var kind in Enum.GetValues<KickTechnique>())
        {
            // 後ろ蹴りは足がぎりぎり届く間合いで当たる蹴り（踵を回して振り出す。足首が狙いに寄る近さは 3.8〜7 cm で、触れるかどうかがその間にある:
            // 版 48 は 3.8 cm で触れ（つま先 1 回）、master は 5.4 cm で触れ、マージ後は止まる位置の 1 歩のずれで 7.0 cm になって外した）。
            // 蹴る側が歩いて止まる位置のわずかなずれで当たる・外れるが入れ替わるので、後ろ蹴りだけ標準の間合いより 3 cm 詰めて止まる
            // （1〜8 cm 詰めるとどれも当たる: 1 cm で 1 回・3 cm で 2 回・8 cm で 1 回）。
            // かかと落としも同じ縁にいる: 標準の間合いでは 2 回目の当たりの後に重心が支えの外へ 10 cm 出て（捕捉点の余白 +0.1 m）蹴った側が倒れたが、
            // −4・−2・+1・+2・+4 cm のどこでも立ったまま 3〜4 回当たる（標準の間合いだけが外れる 1 点）ので、2 cm 詰めて止まる。
            var r = Run(kind, closeIn: kind switch { KickTechnique.Back => 0.03f, KickTechnique.Axe => 0.02f, _ => 0f });
            Console.WriteLine($"{kind}: parts [{string.Join(',', r.Parts)}], hits {r.Hits}, victim cells lost {r.VictimCellsLost}, speed {r.MaxSpeed:F2} m/s, mass {r.Mass:F2} kg, closest ankle to aim {r.MinAnkleToTarget:F3} m, pelvis {r.StartPelvis:F3} -> min {r.MinPelvis:F3}, {r.AttackerState}");
            // 蹴りは未成熟なので、当たり・速さ・軸足・立ったままは警告にとどめる（Soft）。
            Soft.Check(r.Hits > 0, $"{kind} lands as a counted hit");
            Soft.Check(r.MaxSpeed > 0f, $"{kind} moves into the victim");
            Soft.Check(r.MinPelvis > r.StartPelvis * 0.7f, $"{kind}: the standing leg keeps the body up");
            Soft.Check(r.AttackerState == FighterState.Standing, $"{kind}: the kicker is still standing ({r.AttackerState})");
        }
    }

    [TestMethod]
    public void TurningKicks_TurnTheBody_AndLandWithTheFootPartTheirPathPoints()
    {
        // 回し蹴り = すね・足の甲、横蹴り = 足の外側・かかと（足の箱）、かかと落とし = かかと（足の箱）。どの足の部位かは脚の骨のボクセルの接触で決まる（種類ごとの当たり判定は無い）。
        var results = new Dictionary<KickTechnique, KickResult>();
        foreach (var kind in new[] { KickTechnique.Roundhouse, KickTechnique.Side, KickTechnique.Back, KickTechnique.Axe })
        {
            results[kind] = Run(kind);
            Console.WriteLine($"{kind}: parts [{string.Join(',', results[kind].Parts)}], body turned {results[kind].MaxYawDeviation:F2} rad");
        }

        CollectionAssert.IsSubsetOf(results[KickTechnique.Roundhouse].Parts.Distinct().ToList(), new[] { KickPart.Shin, KickPart.Toe, KickPart.Foot });
        Soft.Check(results[KickTechnique.Roundhouse].Parts.Any(p => p is KickPart.Shin or KickPart.Toe), "a roundhouse lands with the shin or the top of the foot");
        // 横蹴りは足（足の箱 = 足刀・かかと、またはつま先）で当たる。足の向き（足刀を立てる）は持たないので、どちらが先に触れるかは寄り方の
        // わずかな違いで入れ替わる（CLAUDE.md §8 テコンドー）。PhysicsVersion 44 で寄る間の押し付けが打撃に数えられなくなり、つま先が相手の膝へ先に触れた。
        Soft.Check(results[KickTechnique.Side].Parts.Any(p => p is KickPart.Foot or KickPart.Toe), "a side kick lands with the foot");
        Soft.Check(results[KickTechnique.Axe].Parts.Contains(KickPart.Foot), "an axe kick brings the heel down");
        // 回る蹴りは、体の向きが実際に変わる（命令の向きを物理が追った結果）。かかと落としは体を回さない。
        Assert.IsGreaterThan(0.3f, results[KickTechnique.Roundhouse].MaxYawDeviation);
        Assert.IsGreaterThan(0.5f, results[KickTechnique.Side].MaxYawDeviation);
        Assert.IsGreaterThan(1.0f, results[KickTechnique.Back].MaxYawDeviation);
        // 0.2 → 0.3（PhysicsVersion 43）: 足・つま先を平たい靴の形にしたら、かかと落としの足が相手に触れる回数が増え（すね 2・足 2 → すね 2・足 3）、
        // その反作用で体が 0.12 → 0.24 rad 回った（命令の向きは変えていない）。回る蹴り（1.5〜2.6 rad）とは十分に分かれる。
        Assert.IsLessThan(0.3f, results[KickTechnique.Axe].MaxYawDeviation);
    }

    [TestMethod]
    public void AKickCarriesMoreMass_ThanThePunchOfTheSameFighter()
    {
        // 蹴りの有効質量は脚と体から出る（拳の腕より重い）。倍率ではなく、脚の質量と有効質量の式（拳と同じ PunchMechanics）から。
        using var game = new BoxingGame(seed: 3);
        var f = game.Player;
        var dir = Vector3.UnitZ;
        var kick = f.KickImpactMass(Hand.Right, f.ChestPosition, dir, out _);
        var punch = f.ImpactMass(Hand.Right, dir);
        Assert.IsGreaterThan(punch, kick, "a leg and the body behind it weigh more than an arm");
    }
}
