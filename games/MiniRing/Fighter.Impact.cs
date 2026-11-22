using System.Numerics;
using Aigis.Physics;

namespace MiniRing;

/// <summary>
/// 当たりの有効質量（2026-11-22、ユーザーの指定: 拳に乗る体重を式の近道でなく物理から）。
/// <para>
/// 体は関節でつながった剛体の木（骨盤・腹・胸・頭・腕・脚）。当たった点に向き n の撃力を受けたとき、その点の速度がどれだけ変わるかを、
/// 関節で連なった体全体の慣性から出す（Featherstone の articulated-body inertia。当たった部位を根にして、木の葉から根へ慣性を畳み込む）。
/// 玉継手（肩・股・背骨・首）は力を伝え、回りは自由。蝶番（肘・膝・足首）は軸のまわりだけ自由で、軸に直交するモーメントは伝える。
/// 打撃の接触は数ミリ秒〜十数ミリ秒で、関節の筋（サーボ）がその間に出せる力積は小さいので、関節は受け身（自由）として扱う。
/// </para>
/// <para>
/// だから「体重が乗る」のは物理の結果になる: 肩 → 肘 → 拳がまっすぐ並んで当たる向きへ揃えば、骨（関節の拘束）を通して胴の質量まで伝わり重く、
/// 肘が曲がって当たる向きと外れれば前腕が肘で回って逃げて軽い。胴がひねられて肩が前へ出ていれば、腕は胴に対して揃う。
/// 床・脚の支えはこの短い間では効かないとみなす（体は宙の木）。
/// </para>
/// </summary>
public sealed partial class Fighter
{
    /// <summary>当たりを関節で連なった体の衝突として解くか（<see cref="FighterTuning.ArticulatedImpact"/>。関節駆動の体は自前の接触の力積を使う）。</summary>
    internal bool UsesArticulatedImpact => _tuning.ArticulatedImpact && !_tuning.JointDrive;

    /// <summary>
    /// 部位 <paramref name="body"/>（体の部位、または手にはめた物・握った物の剛体）の点 <paramref name="point"/> に向き <paramref name="direction"/> の撃力を受けたときの、
    /// 関節で連なった体全体の有効質量 [kg]（その点のその向きの速度の変化 = 撃力 ÷ この値）。手にはめた物・握った物はその手の前腕に固く付いているとみなす。
    /// 体の部位でも手の物でもなければ、その剛体の質量。
    /// </summary>
    public float ArticulatedMass(int body, Vector3 point, Vector3 direction, float closingSpeed = 0f)
    {
        if (direction.LengthSquared() < 1e-12f)
        {
            return _world.GetMass(body);
        }

        var n = Vector3.Normalize(direction);
        var root = RootPartOf(body);
        if (root is not { } start)
        {
            return _world.GetMass(body);
        }

        // 木: 付いている部位どうしを、いまもつながっている関節で結ぶ（折れた・外れた所はつながっていない）。
        var edges = new Dictionary<int, List<(int Other, ImpactJoint Joint)>>();
        foreach (var joint in _joints)
        {
            if (_detached.Contains(joint.ParentPart) || _detached.Contains(joint.ChildPart)
                || _world.GetJointAnchors(joint.PrimaryConstraintId)?.AnchorA is not { } anchor)
            {
                continue;
            }

            var j = new ImpactJoint(anchor, HingeAxis(joint.Joint, joint.ParentPart), joint.Joint);
            Add(joint.ParentPart, joint.ChildPart, j);
            Add(joint.ChildPart, joint.ParentPart, j);
        }

        // 根から幅優先で並べ、葉から根へ畳む。
        var order = new List<(int Part, int Parent, ImpactJoint Joint)> { (start, -1, default) };
        var seen = new HashSet<int> { start };
        for (var i = 0; i < order.Count; i++)
        {
            if (!edges.TryGetValue(order[i].Part, out var next))
            {
                continue;
            }

            foreach (var (other, joint) in next)
            {
                if (seen.Add(other))
                {
                    order.Add((other, order[i].Part, joint));
                }
            }
        }

        var inertia = new Dictionary<int, Spatial>();
        foreach (var (part, _, _) in order)
        {
            inertia[part] = SpatialInertia(part, point);
        }

        for (var i = order.Count - 1; i > 0; i--)
        {
            var (part, parent, joint) = order[i];
            inertia[parent] = inertia[parent].Add(Transmit(inertia[part], joint, point, Bracing(joint, part, point, closingSpeed)));
        }

        // 根は自由な体: 点に [0; n] の撃力（点まわりのモーメント 0）。速度の変化の並進の分が点の速度の変化。
        var dv = inertia[start].Solve([0, 0, 0, n.X, n.Y, n.Z]);
        var along = (dv[3] * n.X) + (dv[4] * n.Y) + (dv[5] * n.Z);
        return along > 1e-9 ? (float)(1.0 / along) : _world.GetMass(body);

        void Add(int a, int b, ImpactJoint j)
        {
            if (!edges.TryGetValue(a, out var list))
            {
                edges[a] = list = [];
            }

            list.Add((b, j));
        }
    }

