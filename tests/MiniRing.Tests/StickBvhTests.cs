using System.Globalization;
using System.Numerics;
using MiniRing.Motion.Stick;

namespace MiniRing.Tests;

/// <summary>
/// BVH の書き出し（<see cref="StickBvh"/>）: 書いた文字列を、ここで別に書いた読み手（BVH の決まり: OFFSET を親の回転で回して足す、回転は Rz·Rx·Ry）で読み直し、
/// 関節の点と部位の先（End Site）が棒人形の順運動学（<see cref="StickFrame"/>）と一致することを確かめる。
/// </summary>
[TestClass]
public sealed class StickBvhTests
{
    private static readonly Lazy<SwingRig> Rig = new(() => SwingRig.Create("jin"));

    [TestMethod]
    public void AnArmedSwing_ReadBackWithTheBvhRules_PutsEveryJointAndTipWhereTheStickFigureHasIt()
    {
        var rig = Rig.Value;
        var genes = rig.RandomGenes(new Random(7));
        var trace = rig.Simulate(genes);
        var bvh = StickBvh.Write(rig.Body, trace.Frames, trace.Dt);

        var parsed = Parse(bvh);
        Assert.AreEqual(trace.Frames.Count, parsed.Frames.Count, "コマの数");
        Assert.AreEqual(trace.Dt, parsed.FrameTime, 1e-6f, "コマの間隔");
        var order = StickBvh.Order(rig.Body);
        Assert.AreEqual(rig.Body.Segments.Count, parsed.Joints.Count(j => !j.EndSite), "部位ごとに 1 本のボーン");
        Assert.IsTrue(parsed.Joints.Any(j => j.Name == "weapon"), "剣もボーン");
        Assert.IsTrue(parsed.Joints.Any(j => j.Name == "upperarm.R") && parsed.Joints.Any(j => j.Name == "upperarm.L"), "左右は .L・.R");

        foreach (var f in new[] { 0, trace.Frames.Count / 3, trace.Frames.Count / 2, trace.Frames.Count - 1 })
        {
            var frame = trace.Frames[f];
            var (positions, _) = Forward(parsed, parsed.Frames[f]);
            var bone = 0;
            for (var j = 0; j < parsed.Joints.Count; j++)
            {
                var joint = parsed.Joints[j];
                var segment = order[joint.EndSite ? joint.Owner : bone];
                var expected = joint.EndSite ? frame.Tip(segment) : frame.Joints[segment];
                Assert.IsTrue(Vector3.Distance(expected, positions[j]) < 1e-3f, $"コマ {f} の {joint.Name}{(joint.EndSite ? " の先" : "")}: {positions[j]} ≠ {expected}");
                if (!joint.EndSite)
                {
                    bone++;
                }
            }
        }
    }

