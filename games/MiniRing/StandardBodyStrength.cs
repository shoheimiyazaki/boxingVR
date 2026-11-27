namespace MiniRing;

/// <summary>
/// 基準の体の筋の値の置き直し（2026-11-27、docs/plans/BASE_BODY_PLAN.md §8.7）: 基準の体を 145 cm・67.6 kg（部位の質量は Aigis の既定）から 175 cm・74.8 kg
/// （ボクセルの実測）へ移したとき、関節ごとの筋の最大トルクを「その関節が支える質量 × てこの長さ」の比 m_new·L_new / m_old·L_old で掛け直す。
/// 試合の手触り（重さとてこに対する筋の強さの割合）を前の基準の体と同じに保つための、基準の体の筋の値そのものの置き直しで、見えない倍率ではない。
/// 体格の違い（§1.7: 断面 × てこ）は、この新しい基準の体に対する比で効く（<see cref="BodyBuild.SectionRatio"/>・<see cref="BodyBuild.LegTorqueRatio"/>）。
/// <para>
/// 前の基準（質量 kg・長さ m）: 骨盤 10・胴 20・頭 5・上腕 2・前腕 1.3・もも 8・すね 4・足 1、グローブ 0.48（今は 0.61: 中の手の骨・肉が実物の密度になった）、骨盤の高さ 0.18・胴 0.36・頭の半分 0.096・
/// 上腕 0.307・前腕 0.283・もも 0.40・すね 0.38・足の長さ 0.26・肩の半幅 0.15。新しい基準は <see cref="FighterTuning.StandardHalfExtents"/> と
/// <see cref="FighterTuning.Masses"/>（<c>StandardBodyStrengthTests</c> が下の値を今の基準の体から計算し直して確かめる）。
/// </para>
/// </summary>
public static class StandardBodyStrength
{
    /// <summary>股: 股より上（骨盤・胴・頭・両腕 41.6 → 44.6 kg）× 股から胴の重心（骨盤の高さ + 胴の半分 0.36 → 0.44 m）。</summary>
    public const float Hip = 1.314f;

    /// <summary>膝: 膝より上（股より上 + 両もも 57.6 → 65.2 kg）× ももの長さ（0.40 → 0.46 m）。</summary>
    public const float Knee = 1.302f;

    /// <summary>足首: 足より上（65.6 → 71.6 kg）× すね + もも（0.78 → 0.91 m）。</summary>
    public const float Ankle = 1.272f;

    /// <summary>つま先（母指球）: 体全体（67.6 → 74.8 kg）× 足の長さ（0.26 → 0.27 m）。</summary>
    public const float Toe = 1.150f;

    /// <summary>胴の関節の筋・回旋筋: 胴と頭と両腕（31.6 → 35.8 kg）× 胴の高さ（0.36 → 0.4825 m）。</summary>
    public const float Trunk = 1.520f;

    /// <summary>首: 頭（5 → 4.57 kg）× 頭の高さの半分（0.096 → 0.114 m）。</summary>
    public const float Neck = 1.083f;

    /// <summary>肩: 腕とグローブ（3.78 → 3.66 kg）× 腕の長さ（0.590 → 0.641 m）。</summary>
    public const float Shoulder = 1.052f;

    /// <summary>肘: 前腕とグローブ（1.78 → 1.70 kg）× 前腕の長さ（0.283 → 0.285 m）。手はボクセルに無く前腕が軽くなった分、肘は少し弱い。</summary>
    public const float Elbow = 0.962f;

    // 部位の筋の量の置き直し（2026-11-27）: 酸素の蓄えと血管（Fighter.Stamina）は部位の質量 × 1 kg あたりで決まるが、仕事をするのは筋で、
    // 筋の体積 = 断面 × 筋の長さ。断面は最大トルク ÷ てこ、筋の長さはてこに比例する（相似）ので、筋の体積は最大トルクに比例する。
    // 上のトルクの置き直しと同じ比で筋の量を置き直す（部位の質量の比で割った分が、部位の質量に掛ける筋の割合の置き直し）。
    // 置き直さないと、強くなった筋の仕事が前の量の蓄えから出て、片手の強い拳を 1 秒ごとに 50 秒で胴の蓄えが尽きた（StaminaRegionProbe: 胴 112 W 対 前 70 W）。

    /// <summary>腕の筋の量: 肩の比 ÷ 腕の質量の比（上腕 + 前腕 3.30 → 3.05 kg）。</summary>
    public const float ArmMuscle = 1.138f;

    /// <summary>胴の筋の量: 胴の比 ÷ 胴の質量の比（骨盤 + 胴 30 → 33.9 kg）。</summary>
    public const float TrunkMuscle = 1.344f;

    /// <summary>脚の筋の量: 股・膝・足首の比の平均 ÷ 片脚の質量の比（もも + すね + 足 13 → 15.1 kg）。</summary>
    public const float LegMuscle = 1.113f;
}
