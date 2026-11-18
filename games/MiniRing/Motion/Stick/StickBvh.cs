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
/// <item>座標は棒人形の世界のまま（+Y 上・+Z 前・床 y = 0、メートル）。Blender の読み込みの既定（前 −Z・上 Y）で Z が上になる。</item>
/// <item>角は前のコマとの差が 180° を超えないよう 360° を足し引きする（Blender がコマの間を補間しても回り込まない）。</item>
/// </list>
/// </summary>
public static class StickBvh
{
    /// <summary>
    /// 姿勢の列 <paramref name="frames"/>（同じ体、コマの間隔 <paramref name="frameTime"/> 秒）を BVH の文字列にする。
    /// </summary>
    public static string Write(StickBody body, IReadOnlyList<StickFrame> frames, float frameTime)
    {
        ArgumentNullException.ThrowIfNull(body);
        ArgumentNullException.ThrowIfNull(frames);
        if (frames.Count == 0)
        {
            throw new ArgumentException("姿勢が 1 つもありません。", nameof(frames));
        }

        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(frameTime);
        var order = Order(body);
        var text = new StringBuilder();
        text.Append("HIERARCHY\n");
        WriteJoint(text, body, order[0], 0);
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
                    var p = frame.Joints[i];
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
            }

            text.AppendJoin(' ', values.Select(v => v.ToString("0.######", CultureInfo.InvariantCulture)));
            text.Append('\n');
        }

        return text.ToString();
    }

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

    private static void WriteJoint(StringBuilder text, StickBody body, int i, int depth)
    {
        var s = body.Segments[i];
        var pad = new string('\t', depth);
        var isRoot = s.Parent < 0;
        var offset = isRoot ? Vector3.Zero : s.Joint - body.Segments[s.Parent].Joint; // 根の位置は動きの数で持つ
        text.Append(CultureInfo.InvariantCulture, $"{pad}{(isRoot ? "ROOT" : "JOINT")} {BoneName(s)}\n{pad}{{\n");
        text.Append(CultureInfo.InvariantCulture, $"{pad}\tOFFSET {Format(offset)}\n");
        text.Append(pad).Append(isRoot ? "\tCHANNELS 6 Xposition Yposition Zposition Zrotation Xrotation Yrotation\n" : "\tCHANNELS 3 Zrotation Xrotation Yrotation\n");
        var children = Children(body, i).ToList();
        foreach (var child in children)
        {
            WriteJoint(text, body, child, depth + 1);
        }

        if (children.Count == 0)
        {
            text.Append(CultureInfo.InvariantCulture, $"{pad}\tEnd Site\n{pad}\t{{\n{pad}\t\tOFFSET {Format(s.Tip - s.Joint)}\n{pad}\t}}\n");
        }

        text.Append(CultureInfo.InvariantCulture, $"{pad}}}\n");
    }

    private static string Format(Vector3 v) => string.Create(CultureInfo.InvariantCulture, $"{v.X:0.######} {v.Y:0.######} {v.Z:0.######}");
}
