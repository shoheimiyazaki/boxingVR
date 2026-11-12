using System.Numerics;
using Aigis.Foundation.Voxels;
using Aigis.Physics;
using Aigis.Physics.Voxels;
using Aigis.Render.Voxels;

namespace MiniRing.Voxels;

/// <summary>体の 1 部位（または切り落とされた破片）: ボクセルの剛体と、その描画のキャッシュ。</summary>
/// <param name="Kind">どの部位か（破片なら元の部位）。</param>
/// <param name="Body">ボクセルの剛体。</param>
/// <param name="Volume">描画用の 3D テクスチャ（GPU がここから表面を作る）。格子が変わったら <see cref="VoxelVolume.Sync(VoxelGrid)"/> で写す。</param>
/// <param name="SeverAxis">骨が断たれたらこの軸（部位のローカル 0/1/2）で切り離す。null なら切れない（胴・頭・足）。</param>
/// <param name="DistalHigh">切れたとき、軸の正の側が体から遠い側か。</param>
/// <param name="FragmentOf">破片なら、元の部位の剛体 id。</param>
public sealed record VoxelPart(BodyPartKind Kind, VoxelBody Body, VoxelVolume Volume, int? SeverAxis, bool DistalHigh, int? FragmentOf = null)
{
    /// <summary>チアノーゼを丸める段の数（テクスチャの書き直しはこの段が変わったときだけ）。</summary>
    private const int CyanosisSteps = 16;

    /// <summary>
    /// いちばん強いチアノーゼで、皮膚の色を寄せる割合。0.6 とくすんだ青灰色では照明の下で肌の陰と見分けられなかった（ユーザーの指摘）ので、
    /// ゲームとして読める強さ（ほぼ寄せきる）と色にした。
    /// </summary>
    private const float MaxCyanosis = 0.85f;

    /// <summary>
    /// 顔のグラデーションの曲がり方（高さ 0..1 の何乗で青くするか）。1 未満で、首の近くだけ元の色を残して顔の大部分をしっかり青くする
    /// （1 だと口元まで薄く、顔の青さが読めなかった）。
    /// </summary>
    private const float CyanosisRise = 0.6f;

    /// <summary>チアノーゼの皮膚の色（青紫。現実よりはっきりさせた、ゲームの合図の色）。</summary>
    private static readonly Vector4 CyanoticSkin = new(0.36f, 0.34f, 0.86f, 1f);

    /// <summary>
    /// 塗り直しの関数の置き場（段ごとに作り直す）。record の値の比較・ハッシュ（<c>_originals</c> の鍵）が描画で変わらないよう、
    /// 中身を書き換える別の箱に入れて、この部位は箱への参照だけを持つ。
    /// </summary>
    private readonly RecolorCache _cache = new();

    private sealed class RecolorCache
    {
        public VoxelRecolor? Recolor { get; set; }

        public int Step { get; set; }
    }

    /// <summary>この部位を描く指示: 格子の変化をテクスチャへ写し、セル座標をワールドへ置く行列を付ける。</summary>
    public VoxelVolumeDraw Draw() => Draw(0f);

