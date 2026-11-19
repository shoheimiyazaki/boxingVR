using System.Numerics;
using Aigis.Physics.Voxels;

namespace MiniRing.Highlights;

/// <summary>観測が読む 1 つの当たり（拳・蹴り・肘・武器をそろえた形）。</summary>
public readonly record struct ObservedHit(Fighter Attacker, Fighter Victim, HitZone Zone, Vector3 Point, float Speed, float Energy, float Damage);

/// <summary>
/// 試合から読む物を 1 か所に集めたもの（読むだけ）。古い世代の試合で同じ観測を回すとき（docs/arena/excitement_generations_*.md）、
/// API の違いはこのファイルだけで吸収する（世代ごとの小さな差し替え）。
/// </summary>
public static class HighlightSource
{
    public static IEnumerable<ObservedHit> Hits(BoxingGame game)
    {
        foreach (var h in game.RecentHits)
        {
            yield return new ObservedHit(h.Attacker, h.Victim, h.Zone, h.Point, h.Speed, h.Energy, h.Damage);
        }

        foreach (var h in game.WeaponHits)
        {
            // 武器の当たりのエネルギーは運動量 × 速さ ÷ 2 の見積もり（TvTelemetry と同じ）。
            yield return new ObservedHit(h.Attacker, h.Victim, h.Zone, h.Point, h.Speed, 0.5f * h.Momentum * h.Speed, h.Damage);
        }
    }

    /// <summary>体のセルの格子（顔の層も。皮一枚・欠けを数える）。</summary>
    public static IEnumerable<VoxelBody> Grids(Fighter f) =>
        f.Voxels is { } v ? v.Layers.Select(p => p.Body) : [];

    public static IEnumerable<(Fighter? Victim, Vector3 Point)> BrokenBones(BoxingGame game) =>
        game.BrokenBones.Select(b => ((Fighter?)b.Victim, b.Position));

    public static IEnumerable<(Fighter? Victim, Vector3 Point)> Severs(BoxingGame game) =>
        game.RecentSevers.Select(s => (s.Victim, s.Position));

    public static IEnumerable<(Vector3 Point, int Grains)> Shatters(BoxingGame game) =>
        game.RecentShatters.Select(s => (s.Point, s.Grains.Count));

    public static float HeadTwist(Fighter f) => f.LastHeadTwist;
}
