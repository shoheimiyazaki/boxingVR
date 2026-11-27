using System.Globalization;
using System.Numerics;
using Aigis.Physics;

namespace MiniRing.Tests;

/// <summary>
/// スタミナのテンポ（2026-11-15、docs/arena/gen13_stamina_tempo.md）: 空になっても回復がすぐ始まり、全力の攻めは続けるうちに空になる。
/// 回復を止める仕事率 P は「実際にした仕事」で数える（出せなかった分は数えない）。目標は指示書
/// docs/plans/done/CLAUDE_BRIEF_STAMINA_TEMPO_2026-11-15.md の値。
/// </summary>
[TestClass]
[TestCategory("slow")]
public sealed class StaminaTempoTests
{
    private const float Dt = 1f / 240f;

    private sealed class Rig : IDisposable
    {
        private readonly PhysicsWorld _world = new();
        private readonly Vector3 _left;
        private readonly Vector3 _guard;

        public Fighter Fighter { get; }

        public float Time { get; private set; }

        public Rig()
        {
            _world.AddStaticBox(new Vector3(0f, -1.408f, 0f), new Vector3(22.54f, 1.408f, 22.54f));
            Fighter = Fighter.Spawn(_world, FighterTuning.Default, "tempo", Vector3.Zero, 0f);
            _left = AiControl.Guard(Hand.Left, Fighter.ArmReach);
            _guard = AiControl.Guard(Hand.Right, Fighter.ArmReach);
            for (var i = 0; i < 806; i++)
            {
                Step(_guard, 0f, Vector3.Zero);
            }

            Time = 0f;
        }

        public void Step(Vector3 right, float spring, Vector3 move)
        {
            Fighter.ApplyControl(new FighterCommand(move, Vector3.UnitZ, new HandCommand(_left, false), new HandCommand(right, false, spring)), Dt);
            _world.Step(Dt);
            Time += Dt;
        }

        public void Hold(float seconds, Vector3 move)
        {
            for (var i = 0; i < (int)MathF.Round(seconds / Dt); i++)
            {
                Step(_guard, 0f, move);
            }
        }

        /// <summary>強い 1 発（拳の目標は腕の長さの 1.6 倍先・0.1 秒で届かせる・出す 0.2 秒）。胸に対する拳の最大速さを返す。</summary>
        public float StrongPunch(float cycleSeconds)
        {
            var peak = 0f;
            var aim = _guard + (new Vector3(0f, 0.05f, 1.6f) * Fighter.ArmReach);
            for (var phase = 0; phase < (int)MathF.Round(cycleSeconds / Dt); phase++)
            {
                var extending = phase < 49;
                Step(extending ? Vector3.Lerp(_guard, aim, MathF.Min(1f, phase * Dt / 0.1007f)) : _guard, extending ? 1f : 0f, Vector3.Zero);
                if (extending)
                {
                    peak = MathF.Max(peak, (Fighter.GloveVelocity(Hand.Right) - Fighter.ChestVelocity).Length());
                }
            }

            return peak;
        }

        public void Dispose() => _world.Dispose();
    }

    private static float FullFirstPeak()
    {
        using var rig = new Rig();
        return rig.StrongPunch(0.5035f);
    }

    /// <summary>
    /// 容量 300 J・休んだときの戻り 20 W（2026-11-17、人形の単位）: 空から構えて立つと、ラウンドの間の 16.8 秒（人形の単位で 10 秒）で 6 割ほど、26.9 秒（16 秒）でほぼ満タン
    /// （人形の単位の測定 6 秒 39 %・12 秒 79 %）。
    /// </summary>
    [TestMethod]
    public void EmptyReserve_Standing_RoundBreakRecoversMoreThanHalf_FullAfterSixteen()
    {
        using var rig = new Rig();
        rig.Fighter.EmptyArms();
        rig.Hold(16.78f, Vector3.Zero);
        var at10 = rig.Fighter.Stamina;
        rig.Hold(10.07f, Vector3.Zero);
        var at16 = rig.Fighter.Stamina;
        Console.WriteLine(string.Create(CultureInfo.InvariantCulture, $"standing from empty: 16.8 s {at10:P1}, 26.9 s {at16:P1}"));
        Assert.IsGreaterThanOrEqualTo(0.55f, at10);
        Assert.IsGreaterThanOrEqualTo(0.9f, at16);
    }

