using System.Collections.Concurrent;
using System.Numerics;
using Aigis.Physics;
using Aigis.Render;
using static MiniRing.Visual.ShapeMesh;

namespace MiniRing.Visual;

/// <summary>
/// ボクサーの見た目（剛体 1 つにつき 1 メッシュ、描画専用。当たり判定は物理の箱のまま）。
/// <para>
/// 比率は成人男性（約 7.5 頭身・肩幅 ≒ 2 頭・上腕 1.5 / 前腕 1.25 / 腿 2 / すね 1.9 頭）を下敷きに、
/// ややデフォルメ（約 6 頭身・大きめのグローブ・広い肩と細い腰）。寸法はすべて物理の箱（HalfExtents）と
/// 関節位置（HumanoidRagdoll.BuildCore と同じ式）から割り出すので、体格の調整に追従する。
/// </para>
/// <para>座標は各剛体のローカル: +Z が体の前、+Y が上。腕は T ポーズの長さ方向が ±X（物理的な側 side = ±1）。</para>
/// </summary>
public sealed class BoxerModel
{
    private static readonly ConcurrentDictionary<(HumanoidRagdoll.HalfExtents, float), BoxerModel> Cache = new();

    private BoxerModel()
    {
    }

    public MeshData Head { get; private init; } = null!;
    public MeshData Torso { get; private init; } = null!;
    public MeshData Pelvis { get; private init; } = null!;

    /// <summary>物理的に −X 側（Aigis の "Left*" 部位）と +X 側の上腕・前腕・グローブ。</summary>
    public MeshData UpperArmNegX { get; private init; } = null!;
    public MeshData UpperArmPosX { get; private init; } = null!;
    public MeshData ForearmNegX { get; private init; } = null!;
    public MeshData ForearmPosX { get; private init; } = null!;
    public MeshData GloveNegX { get; private init; } = null!;
    public MeshData GlovePosX { get; private init; } = null!;
    public MeshData Thigh { get; private init; } = null!;
    public MeshData Shin { get; private init; } = null!;
    public MeshData Foot { get; private init; } = null!;

    /// <summary>頭のメッシュを持ち上げる量 [m]: 関節の重なり代（3cm 固定）で頭が胸に埋まって見えるのを、首を見せて補う。</summary>
    public float HeadLift { get; private init; }

    public static BoxerModel For(HumanoidRagdoll.HalfExtents half, float gloveRadius)
        => Cache.GetOrAdd((half, gloveRadius), key => Create(key.Item1, key.Item2));

    private static BoxerModel Create(HumanoidRagdoll.HalfExtents h, float gloveRadius)
    {
        const float overlap = HumanoidRagdoll.Overlap;
        var headLift = overlap * 0.55f;
        return new BoxerModel
        {
            HeadLift = headLift,
            Head = BuildHead(h.Head),
            Torso = BuildTorso(h.Chest, headLift),
            Pelvis = BuildPelvis(h.Pelvis),
            UpperArmNegX = BuildUpperArm(h.UpperArm, -1f, overlap),
            UpperArmPosX = BuildUpperArm(h.UpperArm, 1f, overlap),
            ForearmNegX = BuildForearm(h.Forearm, -1f, overlap),
            ForearmPosX = BuildForearm(h.Forearm, 1f, overlap),
            GloveNegX = BuildGlove(h.Forearm, -1f, gloveRadius),
            GlovePosX = BuildGlove(h.Forearm, 1f, gloveRadius),
            Thigh = BuildThigh(h.Thigh, overlap),
            Shin = BuildShin(h.Shin, overlap),
            Foot = BuildFoot(h.Foot),
        };
    }

