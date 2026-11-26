using System.Globalization;
using System.Numerics;

namespace MiniRing.Arena.MotionTools;

/// <summary>ある時刻の動きの言葉（「0.30 s: 頭が 18 cm 下がる、腰から 25° 前へ折る」）。</summary>
internal sealed record MotionCaption(float Time, string Text);

/// <summary>区間の自然さの物差し（docs/plans/MOTION_TOOLS_PLAN.md §C6）。</summary>
internal sealed record Naturalness(
    string Effector,
    float PeakSpeed,
    float PeakTime,
    double Jerk,
    string[] Chain,
    float[] ChainPeakTimes,
    float ChainOrder,
    float HeadShake,
    float SupportOutsideSeconds,
    float TwistRange,
    float PathPlaneRms,
    float PathDip)
{
    public string Format()
    {
        var c = CultureInfo.InvariantCulture;
        var chain = string.Join(" → ", Chain.Select((n, i) => string.Create(c, $"{n} {ChainPeakTimes[i]:0.00}")));
        return string.Create(c,
            $"effector {Effector}: peak {PeakSpeed:0.00} m/s at {PeakTime:0.00} s, jerk log10 {Math.Log10(Jerk):0.00} (min-jerk 2.86), chain order {ChainOrder:0.00} [{chain}], head shake {HeadShake:0} m/s², CoM outside support {SupportOutsideSeconds:0.00} s, twist range {TwistRange:0}°, path plane rms {100f * PathPlaneRms:0.0} cm, U-dip {100f * PathDip:0.0} cm");
    }
}

/// <summary>
/// 動きの言葉の辞書（docs/MOTION_WORDS.md）: ユーザーのたとえ（万歳・鉄棒をくぐる・U 字・軌道面・腰から折る…）を測れる量に置き換え、
/// 記録の時刻ごとに言葉の説明を付ける。量の定義は MOTION_WORDS.md の表と同じ。
/// </summary>
internal static class MotionWords
{
    /// <summary>説明を付ける間隔 [s]。</summary>
    public const float CaptionStep = 0.1f;

    /// <summary>頭・腰の高さの変化を言葉にする最小 [m]。</summary>
    public const float HeightWord = 0.05f;

    /// <summary>腰の折れ・胴のひねりの変化を言葉にする最小 [度]。</summary>
    public const float AngleWord = 8f;

    /// <summary>手・刃先が前へ出た量を言葉にする最小 [m]。</summary>
    public const float ReachWord = 0.1f;

    /// <summary>
    /// 腰の折れの符号付き [度]: 腰 → 胸の線が鉛直から前（相手の方）へ倒れれば正、後ろ（のけぞり）なら負。
    /// </summary>
    public static float SignedLean(MotionSample f)
    {
        var pelvis = f.Points[2];
        var chest = f.Points[1];
        var lean = MotionMetrics.TrunkLean(pelvis, chest);
        return Vector3.Dot(chest - pelvis, f.Forward) >= 0f ? lean : -lean;
    }

    /// <summary>胴のひねり [度]（<see cref="MotionMetrics.TorsoTwist"/>）。</summary>
    public static float Twist(MotionSample f) => MotionMetrics.TorsoTwist(f.Points[4], f.Points[7], f.Points[10], f.Points[15]);

    /// <summary>万歳: 両手が頭より高い。</summary>
    public static bool Banzai(MotionSample f) => f.Points[6].Y > f.Points[0].Y && f.Points[9].Y > f.Points[0].Y;

    /// <summary>点 <paramref name="index"/> が胸から相手の方へ出ている量 [m]。</summary>
    public static float Forwardness(MotionSample f, int index) => Vector3.Dot(f.Points[index] - f.Points[1], f.Forward);

