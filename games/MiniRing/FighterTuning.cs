using System.Numerics;
using Aigis.Physics;

namespace MiniRing;

/// <summary>
/// ファイター（約 60cm のアクティブラグドール）の調整値。
/// <para>
/// 体を保つ力は「骨盤ホバー PD + 直立 PD + 水平速度 P」（Unity 版 Triage の ActiveRagdollMotor 方式）。
/// ゲインは加速度単位（1/s², 1/s）で持ち、実行時に質量・慣性を掛けて力積にする — 体格を変えても挙動が保たれる。
/// 関節は Bepu の AngularServo / TwistServo（Hz・減衰比・力上限＝バネ・ダンパ）で駆動する。
/// Bepu の SpringSettings は換算質量に対して定義される（Aigis anti-pattern A12）ので、Unity の N/m 値は移植しない。
/// </para>
/// </summary>
public sealed record FighterTuning
{
    /// <summary>Aigis の既定体格（約 1.7m）に対する縮尺。0.355 ≒ 60cm。</summary>
    public float Scale { get; init; } = 0.355f;

    /// <summary>既定質量（約 67.6kg）への倍率。0.1 ≒ 6.8kg。</summary>
    public float MassScale { get; init; } = 0.1f;

    public float HeadScale { get; init; } = 1.15f;

    /// <summary>腕の長さ方向の倍率。Aigis の関節の重なり代（Overlap 3cm 固定）が 60cm 体格では腕を大きく削るので、その分を補う。</summary>
    public float ArmLengthScale { get; init; } = 1.4f;
    public float GloveRadius { get; init; } = 0.024f;

    // ── 体幹（加速度単位）
    public float StandHeightRatio { get; init; } = 0.97f;
    public float HoverKp { get; init; } = 100f;
    public float HoverKd { get; init; } = 14f;
    public float MaxHoverAccel { get; init; } = 2.2f * 9.81f;
    public float UprightKp { get; init; } = 600f;
    public float UprightKd { get; init; } = 40f;
    public float MaxUprightAccel { get; init; } = 1500f;
    public float TurnSpeedRadians { get; init; } = 2f * MathF.PI;

    // ── 移動・脚
    public float WalkSpeed { get; init; } = 0.55f;
    public float MoveGain { get; init; } = 10f;
    public float MaxMoveAccel { get; init; } = 6f;
    public float StrideLength { get; init; } = 0.16f;
    public float NeutralHipRadians { get; init; } = -8f * MathF.PI / 180f;
    public float NeutralKneeRadians { get; init; } = 14f * MathF.PI / 180f;
    public float GaitHipSwingRadians { get; init; } = 30f * MathF.PI / 180f;
    public float GaitKneeLiftRadians { get; init; } = 40f * MathF.PI / 180f;
    public float LegServoHz { get; init; } = 12f;
    public float NeckServoHz { get; init; } = 8f;

    // ── 腕（サーボ + 拳への PD 力）
    public float ArmServoHz { get; init; } = 10f;
    public float ArmServoMaxSpeed { get; init; } = 40f;
    public float HandKp { get; init; } = 420f;
    public float HandKd { get; init; } = 32f;
    public float MaxHandForce { get; init; } = 35f;
    public float HandReactionShare { get; init; } = 0.5f;
    public float MaxTargetSpeed { get; init; } = 3f;
    public float TargetVelocitySmoothing { get; init; } = 25f;

    // ── 掴み・投げ
    public float GrabRadius { get; init; } = 0.04f;
    public float GrabBreakForce { get; init; } = 260f;
    public float ThrowSpeed { get; init; } = 1.1f;
    public float ThrowAssist { get; init; } = 0.35f;
    public float GrabbedDriveScale { get; init; } = 0.55f;

    // ── 状態
    public float FallUprightness { get; init; } = 0.4f;
    public float FallGrace { get; init; } = 0.15f;
    public float CollapseDuration { get; init; } = 0.3f;
    public float DownDriveScale { get; init; } = 0.05f;
    public float DownTime { get; init; } = 1.6f;
    public float GetUpDuration { get; init; } = 1.0f;
    public float RecoverUprightness { get; init; } = 0.78f;
    public float StunDecayPerSecond { get; init; } = 1.2f;
    public float StunnedDriveScale { get; init; } = 0.3f;
    public float NoGroundFallTime { get; init; } = 0.4f;

    /// <summary>関節の力上限の倍率。トルク ∝ 質量 × 長さ なので MassScale × Scale。</summary>
    public float TorqueScale => MassScale * Scale;

    public HumanoidRagdoll.HalfExtents HalfExtents
    {
        get
        {
            var d = HumanoidRagdoll.HalfExtents.Default;
            return new HumanoidRagdoll.HalfExtents(
                d.Pelvis * Scale, d.Chest * Scale, d.Head * (Scale * HeadScale),
                d.UpperArm * Scale * new Vector3(ArmLengthScale, 1f, 1f), d.Forearm * Scale * new Vector3(ArmLengthScale, 1f, 1f),
                d.Thigh * Scale, d.Shin * Scale, d.Foot * Scale);
        }
    }

    public HumanoidRagdoll.MassDistribution Masses
    {
        get
        {
            var m = HumanoidRagdoll.MassDistribution.Default;
            return m with
            {
                Pelvis = m.Pelvis * MassScale, Chest = m.Chest * MassScale, Head = m.Head * MassScale,
                UpperArm = m.UpperArm * MassScale, Forearm = m.Forearm * MassScale, Thigh = m.Thigh * MassScale,
                Shin = m.Shin * MassScale, Foot = m.Foot * MassScale,
            };
        }
    }

    public static FighterTuning Default { get; } = new();

    internal static Vector3 Up => Vector3.UnitY;
}
