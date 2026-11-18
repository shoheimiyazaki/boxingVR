using System.Globalization;
using System.Numerics;
using MiniRing.Bots;

namespace MiniRing.Tests;

/// <summary>
/// 肘打ち（2026-11-17、ムエタイ）が拳・蹴りと同じ接触（VoxelContact → ApplyHit）で相手の体に当たり、速く出した肘ほどエネルギーが大きいことの数値の確認。
/// 肘用の式・倍率は無い（肘の速さ・腕と体の質量・当たる骨と皮膚だけが結果を決める）。
/// </summary>
[TestClass]
public sealed class ElbowContactTests
{
    private const float Dt = 1f / 240f;

    /// <summary>
    /// 肘の間合い（Standoff）に対する、寄る距離の倍率。2026-11-18（PhysicsVersion 45）: 肘は腕だけの質量（0.3〜0.5 kg）で当たるようになり、
    /// 上向きの肘（0.8 m/s 前後で届いていた）は 1 倍・0.95 倍の間合い（寄り終わった距離 1.13 腕）では当たりに数えない（ダメージ 1 点未満の「触れただけ」。
    /// 版 43 は同じ間合い 1.10 腕で体の質量 2.4 kg が乗って数えた）。0.9 倍（寄り終わりの距離 1.02 腕）なら 3 つの肘とも当たる
    /// （横 1.95 m/s・上 1.74 m/s・下 1.86 m/s）。
    /// </summary>
    private const float CloseIn = 0.9f;

    private sealed record ElbowResult(string Zones, float StartDistance, int Hits, int VictimCellsLost, float MaxSpeed, float MaxMass, float MaxEnergy, float TotalEnergy, float MaxElbowSpeed, float MinElbowToAim);

    private static int Cells(Fighter f) => f.Voxels!.Parts.Concat(f.Voxels.Fragments).Sum(p => p.Body.Grid.FilledCount);

    private static FighterCommand Command(Fighter self, Fighter opponent, Vector3 move, HandCommand? elbow, Hand side, float twist, Vector3 head = default)
    {
        var reach = self.ArmReach;
        var face = opponent.ChestPosition - self.ChestPosition;
        var left = side == Hand.Left && elbow is { } l ? l : new HandCommand(AiControl.Guard(Hand.Left, reach), false);
        var right = side == Hand.Right && elbow is { } r ? r : new HandCommand(AiControl.Guard(Hand.Right, reach), false);
        return new FighterCommand(move, face, left, right, head, TwistBias: twist);
    }

