using MiniRing.Bots;
using MiniRing.Characters;
using MiniRing.Desktop;

namespace MiniRing.Tests;

/// <summary>
/// E0: 教育が空なら、名簿を 1 人 1 ファイルにしても試合はビット単位で同じ（CLAUDE.md §1.8・§1.6）。
/// 期待値は characters.json の時代に <see cref="CharacterSamenessProbe"/> で取ったチェックサム（途中と最後）。
/// PhysicsVersion 40 で取り直した（2026-11-17: 脚の骨の端を骨の胴と同じ太さにした・体が支える質量から外れた部位を除く・倒れている間の手を腰が戻ったら床から離す。グローブ同士のレフェリーなし・こんぼう同士は同じ。LegLossProbe.Probe_WhichRecordedMatchesLoseALimb）。その前は PhysicsVersion 39 で、腕・脚を失う試合だけ取り直した（2026-11-17: 脚を失っても残った脚の長さで立って戦い続ける・切り株の筋肉の断面を切れ目の手前だけで測る。腕も脚も失わない試合（グローブ同士・ロングソード同士など）はビット単位で同じことを確かめた: LegLossProbe.Probe_WhichRecordedMatchesLoseALimb）。その前は PhysicsVersion 38・LogicVersion 38 で取り直した（2026-11-17: スタミナを臨界パワーのモデルに・休む間の足と手数・拳の狙い・肘の締め）。その前は PhysicsVersion 37・LogicVersion 36（2026-11-16。37 はスタミナの容量を倍。36 の理由: スタミナの蓄え・供給を筋肉の量に比例させた（体格のあるキャラの試合が変わる）のと、弱った相手を詰める層を既定にした（ボットの立ち回りが変わる）。その前は PhysicsVersion 35・LogicVersion 32 で取り直した（前は PhysicsVersion 34）。理由: 素手を指の関節で組み直した（素手の質量・形が変わる）のと、急所の狙いの点を実際のセルから求めるようにした（ボットの狙いが変わる）。katana-longsword の途中（half）だけは手も急所も関わらず値が同じ。
/// 名簿・ボット・物理の版を意図して変えたら、Probe で取り直して値を差し替える（古い値のまま黙って合わせない）。
/// </summary>
[TestClass]
public sealed class CharacterSamenessTests
{
    private static readonly Dictionary<string, (ulong Half, ulong End)> Expected = new()
    {
        ["glove-glove-referee"] = (0x4A464CA23C7F33BC, 0xE07D0FC9984DCAC8),
        ["glove-glove-noref"] = (0xE02C484AF63AADF2, 0x9D085D00376429CE),
        ["karate-swarmer-noref"] = (0xCDF6A76BFD09BEB8, 0x96694C0BA4D7A389),
        ["katana-longsword-noref"] = (0x97D8FD8E25E1DA4A, 0xF7FC2F916B494422),
        ["club-bare-noref"] = (0x78C6B2D00870ACA4, 0x3F443483D32C6A2A),
        ["katar-gauntlet-referee"] = (0x42F28CD965070FC3, 0x8902CC4A8767093F),
        ["single-counter-vs-knuckles-noref"] = (0xCAEABE8E9E6493B5, 0x3A1DB5C73031A878),
        ["single-karate-vs-katana"] = (0x494DBD2F1DAB123D, 0x0B3906EEE985A556),
    };

    // 注（LogicVersion 46）: 刀・ロングソードの型を増やして技の集まりと乱数の引き方が変わったので、剣を握る組（katana-longsword-noref・single-karate-vs-katana・w-*）の
    // 「教科書を JSON へ移す前」の値は、今の LEGACY の行（教科書の古い値）から取り直した。コードに埋めていた頃の試合とのビット一致は、拳・こんぼう同士の組だけが今も確かめている。
    /// <summary>武器の教科書を JSON へ移す前（`weapon handling` 段 1 の最初）に <see cref="CharacterSamenessProbe"/> で取った値（master へ入れるとき、首・脊椎と指の手（PhysicsVersion 35）の上で LEGACY の行から取り直した。拳の組の LEGACY が既存の値と全部一致することを確かめてから。2026-11-16）。</summary>
    private static readonly Dictionary<string, (ulong Half, ulong End)> ExpectedWeapons = new()
    {
        ["w-katana-katana-referee"] = (0x52EB7DF87AD40841, 0x0E830495DCF9A32D),
        ["w-longsword-longsword-noref"] = (0x7C8C2AFB37C0ACB3, 0x7D3748764EBE13BD),
        ["w-katana-longsword-referee"] = (0xF45A425F6C613184, 0x7EAA94516785DC26),
        ["w-club-katana-noref"] = (0xCF1EB18FAFB10428, 0x523F3F1CFB5867D4),
        ["w-longsword-club-referee"] = (0x801E36AAFE9873D2, 0x81F804E929195F01),
        ["w-club-club-noref"] = (0x7C19AB725FA1FF8B, 0x28F64F242E6370EC),
    };


