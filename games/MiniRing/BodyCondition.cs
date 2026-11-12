namespace MiniRing;

/// <summary>
/// 受けたダメージの記録（純ロジック）。
/// <para>
/// <b>動きには一切影響しない。</b>点数が溜まったから反応が遅れる・腕が上がらない・立てない、といった性能低下は持たない。
/// 動きに響くのは体の壊れ方（ボクセル、<see cref="Voxels.BoxerBody"/>）とスタミナ（エネルギーの出入り、<see cref="Fighter.Stamina"/>）で、
/// どちらも点数とは別に物理・材料で決まる。記録は画面の得点表示にだけ使う。打たれた場所の見た目は、へこみ（<see cref="LowPoly.BoxerSkin"/>）で表す。
/// </para>
/// </summary>
public sealed class BodyCondition
{
    /// <summary>受けたダメージの合計。</summary>
    public float TotalDamage { get; private set; }

    /// <summary>受けたクリーンヒット（ガード以外への一撃）の数。</summary>
    public int CleanHitsTaken { get; private set; }

    /// <summary><paramref name="damage"/> は 0..約 40 のダメージ点。</summary>
    public void ApplyHit(HitZone zone, float damage)
    {
        TotalDamage += MathF.Max(0f, damage);
        if (zone != HitZone.Guard)
        {
            CleanHitsTaken++;
        }
    }
}
