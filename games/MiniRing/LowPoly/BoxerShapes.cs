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
        BodyPartKind.Chest => Chest(),
        BodyPartKind.Pelvis => Pelvis(),
        BodyPartKind.UpperArm => UpperArm().Rotated(LimbAlongX),
        BodyPartKind.Forearm => Forearm().Rotated(LimbAlongX),
        BodyPartKind.Thigh => Limb(0.95f, 1.0f, 0.72f),
        BodyPartKind.Shin => Limb(0.88f, 1.0f, 0.62f),
        BodyPartKind.Foot => Limb(0.85f, 1.0f, 0.9f).Rotated(LimbAlongZ),
        BodyPartKind.Toe => Limb(0.95f, 0.9f, 0.7f).Rotated(LimbAlongZ),
        _ => throw new ArgumentOutOfRangeException(nameof(kind), kind, "形状の無い部位です。"),
    };

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

    /// <summary>Y → Z（X まわり +90°）。つま先が +Z。</summary>
    private static Quaternion LimbAlongZ => Quaternion.CreateFromAxisAngle(Vector3.UnitX, MathF.PI / 2f);
}
