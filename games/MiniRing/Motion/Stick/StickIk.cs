using System.Numerics;

namespace MiniRing.Motion.Stick;

/// <summary>
/// 部位に固定した点（休みの座標 <paramref name="Rest"/>）を、世界の点 <paramref name="World"/> へ運ぶ目標。
/// <paramref name="Kp"/>・<paramref name="Kd"/> が正なら、動き（<see cref="StickMotion"/>）の中でその点は目標を 2 次の遅れで追う
/// （ゲームの体が手・足を目標へ引くバネ・ダンパと同じ値を渡す: <see cref="FighterTuning.HandKp"/>・<see cref="FootworkTuning.FootKp"/>）。
/// 0 なら目標そのものへ（床に貼った足など）。
/// </summary>
public readonly record struct StickTarget(int Segment, Vector3 Rest, Vector3 World, float Weight = 1f, float Kp = 0f, float Kd = 0f);

/// <summary>部位の向き（前 +Z の水平の向き）の目標 [rad]（体の向き・胴のひねり）。</summary>
public readonly record struct StickYaw(int Segment, float Yaw, float Weight = 1f);

/// <summary>
/// IK の重み（docs/STICK_MOTION.md §3）。目標の外れ [m] の 2 乗に対して、休みの姿勢からの外れ [rad]、関節の負担（トルク ÷ 上限）、
/// 重心が足の支えの外へ出た距離 [m]、前の姿勢からの差 [rad] の 2 乗をどれだけ重く見るか。
/// </summary>
public sealed record StickIkSettings
{
    public static StickIkSettings Default { get; } = new();

    /// <summary>休みの姿勢（ゲームの体の構え）へ寄せる重み。小さいほど目標を優先して大きく動く。</summary>
    public float Rest { get; init; } = 0.0004f;

    /// <summary>関節の負担（静力学のトルク ÷ 上限）の重み。中間の関節を、重さを支えやすい所へ置く。</summary>
    public float Effort { get; init; } = 0.0002f;

    /// <summary>重心が着いた足の凸包の外へ出た距離の重み。</summary>
    public float Balance { get; init; } = 4f;

    /// <summary>前の姿勢からの差の重み（動きの中で、歩ごとに解がちらつかない）。</summary>
    public float Previous { get; init; } = 0.002f;

    /// <summary>反復の上限。</summary>
    public int Iterations { get; init; } = 20;
}

/// <summary>
/// 関節の範囲の中で、目標へ届く自然な姿勢を探す（重みつきの最小二乗、Levenberg–Marquardt、数値微分、反復ごとに範囲へ射影）。
/// 床に着いた足は、足の裏の点を今の位置に留める目標として渡す（<see cref="Plant"/>）。
/// </summary>
public sealed class StickIk(StickBody body, StickIkSettings? settings = null)
{
    private readonly StickIkSettings _s = settings ?? StickIkSettings.Default;

    public StickBody Body { get; } = body ?? throw new ArgumentNullException(nameof(body));

    /// <summary>足 <paramref name="leg"/> を、姿勢 <paramref name="frame"/> の今の位置に床へ貼り付ける目標（足の裏の 3 点、床の高さへ）。</summary>
    public static IEnumerable<StickTarget> Plant(StickFrame frame, Hand leg, float weight = 4f, float groundY = 0f)
    {
        ArgumentNullException.ThrowIfNull(frame);
        var sole = frame.Body.SoleOf(leg);
        foreach (var k in new[] { 0, 3, 5 })
        {
            var world = frame.Point(StickBody.Ids.Foot(leg), sole[k]);
            yield return new StickTarget(StickBody.Ids.Foot(leg), sole[k], world with { Y = groundY }, weight);
        }
    }

    /// <summary>
    /// <paramref name="start"/> から解く。<paramref name="support"/> は床に着いている足（重心の支え・負担の計算に使う）。
    /// <paramref name="previous"/> があれば、その姿勢からの差にも重みを掛ける。結果は範囲の中。
    /// </summary>
    public StickPose Solve(StickPose start, IReadOnlyList<StickTarget> targets, (bool Left, bool Right) support,
        IReadOnlyList<StickYaw>? yaws = null, StickPose? previous = null)
    {
        ArgumentNullException.ThrowIfNull(start);
        ArgumentNullException.ThrowIfNull(targets);
        var x = start.Copy();
        x.Project(Body);
        var r = Residuals(x, targets, support, yaws, previous);
        var cost = Dot(r, r);
        var lambda = 1e-3;
        var n = Body.Dof;
        var jac = new double[r.Length, n];
        for (var iteration = 0; iteration < _s.Iterations; iteration++)
        {
            const float H = 1e-3f;
            for (var k = 0; k < n; k++)
            {
                var saved = x.Values[k];
                x.Values[k] = saved + H;
                var rk = Residuals(x, targets, support, yaws, previous);
                x.Values[k] = saved;
                for (var i = 0; i < r.Length; i++)
                {
                    jac[i, k] = (rk[i] - r[i]) / H;
                }
            }

            var improved = false;
            for (var attempt = 0; attempt < 6 && !improved; attempt++)
            {
                var step = Step(jac, r, n, lambda);
                var trial = x.Copy();
                for (var k = 0; k < n; k++)
                {
                    trial.Values[k] += (float)step[k];
                }

                trial.Project(Body);
                var rt = Residuals(trial, targets, support, yaws, previous);
                var ct = Dot(rt, rt);
                if (ct < cost)
                {
                    (x, r, cost) = (trial, rt, ct);
                    lambda = Math.Max(1e-7, lambda * 0.3);
                    improved = true;
                }
                else
                {
                    lambda *= 10.0;
                }
            }

            if (!improved || cost < 1e-12)
            {
                break;
            }
        }

        LastCost = (float)cost;
        return x;
    }

