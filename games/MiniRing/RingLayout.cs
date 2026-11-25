using System.Numerics;

namespace MiniRing;

/// <summary>試合の場の形。</summary>
public enum ArenaShape
{
    /// <summary>四角いリング（ロープ 3 段・コーナーポスト 4 本）。WVC・今までの試合の形。</summary>
    RopedSquare,

    /// <summary>六角形の金網の檻（VFC）。向かい合う辺は ±Z 方向（2 人が始めに向き合う方向）の平らな面で、頂点は ±X 軸上。</summary>
    HexCage,
}

/// <summary>
/// 試合の場の大きさ: リングの 1 辺と、ラウンドの始めに 2 人が立つ所（両者のコーナー、またはドリル用に中央で離れる距離）。リングを載せるローテーブルと台座はリングに合わせて広がり
/// （リングの縁から台座の縁まで 22.5 cm、天板の縁まで 42 cm。物理の空間の長さ）、テーブルの手前の縁はいつもプレイヤーの立つ所（原点）から 56 cm 先にある。
/// 檻（<see cref="ArenaShape.HexCage"/>）のときの <paramref name="Size"/> は向かい合う辺と辺の距離（内接円の直径）。
/// </summary>
/// <param name="Size">リングの 1 辺 [m]（檻は向かい合う辺と辺の距離 [m]）。</param>
/// <param name="StartGap">コーナーから始めない（<paramref name="FromCorners"/> が偽の）とき、ラウンドの始めの 2 人の足と足の距離 [m]。</param>
/// <param name="Shape">場の形（既定は四角いリング）。</param>
/// <param name="FromCorners">ラウンドを両者のコーナー（<see cref="RestCorner"/>: 赤・青の休むコーナー）から始めるか（2026-11-18、ユーザーの指定「試合の開始はコーナーから」）。
/// 試合のホスト（デスクトップ・VR・動画・テレビ中継）は <see cref="BoxingCorners.StartingAtCorners"/> で真にする。偽（既定）なら中央で <paramref name="StartGap"/> だけ
/// 離れて向き合う（技のドリル・調べもの・リーグ・テストで、すぐに打ち合う形。今までの記録とも同じ）。</param>
public sealed record RingLayout(float Size, float StartGap, ArenaShape Shape = ArenaShape.RopedSquare, bool FromCorners = false)
{
    /// <summary>テーブルの手前の縁から、プレイヤーの立つ所（原点）までの距離 [m]。</summary>
    private const float NearEdge = 0.5634f;

    /// <summary>
    /// 試合の標準のリングの 1 辺 [m]（拳も武器も同じ）: 人の大きさで 5.3 m（表示では人形の縮尺 0.355 で 1.88 m に見える）。
    /// 2026-11-18、ユーザーの指定: 動き回れるようにまず 7.3 m（プロのリングのロープの内側の上限）にしたが大きすぎたので、
    /// 以前の 3.38 m（人形の単位で 1.2 m）との中間にした。
    /// </summary>
    public const float StandardSize = 5.3f;

    /// <summary>
    /// コーナーに立つ体と持ち物（武器・盾・はめる物）の輪郭と、場の縁の当たり（ロープ・金網の内側の面）との間に残す隙間 [m]（2 cm × 2.817）。
    /// 輪郭はボクセルの格子の箱で、ボクセル 1 つ分ほどの余り。生まれた時点で何も縁に食い込まないための量（2026-11-24: 檻の 120° の角で武器・盾が金網に挟まって動けなかった）。
    /// </summary>
    public const float CornerClearance = 0.05634f;

    /// <summary>
    /// 拳の試合: 標準のリング（<see cref="StandardSize"/>）。コーナーから始めない形（ドリル）では 1.24 m 離れて始める（拳の間合いの少し外）。
    /// （2026-11-18 まで 1.2 m（人形の単位）: 身長の 2 倍、人なら 3.4 m で、下がる余地が少なかった。）
    /// </summary>
    public static RingLayout Boxing { get; } = new(StandardSize, 1.239f);

