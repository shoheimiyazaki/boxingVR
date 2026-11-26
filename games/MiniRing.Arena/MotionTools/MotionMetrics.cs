using System.Numerics;

namespace MiniRing.Arena.MotionTools;

/// <summary>
/// 動きの物差し（docs/plans/MOTION_TOOLS_PLAN.md）。どれも点の軌跡（位置の時系列）から出す純粋な関数で、技の種類で分けない（CLAUDE.md §1.3）。
/// 形の物差し（刃筋・目標を追えた量）と自然さの物差し（躍度・運動連鎖の順・頭の揺れ・支えの外の重心）を 1 か所に置き、motionaudit・motioncard・
/// 練習の形の代価（<see cref="FormCost"/>）が同じ式を使う（§1.5）。観測の側の道具で、試合の計算には入らない。
/// </summary>
internal static class MotionMetrics
{
    /// <summary>中央差分の速度 [m/s]（両端は片側差分）。</summary>
    public static Vector3[] Velocities(IReadOnlyList<Vector3> p, float dt)
    {
        ArgumentNullException.ThrowIfNull(p);
        var v = new Vector3[p.Count];
        for (var i = 0; i < p.Count; i++)
        {
            var a = Math.Max(0, i - 1);
            var b = Math.Min(p.Count - 1, i + 1);
            v[i] = b == a ? Vector3.Zero : (p[b] - p[a]) / ((b - a) * dt);
        }

        return v;
    }

    /// <summary>速さ [m/s] の時系列。</summary>
    public static float[] Speeds(IReadOnlyList<Vector3> p, float dt) => [.. Velocities(p, dt).Select(v => v.Length())];

    /// <summary>
    /// 無次元の躍度（Hogan と Flash の滑らかさの物差し）: ∫|d³x/dt³|² dt × T⁵ / L²（T は区間の長さ、L は経路の長さ。
    /// 行って戻る動きも同じ式で比べられる）。最小躍度の直線の動き（x = L(10τ³ − 15τ⁴ + 6τ⁵)）で 720。ぎくしゃくするほど大きい。
    /// </summary>
    public static double NormalizedJerk(IReadOnlyList<Vector3> p, float dt)
    {
        ArgumentNullException.ThrowIfNull(p);
        if (p.Count < 5)
        {
            return double.NaN;
        }

        double sum = 0;
        for (var i = 0; i + 3 < p.Count; i++)
        {
            var j = (p[i + 3] - (3f * p[i + 2]) + (3f * p[i + 1]) - p[i]) / (dt * dt * dt);
            sum += j.LengthSquared();
        }

        // 3 階の差分は n − 3 個しか取れないので、平均 × 区間の長さで積分する（端の 3 歩を落とした分の目減りを除く）。
        var duration = (p.Count - 1) * (double)dt;
        sum = sum / (p.Count - 3) * duration;
        var length = PathLength(p);
        return length < 1e-6 ? double.NaN : sum * Math.Pow(duration, 5) / (length * (double)length);
    }

    /// <summary>経路の長さ [m]。</summary>
    public static float PathLength(IReadOnlyList<Vector3> p)
    {
        ArgumentNullException.ThrowIfNull(p);
        var length = 0f;
        for (var i = 1; i < p.Count; i++)
        {
            length += Vector3.Distance(p[i - 1], p[i]);
        }

        return length;
    }

    /// <summary>
    /// 運動連鎖の順（近い所から遠い所へ: 腰 → 胴 → 肩 → 肘 → 手首）。<paramref name="speedsProximalToDistal"/> は体に近い順の、各点の速さの時系列。
    /// 返す値: 各点の速さが最大になった時刻 [s] と、隣どうしで「近い方が先（か同時）に最大」になっている組の割合（1 なら順のとおり、0 なら逆順）。
    /// </summary>
    public static (float[] PeakTimes, float OrderScore) ChainOrder(IReadOnlyList<IReadOnlyList<float>> speedsProximalToDistal, float dt)
    {
        ArgumentNullException.ThrowIfNull(speedsProximalToDistal);
        var peaks = speedsProximalToDistal.Select(s => ArgMax(s) * dt).ToArray();
        if (peaks.Length < 2)
        {
            return (peaks, float.NaN);
        }

        var ordered = 0;
        for (var i = 1; i < peaks.Length; i++)
        {
            ordered += peaks[i] >= peaks[i - 1] - (0.5f * dt) ? 1 : 0;
        }

        return (peaks, ordered / (float)(peaks.Length - 1));
    }

