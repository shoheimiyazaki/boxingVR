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
/// <item>掌幅と手長は ANSUR II の成人男性平均（88 mm、193 mm）が目安。幅 8 セルの掌を、前腕につながる手首へ絞る。</item>
/// <item>手首の関節（<see cref="WristJoint"/>、手根骨の真ん中）が回転の中心。腕の先が手を握る点（<see cref="Wrist"/>）はその 1 セル後ろ。手首の動く範囲は人の可動域（<see cref="Range"/>: 掌屈 80°・背屈 70°・橈屈 20°・尺屈 30°）で、手形はその範囲の関節角で作る。</item>
/// <item>材質は手の組織（<see cref="BoxerAnatomy.HandSkin"/>・<see cref="BoxerAnatomy.HandMuscle"/>・<see cref="BoxerAnatomy.HandBone"/>: 見た目は皮膚・肉・骨、物性は木）。</item>
/// </list>
/// 座標は半セル（<see cref="H"/>）の整数で書く。格子は原点を挟んで偶数個のセルなので、奇数がセルの中心、偶数がセルの境。
/// </summary>
internal static class HandSkeleton
{
    /// <summary>手のセルの一辺 [m]。</summary>
    public const float Cell = 0.01056f;

    /// <summary>半セル [m]。寸法はこの整数倍で書く。</summary>
    private const float H = 0.5f * Cell;

    /// <summary>格子の中心から見た前腕先端との接続点（<see cref="Weapon.GripOf"/>）。</summary>
    public static readonly Vector3 Wrist = new((PalmBack + 3) * H, (BoneRow + 1) * H, 0f);

    /// <summary>手首の関節（手根骨の真ん中。曲げ・偏りの回転の中心）。</summary>
    public static readonly Vector3 WristJoint = Wrist + (Vector3.UnitX * Cell);

    /// <summary>
    /// 指の付け根（MCP 関節）の位置: 掌の先の面、骨の列（<see cref="BoneRow"/>）と手の甲の側の皮膚の列の境、指ごとの横（2 セルおき）。人さし指・中指の中手骨は薬指・小指より 1 セル長く、
    /// 握ると人さし指・中指の拳頭が先行する。
    /// </summary>
    private static Vector3 Mcp(int finger) => finger == Thumb
        ? new Vector3(0f, (BoneRow - 7) * H, -(PalmSide + 3) * H)
        : new Vector3((finger < 2 ? 8f : 6f) * H, (BoneRow + 1) * H, (finger - 1.5f) * 4f * H);

    /// <summary>
    /// 親指の番号（<see cref="BareHandState.FingerCount"/> の最後）。付け根（手根中手関節）は掌の人さし指の側の縁の、掌の側（母指球）・手首寄り。
    /// 節は中手骨・基節・末節で、曲げは Y 軸まわり（伸ばすと前 +X、曲げると人さし指の側から小指の側 +Z へ、拳の前の面の下を横切る）。
    /// 付け根の座標は X・Z が偶数、Y が偶数（断面のずれが Y・Z とも奇数）で、曲げてもセルが中心に乗る（<see cref="BoneRow"/> と同じ理由）。
    /// 完成イメージ（2026-11-19、Codex の画像生成の参考絵: 親指が人さし指と中指の中節を横切る）に合わせた。
    /// </summary>
    public const int Thumb = 4;

    /// <summary>
    /// 骨の列の高さ（半セル、掌の厚みの真ん中のセル）。指の付け根の関節の軸はその半セル甲の側（セルの境）に置く: 指のセルの付け根からのずれが
    /// 長さの向きも厚みの向きも奇数（半セル）になり、指を 0° でも 90° でも曲げたとき、セルが格子のセルの中心に乗る（軸がセルの中心だと、
    /// 90° 曲げた指のセルがみなセルの境に乗り、隣のセルを取り合って横へ押し出される: 拳の幅が 40 mm でなく 50 mm になっていた）。
    /// </summary>
    private const int BoneRow = 1;

