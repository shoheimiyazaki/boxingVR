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
/// （リングの縁から台座の縁まで 8 cm、天板の縁まで 15 cm）、テーブルの手前の縁はいつもプレイヤーの立つ所（原点）から 20 cm 先にある。
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
    private const float NearEdge = 0.2f;

    /// <summary>
    /// 試合の標準のリングの 1 辺 [m]（拳も武器も同じ）: 人の大きさで 5.3 m を、人形の縮尺（<see cref="FighterTuning.Scale"/> 0.355: 1.7 m の人が 60 cm）で縮めた 1.88 m。
    /// 2026-11-18、ユーザーの指定: 動き回れるようにまず 7.3 m（プロのリングのロープの内側の上限、2.59 m）にしたが大きすぎたので、
    /// 以前の 1.2 m（人なら 3.4 m）との中間（1.89 m）にした。
    /// </summary>
    public const float StandardSize = 5.3f * 0.355f;

    /// <summary>ラウンドの始めに立つコーナーの、リングの縁からの内側の距離 [m]（ロープとコーナーポストに触れない所）。</summary>
    public const float CornerInset = 0.1f;

    /// <summary>
    /// 拳の試合: 標準のリング（<see cref="StandardSize"/>）。コーナーから始めない形（ドリル）では 44 cm 離れて始める（拳の間合いの少し外）。
    /// （2026-11-18 まで 1.2 m: 人形の身長の 2 倍、人なら 3.4 m で、下がる余地が少なかった。）
    /// </summary>
    public static RingLayout Boxing { get; } = new(StandardSize, 0.44f);

    /// <summary>
    /// 武器同士の試合: 拳の試合と同じ標準のリング（2026-11-18、ユーザーの指定。それまでは 2.4 m: 剣道の試合場 9〜11 m 四方をテーブルに載る大きさに縮めた値）。
    /// コーナーから始めない形では 1.0 m 離れて始める（剣道の開始線は 2 人の間 2.8 m、身長 1.7 m の 1.65 倍。
    /// 人形では 1.0 m: どの技の間合い（34〜80 cm）よりも遠い遠間）。
    /// </summary>
    public static RingLayout Weapons { get; } = new(StandardSize, 1.0f);

    /// <summary>
    /// ドリル・調べもの・リーグ・テストの場（リングを指定しないで作った試合）: 2026-11-18 まで拳の試合のリングだった 1.2 m（人形の身長の 2 倍、人なら 3.4 m）で、
    /// 中央で 44 cm 離れて始める。技を 1 つずつ磨くドリルと、その記録（docs/arena）・テストの数値はこの場で測ってきたので、比べられるよう残す。
    /// </summary>
    public static RingLayout DrillBoxing { get; } = new(1.2f, 0.44f);

    /// <summary>武器同士のドリル・調べものの場: 2026-11-18 まで武器の試合のリングだった 2.4 m で、中央で 1.0 m 離れて始める。</summary>
    public static RingLayout DrillWeapons { get; } = new(2.4f, 1.0f);

    /// <summary>
    /// 拳の試合の檻: 向かい合う辺の距離 1.3 m の正六角形（面積 ≈ 1.46 m²。四角い 1.2 m の 1.44 m² と同じ広さ）、始めの間は拳のリングと同じ 44 cm。
    /// </summary>
    public static RingLayout Cage { get; } = new(1.3f, 0.44f, ArenaShape.HexCage);

    /// <summary>
    /// 武器の試合の檻: 向かい合う辺の距離 2.6 m の正六角形（面積 ≈ 5.85 m²。四角い武器のリング 2.4 m の 5.76 m² と同じ広さ）、始めの間は 1.0 m。
    /// </summary>
    public static RingLayout WeaponsCage { get; } = new(2.6f, 1.0f, ArenaShape.HexCage);

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
    public float TableHalf => OuterHalf + 0.15f;

    /// <summary>箱庭の台座の半分の幅 [m]（正方形）。</summary>
    public float PlinthHalf => OuterHalf + 0.08f;

    /// <summary>リング（とテーブル）の中心（床の上）。</summary>
    public Vector3 Center => new(0f, 0f, NearEdge + TableHalf);

    /// <summary>同じリングで、両者のコーナーから始める形。</summary>
    public RingLayout AtCorners() => this with { FromCorners = true }; // メソッド: 記録の JSON に入れ子で書かれないよう

    /// <summary>
    /// ニュートラルコーナー（どちらの持ち場でもない角。四角は左奥・右手前、檻は 120°・300° の頂点）のうち、<paramref name="awayFrom"/> から遠い方の立つ所（床の上）。
    /// ダウンを取った側が、数えている間に下がって待つ所（ボクシングの決まり: 倒れた相手からいちばん遠いニュートラルコーナー）。
    /// </summary>
    public Vector3 NeutralCorner(Vector3 awayFrom, float inset = CornerInset)
    {
        Vector3 a, b;
        if (IsCage)
        {
            var radius = Circumradius - inset;
            a = Center + new Vector3(radius * MathF.Cos(MathF.PI * 2f / 3f), 0f, radius * MathF.Sin(MathF.PI * 2f / 3f));
            b = Center + new Vector3(radius * MathF.Cos(MathF.PI * 5f / 3f), 0f, radius * MathF.Sin(MathF.PI * 5f / 3f));
        }
        else
        {
            var d = Half - inset;
            a = Center + new Vector3(-d, 0f, d);
            b = Center + new Vector3(d, 0f, -d);
        }

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
    /// 休むコーナー（床の上の位置）: 四角は赤が (−X, −Z)・青が (+X, +Z) の角、檻は赤が 240°・青が 60° の頂点（X 軸から Z 軸の向きへ測る）。
    /// どちらも縁から <paramref name="inset"/> だけ内側（四角は両辺から、檻は頂点から中心へ向かって）。
    /// </summary>
    public Vector3 RestCorner(bool red, float inset)
    {
        if (!IsCage)
        {
            var edge = Half - inset;
            return Center + (red ? new Vector3(-edge, 0f, -edge) : new Vector3(edge, 0f, edge));
        }

        var radius = Circumradius - inset;
        var angle = (red ? 240f : 60f) * (MathF.PI / 180f);
        return Center + new Vector3(radius * MathF.Cos(angle), 0f, radius * MathF.Sin(angle));
    }

    /// <summary>形の検査をして自分を返す（リングが小さすぎる・始めの間がリングに収まらない・形が未知の場合は例外）。</summary>
    public RingLayout Validated() =>
        Enum.IsDefined(Shape) && Size is >= 0.8f and <= 3.6f && StartGap > 0.1f && StartGap <= Size - 0.3f
            ? this
            : throw new ArgumentOutOfRangeException(nameof(Size), $"リングは 1 辺 0.8〜3.6 m、始めの間は 0.1 m より広くリングの縁から 15 cm ずつ内に収まること（{Size} m, {StartGap} m, {Shape}）。");
}