    private static int ArgMax(IReadOnlyList<float> s)
    {
        var best = 0;
        for (var i = 1; i < s.Count; i++)
        {
            best = s[i] > s[best] ? i : best;
        }

        return best;
    }

    /// <summary>加速度 [m/s²]（2 階の中央差分。両端は 0）。</summary>
    public static Vector3[] Accelerations(IReadOnlyList<Vector3> p, float dt)
    {
        ArgumentNullException.ThrowIfNull(p);
        var a = new Vector3[p.Count];
        for (var i = 1; i + 1 < p.Count; i++)
        {
            a[i] = (p[i + 1] - (2f * p[i]) + p[i - 1]) / (dt * dt);
        }

        return a;
    }

    /// <summary>頭の揺れ: 頭の加速度の RMS [m/s²]（目の台が揺れるほど大きい。人は走っても頭をほぼ水平に保つ）。</summary>
    public static float HeadShake(IReadOnlyList<Vector3> head, float dt)
    {
        var a = Accelerations(head, dt);
        return a.Length < 3 ? float.NaN : MathF.Sqrt(a[1..^1].Average(x => x.LengthSquared()));
    }

    /// <summary>
    /// 震え（ジッター）の印: 各時刻を中心にした <paramref name="windowSeconds"/> の窓で、加速度の各軸の符号が大きさ <paramref name="minAcceleration"/> [m/s²] 以上で
    /// 入れ替わった回数の最大。滑らかな動き（窓の長さに比べて遅い振動）は 0〜2、窓に何周期も入る速い振動ほど多い。
    /// </summary>
    public static int[] JitterFlips(IReadOnlyList<Vector3> p, float dt, float windowSeconds, float minAcceleration)
    {
        var a = Accelerations(p, dt);
        var half = Math.Max(1, (int)MathF.Round(windowSeconds / dt / 2f));
        var flips = new int[p.Count];
        for (var i = 0; i < p.Count; i++)
        {
            var most = 0;
            for (var axis = 0; axis < 3; axis++)
            {
                var count = 0;
                var last = 0;
                for (var k = Math.Max(1, i - half); k <= Math.Min(p.Count - 2, i + half); k++)
                {
                    var value = axis == 0 ? a[k].X : axis == 1 ? a[k].Y : a[k].Z;
                    if (MathF.Abs(value) < minAcceleration)
                    {
                        continue;
                    }

                    var sign = MathF.Sign(value);
                    count += last != 0 && sign != last ? 1 : 0;
                    last = sign;
                }

                most = Math.Max(most, count);
            }

            flips[i] = most;
        }

        return flips;
    }

    /// <summary>
    /// 支えの外の距離 [m]（床の面 XZ）: 重心の真下の点 <paramref name="com"/> が、床に触れている点 <paramref name="contacts"/> の凸包の外にどれだけ出ているか。
    /// 中なら負（いちばん近い辺までの距離に − を付ける）。触れている点が無ければ NaN。
    /// </summary>
    public static float SupportDistance(Vector2 com, IReadOnlyList<Vector2> contacts)
    {
        ArgumentNullException.ThrowIfNull(contacts);
        if (contacts.Count == 0)
        {
            return float.NaN;
        }

        var hull = ConvexHull(contacts);
        if (hull.Count == 1)
        {
            return Vector2.Distance(com, hull[0]);
        }

        if (hull.Count == 2)
        {
            return SegmentDistance(com, hull[0], hull[1]);
        }

        var inside = true;
        var nearest = float.MaxValue;
        for (var i = 0; i < hull.Count; i++)
        {
            var a = hull[i];
            var b = hull[(i + 1) % hull.Count];
            var cross = ((b.X - a.X) * (com.Y - a.Y)) - ((b.Y - a.Y) * (com.X - a.X));
            inside &= cross >= 0f;
            nearest = MathF.Min(nearest, SegmentDistance(com, a, b));
        }

        return inside ? -nearest : nearest;
    }

