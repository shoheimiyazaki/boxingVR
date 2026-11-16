using System.Numerics;
using MiniRing.Voxels;

namespace MiniRing.Weapons;

/// <summary>指の節。<see cref="Palm"/> は掌（手根骨・中手骨とその肉）。指の節は付け根から先へ基節・中節・末節。</summary>
public enum FingerSegment { Proximal, Middle, Distal, Palm }

/// <summary>
/// 手の関節の角度（rad）。手首は屈曲（掌のほうへ曲げる = 正。伸ばす = 負）と、手の縁（小指側）を前へ向ける偏り、
/// 指は 4 本（人さし指・中指・薬指・小指）× 3 関節（付け根 MCP・第 2 関節 PIP・先の関節 DIP）の屈曲（掌のほうへ曲げる = 正）。
/// 手形（<see cref="HandShape"/>）はこの角度の組で決まる（<see cref="HandSkeleton.PoseOf"/>）。
/// </summary>
public sealed class HandPose
{
    /// <summary>関節の数: 手首の屈曲・偏り + 指 4 本 × 3 関節。</summary>
    public const int JointCount = 2 + (BareHandState.FingerCount * 3);

    private readonly float[] _angles;

    public HandPose(IEnumerable<float> angles)
    {
        _angles = [.. angles];
        if (_angles.Length != JointCount || _angles.Any(a => !float.IsFinite(a)))
        {
            throw new ArgumentException($"手の姿勢は {JointCount} 個の有限な角度が要ります。", nameof(angles));
        }
    }

    public float WristFlexion => _angles[0];

    public float WristDeviation => _angles[1];

    /// <summary>指 <paramref name="finger"/> の関節 <paramref name="joint"/>（0 = 付け根、1 = 第 2 関節、2 = 先の関節）の屈曲。</summary>
    public float Joint(int finger, int joint) => _angles[2 + (finger * 3) + joint];

    /// <summary>関節 <paramref name="index"/>（0 = 手首の屈曲、1 = 手首の偏り、2 以降は指 × 3）の角度。</summary>
    public float this[int index] => _angles[index];

    /// <summary>2 つの姿勢の、関節ごとの角度の差の最大（rad）。</summary>
    public float MaxDifference(HandPose other)
    {
        var worst = 0f;
        for (var i = 0; i < JointCount; i++)
        {
            worst = MathF.Max(worst, MathF.Abs(_angles[i] - other._angles[i]));
        }

        return worst;
    }

    internal float[] ToArray() => [.. _angles];
}

/// <summary>
/// 素手の骨格: 手を「掌と、4 本 × 3 節の指」の骨と、その周りの肉・皮膚のセルの並びとして持つ（親セルの表）。手形は各関節の曲げ角で決まり
/// （<see cref="PoseOf"/>）、親セルは節の向きに沿って並べ直される（<see cref="Positions"/>）。剛体は増やさない（手は 1 つの剛体の中の格子）。
/// <list type="bullet">
/// <item>寸法（人形の縮尺。2 mm のセル）: 掌は 16 × 10 × 24 mm。指の太さは 6 mm（骨 1 セルを皮膚が十字に包む）で、基節 4〜6 mm・中節 4 mm・末節 2〜4 mm（中指 6・4・4）
/// （人の指の骨の長さの比 3 : 2 : 2 ほど。人の手の 1/3.5 ほどの縮尺）。指の付け根は 6 mm おき。</item>
/// <item>手首の関節（<see cref="WristJoint"/>、手根骨の真ん中）が回転の中心。腕の先が手を握る点（<see cref="Wrist"/>）はその 4 mm 後ろ。手首の動く範囲は人の可動域（<see cref="Range"/>: 掌屈 80°・背屈 70°・橈屈 20°・尺屈 30°）で、手形はその範囲の関節角で作る。</item>
/// </list>
/// </summary>
internal static class HandSkeleton
{
    /// <summary>セルの一辺 [m]。</summary>
    public const float Cell = 0.002f;

    private const float Mm = 0.001f;

