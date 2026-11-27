using System.Numerics;
using MiniRing.Motion;

namespace MiniRing.Bots;

/// <summary>
/// 刃の通り道で切るときの好み（教育の値。技の側だけで、体の強さには触れない: CLAUDE.md §1.8）。
/// 振る面と狙う高さの好みは型（<see cref="SwordMotion"/>）から読み、ここは選び方の重みと時間だけを持つ。
/// </summary>
public sealed record BladeCutStyle
{
    /// <summary>通過点の手前の円弧の長さ [m]（振りかぶりの大きさ = 溜め）。</summary>
    public float Runup { get; init; } = 0.42f;

    /// <summary>通過点から先の振り抜きの長さ [m]。</summary>
    public float Follow { get; init; } = 0.30f;

    /// <summary>今の握りから振りかぶりまでの時間 [s]。</summary>
    public float Preparation { get; init; } = 0.22f;

    /// <summary>振りかぶりから振り抜きまでの時間 [s]（短いほど速い振り）。</summary>
    public float Swing { get; init; } = 0.28f;

    /// <summary>空いている度合い（相手の拳・刃から狙いまでの距離 / 腕の長さ、1 で頭打ち）の重み。</summary>
    public float OpenWeight { get; init; } = 1f;

    /// <summary>型の振る面からの角度のずれ（1 − cos）の重み。大きいほど型の面に忠実。</summary>
    public float PlaneWeight { get; init; } = 1f;

    /// <summary>型の狙う高さからのずれ [腕の長さ] の重み。</summary>
    public float HeightWeight { get; init; } = 1f;

    /// <summary>今の握りから振りかぶりまでの手の移動 [腕の長さ] の重み（前の振りの振り抜きから近い次の振りを好む = 連続の振り）。</summary>
    public float ContinuityWeight { get; init; } = 0.5f;

    /// <summary>型の振る面を、前（体フレームの +Z）を軸に回して試す角度 [rad]。</summary>
    public IReadOnlyList<float> PlaneTurns { get; init; } = [0f, 0.45f, -0.45f, 0.9f, -0.9f];

    /// <summary>刃を通す点を、狙いの中心から自分の側へ戻す距離の候補 [m]（部位の表を通す。浅いほど点が下がる: <see cref="DepthWeight"/>）。</summary>
    public IReadOnlyList<float> Depths { get; init; } = [0f, 0.08f, 0.16f];

    /// <summary>通す点を浅くした距離 [m] あたりの点の減り。</summary>
    public float DepthWeight { get; init; } = 3f;

    /// <summary>刃のどこで通すか（握りから剣先までの割合。物打ち ≈ 0.8〜0.9）。</summary>
    public float ContactFraction { get; init; } = 0.85f;

    /// <summary>
    /// 届かせるために足で詰める距離の候補 [m]。型の足の運びに足して、構えに入る間に前へ歩く。円弧の腕は型より伸びないので
    /// （肩の届く範囲の中だけを振る）、型と同じ間合いから刃を通すには足で詰める。
    /// </summary>
    public IReadOnlyList<float> Steps { get; init; } = [0f, 0.15f, 0.3f, 0.45f];

    /// <summary>詰めた距離 [m] あたりの点の減り（詰めるほど相手の間合いに入る）。</summary>
    public float StepWeight { get; init; } = 0.5f;
}

