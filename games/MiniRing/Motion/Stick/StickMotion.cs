using System.Globalization;
using System.Numerics;
using System.Text;

namespace MiniRing.Motion.Stick;

/// <summary>動きの 1 歩の指示: 届けたい目標、床に着いている足、向きの目標、外から加わる力。</summary>
public sealed record StickStep(IReadOnlyList<StickTarget> Targets, (bool Left, bool Right) Support, IReadOnlyList<StickYaw>? Yaws = null,
    IReadOnlyList<StickForce>? External = null);

/// <summary>動きを評価した結果（docs/STICK_MOTION.md §5）。</summary>
public sealed class StickResult
{
    public StickResult(StickBody body, float dt, IReadOnlyList<StickFrame> frames, IReadOnlyList<StickLoad> loads, IReadOnlyList<StickStep> steps, IReadOnlyList<float> targetErrors,
        IReadOnlyList<IReadOnlyList<StickTarget>>? aimed = null)
    {
        Body = body;
        Dt = dt;
        Frames = frames;
        Loads = loads;
        Steps = steps;
        TargetErrors = targetErrors;
        Aimed = aimed ?? [.. steps.Select(s => s.Targets)];
    }

    /// <summary>歩ごとに IK へ渡した目標（追う遅れを通した後。床に貼った足を除く）。届かなさ（形として無理な目標）はこれとの差で見る。</summary>
    public IReadOnlyList<IReadOnlyList<StickTarget>> Aimed { get; }

    public StickBody Body { get; }

    public float Dt { get; }

    public IReadOnlyList<StickFrame> Frames { get; }

    public IReadOnlyList<StickLoad> Loads { get; }

    public IReadOnlyList<StickStep> Steps { get; }

    /// <summary>歩ごとの、目標（床に貼った足を除く）の外れの最大 [m]。</summary>
    public IReadOnlyList<float> TargetErrors { get; }

    private IEnumerable<(StickLoad Load, StickStep Step)> Grounded => Loads.Zip(Steps).Where(p => p.Second.Support.Left || p.Second.Support.Right);

    /// <summary>ZMP が足の支えの外へ出た最大 [m]（中なら負）。</summary>
    public float MaxZmpOutside => Grounded.Select(p => p.Load.ZmpOutside).DefaultIfEmpty(float.NegativeInfinity).Max();

    /// <summary>ZMP が外にいた時間 [s]。</summary>
    public float ZmpOutsideSeconds => Grounded.Count(p => p.Load.ZmpOutside > 0f) * Dt;

    /// <summary>捕捉点が足の支えの外へ出た最大 [m]（ゲームの立つ力が減り始める物差し）。</summary>
    public float MaxCaptureOutside => Grounded.Select(p => p.Load.CaptureOutside).DefaultIfEmpty(float.NegativeInfinity).Max();

    /// <summary>捕捉点が外にいた時間 [s]。</summary>
    public float CaptureOutsideSeconds => Grounded.Count(p => p.Load.CaptureOutside > 0f) * Dt;

    /// <summary>横の床反力が摩擦の上限を超えた最大（≤ 0 なら滑らない）。</summary>
    public float MaxSlip => Grounded.Select(p => p.Load.Slip).DefaultIfEmpty(0f).Max();

    /// <summary>関節 <paramref name="segment"/> の負担（トルク ÷ 上限）の最大。</summary>
    public float PeakRatio(int segment) => Loads.Max(l => l.Ratio(Body, segment));

    /// <summary>全関節の負担の最大と、その関節。</summary>
    public (float Ratio, string Joint) PeakLoad => Enumerable.Range(1, Body.Segments.Count - 1).Select(j => (PeakRatio(j), Body.Segments[j].Name)).MaxBy(p => p.Item1);

    /// <summary>筋がした正の仕事 [J]（関節ごとの τ·ω の正の部分の積分）。</summary>
    public float Work => Loads.Sum(l => l.Power.Sum(p => MathF.Max(0f, p))) * Dt;

    /// <summary>無理の量: 関節ごとの負担の 2 乗の和の積分 [s]。</summary>
    public float Strain => Loads.Sum(l => Enumerable.Range(1, Body.Segments.Count - 1).Sum(j => l.Ratio(Body, j) * l.Ratio(Body, j))) * Dt;

    public float MaxTargetError => TargetErrors.DefaultIfEmpty(0f).Max();

    /// <summary>刃が自分の体へ入り込んだ深さの最大 [m]（武器が無ければ −∞）と、その部位の名前（<see cref="StickBody.SelfCut"/>）。</summary>
    public (float Depth, string Part) MaxSelfCut
    {
        get
        {
            var worst = (Depth: float.NegativeInfinity, Segment: -1);
            foreach (var f in Frames)
            {
                var c = Body.SelfCut(f);
                if (c.Depth > worst.Depth)
                {
                    worst = c;
                }
            }

            return (worst.Depth, worst.Segment >= 0 ? Body.Segments[worst.Segment].Name : "-");
        }
    }

    /// <summary>刃が自分の体に入り込んでいた時間 [s]。</summary>
    public float SelfCutSeconds => Frames.Count(f => Body.SelfCut(f).Depth > 0f) * Dt;

