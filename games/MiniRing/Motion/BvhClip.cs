using System.Globalization;
using System.Numerics;

namespace MiniRing.Motion;

/// <summary>
/// BVH（Biovision Hierarchy）のモーションキャプチャ 1 本: 骨の木と、フレームごとの各関節の回転（と根の位置）。
/// <see cref="Pose"/> で、あるフレームの全関節のワールドの位置と向き（前進の運動学）を返す。単位はファイルのまま（SFU のデータはインチ）。
/// 回転の順はチャンネルの並び（例: Xrotation Yrotation Zrotation なら R = Rx · Ry · Rz、子の点 p は親の回転 · R · (offset + …)）。
/// </summary>
public sealed class BvhClip
{
    private readonly Joint[] _joints;
    private readonly float[][] _frames;

    private BvhClip(Joint[] joints, float[][] frames, float frameTime)
    {
        _joints = joints;
        _frames = frames;
        FrameTime = frameTime;
    }

    /// <summary>関節 1 つ: 名前・親（根は −1）・親からの位置・チャンネル（フレームの値の並びのうち、どこからいくつ）。</summary>
    private sealed record Joint(string Name, int Parent, Vector3 Offset, string[] Channels, int First);

    /// <summary>1 フレームの長さ [s]。</summary>
    public float FrameTime { get; }

    /// <summary>フレームの数。</summary>
    public int FrameCount => _frames.Length;

    /// <summary>長さ [s]。</summary>
    public float Duration => FrameCount * FrameTime;

    /// <summary>関節の名前（ファイルの順）。</summary>
    public IReadOnlyList<string> JointNames => [.. _joints.Select(j => j.Name)];

    /// <summary>名前の関節の番号（無ければ例外）。</summary>
    public int IndexOf(string name)
    {
        for (var i = 0; i < _joints.Length; i++)
        {
            if (_joints[i].Name == name)
            {
                return i;
            }
        }

        throw new KeyNotFoundException($"BVH に関節 {name} がありません。");
    }

    /// <summary>関節の親の番号（根は −1）。</summary>
    public int ParentOf(int joint) => _joints[joint].Parent;

    /// <summary>
    /// フレーム <paramref name="frame"/> の全関節のワールドの位置と向き（ファイルの単位・座標のまま）。
    /// </summary>
    public (Vector3 Position, Quaternion Rotation)[] Pose(int frame)
    {
        var values = _frames[Math.Clamp(frame, 0, _frames.Length - 1)];
        var pose = new (Vector3, Quaternion)[_joints.Length];
        for (var i = 0; i < _joints.Length; i++)
        {
            var joint = _joints[i];
            var local = Quaternion.Identity;
            var translation = joint.Offset;
            for (var c = 0; c < joint.Channels.Length; c++)
            {
                var v = values[joint.First + c];
                switch (joint.Channels[c])
                {
                    case "Xposition": translation.X = joint.Offset.X + v; break;
                    case "Yposition": translation.Y = joint.Offset.Y + v; break;
                    case "Zposition": translation.Z = joint.Offset.Z + v; break;
                    // チャンネルの順に右から掛ける（R = R1 · R2 · R3）。System.Numerics の a * b は「b の後に a」なので local * r。
                    case "Xrotation": local *= Quaternion.CreateFromAxisAngle(Vector3.UnitX, v * MathF.PI / 180f); break;
                    case "Yrotation": local *= Quaternion.CreateFromAxisAngle(Vector3.UnitY, v * MathF.PI / 180f); break;
                    case "Zrotation": local *= Quaternion.CreateFromAxisAngle(Vector3.UnitZ, v * MathF.PI / 180f); break;
                    default: throw new FormatException($"知らないチャンネル {joint.Channels[c]}。");
                }
            }

            if (joint.Parent < 0)
            {
                pose[i] = (translation, Quaternion.Normalize(local));
            }
            else
            {
                var (pp, pq) = pose[joint.Parent];
                pose[i] = (pp + Vector3.Transform(translation, pq), Quaternion.Normalize(pq * local));
            }
        }

        return pose;
    }

    /// <summary>BVH の文字列を読む。形が崩れていれば <see cref="FormatException"/>。</summary>
    public static BvhClip Parse(string text)
    {
        ArgumentNullException.ThrowIfNull(text);
        var tokens = text.Split((char[])[' ', '\t', '\r', '\n'], StringSplitOptions.RemoveEmptyEntries);
        var at = 0;
        string Next() => at < tokens.Length ? tokens[at++] : throw new FormatException("BVH が途中で終わっています。");
        float Number() => float.Parse(Next(), NumberStyles.Float, CultureInfo.InvariantCulture);
        void Expect(string word)
        {
            var got = Next();
            if (!string.Equals(got, word, StringComparison.OrdinalIgnoreCase))
            {
                throw new FormatException($"BVH: {word} のはずが {got}。");
            }
        }

        Expect("HIERARCHY");
        var joints = new List<Joint>();
        var channelCount = 0;
        var stack = new Stack<int>();
        var word = Next();
        if (!string.Equals(word, "ROOT", StringComparison.OrdinalIgnoreCase))
        {
            throw new FormatException("BVH: ROOT がありません。");
        }

        // 関節: 名前 { OFFSET x y z CHANNELS n ... (JOINT ... | End Site { OFFSET ... })* }
        void ReadJoint(int parent)
        {
            var name = Next();
            Expect("{");
            Expect("OFFSET");
            var offset = new Vector3(Number(), Number(), Number());
            Expect("CHANNELS");
            var n = int.Parse(Next(), CultureInfo.InvariantCulture);
            var channels = new string[n];
            for (var c = 0; c < n; c++)
            {
                channels[c] = Next();
            }

            var index = joints.Count;
            joints.Add(new Joint(name, parent, offset, channels, channelCount));
            channelCount += n;
            while (true)
            {
                var w = Next();
                if (w == "}")
                {
                    return;
                }

                if (string.Equals(w, "JOINT", StringComparison.OrdinalIgnoreCase))
                {
                    ReadJoint(index);
                }
                else if (string.Equals(w, "End", StringComparison.OrdinalIgnoreCase))
                {
                    Expect("Site");
                    Expect("{");
                    Expect("OFFSET");
                    _ = (Number(), Number(), Number());
                    Expect("}");
                }
                else
                {
                    throw new FormatException($"BVH: 関節 {name} の中に知らない語 {w}。");
                }
            }
        }

        ReadJoint(-1);
        Expect("MOTION");
        Expect("Frames:");
        var frames = int.Parse(Next(), CultureInfo.InvariantCulture);
        Expect("Frame");
        Expect("Time:");
        var frameTime = Number();
        if (frames <= 0 || !(frameTime > 0f))
        {
            throw new FormatException("BVH: フレームの数・長さが正しくありません。");
        }

        var data = new float[frames][];
        for (var f = 0; f < frames; f++)
        {
            var row = new float[channelCount];
            for (var c = 0; c < channelCount; c++)
            {
                row[c] = Number();
            }

            data[f] = row;
        }

        return new BvhClip([.. joints], data, frameTime);
    }

    /// <summary>ファイルから読む。</summary>
    public static BvhClip Load(string path) => Parse(File.ReadAllText(path));
}