    /// <summary>記録の全体に、<see cref="CaptionStep"/> ごとの言葉を付ける（変化が小さい時刻は飛ばす）。</summary>
    public static IReadOnlyList<MotionCaption> Captions(MotionTrace trace)
    {
        ArgumentNullException.ThrowIfNull(trace);
        var frames = trace.Frames;
        var list = new List<MotionCaption>();
        if (frames.Count < 2)
        {
            return list;
        }

        var c = CultureInfo.InvariantCulture;
        var stride = Math.Max(1, (int)MathF.Round(CaptionStep / trace.Dt));
        var wasBanzai = false;
        for (var i = stride; i < frames.Count; i += stride)
        {
            var a = frames[i - stride];
            var b = frames[i];
            var words = new List<string>();
            var dHead = b.Points[0].Y - a.Points[0].Y;
            if (MathF.Abs(dHead) >= HeightWord)
            {
                words.Add(string.Create(c, $"頭が {100f * MathF.Abs(dHead):0} cm {(dHead < 0 ? "下がる" : "上がる")}"));
            }

            var dPelvis = b.Points[2].Y - a.Points[2].Y;
            if (MathF.Abs(dPelvis) >= HeightWord)
            {
                words.Add(string.Create(c, $"腰が {100f * MathF.Abs(dPelvis):0} cm {(dPelvis < 0 ? "沈む" : "伸び上がる")}"));
            }

            var dLean = SignedLean(b) - SignedLean(a);
            if (MathF.Abs(dLean) >= AngleWord)
            {
                words.Add(string.Create(c, $"腰から {MathF.Abs(dLean):0}° {(dLean > 0 ? "前へ折る" : "起こす・反る")}（今 {SignedLean(b):0}°）"));
            }

            var dTwist = Wrap(Twist(b) - Twist(a));
            if (MathF.Abs(dTwist) >= AngleWord)
            {
                words.Add(string.Create(c, $"胴を {MathF.Abs(dTwist):0}° {(dTwist > 0 ? "左へ" : "右へ")}ひねる"));
            }

            foreach (var (index, label) in new[] { (6, "左手"), (9, "右手"), (20, "刃先") })
            {
                if (index == 20 && Vector3.Distance(b.Points[20], b.Points[9]) < 0.03f)
                {
                    continue; // 武器が無い（刃先 = 右手）。
                }

                var d = Forwardness(b, index) - Forwardness(a, index);
                if (MathF.Abs(d) >= ReachWord)
                {
                    words.Add(string.Create(c, $"{label}が {100f * MathF.Abs(d):0} cm {(d > 0 ? "前へ出る" : "引かれる")}"));
                }
            }

            foreach (var (heel, toe, label, heelPoint) in new[] { (0, 1, "左足", 13), (2, 3, "右足", 18) })
            {
                var was = a.Contacts[heel] || a.Contacts[toe];
                var now = b.Contacts[heel] || b.Contacts[toe];
                if (was && !now)
                {
                    words.Add($"{label}が浮く");
                }
                else if (!was && now)
                {
                    words.Add(string.Create(c, $"{label}が着く（{100f * Vector3.Dot(b.Points[heelPoint] - a.Points[heelPoint], b.Forward):0} cm 前へ）"));
                }
            }

            var banzai = Banzai(b);
            if (banzai && !wasBanzai)
            {
                words.Add("万歳（両手が頭より上）");
            }

            wasBanzai = banzai;
            var hits = frames.Skip(i - stride + 1).Take(stride).Where(f => f.Hit is not null).Select(f => f.Hit!).Distinct().ToList();
            if (hits.Count > 0)
            {
                words.Add("当たり: " + string.Join(", ", hits));
            }

            if (words.Count > 0)
            {
                list.Add(new MotionCaption(b.Time, string.Join("、", words)));
            }
        }

        return list;
    }