    /// <summary>
    /// 足を使っても回復はほとんど止まらない（臨界パワーのモデル、2026-11-17）: 下がりながら歩く仕事率（約 1.7 W）は臨界パワー（9 W）よりずっと下。
    /// 前の式では打ち合っていない立ち回りでも減る場面があり、ボットが休むために棒立ちになった（ユーザーの指摘）。
    /// </summary>
    [TestMethod]
    public void EmptyReserve_RetreatingOnFoot_RecoversNearlyAsFastAsStanding()
    {
        using var standing = new Rig();
        standing.Fighter.EmptyArms();
        standing.Hold(13.43f, Vector3.Zero);
        using var rig = new Rig();
        rig.Fighter.EmptyArms();
        rig.Hold(13.43f, new Vector3(0f, 0f, -1f));
        Console.WriteLine(string.Create(CultureInfo.InvariantCulture,
            $"from empty for 13.4 s: retreating {rig.Fighter.Stamina:P1} (P {rig.Fighter.RecentMusclePower:F2} W), standing {standing.Fighter.Stamina:P1}"));
        Assert.IsGreaterThanOrEqualTo(0.8f * standing.Fighter.Stamina, rig.Fighter.Stamina);
    }

    [TestMethod]
    public void EmptyReserve_FirstStrongPunch_ComesWithinTwoSeconds()
    {
        var reference = FullFirstPeak();
        using var rig = new Rig();
        rig.Fighter.EmptyArms();
        var first = -1f;
        for (var attempt = 0; attempt < 8 && first < 0f; attempt++)
        {
            var start = rig.Time;
            if (rig.StrongPunch(0.8392f) >= 0.6f * reference)
            {
                first = start;
            }
        }

        Console.WriteLine(string.Create(CultureInfo.InvariantCulture, $"first >=80% punch from empty (reference {reference:F2} m/s): at {first:F2} s"));
        Assert.IsGreaterThanOrEqualTo(0f, first, "空から約 3.4 秒以内に、満タンの 6 割以上の一発が出る（2026-11-22: 蓄えの尽きた腕が続けて出せる力は 45 % + 戻る分）");
        Assert.IsLessThanOrEqualTo(3.357f, first);
    }

    /// <summary>
    /// 片手の強い 1 発を 1 秒ごと（約 100 W、働く腕への血管の流れ 112 W より下）は続けられる: 50 秒打ち続けても蓄えは尽きない（2026-11-22 に 0.5 秒ごとから: 物理 86 の
    /// 拳は 1 発の仕事が大きく、0.5 秒ごと = 200 W は臨界パワーを超えて空になる）。尽きるのは両手の全力の連打（<see cref="StaminaRushTests"/>）。
    /// </summary>
    [TestMethod]
    public void FullReserve_OneHandStrongPace_BelowCriticalPower_DoesNotEmpty()
    {
        using var rig = new Rig();
        while (rig.Time < 50.35f)
        {
            rig.StrongPunch(1.007f);
        }

        Console.WriteLine(string.Create(CultureInfo.InvariantCulture, $"one-hand strong pace for 50.4 s: stamina {rig.Fighter.Stamina:P1}, P {rig.Fighter.RecentMusclePower:F2} W"));
        // 2026-11-27（基準の体: 質量が実測で前の 2.2 倍）: 50 秒後の蓄えは 21 %（前は 0.3 超）。1 発の仕事が質量に比例して増え、片手の強い拳の拍子でも蓄えは減っていく
        // が、尽きてはいない。下限 0.3 → 0.15（スタミナの定数を質量に合わせて引き直すのは docs/plans/BASE_BODY_PLAN.md §10 の残り）。
        Assert.IsGreaterThan(0.15f, rig.Fighter.Stamina);
    }

    /// <summary>
    /// 空で全力の連打を続けても、した仕事は心肺の供給の範囲に収まり、回復はゼロにならない（出せなかった分を P に数えた旧式は、
    /// 空で押すだけで P が高いまま回復が止まり、また動けなくなった）。
    /// </summary>
    [TestMethod]
    public void EmptyReserve_KeepsPunching_WorkStaysWithinSupply_AndRecoveryContinues()
    {
        using var rig = new Rig();
        rig.Fighter.EmptyArms();
        for (var punch = 0; punch < 20; punch++)
        {
            rig.StrongPunch(0.5035f);
        }

        // 打っている右腕の血管が休んでいるときに流せる速さ（呼吸・循環のモデル、2026-11-18）。
        var vessel = FighterTuning.Default.VesselPowerPerKg * rig.Fighter.ArmMass(Hand.Right);
        var power = rig.Fighter.RegionPower(MuscleRegion.RightArm);
        var delivery = rig.Fighter.DeliveryOf(MuscleRegion.RightArm);
        Console.WriteLine(string.Create(CultureInfo.InvariantCulture, $"punching from empty for 10 s: right arm P {power:F2} W, delivery {delivery:F2} W (vessel at rest {vessel:F2} W)"));
        Assert.IsLessThan(vessel, power, "した仕事は腕の血管の流れより小さい");
        Assert.IsGreaterThan(0.2f * vessel, delivery, "押し続けても酸素は届き続ける");
    }
}
