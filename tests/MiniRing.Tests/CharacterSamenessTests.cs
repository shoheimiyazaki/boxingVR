using MiniRing.Bots;
using MiniRing.Characters;
using MiniRing.Desktop;

namespace MiniRing.Tests;

/// <summary>
/// E0: 教育が空なら、名簿を 1 人 1 ファイルにしても試合はビット単位で同じ（CLAUDE.md §1.8・§1.6）。
/// 期待値は characters.json の時代に <see cref="CharacterSamenessProbe"/> で取ったチェックサム（途中と最後）。
/// PhysicsVersion 35・LogicVersion 32 で取り直した（2026-11-16。前は PhysicsVersion 34）。理由: 素手を指の関節で組み直した（素手の質量・形が変わる）のと、急所の狙いの点を実際のセルから求めるようにした（ボットの狙いが変わる）。katana-longsword の途中（half）だけは手も急所も関わらず値が同じ。
/// 名簿・ボット・物理の版を意図して変えたら、Probe で取り直して値を差し替える（古い値のまま黙って合わせない）。
/// </summary>
[TestClass]
public sealed class CharacterSamenessTests
{
    private static readonly Dictionary<string, (ulong Half, ulong End)> Expected = new()
    {
        ["glove-glove-referee"] = (0xB4762E26560C3086, 0x53C8F7647F522B0E),
        ["glove-glove-noref"] = (0x6D3B95659F6ED548, 0x70E2D36C24D13CD3),
        ["karate-swarmer-noref"] = (0x899D98779E196C02, 0xBDD97EC44C311A24),
        ["katana-longsword-noref"] = (0xAAB20D2F22A47999, 0xA5DD384AAE890328),
        ["club-bare-noref"] = (0x0501A7B1647D225F, 0x3A10CB9F1B5B3555),
        ["katar-gauntlet-referee"] = (0x234A01F6C5A0A841, 0x1E7759C3E0CE64C0),
        ["single-counter-vs-knuckles-noref"] = (0x3A88AB9F54DE6C0C, 0xFCBF9C40B39171BF),
        ["single-karate-vs-katana"] = (0x1DA95869A090C7FD, 0xE7F22858347A879B),
    };

    /// <summary>武器の教科書を JSON へ移す前（`weapon handling` 段 1 の最初）に <see cref="CharacterSamenessProbe"/> で取った値（master へ入れるとき、首・脊椎と指の手（PhysicsVersion 35）の上で LEGACY の行から取り直した。拳の組の LEGACY が既存の値と全部一致することを確かめてから。2026-11-16）。</summary>
    private static readonly Dictionary<string, (ulong Half, ulong End)> ExpectedWeapons = new()
    {
        ["w-katana-katana-referee"] = (0xFAED651F26802F78, 0x7DA096D1C0452412),
        ["w-longsword-longsword-noref"] = (0x2CFD280625F45D5E, 0xE43073D9EAEF69C7),
        ["w-katana-longsword-referee"] = (0xC7CECE5951ECC946, 0x9918A78CD5C27A8B),
        ["w-club-katana-noref"] = (0xC8A8EAF34DD7725A, 0xA8443D09B33CB78B),
        ["w-longsword-club-referee"] = (0x69BEC679D8E17D2F, 0xC70600162BADF58B),
        ["w-club-club-noref"] = (0x01205C7B5B13E168, 0xC3D4597E92BD317A),
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
        ["katana-longsword-noref"] = (0xE78E96D1F264F487, 0xE8CF64871A87EE4A),
        ["single-karate-vs-katana"] = (0x754E774A2A9EC32B, 0x8E97F4B066B691F3),
        ["w-katana-katana-referee"] = (0x360E12F0D2287516, 0x414584290A199B35),
        ["w-longsword-longsword-noref"] = (0x008264468D21B538, 0x5CEBD3203AC89A03),
        ["w-katana-longsword-referee"] = (0x8671EDA64A4970A2, 0x6A369C4583BDF4F7),
        ["w-club-katana-noref"] = (0x342B20E3B1C5D66A, 0x14A475F9B1709CC7),
        ["w-longsword-club-referee"] = (0xE8E167A52AEB068F, 0x3F1685CD16D8FEDD),
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
