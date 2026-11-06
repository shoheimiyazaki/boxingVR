namespace MiniRing;

public enum HitZone
{
    Head,
    Body,
    Limb,
}

/// <summary>
/// 打撃ダメージの純関数。Triage BrainDamageCalc の「(速度 − しきい値)² × 倍率 × 部位係数」を流用
/// （速度は 60cm 体格のスケール）。
/// </summary>
public static class PunchMath
{
    public const float SpeedThreshold = 0.7f;
    public const float DamageMultiplier = 12f;
    public const float MaxDamage = 35f;
    public const float KnockdownDamage = 18f;
    public const float StunPerDamage = 1f / 30f;

    public static float ZoneCoefficient(HitZone zone) => zone switch
    {
        HitZone.Head => 1.0f,
        HitZone.Body => 0.5f,
        _ => 0.15f,
    };

    public static float Damage(float impactSpeed, HitZone zone)
    {
        var over = impactSpeed - SpeedThreshold;
        return over <= 0f ? 0f : MathF.Min(MaxDamage, over * over * DamageMultiplier * ZoneCoefficient(zone));
    }

    public static float Stun(float damage) => Math.Clamp(damage * StunPerDamage, 0f, 1f);

    public static bool IsKnockdown(float damage) => damage >= KnockdownDamage;
}

/// <summary>1 ヒットの結果（HUD・効果音・ハプティクス用）。</summary>
public readonly record struct HitEvent(Fighter Attacker, Hand Hand, Fighter Victim, HitZone Zone, System.Numerics.Vector3 Point, float Speed, float Damage);
