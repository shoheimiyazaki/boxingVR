using System.Numerics;

namespace MiniRing.LowPoly;

/// <summary>
/// ボクサーの部位ごとのローポリ形状。座標は部位の箱の半径（half extents）で割った正規化値（各軸およそ −1..1）。
/// 部位ローカルは +Z が体の前、+Y が上、+X が本人の左（<see cref="HitZones"/> と同じ）。
/// </summary>
public static class BoxerShapes
{
    private const int LimbSides = 6;

    /// <summary>頭の輪の点数（正面・左右 35°/80°/130°・真後ろ）。</summary>
    private const int HeadRingPoints = 8;

    /// <summary>頭の輪のうち目（眼窩）の高さの輪。<see cref="Head"/> の輪の並び順。</summary>
    private const int EyeRing = 3;

    /// <summary>眼窩のノード（左目・右目）。細分化しても元の頂点のインデックスは変わらない（<see cref="LowPolyMesh.Subdivide"/>）。</summary>
    public static (int Left, int Right) EyeSockets { get; } = (1 + (EyeRing * HeadRingPoints) + 1, 1 + (EyeRing * HeadRingPoints) + 7);

    /// <summary>
    /// 頭: 頭頂・額・眉（眉弓）・目（鼻筋と窪んだ眼窩・こめかみ）・鼻（鼻先と頬骨・耳）・口・顎（顎先とエラ）・顎の下。
    /// 輪 7 本 × 8 点 + 極 2 つ = 58 ノード、112 枚。
    /// </summary>
    public static LowPolyMesh Head() => Lathe.Build(
        new Vector3(0f, 1f, -0.05f),
        [
            Lathe.MirroredRing(0.80f, (0f, 0.55f, 0f), (35f, 0.58f, 0f), (80f, 0.62f, 0f), (130f, 0.62f, 0f), (180f, 0.62f, 0f)), // 頭頂
            Lathe.MirroredRing(0.48f, (0f, 0.86f, 0f), (35f, 0.86f, 0f), (80f, 0.90f, 0f), (130f, 0.90f, 0f), (180f, 0.90f, 0f)), // 額
            Lathe.MirroredRing(0.20f, (0f, 0.90f, 0f), (35f, 0.98f, 0.02f), (80f, 0.90f, 0f), (130f, 0.90f, 0f), (180f, 0.92f, 0f)), // 眉弓
            Lathe.MirroredRing(0.02f, (0f, 0.96f, 0f), (35f, 0.78f, 0f), (80f, 0.92f, 0f), (130f, 0.88f, 0f), (180f, 0.88f, 0f)), // 鼻筋・眼窩
            Lathe.MirroredRing(-0.22f, (0f, 1.12f, -0.04f), (35f, 0.95f, 0.02f), (80f, 1.0f, 0f), (130f, 0.86f, 0f), (180f, 0.85f, 0f)), // 鼻先・頬骨・耳
            Lathe.MirroredRing(-0.46f, (0f, 0.90f, 0f), (35f, 0.84f, 0f), (80f, 0.82f, 0f), (130f, 0.74f, 0f), (180f, 0.76f, 0f)), // 口
            Lathe.MirroredRing(-0.72f, (0f, 0.88f, -0.06f), (35f, 0.72f, -0.02f), (80f, 0.64f, 0.04f), (130f, 0.50f, 0f), (180f, 0.50f, 0f)), // 顎先・エラ
        ],
        new Vector3(0f, -0.86f, -0.1f));

    /// <summary>
    /// 胸: 首の付け根・僧帽筋・鎖骨と肩・胸筋・みぞおち・腹筋・腰のくびれ。
    /// 輪 6 本 × 8 点 + 極 2 つ = 50 ノード、96 枚。
    /// </summary>
    public static LowPolyMesh Chest() => Lathe.Build(
        new Vector3(0f, 1f, 0f),
        [
            Lathe.MirroredRing(0.90f, (0f, 0.38f, 0f), (35f, 0.42f, 0f), (80f, 0.55f, -0.02f), (130f, 0.48f, 0f), (180f, 0.42f, 0f)), // 首の付け根・僧帽筋
            Lathe.MirroredRing(0.72f, (0f, 0.82f, -0.04f), (35f, 0.92f, 0f), (80f, 1.0f, 0f), (130f, 0.94f, 0f), (180f, 0.86f, 0f)), // 鎖骨・肩
            Lathe.MirroredRing(0.34f, (0f, 0.92f, 0f), (35f, 1.06f, 0f), (80f, 0.96f, 0f), (130f, 0.92f, 0f), (180f, 0.92f, 0f)), // 胸筋・広背筋
            Lathe.MirroredRing(0.02f, (0f, 0.86f, 0.04f), (35f, 0.98f, -0.04f), (80f, 0.90f, 0f), (130f, 0.88f, 0f), (180f, 0.90f, 0f)), // 胸筋の下端・みぞおち
            Lathe.MirroredRing(-0.38f, (0f, 0.94f, 0f), (35f, 0.92f, 0f), (80f, 0.84f, 0f), (130f, 0.84f, 0f), (180f, 0.88f, 0f)), // 腹筋・肋
            Lathe.MirroredRing(-0.78f, (0f, 0.84f, 0f), (35f, 0.82f, 0f), (80f, 0.76f, 0f), (130f, 0.78f, 0f), (180f, 0.82f, 0f)), // 腰のくびれ
        ],
        new Vector3(0f, -1f, 0f));

