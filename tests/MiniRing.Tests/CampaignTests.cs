using MiniRing.Arena;
using MiniRing.Arena.Campaign;
using MiniRing.Characters;

namespace MiniRing.Tests;

/// <summary>
/// 2026-11-26: キャンペーン（自作のボクサーで新人戦 → ランキング戦 → タイトル）。出られるかは体重（階級・前日計量）と戦績と団体のファイルの値だけで決まり
/// 身長では弾かれないこと・どんな体でも出られる新人戦があること（詰まない）・IVF は階級ごとに別の順位と王者を持つこと、
/// ボクサーの作成・保存・読み込み・複数人、戦績と席の更新、減量で体の値・計量の体重・力が同じ式で下がること。
/// </summary>
[TestClass]
public sealed class CampaignTests
{
    private static string TempDir() => Path.Combine(Path.GetTempPath(), $"miniring-campaign-{Guid.NewGuid():N}");

    private static CampaignBoxer NewBoxer(int weightKg)
    {
        var draft = CharacterMaker.NewDraft("standard") with { Id = "test-boxer", Name = "テスト", Country = "japan", Order = 1 };
        var (character, _) = CharacterMaker.ToFile(draft);
        return new CampaignBoxer(character, CampaignCareer.Empty).ChangeWeight(weightKg);
    }

    /// <summary>この体重を作れる身長（<paramref name="tallest"/> なら高い方から探す）の新しいボクサー。</summary>
    private static CampaignBoxer BoxerOfWeight(int weightKg, bool tallest = false)
    {
        var heights = Enumerable.Range(90, 160).Select(h => tallest ? 339 - h : h);
        foreach (var height in heights)
        {
            CampaignBoxer boxer;
            try
            {
                var draft = CharacterMaker.NewDraft("standard", heightCm: height) with { Id = "test-boxer", Name = "テスト", Country = "japan", Order = 1 };
                var (character, _) = CharacterMaker.ToFile(draft);
                boxer = new CampaignBoxer(character, CampaignCareer.Empty);
            }
            catch (ArgumentOutOfRangeException)
            {
                continue; // この身長は作れない
            }

            var (min, max) = boxer.WeightRange;
            if (weightKg >= min && weightKg <= max) { return boxer.ChangeWeight(weightKg); }
        }

        throw new AssertFailedException($"{weightKg} kg の体が作れません。");
    }

    /// <summary>戦績を足したボクサー（試合の中身は見ない。数える範囲だけが効く）。</summary>
    private static CampaignBoxer WithBouts(CampaignBoxer boxer, string org, string division, int wins, int losses = 0)
    {
        var bouts = Enumerable.Range(0, wins + losses)
            .Select(i => new CampaignBout($"{org}-rookie", org, division, "x", i < wins ? BoutResult.Win : BoutResult.Loss, false, boxer.WeightKg, null, null));
        return boxer with { Career = boxer.Career with { Bouts = [.. boxer.Career.Bouts, .. bouts] } };
    }

    private static CampaignEvent Ivf(string tier, string division) => CampaignEvents.Get($"ivf-{tier}-{division}");

    [TestMethod]
    public void NoBodyIsStuck_EveryWeightFrom31To159Kg_HasARookieEvent()
    {
        var rookies = CampaignEvents.All.Where(e => e.Tier == CampaignTier.Rookie).ToList();
        var ivf = Organizations.Get("ivf");
        for (var kg = 31; kg <= 159; kg += 4)
        {
            var boxer = BoxerOfWeight(kg);
            var open = rookies.Where(e => CampaignLadder.Check(e, boxer, CharacterRoster.All).Entry is not null).Select(e => e.Id).ToList();
            Assert.IsTrue(open.Count >= 1, $"{kg} kg: no rookie event is open");
            Assert.IsTrue(open.Contains("vfc-rookie"), $"{kg} kg: VFC rookie has no weight or record condition");
            Assert.IsTrue(open.Contains($"ivf-rookie-{ivf.DivisionOf(boxer.WeightKg).Id}"), $"{kg} kg: IVF has a rookie event in every division");
        }
    }

