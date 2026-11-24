using System.Collections.Concurrent;
using System.Numerics;
using System.Text;

namespace MiniRing.Motion;

/// <summary>
/// VR の記録（<see cref="VrMotionClip"/>、正規化した両手の目標・武器の向き・頭のずれ・歩き）を、剣士のボットが技として出せる型
/// （<see cref="SwordMotion"/>）にする。記録の打ちの山（写像後の切っ先が最も速い所）の前後を取り出し、その前を構えからつなぎ、後を構えへ戻す。
/// どれも<b>目標の経路</b>で、体はバネで追う（CLAUDE.md §1.1・§1.2。姿勢は書き込まない）。docs/arena/vrmotion_pick_2026-11-24.md・docs/VR_MOTION.md。
/// </summary>
public static class VrMotionForms
{
    /// <summary>型にする区間: 打ちの山の前（振りかぶりを含む）[s]。</summary>
    public const float Before = 0.7f;

    /// <summary>型にする区間: 打ちの山の後（振り抜き）[s]。</summary>
    public const float After = 0.4f;

    /// <summary>構えを保つ時間 [s]（型の始め。<see cref="SwordForms"/> の型の始めの保ちと同じくらい）。</summary>
    public const float HoldGuard = 0.15f;

    /// <summary>構えから記録の区間の始めへつなぐ時間 [s]。</summary>
    public const float BlendIn = 0.2f;

    /// <summary>記録の区間の終わりから構えへ戻す時間 [s]（型の戻りと同じくらい）。</summary>
    public const float BlendOut = 0.5f;

    private const string ResourcePrefix = "MiniRing.Motion.VrForms.";

    private static readonly ConcurrentDictionary<string, VrMotionClip> Clips = new(StringComparer.Ordinal);

    /// <summary>組み込みの VR の振りの名前（<c>Motion/VrForms/&lt;名前&gt;.vrmotion</c>。docs/vrmotion/ の写し）。</summary>
    public static IReadOnlyList<string> Embedded { get; } =
    [
        .. typeof(VrMotionForms).Assembly.GetManifestResourceNames()
            .Where(n => n.StartsWith(ResourcePrefix, StringComparison.Ordinal) && n.EndsWith(VrMotionClip.Extension, StringComparison.Ordinal))
            .Select(n => n[ResourcePrefix.Length..^VrMotionClip.Extension.Length])
            .Order(StringComparer.Ordinal),
    ];

    /// <summary>組み込みの VR の振り <paramref name="id"/>。無ければ <see cref="ArgumentException"/>。読んだものは覚えておく（中身は変わらない）。</summary>
    public static VrMotionClip Load(string id)
    {
        ArgumentException.ThrowIfNullOrEmpty(id);
        return Clips.GetOrAdd(id, key =>
        {
            using var stream = typeof(VrMotionForms).Assembly.GetManifestResourceStream(ResourcePrefix + key + VrMotionClip.Extension)
                ?? throw new ArgumentException($"組み込みの VR の振り '{key}' がありません（Motion/VrForms/）。", nameof(id));
            using var reader = new StreamReader(stream, Encoding.UTF8);
            return VrMotionClip.FromJson(reader.ReadToEnd());
        });
    }

    /// <summary>切っ先を測る長さ（腕の長さ = 1。<see cref="SwordForms"/> の刀身と同じ）。</summary>
    private const float Blade = 1.7f;

