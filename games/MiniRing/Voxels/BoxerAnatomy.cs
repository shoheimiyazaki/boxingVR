using System.Collections.Concurrent;
using System.Numerics;
using System.Runtime.CompilerServices;
using Aigis.Foundation.Voxels;
using MiniRing.LowPoly;
using MiniRing.Materials;

namespace MiniRing.Voxels;

/// <summary>
/// 人形の体の中身（ボクセルの材質の並び）。部位の箱ごとに、皮膚と肉・骨・臓器を置く。
/// <para>
/// 座標は部位のローカル（箱の中心が原点。<c>n</c> は箱の半径で割った −1..1）。体の前は +Z、本人の左は +X
/// （<see cref="HandExtensions.LocalSide"/> と同じ）。腕は長さが X、脚は Y、足は Z に沿う（HumanoidRagdoll の決まり）。
/// </para>
/// <list type="bullet">
/// <item>頭: 頭蓋（殻）の中に脳。顔の前に目。</item>
/// <item>胸: 背骨・肋骨（帯）・胸骨の籠の中に、心臓（左寄り）・肺（左右）・肝臓（右下）。</item>
/// <item>腰: 骨盤の環と背骨、腸。</item>
/// <item>腕・脚: 長さに沿った骨の芯、周りは肉。足は骨の板。</item>
/// </list>
/// 外側の 1 層は皮膚。目も臓器の一種（顔に出ている）。目を除く臓器はどれも致命的（<see cref="VoxelMaterial.Vital"/>）:
/// 半分以上壊れると死ぬ（<see cref="BoxerBody"/>）。
/// </summary>
public static class BoxerAnatomy
{
    public const byte Skin = 1;
    public const byte Muscle = 2;
    public const byte Bone = 3;
    public const byte Brain = 4;
    public const byte Heart = 5;
    public const byte Lung = 6;
    public const byte Liver = 7;
    public const byte Gut = 8;
    public const byte Eye = 9;

    // 身なり（外側の 1 層の皮膚の代わり。中身は裸と同じ）: トランクス（赤コーナー・青コーナー）とその腰のゴム、ブーツ。
    public const byte RedTrunks = 10;
    public const byte BlueTrunks = 11;
    public const byte Waistband = 12;
    public const byte Boot = 13;

    /// <summary>髪（頭の上と後ろの外側の層。皮膚の代わり）。</summary>
    public const byte Hair = 14;

    /// <summary>黒目（左右の目それぞれ、白目の表のボクセル 1 つ）。</summary>
    public const byte Pupil = 15;

    /// <summary>身なりの材質（布と革）か。皮膚の代わりの層なので、音・見え方の扱いは皮膚に合わせる。</summary>
    public static bool IsClothing(byte material) => material is >= RedTrunks and <= Boot;

    // 武器の材質（体と同じ表に置き、破片の色・音・染みを体と同じ仕組みで扱う）: 鉄（剣の刃・鍔・柄頭）と木（棍棒・剣の握り）。
    public const byte Iron = 16;
    public const byte Wood = 17;

    // グローブの材質: 表の革（コーナーの色）と、中の詰め物（発泡材）。
    public const byte RedGlove = 18;
    public const byte BlueGlove = 19;
    public const byte Padding = 20;

    // グローブの色違い（物性は革のまま。見た目だけ）: 親指の袋の濃い革（コーナーの色ごと）と、帯・紐・縫い目の白。
    public const byte RedGloveThumb = 27;
    public const byte BlueGloveThumb = 28;
    public const byte GloveTrim = 29;

    /// <summary>唇（顔の口の色の部位。皮膚と同じ強さ・密度・組織なので、試合の物理は変わらない。色だけ肌の色から赤みへ寄せる）。</summary>
    public const byte Lip = 21;

    /// <summary>脊髄（背骨の中を通る神経の束。柔らかく弱い組織）。削れた残りの断面が、その下の筋の力を決める（<see cref="BoxerBody.CordFunction"/>）。</summary>
    public const byte Cord = 22;

    // ── 手（素手の拳・はめる物の中の拳）の組織: 見た目と組織の種類は皮膚・肉・骨のまま、押されたときのふるまい（固さ・強さ・密度）と
    // 削れにくさは木の値（2026-11-18、ユーザーの指定。近道: docs/DEBT.md「手の物性を木の値で代用」）。2026-11-25 に武器の木を実物の樫へ強めたとき、手は前の木の値のまま MaterialTable.Hand に分けた。
    // 14 mm のセルでは手の細かい骨の集まりを表せない（指は 1 セルの骨）。木の値で、拳の当たりの硬さを手の形を保ったまま近似する。