    /// <summary>指の骨の列の横のずれ（半セル）: 2 セル幅の指のうち、手の中心の側のセル（外の側は皮膚で、拳の横の面に骨が出ない）。</summary>
    private static float BoneSide(int finger) => finger is < 2 or Thumb ? 1f : -1f;

    /// <summary>
    /// 指ごとの節の長さ [半セル]（基節・中節・末節。親指のみ中手骨・基節・末節）。貫手では中指の先が前に出る。
    /// </summary>
    private static readonly float[][] LengthsHalf = [[8f, 4f, 2f], [8f, 6f, 4f], [8f, 4f, 2f], [6f, 4f, 2f], [6f, 4f, 4f]];

    /// <summary>指 <paramref name="finger"/> の節 <paramref name="segment"/> の長さ [m]。</summary>
    public static float SegmentLength(int finger, int segment) => LengthsHalf[finger][segment] * H;

    /// <summary>中指の長さ [m]（付け根から先まで。関節角のずれを、指先の動いた距離へ直す目安）。</summary>
    public static float MiddleFingerLength { get; } = SegmentLength(1, 0) + SegmentLength(1, 1) + SegmentLength(1, 2);

    /// <summary>親セル 1 つ: 材質、どの指のどの節か、その節の座標系（掌は手首が原点、指の節は節の根元の関節が原点で +X が指の向き）での位置。</summary>
    internal sealed record Parent(byte Material, int Finger, FingerSegment Segment, Vector3 Local);

    /// <summary>親セルの表（まっすぐ伸ばした手）。並べる順（骨 → 肉 → 皮膚、掌 → 指）に並んでいる: 場所が重なったとき、先に置いたほうがその場所を取る。</summary>
    internal static IReadOnlyList<Parent> Parents { get; } = BuildParents();

    private static List<Parent> BuildParents()
    {
        var palm = new List<Parent>();
        for (var xi = PalmBack; xi <= PalmFront; xi += 2)
        {
            for (var yi = PalmLow; yi <= PalmHigh; yi += 2)
            {
                for (var zi = -PalmSide; zi <= PalmSide; zi += 2)
                {
                    if (!InPalm(xi, yi, zi))
                    {
                        continue;
                    }

                    var position = new Vector3(xi, yi, zi) * H;
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
                    for (var dy = -3; dy <= 1; dy += 2)
                    {
                        for (var dz = -1; dz <= (finger == Thumb ? 3 : 1); dz += 2)
                        {
                            // 断面は幅 2 × 高さ 3 セル。骨は真ん中の高さの、手の中心の側の 1 セル（<see cref="BoneSide"/>）。上下と外の側は皮膚。
                            // 親指は隣に覆ってくれる指が無いので、骨 1 セルを上下左右の皮膚が包む十字（5 セル、四隅は空ける）。
                            // 節の先の端（末節の先）は指先の皮膚の 1 層。
                            if (finger == Thumb && dy != -1 && dz != BoneSide(finger))
                            {
                                continue;
                            }

                            var bone = dy == -1 && dz == BoneSide(finger) && k < length;
                            fingers.Add(new Parent(bone ? BoxerAnatomy.HandBone : BoxerAnatomy.HandSkin, finger, (FingerSegment)segment,
                                new Vector3(((2 * k) + 1) * H, dy * H, dz * H)));
                        }
                    }

                    // 中指の末節は隣の指より先に出るので、骨の薬指側も皮膚で覆う。
                    if (finger == 1 && segment == (int)FingerSegment.Distal)
                    {
                        fingers.Add(new Parent(BoxerAnatomy.HandSkin, finger, FingerSegment.Distal,
                            new Vector3(((2 * k) + 1) * H, -H, 3f * H)));
                    }
                }
            }
        }

        // 場所の取り合いの順: 骨 → 肉 → 皮膚（硬いものが先に場所を取り、柔らかい肉・皮膚が押しのけられる）。同じ材質では掌が先。
        return [.. palm.Concat(fingers).Select((p, i) => (p, i)).OrderBy(t => Rank(t.p.Material)).ThenBy(t => t.i).Select(t => t.p)];
    }

