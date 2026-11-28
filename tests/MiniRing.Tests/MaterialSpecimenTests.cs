using System.Globalization;
using System.Numerics;
using System.Text;
using Aigis.Foundation.Voxels;
using Aigis.Physics.Voxels;
using MiniRing.Materials;

namespace MiniRing.Tests;

/// <summary>
/// L1 の試験片（2026-11-28、物理の憲法 §1 L1 のテスト・地図 §5 の順 2）: 剛体の世界を作らずに、材料ごとに引張・圧縮・曲げ・切断・押し込みを回す。
/// <list type="bullet">
/// <item>(a) 実物の範囲: 材料の表の<b>人の値</b>（相似の倍率を掛ける前、<see cref="MaterialTable"/> の <c>Human*</c>）を文献の範囲と比べる。範囲の外の物は
/// 「既知の外れ」（<see cref="KnownOutside"/>）として記録し、今は直さない。外れの組が変わったら落ちる（直ったら一覧から消す・新しく外れたら原因を書く）。
/// 試合で使う値（× <see cref="MaterialTable.StressSimilitude"/> = 0.40）は応力がみな実物の 4 割で、範囲の外は記録だけ（原因は見えない相似の倍率 <see cref="MaterialTable.Doll"/>、DEBT「実物大の相似の換算」）。</item>
/// <item>(b) 相似: 寸法を k 倍（セルの数は同じ）すると、折れる力・潰れる力は k² 倍、切る仕事は k³ 倍。曲げは本物の式（<see cref="VoxelBending.WorstBend"/>）で確かめる。
/// セルを細かくしても（同じ大きさの棒を 2 倍の細かさで）折れる力が大きく変わらない。</item>
/// <item>(c) 順序: 潰れる強さは 鉄 &gt; 骨 &gt; 木（木目に直角）&gt; 皮膚 &gt; 筋 &gt; 臓器 &gt; 脳（硬い方が壊れない）。</item>
/// <item>(d) 倍率は材料の比を変えない: 試合の値どうしの強さの比 = 人の値どうしの比。</item>
/// </list>
/// 引張・圧縮・切断・押し込みは、<see cref="VoxelContact"/> の文書にある圧力のモデル（押す力 = 運動エネルギー / たわむ距離、圧力 = 力 / 触れた面積、
/// 圧力 &gt; 強さで降伏し 1 m 進むごとに 強さ × 面積 の仕事）をそのまま書いた純関数（<see cref="Specimen"/>）。接触の掃き（剛体が要る）はここでは回さない。
/// </summary>
[TestClass]
[TestCategory("L1")]
public sealed class MaterialSpecimenTests
{
    public TestContext TestContext { get; set; } = null!;

    /// <summary>試験片の式（L1 の局所の構成則。<see cref="VoxelContact"/> の文書のモデル）。</summary>
    private static class Specimen
    {
        /// <summary>断面 <paramref name="area"/> [m²] の棒が引張で切れる力 [N]（引張の強さを持たない材料は紐の応力: 受動 ＋ 能動）。</summary>
        public static float TensionBreakForce(VoxelMechanics m, float area) =>
            (m.Tensile ?? ((m.PassiveTie ?? 0f) + (m.ActiveTie ?? 0f))) * area;

        /// <summary>面積 <paramref name="area"/> を押して降伏し始める力 [N]（圧縮・押し込み）。</summary>
        public static float YieldForce(VoxelMechanics m, float area) => m.Strength * area;

        /// <summary>触れた面積 <paramref name="area"/> の刃が深さ <paramref name="depth"/> [m] まで切り進む仕事 [J]。</summary>
        public static float CutWork(VoxelMechanics m, float area, float depth) => m.Strength * area * depth;

        /// <summary>運動エネルギー <paramref name="energy"/> [J] の打撃が両方のたわむ距離で止まるときの力 [N]。</summary>
        public static float StopForce(float energy, VoxelMechanics a, VoxelMechanics b) => energy / (a.Give + b.Give);
    }

    /// <summary>文献の範囲。<paramref name="Property"/>: density・strength（降伏・潰れ・破断の応力）・tensile（引張・曲げの破断）・stiffness（ヤング率）・activeTie。</summary>
    private sealed record Range(string Material, string Property, float Low, float High, string Source);

