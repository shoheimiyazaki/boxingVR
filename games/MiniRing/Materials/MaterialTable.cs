using Aigis.Foundation.Voxels;

namespace MiniRing.Materials;

/// <summary>
/// 材料の表（物理の憲法 L1。docs/PHYSICS_CONSTITUTION.md §1 L1）: ボクセルの材質ごとの物性（密度・降伏する圧力・たわむ距離・引張・ヤング率・紐の応力・擦り減り）
/// と削れにくさ。2026-11-28 に <c>BoxerAnatomy</c>（ゲームの体の解剖 = どこに何を置くか）から分けた。値は分ける前と同じ（<see cref="Version"/> 1）。
/// <para>
/// この層は体の部位・試合・団体・キャラを知らない（`ArchitectureRulesTests` が見る）。体のどこにどの材料を置くか・色は <c>BoxerAnatomy</c> が持つ。
/// </para>
/// <para>
/// 人の値は <c>Human*</c>（実物の値。出典は値の横）、試合で使う値はそれに相似の倍率（<see cref="Doll"/>）を掛けた物。倍率は見えない倍率で、憲法の P1 を破る近道
/// （docs/DEBT.md「実物大の相似の換算」）。試験片のテスト（`MaterialSpecimenTests`）が、人の値と実物の範囲・倍率の効きを記録する。
/// </para>
/// </summary>
public static class MaterialTable
{
    /// <summary>
    /// 材料の版（L1）。材料の値（この表）か、材料の局所の式（`Aigis.Physics.Voxels` の圧力・曲げ）を変えて試合の結果が変わるなら上げる。
    /// 体の版（L2〜L3）は <c>Fighter.BodyVersion</c>。履歴は docs/VERSIONS.md「MaterialVersion」。
    /// 1: 2026-11-28、体の版 107 のときの表のまま、体の版から分けた。
    /// </summary>
    public const int Version = 1;

    // ── 押されたときのふるまい（VoxelContact）: 密度 [kg/m³]・降伏する圧力 [Pa]・たわむ距離 [m]・（武器だけ）折れる曲げ応力とヤング率 [Pa]。
    // 体の値は、人形の単位（〜2026-11）で剣（0.085kg）の刃が 6 m/s では骨で止まり 16 m/s で骨まで断つように合わせた。今（PhysicsVersion 93、
    // セルは奥行きを斬り抜けた分だけ断つ）は日本刀（0.61kg）の物打ちで前腕の骨 12 m/s・首と腿 22〜26 m/s（docs/BLADE_CUT_STUDY.md）。
    // ボクセルの刃先は本物よりずっと鈍い（3.5mm）ので、肉の値は本物の切れにくさより低い。
    //
    // 人形の相似則: 人形の体は人のおよそ 0.32 倍（腕の長さの比、<see cref="Similitude"/>）。同じ重力の下で釣り合う動きは時間が √0.32 倍、
    // 速さが √0.32 倍になり、ぶつかる物の運動エネルギーは大きさの 4 乗で小さくなる（剣は人の剣のおよそ 1/100）。一方で材質の強さ（圧力）を
    // 人の値のままにすると、壊すのに要る仕事は大きさの 3 乗でしか小さくならず、人形の剣は人の剣より何倍も斬れない（頭蓋で 1 層止まる）。
    // 力学的に似た（相似の）壊れ方にするには、応力の次元を持つ値を大きさに比例して小さくする（応力 ∝ ρv² ∝ 大きさ）。強さ・引張・
    // ヤング率に <see cref="Similitude"/> を掛ける（鉄と木も同じ割合なので、材質の強さの比 = 壊れ方の非対称は変わらない）。
    // 皮膚の擦り減り（Wear）: 破れない打撃でも、1 発ごとに丈夫さの一部が減る（皮膚の強さいっぱいの押しで 1 発 12%、半分の押しで 3%）。
    // 同じ所を打たれ続けると赤くなっていき（血がにじむ）、やがて裂ける（顔のカット）。グローブの試合で測って決めた（docs/VOXEL_WEAPONS.md）。