    /// <summary>格子の中心（掌の真ん中）から見た、腕の先が手を握る点（前腕の端。<see cref="Weapon.GripOf"/>）。</summary>
    public static readonly Vector3 Wrist = new(-8f * Mm, 2f * Mm, 0f);

    /// <summary>手首の関節（手根骨の真ん中。曲げ・偏りの回転の中心）。</summary>
    public static readonly Vector3 WristJoint = new(-4f * Mm, 2f * Mm, 0f);

    /// <summary>
    /// 指の付け根（MCP 関節）の位置: 掌の先の面、骨の高さ（y = 3 mm）、指ごとの横（6 mm おき）。人さし指・中指の中手骨は薬指・小指より 2 mm 長く、
    /// 握ると人さし指・中指の拳頭が先行する。
    /// </summary>
    private static Vector3 Mcp(int finger) => new((finger < 2 ? 8f : 6f) * Mm, 3f * Mm, ((finger - 1.5f) * 6f) * Mm);

    /// <summary>
    /// 指ごとの節の長さ [mm]（基節・中節・末節）。中指がいちばん長く（14）、人さし指・薬指は 12、小指は 10（人の指の長さの比 1 : 0.9 : 0.93 : 0.7 ほどを 2 mm のセルに丸めた）。
    /// 貫手では中指の先が前に出て、先端が細くなる。指の断面は十字（骨 + 上下左右の皮膚の 5 セル。四隅は丸めて空ける）。
    /// </summary>
    private static readonly float[][] LengthsMm = [[6f, 4f, 2f], [6f, 4f, 4f], [6f, 4f, 2f], [4f, 4f, 2f]];

    /// <summary>指 <paramref name="finger"/> の節 <paramref name="segment"/> の長さ [m]。</summary>
    public static float SegmentLength(int finger, int segment) => LengthsMm[finger][segment] * Mm;

    /// <summary>親セル 1 つ: 材質、どの指のどの節か、その節の座標系（掌は手首が原点、指の節は節の根元の関節が原点で +X が指の向き）での位置。</summary>
    internal sealed record Parent(byte Material, int Finger, FingerSegment Segment, Vector3 Local);

    /// <summary>親セルの表（まっすぐ伸ばした手）。並べる順（骨 → 肉 → 皮膚、掌 → 指）に並んでいる: 場所が重なったとき、先に置いたほうがその場所を取る。</summary>
    internal static IReadOnlyList<Parent> Parents { get; } = BuildParents();

    private static List<Parent> BuildParents()
    {
        var palm = new List<Parent>();
        for (var xi = -7; xi <= 7; xi += 2)
        {
            for (var yi = -1; yi <= 5; yi += 2)
            {
                for (var zi = -11; zi <= 11; zi += 2)
                {
                    if (!InPalm(xi, yi, zi))
                    {
                        continue;
                    }

                    var position = new Vector3(xi, yi, zi) * Mm;
                    palm.Add(new Parent(PalmMaterial(xi, yi, zi), BareHandState.FingerCount, FingerSegment.Palm, position - Wrist));
                }
            }
        }

        var fingers = new List<Parent>();
        for (var finger = 0; finger < BareHandState.FingerCount; finger++)
        {
            for (var segment = 0; segment < 3; segment++)
            {
                var length = (int)MathF.Round(SegmentLength(finger, segment) / Cell);
                for (var k = 0; k < length + (segment == 2 ? 1 : 0); k++)
                {
                    for (var dy = -2; dy <= 2; dy += 2)
                    {
                        for (var dz = -2; dz <= 2; dz += 2)
                        {
                            // 断面は十字（骨 1 セルの上下左右を皮膚が包む。四隅は空く: 指は丸く、隣の指との間に溝ができ、拳頭・指先は小さな面になる）。
                            if (dy != 0 && dz != 0)
                            {
                                continue;
                            }

                            // 骨は節の真ん中の 1 セル。節の先の端（末節の先）は指先の皮膚の 1 層。
                            var bone = dy == 0 && dz == 0 && k < length;
                            fingers.Add(new Parent(bone ? BoxerAnatomy.Bone : BoxerAnatomy.Skin, finger, (FingerSegment)segment,
                                new Vector3(((2 * k) + 1) * Mm, dy * Mm, dz * Mm)));
                        }
                    }
                }
            }
        }

        // 場所の取り合いの順: 骨 → 肉 → 皮膚（硬いものが先に場所を取り、柔らかい肉・皮膚が押しのけられる）。同じ材質では掌が先。
        return [.. palm.Concat(fingers).Select((p, i) => (p, i)).OrderBy(t => Rank(t.p.Material)).ThenBy(t => t.i).Select(t => t.p)];
    }

