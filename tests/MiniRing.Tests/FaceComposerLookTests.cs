using MiniRing.Voxels;

namespace MiniRing.Tests;

/// <summary>
/// 顔の見た目（F3）: 目の大きさ・瞳の色・ひげと唇・髪の届く範囲と髪質・顔の形・まぶた・鼻筋の陰・唇の厚み、名簿の顔（人種・国の違いは値でファイルに書く）。
/// 20 x 28 x 20 の格子（頭の箱の参考図と同じ）で組み立てて、正面から見える材質を調べる。docs/CHARACTERS.md §2.0。
/// </summary>
[TestClass]
public sealed class FaceComposerLookTests
{
    private static FaceSpec Spec(params (string Category, string Id, float Weight)[] choice)
    {
        var parts = FacePartLibrary.Categories.ToDictionary(c => c, c => (IReadOnlyDictionary<string, float>)new Dictionary<string, float> { [$"{c}.1"] = 1f });
        parts["beard"] = new Dictionary<string, float>();
        foreach (var group in choice.GroupBy(c => c.Category))
        {
            parts[group.Key] = group.ToDictionary(c => c.Id, c => c.Weight);
        }

        return new FaceSpec(parts, FaceParts.Default);
    }

    private static FaceVolume Compose(params (string Category, string Id, float Weight)[] choice) => FaceComposer.Compose(Spec(choice), 20, 28, 20);

    /// <summary>正面から見える（柱ごとのいちばん前の）材質。[y（上から）][x]。</summary>
    private static FaceMaterial[][] Front(FaceVolume v)
        => [.. Enumerable.Range(0, v.Ny).Reverse().Select(y => Enumerable.Range(0, v.Nx).Select(x =>
            Enumerable.Range(0, v.Nz).Reverse().Select(z => v.At(x, y, z)).FirstOrDefault(m => m != FaceMaterial.Empty)).ToArray())];

    private static readonly string[] KeyParts = ["nose", "hair", "beard", "jaw"];

    private static bool IsEye(FaceMaterial m) => m is FaceMaterial.White or FaceMaterial.Iris;

    private static string Embedded(string name)
    {
        using var s = typeof(FacePartLibrary).Assembly.GetManifestResourceStream($"MiniRing.Voxels.FaceParts.{name}.json")!;
        return new StreamReader(s).ReadToEnd();
    }

    [TestMethod]
    public void Library_HasTheExpectedPartsPerCategory()
    {
        var library = FacePartLibrary.Default;
        // 目 = まぶたの違い 6、口 = 形 6 + 唇の厚み・人中 6、あご = 形 6 + 顔の形 5、髪 = 型 6 + 髪質 6、ひげ = 6 + 頬まで覆う全体のひげ 1、眉の張り出し・鼻筋 4。
        var expected = new Dictionary<string, int> { ["eyes"] = 6, ["brows"] = 6, ["mouth"] = 12, ["nose"] = 6, ["ears"] = 6, ["jaw"] = 11, ["hair"] = 13, ["beard"] = 8, ["browridge"] = 4 };
        foreach (var (category, count) in expected)
        {
            Assert.HasCount(count, library.Parts(category), category);
        }

        Assert.AreEqual("monolid", library.Get("eyes.1").Name);
        Assert.AreEqual("full lips", library.Get("mouth.9").Name);
        Assert.IsGreaterThan(library.Get("hair.2").Reach, library.Get("hair.6").Reach, "an afro adds volume beyond the skull");
    }

