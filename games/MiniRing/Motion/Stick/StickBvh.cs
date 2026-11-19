using System.Globalization;
using System.Numerics;
using System.Text;

namespace MiniRing.Motion.Stick;

/// <summary>
/// 棒人形の姿勢の列を BVH（モーションキャプチャの階層と動きの書式）で書き出す（docs/STICK_MOTION.md §12）。Blender の File &gt; Import &gt; Motion Capture (.bvh) で、
/// 部位ごとのボーンを持つアーマチュアと、コマごとの動きになる。
/// <list type="bullet">
/// <item>階層 = 棒人形の部位の親子（根は骨盤）。ボーンの頭 = 部位の関節の点。<c>OFFSET</c> = 親の関節から自分の関節まで（休みの姿勢 = T ポーズ、メートル）。子の無い部位は <c>End Site</c> に部位の先（足先・頭の上・剣先）を置く。</item>
/// <item>動き = 根の位置（世界）と、部位ごとの親から見た回転（休みからの回転。<see cref="StickFrame.Rotations"/> の親に対する差）を、Z・X・Y の順のオイラー角（度）で。BVH の決まりどおり、回転は <c>Rz·Rx·Ry</c> を列ベクトルに掛ける。</item>
/// <item>座標は棒人形の世界の向き（+Y 上・+Z 前・床 y = 0）。Blender の読み込みの既定（前 −Z・上 Y）で Z が上になる。長さは <c>scale</c> 倍（ゲームの人形は人の 0.355 倍の縮尺なので、Arena は既定で 1 ÷ <see cref="FighterTuning.Scale"/> を掛けて人の大きさ [m] にする）。</item>
/// <item>角は前のコマとの差が 180° を超えないよう 360° を足し引きする（Blender がコマの間を補間しても回り込まない）。</item>
/// <item>両手で握る武器（<see cref="StickBody.GripHands"/> が 2 つ）: もう一方の手の握りの点を、ボーン <c>grip.L</c>（または <c>grip.R</c>）として足す。
/// 剣を握る手の前腕の子で、頭 = 剣の握り（剣と同じ点）、回転 = 剣と同じ、先 = 剣の軸に沿ってもう一方の手が握る点（<see cref="OffHandGrip"/>）。
/// 剣と一緒に動くので、Blender でもう一方の前腕に IK（目標 = このボーンの先、つなぐ長さ 2）を掛ければ、手が柄から離れない（<c>tools/blender/stick_bvh_ik.py</c>）。
/// 剣のボーンの子にしないのは、子があると Blender が剣のボーンの先を子の頭へ寄せてしまうため。</item>
/// </list>
/// </summary>
public static class StickBvh
{
    /// <summary>
    /// 姿勢の列 <paramref name="frames"/>（同じ体、コマの間隔 <paramref name="frameTime"/> 秒）を BVH の文字列にする。長さ（位置・OFFSET）は <paramref name="scale"/> 倍。
    /// </summary>
    public static string Write(StickBody body, IReadOnlyList<StickFrame> frames, float frameTime, float scale = 1f)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(scale);
        ArgumentNullException.ThrowIfNull(body);
        ArgumentNullException.ThrowIfNull(frames);
        if (frames.Count == 0)
        {
            throw new ArgumentException("姿勢が 1 つもありません。", nameof(frames));
        }

        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(frameTime);
        var order = Order(body);
        var weapon = body.Armed && body.GripHands.Count == 2 ? StickBody.Ids.Weapon : -1;
        var grip = weapon >= 0 ? new OffHand(weapon, OffHandGrip(body, frames)) : null;
        var text = new StringBuilder();
        text.Append("HIERARCHY\n");
        WriteJoint(text, body, order[0], 0, scale, grip);
        text.Append("MOTION\n");
        text.Append(CultureInfo.InvariantCulture, $"Frames: {frames.Count}\n");
        text.Append(CultureInfo.InvariantCulture, $"Frame Time: {frameTime:0.########}\n");
        var previous = new Vector3[body.Segments.Count];
        var values = new List<float>();
        for (var f = 0; f < frames.Count; f++)
        {
            var frame = frames[f];
            if (frame.Body.Segments.Count != body.Segments.Count)
            {
                throw new ArgumentException($"コマ {f} は別の体の姿勢です。", nameof(frames));
            }

            values.Clear();
            foreach (var i in order)
            {
                var s = body.Segments[i];
                if (s.Parent < 0)
                {
                    var p = frame.Joints[i] * scale;
                    values.Add(p.X);
                    values.Add(p.Y);
                    values.Add(p.Z);
                }

                var local = s.Parent < 0 ? frame.Rotations[i] : Quaternion.Conjugate(frame.Rotations[s.Parent]) * frame.Rotations[i];
                var euler = EulerZxyDegrees(Quaternion.Normalize(local));
                if (f > 0)
                {
                    euler = Unwrap(euler, previous[i]);
                }

                previous[i] = euler;
                values.Add(euler.Z);
                values.Add(euler.X);
                values.Add(euler.Y);
                if (i == weapon)
                {
                    // もう一方の手の握り（剣と同じ回転。階層では剣のすぐ後ろに書く）。
                    values.Add(euler.Z);
                    values.Add(euler.X);
                    values.Add(euler.Y);
                }
            }

            text.AppendJoin(' ', values.Select(v => v.ToString("0.######", CultureInfo.InvariantCulture)));
            text.Append('\n');
        }

