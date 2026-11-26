using System.Globalization;
using System.Numerics;

namespace MiniRing.Arena.MotionTools;

/// <summary>見つけた不自然な瞬間（時刻は記録の時刻 [s]）。</summary>
internal sealed record MotionFinding(string Kind, string Where, float From, float To, float Value, string Text);

/// <summary>
/// 動きの記録（<see cref="MotionTrace"/>）から、人の目で「おかしい」と言われてきた瞬間を物理の定義で探す（docs/plans/MOTION_TOOLS_PLAN.md §B4）。
/// 閾値は道具の感度（どこから一覧に載せるか）で、試合の計算には入らない。どれも量の単位と、人のふつうの値からの理由を添える。
/// </summary>
internal static class MotionAudit
{
    /// <summary>
    /// 関節の隙間 [m]: 関節の両側の点がこれ以上離れたら「つなぎ目が開いた」。人の関節は開かない（関節包と靭帯で 1 cm 未満）。拘束のばねの普段のずれは数 mm で、
    /// 3 cm はすねの長さ（約 43 cm）の 7%: 目で見て「脚が外れた」と分かり始める大きさ。人の計測（motionhuman）の骨の長さのぶれは最大 8.6 cm あるが、
    /// これは皮膚の印から関節の中心を推す計測のぶれで、体の隙間ではないので閾値の根拠にしない（2026-11-26 見直し、値は据え置き）。
    /// </summary>
    public const float GapMetres = 0.03f;

    /// <summary>引っかかり: 足が床に着いたまま、股関節から足首までが脚の長さのこの割合以上に伸び、腰がその足から離れていく。</summary>
    public const float SnagStretch = 0.97f;

    /// <summary>引っかかりの腰の離れる速さ [m/s]（ゆっくりした構えの伸びを除く）。</summary>
    public const float SnagPelvisSpeed = 0.3f;

    /// <summary>
    /// 滑り: 床に着いた点が床に沿ってこれ以上の速さで動く [m/s]。すり足は設計どおり床に着いたまま運ぶ（docs/BOXING.md「足首は PD の力で目標へ引く」）。
    /// 人の値（motionhuman、極真の蹴り 18 本、床から 2 cm 以内の足の印）: 床に着いた足の速さの 95 パーセンタイル 1.75 m/s、99 パーセンタイル 2.6 m/s
    /// （軸足の回転・踏み替えの瞬間を含む）。人が 20 回に 1 回しか出さない速さ（p95）から上を載せる（2026-11-26、1.5 → 1.75）。
    /// </summary>
    public const float SlideSpeed = 1.75f;

    /// <summary>震え: 0.1 s の窓に加速度の向きの入れ替わりがこれ以上（30 Hz 以上の振動。人の生理的な震えは 8〜12 Hz で小さい）。</summary>
    public const int JitterFlips = 6;

    /// <summary>震えとして数える加速度の大きさ [m/s²]（小さな数値の揺れを除く）。</summary>
    public const float JitterAcceleration = 30f;

    /// <summary>急な加速 [m/s²]: 当たりの前後でないのにこれを超えた（人の拳・足の加速はふつう 500 m/s² 以下、当たりの衝撃は除く）。</summary>
    public const float SpikeAcceleration = 1500f;

    /// <summary>支えの外の重心 [m]: 接地の点の凸包からこれ以上外へ、<see cref="SupportSeconds"/> 以上（歩く・踏み込む一瞬は外れて当たり前）。</summary>
    public const float SupportOutside = 0.05f;

    public const float SupportSeconds = 0.3f;

    /// <summary>浮いた: 立っているのにどちらの足も床に触れない時間 [s]（跳ぶ技でなければ不自然）。</summary>
    public const float AirborneSeconds = 0.25f;

    /// <summary>追えない: 手の目標と実際の手の距離が腕の長さのこの割合以上、<see cref="TrackingSeconds"/> 以上。</summary>
    public const float TrackingReach = 0.4f;

    public const float TrackingSeconds = 0.25f;

    /// <summary>左右の取り違え: 左手の目標が右手の方に、右手の目標が左手の方に、この距離 [m] 以上近い。</summary>
    public const float SwapMargin = 0.1f;

