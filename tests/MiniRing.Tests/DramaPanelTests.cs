using MiniRing.Arena;

namespace MiniRing.Tests;

[TestClass]
public sealed class DramaPanelTests
{
    [TestMethod]
    public void Compare_PairsByCardSeedAndCorners_AndReportsTheLowerTail()
    {
        var before = new DramaPanel.Samples(88, 64, 67.14f,
        [
            new("a:b", 1, "a", "b", 30f),
            new("a:b", 2, "a", "b", 90f),
            new("c:d", 1, "d", "c", 50f),
        ]);
        var after = before with
        {
            PhysicsVersion = 89,
            Bouts = [before.Bouts[2] with { Drama = 50.00001f }, before.Bouts[1] with { Drama = 120f }, before.Bouts[0] with { Drama = 10f }],
        };

        var (total, cards, interval) = DramaPanel.Compare(before, after);
        Assert.AreEqual(3, total.Count);
        Assert.AreEqual(50d, total.Before.Median);
        Assert.AreEqual(18d, total.After.P10, 1e-8);
        Assert.AreEqual(0d, total.PairedChange.Median, 0.0001d);
        Assert.AreEqual(1, total.Lower);
        Assert.AreEqual(1, total.Equal);
        Assert.AreEqual(1, total.Higher);
        Assert.HasCount(2, cards);
        Assert.IsLessThan(total.PairedChange.Mean, interval[0]);
        Assert.IsGreaterThan(total.PairedChange.Mean, interval[1]);
    }

    [TestMethod]
    public void Compare_RejectsDuplicateAndUnmatchedBouts()
    {
        var bout = new DramaPanel.Bout("a:b", 1, "a", "b", 50f);
        var before = new DramaPanel.Samples(88, 64, 67.14f, [bout]);
        Assert.ThrowsExactly<ArgumentException>(() => DramaPanel.Compare(before, before with { Bouts = [bout, bout] }));
        Assert.ThrowsExactly<ArgumentException>(() => DramaPanel.Compare(before, before with { Bouts = [bout with { Red = "b", Blue = "a" }] }));
        Assert.ThrowsExactly<ArgumentException>(() => DramaPanel.Compare(before, before with { Seconds = 30f }));
    }
}
