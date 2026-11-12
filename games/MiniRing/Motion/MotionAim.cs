using System.Collections.Concurrent;
using System.Numerics;
using MiniRing.Weapons;

namespace MiniRing.Motion;

/// <summary>
/// 狙いの付け方（<see cref="MotionAim"/>）の調整値。
/// </summary>
/// <param name="Gain">落ちる所と狙う所の上下・左右の差のうち、手の通り道をずらす割合（0 = ずらさない、1 = ちょうど狙う所へ）。</param>
/// <param name="Lead">ずらし始めから落ちる時までのうち、ずらしきるまでの割合（小さいほど早くずらしきる）。</param>
/// <param name="MaxShift">手の通り道をずらす上限 [腕の長さ]（これより遠い所は、別の技で狙う）。</param>
/// <param name="ForwardGain">狙う所が相手の胸より手前（腕・拳）・奥にある分を、技を出す間合いへ足す割合。</param>
public sealed record AimTuning(float Gain = 1f, float Lead = 0.6f, float MaxShift = 0.6f, float ForwardGain = 1f)
{
    /// <summary>形の検査をして自分を返す。</summary>
    public AimTuning Validated() =>
        Gain is >= 0f and <= 1.5f && Lead is > 0.05f and <= 1f && MaxShift is >= 0f and <= 1.5f && ForwardGain is >= 0f and <= 1.5f
            ? this
            : throw new ArgumentOutOfRangeException(nameof(Gain), "狙いの調整値が範囲の外です。");
}

/// <summary>
/// 技を相手の決めた部位へ向ける（姿勢を書くのではなく、目標の通り道を変える: 体はその通り道をバネで追う）。
/// <list type="bullet">
/// <item><b>落ちる所</b>: 技の動きから幾何で出す。剣の先がいちばん速い時より後で、物打ち（鍔から切っ先までの 8 割の所）がいちばん前へ届いた
/// フレームの物打ちの位置（体フレーム、踏み込んだ分も足す。<see cref="StrikeDrill.ReachOf"/> と同じ所）。</item>
/// <item><b>前後</b>: 技の間合いは「相手の胸が物打ちの届く所にある」距離。狙う所が胸より手前（構えた拳・前腕）なら、その分だけ遠くから出す。</item>
/// <item><b>上下・左右</b>: 落ちる所と、今の相手のその部位の位置の差だけ、両手の通り道を平行にずらす。ずらしは技の始めの 0 から、落ちる時の少し前に
/// ずらしきるまで滑らかに増やす（構えから跳ばない）。剣の向きと刃筋はそのまま。</item>
/// </list>
/// 相手の姿勢（屈む・反る・構えを上げる）が違えば部位の位置が違うので、同じ技でも違う通り道になる。
/// </summary>
public static class MotionAim
{
    /// <summary>狙える部位（入力元が見える相手の体の位置から決まる所）。</summary>
    public static IReadOnlyList<BodyPartKind> Targets { get; } = [BodyPartKind.Head, BodyPartKind.Chest, BodyPartKind.Pelvis, BodyPartKind.UpperArm, BodyPartKind.Forearm];

    private static readonly ConcurrentDictionary<(MotionTechnique, float), (Vector3 Point, int Frame)> Landings = new();

    /// <summary>
    /// 技が落ちる所（体フレーム、腕の長さを 1 とした値。踏み込んだ分の前進を Z に足す）と、そのフレームの番号。
    /// </summary>
    public static (Vector3 Point, int Frame) Landing(MotionTechnique technique, float armReach)
    {
        ArgumentNullException.ThrowIfNull(technique);
        return Landings.GetOrAdd((technique, armReach), key => Compute(key.Item1, key.Item2));
    }

    private static (Vector3 Point, int Frame) Compute(MotionTechnique technique, float armReach)
    {
        var (top, _) = Weapon.TwoHandGripsOf(technique.Weapon);
        var guardX = top.X + 0.012f;
        var tipX = Weapon.TipOf(technique.Weapon).X;
        var monouchi = ((guardX - top.X) + (0.8f * (tipX - guardX))) / armReach;
        var tip = (tipX - top.X) / armReach;
        var frames = technique.Motion.Frames;
        var dt = technique.Motion.FrameTime;
        var advance = new float[frames.Count];
        var speed = new float[frames.Count];
        for (var i = 0; i < frames.Count; i++)
        {
            advance[i] = (i > 0 ? advance[i - 1] : 0f) + (frames[i].Velocity.Z * dt);
            if (i > 0)
            {
                Vector3 Tip(int k) => frames[k].Right + (frames[k].Blade * tip) + new Vector3(0f, 0f, advance[k]);
                speed[i] = Vector3.Distance(Tip(i), Tip(i - 1)) / dt;
            }
        }

        var peak = Array.IndexOf(speed, speed.Max());
        var best = peak;
        var reach = float.MinValue;
        for (var i = peak; i < frames.Count; i++)
        {
            var z = (frames[i].Right + (frames[i].Blade * monouchi)).Z + advance[i];
            if (z > reach)
            {
                reach = z;
                best = i;
            }
        }

        var point = frames[best].Right + (frames[best].Blade * monouchi) + new Vector3(0f, 0f, advance[best]);
        return (point, best);
    }