    /// <summary>皮膚の強さいっぱいで押す 1 発が擦り減らす、丈夫さの割合（<see cref="VoxelMechanics.Wear"/>）。</summary>
    public const float SkinWear = 0.12f;

    // ── 曲げを支える肉（2026-11-20、PhysicsVersion 61）: 腕・脚・首の梁（VoxelBending.FindBreak）では、骨のセルだけが曲げの応力を持つが、伸ばされる側の
    // 筋肉・皮膚は紐として先に曲げを受け持つ（紐の応力 × 面積 × 断面の中心からの距離まで。受けきれない分だけ骨へ行く）。首の柱は椎骨と椎間板・靭帯の
    // 連なりで、骨が歪むより先に周りの肉が張って支える。筋肉の分は体の張り（VoxelBody.TieActivation = Fighter.Tone × 神経の働き）に比例する:
    // 力の入った首は強く、気を失った首は受動の分だけ。肉が削れれば紐のセルが減り、その分だけ支えも減る（見える欠損 = 弱さ、CLAUDE.md §1.7）。

    /// <summary>
    /// 筋肉が全力で縮んだときの引張の応力 [Pa]（人の値。<see cref="Doll"/> で相似則を掛ける）: 骨格筋の最大等尺張力（比張力）は生理学的断面積あたり
    /// 0.3〜0.4 MPa（例: Fukunaga ら、Powell ら）。その真ん中の 0.35 MPa。紐として数えるのは梁の断面にある筋肉のセルの面積（線維の向きは梁の向きと見る）。
    /// </summary>
    public const float MuscleActiveTie = 0.35e6f;

    /// <summary>
    /// 張りに依らない受動の引張の応力 [Pa]（人の値）: 筋膜・腱鞘・靭帯・皮膚が、骨が折れる手前のひずみ（数 %）で受け持つ分。軟組織の応力 – ひずみの曲線の
    /// つま先の領域（コラーゲンが伸びきる前）で、筋の受動張力（平行弾性要素、至適長で最大等尺張力の 1 割前後）と同じ程度の 0.035 MPa
    /// （= <see cref="MuscleActiveTie"/> の 1 割）。靭帯のセルは無いので、筋肉・皮膚のセルに薄く持たせる。小さいので、気を失った首はほぼ骨だけで支える。
    /// </summary>
    public const float SoftTissuePassiveTie = 0.035e6f;

    /// <summary>人形の体の大きさの、人に対する比（腕の長さの比 0.17 m / 0.53 m）。2026-11-20 まで（人形の物理）材質の強さの相似則に使った値。</summary>
    public const float Similitude = 0.32f;

    /// <summary>
    /// 段 2（相似の換算、REAL_SCALE_PLAN §2.3）の応力の倍率: 人形のときの <see cref="Similitude"/> に、人形 → 実物大の応力の相似の倍率
    /// M/(L T²) = 10 / 2.817² = 1.260 を掛けた 0.4033（人の値の 4 割）。今の壊れ方を保つための近道で、段 3 で 1 にする（docs/DEBT.md）。
    /// </summary>
    public const float StressSimilitude = Similitude * 1.2602f;

    /// <summary>段 2 のたわむ距離の倍率: 表の値（皮膚 5 mm など）は人形の体のセル（5 mm）で合わせた長さなので、実物大では × L = 2.817。段 3 で人の値（× 1）へ。</summary>
    public const float GiveSimilitude = 2.8169f;