    // ── 頭: 顎が細く頭頂が丸い。顔の前に鼻と眉の張り出し（単色でも向きが分かる）。 ──────────────
    private static MeshData BuildHead(Vector3 h)
    {
        // 頭蓋: 顎（下端）は細く前へ、頬骨で最も広く、頭頂は丸く、後頭部は後ろへ張る。
        var skull = Loft(new Vector3(0f, -h.Y, 0f), new Vector3(0f, h.Y, 0f), Vector3.UnitX, s =>
        {
            // 下端（顎の下）は少しだけ丸め、上端（頭頂）は円弧で丸く閉じる。
            var chin = Curve(s, (0f, 0.55f), (0.1f, 0.9f), (0.2f, 1f));
            var crown = s > 0.62f ? MathF.Sqrt(MathF.Max(0f, 1f - MathF.Pow((s - 0.62f) / 0.38f, 2f))) : 1f;
            var width = Curve(s, (0f, 0.62f), (0.22f, 0.8f), (0.48f, 0.95f), (0.62f, 1f), (1f, 0.95f)) * h.X;
            var depth = Curve(s, (0f, 0.62f), (0.25f, 0.86f), (0.6f, 1f), (1f, 1f)) * h.Z;
            var forward = Curve(s, (0f, 0.22f), (0.3f, 0.1f), (0.65f, -0.04f), (1f, -0.12f)) * h.Z;
            var c = chin * crown;
            return new Ring(width * c, depth * c, 2.15f, new Vector2(0f, forward));
        }, rings: 30, around: 34);

        // 眉: 顔の表面（z ≈ 0.95）に沿う横長の張り出し。目の上の影で表情が出る。
        var brow = Loft(new Vector3(-0.55f * h.X, 0.2f * h.Y, 0.88f * h.Z), new Vector3(0.55f * h.X, 0.2f * h.Y, 0.88f * h.Z), Vector3.UnitY,
            s => new Ring(0.1f * h.Y * Cap(s, 0.5f), 0.14f * h.Z * Cap(s, 0.5f), 2f, new Vector2(0f, -0.14f * h.Z * MathF.Pow((2f * s) - 1f, 2f))),
            rings: 14, around: 12);

        // 鼻: 眉間から下向きに出る小さな楔。
        var nose = Loft(new Vector3(0f, 0.08f * h.Y, 0.88f * h.Z), new Vector3(0f, -0.22f * h.Y, 1.02f * h.Z), Vector3.UnitX,
            s => new Ring(Curve(s, (0f, 0.07f), (0.8f, 0.13f), (1f, 0.11f)) * h.X * Cap(s, 0.3f), Curve(s, (0f, 0.09f), (1f, 0.15f)) * h.Z * Cap(s, 0.35f)),
            rings: 10, around: 12);
        return Build("boxer.head", skull, brow, nose, Ear(h, 1f), Ear(h, -1f));
    }

    private static List<Vector3[]> Ear(Vector3 h, float side)
        => Loft(new Vector3(side * 0.9f * h.X, 0.02f * h.Y, -0.08f * h.Z), new Vector3(side * 1.03f * h.X, 0.02f * h.Y, -0.1f * h.Z), Vector3.UnitY,
            s => new Ring(0.2f * h.Y * Cap(s, 0.5f), 0.13f * h.Z * Cap(s, 0.5f)), rings: 6, around: 12);

    // ── 胴: 細い腰から逆三角形に広がる胸、僧帽筋の傾斜、肩の三角筋、首。 ──────────────────────
    private static MeshData BuildTorso(Vector3 h, float headLift)
    {
        // 細い腰 → 広がる胸郭 → 肩幅（三角筋の付け根まで）→ 僧帽筋のなだらかな傾斜で首へ。胸筋の分だけ前に張る。
        var body = Loft(new Vector3(0f, -1.25f * h.Y, 0f), new Vector3(0f, 1.15f * h.Y, 0f), Vector3.UnitX, s =>
        {
            var bottom = Curve(s, (0f, 0.8f), (0.06f, 1f));
            // 肩の高さでは肩関節（x = ±h.X）より外へ張らない: 外側の丸みは上腕メッシュの三角筋が受け持つ（腕と一緒に回る）。
            var width = Curve(s, (0f, 0.66f), (0.28f, 0.66f), (0.58f, 0.96f), (0.76f, 1.05f), (0.86f, 0.98f), (0.94f, 0.72f), (1f, 0.34f)) * h.X;
            var depth = Curve(s, (0f, 0.8f), (0.45f, 0.92f), (0.68f, 1.1f), (0.85f, 0.9f), (0.93f, 0.68f), (1f, 0.45f)) * h.Z;
            var forward = Curve(s, (0f, 0.02f), (0.45f, 0.04f), (0.68f, 0.12f), (0.9f, 0.02f), (1f, -0.08f)) * h.Z;
            // 下は角ばった胴、上へ行くほど丸く（肩口・首の付け根に角を作らない）。
            return new Ring(width * bottom, depth * bottom, Curve(s, (0f, 2.5f), (0.7f, 2.3f), (1f, 2f)), new Vector2(0f, forward));
        }, rings: 30, around: 36);
        var neck = Loft(new Vector3(0f, 0.8f * h.Y, -0.05f * h.Z), new Vector3(0f, h.Y + headLift + HumanoidRagdoll.Overlap * 0.4f, 0f), Vector3.UnitX,
            s => new Ring(0.36f * h.X, 0.62f * h.Z, 2.2f), rings: 6, around: 20);
        return Build("boxer.torso", body, neck);
    }

