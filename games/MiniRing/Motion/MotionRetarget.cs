using System.Numerics;

namespace MiniRing.Motion;

/// <summary>
/// モーションキャプチャ（BVH）の、両手で剣を持つ人の動きを、体の大きさによらない剣の動き（<see cref="SwordMotion"/>）に直す。
/// <list type="bullet">
/// <item>体フレーム: 原点は両肩（LeftArm / RightArm）の中点、向きは腰（Hips）の前（+Z）の水平の向き。</item>
/// <item>長さは演者の腕の長さ（肩 → 肘 → 手首）で割る。</item>
/// <item>手の位置は握り（掌の真ん中: 手首から指先の印 Wrist_End へ <see cref="PalmShare"/> の所）。手首の関節は柄の横にあるので、両手首を
/// 結ぶ線は剣の向きにならない（中段で 45° 右を向いてしまう）。剣の向きは左手の握り（柄頭）から右手の握り（鍔）へ。剣の先は右手から刀身の
/// 長さ（実寸 <see cref="BladeLength"/>）先。</item>
/// <item>刃筋は剣の先の動きの面へ合わせる（<see cref="SwordMotion.FollowCut"/>）。始まりは下向き（中段の構えの刃は下）。</item>
/// <item>頭のずれは、録った全体の中ほど（中央値）の頭の位置からの差。胴のひねりは胸（Spine1）と腰の向きの差。</item>
/// </list>
/// </summary>
public static class MotionRetarget
{
    /// <summary>剣の先を測る刀身の長さ（実寸、m）。</summary>
    public const float BladeLength = 0.75f;

    /// <summary>手首から指先の印（Wrist_End）までのうち、握り（掌の真ん中）がある割合。</summary>
    public const float PalmShare = 0.5f;

    /// <summary>BVH の長さの単位（SFU のデータはインチ）。</summary>
    public const float Inch = 0.0254f;