    private static readonly (string Name, VoxelMechanics Human, VoxelMechanics Game)[] Materials =
    [
        ("skin", MaterialTable.HumanSkin, MaterialTable.Skin),
        ("muscle", MaterialTable.HumanMuscle, MaterialTable.Muscle),
        ("bone", MaterialTable.HumanBone, MaterialTable.Bone),
        ("organ", MaterialTable.HumanOrgan, MaterialTable.Organ),
        ("brain", MaterialTable.HumanBrain, MaterialTable.Brain),
        ("hair", MaterialTable.HumanSkin, MaterialTable.Skin), // 髪は皮膚の値（BoxerAnatomy の表）
        ("cloth", MaterialTable.HumanCloth, MaterialTable.Cloth),
        ("iron", MaterialTable.HumanIron, MaterialTable.Iron),
        ("wood", MaterialTable.HumanWood, MaterialTable.Wood),
    ];

    // 脂肪の材料は表に無い（皮下脂肪は皮膚・筋のセルに含めている）。足すときはここに範囲を置く（密度 900〜950 kg/m³: Duck 1990）。
    private static readonly Range[] Literature =
    [
        new("skin", "density", 1020f, 1150f, "Duck, Physical Properties of Tissue (1990) 表 4.1"),
        new("skin", "strength", 5e6f, 30e6f, "引張の破断 5〜30 MPa: Ní Annaidh ら 2012 J Mech Behav Biomed Mater 5:139・Edwards & Marks 1995"),
        new("muscle", "density", 1040f, 1090f, "Duck 1990"),
        new("muscle", "strength", 0.1e6f, 1.0e6f, "受動の破断 0.1〜1 MPa: Yamada, Strength of Biological Materials (1970)"),
        new("muscle", "activeTie", 0.2e6f, 0.4e6f, "比張力 0.2〜0.4 MPa: Fukunaga ら 2001・Powell ら 1984"),
        new("bone", "density", 1800f, 2000f, "皮質骨: Duck 1990・Currey, Bones (2002)"),
        new("bone", "strength", 130e6f, 225e6f, "皮質骨の圧縮の破断: Reilly & Burstein 1975 J Biomech 8:393"),
        new("bone", "tensile", 100e6f, 200e6f, "皮質骨の曲げ強さ: Currey 2002"),
        new("bone", "stiffness", 15e9f, 20e9f, "Reilly & Burstein 1975"),
        new("organ", "density", 1040f, 1070f, "肝臓・心臓: Duck 1990"),
        new("organ", "strength", 0.02e6f, 0.3e6f, "肝臓の破断の幅（要確認）: Yamada 1970・Kemper ら 2010 Stapp"),
        new("brain", "density", 1030f, 1050f, "Duck 1990"),
        new("brain", "strength", 0.003e6f, 0.05e6f, "脳の破断の幅（要確認）: Miller & Chinzei 2002 J Biomech"),
        new("hair", "density", 1300f, 1320f, "Robbins, Chemical and Physical Behavior of Human Hair (2012)"),
        new("hair", "strength", 150e6f, 270e6f, "毛 1 本の引張の破断: Robbins 2012"),
        new("cloth", "strength", 10e6f, 80e6f, "織物（綿・ポリエステル）の帯の引張の破断の目安（要確認）"),
        new("iron", "density", 7750f, 8050f, "炭素鋼: ASM Handbook Vol.1"),
        new("iron", "strength", 350e6f, 1500e6f, "降伏: ASM Handbook Vol.1（焼き入れで上側）"),
        new("iron", "tensile", 600e6f, 2000e6f, "引張: ASM Handbook Vol.1"),
        new("iron", "stiffness", 190e9f, 215e9f, "ASM Handbook Vol.1"),
        new("wood", "density", 600f, 800f, "樫: USDA Wood Handbook FPL-GTR-190 表 5-3a"),
        new("wood", "strength", 4e6f, 10e6f, "木目に直角の圧縮: Wood Handbook 表 5-3a"),
        new("wood", "tensile", 80e6f, 120e6f, "曲げ強さ: Wood Handbook 表 5-3a"),
        new("wood", "stiffness", 10e9f, 14e9f, "Wood Handbook 表 5-3a"),
    ];

    /// <summary>
    /// 人の値が実物の範囲の外にある所（2026-11-28 に測った）。原因:
    /// skin.strength 1.2 MPa: ボクセルの刃先が実物より鈍い（3.5 mm）ので、肉の切れにくさを本物より低く合わせた（MaterialTable の注釈）。表の値は「降伏する圧力」で、文献は引張の破断。
    /// bone.strength 25 MPa: 潰れる圧力を打撃の削れ方に合わせた値で、皮質骨の圧縮の破断（130〜225 MPa）より 1 桁低い（曲げで折れる強さ tensile は範囲の中）。
    /// brain.strength 0.12 MPa: 脳を臓器より少し弱い程度にした値で、文献より数倍強い。
    /// hair.*: 髪は皮膚の値の近道（毛の束を 1 セルの皮膚として持つ）。
    /// cloth.strength 1.2 MPa: 布は皮膚と同じ打たれ強さにする決まり（着ても強さが変わらない）。
    /// </summary>
    private static readonly HashSet<string> KnownOutside =
    [
        "skin.strength", "bone.strength", "brain.strength", "hair.density", "hair.strength", "cloth.strength",
    ];