    // ── 腰: トランクス（ウエストバンドの段差だけで表現）。 ─────────────────────────────────────
    private static MeshData BuildPelvis(Vector3 h)
    {
        var trunks = Loft(new Vector3(0f, -1.05f * h.Y, 0f), new Vector3(0f, 1.05f * h.Y, 0f), Vector3.UnitX, s =>
        {
            // 下端は丸く閉じる（ダウン時や脚を開いたときに中が見えないように）。上端は胴の中に埋まる。
            var cap = Cap(MathF.Min(s, 0.5f), 0.18f) * Curve(s, (0.9f, 1f), (1f, 0.85f));
            var band = Curve(s, (0f, 1f), (0.72f, 1f), (0.78f, 1.07f), (0.95f, 1.07f), (1f, 1f));
            var width = Curve(s, (0f, 1.02f), (0.5f, 1.06f), (1f, 0.96f)) * h.X * band;
            return new Ring(width * cap, 1.0f * h.Z * band * cap, 2.8f);
        }, rings: 18, around: 32);
        return Build("boxer.pelvis", trunks);
    }

    // ── 上腕: 肩から肘へ。上腕二頭筋のふくらみ。 ────────────────────────────────────────────
    private static MeshData BuildUpperArm(Vector3 h, float side, float overlap)
    {
        var shoulder = -side * (h.X - overlap);
        var elbow = side * h.X;
        // 肘側は関節の中心から半径ぶん先まで伸ばし、丸い端を関節の球に見立てる（曲げても外側がくびれない）。
        var arm = Loft(new Vector3(shoulder - (side * 0.016f), 0f, 0f), new Vector3(elbow + (side * 0.9f * h.Y), 0f, 0f), Vector3.UnitY, s =>
        {
            // 肩側は三角筋で太く丸く（腕と一緒に回る）、中ほどに上腕二頭筋、肘で細る。
            var cap = Cap(s, 0.18f);
            var r = Curve(s, (0f, 1.3f), (0.18f, 1.32f), (0.38f, 1.08f), (0.55f, 1.12f), (0.85f, 0.84f), (1f, 0.8f));
            return new Ring(r * h.Y * cap, r * h.Z * 1.05f * cap, 2f, new Vector2(0f, Curve(s, (0f, 0f), (0.55f, 0.08f), (1f, 0f)) * h.Z));
        }, rings: 22, around: 22);
        return Build("boxer.upperarm", arm);
    }

    // ── 前腕: 肘から手首へ細る（手首はグローブに隠れる）。 ─────────────────────────────────────
    private static MeshData BuildForearm(Vector3 h, float side, float overlap)
    {
        var elbow = -side * (h.X - overlap);
        var wrist = side * (h.X * 0.75f);
        var arm = Loft(new Vector3(elbow - (side * 0.9f * h.Y), 0f, 0f), new Vector3(wrist, 0f, 0f), Vector3.UnitY, s =>
        {
            var cap = Cap(s, 0.2f);
            var r = Curve(s, (0f, 0.9f), (0.3f, 1.1f), (0.85f, 0.78f), (1f, 0.72f));
            return new Ring(r * h.Y * cap, r * h.Z * cap, 2f);
        }, rings: 16, around: 20);
        return Build("boxer.forearm", arm);
    }

