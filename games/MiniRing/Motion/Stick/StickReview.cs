using System.Globalization;
using System.Numerics;
using System.Text;

namespace MiniRing.Motion.Stick;

/// <summary>警報の重さ。</summary>
public enum StickGrade { Good, Caution, Warning }

/// <summary>
/// 不自然な動きの警報 1 つ（docs/STICK_MOTION.md §10）: 何が（<paramref name="Kind"/>）、どこで（<paramref name="Where"/>: 関節・部位・目標）、いつ（<paramref name="From"/>〜<paramref name="To"/> [s]）、
/// どれだけ（<paramref name="Peak"/>、単位は種類ごと）。
/// </summary>
public sealed record StickAlert(StickGrade Grade, string Kind, string Where, float From, float To, float Peak, string Unit)
{
    public override string ToString() => string.Create(CultureInfo.InvariantCulture,
        $"{(Grade == StickGrade.Warning ? "警告" : "注意")} {Kind}: {Where} {From:0.00}〜{To:0.00} s（最大 {Peak:0.###} {Unit}）");
}

/// <summary>警報と物差しの閾値（ゲームの体の値から）。</summary>
public sealed record StickReviewSettings
{
    /// <summary>脚の関節（股・膝・足首）のサーボの最大の速さ [rad/s]（Fighter の LegServoSpeed）。</summary>
    public float LegSpeed { get; init; } = 10f;

    /// <summary>腕の関節（肩・肘）のサーボの最大の速さ [rad/s]（<see cref="FighterTuning.ArmServoMaxSpeed"/>）。</summary>
    public float ArmSpeed { get; init; } = 40f;

    /// <summary>胴・首・手首（武器）の関節の速さの目安 [rad/s]（サーボの値が無いので、脚の 2 倍）。</summary>
    public float TrunkSpeed { get; init; } = 20f;

    /// <summary>関節の範囲の端から、これ以内を「端に張り付いている」とする [rad]（3°）。</summary>
    public float LimitMargin { get; init; } = 3f * MathF.PI / 180f;

    /// <summary>IK が目標へ届けられなかった距離の閾値 [m]（追う遅れの後の目標との差。体の範囲・形として無理な目標）。</summary>
    public float Reach { get; init; } = 0.01f;

    /// <summary>体の部位どうし・刃と体が入り込む深さの閾値 [m]。</summary>
    public float Penetration { get; init; } = 0.005f;

    /// <summary>両手で握る武器で、もう一方の手の目標が武器の線から外れている距離の閾値 [m]（両手で同じ棒を握れない目標）。</summary>
    public float Grip { get; init; } = 0.01f;

    /// <summary>警報にする最短の長さ [s]（1 歩だけのちらつきを拾わない）。</summary>
    public float MinDuration { get; init; } = 0.05f;

    /// <summary>
    /// 滑らかさの閾値（手先・刃先の 1 本の動き（速さの谷で切る）の、次元の無い躍度の対数 LDLJ。Balasubramanian ら 2015）。これより小さい
    /// （ぎくしゃく）と注意、<see cref="JerkWarning"/> より小さいと警告。この差分の計算では、躍度最小の 1 本の動きが約 −4.9（連続の式の −6 より
    /// 端の分だけ高い）。注意はそれより約 2、警告は約 4 低い所。
    /// </summary>
    public float JerkCaution { get; init; } = -7f;

    public float JerkWarning { get; init; } = -9f;

    public static StickReviewSettings From(FighterTuning tuning) => new() { ArmSpeed = tuning.ArmServoMaxSpeed };
}

/// <summary>
/// 棒人形の動きの「人間らしさ」の見立て（docs/STICK_MOTION.md §10）: 不自然さの警報と、滑らかさ・エネルギーの効率・力の分け方の物差し。
/// 動画のコマを目で見て拾えない不自然さ（無理な力・範囲の端・届かない目標・体の通り抜け・崩れ・ぎくしゃく）を、数と言葉で出す。
/// </summary>
public sealed class StickReview
{
    private StickReview(IReadOnlyList<StickAlert> alerts, IReadOnlyList<(string Name, float Ldlj)> smoothness, float workRate, float kineticShare,
        float braking, float loadSpread, float strain, float score)
    {
        Alerts = alerts;
        Smoothness = smoothness;
        WorkRate = workRate;
        KineticShare = kineticShare;
        Braking = braking;
        LoadSpread = loadSpread;
        Strain = strain;
        Score = score;
    }

