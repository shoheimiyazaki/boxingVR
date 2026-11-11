using System.Numerics;
using Aigis.Foundation.Voxels;
using Aigis.Physics;
using Aigis.Physics.Voxels;
using Aigis.Render.Voxels;

namespace MiniRing.Voxels;

/// <summary>体の 1 部位（または切り落とされた破片）: ボクセルの剛体と、その描画のキャッシュ。</summary>
/// <param name="Kind">どの部位か（破片なら元の部位）。</param>
/// <param name="Body">ボクセルの剛体。</param>
/// <param name="Volume">描画用の 3D テクスチャ（GPU がここから表面を作る）。格子が変わったら <see cref="VoxelVolume.Sync"/> で写す。</param>
/// <param name="SeverAxis">骨が断たれたらこの軸（部位のローカル 0/1/2）で切り離す。null なら切れない（胴・頭・足）。</param>
/// <param name="DistalHigh">切れたとき、軸の正の側が体から遠い側か。</param>
/// <param name="FragmentOf">破片なら、元の部位の剛体 id。</param>
public sealed record VoxelPart(BodyPartKind Kind, VoxelBody Body, VoxelVolume Volume, int? SeverAxis, bool DistalHigh, int? FragmentOf = null)
{
    /// <summary>この部位を描く指示: 格子の変化をテクスチャへ写し、セル座標をワールドへ置く行列を付ける。</summary>
    public VoxelVolumeDraw Draw()
    {
        Volume.Sync(Body.Grid);
        return new VoxelVolumeDraw(Volume, VoxelVolume.CellToWorld(Body.Grid, Body.GridToWorld()));
    }
}

/// <summary>1 発の結果: 削れたボクセルの数と、骨が断たれて切り離された破片（無ければ null）。</summary>
public readonly record struct ImpactResult(int Carved, VoxelPart Part, VoxelPart? Fragment);

/// <summary>
/// ファイター 1 人のボクセルの体。部位の箱ごとに中身（<see cref="BoxerAnatomy"/>）を持ち、打撃で削れていく。
/// <list type="bullet">
/// <item>肉（皮膚と筋肉）の残りがその部位の力（<see cref="Muscle"/>）。腕・脚・胴の力が落ちる（<see cref="Fighter"/> が読む）。</item>
/// <item>腕・脚の骨が断たれたら、そこから先が破片（別の剛体）になって落ちる（<see cref="Impact"/>）。</item>
/// <item>どれかの臓器が半分以上壊れたら死ぬ（<see cref="DeathCause"/>）。</item>
/// </list>
/// </summary>
public sealed class BoxerBody
{
    private readonly Dictionary<int, VoxelPart> _byBody = [];
    private readonly List<VoxelPart> _parts = [];
    private readonly List<VoxelPart> _fragments = [];
    private readonly float _deathFraction;

    /// <param name="fighter">体の持ち主。</param>
    /// <param name="world">物理のワールド。</param>
    /// <param name="tuning">ボクセルの大きさ・死ぬ条件。</param>
    /// <param name="shapes">部位ごとの形（ローポリの体）。渡すと箱ではなくその形の内側だけを埋める。</param>
    /// <param name="outfit">身なり（トランクスとブーツ）。null なら裸。</param>
    /// <param name="overrides">ボクセル編集ソフトで直した中身（<see cref="BoxerVoxelOverrides.Shipped"/> など）。null なら生成のまま。</param>
    public BoxerBody(Fighter fighter, PhysicsWorld world, FighterTuning tuning, LowPoly.BoxerModel? shapes = null, Outfit? outfit = null, BoxerVoxelOverrides? overrides = null)
    {
        ArgumentNullException.ThrowIfNull(fighter);
        ArgumentNullException.ThrowIfNull(world);
        ArgumentNullException.ThrowIfNull(tuning);
        _deathFraction = tuning.OrganDeathFraction;
        var p = fighter.Parts;
        foreach (var id in fighter.AllParts)
        {
            var kind = fighter.KindOf(id);
            var grid = BoxerAnatomy.Build(kind, fighter.HalfExtentsOf(id), tuning.VoxelSize, shapes?.Mesh(kind), outfit, overrides);
            // 腕は長さが X: Aigis の Left 腕は −X 側に付くので体から遠いのは −X、Right 腕は +X。脚は Y で、遠いのは下。
            var (axis, distalHigh) = kind switch
            {
                BodyPartKind.UpperArm or BodyPartKind.Forearm => ((int?)0, id == p.RightUpperArm || id == p.RightForearm),
                BodyPartKind.Thigh or BodyPartKind.Shin => ((int?)1, false),
                _ => ((int?)null, false),
            };
            var part = new VoxelPart(kind, new VoxelBody(world, id, grid), VoxelVolume.For(grid), axis, distalHigh);
            _parts.Add(part);
            _byBody[id] = part;
        }
    }