    // 色は皮膚・肉・骨とほとんど同じ（赤か緑を 0.01 ずらす）: 目では同じに見え、.vox の読み戻し（色 → 材質）では区別できる。

    /// <summary>手の皮膚（見た目は皮膚、物性は木）。</summary>
    public const byte HandSkin = 23;

    /// <summary>手の肉（掌の厚み。見た目は肉、物性は木）。</summary>
    public const byte HandMuscle = 24;

    /// <summary>手の骨（中手骨・手根骨・指の骨。見た目は骨、物性は木）。</summary>
    public const byte HandBone = 25;

    /// <summary>
    /// 動脈（大動脈と、その枝のうち CT で管として追える太さのもの。<c>tools/ct-body</c> が CT から置く。生成の体には無い）。
    /// 壁は肉と同じ強さ・密度の柔らかい組織で、致命的ではない（出血のモデルが無いので、切れても死なない。近道: docs/DEBT.md「左右の部位の格子と CT の体」）。
    /// 血の染みは乗せない（削れて出てきた管が赤い線のまま見えるように）。
    /// </summary>
    public const byte Artery = 26;

    /// <summary>皮膚か手の皮膚か（表の層: 擦り減り・染み・音の扱い）。</summary>
    public static bool IsSkinLike(byte material) => material is Skin or HandSkin;

    /// <summary>骨か手の骨か。</summary>
    public static bool IsBoneLike(byte material) => material is Bone or HandBone;

    // 材料の物性（密度・強さ・たわみ…）と削れにくさは L1 の材料の表（Materials/MaterialTable.cs）にある。ここはどこに何を置くかと色だけ。

    private static Vector4 Rgb(float r, float g, float b) => new(r, g, b, 1f);

