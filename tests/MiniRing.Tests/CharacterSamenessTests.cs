using MiniRing.Bots;
using MiniRing.Characters;
using MiniRing.Desktop;

namespace MiniRing.Tests;

/// <summary>
/// E0: 教育が空なら、名簿を 1 人 1 ファイルにしても試合はビット単位で同じ（CLAUDE.md §1.8・§1.6）。
/// 期待値は characters.json の時代に <see cref="CharacterSamenessProbe"/> で取ったチェックサム（途中と最後）。
/// PhysicsVersion 53・LogicVersion 55 で全部を取り直した（2026-11-19: master の 52（呼吸と循環のスタミナ・素手の拳・投げ技の床から支える体）とブランチ claude/sword-combat-techniques-51f876 の 49（握る・拾う・関節のつなぎ・着いた足の滑りの直し）を合わせ、蹴りの差分（Bots/kicks）を練習し直した後。raytrek の CharacterSamenessProbe。拳・グローブ同士の組も、足の運びの構えの寄せ方（骨盤で出す）と呼吸のモデルで変わった）。
/// PhysicsVersion 51 で全部を取り直した（2026-11-19: ブランチ claude/hand-voxels の素手の拳（5 mm のセル・幅 40 mm・木の物性）を master の 50 と合わせた。tower32 の CharacterSamenessProbe）。
/// PhysicsVersion 40 で取り直した（2026-11-17: 脚の骨の端を骨の胴と同じ太さにした・体が支える質量から外れた部位を除く・倒れている間の手を腰が戻ったら床から離す。グローブ同士のレフェリーなし・こんぼう同士は同じ。LegLossProbe.Probe_WhichRecordedMatchesLoseALimb）。その前は PhysicsVersion 39 で、腕・脚を失う試合だけ取り直した（2026-11-17: 脚を失っても残った脚の長さで立って戦い続ける・切り株の筋肉の断面を切れ目の手前だけで測る。腕も脚も失わない試合（グローブ同士・ロングソード同士など）はビット単位で同じことを確かめた: LegLossProbe.Probe_WhichRecordedMatchesLoseALimb）。その前は PhysicsVersion 38・LogicVersion 38 で取り直した（2026-11-17: スタミナを臨界パワーのモデルに・休む間の足と手数・拳の狙い・肘の締め）。その前は PhysicsVersion 37・LogicVersion 36（2026-11-16。37 はスタミナの容量を倍。36 の理由: スタミナの蓄え・供給を筋肉の量に比例させた（体格のあるキャラの試合が変わる）のと、弱った相手を詰める層を既定にした（ボットの立ち回りが変わる）。その前は PhysicsVersion 35・LogicVersion 32 で取り直した（前は PhysicsVersion 34）。理由: 素手を指の関節で組み直した（素手の質量・形が変わる）のと、急所の狙いの点を実際のセルから求めるようにした（ボットの狙いが変わる）。katana-longsword の途中（half）だけは手も急所も関わらず値が同じ。
/// PhysicsVersion 51 で取り直した（2026-11-18: 素手の拳を 5 mm のセル・幅 40 mm にし、手の組織の物性を木に。素手・空手・剣の組（剣士の空いた手・握り替えの間の素手）が変わる。グローブ同士・カタールと籠手・メリケンサック・こんぼう同士の組（はめる物の拳は前のまま）は同じ。CharacterSamenessProbe）。
/// PhysicsVersion 44（2026-11-17: 剣・刀・ロングソードの柄に鉄の芯を通した・削り切れた武器を分ける）で、剣を握る組（katana-longsword・single-karate-vs-katana・w-katana*・w-longsword*・w-club-katana）だけ取り直した。拳同士・こんぼう同士・空手・カタール・ガントレットの試合はビット単位で同じ。
/// PhysicsVersion 50・LogicVersion 54 で全部を取り直した（2026-11-19: master の 49・53（突進の物理・間の層・VFC・剣の技）と、ブランチ claude/grappling-fix の 48・53（投げ技の枝とマージ後の直し）を合わせた。値は手元で CharacterSamenessProbe を回して取った）。
/// PhysicsVersion 48・LogicVersion 53 で全部を取り直した（2026-11-18: マージ後の直し。体重を受ける足の裏全体を支えに・縦軸のねじりは減らさない・膝の制御の蹴りの間は軸足を振り出さない・床の衝撃は触れた面の中心で、と蹴りの差分の練習し直し。値は手元で CharacterSamenessProbe を回して取った）。
/// PhysicsVersion 47・LogicVersion 52 で全部を取り直した（2026-11-18: ブランチ claude/throwing-grappling-mechanics（床からの支え・釣り合い・起き上がり・床の衝撃・投げ・肘の靭帯・蹴る前に軸足へ乗る）を master と合わせた。値は tower32 で CharacterSamenessProbe を回して取った）。
/// LogicVersion 49 で全部を取り直した（2026-11-18: master の休み・回し蹴りと、ブランチ claude/sword-combat-techniques の剣の型・連続の守り・内側の技・バットを合わせた。拳・素手・こんぼう同士の組は 45 のときと同じ値）。PhysicsVersion 45 で全部を取り直した（2026-11-18: ブランチ claude/staff-vr-motion-kick のつま先の止め・平たい靴の形（ブランチの 43）と、柄の鉄の芯（44）を合わせた。途中（half）は多くの組で同じ）。その前は PhysicsVersion 40 で取り直した（2026-11-17: 脚の骨の端を骨の胴と同じ太さにした・体が支える質量から外れた部位を除く・倒れている間の手を腰が戻ったら床から離す。グローブ同士のレフェリーなし・こんぼう同士は同じ。LegLossProbe.Probe_WhichRecordedMatchesLoseALimb）。その前は PhysicsVersion 39 で、腕・脚を失う試合だけ取り直した（2026-11-17: 脚を失っても残った脚の長さで立って戦い続ける・切り株の筋肉の断面を切れ目の手前だけで測る。腕も脚も失わない試合（グローブ同士・ロングソード同士など）はビット単位で同じことを確かめた: LegLossProbe.Probe_WhichRecordedMatchesLoseALimb）。その前は PhysicsVersion 38・LogicVersion 38 で取り直した（2026-11-17: スタミナを臨界パワーのモデルに・休む間の足と手数・拳の狙い・肘の締め）。その前は PhysicsVersion 37・LogicVersion 36（2026-11-16。37 はスタミナの容量を倍。36 の理由: スタミナの蓄え・供給を筋肉の量に比例させた（体格のあるキャラの試合が変わる）のと、弱った相手を詰める層を既定にした（ボットの立ち回りが変わる）。その前は PhysicsVersion 35・LogicVersion 32 で取り直した（前は PhysicsVersion 34）。理由: 素手を指の関節で組み直した（素手の質量・形が変わる）のと、急所の狙いの点を実際のセルから求めるようにした（ボットの狙いが変わる）。katana-longsword の途中（half）だけは手も急所も関わらず値が同じ。
/// PhysicsVersion 52（2026-11-18: スタミナを呼吸と循環のモデルに。枝では 50、master の 51 と合わせて 52）で、拳のボットが疲れる組だけ取り直した（教育なしは glove-glove の 2 組、戦略の層ありは 4 組。剣・こんぼう・空手・カタールの教育なしの組はビット単位で同じ: その長さの試合では疲れが効いていない）。
/// PhysicsVersion 49・LogicVersion 53 で全部を取り直した（2026-11-18: ブランチ claude/rush-physics の体の運び（47）・claude/spacing-control-features の間の層（52）・claude/vfc の再生と骨折の治り方（47・48）を合わせた。raytrek の CharacterSamenessProbe）。
/// PhysicsVersion 47 で全部を取り直した（2026-11-18: ブランチ claude/rush-physics の体の運び（打撃の当たる速さを相対速度に・体の質量は手足の押し出しと体自身の動きの分だけ・脳しんとうの係数 100 倍）を master と合わせた。docs/arena/rush_physics.md）。
/// 名簿・ボット・物理の版を意図して変えたら、Probe で取り直して値を差し替える（古い値のまま黙って合わせない）。
/// </summary>
[TestClass]
[TestCategory("slow")]
public sealed class CharacterSamenessTests
{
    // 2026-11-19（PhysicsVersion 49: 滑りの直し、LogicVersion 52: 蹴りの練習のやり直し）: 全部の組を取り直した（CharacterSamenessProbe。値の変わった組だけ書き換わる）。止まっている間に足が流れなくなったので、
    // 値が変わったのはレフェリーのある組・single-* の組・一部の武器の組の終わり（足の置き方の直しと蹴りの差分のため。ほかは前と同じ）。
    // 2026-11-18（PhysicsVersion 44: ダウンの間の審判の指示）: レフェリーのいる試合でダウンが入る組だけ取り直した（CharacterSamenessProbe。ほかの組は変わらない）。
    // 2026-11-18（LogicVersion 50 の作業中）: マージ 000bd92（PhysicsVersion 46）で master 側の値が入り、ブランチの審判の指示で取り直したレフェリーありの組の値が
    // 消えていたので戻した（glove-glove-referee は d6b1257 で取り直した値と同じ。katar-gauntlet-referee の終わりも同じ理由。レフェリーなしの組は変わらない）。
    private static readonly Dictionary<string, (ulong Half, ulong End)> Expected = new()
    {
        ["glove-glove-referee"] = (0xFE40DEDC495A703B, 0x974205954CC78C90),
        ["glove-glove-noref"] = (0xEFE5A702D6719E09, 0x161D1D4CAAE4EC95),
        ["karate-swarmer-noref"] = (0x29386C0B78D8388B, 0x7C444B161083B1F5),
        ["katana-longsword-noref"] = (0x3D119D4A2E8F9C23, 0x7311F50A4A9DFE82),
        ["club-bare-noref"] = (0x4861635153367572, 0x4E5D2C5EB40B1F9B),
        ["katar-gauntlet-referee"] = (0x249745A2F2DCB988, 0x178F00D7F76D2BA0),
        ["single-counter-vs-knuckles-noref"] = (0xF66D353253AAFBD5, 0xBE7411C6FEDACDF0),
        ["single-karate-vs-katana"] = (0x4F87AA0BF5C9642D, 0x7D44256D91646417),
    };

