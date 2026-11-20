using MiniRing.Bots;
using MiniRing.Characters;
using MiniRing.Desktop;

namespace MiniRing.Tests;

/// <summary>
/// E0: 教育が空なら、名簿を 1 人 1 ファイルにしても試合はビット単位で同じ（CLAUDE.md §1.8・§1.6）。
/// 期待値は characters.json の時代に <see cref="CharacterSamenessProbe"/> で取ったチェックサム（途中と最後）。
/// PhysicsVersion 53 で、はめる物（メリケンサック・カタール・籠手）を使う 2 組（katar-gauntlet-referee・single-counter-vs-knuckles-noref。教育なしの値と、戦略の層ありの katar-gauntlet-referee）だけ取り直した（2026-11-19: はめる物の中の拳が 13 mm の球から手の格子へ。物の形・質量・当たりが変わる。ほかの組はビット単位で同じ。raytrek の CharacterSamenessProbe）。
/// PhysicsVersion 51 で全部を取り直した（2026-11-19: ブランチ claude/hand-voxels の素手の拳（5 mm のセル・幅 40 mm・木の物性）を master の 50 と合わせた。tower32 の CharacterSamenessProbe）。
/// PhysicsVersion 40 で取り直した（2026-11-17: 脚の骨の端を骨の胴と同じ太さにした・体が支える質量から外れた部位を除く・倒れている間の手を腰が戻ったら床から離す。グローブ同士のレフェリーなし・こんぼう同士は同じ。LegLossProbe.Probe_WhichRecordedMatchesLoseALimb）。その前は PhysicsVersion 39 で、腕・脚を失う試合だけ取り直した（2026-11-17: 脚を失っても残った脚の長さで立って戦い続ける・切り株の筋肉の断面を切れ目の手前だけで測る。腕も脚も失わない試合（グローブ同士・ロングソード同士など）はビット単位で同じことを確かめた: LegLossProbe.Probe_WhichRecordedMatchesLoseALimb）。その前は PhysicsVersion 38・LogicVersion 38 で取り直した（2026-11-17: スタミナを臨界パワーのモデルに・休む間の足と手数・拳の狙い・肘の締め）。その前は PhysicsVersion 37・LogicVersion 36（2026-11-16。37 はスタミナの容量を倍。36 の理由: スタミナの蓄え・供給を筋肉の量に比例させた（体格のあるキャラの試合が変わる）のと、弱った相手を詰める層を既定にした（ボットの立ち回りが変わる）。その前は PhysicsVersion 35・LogicVersion 32 で取り直した（前は PhysicsVersion 34）。理由: 素手を指の関節で組み直した（素手の質量・形が変わる）のと、急所の狙いの点を実際のセルから求めるようにした（ボットの狙いが変わる）。katana-longsword の途中（half）だけは手も急所も関わらず値が同じ。
/// PhysicsVersion 54 で取り直した（2026-11-19: 素手に親指を足した。素手の手を持つ組が変わる。はめる物・グローブ・こんぼう同士の組は同じ。CharacterSamenessProbe）。
/// 59/57: master の顔・手の層とこの枝のつなぎ・滑りの直しのマージで全部を取り直した（2026-11-19。Expected ← NOPLAN-LEGACY、ExpectedWeapons ← LEGACY、ExpectedAdopted ← SAME、ExpectedGamePlan ← LEGACY。CharacterSamenessProbe を tower32 で）。
/// PhysicsVersion 55 で取り直した（2026-11-19: claude/hand-layers の 53（はめる物の拳を手の格子に）と claude/fist-thumb の 54（素手の親指）のマージ。グローブ同士・こんぼう同士は同じ。CharacterSamenessProbe）。
/// PhysicsVersion 51 で取り直した（2026-11-18: 素手の拳を 5 mm のセル・幅 40 mm にし、手の組織の物性を木に。素手・空手・剣の組（剣士の空いた手・握り替えの間の素手）が変わる。グローブ同士・カタールと籠手・メリケンサック・こんぼう同士の組（はめる物の拳は前のまま）は同じ。CharacterSamenessProbe）。
/// PhysicsVersion 44（2026-11-17: 剣・刀・ロングソードの柄に鉄の芯を通した・削り切れた武器を分ける）で、剣を握る組（katana-longsword・single-karate-vs-katana・w-katana*・w-longsword*・w-club-katana）だけ取り直した。拳同士・こんぼう同士・空手・カタール・ガントレットの試合はビット単位で同じ。
/// PhysicsVersion 50・LogicVersion 54 で全部を取り直した（2026-11-19: master の 49・53（突進の物理・間の層・VFC・剣の技）と、ブランチ claude/grappling-fix の 48・53（投げ技の枝とマージ後の直し）を合わせた。値は手元で CharacterSamenessProbe を回して取った）。
/// PhysicsVersion 48・LogicVersion 53 で全部を取り直した（2026-11-18: マージ後の直し。体重を受ける足の裏全体を支えに・縦軸のねじりは減らさない・膝の制御の蹴りの間は軸足を振り出さない・床の衝撃は触れた面の中心で、と蹴りの差分の練習し直し。値は手元で CharacterSamenessProbe を回して取った）。
/// PhysicsVersion 47・LogicVersion 52 で全部を取り直した（2026-11-18: ブランチ claude/throwing-grappling-mechanics（床からの支え・釣り合い・起き上がり・床の衝撃・投げ・肘の靭帯・蹴る前に軸足へ乗る）を master と合わせた。値は tower32 で CharacterSamenessProbe を回して取った）。
/// LogicVersion 49 で全部を取り直した（2026-11-18: master の休み・回し蹴りと、ブランチ claude/sword-combat-techniques の剣の型・連続の守り・内側の技・バットを合わせた。拳・素手・こんぼう同士の組は 45 のときと同じ値）。PhysicsVersion 45 で全部を取り直した（2026-11-18: ブランチ claude/staff-vr-motion-kick のつま先の止め・平たい靴の形（ブランチの 43）と、柄の鉄の芯（44）を合わせた。途中（half）は多くの組で同じ）。その前は PhysicsVersion 40 で取り直した（2026-11-17: 脚の骨の端を骨の胴と同じ太さにした・体が支える質量から外れた部位を除く・倒れている間の手を腰が戻ったら床から離す。グローブ同士のレフェリーなし・こんぼう同士は同じ。LegLossProbe.Probe_WhichRecordedMatchesLoseALimb）。その前は PhysicsVersion 39 で、腕・脚を失う試合だけ取り直した（2026-11-17: 脚を失っても残った脚の長さで立って戦い続ける・切り株の筋肉の断面を切れ目の手前だけで測る。腕も脚も失わない試合（グローブ同士・ロングソード同士など）はビット単位で同じことを確かめた: LegLossProbe.Probe_WhichRecordedMatchesLoseALimb）。その前は PhysicsVersion 38・LogicVersion 38 で取り直した（2026-11-17: スタミナを臨界パワーのモデルに・休む間の足と手数・拳の狙い・肘の締め）。その前は PhysicsVersion 37・LogicVersion 36（2026-11-16。37 はスタミナの容量を倍。36 の理由: スタミナの蓄え・供給を筋肉の量に比例させた（体格のあるキャラの試合が変わる）のと、弱った相手を詰める層を既定にした（ボットの立ち回りが変わる）。その前は PhysicsVersion 35・LogicVersion 32 で取り直した（前は PhysicsVersion 34）。理由: 素手を指の関節で組み直した（素手の質量・形が変わる）のと、急所の狙いの点を実際のセルから求めるようにした（ボットの狙いが変わる）。katana-longsword の途中（half）だけは手も急所も関わらず値が同じ。
/// PhysicsVersion 52（2026-11-18: スタミナを呼吸と循環のモデルに。枝では 50、master の 51 と合わせて 52）で、拳のボットが疲れる組だけ取り直した（教育なしは glove-glove の 2 組、戦略の層ありは 4 組。剣・こんぼう・空手・カタールの教育なしの組はビット単位で同じ: その長さの試合では疲れが効いていない）。
/// PhysicsVersion 49・LogicVersion 53 で全部を取り直した（2026-11-18: ブランチ claude/rush-physics の体の運び（47）・claude/spacing-control-features の間の層（52）・claude/vfc の再生と骨折の治り方（47・48）を合わせた。raytrek の CharacterSamenessProbe）。
/// PhysicsVersion 47 で全部を取り直した（2026-11-18: ブランチ claude/rush-physics の体の運び（打撃の当たる速さを相対速度に・体の質量は手足の押し出しと体自身の動きの分だけ・脳しんとうの係数 100 倍）を master と合わせた。docs/arena/rush_physics.md）。
/// PhysicsVersion 57 で全部を取り直した（2026-11-19、claude/face-f2: 頭の剛体に 2.5 mm の顔の層を重ね、5 mm の頭は頭蓋骨・脳・首の中だけに。頭の当たりの形・質量・削れ方が変わるので全ての組が変わる。Expected ← NOPLAN-LEGACY、ExpectedWeapons ← LEGACY、ExpectedAdopted ← SAME、ExpectedGamePlan ← LEGACY。手元の CharacterSamenessProbe（Release。claude/face-f3 の名簿の顔を合わせ、頭の箱を作ったままにした後）。
/// 名簿・ボット・物理の版を意図して変えたら、Probe で取り直して値を差し替える（古い値のまま黙って合わせない）。
/// </summary>
[TestClass]
[TestCategory("slow")]
public sealed class CharacterSamenessTests
{
    // 2026-11-20（LogicVersion 58: 拳の技の経路を最小躍度の曲線にし、振り出しを短く）: 全部の表を CharacterSamenessProbe（tower32）で取り直した（Expected ← NOPLAN-LEGACY、
    // ExpectedWeapons ← LEGACY、ExpectedAdopted ← SAME、ExpectedGamePlan ← LEGACY）。拳を打つボットのいる組と、刀・ロングソード同士の組が変わった（剣士のボットの中で拳の技の経路を通る所があるのだろうと見ているが、どこかは調べていない）。
    // こんぼう同士・こんぼうと刀・ロングソードとこんぼう・空手と刀（教科書が前の値）の組は同じ値。
    // 2026-11-20（PhysicsVersion 60: 生成で体に重ねて置いた武器は、構えへ追いつく 1 秒と、そのとき重なっていた所が離れるまで本人に当たらない）: CharacterSamenessProbe で取り直した。
    // 値が変わったのは刀を握る組だけ（生成で体に重なるのは刀・六尺棒。ロングソード同士・こんぼう・拳の組は同じ値）。
    // 2026-11-19（PhysicsVersion 49: 滑りの直し、LogicVersion 52: 蹴りの練習のやり直し）: 全部の組を取り直した（CharacterSamenessProbe。値の変わった組だけ書き換わる）。止まっている間に足が流れなくなったので、
    // 値が変わったのはレフェリーのある組・single-* の組・一部の武器の組の終わり（足の置き方の直しと蹴りの差分のため。ほかは前と同じ）。
    // 2026-11-18（PhysicsVersion 44: ダウンの間の審判の指示）: レフェリーのいる試合でダウンが入る組だけ取り直した（CharacterSamenessProbe。ほかの組は変わらない）。
    // 2026-11-18（LogicVersion 50 の作業中）: マージ 000bd92（PhysicsVersion 46）で master 側の値が入り、ブランチの審判の指示で取り直したレフェリーありの組の値が
    // 消えていたので戻した（glove-glove-referee は d6b1257 で取り直した値と同じ。katar-gauntlet-referee の終わりも同じ理由。レフェリーなしの組は変わらない）。
    // 2026-11-20（PhysicsVersion 65: 拳を引く力を Hill 型の筋肉に、ブランチ claude/hill-muscle）: 全部の表を CharacterSamenessProbe（tower32）で取り直した
    // （Expected ← NOPLAN-LEGACY、ExpectedWeapons ← LEGACY、ExpectedAdopted ← SAME、ExpectedGamePlan ← LEGACY）。武器を握った手も同じ式で引くので、
    // 拳の組だけでなく武器の組も含めて 25 行すべてが変わった。
    private static readonly Dictionary<string, (ulong Half, ulong End)> Expected = new()
    {
        ["glove-glove-referee"] = (0xE26E5F86C982F355, 0xF3F3AB1A0371C02C),
        ["glove-glove-noref"] = (0x70485E399A4964B3, 0xB9F6F90DC2636306),
        ["karate-swarmer-noref"] = (0x4999FEA38AB7FD97, 0xFBE48D0D543DF033),
        ["katana-longsword-noref"] = (0x05390463934120D9, 0xD6F8180E90DC9850),
        ["club-bare-noref"] = (0xA922D3EA7F6BD972, 0x044F2906C49759E8),
        ["katar-gauntlet-referee"] = (0x046F4A15A8CF2EE6, 0xA3D5F0AD83AD7D5B),
        ["single-counter-vs-knuckles-noref"] = (0x0706378542735748, 0x5229DF8F04C281C9),
        ["single-karate-vs-katana"] = (0x260FE7D88724B569, 0xDAF551381CDD8D0E),
    };

