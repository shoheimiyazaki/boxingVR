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
/// 試合の場の大きさ: リングの 1 辺と、ラウンドの始めに 2 人が立つ間（足と足の距離）。リングを載せるローテーブルと台座はリングに合わせて広がり
/// （リングの縁から台座の縁まで 8 cm、天板の縁まで 15 cm）、テーブルの手前の縁はいつもプレイヤーの立つ所（原点）から 20 cm 先にある。
/// 檻（<see cref="ArenaShape.HexCage"/>）のときの <paramref name="Size"/> は向かい合う辺と辺の距離（内接円の直径）。
/// </summary>
/// <param name="Size">リングの 1 辺 [m]（檻は向かい合う辺と辺の距離 [m]）。</param>
/// <param name="StartGap">ラウンドの始めの 2 人の足と足の距離 [m]。</param>
/// <param name="Shape">場の形（既定は四角いリング）。</param>
public sealed record RingLayout(float Size, float StartGap, ArenaShape Shape = ArenaShape.RopedSquare)
{
    /// <summary>テーブルの手前の縁から、プレイヤーの立つ所（原点）までの距離 [m]。</summary>
    private const float NearEdge = 0.2f;

    /// <summary>
    /// 拳の試合: 1.2 m のリング（身長 60 cm の人形の 2 倍）で、44 cm 離れて始める（拳の間合いの少し外）。
    /// </summary>
    public static RingLayout Boxing { get; } = new(1.2f, 0.44f);

    /// <summary>
    /// 武器同士の試合: 2.4 m のリング（人形の身長の 4 倍。人なら 7 m 弱で、剣道の試合場 9〜11 m 四方をテーブルに載る大きさに縮めた）と、
    /// 1.0 m 離れて始める（剣道の開始線は 2 人の間 2.8 m、身長 1.7 m の 1.65 倍。人形では 1.0 m: どの技の間合い（34〜80 cm）よりも遠い遠間）。
    /// 下がって外し、また踏み込む間合いの取り合いができる広さ。
    /// </summary>
    public static RingLayout Weapons { get; } = new(2.4f, 1.0f);

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
