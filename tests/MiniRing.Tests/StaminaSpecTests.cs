using System.Globalization;
using System.Numerics;

namespace MiniRing.Tests;

/// <summary>
/// スタミナの仕様（2026-11-22、PhysicsVersion 87、docs/BOXING.md §2）が物理として起きていること:
/// 立つ・構える・足運びだけでは胴も腕も減らない／連打では胴の仕事が立っているときの桁違いに大きい／胴の酸素が切れても体は崩れない（弱るのは回旋だけ）／
/// 息が上がるとあごが上がり胸が上下する。数値は TrunkWorkProbe・FatigueLookProbe の測定から余裕を取った下限・上限。
/// </summary>
[TestClass]
[TestCategory("slow")]
public sealed class StaminaSpecTests
{
    private const float Dt = 1f / 60f;

    private static BoxingGame NewGame(Func<float, FighterIntent> plan, out Func<float, FighterIntent> planOut)
    {
        planOut = plan;
        var corners = BoxingCorners.Default with { Player = _ => new IntentControl(), Cpu = _ => new IntentControl() };
        var game = new BoxingGame(FighterTuning.Default with { ConcussionSpin = float.PositiveInfinity, CoreToneFloor = 1f }, seed: 3, corners: corners);
        ((IntentControl)game.CpuControl!).Input = new FighterIntent(Vector2.Zero, Evade: true);
        return game;
    }

    private static FighterIntent Rush(float t)
    {
        var phase = t / 0.2014f;
        var index = (int)MathF.Floor(phase);
        var down = phase - index < 0.67f;
        return new FighterIntent(Vector2.Zero, Light: down && index % 2 == 0, Heavy: down && index % 2 == 1);
    }

    private static FighterIntent Footwork(float t)
        => new(new Vector2(MathF.Sign(MathF.Sin(t * 0.4171f)), MathF.Sin(t * 1.132f) * 0.6f));

    /// <summary>plan で seconds 秒動かし、部位ごとの最小の酸素の残りと、胴の仕事の平均の仕事率 [W]（始めの 2 秒を除く）を返す。</summary>
    private static (float MinArms, float MinTrunk, float MinLegs, float TrunkWatts) Run(Func<float, FighterIntent> plan, float seconds)
    {
        using var game = NewGame(plan, out _);
        var control = (IntentControl)game.PlayerControl!;
        var minArms = 1f;
        var minTrunk = 1f;
        var minLegs = 1f;
        var workAt2 = 0f;
        for (var step = 1; step <= (int)(seconds / Dt); step++)
        {
            control.Input = plan(step * Dt);
            game.Step(new BoxingInput(default), Dt);
            var p = game.Player;
            minArms = MathF.Min(minArms, MathF.Min(p.OxygenOf(MuscleRegion.LeftArm), p.OxygenOf(MuscleRegion.RightArm)));
            minTrunk = MathF.Min(minTrunk, p.OxygenOf(MuscleRegion.Trunk));
            minLegs = MathF.Min(minLegs, MathF.Min(p.OxygenOf(MuscleRegion.LeftLeg), p.OxygenOf(MuscleRegion.RightLeg)));
            if (step == (int)(2f / Dt))
            {
                workAt2 = p.RegionWork(MuscleRegion.Trunk);
            }
        }

        var watts = (game.Player.RegionWork(MuscleRegion.Trunk) - workAt2) / (seconds - 2f);
        return (minArms, minTrunk, minLegs, watts);
    }