    public IReadOnlyList<StickAlert> Alerts { get; }

    /// <summary>末端（手首・刃先・足首・頭）ごとの滑らかさ LDLJ（大きいほど滑らか）。動きの小さい末端は除く。</summary>
    public IReadOnlyList<(string Name, float Ldlj)> Smoothness { get; }

    /// <summary>筋の正の仕事率を、体重 × 重力 × 脚の長さで割った物 [1/s]（同じ技なら小さいほど省エネ）。</summary>
    public float WorkRate { get; }

    /// <summary>運動エネルギーの山 ÷ 筋の正の仕事（筋の仕事のうち、体の動きになった割合。小さいと自分と押し合っている）。</summary>
    public float KineticShare { get; }

    /// <summary>ブレーキの割合: 筋の負の仕事 ÷ 正の仕事（大きいと、出した勢いを自分で止めている）。</summary>
    public float Braking { get; }

    /// <summary>力の偏り: 負担のある歩での「いちばん重い関節の負担 ÷ 全関節の負担の和」の平均（1 に近いほど 1 つの関節に頼る）。</summary>
    public float LoadSpread { get; }

    /// <summary>無理の平均: 関節ごとの負担の 2 乗の和の時間平均。</summary>
    public float Strain { get; }

    /// <summary>人間らしさの点（100 から、警告 15・注意 5・滑らかさ・偏り・無理で引く。0〜100）。</summary>
    public float Score { get; }

    public int Warnings => Alerts.Count(a => a.Grade == StickGrade.Warning);

    public int Cautions => Alerts.Count(a => a.Grade == StickGrade.Caution);