    /// <summary>
    /// 立って構えるだけの相手へ、止まったまま台本の指令で肘を打つ。<paramref name="distanceScale"/> は胸と胸の距離（その肘の Standoff の倍率）。
    /// </summary>
    private static ElbowResult Run(ElbowTechnique kind, Hand side = Hand.Right, float durationScale = 1f, float distanceScale = 1f)
    {
        using var game = new BoxingGame(seed: 3, corners: new BoxingCorners("still", _ => new StandGuard()));
        var attacker = game.Player;
        var victim = game.Cpu;
        var spec = ElbowSpec.Of(kind);
        for (var i = 0; i < 240 * 6; i++)
        {
            var d = Vector3.Distance(attacker.ChestPosition with { Y = 0f }, victim.ChestPosition with { Y = 0f });
            if (d < spec.Standoff * distanceScale * attacker.ArmReach)
            {
                break;
            }

            var toward = Vector3.Normalize((victim.ChestPosition - attacker.ChestPosition) with { Y = 0f });
            game.Step(new BoxingInput(Command(attacker, victim, toward, null, side, 0f)), Dt);
        }

        for (var i = 0; i < 120; i++)
        {
            game.Step(new BoxingInput(Command(attacker, victim, Vector3.Zero, null, side, 0f)), Dt);
        }

        var reach = attacker.ArmReach;
        var duration = spec.Duration * durationScale;
        var aimWorld = spec.AimWorld(game.PlayerSense);
        var before = Cells(victim);
        var hits = new HashSet<HitEvent>();
        var maxSpeed = 0f;
        var maxMass = 0f;
        var maxEnergy = 0f;
        var total = 0f;
        var maxElbowSpeed = 0f;
        var minToAim = float.MaxValue;
        var zones = new List<string>();
        var startDistance = Vector3.Distance(attacker.ChestPosition with { Y = 0f }, victim.ChestPosition with { Y = 0f }) / attacker.ArmReach;
        var steps = (int)((duration + 0.5f) / Dt);
        var previous = ElbowTip(attacker, side);
        for (var i = 0; i < steps; i++)
        {
            var t = i * Dt;
            var sense = game.PlayerSense;
            var aim = sense.Self.WorldToLocal(aimWorld);
            var command = t <= duration ? spec.Command(side, t, duration, AiControl.Guard(side, reach), aim, reach) : (HandCommand?)null;
            var twist = t <= duration ? spec.TorsoTwist(side, t, duration) : 0f;
            var toward2 = Vector3.Normalize((victim.ChestPosition - attacker.ChestPosition) with { Y = 0f });
            var head = t <= duration ? spec.HeadShift(side, t, duration, reach) : Vector3.Zero;
            var move = t <= duration ? toward2 * spec.StepEnvelope(t, duration) : Vector3.Zero;
            game.Step(new BoxingInput(Command(attacker, victim, move, command, side, twist, head)), Dt);
            var tip = ElbowTip(attacker, side);
            maxElbowSpeed = MathF.Max(maxElbowSpeed, Vector3.Distance(tip, previous) / Dt);
            previous = tip;
            minToAim = MathF.Min(minToAim, Vector3.Distance(tip, aimWorld));
            if (game.LastHit is { Elbow: true } hit && hits.Add(hit))
            {
                zones.Add(hit.Zone.ToString());
                maxSpeed = MathF.Max(maxSpeed, hit.Speed);
                maxMass = MathF.Max(maxMass, hit.ImpactMass);
                maxEnergy = MathF.Max(maxEnergy, hit.Energy);
                total += hit.Energy;
            }
        }

        return new ElbowResult(string.Join(",", zones), startDistance, hits.Count, before - Cells(victim), maxSpeed, maxMass, maxEnergy, total, maxElbowSpeed, minToAim);
    }

    /// <summary>肘の先（前腕の肩寄りの端 = 上腕と前腕の継ぎ目）の位置。</summary>
    private static Vector3 ElbowTip(Fighter f, Hand side) => f.ElbowPosition(side);

    private static string Describe(string name, ElbowResult r) => string.Create(CultureInfo.InvariantCulture,
        $"{name}: zones [{r.Zones}] d0 {r.StartDistance:F2}, hits {r.Hits}, speed {r.MaxSpeed:F2} m/s, mass {r.MaxMass:F2} kg, energy {r.MaxEnergy:F3} J (sum {r.TotalEnergy:F3}), victim cells lost {r.VictimCellsLost}, elbow top speed {r.MaxElbowSpeed:F2} m/s, closest to aim {r.MinElbowToAim:F3} m");