    /// <summary>
    /// 材質の表。色は 1 つずつ（皮膚・肉・骨・脳・臓器ごと・目）: 削れて出てきた中身がその色で見える。照明の下でも見分けられるよう、
    /// 互いに離した鮮やかな色にし、血の染みは皮膚（と少しだけ筋肉）にしか乗せない（中身の色が血で隠れないように）。
    /// 目だけは、表に出ている面（黒目）と中（白い眼球）で色が違う。どちらのファイターかはグローブの色で見分ける。
    /// </summary>
    public static VoxelPalette Palette { get; } = new(
    [
        new VoxelMaterial("empty", VoxelTissue.Empty, 1f, false, Vector4.Zero, Vector4.Zero),
        new VoxelMaterial("skin", VoxelTissue.Flesh, MaterialTable.SkinToughness, false, Rgb(0.86f, 0.6f, 0.42f), Rgb(0.86f, 0.6f, 0.42f), Stainable: 1f, Mechanics: MaterialTable.Skin),
        new VoxelMaterial("muscle", VoxelTissue.Flesh, 1f, false, Rgb(0.8f, 0.05f, 0.07f), Rgb(0.8f, 0.05f, 0.07f), Stainable: 0.3f, Mechanics: MaterialTable.Muscle),
        new VoxelMaterial("bone", VoxelTissue.Bone, 8f, false, Rgb(1f, 0.98f, 0.88f), Rgb(1f, 0.98f, 0.88f), Stainable: 0f, Mechanics: MaterialTable.Bone),
        new VoxelMaterial("brain", VoxelTissue.Organ, 1.4f, true, Rgb(1f, 0.4f, 0.8f), Rgb(1f, 0.4f, 0.8f), Stainable: 0f, Mechanics: MaterialTable.Brain),
        new VoxelMaterial("heart", VoxelTissue.Organ, 1.4f, true, Rgb(0.55f, 0f, 0.35f), Rgb(0.55f, 0f, 0.35f), Stainable: 0f, Mechanics: MaterialTable.Organ),
        new VoxelMaterial("lung", VoxelTissue.Organ, 1.2f, true, Rgb(0.45f, 0.8f, 1f), Rgb(0.45f, 0.8f, 1f), Stainable: 0f, Mechanics: MaterialTable.Organ),
        new VoxelMaterial("liver", VoxelTissue.Organ, 1.4f, true, Rgb(0.36f, 0.13f, 0.04f), Rgb(0.36f, 0.13f, 0.04f), Stainable: 0f, Mechanics: MaterialTable.Organ),
        new VoxelMaterial("gut", VoxelTissue.Organ, 1.2f, true, Rgb(1f, 0.82f, 0.25f), Rgb(1f, 0.82f, 0.25f), Stainable: 0f, Mechanics: MaterialTable.Organ),
        new VoxelMaterial("eye", VoxelTissue.Organ, 0.8f, false, Rgb(1f, 1f, 1f), Rgb(1f, 1f, 1f), Stainable: 0f, Mechanics: MaterialTable.Brain),
        new VoxelMaterial("red trunks", VoxelTissue.Flesh, MaterialTable.SkinToughness, false, Rgb(1f, 0.24f, 0.18f), Rgb(1f, 0.24f, 0.18f), Stainable: 0.5f, Mechanics: MaterialTable.Cloth),
        new VoxelMaterial("blue trunks", VoxelTissue.Flesh, MaterialTable.SkinToughness, false, Rgb(0.08f, 0.2f, 0.72f), Rgb(0.08f, 0.2f, 0.72f), Stainable: 0.5f, Mechanics: MaterialTable.Cloth),
        new VoxelMaterial("waistband", VoxelTissue.Flesh, MaterialTable.SkinToughness, false, Rgb(0.95f, 0.95f, 0.9f), Rgb(0.95f, 0.95f, 0.9f), Stainable: 0.8f, Mechanics: MaterialTable.Cloth),
        new VoxelMaterial("boot", VoxelTissue.Flesh, MaterialTable.SkinToughness, false, Rgb(0.1f, 0.1f, 0.11f), Rgb(0.1f, 0.1f, 0.11f), Stainable: 0.3f, Mechanics: MaterialTable.Leather),
        new VoxelMaterial("hair", VoxelTissue.Flesh, MaterialTable.SkinToughness, false, Rgb(0.13f, 0.08f, 0.05f), Rgb(0.13f, 0.08f, 0.05f), Stainable: 0.2f, Mechanics: MaterialTable.Skin),
        new VoxelMaterial("pupil", VoxelTissue.Organ, 0.8f, false, Rgb(0.02f, 0.02f, 0.03f), Rgb(0.02f, 0.02f, 0.03f), Stainable: 0f, Mechanics: MaterialTable.Brain),
        new VoxelMaterial("iron", VoxelTissue.Solid, 50f, false, Rgb(0.72f, 0.74f, 0.78f), Rgb(0.82f, 0.84f, 0.88f), Stainable: 0.6f, Mechanics: MaterialTable.Iron),
        new VoxelMaterial("wood", VoxelTissue.Solid, MaterialTable.WoodToughness, false, Rgb(0.78f, 0.6f, 0.38f), Rgb(0.5f, 0.32f, 0.16f), Stainable: 0.7f, Mechanics: MaterialTable.Wood),
        new VoxelMaterial("red glove", VoxelTissue.Solid, MaterialTable.SkinToughness, false, Rgb(0.85f, 0.1f, 0.1f), Rgb(0.85f, 0.1f, 0.1f), Stainable: 0.6f, Mechanics: MaterialTable.GloveLeather),
        new VoxelMaterial("blue glove", VoxelTissue.Solid, MaterialTable.SkinToughness, false, Rgb(0.1f, 0.25f, 0.85f), Rgb(0.1f, 0.25f, 0.85f), Stainable: 0.6f, Mechanics: MaterialTable.GloveLeather),
        new VoxelMaterial("padding", VoxelTissue.Solid, 1f, false, Rgb(0.92f, 0.9f, 0.82f), Rgb(0.92f, 0.9f, 0.82f), Stainable: 0.4f, Mechanics: MaterialTable.Padding),
        new VoxelMaterial("lip", VoxelTissue.Flesh, MaterialTable.SkinToughness, false, Rgb(0.8f, 0.4f, 0.38f), Rgb(0.8f, 0.4f, 0.38f), Stainable: 1f, Mechanics: MaterialTable.Skin),
        new VoxelMaterial("cord", VoxelTissue.Flesh, 0.5f, false, Rgb(0.5f, 1f, 0.15f), Rgb(0.5f, 1f, 0.15f), Stainable: 0f, Mechanics: MaterialTable.Cord),
        new VoxelMaterial("hand skin", VoxelTissue.Flesh, MaterialTable.HandToughness, false, Rgb(0.87f, 0.6f, 0.42f), Rgb(0.87f, 0.6f, 0.42f), Stainable: 1f, Mechanics: MaterialTable.Hand),
        new VoxelMaterial("hand muscle", VoxelTissue.Flesh, MaterialTable.HandToughness, false, Rgb(0.81f, 0.05f, 0.07f), Rgb(0.81f, 0.05f, 0.07f), Stainable: 0.3f, Mechanics: MaterialTable.Hand),
        new VoxelMaterial("hand bone", VoxelTissue.Bone, MaterialTable.HandToughness, false, Rgb(1f, 0.97f, 0.88f), Rgb(1f, 0.97f, 0.88f), Stainable: 0f, Mechanics: MaterialTable.Hand),
        new VoxelMaterial("artery", VoxelTissue.Flesh, 1f, false, Rgb(1f, 0f, 0.25f), Rgb(1f, 0f, 0.25f), Stainable: 0f, Mechanics: MaterialTable.Muscle),
        new VoxelMaterial("red glove thumb", VoxelTissue.Solid, MaterialTable.SkinToughness, false, Rgb(0.58f, 0.06f, 0.07f), Rgb(0.58f, 0.06f, 0.07f), Stainable: 0.6f, Mechanics: MaterialTable.GloveLeather),
        new VoxelMaterial("blue glove thumb", VoxelTissue.Solid, MaterialTable.SkinToughness, false, Rgb(0.06f, 0.16f, 0.58f), Rgb(0.06f, 0.16f, 0.58f), Stainable: 0.6f, Mechanics: MaterialTable.GloveLeather),
        new VoxelMaterial("glove trim", VoxelTissue.Solid, MaterialTable.SkinToughness, false, Rgb(0.97f, 0.97f, 0.94f), Rgb(0.97f, 0.97f, 0.94f), Stainable: 0.8f, Mechanics: MaterialTable.GloveLeather),
    ]);

