using MiniRing.Bots;
using MiniRing.Characters;
using MiniRing.Desktop;

namespace MiniRing.Tests;

/// <summary>
/// E0: 教育が空なら、名簿を 1 人 1 ファイルにしても試合はビット単位で同じ（CLAUDE.md §1.8・§1.6）。
/// 期待値は characters.json の時代に <see cref="CharacterSamenessProbe"/> で取ったチェックサム（途中と最後）。
/// PhysicsVersion 40 で取り直した（2026-11-17: 脚の骨の端を骨の胴と同じ太さにした・体が支える質量から外れた部位を除く・倒れている間の手を腰が戻ったら床から離す。グローブ同士のレフェリーなし・こんぼう同士は同じ。LegLossProbe.Probe_WhichRecordedMatchesLoseALimb）。その前は PhysicsVersion 39 で、腕・脚を失う試合だけ取り直した（2026-11-17: 脚を失っても残った脚の長さで立って戦い続ける・切り株の筋肉の断面を切れ目の手前だけで測る。腕も脚も失わない試合（グローブ同士・ロングソード同士など）はビット単位で同じことを確かめた: LegLossProbe.Probe_WhichRecordedMatchesLoseALimb）。その前は PhysicsVersion 38・LogicVersion 38 で取り直した（2026-11-17: スタミナを臨界パワーのモデルに・休む間の足と手数・拳の狙い・肘の締め）。その前は PhysicsVersion 37・LogicVersion 36（2026-11-16。37 はスタミナの容量を倍。36 の理由: スタミナの蓄え・供給を筋肉の量に比例させた（体格のあるキャラの試合が変わる）のと、弱った相手を詰める層を既定にした（ボットの立ち回りが変わる）。その前は PhysicsVersion 35・LogicVersion 32 で取り直した（前は PhysicsVersion 34）。理由: 素手を指の関節で組み直した（素手の質量・形が変わる）のと、急所の狙いの点を実際のセルから求めるようにした（ボットの狙いが変わる）。katana-longsword の途中（half）だけは手も急所も関わらず値が同じ。
/// PhysicsVersion 44（2026-11-17: 剣・刀・ロングソードの柄に鉄の芯を通した・削り切れた武器を分ける）で、剣を握る組（katana-longsword・single-karate-vs-katana・w-katana*・w-longsword*・w-club-katana）だけ取り直した。拳同士・こんぼう同士・空手・カタール・ガントレットの試合はビット単位で同じ。
/// PhysicsVersion 47・LogicVersion 52 で全部を取り直した（2026-11-18: ブランチ claude/throwing-grappling-mechanics（床からの支え・釣り合い・起き上がり・床の衝撃・投げ・肘の靭帯・蹴る前に軸足へ乗る）を master と合わせた。値は tower32 で CharacterSamenessProbe を回して取った）。
/// LogicVersion 49 で全部を取り直した（2026-11-18: master の休み・回し蹴りと、ブランチ claude/sword-combat-techniques の剣の型・連続の守り・内側の技・バットを合わせた。拳・素手・こんぼう同士の組は 45 のときと同じ値）。PhysicsVersion 45 で全部を取り直した（2026-11-18: ブランチ claude/staff-vr-motion-kick のつま先の止め・平たい靴の形（ブランチの 43）と、柄の鉄の芯（44）を合わせた。途中（half）は多くの組で同じ）。その前は PhysicsVersion 40 で取り直した（2026-11-17: 脚の骨の端を骨の胴と同じ太さにした・体が支える質量から外れた部位を除く・倒れている間の手を腰が戻ったら床から離す。グローブ同士のレフェリーなし・こんぼう同士は同じ。LegLossProbe.Probe_WhichRecordedMatchesLoseALimb）。その前は PhysicsVersion 39 で、腕・脚を失う試合だけ取り直した（2026-11-17: 脚を失っても残った脚の長さで立って戦い続ける・切り株の筋肉の断面を切れ目の手前だけで測る。腕も脚も失わない試合（グローブ同士・ロングソード同士など）はビット単位で同じことを確かめた: LegLossProbe.Probe_WhichRecordedMatchesLoseALimb）。その前は PhysicsVersion 38・LogicVersion 38 で取り直した（2026-11-17: スタミナを臨界パワーのモデルに・休む間の足と手数・拳の狙い・肘の締め）。その前は PhysicsVersion 37・LogicVersion 36（2026-11-16。37 はスタミナの容量を倍。36 の理由: スタミナの蓄え・供給を筋肉の量に比例させた（体格のあるキャラの試合が変わる）のと、弱った相手を詰める層を既定にした（ボットの立ち回りが変わる）。その前は PhysicsVersion 35・LogicVersion 32 で取り直した（前は PhysicsVersion 34）。理由: 素手を指の関節で組み直した（素手の質量・形が変わる）のと、急所の狙いの点を実際のセルから求めるようにした（ボットの狙いが変わる）。katana-longsword の途中（half）だけは手も急所も関わらず値が同じ。
/// 名簿・ボット・物理の版を意図して変えたら、Probe で取り直して値を差し替える（古い値のまま黙って合わせない）。
/// </summary>
[TestClass]
[TestCategory("slow")]
public sealed class CharacterSamenessTests
{
    // 2026-11-18（PhysicsVersion 44: ダウンの間の審判の指示）: レフェリーのいる試合でダウンが入る組だけ取り直した（CharacterSamenessProbe。ほかの組は変わらない）。
    // 2026-11-18（LogicVersion 50 の作業中）: マージ 000bd92（PhysicsVersion 46）で master 側の値が入り、ブランチの審判の指示で取り直したレフェリーありの組の値が
    // 消えていたので戻した（glove-glove-referee は d6b1257 で取り直した値と同じ。katar-gauntlet-referee の終わりも同じ理由。レフェリーなしの組は変わらない）。
    private static readonly Dictionary<string, (ulong Half, ulong End)> Expected = new()
    {
        ["glove-glove-referee"] = (0xF68FE6391E0778B2, 0xC8A6EB6E8061EEAD),
        ["glove-glove-noref"] = (0x7E90985C6C4AB082, 0xC4C8374D11E0EEAE),
        ["karate-swarmer-noref"] = (0x7242D8322FE9F2AE, 0x80031181A0AC0FF8),
        ["katana-longsword-noref"] = (0x5DBCCA12BC308C5D, 0x6EB2F9FCBB36F73D),
        ["club-bare-noref"] = (0x5565EACCB3243E77, 0x395F8C072748D5D5),
        ["katar-gauntlet-referee"] = (0x0824445A264398AC, 0xE9CCC3508D32E7C5),
        ["single-counter-vs-knuckles-noref"] = (0xA2268F496BB393C4, 0xD4E1DB9AE3D156E9),
        ["single-karate-vs-katana"] = (0x6C68B8DA2A39097B, 0x58122E0E82771B32),
    };