    [TestMethod]
    public void Eyes_AreThreeOrFourWide_TwoOrThreeTall_WithAOneCellIris()
    {
        // シンプルな顔（2026-11-20、ユーザーの指示）: 白目は幅 3〜4・高さ 2〜3、黒目は 1 セル。大きさの値（EyeSize）で崩れない。左右は鏡写し。
        foreach (var part in FacePartLibrary.Default.Parts("eyes"))
        {
            foreach (var size in new[] { 0.8f, 1f, 1.3f })
            {
                var spec = Spec(("eyes", part.Id, 1f)) with { Values = FaceParts.Default with { EyeSize = size } };
                var front = Front(FaceComposer.Compose(spec, 20, 28, 20));
                foreach (var (from, to) in new[] { (0, 10), (10, 20) })
                {
                    var cells = Enumerable.Range(0, 28).SelectMany(y => Enumerable.Range(from, to - from).Where(x => IsEye(front[y][x])).Select(x => (y, x))).ToList();
                    var width = cells.Max(c => c.x) - cells.Min(c => c.x) + 1;
                    var height = cells.Max(c => c.y) - cells.Min(c => c.y) + 1;
                    Assert.IsTrue(width is 3 or 4, $"{part.Id} x{size}: eye width {width}");
                    Assert.IsTrue(height is 2 or 3, $"{part.Id} x{size}: eye height {height}");
                    Assert.AreEqual(width * height, cells.Count, $"{part.Id} x{size}: the white is a full rectangle");
                    Assert.AreEqual(1, cells.Count(c => front[c.y][c.x] == FaceMaterial.Iris), $"{part.Id} x{size}: one iris cell");
                }

                for (var y = 0; y < 28; y++)
                {
                    for (var x = 0; x < 10; x++)
                    {
                        Assert.AreEqual(IsEye(front[y][x]), IsEye(front[y][19 - x]), $"{part.Id}: eyes mirror at row {y}");
                    }
                }
            }
        }
    }

    [TestMethod]
    public void IrisDefaultsToDarkBrown_AndEveryMaterialHasAColour()
    {
        var colors = FacePalette.Resolve(Spec());
        var iris = colors[FaceMaterial.Iris];
        Assert.IsGreaterThan(iris.Z, iris.X, "dark brown has more red than blue");
        Assert.IsLessThan(0.3f, iris.X);
        foreach (var m in Enum.GetValues<FaceMaterial>().Where(m => m != FaceMaterial.Empty))
        {
            Assert.IsTrue(colors.ContainsKey(m), m.ToString());
        }

        var blue = FacePalette.Resolve(Spec() with { Iris = new System.Numerics.Vector3(0.35f, 0.49f, 0.61f) })[FaceMaterial.Iris];
        Assert.IsGreaterThan(blue.X, blue.Z, "the spec's iris colour wins over the default");
        Assert.IsLessThan(colors[FaceMaterial.Skin].Y, colors[FaceMaterial.Lip].Y, "lips are redder than the skin");
    }

    [TestMethod]
    public void Hair_AddsAtMostItsReachBeyondTheHead_ExceptAnAfro()
    {
        // 普通の髪は頭の輪郭の外へ 2 セル以内（アフロだけ大きい）。「頭」= 皮膚と顔のセルの範囲。
        static int HairBeyond(string hair)
        {
            var v = Compose(("hair", hair, 1f));
            var skin = Enumerable.Range(0, v.Cells.Length).Where(i => v.Cells[i] is not (FaceMaterial.Empty or FaceMaterial.Hair)).ToList();
            var hairCells = Enumerable.Range(0, v.Cells.Length).Where(i => v.Cells[i] == FaceMaterial.Hair).ToList();
            Assert.IsNotEmpty(hairCells, hair);
            static int X(int i) => i % 20;
            static int Z(int i) => i / 560;
            return Math.Max(Math.Max(skin.Min(X) - hairCells.Min(X), hairCells.Max(X) - skin.Max(X)), Math.Max(skin.Min(Z) - hairCells.Min(Z), hairCells.Max(Z) - skin.Max(Z)));
        }

        foreach (var hair in new[] { "hair.1", "hair.2", "hair.4", "hair.7", "hair.8", "hair.9", "hair.10", "hair.11" })
        {
            Assert.IsLessThanOrEqualTo(2, HairBeyond(hair), hair);
        }

        Assert.IsGreaterThan(HairBeyond("hair.2"), HairBeyond("hair.6"), "an afro adds volume");
    }

    [TestMethod]
    public void HairTexture_RoughensTheOutline_StraightHairDoesNot()
    {
        // 髪質（frizz）: 縮れた髪は頭の外に毛先が出る（まっすぐな髪より髪のセルが多い）。
        int HairCells(string id) => Compose(("hair", id, 1f)).Count(FaceMaterial.Hair);
        Assert.IsGreaterThan(HairCells("hair.7"), HairCells("hair.9"), "loose curls (same silhouette, frizz 0.6) have more hair cells than straight hair");
        Assert.AreEqual(HairCells("hair.9"), HairCells("hair.9"), "deterministic");
    }