    [TestMethod]
    public void HeightNeverBlocksEntry_ShortAndTallOfTheSameWeightOpenTheSameEvents()
    {
        foreach (var kg in new[] { 60, 75, 100 })
        {
            var shortOne = BoxerOfWeight(kg);
            var tallOne = BoxerOfWeight(kg, tallest: true);
            Assert.IsTrue(CharacterProfile.HeightOf(tallOne.Character.Build) - CharacterProfile.HeightOf(shortOne.Character.Build) > 20, "the two bodies differ in height");
            foreach (var ev in CampaignEvents.All)
            {
                var a = CampaignLadder.Check(ev, shortOne, CharacterRoster.All).Reason;
                var b = CampaignLadder.Check(ev, tallOne, CharacterRoster.All).Reason;
                Assert.AreEqual(a is null, b is null, $"{ev.Id} at {kg} kg: {a} / {b}");
                Assert.IsFalse((a + b).Contains("身長", StringComparison.Ordinal), $"{ev.Id}: height is not a condition");
            }
        }
    }

    [TestMethod]
    public void IvfWeighIn_OverTheDivisionLimit_IsDisqualifiedWithTheReason()
    {
        var ev = Ivf("rookie", "lightheavy");
        var limit = ev.Division.MaxWholeKg!.Value;
        var at = CampaignLadder.Check(ev, NewBoxer(limit), CharacterRoster.All);
        Assert.IsNull(at.Reason, at.Reason);
        Assert.AreEqual("lightheavy", at.Entry!.Division.Id);
        Assert.AreEqual(at.Entry.Division, ev.Organization.DivisionOf(OrgBouts.WeightOf(at.Entry.Opponent)), "the opponent is in the same division");

        var over = CampaignLadder.Check(ev, NewBoxer(limit + 1), CharacterRoster.All);
        Assert.IsNull(over.Entry, "no bout is made");
        StringAssert.Contains(over.Reason, "前日計量");
        StringAssert.Contains(over.Reason, "失格");
        Assert.IsNull(CampaignLadder.Check(Ivf("rookie", "cruiser"), NewBoxer(limit + 1), CharacterRoster.All).Reason, "the next division up takes the boxer");

        // 計量のない団体（WVC・VFC）は体重を発表するだけで弾かない。
        Assert.IsFalse(Organizations.Get("wvc").Entry.WeighIn);
        Assert.IsTrue(Organizations.Get("ivf").Entry.WeighIn);
        Assert.IsNull(CampaignLadder.Check(CampaignEvents.Get("wvc-rookie"), BoxerOfWeight(150), CharacterRoster.All).Reason);
    }

    [TestMethod]
    public void WeightChange_MovesTheBoxerAcrossDivisions_AndReChecksEntry()
    {
        var heavy = NewBoxer(85);
        StringAssert.Contains(CampaignLadder.Check(Ivf("rookie", "lightheavy"), heavy, CharacterRoster.All).Reason, "失格");
        Assert.AreEqual("cruiser", CampaignLadder.Check(Ivf("rookie", "cruiser"), heavy, CharacterRoster.All).Entry!.Division.Id);
        var cut = heavy.ChangeWeight(78);
        Assert.AreEqual("lightheavy", CampaignLadder.Check(Ivf("rookie", "lightheavy"), cut, CharacterRoster.All).Entry!.Division.Id);
        StringAssert.Contains(CampaignLadder.Check(Ivf("rookie", "cruiser"), cut, CharacterRoster.All).Reason, "ライトヘビー級", "too light for the cruiserweight event: says which division the boxer is in");
    }