    /// <summary>
    /// 関節の筋の構え: 打撃の接触の間（<see cref="FighterTuning.ImpactContactSeconds"/> = T）に、関節の筋（体が使うのと同じサーボのばね k・減衰 b）が
    /// 関節の回りを受け止める分を、関節の回りに足す慣性 D [kg·m²] とみなす: D = k T²/π² + b T（半周期の正弦の接触の間のばねと減衰の力積）。
    /// k は筋のばねの振動数 f と、関節より先の部位の関節まわりの慣性 I から k = I (2πf)²、b は臨界減衰 2√(k I)。
    /// 筋の力には上限がある: 接触の間に関節が出せる角の力積は τ_max T で、打撃が関節を回そうとする速さ v / r（r は関節から当たった点まで）を止めるには
    /// D (v / r) だけ要るので、D ≤ τ_max T r / v（打撃が速いほど、筋は関節を固めきれずに曲がる）。v が 0（速さを渡さない）なら上限を掛けない。
    /// 疲れによる筋の弱り（2026-11-22、スタミナの仕様）: ばね k も出せるトルクも、その関節を動かす部位の駆動の割合 <see cref="VigorOf"/> に比例して落ちる
    /// （酸素の切れた腕は当たりの瞬間に関節を固められない）。意識による弱りは入れない（反射の固さ）。
    /// </summary>
    private double Bracing(ImpactJoint joint, int child, Vector3 point, float closingSpeed)
    {
        var t = _tuning;
        var scale = t.TorqueScale;
        var (hz, maxTorque) = joint.Kind switch
        {
            HumanoidRagdoll.RagdollJoint.LeftShoulder or HumanoidRagdoll.RagdollJoint.RightShoulder => (t.ArmServoHz, 40f * scale),
            HumanoidRagdoll.RagdollJoint.LeftElbow or HumanoidRagdoll.RagdollJoint.RightElbow => (t.ArmServoHz, 30f * scale),
            HumanoidRagdoll.RagdollJoint.Neck => (t.NeckServoHz, t.NeckMaxTorque * scale),
            HumanoidRagdoll.RagdollJoint.LeftHip or HumanoidRagdoll.RagdollJoint.RightHip => (t.LegServoHz, t.HipMaxTorque * scale),
            HumanoidRagdoll.RagdollJoint.LeftKnee or HumanoidRagdoll.RagdollJoint.RightKnee => (t.LegServoHz, t.KneeMaxTorque * scale),
            HumanoidRagdoll.RagdollJoint.LeftAnkle or HumanoidRagdoll.RagdollJoint.RightAnkle => (t.LegServoHz, t.AnkleMaxTorque * scale),
            _ => (0f, t.TrunkRotatorTorque * scale), // 背骨（胴の関節の筋: ばねは DriveRelativeTorso と同じ τ / 可動域）
        };

        var vigor = joint.Kind switch
        {
            HumanoidRagdoll.RagdollJoint.LeftShoulder or HumanoidRagdoll.RagdollJoint.LeftElbow => VigorOf(MuscleRegion.LeftArm),
            HumanoidRagdoll.RagdollJoint.RightShoulder or HumanoidRagdoll.RagdollJoint.RightElbow => VigorOf(MuscleRegion.RightArm),
            HumanoidRagdoll.RagdollJoint.Neck => 1f,
            HumanoidRagdoll.RagdollJoint.LeftHip or HumanoidRagdoll.RagdollJoint.RightHip or HumanoidRagdoll.RagdollJoint.LeftKnee
                or HumanoidRagdoll.RagdollJoint.RightKnee or HumanoidRagdoll.RagdollJoint.LeftAnkle or HumanoidRagdoll.RagdollJoint.RightAnkle => VigorOf(MuscleRegion.Legs),
            _ => VigorOf(MuscleRegion.Trunk),
        };
        maxTorque *= vigor;
        var s = _world.GetState(child);
        var d = s.Position - joint.Anchor;
        var local = _world.GetLocalInertiaDiagonal(child);
        var inertia = MathF.Max(local.X, MathF.Max(local.Y, local.Z)) + (_world.GetMass(child) * d.LengthSquared());
        var stiffness = (hz > 0f ? inertia * MathF.Pow(2f * MathF.PI * hz, 2f) * vigor : maxTorque / MathF.Max(0.1f, PunchTwistLimit));
        var damping = 2f * MathF.Sqrt(stiffness * inertia);
        var contact = t.ImpactContactSeconds;
        double bracing = (stiffness * contact * contact / (MathF.PI * MathF.PI)) + (damping * contact);
        if (closingSpeed > 1e-3f)
        {
            var lever = MathF.Max(0.02f, Vector3.Distance(point, joint.Anchor));
            bracing = Math.Min(bracing, maxTorque * contact * lever / closingSpeed);
        }

        return bracing;
    }