        return text.ToString();
    }

    /// <summary>
    /// 両手で握る武器の、もう一方の手が握る点: 剣を握る手（剣の関節の点）から剣の軸に沿った距離 [m]（負 = 柄頭の側）。
    /// 動きの全部のコマの「もう一方の手首 − 剣の握り」を剣の軸へ写した値の平均（型の経路が置いた両手の間隔）。
    /// </summary>
    public static float OffHandGrip(StickBody body, IReadOnlyList<StickFrame> frames)
    {
        ArgumentNullException.ThrowIfNull(body);
        ArgumentNullException.ThrowIfNull(frames);
        if (!body.Armed || body.GripHands.Count != 2 || frames.Count == 0)
        {
            return 0f;
        }

        var weapon = body.Segments[StickBody.Ids.Weapon];
        var other = body.GripHands.Select(StickBody.Ids.Forearm).Single(f => f != weapon.Parent);
        var sum = 0f;
        foreach (var frame in frames)
        {
            var axis = Vector3.Normalize(frame.Tip(StickBody.Ids.Weapon) - frame.Joints[StickBody.Ids.Weapon]);
            sum += Vector3.Dot(frame.Tip(other) - frame.Joints[StickBody.Ids.Weapon], axis);
        }

        return sum / frames.Count;
    }

    /// <summary>もう一方の手の握りのボーンの名前（<c>grip.L</c> か <c>grip.R</c>。もう一方の手の左右）。</summary>
    public static string OffHandBoneName(StickBody body)
    {
        ArgumentNullException.ThrowIfNull(body);
        var weapon = body.Segments[StickBody.Ids.Weapon];
        var other = body.GripHands.Select(StickBody.Ids.Forearm).Single(f => f != weapon.Parent);
        return BoneName(body.Segments[other]).Replace(body.Segments[other].Name.Split('-')[0], "grip", StringComparison.Ordinal);
    }

    private sealed record OffHand(int Weapon, float Distance);

    /// <summary>書き出す部位の順（根から深さ優先、子は番号の順）。BVH の動きの数の並びもこの順。</summary>
    public static IReadOnlyList<int> Order(StickBody body)
    {
        ArgumentNullException.ThrowIfNull(body);
        var order = new List<int>();
        void Visit(int i)
        {
            order.Add(i);
            foreach (var child in Children(body, i))
            {
                Visit(child);
            }
        }

        var root = Enumerable.Range(0, body.Segments.Count).Single(i => body.Segments[i].Parent < 0);
        Visit(root);
        return order;
    }

    /// <summary>ボーンの名前: 部位の名前の左右（<c>-Left</c>・<c>-Right</c>）を Blender の左右の印（<c>.L</c>・<c>.R</c>）にする（左右の写しが効く）。左右はファイターの左右。</summary>
    public static string BoneName(StickSegment segment)
    {
        ArgumentNullException.ThrowIfNull(segment);
        var name = segment.Name.Replace("-Left", ".L", StringComparison.Ordinal).Replace("-Right", ".R", StringComparison.Ordinal);
        return new string(name.Select(c => char.IsLetterOrDigit(c) || c is '.' or '_' ? c : '_').ToArray());
    }

    /// <summary>回転 <paramref name="q"/> を、<c>Rz(z)·Rx(x)·Ry(y)</c>（列ベクトルに掛ける）になる角 (x, y, z) [度] にする。</summary>
    public static Vector3 EulerZxyDegrees(Quaternion q)
    {
        var (x, y, z, w) = (q.X, q.Y, q.Z, q.W);
        var r01 = 2f * ((x * y) - (w * z));
        var r11 = 1f - (2f * ((x * x) + (z * z)));
        var r20 = 2f * ((x * z) - (w * y));
        var r21 = 2f * ((y * z) + (w * x));
        var r22 = 1f - (2f * ((x * x) + (y * y)));
        var ax = MathF.Asin(Math.Clamp(r21, -1f, 1f));
        var ay = MathF.Atan2(-r20, r22);
        var az = MathF.Atan2(-r01, r11);
        const float ToDegrees = 180f / MathF.PI;
        return new Vector3(ax, ay, az) * ToDegrees;
    }

    private static Vector3 Unwrap(Vector3 angle, Vector3 previous)
    {
        static float Near(float a, float p) => a + (360f * MathF.Round((p - a) / 360f));
        return new Vector3(Near(angle.X, previous.X), Near(angle.Y, previous.Y), Near(angle.Z, previous.Z));
    }

    private static IEnumerable<int> Children(StickBody body, int parent)
        => Enumerable.Range(0, body.Segments.Count).Where(i => body.Segments[i].Parent == parent);

    private static void WriteJoint(StringBuilder text, StickBody body, int i, int depth, float scale, OffHand? grip)
    {
        var s = body.Segments[i];
        var pad = new string('\t', depth);
        var isRoot = s.Parent < 0;
        var offset = isRoot ? Vector3.Zero : (s.Joint - body.Segments[s.Parent].Joint) * scale; // 根の位置は動きの数で持つ
        text.Append(CultureInfo.InvariantCulture, $"{pad}{(isRoot ? "ROOT" : "JOINT")} {BoneName(s)}\n{pad}{{\n");
        text.Append(CultureInfo.InvariantCulture, $"{pad}\tOFFSET {Format(offset)}\n");
        text.Append(pad).Append(isRoot ? "\tCHANNELS 6 Xposition Yposition Zposition Zrotation Xrotation Yrotation\n" : "\tCHANNELS 3 Zrotation Xrotation Yrotation\n");
        var children = Children(body, i).ToList();
        foreach (var child in children)
        {
            WriteJoint(text, body, child, depth + 1, scale, grip);
            if (grip is not null && child == grip.Weapon)
            {
                var w = body.Segments[child];
                var inner = pad + "\t";
                var axis = Vector3.Normalize(w.Tip - w.Joint);
                text.Append(CultureInfo.InvariantCulture, $"{inner}JOINT {OffHandBoneName(body)}\n{inner}{{\n{inner}\tOFFSET {Format((w.Joint - s.Joint) * scale)}\n");
                text.Append(inner).Append("\tCHANNELS 3 Zrotation Xrotation Yrotation\n");
                text.Append(CultureInfo.InvariantCulture, $"{inner}\tEnd Site\n{inner}\t{{\n{inner}\t\tOFFSET {Format(axis * grip.Distance * scale)}\n{inner}\t}}\n{inner}}}\n");
            }
        }

        if (children.Count == 0)
        {
            text.Append(CultureInfo.InvariantCulture, $"{pad}\tEnd Site\n{pad}\t{{\n{pad}\t\tOFFSET {Format((s.Tip - s.Joint) * scale)}\n{pad}\t}}\n");
        }

        text.Append(CultureInfo.InvariantCulture, $"{pad}}}\n");
    }

    private static string Format(Vector3 v) => string.Create(CultureInfo.InvariantCulture, $"{v.X:0.######} {v.Y:0.######} {v.Z:0.######}");
}