    /// <summary>
    /// 人の材質の値を、段 2 の相似則で今の人形と同じ壊れ方にする（強さ・引張・ヤング率・紐の応力 × <see cref="StressSimilitude"/>、
    /// たわむ距離 × <see cref="GiveSimilitude"/>）。密度は実物のまま（2026-11-27 まで × 0.4474 の相似の倍率を掛けていたが、体重をボクセルで実測するので外した:
    /// docs/plans/BASE_BODY_PLAN.md §3）。
    /// </summary>
    public static VoxelMechanics Doll(VoxelMechanics human) => human with
    {
        Give = human.Give * GiveSimilitude,
        Strength = human.Strength * StressSimilitude,
        Tensile = human.Tensile * StressSimilitude,
        Stiffness = human.Stiffness * StressSimilitude,
        PassiveTie = human.PassiveTie * StressSimilitude,
        ActiveTie = human.ActiveTie * StressSimilitude,
    };

    // ── 人の値（相似の倍率を掛ける前）。試験片のテストが実物の範囲と比べる。静的な初期化は書いた順なので、試合の値より先に置く。

    /// <summary>皮膚（人の値）。</summary>
    public static VoxelMechanics HumanSkin { get; } = new(1100f, 1.2e6f, 0.005f, Wear: SkinWear, PassiveTie: SoftTissuePassiveTie);

    /// <summary>骨格筋（人の値）。</summary>
    public static VoxelMechanics HumanMuscle { get; } = new(1060f, 0.3e6f, 0.005f, PassiveTie: SoftTissuePassiveTie, ActiveTie: MuscleActiveTie);

    // 骨は腕・脚の梁として曲げを受ける（肉は質量だけ。VoxelBody.BeamAxis）: 打撃の曲げが骨の強さを超えた所で折れる（VoxelBending。武器が折れる
    // のと同じモデル）。値は皮質骨（曲げて折れる強さ 150 MPa・ヤング率 17 GPa）そのもの（相似則だけ掛ける）。骨の形は人の CT から作った本物の太さ
    // （PhysicsVersion 63、docs/plans/CT_BODY_PLAN.md）。それより前の骨は本物より太い棒（前腕で 1 cm 角対 4 mm 2 本）で、断面係数・断面二次モーメントの
    // 比（0.076・0.031）を掛けた実効値にしていた。測った（CtBoneProbe.BreakingForce_OldAndCt）真ん中を折る力: 前腕 89 → 145 N・上腕 138 → 349 N・
    // すね 248 → 248 N・腿 424 → 138 N（腿は CT の大腿骨の真ん中の断面が 2〜3 セルしかなく、本物より弱め: docs/DEBT.md「左右の部位の格子と CT の体」）。

    /// <summary>皮質骨（人の値）。</summary>
    public static VoxelMechanics HumanBone { get; } = new(1900f, 25e6f, 0.0005f, Tensile: 150e6f, Stiffness: 17e9f);

    /// <summary>脊髄（人の値）: 柔らかく弱い神経組織（脳より少し弱い）。</summary>
    public static VoxelMechanics HumanCord { get; } = new(1040f, 0.05e6f, 0.005f);

    /// <summary>臓器（心臓・肺・肝臓・腸。人の値）。</summary>
    public static VoxelMechanics HumanOrgan { get; } = new(1050f, 0.2e6f, 0.005f);

    /// <summary>脳・目（人の値）。</summary>
    public static VoxelMechanics HumanBrain { get; } = new(1040f, 0.12e6f, 0.005f);

    /// <summary>布（トランクス・帯。人の値の扱い）。</summary>
    public static VoxelMechanics HumanCloth { get; } = new(1100f, 1.2e6f, 0.005f);

    /// <summary>革（靴。人の値の扱い）。</summary>
    public static VoxelMechanics HumanLeather { get; } = new(1100f, 2.0e6f, 0.004f);

    /// <summary>グローブの革（密度は近道: <see cref="GloveLeather"/>）。</summary>
    public static VoxelMechanics HumanGloveLeather { get; } = new(492f, 2.0e6f, 0.004f);

    /// <summary>グローブの詰め物（発泡材。密度は近道: <see cref="Padding"/>）。</summary>
    public static VoxelMechanics HumanPadding { get; } = new(53.7f, 0.15e6f, 0.008f);

