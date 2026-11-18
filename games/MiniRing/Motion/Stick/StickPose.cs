using System.Numerics;

namespace MiniRing.Motion.Stick;

/// <summary>
/// 棒人形の姿勢（docs/STICK_MOTION.md §3）: 根（骨盤の中心の位置と向き）と、関節ごとの休みからの回転（玉は回転ベクトル、蝶番は角）。
/// 座標は棒人形の世界（+Z 前・+Y 上・床は y = 0）。値の並びは <see cref="StickBody.Offsets"/>。
/// </summary>
public sealed class StickPose
{
    public StickPose(float[] values)
    {
        ArgumentNullException.ThrowIfNull(values);
        Values = values;
    }

    public float[] Values { get; }

    public StickPose Copy() => new((float[])Values.Clone());

    /// <summary>休みの姿勢（T ポーズ、どの関節も回っていない）。</summary>
    public static StickPose Rest(int dof) => new(new float[dof]);

    public Vector3 RootPosition(StickBody body) => new(Values[0], Values[1], Values[2]);

    /// <summary>関節の範囲へ寄せる（玉は振れの角とねじれの角を別々に丸める、蝶番は角を丸める）。根は寄せない。</summary>
    public void Project(StickBody body)
    {
        for (var i = 1; i < body.Segments.Count; i++)
        {
            var s = body.Segments[i];
            var at = body.Offsets[i];
            switch (s.Kind)
            {
                case StickJointKind.Hinge:
                    Values[at] = Math.Clamp(Values[at], s.TwistMin, s.TwistMax);
                    break;
                case StickJointKind.Ball:
                    var q = StickMath.Exp(new Vector3(Values[at], Values[at + 1], Values[at + 2]));
                    var (swing, twist) = StickMath.SwingTwist(q, s.Axis);
                    var swingAngle = StickMath.Angle(swing);
                    var twistAngle = StickMath.SignedAngle(twist, s.Axis);
                    if (swingAngle > s.SwingLimit || twistAngle < s.TwistMin || twistAngle > s.TwistMax)
                    {
                        var swingAxis = StickMath.AxisOf(swing);
                        var clamped = Quaternion.CreateFromAxisAngle(swingAxis, MathF.Min(swingAngle, s.SwingLimit))
                            * Quaternion.CreateFromAxisAngle(s.Axis, Math.Clamp(twistAngle, s.TwistMin, s.TwistMax));
                        var r = StickMath.Log(clamped);
                        (Values[at], Values[at + 1], Values[at + 2]) = (r.X, r.Y, r.Z);
                    }

                    break;
            }
        }
    }

    /// <summary>関節 <paramref name="segment"/> の、休みからの回転（親の座標）。</summary>
    public Quaternion Local(StickBody body, int segment)
    {
        var s = body.Segments[segment];
        var at = body.Offsets[segment];
        return s.Kind switch
        {
            StickJointKind.Root or StickJointKind.Ball => StickMath.Exp(new Vector3(Values[at + (s.Kind == StickJointKind.Root ? 3 : 0)], Values[at + (s.Kind == StickJointKind.Root ? 4 : 1)], Values[at + (s.Kind == StickJointKind.Root ? 5 : 2)])),
            StickJointKind.Hinge => Quaternion.CreateFromAxisAngle(s.Axis, Values[at]),
            _ => Quaternion.Identity,
        };
    }