    /// <summary>
    /// 相手の部位 <paramref name="part"/> の今の位置（ワールド）。見える所から決める: 頭・胸・腰はそのまま、上腕は肩から拳の 3 割、前腕は
    /// 拳から肩の 2 割（小手）。左右は、こちらの右手（剣の側）に近い方の腕。
    /// </summary>
    public static Vector3 TargetPoint(in FighterSense sense, BodyPartKind part)
    {
        var body = sense.OpponentBody;
        var pelvis = body.Pelvis == default ? sense.OpponentChest - new Vector3(0f, 0.7f * sense.ArmReach, 0f) : body.Pelvis;
        switch (part)
        {
            case BodyPartKind.Head:
                return sense.OpponentHead;
            case BodyPartKind.Pelvis:
                return pelvis;
            case BodyPartKind.UpperArm:
            case BodyPartKind.Forearm:
                var mine = sense.Self.LocalToWorld(new Vector3(-0.3f * sense.ArmReach, 0f, 0f)); // 右手の側（体フレームの −X）
                var (shoulder, fist) = Arm(sense, Hand.Left);
                var (shoulder2, fist2) = Arm(sense, Hand.Right);
                if (Vector3.DistanceSquared(fist2, mine) < Vector3.DistanceSquared(fist, mine))
                {
                    (shoulder, fist) = (shoulder2, fist2);
                }

                return part == BodyPartKind.UpperArm ? Vector3.Lerp(shoulder, fist, 0.3f) : Vector3.Lerp(fist, shoulder, 0.2f);
            default:
                return sense.OpponentChest;
        }
    }

    private static (Vector3 Shoulder, Vector3 Fist) Arm(in FighterSense sense, Hand hand)
    {
        var shoulder = sense.OpponentBody.Pelvis == default ? sense.OpponentChest : sense.OpponentBody.Shoulder(hand);
        var fist = sense.Opponent(hand).Position;
        return (shoulder, fist == default ? shoulder : fist);
    }

    /// <summary>
    /// 狙う所が相手の胸より手前にある分（胸と胸を結ぶ向きの距離 [m]。奥なら負）: 技はこの分だけ遠くから出す。
    /// </summary>
    public static float ForwardOffset(in FighterSense sense, Vector3 target, Vector3 face)
    {
        var chest = Vector3.Dot(sense.OpponentChest - sense.Self.Origin, face);
        var aimed = Vector3.Dot(target - sense.Self.Origin, face);
        return chest - aimed;
    }

    /// <summary>
    /// 技 <paramref name="technique"/> の両手の通り道を、落ちる所が <paramref name="targetLocal"/>（体フレーム、腕の長さ単位）の上下・左右に来るよう
    /// ずらした技（前後は間合いで合わせるので、ずらさない）。
    /// </summary>
    public static MotionTechnique Aimed(MotionTechnique technique, Vector3 targetLocal, float armReach, AimTuning tuning)
    {
        ArgumentNullException.ThrowIfNull(technique);
        ArgumentNullException.ThrowIfNull(tuning);
        var (point, frame) = Landing(technique, armReach);
        var shift = new Vector3(targetLocal.X - point.X, targetLocal.Y - point.Y, 0f) * tuning.Gain;
        if (shift.Length() > tuning.MaxShift)
        {
            shift = Vector3.Normalize(shift) * tuning.MaxShift;
        }

        if (shift.LengthSquared() < 1e-8f)
        {
            return technique;
        }

        var frames = technique.Motion.Frames;
        var full = Math.Max(1, (int)MathF.Round(frame * tuning.Lead));
        var aimed = new MotionFrame[frames.Count];
        for (var i = 0; i < frames.Count; i++)
        {
            var x = Math.Clamp((float)i / full, 0f, 1f);
            var w = x * x * (3f - (2f * x));
            var f = frames[i];
            aimed[i] = f with { Right = f.Right + (shift * w), Left = f.Left + (shift * w) };
        }

        return technique with { Motion = technique.Motion with { Frames = aimed } };
    }
}
