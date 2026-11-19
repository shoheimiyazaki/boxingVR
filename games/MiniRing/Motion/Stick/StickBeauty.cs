using System.Numerics;

namespace MiniRing.Motion.Stick;

/// <summary>
/// 動きの「きれいさ」の関数（docs/STICK_MOTION.md §11）。時刻つき（等間隔 <c>dt</c>）の 3D の点の列を受ける汎用の関数で、棒人形にも物理の記録にも使える。
/// どれも種類での場合分けをしない連続量（CLAUDE.md §1.3）。
/// <list type="bullet">
/// <item>滑らかさ: LDLJ（<see cref="StickReview.Ldlj"/> と同じ式を呼ぶ）、SPARC（速さの波形のスペクトル弧長）、速さの山の数、曲率の変化。</item>
/// <item>断面に乗る度合い: 平面からの距離の RMS ÷ 点の広がり。平面を与えなければ主成分の最小固有値から出す。</item>
/// <item>刃筋: 刃先の速さの向きが、刃の面（刃の向きと刃先の向きが張る面）の中にあるか。</item>
/// </list>
/// </summary>
public static class StickBeauty
{
    /// <summary>軌跡の LDLJ（次元の無い躍度の対数。大きい（0 に近い）ほど滑らか）。式は <see cref="StickReview.Ldlj"/>（Balasubramanian ら 2015）。</summary>
    public static float? Ldlj(IReadOnlyList<Vector3> path, float dt) => StickReview.Ldlj(path, dt);

    /// <summary>点の列から、各点の速さ [m/s]（中央差分、両端は片側差分）。</summary>
    public static float[] Speeds(IReadOnlyList<Vector3> path, float dt)
    {
        ArgumentNullException.ThrowIfNull(path);
        var n = path.Count;
        var speed = new float[n];
        for (var i = 0; i < n; i++)
        {
            var a = Math.Max(0, i - 1);
            var b = Math.Min(n - 1, i + 1);
            speed[i] = b > a ? Vector3.Distance(path[b], path[a]) / ((b - a) * dt) : 0f;
        }

        return speed;
    }

    /// <summary>
    /// SPARC（Spectral Arc Length、Balasubramanian, Melendez-Calderon, Burdet 2015）: 速さの波形のフーリエ振幅（直流で割ってゼロ詰めした物）の、
    /// 周波数（遮断周波数で割った物）に対する弧長に − を付けた値。小さい（負に大きい）ほど波形が複雑で、ぎくしゃくしている。滑らかな 1 つの山は約 −1.4。
    /// 遮断周波数は、振幅が <paramref name="amplitudeThreshold"/> 以上の最も高い周波数（上限 <paramref name="maxCutoffHz"/>）。
    /// </summary>
    public static float? Sparc(IReadOnlyList<float> speed, float dt, float maxCutoffHz = 10f, float amplitudeThreshold = 0.05f, int padLevel = 4)
    {
        ArgumentNullException.ThrowIfNull(speed);
        if (speed.Count < 8 || speed.Max() < 1e-6f)
        {
            return null;
        }

        var size = 1;
        while (size < speed.Count)
        {
            size <<= 1;
        }

        size <<= padLevel;
        var re = new double[size];
        var im = new double[size];
        for (var i = 0; i < speed.Count; i++)
        {
            re[i] = speed[i];
        }

        Fft(re, im);
        var half = size / 2;
        var dc = Math.Sqrt((re[0] * re[0]) + (im[0] * im[0]));
        if (dc < 1e-12)
        {
            return null;
        }

        var df = 1.0 / (size * dt);
        var amplitude = new double[half + 1];
        for (var k = 0; k <= half; k++)
        {
            amplitude[k] = Math.Sqrt((re[k] * re[k]) + (im[k] * im[k])) / dc;
        }

        var kMax = Math.Min(half, (int)Math.Floor(maxCutoffHz / df));
        var kc = 1;
        for (var k = kMax; k >= 1; k--)
        {
            if (amplitude[k] >= amplitudeThreshold)
            {
                kc = k;
                break;
            }
        }

        var fc = kc * df;
        var arc = 0.0;
        for (var k = 0; k < kc; k++)
        {
            var dv = amplitude[k + 1] - amplitude[k];
            arc += Math.Sqrt(((df / fc) * (df / fc)) + (dv * dv));
        }

        return (float)-arc;
    }