    /// <summary>腰のゴム: 骨盤の外形の上からこの高さ（正規化 Y）より上。</summary>
    private const float WaistbandFrom = 0.55f;

    /// <summary>トランクスの裾: 太ももの外形のこの高さ（正規化 Y、+1 が付け根）より上を覆う。太ももの半ばより少し下まで。</summary>
    private const float TrunksHemAt = -0.15f;

    /// <summary>ブーツの履き口: すねの外形のこの高さ（正規化 Y、−1 が足首）より下を覆う。ボクシングシューズの丈（すねの半ば）。</summary>
    private const float BootTopAt = 0.05f;

    /// <summary>
    /// 部位 <paramref name="kind"/> の箱を、<paramref name="cellSize"/> [m] ほどのボクセルで埋める。<paramref name="shape"/>
    /// （部位のローカルの閉じたメッシュ、ローポリの体）を渡すと、その内側だけを埋める（外は空）。
    /// <paramref name="outfit"/> を渡すと、外側の皮膚の層のうち、トランクスとブーツの所をその材質にする（<see cref="Dress"/>）。
    /// </summary>
    /// <remarks><c>overrides</c>: ボクセル編集ソフトで直した中身（<see cref="BoxerVoxelModel"/>）。この部位の直しがあり、セルの数が同じなら生成せずそれを使う。</remarks>
    public static VoxelGrid Build(BodyPartKind kind, Vector3 halfExtents, float cellSize, LowPolyMesh? shape = null, Outfit? outfit = null, BoxerVoxelOverrides? overrides = null)
    {
        var dims = VoxelGrid.CellsFor(halfExtents, cellSize);
        if (overrides?.For(kind, dims) is { } edited)
        {
            return WithJointEnds(kind, FromEdited(halfExtents, dims, edited, outfit));
        }

        var inside = shape is null ? null : InsideMask(shape, halfExtents, dims);
        var cell = 2f * halfExtents / new Vector3(dims.X, dims.Y, dims.Z);
        // 骨・臓器の配置は「体の外形」を −1..1 とした座標で決める。形を渡されたらその外形（頂点の範囲）、無ければ箱。
        // 箱のままだと、箱より細いメッシュの腕がほとんど骨になる。
        var (center, half) = shape is null ? (Vector3.Zero, halfExtents) : Bounds(shape);
        var grid = new VoxelGrid(Palette, halfExtents, dims, (p, _) =>
        {
            var n = (p - center) / half;
            if (inside is not null)
            {
                var c = (p + halfExtents) / cell;
                var i = (int)c.X + (dims.X * ((int)c.Y + (dims.Y * (int)c.Z)));
                if (!inside[i])
                {
                    return 0;
                }
            }

            return Fill(kind, n);
        });

        // 外側の 1 層は皮膚（目だけは顔に出たまま）。身なりがあれば、その皮膚のうち覆われた所は布と革。
        grid.Remap((c, material, surface) =>
        {
            if (!surface || material == Eye)
            {
                return material;
            }

            var n = (grid.CellCenter(c.X, c.Y, c.Z) - center) / half;
            if (kind == BodyPartKind.Head && IsHair(n))
            {
                return Hair; // 頭の上と後ろは髪
            }

            return outfit is null ? Skin : Dress(kind, n, outfit);
        });
        if (kind == BodyPartKind.Head)
        {
            PlacePupils(grid, center, half);
        }

        return WithJointEnds(kind, grid);
    }

