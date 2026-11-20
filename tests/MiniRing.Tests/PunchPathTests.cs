using System.Numerics;
using MiniRing.Bots;

namespace MiniRing.Tests;

/// <summary>
/// 技の経路の時間の形（2026-11-20、LogicVersion 58）: 拳の目標は最小躍度の曲線で動き、速さの山は振り出しの途中にある。
/// 前は目標が経路の上を等速で動き（始めに急に動き出し、終わりで急に止まる）、行程 約 1 腕を技の時間の 3 割で動くだけだったので、
/// 拳の速さの山は 1〜2 m/s で頭打ちだった（拳の速さを決めていたのは腕の力ではなく目標の速さ。docs/BOXING.md 1-7）。
/// </summary>
[TestClass]
public sealed class PunchPathTests
{
    private const float Reach = 0.19f;
    private static readonly Vector3 GuardLocal = new(-0.025f, 0f, 0.07f);
    private static readonly Vector3 AimLocal = new(0f, 0.03f, 0.2f);

    [TestMethod]
    public void MinimumJerk_StartsAndEndsAtRest_AndPeaksInTheMiddle()
    {
        Assert.AreEqual(0f, PunchPath.MinimumJerk(0f), 1e-6f);
        Assert.AreEqual(1f, PunchPath.MinimumJerk(1f), 1e-6f);
        Assert.AreEqual(0.5f, PunchPath.MinimumJerk(0.5f), 1e-6f);
        Assert.AreEqual(0f, PunchPath.MinimumJerk(-0.3f), 1e-6f, "前は 0 に留まる");
        Assert.AreEqual(1f, PunchPath.MinimumJerk(1.4f), 1e-6f, "後は 1 に留まる");
        const float h = 1e-3f;
        float Rate(float u) => (PunchPath.MinimumJerk(u + h) - PunchPath.MinimumJerk(u - h)) / (2f * h);
        Assert.AreEqual(1.875f, Rate(0.5f), 1e-3f, "速さの山は平均の 1.875 倍");
        Assert.IsLessThan(0.05f, Rate(0.02f), "動き出しは静か");
        Assert.IsLessThan(0.05f, Rate(0.98f), "着く所で止まる");
    }

    /// <summary>どの拳の技も（途中で引くフェイントも）、目標の速さの山が振り出し（構え → 打ち抜く先）の途中にあり、始めと着く所では遅い。</summary>
    [TestMethod]
    public void EveryPunchTarget_PeaksInTheMiddleOfTheLaunch()
    {
        foreach (var technique in Enum.GetValues<Technique>())
        {
            var spec = PunchSpec.Of(technique);
            var hand = spec.Lead ? Hand.Left : Hand.Right;
            var arrival = spec.Duration * PunchSpec.ArrivalShare;
            const float dt = 1f / 2400f;
            var speeds = new List<(float T, float Speed)>();
            for (var t = 0f; t < arrival; t += dt)
            {
                var a = spec.Evaluate(hand, t, spec.Duration, GuardLocal, AimLocal, Reach);
                var b = spec.Evaluate(hand, t + dt, spec.Duration, GuardLocal, AimLocal, Reach);
                speeds.Add((t + (0.5f * dt), Vector3.Distance(a, b) / dt));
            }

            var peak = speeds.MaxBy(s => s.Speed);
            Assert.IsGreaterThan(0.2f * arrival, peak.T, $"{technique}: 山は動き出しの直後ではない（{peak.T:0.000} s / {arrival:0.000} s）");
            Assert.IsLessThan(0.8f * arrival, peak.T, $"{technique}: 山は着く直前ではない");
            Assert.IsLessThan(0.25f * peak.Speed, speeds[0].Speed, $"{technique}: 動き出しは山より十分遅い");
            Assert.IsLessThan(0.25f * peak.Speed, speeds[^1].Speed, $"{technique}: 着く所は山より十分遅い");
        }
    }

    /// <summary>
    /// 振り出しの時間: 目標は技の時間の <see cref="PunchSpec.ArrivalShare"/> で打ち抜く先へ着き、構えへ戻り始めるまで（<see cref="PunchSpec.OutboundShare"/>）そこに留まる。
    /// </summary>
    [TestMethod]
    public void Target_ArrivesEarly_HoldsTheExtension_ThenReturnsSmoothly()
    {
        var spec = PunchSpec.Of(Technique.Cross);
        var d = spec.Duration;
        var arrived = spec.Evaluate(Hand.Right, d * PunchSpec.ArrivalShare, d, GuardLocal, AimLocal, Reach);
        var held = spec.Evaluate(Hand.Right, d * PunchSpec.OutboundShare, d, GuardLocal, AimLocal, Reach);
        Assert.IsLessThan(1e-5f, Vector3.Distance(arrived, held), "着いてから戻り始めるまで、目標は打ち抜く先に留まる");
        var justAfter = spec.Evaluate(Hand.Right, d * (PunchSpec.OutboundShare + 0.01f), d, GuardLocal, AimLocal, Reach);
        Assert.IsLessThan(0.002f, Vector3.Distance(held, justAfter), "戻りも静かに動き出す（等速の戻りだと 1% の時間で 2.5% 動いた）");
    }