    private static int Rank(byte material) => material == BoxerAnatomy.Bone ? 0 : material == BoxerAnatomy.Muscle ? 1 : 2;

    /// <summary>
    /// 掌のセル（座標は mm、中心が奇数）の材質: 中手骨（指の延長の線上、骨の高さ）と手根骨の塊は骨、ブロックの外側の 1 層は皮膚、残りは筋肉（掌の厚み）。
    /// </summary>
    private static byte PalmMaterial(int x, int y, int z)
    {
        var metacarpal = y == 3 && x >= -1 && z is -9 or -3 or 3 or 9;
        var carpal = x is -5 or -3 && y is 1 or 3 && Math.Abs(z) <= 7;
        if (metacarpal || carpal)
        {
            return BoxerAnatomy.Bone;
        }

        var outside = !InPalm(x - 2, y, z) || !InPalm(x + 2, y, z) || !InPalm(x, y - 2, z) || !InPalm(x, y + 2, z) || !InPalm(x, y, z - 2) || !InPalm(x, y, z + 2);
        return outside ? BoxerAnatomy.Skin : BoxerAnatomy.Muscle;
    }

    /// <summary>掌のブロック（x = −7 … 、y = −1 … 5、z = −11 … 11 mm）の中か。先の面は指ごとの付け根の位置まで（人さし指・中指の列が 2 mm 長い）。</summary>
    private static bool InPalm(int x, int y, int z)
        => x >= -7 && y is >= -1 and <= 5 && Math.Abs(z) <= 11 && x <= (Mcp(Math.Clamp((z + 12) / 6, 0, 3)).X / Mm) - 1f;

    /// <summary>屈曲 <paramref name="angle"/>（掌のほうへ = −Y へ曲げる）の回転。</summary>
    private static Vector3 Flex(Vector3 v, float angle)
    {
        var (s, c) = MathF.SinCos(angle);
        return new Vector3((v.X * c) + (v.Y * s), (-v.X * s) + (v.Y * c), v.Z);
    }

    /// <summary>手の縁（+Z 側 = 小指側）を前（+X）へ向ける回転（Y 軸まわり）。</summary>
    private static Vector3 Deviate(Vector3 v, float angle)
    {
        var (s, c) = MathF.SinCos(angle);
        return new Vector3((v.X * c) + (v.Z * s), v.Y, (-v.X * s) + (v.Z * c));
    }

    /// <summary>
    /// 姿勢 <paramref name="pose"/> のときの、全部の親セルの中心（格子のローカル [m]）: 節ごとに、根元の関節から先へ関節の角度を順にたどり
    /// （付け根の屈曲 → 基節の先で第 2 関節の屈曲 → …）、最後に手首の回転（手首を中心に）を掛ける。
    /// </summary>
    public static Vector3[] Positions(HandPose pose)
    {
        ArgumentNullException.ThrowIfNull(pose);
        var result = new Vector3[Parents.Count];
        for (var i = 0; i < result.Length; i++)
        {
            var p = Parents[i];
            Vector3 inPalm;
            if (p.Segment == FingerSegment.Palm)
            {
                inPalm = Wrist + p.Local;
            }
            else
            {
                var f = p.Finger;
                var v = p.Local;
                for (var s = (int)p.Segment; s >= 0; s--)
                {
                    v = Flex(v, pose.Joint(f, s));
                    if (s > 0)
                    {
                        v += new Vector3(SegmentLength(f, s - 1), 0f, 0f);
                    }
                }

                inPalm = Mcp(f) + v;
            }

            result[i] = WristJoint + Deviate(Flex(inPalm - WristJoint, pose.WristFlexion), pose.WristDeviation);
        }

        return result;
    }