    /// <summary>
    /// 速さの山の数: 谷（山の高さから <paramref name="prominence"/> × 最大の速さ以上落ちた所）で区切った山の数。2 つ以上の山は「止まりかけて打ち直す」動き。
    /// </summary>
    public static int SpeedPeaks(IReadOnlyList<float> speed, float prominence = 0.1f)
    {
        ArgumentNullException.ThrowIfNull(speed);
        if (speed.Count == 0)
        {
            return 0;
        }

        var threshold = prominence * speed.Max();
        if (threshold <= 0f)
        {
            return 0;
        }

        var peaks = 0;
        var rising = true;
        var high = speed[0];
        var low = speed[0];
        foreach (var s in speed)
        {
            if (rising)
            {
                if (s > high)
                {
                    high = s;
                }
                else if (high - s >= threshold)
                {
                    peaks += high >= threshold ? 1 : 0;
                    rising = false;
                    low = s;
                }
            }
            else if (s < low)
            {
                low = s;
            }
            else if (s - low >= threshold)
            {
                rising = true;
                high = s;
            }
        }

        if (rising && high >= threshold && high - low >= threshold)
        {
            peaks++;
        }

        return peaks;
    }

    /// <summary>
    /// 最大の速さの山を含む 1 本の動き（谷から谷まで）の範囲 [From, To]（両端を含む）。左右へ、速さが谷から <paramref name="prominence"/> × 最大の速さ以上
    /// 登り返す所（別の山）の手前の谷まで広げる。
    /// </summary>
    public static (int From, int To) MainSubmovement(IReadOnlyList<float> speed, float prominence = 0.1f)
    {
        ArgumentNullException.ThrowIfNull(speed);
        if (speed.Count == 0)
        {
            return (0, -1);
        }

        var peak = 0;
        for (var i = 1; i < speed.Count; i++)
        {
            if (speed[i] > speed[peak])
            {
                peak = i;
            }
        }

        var threshold = prominence * speed[peak];
        var from = peak;
        var low = from;
        for (var i = peak - 1; i >= 0; i--)
        {
            if (speed[i] < speed[low])
            {
                low = i;
            }
            else if (speed[i] - speed[low] >= threshold)
            {
                break;
            }

            from = low;
        }

        var to = peak;
        low = to;
        for (var i = peak + 1; i < speed.Count; i++)
        {
            if (speed[i] < speed[low])
            {
                low = i;
            }
            else if (speed[i] - speed[low] >= threshold)
            {
                break;
            }

            to = low;
        }

        return (from, to);
    }

    /// <summary>
    /// 曲率の変化（次元の無い量）: 軌跡を弧長で等間隔に取り直し、3 点の曲率 κ（Menger）の弧長微分の 2 乗の積分 × L³（L = 軌跡の長さ）。
    /// 直線・円弧（κ が一定）は 0、曲がり方が急に変わる・揺れる軌跡ほど大きい。
    /// </summary>
    public static float CurvatureVariation(IReadOnlyList<Vector3> path, int samples = 48)
    {
        ArgumentNullException.ThrowIfNull(path);
        if (path.Count < 4)
        {
            return 0f;
        }

        var cumulative = new double[path.Count];
        for (var i = 1; i < path.Count; i++)
        {
            cumulative[i] = cumulative[i - 1] + Vector3.Distance(path[i], path[i - 1]);
        }

        var length = cumulative[^1];
        if (length < 1e-6)
        {
            return 0f;
        }

        var resampled = new Vector3[samples];
        var at = 0;
        for (var k = 0; k < samples; k++)
        {
            var target = length * k / (samples - 1);
            while (at < path.Count - 2 && cumulative[at + 1] < target)
            {
                at++;
            }

            var span = cumulative[at + 1] - cumulative[at];
            var u = span > 1e-12 ? (float)Math.Clamp((target - cumulative[at]) / span, 0.0, 1.0) : 0f;
            resampled[k] = CatmullRom(path[Math.Max(0, at - 1)], path[at], path[at + 1], path[Math.Min(path.Count - 1, at + 2)], u);
        }

        var ds = (float)(length / (samples - 1));
        var curvature = new float[samples - 2];
        for (var k = 1; k < samples - 1; k++)
        {
            var a = resampled[k] - resampled[k - 1];
            var b = resampled[k + 1] - resampled[k];
            var c = resampled[k + 1] - resampled[k - 1];
            var denominator = a.Length() * b.Length() * c.Length();
            curvature[k - 1] = denominator > 1e-12f ? 2f * Vector3.Cross(a, b).Length() / denominator : 0f;
        }

        var sum = 0.0;
        for (var k = 1; k < curvature.Length; k++)
        {
            var d = (curvature[k] - curvature[k - 1]) / ds;
            sum += d * d * ds;
        }

        return (float)(sum * length * length * length);
    }

