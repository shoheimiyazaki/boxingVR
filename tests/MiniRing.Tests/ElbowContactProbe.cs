using System.Globalization;
using MiniRing.Bots;

namespace MiniRing.Tests;

/// <summary>調査用（スイートの外）: 肘打ちの種類と間合いの倍率ごとの当たり・速さ・エネルギーを、ストレート・ジャブと並べて書く。2026-11-20 に <see cref="ElbowContactTests"/> から移した。</summary>
[TestClass]
[TestCategory("Probe")]
public sealed class ElbowContactProbe
{
    [TestMethod]
    public void Sweep_Probe()
    {
        Console.WriteLine(ElbowContactTests.Describe("Cross x1.0", ElbowContactTests.RunPunch(Technique.Cross, 1.0f, 0.7553f)));
        Console.WriteLine(ElbowContactTests.Describe("Jab x0.9", ElbowContactTests.RunPunch(Technique.Jab, 0.9f, 0.5874f)));
        foreach (var kind in Enum.GetValues<ElbowTechnique>())
        {
            foreach (var scale in new[] { ElbowContactTests.CloseIn, 1.0f })
            {
                Console.WriteLine(ElbowContactTests.Describe(kind + " x" + scale.ToString(CultureInfo.InvariantCulture), ElbowContactTests.Run(kind, distanceScale: scale)));
            }
        }
    }
}