    [TestMethod]
    public void StandingGuarding_AndFootwork_DoNotDrainArmsTrunkOrLegs()
    {
        var idle = Run(_ => default, 12f);
        var foot = Run(Footwork, 20f);
        Console.WriteLine(string.Create(CultureInfo.InvariantCulture,
            $"idle: arms {idle.MinArms:P0} trunk {idle.MinTrunk:P0} ({idle.TrunkWatts:F0} W); footwork: arms {foot.MinArms:P0} trunk {foot.MinTrunk:P0} legs {foot.MinLegs:P0} ({foot.TrunkWatts:F0} W)"));
        Assert.IsGreaterThan(0.95f, idle.MinArms, "構えて立つだけで腕の蓄えは減らない");
        Assert.IsGreaterThan(0.95f, idle.MinTrunk, "構えて立つだけで胴の蓄えは減らない（胴の筋の仕事を数えても）");
        Assert.IsGreaterThan(0.95f, foot.MinArms);
        Assert.IsGreaterThan(0.95f, foot.MinTrunk);
        Assert.IsGreaterThan(0.95f, foot.MinLegs);
    }

    [TestMethod]
    public void Rush_UsesTheTrunk_ManyTimesMoreThanStanding_AndEmptiesIt()
    {
        var idle = Run(_ => default, 12f);
        var rush = Run(Rush, 14f);
        Console.WriteLine(string.Create(CultureInfo.InvariantCulture,
            $"trunk work: idle {idle.TrunkWatts:F0} W, rush {rush.TrunkWatts:F0} W; rush min oxygen: arms {rush.MinArms:P0}, trunk {rush.MinTrunk:P0}"));
        Assert.IsGreaterThan(8f * idle.TrunkWatts, rush.TrunkWatts, "連打の胴の仕事は、立っているときの桁違い（腰の駆動と胸の回旋が酸素を使う）");
        Assert.IsLessThan(0.2f, rush.MinArms, "腕の酸素が切れる");
        Assert.IsLessThan(0.3f, rush.MinTrunk, "胴の酸素も切れる（腕に少し遅れて）");
    }

    private static (float HeadPitchMean, float ChestPitchMin, float ChestPitchMax, float ChestPitchMean) Posture(Action<Fighter>? prepare, float seconds = 8f)
    {
        using var game = NewGame(_ => default, out _);
        prepare?.Invoke(game.Player);
        var head = new List<float>();
        var chest = new List<float>();
        static float Pitch(Vector3 v) => MathF.Atan2(-v.Y, MathF.Sqrt((v.X * v.X) + (v.Z * v.Z))) * 180f / MathF.PI;
        for (var step = 1; step <= (int)(seconds / Dt); step++)
        {
            game.Step(new BoxingInput(default), Dt);
            if (step * Dt >= 3f)
            {
                head.Add(Pitch(Vector3.Transform(Vector3.UnitZ, game.Player.HeadOrientation)));
                chest.Add(Pitch(Vector3.Transform(Vector3.UnitZ, game.Player.TorsoOrientation)));
            }
        }

        return (head.Average(), chest.Min(), chest.Max(), chest.Average());
    }

    [TestMethod]
    public void OutOfBreath_ChinGoesUp_ChestRocksWithTheBreath_ButTheBodyStaysUp()
    {
        var fresh = Posture(null);
        var empty = Posture(f => f.Stamina = 0f);
        Console.WriteLine(string.Create(CultureInfo.InvariantCulture,
            $"head pitch mean: fresh {fresh.HeadPitchMean:F1}°, out of breath {empty.HeadPitchMean:F1}° (positive = down); chest pitch: fresh {fresh.ChestPitchMin:F1}..{fresh.ChestPitchMax:F1}°, out of breath {empty.ChestPitchMin:F1}..{empty.ChestPitchMax:F1}°"));
        Assert.IsLessThan(fresh.HeadPitchMean - 12f, empty.HeadPitchMean, "息が上がるとあごが上がる（顔が上を向く）");
        Assert.IsGreaterThan((fresh.ChestPitchMax - fresh.ChestPitchMin) + 2f, empty.ChestPitchMax - empty.ChestPitchMin, "息が上がると胸の上下が大きい（呼吸の筋のトルク）");
        Assert.IsLessThan(14f, MathF.Abs(empty.ChestPitchMean - fresh.ChestPitchMean) + 0f, "酸素が全部切れても胴は崩れない（弱るのは回旋だけ。起こして保つ力は疲れで崩れない）");
    }
}