    /// <summary>焼き入れした鋼（人の値）: 降伏 600MPa、引張 1.5GPa、ヤング率 200GPa、密度 7800 kg/m³。</summary>
    public static VoxelMechanics HumanIron { get; } = new(7800f, 600e6f, 0.0002f, Tensile: 1.5e9f, Stiffness: 200e9f);

    /// <summary>樫（人の値。出典は <see cref="Wood"/>）。</summary>
    public static VoxelMechanics HumanWood { get; } = new(700f, 8e6f, 0.001f, Tensile: 105e6f, Stiffness: 12.3e9f);

    /// <summary>手の組織（2026-11-25 までの木の値。近道: <see cref="Hand"/>）。</summary>
    public static VoxelMechanics HumanHand { get; } = new(313f, 8e6f, 0.001f, Tensile: 60e6f, Stiffness: 10e9f);

    // ── 試合で使う値（人の値 × 相似の倍率）。

    /// <summary>皮膚（髪・唇も同じ値）。</summary>
    public static VoxelMechanics Skin { get; } = Doll(HumanSkin);

    /// <summary>骨格筋（動脈の壁も同じ値）。</summary>
    public static VoxelMechanics Muscle { get; } = Doll(HumanMuscle);

    /// <summary>皮質骨。</summary>
    public static VoxelMechanics Bone { get; } = Doll(HumanBone);

    /// <summary>脊髄。</summary>
    public static VoxelMechanics Cord { get; } = Doll(HumanCord);

    /// <summary>臓器。</summary>
    public static VoxelMechanics Organ { get; } = Doll(HumanOrgan);

    /// <summary>脳・目。</summary>
    public static VoxelMechanics Brain { get; } = Doll(HumanBrain);

    /// <summary>布。</summary>
    public static VoxelMechanics Cloth { get; } = Doll(HumanCloth);

    /// <summary>革。</summary>
    public static VoxelMechanics Leather { get; } = Doll(HumanLeather);

    /// <summary>
    /// グローブの革（表と親指・縁取り）: 強さは革のまま、密度 492 kg/m³ は近道（2026-11-27 まで全材料に掛けていた相似の 0.4474 × 1100 と同じ値を明示）。
    /// グローブの格子は革の殻が実物（1〜2 mm）より厚い（セル 1 つぶん）ので、革の密度のままだと 1.1 kg（実物の 16 oz は 0.45 kg）になる（GloveShapeTests）。
    /// 殻の厚みを実物へ直すまで、質量を前のまま保つ（docs/DEBT.md「キャラクター」(1)）。
    /// </summary>
    public static VoxelMechanics GloveLeather { get; } = Doll(HumanGloveLeather);

    /// <summary>グローブの詰め物（発泡材）: 軽く（120 kg/m³）、弱い圧力で潰れ（0.15 MPa）、大きくたわむ（8 mm）。拳の衝撃を広げて和らげる。</summary>
    /// <remarks>密度 54 kg/m³ は 2026-11-27 まで掛けていた相似の 0.4474 × 120 を明示した値（グローブの質量を前のまま保つ近道: <see cref="GloveLeather"/>）。</remarks>
    public static VoxelMechanics Padding { get; } = Doll(HumanPadding);

    /// <summary>
    /// 剣の鋼: 焼き入れした鋼（人で降伏 600MPa、引張 1.5GPa、ヤング率 200GPa。強さ・たわみは人形の相似則で縮める）。密度だけは実物の 7800 kg/m³
    /// （<see cref="WeaponDensity"/>）。
    /// </summary>
    public static VoxelMechanics Iron { get; } = WeaponDensity(Doll(HumanIron), 7800f);