    private static int Rank(byte material) => material == BoxerAnatomy.HandBone ? 0 : material == BoxerAnatomy.HandMuscle ? 1 : 2;

    // 掌の範囲（半セル、セルの中心の奇数）。先端は指ごとの付け根まで。
    private const int PalmBack = -11;
    private const int PalmFront = 7;
    private const int PalmLow = -1;
    private const int PalmHigh = 3;
    private const int PalmSide = 7;
    private const int WristSide = 5;

    /// <summary>指 <paramref name="finger"/> の骨の列の横の位置（半セル）。</summary>
    private static int BoneZ(int finger) => (int)(Mcp(finger).Z / H) + (int)BoneSide(finger);

    /// <summary>
    /// 掌のセル（座標は半セル、中心が奇数）の材質: 中手骨（指の骨の列の延長、骨の高さ）と手根骨の列は骨、ブロックの外側の 1 層は皮膚、残りは肉。
    /// </summary>
    private static byte PalmMaterial(int x, int y, int z)
    {
        var boneHeight = y == BoneRow;
        var metacarpal = boneHeight && x >= -1 && Enumerable.Range(0, Thumb).Any(f => BoneZ(f) == z); // 親指の中手骨は親指の節の側にある
        var carpal = boneHeight && x == PalmBack + 6 && Math.Abs(z) < WristSide;
        if (metacarpal || carpal)
        {
            return BoxerAnatomy.HandBone;
        }

        var outside = !InPalm(x - 2, y, z) || !InPalm(x + 2, y, z) || !InPalm(x, y - 2, z) || !InPalm(x, y + 2, z) || !InPalm(x, y, z - 2) || !InPalm(x, y, z + 2);
        return outside ? BoxerAnatomy.HandSkin : BoxerAnatomy.HandMuscle;
    }