    [TestMethod]
    public void RookieEntry_IsFourProBoutsAtMost_CountedAcrossOrgs_ExceptVfc()
    {
        var boxer = NewBoxer(70);
        var division = Organizations.Get("ivf").DivisionOf(boxer.WeightKg).Id;
        var third = WithBouts(boxer, "wvc", "open", wins: 2, losses: 1); // 次が 4 戦目
        var fourth = WithBouts(third, "vfc", "open", wins: 1);            // 次が 5 戦目
        foreach (var id in new[] { $"ivf-rookie-{division}", "wvc-rookie" })
        {
            var ev = CampaignEvents.Get(id);
            Assert.IsNull(CampaignLadder.Check(ev, third, CharacterRoster.All).Reason, $"{id}: the 4th pro bout is still a rookie bout");
            StringAssert.Contains(CampaignLadder.Check(ev, fourth, CharacterRoster.All).Reason, "4 戦目まで", $"{id}: the 5th is not");
        }

        Assert.IsNull(CampaignLadder.Check(CampaignEvents.Get("vfc-rookie"), WithBouts(fourth, "ivf", division, wins: 10), CharacterRoster.All).Reason, "VFC rookie has no record condition");
    }

    [TestMethod]
    public void RankingEntry_ThreeWinsInTheDivisionForIvf_ThreeProWinsForWvc_OneVfcWinForVfc()
    {
        var boxer = NewBoxer(70);
        var org = Organizations.Get("ivf");
        var division = org.DivisionOf(boxer.WeightKg).Id;
        var other = org.Divisions.First(d => d.Id != division).Id;
        var ranking = Ivf("ranking", division);

        StringAssert.Contains(CampaignLadder.Check(ranking, WithBouts(boxer, "ivf", division, wins: 2), CharacterRoster.All).Reason, "3 勝から");
        Assert.IsNull(CampaignLadder.Check(ranking, WithBouts(boxer, "ivf", division, wins: 3), CharacterRoster.All).Reason, "3 wins in the division open the ranking bout");
        Assert.IsNotNull(CampaignLadder.Check(ranking, WithBouts(boxer, "ivf", other, wins: 3), CharacterRoster.All).Reason, "wins in another division do not count");
        Assert.IsNotNull(CampaignLadder.Check(ranking, WithBouts(boxer, "wvc", "open", wins: 3), CharacterRoster.All).Reason, "wins in another org do not count");

        var wvc = CampaignEvents.Get("wvc-ranking");
        Assert.IsNotNull(CampaignLadder.Check(wvc, WithBouts(boxer, "ivf", division, wins: 2), CharacterRoster.All).Reason);
        Assert.IsNull(CampaignLadder.Check(wvc, WithBouts(boxer, "ivf", division, wins: 3), CharacterRoster.All).Reason, "WVC counts pro wins anywhere");

        var vfc = CampaignEvents.Get("vfc-ranking");
        Assert.IsNotNull(CampaignLadder.Check(vfc, boxer, CharacterRoster.All).Reason);
        Assert.IsNull(CampaignLadder.Check(vfc, WithBouts(boxer, "vfc", "open", wins: 1), CharacterRoster.All).Reason);
    }

    [TestMethod]
    public void IvfSeats_AreSeparatePerDivision_EachWithItsOwnChampion()
    {
        var org = Organizations.Get("ivf");
        var standings = OrgStandings.Of(org);
        foreach (var tier in new[] { CampaignTier.Rookie, CampaignTier.Ranking, CampaignTier.Title })
        {
            CollectionAssert.AreEquivalent(org.Divisions.Select(d => d.Id).ToList(),
                CampaignEvents.All.Where(e => e.Org == "ivf" && e.Tier == tier).Select(e => e.Division.Id).ToList(), $"an IVF {tier} event per division");
        }

        var champions = org.Divisions.Select(d => standings.For(d).Champion).ToList();
        Assert.AreEqual(champions.Count, champions.Distinct().Count(), "each division has its own champion");

        // ミドル級で 1 位のボクサー: ミドル級のタイトルに出られ、相手はミドル級の王者。勝っても他の階級の席はランキング外のまま。
        var boxer = NewBoxer(70);
        var middle = org.DivisionOf(boxer.WeightKg);
        var heavier = org.Divisions[org.Divisions.ToList().IndexOf(middle) + 1];
        boxer = boxer with { Career = boxer.Career.WithSeat("ivf", middle.Id, 1) };
        var title = CampaignLadder.Check(Ivf("title", middle.Id), boxer, CharacterRoster.All);
        Assert.AreEqual(standings.For(middle).Champion, title.Entry!.Opponent.Id);
        boxer = boxer.AfterBout(title.Entry, BoutResult.Win, finish: true, weapon: null, standings.For(middle).Ranking.Count);
        Assert.AreEqual(0, boxer.Career.SeatIn("ivf", middle.Id), "champion of the middleweight division");
        Assert.IsNull(boxer.Career.SeatIn("ivf", heavier.Id), "other divisions keep their own ranking");

        // 増量して上の階級へ行くと、そこはランキング外から（タイトルには出られない）。元の階級の王座は前日計量で失格。
        var up = boxer.ChangeWeight(heavier.MaxWholeKg!.Value);
        StringAssert.Contains(CampaignLadder.Check(Ivf("title", heavier.Id), up, CharacterRoster.All).Reason, "ランキング外");
        StringAssert.Contains(CampaignLadder.Check(Ivf("title", middle.Id), up, CharacterRoster.All).Reason, "失格");
    }