    [TestMethod]
    public void Beard_LeavesTheLipsVisible_AndReachesTheChin()
    {
        var plain = Compose(("mouth", "mouth.8", 1f));
        var bearded = Compose(("mouth", "mouth.8", 1f), ("beard", "beard.7", 1f));
        Assert.IsGreaterThan(plain.Count(FaceMaterial.Hair) + 20, bearded.Count(FaceMaterial.Hair), "a full beard adds a lot of hair");
        Assert.AreEqual(plain.Count(FaceMaterial.Lip), bearded.Count(FaceMaterial.Lip), "the beard does not take away any lip cell");
        var front = Front(bearded);
        var lipRows = Enumerable.Range(0, front.Length).Where(y => front[y].Contains(FaceMaterial.Lip)).ToList();
        Assert.IsNotEmpty(lipRows);
        foreach (var y in lipRows)
        {
            for (var x = Array.IndexOf(front[y], FaceMaterial.Lip); x <= Array.LastIndexOf(front[y], FaceMaterial.Lip); x++)
            {
                Assert.AreNotEqual(FaceMaterial.Hair, front[y][x], $"hair between the lips at row {y}, x={x}");
            }
        }

        var beardRows = Enumerable.Range(0, front.Length).Where(y => y >= 11 && front[y].Contains(FaceMaterial.Hair)).ToList();
        Assert.IsGreaterThan(20, beardRows.Max(), "the beard reaches the chin");
    }

    [TestMethod]
    public void FaceShapes_ChangeTheSilhouette_AndTheCraniumStaysRound()
    {
        int Width(string jaw, int row) => Front(Compose(("jaw", jaw, 1f)))[row].Count(m => m != FaceMaterial.Empty);
        // 四角い顔は頭の上でも箱にならない（頭の上の輪郭は基準の丸い頭より外へ出ない）。
        Assert.IsLessThanOrEqualTo(14, Width("jaw.9", 2), "a square face keeps the base head outline (14 cells at row 2), not a box");
        Assert.IsGreaterThanOrEqualTo(Width("jaw.3", 22), Width("jaw.7", 22), "a wide face is at least as wide at the jaw");
        Assert.AreNotEqual(Compose(("jaw", "jaw.8", 1f)).Cells.Count(c => c != FaceMaterial.Empty), Compose(("jaw", "jaw.7", 1f)).Cells.Count(c => c != FaceMaterial.Empty));
    }

    [TestMethod]
    public void EyeParts_DifferInSize()
    {
        int White(string id) => Compose(("eyes", id, 1f)).Count(FaceMaterial.White);
        Assert.IsGreaterThan(White("eyes.2"), White("eyes.3"), "a double eyelid (4x3) shows more white than an epicanthic fold (3x2)");
    }

    [TestMethod]
    public void Nose_HasTwoSeparateNostrils_WithSkinBetween_AndNoBumpBetweenTheEyes()
    {
        // ユーザーの指摘（2026-11-20）: 鼻の穴が 1 本につながる・目の間の出っ張り。鼻は単純な形（NoseShape）: 底の行は [小鼻][穴][鼻柱][穴][小鼻]。
        foreach (var part in FacePartLibrary.Default.Parts("nose"))
        {
            var v = Compose(("nose", part.Id, 1f));
            var front = Front(v);
            var rows = Enumerable.Range(0, 28).Where(y => front[y].Contains(FaceMaterial.Nostril)).ToList();
            Assert.HasCount(1, rows, $"{part.Id}: nostrils on one row");
            var xs = Enumerable.Range(0, 20).Where(x => front[rows[0]][x] == FaceMaterial.Nostril).ToList();
            Assert.HasCount(2, xs, $"{part.Id}: two nostrils");
            Assert.IsGreaterThan(1, xs[1] - xs[0], $"{part.Id}: skin (columella) between the nostrils");
            Assert.AreEqual(19 - xs[0], xs[1], $"{part.Id}: the nostrils mirror");

            // 目の行（白目のある行）では、目の間の柱が目の柱より前へ出ない。
            int Top(int x, int y) => Enumerable.Range(0, 20).Reverse().First(z => v.At(x, 27 - y, z) != FaceMaterial.Empty);
            foreach (var y in Enumerable.Range(0, 28).Where(y => front[y].Any(IsEye)))
            {
                var eye = Enumerable.Range(0, 20).Where(x => IsEye(front[y][x])).Max(x => Top(x, y));
                var between = Enumerable.Range(8, 4).Max(x => Top(x, y));
                Assert.IsLessThanOrEqualTo(eye, between, $"{part.Id}: row {y}: between the eyes ({between}) stands out of the eyes ({eye})");
            }
        }

        int NoseCells(float nose) => FaceComposer.Compose(Spec() with { Values = FaceParts.Default with { Nose = nose } }, 20, 28, 20).Cells.Count(c => c != FaceMaterial.Empty);
        Assert.IsGreaterThan(NoseCells(0.8f), NoseCells(1.3f), "a higher nose stands out more");
    }