    public static StickReview Of(StickResult result, StickReviewSettings? settings = null)
    {
        ArgumentNullException.ThrowIfNull(result);
        var s = settings ?? new StickReviewSettings();
        var body = result.Body;
        var n = result.Frames.Count;
        var dt = result.Dt;
        var alerts = new List<StickAlert>();

        void Series(string kind, string where, Func<int, float> value, float threshold, float warning, string unit)
        {
            var start = -1;
            var peak = 0f;
            for (var i = 0; i <= n; i++)
            {
                var v = i < n ? value(i) : float.NegativeInfinity;
                if (v > threshold)
                {
                    start = start < 0 ? i : start;
                    peak = MathF.Max(peak, v);
                }
                else if (start >= 0)
                {
                    var duration = (i - start) * dt;
                    if (duration >= s.MinDuration)
                    {
                        var grade = peak > warning || duration > 0.2f ? StickGrade.Warning : StickGrade.Caution;
                        alerts.Add(new StickAlert(grade, kind, where, start * dt, i * dt, peak, unit));
                    }

                    start = -1;
                    peak = 0f;
                }
            }
        }

        // 1. 関節の力が上限を超える（体が目標から遅れる・崩れる所）。
        for (var j = 1; j < body.Segments.Count; j++)
        {
            var joint = j;
            Series("関節の力が上限を超える", body.Segments[j].Name, i => result.Loads[i].Ratio(body, joint), 1f, 1.5f, "倍");
        }

        // 2. 関節が範囲の端に張り付く・3. 関節が速すぎる。
        var local = new Quaternion[n][];
        for (var i = 0; i < n; i++)
        {
            local[i] = new Quaternion[body.Segments.Count];
            for (var j = 1; j < body.Segments.Count; j++)
            {
                var f = result.Frames[i];
                local[i][j] = Quaternion.Normalize(Quaternion.Conjugate(f.Rotations[body.Segments[j].Parent]) * f.Rotations[j]);
            }
        }

        var guardFrame = new StickFrame(body, body.Guard);
        for (var j = 1; j < body.Segments.Count; j++)
        {
            var joint = j;
            var seg = body.Segments[j];
            // ゲームの構えで既に端の近くにいる関節は、構えより端へ進んだ分だけを数える（構えそのものを毎回とがめない）。
            var guardMargin = Margin(seg, Quaternion.Normalize(Quaternion.Conjugate(guardFrame.Rotations[seg.Parent]) * guardFrame.Rotations[j]));
            var allowed = MathF.Min(s.LimitMargin, guardMargin - (1f * MathF.PI / 180f));
            Series("関節が範囲の端に張り付く", seg.Name, i => allowed - Margin(seg, local[i][joint]), 0f, s.LimitMargin, "rad 端の先");
            var limit = seg.Name.StartsWith("thigh", StringComparison.Ordinal) || seg.Name.StartsWith("shin", StringComparison.Ordinal) || seg.Name.StartsWith("foot", StringComparison.Ordinal)
                ? s.LegSpeed
                : seg.Name.StartsWith("upperarm", StringComparison.Ordinal) || seg.Name.StartsWith("forearm", StringComparison.Ordinal) ? s.ArmSpeed : s.TrunkSpeed;
            Series("関節がサーボより速く回る", seg.Name, i => i == 0 ? 0f : StickDynamics.AngularVelocity(local[i - 1][joint], local[i][joint], dt).Length() / limit, 1f, 1.5f, "倍");
        }

        // 4. 目標に届かない（IK が、追う遅れを通した後の目標へ届けられない: 体の範囲・形として無理）。
        var keys = result.Aimed.SelectMany(a => a).Select(t => (t.Segment, t.Rest)).Distinct().ToList();
        foreach (var (segment, rest) in keys)
        {
            var name = segment == StickBody.Ids.Pelvis ? "骨盤" : body.Segments[segment].Name + (rest == body.Segments[segment].Tip ? " の先" : "");
            Series("目標に届かない", name, i =>
            {
                foreach (var t in result.Aimed[i])
                {
                    if (t.Segment == segment && t.Rest == rest)
                    {
                        return Vector3.Distance(result.Frames[i].Point(segment, rest), t.World);
                    }
                }

                return 0f;
            }, s.Reach, 3f * s.Reach, "m");
        }

        // 5. 両手で握る武器: もう一方の手の目標が武器の線（握る手 → 刃先の目標）から外れる（両手で同じ棒を握れない目標）。
        if (body.Armed && body.GripHands.Count == 2)
        {
            Series("両手の目標が同じ武器の上に無い", "左手", i => GripOff(result.Steps[i].Targets, body), s.Grip, 3f * s.Grip, "m");
        }

        // 6. 体の通り抜け: 刃が自分の体へ・手足が体へ入り込む。
        if (body.Armed)
        {
            Series("刃が自分の体を通る", "刃", i => body.SelfCut(result.Frames[i]).Depth, s.Penetration, 3f * s.Penetration, "m");
        }

        foreach (var (a, b) in Pairs(body))
        {
            var (ia, ib) = (a, b);
            Series("体の部位が通り抜ける", $"{body.Segments[a].Name} と {body.Segments[b].Name}", i => Overlap(body, result.Frames[i], ia, ib), s.Penetration, 3f * s.Penetration, "m");
        }

        // 7. 崩れ: 捕捉点が足の支えの外へ（ゲームの立つ力が減る）・床が上へ押さない（宙）。
        var grounded = Enumerable.Range(0, n).Select(i => result.Steps[i].Support.Left || result.Steps[i].Support.Right).ToArray();
        Series("捕捉点が支えの外へ出る", "重心", i => grounded[i] ? result.Loads[i].CaptureOutside : 0f, 0f, 0.02f, "m");
        Series("床が上へ押さない（宙に浮く力が要る）", "全身", i => grounded[i] && float.IsPositiveInfinity(result.Loads[i].ZmpOutside) ? 1f : 0f, 0.5f, 0.5f, "");

        // 物差し: 滑らかさ（LDLJ）。
        var smoothness = new List<(string, float)>();
        var effectors = new List<(string Name, int Segment, Vector3 Point)>
        {
            ("左の手首", StickBody.Ids.ForearmL, body.Segments[StickBody.Ids.ForearmL].Tip),
            ("右の手首", StickBody.Ids.ForearmR, body.Segments[StickBody.Ids.ForearmR].Tip),
            ("頭", StickBody.Ids.Head, body.Segments[StickBody.Ids.Head].Tip),
            ("左の足首", StickBody.Ids.FootL, body.Segments[StickBody.Ids.FootL].Joint),
            ("右の足首", StickBody.Ids.FootR, body.Segments[StickBody.Ids.FootR].Joint),
        };
        if (body.Armed)
        {
            effectors.Add(("刃先", StickBody.Ids.Weapon, body.Segments[StickBody.Ids.Weapon].Tip));
        }

        foreach (var (name, segment, rest) in effectors)
        {
            var path = result.Frames.Select(f => f.Point(segment, rest)).ToArray();
            var worst = (Value: float.PositiveInfinity, From: 0, To: 0);
            foreach (var (from, to) in Submovements(path, dt))
            {
                if (Ldlj(path[from..(to + 1)], dt) is { } v && v < worst.Value)
                {
                    worst = (v, from, to);
                }
            }

            if (float.IsFinite(worst.Value))
            {
                smoothness.Add((name, worst.Value));
                if (worst.Value < s.JerkCaution)
                {
                    alerts.Add(new StickAlert(worst.Value < s.JerkWarning ? StickGrade.Warning : StickGrade.Caution, "ぎくしゃくした動き", name, worst.From * dt, worst.To * dt, worst.Value, "LDLJ"));
                }
            }
        }

        // 物差し: エネルギー。
        var duration = MathF.Max(dt, (n - 1) * dt);
        var legLength = Vector3.Distance(body.Segments[StickBody.Ids.ThighL].Joint, body.Segments[StickBody.Ids.FootL].Joint);
        var positive = result.Work;
        var negative = result.Loads.Sum(l => l.Power.Sum(p => MathF.Max(0f, -p))) * dt;
        var workRate = positive / (duration * body.TotalMass * 9.81f * legLength);
        var kinetic = 0f;
        for (var i = 1; i < n - 1; i++)
        {
            var e = 0f;
            for (var j = 0; j < body.Segments.Count; j++)
            {
                var v = (result.Frames[i + 1].Center(j) - result.Frames[i - 1].Center(j)) / (2f * dt);
                e += 0.5f * body.Segments[j].Mass * v.LengthSquared();
            }

            kinetic = MathF.Max(kinetic, e);
        }

        var kineticShare = positive > 1e-6f ? kinetic / positive : 0f;
        var braking = positive > 1e-6f ? negative / positive : 0f;

        // 物差し: 力の分け方。
        var spreads = new List<float>();
        foreach (var load in result.Loads)
        {
            var ratios = Enumerable.Range(1, body.Segments.Count - 1).Select(j => load.Ratio(body, j)).ToArray();
            var sum = ratios.Sum();
            if (sum > 0.2f)
            {
                spreads.Add(ratios.Max() / sum);
            }
        }

        var loadSpread = spreads.Count > 0 ? spreads.Average() : 0f;
        if (loadSpread > 0.5f)
        {
            alerts.Add(new StickAlert(loadSpread > 0.7f ? StickGrade.Warning : StickGrade.Caution, "力が 1 つの関節に偏る", "全身", 0f, (n - 1) * dt, loadSpread, "の割合"));
        }

        // 範囲の端に始めから終わりまで張り付くのは、動きではなく構え（姿勢）の問題: 「構えから続く」として注意に下げて分ける（毎回の構えをとがめて、動きの警報が埋もれない）。
        // 届かない目標・通り抜けは、ずっと続いても警告のまま（形として無理な目標）。
        var total = (n - 1) * dt;
        for (var k = 0; k < alerts.Count; k++)
        {
            var a = alerts[k];
            if (a.Kind == "関節が範囲の端に張り付く" && a.From <= dt && a.To >= 0.9f * total)
            {
                alerts[k] = a with { Grade = StickGrade.Caution, Kind = "（構えから続く）" + a.Kind };
            }
        }

        var strain = result.Strain / duration;
        var worstJerk = smoothness.Count > 0 ? smoothness.Min(x => x.Item2) : 0f;
        var score = 100f - (15f * alerts.Count(a => a.Grade == StickGrade.Warning)) - (5f * alerts.Count(a => a.Grade == StickGrade.Caution))
            - (10f * MathF.Max(0f, s.JerkCaution - worstJerk)) - (20f * MathF.Max(0f, loadSpread - 0.4f)) - (10f * strain);
        return new StickReview([.. alerts.OrderByDescending(a => a.Grade).ThenBy(a => a.From)], smoothness, workRate, kineticShare, braking, loadSpread, strain, Math.Clamp(score, 0f, 100f));
    }