/// <summary>
/// 型から読んだ振りの好み: 体フレームの振る面の法線・刃の進む向き・狙う高さ（刃の物打ちの高さ）[腕の長さ]・打ちの始めの時刻 [型の秒]。
/// </summary>
public readonly record struct CutPreference(Vector3 Normal, Vector3 Travel, float Height, float Start, float Peak, Vector3 PeakGrip)
{
    /// <summary>型 <paramref name="motion"/> の腰が <paramref name="from"/> から <paramref name="to"/>（型の秒）までに進む量（体フレーム、m）。</summary>
    public static Vector3 Advance(SwordMotion motion, float from, float to, float armReach)
    {
        var sum = Vector3.Zero;
        for (var k = 0; k < motion.Frames.Count; k++)
        {
            var at = k * motion.FrameTime;
            if (at >= from && at < to)
            {
                sum += motion.Frames[k].Velocity * motion.FrameTime;
            }
        }

        return sum * armReach;
    }

    /// <summary>
    /// 型のいちばん速い打ち（<see cref="StrikeFinder"/>）の山での、剣先の進む向きと剣の向きから振る面を読む。打ちが無い型は null。
    /// </summary>
    public static CutPreference? Of(SwordMotion motion)
    {
        var strike = StrikeFinder.Find(motion).OrderByDescending(s => s.PeakSpeed).FirstOrDefault();
        if (strike.PeakSpeed <= 0f)
        {
            return null;
        }

        var frames = motion.Frames;
        var i = Math.Clamp((int)(strike.Peak / motion.FrameTime), 1, frames.Count - 2);
        Vector3 Tip(int k) => frames[k].Right + (frames[k].Blade * 1.4f);
        var travel = Tip(i + 1) - Tip(i - 1);
        var normal = Vector3.Cross(frames[i].Blade, travel);
        if (normal.LengthSquared() < 1e-8f)
        {
            return null;
        }

        normal = Vector3.Normalize(normal);
        travel = Vector3.Normalize(travel - (Vector3.Dot(travel, normal) * normal));
        // 物打ち（剣先から 2 割手前 ≈ BladeCutGeometry の既定の通過点）の高さ。
        var height = (frames[i].Right + (frames[i].Blade * (0.8f * 1.4f))).Y;
        return new CutPreference(normal, travel, height, strike.Start, strike.Peak, frames[i].Right);
    }

    /// <summary>前（+Z）を軸に <paramref name="angle"/> だけ回した面。</summary>
    public CutPreference Turned(float angle)
    {
        var q = Quaternion.CreateFromAxisAngle(Vector3.UnitZ, angle);
        return this with { Normal = Vector3.Transform(Normal, q), Travel = Vector3.Transform(Travel, q) };
    }
}

/// <summary>選んだ切り: 計画は再生器の中。<see cref="Step"/> は届かせるために足で詰める距離 [m]（構えに入る間に前へ運ぶ）。</summary>
public readonly record struct ChosenCut(BodyPartKind Target, float Turn, float Depth, float Step);