    /// <summary>記録を調べて、見つけた瞬間を時刻の順に返す。</summary>
    public static IReadOnlyList<MotionFinding> Run(MotionTrace trace)
    {
        ArgumentNullException.ThrowIfNull(trace);
        var frames = trace.Frames;
        var dt = trace.Dt;
        var found = new List<MotionFinding>();
        if (frames.Count < 4)
        {
            return found;
        }

        var standing = frames.Select(f => !f.Down).ToArray();
        string T(float x) => x.ToString("0.00", CultureInfo.InvariantCulture);
        void Add(string kind, string where, (int Start, int End) run, float value, string text)
            => found.Add(new MotionFinding(kind, where, frames[run.Start].Time, frames[run.End].Time, value, text));

        // 関節の隙間。
        foreach (var run in MotionMetrics.Runs([.. frames.Select(f => f.MaxGap > GapMetres)], dt, 0.05f))
        {
            var worst = frames.Skip(run.Start).Take(run.End - run.Start + 1).MaxBy(f => f.MaxGap)!;
            Add("gap", worst.GapJoint, run, worst.MaxGap, $"{worst.GapJoint} の関節が {T(100f * worst.MaxGap)} cm 開く");
        }

        // 足の引っかかり・滑り。
        var pelvis = trace.Series("pelvis");
        var pelvisVelocity = MotionMetrics.Velocities(pelvis, dt);
        foreach (var (side, hip, ankle, heel, toe, heelContact, toeContact) in new[] { ("左足", "hipL", "ankleL", "heelL", "toeL", 0, 1), ("右足", "hipR", "ankleR", "heelR", "toeR", 2, 3) })
        {
            var hips = trace.Series(hip);
            var ankles = trace.Series(ankle);
            var snag = new bool[frames.Count];
            for (var i = 0; i < frames.Count; i++)
            {
                var planted = frames[i].Contacts[heelContact] || frames[i].Contacts[toeContact];
                var away = ankles[i] - pelvis[i];
                away.Y = 0f;
                var leaving = away.LengthSquared() < 1e-8f ? 0f : -Vector3.Dot(pelvisVelocity[i], Vector3.Normalize(away));
                snag[i] = standing[i] && planted && Vector3.Distance(hips[i], ankles[i]) >= SnagStretch * trace.LegLength && leaving > SnagPelvisSpeed;
            }

            foreach (var run in MotionMetrics.Runs(snag, dt, 0.08f))
            {
                Add("snag", side, run, (run.End - run.Start + 1) * dt, $"{side}が床に残ったまま脚が伸び切り、腰が離れていく（引っかかり）");
            }

            foreach (var (point, contact, label) in new[] { (heel, heelContact, "かかと"), (toe, toeContact, "つま先") })
            {
                var series = trace.Series(point);
                var v = MotionMetrics.Velocities(series, dt);
                var slide = new bool[frames.Count];
                for (var i = 0; i < frames.Count; i++)
                {
                    slide[i] = standing[i] && frames[i].Contacts[contact] && new Vector2(v[i].X, v[i].Z).Length() > SlideSpeed;
                }

                foreach (var run in MotionMetrics.Runs(slide, dt, 0.08f))
                {
                    var peak = Enumerable.Range(run.Start, run.End - run.Start + 1).Max(i => new Vector2(v[i].X, v[i].Z).Length());
                    Add("slide", side + label, run, peak, $"{side}の{label}が床に着いたまま {T(peak)} m/s で滑る");
                }
            }
        }

        // 四足の後ろ脚の滑り（引っかかりは腿の付け根の点が無いので見ない）。
        if (trace.Quadruped)
        {
            foreach (var (point, contact, label) in new[] { ("rheelL", 4, "左後ろ脚のかかと"), ("rtoeL", 5, "左後ろ脚のつま先"), ("rheelR", 6, "右後ろ脚のかかと"), ("rtoeR", 7, "右後ろ脚のつま先") })
            {
                var v = MotionMetrics.Velocities(trace.Series(point), dt);
                var slide = frames.Select((f, i) => standing[i] && f.Contacts[contact] && new Vector2(v[i].X, v[i].Z).Length() > SlideSpeed).ToArray();
                foreach (var run in MotionMetrics.Runs(slide, dt, 0.08f))
                {
                    var peak = Enumerable.Range(run.Start, run.End - run.Start + 1).Max(i => new Vector2(v[i].X, v[i].Z).Length());
                    Add("slide", label, run, peak, $"{label}が床に着いたまま {T(peak)} m/s で滑る");
                }
            }
        }

        // 震え・急な加速。
        var hitNear = new bool[frames.Count];
        for (var i = 0; i < frames.Count; i++)
        {
            if (frames[i].Hit is not null)
            {
                for (var k = Math.Max(0, i - (int)(0.06f / dt)); k <= Math.Min(frames.Count - 1, i + (int)(0.06f / dt)); k++)
                {
                    hitNear[k] = true;
                }
            }
        }

        foreach (var name in new[] { "head", "pelvis", "handL", "handR", "tip", "toeL", "toeR" })
        {
            var series = trace.Series(name);
            var flips = MotionMetrics.JitterFlips(series, dt, 0.1f, JitterAcceleration);
            foreach (var run in MotionMetrics.Runs([.. flips.Select((n, i) => n >= JitterFlips && standing[i])], dt, 0.05f))
            {
                Add("jitter", name, run, flips[run.Start..(run.End + 1)].Max(), $"{name} が細かく震える（0.1 s に加速度の向きが {flips[run.Start..(run.End + 1)].Max()} 回入れ替わる）");
            }

            var a = MotionMetrics.Accelerations(series, dt);
            foreach (var run in MotionMetrics.Runs([.. a.Select((x, i) => x.Length() > SpikeAcceleration && !hitNear[i] && standing[i])], dt, 0f))
            {
                var peak = a[run.Start..(run.End + 1)].Max(x => x.Length());
                Add("spike", name, run, peak, $"{name} が当たりも無いのに {T(peak / 9.81f)} G で加速する");
            }
        }

        // 自分を削った。
        for (var i = 0; i < frames.Count; i++)
        {
            if (frames[i].SelfCells > 0)
            {
                Add("self", "weapon", (i, i), frames[i].SelfCells, $"持った物で自分の体を {frames[i].SelfCells} セル削る");
            }
        }

        // 支えの外の重心・浮いた。
        var com = trace.Series("com");
        var support = new float[frames.Count];
        for (var i = 0; i < frames.Count; i++)
        {
            var points = new List<Vector2>();
            for (var k = 0; k < frames[i].Contacts.Length; k++)
            {
                if (frames[i].Contacts[k])
                {
                    var p = frames[i].Points[Array.IndexOf(MotionTrace.PointNames, MotionTrace.ContactNames[k])];
                    points.Add(new Vector2(p.X, p.Z));
                }
            }

            support[i] = MotionMetrics.SupportDistance(new Vector2(com[i].X, com[i].Z), points);
        }

        foreach (var run in MotionMetrics.Runs([.. support.Select((d, i) => standing[i] && d > SupportOutside)], dt, SupportSeconds))
        {
            var worst = support[run.Start..(run.End + 1)].Max();
            Add("balance", "com", run, worst, $"重心が足の支えの外へ {T(100f * worst)} cm 出たまま {T((run.End - run.Start + 1) * dt)} s");
        }

        foreach (var run in MotionMetrics.Runs([.. frames.Select((f, i) => standing[i] && !f.Contacts.Any(c => c))], dt, AirborneSeconds))
        {
            Add("airborne", "feet", run, (run.End - run.Start + 1) * dt, $"立っているのに両足が {T((run.End - run.Start + 1) * dt)} s 床から離れる");
        }

        // 目標を追えない・左右の取り違え。
        var handL = trace.Series("handL");
        var handR = trace.Series("handR");
        foreach (var (label, target, hand) in new[] { ("左手", (Func<MotionSample, Vector3>)(f => f.LeftTarget), handL), ("右手", f => f.RightTarget, handR) })
        {
            var error = frames.Select((f, i) => Vector3.Distance(target(f), hand[i])).ToArray();
            foreach (var run in MotionMetrics.Runs([.. error.Select((e, i) => standing[i] && e > TrackingReach * trace.ArmReach)], dt, TrackingSeconds))
            {
                var worst = error[run.Start..(run.End + 1)].Max();
                Add("tracking", label, run, worst, $"{label}が目標から {T(100f * worst)} cm 離れたまま追えない");
            }
        }

        var swap = frames.Select((f, i) => standing[i]
            && Vector3.Distance(f.LeftTarget, handR[i]) + SwapMargin < Vector3.Distance(f.LeftTarget, handL[i])
            && Vector3.Distance(f.RightTarget, handL[i]) + SwapMargin < Vector3.Distance(f.RightTarget, handR[i])).ToArray();
        foreach (var run in MotionMetrics.Runs(swap, dt, 0.1f))
        {
            Add("swap", "hands", run, (run.End - run.Start + 1) * dt, "左手の目標の近くに右手、右手の目標の近くに左手がある（左右の配線の取り違え）");
        }

        // 倒れた（記録に残す。おかしいかどうかは場面による）。
        foreach (var run in MotionMetrics.Runs([.. frames.Select(f => f.Down)], dt, 0f))
        {
            Add("down", "body", run, (run.End - run.Start + 1) * dt, "倒れている");
        }

        return [.. found.OrderBy(f => f.From)];
    }

    /// <summary>種類ごとの件数の 1 行と、1 件 1 行の一覧。</summary>
    public static string Format(IReadOnlyList<MotionFinding> findings)
    {
        ArgumentNullException.ThrowIfNull(findings);
        var lines = new List<string>
        {
            "findings: " + (findings.Count == 0 ? "none" : string.Join(", ", findings.GroupBy(f => f.Kind).Select(g => $"{g.Key} {g.Count()}"))),
        };
        lines.AddRange(findings.Select(f => string.Create(CultureInfo.InvariantCulture, $"  {f.From,6:0.00}-{f.To,6:0.00} s  {f.Kind,-8} {f.Where,-10} {f.Text}")));
        return string.Join(Environment.NewLine, lines);
    }
}