    /// <summary>人に読める形の見立て（物差しと警報の一覧）。</summary>
    public string Report()
    {
        var sb = new StringBuilder();
        sb.Append(CultureInfo.InvariantCulture, $"人間らしさ {Score:0} 点（警告 {Warnings}・注意 {Cautions}）");
        sb.AppendLine();
        sb.Append(CultureInfo.InvariantCulture, $"- 滑らかさ LDLJ（1 本の動きごとの最悪。大きいほど滑らか、躍度最小で −4.9、注意 −7・警告 −9）: {string.Join("・", Smoothness.Select(x => $"{x.Name} {x.Ldlj:0.0}"))}");
        sb.AppendLine();
        sb.Append(CultureInfo.InvariantCulture, $"- エネルギー: 仕事率 {WorkRate:0.00}/s（体重 × g × 脚の長さで割る）・動きになった割合 {KineticShare:0.00}・ブレーキ {Braking:0.00}");
        sb.AppendLine();
        sb.Append(CultureInfo.InvariantCulture, $"- 力の分け方: 偏り {LoadSpread:0.00}（1 つの関節が負う割合）・無理 {Strain:0.000}");
        sb.AppendLine();
        foreach (var alert in Alerts)
        {
            sb.Append("- ").Append(alert).AppendLine();
        }

        return sb.ToString();
    }

