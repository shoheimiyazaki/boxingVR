using MiniRing.Bots;
using MiniRing.Characters;
using MiniRing.Desktop;

namespace MiniRing.Tests;

/// <summary>
/// E0: 教育が空なら、名簿を 1 人 1 ファイルにしても試合はビット単位で同じ（CLAUDE.md §1.8・§1.6）。
/// 期待値は characters.json の時代に <see cref="CharacterSamenessProbe"/> で取ったチェックサム（途中と最後）。
/// PhysicsVersion 39 で、腕・脚を失う試合だけ取り直した（2026-11-17: 脚を失っても残った脚の長さで立って戦い続ける・切り株の筋肉の断面を切れ目の手前だけで測る。腕も脚も失わない試合（グローブ同士・ロングソード同士など）はビット単位で同じことを確かめた: LegLossProbe.Probe_WhichRecordedMatchesLoseALimb）。その前は PhysicsVersion 38・LogicVersion 38 で取り直した（2026-11-17: スタミナを臨界パワーのモデルに・休む間の足と手数・拳の狙い・肘の締め）。その前は PhysicsVersion 37・LogicVersion 36（2026-11-16。37 はスタミナの容量を倍。36 の理由: スタミナの蓄え・供給を筋肉の量に比例させた（体格のあるキャラの試合が変わる）のと、弱った相手を詰める層を既定にした（ボットの立ち回りが変わる）。その前は PhysicsVersion 35・LogicVersion 32 で取り直した（前は PhysicsVersion 34）。理由: 素手を指の関節で組み直した（素手の質量・形が変わる）のと、急所の狙いの点を実際のセルから求めるようにした（ボットの狙いが変わる）。katana-longsword の途中（half）だけは手も急所も関わらず値が同じ。
/// 名簿・ボット・物理の版を意図して変えたら、Probe で取り直して値を差し替える（古い値のまま黙って合わせない）。
/// </summary>
[TestClass]
public sealed class CharacterSamenessTests
{
    private static readonly Dictionary<string, (ulong Half, ulong End)> Expected = new()
    {
        ["glove-glove-referee"] = (0x4A464CA23C7F33BC, 0xAEB721C6603D1462),
        ["glove-glove-noref"] = (0xE02C484AF63AADF2, 0x9D085D00376429CE),
        ["karate-swarmer-noref"] = (0xAB1424A9C6CE4764, 0x639ADF0CB971E2F4),
        ["katana-longsword-noref"] = (0xD365E042DA704387, 0x6A4C1AAFF10940C3),
        ["club-bare-noref"] = (0x4B35B2340E9B285D, 0xB80D03FBFA9A770C),
        ["katar-gauntlet-referee"] = (0x42F28CD965070FC3, 0x4F9958E0C0B16D3C),
        ["single-counter-vs-knuckles-noref"] = (0xB9CA8E1645B0D3FC, 0xA3CB7FE6ECC6D397),
        ["single-karate-vs-katana"] = (0x42CDFF9DEA701301, 0x417083E9818F409F),
    };

    /// <summary>武器の教科書を JSON へ移す前（`weapon handling` 段 1 の最初）に <see cref="CharacterSamenessProbe"/> で取った値（master へ入れるとき、首・脊椎と指の手（PhysicsVersion 35）の上で LEGACY の行から取り直した。拳の組の LEGACY が既存の値と全部一致することを確かめてから。2026-11-16）。</summary>
    private static readonly Dictionary<string, (ulong Half, ulong End)> ExpectedWeapons = new()
    {
        ["w-katana-katana-referee"] = (0xC69A9B4CD80D5297, 0xA19AEA5A228349BC),
        ["w-longsword-longsword-noref"] = (0xF6196701415CAE18, 0x92FAB3FD49623B6B),
        ["w-katana-longsword-referee"] = (0xC244E1F8748C67F9, 0xF773B2406A9CD074),
        ["w-club-katana-noref"] = (0xF6BEBC8ADCBAB7DD, 0x2A66974A2114B4C0),
        ["w-longsword-club-referee"] = (0x12F429B73501967E, 0x5DD777DC0D27F827),
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
        ["w-katana-katana-referee"] = (0x1074011FB1918488, 0xC76D7CD7D1F4C5D5),
        ["w-longsword-longsword-noref"] = (0x458D0C361D42E727, 0x8CD054413D20C638),
        ["w-katana-longsword-referee"] = (0x7A100352645AC682, 0xA1BBC7402F19E502),
        ["w-club-katana-noref"] = (0x26AFA0ECBB5BDCF6, 0x926DF0E9EE36AC01),
        ["w-longsword-club-referee"] = (0x02B69244E56D4C96, 0xFF55E9ABEA775F57),
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