    /// <summary>取り除く「離れて残ったセル」の大きさの上限（セル数）。</summary>
    private const int StrayCellLimit = 2;

    /// <summary>
    /// 顔の層へ出した後の頭の格子で、いちばん大きいつながりに 6 近傍でつながらない 1〜2 セルを取り除く（骨・肉・皮膚）。残すと最初の歩で黙って破片に分かれ、
    /// その破片が頭の <c>BodyId</c>（顔の層と同じ剛体）を「断たれた部位」にして、頭と顔の層の再生が全部止まった（OrgHealing）。
    /// 首の隙間を埋める <see cref="JointBridge"/> は頭の下の縁の皮膚のセルを使うので、離れた皮膚のセルを外すと隙間のセルの数が、親のまわりの有り無しで食い違う
    /// （JointBridgeTests.TheNeckGap: 30 対 22。隙間の埋め方が頭の縁の細かいセルに敏感な別件で、BASE_BODY_PLAN §10.6）。つながっていれば何もしない。
    /// </summary>
    internal static void DropStrayCells(VoxelGrid grid)
    {
        if (grid.IsConnected())
        {
            return;
        }

        var parts = grid.ConnectedComponents();
        var main = parts.OrderByDescending(p => p.Length).First();
        var removed = new List<RemovedVoxel>();
        foreach (var part in parts.Where(p => !ReferenceEquals(p, main) && p.Length <= StrayCellLimit))
        {
            foreach (var i in part)
            {
                grid.Break(i, removed);
            }
        }
    }

    /// <summary>
    /// 脚の骨の端（膝・股の側の骨端）を、骨の胴の部分と同じ太さにする。人の骨は関節の所がいちばん太い。脚の外形（直した .vox も生成も）は
    /// 膝・股で丸く絞れていて、骨の棒が端で 1〜2 セルまで細り、そこが曲げで折れて脚が丸ごと落ちていた（2026-11-17、ユーザーの指摘
    /// 「脚がもげやすい」。脚の折れ 16 件のうち 12 件が端の段: <c>LegLossProbe.Probe_HowLegsBreak</c>）。
    /// どれかの段に骨のある所の和（骨の軸に直角な断面の形。生成の骨の棒なら骨の胴の断面）を、骨のある段の全部へ置き、その外側の空いたセルを、その段の外側の皮膚
    /// （トランクス・ブーツならそれ）と同じ材質の 1 層で包む。腿・すね以外はそのまま返す。何度かけても同じ（2 度目は何も変えない）。
    /// </summary>
    private static VoxelGrid WithJointEnds(BodyPartKind kind, VoxelGrid grid)
    {
        if (kind is not (BodyPartKind.Thigh or BodyPartKind.Shin))
        {
            return grid;
        }

        var (nx, ny, nz) = grid.Dimensions;
        var materials = new byte[nx * ny * nz];
        int Index(int x, int y, int z) => x + (nx * (y + (ny * z)));
        // 脚の骨の軸は Y。骨の胴の断面は、骨のある段のうち骨のセルの数が真ん中（中央値）の段の並び（生成の骨の棒・CT を入れる前の .vox なら棒の断面そのもの）。
        // それを、両端から続いて胴より細い段（外形が絞れて骨が痩せた端）だけに置く。CT の骨（曲がった大腿骨・太い骨端）は端が胴より太いので何も変わらない
        // （以前は全部の段の骨の和を全部の段に置いていて、段ごとに位置のずれる CT の大腿骨が太い柱になった: 122 → 702 セル）。2 度かけても同じ。
        var slices = Enumerable.Range(0, ny).Select(y => Footprint(grid, y)).ToArray();
        var boneSlices = Enumerable.Range(0, ny).Where(y => slices[y].Count > 0).ToArray();
        if (boneSlices.Length == 0)
        {
            return grid;
        }

        var shaft = boneSlices.OrderBy(y => slices[y].Count).ThenBy(y => y).ElementAt(boneSlices.Length / 2);
        var footprint = slices[shaft].OrderBy(c => c.Z).ThenBy(c => c.X).ToList();
        var thin = new List<int>();
        foreach (var run in new[] { boneSlices, boneSlices.Reverse().ToArray() })
        {
            foreach (var y in run.TakeWhile(y => slices[y].Count < footprint.Count))
            {
                thin.Add(y);
            }
        }

        for (var z = 0; z < nz; z++)
        {
            for (var y = 0; y < ny; y++)
            {
                for (var x = 0; x < nx; x++)
                {
                    materials[Index(x, y, z)] = grid.At(x, y, z);
                }
            }
        }

        // 段の外側の材質（皮膚・トランクス・ブーツ）: 骨・肉でない、いちばん多いもの。
        byte CoverOf(int y) => Enumerable.Range(0, nz).SelectMany(z => Enumerable.Range(0, nx).Select(x => grid.At(x, y, z)))
            .Where(m => m != 0 && m != Bone && m != Muscle).GroupBy(m => m).OrderByDescending(g => g.Count()).ThenBy(g => g.Key).FirstOrDefault()?.Key ?? Skin;
        var changed = false;
        var (low, high) = (boneSlices.Min(), boneSlices.Max());
        // 骨の端の外（骨の軸の向き）も 1 層で包む: 端の段の骨が、絞れた次の段から外に出ないように。材質は端の段の外側のもの。太らせた端だけ。
        foreach (var (y, from) in new[] { (low - 1, low), (high + 1, high) })
        {
            if (y < 0 || y >= ny || !thin.Contains(from))
            {
                continue;
            }

            var cap = CoverOf(from);
            foreach (var (x, z) in footprint.Where(c => materials[Index(c.X, y, c.Z)] == 0))
            {
                materials[Index(x, y, z)] = cap;
                changed = true;
            }
        }

        foreach (var y in thin)
        {
            var cover = CoverOf(y);
            foreach (var (x, z) in footprint)
            {
                if (materials[Index(x, y, z)] != Bone)
                {
                    materials[Index(x, y, z)] = Bone;
                    changed = true;
                }

                for (var dz = -1; dz <= 1; dz++)
                {
                    for (var dx = -1; dx <= 1; dx++)
                    {
                        var (sx, sz) = (x + dx, z + dz);
                        if (sx >= 0 && sx < nx && sz >= 0 && sz < nz && materials[Index(sx, y, sz)] == 0)
                        {
                            materials[Index(sx, y, sz)] = cover;
                            changed = true;
                        }
                    }
                }
            }
        }

        if (!changed)
        {
            return grid;
        }

        var cell = 2f * grid.HalfExtents / new Vector3(nx, ny, nz);
        return new VoxelGrid(grid.Palette, grid.HalfExtents, grid.Dimensions, (p, _) =>
        {
            var c = (p + grid.HalfExtents) / cell;
            return materials[Index((int)c.X, (int)c.Y, (int)c.Z)];
        });
    }