    /// <summary>関節の範囲の端までの余り [rad]（玉は振れ・ねじれの小さい方、蝶番は角の両端の小さい方）。</summary>
    private static float Margin(StickSegment s, Quaternion local)
    {
        switch (s.Kind)
        {
            case StickJointKind.Hinge:
                var angle = StickMath.SignedAngle(StickMath.SwingTwist(local, s.Axis).Twist, s.Axis);
                return MathF.Min(angle - s.TwistMin, s.TwistMax - angle);
            case StickJointKind.Ball:
                var (swing, twist) = StickMath.SwingTwist(local, s.Axis);
                var t = StickMath.SignedAngle(twist, s.Axis);
                return MathF.Min(s.SwingLimit - StickMath.Angle(swing), MathF.Min(t - s.TwistMin, s.TwistMax - t));
            default:
                return float.PositiveInfinity;
        }
    }

    /// <summary>両手持ちの武器で、左手の目標が「右手の目標 → 刃先の目標」の線から外れた距離（目標が揃わない歩は 0）。</summary>
    private static float GripOff(IReadOnlyList<StickTarget> targets, StickBody body)
    {
        StickTarget? right = null;
        StickTarget? left = null;
        StickTarget? tip = null;
        foreach (var t in targets)
        {
            if (t.Segment == StickBody.Ids.ForearmR)
            {
                right = t;
            }
            else if (t.Segment == StickBody.Ids.ForearmL)
            {
                left = t;
            }
            else if (t.Segment == StickBody.Ids.Weapon)
            {
                tip = t;
            }
        }

        if (right is not { } r || left is not { } l || tip is not { } w)
        {
            return 0f;
        }

        var axis = w.World - r.World;
        if (axis.LengthSquared() < 1e-9f)
        {
            return 0f;
        }

        axis = Vector3.Normalize(axis);
        var d = l.World - r.World;
        return (d - (Vector3.Dot(d, axis) * axis)).Length();
    }

    /// <summary>通り抜けを調べる部位の組: 親子・同じ関節を共にする組・握った武器を除く、手足と胴・頭、左右の手足どうし。</summary>
    private static IEnumerable<(int A, int B)> Pairs(StickBody body)
    {
        int[] arms = [StickBody.Ids.UpperArmL, StickBody.Ids.ForearmL, StickBody.Ids.UpperArmR, StickBody.Ids.ForearmR];
        int[] legs = [StickBody.Ids.ThighL, StickBody.Ids.ShinL, StickBody.Ids.FootL, StickBody.Ids.ThighR, StickBody.Ids.ShinR, StickBody.Ids.FootR];
        int[] trunk = [StickBody.Ids.Chest, StickBody.Ids.Head, StickBody.Ids.Pelvis];
        foreach (var a in arms)
        {
            foreach (var t in trunk.Where(t => body.Segments[a].Parent != t && !(a is StickBody.Ids.UpperArmL or StickBody.Ids.UpperArmR && t == StickBody.Ids.Chest)))
            {
                yield return (a, t);
            }
        }

        foreach (var (a, b) in new[] { (StickBody.Ids.ShinL, StickBody.Ids.ShinR), (StickBody.Ids.FootL, StickBody.Ids.FootR), (StickBody.Ids.ShinL, StickBody.Ids.FootR), (StickBody.Ids.FootL, StickBody.Ids.ShinR) })
        {
            yield return (a, b);
        }

        foreach (var l in legs.Where(l => l is not (StickBody.Ids.ThighL or StickBody.Ids.ThighR)))
        {
            yield return (l, StickBody.Ids.Pelvis);
        }
    }

