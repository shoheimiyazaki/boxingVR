using System.Numerics;

namespace MiniRing;

/// <summary>
/// 試合の場の大きさ: リングの 1 辺と、ラウンドの始めに 2 人が立つ間（足と足の距離）。リングを載せるローテーブルと台座はリングに合わせて広がり
/// （リングの縁から台座の縁まで 8 cm、天板の縁まで 15 cm）、テーブルの手前の縁はいつもプレイヤーの立つ所（原点）から 20 cm 先にある。
/// </summary>
/// <param name="Size">リングの 1 辺 [m]。</param>
/// <param name="StartGap">ラウンドの始めの 2 人の足と足の距離 [m]。</param>
public sealed record RingLayout(float Size, float StartGap)
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

    /// <summary>リングの半分の幅 [m]。</summary>
    public float Half => Size * 0.5f;

    /// <summary>ローテーブルの天板の半分の幅 [m]（正方形）。</summary>
    public float TableHalf => Half + 0.15f;

    /// <summary>箱庭の台座の半分の幅 [m]（正方形）。</summary>
    public float PlinthHalf => Half + 0.08f;

    /// <summary>リング（とテーブル）の中心（床の上）。</summary>
    public Vector3 Center => new(0f, 0f, NearEdge + TableHalf);

    /// <summary>形の検査をして自分を返す（リングが小さすぎる・始めの間がリングに収まらない場合は例外）。</summary>
    public RingLayout Validated() =>
        Size is >= 0.8f and <= 3.6f && StartGap > 0.1f && StartGap <= Size - 0.3f
            ? this
            : throw new ArgumentOutOfRangeException(nameof(Size), $"リングは 1 辺 0.8〜3.6 m、始めの間は 0.1 m より広くリングの縁から 15 cm ずつ内に収まること（{Size} m, {StartGap} m）。");
}