    // 注（LogicVersion 47: 間合いの内側の技を試合で使い、同じ間合いの技の選びを重みで引くようにしたので、剣を握る組の LEGACY・SAME を取り直した。拳・こんぼう同士の組は元のまま一致）。
    // 注（LogicVersion 46）: 刀・ロングソードの型を増やして技の集まりと乱数の引き方が変わったので、剣を握る組（katana-longsword-noref・single-karate-vs-katana・w-*）の
    // 「教科書を JSON へ移す前」の値は、今の LEGACY の行（教科書の古い値）から取り直した。コードに埋めていた頃の試合とのビット一致は、拳・こんぼう同士の組だけが今も確かめている。
    /// <summary>武器の教科書を JSON へ移す前（`weapon handling` 段 1 の最初）に <see cref="CharacterSamenessProbe"/> で取った値（master へ入れるとき、首・脊椎と指の手（PhysicsVersion 35）の上で LEGACY の行から取り直した。拳の組の LEGACY が既存の値と全部一致することを確かめてから。2026-11-16）。</summary>
    private static readonly Dictionary<string, (ulong Half, ulong End)> ExpectedWeapons = new()
    {
        ["w-katana-katana-referee"] = (0x331C4CA146120A5B, 0x0E8C00393F8D525E), // レフェリーありの組はマージ 000bd92 の後に取り直した（上の注）
        ["w-longsword-longsword-noref"] = (0x639686C2A43C6364, 0x1C5006EA41630937),
        ["w-katana-longsword-referee"] = (0x942CCA9835A96200, 0x61A224AFE76D8139),
        ["w-club-katana-noref"] = (0x74A3327845F8AB5F, 0xC890A794C938EDA6),
        ["w-longsword-club-referee"] = (0x9D6F4AABD0C794D0, 0x0F0E1A0430D0539E),
        ["w-club-club-noref"] = (0x2E5F0B1B193B367E, 0x088BA0C08F090A2E),
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
        ["katana-longsword-noref"] = (0x7F2D9A1A01F6BCB1, 0xAEF886FE86A56542),
        ["single-karate-vs-katana"] = (0xA7ED1DFE8689F87D, 0xB53B9C457C26E55B),
        ["w-katana-katana-referee"] = (0xC21C80F63265F485, 0x1188CE6647FB6002), // レフェリーありの組はマージ 000bd92 の後に取り直した（上の注）
        ["w-longsword-longsword-noref"] = (0x485849C295020E4B, 0x86BF39E9C8C1CA6E),
        ["w-katana-longsword-referee"] = (0xEA0A3A4B1E680BD1, 0x59F554A60276CC93),
        ["w-club-katana-noref"] = (0x26C3A0D5DF8FE7C9, 0x5546CC50D0A007E3),
        ["w-longsword-club-referee"] = (0xFF37AC335DB7C551, 0xD57A46574854FEBD),
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
            // 名簿の教育は戦略の層（GamePlan、LogicVersion 50）だけなので、それを外せば教育は空: 層が無ければボットの判断はビット単位で今まで通り。
            var (half, end) = CharacterSamenessCases.Run(c, legacyWeapons: true, gamePlan: false);
            Assert.AreEqual(Expected[c.Name], (half, end), c.Name);
        }
    }

    /// <summary>
    /// 名簿の教育に戦略の層（<c>education.style.GamePlan</c>、LogicVersion 50、2026-11-18）を入れた試合のチェックサム（<see cref="CharacterSamenessProbe"/> の SAME の行）。
    /// 拳のボットが戦う組だけが変わる（両方とも剣・こんぼうのボット、または人が操る側と武器のボットの組は、教育に GamePlan があっても同じ）。
    /// </summary>
    private static readonly Dictionary<string, (ulong Half, ulong End)> ExpectedGamePlan = new()
    {
        ["glove-glove-referee"] = (0x2A9F2EB62B980A7F, 0xD433BE4E493CF7B1),
        ["glove-glove-noref"] = (0x8165C107AEC9E60A, 0x9AE9CF4121EF4CD9),
        ["karate-swarmer-noref"] = (0xEEF84CE2A3ABF6D2, 0xB637541F21BACCAE),
        ["club-bare-noref"] = (0x967AF59CF435A40C, 0x43C83414D8FAD345),
        ["katar-gauntlet-referee"] = (0xACB926FA7EE1D8D5, 0x7F28F965F702C080),
        ["single-counter-vs-knuckles-noref"] = (0xF67FAB34F2884A42, 0x24A1F80818515FB0),
    };

    [TestMethod]
    public void GamePlanEducation_ChangesOnlyTheMatchesWithFistBots()
    {
        foreach (var c in CharacterSamenessCases.All)
        {
            var (half, end) = CharacterSamenessCases.Run(c, legacyWeapons: true);
            if (ExpectedGamePlan.TryGetValue(c.Name, out var educated))
            {
                Assert.AreEqual(educated, (half, end), c.Name);
                Assert.AreNotEqual(Expected[c.Name], (half, end), "the game plan reaches the fist bots: " + c.Name);
            }
            else
            {
                Assert.AreEqual(Expected[c.Name], (half, end), "no fist bot, no change: " + c.Name);
            }
        }
    }
}
