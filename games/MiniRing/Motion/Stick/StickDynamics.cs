using System.Numerics;

namespace MiniRing.Motion.Stick;

/// <summary>体の部位の点に外から加わる力（打たれた・押された・担いだ重さ）。</summary>
/// <param name="Segment">部位。</param>
/// <param name="Rest">力の作用点（部位に固定した、休みの座標）。</param>
/// <param name="Force">力 [N]（世界）。</param>
public readonly record struct StickForce(int Segment, Vector3 Rest, Vector3 Force);

/// <summary>1 歩の力学の結果（docs/STICK_MOTION.md §4）。</summary>
/// <param name="Zmp">床反力の作用点（床の上の点。上向きの床反力が要らない（宙）なら NaN）。</param>
/// <param name="ZmpOutside">ZMP が床に着いた足の凸包の外へ出た距離 [m]（中なら負。足が着いていなければ +∞）。</param>
/// <param name="CapturePoint">捕捉点 ξ = 重心 ＋ 水平の速さ / ω（ゲームの立つ力の物差し、docs/GRAPPLING.md §7）。</param>
/// <param name="CaptureOutside">捕捉点が足の凸包の外へ出た距離 [m]。</param>
/// <param name="GroundForce">床が出す力 [N]（世界）。</param>
/// <param name="Slip">横の床反力が摩擦の上限を超えた割合（|水平| / (μ × 縦) − 1、超えなければ ≤ 0）。</param>
/// <param name="Torques">関節ごとに筋が出すトルク [N·m]（根は 0。蝶番は軸まわりの大きさ、玉は大きさ）。</param>
/// <param name="TorqueVectors">関節ごとのトルクのベクトル（世界、親が子へ）。</param>
/// <param name="Power">関節ごとの仕事率 τ·ω [W]（正で筋が仕事をする）。</param>
public readonly record struct StickLoad(Vector3 Zmp, float ZmpOutside, Vector2 CapturePoint, float CaptureOutside, Vector3 GroundForce, float Slip,
    float[] Torques, Vector3[] TorqueVectors, float[] Power)
{
    /// <summary>関節 <paramref name="segment"/> の負担（トルク ÷ 上限）。</summary>
    public float Ratio(StickBody body, int segment) => body.Segments[segment].MaxTorque > 0f ? Torques[segment] / body.Segments[segment].MaxTorque : 0f;
}

/// <summary>
/// 棒人形の力学: 姿勢の列から、床反力・ZMP・捕捉点・関節のトルク・仕事率を出す（Newton–Euler の逆動力学、部位は質量を持つ箱）。
/// 床に着いている足は呼ぶ側が決める（Solve の support）。足は床に貼り付いたとして、横の力は摩擦の比だけ数える。
/// </summary>
public static class StickDynamics
{
    public static readonly Vector3 Gravity = new(0f, -9.81f, 0f);

