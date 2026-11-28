using MiniRing.Bots;
using MiniRing.Highlights;

namespace MiniRing.Tests;

/// <summary>
/// 標準の体（分割胴、<see cref="FighterTuning.Default"/>）が、打たれてもいないのに（意識が保たれたまま）自分で倒れる回数の上限。
/// 転倒は「ダウン」「見込みの動き」として面白さの点に数えられるので、体のバランスが崩れて倒れるほど点が稼げてしまう（測り方の穴）。
/// そこで体の側で、倒れる回数が増えないことを物理の量として見張る。
/// 測った値（標準ボット 3 組 × 種 1〜4、67 秒）: 標準の体 0.7 回/試合、ダイナミック版の体（足の受け止め許容が従来のまま）4.2 回/試合、試合用の体 1.5 回/試合。
/// </summary>
[TestClass]
[TestCategory("L3b")]
[TestCategory("slow")]
public sealed class SelfFallTests
{
    private const float MaxSelfFallsPerBout = 3f; // 今 1.5。足の受け止め許容を戻した体（4.2）は通らない

    [TestMethod]
    public void MatchBodyBouts_DoNotHaveTheFightersFallOverUnhit()
    {
        var falls = 0;
        var bouts = 0;
        foreach (var (red, blue) in new[] { ("champion", "swarmer"), ("slugger", "outboxer") })
        {
            for (var seed = 1; seed <= 2; seed++)
            {
                var previous = new[] { FighterState.Standing, FighterState.Standing };
                ExcitementRun.Bout(Contender.Named(red), Contender.Named(blue), seed, 67.14f, afterStep: (game, _) =>
                {
                    var now = new[] { game.Player.State, game.Cpu.State };
                    var fighters = new[] { game.Player, game.Cpu };
                    for (var i = 0; i < 2; i++)
                    {
                        if (previous[i] == FighterState.Standing && now[i] != FighterState.Standing && fighters[i].Consciousness > 0.9f)
                        {
                            falls++;
                        }
                    }

                    previous = now;
                });
                bouts++;
            }
        }

        var perBout = (float)falls / bouts;
        Console.WriteLine($"自分で倒れた回数: {falls} 回 / {bouts} 試合 = {perBout:0.0} 回/試合");
        Assert.IsLessThanOrEqualTo(MaxSelfFallsPerBout, perBout, "打たれずに倒れる回数が多すぎる（体のバランス・足の受け止め）");
    }
}