    /// <summary>
    /// ゲームの体の今の姿勢を写す: <paramref name="game"/> を <paramref name="seconds"/> 秒進め、部位の向きから関節の回転を逆算する
    /// （向き合わせの回転を外し、足の裏の低い所が床 y = 0 に来るよう根を置く）。範囲の外は寄せる。
    /// </summary>
    public static StickPose Capture(StickBody body, BoxingGame game, Fighter fighter, float seconds)
    {
        ArgumentNullException.ThrowIfNull(body);
        ArgumentNullException.ThrowIfNull(game);
        ArgumentNullException.ThrowIfNull(fighter);
        for (var t = 0f; t < seconds; t += 1f / 60f)
        {
            game.Step(default, 1f / 60f);
        }

        var p = fighter.Parts;
        var world = game.World;
        var unyaw = Quaternion.CreateFromAxisAngle(Vector3.UnitY, -fighter.Frame.YawRadians);
        var ids = new int[StickBody.Ids.Count];
        ids[StickBody.Ids.Pelvis] = p.Pelvis;
        ids[StickBody.Ids.Chest] = p.Chest;
        ids[StickBody.Ids.Head] = p.Head;
        ids[StickBody.Ids.UpperArmL] = p.RightUpperArm;
        ids[StickBody.Ids.ForearmL] = p.RightForearm;
        ids[StickBody.Ids.UpperArmR] = p.LeftUpperArm;
        ids[StickBody.Ids.ForearmR] = p.LeftForearm;
        ids[StickBody.Ids.ThighL] = p.RightThigh;
        ids[StickBody.Ids.ShinL] = p.RightShin;
        ids[StickBody.Ids.FootL] = p.RightFoot;
        ids[StickBody.Ids.ThighR] = p.LeftThigh;
        ids[StickBody.Ids.ShinR] = p.LeftShin;
        ids[StickBody.Ids.FootR] = p.LeftFoot;
        var rotations = ids.Select(id => Quaternion.Normalize(unyaw * world.GetState(id).Orientation)).ToArray();
        var pose = Rest(body.Dof);
        var root = StickMath.Log(rotations[0]);
        (pose.Values[3], pose.Values[4], pose.Values[5]) = (root.X, root.Y, root.Z);
        for (var i = 1; i < body.Segments.Count; i++)
        {
            var s = body.Segments[i];
            var local = Quaternion.Normalize(Quaternion.Conjugate(rotations[s.Parent]) * rotations[i]);
            var at = body.Offsets[i];
            if (s.Kind == StickJointKind.Hinge)
            {
                pose.Values[at] = StickMath.SignedAngle(StickMath.SwingTwist(local, s.Axis).Twist, s.Axis);
            }
            else
            {
                var r = StickMath.Log(local);
                (pose.Values[at], pose.Values[at + 1], pose.Values[at + 2]) = (r.X, r.Y, r.Z);
            }
        }

        pose.Project(body);
        var lowest = new StickFrame(body, pose).LowestSole();
        pose.Values[1] -= lowest;
        return pose;
    }
}

/// <summary>姿勢から出した世界の点・向き（順運動学の結果）。</summary>
public sealed class StickFrame
{
    public StickFrame(StickBody body, StickPose pose)
    {
        ArgumentNullException.ThrowIfNull(body);
        ArgumentNullException.ThrowIfNull(pose);
        Body = body;
        var n = body.Segments.Count;
        Rotations = new Quaternion[n];
        Joints = new Vector3[n];
        for (var i = 0; i < n; i++)
        {
            var s = body.Segments[i];
            if (s.Parent < 0)
            {
                Rotations[i] = pose.Local(body, i);
                Joints[i] = pose.RootPosition(body);
            }
            else
            {
                var parent = body.Segments[s.Parent];
                Rotations[i] = Quaternion.Normalize(Rotations[s.Parent] * pose.Local(body, i));
                Joints[i] = Joints[s.Parent] + Vector3.Transform(s.Joint - parent.Joint, Rotations[s.Parent]);
            }
        }
    }

    public StickBody Body { get; }

    /// <summary>部位ごとの世界の向き（休みの姿勢から）。</summary>
    public Quaternion[] Rotations { get; }

    /// <summary>部位ごとの関節の点（根は骨盤の中心）。</summary>
    public Vector3[] Joints { get; }

    /// <summary>部位 <paramref name="segment"/> に固定した点（休みの座標 <paramref name="rest"/>）の世界の位置。</summary>
    public Vector3 Point(int segment, Vector3 rest) => Joints[segment] + Vector3.Transform(rest - Body.Segments[segment].Joint, Rotations[segment]);

    public Vector3 Tip(int segment) => Point(segment, Body.Segments[segment].Tip);

    public Vector3 Center(int segment) => Point(segment, Body.Segments[segment].Center);

    /// <summary>全身の重心。</summary>
    public Vector3 CenterOfMass
    {
        get
        {
            var sum = Vector3.Zero;
            for (var i = 0; i < Body.Segments.Count; i++)
            {
                sum += Center(i) * Body.Segments[i].Mass;
            }

            return sum / Body.TotalMass;
        }
    }

    /// <summary>足 <paramref name="leg"/> の裏の点（世界）。</summary>
    public IEnumerable<Vector3> Sole(Hand leg) => Body.SoleOf(leg).Select(p => Point(StickBody.Ids.Foot(leg), p));