    /// <summary>
    /// 区間 [<paramref name="from"/>, <paramref name="to"/>] の自然さ: いちばん速く動いた末端（手・刃先・つま先）を打つ所とし、その末端の躍度と、
    /// 体に近い所から末端までの速さの山の順（拳・刃: 腰 → 胸 → 肩 → 肘 → 手、蹴り: 腰 → 股関節 → 膝 → 足首 → つま先）を測る。
    /// </summary>
    public static Naturalness Natural(MotionTrace trace, float from, float to, string? effectorHint = null)
    {
        ArgumentNullException.ThrowIfNull(trace);
        var span = trace.Frames.Where(f => f.Time >= from && f.Time <= to).ToList();
        var dt = trace.Dt;
        Vector3[] S(string n)
        {
            var k = Array.IndexOf(MotionTrace.PointNames, n);
            return [.. span.Select(f => f.Points[k])];
        }

        var candidates = new[] { "handL", "handR", "tip", "toeL", "toeR" };
        var effector = effectorHint ?? candidates.MaxBy(n => MotionMetrics.Speeds(S(n), dt).DefaultIfEmpty().Max())!;
        var speeds = MotionMetrics.Speeds(S(effector), dt);
        var peakIndex = speeds.Length == 0 ? 0 : Array.IndexOf(speeds, speeds.Max());
        var side = effector.EndsWith('L') ? "L" : effector.EndsWith('R') ? "R" : (MotionMetrics.Speeds(S("handR"), dt).DefaultIfEmpty().Max() >= MotionMetrics.Speeds(S("handL"), dt).DefaultIfEmpty().Max() ? "R" : "L");
        string[] chain = effector.StartsWith("toe", StringComparison.Ordinal)
            ? ["pelvis", "hip" + side, "knee" + side, "ankle" + side, "toe" + side]
            : ["pelvis", "chest", "shoulder" + side, "elbow" + side, "hand" + side, .. effector == "tip" ? new[] { "tip" } : []];
        // 打ちの前 0.5 s から打ちの山まで（戻りの速さを数えない）。
        var start = Math.Max(0, peakIndex - (int)(0.5f / dt));
        var order = MotionMetrics.ChainOrder([.. chain.Select(n => (IReadOnlyList<float>)MotionMetrics.Speeds(S(n), dt)[start..(peakIndex + 1)])], dt);
        // 躍度は打ちの山の前後 0.3 s（構えの静止を含めると T⁵ が大きくなる）。
        var jStart = Math.Max(0, peakIndex - (int)(0.3f / dt));
        var jEnd = Math.Min(span.Count - 1, peakIndex + (int)(0.3f / dt));
        // 躍度の前に 5 点（記録の間隔 1/120 s で約 40 ms）の移動平均をかける: 物理の 1 歩ごとの接触の細かな揺れを 3 階の差分が何千倍にもするので、
        // 人の動きの計測と同じく 20 Hz ほどより上を落としてから比べる（震えそのものは motionaudit の jitter が見る）。
        var path = Smooth(S(effector), 2)[jStart..(jEnd + 1)];
        var plane = MotionMetrics.PlaneFit(path);
        var outside = 0;
        for (var i = 0; i < span.Count; i++)
        {
            var pts = Enumerable.Range(0, 4).Where(k => span[i].Contacts[k]).Select(k => span[i].Points[Array.IndexOf(MotionTrace.PointNames, MotionTrace.ContactNames[k])]).Select(p => new Vector2(p.X, p.Z)).ToList();
            var com = span[i].Points[3];
            outside += MotionMetrics.SupportDistance(new Vector2(com.X, com.Z), pts) > MotionAudit.SupportOutside ? 1 : 0;
        }

        // 角は ±180° で折り返すので、隣どうしの差を足して伸ばしてから幅を取る。
        var twists = new List<float>();
        foreach (var f in span)
        {
            twists.Add(twists.Count == 0 ? Twist(f) : twists[^1] + Wrap(Twist(f) - twists[^1]));
        }

        return new Naturalness(effector, speeds.DefaultIfEmpty().Max(), span.Count == 0 ? 0f : span[peakIndex].Time,
            MotionMetrics.NormalizedJerk(path, dt), chain, [.. order.PeakTimes.Select(t => t + (span.Count == 0 ? 0f : span[start].Time))], order.OrderScore,
            MotionMetrics.HeadShake(S("head"), dt), outside * dt, twists.Count == 0 ? 0f : twists.Max() - twists.Min(), plane.Rms, Dip(path));
    }

    /// <summary>角の差を −180..180 [度] へ。</summary>
    public static float Wrap(float degrees) => degrees - (360f * MathF.Round(degrees / 360f));

    /// <summary>中心の移動平均（前後 <paramref name="half"/> 点。両端は窓を縮める）。</summary>
    public static Vector3[] Smooth(IReadOnlyList<Vector3> p, int half)
    {
        ArgumentNullException.ThrowIfNull(p);
        var result = new Vector3[p.Count];
        for (var i = 0; i < p.Count; i++)
        {
            var a = Math.Max(0, i - half);
            var b = Math.Min(p.Count - 1, i + half);
            var sum = Vector3.Zero;
            for (var k = a; k <= b; k++)
            {
                sum += p[k];
            }

            result[i] = sum / (b - a + 1);
        }

        return result;
    }

    /// <summary>U 字の深さ [m]: 経路の両端を結ぶ線より、途中がどれだけ下へ垂れたか（「U 字に振る」）。</summary>
    public static float Dip(IReadOnlyList<Vector3> path)
    {
        ArgumentNullException.ThrowIfNull(path);
        if (path.Count < 3)
        {
            return 0f;
        }

        var deepest = 0f;
        for (var i = 1; i < path.Count - 1; i++)
        {
            var t = i / (float)(path.Count - 1);
            var line = path[0].Y + ((path[^1].Y - path[0].Y) * t);
            deepest = MathF.Max(deepest, line - path[i].Y);
        }

        return deepest;
    }
}
