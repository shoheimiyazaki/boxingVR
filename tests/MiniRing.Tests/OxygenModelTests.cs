using System.Globalization;
using System.Numerics;
using Aigis.Physics;

namespace MiniRing.Tests;

/// <summary>
/// 呼吸と循環（2026-11-18、ユーザーの設計）: 部位ごとの酸素の蓄えが尽きたとき、届く速さを決めた所（血管・心臓・肺）が
/// それぞれ別の見える合図に出る。血管 → その腕だけが遅れる、心臓 → 顔の青白さ、肺 → 血の酸素が減って唇の青さと息のあえぎ。
/// </summary>
[TestClass]
public sealed class OxygenModelTests
{
    private const float Dt = 1f / 240f;

    private sealed class Rig : IDisposable
    {
        private readonly PhysicsWorld _world = new();
        private readonly Vector3 _left;
        private readonly Vector3 _guard;

        public Fighter Fighter { get; }

        public Rig(FighterTuning tuning)
        {
            _world.AddStaticBox(new Vector3(0f, -0.5f, 0f), new Vector3(8f, 0.5f, 8f));
            Fighter = Fighter.Spawn(_world, tuning, "oxygen", Vector3.Zero, 0f);
            _left = AiControl.Guard(Hand.Left, Fighter.ArmReach);
            _guard = AiControl.Guard(Hand.Right, Fighter.ArmReach);
            Hold(2f);
        }

        private void Step(Vector3 right, float spring)
        {
            Fighter.ApplyControl(new FighterCommand(Vector3.Zero, Vector3.UnitZ, new HandCommand(_left, false), new HandCommand(right, false, spring)), Dt);
            _world.Step(Dt);
        }

        public void Hold(float seconds)
        {
            for (var i = 0; i < (int)MathF.Round(seconds / Dt); i++)
            {
                Step(_guard, 0f);
            }
        }

        /// <summary>右手だけで強い 1 発を <paramref name="cycle"/> 秒ごとに <paramref name="seconds"/> 秒打つ。</summary>
        public void PunchRight(float seconds, float cycle)
        {
            var aim = _guard + (new Vector3(0f, 0.05f, 1.6f) * Fighter.ArmReach);
            var steps = (int)MathF.Round(seconds / Dt);
            var period = (int)MathF.Round(cycle / Dt);
            for (var i = 0; i < steps; i++)
            {
                var phase = i % period;
                var extending = phase < period / 2;
                Step(extending ? Vector3.Lerp(_guard, aim, MathF.Min(1f, phase * Dt / 0.06f)) : _guard, extending ? 1f : 0f);
            }
        }

        public void Dispose() => _world.Dispose();
    }

    [TestMethod]
    public void OneArmWorkingHard_IsLimitedByItsOwnVessels_AndOnlyThatArmTires()
    {
        using var rig = new Rig(FighterTuning.Default);
        rig.Fighter.SetOxygen(MuscleRegion.RightArm, 0.5f); // 右腕はもう打ち続けてきた（満タンから半分まで 20 秒ほどかかるのを省く）
        var vessels = 0;
        for (var i = 0; i < 60; i++)
        {
            rig.PunchRight(0.25f, 0.25f);
            vessels += rig.Fighter.LimitOf(MuscleRegion.RightArm) == OxygenLimit.Vessels ? 1 : 0;
        }

        var f = rig.Fighter;
        Console.WriteLine(string.Create(CultureInfo.InvariantCulture,
            $"15 s right-hand punching: right O2 {f.OxygenOf(MuscleRegion.RightArm):P0} vigor {f.VigorOf(MuscleRegion.RightArm):F2}, left O2 {f.OxygenOf(MuscleRegion.LeftArm):P0} vigor {f.VigorOf(MuscleRegion.LeftArm):F2}; vessel-limited samples {vessels}/60; pallor {f.Pallor:F2}, cyanosis {f.Cyanosis:F2}, winded {f.Winded:F2}"));
        Assert.IsLessThan(0.45f, f.OxygenOf(MuscleRegion.RightArm), "打ち続けた右腕の蓄えは減る");
        Assert.IsGreaterThan(0.9f, f.OxygenOf(MuscleRegion.LeftArm), "構えていた左腕は満ちたまま");
        Assert.IsLessThan(f.VigorOf(MuscleRegion.LeftArm), f.VigorOf(MuscleRegion.RightArm), "右腕だけが弱る（腕の追従の遅れ）");
        Assert.IsGreaterThan(30, vessels, "右腕へ届く速さを決めているのは、その腕の血管");
        Assert.IsLessThan(0.05f, f.Pallor, "心臓は足りている（顔は青白くならない）");
        Assert.IsLessThan(0.05f, f.Cyanosis, "肺も足りている（唇は青くならない）");
    }