    /// <summary>
    /// 倒れた相手への攻め（GroundStrike）の好みは、名前で引いた教科書そのものではなく、解決して JSON を通した（検証済みの）スタイルから読む。
    /// 教育が空のとき、その好みの値が教科書と同じ（値域の丸めで変わらない）ことを全スタイル・全キャラで確かめる。
    /// </summary>
    [TestMethod]
    public void GroundPreferences_OfTheResolvedStyle_EqualTheTextbooks()
    {
        static float[] Ground(BotStyle s) => [s.GroundDrive, s.GroundLoad, s.GroundCut, s.GroundStomp, s.GroundRest, s.Southpaw ? 1f : 0f];
        foreach (var textbook in BotCatalog.Styles)
        {
            var viaCorners = BotCatalog.Corners(textbook).CpuStyle!;
            CollectionAssert.AreEqual(Ground(textbook), Ground(viaCorners), textbook.Name);
        }

        foreach (var c in CharacterRoster.All)
        {
            var viaCorners = new MatchSelection(MatchMode.Spectate, c, CharacterRoster.Get("albert")).Corners;
            CollectionAssert.AreEqual(Ground(BotCatalog.Style(c.StyleName)), Ground(viaCorners.RedBotStyle!), c.Id);
            CollectionAssert.AreEqual(Ground(BotCatalog.Style(c.StyleName)), Ground(viaCorners.PlayerStyle!), c.Id);
        }
    }

    /// <summary>
    /// 鍛えた武器の教科書（LogicVersion 33（ブランチでは 31）: 刀・ロングソードの技の時計）で戦わせたときのチェックサム（剣を握る組だけ。拳同士・こんぼう同士は変わらない）。
    /// `CharacterSamenessProbe` の SAME の行から取った。教科書・ボット・物理の版を意図して変えたら、取り直す。
    /// </summary>
    private static readonly Dictionary<string, (ulong Half, ulong End)> ExpectedAdopted = new()
    {
        ["katana-longsword-noref"] = (0x22CFC13C78AB3042, 0xA9849C9B5A739D3F),
        ["single-karate-vs-katana"] = (0xB5AD2098894F1C35, 0x0D53ED27CC803F5A),
        ["w-katana-katana-referee"] = (0x1036ED5C4BD83ACC, 0x20034730F26819B5),
        ["w-longsword-longsword-noref"] = (0x79A3E016A7F201F7, 0x9C13C6AA383DF9A9),
        ["w-katana-longsword-referee"] = (0x646B1EBE835CF994, 0x2BC436E2388AD25B),
        ["w-club-katana-noref"] = (0x7D7A4282E2CB8B9E, 0xCADC57BD892A95E6),
        ["w-longsword-club-referee"] = (0x06130CBCE388A387, 0x29DCDCE42BC9ABAD),
    };

    /// <summary>
    /// 教科書を JSON へ移しただけの段階（技の時計なし: <see cref="CharacterSamenessCases.LegacyWeaponStyle"/>）の値で戦わせると、
    /// 値をコードに埋めていた頃の試合とビット単位で同じ。
    /// </summary>
    [TestMethod]
    public void WeaponTextbooksInJson_WithTheOldValues_PlayTheSameMatchesAsTheHardCodedWeaponBots()
    {
        Assert.HasCount(ExpectedWeapons.Count, CharacterSamenessCases.Weapons, "every case has a recorded checksum");
        foreach (var c in CharacterSamenessCases.Weapons)
        {
            var (half, end) = CharacterSamenessCases.Run(c, legacyWeapons: true);
            Assert.AreEqual(ExpectedWeapons[c.Name], (half, end), c.Name);
        }
    }

    [TestMethod]
    public void AdoptedWeaponTextbooks_PlayTheRecordedMatches_AndDifferFromTheOldOnes()
    {
        foreach (var c in CharacterSamenessCases.All.Concat(CharacterSamenessCases.Weapons).Where(c => ExpectedAdopted.ContainsKey(c.Name)))
        {
            var (half, end) = CharacterSamenessCases.Run(c);
            Assert.AreEqual(ExpectedAdopted[c.Name], (half, end), c.Name);
            var old = Expected.TryGetValue(c.Name, out var known) ? known : ExpectedWeapons[c.Name];
            Assert.AreNotEqual(old, (half, end), "the trained textbook plays a different match: " + c.Name);
        }
    }

    [TestMethod]
    public void EmptyEducation_PlaysTheSameMatchesAsBeforeTheFilesWereSplit()
    {
        Assert.HasCount(Expected.Count, CharacterSamenessCases.All, "every case has a recorded checksum");
        foreach (var c in CharacterSamenessCases.All)
        {
            // 武器の組は、教科書を鍛える前の値（LegacyWeaponStyle）で。拳同士の組は今のまま。
            var (half, end) = CharacterSamenessCases.Run(c, legacyWeapons: true);
            Assert.AreEqual(Expected[c.Name], (half, end), c.Name);
        }
    }
}