    // ── グローブ: 手首を包むカフ → 丸く膨らんだ拳。親指のふくらみ。 ──────────────────────────
    private static MeshData BuildGlove(Vector3 forearm, float side, float gloveRadius)
    {
        var center = side * forearm.X;
        var r = gloveRadius * 1.2f;
        var glove = Loft(new Vector3(center - (side * r * 1.25f), 0f, 0f), new Vector3(center + (side * r * 1.05f), 0f, 0f), Vector3.UnitY, s =>
        {
            // カフ（手首を包む筒）→ 拳のふくらみ → 先端は丸く閉じる。
            var cuff = Curve(s, (0f, 0.62f), (0.25f, 0.66f), (0.45f, 0.95f), (0.7f, 1f), (1f, 0.9f));
            var start = Curve(s, (0f, 0.85f), (0.04f, 1f));
            var end = s > 0.7f ? MathF.Sqrt(MathF.Max(0f, 1f - MathF.Pow((s - 0.7f) / 0.3f, 2f))) : 1f;
            return new Ring(r * cuff * start * end, r * 0.92f * cuff * start * end, 2.1f);
        }, rings: 22, around: 26);
        var thumb = Loft(new Vector3(center - (side * r * 0.35f), 0.0f, r * 0.62f), new Vector3(center + (side * r * 0.35f), 0f, r * 0.88f), Vector3.UnitY,
            s => new Ring(r * 0.32f * Cap(s, 0.5f), r * 0.3f * Cap(s, 0.5f)), rings: 10, around: 14);
        return Build("boxer.glove", glove, thumb);
    }

    // ── 腿・すね: 大腿四頭筋とふくらはぎ。 ─────────────────────────────────────────────────
    private static MeshData BuildThigh(Vector3 h, float overlap)
    {
        // 膝は腿とすねの丸い端を深く重ねて、人形の継ぎ目に見せない。
        var leg = Loft(new Vector3(0f, h.Y - overlap + 0.012f, 0f), new Vector3(0f, -h.Y - (0.35f * h.X), 0f), Vector3.UnitX, s =>
        {
            var cap = Cap(s, 0.22f);
            var r = Curve(s, (0f, 0.92f), (0.28f, 0.92f), (0.85f, 0.7f), (1f, 0.68f));
            return new Ring(r * h.X * cap, r * h.Z * cap, 2f, new Vector2(0f, Curve(s, (0f, 0f), (0.35f, 0.08f), (1f, 0f)) * h.Z));
        }, rings: 18, around: 22);
        return Build("boxer.thigh", leg);
    }

    private static MeshData BuildShin(Vector3 h, float overlap)
    {
        var leg = Loft(new Vector3(0f, h.Y - overlap + 0.016f, 0f), new Vector3(0f, -h.Y - 0.002f, 0f), Vector3.UnitX, s =>
        {
            var cap = Cap(s, 0.2f);
            var r = Curve(s, (0f, 0.9f), (0.12f, 0.88f), (0.35f, 1.0f), (0.8f, 0.58f), (1f, 0.55f));
            return new Ring(r * h.X * cap, r * h.Z * cap, 2f, new Vector2(0f, Curve(s, (0f, 0f), (0.3f, -0.15f), (1f, 0f)) * h.Z));
        }, rings: 18, around: 22);
        return Build("boxer.shin", leg);
    }

    // ── 足: ボクシングシューズ（ローカット）。 ─────────────────────────────────────────────
    private static MeshData BuildFoot(Vector3 h)
    {
        var shoe = Loft(new Vector3(0f, 0f, -h.Z), new Vector3(0f, 0f, h.Z), Vector3.UnitX, s =>
        {
            var cap = Cap(s, 0.18f);
            var width = Curve(s, (0f, 0.85f), (0.6f, 1.02f), (1f, 0.85f)) * h.X;
            var height = Curve(s, (0f, 1f), (0.7f, 0.85f), (1f, 0.65f)) * h.Y;
            var lift = Curve(s, (0f, 0f), (0.7f, -0.05f), (1f, -0.2f)) * h.Y;
            return new Ring(width * cap, height * cap, 2.6f, new Vector2(0f, lift));
        }, rings: 16, around: 22);
        // 足首を包む筒は付けない: 膝・足首を曲げた姿勢では足首の位置が筒からずれて浮いて見えるため（PDCA で確認）。
        return Build("boxer.foot", shoe);
    }
}