    // 注（LogicVersion 47: 間合いの内側の技を試合で使い、同じ間合いの技の選びを重みで引くようにしたので、剣を握る組の LEGACY・SAME を取り直した。拳・こんぼう同士の組は元のまま一致）。
    // 注（LogicVersion 46）: 刀・ロングソードの型を増やして技の集まりと乱数の引き方が変わったので、剣を握る組（katana-longsword-noref・single-karate-vs-katana・w-*）の
    // 「教科書を JSON へ移す前」の値は、今の LEGACY の行（教科書の古い値）から取り直した。コードに埋めていた頃の試合とのビット一致は、拳・こんぼう同士の組だけが今も確かめている。
    /// <summary>武器の教科書を JSON へ移す前（`weapon handling` 段 1 の最初）に <see cref="CharacterSamenessProbe"/> で取った値（master へ入れるとき、首・脊椎と指の手（PhysicsVersion 35）の上で LEGACY の行から取り直した。拳の組の LEGACY が既存の値と全部一致することを確かめてから。2026-11-16）。</summary>
    private static readonly Dictionary<string, (ulong Half, ulong End)> ExpectedWeapons = new()
    {
        ["w-katana-katana-referee"] = (0x08DD0432F447ADB7, 0x8F6855F0A4CEAF1C),
        ["w-longsword-longsword-noref"] = (0xA702A55D213E5DC1, 0xF91B0AD9F310D05A),
        ["w-katana-longsword-referee"] = (0x62AB52D2277C5AD7, 0x4EC2D3B12CE360F6),
        ["w-club-katana-noref"] = (0x67F6F83507424E0B, 0x9573E68A9C0FAE3F),
        ["w-longsword-club-referee"] = (0x13BDDF49F1528779, 0xF2BCAD64A3CC1D07),
        ["w-club-club-noref"] = (0x9353DF4A844B4B62, 0x8D1B72AEF0F219E4),
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
        ["katana-longsword-noref"] = (0xE02EC494A0B8183E, 0xBCBBB83FC230365B),
        ["single-karate-vs-katana"] = (0x08526BED88283147, 0x4494E3AC5D0D152D),
        ["w-katana-katana-referee"] = (0x50A3119531F83B20, 0xEFBBB866CFB610BE),
        ["w-longsword-longsword-noref"] = (0xA21E5946495C5907, 0x29968029ACAF4299),
        ["w-katana-longsword-referee"] = (0x5F61B7879B05519C, 0xBDDDAEBB4554EC6F),
        ["w-club-katana-noref"] = (0xE618219C324F05B8, 0x61A8BB3497F174CB),
        ["w-longsword-club-referee"] = (0xC67327D2AE0D4D72, 0xD3F480C1819AF771),
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
    /// 層を使う（<c>Weight</c> 1）拳のボットが戦う組だけが変わる。両方とも剣・こんぼうのボット、人が操る側と武器のボットの組、
    /// 層を外した（<c>Weight</c> 0: albert・joao など、docs/BOT_PDCA.md「戦略の層（GamePlan）をキャラごとに採る」）拳のボットの組は、教育に GamePlan があっても同じ。
    /// </summary>
    private static readonly Dictionary<string, (ulong Half, ulong End)> ExpectedGamePlan = new()
    {
        ["glove-glove-referee"] = (0x9EC81639C26EF0A3, 0x95E3893A0DC35D67),
        ["glove-glove-noref"] = (0xBA052324F9AD440E, 0xDC60D9B6B9A625EA),
        ["karate-swarmer-noref"] = (0x353434767276E5BC, 0xBA7C70791EA83D5E),
        ["katar-gauntlet-referee"] = (0x592B172B8916BEB4, 0x99FD4D8904A6B1FB),
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