    /// <summary>
    /// 同じ体・同じ溜め・同じ体の目標で、拳の目標の時間の形だけを、前の等速の経路と今の経路で比べる: 今の経路の方が拳の打ち出しの最高速が速い
    /// （腕の力・バネの固さ・目標の速度の上限は同じ。物理が許す範囲で、目標を先へ速く出した分だけ速い）。
    /// </summary>
    [TestMethod]
    [TestCategory("slow")]
    public void Cross_LaunchesFasterThanTheUniformPath()
    {
        var uniform = PeakFistSpeed(legacy: true);
        var smooth = PeakFistSpeed(legacy: false);
        Assert.IsGreaterThan(uniform * 1.25f, smooth, $"打ち出しの最高速: 等速 {uniform:0.00} m/s → 今 {smooth:0.00} m/s");
    }

    /// <summary>手を下げて立つ相手へ、1.05 腕の間合いまで寄ってから右ストレートを 1 本打ち、打ち出しから 0.2 秒の拳の速さの最大 [m/s]。</summary>
    private static float PeakFistSpeed(bool legacy)
    {
        const float dt = 1f / 240f;
        const float load = 0.08f;
        const float range = 1.05f;
        using var game = new BoxingGame(seed: 1, corners: new BoxingCorners("still", _ => new StandGuard(GuardPosture.LowGuard)));
        var me = game.Player;
        var spec = PunchSpec.Of(Technique.Cross);
        var style = BotCatalog.Boxer;
        float? last = null;
        var settled = 0;
        PunchAction? action = null;
        var peak = 0f;
        for (var i = 0; i < 240 * 8; i++)
        {
            var sense = game.PlayerSense;
            var reach = sense.ArmReach;
            var toward = Vector3.Normalize((game.Cpu.ChestPosition - me.ChestPosition) with { Y = 0f });
            var distance = Vector3.Distance(me.ChestPosition with { Y = 0f }, game.Cpu.ChestPosition with { Y = 0f }) / reach;
            var headLocal = sense.Self.WorldToLocal(sense.SelfHead);
            Vector3 Guard(Hand h) => TechniqueExecution.Guard(h, style.Lead, reach, headLocal, style.GuardHeight, style.GuardWidth, style.GuardReach, 0f);
            var move = Vector3.Zero;
            var right = Guard(Hand.Right);
            var spring = 0f;
            var twist = 0f;
            var head = Vector3.Zero;
            if (action is not { } a)
            {
                var error = distance - range;
                var closing = last is { } l ? (distance - l) / dt : 0f;
                last = distance;
                move = toward * Math.Clamp((2f * error) + (0.6f * closing), -1f, 1f);
                settled = MathF.Abs(error) < 0.04f ? settled + 1 : 0;
                if (settled > 120)
                {
                    action = new PunchAction(Technique.Cross, spec, -load, spec.Duration, false, AimWorld: sense.OpponentHead, Load: load);
                }
            }
            else
            {
                var aim = sense.Self.WorldToLocal(a.AimWorld);
                right = legacy && a.Time >= 0f ? UniformPath(spec, a.Time, Guard(Hand.Right), aim, reach) : TechniqueExecution.Punch(a, Hand.Right, style.Lead, Guard(Hand.Right), aim, reach);
                spring = TechniqueExecution.Spring(a);
                twist = TechniqueExecution.Twist(a, Hand.Right, style.Lead);
                head = TechniqueExecution.HeadShift(a, style.Lead, reach);
                move = toward * (spec.StepIn * TechniqueExecution.Envelope(a));
                if (a.Time is >= 0f and < 0.2f)
                {
                    peak = MathF.Max(peak, me.GloveVelocity(Hand.Right).Length());
                }

                if (a.Time > 0.2f)
                {
                    return peak;
                }

                action = a with { Time = a.Time + dt };
            }

            var face = Vector3.Normalize((game.Cpu.ChestPosition - me.ChestPosition) with { Y = 0f });
            game.Step(new BoxingInput(new FighterCommand(move, face, new HandCommand(Guard(Hand.Left), false, Tuck: style.ElbowTuck),
                new HandCommand(right, false, spring), head, twist)), dt);
        }

        Assert.Fail("間合いに寄れなかった");
        return 0f;
    }

    /// <summary>前の経路（LogicVersion 57 まで）: 構え → 打ち抜く先を、技の時間の 0.6 × 0.5 で等速に動き、残りを等速で構えへ戻す。</summary>
    private static Vector3 UniformPath(PunchSpec spec, float time, Vector3 guard, Vector3 aim, float reach)
    {
        var t = Math.Clamp(time / spec.Duration, 0f, 1f);
        var through = PunchPath.Through(aim, spec.Overshoot, reach);
        var straight = (guard + through) * 0.5f;
        var windup = Vector3.Lerp(straight, PunchPath.Windup(Hand.Right, spec.Lateral, spec.Low, reach), spec.Curve);
        var outbound = PunchPath.Point(guard, windup, through, t / (0.6f * 0.5f));
        return Vector3.Lerp(outbound, guard, MathF.Max(0f, (t - 0.6f) / 0.4f));
    }
}