    /// <summary>
    /// 与えた平面（<paramref name="origin"/> を通り法線 <paramref name="normal"/>）からの外れ: 点の平面からの距離の RMS ÷ 点の広がり（重心からの距離の RMS）。
    /// 0 = 全部が平面の上、大きいほど平面から外れて動く（点の列が空間に広がる）。
    /// </summary>
    public static float PlaneMisfit(IReadOnlyList<Vector3> points, Vector3 origin, Vector3 normal)
    {
        ArgumentNullException.ThrowIfNull(points);
        if (points.Count == 0)
        {
            return 0f;
        }

        var n = Vector3.Normalize(normal);
        var centroid = Vector3.Zero;
        foreach (var p in points)
        {
            centroid += p;
        }

        centroid /= points.Count;
        double off = 0;
        double spread = 0;
        foreach (var p in points)
        {
            var d = Vector3.Dot(p - origin, n);
            off += d * d;
            spread += (p - centroid).LengthSquared();
        }

        return spread > 1e-12 ? (float)Math.Sqrt(off / spread) : 0f;
    }

    /// <summary>
    /// 平面を与えないときの外れ: 点の共分散の最小固有値から、最もよく合う平面の RMS ÷ 広がり（= √(最小固有値 ÷ 固有値の和)）と、その平面の法線。
    /// </summary>
    public static (float Misfit, Vector3 Normal) BestFitPlane(IReadOnlyList<Vector3> points)
    {
        ArgumentNullException.ThrowIfNull(points);
        if (points.Count < 3)
        {
            return (0f, Vector3.UnitY);
        }

        var mean = Vector3.Zero;
        foreach (var p in points)
        {
            mean += p;
        }

        mean /= points.Count;
        double xx = 0, xy = 0, xz = 0, yy = 0, yz = 0, zz = 0;
        foreach (var p in points)
        {
            var d = p - mean;
            xx += d.X * d.X;
            xy += d.X * d.Y;
            xz += d.X * d.Z;
            yy += d.Y * d.Y;
            yz += d.Y * d.Z;
            zz += d.Z * d.Z;
        }

        var trace = xx + yy + zz;
        if (trace < 1e-18)
        {
            return (0f, Vector3.UnitY);
        }

        // 対称 3×3 の固有値（三角関数の解）。最小の固有値 λ に対し、(A − λI) の行の外積が固有ベクトル。
        var q = trace / 3.0;
        var p2 = ((xx - q) * (xx - q)) + ((yy - q) * (yy - q)) + ((zz - q) * (zz - q)) + (2 * ((xy * xy) + (xz * xz) + (yz * yz)));
        var pp = Math.Sqrt(p2 / 6.0);
        double lambdaMin;
        if (pp < 1e-18)
        {
            lambdaMin = q;
        }
        else
        {
            var bxx = (xx - q) / pp;
            var byy = (yy - q) / pp;
            var bzz = (zz - q) / pp;
            var bxy = xy / pp;
            var bxz = xz / pp;
            var byz = yz / pp;
            var det = (bxx * ((byy * bzz) - (byz * byz))) - (bxy * ((bxy * bzz) - (byz * bxz))) + (bxz * ((bxy * byz) - (byy * bxz)));
            var r = Math.Clamp(det / 2.0, -1.0, 1.0);
            var phi = Math.Acos(r) / 3.0;
            lambdaMin = q + (2 * pp * Math.Cos(phi + (2 * Math.PI / 3)));
        }

        lambdaMin = Math.Max(0.0, lambdaMin);
        var r0 = new Vector3((float)(xx - lambdaMin), (float)xy, (float)xz);
        var r1 = new Vector3((float)xy, (float)(yy - lambdaMin), (float)yz);
        var r2 = new Vector3((float)xz, (float)yz, (float)(zz - lambdaMin));
        var c01 = Vector3.Cross(r0, r1);
        var c02 = Vector3.Cross(r0, r2);
        var c12 = Vector3.Cross(r1, r2);
        var best = c01;
        if (c02.LengthSquared() > best.LengthSquared())
        {
            best = c02;
        }

        if (c12.LengthSquared() > best.LengthSquared())
        {
            best = c12;
        }

        var normal = best.LengthSquared() > 1e-18f ? Vector3.Normalize(best) : Vector3.UnitY;
        return ((float)Math.Sqrt(lambdaMin / trace), normal);
    }

