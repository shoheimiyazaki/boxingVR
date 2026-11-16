using MiniRing.Characters;

namespace MiniRing.Tests;

/// <summary>キャラクターのプロフィール（経歴・人柄。実況・解説の資料）: 名簿の全員が持ち、見えている体と身長・体重が合う。</summary>
[TestClass]
public sealed class CharacterProfileTests
{
    [TestMethod]
    public void EveryRosterCharacter_HasAProfile_MatchingTheVisibleBody()
    {
        foreach (var c in CharacterRoster.All)
        {
            var p = c.Profile ?? throw new AssertFailedException($"{c.Id} にプロフィールがありません");
            Assert.AreEqual(CharacterProfile.HeightOf(c.Build), p.HeightCm, c.Id);
            Assert.AreEqual(CharacterProfile.WeightOf(c.Build), p.WeightKg, c.Id);
            Assert.IsGreaterThanOrEqualTo(4, p.Episodes.Count, $"{c.Id}: 中継で使えるエピソードがいくつもある");
            StringAssert.Contains(p.Briefing(c.Name, c.HudName, c.Tagline), c.HudName);
        }
    }

    [TestMethod]
    public void BiggerVisibleBody_IsTallerAndHeavierOnPaper()
    {
        var dmitri = CharacterRoster.Get("dmitri");
        var ramon = CharacterRoster.Get("ramon");
        Assert.IsGreaterThan(ramon.Profile!.HeightCm, dmitri.Profile!.HeightCm);
        Assert.IsGreaterThan(ramon.Profile.WeightKg, dmitri.Profile.WeightKg, "見た目の大きさと紙の上の体重が食い違わない");
    }

    [TestMethod]
    public void Validate_RejectsAWeightThatDoesNotMatchTheBody()
    {
        var build = new BodyBuild(Height: 1.1f, Muscle: 1.2f);
        var good = new CharacterProfile
        {
            Age = 30, HeightCm = CharacterProfile.HeightOf(build), WeightKg = CharacterProfile.WeightOf(build), Birthplace = "a", Hometown = "b", Stance = "オーソドックス",
            NicknameStory = "c", Trainer = "d", FightingStyle = "e", Personality = "f", Family = "g", Quote = "h",
            Career = ["i"], Strengths = ["j"], Weaknesses = ["k"], Hobbies = ["l"], Episodes = ["m"], CommentaryNotes = ["n"],
            FightRecord = new FightRecord { Wins = 10, Losses = 1, Kos = 7 },
        };

        Assert.AreSame(good, good.Validate(build));
        Assert.ThrowsExactly<InvalidDataException>(() => (good with { WeightKg = good.WeightKg + 10 }).Validate(build));
        Assert.ThrowsExactly<InvalidDataException>(() => (good with { FightRecord = new FightRecord { Wins = 3, Kos = 5 } }).Validate(build), "K.O. は勝ちの数以下");
        Assert.ThrowsExactly<InvalidDataException>(() => (good with { Episodes = [] }).Validate(build));
    }
}