    /// <summary>段 <paramref name="y"/>（Y の断面）の骨のセルの (x, z)。</summary>
    private static List<(int X, int Z)> Footprint(VoxelGrid grid, int y)
    {
        var (nx, _, nz) = grid.Dimensions;
        var cells = new List<(int X, int Z)>();
        for (var z = 0; z < nz; z++)
        {
            for (var x = 0; x < nx; x++)
            {
                if (grid.At(x, y, z) == Bone)
                {
                    cells.Add((x, z));
                }
            }
        }

        return cells;
    }

    /// <summary>
    /// 直した中身から格子を作る。トランクス（赤で塗ってある）はコーナーの色に、身なりが無ければ服の所は皮膚にする（生成と同じ）。
    /// 皮膚の層（元の表面）は格子が空との境から決める。
    /// </summary>
    private static VoxelGrid FromEdited(Vector3 halfExtents, (int X, int Y, int Z) dims, byte[] materials, Outfit? outfit)
    {
        var cell = 2f * halfExtents / new Vector3(dims.X, dims.Y, dims.Z);
        return new VoxelGrid(Palette, halfExtents, dims, (p, _) =>
        {
            var c = (p + halfExtents) / cell;
            var m = materials[(int)c.X + (dims.X * ((int)c.Y + (dims.Y * (int)c.Z)))];
            return !IsClothing(m) ? m
                : outfit is null ? Skin
                : m is RedTrunks or BlueTrunks ? outfit.Trunks
                : m;
        });
    }

    /// <summary>
    /// 外側の層の 1 セル（正規化座標 <paramref name="n"/>）が何を着ているか:腰はトランクス（上の帯はゴム）、太ももの上半分はトランクスの裾、
    /// 足とすねの下はブーツ。ほかは皮膚。脚の部位は +Y が体の上（付け根）側。
    /// </summary>
    /// <summary>頭の外側の層の 1 セル（正規化座標 <paramref name="n"/>、+Z が顔）が髪か: 頭の上（額の生え際より上）と後ろ（耳の後ろからうなじの上まで）。</summary>
    public static bool IsHair(Vector3 n) => n.Y > HairlineAt || (n.Z < -0.15f && n.Y > -0.25f);

    /// <summary>髪の生え際の高さ（正規化 Y）。目（0.18 の高さ）より上に額を残す。</summary>
    private const float HairlineAt = 0.45f;

