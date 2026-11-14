using System.Numerics;

namespace MiniRing.Bots;

public enum KickTechnique { Front, Low, Middle, High, Knee }

/// <summary>蹴りも筋肉が追う目標の経路。威力の倍率を持たず、質量・速さ・接触する骨と皮膚が結果を決める。</summary>
public readonly record struct KickSpec(KickTechnique Technique, float Duration, float Lateral, float ChamberHeight, float Overshoot)
{
    public const float ChamberShare = 0.28f;
    public const float ContactShare = 0.53f;

    public static KickSpec Of(KickTechnique technique) => technique switch
    {
        KickTechnique.Front => new(technique, 0.80f, 0f, 0.52f, 0.14f),
        KickTechnique.Low => new(technique, 0.85f, 0.65f, 0.38f, 0.16f),
        KickTechnique.Middle => new(technique, 0.95f, 0.72f, 0.60f, 0.15f),
        KickTechnique.High => new(technique, 1.12f, 0.78f, 0.82f, 0.12f),
        KickTechnique.Knee => new(technique, 0.75f, 0f, 0.56f, 0.08f),
        _ => throw new ArgumentOutOfRangeException(nameof(technique)),
    };

    /// <summary>
    /// その蹴りで当てたい点（ワールド）。足は腕より短いので、高い所ほど近づかないと届かない: 前蹴り・膝は腹（相手の腰）、ローは腿
    /// （腰の下）、ミドルは胸、ハイは頭。威力は変えない（どこへ向かう経路かだけ）。
    /// </summary>
    public Vector3 AimWorld(in FighterSense sense)
    {
        var pelvis = sense.OpponentBody == default ? sense.OpponentChest - new Vector3(0f, 0.5f * sense.ArmReach, 0f) : sense.OpponentBody.Pelvis;
        return Technique switch
        {
            KickTechnique.High => sense.OpponentHead,
            KickTechnique.Middle => sense.OpponentChest,
            KickTechnique.Low => pelvis - new Vector3(0f, 0.5f * sense.ArmReach, 0f),
            _ => pelvis,
        };
    }

    public LegCommand Evaluate(Hand leg, float time, float duration, Vector3 rest, Vector3 aim, float reach)
    {
        if (!float.IsFinite(time) || !float.IsFinite(duration) || duration <= 0f || !float.IsFinite(reach) || reach <= 0f)
        {
            throw new ArgumentOutOfRangeException(nameof(duration));
        }

        var t = Math.Clamp(time / duration, 0f, 1f);
        var chamber = rest + new Vector3(leg.LocalSide() * Lateral * reach, ChamberHeight * reach, 0.10f * reach);
        var contact = aim + new Vector3(0f, 0f, Overshoot * reach);
        // 膝打ちは足首を引き付けたまま膝を狙いへ運ぶ。IK の膝方向も同じ狙いを向く。
        if (Technique == KickTechnique.Knee)
        {
            contact = aim + new Vector3(0f, -0.30f * reach, -0.20f * reach);
        }

        var target = t <= ChamberShare ? Vector3.Lerp(rest, chamber, Smooth(t / ChamberShare))
            : t <= ContactShare ? Vector3.Lerp(chamber, contact, Smooth((t - ChamberShare) / (ContactShare - ChamberShare)))
            : t <= 0.76f ? Vector3.Lerp(contact, chamber, Smooth((t - ContactShare) / (0.76f - ContactShare)))
            : Vector3.Lerp(chamber, rest, Smooth((t - 0.76f) / 0.24f));
        var knee = Technique == KickTechnique.Knee ? aim : chamber + new Vector3(0f, 0.2f * reach, 0.15f * reach);
        var pitch = Technique == KickTechnique.Front ? -0.45f : Technique == KickTechnique.Knee ? 0.9f : 0.25f;
        return new LegCommand(target, knee, Quaternion.CreateFromAxisAngle(Vector3.UnitX, pitch));
    }

    private static float Smooth(float t) => t * t * (3f - (2f * t));
}