    /// <summary>
    /// 武器同士の試合: 拳の試合と同じ標準のリング（2026-11-18、ユーザーの指定。それまでは 2.4 m: 剣道の試合場 9〜11 m 四方をテーブルに載る大きさに縮めた値）。
    /// コーナーから始めない形では 2.82 m 離れて始める（剣道の開始線は 2 人の間 2.8 m、身長 1.7 m の 1.65 倍。
    /// どの技の間合い（0.96〜2.25 m）よりも遠い遠間）。
    /// </summary>
    public static RingLayout Weapons { get; } = new(StandardSize, 2.817f);

    /// <summary>
    /// ドリル・調べもの・リーグ・テストの場（リングを指定しないで作った試合）: 2026-11-18 まで拳の試合のリングだった 3.38 m（人形の単位で 1.2 m、身長の 2 倍）で、
    /// 中央で 1.24 m 離れて始める。技を 1 つずつ磨くドリルと、その記録（docs/arena）・テストの数値はこの場で測ってきたので、比べられるよう残す。
    /// </summary>
    public static RingLayout DrillBoxing { get; } = new(3.38f, 1.239f);

    /// <summary>武器同士のドリル・調べものの場: 2026-11-18 まで武器の試合のリングだった 6.76 m（人形の単位で 2.4 m）で、中央で 2.82 m 離れて始める。</summary>
    public static RingLayout DrillWeapons { get; } = new(6.761f, 2.817f);

    /// <summary>
    /// 拳の試合の檻: 向かい合う辺の距離 3.66 m の正六角形（面積 ≈ 11.6 m²。四角い 3.38 m の 11.4 m² と同じ広さ）、始めの間は拳のリングと同じ 1.24 m。
    /// </summary>
    public static RingLayout Cage { get; } = new(3.662f, 1.239f, ArenaShape.HexCage);

    /// <summary>
    /// 武器の試合の檻: 向かい合う辺の距離 7.32 m の正六角形（面積 ≈ 46.4 m²。四角い武器のリング 6.76 m の 45.7 m² と同じ広さ）、始めの間は 2.82 m。
    /// </summary>
    public static RingLayout WeaponsCage { get; } = new(7.324f, 2.817f, ArenaShape.HexCage);

    /// <summary>檻か。</summary>
    public bool IsCage => Shape == ArenaShape.HexCage;

    /// <summary>リングの半分の幅 [m]（檻は向かい合う辺までの距離 = 内接円の半径）。</summary>
    public float Half => Size * 0.5f;

    /// <summary>檻の外接円の半径 R [m]（六角形の頂点までの距離。向かい合う辺の距離 Size を √3 で割る）。四角いリングでは <see cref="Half"/>。</summary>
    public float Circumradius => IsCage ? Size / MathF.Sqrt(3f) : Half;

    /// <summary>
    /// 場がローテーブルの上へ張り出す半分の幅 [m]: 四角いリングは <see cref="Half"/>、檻は頂点（X 方向）の R。台座・天板はこれに余白を足す
    /// （正方形のまま、檻の頂点まで覆う）。
    /// </summary>
    public float OuterHalf => IsCage ? Circumradius : Half;

    /// <summary>ローテーブルの天板の半分の幅 [m]（正方形）。</summary>
    public float TableHalf => OuterHalf + 0.4225f;

    /// <summary>箱庭の台座の半分の幅 [m]（正方形）。</summary>
    public float PlinthHalf => OuterHalf + 0.2254f;

    /// <summary>リング（とテーブル）の中心（床の上）。</summary>
    public Vector3 Center => new(0f, 0f, NearEdge + TableHalf);

    /// <summary>同じリングで、両者のコーナーから始める形。</summary>
    public RingLayout AtCorners() => this with { FromCorners = true }; // メソッド: 記録の JSON に入れ子で書かれないよう