    /// <summary>部位 a と b（太さのある棒）の入り込む深さ [m]（負なら隙間）。</summary>
    internal static float Overlap(StickBody body, StickFrame frame, int a, int b)
    {
        var (pa, qa, ra) = Capsule(body, frame, a);
        var (pb, qb, rb) = Capsule(body, frame, b);
        return ra + rb - StickMath.SegmentDistance(pa, qa, pb, qb);
    }

    private static (Vector3 P, Vector3 Q, float Radius) Capsule(StickBody body, StickFrame frame, int i)
    {
        var s = body.Segments[i];
        var h = s.HalfExtents;
        var longest = MathF.Max(h.X, MathF.Max(h.Y, h.Z));
        var radius = 0.5f * (h.X + h.Y + h.Z - longest);
        if (i is StickBody.Ids.Pelvis or StickBody.Ids.Chest or StickBody.Ids.Head)
        {
            var axis = h.X >= h.Y && h.X >= h.Z ? new Vector3(h.X, 0f, 0f) : h.Y >= h.Z ? new Vector3(0f, h.Y, 0f) : new Vector3(0f, 0f, h.Z);
            var inner = axis * MathF.Max(0f, 1f - (radius / longest));
            return (frame.Point(i, s.Center - inner), frame.Point(i, s.Center + inner), radius);
        }

        // 手足: 関節と先の点を結ぶ線を、両端の丸みの分だけ縮める（隣の部位との継ぎ目で重ならない）。
        var p = frame.Joints[i];
        var q = frame.Tip(i);
        var d = q - p;
        var length = d.Length();
        var trim = length > 2f * radius ? d / length * radius : d * 0.5f;
        return (p + trim, q - trim, radius);
    }

    /// <summary>
    /// 軌跡を 1 本ずつの動きに切る: 速さが山の 20 % を下回る谷で区切り、山が 5 cm/s 以上・6 歩以上の区間を返す。
    /// </summary>
    public static IEnumerable<(int From, int To)> Submovements(IReadOnlyList<Vector3> path, float dt)
    {
        ArgumentNullException.ThrowIfNull(path);
        var n = path.Count;
        if (n < 6)
        {
            yield break;
        }

        var speed = new float[n];
        for (var i = 1; i < n - 1; i++)
        {
            speed[i] = Vector3.Distance(path[i + 1], path[i - 1]) / (2f * dt);
        }

        var floor = 0.2f * speed.Max();
        var start = -1;
        var peak = 0f;
        for (var i = 0; i <= n; i++)
        {
            var moving = i < n && speed[i] > floor;
            if (moving)
            {
                start = start < 0 ? Math.Max(0, i - 1) : start;
                peak = MathF.Max(peak, speed[i]);
            }
            else if (start >= 0)
            {
                var end = Math.Min(n - 1, i);
                if (peak >= 0.05f && end - start >= 6)
                {
                    yield return (start, end);
                }

                start = -1;
                peak = 0f;
            }
        }
    }

    /// <summary>
    /// 軌跡の滑らかさ: 次元の無い躍度の対数 LDLJ = −ln(T³ / v_peak² ∫|躍度|² dt)（速さの山が 5 cm/s に満たない末端は null）。
    /// </summary>
    public static float? Ldlj(IReadOnlyList<Vector3> path, float dt)
    {
        ArgumentNullException.ThrowIfNull(path);
        if (path.Count < 6)
        {
            return null;
        }

        var peak = 0f;
        for (var i = 1; i < path.Count - 1; i++)
        {
            peak = MathF.Max(peak, Vector3.Distance(path[i + 1], path[i - 1]) / (2f * dt));
        }

        if (peak < 0.05f)
        {
            return null;
        }

        var integral = 0.0;
        for (var i = 2; i < path.Count - 2; i++)
        {
            var jerk = (path[i + 2] - (2f * path[i + 1]) + (2f * path[i - 1]) - path[i - 2]) / (2f * dt * dt * dt);
            integral += jerk.LengthSquared() * dt;
        }

        var duration = (path.Count - 1) * dt;
        var value = Math.Pow(duration, 3) / (peak * peak) * integral;
        return value > 0 ? (float)-Math.Log(value) : 0f;
    }
}