    /// <summary>体に付いている部位（Aigis の 13 + つま先 2）。</summary>
    public IReadOnlyList<VoxelPart> Parts => _parts;

    /// <summary>切り落とされた破片。</summary>
    public IReadOnlyList<VoxelPart> Fragments => _fragments;

    public VoxelPart? PartOf(int bodyId) => _byBody.GetValueOrDefault(bodyId);

    /// <summary>その部位の力（肉の残りの割合、0..1）。知らない剛体なら 1。</summary>
    public float Muscle(int bodyId) => _byBody.TryGetValue(bodyId, out var part) ? part.Body.Grid.Fraction(VoxelTissue.Flesh) : 1f;

    /// <summary>
    /// 脳の働き（0..1）: 脳のボクセルの残り。死ぬ残り（<paramref name="deathFraction"/>）で 0、無傷で 1。
    /// 少し削れただけでは働きはあまり落ちず、死ぬ残りに近づくほど急に落ちる（残りの割合の平方根）。
    /// </summary>
    public float BrainFunction(float deathFraction)
    {
        int left = 0, start = 0;
        foreach (var part in _parts)
        {
            left += part.Body.Grid.Remaining(BoxerAnatomy.Brain);
            start += part.Body.Grid.Initial(BoxerAnatomy.Brain);
        }

        return start == 0 ? 1f : MathF.Sqrt(Math.Clamp(((float)left / start - deathFraction) / MathF.Max(1e-3f, 1f - deathFraction), 0f, 1f));
    }

    /// <summary>
    /// 半分以上（<see cref="FighterTuning.OrganDeathFraction"/> 未満しか残っていない）壊れた臓器の名前。
    /// 生きていれば null。
    /// </summary>
    public string? DeathCause()
    {
        var palette = BoxerAnatomy.Palette;
        for (var m = 1; m < palette.Count; m++)
        {
            var material = palette[(byte)m];
            if (!material.Vital)
            {
                continue;
            }

            int left = 0, start = 0;
            foreach (var part in _parts)
            {
                left += part.Body.Grid.Remaining((byte)m);
                start += part.Body.Grid.Initial((byte)m);
            }

            if (start > 0 && left < _deathFraction * start)
            {
                return material.Name;
            }
        }

        return null;
    }

    /// <summary>
    /// 部位 <paramref name="bodyId"/> に 1 発を刻む。<paramref name="energy"/> は削る量（肉 1 ボクセル = 1）。
    /// 削れたボクセルは <paramref name="chips"/> へ（演出用）。<paramref name="chunks"/> があれば、一部は塊のまま剥がれてそちらへ。骨が断たれたら破片を切り離して返す（関節の付け替えは呼び出し側）。
    /// <paramref name="spread"/> は破片の噴き出す円錐の広さ（<see cref="Aigis.Physics.Voxels.VoxelBody.Impact"/>）。
    /// </summary>
    public ImpactResult? Impact(int bodyId, Vector3 point, Vector3 direction, float energy, float chipSpeed, Func<float> jitter, ICollection<VoxelChip> chips, float spread = 0.4f, ICollection<VoxelChunk>? chunks = null, ICollection<VoxelCutFace>? cutFaces = null)
    {
        if (!_byBody.TryGetValue(bodyId, out var part))
        {
            return null;
        }

        var carved = part.Body.Impact(point, direction, energy, chipSpeed, jitter, chips, spread, chunks, cutFaces: cutFaces);
        return new ImpactResult(carved, part, carved > 0 ? SeverIfBroken(bodyId) : null);
    }

    /// <summary>
    /// 部位 <paramref name="bodyId"/> の骨が断たれていれば（武器で切られた後など）、そこから先を破片にして切り離し、その破片を返す
    /// （関節の付け替えは呼び出し側）。切れない部位・骨がつながっている部位・知らない剛体なら null。
    /// </summary>
    public VoxelPart? SeverIfBroken(int bodyId)
    {
        if (!_byBody.TryGetValue(bodyId, out var part) || part.SeverAxis is not { } axis || part.Body.Sever(axis, part.DistalHigh) is not { } piece)
        {
            return null;
        }

        var fragment = new VoxelPart(part.Kind, piece, VoxelVolume.For(piece.Grid), axis, part.DistalHigh, bodyId);
        _fragments.Add(fragment);
        return fragment;
    }
}
