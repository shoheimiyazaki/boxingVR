namespace MiniRing.Motion;

/// <summary>両手で剣を握って戦う体の調整。</summary>
public static class SwordStance
{
    /// <summary>
    /// 剣の構え: 足は剣道の構えに近く、右足を半歩前（ボクサーの構えより前後も左右も狭い）、両足のつま先は前、後ろ足のかかとを少し上げる。
    /// 両手で柄を締めた手首の固さは、試合の体と同じ握り込んだ手首（<see cref="FighterTuning.GripWristServoHz"/>、Fighter の Arm.Clenched）。
    /// 2026-11-25 まではここだけ 17.9 Hz にしていた（ドリルと試合で手首が違った）。
    /// </summary>
    public static FighterTuning Tuning { get; } = FighterTuning.Default with
    {
        Footwork = FighterTuning.Default.Footwork with { StanceLength = 0.38f, StanceWidth = 0.22f, LeadYawDegrees = 0f, RearYawDegrees = -6f, HeelRaiseDegrees = 18f },
    };

    /// <summary>
    /// 動きの再生の速さ: 型・モーションキャプチャの時刻は録った人の秒。人形の単位では、人形の体（腕の長さの比で人の 0.32 倍）に合わせて
    /// 1/√0.32 ≈ 1.77 倍の速さでなぞっていた。実物大の物理（時間 × √(1/0.355) = 1.678）でも同じ動きを保つため 1.77 / 1.678 ≈ 1.055 倍
    /// （≈ √(0.355 / 0.32): 腕の長さの比 0.32 と体の縮尺 0.355 の違いが残る分。型・キャプチャのキーの時刻は換算しない: 時間の換算はこの 1 つに集める）。
    /// </summary>
    public const float Rate = 1.055f;
}
