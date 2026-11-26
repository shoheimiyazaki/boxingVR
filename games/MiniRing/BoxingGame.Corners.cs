using System.Numerics;
using Aigis.Physics.Voxels;

namespace MiniRing;

/// <summary>
/// コーナーに立つ所: 体と持ち物（武器・盾・はめる物）を上から見た輪郭から、場の縁（ロープ・金網）に触れない所を出す（<see cref="RingLayout.CornerFor"/>）。
/// 檻の角は 120° と狭く、一律の距離で立たせると、生まれた時点で武器・盾が金網に食い込み、物理はそれを押し出せず動けなかった（2026-11-24）。
/// </summary>
public sealed partial class BoxingGame
{
    /// <summary>選手ごとの、生成した姿勢の輪郭（骨盤から見た、体と持ち物の格子の箱の角の水平のずれ: X, Z）。休み・ダウンの間のコーナーも同じ輪郭で決める。</summary>
    private readonly Dictionary<Fighter, IReadOnlyList<Vector2>> _footprints = [];

    /// <summary>選手 <paramref name="fighter"/> の輪郭（<see cref="_footprints"/>）。測る前なら空（体の大きさを持たない点として扱う）。</summary>
    internal IReadOnlyList<Vector2> FootprintOf(Fighter fighter) => _footprints.TryGetValue(fighter, out var footprint) ? footprint : [];

    /// <summary>
    /// 生成した直後の <paramref name="fighter"/> の輪郭を測って覚え、コーナーから始める試合（<see cref="RingLayout.FromCorners"/>）なら、体と持ち物を
    /// まとめて自分のコーナーの立つ所（<see cref="RingLayout.RestCorner"/>）へ水平に平行移動する（向きは変えない）。生成の向き合わせ（<see cref="Fighter.Spawn"/>）と同じく、
    /// まだ一歩も進めていない（速度 0）ときだけの置き直しで、関節はローカルの定義なので保たれる（CLAUDE.md §1.1 の例外: 生成直後の配置）。
    /// </summary>
    private void PlaceAtCorner(Fighter fighter, int seat)
    {
        var footprint = MeasureFootprint(fighter);
        _footprints[fighter] = footprint;
        if (!Layout.FromCorners && _fighters.Length <= 2)
        {
            return; // バトルロイヤルはいつも席の頂点から
        }

        var shift = Layout.SeatCorner(seat, _fighters.Length, footprint) - fighter.PelvisPosition;
        shift.Y = 0f;
        foreach (var id in BodiesOf(fighter))
        {
            var s = _world.GetState(id);
            _world.SetDynamicPose(id, s.Position + shift, s.Orientation);
        }
    }

    /// <summary>体の剛体と、その選手が握る・はめる物の剛体（どれも生成の直後で、選手と一緒に動かす物）。</summary>
    private IEnumerable<int> BodiesOf(Fighter fighter) =>
        fighter.AllParts.Concat(HeldGrids(fighter).Select(g => g.BodyId)).Distinct();

    /// <summary>選手が握る・はめる物の格子。</summary>
    private IEnumerable<VoxelBody> HeldGrids(Fighter fighter) =>
        _wielders.Where(w => w.Value == fighter).SelectMany(w => w.Key.Grids);

    /// <summary>
    /// 体の格子（<see cref="Voxels.BoxerBody.Layers"/>）と持ち物の格子を囲む箱の 8 つの角を、骨盤から見た水平のずれ（X, Z）にしたもの。
    /// 形の大きさだけを見る（武器の種類は見ない）。箱は格子の空いたセルも含むので、輪郭は見た目より少し大きめ。
    /// </summary>
    private List<Vector2> MeasureFootprint(Fighter fighter)
    {
        var pelvis = fighter.PelvisPosition;
        var grids = (fighter.Voxels?.Layers.Select(p => p.Body) ?? []).Concat(HeldGrids(fighter));
        var points = new List<Vector2>();
        foreach (var grid in grids)
        {
            var s = _world.GetState(grid.BodyId);
            var half = grid.Grid.HalfExtents;
            for (var corner = 0; corner < 8; corner++)
            {
                var local = grid.GridCenterLocal + new Vector3((corner & 1) == 0 ? -half.X : half.X, (corner & 2) == 0 ? -half.Y : half.Y, (corner & 4) == 0 ? -half.Z : half.Z);
                var p = s.Position + Vector3.Transform(local, s.Orientation) - pelvis;
                points.Add(new Vector2(p.X, p.Z));
            }
        }

        return points;
    }
}