    [TestMethod]
    public void WeaponsFollowTheOrgFile()
    {
        Assert.IsTrue(CampaignLadder.Check(CampaignEvents.Get("vfc-rookie"), NewBoxer(70), CharacterRoster.All).Entry!.Weapons.Count > 1, "VFC allows weapons, so the boxer can pick one per event");
        var division = Organizations.Get("ivf").DivisionOf(70).Id;
        Assert.AreEqual(1, CampaignLadder.Check(Ivf("rookie", division), NewBoxer(70), CharacterRoster.All).Entry!.Weapons.Count, "IVF: bare hands only");
    }

    [TestMethod]
    public void Ladder_RookieWins_OpenTheRankingBout_RankingWinsClimb_ThenTheTitle()
    {
        var boxer = NewBoxer(70);
        var org = Organizations.Get("ivf");
        var division = org.DivisionOf(boxer.WeightKg);
        var ranked = OrgStandings.Of(org).For(division).Ranking.Count;
        Assert.IsTrue(ranked >= 1, "IVF middleweight has a ranking in the standings file");

        StringAssert.Contains(CampaignLadder.Check(Ivf("ranking", division.Id), boxer, CharacterRoster.All).Reason, "勝ち");
        StringAssert.Contains(CampaignLadder.Check(Ivf("title", division.Id), boxer, CharacterRoster.All).Reason, "ランキング外");
        for (var i = 0; i < 3; i++)
        {
            var rookie = CampaignLadder.Check(Ivf("rookie", division.Id), boxer, CharacterRoster.All).Entry!;
            boxer = boxer.AfterBout(rookie, BoutResult.Win, finish: true, weapon: null, ranked);
        }

        Assert.AreEqual(3, boxer.Career.Wins);
        Assert.IsNull(boxer.Career.SeatIn("ivf", division.Id), "rookie wins do not seat the boxer");
        for (var expected = ranked; expected >= 1; expected--)
        {
            var bout = CampaignLadder.Check(Ivf("ranking", division.Id), boxer, CharacterRoster.All);
            if (bout.Entry is null) { break; }
            boxer = boxer.AfterBout(bout.Entry, BoutResult.Win, finish: false, weapon: null, ranked);
            Assert.AreEqual(expected, boxer.Career.SeatIn("ivf", division.Id));
        }

        Assert.AreEqual(1, boxer.Career.SeatIn("ivf", division.Id));
        var title = CampaignLadder.Check(Ivf("title", division.Id), boxer, CharacterRoster.All);
        Assert.AreEqual(OrgStandings.Of(org).For(division).Champion, title.Entry!.Opponent.Id);
        var losing = boxer.AfterBout(title.Entry, BoutResult.Loss, finish: true, weapon: null, ranked);
        Assert.AreEqual(1, losing.Career.SeatIn("ivf", division.Id), "a failed challenge keeps the seat");
        boxer = boxer.AfterBout(title.Entry, BoutResult.Win, finish: true, weapon: null, ranked);
        Assert.AreEqual(0, boxer.Career.SeatIn("ivf", division.Id));
        Assert.AreEqual("王者", CampaignLadder.SeatName(boxer.Career.SeatIn("ivf", division.Id)));
    }

