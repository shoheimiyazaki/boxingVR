using System.Numerics;

namespace MiniRing.Motion;

/// <summary>
/// 技の時計の進め方（判断の側）。体は目標をバネで追うので、速い振りは目標から遅れる（握りが数 cm、剣の向きが数十度）。遅れた体の剣は、
/// 目標の経路から外れて自分の胴・脚を通る（自傷の主因: <c>miniring-arena selfcuts</c>）。目標を先へ進め続けず、体が付いてくるのを待つ
/// （速さで人が決まるのではなく、体が追えた分だけ振りが進む）。経路そのものは変えない。
/// </summary>
/// <param name="GripSlack">握りの位置のずれがこれ [腕の長さ] で、時計が 1/2 の速さになる。</param>
/// <param name="AngleSlack">剣の向きのずれがこれ [rad] で、時計が 1/2 の速さになる。</param>
/// <param name="Floor">時計の進む割合の下限（待ちすぎて振りが止まらないように）。</param>
public sealed record SwingPacing(float GripSlack = 0.25f, float AngleSlack = 0.6f, float Floor = 0.15f)
{
    /// <summary>握りのずれ <paramref name="gripError"/> [腕の長さ] と剣の向きのずれ <paramref name="angleError"/> [rad] から、時計の進む割合（0..1）。</summary>
    public float Gate(float gripError, float angleError)
    {
        var e = MathF.Max(gripError / GripSlack, angleError / AngleSlack);
        return MathF.Max(Floor, 1f / (1f + (e * e)));
    }
}

/// <summary>
/// 自分の体を避ける刃の通り道（判断の側）。剣士のボットが出す目標（両手の位置と剣の向き）は、録った動きや型の手作りのキーから来るので、
/// 刃の線が自分の頭・胴・脚を通る振りが混じる（人の体と人形の体の比が違う・狙いで通り道をずらす・体のひねりを数えていない）。
/// 体は目標をバネで追うので、目標が体を通れば刃は自分を斬る。物理で「自分には当たらない」とはせず（拳と同じく同じ体・同じ接触を通る）、
/// 目標の側で、刃の線が自分の体から <see cref="Margin"/> 以上離れるよう、剣の向きを握りの回りで外へ傾ける。
/// <para>
/// 体は、肩の中心を原点にした体フレーム（+Z 前・+X 左・+Y 上、腕の長さが 1）の箱 4 つ（頭・胸から腰・両脚）で見積もる（直立の人形の部位の
/// ボクセルを測った広がりに、余裕を足した値）。腕は握りの側にあるので数えない。
/// </para>
/// </summary>
/// <param name="Margin">刃の線（厚みの半分を含む）を体の箱からこれだけ離す [腕の長さ]。体は目標から遅れて追うので、その遅れの分も含む。</param>
/// <param name="FromGrip">握りからこの距離（腕の長さ）より手前の刃は見ない。握りは体の前の腕の先にあり、手元は構えでいつも体に近い。</param>
/// <param name="MaxTilt">剣の向きを傾けてよい上限 [rad]。これで離せない振りは、通り道が体の中にあるので、そのままにする（別の技に任せる）。</param>
public sealed record SelfClearance(float Margin = 0.1f, float FromGrip = 0.45f, float MaxTilt = 0.6f)
{
    /// <summary>剣士のボットが使う値（既定の <see cref="Margin"/>・<see cref="FromGrip"/>・<see cref="MaxTilt"/>）。</summary>
    public static SelfClearance Adopted { get; } = new();

    /// <summary>体の箱（体フレーム、腕の長さを 1 とした値）: 下限と上限。測り方は <see cref="Margin"/> の前の段落。</summary>
    private static readonly (Vector3 Lo, Vector3 Hi, string Name)[] Boxes =
    [
        (new Vector3(-0.17f, 0.10f, -0.17f), new Vector3(0.17f, 0.50f, 0.17f), "頭"),
        (new Vector3(-0.30f, -0.80f, -0.20f), new Vector3(0.30f, 0.17f, 0.22f), "胴"),
        (new Vector3(-0.42f, -2.15f, -0.52f), new Vector3(0.38f, -0.75f, 0.62f), "脚"),
    ];

    /// <summary>刃の線の上の見る点の数（握りから先まで）。</summary>
    private const int Samples = 24;

    /// <summary>傾けて探す回数。</summary>
    private const int Iterations = 8;