    /// <summary>前腕側は 4 セルから 6 セルへ広げ、掌の先は指ごとの付け根まで。</summary>
    private static bool InPalm(int x, int y, int z)
    {
        var side = x <= PalmBack + 2 ? WristSide - 2 : x <= PalmBack + 6 ? WristSide : PalmSide;
        return x >= PalmBack && y is >= PalmLow and <= PalmHigh && Math.Abs(z) <= side
            && x <= (Mcp(Math.Clamp((z + PalmSide + 1) / 4, 0, 3)).X / H) - 1f;
    }

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
                    v = f == Thumb ? Deviate(v, -pose.Joint(f, s)) : Flex(v, pose.Joint(f, s));
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
        // 親指（Y 軸まわり）: 拳では人さし指と中指の中節の前を横切る。開いた手では人さし指の横に添える。
        float[] thumbFist = [ThumbFistAngles[0], ThumbFistAngles[1], ThumbFistAngles[2]];
        float[] thumbTucked = [0f, 30f * Deg, 0f];
        var (wristFlex, wristDeviation, fingers) = shape switch
        {
            HandShape.Seiken => (0f, 0f, new[] { fist, fist, fist, fist, thumbFist }),
            HandShape.SpearHand => (0f, 0f, new[] { straight, straight, straight, straight, thumbTucked }),
            HandShape.KnifeHand => (0f, 20f * Deg, new[] { straight, straight, straight, straight, thumbTucked }),
            HandShape.OneKnuckle => (0f, 0f, new[] { index, fist, fist, fist, thumbFist }),
            HandShape.PalmHeel => (-70f * Deg, 0f, new[] { cocked, cocked, cocked, cocked, straight }),
            HandShape.BackFist => (80f * Deg, 0f, new[] { fist, fist, fist, fist, thumbFist }),
            _ => throw new ArgumentOutOfRangeException(nameof(shape), shape, "知らない手形です。"),
        };
        return new HandPose([wristFlex, wristDeviation, .. fingers.SelectMany(f => f)]);
    }

    /// <summary>
    /// 握る手（剣・棒・バットの柄を握る形）の関節角: 指の 3 関節とも 90°（基節が下へ、中節が後ろへ、末節が上へ回って柄を巻く）。
    /// 素手の拳と同じ骨格・同じ材質から、関節角と握る物（<see cref="BareHandState.Placed"/> の held、<see cref="HeldBar"/>）だけを変えて作る。
    /// 柄の入る所は先に取ってあるので、曲げで押しのけられた組織は柄の外へ回る（柄の中を埋めない）。
    /// </summary>
    public static HandPose GripPose { get; } = new([0f, 0f, .. Enumerable.Repeat<float[]>([90f * Deg, 90f * Deg, 90f * Deg], Thumb).SelectMany(f => f),
        40f * Deg, 40f * Deg, 20f * Deg]); // 親指は柄の反対から巻く

    /// <summary>拳の親指の関節角（中手骨・基節・末節）。</summary>
    private static readonly float[] ThumbFistAngles = [10f * Deg, 80f * Deg, 30f * Deg];

    /// <summary>
    /// 握る手の、柄の軸の通る点（格子のローカル。軸は Z、拳頭の並ぶ向き）: X は人さし指の付け根、Y はそこから基節の長さだけ掌の側
    /// （90° に曲げた基節の先、第 2 関節の高さ）。曲げた指と掌に囲まれる輪の真ん中。
    /// </summary>
    public static Vector3 GripCenter { get; } = new(Mcp(0).X, Mcp(0).Y - SegmentLength(0, 0), 0f);

    /// <summary>半径 <paramref name="radius"/> [m] の丸い柄（<see cref="GripCenter"/> を通って Z に沿い、Z の長さは限らない: 手の格子を貫く）の占める所。</summary>
    public static Func<Vector3, bool> HeldBar(float radius)
        => p => ((p.X - GripCenter.X) * (p.X - GripCenter.X)) + ((p.Y - GripCenter.Y) * (p.Y - GripCenter.Y)) <= radius * radius;

    /// <summary>力を抜いた手の指（自然に少し曲がる。付け根 20°・第 2 関節 29°・先 14°）。折れた指・使えない指はこの角度へ力なく落ちる。</summary>
    public static readonly float[] RestFlexion = [20f * Deg, 29f * Deg, 14f * Deg];

    // ── 関節の動き（バネ・ダンパ）: 時間は人の測定から、強さはその時間と慣性から ──

    /// <summary>
    /// 指が目標の曲げ角に落ち着くまでの時間 [s]（臨界減衰の 95 %）。人が拳を握るのに 0.1 s ほど（人形の単位では、重力下の振り子の時間が
    /// 長さの平方根に比例するので 0.1 × 0.53 ≈ 0.055 s にしていた。その換算 0.055 × 1.6784）。
    /// </summary>
    public const float FingerSettleSeconds = 0.0923f;

    /// <summary>手首が目標の向きに落ち着くまでの時間 [s]。手全体を回すので指より少しゆっくり（人の手首は指の 1.5 倍ほど）。</summary>
    public const float WristSettleSeconds = 0.1343f;

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
                // 指は Z 軸まわり（X・Y の距離）、親指は Y 軸まわり（X・Z の距離）に曲がる。
                var dx = flat[i].X - pivotX;
                var dy = p.Finger == Thumb ? flat[i].Z - Mcp(p.Finger).Z : flat[i].Y - Mcp(p.Finger).Y;
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
                .MinBy(i => Vector3.DistanceSquared(positions[i], new Vector3(front, 0f, Mcp(1).Z)));
            return positions[index];
        }
    }
}