    private static float? Value(VoxelMechanics m, string property) => property switch
    {
        "density" => m.Density,
        "strength" => m.Strength,
        "tensile" => m.Tensile,
        "stiffness" => m.Stiffness,
        "activeTie" => m.ActiveTie,
        _ => throw new ArgumentOutOfRangeException(nameof(property), property, null),
    };

    [TestMethod]
    public void HumanValues_AgainstTheLiterature_OnlyTheKnownOnesFallOutside()
    {
        var table = new StringBuilder("| 材料.量 | 人の値 | 試合の値 | 実物の範囲 | 人の値 | 出典 |\n|---|---|---|---|---|---|\n");
        var outside = new HashSet<string>();
        foreach (var range in Literature)
        {
            var (_, human, game) = Materials.Single(m => m.Name == range.Material);
            var h = Value(human, range.Property) ?? throw new AssertFailedException($"{range.Material} has no {range.Property}");
            var g = Value(game, range.Property)!.Value;
            var key = $"{range.Material}.{range.Property}";
            var inside = h >= range.Low && h <= range.High;
            if (!inside)
            {
                outside.Add(key);
            }

            table.Append(CultureInfo.InvariantCulture, $"| {key} | {h:G4} | {g:G4} | {range.Low:G3}〜{range.High:G3} | {(inside ? "中" : "外")} | {range.Source} |\n");
        }

        TestContext.WriteLine(table.ToString());
        var newlyOutside = outside.Except(KnownOutside).ToList();
        var nowInside = KnownOutside.Except(outside).ToList();
        Assert.IsEmpty(newlyOutside, "新しく実物の範囲の外へ出た（原因を KnownOutside の注釈に書くか、値を直す）: " + string.Join(", ", newlyOutside));
        Assert.IsEmpty(nowInside, "範囲の中へ戻った（KnownOutside から消す）: " + string.Join(", ", nowInside));
    }

    [TestMethod]
    public void TheSimilitudeFactor_KeepsEveryRatioBetweenMaterials()
    {
        // (d) 見えない倍率は全部の応力に同じ値を掛けるので、材料どうしの比（壊れ方の非対称）は人の値と同じ。密度は掛けない。
        foreach (var (name, human, game) in Materials)
        {
            Assert.AreEqual(MaterialTable.StressSimilitude, game.Strength / human.Strength, 1e-4f, name);
            if (human.Tensile is { } t)
            {
                Assert.AreEqual(MaterialTable.StressSimilitude, game.Tensile!.Value / t, 1e-4f, name);
            }
        }

        Assert.AreEqual(MaterialTable.HumanIron.Strength / MaterialTable.HumanWood.Strength, MaterialTable.Iron.Strength / MaterialTable.Wood.Strength, 1e-2f);
        TestContext.WriteLine($"試合の応力 = 人の値 × {MaterialTable.StressSimilitude:0.0000}（見えない倍率、DEBT「実物大の相似の換算」）、たわむ距離 × {MaterialTable.GiveSimilitude:0.000}");
    }

    [TestMethod]
    public void HarderMaterials_YieldLater_InTheOrderOfTheirStrength()
    {
        // (c) 潰れる強さの順: 硬い方が壊れない（圧力は柔らかい方の強さで頭打ちになる）。
        string[] order = ["iron", "bone", "wood", "skin", "muscle", "organ", "brain"];
        var strengths = order.Select(n => Materials.First(m => m.Name == n).Game.Strength).ToArray();
        for (var i = 1; i < strengths.Length; i++)
        {
            Assert.IsGreaterThan(strengths[i], strengths[i - 1], $"{order[i - 1]} は {order[i]} より潰れにくい");
        }

        // 同じ打撃（同じ力）を鉄と木の間で受けると、降伏の圧力に先に届くのは木だけ。
        var area = 1e-4f;
        var force = Specimen.YieldForce(MaterialTable.Wood, area) * 1.5f;
        Assert.IsTrue(force / area > MaterialTable.Wood.Strength && force / area < MaterialTable.Iron.Strength);
    }