    private static float SegmentDistance(Vector2 p, Vector2 a, Vector2 b)
    {
        var ab = b - a;
        var t = ab.LengthSquared() < 1e-12f ? 0f : Math.Clamp(Vector2.Dot(p - a, ab) / ab.LengthSquared(), 0f, 1f);
        return Vector2.Distance(p, a + (t * ab));
    }

    /// <summary>反時計回りの凸包（Andrew の単調鎖）。</summary>
    public static IReadOnlyList<Vector2> ConvexHull(IReadOnlyList<Vector2> points)
    {
        var sorted = points.Distinct().OrderBy(p => p.X).ThenBy(p => p.Y).ToList();
        if (sorted.Count < 3)
        {
            return sorted;
        }

        static float Cross(Vector2 o, Vector2 a, Vector2 b) => ((a.X - o.X) * (b.Y - o.Y)) - ((a.Y - o.Y) * (b.X - o.X));
        var hull = new List<Vector2>();
        foreach (var pass in new[] { sorted, Enumerable.Reverse(sorted).ToList() })
        {
            var start = hull.Count;
            foreach (var p in pass)
            {
                while (hull.Count >= start + 2 && Cross(hull[^2], hull[^1], p) <= 0f)
                {
                    hull.RemoveAt(hull.Count - 1);
                }

                hull.Add(p);
            }

            hull.RemoveAt(hull.Count - 1);
        }

        return hull;
    }

    /// <summary>
    /// 刃筋のずれ [度]: 速度 <paramref name="velocity"/> が、向き <paramref name="orientation"/> の刃の面（ローカル XY）から外れた角。速さがほぼ 0 なら NaN。
    /// （HandleLab・FormFidelityProbe・FormCost の刃筋の項と同じ定義。）
    /// </summary>
    public static float EdgeOffPlaneDegrees(Vector3 velocity, Quaternion orientation)
    {
        var local = Vector3.Transform(velocity, Quaternion.Conjugate(orientation));
        var length = local.Length();
        return length < 1e-4f ? float.NaN : MathF.Asin(Math.Clamp(MathF.Abs(local.Z) / length, 0f, 1f)) * 180f / MathF.PI;
    }

    /// <summary>3 点の角 [度]（<paramref name="b"/> での a-b-c の角。膝なら股関節・膝・足首で、伸び切って 180）。</summary>
    public static float AngleAt(Vector3 a, Vector3 b, Vector3 c)
    {
        var u = a - b;
        var w = c - b;
        var d = u.Length() * w.Length();
        return d < 1e-8f ? float.NaN : MathF.Acos(Math.Clamp(Vector3.Dot(u, w) / d, -1f, 1f)) * 180f / MathF.PI;
    }

    /// <summary>腰の折れ [度]: 腰 → 胸の線が鉛直から倒れた角（ヒップヒンジ・のけぞりのどちらも正）。</summary>
    public static float TrunkLean(Vector3 pelvis, Vector3 chest) => AngleAt(pelvis + Vector3.UnitY, pelvis, chest);

