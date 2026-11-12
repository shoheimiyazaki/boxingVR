using MiniRing.Audio;
using MiniRing.Bots;
using MiniRing.Voxels;
using MiniRing.Weapons;

namespace MiniRing.Tests;

/// <summary>
/// 何も壊さない当たりの音（<see cref="BoxingGame.RecentContacts"/>）: ぶつかった面の材質の組から音の種類を選び、試合の中で鳴る。
/// </summary>
[TestClass]
public sealed class ContactEventTests
{
    [TestMethod]
    public void MaterialPairs_MapToTheSoundOfThatPair_InEitherOrder()
    {
        Assert.AreEqual(ContactPair.MetalMetal, ContactPairs.Of(BoxerAnatomy.Iron, BoxerAnatomy.Iron));
        Assert.AreEqual(ContactPair.MetalWood, ContactPairs.Of(BoxerAnatomy.Wood, BoxerAnatomy.Iron));
        Assert.AreEqual(ContactPair.WoodWood, ContactPairs.Of(BoxerAnatomy.Wood, BoxerAnatomy.Wood));
        Assert.AreEqual(ContactPair.GloveGlove, ContactPairs.Of(BoxerAnatomy.RedGlove, BoxerAnatomy.BlueGlove));
        Assert.AreEqual(ContactPair.GloveFlesh, ContactPairs.Of(BoxerAnatomy.Skin, BoxerAnatomy.RedGlove));
        Assert.AreEqual(ContactPair.MetalFlesh, ContactPairs.Of(BoxerAnatomy.Iron, BoxerAnatomy.Bone));
        Assert.AreEqual(ContactPair.WoodFlesh, ContactPairs.Of(BoxerAnatomy.Muscle, BoxerAnatomy.Wood));
        Assert.AreEqual(ContactPair.FleshFlesh, ContactPairs.Of(BoxerAnatomy.Skin, BoxerAnatomy.Skin));
        Assert.IsNull(ContactPairs.Of(0, BoxerAnatomy.Skin));
    }

    [TestMethod]
    public void GlovedBots_MakeGloveSounds_WhenTheyBumpWithoutBreaking()
    {
        using var game = new BoxingGame(seed: 1, corners: new BoxingCorners("slugger", s => BotCatalog.Create("slugger", s), s => BotCatalog.Create("champion", s), PlayerWear: HandWear.Glove, CpuWear: HandWear.Glove));
        var pairs = new Dictionary<ContactPair, int>();
        for (var t = 0f; t < 15f; t += 1f / 60f)
        {
            game.Step(default, 1f / 60f);
            foreach (var c in game.RecentContacts)
            {
                Assert.IsTrue(c.Strength is > 0f and <= 1f);
                pairs[c.Pair] = pairs.GetValueOrDefault(c.Pair) + 1;
            }
        }

        Console.WriteLine(string.Join(", ", pairs.Select(kv => $"{kv.Key} {kv.Value}")));
        Assert.IsGreaterThan(0, pairs.GetValueOrDefault(ContactPair.GloveGlove), "gloves meeting gloves (blocks, parries)");
        Assert.IsTrue(pairs.Keys.All(p => p is ContactPair.GloveGlove or ContactPair.GloveFlesh), "only leather and flesh meet in a gloved bout");
    }
}