    /// <summary>
    /// 点 <paramref name="p"/> の、体の箱までの符号つき距離（箱の外なら正、中なら負）と、箱から外へ向かう向き。
    /// 全ての箱のうち最も近いものを返す。<paramref name="lean"/> は頭の構えからのずれ（前傾・沈み、腕の長さ）: 頭の箱はそれだけ、胴の箱はその半分だけ、ずれた先まで広げる（体はずれる途中のどこにもいるので）。
    /// </summary>
    public static (float Distance, Vector3 Outward, string Box) Nearest(Vector3 p, Vector3 lean = default)
    {
        var best = (Distance: float.MaxValue, Outward: Vector3.UnitX, Box: "");
        foreach (var (lo0, hi0, name) in Boxes)
        {
            var share = name == "頭" ? 1f : name == "胴" ? 0.5f : 0f;
            var lo = Vector3.Min(lo0, lo0 + (lean * share));
            var hi = Vector3.Max(hi0, hi0 + (lean * share));
            var centre = (lo + hi) * 0.5f;
            var half = (hi - lo) * 0.5f;
            var q = p - centre;
            var d = Vector3.Abs(q) - half;
            var outside = Vector3.Max(d, Vector3.Zero);
            float distance;
            Vector3 outward;
            if (outside.LengthSquared() > 0f)
            {
                distance = outside.Length();
                outward = Vector3.Normalize(new Vector3(MathF.Sign(q.X) * outside.X, MathF.Sign(q.Y) * outside.Y, MathF.Sign(q.Z) * outside.Z));
            }
            else
            {
                // 箱の中: いちばん近い面から外へ。
                distance = MathF.Max(d.X, MathF.Max(d.Y, d.Z));
                outward = d.X >= d.Y && d.X >= d.Z ? new Vector3(q.X >= 0f ? 1f : -1f, 0f, 0f)
                    : d.Y >= d.Z ? new Vector3(0f, q.Y >= 0f ? 1f : -1f, 0f)
                    : new Vector3(0f, 0f, q.Z >= 0f ? 1f : -1f);
            }

            if (distance < best.Distance)
            {
                best = (distance, outward, name);
            }
        }

        return best;
    }

    /// <summary>
    /// 刃（握り <paramref name="grip"/> から <paramref name="blade"/> の向きへ長さ <paramref name="length"/>）の線が体から離れている距離（負なら体の中）と、
    /// いちばん近い所の握りからの距離・体の側・体から外へ向かう向き。見るのは握りから <see cref="FromGrip"/> より先だけ。
    /// </summary>
    public (float Clearance, float At, string Box, Vector3 Outward) Along(Vector3 grip, Vector3 blade, float length, Vector3 lean = default)
    {
        var worst = (Clearance: float.MaxValue, At: 0f, Box: "", Outward: Vector3.UnitX);
        for (var i = 0; i < Samples; i++)
        {
            var s = FromGrip + ((length - FromGrip) * (i / (float)(Samples - 1)));
            if (s <= 0f)
            {
                continue;
            }

            var (d, outward, box) = Nearest(grip + (blade * s), lean);
            if (d < worst.Clearance)
            {
                worst = (d, s, box, outward);
            }
        }

        return worst;
    }

    /// <summary>
    /// 動きのフレーム <paramref name="frame"/> の剣を、握り（上の手）の回りで、体から <see cref="Margin"/> 以上離れるまで外へ傾けたフレーム
    /// （<see cref="MaxTilt"/> まで。離れていればそのまま）。手の位置・頭・ひねり・歩きは変えない。刃筋は新しい向きに垂直に取り直す。
    /// </summary>
    /// <param name="frame">動きのフレーム。</param>
    /// <param name="length">握り（右手）から切っ先までの長さ [腕の長さ]。</param>
    public MotionFrame Keep(MotionFrame frame, float length)
    {
        var blade = frame.Blade;
        for (var i = 0; i < Iterations; i++)
        {
            var (clearance, at, _, outward) = Along(frame.Right, blade, length, frame.Head);
            if (clearance >= Margin)
            {
                break;
            }

            // その点を外へ (Margin − 今の離れ) だけ動かすのに、握りの回りで傾ける量（点が遠いほど少なくてよい）。
            var across = outward - (Vector3.Dot(outward, blade) * blade);
            if (across.LengthSquared() < 1e-6f)
            {
                break; // 刃の向きと同じ向きにしか外へ出られない（刃は線の端の面に刺さっている）: 傾けても離れない
            }

            var next = Vector3.Normalize(blade + (Vector3.Normalize(across) * ((Margin - clearance) / at)));
            if (MathF.Acos(Math.Clamp(Vector3.Dot(next, frame.Blade), -1f, 1f)) > MaxTilt)
            {
                break;
            }

            blade = next;
        }

        if (blade == frame.Blade)
        {
            return frame;
        }

        return frame with { Blade = blade, Edge = SwordMotion.Perpendicular(frame.Edge, blade) };
    }
}
