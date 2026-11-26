using MiniRing.Arena;
using MiniRing.Arena.Campaign;
using MiniRing.Characters;

namespace MiniRing.Tests;

/// <summary>
/// 2026-11-26: キャンペーン（自作のボクサーで新人戦 → ランキング戦 → タイトル）。出られる大会は体（計量の体重・身長）と団体のファイルの値から出ること、
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

    [TestMethod]
    public void IvfRookie_TakesUpToLightHeavy_ByWeighIn_AndSaysWhyNotAbove()
    {
        var ev = CampaignEvents.Get("ivf-rookie");
        var limit = ev.Organization.Division("lightheavy").LimitKg!.Value;
        var at = CampaignLadder.Check(ev, NewBoxer(limit), CharacterRoster.All);
        Assert.IsNull(at.Reason, at.Reason);
        Assert.AreEqual("lightheavy", at.Entry!.Division.Id);
        Assert.AreEqual(at.Entry.Division, ev.Organization.DivisionOf(OrgBouts.WeightOf(at.Entry.Opponent)), "the opponent is in the same division");

        var over = CampaignLadder.Check(ev, NewBoxer(limit + 1), CharacterRoster.All);
        Assert.IsNull(over.Entry);
        StringAssert.Contains(over.Reason, "クルーザー級");
    }

    [TestMethod]
    public void VfcRookie_HasAWeightFloor_FromTheEventFile()
    {
        var ev = CampaignEvents.Get("vfc-rookie");
        var floor = ev.MinKg!.Value;
        StringAssert.Contains(CampaignLadder.Check(ev, NewBoxer(floor - 1), CharacterRoster.All).Reason, "軽すぎ");
        var ok = CampaignLadder.Check(ev, NewBoxer(floor), CharacterRoster.All);
        Assert.IsNull(ok.Reason, ok.Reason);
        Assert.IsTrue(ok.Entry!.Weapons.Count > 1, "VFC allows weapons, so the boxer can pick one per event");
        Assert.AreEqual(1, CampaignLadder.Check(CampaignEvents.Get("ivf-rookie"), NewBoxer(70), CharacterRoster.All).Entry!.Weapons.Count, "IVF: bare hands only");
    }

    [TestMethod]
    public void Ladder_RookieWins_OpenTheRankingBout_RankingWinsClimb_ThenTheTitle()
    {
        var boxer = NewBoxer(70);
        var org = Organizations.Get("ivf");
        var division = org.DivisionOf(boxer.WeightKg);
        var ranked = OrgStandings.Of(org).For(division).Ranking.Count;
        Assert.IsTrue(ranked >= 1, "IVF middleweight has a ranking in the standings file");

        StringAssert.Contains(CampaignLadder.Check(CampaignEvents.Get("ivf-ranking"), boxer, CharacterRoster.All).Reason, "勝ち");
        StringAssert.Contains(CampaignLadder.Check(CampaignEvents.Get("ivf-title"), boxer, CharacterRoster.All).Reason, "ランキング外");
        for (var i = 0; i < 2; i++)
        {
            var rookie = CampaignLadder.Check(CampaignEvents.Get("ivf-rookie"), boxer, CharacterRoster.All).Entry!;
            boxer = boxer.AfterBout(rookie, BoutResult.Win, finish: true, weapon: null, ranked);
        }

        Assert.AreEqual(2, boxer.Career.Wins);
        Assert.IsNull(boxer.Career.SeatIn("ivf", division.Id), "rookie wins do not seat the boxer");
        for (var expected = ranked; expected >= 1; expected--)
        {
            var bout = CampaignLadder.Check(CampaignEvents.Get("ivf-ranking"), boxer, CharacterRoster.All);
            if (bout.Entry is null) { break; }
            boxer = boxer.AfterBout(bout.Entry, BoutResult.Win, finish: false, weapon: null, ranked);
            Assert.AreEqual(expected, boxer.Career.SeatIn("ivf", division.Id));
        }

        Assert.AreEqual(1, boxer.Career.SeatIn("ivf", division.Id));
        var title = CampaignLadder.Check(CampaignEvents.Get("ivf-title"), boxer, CharacterRoster.All);
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
    public void WeightChange_MovesTheBoxerAcrossDivisions_AndReChecksEntry()
    {
        var ev = CampaignEvents.Get("ivf-rookie");
        var heavy = NewBoxer(85);
        Assert.IsNull(CampaignLadder.Check(ev, heavy, CharacterRoster.All).Entry, "cruiserweight is above the rookie event");
        var cut = heavy.ChangeWeight(78);
        Assert.AreEqual("lightheavy", CampaignLadder.Check(ev, cut, CharacterRoster.All).Entry!.Division.Id);
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
    public void EventFile_RejectsUnknownDivisions()
    {
        const string Json = """{ "events": [ { "id": "x", "name": "x", "org": "ivf", "tier": "rookie", "divisions": ["nope"] } ] }""";
        Assert.ThrowsExactly<InvalidDataException>(() => CampaignEvents.FromJson(Json));
        Assert.IsTrue(CampaignEvents.All.Count >= 9);
    }
}