    /// <summary>部位の点の速さの最大 [m/s]。</summary>
    public float PeakSpeed(int segment, Vector3 rest)
    {
        var peak = 0f;
        for (var i = 1; i < Frames.Count; i++)
        {
            peak = MathF.Max(peak, Vector3.Distance(Frames[i].Point(segment, rest), Frames[i - 1].Point(segment, rest)) / Dt);
        }

        return peak;
    }

    public string Summary()
    {
        var (ratio, joint) = PeakLoad;
        return string.Create(CultureInfo.InvariantCulture,
            $"ZMP 外れ 最大 {MaxZmpOutside * 100f:0.0} cm・{ZmpOutsideSeconds:0.00} s / 捕捉点 外れ 最大 {MaxCaptureOutside * 100f:0.0} cm・{CaptureOutsideSeconds:0.00} s / 滑り {MaxSlip:0.00} / 負担 最大 {ratio:0.00}（{joint}）/ 仕事 {Work:0.000} J / 無理 {Strain:0.000} / 目標の外れ 最大 {MaxTargetError * 100f:0.0} cm");
    }

    /// <summary>歩ごとの表（CSV）: 時刻・重心・ZMP・捕捉点・外れ・床の縦の力・関節の負担。</summary>
    public string Csv()
    {
        var sb = new StringBuilder("t,com_x,com_y,com_z,zmp_x,zmp_z,zmp_out,cp_x,cp_z,cp_out,ground_y");
        for (var j = 1; j < Body.Segments.Count; j++)
        {
            sb.Append(CultureInfo.InvariantCulture, $",{Body.Segments[j].Name}");
        }

        sb.AppendLine();
        for (var i = 0; i < Frames.Count; i++)
        {
            var c = Frames[i].CenterOfMass;
            var l = Loads[i];
            sb.Append(CultureInfo.InvariantCulture, $"{i * Dt:0.000},{c.X:0.0000},{c.Y:0.0000},{c.Z:0.0000},{l.Zmp.X:0.0000},{l.Zmp.Z:0.0000},{l.ZmpOutside:0.0000},{l.CapturePoint.X:0.0000},{l.CapturePoint.Y:0.0000},{l.CaptureOutside:0.0000},{l.GroundForce.Y:0.00}");
            for (var j = 1; j < Body.Segments.Count; j++)
            {
                sb.Append(CultureInfo.InvariantCulture, $",{l.Ratio(Body, j):0.000}");
            }

            sb.AppendLine();
        }

        return sb.ToString();
    }
}

/// <summary>
/// 動きを棒人形で走らせる: 歩ごとに指示（<see cref="StickStep"/>）を受け、床に着いた足は着いた所に貼り付けたまま IK で解き、
/// 姿勢の列から力学を出す。目標は呼ぶ側が時刻から決める（技の経路と同じ: 目標の時間変化）。
/// </summary>
public static class StickMotion
{
    /// <summary>
    /// 動きの IK の重み: 重心を足の上へ寄せる項・静力学の負担の項は入れない（足が床を離れた歩で、重心が 1 歩で数 cm 瞬間移動した。
    /// 体は重心を瞬時には動かせない: 釣り合いは動きの結果として力学（ZMP・捕捉点）で見る）。
    /// </summary>
    public static StickIkSettings MotionSettings { get; } = StickIkSettings.Default with { Balance = 0f, Effort = 0f };

    /// <summary>
    /// 関節の角を通す低域フィルタの 1 回分の切れ目 [Hz]（前後に 1 回ずつ通す位相の遅れの無いもの。2 回通すので実効の −3 dB は約 0.64 倍の 3.8 Hz）。ゲームの体のサーボ・手足の PD は 3〜5.4 Hz で、
    /// それより速い角の変化（IK の収束の揺らぎ）は体が追えない。2 階の差分（加速度）がその揺らぎで膨らまないように落とす。
    /// </summary>
    public const float SmoothingHz = 5.958f;