    /// <summary>
    /// この部位を描く指示。頭なら <paramref name="cyanosis"/>（0..1）だけ顔の皮膚を青紫へ寄せる（チアノーゼ: 息が上がり酸素が足りない）。
    /// 顔だけ、下から上へのグラデーション: 頭の箱の下の端（首とのつなぎ目）は元の肌の色のまま、上へ行くほど青い（ユーザーの指定。
    /// 全身を青くすると照明の陰と混ざって分かりにくかった）。16 段に丸めて、段が変わったときだけテクスチャを書き直す。見た目だけ。
    /// </summary>
    public VoxelVolumeDraw Draw(float cyanosis)
    {
        var step = Kind == BodyPartKind.Head ? (int)MathF.Round(Math.Clamp(cyanosis, 0f, 1f) * CyanosisSteps) : 0;
        if (step != _cache.Step)
        {
            // 塗り直しの関数は段が変わったときだけ作る（毎フレーム作ると描画の道でごみが出る。VR で効く）。
            var height = MathF.Max(1f, Body.Grid.Dimensions.Y - 1);
            var amount = MaxCyanosis * step / CyanosisSteps;
            _cache.Recolor = step == 0 ? null : (m, color, _, y, _) => m == BoxerAnatomy.Skin
                ? Vector4.Lerp(color, CyanoticSkin, amount * MathF.Pow(y / height, CyanosisRise)) with { W = color.W }
                : color;
            _cache.Step = step;
        }

        Volume.Sync(Body.Grid, step, _cache.Recolor);
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
    /// <param name="look">顔（肌・髪の色と髪型・眉・ひげ。<see cref="FaceLook"/>）。null なら作ったまま。見た目だけ。</param>
    public BoxerBody(Fighter fighter, PhysicsWorld world, FighterTuning tuning, LowPoly.BoxerModel? shapes = null, Outfit? outfit = null, BoxerVoxelOverrides? overrides = null, FaceLook? look = null)
    {
        ArgumentNullException.ThrowIfNull(fighter);
        ArgumentNullException.ThrowIfNull(world);
        ArgumentNullException.ThrowIfNull(tuning);
        _deathFraction = tuning.OrganDeathFraction;
        var p = fighter.Parts;
        VoxelPalette? lookPalette = null;
        foreach (var id in fighter.AllParts)
        {
            var kind = fighter.KindOf(id);
            var grid = BoxerAnatomy.Build(kind, fighter.HalfExtentsOf(id), tuning.VoxelSize, shapes?.Mesh(kind), outfit, overrides);
            if (look is not null)
            {
                lookPalette ??= look.Palette(grid.Palette);
                grid = look.Restyle(kind, grid, lookPalette);
            }
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
            AddOrganSites(part);
            _originals[part] = Original.Of(grid);
        }
    }

    /// <summary>
    /// 部位の格子の、作ったときの中身: 各セルの材質と表面だったか、と、治す順（格子の中心に近い順 = 内側から外へ。浮いたボクセルを作らない）。
    /// </summary>
    private sealed record Original(byte[] Materials, bool[] Surface, int[] HealOrder)
    {
        public static Original Of(VoxelGrid grid)
        {
            var (nx, ny, nz) = grid.Dimensions;
            var count = nx * ny * nz;
            var materials = new byte[count];
            var surface = new bool[count];
            var center = new Vector3(nx - 1, ny - 1, nz - 1) * 0.5f;
            var order = new List<(float Distance, int Index)>();
            for (var i = 0; i < count; i++)
            {
                materials[i] = grid.AtIndex(i);
                if (materials[i] == 0)
                {
                    continue;
                }

                var (x, y, z) = grid.Coordinates(i);
                surface[i] = grid.IsOriginalSurface(x, y, z);
                order.Add((Vector3.DistanceSquared(new Vector3(x, y, z) * grid.CellSize, center * grid.CellSize), i));
            }

            return new Original(materials, surface, [.. order.OrderBy(o => o.Distance).ThenBy(o => o.Index).Select(o => o.Index)]);
        }
    }

    private readonly Dictionary<VoxelPart, Original> _originals = [];

    /// <summary>
    /// 削れた体を治す（ラウンドの間の再生）: 付いている部位の、削られたセルを元の材質で、内側から外へ最大 <paramref name="budget"/> 個戻す。
    /// 切り離された部位（骨が断たれて先が落ちた所）は生えない。臓器・脳が戻れば、その働き（意識・息・腹）も戻る。戻した数を返す。
    /// </summary>
    public int Regenerate(int budget)
    {
        var restored = 0;
        var severed = new HashSet<int>();
        foreach (var fragment in _fragments)
        {
            if (fragment.FragmentOf is { } from)
            {
                severed.Add(from);
            }
        }

        foreach (var part in _parts)
        {
            if (restored >= budget || severed.Contains(part.Body.BodyId) || !_originals.TryGetValue(part, out var original)
                || part.Body.Grid.FilledCount >= original.HealOrder.Length)
            {
                continue; // 治った部位（削れていない）は数えない
            }

            var grid = part.Body.Grid;
            var before = restored;
            foreach (var i in original.HealOrder)
            {
                if (restored >= budget)
                {
                    break;
                }

                if (grid.AtIndex(i) == 0 && grid.Restore(i, original.Materials[i], original.Surface[i]))
                {
                    restored++;
                }
            }

            if (restored > before)
            {
                part.Body.UpdateMass();
            }
        }

        return restored;
    }

    /// <summary>胴の臓器（打撲と働きを持つもの）: 心臓・肺・肝臓・腸。脳は意識（<see cref="BrainFunction"/>）、目は見た目だけ。</summary>
    public static IReadOnlyList<byte> TrunkOrgans { get; } = [BoxerAnatomy.Heart, BoxerAnatomy.Lung, BoxerAnatomy.Liver, BoxerAnatomy.Gut];

    /// <summary>臓器の置き場所: どの部位の格子の、どこ（格子のローカルの重心）に、どれだけの大きさ（同じ体積の球の半径）で。</summary>
    private readonly record struct OrganSite(VoxelPart Part, byte Organ, Vector3 LocalCentroid, float Radius);

    private readonly List<OrganSite> _organSites = [];
    private readonly Dictionary<byte, float> _bruise = [];

    private void AddOrganSites(VoxelPart part)
    {
        var grid = part.Body.Grid;
        var (nx, ny, nz) = grid.Dimensions;
        foreach (var organ in TrunkOrgans)
        {
            var sum = Vector3.Zero;
            var count = 0;
            for (var z = 0; z < nz; z++)
            {
                for (var y = 0; y < ny; y++)
                {
                    for (var x = 0; x < nx; x++)
                    {
                        if (grid.At(x, y, z) == organ)
                        {
                            sum += new Vector3(x + 0.5f, y + 0.5f, z + 0.5f);
                            count++;
                        }
                    }
                }
            }

            if (count > 0)
            {
                var centroid = (sum / count * grid.CellSize) - grid.HalfExtents;
                var volume = count * grid.CellSize.X * grid.CellSize.Y * grid.CellSize.Z;
                _organSites.Add(new OrganSite(part, organ, centroid, MathF.Cbrt(3f * volume / (4f * MathF.PI))));
            }
        }
    }

    /// <summary>
    /// 打撃の衝撃を臓器へ伝える（皮膚の上からの打撲。削れなくても効く）。臓器ごとに、当たった点から臓器の表面までの距離 d について
    /// <c>energy × perJoule × exp(−d / falloff)</c> だけ打撲が増える（0..1 で頭打ち）。衝撃は組織を伝わるうちに減る（距離の指数で）。
    /// 臓器の名前で場合分けしない: どの臓器が効くかは、当たった所との位置関係だけで決まる。
    /// </summary>
    /// <returns>臓器ごとの打撲の増え分（0 のものは含まない）。</returns>
    public IReadOnlyDictionary<byte, float> Bruise(Vector3 point, float energy, float perJoule, float falloff)
    {
        var added = new Dictionary<byte, float>();
        if (energy <= 0f)
        {
            return added;
        }

        foreach (var site in _organSites)
        {
            var center = Vector3.Transform(site.LocalCentroid, site.Part.Body.GridToWorld());
            var depth = MathF.Max(0f, Vector3.Distance(point, center) - site.Radius);
            var dose = energy * perJoule * MathF.Exp(-depth / MathF.Max(1e-4f, falloff));
            var before = _bruise.GetValueOrDefault(site.Organ);
            var after = MathF.Min(1f, before + dose);
            if (after > before)
            {
                _bruise[site.Organ] = after;
                added[site.Organ] = added.GetValueOrDefault(site.Organ) + (after - before);
            }
        }

        return added;
    }

    /// <summary>打撲が時間とともに引く（一定の速さ <paramref name="rate"/> [/s]）。</summary>
    public void HealBruises(float dt, float rate)
    {
        foreach (var organ in TrunkOrgans)
        {
            if (_bruise.TryGetValue(organ, out var b) && b > 0f)
            {
                _bruise[organ] = MathF.Max(0f, b - (rate * dt));
            }
        }
    }

    /// <summary>臓器の中心（ワールド。いちばん大きな置き場所の重心）。その臓器が無ければ null。</summary>
    public Vector3? OrganCenter(byte organ)
    {
        OrganSite? best = null;
        foreach (var site in _organSites)
        {
            if (site.Organ == organ && (best is null || site.Radius > best.Value.Radius))
            {
                best = site;
            }
        }

        return best is { } s ? Vector3.Transform(s.LocalCentroid, s.Part.Body.GridToWorld()) : null;
    }

    /// <summary>臓器の今の打撲（0..1、一時的）。</summary>
    public float BruiseOf(byte organ) => _bruise.GetValueOrDefault(organ);

    /// <summary>臓器が削られた割合（0..1、恒久的）。</summary>
    public float OrganLoss(byte organ)
    {
        int left = 0, start = 0;
        foreach (var part in _parts)
        {
            left += part.Body.Grid.Remaining(organ);
            start += part.Body.Grid.Initial(organ);
        }

        return start == 0 ? 0f : Math.Clamp(1f - ((float)left / start), 0f, 1f);
    }

    /// <summary>
    /// 臓器の働き（0..1）= 打撲で一時的に落ちる分（1 − 打撲）× 削れて恒久的に落ちる分（<c>0.5^(削れた割合 / halvingLoss)</c>、脳と同じ形）。
    /// </summary>
    public float OrganFunction(byte organ, float halvingLoss) => (1f - BruiseOf(organ)) * BrainFunctionOf(OrganLoss(organ), halvingLoss);

    /// <summary>体に付いている部位（Aigis の 13 + つま先 2）。</summary>
    public IReadOnlyList<VoxelPart> Parts => _parts;

    /// <summary>切り落とされた破片。</summary>
    public IReadOnlyList<VoxelPart> Fragments => _fragments;

    public VoxelPart? PartOf(int bodyId) => _byBody.GetValueOrDefault(bodyId);

    /// <summary>その部位の力（肉の残りの割合、0..1）。知らない剛体なら 1。</summary>
    public float Muscle(int bodyId) => _byBody.TryGetValue(bodyId, out var part) ? part.Body.Grid.Fraction(VoxelTissue.Flesh) : 1f;

    /// <summary>
    /// 脳の働き（0..1）: 脳が削られた割合 <c>lost</c> について <c>0.5^(lost / halvingLoss)</c>。無傷で 1、
    /// <paramref name="halvingLoss"/>（既定 10%）削られるごとに半分になる（10% で 0.5、20% で 0.25、30% で 0.125 …）。
    /// 少しの削れでも働きははっきり落ちる（ユーザーの指示: 2026-11-11。以前は死ぬ残りで 0 になる平方根で、少しの削れは効きにくかった）。
    /// 死ぬかどうかは別に <see cref="DeathCause"/> が決める（残りが <see cref="FighterTuning.OrganDeathFraction"/> 未満）。
    /// </summary>
    public float BrainFunction(float halvingLoss)
    {
        int left = 0, start = 0;
        foreach (var part in _parts)
        {
            left += part.Body.Grid.Remaining(BoxerAnatomy.Brain);
            start += part.Body.Grid.Initial(BoxerAnatomy.Brain);
        }

        return BrainFunctionOf(start == 0 ? 0f : 1f - ((float)left / start), halvingLoss);
    }

    /// <summary>脳が <paramref name="lost"/>（割合）削られたときの働き: <c>0.5^(lost / halvingLoss)</c>。</summary>
    public static float BrainFunctionOf(float lost, float halvingLoss)
        => MathF.Pow(0.5f, Math.Clamp(lost, 0f, 1f) / MathF.Max(1e-3f, halvingLoss));

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
