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

/// <summary>1 発の結果: 削れたボクセルの数と、骨が断たれた所（無ければ null）。</summary>
public readonly record struct ImpactResult(int Carved, VoxelPart Part, BoneBreak? Break);

/// <summary>
/// 腕・脚の骨が断たれた・折れた所。部位はそこで 2 つの剛体に分かれる（体に近い側が元の部位、遠い側が <paramref name="Fragment"/>）。
/// 骨の切れ目をまたいで、まだ肉（皮膚・筋肉）がつないでいれば、その肉の橋が 2 つをつなぐ（<see cref="Fighter.Break"/>: 駆動の無い弱いバネ）。
/// 肉まで断たれていれば、遠い側は落ちる。
/// </summary>
/// <param name="Part">骨が切れた部位（体に近い側として残る）。</param>
/// <param name="Fragment">遠い側の破片（その先に何も残っていなければ null: 関節の所で骨の端が削り取られた）。</param>
/// <param name="Point">切れ目（ワールド。骨の軸の上、切れ目の面）。</param>
/// <param name="BridgeCells">切れ目をまたいでつないでいる肉のセルの数（0 なら断たれた）。</param>
/// <param name="TearForce">肉の橋が引っぱりで裂ける力 [N]（セルごとの「強さ × 断面の面積」の和）。</param>
/// <param name="BendTorque">肉の橋が曲げで持ちこたえられるトルク [N·m]（セルごとの「強さ × 面積 × 骨の軸からの距離」の和）。</param>
/// <param name="Bent">打撃の曲げで折れた（骨のセルは残っている）か、削られて断たれたか。</param>
public sealed record BoneBreak(VoxelPart Part, VoxelPart? Fragment, Vector3 Point, int BridgeCells, float TearForce, float BendTorque, bool Bent)
{
    public FleshBridge? Bridge { get; init; }
    /// <summary>肉がまだつないでいる（遠い側がぶら下がる）。</summary>
    public bool Hangs => Fragment is not null && BridgeCells > 0;
}

/// <summary>分割後も、折れ目の両側に残った皮膚・筋肉の対応セルを追跡する断面。</summary>
public sealed record FleshBridge(VoxelBody Stub, VoxelBody Fragment, IReadOnlyList<FleshBridge.Cell> Cells, int FullCells)
{
    public readonly record struct Cell(int StubIndex, int FragmentIndex, float Tear, float Bend);

    public (int Count, float Fraction, float Tear, float Bend) Measure()
    {
        var count = 0;
        float tear = 0f, bend = 0f;
        foreach (var cell in Cells)
        {
            if (Flesh(Stub.Grid, cell.StubIndex) && Flesh(Fragment.Grid, cell.FragmentIndex))
            {
                count++;
                tear += cell.Tear;
                bend += cell.Bend;
            }
        }
        return (count, FullCells > 0 ? Math.Clamp((float)count / FullCells, 0f, 1f) : 0f, tear, bend);
    }

    private static bool Flesh(VoxelGrid grid, int i) => i >= 0 && grid.Palette[grid.MaterialAt(i)].Tissue == VoxelTissue.Flesh;