    // ── 手形 = 関節の曲げ角 ──

    private const float Deg = MathF.PI / 180f;

    /// <summary>
    /// 関節の動く範囲 [rad]（人の可動域）。0 = 手首の屈曲（正 = 掌屈 80°、負 = 背屈 70°）、1 = 手首の偏り（正 = 橈屈 20°、負 = 尺屈 30°）、
    /// 指の付け根は −45°（反らす）〜 90°、第 2 関節は 0〜110°、先の関節は 0〜80°。手形の関節角も、動いて追う目標も、この範囲に収める。
    /// </summary>
    public static (float Min, float Max) Range(int joint) => joint switch
    {
        0 => (-70f * Deg, 80f * Deg),
        1 => (-30f * Deg, 20f * Deg),
        _ => ((joint - 2) % 3) switch
        {
            0 => (-45f * Deg, 90f * Deg),
            1 => (0f, 110f * Deg),
            _ => (0f, 80f * Deg),
        },
    };

    /// <summary>
    /// 手形の関節角（すべて <see cref="Range"/> の中）。
    /// <list type="bullet">
    /// <item>正拳: 指を全部握る（付け根 90°・第 2 関節 100°・先 60°）。付け根の拳頭が前に出る。</item>
    /// <item>貫手: 指を全部伸ばしてそろえる（先端が前）。</item>
    /// <item>手刀: 指を伸ばしてそろえ、手首を橈屈 20° まで（小指側の縁が少し前へ）。前腕の回内・回外（縁を前へ向ける本来の動き）は手の関節ではなく手の向きの指令で、ここでは持たないので、まっすぐ突くと貫手とほぼ同じ。</item>
    /// <item>一本拳: 人さし指だけ付け根を浅く（20°）・第 2 関節を深く（110°）曲げ、第 2 関節を前へ突き出す。残りは握る。</item>
    /// <item>掌底: 手首を背屈 70° まで反らし、指は付け根で 20° 反らす（指は手首と合わせて前腕に対し 90° = 真上を向き、掌の面と根元が前に出る）。</item>
    /// <item>裏拳: 握ったまま手首を掌屈 80° まで曲げ、手の甲を前へ向ける。</item>
    /// </list>
    /// 形ごとの強さや威力の倍率ではなく、セルの置き場だけを決める。
    /// </summary>
    public static HandPose PoseOf(HandShape shape)
    {
        float[] fist = [90f * Deg, 100f * Deg, 60f * Deg];
        float[] straight = [0f, 0f, 0f];
        float[] index = [20f * Deg, 110f * Deg, 60f * Deg];
        float[] cocked = [-20f * Deg, 0f, 0f];
        var (wristFlex, wristDeviation, fingers) = shape switch
        {
            HandShape.Seiken => (0f, 0f, new[] { fist, fist, fist, fist }),
            HandShape.SpearHand => (0f, 0f, new[] { straight, straight, straight, straight }),
            HandShape.KnifeHand => (0f, 20f * Deg, new[] { straight, straight, straight, straight }),
            HandShape.OneKnuckle => (0f, 0f, new[] { index, fist, fist, fist }),
            HandShape.PalmHeel => (-70f * Deg, 0f, new[] { cocked, cocked, cocked, cocked }),
            HandShape.BackFist => (80f * Deg, 0f, new[] { fist, fist, fist, fist }),
            _ => throw new ArgumentOutOfRangeException(nameof(shape), shape, "知らない手形です。"),
        };
        return new HandPose([wristFlex, wristDeviation, .. fingers.SelectMany(f => f)]);
    }

    /// <summary>力を抜いた手の指（自然に少し曲がる。付け根 20°・第 2 関節 29°・先 14°）。折れた指・使えない指はこの角度へ力なく落ちる。</summary>
    public static readonly float[] RestFlexion = [20f * Deg, 29f * Deg, 14f * Deg];

    // ── 関節の動き（バネ・ダンパ）: 時間は人の測定から、強さはその時間と慣性から ──