    /// <summary>直前の解の残りの大きさ（重みつきの 2 乗の和）。</summary>
    public float LastCost { get; private set; }

    private float[] Residuals(StickPose pose, IReadOnlyList<StickTarget> targets, (bool Left, bool Right) support, IReadOnlyList<StickYaw>? yaws, StickPose? previous)
    {
        var frame = new StickFrame(Body, pose);
        var list = new List<float>(targets.Count * 3 + (2 * Body.Dof) + Body.Segments.Count + 1);
        foreach (var t in targets)
        {
            var d = (frame.Point(t.Segment, t.Rest) - t.World) * MathF.Sqrt(t.Weight);
            list.Add(d.X);
            list.Add(d.Y);
            list.Add(d.Z);
        }

        if (yaws is not null)
        {
            foreach (var y in yaws)
            {
                var forward = Vector3.Transform(Vector3.UnitZ, frame.Rotations[y.Segment]);
                var yaw = MathF.Atan2(forward.X, forward.Z);
                var diff = MathF.IEEERemainder(yaw - y.Yaw, 2f * MathF.PI);
                list.Add(diff * MathF.Sqrt(y.Weight) * Body.ArmReach);
            }
        }

        var rest = MathF.Sqrt(_s.Rest);
        var guard = Body.Guard.Values;
        for (var k = 6; k < Body.Dof; k++)
        {
            list.Add((pose.Values[k] - guard[k]) * rest);
        }

        if (previous is not null)
        {
            var w = MathF.Sqrt(_s.Previous);
            for (var k = 0; k < Body.Dof; k++)
            {
                list.Add((pose.Values[k] - previous.Values[k]) * w);
            }
        }

        if (_s.Effort > 0f && (support.Left || support.Right))
        {
            var load = StickDynamics.Static(frame, support);
            var w = MathF.Sqrt(_s.Effort);
            for (var j = 1; j < Body.Segments.Count; j++)
            {
                list.Add(load.Ratio(Body, j) * w);
            }
        }

        if (_s.Balance > 0f && (support.Left || support.Right))
        {
            var hull = new SupportPolygon();
            foreach (var leg in new[] { Hand.Left, Hand.Right })
            {
                if (leg == Hand.Left ? support.Left : support.Right)
                {
                    foreach (var p in frame.Sole(leg))
                    {
                        hull.Add(new Vector2(p.X, p.Z));
                    }
                }
            }

            hull.Build();
            var com = frame.CenterOfMass;
            list.Add(MathF.Max(0f, hull.SignedDistance(new Vector2(com.X, com.Z))) * MathF.Sqrt(_s.Balance));
        }

        return [.. list];
    }

    private static double Dot(float[] a, float[] b)
    {
        var sum = 0.0;
        for (var i = 0; i < a.Length; i++)
        {
            sum += (double)a[i] * b[i];
        }

        return sum;
    }

    /// <summary>(JᵀJ + λ diag) Δ = −Jᵀ r をコレスキー分解で解く。</summary>
    private static double[] Step(double[,] jac, float[] r, int n, double lambda)
    {
        var m = r.Length;
        var a = new double[n, n];
        var g = new double[n];
        for (var i = 0; i < n; i++)
        {
            for (var k = 0; k < m; k++)
            {
                g[i] -= jac[k, i] * r[k];
            }

            for (var j = 0; j <= i; j++)
            {
                var sum = 0.0;
                for (var k = 0; k < m; k++)
                {
                    sum += jac[k, i] * jac[k, j];
                }

                a[i, j] = sum;
                a[j, i] = sum;
            }
        }

        for (var i = 0; i < n; i++)
        {
            a[i, i] += lambda * (a[i, i] + 1e-6);
        }

        // コレスキー: A = L Lᵀ（下三角を a に上書き）。
        for (var i = 0; i < n; i++)
        {
            for (var j = 0; j <= i; j++)
            {
                var sum = a[i, j];
                for (var k = 0; k < j; k++)
                {
                    sum -= a[i, k] * a[j, k];
                }

                a[i, j] = i == j ? Math.Sqrt(Math.Max(sum, 1e-12)) : sum / a[j, j];
            }
        }

        var y = new double[n];
        for (var i = 0; i < n; i++)
        {
            var sum = g[i];
            for (var k = 0; k < i; k++)
            {
                sum -= a[i, k] * y[k];
            }

            y[i] = sum / a[i, i];
        }

        var x = new double[n];
        for (var i = n - 1; i >= 0; i--)
        {
            var sum = y[i];
            for (var k = i + 1; k < n; k++)
            {
                sum -= a[k, i] * x[k];
            }

            x[i] = sum / a[i, i];
        }

        return x;
    }
}
