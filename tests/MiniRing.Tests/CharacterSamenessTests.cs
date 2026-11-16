using MiniRing.Bots;
using MiniRing.Characters;
using MiniRing.Desktop;

namespace MiniRing.Tests;

/// <summary>
/// E0: 教育が空なら、名簿を 1 人 1 ファイルにしても試合はビット単位で同じ（CLAUDE.md §1.8・§1.6）。
/// 期待値は characters.json の時代に <see cref="CharacterSamenessProbe"/> で取ったチェックサム（途中と最後）。
/// PhysicsVersion 36・LogicVersion 36 で取り直した（2026-11-16。理由: スタミナの蓄え・供給を筋肉の量に比例させた（体格のあるキャラの試合が変わる）のと、弱った相手を詰める層を既定にした（ボットの立ち回りが変わる）。その前は PhysicsVersion 35・LogicVersion 32 で取り直した（前は PhysicsVersion 34）。理由: 素手を指の関節で組み直した（素手の質量・形が変わる）のと、急所の狙いの点を実際のセルから求めるようにした（ボットの狙いが変わる）。katana-longsword の途中（half）だけは手も急所も関わらず値が同じ。
/// 名簿・ボット・物理の版を意図して変えたら、Probe で取り直して値を差し替える（古い値のまま黙って合わせない）。
/// </summary>
[TestClass]
public sealed class CharacterSamenessTests
{
    private static readonly Dictionary<string, (ulong Half, ulong End)> Expected = new()
    {
        ["glove-glove-referee"] = (0xF58A94E0A14201DD, 0x7F3034DA59D280F3),
        ["glove-glove-noref"] = (0xC8B6E1D35B2307D2, 0x729BE874484F5EF2),
        ["karate-swarmer-noref"] = (0x13260D37F048A93F, 0x963B585D47CF6A0E),
        ["katana-longsword-noref"] = (0xDD266FAA7F744FB9, 0x17E7D77BB2137557),
        ["club-bare-noref"] = (0xB9583E666999C667, 0xF00C222D0DEFF4C4),
        ["katar-gauntlet-referee"] = (0x0CA952F4B2EBCC84, 0x27BEE78C4DD37765),
        ["single-counter-vs-knuckles-noref"] = (0x0FED09DD4A681413, 0x910DE9D1D8437D13),
        ["single-karate-vs-katana"] = (0xB7732C310D5E3A45, 0x390FB1382160BFF6),
    };

    /// <summary>武器の教科書を JSON へ移す前（`weapon handling` 段 1 の最初）に <see cref="CharacterSamenessProbe"/> で取った値（master へ入れるとき、首・脊椎と指の手（PhysicsVersion 35）の上で LEGACY の行から取り直した。拳の組の LEGACY が既存の値と全部一致することを確かめてから。2026-11-16）。</summary>
    private static readonly Dictionary<string, (ulong Half, ulong End)> ExpectedWeapons = new()
    {
        ["w-katana-katana-referee"] = (0x821CE1AB7A4E1E0A, 0x50CDA4FD7AEC0B7D),
        ["w-longsword-longsword-noref"] = (0x0ED7F84EDAAD8BC9, 0xA6EE4E9428ECD1BE),
        ["w-katana-longsword-referee"] = (0xBEA5D0B9A26BAA50, 0x13FB1728AB198386),
        ["w-club-katana-noref"] = (0xB0BD1FB6C87200D0, 0xA960EB839D814990),
        ["w-longsword-club-referee"] = (0x0B1773B1A024E3FC, 0xC16615B11E2044E0),
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
        ["katana-longsword-noref"] = (0x8154CEC2A35EA31B, 0xFABC436F37007475),
        ["single-karate-vs-katana"] = (0xFF123DB0F74616B6, 0xD38CF4E1BAF6029B),
        ["w-katana-katana-referee"] = (0x954C0E9E8F154CBB, 0xBA8ECACE49D48FED),
        ["w-longsword-longsword-noref"] = (0x2648075E38D6CAA9, 0xA036695301B3E0FD),
        ["w-katana-longsword-referee"] = (0xC00435726A1332F8, 0x48D6ADD338F8F089),
        ["w-club-katana-noref"] = (0x461F5FD4BDFE42D9, 0x65CBE76A12EFAC0A),
        ["w-longsword-club-referee"] = (0xFFC4DE8F28C68D0B, 0x2D446F84946222AC),
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