    [TestMethod]
    public void TensionCompressionCutAndIndentation_FollowTheSimilarityLaw()
    {
        // (b) 寸法を k 倍: 断面は k²、切る深さも k 倍。力は k²、仕事は k³。押し込み: 同じ力を 1/10 の面積に集めると 10 倍の圧力（応力の集中）。
        foreach (var (name, _, m) in Materials)
        {
            const float s = 0.01408f; // 基準のセル
            foreach (var k in new[] { 0.5f, 2f, 3f })
            {
                var a1 = s * s;
                var ak = a1 * k * k;
                Assert.AreEqual(k * k, Specimen.YieldForce(m, ak) / Specimen.YieldForce(m, a1), 1e-3f, name);
                if (Specimen.TensionBreakForce(m, a1) > 0f)
                {
                    Assert.AreEqual(k * k, Specimen.TensionBreakForce(m, ak) / Specimen.TensionBreakForce(m, a1), 1e-3f, name);
                }

                Assert.AreEqual(k * k * k, Specimen.CutWork(m, ak, s * k) / Specimen.CutWork(m, a1, s), 1e-2f, name);
            }

            var flat = 1e-4f;
            var edge = flat / 10f;
            Assert.AreEqual(10f, Specimen.YieldForce(m, flat) / Specimen.YieldForce(m, edge), 1e-3f, name);
        }

        // 打撃が止まる力は、両方のたわむ距離の和で決まる（柔らかい詰め物を挟むと力が下がる）。
        Assert.IsLessThan(Specimen.StopForce(10f, MaterialTable.Skin, MaterialTable.Hand), Specimen.StopForce(10f, MaterialTable.Skin, MaterialTable.Padding));
    }

    /// <summary>材料 1 種の、長さ <paramref name="cells"/>.X の角棒（セルの寸法 <paramref name="cell"/>）。</summary>
    private static VoxelGrid Bar(VoxelMechanics m, (int X, int Y, int Z) cells, float cell)
    {
        var material = new VoxelMaterial("specimen", VoxelTissue.Solid, 1f, false, Vector4.One, Vector4.One, Mechanics: m);
        var palette = new VoxelPalette([new VoxelMaterial("empty", VoxelTissue.Empty, 0f, false, Vector4.Zero, Vector4.Zero), material]);
        return new VoxelGrid(palette, new Vector3(cells.X, cells.Y, cells.Z) * (cell / 2f), cells, (_, _) => 1);
    }

    /// <summary>真ん中を横から押して折れる力 [N]（<see cref="VoxelBending.WorstBend"/> の比は力に比例する）。</summary>
    private static float BendBreakForce(VoxelGrid bar)
    {
        var push = new Vector3(0f, -100f, 0f);
        var worst = VoxelBending.WorstBend(bar, Vector3.Zero, push) ?? throw new AssertFailedException("the bar carries no section");
        return push.Length() / worst.Ratio;
    }

    [TestMethod]
    public void ThreePointBending_OfABar_ScalesAsTheSquareOfItsSize_AndMatchesBeamTheory()
    {
        var log = new StringBuilder("| 材料 | セル [mm] | 折れる力 [N] | 梁の式 [N] | k 倍の比 / k² |\n|---|---|---|---|---|\n");
        foreach (var (name, _, m) in Materials.Where(m => m.Game.Tensile is not null))
        {
            const float s = 0.01f;
            var baseForce = BendBreakForce(Bar(m, (16, 4, 4), s));
            // 自由な棒の真ん中を押す: 慣性が一様に受け、中央の曲げモーメントは F L / 8。σ = M c / I、正方形の断面 b: I = b⁴/12・c = b/2。
            var (length, side) = (16 * s, 4 * s);
            var theory = m.Tensile!.Value * (side * side * side / 6f) * 8f / length;
            Assert.AreEqual(theory, baseForce, theory * 0.35f, $"{name}: 曲げの式が梁の理論から離れすぎ");

            foreach (var k in new[] { 2f, 3f })
            {
                var scaled = BendBreakForce(Bar(m, (16, 4, 4), s * k));
                Assert.AreEqual(k * k, scaled / baseForce, k * k * 1e-3f, $"{name}: 寸法 ×{k} で折れる力は ×{k * k}");
                log.Append(CultureInfo.InvariantCulture, $"| {name} | {s * k * 1000f:0} | {scaled:G4} | {theory * k * k:G4} | {scaled / baseForce / (k * k):0.0000} |\n");
            }

            // 同じ大きさの棒を 2 倍細かいセルで: 折れる力は大きく変わらない（セルの寸法で法則が変わらない）。
            var finer = BendBreakForce(Bar(m, (32, 8, 8), s / 2f));
            Assert.AreEqual(baseForce, finer, baseForce * 0.25f, $"{name}: セルを細かくしても折れる力が同じ程度");
            log.Append(CultureInfo.InvariantCulture, $"| {name} | {s * 1000f:0} | {baseForce:G4} | {theory:G4} | 細かい格子 {finer:G4} |\n");
        }

        TestContext.WriteLine(log.ToString());
    }
}