    [TestMethod]
    public void WeakHeart_LimitsTheRecovery_AndTheFaceTurnsPale()
    {
        using var rig = new Rig(FighterTuning.Default with { HeartPower = 3f });
        rig.Fighter.EmptyArms();
        rig.Hold(3f);
        var f = rig.Fighter;
        Console.WriteLine(string.Create(CultureInfo.InvariantCulture,
            $"weak heart, 3 s after emptying the arms: limit {f.LimitOf(MuscleRegion.RightArm)}, right O2 {f.OxygenOf(MuscleRegion.RightArm):P0}, delivery {f.DeliveryOf(MuscleRegion.RightArm):F2} W; pallor {f.Pallor:F2}, cyanosis {f.Cyanosis:F2}"));
        Assert.AreEqual(OxygenLimit.Heart, f.LimitOf(MuscleRegion.RightArm));
        Assert.IsGreaterThan(0.5f, f.Pallor, "心臓が送りきれないと顔が青白い");
        Assert.IsLessThan(0.05f, f.Cyanosis, "肺は足りているので血の酸素は減らない");
        Assert.IsLessThanOrEqualTo(3.01f, f.DeliveryOf(MuscleRegion.RightArm) + f.DeliveryOf(MuscleRegion.LeftArm), "両腕へは心臓の速さ（3 W）の分だけ");
    }

    [TestMethod]
    public void WeakLungs_DrainTheBlood_TheLipsTurnBlue_AndTheFighterPants()
    {
        using var rig = new Rig(FighterTuning.Default with { LungPower = 3f });
        rig.Fighter.EmptyArms();
        rig.Hold(3f);
        var f = rig.Fighter;
        Console.WriteLine(string.Create(CultureInfo.InvariantCulture,
            $"weak lungs, 3 s after emptying the arms: limit {f.LimitOf(MuscleRegion.RightArm)}, blood {f.BloodOxygen:P0}; cyanosis {f.Cyanosis:F2}, winded {f.Winded:F2}, pallor {f.Pallor:F2}"));
        Assert.AreEqual(OxygenLimit.Lungs, f.LimitOf(MuscleRegion.RightArm));
        Assert.IsLessThan(0.1f, f.BloodOxygen, "肺が追いつかず血の酸素が尽きる");
        Assert.IsGreaterThan(0.9f, f.Cyanosis, "唇・顔が青紫");
        Assert.IsGreaterThan(0.9f, f.Winded, "あえぐ");
        Assert.IsLessThan(0.05f, f.Pallor, "心臓は足りている");
    }

    [TestMethod]
    public void HealthyBody_RestingAfterEmptyArms_IsLimitedByTheVessels_WithoutAnyFaceCue()
    {
        using var rig = new Rig(FighterTuning.Default);
        rig.Fighter.EmptyArms();
        rig.Hold(2f);
        var f = rig.Fighter;
        Assert.AreEqual(OxygenLimit.Vessels, f.LimitOf(MuscleRegion.RightArm));
        Assert.IsLessThan(0.05f, f.Pallor);
        Assert.IsLessThan(0.05f, f.Cyanosis);
    }
}
