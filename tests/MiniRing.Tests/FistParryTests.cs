using System.Numerics;
using MiniRing.Bots;
namespace MiniRing.Tests;
[TestClass]
public sealed class FistParryTests
{
    [TestMethod]
    public void Parry_SweepsAcrossTheIncomingFist_WithoutMovingTheHead()
    {
        var sense = new FighterSense(new BodyFrame(Vector3.Zero, 0f), 0.5352f, new(0f,0f,0.8451f), new(0f,0f,0.8451f), true,
            OpponentRight: new(new(0.05634f,0.1408f,0.338f), new(0f,0f,-1.007f)));
        var move = DefenseBook.Default.Of(Defense.Parry);
        var start = TechniqueExecution.ParryGlove(move, sense, 1f, 0f);
        var end = TechniqueExecution.ParryGlove(move, sense, 1f, 0.5f);
        Assert.IsLessThan(sense.OpponentRight.Position.X, start.X);
        Assert.IsGreaterThan(sense.OpponentRight.Position.X, end.X);
        Assert.AreEqual(Vector3.Zero, TechniqueExecution.DefenseHead(move, 1f, 0.5f));
    }
}