    /// <summary>
    /// ニュートラルコーナー（どちらの持ち場でもない角。四角は左奥・右手前、檻は 120°・300° の頂点）のうち、<paramref name="awayFrom"/> から遠い方の立つ所（床の上）。
    /// ダウンを取った側が、数えている間に下がって待つ所（ボクシングの決まり: 倒れた相手からいちばん遠いニュートラルコーナー）。
    /// 立つ所は休むコーナーと同じ式（<see cref="CornerFor"/>: 輪郭 <paramref name="footprint"/> がどの面にも触れない所）。
    /// </summary>
    public Vector3 NeutralCorner(Vector3 awayFrom, IReadOnlyList<Vector2> footprint)
    {
        var (a, b) = IsCage ? (CornerFor(120f, footprint), CornerFor(300f, footprint)) : (CornerFor(135f, footprint), CornerFor(315f, footprint));

        static float Flat(Vector3 p, Vector3 q) => new Vector2(p.X - q.X, p.Z - q.Z).LengthSquared();
        return Flat(a, awayFrom) >= Flat(b, awayFrom) ? a : b;
    }

    private static readonly float[] CageNormalAngles = [MathF.PI / 6f, -MathF.PI / 6f];

    /// <summary>
    /// 場の縁（四角は 4 つの辺、檻は 6 つの面）までの内側の距離 [m]（水平。負なら外）。点が縁から何 m 内側にいるか。
    /// 檻は、面の法線が 90°・30°・−30°（X 軸から Z 軸の向きへ測る）の 3 方向の軸への射影の最大から出す。
    /// </summary>
    public float EdgeDistance(Vector3 point)
    {
        var d = point - Center;
        if (!IsCage)
        {
            return Half - MathF.Max(MathF.Abs(d.X), MathF.Abs(d.Z));
        }

        var worst = MathF.Abs(d.Z);
        foreach (var angle in CageNormalAngles)
        {
            worst = MathF.Max(worst, MathF.Abs((d.X * MathF.Cos(angle)) + (d.Z * MathF.Sin(angle))));
        }

        return Half - worst;
    }

    /// <summary>
    /// 場の縁の当たりの面が、縁（<see cref="Half"/>）から内側へ出ている厚み [m]: 四角いリングはロープの当たりの箱の半分の厚み（<see cref="Ring.RopeCollisionHalf"/>）、
    /// 檻は金網の内側の面がちょうど縁（<see cref="HexCage"/>: 壁の箱は縁の外側に置く）なので 0。
    /// </summary>
    public float BoundaryInset => IsCage ? 0f : Ring.RopeCollisionHalf;

    /// <summary>
    /// 休むコーナー（床の上の、骨盤の立つ所）: 四角は赤が (−X, −Z)・青が (+X, +Z) の角、檻は赤が 240°・青が 60° の頂点（X 軸から Z 軸の向きへ測る）の側。
    /// 立つ所は <see cref="CornerFor"/>（体と持ち物の輪郭 <paramref name="footprint"/> が、どの面にも <see cref="CornerClearance"/> の隙間を残して触れない、角にいちばん近い所）。
    /// </summary>
    public Vector3 RestCorner(bool red, IReadOnlyList<Vector2> footprint) => SeatCorner(red ? 0 : 1, 2, footprint);