    /// <summary>
    /// 記録 <paramref name="clip"/> を型 <paramref name="name"/> にする。<paramref name="guard"/> は構え（<c>FormSwordsman.Guard</c>）。
    /// 歩き（<see cref="VrMotionFrame.MoveLocal"/>、歩く速さに対する割合）は、腰の速さ（腕の長さ / s）へ戻す: 同じ体で同じ歩く指令になるよう、
    /// <paramref name="walkSpeed"/> [m/s]・<paramref name="armReach"/> [m]・再生の速さ <paramref name="rate"/> で割る（<see cref="MotionImitation.ToCommand"/> の逆）。
    /// 胴のひねりは、手の位置から決まるひねり（<see cref="PunchMechanics.TorsoTwist"/>）と同じにする（記録はひねりを持たないので、なぞるときと同じ体の動きにする）。
    /// </summary>
    public static SwordMotion ToSwordMotion(VrMotionClip clip, string name, MotionFrame guard, float walkSpeed, float armReach, float rate, float maxTwist, float frameRate = 60f)
    {
        ArgumentNullException.ThrowIfNull(clip);
        ArgumentException.ThrowIfNullOrEmpty(name);
        if (!(walkSpeed > 0f) || !(armReach > 0f) || !(rate > 0f) || !(frameRate > 0f))
        {
            throw new ArgumentOutOfRangeException(nameof(walkSpeed), "walkSpeed・armReach・rate・frameRate は正。");
        }

        var peak = StrikePeak(clip);
        var start = MathF.Max(0f, peak - Before);
        var end = MathF.Min(clip.Duration, peak + After);
        var dt = 1f / frameRate;
        var frames = new List<MotionFrame>();
        var lastOrientation = Quaternion.Identity;
        MotionFrame At(float time)
        {
            var f = clip.Sample(time);
            var q = f.RightWeaponOrientation ?? lastOrientation;
            lastOrientation = q;
            var blade = Vector3.Normalize(Vector3.Transform(Vector3.UnitX, q));
            var edge = -Vector3.Normalize(Vector3.Transform(Vector3.UnitY, q)); // MotionFrame.Orientation の逆（ローカル −Y が刃）
            var velocity = f.MoveLocal * (walkSpeed / (armReach * rate));
            var twist = PunchMechanics.TorsoTwist(f.LeftTarget, f.RightTarget, 1f, maxTwist);
            return new MotionFrame(f.RightTarget, f.LeftTarget, blade, edge, f.HeadOffset, twist, velocity);
        }

        static float Smooth(float u) => u * u * (3f - (2f * u));
        for (var t = 0f; t < HoldGuard; t += dt)
        {
            frames.Add(guard);
        }

        var first = At(start);
        for (var t = 0f; t < BlendIn; t += dt)
        {
            frames.Add(MotionFrame.Lerp(guard, first, Smooth(t / BlendIn)));
        }

        for (var t = start; t <= end; t += dt)
        {
            frames.Add(At(t));
        }

        var last = At(end);
        for (var t = dt; t <= BlendOut + 1e-4f; t += dt)
        {
            frames.Add(MotionFrame.Lerp(last, guard, Smooth(MathF.Min(1f, t / BlendOut))));
        }

        return new SwordMotion(name, dt, frames);
    }

    /// <summary>
    /// 打ちの山の時刻 [s]（クリップの始まりから）: 写像後の切っ先（右手の目標 + 刀の向き × 刀身）の速さを 5 コマの平均でならした最大。
    /// 1〜2 コマのトラッキングの飛びで山を取り違えないよう、ならしてから選ぶ。
    /// </summary>
    public static float StrikePeak(VrMotionClip clip)
    {
        ArgumentNullException.ThrowIfNull(clip);
        var n = clip.Frames.Count;
        if (n < 3)
        {
            return 0f;
        }

        var speed = new float[n];
        var last = Quaternion.Identity;
        Vector3 Tip(int i)
        {
            var f = clip.Frames[i];
            last = f.RightWeaponOrientation ?? last;
            return f.RightTarget + (Vector3.Transform(Vector3.UnitX, last) * Blade);
        }

        var previous = Tip(0);
        for (var i = 1; i < n; i++)
        {
            var tip = Tip(i);
            var step = clip.Frames[i].Time - clip.Frames[i - 1].Time;
            speed[i] = step > 1e-6f ? Vector3.Distance(tip, previous) / step : 0f;
            previous = tip;
        }

        var best = 0;
        var bestSpeed = float.NegativeInfinity;
        for (var i = 2; i < n - 2; i++)
        {
            var s = (speed[i - 2] + speed[i - 1] + speed[i] + speed[i + 1] + speed[i + 2]) / 5f;
            if (s > bestSpeed)
            {
                bestSpeed = s;
                best = i;
            }
        }

        return clip.Frames[best].Time - clip.Frames[0].Time;
    }
}