    [TestMethod]
    public void WeightCut_ThinsTheMuscle_LowersTheWeighIn_AndTheStrengthBySameSection()
    {
        var boxer = NewBoxer(72);
        var cut = boxer.ChangeWeight(66);
        Assert.AreEqual(66, cut.WeightKg);
        Assert.AreEqual(boxer.Character.Build.Height, cut.Character.Build.Height, "the skeleton stays");
        Assert.AreEqual(boxer.Character.Build.Arms, cut.Character.Build.Arms);
        Assert.IsTrue(cut.Character.Build.Muscle < boxer.Character.Build.Muscle);
        // 力は断面（SectionRatio）に比例する（§1.7）。減量で太さが減った分だけ同じ式で落ち、隠れた倍率は無い。
        Assert.IsTrue(cut.Character.Build.SectionRatio < boxer.Character.Build.SectionRatio);
        var ratio = cut.Character.Build.Muscle / boxer.Character.Build.Muscle;
        Assert.AreEqual(ratio * ratio, cut.Character.Build.SectionRatio / boxer.Character.Build.SectionRatio, 1e-5);
        Assert.AreEqual(new WeightChange(0, 72, 66), cut.Career.WeightLog[^1]);

        var (min, max) = boxer.WeightRange;
        Assert.ThrowsExactly<ArgumentOutOfRangeException>(() => boxer.ChangeWeight(min - 1));
        Assert.ThrowsExactly<ArgumentOutOfRangeException>(() => boxer.ChangeWeight(max + 1));
    }

    [TestMethod]
    public void Store_CreatesManyBoxers_SavesLoadsAndDeletes_OutsideTheRoster()
    {
        var dir = TempDir();
        try
        {
            var store = new CampaignStore(dir);
            var a = store.Create(CharacterMaker.NewDraft("heavy"));
            var b = store.Create(CharacterMaker.NewDraft("slim") with { Name = "細い人" });
            var c = store.Create(CharacterMaker.NewDraft("rangy"));
            Assert.AreEqual(3, new[] { a.Id, b.Id, c.Id }.Distinct().Count());
            Assert.IsFalse(CharacterRoster.All.Any(r => r.Id == a.Id || r.Id == b.Id || r.Id == c.Id), "boxers never collide with the fictional roster");
            Assert.AreEqual("細い人", b.Character.Name);

            var entry = CampaignLadder.Check(CampaignEvents.Get("wvc-rookie"), a, CharacterRoster.All).Entry!;
            var played = a.AfterBout(entry, BoutResult.Win, finish: true, weapon: MiniRing.Weapons.WeaponKind.Katana, rankedCount: 3).ChangeWeight(a.WeightKg - 2);
            store.Save(played);

            var loaded = store.LoadAll();
            Assert.AreEqual(3, loaded.Count);
            var back = loaded.Single(x => x.Id == a.Id);
            Assert.AreEqual(played.WeightKg, back.WeightKg);
            Assert.AreEqual(played.Character.Build, back.Character.Build);
            CollectionAssert.AreEqual(played.Career.Bouts.ToArray(), back.Career.Bouts.ToArray());
            CollectionAssert.AreEqual(played.Career.WeightLog.ToArray(), back.Career.WeightLog.ToArray());
            Assert.AreEqual("katana", back.Career.Bouts[0].Weapon);
            Assert.IsNull(back.Character.Weapon, "the event's weapon is not written into the body file");

            store.Delete(b.Id);
            Assert.AreEqual(2, store.LoadAll().Count);
        }
        finally
        {
            if (Directory.Exists(dir)) { Directory.Delete(dir, recursive: true); }
        }
    }