    /// <summary>
    /// <paramref name="seats"/> 人の試合で、席 <paramref name="seat"/>（赤 = 0、青 = 1、バトルロイヤルの追加の席 = 2..）の休むコーナー・始めの立つ所。
    /// 角の向きは赤の角（檻 240°・四角 225°）から 360° / 人数ずつ回した所（2 人なら赤・青の対角、檻で 6 人なら 6 つの頂点すべて）。
    /// 立つ所は <see cref="CornerFor"/>（輪郭が縁に触れない、角にいちばん近い所）。
    /// </summary>
    public Vector3 SeatCorner(int seat, int seats, IReadOnlyList<Vector2> footprint)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(seats, 2);
        ArgumentOutOfRangeException.ThrowIfNegative(seat);
        ArgumentOutOfRangeException.ThrowIfGreaterThanOrEqual(seat, seats);
        var start = IsCage ? 240f : 225f;
        return CornerFor((start + (seat * (360f / seats))) % 360f, footprint); // 2 人なら 240°・60°（225°・45°）がちょうど出る
    }

    /// <summary>
    /// バトルロイヤル（6 人）の檻: 向かい合う辺の距離 8.97 m の正六角形（面積 ≈ 69.7 m²）。1 人あたりの広さを素手の檻（<see cref="Cage"/>、1 人 5.8 m²）と
    /// 武器の檻（<see cref="WeaponsCage"/>、1 人 23.2 m²）の幾何平均 11.6 m² にした（素手と武器が混ざる）。始めは席ごとの頂点（<see cref="SeatCorner"/>）から。
    /// </summary>
    public static RingLayout RoyaleCage { get; } = new(8.97f, 2.817f, ArenaShape.HexCage, FromCorners: true);

    /// <summary>
    /// 中心から <paramref name="degrees"/> の向き（X 軸から Z 軸の向きへ測る。角の二等分線）の上で、角にいちばん近く、輪郭 <paramref name="footprint"/>
    /// （骨盤から見た、体と持ち物の格子の箱の角の水平のずれ: X, Z）のどの点も、場のどの面からも <see cref="BoundaryInset"/> ＋ <see cref="CornerClearance"/> 以上内側にある所。
    /// 面の法線 n、二等分線の向き u、中心からの距離 s、輪郭の点 q について n·(s u + q) ≤ Half − 厚み − 隙間 を、u に向いた面（n·u &gt; 0）ごとに解いた s の最小
    /// （<see cref="EdgeDistance"/> と同じ面の並び）。場の形と持ち物の大きさだけで決まり、持ち物の種類は見ない。輪郭が場より大きければ中心に立つ。
    /// </summary>
    public Vector3 CornerFor(float degrees, IReadOnlyList<Vector2> footprint)
    {
        ArgumentNullException.ThrowIfNull(footprint);
        var angle = degrees * (MathF.PI / 180f);
        var u = new Vector2(MathF.Cos(angle), MathF.Sin(angle));
        var vertex = IsCage ? Circumradius : Half * MathF.Sqrt(2f); // 角（頂点）までの距離
        var limit = Half - BoundaryInset - CornerClearance;
        var reach = vertex;
        foreach (var n in FaceNormals())
        {
            var toward = Vector2.Dot(n, u);
            if (toward <= 1e-4f)
            {
                continue;
            }

            var widest = 0f;
            foreach (var q in footprint)
            {
                widest = MathF.Max(widest, Vector2.Dot(n, q));
            }

            reach = MathF.Min(reach, (limit - widest) / toward);
        }

        reach = MathF.Max(0f, reach);
        return Center + new Vector3(u.X * reach, 0f, u.Y * reach);
    }

    /// <summary>場の面の外向きの法線（水平、X と Z）: 四角は ±X・±Z、檻は 30°・90°・150°・210°・270°・330°。</summary>
    private IEnumerable<Vector2> FaceNormals()
    {
        if (!IsCage)
        {
            yield return Vector2.UnitX;
            yield return -Vector2.UnitX;
            yield return Vector2.UnitY;
            yield return -Vector2.UnitY;
            yield break;
        }

        for (var k = 0; k < 6; k++)
        {
            var a = (MathF.PI / 6f) + (k * MathF.PI / 3f);
            yield return new Vector2(MathF.Cos(a), MathF.Sin(a));
        }
    }

    /// <summary>形の検査をして自分を返す（リングが小さすぎる・始めの間がリングに収まらない・形が未知の場合は例外）。</summary>
    public RingLayout Validated() =>
        Enum.IsDefined(Shape) && Size is >= 2.254f and <= 10.14f && StartGap > 0.2817f && StartGap <= Size - 0.8451f
            ? this
            : throw new ArgumentOutOfRangeException(nameof(Size), $"リングは 1 辺 2.25〜10.1 m、始めの間は 0.28 m より広くリングの縁から 42 cm ずつ内に収まること（{Size} m, {StartGap} m, {Shape}）。");
}