    [TestMethod]
    public void ATwoHandedForm_GetsAGripBoneThatMovesWithTheSword_AtTheOtherHandsGrip()
    {
        var ground = MiniRing.Arena.StickGround.For("jin"); // 刀（両手）
        var form = MiniRing.Bots.FormSwordsman.Forms(MiniRing.Characters.CharacterRoster.Get("jin").Weapon!.Value)[0];
        var result = MiniRing.Arena.StickForm.Run(ground, form);
        var body = result.Body;
        Assert.AreEqual(2, body.GripHands.Count, "刀は両手で握る");
        var bvh = Parse(StickBvh.Write(body, result.Frames, result.Dt));
        var name = StickBvh.OffHandBoneName(body);
        Assert.AreEqual("grip.L", name.Replace(".R", ".L", StringComparison.Ordinal), "名前は grip.L か grip.R");
        var d = StickBvh.OffHandGrip(body, result.Frames);
        Assert.IsTrue(MathF.Abs(d) > 0.01f, $"両手の間隔 {d:0.000} m");

        var segments = Enumerable.Range(0, body.Segments.Count).ToDictionary(i => StickBvh.BoneName(body.Segments[i]));
        var gripEnd = bvh.Joints.FindIndex(j => j.EndSite && j.Name == name);
        Assert.IsTrue(gripEnd > 0, "握りのボーンに先がある");
        var other = body.GripHands.Select(StickBody.Ids.Forearm).Single(f => f != body.Segments[StickBody.Ids.Weapon].Parent);
        var slip = 0f;
        for (var f = 0; f < result.Frames.Count; f++)
        {
            var frame = result.Frames[f];
            var (positions, _) = Forward(bvh, bvh.Frames[f]);
            for (var j = 0; j < bvh.Joints.Count; j++)
            {
                var joint = bvh.Joints[j];
                if (joint.Name == name)
                {
                    continue;
                }

                var s = segments[joint.Name];
                var expected = joint.EndSite ? frame.Tip(s) : frame.Joints[s];
                Assert.IsTrue(Vector3.Distance(expected, positions[j]) < 1e-3f, $"コマ {f} の {joint.Name}: 握りのボーンを足しても、ほかのボーンの点は変わらない");
            }

            var grip = frame.Joints[StickBody.Ids.Weapon] + (Vector3.Normalize(frame.Tip(StickBody.Ids.Weapon) - frame.Joints[StickBody.Ids.Weapon]) * d);
            Assert.IsTrue(Vector3.Distance(grip, positions[gripEnd]) < 1e-3f, $"コマ {f}: 握りのボーンの先は剣の軸の上で、剣と一緒に動く");
            slip += Vector3.Distance(frame.Tip(other), positions[gripEnd]);
        }

        Assert.IsTrue(slip / result.Frames.Count < 0.03f, $"もう一方の手首は握りの点の近く（平均 {slip / result.Frames.Count * 100f:0.0} cm）");
    }

    [TestMethod]
    public void TheScale_MultipliesTheLengths_AndKeepsTheAngles()
    {
        var rig = Rig.Value;
        var trace = rig.Simulate(rig.RandomGenes(new Random(9)));
        var one = Parse(StickBvh.Write(rig.Body, trace.Frames, trace.Dt));
        var three = Parse(StickBvh.Write(rig.Body, trace.Frames, trace.Dt, 3f));
        for (var j = 0; j < one.Joints.Count; j++)
        {
            Assert.IsTrue(Vector3.Distance(one.Joints[j].Offset * 3f, three.Joints[j].Offset) < 1e-4f, one.Joints[j].Name);
        }

        var last = trace.Frames.Count - 1;
        Assert.AreEqual(one.Frames[last][1] * 3f, three.Frames[last][1], 1e-4f, "根の高さ");
        Assert.AreEqual(one.Frames[last][^1], three.Frames[last][^1], 1e-4f, "角は変わらない");
    }

    [TestMethod]
    public void TheEulerAngles_RebuildTheRotation_InTheZxyOrder()
    {
        var random = new Random(11);
        for (var i = 0; i < 200; i++)
        {
            var axis = Vector3.Normalize(new Vector3(random.NextSingle() - 0.5f, random.NextSingle() - 0.5f, random.NextSingle() - 0.5f));
            var q = Quaternion.CreateFromAxisAngle(axis, (random.NextSingle() - 0.5f) * 5f);
            var e = StickBvh.EulerZxyDegrees(q);
            var m = Euler(e.Z, e.X, e.Y);
            var v = new Vector3(0.3f, -0.7f, 0.5f);
            Assert.IsTrue(Vector3.Distance(Vector3.Transform(v, q), Mul(m, v)) < 1e-4f, $"角 {e}");
        }
    }

    // ---- ここで別に書いた BVH の読み手 ----

    private sealed record Joint(string Name, int Parent, bool EndSite, int Owner)
    {
        public Vector3 Offset { get; set; }
    }

    private sealed record Bvh(List<Joint> Joints, List<float[]> Frames, float FrameTime);

