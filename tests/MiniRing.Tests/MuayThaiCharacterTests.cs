using MiniRing.Bots;
using MiniRing.Characters;

namespace MiniRing.Tests;

/// <summary>ムエタイのキャラ（narong）と教科書（muaythai）が読めて、肘・蹴り・拳をすべて持つこと。</summary>
[TestClass]
public sealed class MuayThaiCharacterTests
{
    [TestMethod]
    public void Narong_LoadsFromTheRoster_WithTheMuayThaiTextbook()
    {
        var narong = CharacterRoster.Get("narong");
        Assert.AreEqual("thailand", narong.Country.Id);
        Assert.AreEqual("タイ", narong.Country.Name);
        Assert.AreEqual("muaythai", narong.StyleName);
        Assert.AreSame(BotCatalog.MuayThai, BotCatalog.Style("muaythai"));
    }

    [TestMethod]
    public void TheMuayThaiTextbook_CombinesBoxingKicksAndElbows()
    {
        var style = BotCatalog.MuayThai;
        Assert.IsGreaterThan(0f, style.ElbowRate, "elbows");
        Assert.IsGreaterThan(0f, style.KickRate, "kicks");
        Assert.IsNotEmpty(style.ComboWeights, "boxing combinations");
        foreach (var technique in Enum.GetValues<ElbowTechnique>())
        {
            Assert.IsTrue(style.ElbowWeights.ContainsKey(technique), $"{technique} has a weight");
        }

        Assert.IsTrue(style.KickWeights.ContainsKey(KickTechnique.Roundhouse), "the shin roundhouse");
        Assert.IsTrue(style.KickWeights.ContainsKey(KickTechnique.Knee), "the knee");
    }

    [TestMethod]
    public void AStyleWithoutElbows_NeverThrowsOne()
    {
        // ElbowRate 0 の既存の教科書は、肘を出さない（判断も乱数の消費も今までと同じ）。
        foreach (var style in BotCatalog.Styles.Where(s => s.Name != "muaythai"))
        {
            Assert.AreEqual(0f, style.ElbowRate, style.Name);
        }
    }
}
