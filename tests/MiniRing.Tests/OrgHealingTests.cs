using Aigis.Foundation.Voxels;
using MiniRing.Arena;

namespace MiniRing.Tests;

/// <summary>
/// ラウンドの間の再生の速さは団体のファイルの値（2026-11-24、ユーザーの指定「WVC は顔の削れがインターバル中に完治している。ほぼ治らないぐらいで」
/// 「VFC はもっと早く」）。同じ顔の削れを、同じ長さの休み 1 回にかけて、試合の道（<see cref="BoxingGame"/> の休み）で治った量を比べる。
/// 比べる相手は同じビルドの標準の決まり（<see cref="MatchRules.Tv"/>、IVF と同じ速さ）で、固定の値は持たない。
/// </summary>
[TestClass]
[TestCategory("slow")]
public sealed class OrgHealingTests
{
    private const float Dt = 1f / 240f;

    /// <summary>撮影の既定の休み（<see cref="MatchRules.Tv"/>）。</summary>
    private static float BreakSeconds => MatchRules.Tv.BreakSeconds;

    /// <summary>
    /// 顔の層の外側 <paramref name="depth"/> 層を削る（顔じゅうを殴られて表面が削れた状態の代わり）。削ったセルの数を返す。
    /// 1 層ずつ「今の空きに接するセル」を集めてから壊す（その層の中で連鎖させない）。
    /// </summary>
    private static int CarveFace(Fighter f, int depth)
    {
        var grid = f.Voxels!.Face!.Body.Grid;
        var before = grid.FilledCount;
        var (nx, ny, nz) = grid.Dimensions;
        var removed = new List<RemovedVoxel>();
        for (var layer = 0; layer < depth; layer++)
        {
            var shell = new List<int>();
            for (var i = 0; i < grid.CellCount; i++)
            {
                if (grid.MaterialAt(i) == 0)
                {
                    continue;
                }

                var (x, y, z) = grid.Coordinates(i);
                if (Open(x - 1, y, z) || Open(x + 1, y, z) || Open(x, y - 1, z) || Open(x, y + 1, z) || Open(x, y, z - 1) || Open(x, y, z + 1))
                {
                    shell.Add(i);
                }
            }

            foreach (var i in shell)
            {
                grid.Break(i, removed);
            }
        }

        return before - grid.FilledCount;

        bool Open(int x, int y, int z) => x < 0 || y < 0 || z < 0 || x >= nx || y >= ny || z >= nz || grid.At(x, y, z) == 0;
    }

    /// <summary>
    /// 決まり <paramref name="rules"/> の試合で、1 ラウンド目の途中に CPU の顔を削り、休みを 1 回終えた所（2 ラウンド目の始め）で、削った分の何割が戻ったか。
    /// </summary>
    private static (int Carved, float Back) HealedOverOneBreak(MatchRules rules)
    {
        var timed = rules with { RoundSeconds = 1.678f, Rounds = 2, BreakSeconds = BreakSeconds, IntroSeconds = 0f, EndSeconds = 0f };
        using var game = new BoxingGame(seed: 1, corners: new BoxingCorners("still", _ => new StandGuard(), _ => new StandGuard()), rules: timed);
        for (var t = 0f; t < 0.8392f; t += Dt)
        {
            game.Step(default, Dt);
        }

        var grid = game.Cpu.Voxels!.Face!.Body.Grid;
        var carved = CarveFace(game.Cpu, depth: 3);
        var wounded = grid.FilledCount;
        var sawBreak = false;
        while (game.MatchRound < 2 && !game.MatchOver)
        {
            game.Step(default, Dt);
            sawBreak |= game.InBreak;
        }

        Assert.IsTrue(sawBreak, "the match went through one break");
        return (carved, (float)(grid.FilledCount - wounded) / carved);
    }

    [TestMethod]
    public void TheWvcBarelyHeals_TheStandardHealsSome_AndTheVfcHealsMost_InOneBreak()
    {
        var wvc = HealedOverOneBreak(TvShowRules.Rules(2, BreakSeconds, Organizations.Get("wvc")));
        var standard = HealedOverOneBreak(TvShowRules.Rules(2, BreakSeconds));
        var vfc = HealedOverOneBreak(TvShowRules.Rules(2, BreakSeconds, Organizations.Get("vfc")));
        Console.WriteLine($"face carved {standard.Carved} cells; back after one {BreakSeconds:0.##} s break: WVC {wvc.Back:P1}, standard (IVF) {standard.Back:P1}, VFC {vfc.Back:P1}");

        Assert.IsGreaterThan(MatchRules.Tv.RegenVoxelsPerSecond * BreakSeconds, standard.Carved, "the wound is bigger than one standard break heals, so the three can be told apart");
        Assert.IsLessThan(standard.Back, wvc.Back, "the WVC heals less than the standard rules");
        Assert.IsLessThan(vfc.Back, standard.Back, "the VFC heals more than the standard rules");
        Assert.IsLessThan(0.05f, wvc.Back, "in the WVC a carved face is still carved in the next round");
    }
}