    /// <summary>
    /// 黒目を置く: 左右の目それぞれ、白目の表に出ているボクセルのうち、目の真ん中の前（<see cref="HeadAt"/> の目の中心）にいちばん近い 1 つを黒目にする。
    /// </summary>
    private static void PlacePupils(VoxelGrid grid, Vector3 center, Vector3 half)
    {
        var picks = new (int X, int Y, int Z)?[2];
        var best = new[] { float.MaxValue, float.MaxValue };
        var (dx, dy, dz) = grid.Dimensions;
        for (var z = 0; z < dz; z++)
        {
            for (var y = 0; y < dy; y++)
            {
                for (var x = 0; x < dx; x++)
                {
                    if (grid.At(x, y, z) != Eye || !grid.IsOriginalSurface(x, y, z))
                    {
                        continue;
                    }

                    var n = (grid.CellCenter(x, y, z) - center) / half;
                    var side = n.X > 0f ? 0 : 1;
                    var d = Vector3.DistanceSquared(n, new Vector3((side == 0 ? 1f : -1f) * EyeSide, EyeHeight, 1f));
                    if (d < best[side])
                    {
                        best[side] = d;
                        picks[side] = (x, y, z);
                    }
                }
            }
        }

        grid.Remap((c, material, _) => c == picks[0] || c == picks[1] ? Pupil : material);
    }

    /// <summary>目の中心の横の位置と高さ（正規化座標）。</summary>
    private const float EyeSide = 0.38f;
    private const float EyeHeight = 0.18f;

    public static byte Dress(BodyPartKind kind, Vector3 n, Outfit outfit)
    {
        ArgumentNullException.ThrowIfNull(outfit);
        return kind switch
        {
            BodyPartKind.Pelvis => n.Y > WaistbandFrom ? Waistband : outfit.Trunks,
            BodyPartKind.Thigh when n.Y > TrunksHemAt => outfit.Trunks,
            BodyPartKind.Shin when n.Y < BootTopAt => Boot,
            BodyPartKind.Foot or BodyPartKind.Toe => Boot,
            _ => Skin,
        };
    }

    /// <summary>
    /// どのセルの中心がメッシュの内側か。メッシュ × 格子の大きさごとに 1 度だけ数える（ラウンドごと・両ファイターで同じ）。
    /// 対戦場は試合を並列に回すので、スレッドをまたいで安全な表に置く。
    /// </summary>
    private static bool[] InsideMask(LowPolyMesh shape, Vector3 halfExtents, (int X, int Y, int Z) dims)
    {
        var perMesh = Masks.GetValue(shape, _ => new ConcurrentDictionary<(Vector3, (int, int, int)), bool[]>());
        return perMesh.GetOrAdd((halfExtents, dims), key =>
        {
            var (half, (nx, ny, nz)) = key;
            var cell = 2f * half / new Vector3(nx, ny, nz);
            var mask = new bool[nx * ny * nz];
            for (var z = 0; z < nz; z++)
            {
                for (var y = 0; y < ny; y++)
                {
                    for (var x = 0; x < nx; x++)
                    {
                        mask[x + (nx * (y + (ny * z)))] = shape.Contains(-half + (new Vector3(x + 0.5f, y + 0.5f, z + 0.5f) * cell));
                    }
                }
            }

            return mask;
        });
    }

    private static (Vector3 Center, Vector3 Half) Bounds(LowPolyMesh mesh)
    {
        var min = new Vector3(float.MaxValue);
        var max = new Vector3(float.MinValue);
        foreach (var v in mesh.Vertices)
        {
            min = Vector3.Min(min, v);
            max = Vector3.Max(max, v);
        }

        return ((min + max) * 0.5f, Vector3.Max((max - min) * 0.5f, new Vector3(1e-4f)));
    }

    private static readonly ConditionalWeakTable<LowPolyMesh, ConcurrentDictionary<(Vector3, (int, int, int)), bool[]>> Masks = new();

    /// <summary>正規化座標 <paramref name="n"/>（−1..1）の材質。</summary>
    public static byte Fill(BodyPartKind kind, Vector3 n) => kind switch
    {
        BodyPartKind.Head => HeadAt(n),
        BodyPartKind.Chest => ChestAt(n),
        BodyPartKind.Abdomen => ChestAt(n with { Y = -0.62f + (0.38f * n.Y) }),
        BodyPartKind.Pelvis => PelvisAt(n),
        BodyPartKind.UpperArm or BodyPartKind.Forearm => Rod(n.Y, n.Z, 0.42f),
        BodyPartKind.Thigh or BodyPartKind.Shin => Rod(n.X, n.Z, 0.4f),
        BodyPartKind.Foot or BodyPartKind.Toe => MathF.Abs(n.Y) < 0.45f && MathF.Abs(n.X) < 0.55f && MathF.Abs(n.Z) < 0.85f ? Bone : Muscle,
        _ => Muscle,
    };