    /// <summary>
    /// 胴のひねり（X ファクター）[度]: 肩の線と腰の線の、床の面での向きの差（符号付き、−180..180）。打つ前に開き、打ちで戻る。
    /// </summary>
    public static float TorsoTwist(Vector3 leftShoulder, Vector3 rightShoulder, Vector3 leftHip, Vector3 rightHip)
    {
        var s = MathF.Atan2(leftShoulder.Z - rightShoulder.Z, leftShoulder.X - rightShoulder.X);
        var h = MathF.Atan2(leftHip.Z - rightHip.Z, leftHip.X - rightHip.X);
        var d = (s - h) * 180f / MathF.PI;
        return d > 180f ? d - 360f : d < -180f ? d + 360f : d;
    }

    /// <summary>目標を追えた量: 目標と実際の点の距離の RMS [m]。</summary>
    public static float TrackingRms(IReadOnlyList<Vector3> target, IReadOnlyList<Vector3> actual)
    {
        ArgumentNullException.ThrowIfNull(target);
        ArgumentNullException.ThrowIfNull(actual);
        var n = Math.Min(target.Count, actual.Count);
        return n == 0 ? float.NaN : MathF.Sqrt(Enumerable.Range(0, n).Average(i => Vector3.DistanceSquared(target[i], actual[i])));
    }

    /// <summary>
    /// 軌道面: 点の集まりにいちばん合う平面の、面からのずれの RMS [m] と面の法線（共分散の最小固有ベクトル、べき乗法の逆で近似）。
    /// 「同じ軌道面で振る」は RMS が小さいこと。
    /// </summary>
    public static (float Rms, Vector3 Normal) PlaneFit(IReadOnlyList<Vector3> p)
    {
        ArgumentNullException.ThrowIfNull(p);
        if (p.Count < 3)
        {
            return (float.NaN, Vector3.UnitY);
        }

        var mean = p.Aggregate(Vector3.Zero, (s, x) => s + x) / p.Count;
        double xx = 0, xy = 0, xz = 0, yy = 0, yz = 0, zz = 0;
        foreach (var q in p)
        {
            var d = q - mean;
            xx += d.X * d.X; xy += d.X * d.Y; xz += d.X * d.Z; yy += d.Y * d.Y; yz += d.Y * d.Z; zz += d.Z * d.Z;
        }

        // 最小固有値の固有ベクトル = 各軸の候補のうち、残差が最小の法線を細かく探す（3×3 で十分な精度。外部の線形代数に頼らない）。
        var best = Vector3.UnitY;
        var bestValue = double.MaxValue;
        const int Steps = 90;
        for (var i = 0; i <= Steps; i++)
        {
            var theta = Math.PI * i / Steps;
            for (var j = 0; j < 2 * Steps; j++)
            {
                var phi = Math.PI * j / Steps;
                var n = new Vector3((float)(Math.Sin(theta) * Math.Cos(phi)), (float)Math.Cos(theta), (float)(Math.Sin(theta) * Math.Sin(phi)));
                var value = (n.X * n.X * xx) + (n.Y * n.Y * yy) + (n.Z * n.Z * zz) + (2 * n.X * n.Y * xy) + (2 * n.X * n.Z * xz) + (2 * n.Y * n.Z * yz);
                if (value < bestValue)
                {
                    bestValue = value;
                    best = n;
                }
            }
        }

        return ((float)Math.Sqrt(Math.Max(0d, bestValue) / p.Count), best);
    }

    /// <summary>時刻の印の列から、<paramref name="minSeconds"/> 以上続いた区間（始めと終わりの添字）を返す。</summary>
    public static IReadOnlyList<(int Start, int End)> Runs(IReadOnlyList<bool> flags, float dt, float minSeconds)
    {
        ArgumentNullException.ThrowIfNull(flags);
        var runs = new List<(int, int)>();
        var start = -1;
        for (var i = 0; i <= flags.Count; i++)
        {
            var on = i < flags.Count && flags[i];
            if (on && start < 0)
            {
                start = i;
            }
            else if (!on && start >= 0)
            {
                if ((i - start) * dt >= minSeconds - (0.5f * dt))
                {
                    runs.Add((start, i - 1));
                }

                start = -1;
            }
        }

        return runs;
    }
}