    /// <summary>足の裏の点のいちばん低い高さ。</summary>
    public float LowestSole() => Sole(Hand.Left).Concat(Sole(Hand.Right)).Min(p => p.Y);

    /// <summary>肩の中心（ゲームの体フレームの原点、<see cref="Fighter.Frame"/>）。</summary>
    public Vector3 ShoulderCenter => 0.5f * (Joints[StickBody.Ids.UpperArmL] + Joints[StickBody.Ids.UpperArmR]);
}

/// <summary>回転の小道具（回転ベクトル・振れとねじれの分解）。</summary>
public static class StickMath
{
    public static Quaternion Exp(Vector3 r)
    {
        var angle = r.Length();
        return angle < 1e-9f ? Quaternion.Identity : Quaternion.CreateFromAxisAngle(r / angle, angle);
    }

    public static Vector3 Log(Quaternion q)
    {
        q = Quaternion.Normalize(q);
        if (q.W < 0f)
        {
            q = -q;
        }

        var v = new Vector3(q.X, q.Y, q.Z);
        var s = v.Length();
        return s < 1e-9f ? Vector3.Zero : v / s * (2f * MathF.Atan2(s, q.W));
    }

    /// <summary>q = 振れ × ねじれ（ねじれは <paramref name="axis"/> まわり）。</summary>
    public static (Quaternion Swing, Quaternion Twist) SwingTwist(Quaternion q, Vector3 axis)
    {
        var v = new Vector3(q.X, q.Y, q.Z);
        var p = Vector3.Dot(v, axis) * axis;
        var twist = new Quaternion(p, q.W);
        var length = twist.Length();
        twist = length < 1e-9f ? Quaternion.Identity : Quaternion.Normalize(twist);
        return (Quaternion.Normalize(q * Quaternion.Conjugate(twist)), twist);
    }

    public static float Angle(Quaternion q) => 2f * MathF.Acos(Math.Clamp(MathF.Abs(q.W), 0f, 1f));

    public static float SignedAngle(Quaternion q, Vector3 axis)
    {
        var s = Vector3.Dot(new Vector3(q.X, q.Y, q.Z), axis);
        var angle = 2f * MathF.Atan2(MathF.Abs(s), MathF.Abs(q.W));
        return MathF.Sign(s) * MathF.Sign(q.W == 0f ? 1f : q.W) * angle;
    }

    /// <summary>線分 a–b と線分 p–q の最も近い距離。</summary>
    public static float SegmentDistance(Vector3 a, Vector3 b, Vector3 p, Vector3 q)
    {
        var d1 = b - a;
        var d2 = q - p;
        var r = a - p;
        var aa = Vector3.Dot(d1, d1);
        var ee = Vector3.Dot(d2, d2);
        var f = Vector3.Dot(d2, r);
        float s;
        float t;
        if (aa <= 1e-12f && ee <= 1e-12f)
        {
            return r.Length();
        }

        if (aa <= 1e-12f)
        {
            (s, t) = (0f, Math.Clamp(f / ee, 0f, 1f));
        }
        else
        {
            var c = Vector3.Dot(d1, r);
            if (ee <= 1e-12f)
            {
                (s, t) = (Math.Clamp(-c / aa, 0f, 1f), 0f);
            }
            else
            {
                var bb = Vector3.Dot(d1, d2);
                var denom = (aa * ee) - (bb * bb);
                s = denom > 1e-12f ? Math.Clamp(((bb * f) - (c * ee)) / denom, 0f, 1f) : 0f;
                t = ((bb * s) + f) / ee;
                if (t < 0f)
                {
                    (t, s) = (0f, Math.Clamp(-c / aa, 0f, 1f));
                }
                else if (t > 1f)
                {
                    (t, s) = (1f, Math.Clamp((bb - c) / aa, 0f, 1f));
                }
            }
        }

        return Vector3.Distance(a + (d1 * s), p + (d2 * t));
    }

    public static Vector3 AxisOf(Quaternion q)
    {
        var v = new Vector3(q.X, q.Y, q.Z);
        var length = v.Length();
        return length < 1e-9f ? Vector3.UnitX : v / length * MathF.Sign(q.W == 0f ? 1f : q.W);
    }
}