    [TestMethod]
    public void Silhouette_HasNoNotches_ForEveryRosterFace()
    {
        // 顔の線に変な凸凹（ユーザーの指摘）: 正面の頭の幅（髪・ひげ・耳を除く）は、隣の行との差が 2 セル以内、頬より下では行ごとに増えない。
        foreach (var c in MiniRing.Characters.CharacterRoster.All)
        {
            var shape = FaceShape.From(c.Face.Values, c.Face.Weighted("jaw", FacePartLibrary.Default));
            int Width(int y) => Enumerable.Range(0, 20).Count(x => Enumerable.Range(0, 20).Any(z => shape.Inside(x + 0.5f, y + 0.5f, z + 0.5f)));
            // 頭頂の丸み（CrownRow より上: 楕円の上端は 1 行で開く）と、あご先の最後の行は除く。
            for (var y = (int)FaceShape.CrownRow + 1; y < (int)shape.ChinBottom - 1; y++)
            {
                Assert.IsLessThanOrEqualTo(2, Math.Abs(Width(y) - Width(y - 1)), $"{c.Id}: row {y} width {Width(y)} vs {Width(y - 1)}");
                if (y > 15)
                {
                    Assert.IsLessThanOrEqualTo(Width(y - 1), Width(y), $"{c.Id}: the face widens again at row {y}");
                }
            }
        }
    }

    [TestMethod]
    public void Brows_AreContinuous_AndOneCellAwayFromTheEyes()
    {
        foreach (var c in MiniRing.Characters.CharacterRoster.All)
        {
            var front = Front(FaceComposer.Compose(c.Face, 20, 28, 20));
            foreach (var (from, to) in new[] { (0, 10), (10, 20) })
            {
                var brow = Enumerable.Range(0, 28).SelectMany(y => Enumerable.Range(from, to - from).Where(x => front[y][x] == FaceMaterial.Brow).Select(x => (y, x))).ToHashSet();
                if (brow.Count == 0)
                {
                    continue;
                }

                var seen = new HashSet<(int, int)>();
                var stack = new Stack<(int, int)>();
                stack.Push(brow.First());
                while (stack.Count > 0)
                {
                    var (y, x) = stack.Pop();
                    if (brow.Contains((y, x)) && seen.Add((y, x)))
                    {
                        foreach (var (dy, dx) in new[] { (1, 0), (-1, 0), (0, 1), (0, -1) })
                        {
                            stack.Push((y + dy, x + dx));
                        }
                    }
                }

                Assert.HasCount(brow.Count, seen, $"{c.Id}: one connected brow");
                foreach (var (y, x) in brow)
                {
                    for (var dy = -1; dy <= 1; dy++)
                    {
                        for (var dx = -1; dx <= 1; dx++)
                        {
                            var (ay, ax) = (y + dy, x + dx);
                            if (ay is >= 0 and < 28 && ax is >= 0 and < 20)
                            {
                                Assert.IsFalse(IsEye(front[ay][ax]), $"{c.Id}: brow touches an eye at row {y}");
                            }
                        }
                    }
                }
            }
        }
    }

    [TestMethod]
    public void Lips_FullnessChangesTheLipArea()
    {
        int Lips(string id) => Compose(("mouth", id, 1f)).Count(FaceMaterial.Lip);
        Assert.IsGreaterThan(Lips("mouth.7"), Lips("mouth.9"), "full lips cover more than thin lips");
        Assert.IsGreaterThan(Lips("mouth.9"), Lips("mouth.10"), "very full more than full");
    }