    [TestMethod]
    public void Outcome_ComesFromTheFinishedMatchOnly()
    {
        Assert.IsNull(BoutOutcome.Of(false, 1, 0, null), "an unfinished match records nothing");
        Assert.AreEqual(new BoutOutcome(BoutResult.Win, true), BoutOutcome.Of(true, 1, 0, null));
        Assert.AreEqual(new BoutOutcome(BoutResult.Loss, false), BoutOutcome.Of(true, 0, 1, "X WINS BY DECISION"));
        Assert.AreEqual(new BoutOutcome(BoutResult.Draw, false), BoutOutcome.Of(true, 0, 0, "DRAW"));
    }

    [TestMethod]
    public void EventFile_RejectsUnknownFieldsAndOrgs_AndExpandsPerDivision()
    {
        Assert.ThrowsExactly<InvalidDataException>(() => CampaignEvents.FromJson("""{ "events": [ { "id": "x", "name": "x", "org": "ivf", "tier": "rookie", "minHeightCm": 140 } ] }"""),
            "height conditions are gone from the file format");
        Assert.ThrowsExactly<InvalidDataException>(() => CampaignEvents.FromJson("""{ "events": [ { "id": "x", "name": "x", "org": "nope", "tier": "rookie" } ] }"""));
        var expanded = CampaignEvents.FromJson("""{ "events": [ { "id": "x", "name": "X {division}", "org": "ivf", "tier": "rookie", "record": { "scope": "career", "maxBouts": 4 } } ] }""");
        Assert.AreEqual(Organizations.Get("ivf").Divisions.Count, expanded.Count);
        Assert.AreEqual("X ヘビー級", expanded[^1].Name);
        Assert.AreEqual(3 + (3 * Organizations.Get("ivf").Divisions.Count) + 3, CampaignEvents.All.Count);
    }

    /// <summary>既定の置き場は LocalApplicationData の下のゲームのフォルダー（作業フォルダーでない）。</summary>
    [TestMethod]
    public void DefaultDirectory_IsUnderLocalAppData_GameFolder()
    {
        var root = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData, Environment.SpecialFolderOption.DoNotVerify);
        var dir = CampaignStore.DefaultDirectory("fallback");
        if (string.IsNullOrEmpty(root)) { Assert.AreEqual("fallback", dir); return; }
        Assert.AreEqual(Path.Combine(root, CampaignStore.AppFolderName, "campaign"), dir);
    }

    /// <summary>古い置き場のボクサーは新しい置き場へ写り（古い方は残る）、新しい方にある名前は上書きしない。2 度目は何も写さない。</summary>
    [TestMethod]
    public void CopyLegacy_CopiesOnce_KeepsOld_DoesNotOverwriteNew()
    {
        var legacy = TempDir();
        var target = TempDir();
        try
        {
            Directory.CreateDirectory(legacy);
            Directory.CreateDirectory(target);
            File.WriteAllText(Path.Combine(legacy, "a.json"), "old-a");
            File.WriteAllText(Path.Combine(legacy, "b.json"), "old-b");
            File.WriteAllText(Path.Combine(legacy, "note.txt"), "not a boxer");
            File.WriteAllText(Path.Combine(target, "b.json"), "new-b");

            Assert.AreEqual(1, CampaignStore.CopyLegacy(legacy, target));
            Assert.AreEqual("old-a", File.ReadAllText(Path.Combine(target, "a.json")));
            Assert.AreEqual("new-b", File.ReadAllText(Path.Combine(target, "b.json")), "the new place wins");
            Assert.IsFalse(File.Exists(Path.Combine(target, "note.txt")));
            Assert.IsTrue(File.Exists(Path.Combine(legacy, "a.json")), "the old file stays");
            Assert.AreEqual(0, CampaignStore.CopyLegacy(legacy, target));
            Assert.AreEqual(0, CampaignStore.CopyLegacy(Path.Combine(legacy, "missing"), target));
            Assert.AreEqual(0, CampaignStore.CopyLegacy(target, target));
        }
        finally
        {
            if (Directory.Exists(legacy)) { Directory.Delete(legacy, recursive: true); }
            if (Directory.Exists(target)) { Directory.Delete(target, recursive: true); }
        }
    }
}