    /// <summary>
    /// 刃筋の角（速さで重みを付けた平均）[rad]: 刃先の速さの向きの、刃の向き <c>a</c> に垂直な成分 v⊥ が、刃の面（<c>a</c> と刃先の向き <c>e</c> が張る面）の中にあるか。
    /// 刃の面の法線 = a × e（刃の腹の向き）で、角 = asin(|v̂⊥ · 法線|)。0 = 刃先の向きへ（刃の面の中を）切っている、π/2 = 腹で叩いている。
    /// 両刃なので符号は見ない。速さの無い（v⊥ が 1 cm/s 未満の）点は数えない。
    /// </summary>
    public static float EdgeAngle(IReadOnlyList<Vector3> tip, IReadOnlyList<Vector3> axes, IReadOnlyList<Vector3> edges, float dt, int from = 0, int to = -1)
    {
        ArgumentNullException.ThrowIfNull(tip);
        ArgumentNullException.ThrowIfNull(axes);
        ArgumentNullException.ThrowIfNull(edges);
        var last = to < 0 ? tip.Count - 1 : to;
        double sum = 0;
        double weight = 0;
        for (var i = Math.Max(from, 0); i <= last; i++)
        {
            var a = Math.Max(0, i - 1);
            var b = Math.Min(tip.Count - 1, i + 1);
            if (b <= a)
            {
                continue;
            }

            var v = (tip[b] - tip[a]) / ((b - a) * dt);
            var axis = Vector3.Normalize(axes[i]);
            var perpendicular = v - (Vector3.Dot(v, axis) * axis);
            var speed = perpendicular.Length();
            if (speed < 0.01f)
            {
                continue;
            }

            var flat = Vector3.Cross(axis, edges[i]);
            if (flat.LengthSquared() < 1e-12f)
            {
                continue;
            }

            var angle = Math.Asin(Math.Clamp(Math.Abs(Vector3.Dot(perpendicular / speed, Vector3.Normalize(flat))), 0f, 1f));
            sum += angle * speed;
            weight += speed;
        }

        return weight > 0 ? (float)(sum / weight) : 0f;
    }

    /// <summary>Catmull-Rom 補間（p1 → p2 の間、u = 0〜1）。折れ線よりも円弧の曲率を正しく保つ。</summary>
    private static Vector3 CatmullRom(Vector3 p0, Vector3 p1, Vector3 p2, Vector3 p3, float u)
        => 0.5f * ((2f * p1) + ((p2 - p0) * u) + (((2f * p0) - (5f * p1) + (4f * p2) - p3) * u * u) + (((3f * p1) - p0 - (3f * p2) + p3) * u * u * u));

    private static void Fft(double[] re, double[] im)
    {
        var n = re.Length;
        for (int i = 1, j = 0; i < n; i++)
        {
            var bit = n >> 1;
            for (; (j & bit) != 0; bit >>= 1)
            {
                j ^= bit;
            }

            j ^= bit;
            if (i < j)
            {
                (re[i], re[j]) = (re[j], re[i]);
                (im[i], im[j]) = (im[j], im[i]);
            }
        }

        for (var len = 2; len <= n; len <<= 1)
        {
            var angle = -2 * Math.PI / len;
            var wr = Math.Cos(angle);
            var wi = Math.Sin(angle);
            for (var i = 0; i < n; i += len)
            {
                double cr = 1, ci = 0;
                for (var k = 0; k < len / 2; k++)
                {
                    var ur = re[i + k];
                    var ui = im[i + k];
                    var vr = (re[i + k + (len / 2)] * cr) - (im[i + k + (len / 2)] * ci);
                    var vi = (re[i + k + (len / 2)] * ci) + (im[i + k + (len / 2)] * cr);
                    re[i + k] = ur + vr;
                    im[i + k] = ui + vi;
                    re[i + k + (len / 2)] = ur - vr;
                    im[i + k + (len / 2)] = ui - vi;
                    var next = (cr * wr) - (ci * wi);
                    ci = (cr * wi) + (ci * wr);
                    cr = next;
                }
            }
        }
    }
}