    private static Bvh Parse(string text)
    {
        var joints = new List<Joint>();
        var stack = new Stack<int>();
        var lines = text.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        var at = 0;
        var boneCount = 0;
        for (; at < lines.Length && lines[at] != "MOTION"; at++)
        {
            var words = lines[at].Split(' ', '\t');
            switch (words[0])
            {
                case "ROOT" or "JOINT":
                    joints.Add(new Joint(words[1], stack.Count > 0 ? stack.Peek() : -1, false, boneCount++));
                    break;
                case "End":
                    joints.Add(new Joint(joints[stack.Peek()].Name, stack.Peek(), true, joints[stack.Peek()].Owner));
                    break;
                case "{":
                    stack.Push(joints.Count - 1);
                    break;
                case "}":
                    stack.Pop();
                    break;
                case "OFFSET":
                    joints[stack.Peek()].Offset = new Vector3(F(words[1]), F(words[2]), F(words[3]));
                    break;
            }
        }

        var count = int.Parse(lines[at + 1].Split(':')[1], CultureInfo.InvariantCulture);
        var frameTime = F(lines[at + 2].Split(':')[1].Trim());
        var frames = lines.Skip(at + 3).Take(count).Select(l => l.Split(' ').Select(F).ToArray()).ToList();
        return new Bvh(joints, frames, frameTime);
    }

    /// <summary>BVH の決まりどおりの順運動学: 根は位置の数、ほかは親の点 + 親の回転 × OFFSET。回転は親の回転 × Rz·Rx·Ry。</summary>
    private static (Vector3[] Positions, float[][,] Rotations) Forward(Bvh bvh, float[] values)
    {
        var n = bvh.Joints.Count;
        var positions = new Vector3[n];
        var rotations = new float[n][,];
        var at = 0;
        for (var j = 0; j < n; j++)
        {
            var joint = bvh.Joints[j];
            if (joint.Parent < 0)
            {
                positions[j] = new Vector3(values[at], values[at + 1], values[at + 2]) + joint.Offset;
                rotations[j] = Euler(values[at + 3], values[at + 4], values[at + 5]);
                at += 6;
                continue;
            }

            positions[j] = positions[joint.Parent] + Mul(rotations[joint.Parent], joint.Offset);
            if (joint.EndSite)
            {
                rotations[j] = rotations[joint.Parent];
                continue;
            }

            rotations[j] = Mul(rotations[joint.Parent], Euler(values[at], values[at + 1], values[at + 2]));
            at += 3;
        }

        return (positions, rotations);
    }

    private static float[,] Euler(float zDegrees, float xDegrees, float yDegrees)
    {
        var (z, x, y) = (zDegrees * MathF.PI / 180f, xDegrees * MathF.PI / 180f, yDegrees * MathF.PI / 180f);
        var rz = new float[,] { { MathF.Cos(z), -MathF.Sin(z), 0f }, { MathF.Sin(z), MathF.Cos(z), 0f }, { 0f, 0f, 1f } };
        var rx = new float[,] { { 1f, 0f, 0f }, { 0f, MathF.Cos(x), -MathF.Sin(x) }, { 0f, MathF.Sin(x), MathF.Cos(x) } };
        var ry = new float[,] { { MathF.Cos(y), 0f, MathF.Sin(y) }, { 0f, 1f, 0f }, { -MathF.Sin(y), 0f, MathF.Cos(y) } };
        return Mul(Mul(rz, rx), ry);
    }

    private static float[,] Mul(float[,] a, float[,] b)
    {
        var m = new float[3, 3];
        for (var i = 0; i < 3; i++)
        {
            for (var j = 0; j < 3; j++)
            {
                for (var k = 0; k < 3; k++)
                {
                    m[i, j] += a[i, k] * b[k, j];
                }
            }
        }

        return m;
    }

    private static Vector3 Mul(float[,] m, Vector3 v)
        => new((m[0, 0] * v.X) + (m[0, 1] * v.Y) + (m[0, 2] * v.Z), (m[1, 0] * v.X) + (m[1, 1] * v.Y) + (m[1, 2] * v.Z), (m[2, 0] * v.X) + (m[2, 1] * v.Y) + (m[2, 2] * v.Z));

    private static float F(string s) => float.Parse(s, NumberStyles.Float, CultureInfo.InvariantCulture);
}
