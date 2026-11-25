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

    /// <summary>形の代価（大きいほど悪い）。どの量も負なら 0 として扱う（スタミナの回復を得点にしない）。</summary>
    public static double Of(double textbookCentimetres, double trackingCentimetres, double edgeDegrees, double staminaUsed)
        => (PerTextbookCentimetre * Math.Max(0d, textbookCentimetres)) + (PerTrackingCentimetre * Math.Max(0d, trackingCentimetres))
           + (PerEdgeDegree * Math.Max(0d, edgeDegrees)) + (PerStamina * Math.Max(0d, staminaUsed));
}
