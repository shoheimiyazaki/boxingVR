using System.Numerics;

namespace MiniRing.Motion;

/// <summary>
/// 剣の動きの中の打ち 1 つ: 始まり（打つ前に剣が止まっていた所）・いちばん速い所・終わり（打ち終えて剣がまた止まった所）の時刻と、
/// その時の剣の先の速さ、打ち終えた剣の先の位置（体フレーム、腕の長さ = 1）と、打ちの形（<see cref="StrikeKind"/>）。
/// </summary>
public readonly record struct Strike(float Start, float Peak, float End, float PeakSpeed, Vector3 TipAtPeak, Vector3 TipAtEnd, StrikeKind Kind, float Advance);

/// <summary>打ちの形（剣の先がどこを通ったか）。</summary>
public enum StrikeKind
{
    /// <summary>上から頭へ（剣の先が頭の高さで、上から下へ）。</summary>
    Men,

    /// <summary>斜め上から（左右の面・袈裟）。</summary>
    Diagonal,

    /// <summary>手首の高さへ小さく（小手）。</summary>
    Kote,

    /// <summary>横から胴へ。</summary>
    Do,

    /// <summary>剣の向きへまっすぐ（突き）。</summary>
    Tsuki,

    /// <summary>そのほか（下からの斬り上げなど）。</summary>
    Other,
}

/// <summary>剣の動きから、打ちを探す。</summary>
public static class StrikeFinder
{
    /// <summary>
    /// <paramref name="motion"/> の中の打ち: 剣の先の速さ（腕の長さ / s）が <paramref name="fast"/> を超える山ごとに 1 つ。前後へ、速さが
    /// <paramref name="still"/> を下回る所までを打ちの区間とする。剣の先は右手から <paramref name="blade"/>（腕の長さ = 1）先。
    /// </summary>
    public static IReadOnlyList<Strike> Find(SwordMotion motion, float blade = 1.4f, float fast = 10f, float still = 2.5f)
    {
        ArgumentNullException.ThrowIfNull(motion);
        var n = motion.Frames.Count;
        var tip = new Vector3[n];
        var body = new Vector3[n]; // 腰の進んだ所（体フレームの速さを積む）
        for (var i = 0; i < n; i++)
        {
            var f = motion.Frames[i];
            tip[i] = f.Right + (f.Blade * blade);
            body[i] = i == 0 ? Vector3.Zero : body[i - 1] + (f.Velocity * motion.FrameTime);
        }

        var speed = new float[n];
        for (var i = 0; i < n; i++)
        {
            var (a, b) = (Math.Max(0, i - 2), Math.Min(n - 1, i + 2));
            speed[i] = b > a ? Vector3.Distance(tip[b] + body[b], tip[a] + body[a]) / ((b - a) * motion.FrameTime) : 0f;
        }

        var strikes = new List<Strike>();
        var k = 0;
        while (k < n)
        {
            if (speed[k] < fast)
            {
                k++;
                continue;
            }

            // 山の頂上と、その前後で止まった所。
            var peak = k;
            while (k < n && speed[k] >= fast)
            {
                if (speed[k] > speed[peak])
                {
                    peak = k;
                }

                k++;
            }

            var start = peak;
            while (start > 0 && speed[start] > still)
            {
                start--;
            }

            var end = peak;
            while (end < n - 1 && speed[end] > still)
            {
                end++;
            }

            k = Math.Max(k, end + 1);
            var p = motion.Frames[peak];
            var swing = tip[Math.Min(n - 1, peak + 2)] - tip[Math.Max(0, peak - 2)];
            strikes.Add(new Strike(start * motion.FrameTime, peak * motion.FrameTime, end * motion.FrameTime, speed[peak], tip[peak], tip[end],
                Classify(p, swing, tip[peak]), body[end].Z - body[start].Z));
        }

        return strikes;
    }

    /// <summary>打ちの形: 剣の先の動きの向き（<paramref name="swing"/>）と、いちばん速い時の剣の先の高さ・向きから。</summary>
    private static StrikeKind Classify(MotionFrame at, Vector3 swing, Vector3 tip)
    {
        var dir = swing.LengthSquared() > 1e-8f ? Vector3.Normalize(swing) : Vector3.Zero;
        if (Vector3.Dot(dir, at.Blade) > 0.8f)
        {
            return StrikeKind.Tsuki; // 剣の向きへまっすぐ
        }

        if (dir.Y < -0.5f)
        {
            // 上から下へ: 横へも大きく動けば斜め。剣の先が肩より下で止まる小さな打ちは小手。
            return MathF.Abs(dir.X) > 0.55f ? StrikeKind.Diagonal : tip.Y < -0.15f ? StrikeKind.Kote : StrikeKind.Men;
        }

        return MathF.Abs(dir.X) > 0.6f && tip.Y < 0f ? StrikeKind.Do : StrikeKind.Other;
    }
}
