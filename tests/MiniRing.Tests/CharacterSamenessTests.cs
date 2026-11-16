using MiniRing.Bots;
using MiniRing.Characters;
using MiniRing.Desktop;

namespace MiniRing.Tests;

/// <summary>
/// E0: 教育が空なら、名簿を 1 人 1 ファイルにしても試合はビット単位で同じ（CLAUDE.md §1.8・§1.6）。
/// 期待値は characters.json の時代に <see cref="CharacterSamenessProbe"/> で取ったチェックサム（途中と最後）。
/// PhysicsVersion 37・LogicVersion 36 で取り直した（2026-11-16。37 はスタミナの容量を倍。36 の理由: スタミナの蓄え・供給を筋肉の量に比例させた（体格のあるキャラの試合が変わる）のと、弱った相手を詰める層を既定にした（ボットの立ち回りが変わる）。その前は PhysicsVersion 35・LogicVersion 32 で取り直した（前は PhysicsVersion 34）。理由: 素手を指の関節で組み直した（素手の質量・形が変わる）のと、急所の狙いの点を実際のセルから求めるようにした（ボットの狙いが変わる）。katana-longsword の途中（half）だけは手も急所も関わらず値が同じ。
/// 名簿・ボット・物理の版を意図して変えたら、Probe で取り直して値を差し替える（古い値のまま黙って合わせない）。
/// </summary>
[TestClass]
public sealed class CharacterSamenessTests
{
    private static readonly Dictionary<string, (ulong Half, ulong End)> Expected = new()
    {
        ["glove-glove-referee"] = (0xF58A94E0A14201DD, 0x5A861363C29E0FD9),
        ["glove-glove-noref"] = (0xC8B6E1D35B2307D2, 0x16D56164FEE3DB95),
        ["karate-swarmer-noref"] = (0x4FBC454A16E8D3AD, 0xF2656A7C27180795),
        ["katana-longsword-noref"] = (0xD365E042DA704387, 0xB2835EA685E4513D),
        ["club-bare-noref"] = (0x93D061005B123F3D, 0x3EBC7C8F092A542E),
        ["katar-gauntlet-referee"] = (0x9C302029C7605EC5, 0x2FD208BE8B5AECE8),
        ["single-counter-vs-knuckles-noref"] = (0xD218BFD5354993B3, 0xC1B1B8F7D0A63EA9),
        ["single-karate-vs-katana"] = (0x0AE36EB22580946D, 0x478FA7A91D7D49D1),
    };

    /// <summary>武器の教科書を JSON へ移す前（`weapon handling` 段 1 の最初）に <see cref="CharacterSamenessProbe"/> で取った値（master へ入れるとき、首・脊椎と指の手（PhysicsVersion 35）の上で LEGACY の行から取り直した。拳の組の LEGACY が既存の値と全部一致することを確かめてから。2026-11-16）。</summary>
    private static readonly Dictionary<string, (ulong Half, ulong End)> ExpectedWeapons = new()
    {
        ["w-katana-katana-referee"] = (0x90EB1DDA553AB22B, 0x2198E1765948BF63),
        ["w-longsword-longsword-noref"] = (0x8F15CCDB3679A629, 0x3CE2253A2447C2F5),
        ["w-katana-longsword-referee"] = (0xC244E1F8748C67F9, 0xF773B2406A9CD074),
        ["w-club-katana-noref"] = (0x3D4C96D4789093B5, 0x0D82B97CE0C9D241),
        ["w-longsword-club-referee"] = (0x12F429B73501967E, 0x32DCA2C9FFB647D2),
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
        ["katana-longsword-noref"] = (0x69DE8A84F5D8D755, 0xA7DC1CF08B0D51CF),
        ["single-karate-vs-katana"] = (0xEEA9CDB8DCC1D24A, 0x18221ACB29A4379B),
        ["w-katana-katana-referee"] = (0x954C0E9E8F154CBB, 0xE2A5EAC55B3EFA1C),
        ["w-longsword-longsword-noref"] = (0x9B8908AD164E8A0F, 0x7F2870AB945D4B68),
        ["w-katana-longsword-referee"] = (0x7A100352645AC682, 0xA1BBC7402F19E502),
        ["w-club-katana-noref"] = (0x257EC411D642C351, 0xD7C709F34A52FE84),
        ["w-longsword-club-referee"] = (0xCB074B8119D06324, 0x12F4972DE21DFFAB),
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