    private static byte HeadAt(Vector3 n)
    {
        // 目: 顔の前面、左右に 1 つずつ。
        if (n.Z > 0.55f && MathF.Abs(MathF.Abs(n.X) - EyeSide) < 0.2f && MathF.Abs(n.Y - EyeHeight) < 0.18f)
        {
            return Eye;
        }

        var r = Ellipsoid(n, new Vector3(0f, 0.08f, -0.05f), new Vector3(0.85f, 0.85f, 0.85f));
        return r < 0.74f && n.Y > -0.45f ? Brain
            : r < 0.9f && n.Y > -0.6f ? Bone // 頭蓋
            : n.Y < -0.55f && MathF.Abs(n.X) < 0.6f && n.Z > -0.2f && n.Z < 0.7f && MathF.Abs(n.Y + 0.75f) < 0.12f ? Bone // あご
            : Muscle;
    }

    private static byte ChestAt(Vector3 n)
    {
        // 背骨: 背中側の中央の柱。
        if (MathF.Abs(n.X) < 0.16f && n.Z < -0.5f && n.Z > -0.9f)
        {
            return Bone;
        }

        // 心臓（本人の左 = +X）、肺（左右）、肝臓（本人の右 = −X の下）。
        if (Ellipsoid(n, new Vector3(0.22f, 0.05f, 0.15f), new Vector3(0.3f, 0.3f, 0.45f)) < 1f)
        {
            return Heart;
        }

        if (Ellipsoid(n, new Vector3(-0.42f, -0.62f, 0.05f), new Vector3(0.38f, 0.26f, 0.5f)) < 1f)
        {
            return Liver;
        }

        var lung = MathF.Min(
            Ellipsoid(n, new Vector3(0.45f, 0.25f, -0.05f), new Vector3(0.32f, 0.5f, 0.5f)),
            Ellipsoid(n, new Vector3(-0.45f, 0.25f, -0.05f), new Vector3(0.32f, 0.5f, 0.5f)));
        if (lung < 1f)
        {
            return Lung;
        }

        // 肋骨: 胴を囲む殻を、上下に帯状に（帯の間は肉）。前の中央は胸骨。
        var ring = Ellipsoid(n, Vector3.Zero, new Vector3(0.82f, 1f, 0.8f));
        var band = (int)MathF.Floor((n.Y + 1f) * 4.5f) % 2 == 0;
        if (ring > 0.82f && ring < 1.02f && n.Y > -0.75f && n.Y < 0.85f && (band || (n.Z > 0.6f && MathF.Abs(n.X) < 0.15f)))
        {
            return Bone;
        }

        return Muscle;
    }

    private static byte PelvisAt(Vector3 n)
    {
        if (MathF.Abs(n.X) < 0.16f && n.Z < -0.5f && n.Z > -0.9f)
        {
            return Bone; // 背骨
        }

        if (Ellipsoid(n, new Vector3(0f, 0.1f, 0.2f), new Vector3(0.6f, 0.55f, 0.55f)) < 1f)
        {
            return Gut;
        }

        var ring = Ellipsoid(n, new Vector3(0f, -0.1f, 0f), new Vector3(0.85f, 0.9f, 0.8f));
        return ring > 0.78f && ring < 1.0f && n.Y < 0.3f ? Bone : Muscle; // 骨盤の環
    }

    private static byte Rod(float a, float b, float radius) => (a * a) + (b * b) < radius * radius ? Bone : Muscle;

    private static float Ellipsoid(Vector3 n, Vector3 center, Vector3 radii)
    {
        var d = (n - center) / radii;
        return d.Length();
    }
}

/// <summary>
/// ボクサーの身なり。トランクスの色はコーナーの色（ブーツと腰のゴムは共通）。作れるのは赤と青の 2 つだけ
/// （ほかの材質をトランクスにすると、腰の削れにくさが変わってしまう）。
/// </summary>
public sealed record Outfit
{
    private Outfit(byte trunks) => Trunks = trunks;

    /// <summary>トランクスの材質（<see cref="BoxerAnatomy.RedTrunks"/> か <see cref="BoxerAnatomy.BlueTrunks"/>）。</summary>
    public byte Trunks { get; }

    public static Outfit RedCorner { get; } = new(BoxerAnatomy.RedTrunks);

    public static Outfit BlueCorner { get; } = new(BoxerAnatomy.BlueTrunks);
}
