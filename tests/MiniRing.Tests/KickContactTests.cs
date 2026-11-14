using System.Numerics;
using MiniRing.Bots;

namespace MiniRing.Tests;

/// <summary>蹴りが拳と同じ接触（VoxelContact → ApplyHit）で相手の体に当たり、軸足が体を支えていることの数値の確認。</summary>
[TestClass]
public sealed class KickContactTests
{
    private const float Dt = 1f / 240f;

    private sealed record KickResult(int Hits, int VictimCellsLost, float MinPelvis, float StartPelvis, FighterState AttackerState,
        float MaxSpeed, float Mass, float MinAnkleToTarget);

    private static int Cells(Fighter f) => f.Voxels!.Parts.Concat(f.Voxels.Fragments).Sum(p => p.Body.Grid.FilledCount);

    private static FighterCommand Command(Fighter self, Fighter opponent, Vector3 move, LegCommand? leg, Hand side)
    {
        var reach = self.ArmReach;
        return new FighterCommand(move, opponent.ChestPosition - self.ChestPosition,
            new HandCommand(AiControl.Guard(Hand.Left, reach), false), new HandCommand(AiControl.Guard(Hand.Right, reach), false),
            LeftLeg: side == Hand.Left ? leg : null, RightLeg: side == Hand.Right ? leg : null);
    }

    /// <summary>足は腕より短い（高い所ほど近づかないと届かない）: 蹴りごとの、胸と胸の距離（腕の長さ倍）。</summary>
    private static float ApproachOf(KickTechnique kind) => kind switch
    {
        KickTechnique.High => 0.8f,
        KickTechnique.Middle => 0.9f,
        KickTechnique.Knee => 0.8f,
        _ => 0.95f,
    };

    private static KickResult Run(KickTechnique kind, Hand side = Hand.Right, float durationScale = 1f)
    {
        // 蹴られる側は立って構えるだけ。蹴る側（赤）は台本の指令で歩いて詰め、止まって蹴る。
        using var game = new BoxingGame(seed: 3, corners: new BoxingCorners("still", _ => new StandGuard()));
        var attacker = game.Player;
        var victim = game.Cpu;
        for (var i = 0; i < 240 * 6; i++)
        {
            var d = Vector3.Distance(attacker.ChestPosition with { Y = 0f }, victim.ChestPosition with { Y = 0f });
            if (d < ApproachOf(kind) * attacker.ArmReach)
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
        var duration = spec.Duration * durationScale;
        var sense = game.PlayerSense;
        var aimWorld = spec.AimWorld(sense);
        var aim = sense.Self.WorldToLocal(aimWorld);
        var rest = new Vector3(side.LocalSide() * 0.2f * reach, -1.4f * reach, 0f);
        var before = Cells(victim);
        var startPelvis = attacker.PelvisPosition.Y;
        var minPelvis = startPelvis;
        var minAnkle = float.MaxValue;
        var hits = new HashSet<HitEvent>();
        var maxSpeed = 0f;
        var mass = 0f;
        var steps = (int)((duration + 0.5f) / Dt);
        for (var i = 0; i < steps; i++)
        {
            var t = i * Dt;
            var leg = t <= duration ? spec.Evaluate(side, t, duration, rest, aim, reach) : (LegCommand?)null;
            game.Step(new BoxingInput(Command(attacker, victim, Vector3.Zero, leg, side)), Dt);
            minPelvis = MathF.Min(minPelvis, attacker.PelvisPosition.Y);
            minAnkle = MathF.Min(minAnkle, Vector3.Distance(attacker.AnklePosition(side), aimWorld));
            if (game.LastHit is { Kick: true } hit && hits.Add(hit))
            {
                maxSpeed = MathF.Max(maxSpeed, hit.Speed);
                mass = MathF.Max(mass, hit.ImpactMass);
            }
        }

        return new KickResult(hits.Count, before - Cells(victim), minPelvis, startPelvis, attacker.State, maxSpeed, mass, minAnkle);
    }

    [TestMethod]
    public void EveryKick_LandsAsAHit_ThroughTheFistImpactPath_AndTheKickerKeepsStanding()
    {
        foreach (var kind in Enum.GetValues<KickTechnique>())
        {
            var r = Run(kind);
            Console.WriteLine($"{kind}: hits {r.Hits}, victim cells lost {r.VictimCellsLost}, speed {r.MaxSpeed:F2} m/s, mass {r.Mass:F2} kg, closest ankle to aim {r.MinAnkleToTarget:F3} m, pelvis {r.StartPelvis:F3} -> min {r.MinPelvis:F3}, {r.AttackerState}");
            Assert.IsGreaterThan(0, r.Hits, $"{kind} lands as a counted hit");
            Assert.IsGreaterThan(0f, r.MaxSpeed, $"{kind} moves into the victim");
            Assert.IsGreaterThan(r.StartPelvis * 0.7f, r.MinPelvis, $"{kind}: the standing leg keeps the body up");
            Assert.AreEqual(FighterState.Standing, r.AttackerState, $"{kind}: the kicker is still standing");
        }
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