    /// <summary>関節: 中心（ワールド）と、蝶番なら軸（ワールド。玉継手は null）。</summary>
    private readonly record struct ImpactJoint(Vector3 Anchor, Vector3? Axis, HumanoidRagdoll.RagdollJoint Kind);

    /// <summary>撃力を受けた剛体の、体の中での根の部位: 付いている部位ならそれ、手にはめた物・握った物ならその手の前腕。</summary>
    private int? RootPartOf(int body)
    {
        if (_all.Contains(body))
        {
            return _detached.Contains(body) ? null : body;
        }

        foreach (var arm in _arms)
        {
            if (!arm.Severed && arm.Held?.Body == body)
            {
                return arm.Forearm;
            }
        }

        return null;
    }

    /// <summary>蝶番の関節の軸（ワールド）: 肘は上腕の Y、膝・足首は親（腿・すね）の X（HumanoidRagdoll が立てた形で作った軸）。それ以外は玉継手（null）。</summary>
    private Vector3? HingeAxis(HumanoidRagdoll.RagdollJoint joint, int parent) => joint switch
    {
        HumanoidRagdoll.RagdollJoint.LeftElbow or HumanoidRagdoll.RagdollJoint.RightElbow
            => Vector3.Transform(Vector3.UnitY, _world.GetState(parent).Orientation),
        HumanoidRagdoll.RagdollJoint.LeftKnee or HumanoidRagdoll.RagdollJoint.RightKnee or HumanoidRagdoll.RagdollJoint.LeftAnkle or HumanoidRagdoll.RagdollJoint.RightAnkle
            => Vector3.Transform(Vector3.UnitX, _world.GetState(parent).Orientation),
        _ => null,
    };

    /// <summary>部位（と、その前腕に付いた手の物）の、点 <paramref name="origin"/> まわりの空間の慣性。</summary>
    private Spatial SpatialInertia(int part, Vector3 origin)
    {
        var sum = Spatial.Of(_world.GetMass(part), _world.GetState(part), _world.GetLocalInertiaDiagonal(part), origin);
        foreach (var arm in _arms)
        {
            if (!arm.Severed && arm.Forearm == part && arm.Held is { } held)
            {
                sum = sum.Add(Spatial.Of(_world.GetMass(held.Body), _world.GetState(held.Body), _world.GetLocalInertiaDiagonal(held.Body), origin));
            }
        }

        return sum;
    }

    /// <summary>
    /// 子の articulated inertia を、受け身の関節を通して親へ伝わる分にする: I − I S (Sᵀ I S)⁻¹ Sᵀ I（S は関節が許す動きの基底:
    /// 玉継手は中心まわりの 3 つの回り、蝶番は軸まわりの 1 つ）。
    /// </summary>
    private static Spatial Transmit(Spatial child, ImpactJoint joint, Vector3 origin, double bracing)
    {
        var axes = joint.Axis is { } axis ? new[] { Vector3.Normalize(axis) } : new[] { Vector3.UnitX, Vector3.UnitY, Vector3.UnitZ };
        var k = axes.Length;
        var s = new double[6, k];
        for (var c = 0; c < k; c++)
        {
            var e = axes[c];
            var v = Vector3.Cross(e, origin - joint.Anchor); // 関節の中心まわりの回りの、点 origin での速さ
            s[0, c] = e.X;
            s[1, c] = e.Y;
            s[2, c] = e.Z;
            s[3, c] = v.X;
            s[4, c] = v.Y;
            s[5, c] = v.Z;
        }

        var u = new double[6, k]; // I S
        for (var r = 0; r < 6; r++)
        {
            for (var c = 0; c < k; c++)
            {
                double acc = 0;
                for (var m = 0; m < 6; m++)
                {
                    acc += child.M[r, m] * s[m, c];
                }

                u[r, c] = acc;
            }
        }

        var d = new double[k, k]; // Sᵀ I S
        for (var a = 0; a < k; a++)
        {
            for (var b = 0; b < k; b++)
            {
                double acc = 0;
                for (var m = 0; m < 6; m++)
                {
                    acc += s[m, a] * u[m, b];
                }

                d[a, b] = acc + (a == b ? bracing : 0); // 関節の筋の構え: 関節の回りに足す慣性（Bracing）
            }
        }

        var dInv = Invert(d);
        var result = new double[6, 6];
        for (var r = 0; r < 6; r++)
        {
            for (var c = 0; c < 6; c++)
            {
                double acc = 0;
                for (var a = 0; a < k; a++)
                {
                    for (var b = 0; b < k; b++)
                    {
                        acc += u[r, a] * dInv[a, b] * u[c, b];
                    }
                }

                result[r, c] = child.M[r, c] - acc;
            }
        }

        return new Spatial(result);
    }

