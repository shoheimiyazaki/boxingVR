using System.Numerics;

namespace MiniRing.Tests;

/// <summary>調査用（スイートの外）: 顎と頭の真ん中への重い一撃で、脳のセルがどれだけ失われ、意識・脳しんとうがどうなるかを書く。2026-11-20 に <see cref="DamageModelTests"/> から移した。</summary>
[TestClass]
[TestCategory("Probe")]
public sealed class DamageModelProbe
{
    [TestMethod]
    public void BrainLoss_Probe()
    {
        foreach (var (name, where) in new (string, Func<Fighter, Vector3>)[] { ("chin", DamageModelTests.Chin), ("centre", DamageModelTests.Centre) })
        {
            var (game, cpu) = DamageModelTests.NewGame();
            using (game)
            {
                int Brain() => cpu.Voxels!.Parts.Sum(p => p.Body.Grid.Remaining(MiniRing.Voxels.BoxerAnatomy.Brain));
                var initial = cpu.Voxels!.Parts.Sum(p => p.Body.Grid.Initial(MiniRing.Voxels.BoxerAnatomy.Brain));
                DamageModelTests.Hit(game, cpu, where(cpu), mass: 4.5f, speed: 2.2f);
                Console.WriteLine($"{name}: brain {initial} → {Brain()} (lost {1f - ((float)Brain() / initial):P1}), function {cpu.Voxels.BrainFunction(FighterTuning.Default.BrainHalvingLoss):0.00}, concussion {cpu.Concussion:0.00}, consciousness {cpu.Consciousness:0.00}");
            }
        }
    }
}
