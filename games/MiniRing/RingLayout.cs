using System.Numerics;

namespace MiniRing;

/// <summary>
/// 試合の場の大きさ: リングの 1 辺と、ラウンドの始めに 2 人が立つ所（両者のコーナー、またはドリル用に中央で離れる距離）。リングを載せるローテーブルと台座はリングに合わせて広がり
/// （リングの縁から台座の縁まで 8 cm、天板の縁まで 15 cm）、テーブルの手前の縁はいつもプレイヤーの立つ所（原点）から 20 cm 先にある。
/// </summary>
/// <param name="Size">リングの 1 辺 [m]。</param>
/// <param name="StartGap">コーナーから始めない（<paramref name="FromCorners"/> が偽の）とき、ラウンドの始めの 2 人の足と足の距離 [m]。</param>
/// <param name="FromCorners">ラウンドを両者のコーナー（赤は手前左、青は奥右）から始めるか（2026-11-18、ユーザーの指定「試合の開始はコーナーから」）。
/// 試合のホスト（デスクトップ・VR・動画・テレビ中継）は <see cref="BoxingCorners.StartingAtCorners"/> で真にする。偽（既定）なら中央で <paramref name="StartGap"/> だけ
/// 離れて向き合う（技のドリル・調べもの・リーグ・テストで、すぐに打ち合う形。今までの記録とも同じ）。</param>
public sealed record RingLayout(float Size, float StartGap, bool FromCorners = false)
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

    /// <summary>リングの半分の幅 [m]。</summary>
    public float Half => Size * 0.5f;

    /// <summary>ローテーブルの天板の半分の幅 [m]（正方形）。</summary>
    public float TableHalf => Half + 0.15f;

    /// <summary>箱庭の台座の半分の幅 [m]（正方形）。</summary>
    public float PlinthHalf => Half + 0.08f;

    /// <summary>リング（とテーブル）の中心（床の上）。</summary>
    public Vector3 Center => new(0f, 0f, NearEdge + TableHalf);

    /// <summary>同じリングで、両者のコーナーから始める形。</summary>
    public RingLayout AtCorners() => this with { FromCorners = true }; // メソッド: 記録の JSON に入れ子で書かれないよう

    /// <summary>赤（手前左）・青（奥右）のコーナーの立つ所（床の上）。<paramref name="inset"/> はリングの縁から内側への距離 [m]。</summary>
    public Vector3 Corner(bool red, float inset = CornerInset)
    {
        var d = Half - inset;
        return Center + (red ? new Vector3(-d, 0f, -d) : new Vector3(d, 0f, d));
    }

    /// <summary>
    /// ニュートラルコーナー（どちらの持ち場でもない左奥・右手前）のうち、<paramref name="awayFrom"/> から遠い方の立つ所（床の上）。
    /// ダウンを取った側が、数えている間に下がって待つ所（ボクシングの決まり: 倒れた相手からいちばん遠いニュートラルコーナー）。
    /// </summary>
    public Vector3 NeutralCorner(Vector3 awayFrom, float inset = CornerInset)
    {
        var d = Half - inset;
        var a = Center + new Vector3(-d, 0f, d);
        var b = Center + new Vector3(d, 0f, -d);
        static float Flat(Vector3 p, Vector3 q) => new Vector2(p.X - q.X, p.Z - q.Z).LengthSquared();
        return Flat(a, awayFrom) >= Flat(b, awayFrom) ? a : b;
    }

    /// <summary>形の検査をして自分を返す（リングが小さすぎる・始めの間がリングに収まらない場合は例外）。</summary>
    public RingLayout Validated() =>
        Size is >= 0.8f and <= 3.6f && StartGap > 0.1f && StartGap <= Size - 0.3f
            ? this
            : throw new ArgumentOutOfRangeException(nameof(Size), $"リングは 1 辺 0.8〜3.6 m、始めの間は 0.1 m より広くリングの縁から 15 cm ずつ内に収まること（{Size} m, {StartGap} m）。");
}