/// <summary>
/// どこを・どの面で切るかを状況から選び、通り道は <see cref="BladeCutPlan"/> で作る。狙い（相手の部位）× 振る面（型の面を回した候補）×
/// 通す深さ × 足で詰める距離のすべてに同じ式で点を付け（空き − 面のずれ − 高さのずれ − 手の移動 − 浅さ − 詰め）、届く円弧が作れる最高点を採る。
/// 技の種類で分けない（§1.3）。連続の振り: 計画の始めは今の握り（前の振りの振り抜きの姿勢そのもの）で、そこから近い振りかぶりほど点が高い。
/// </summary>
public static class BladeCutChooser
{
    /// <param name="playback">計画を始める再生器。</param>
    /// <param name="hand">刃を握る手。</param>
    /// <param name="sense">今の感覚（握り・腕の寸法・相手の部位）。</param>
    /// <param name="preference">型から読んだ振る面と高さの好み。</param>
    /// <param name="style">選び方の重みと時間（教育）。</param>
    /// <param name="targets">狙いの候補の部位。</param>
    /// <param name="advance">通過までに型の足が体を運ぶ見込み（体フレーム、m。<see cref="CutPreference.Advance"/>）。</param>
    /// <param name="chosen">選んだ狙い・面・深さ・詰め。</param>
    public static bool TryStart(BladeCutPlayback playback, Hand hand, in FighterSense sense, CutPreference preference, BladeCutStyle style,
        IReadOnlyList<BodyPartKind> targets, Vector3 advance, out ChosenCut chosen)
    {
        chosen = default;
        if (sense.BladeGrip(hand) is not { } grip || sense.ArmReach <= 0f)
        {
            return false;
        }

        var reach = sense.ArmReach;
        var candidates = new List<(float Score, BladeCutRequest Request, ChosenCut Cut)>();
        foreach (var part in targets)
        {
            var world = MotionAim.TargetPoint(sense, part);
            var open = MathF.Min(1f, Cover(sense, part, world) / reach);
            var center = sense.Self.WorldToLocal(world);
            var back = new Vector3(center.X, 0f, center.Z);
            back = back.LengthSquared() > 1e-8f ? -Vector3.Normalize(back) : Vector3.Zero;
            foreach (var depth in style.Depths)
            {
                var local = center + (back * depth);
                foreach (var turn in style.PlaneTurns)
                {
                    var plane = preference.Turned(turn);
                    var request = new BladeCutRequest(local, plane.Travel, plane.Normal, style.Runup, style.Follow, 0f, style.Preparation, style.Swing);
                    // 振りかぶりの握り ≈ 通過点から進む向きの逆へ Runup 戻った所。今の握りからそこまでの移動を短く（連続の振り）。
                    var windup = local - (plane.Travel * style.Runup);
                    var move = Vector3.Distance(grip.Pose.Grip, windup) / reach;
                    var basis = (style.OpenWeight * open)
                        - (style.PlaneWeight * (1f - MathF.Cos(turn)))
                        - (style.HeightWeight * MathF.Abs((local.Y / reach) - preference.Height))
                        - (style.ContinuityWeight * move)
                        - (style.DepthWeight * depth);
                    foreach (var step in style.Steps)
                    {
                        candidates.Add((basis - (style.StepWeight * step), request, new ChosenCut(part, turn, depth, step)));
                    }
                }
            }
        }

        // 並べ替えは安定（同点は狙い・面・深さ・詰めの並びの順）: 決定的（§1.6）。
        foreach (var (_, request, cut) in candidates.OrderByDescending(c => c.Score))
        {
            var forward = new Vector3(request.Point.X, 0f, request.Point.Z);
            forward = forward.LengthSquared() > 1e-8f ? Vector3.Normalize(forward) : Vector3.UnitZ;
            if (playback.TryStart(hand, Moved(sense, advance + (forward * cut.Step)), request, style.ContactFraction))
            {
                chosen = cut;
                return true;
            }
        }

        return false;
    }

    /// <summary>通過の時の肩の見込み: 型の足が運ぶ分と詰める分だけ、両肩を先へ置いて届く円弧を探す（踏み込みながら振る）。</summary>
    private static FighterSense Moved(in FighterSense sense, Vector3 by)
    {
        static Motion.BladeArm? Shift(Motion.BladeArm? arm, Vector3 by) => arm is { } a ? a with { Shoulder = a.Shoulder + by } : null;
        return by == default ? sense : sense with
        {
            SelfBladeArmLeft = Shift(sense.SelfBladeArmLeft, by),
            SelfBladeArmRight = Shift(sense.SelfBladeArmRight, by),
        };
    }

    /// <summary>部位 <paramref name="part"/> が相手の拳・刃からどれだけ離れているか [m]（守られていない度合い）。</summary>
    public static float Cover(in FighterSense sense, BodyPartKind part, Vector3 at)
    {
        var cover = float.MaxValue;
        foreach (var fist in new[] { sense.OpponentLeft.Position, sense.OpponentRight.Position })
        {
            if (fist != default && !(part == BodyPartKind.Forearm && Vector3.Distance(fist, at) < 0.3f * sense.ArmReach))
            {
                cover = MathF.Min(cover, Vector3.Distance(fist, at));
            }
        }

        if (sense.OpponentBlade.Present)
        {
            var (a, b) = (sense.OpponentBlade.Hilt, sense.OpponentBlade.Tip);
            var ab = b - a;
            var t = ab.LengthSquared() > 1e-8f ? Math.Clamp(Vector3.Dot(at - a, ab) / ab.LengthSquared(), 0f, 1f) : 0f;
            cover = MathF.Min(cover, Vector3.Distance(at, a + (ab * t)));
        }

        return cover;
    }
}
