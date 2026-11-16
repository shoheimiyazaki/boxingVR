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

    [TestMethod]
    public void EmptyReserve_Standing_HalfAfterThreeSeconds_FullAfterSix()
    {
        using var rig = new Rig();
        rig.Fighter.Stamina = 0f;
        rig.Hold(3f, Vector3.Zero);
        var at3 = rig.Fighter.Stamina;
        rig.Hold(3f, Vector3.Zero);
        var at6 = rig.Fighter.Stamina;
        Console.WriteLine(string.Create(CultureInfo.InvariantCulture, $"standing from empty: 3 s {at3:P1}, 6 s {at6:P1}"));
        Assert.IsGreaterThanOrEqualTo(0.5f, at3);
        Assert.IsGreaterThanOrEqualTo(0.95f, at6);
    }

    [TestMethod]
    public void EmptyReserve_RetreatingOnFoot_EightyPercentAfterEightSeconds()
    {
        using var rig = new Rig();
        rig.Fighter.Stamina = 0f;
        rig.Hold(8f, new Vector3(0f, 0f, -1f));
        Console.WriteLine(string.Create(CultureInfo.InvariantCulture, $"retreating from empty: 8 s {rig.Fighter.Stamina:P1}, P {rig.Fighter.RecentMusclePower:F2} W"));
        Assert.IsGreaterThanOrEqualTo(0.8f, rig.Fighter.Stamina);
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

    [TestMethod]
    public void FullReserve_AllOutPunching_EmptiesInSixToTenSeconds()
    {
        using var rig = new Rig();
        var emptyAt = -1f;
        for (var punch = 0; punch < 60 && emptyAt < 0f; punch++)
        {
            rig.StrongPunch(0.3f);
            if (rig.Fighter.Stamina < 0.1f)
            {
                emptyAt = rig.Time;
            }
        }

        Console.WriteLine(string.Create(CultureInfo.InvariantCulture, $"all-out punching from full: stamina < 10 % at {emptyAt:F1} s"));
        Assert.IsGreaterThanOrEqualTo(6f, emptyAt);
        Assert.IsLessThanOrEqualTo(10f, emptyAt);
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
