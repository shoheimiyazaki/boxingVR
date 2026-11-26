namespace MiniRing.Arena;

/// <summary>
/// 練習の点から引く「形の代価」（2026-11-25、docs/plans/FORM_DECAY_STUDY.md）。練習（kickshin・clawlab・handlespar…）が当たった量だけを点にしていたので、
/// 手本の経路をねじる・体が追えない振り・刃を寝かせて叩く振り・疲れる振りに罰が無く、練習するほどフォームが崩れた。
/// 1 つの式で、どの練習も同じ重みを使う（同じ現象を道具ごとの式で書かない: CLAUDE.md §1.5）。項はどれも物理の量（距離・角・体が使ったスタミナ）の連続な関数で、
/// 技の種類で分けない（§1.3）。重みは「1 発のダメージ 10 前後」の点の単位で、各項の普通の大きさが 1 発の 1〜3 割になるように置いた。
/// </summary>
internal static class FormCost
{
    /// <summary>手本（教科書の経路）からずらした距離 1 cm あたりの代価。蹴りなら膝の制御点を、刃物なら刃先を、手本の経路から動かした量。</summary>
    public const double PerTextbookCentimetre = 0.15;

    /// <summary>体が目標の経路を追えなかった距離（RMS）1 cm あたりの代価。</summary>
    public const double PerTrackingCentimetre = 0.15;

    /// <summary>刃筋（刃先の動く向きが刃の面から外れた角、打ちの区間の平均）1° あたりの代価。刃の無い物（拳・脚・棒）は 0° として渡す。</summary>
    public const double PerEdgeDegree = 0.05;

    /// <summary>1 回の技で使ったスタミナ（0..1 の割合）あたりの代価。満タンから空まで（1）で 1 発のダメージの 3 倍ほど。</summary>
    public const double PerStamina = 30.0;

    /// <summary>
    /// 部位の仕事の偏り（体の部位ごとの仕事 / その部位の筋の蓄えの大きさ、の最大 / 平均 − 1。全身で均等なら 0、1 部位だけなら部位の数 − 1 = 4）1 あたりの代価。
    /// 2026-11-25（ガルヴァの全身で振る練習、huntlab --goal form、jin 種 1・20 s）: 腕だけで振る今の鎌の振り（腕の仕事 98%・偏り 1.69）で 1.0 点（1 発の 1 割）。
    /// </summary>
    public const double PerImbalance = 0.6;

    /// <summary>
    /// 先端の軌道の滑らかさ: 先端の加加速度（ジャーク）の RMS 100 m/s³ あたりの代価。2026-11-25（同じ測り）: 今の鎌の振り（RMS 15500 m/s³。当たって止まる歩が大きい）で
    /// 2.0 点（1 発の 2 割）。
    /// </summary>
    public const double PerHundredJerk = 0.013;

    /// <summary>
    /// 速さの不足: 先端の速さ（速さの 2 乗で重みを付けた平均）が <see cref="CuttingSpeed"/> に足りない 1 m/s あたりの代価。2026-11-25（同じ測り）: 今の鎌（16.8 m/s、
    /// 8.2 m/s 足りない）で 2.0 点（1 発の 2 割）。刃筋（41°・2.1 点）・1 振りの仕事（人の満タンの 3.7%・1.1 点）と同じ大きさにそろえた。
    /// </summary>
    public const double PerSlowMetrePerSecond = 0.25;

    /// <summary>
    /// 速さの物差し [m/s]: はめた鎌が刃の面の傾き 10° 以内で首を断つ速さ（`bladecut --blade claw`、docs/BLADE_CUT_STUDY.md・docs/BOT_PDCA.md「ガルヴァの狩り」）。
    /// </summary>
    public const double CuttingSpeed = 25.0;

    /// <summary>形の代価（大きいほど悪い）。どの量も負なら 0 として扱う（スタミナの回復を得点にしない）。</summary>
    /// <param name="textbookCentimetres">手本の経路からずらした距離 [cm]。</param>
    /// <param name="trackingCentimetres">体が目標の経路を追えなかった距離（RMS）[cm]。</param>
    /// <param name="edgeDegrees">刃筋の角 [°]。</param>
    /// <param name="staminaUsed">1 回の技で使ったスタミナ（満タン = 1）。</param>
    /// <param name="imbalance">部位の仕事の偏り（<see cref="PerImbalance"/>）。</param>
    /// <param name="jerk">先端のジャークの RMS [m/s³]。</param>
    /// <param name="speed">先端の速さ [m/s]（0 なら速さの項を数えない: 速さを測らない練習）。</param>
    public static double Of(double textbookCentimetres, double trackingCentimetres, double edgeDegrees, double staminaUsed,
        double imbalance = 0d, double jerk = 0d, double speed = 0d)
        => (PerTextbookCentimetre * Math.Max(0d, textbookCentimetres)) + (PerTrackingCentimetre * Math.Max(0d, trackingCentimetres))
           + (PerEdgeDegree * Math.Max(0d, edgeDegrees)) + (PerStamina * Math.Max(0d, staminaUsed))
           + (PerImbalance * Math.Max(0d, imbalance)) + (PerHundredJerk * Math.Max(0d, jerk) / 100d)
           + (speed > 0d ? PerSlowMetrePerSecond * Math.Max(0d, CuttingSpeed - speed) : 0d);

    /// <summary>
    /// 自然さの項の差し込み口（docs/plans/MOTION_TOOLS_PLAN.md §C7。まだどの練習も使っていない: 採るかは練習ごとに比べ直してから）。
    /// 運動連鎖の順の乱れ 1（<see cref="MotionTools.MotionMetrics.ChainOrder"/> の 1 − 割合）あたりと、無次元の躍度が最小躍度（720）の何桁上か 1 桁あたりの代価。
    /// </summary>
    public const double PerChainDisorder = 1.0;

    public const double PerJerkDecade = 0.5;

    /// <summary>自然さの代価（大きいほど悪い）。<paramref name="chainOrder"/> は 0..1（1 が近い所から順）、<paramref name="normalizedJerk"/> は無次元の躍度。</summary>
    public static double OfNaturalness(double chainOrder, double normalizedJerk)
        => (PerChainDisorder * Math.Clamp(1d - (double.IsNaN(chainOrder) ? 1d : chainOrder), 0d, 1d))
           + (double.IsFinite(normalizedJerk) && normalizedJerk > 720d ? PerJerkDecade * Math.Log10(normalizedJerk / 720d) : 0d);
}