    public FleshBridge MoveStub(VoxelBody next)
    {
        Matrix4x4.Invert(next.GridToWorld(), out var inverse);
        var transform = Stub.GridToWorld() * inverse;
        return this with { Stub = next, Cells = Cells.Select(c =>
        {
            if (c.StubIndex < 0)
            {
                return c;
            }
            var (x, y, z) = Stub.Grid.Coordinates(c.StubIndex);
            return c with { StubIndex = next.Grid.CellAt(Vector3.Transform(Stub.Grid.CellCenter(x, y, z), transform)) };
        }).ToArray() };
    }
}

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
    private (int Version, VisionState State)? _vision;

    /// <summary>眼球・黒目の元の位置ごとの残存 HP。同じ組織が残る割合を左右別に数える。</summary>
    public VisionState Vision
    {
        get
        {
            var head = _parts.Single(p => p.Kind == BodyPartKind.Head);
            var grid = head.Body.Grid;
            if (_vision is { } cached && cached.Version == grid.Version)
            {
                return cached.State;
            }
            var original = _originals[head];
            float left = 0f, right = 0f, fullLeft = 0f, fullRight = 0f;
            for (var i = 0; i < original.Materials.Length; i++)
            {
                var material = original.Materials[i];
                if (material is not (BoxerAnatomy.Eye or BoxerAnatomy.Pupil))
                {
                    continue;
                }
                var (x, y, z) = grid.Coordinates(i);
                var full = grid.Palette[material].Toughness;
                var remaining = grid.MaterialAt(i) == material ? Math.Clamp(grid.HealthAt(i), 0f, full) : 0f;
                if (grid.CellCenter(x, y, z).X > 0f)
                {
                    fullLeft += full;
                    left += remaining;
                }
                else
                {
                    fullRight += full;
                    right += remaining;
                }
            }
            var state = new VisionState(fullLeft > 0f ? left / fullLeft : 0f, fullRight > 0f ? right / fullRight : 0f);
            _vision = (grid.Version, state);
            return state;
        }
    }
    /// <summary>ドリル・テスト用: その側（+X が左）の目の白目・黒目を全部潰す（打撃で潰れたのと同じ、セルを空にする）。</summary>
    public void DestroyEye(bool left)
    {
        var grid = _parts.Single(p => p.Kind == BodyPartKind.Head).Body.Grid;
        for (var i = 0; i < grid.CellCount; i++)
        {
            if (grid.MaterialAt(i) is not (BoxerAnatomy.Eye or BoxerAnatomy.Pupil))
            {
                continue;
            }

            var (x, y, z) = grid.Coordinates(i);
            if ((grid.CellCenter(x, y, z).X > 0f) == left)
            {
                grid.SetCellState(i, (0, 0f, false, 0f));
            }
        }
    }

    private readonly Dictionary<int, VoxelPart> _byBody = [];
    private readonly List<VoxelPart> _parts = [];
    private readonly List<VoxelPart> _fragments = [];
    private readonly float _deathFraction;

    /// <summary>作ったときの骨の、切れる軸に沿った両端のスライス（腕・脚の部位ごと）。関節はその外側で骨を受けている。</summary>
    private readonly Dictionary<int, (int Low, int High)> _boneSpan = [];

    /// <summary>遠い側の端がまだ次の部位の関節を受けている部位（上腕・太もも・すね。骨が切れたら、その先は破片の側へ移る）。</summary>
    private readonly HashSet<int> _holdsFarEnd = [];

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
            var half = fighter.HalfExtentsOf(id);
            var sourceHalf = half / tuning.Build.ScaleOf(kind);
            // 同じ材質セルを伸縮する。太さを変えてセルを増やすと解像度と筋肉量が混ざる。
            var sourceShape = shapes?.Mesh(kind);
            if (tuning.Build != BodyBuild.Standard)
            {
                sourceShape = LowPoly.BoxerModel.Build((tuning with { Build = BodyBuild.Standard }).HalfExtents, tuning.ModelSubdivisions).Mesh(kind);
            }
            var source = BoxerAnatomy.Build(kind, sourceHalf, tuning.VoxelSize, sourceShape, outfit, overrides);
            var grid = source;
            if (half != sourceHalf)
            {
                grid = new VoxelGrid(source.Palette, half, source.Dimensions, (point, _) =>
                {
                    var c = (point + half) / (2f * half);
                    return source.At(Math.Clamp((int)(c.X * source.Dimensions.X), 0, source.Dimensions.X - 1),
                        Math.Clamp((int)(c.Y * source.Dimensions.Y), 0, source.Dimensions.Y - 1),
                        Math.Clamp((int)(c.Z * source.Dimensions.Z), 0, source.Dimensions.Z - 1));
                });
            }
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
            // 腕・脚は骨で曲げを受ける梁（肉は質量だけ）: 打撃の曲げが骨の強さを超えた所で折れる（VoxelContact が場所を返す）。
            var part = new VoxelPart(kind, new VoxelBody(world, id, grid, materialMass: false) { BeamAxis = axis }, VoxelVolume.For(grid), axis, distalHigh);
            _parts.Add(part);
            _byBody[id] = part;
            AddOrganSites(part);
            _originals[part] = Original.Of(grid);
            if (axis is { } along && BoneSpan(grid, along) is { } span)
            {
                _boneSpan[id] = span;
                if (kind is BodyPartKind.UpperArm or BodyPartKind.Thigh or BodyPartKind.Shin)
                {
                    _holdsFarEnd.Add(id); // 肘・膝・足首の関節が、この部位の遠い側の骨の端に付いている
                }
            }
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

    /// <summary>部位ごとの、治し切った時の格子の版（<see cref="Regenerate"/> が飛ばす目印）。</summary>
    private readonly Dictionary<VoxelPart, int> _healedAt = new(ReferenceEqualityComparer.Instance);

    /// <summary>
    /// 削れた体を治す（ラウンドの間の再生）: 付いている部位の、削られたセルを元の材質で、内側から外へ最大 <paramref name="budget"/> 個戻す。
    /// 切り離された部位（骨が断たれて先が落ちた所）は生えない。臓器・脳が戻れば、その働き（意識・息・腹）も戻る。戻した数を返す。
    /// </summary>
    public int Regenerate(int budget)
    {
        // 臓器が潰れて皮膚が押し込まれた所は数が減らない（中身が入れ替わっただけ）ので、「埋まった数が元に届いたら治った」とは言えない。
        // 治し切った時の格子の版を覚えておき、それから変わっていない部位は飛ばす。
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
                || _healedAt.TryGetValue(part, out var healed) && healed == part.Body.Grid.Version)
            {
                continue; // 治った部位（治してから変わっていない）は数えない
            }

            var grid = part.Body.Grid;
            var before = restored;
            foreach (var i in original.HealOrder)
            {
                if (restored >= budget)
                {
                    break;
                }

                // 削れた所だけでなく、へこみで押し込まれた所（潰れた肉の場所に入った皮膚）・擦り減った皮膚（赤み）も元に戻す。
                // 数えるのは、中身が入れ替わったセル（埋め直した・押し込まれた所を戻した）だけ。赤み・擦り減りを消すだけなら予算を使わない。
                var was = grid.AtIndex(i);
                if (grid.Reset(i, original.Materials[i], original.Surface[i]) && was != original.Materials[i])
                {
                    restored++;
                }
            }

            if (restored > before)
            {
                part.Body.UpdateMass();
            }

            if (restored < budget)
            {
                _healedAt[part] = grid.Version; // 全部戻った（予算が尽きずに回り切った）
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

    /// <summary>
    /// その部位の力（残った筋肉の断面の割合、0..1）。知らない剛体なら 1。
    /// 腕・脚（梁の軸のある部位）は、軸に沿った薄切りごとの肉（皮膚と筋肉）の残りを数え、いちばん細い半分の薄切り（肉のセルが最大の 4 分の 1 未満の端は除く）の平均を取る:
    /// 筋肉は直列に力を伝えるので、どこか 1 か所が細くなればそこで力が決まる（体積の割合では、ごく一部が深く削れても力が落ちなかった）。
    /// 一様に削れた部位では体積の割合と同じ。軸のない部位（頭・胴）は体積の割合。
    /// </summary>
    public float Muscle(int bodyId)
    {
        if (!_byBody.TryGetValue(bodyId, out var part))
        {
            return 1f;
        }

        var grid = part.Body.Grid;
        if (part.Body.BeamAxis is not { } axis || !_originals.TryGetValue(part, out var original))
        {
            return grid.Fraction(VoxelTissue.Flesh);
        }

        if (_sectionCache.TryGetValue(bodyId, out var cached) && cached.Version == grid.Version)
        {
            return cached.Value;
        }

        var dims = grid.Dimensions;
        var slices = axis == 0 ? dims.X : axis == 1 ? dims.Y : dims.Z;
        var initial = new int[slices];
        var left = new int[slices];
        var palette = grid.Palette;
        for (var i = 0; i < original.Materials.Length; i++)
        {
            var was = original.Materials[i];
            if (was == 0 || palette[was].Tissue != VoxelTissue.Flesh)
            {
                continue;
            }

            var (x, y, z) = grid.Coordinates(i);
            var slice = axis == 0 ? x : axis == 1 ? y : z;
            initial[slice]++;
            var now = grid.AtIndex(i);
            left[slice] += now != 0 && palette[now].Tissue == VoxelTissue.Flesh ? 1 : 0;
        }

        // 丸い端・骨が断面の大半を占める所の薄切り（肉のセルが少ない）は数えない: 1 セル欠けただけで割合が大きく動くため。
        var thick = initial.Max();
        var fractions = new List<float>();
        for (var k = 0; k < slices; k++)
        {
            if (initial[k] > 0 && initial[k] * 4 >= thick)
            {
                fractions.Add((float)left[k] / initial[k]);
            }
        }

        fractions.Sort();
        var weakest = Math.Max(1, (fractions.Count + 1) / 2);
        var value = fractions.Count == 0 ? 1f : fractions.Take(weakest).Average();
        _sectionCache[bodyId] = (grid.Version, value);
        return value;
    }

    private readonly Dictionary<int, (int Version, float Value)> _sectionCache = [];

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
        return new ImpactResult(carved, part, carved > 0 ? BreakIfSevered(bodyId) : null);
    }

    /// <summary>
    /// 部位 <paramref name="bodyId"/> の骨が断たれていれば（削られて骨の無いスライスができた）、そこで部位を 2 つに分け、その切れ目を返す
    /// （関節の付け替えは呼び出し側: <see cref="Fighter.Break"/>）。骨の端を受けている関節も骨として数える: 肘の側の骨の端が削り取られたら、
    /// 前腕はもう骨で支えられていない（以前は骨が両側に残る切れ目しか見ず、肘の無い前腕が空中の関節に付いたまま動いていた）。
    /// 切れない部位・骨がつながっている部位・知らない剛体なら null。
    /// </summary>
    public BoneBreak? BreakIfSevered(int bodyId)
    {
        if (!_byBody.TryGetValue(bodyId, out var part) || part.SeverAxis is not { } axis || !_boneSpan.TryGetValue(bodyId, out var span))
        {
            return null;
        }

        // 体に近い側の関節は、元の部位にいつも付いている。遠い側の関節は、まだその端を受けていれば。
        var near = part.DistalHigh ? span.Low - 1 : span.High + 1;
        int? far = _holdsFarEnd.Contains(bodyId) ? (part.DistalHigh ? span.High + 1 : span.Low - 1) : null;
        var (low, high) = part.DistalHigh ? ((int?)near, far) : (far, (int?)near);
        var grid = part.Body.Grid;
        var slice = grid.FindBoneBreak(axis, fromHigh: !part.DistalHigh, low, high) ?? EmptyJointSlice(grid, _originals[part].Materials, axis, span, part.DistalHigh, far is not null);
        return slice is { } at ? BreakAt(part, axis, at, bent: false) : null;
    }

    /// <summary>
    /// 遠い側の関節（肘・膝・足首）を受けている、骨の端のすぐ先のスライス（骨と関節の間の肉と靭帯）が、まるごと無くなっていればそのスライス
    /// （無ければ null）。そこでは関節が何も受けていない（肘の肉が斬り取られた上腕の先に、前腕が付いていてはいけない）。
    /// 骨の端に接したスライスだけを見る: その先の丸い端のスライスは数セルしかなく、かすっただけで空になる（それで腕が外れてはいけない）。
    /// 体に近い側（肩・股）は見ない: そこで分けると、関節とサーボに付いた元の部位に数セルしか残らない（骨の端が削られれば FindBoneBreak が見る）。
    /// </summary>
    private static int? EmptyJointSlice(VoxelGrid grid, byte[] original, int axis, (int Low, int High) span, bool distalHigh, bool holdsFarEnd)
    {
        var slices = axis == 0 ? grid.Dimensions.X : axis == 1 ? grid.Dimensions.Y : grid.Dimensions.Z;
        var filled = new int[slices];
        var had = new int[slices];
        for (var i = 0; i < grid.CellCount; i++)
        {
            var (x, y, z) = grid.Coordinates(i);
            var s = axis == 0 ? x : axis == 1 ? y : z;
            filled[s] += grid.MaterialAt(i) != 0 ? 1 : 0;
            had[s] += original[i] != 0 ? 1 : 0;
        }

        var next = distalHigh ? span.High + 1 : span.Low - 1;
        return holdsFarEnd && next >= 0 && next < slices && filled[next] == 0 && had[next] > 0 ? next : null;
    }

    /// <summary>
    /// 打撃の曲げで部位 <paramref name="bodyId"/> の骨が折れた（<see cref="VoxelContactResult.BentFor"/>）: そのスライスで部位を 2 つに分け、
    /// 切れ目を返す。肉は断たれていないので、ふつうは肉の橋がつないだまま（遠い側はぶら下がる）。知らない部位なら null。
    /// </summary>
    public BoneBreak? BreakBent(int bodyId, VoxelBreak bend)
    {
        if (!_byBody.TryGetValue(bodyId, out var part) || part.SeverAxis is not { } axis || axis != bend.Axis)
        {
            return null;
        }

        return BreakAt(part, axis, bend.Slice, bent: true);
    }

    private BoneBreak BreakAt(VoxelPart part, int axis, int slice, bool bent)
    {
        var body = part.Body;
        var grid = body.Grid;
        // 切る前に、切れ目をまたいでつないでいる肉を数える（切った後は 2 つの格子に分かれて見えない）。
        var bridge = grid.Bridge(axis, slice);
        var cell = grid.CellSize;
        var along = axis == 0 ? cell.X : axis == 1 ? cell.Y : cell.Z;
        var step = (axis == 0 ? Vector3.UnitX : axis == 1 ? Vector3.UnitY : Vector3.UnitZ) * along * (part.DistalHigh ? 1f : -1f);
        var full = Enumerable.Range(0, grid.CellCount).Count(i =>
        {
            var c = grid.Coordinates(i);
            var far = grid.CellAt(grid.CellCenter(c.X, c.Y, c.Z) + step);
            return (axis == 0 ? c.X : axis == 1 ? c.Y : c.Z) == slice && far >= 0 &&
                grid.Palette[_originals[part].Materials[i]].Tissue == VoxelTissue.Flesh &&
                grid.Palette[_originals[part].Materials[far]].Tissue == VoxelTissue.Flesh;
        });
        var samples = new List<(int Index, Vector3 Far, float Tear, float Bend)>();
        var area = grid.CellVolume / along;
        float tear = 0f, bend = 0f;
        foreach (var i in bridge)
        {
            var strength = grid.Palette[grid.MaterialAt(i)].MechanicsOrDefault.Strength;
            var (x, y, z) = grid.Coordinates(i);
            var at = grid.CellCenter(x, y, z);
            var across = axis == 0 ? new Vector2(at.Y, at.Z) : axis == 1 ? new Vector2(at.X, at.Z) : new Vector2(at.X, at.Y);
            var far = at + step;
            var farIndex = grid.CellAt(far);
            if (grid.Palette[grid.MaterialAt(i)].Tissue != VoxelTissue.Flesh || farIndex < 0 || grid.Palette[grid.MaterialAt(farIndex)].Tissue != VoxelTissue.Flesh)
            {
                continue;
            }
            samples.Add((i, Vector3.Transform(far, body.GridToWorld()), strength * area, strength * area * across.Length()));
            tear += strength * area;
            bend += strength * area * across.Length();
        }

        // 切れ目: 骨の軸（格子の中心線）の上の、切れ目のスライスの遠い側の面。
        var half = axis == 0 ? grid.HalfExtents.X : axis == 1 ? grid.HalfExtents.Y : grid.HalfExtents.Z;
        var face = -half + ((slice + (part.DistalHigh ? 1 : 0)) * along);
        var local = axis == 0 ? new Vector3(face, 0f, 0f) : axis == 1 ? new Vector3(0f, face, 0f) : new Vector3(0f, 0f, face);
        var point = Vector3.Transform(local, body.GridToWorld());
        var piece = body.SplitAt(axis, slice, part.DistalHigh);
        VoxelPart? fragment = null;
        if (piece is not null)
        {
            piece.BeamAxis = axis; // 破片も骨で曲げを受ける梁（武器のように Snap で飛ばない。破片はそれ以上は分けない）
            fragment = new VoxelPart(part.Kind, piece, VoxelVolume.For(piece.Grid), axis, part.DistalHigh, body.BodyId);
            _fragments.Add(fragment);
        }

        _holdsFarEnd.Remove(body.BodyId); // 遠い側の関節は破片の側へ移る（何も残っていなければ外れる）
        var hangs = fragment is not null;
        FleshBridge? tracked = null;
        if (piece is not null)
        {
            Matrix4x4.Invert(piece.GridToWorld(), out var inverse);
            tracked = new FleshBridge(body, piece, samples.Select(c => new FleshBridge.Cell(c.Index,
                piece.Grid.CellAt(Vector3.Transform(c.Far, inverse)), c.Tear, c.Bend)).ToArray(), full);
        }
        return new BoneBreak(part, fragment, point, hangs ? samples.Count : 0, hangs ? tear : 0f, hangs ? bend : 0f, bent) { Bridge = tracked };
    }

    /// <summary>格子 <paramref name="grid"/> の骨の、軸 <paramref name="axis"/> に沿った両端のスライス（骨が無ければ null）。</summary>
    private static (int Low, int High)? BoneSpan(VoxelGrid grid, int axis)
    {
        int low = int.MaxValue, high = -1;
        for (var i = 0; i < grid.CellCount; i++)
        {
            var m = grid.AtIndex(i);
            if (m != 0 && grid.Palette[m].Tissue == VoxelTissue.Bone)
            {
                var (x, y, z) = grid.Coordinates(i);
                var s = axis == 0 ? x : axis == 1 ? y : z;
                low = Math.Min(low, s);
                high = Math.Max(high, s);
            }
        }

        return high < 0 ? null : (low, high);
    }
}
