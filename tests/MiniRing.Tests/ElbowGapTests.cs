using System.Globalization;
using System.Numerics;
using MiniRing.Arena;

namespace MiniRing.Tests;

/// <summary>パンチ中の肘の関節（上腕の肘の点と前腕の肘の点）のずれを測る。 回帰: 肘の隙間はセルの半分（7 mm）未満。</summary>
[TestClass]
[TestCategory("slow")]
public sealed class ElbowGapTests
{
    [TestMethod]
    public void Elbow_StaysClosed_WhileThePunchGoesOut()
    {
        using var bout = new PunchLab.Bout(new(PunchLabPath.Standard, "still", "reach", 1.05f, false), 1, Bots.BotCatalog.Boxer.Telegraph, FighterTuning.Default);
        var f = bout.Game.Player;
        var world = bout.Game.World;
        var side = Hand.Right.LocalSide();
        var upper = side > 0f ? f.Parts.RightUpperArm : f.Parts.LeftUpperArm;
        var fore = side > 0f ? f.Parts.RightForearm : f.Parts.LeftForearm;
        var u0 = world.GetState(upper);
        var f0 = world.GetState(fore);
        var half = f.HalfExtents;
        var overlap = FighterTuning.Default.JointOverlap;
        var elbow = f0.Position - (Vector3.Transform(Vector3.UnitX, u0.Orientation) * side * (half.Forearm.X - overlap));
        var aU = Vector3.Transform(elbow - u0.Position, Quaternion.Conjugate(u0.Orientation));
        var aF = Vector3.Transform(elbow - f0.Position, Quaternion.Conjugate(f0.Orientation));
        var max = 0f;
        var maxT = 0f;
        var n = 0;
        while (bout.PunchTime is null && n++ < 5000) { bout.Step(); }
        while (bout.PunchTime is { } t && t < 0.6f)
        {
            var u = world.GetState(upper);
            var fo = world.GetState(fore);
            var gap = Vector3.Distance(u.Position + Vector3.Transform(aU, u.Orientation), fo.Position + Vector3.Transform(aF, fo.Orientation));
            if (gap > max) { max = gap; maxT = t; }
            bout.Step();
        }
        Assert.IsLessThan(0.007f, max, "肘の隙間 [m]");
        Console.WriteLine(string.Create(CultureInfo.InvariantCulture, $"ELBOWGAP max={max * 1000f:F1} mm at t={maxT:F3} (cell about 14 mm) upperHalf={half.UpperArm} foreHalf={half.Forearm} overlap={overlap}"));
    }
}