    /// <summary>骨盤（トランクス）: 腰・腰骨・尻。</summary>
    public static LowPolyMesh Pelvis() => Lathe.Build(
        new Vector3(0f, 1f, 0f),
        [
            Lathe.MirroredRing(0.60f, (0f, 0.85f, 0f), (35f, 0.85f, 0f), (80f, 0.85f, 0f), (130f, 0.85f, 0f), (180f, 0.85f, 0f)),
            Lathe.MirroredRing(0f, (0f, 0.95f, 0f), (35f, 1.0f, 0f), (80f, 1.0f, 0f), (130f, 1.0f, 0f), (180f, 1.05f, 0f)),
            Lathe.MirroredRing(-0.60f, (0f, 0.80f, 0f), (35f, 0.85f, 0f), (80f, 0.90f, 0f), (130f, 0.90f, 0f), (180f, 0.95f, 0f)),
        ],
        new Vector3(0f, -1f, 0f));

    /// <summary>
    /// 部位の正規化形状。腕は長さ方向がローカル X、脚は Y、足は Z（<see cref="Aigis.Physics.HumanoidRagdoll"/> の箱と同じ）。
    /// 腕は左右で向きが逆になるので、長さ方向に対称な形にしておく。
    /// </summary>
    public static LowPolyMesh For(BodyPartKind kind) => kind switch
    {
        BodyPartKind.Head => Head(),
        BodyPartKind.Chest or BodyPartKind.Abdomen => Chest(),
        BodyPartKind.Pelvis => Pelvis(),
        BodyPartKind.UpperArm => UpperArm().Rotated(LimbAlongX),
        BodyPartKind.Forearm => Forearm().Rotated(LimbAlongX),
        BodyPartKind.Coxa => Limb(0.88f, 1f, 0.88f).Rotated(LimbAlongX),
        BodyPartKind.Thigh => Limb(0.95f, 1.0f, 0.72f),
        BodyPartKind.Shin => Limb(0.88f, 1.0f, 0.62f),
        BodyPartKind.Foot => Heel(),
        BodyPartKind.Toe => Toe(),
        _ => throw new ArgumentOutOfRangeException(nameof(kind), kind, "形状の無い部位です。"),
    };

    /// <summary>
    /// 足（かかと側、母指球まで）: 靴の形。足裏（−Y）は平らで箱の幅いっぱい、上（+Y）は足首へ向けて細る。上から見ると、かかと（−Z）は丸く、
    /// 前（+Z）はつま先とそろう直線。以前は Z に沿った丸い六角柱で、足裏が 3 セル幅しかなく、平たいつま先とつながらなかった。
    /// </summary>
    private static LowPolyMesh Heel() => Lathe.Build(
        new Vector3(0f, 1f, -0.15f),
        [
            FootOutline(0.9f, 0.7f, -0.75f, 0.45f, backRound: 0.6f, frontRound: 0.6f),
            FootOutline(0.3f, 0.92f, -0.95f, 0.95f, backRound: 0.8f, frontRound: 0.1f),
            FootOutline(-0.55f, 1f, -1f, 1f, backRound: 0.8f, frontRound: 0f),
            FootOutline(-0.97f, 0.98f, -0.97f, 1f, backRound: 0.8f, frontRound: 0f),
        ],
        new Vector3(0f, -1f, 0f));

    /// <summary>
    /// つま先（母指球より前）: 平たい足先。足裏（−Y）は平らで箱の幅いっぱい、甲（+Y）は母指球（後ろ −Z）から指先（前 +Z）へ下がり、
    /// 上から見ると後ろはかかと側とそろう直線、前は指の並びの丸み。Y 軸のろくろで、輪は高さごとの平面の輪郭。
    /// 以前は Z に沿った丸い六角柱で、両端がとがって丸い塊に見えた。
    /// </summary>
    private static LowPolyMesh Toe() => Lathe.Build(
        new Vector3(0f, 1f, -0.7f),
        [
            FootOutline(0.92f, 0.85f, -1f, -0.35f, backRound: 0f, frontRound: 1f),
            FootOutline(0.35f, 0.95f, -1f, 0.35f, backRound: 0f, frontRound: 1f),
            FootOutline(-0.45f, 1f, -1f, 0.85f, backRound: 0f, frontRound: 1f),
            FootOutline(-0.97f, 0.98f, -1f, 0.92f, backRound: 0f, frontRound: 1f),
        ],
        new Vector3(0f, -1f, 0f));