    /// <summary>
    /// 武器の材料（鋼・樫）の密度を実物に戻す（2026-11-25、段 3 の一部。docs/plans/FORM_DECAY_STUDY.md）。段 2 の相似の密度の倍率（0.4474、2026-11-27 に体の材料からも外した）を
    /// 掛けたままだと、形は実物大なのに刀 0.61 kg・ロングソード 0.60 kg・六尺棒 0.52 kg と実物の 4〜5 割の重さで、軽い刃が手首の先で暴れた。
    /// 戻すと形を変えずに刀 1.36 kg（実物は拵え込みで 1.0〜1.4 kg）・ロングソード 1.33 kg（1.3〜1.6 kg）・片手剣 1.95 kg（1.0〜1.5 kg: 刃が厚く、重い側に外れる）・
    /// 六尺棒 1.17 kg（1.2〜1.5 kg）・杖 0.54 kg（0.5〜0.7 kg）・盾 4.8 kg（3〜6 kg）になる。<see cref="Doll"/> が密度を変えなくなった（2026-11-27）ので、今は確かめの意味だけ。
    /// </summary>
    private static VoxelMechanics WeaponDensity(VoxelMechanics doll, float realDensity) => doll with { Density = realDensity };

    /// <summary>
    /// 樫（ホワイトオーク、含水率 12 %）: 曲げて折れる強さ（曲げ強さ）105 MPa、ヤング率 12.3 GPa、木目に直角に潰れる強さ 7.4 MPa、比重 0.68
    /// （USDA Forest Products Laboratory『Wood Handbook』FPL-GTR-190 表 5-3a。北米のアカガシワ 99 MPa・12.5 GPa・7.0 MPa・0.63、ホワイトアッシュ 106 MPa・12.0 GPa も同じ程度）。
    /// 潰れる強さ 8 MPa・密度 700 kg/m³ は表の広葉樹（樫・トネリコ: 7.0〜8.0 MPa、706〜762 kg/m³）の幅の中なので前の値のまま（武器の質量・削れ方を変えない）。
    /// 2026-11-25 まで曲げ強さ 60 MPa・ヤング率 10 GPa（針葉樹ほどの値）で、実物の樫の 6 割弱の強さしかなく折れすぎた（docs/VOXEL_WEAPONS.md「木の強さ」）。
    /// 骨（皮質骨 150 MPa・17 GPa）と同じ相似則（<see cref="Doll"/>）で縮める: 木と骨の強さの比は実物どおり。密度だけは実物（<see cref="WeaponDensity"/>）。
    /// </summary>
    public static VoxelMechanics Wood { get; } = WeaponDensity(Doll(HumanWood), 700f);

    /// <summary>
    /// 手の組織（<c>HandSkin</c>・<c>HandMuscle</c>・<c>HandBone</c>）の物性: 強さは 2026-11-25 までの木の値（曲げ強さ 60 MPa・ヤング率 10 GPa）のまま。
    /// 人の手の値ではない近道（docs/DEBT.md「手の物性を木の値で代用」）。武器の木を実物の樫へ直したとき、拳の当たり・削れ方を変えないよう分けた。
    /// 密度 313 kg/m³（2026-11-27 までの木の 700 × 相似の 0.4474 と同じ値）も近道: 手の格子はグローブの中を埋めた形で実物の手の 2〜3 倍の体積があり、
    /// 手の組織の密度（≈ 1100）にすると手＋グローブが 1.1 kg（実物は手 0.45 kg ＋ 10 oz のグローブ 0.28 kg）になる（GloveShapeTests）。手の形を実物へ直すまで質量を前のまま保つ。
    /// </summary>
    public static VoxelMechanics Hand { get; } = Doll(HumanHand);

    // ── 削れにくさ（VoxelMaterial.Toughness: 1 セルを削り取るのに要るダメージ。肉 ≈ 1）。名前の付いた物だけここ。臓器・目などの値は BoxerAnatomy の表の中。

    /// <summary>布と革は皮膚と同じ削れにくさ・同じ組織（肉）にする: 着ても打たれ強さと部位の力は裸のときと変わらない。</summary>
    public const float SkinToughness = 0.7f;

    /// <summary>木の削れにくさ。</summary>
    public const float WoodToughness = 5f;

    /// <summary>手の組織の削れにくさ（木の値の代用。<see cref="Hand"/>）。</summary>
    public const float HandToughness = WoodToughness;
}
