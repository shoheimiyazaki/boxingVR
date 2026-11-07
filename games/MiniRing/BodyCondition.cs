namespace MiniRing;

/// <summary>
/// 受けたダメージの記録（部位ごとの累計・純ロジック）。
/// <para>
/// <b>動きには一切影響しない。</b>この POC はラグドールの体を動かす面白さに絞るので、ダメージで反応が遅れる・
/// 腕が上がらない・立てない・スタミナが尽きる、といった性能低下は持たない（体は常にフルパワーで動く）。
/// 記録は見た目（打たれた部位が赤黒くなる）と、画面の得点表示にだけ使う。
/// </para>
/// </summary>
public sealed class BodyCondition
{
    /// <summary>この累計ダメージで部位が最も赤黒くなる。</summary>
    private const float FullBruiseDamage = 80f;

    private readonly float[] _region = new float[(int)BodyRegion.Count];

    /// <summary>受けたダメージの合計。</summary>
    public float TotalDamage { get; private set; }

    /// <summary>受けたクリーンヒット（ガード以外への一撃）の数。</summary>
    public int CleanHitsTaken { get; private set; }

    /// <summary>部位の傷み具合 0..1（見た目用）。</summary>
    public float Bruise(BodyRegion region) => MathF.Min(1f, _region[(int)region] / FullBruiseDamage);

    /// <summary><paramref name="damage"/> は 0..約 40 のダメージ点。<paramref name="side"/> は腕に当たったときどちらの腕か。</summary>
    public void ApplyHit(HitZone zone, Hand side, float damage)
    {
        var d = MathF.Max(0f, damage);
        TotalDamage += d;
        if (zone != HitZone.Guard)
        {
            CleanHitsTaken++;
        }

        _region[(int)RegionOf(zone, side)] += d;
    }

    private static BodyRegion RegionOf(HitZone zone, Hand side) => zone switch
    {
        HitZone.Chin or HitZone.Temple or HitZone.Head or HitZone.Neck => BodyRegion.Head,
        HitZone.Heart or HitZone.Liver or HitZone.SolarPlexus or HitZone.Body => BodyRegion.Torso,
        HitZone.Shoulder => side == Hand.Left ? BodyRegion.LeftUpperArm : BodyRegion.RightUpperArm,
        HitZone.Elbow or HitZone.Guard => side == Hand.Left ? BodyRegion.LeftForearm : BodyRegion.RightForearm,
        _ => BodyRegion.Legs,
    };
}

/// <summary>見た目の傷みを付ける体の区分。</summary>
public enum BodyRegion
{
    Head,
    Torso,
    LeftUpperArm,
    RightUpperArm,
    LeftForearm,
    RightForearm,
    Legs,
    Count,
}