    /// <param name="body">体。</param>
    /// <param name="step">時刻と今の姿勢から、その歩の指示を出す。</param>
    /// <param name="seconds">長さ [s]。</param>
    /// <param name="dt">歩の間隔 [s]。</param>
    /// <param name="settings">IK の重み（null なら <see cref="MotionSettings"/>）。</param>
    /// <param name="start">始めの姿勢（null ならゲームの構え）。</param>
    /// <param name="velocity">始めに体が動いている速さ（世界）[m/s]。2 次の遅れで追う目標（手・頭・骨盤）はこの速さから始まる
    /// （歩いて間合いを詰めながら技を始める: その勢いを止めるのも動きのうち）。</param>
    public static StickResult Run(StickBody body, Func<float, StickFrame, StickStep> step, float seconds, float dt = 1f / 60f,
        StickIkSettings? settings = null, StickPose? start = null, Vector3 velocity = default)
    {
        ArgumentNullException.ThrowIfNull(body);
        ArgumentNullException.ThrowIfNull(step);
        var ik = new StickIk(body, settings ?? MotionSettings);
        var pose = (start ?? body.Guard).Copy();
        var frame = new StickFrame(body, pose);
        var planted = new IReadOnlyList<StickTarget>?[2];
        var frames = new List<StickFrame>();
        var poses = new List<StickPose>();
        var aimed = new List<IReadOnlyList<StickTarget>>();
        var steps = new List<StickStep>();
        var errors = new List<float>();
        var count = (int)MathF.Round(seconds / dt) + 1;
        var follow = new Dictionary<(int, Vector3), (Vector3 X, Vector3 V)>();

        // 始まりの姿勢: 最初の歩の目標そのもの（追う遅れなし）へ、反復を多くして解く。動きはそこから始まる（武器が休みの向きから追い始めない）。
        var first = step(0f, frame);
        var settle = new List<StickTarget>(first.Targets);
        foreach (var leg in new[] { Hand.Left, Hand.Right })
        {
            if (leg == Hand.Left ? first.Support.Left : first.Support.Right)
            {
                settle.AddRange(StickIk.Plant(frame, leg));
            }
        }

        pose = new StickIk(body, (settings ?? MotionSettings) with { Iterations = 80 }).Solve(pose, settle, first.Support, first.Yaws);
        frame = new StickFrame(body, pose);
        for (var i = 0; i < count; i++)
        {
            var t = i * dt;
            var s = step(t, frame);
            var targets = new List<StickTarget>(s.Targets.Count + 6);
            var seen = new HashSet<(int, Vector3)>();
            foreach (var target in s.Targets)
            {
                if (target.Kp <= 0f)
                {
                    targets.Add(target);
                    continue;
                }

                // 目標を 2 次の遅れで追う（ゲームの体のバネ・ダンパ）。初めて出た目標は、その点の今の位置から。
                var key = (target.Segment, target.Rest);
                seen.Add(key);
                var (x, v) = follow.TryGetValue(key, out var state) ? state : (frame.Point(target.Segment, target.Rest), i == 0 ? velocity : Vector3.Zero);
                // 陰解法（固いバネでも発散しない）: v' = (v + dt Kp (目標 − x)) / (1 + dt Kd + dt² Kp)、x' = x + dt v'。
                v = (v + (dt * target.Kp * (target.World - x))) / (1f + (dt * target.Kd) + (dt * dt * target.Kp));
                x += v * dt;
                follow[key] = (x, v);
                targets.Add(target with { World = x });
            }

            foreach (var key in follow.Keys.Where(k => !seen.Contains(k)).ToList())
            {
                follow.Remove(key);
            }
            foreach (var leg in new[] { Hand.Left, Hand.Right })
            {
                var k = leg == Hand.Left ? 0 : 1;
                var on = leg == Hand.Left ? s.Support.Left : s.Support.Right;
                if (!on)
                {
                    planted[k] = null;
                    continue;
                }

                planted[k] ??= [.. StickIk.Plant(frame, leg)];
            }

            aimed.Add([.. targets]);
            foreach (var plant in planted)
            {
                if (plant is not null)
                {
                    targets.AddRange(plant);
                }
            }

            pose = ik.Solve(pose, targets, s.Support, s.Yaws, i > 0 ? pose : null);
            frame = new StickFrame(body, pose);
            poses.Add(pose);
            steps.Add(s);
        }

        Smooth(poses, dt);
        foreach (var (p, s) in poses.Zip(steps))
        {
            var f = new StickFrame(body, p);
            frames.Add(f);
            errors.Add(s.Targets.Count == 0 ? 0f : s.Targets.Max(x => Vector3.Distance(f.Point(x.Segment, x.Rest), x.World)));
        }

        var loads = new List<StickLoad>(frames.Count);
        for (var i = 0; i < frames.Count; i++)
        {
            // 端の歩は差分が取れないので静力学（止まっているとみなす）。
            loads.Add(i == 0 || i == frames.Count - 1
                ? StickDynamics.Static(frames[i], steps[i].Support, steps[i].External)
                : StickDynamics.Solve(frames[i - 1], frames[i], frames[i + 1], dt, steps[i].Support, steps[i].External));
        }

        return new StickResult(body, dt, frames, loads, steps, errors, aimed);
    }

    /// <summary>姿勢の数の列を、前後に 1 回ずつ 1 次の低域フィルタへ通す（<see cref="SmoothingHz"/>）。</summary>
    private static void Smooth(List<StickPose> poses, float dt)
    {
        if (poses.Count < 3)
        {
            return;
        }

        var alpha = 1f - MathF.Exp(-dt * 2f * MathF.PI * SmoothingHz);
        var n = poses[0].Values.Length;
        for (var k = 0; k < n; k++)
        {
            var y = poses[0].Values[k];
            for (var i = 1; i < poses.Count; i++)
            {
                y += alpha * (poses[i].Values[k] - y);
                poses[i].Values[k] = y;
            }

            y = poses[^1].Values[k];
            for (var i = poses.Count - 2; i >= 0; i--)
            {
                y += alpha * (poses[i].Values[k] - y);
                poses[i].Values[k] = y;
            }
        }
    }
}