    [TestMethod]
    public void Stubble_IsAUniformSkinTone_NotScatteredHairCells()
    {
        var stubble = Compose(("beard", "beard.1", 1f));
        Assert.AreEqual(Compose().Count(FaceMaterial.Hair), stubble.Count(FaceMaterial.Hair), "stubble adds no hair cells (only the scalp hair of the default spec)");
        Assert.IsGreaterThan(20, stubble.Count(FaceMaterial.Stubble));
        // 無精ひげの色は肌と髪の間（一様）。
        var c = FacePalette.Resolve(Spec());
        Assert.IsLessThan(c[FaceMaterial.Skin].X, c[FaceMaterial.Stubble].X);
        Assert.IsGreaterThan(c[FaceMaterial.Hair].X, c[FaceMaterial.Stubble].X);
        Assert.IsGreaterThan(30, Compose(("beard", "beard.7", 1f)).Count(FaceMaterial.Hair));
    }

    [TestMethod]
    public void FullBeard_IsOneConnectedRegion_FromSideburnsToChin()
    {
        var front = Front(Compose(("mouth", "mouth.8", 1f), ("beard", "beard.7", 1f)));
        bool IsBeard(int y, int x) => y >= 11 && front[y][x] == FaceMaterial.Hair;
        var seen = new HashSet<(int, int)>();
        var stack = new Stack<(int, int)>();
        var start = Enumerable.Range(0, 28).SelectMany(y => Enumerable.Range(0, 20).Select(x => (y, x))).First(t => t.y >= 23 && IsBeard(t.y, t.x));
        stack.Push(start);
        while (stack.Count > 0)
        {
            var (y, x) = stack.Pop();
            if (y < 0 || y >= 28 || x < 0 || x >= 20 || !IsBeard(y, x) || !seen.Add((y, x)))
            {
                continue;
            }

            foreach (var (dy, dx) in new[] { (1, 0), (-1, 0), (0, 1), (0, -1) })
            {
                stack.Push((y + dy, x + dx));
            }
        }

        var all = Enumerable.Range(0, 28).Sum(y => Enumerable.Range(0, 20).Count(x => IsBeard(y, x)));
        Assert.AreEqual(all, seen.Count, "the beard is one region (jaw, chin, cheeks, moustache, sideburns)");
    }

    [TestMethod]
    public void Brows_AreTwo_WithAGapBetween()
    {
        var front = Front(Compose(("brows", "brows.6", 1f)));
        for (var y = 0; y < front.Length; y++)
        {
            Assert.IsFalse(front[y][9] == FaceMaterial.Brow && front[y][10] == FaceMaterial.Brow, $"unibrow at row {y}");
            Assert.AreNotEqual(FaceMaterial.Brow, front[y][9]);
            Assert.AreNotEqual(FaceMaterial.Brow, front[y][10]);
        }
    }

    [TestMethod]
    public void LongHair_HangsBesideTheFace_AndBehindTheEar()
    {
        var front = Front(Compose(("hair", "hair.12", 1f)));
        // 目の高さから下の顔の横（頭の縁の外の列）に髪が垂れる。
        Assert.IsGreaterThan(8, Enumerable.Range(13, 12).Count(y => front[y][0] == FaceMaterial.Hair || front[y][1] == FaceMaterial.Hair));
        var cropFront = Front(Compose(("hair", "hair.2", 1f)));
        Assert.AreEqual(0, Enumerable.Range(13, 12).Count(y => cropFront[y][0] == FaceMaterial.Hair || cropFront[y][1] == FaceMaterial.Hair));
        var v = Compose(("hair", "hair.13", 1f));
        // 横から見て、後ろ（z 小さい側）の低い所（首の高さ）にも髪が下がる（結んだ尾）。
        Assert.IsGreaterThan(5, Enumerable.Range(0, 8).Count(y => Enumerable.Range(0, 4).Any(z => Enumerable.Range(0, 20).Any(x => v.At(x, y, z) == FaceMaterial.Hair))));
    }

