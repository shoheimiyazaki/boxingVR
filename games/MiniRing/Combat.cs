using System.Numerics;

namespace MiniRing;

/// <summary>
/// 1 ヒットの結果（HUD・効果音・ハプティクス用）。
/// <paramref name="ImpactMass"/> は拳に乗った質量 [kg]、<paramref name="BodyShare"/> はそのうち体から来た割合（0 = 手打ち、1 に近いほど体重が乗った）。
/// </summary>
public readonly record struct HitEvent(
    Fighter Attacker, Hand Hand, Fighter Victim, HitZone Zone, Vector3 Point,
    float Speed, float ImpactMass, float BodyShare, float Damage)
{
    public float Momentum => ImpactMass * Speed;
}

/// <summary>部位ごとの効き目（ダメージの倍率）。どの能力が落ちるかは <see cref="BodyCondition.ApplyHit"/>。</summary>
public static class ZoneDamage
{
    public static float Multiplier(HitZone zone) => zone switch
    {
        HitZone.Chin => 1.3f,
        HitZone.Temple => 1.15f,
        HitZone.Head => 1.0f,
        HitZone.Neck => 1.1f,
        HitZone.Heart => 1.0f,
        HitZone.Liver => 1.1f,
        HitZone.SolarPlexus => 0.9f,
        HitZone.Body => 0.7f,
        HitZone.Shoulder => 0.6f,
        HitZone.Elbow => 0.5f,
        HitZone.Guard => 0.25f,
        HitZone.Knee => 0.8f,
        _ => 0.4f,
    };

    /// <summary>この一撃でダウンするか（顎・こめかみは軽めでも倒れる）。</summary>
    public static bool IsKnockdown(HitZone zone, float damage)
        => damage * (zone is HitZone.Chin or HitZone.Temple ? 1.5f : 1f) >= 20f;

    /// <summary>よろめき（0..1）。</summary>
    public static float Stun(float damage) => Math.Clamp(damage / 30f, 0f, 1f);
}
