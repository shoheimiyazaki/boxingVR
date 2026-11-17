using System.Globalization;
using System.Numerics;
using Aigis.Physics;

namespace MiniRing.Tests;

/// <summary>
/// スタミナのテンポ（2026-11-15、docs/arena/gen13_stamina_tempo.md）: 空になっても回復がすぐ始まり、全力の攻めは続けるうちに空になる。
/// 回復を止める仕事率 P は「実際にした仕事」で数える（出せなかった分は数えない）。目標は指示書
/// docs/plans/CLAUDE_BRIEF_STAMINA_TEMPO_2026-11-15.md の値。
/// </summary>
[TestClass]
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
            _world.AddStaticBox(new Vector3(0f, -0.5f, 0f), new Vector3(8f, 0.5f, 8f));
            Fighter = Fighter.Spawn(_world, FighterTuning.Default, "tempo", Vector3.Zero, 0f);
            _left = AiControl.Guard(Hand.Left, Fighter.ArmReach);
            _guard = AiControl.Guard(Hand.Right, Fighter.ArmReach);
            for (var i = 0; i < 480; i++)
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

        /// <summary>強い 1 発（拳の目標は腕の長さの 1.6 倍先・0.06 秒で届かせる・出す 0.12 秒）。胸に対する拳の最大速さを返す。</summary>
        public float StrongPunch(float cycleSeconds)
        {
            var peak = 0f;
            var aim = _guard + (new Vector3(0f, 0.05f, 1.6f) * Fighter.ArmReach);
            for (var phase = 0; phase < (int)MathF.Round(cycleSeconds / Dt); phase++)
            {
                var extending = phase < 29;
                Step(extending ? Vector3.Lerp(_guard, aim, MathF.Min(1f, phase * Dt / 0.06f)) : _guard, extending ? 1f : 0f, Vector3.Zero);
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
        return rig.StrongPunch(0.3f);
    }

    /// <summary>
    /// 容量 300 J・休んだときの戻り 20 W（2026-11-17）: 空から構えて立つと、ラウンドの間の 10 秒で 6 割ほど、16 秒でほぼ満タン
    /// （測定 6 秒 39 %・12 秒 79 %）。
    /// </summary>
    [TestMethod]
    public void EmptyReserve_Standing_RoundBreakRecoversMoreThanHalf_FullAfterSixteen()
    {
        using var rig = new Rig();
        rig.Fighter.Stamina = 0f;
        rig.Hold(10f, Vector3.Zero);
        var at10 = rig.Fighter.Stamina;
        rig.Hold(6f, Vector3.Zero);
        var at16 = rig.Fighter.Stamina;
        Console.WriteLine(string.Create(CultureInfo.InvariantCulture, $"standing from empty: 10 s {at10:P1}, 16 s {at16:P1}"));
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
        standing.Fighter.Stamina = 0f;
        standing.Hold(8f, Vector3.Zero);
        using var rig = new Rig();
        rig.Fighter.Stamina = 0f;
        rig.Hold(8f, new Vector3(0f, 0f, -1f));
        Console.WriteLine(string.Create(CultureInfo.InvariantCulture,
            $"from empty for 8 s: retreating {rig.Fighter.Stamina:P1} (P {rig.Fighter.RecentMusclePower:F2} W), standing {standing.Fighter.Stamina:P1}"));
        Assert.IsGreaterThanOrEqualTo(0.8f * standing.Fighter.Stamina, rig.Fighter.Stamina);
    }

    [TestMethod]
    public void EmptyReserve_FirstStrongPunch_ComesWithinTwoSeconds()
    {
        var reference = FullFirstPeak();
        using var rig = new Rig();
        rig.Fighter.Stamina = 0f;
        var first = -1f;
        for (var attempt = 0; attempt < 8 && first < 0f; attempt++)
        {
            var start = rig.Time;
            if (rig.StrongPunch(0.5f) >= 0.8f * reference)
            {
                first = start;
            }
        }

        Console.WriteLine(string.Create(CultureInfo.InvariantCulture, $"first >=80% punch from empty (reference {reference:F2} m/s): at {first:F2} s"));
        Assert.IsGreaterThanOrEqualTo(0f, first, "空から 4 秒以内にまともな一発が出る");
        Assert.IsLessThanOrEqualTo(2f, first);
    }

    /// <summary>
    /// 片手の強い 1 発を 0.3 秒ごと（約 8 W、臨界パワー 9 W より下）は続けられる: 30 秒打ち続けても蓄えは尽きない（2026-11-17）。
    /// 尽きるのは両手の全力の連打（20〜40 W、<see cref="StaminaRushTests"/>）。前の目標は「この打ち方で 12〜20 秒で空」だった。
    /// </summary>
    [TestMethod]
    public void FullReserve_OneHandStrongPace_BelowCriticalPower_DoesNotEmpty()
    {
        using var rig = new Rig();
        while (rig.Time < 30f)
        {
            rig.StrongPunch(0.3f);
        }

        Console.WriteLine(string.Create(CultureInfo.InvariantCulture, $"one-hand strong pace for 30 s: stamina {rig.Fighter.Stamina:P1}, P {rig.Fighter.RecentMusclePower:F2} W"));
        Assert.IsGreaterThan(0.3f, rig.Fighter.Stamina);
    }

    /// <summary>
    /// 空で全力の連打を続けても、した仕事は心肺の供給の範囲に収まり、回復はゼロにならない（出せなかった分を P に数えた旧式は、
    /// 空で押すだけで P が高いまま回復が止まり、また動けなくなった）。
    /// </summary>
    [TestMethod]
    public void EmptyReserve_KeepsPunching_WorkStaysWithinSupply_AndRecoveryContinues()
    {
        using var rig = new Rig();
        rig.Fighter.Stamina = 0f;
        for (var punch = 0; punch < 20; punch++)
        {
            rig.StrongPunch(0.3f);
        }

        var tuning = FighterTuning.Default;
        Console.WriteLine(string.Create(CultureInfo.InvariantCulture, $"punching from empty for 6 s: P {rig.Fighter.RecentMusclePower:F2} W, recovery {rig.Fighter.RecoveryPower:F2} W"));
        Assert.IsLessThan(tuning.StaminaRecoveryPower, rig.Fighter.RecentMusclePower, "した仕事は最大の供給より小さい");
        Assert.IsGreaterThan(0.2f * tuning.StaminaRecoveryPower, rig.Fighter.RecoveryPower, "押し続けても回復は残る");
    }
}