    /// <summary>
    /// 足の、高さ <paramref name="y"/> での平面の輪郭: 幅 ±<paramref name="halfWidth"/>、Z は <paramref name="back"/>..<paramref name="front"/> の角の丸い長方形。
    /// 前後の角の丸みは、取れる最大の丸み（幅の半分と長さの半分の小さい方）に対する割合（1 = 半円。0 でも 1 割は面取りして点が重ならないようにする）。
    /// 4 つの角の丸みの円に 3 点ずつ（0°・45°・90°）、θ（+Z から +X へ）の順に 12 点。長方形の角の位置に点が来るので、つなぎ目（かかと側の前・つま先の後ろ）が
    /// 幅いっぱいの直線になる。4 つの輪で三角形 96 枚（低ポリの上限 112 の内）。
    /// </summary>
    private static List<Vector3> FootOutline(float y, float halfWidth, float back, float front, float backRound, float frontRound)
    {
        const float LeastRound = 0.1f;
        var most = MathF.Min(halfWidth, 0.5f * (front - back));
        var rf = MathF.Max(LeastRound, frontRound) * most;
        var rb = MathF.Max(LeastRound, backRound) * most;
        var points = new List<Vector3>(12);
        foreach (var (sx, r, cz, start) in new[]
        {
            (1f, rf, front - rf, 0f), // 右前: +Z → +X
            (1f, rb, back + rb, 90f), // 右後ろ: +X → −Z
            (-1f, rb, back + rb, 180f), // 左後ろ: −Z → −X
            (-1f, rf, front - rf, 270f), // 左前: −X → +Z
        })
        {
            var cx = sx * (halfWidth - r);
            for (var k = 0; k < 3; k++)
            {
                var a = (start + (45f * k)) * MathF.PI / 180f;
                points.Add(new Vector3(cx + (r * MathF.Sin(a)), y, cz + (r * MathF.Cos(a))));
            }
        }

        return points;
    }

    /// <summary>Y 軸の六角柱（上端・中ほど・下端の太さ）。</summary>
    private static LowPolyMesh Limb(float upper, float middle, float lower) => Lathe.Build(
        new Vector3(0f, 1f, 0f),
        [Lathe.RoundRing(0.72f, upper, LimbSides), Lathe.RoundRing(0.1f, middle, LimbSides), Lathe.RoundRing(-0.72f, lower, LimbSides)],
        new Vector3(0f, -1f, 0f));

    /// <summary>
    /// 上腕（Y で作って X へ回す）: 両端は細く、中ほどの前（+Z）に力こぶ、後ろに三頭筋。
    /// 左右の腕で長さ方向の向きが逆になるので、前後の形だけを付けて長さ方向は対称にする。
    /// </summary>
    private static LowPolyMesh UpperArm() => Lathe.Build(
        new Vector3(0f, 1f, 0f),
        [
            Lathe.MirroredRing(0.70f, (0f, 0.85f, 0f), (60f, 0.85f, 0f), (120f, 0.85f, 0f), (180f, 0.85f, 0f)),
            Lathe.MirroredRing(0.22f, (0f, 1.05f, 0f), (60f, 0.92f, 0f), (120f, 0.88f, 0f), (180f, 0.98f, 0f)), // 力こぶ・三頭筋
            Lathe.MirroredRing(-0.22f, (0f, 1.05f, 0f), (60f, 0.92f, 0f), (120f, 0.88f, 0f), (180f, 0.98f, 0f)),
            Lathe.MirroredRing(-0.70f, (0f, 0.85f, 0f), (60f, 0.85f, 0f), (120f, 0.85f, 0f), (180f, 0.85f, 0f)),
        ],
        new Vector3(0f, -1f, 0f));

    /// <summary>前腕: 上下（ローカル Y）より前後（Z）が薄い平たい形で、中ほどが太い。</summary>
    private static LowPolyMesh Forearm() => Lathe.Build(
        new Vector3(0f, 1f, 0f),
        [
            Lathe.MirroredRing(0.70f, (0f, 0.78f, 0f), (60f, 0.88f, 0f), (120f, 0.88f, 0f), (180f, 0.78f, 0f)),
            Lathe.MirroredRing(0f, (0f, 0.92f, 0f), (60f, 1.0f, 0f), (120f, 1.0f, 0f), (180f, 0.92f, 0f)),
            Lathe.MirroredRing(-0.70f, (0f, 0.78f, 0f), (60f, 0.88f, 0f), (120f, 0.88f, 0f), (180f, 0.78f, 0f)),
        ],
        new Vector3(0f, -1f, 0f));

    /// <summary>Y → X（Z まわり −90°）。鏡映ではなく回転なので面の表裏が保たれる。</summary>
    private static Quaternion LimbAlongX => Quaternion.CreateFromAxisAngle(Vector3.UnitZ, -MathF.PI / 2f);
}