    /// <summary>
    /// 指が目標の曲げ角に落ち着くまでの時間 [s]（臨界減衰の 95 %）。人が拳を握るのに 0.1 s ほど、人形の縮尺（長さ 1/3.5。
    /// 重力下の振り子の時間は長さの平方根に比例）で 0.1 × 0.53 ≈ 0.055 s。
    /// </summary>
    public const float FingerSettleSeconds = 0.055f;

    /// <summary>手首が目標の向きに落ち着くまでの時間 [s]。手全体を回すので指より少しゆっくり（人の手首は指の 1.5 倍ほど）。</summary>
    public const float WristSettleSeconds = 0.08f;

    /// <summary>力を抜いた手へ戻そうとする受け身の力（腱・関節包のばね）の、完全に力を入れたときの強さに対する割合。力が入らない関節（折れた骨の節）はこれだけで保たれる。</summary>
    public const float PassiveShare = 0.02f;

    /// <summary>臨界減衰の 95 % の時間 T と、バネの固有角振動数 ω の関係: T = 4.74 / ω。</summary>
    private const float SettleFactor = 4.74f;

    /// <summary>
    /// 関節の慣性モーメント [kg m²]（まっすぐな手での、その関節より先の親セルの質量 × 関節からの距離²。材質の密度 × 体積）。
    /// 0 = 手首の屈曲、1 = 手首の偏り、2 以降は指 × 3。
    /// </summary>
    public static readonly float[] Inertia = BuildInertia();

    private static float[] BuildInertia()
    {
        var inertia = new float[HandPose.JointCount];
        var flat = Positions(new HandPose(new float[HandPose.JointCount]));
        for (var i = 0; i < flat.Length; i++)
        {
            var p = Parents[i];
            var mass = BoxerAnatomy.Palette[p.Material].MechanicsOrDefault.Density * Cell * Cell * Cell;
            var fromWrist = flat[i] - WristJoint;
            inertia[0] += mass * ((fromWrist.X * fromWrist.X) + (fromWrist.Y * fromWrist.Y));
            inertia[1] += mass * ((fromWrist.X * fromWrist.X) + (fromWrist.Z * fromWrist.Z));
            if (p.Segment == FingerSegment.Palm)
            {
                continue;
            }

            var pivotX = Mcp(p.Finger).X;
            for (var joint = 0; joint <= (int)p.Segment; joint++)
            {
                var dx = flat[i].X - pivotX;
                var dy = flat[i].Y - Mcp(p.Finger).Y;
                inertia[2 + (p.Finger * 3) + joint] += mass * ((dx * dx) + (dy * dy));
                pivotX += SegmentLength(p.Finger, joint);
            }
        }

        return inertia;
    }

    /// <summary>
    /// 関節 <paramref name="joint"/> の、完全に力を入れたときのバネの固さ [N m/rad]: 慣性 × (4.74 / 落ち着くまでの時間)²。
    /// 「指の強さ（曲げに逆らう力）」はこの値で、目標の角度から押し戻される力のトルク = 固さ × 角度のずれ（× 骨の残りの強さ）になる。
    /// </summary>
    public static float Stiffness(int joint)
    {
        var omega = SettleFactor / (joint < 2 ? WristSettleSeconds : FingerSettleSeconds);
        return Inertia[joint] * omega * omega;
    }

    /// <summary>正拳の拳頭の前の面の、打つ側の先（格子のローカル）: 中指の付け根の前の皮膚。ボットが拳の届く先として使う点。</summary>
    public static Vector3 SeikenTip { get; } = ComputeSeikenTip();

    private static Vector3 ComputeSeikenTip()
    {
        {
            var positions = Positions(PoseOf(HandShape.Seiken));
            var front = positions.Max(p => p.X);
            var index = Enumerable.Range(0, positions.Length)
                .Where(i => positions[i].X >= front - (0.5f * Cell))
                .MinBy(i => Vector3.DistanceSquared(positions[i], new Vector3(front, 0f, -3f * Mm)));
            return positions[index];
        }
    }
}