    /// <summary>
    /// 歩 <paramref name="previous"/> → <paramref name="current"/> → <paramref name="next"/>（間隔 <paramref name="dt"/>）の真ん中の歩の力学。
    /// 止まった姿勢の静力学は 3 つとも同じ姿勢を渡す。<paramref name="external"/> は外から加わる力。
    /// </summary>
    public static StickLoad Solve(StickFrame previous, StickFrame current, StickFrame next, float dt, (bool Left, bool Right) support,
        IReadOnlyList<StickForce>? external = null, float groundY = 0f)
    {
        ArgumentNullException.ThrowIfNull(previous);
        ArgumentNullException.ThrowIfNull(current);
        ArgumentNullException.ThrowIfNull(next);
        var body = current.Body;
        var n = body.Segments.Count;
        var force = new Vector3[n];
        var moment = new Vector3[n];
        var omega = new Vector3[n];
        var centers = new Vector3[n];
        var comVelocity = Vector3.Zero;
        for (var i = 0; i < n; i++)
        {
            var s = body.Segments[i];
            var c0 = previous.Center(i);
            var c1 = current.Center(i);
            var c2 = next.Center(i);
            centers[i] = c1;
            var a = dt > 0f ? (c2 - (2f * c1) + c0) / (dt * dt) : Vector3.Zero;
            comVelocity += dt > 0f ? (c2 - c0) / (2f * dt) * s.Mass : Vector3.Zero;
            force[i] = s.Mass * (a - Gravity);
            var w0 = dt > 0f ? AngularVelocity(previous.Rotations[i], current.Rotations[i], dt) : Vector3.Zero;
            var w1 = dt > 0f ? AngularVelocity(current.Rotations[i], next.Rotations[i], dt) : Vector3.Zero;
            var w = 0.5f * (w0 + w1);
            omega[i] = w;
            var alpha = dt > 0f ? (w1 - w0) / dt : Vector3.Zero;
            moment[i] = InertiaTimes(current.Rotations[i], s.Inertia, alpha) + Vector3.Cross(w, InertiaTimes(current.Rotations[i], s.Inertia, w));
        }

        comVelocity /= body.TotalMass;

        // 外の力の作用点（世界）。
        var ext = external ?? [];
        var extPoints = ext.Select(f => current.Point(f.Segment, f.Rest)).ToArray();

        // 床が出すべき力とモーメント（原点まわり）: 体全体の F = Σ m(a − g) − 外の力。
        var groundForce = Vector3.Zero;
        var groundMoment = Vector3.Zero;
        for (var i = 0; i < n; i++)
        {
            groundForce += force[i];
            groundMoment += Vector3.Cross(centers[i], force[i]) + moment[i];
        }

        for (var k = 0; k < ext.Count; k++)
        {
            groundForce -= ext[k].Force;
            groundMoment -= Vector3.Cross(extPoints[k], ext[k].Force);
        }

        // ZMP: 床 y = groundY の点 p で、p × R の水平の成分が必要なモーメントと等しくなる所。
        var zmp = new Vector3(float.NaN);
        if (groundForce.Y > 1e-6f)
        {
            var px = (groundMoment.Z + (groundY * groundForce.X)) / groundForce.Y;
            var pz = ((groundY * groundForce.Z) - groundMoment.X) / groundForce.Y;
            zmp = new Vector3(px, groundY, pz);
        }

        var both = new SupportPolygon();
        foreach (var leg in new[] { Hand.Left, Hand.Right })
        {
            if (leg == Hand.Left ? support.Left : support.Right)
            {
                foreach (var p in current.Sole(leg))
                {
                    both.Add(new Vector2(p.X, p.Z));
                }
            }
        }

        both.Build();
        var zmpOutside = float.IsNaN(zmp.X) ? float.PositiveInfinity : both.SignedDistance(new Vector2(zmp.X, zmp.Z));
        var com = current.CenterOfMass;
        var height = MathF.Max(0.02f, com.Y - groundY);
        var omega0 = MathF.Sqrt(-Gravity.Y / height);
        var capture = new Vector2(com.X + (comVelocity.X / omega0), com.Z + (comVelocity.Z / omega0));
        var captureOutside = both.SignedDistance(capture);
        var slip = groundForce.Y > 1e-6f ? (new Vector2(groundForce.X, groundForce.Z).Length() / (body.Friction * groundForce.Y)) - 1f : 1f;

        // 床反力を足へ分ける: 両足なら ZMP（凸包へ寄せた点）を 2 つの足の真ん中を結ぶ線へ落とした所で、てこの比。
        var contacts = new List<StickForce>();
        var contactPoints = new List<Vector3>();
        if (!both.IsEmpty && !float.IsNaN(zmp.X))
        {
            var z = both.Clamp(new Vector2(zmp.X, zmp.Z));
            var centersOf = new[] { Centroid(current, Hand.Left), Centroid(current, Hand.Right) };
            var shares = new[] { support.Left ? 1f : 0f, support.Right ? 1f : 0f };
            if (support.Left && support.Right)
            {
                var d = centersOf[1] - centersOf[0];
                var t = d.LengthSquared() > 1e-9f ? Math.Clamp(Vector2.Dot(z - centersOf[0], d) / d.LengthSquared(), 0f, 1f) : 0.5f;
                shares = [1f - t, t];
            }

            var blend = (centersOf[0] * shares[0]) + (centersOf[1] * shares[1]);
            for (var k = 0; k < 2; k++)
            {
                if (shares[k] <= 0f)
                {
                    continue;
                }

                var leg = k == 0 ? Hand.Left : Hand.Right;
                var at = centersOf[k] + (z - blend);
                contacts.Add(new StickForce(StickBody.Ids.Foot(leg), Vector3.Zero, groundForce * shares[k]));
                contactPoints.Add(new Vector3(at.X, groundY, at.Y));
            }
        }

        // 関節のトルク: 関節より先の部位が要るモーメント − 床・外の力が出すモーメント（関節まわり）。
        var torques = new float[n];
        var vectors = new Vector3[n];
        var power = new float[n];
        for (var j = 1; j < n; j++)
        {
            var inSubtree = body.InSubtree[j];
            var pivot = current.Joints[j];
            var tau = Vector3.Zero;
            for (var i = 0; i < n; i++)
            {
                if (inSubtree[i])
                {
                    tau += Vector3.Cross(centers[i] - pivot, force[i]) + moment[i];
                }
            }

            for (var k = 0; k < ext.Count; k++)
            {
                if (inSubtree[ext[k].Segment])
                {
                    tau -= Vector3.Cross(extPoints[k] - pivot, ext[k].Force);
                }
            }

            for (var k = 0; k < contacts.Count; k++)
            {
                if (inSubtree[contacts[k].Segment])
                {
                    tau -= Vector3.Cross(contactPoints[k] - pivot, contacts[k].Force);
                }
            }

            var s = body.Segments[j];
            vectors[j] = tau;
            torques[j] = s.Kind == StickJointKind.Hinge ? MathF.Abs(Vector3.Dot(tau, Vector3.Transform(s.Axis, current.Rotations[j]))) : tau.Length();
            power[j] = Vector3.Dot(tau, omega[j] - omega[s.Parent]);
        }

        return new StickLoad(zmp, zmpOutside, capture, captureOutside, groundForce, slip, torques, vectors, power);
    }

    /// <summary>止まった姿勢の静力学（重さと外の力だけ）。</summary>
    public static StickLoad Static(StickFrame frame, (bool Left, bool Right) support, IReadOnlyList<StickForce>? external = null)
        => Solve(frame, frame, frame, 0f, support, external);

    private static Vector2 Centroid(StickFrame frame, Hand leg)
    {
        var sum = Vector2.Zero;
        var count = 0;
        foreach (var p in frame.Sole(leg))
        {
            sum += new Vector2(p.X, p.Z);
            count++;
        }

        return sum / count;
    }

    /// <summary>向き a → b を dt で回る角速度（世界）。</summary>
    public static Vector3 AngularVelocity(Quaternion a, Quaternion b, float dt) => StickMath.Log(b * Quaternion.Conjugate(a)) / dt;

    private static Vector3 InertiaTimes(Quaternion rotation, Vector3 inertia, Vector3 v)
    {
        var local = Vector3.Transform(v, Quaternion.Conjugate(rotation));
        return Vector3.Transform(local * inertia, rotation);
    }
}