    /// <summary>
    /// <paramref name="clip"/> の <paramref name="from"/> 〜 <paramref name="to"/> 秒を、<paramref name="rate"/> フレーム / s の剣の動きにする。
    /// </summary>
    public static SwordMotion FromBvh(BvhClip clip, string name, float from, float to, float rate = 60f)
    {
        ArgumentNullException.ThrowIfNull(clip);
        int J(string n) => clip.IndexOf(n);
        var (hips, chest, head) = (J("Hips"), J("Spine1"), J("Head"));
        var (lShoulder, rShoulder) = (J("LeftArm"), J("RightArm"));
        var (lElbow, lHand, rHand) = (J("LeftForeArm"), J("LeftHand"), J("RightHand"));
        var (lEnd, rEnd) = (J("L_Wrist_End"), J("R_Wrist_End"));
        var rest = clip.Pose(0);
        var arm = (Vector3.Distance(rest[lShoulder].Position, rest[lElbow].Position) + Vector3.Distance(rest[lElbow].Position, rest[lHand].Position)) * Inch;
        var dt = 1f / rate;
        var end = MathF.Min(to, clip.Duration);
        if (!(end > from + dt))
        {
            throw new ArgumentException("区間が短すぎます。", nameof(to));
        }

        // 1 フレームの体フレームと、その中の量（m）。
        (Vector3 Origin, float Yaw, Vector3 Right, Vector3 Left, Vector3 Head, float ChestYaw, Vector3 Hips) Read(float t)
        {
            var pose = clip.Pose((int)MathF.Round(t / clip.FrameTime));
            Vector3 P(int j) => pose[j].Position * Inch;
            static float YawOf(Quaternion q)
            {
                var f = Vector3.Transform(Vector3.UnitZ, q);
                return MathF.Atan2(f.X, f.Z);
            }

            Vector3 Grip(int wrist, int end) => Vector3.Lerp(P(wrist), P(end), PalmShare);
            return ((P(lShoulder) + P(rShoulder)) * 0.5f, YawOf(pose[hips].Rotation), Grip(rHand, rEnd), Grip(lHand, lEnd), P(head), YawOf(pose[chest].Rotation), P(hips));
        }

        static Vector3 Local(Vector3 v, float yaw)
        {
            var (s, c) = MathF.SinCos(-yaw);
            return new Vector3((c * v.X) + (s * v.Z), v.Y, (-s * v.X) + (c * v.Z));
        }

        var samples = new List<(Vector3 Origin, float Yaw, Vector3 Right, Vector3 Left, Vector3 Head, float ChestYaw, Vector3 Hips)>();
        for (var t = from; t <= end; t += dt)
        {
            samples.Add(Read(t));
        }

        // 中立の頭: 録った全体（切り出す区間ではなく）の中央値（体フレームの中の頭の位置）。区間ごとに取ると、踏み込みの区間では
        // 前へ出た頭が中立になってしまい、構えと技で頭の目標が食い違う。
        var heads = samples.Select(s => Local(s.Head - s.Origin, s.Yaw) / arm).ToList();
        var whole = new List<Vector3>();
        for (var t = 0f; t < clip.Duration; t += 0.1f)
        {
            var s = Read(t);
            whole.Add(Local(s.Head - s.Origin, s.Yaw) / arm);
        }

        var neutral = new Vector3(0f, Median(whole.Select(h => h.Y)), Median(whole.Select(h => h.Z)));
        var n = samples.Count;
        // 剣には印が無い: 両手の握りを結ぶ線は、構えでも剣先が右へ 30° ほどそれる（握り方と印の付け方による）。中段の構えの剣先は
        // 相手の喉（体の真ん中の線）を向くので、録った全体の「止まった中段」の剣の方位角の中央値を、剣の向きのずれとして戻す。
        var yawFix = Quaternion.CreateFromAxisAngle(Vector3.UnitY, -SwordYawBias(clip, Read, Local));
        var blades = new Vector3[n];
        var tips = new Vector3[n];
        for (var i = 0; i < n; i++)
        {
            var s = samples[i];
            blades[i] = Vector3.Transform(Vector3.Normalize(Local(s.Right - s.Left, s.Yaw) + new Vector3(1e-6f)), yawFix);
            tips[i] = Local(s.Right - s.Origin, s.Yaw) + (blades[i] * BladeLength);
        }

        var tipVelocity = new Vector3[n];
        var velocity = new Vector3[n];
        for (var i = 0; i < n; i++)
        {
            var (a, b) = (Math.Max(0, i - 3), Math.Min(n - 1, i + 3));
            var span = (b - a) * dt;
            tipVelocity[i] = span > 0f ? (tips[b] - tips[a]) / span : Vector3.Zero;
            var move = span > 0f ? Local(samples[b].Hips - samples[a].Hips, samples[i].Yaw) / span : Vector3.Zero;
            velocity[i] = new Vector3(move.X, 0f, move.Z) / arm;
        }

        var edges = new Vector3[n];
        edges[0] = SwordMotion.Perpendicular(-Vector3.UnitY, blades[0]);
        SwordMotion.FollowCut(blades, tipVelocity, edges, moving: 1.0f, follow: 0.25f);
        var frames = new MotionFrame[n];
        for (var i = 0; i < n; i++)
        {
            var s = samples[i];
            frames[i] = new MotionFrame(
                Local(s.Right - s.Origin, s.Yaw) / arm,
                Local(s.Left - s.Origin, s.Yaw) / arm,
                blades[i],
                edges[i],
                heads[i] - neutral,
                Wrap(s.ChestYaw - s.Yaw),
                velocity[i]);
        }

        return new SwordMotion(name, dt, frames);
    }

    /// <summary>
    /// 両手の握りを結ぶ線の、止まった中段（握りがほとんど動かず、剣先が 5〜50° 上を向く）での方位角の中央値 [rad]（録った全体から、0.1 秒ごと）。
    /// </summary>
    private static float SwordYawBias(BvhClip clip,
        Func<float, (Vector3 Origin, float Yaw, Vector3 Right, Vector3 Left, Vector3 Head, float ChestYaw, Vector3 Hips)> read,
        Func<Vector3, float, Vector3> local)
    {
        var azimuths = new List<float>();
        (Vector3 Origin, float Yaw, Vector3 Right, Vector3 Left, Vector3 Head, float ChestYaw, Vector3 Hips)? last = null;
        for (var t = 0f; t < clip.Duration; t += 0.1f)
        {
            var s = read(t);
            if (last is { } before && Vector3.Distance(s.Right, before.Right) < 0.02f)
            {
                var blade = Vector3.Normalize(local(s.Right - s.Left, s.Yaw) + new Vector3(1e-6f));
                var elevation = MathF.Asin(Math.Clamp(blade.Y, -1f, 1f));
                if (elevation is > 5f * MathF.PI / 180f and < 50f * MathF.PI / 180f)
                {
                    azimuths.Add(MathF.Atan2(blade.X, blade.Z));
                }
            }

            last = s;
        }

        return azimuths.Count < 5 ? 0f : Median(azimuths);
    }

    private static float Median(IEnumerable<float> values)
    {
        var sorted = values.OrderBy(v => v).ToList();
        return sorted.Count == 0 ? 0f : sorted[sorted.Count / 2];
    }

    private static float Wrap(float angle) => MathF.IEEERemainder(angle, 2f * MathF.PI);
}
