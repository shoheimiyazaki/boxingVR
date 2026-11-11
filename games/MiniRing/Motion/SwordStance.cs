namespace MiniRing.Motion;

/// <summary>両手で剣を握って戦う体の調整。</summary>
public static class SwordStance
{
    /// <summary>
    /// 剣の構え: 足は剣道の構えに近く、右足を半歩前（ボクサーの構えより前後も左右も狭い）、両足のつま先は前、後ろ足のかかとを少し上げる。
    /// 両手で柄を締めた手首は、片手で棍棒を握るより固い（30 Hz。剣の向きの遅れが半分ほどになる）。
    /// </summary>
    public static FighterTuning Tuning { get; } = FighterTuning.Default with
    {
        WristServoHz = 30f,
        Footwork = FighterTuning.Default.Footwork with { StanceLength = 0.38f, StanceWidth = 0.22f, LeadYawDegrees = 0f, RearYawDegrees = -6f, HeelRaiseDegrees = 18f },
    };

    /// <summary>
    /// 動きの再生の速さ: 人形の体の大きさは人のおよそ 0.32 倍（腕の長さの比）。同じ重力の下で体が釣り合う動きの速さは大きさの √ に比例するので、
    /// 人の動きを 1/√0.32 ≈ 1.77 倍の速さでなぞる。
    /// </summary>
    public const float Rate = 1.77f;
}