    /// <summary>同じ台本の枠で、右のストレートを打つ（肘との比較の基準）。</summary>
    private static ElbowResult RunPunch(Technique technique, float standoff, float duration)
    {
        using var game = new BoxingGame(seed: 3, corners: new BoxingCorners("still", _ => new StandGuard()));
        var attacker = game.Player;
        var victim = game.Cpu;
        for (var i = 0; i < 240 * 6; i++)
        {
            var d = Vector3.Distance(attacker.ChestPosition with { Y = 0f }, victim.ChestPosition with { Y = 0f });
            if (d < standoff * attacker.ArmReach)
            {
                break;
            }

            game.Step(new BoxingInput(Command(attacker, victim, Vector3.Normalize((victim.ChestPosition - attacker.ChestPosition) with { Y = 0f }), null, Hand.Right, 0f)), Dt);
        }

        for (var i = 0; i < 120; i++)
        {
            game.Step(new BoxingInput(Command(attacker, victim, Vector3.Zero, null, Hand.Right, 0f)), Dt);
        }

        var reach = attacker.ArmReach;
        var spec = PunchSpec.Of(technique);
        var aimWorld = game.PlayerSense.OpponentHead;
        var before = Cells(victim);
        var hits = new HashSet<HitEvent>();
        var zones = new List<string>();
        float maxSpeed = 0f, maxMass = 0f, maxEnergy = 0f, total = 0f;
        var steps = (int)((duration + 0.5f) / Dt);
        for (var i = 0; i < steps; i++)
        {
            var t = i * Dt;
            var aim = game.PlayerSense.Self.WorldToLocal(aimWorld);
            HandCommand? command = null;
            if (t <= duration)
            {
                var target = spec.Evaluate(Hand.Right, t, duration, AiControl.Guard(Hand.Right, reach), aim, reach);
                var spring = t < duration * PunchSpec.OutboundShare * PunchSpec.LaunchShare ? 1f : 0f;
                command = new HandCommand(target, false, spring);
            }

            game.Step(new BoxingInput(Command(attacker, victim, Vector3.Zero, command, Hand.Right, 0f)), Dt);
            if (game.LastHit is { Elbow: false, Kick: false } hit && hits.Add(hit))
            {
                zones.Add(hit.Zone.ToString());
                maxSpeed = MathF.Max(maxSpeed, hit.Speed);
                maxMass = MathF.Max(maxMass, hit.ImpactMass);
                maxEnergy = MathF.Max(maxEnergy, hit.Energy);
                total += hit.Energy;
            }
        }

        return new ElbowResult(string.Join(",", zones), 0f, hits.Count, before - Cells(victim), maxSpeed, maxMass, maxEnergy, total, 0f, 0f);
    }

    [TestMethod]
    public void Sweep_Probe()
    {
        Console.WriteLine(Describe("Cross x1.0", RunPunch(Technique.Cross, 1.0f, 0.45f)));
        Console.WriteLine(Describe("Jab x0.9", RunPunch(Technique.Jab, 0.9f, 0.35f)));
        foreach (var kind in Enum.GetValues<ElbowTechnique>())
        {
            foreach (var scale in new[] { CloseIn, 1.0f })
            {
                Console.WriteLine(Describe(kind + " x" + scale.ToString(CultureInfo.InvariantCulture), Run(kind, distanceScale: scale)));
            }
        }
    }

    [TestMethod]
    public void EveryElbow_LandsAsAHit_ThroughTheFistImpactPath()
    {
        foreach (var kind in Enum.GetValues<ElbowTechnique>())
        {
            var r = Run(kind, distanceScale: CloseIn);
            Console.WriteLine(Describe(kind.ToString(), r));
            Assert.IsGreaterThan(0, r.Hits, $"{kind} lands as a counted hit (ApplyHit)");
            Assert.IsGreaterThan(0f, r.MaxSpeed, $"{kind} moves into the victim");
            Assert.IsGreaterThan(0f, r.MaxEnergy, $"{kind} carries energy");
        }
    }

    [TestMethod]
    public void AFasterElbow_CarriesMoreEnergy()
    {
        // 同じ経路を時間だけ変える。速く出した肘ほど速さが大きく、エネルギー（½ m v²）が大きい（倍率ではなく物理）。
        // 2026-11-18（PhysicsVersion 45）: 腕だけの質量（0.3〜0.5 kg）で当たるので、遅い肘（durationScale 1.8）は 1 点未満の「触れただけ」で
        // 当たりに数えない（版 43 は体の質量 1.9 kg が乗って 0.77 m/s でも数えた）。遅い方を、まだ当たりに数える 1.4 倍にした
        // （間合い 0.9 倍: 速さ 1.72 m/s・エネルギー 0.751 J。速い方 0.8 倍は 2.31 m/s・1.278 J。1.6 倍は当たらない）。
        var slow = Run(ElbowTechnique.Horizontal, durationScale: 1.4f, distanceScale: CloseIn);
        var fast = Run(ElbowTechnique.Horizontal, durationScale: 0.8f, distanceScale: CloseIn);
        Console.WriteLine(Describe("slow", slow));
        Console.WriteLine(Describe("fast", fast));
        Assert.IsGreaterThan(0, fast.Hits, "the fast elbow lands");
        Assert.IsGreaterThan(slow.MaxElbowSpeed, fast.MaxElbowSpeed, "the fast path moves the elbow faster");
        Assert.IsGreaterThan(slow.MaxEnergy, fast.MaxEnergy, "a faster elbow carries more energy");
    }
}