    [TestMethod]
    public void Face_HasNoShadingLines()
    {
        // 鼻の脇・眉の脇の影の線は、低い解像度でしわに見えた（2026-11-20）。顔の真ん中（耳の外側を除く）には影の皮膚が無い。
        var front = Front(Compose(("browridge", "browridge.3", 1f), ("nose", "nose.5", 1f)));
        for (var y = 0; y < 28; y++)
        {
            for (var x = 3; x < 17; x++)
            {
                Assert.AreNotEqual(FaceMaterial.Shadow, front[y][x], $"shading at row {y}, x={x}");
            }
        }
    }

    [TestMethod]
    public void Load_StopsOnABadFrizzOrDensity()
    {
        var all = FacePartLibrary.Categories.ToDictionary(c => c, Embedded);
        var baseJson = Embedded("base");
        var badFrizz = all.ToDictionary(p => p.Key, p => p.Value);
        badFrizz["hair"] = all["hair"].Replace("\"frizz\": 0.85", "\"frizz\": 2", StringComparison.Ordinal);
        Assert.AreNotEqual(all["hair"], badFrizz["hair"]);
        Assert.ThrowsExactly<InvalidDataException>(() => FacePartLibrary.Load(baseJson, badFrizz));
        var badDensity = all.ToDictionary(p => p.Key, p => p.Value);
        badDensity["beard"] = all["beard"].Replace("\"density\": 0.45", "\"density\": 0", StringComparison.Ordinal);
        Assert.AreNotEqual(all["beard"], badDensity["beard"]);
        Assert.ThrowsExactly<InvalidDataException>(() => FacePartLibrary.Load(baseJson, badDensity));
    }

    [TestMethod]
    public void Roster_EveryoneHasAFaceWithColours_AndTheFacesAreAllDifferent()
    {
        var faces = new HashSet<string>();
        foreach (var c in MiniRing.Characters.CharacterRoster.All)
        {
            Assert.IsNotNull(c.Face.Skin, c.Id + " skin");
            Assert.IsNotNull(c.Face.Hair, c.Id + " hair");
            Assert.IsNotNull(c.Face.Iris, c.Id + " iris");
            var v = FaceComposer.Compose(c.Face, 20, 28, 20);
            faces.Add(string.Concat(v.Cells.Select(m => (char)('a' + (int)m))) + c.Face.Skin + c.Face.Hair + c.Face.Iris);
        }

        Assert.HasCount(MiniRing.Characters.CharacterRoster.All.Count, faces, "no two fighters share a face");
    }

    [TestMethod]
    public void Roster_RegionalVariation_IsInTheValuesNotTheCode()
    {
        // 沖縄・日本本土・韓国のキャラは、まぶた・眉の張り出し・顔の幅の連続の値が違う（コードに国の分岐は無い）。
        var roster = MiniRing.Characters.CharacterRoster.All.ToDictionary(c => c.Id);
        float Weight(string id, string category, string part) => roster[id].Face.Parts[category].GetValueOrDefault(part);
        Assert.IsGreaterThan(Weight("jin", "eyes", "eyes.3"), Weight("akira", "eyes", "eyes.3"), "Okinawan: more double eyelid than mainland");
        Assert.IsGreaterThan(Weight("taeyun", "browridge", "browridge.4"), Weight("akira", "browridge", "browridge.4"), "Okinawan: stronger brow ridge than Korean");
        Assert.IsGreaterThan(Weight("akira", "browridge", "browridge.1"), Weight("taeyun", "browridge", "browridge.1"), "Korean: flatter brow and bridge");
        Assert.IsGreaterThan(roster["jin"].Face.Values.FaceWidth, roster["tsuyoshi"].Face.Values.FaceWidth, "Okinawan: wider face");
        Assert.IsGreaterThan(Weight("taeyun", "eyes", "eyes.2"), Weight("taeyun", "eyes", "eyes.1"), "Korean: monolid");

        // ヨーロッパ人どうしも違う（鼻・髪・ひげの部品の組、瞳の色）。
        var europeans = new[] { "albert", "dmitri", "edgar", "gaston", "konrad", "sean" };
        string Key(string id) => string.Join('|', KeyParts.Select(c => string.Join(',', roster[id].Face.Parts[c].Keys)));
        Assert.HasCount(europeans.Length, europeans.Select(Key).Distinct().ToList());
        Assert.IsGreaterThan(3, europeans.Select(id => roster[id].Face.Iris).Distinct().Count());
    }
}
