using System.Collections.Immutable;
using System.Numerics;
using Aigis.Physics;

namespace MiniRing.LowPoly;

/// <summary>
/// 1 人のファイターの見た目の体: 剛体ごとの <see cref="DentedMesh"/>。ラウンドごとに新品（ファイターと同じ寿命）。
/// 物理（当たり判定は箱のまま）には影響しない、見た目だけの状態。
/// </summary>
public sealed class BoxerSkin
{
    private readonly FighterTuning _tuning;
    private ImmutableDictionary<int, DentedMesh> _parts;

    public BoxerSkin(BoxerModel model, Fighter fighter, FighterTuning tuning)
    {
        ArgumentNullException.ThrowIfNull(model);
        ArgumentNullException.ThrowIfNull(fighter);
        ArgumentNullException.ThrowIfNull(tuning);
        _tuning = tuning;
        _parts = fighter.AllParts.ToImmutableDictionary(id => id, id =>
        {
            var kind = fighter.KindOf(id);
            return new DentedMesh(model.Mesh(kind), model.MinHalfExtent(kind) * tuning.MaxDentRatio);
        });
    }

    public DentedMesh Part(int bodyId) => _parts[bodyId];

    /// <summary>
    /// 拳の当たりで部位 <paramref name="bodyId"/> をへこませる。<paramref name="fistCenter"/> と <paramref name="direction"/>（力の向き）はワールド座標、
    /// <paramref name="body"/> はその瞬間の部位の姿勢。深さはダメージに比例（1 発の上限あり）。
    /// </summary>
    public void Dent(int bodyId, BodyState body, Vector3 fistCenter, Vector3 direction, float damage)
    {
        var toLocal = Quaternion.Conjugate(body.Orientation);
        var depth = MathF.Min(_tuning.MaxDentDepthPerHit, damage * _tuning.DentDepthPerDamage);
        var dented = _parts[bodyId].Impact(
            Vector3.Transform(fistCenter - body.Position, toLocal), _tuning.GloveRadius,
            Vector3.Transform(direction, toLocal), depth, _tuning.DentSpread);
        _parts = _parts.SetItem(bodyId, dented);
    }
}
