using System.Numerics;
using MiniRing.Bots;
namespace MiniRing.Tests;
[TestClass]
public sealed class FistParryTests
{
    [TestMethod]
    public void Parry_SweepsAcrossTheIncomingFist_WithoutMovingTheHead()
    {
        var sense = new FighterSense(new BodyFrame(Vector3.Zero, 0f), 0.19f, new(0f,0f,0.3f), new(0f,0f,0.3f), true,
            OpponentRight: new(new(0.02f,0.05f,0.12f), new(0f,0f,-0.6f)));
        var move = DefenseBook.Default.Of(Defense.Parry);
        var start = TechniqueExecution.ParryGlove(move, sense, 1f, 0f);
        var end = TechniqueExecution.ParryGlove(move, sense, 1f, 0.5f);
        Assert.IsLessThan(sense.OpponentRight.Position.X, start.X);
        Assert.IsGreaterThan(sense.OpponentRight.Position.X, end.X);
        Assert.AreEqual(Vector3.Zero, TechniqueExecution.DefenseHead(move, 1f, 0.5f));
    }
}
