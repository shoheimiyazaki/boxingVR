using System.Globalization;
using System.Numerics;

namespace MiniRing.Tests;

/// <summary>
/// ユーザーの目標（2026-11-17）: 弱・強のボタンを交互に連打する全力のラッシュが 10 秒、力が落ちながらさらに 10 秒まともに打てる。
/// 意図操作で、ガードを固めた相手へ 30 秒連打し、連打を始めてからの 10 秒ごとに、1 秒ごとの拳の最大の速さを平均して比べる。
/// 測定（基準体格、種 3）: 0〜10 秒 3.45 m/s、10〜20 秒 1.86 m/s（0.54 倍）、20〜30 秒 1.35 m/s（StaminaFeelProbe）。
/// 49（足の滑りの直し）: 2.49・2.13（0.86 倍）・1.30 m/s。止まっている間に足が流れなくなり、打つたびに体が滑って前へ出る分が拳の速さに乗らなくなった
/// （同じ種で 49 の前の立ち方へ戻すと 2.90・1.65・1.28）。着いた足が体を支えるぶん、打つ拳は遅い。
/// 相手が倒れて連打が止まった間は数えない（脳しんとうは切る。崩れて倒れることはある）。
/// </summary>
[TestClass]
[TestCategory("slow")]
public sealed class StaminaRushTests
{
    private const float Dt = 1f / 60f;

    [TestMethod]
    public void AlternatingRush_FullForTenSeconds_ThenFadesOverTheNextTen()
    {
        var corners = BoxingCorners.Default with { Player = _ => new IntentControl(), Cpu = _ => new IntentControl() };
        // 意識（ConcussionSpin を無限大に）に加え、腹の打撲による体の張りの低下（CoreToneFloor）も切る: 的の相手は胴を打たれ続け、7 秒ほどで膝から力が抜けて倒れる
        // （2026-11-18、床から支える体で測り直し）。倒れて待つ間に打つ側のスタミナが戻ると 10 秒の全力が延びてしまう。測るのは打つ側の拳とスタミナ。
        using var game = new BoxingGame(FighterTuning.Default with { ConcussionSpin = float.PositiveInfinity, CoreToneFloor = 1f }, seed: 3, corners: corners);
        var control = (IntentControl)game.PlayerControl!;
        ((IntentControl)game.CpuControl).Input = new FighterIntent(Vector2.Zero, Evade: true);
        var rushing = 0f;
        var clock = 0f;
        var tap = 0f;
        var second = 0f;
        var peak = 0f;
        var windows = new (double Sum, int Count)[3];
        while (rushing < 30f && clock < 90f)
        {
            var standing = game.Player.State == FighterState.Standing && game.Cpu.State == FighterState.Standing;
            var phase = tap / 0.12f;
            var index = (int)MathF.Floor(phase);
            var down = standing && phase - index < 0.67f;
            control.Input = new FighterIntent(Vector2.Zero, Light: down && index % 2 == 0, Heavy: down && index % 2 == 1);
            game.Step(new BoxingInput(default), Dt);
            clock += Dt;
            if (!standing)
            {
                continue;
            }

            tap += Dt;
            rushing += Dt;
            second += Dt;
            foreach (var hand in new[] { Hand.Left, Hand.Right })
            {
                peak = MathF.Max(peak, (game.Player.GloveVelocity(hand) - game.Player.ChestVelocity).Length());
            }

            if (second >= 1f)
            {
                var window = Math.Min(2, (int)((rushing - 1e-3f) / 10f));
                windows[window] = (windows[window].Sum + peak, windows[window].Count + 1);
                second = 0f;
                peak = 0f;
            }
        }

        var mean = windows.Select(w => w.Count > 0 ? w.Sum / w.Count : 0).ToArray();
        Console.WriteLine(string.Create(CultureInfo.InvariantCulture,
            $"rush fist peak per second: 0-10 s {mean[0]:F2}, 10-20 s {mean[1]:F2}, 20-30 s {mean[2]:F2} m/s; stamina {game.Player.Stamina:P0}"));
        Assert.IsTrue(windows.All(w => w.Count >= 8), "どの 10 秒も連打できている");
        Assert.IsGreaterThan(2.2, mean[0], "始めの 10 秒は全力（満タンの拳。49: 足が着いたままなので 2.5 m/s 前後。滑っていた頃は 3 m/s 前後）");
        Assert.IsTrue(mean[1] < 0.9 * mean[0] && mean[1] > 0.35 * mean[0], "次の 10 秒は落ちながらもまともに打てる（49: 0.86 倍）");
        Assert.IsLessThan(mean[1], mean[2], "その後はさらに弱い");
    }
}
