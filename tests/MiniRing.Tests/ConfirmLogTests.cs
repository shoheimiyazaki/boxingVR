using System.Text.Json;
using MiniRing.Arena;

namespace MiniRing.Tests;

/// <summary>採否の比べ直しの台帳（docs/arena/confirm.jsonl）は、どの道具からでも同じ形の 1 行の JSON になる。</summary>
[TestClass]
public sealed class ConfirmLogTests
{
    [TestMethod]
    public void IdenticalDifferences_GiveAnInfiniteTValue_AndStillOneLine()
    {
        var path = Path.Combine(Path.GetTempPath(), $"confirm-{Guid.NewGuid():N}.jsonl");
        try
        {
            int[] seeds = [1, 2, 3];
            var result = PairedEvaluation.Confirm(s => s.Select(_ => 1.5d).ToArray(), s => s.Select(_ => 1d).ToArray(), seeds);
            Assert.IsTrue(double.IsPositiveInfinity(result.TValue), "every seed gives the same difference, so the standard error is 0");
            ConfirmLog.Append("test", "constant difference", "A", "B", seeds, result, path: path);
            using var doc = JsonDocument.Parse(File.ReadAllLines(path).Single());
            Assert.AreEqual("Infinity", doc.RootElement.GetProperty("result").GetProperty("TValue").GetString());
        }
        finally
        {
            File.Delete(path);
        }
    }

    [TestMethod]
    public void OneConfirmation_IsOneJsonLine_WithTheVerdictAndTheInterval()
    {
        var path = Path.Combine(Path.GetTempPath(), $"confirm-{Guid.NewGuid():N}.jsonl");
        try
        {
            int[] seeds = [1, 2, 3, 4];
            var result = PairedEvaluation.Confirm(s => s.Select(x => 1d + (x * 0.01)).ToArray(), s => s.Select(_ => 0d).ToArray(), seeds);
            ConfirmLog.Append("test", "does A beat B", "A", "B", seeds, result, path: path);
            ConfirmLog.Append("test", "again", "A", "B", seeds, result, path: path);

            var lines = File.ReadAllLines(path);
            Assert.AreEqual(2, lines.Length);
            using var doc = JsonDocument.Parse(lines[0]);
            var root = doc.RootElement;
            Assert.AreEqual("test", root.GetProperty("tool").GetString());
            Assert.AreEqual("1,2,3,4", root.GetProperty("seeds").GetString());
            var r = root.GetProperty("result");
            Assert.AreEqual(4, r.GetProperty("N").GetInt32());
            Assert.IsTrue(r.GetProperty("CiLow").GetDouble() > 0d);
            Assert.AreEqual("A が上", r.GetProperty("Verdict").GetString());
            StringAssert.StartsWith(root.GetProperty("versions").GetString(), "physics ");
        }
        finally
        {
            File.Delete(path);
        }
    }
}
