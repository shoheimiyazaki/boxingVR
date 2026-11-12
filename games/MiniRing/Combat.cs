using System.Numerics;

namespace MiniRing;

/// <summary>
/// 1 ヒットの結果（HUD・効果音・ハプティクス用）。
/// <paramref name="ImpactMass"/> は拳に乗った質量 [kg]、<paramref name="BodyShare"/> はそのうち体から来た割合（0 = 手打ち、1 に近いほど体重が乗った）。
/// <paramref name="Face"/> は打った面の材質（<see cref="Voxels.BoxerAnatomy"/> の番号: グローブの革・素手の皮膚・鉄。0 = 分からない）。打撃音の種類に使う。
/// </summary>
public readonly record struct HitEvent(
    Fighter Attacker, Hand Hand, Fighter Victim, HitZone Zone, Vector3 Point,
    float Speed, float ImpactMass, float BodyShare, float Damage, byte Face = 0)
{
    public float Momentum => ImpactMass * Speed;
}

/// <summary>部位ごとの効き目（ダメージの倍率）。ダメージは記録と音にだけ使い、動きは落とさない。</summary>
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
}