    private static double[,] Invert(double[,] m)
    {
        var k = m.GetLength(0);
        var a = (double[,])m.Clone();
        var inv = new double[k, k];
        for (var i = 0; i < k; i++)
        {
            inv[i, i] = 1;
        }

        for (var col = 0; col < k; col++)
        {
            var pivot = col;
            for (var r = col + 1; r < k; r++)
            {
                if (Math.Abs(a[r, col]) > Math.Abs(a[pivot, col]))
                {
                    pivot = r;
                }
            }

            if (Math.Abs(a[pivot, col]) < 1e-12)
            {
                return new double[k, k]; // 動きの無い関節（慣性 0）: 伝える分を引かない
            }

            for (var c = 0; c < k; c++)
            {
                (a[col, c], a[pivot, c]) = (a[pivot, c], a[col, c]);
                (inv[col, c], inv[pivot, c]) = (inv[pivot, c], inv[col, c]);
            }

            var p = a[col, col];
            for (var c = 0; c < k; c++)
            {
                a[col, c] /= p;
                inv[col, c] /= p;
            }

            for (var r = 0; r < k; r++)
            {
                if (r == col)
                {
                    continue;
                }

                var f = a[r, col];
                for (var c = 0; c < k; c++)
                {
                    a[r, c] -= f * a[col, c];
                    inv[r, c] -= f * inv[col, c];
                }
            }
        }

        return inv;
    }

    /// <summary>
    /// 空間の慣性（6×6、ある点 O まわり。速度は [ω; v_O]、運動量は [O まわりの角運動量; 運動量]）:
    /// [[I_c − m[c]×[c]×, m[c]×], [−m[c]×, m·1]]（c は重心 − O）。
    /// </summary>
    private readonly record struct Spatial(double[,] M)
    {
        public static Spatial Of(float mass, BodyState state, Vector3 localInertia, Vector3 origin)
        {
            var r = Matrix4x4.CreateFromQuaternion(state.Orientation);
            // ワールドの慣性テンソル R diag Rᵀ。
            var col = new[] { new Vector3(r.M11, r.M12, r.M13), new Vector3(r.M21, r.M22, r.M23), new Vector3(r.M31, r.M32, r.M33) };
            var ic = new double[3, 3];
            for (var i = 0; i < 3; i++)
            {
                for (var j = 0; j < 3; j++)
                {
                    ic[i, j] = (localInertia.X * Get(col[0], i) * Get(col[0], j)) + (localInertia.Y * Get(col[1], i) * Get(col[1], j)) + (localInertia.Z * Get(col[2], i) * Get(col[2], j));
                }
            }

            var c = state.Position - origin;
            double[,] cx = { { 0, -c.Z, c.Y }, { c.Z, 0, -c.X }, { -c.Y, c.X, 0 } };
            var m = new double[6, 6];
            for (var i = 0; i < 3; i++)
            {
                for (var j = 0; j < 3; j++)
                {
                    double cxcx = 0;
                    for (var t = 0; t < 3; t++)
                    {
                        cxcx += cx[i, t] * cx[t, j];
                    }

                    m[i, j] = ic[i, j] - (mass * cxcx);
                    m[i, j + 3] = mass * cx[i, j];
                    m[i + 3, j] = -mass * cx[i, j];
                    m[i + 3, j + 3] = i == j ? mass : 0;
                }
            }

            return new Spatial(m);

            static double Get(Vector3 v, int i) => i == 0 ? v.X : i == 1 ? v.Y : v.Z;
        }

        public Spatial Add(Spatial other)
        {
            var m = new double[6, 6];
            for (var i = 0; i < 6; i++)
            {
                for (var j = 0; j < 6; j++)
                {
                    m[i, j] = M[i, j] + other.M[i, j];
                }
            }

            return new Spatial(m);
        }

        /// <summary>M x = b を解く（6×6、部分ピボットの消去法）。</summary>
        public double[] Solve(double[] b)
        {
            var inv = Invert(M);
            var x = new double[6];
            for (var i = 0; i < 6; i++)
            {
                double acc = 0;
                for (var j = 0; j < 6; j++)
                {
                    acc += inv[i, j] * b[j];
                }

                x[i] = acc;
            }

            return x;
        }
    }
}