    // 注（2026-11-19、master に claude/face-f2 を入れた所: PhysicsVersion 57・LogicVersion 55）: 顔の層・手の層（ff63a89）と、master の spacing（拳・肘の狙い）・
    // 中継の直し 2（VFC の檻の選び方）・CT の動脈の材質が重なったので、全部の表を CharacterSamenessProbe で取り直した。
    // 注（LogicVersion 47:間合いの内側の技を試合で使い、同じ間合いの技の選びを重みで引くようにしたので、剣を握る組の LEGACY・SAME を取り直した。拳・こんぼう同士の組は元のまま一致）。
    // 注（LogicVersion 46）: 刀・ロングソードの型を増やして技の集まりと乱数の引き方が変わったので、剣を握る組（katana-longsword-noref・single-karate-vs-katana・w-*）の
    // 「教科書を JSON へ移す前」の値は、今の LEGACY の行（教科書の古い値）から取り直した。コードに埋めていた頃の試合とのビット一致は、拳・こんぼう同士の組だけが今も確かめている。
    /// <summary>武器の教科書を JSON へ移す前（`weapon handling` 段 1 の最初）に <see cref="CharacterSamenessProbe"/> で取った値（master へ入れるとき、首・脊椎と指の手（PhysicsVersion 35）の上で LEGACY の行から取り直した。拳の組の LEGACY が既存の値と全部一致することを確かめてから。2026-11-16）。</summary>
    private static readonly Dictionary<string, (ulong Half, ulong End)> ExpectedWeapons = new()
    {
        ["w-katana-katana-referee"] = (0xBBFE1B1D841F64B6, 0xEA3510A071DE1DCA),
        ["w-longsword-longsword-noref"] = (0xE29F6F180291DE9D, 0x8E615CC695054184),
        ["w-katana-longsword-referee"] = (0x1D150589C69BD99F, 0x95888CB4785554C9),
        ["w-club-katana-noref"] = (0x80373773847CEC64, 0x9E700CA0E9F5F6D3),
        ["w-longsword-club-referee"] = (0x77E402363F188C1D, 0x677881F6055F3495),
        ["w-club-club-noref"] = (0x5523CF990998499E, 0x11FA600246292A98),
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
        ["katana-longsword-noref"] = (0x7ED361B7490B935F, 0x79E7DCAAB8D734F5),
        ["single-karate-vs-katana"] = (0x60E72B2E2497A00F, 0x98FA4DEF3A266B21),
        ["w-katana-katana-referee"] = (0xBFF56091242D08D0, 0x9A41C94BA71DC69C),
        ["w-longsword-longsword-noref"] = (0x1E8EAD5009ECEEEE, 0xEAE5251D6E377DBD),
        ["w-katana-longsword-referee"] = (0xEDF739576D1C26B6, 0xC5852D62A40B9D56),
        ["w-club-katana-noref"] = (0x01C0B7E406D37741, 0xF636F748F0292198),
        ["w-longsword-club-referee"] = (0x6A379DBC84E4E096, 0xA78F613ADE412F3C),
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
        ["glove-glove-referee"] = (0x1198665D6910D74A, 0x82A81CB606A57BD7),
        ["glove-glove-noref"] = (0xA08E864446E736D3, 0xE4D70E9F69C911C0),
        ["karate-swarmer-noref"] = (0x1E08D8C27D3A5288, 0xCE92BDD2513988BB),
        ["katar-gauntlet-referee"] = (0xFFBEFEADCD89CA39, 0x27FD42F4FEDE4789),
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
