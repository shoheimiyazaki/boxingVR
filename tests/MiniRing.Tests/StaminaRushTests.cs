using System.Globalization;
using System.Numerics;

namespace MiniRing.Tests;

/// <summary>
/// ユーザーの仕様（2026-11-22、PhysicsVersion 87）: 体重を乗せたラッシュ（弱・強のボタンを交互に連打）が 5 秒続き、その後の 5 秒で動きが鈍り、
/// 酸素の供給と釣り合ったノロノロのパンチになる（物理 86 までの目標は 17 秒ずつだった）。
/// 意図操作で、ガードを固めた相手へ 20 秒連打し、5 秒ごとの窓で、0.83 秒ごとの拳の最大の速さを平均して比べる。
/// 測定（基準体格、種 3、TrunkWorkProbe / StaminaRushTests）: 0〜5 秒 約 5.6 m/s、5〜10 秒 約 3.1 m/s（0.56 倍）、10〜15 秒・15〜20 秒 約 2.1 m/s（満タンの 4 割）。
/// 相手が倒れて連打が止まった間は数えない（脳しんとうは切る。崩れて倒れることはある）。
/// </summary>
[TestClass]
[TestCategory("slow")]
public sealed class StaminaRushTests
{
    private const float Dt = 1f / 60f;

    private static double[] RushWindows(float seconds, Action<BoxingGame>? setup = null)
    {
        var corners = BoxingCorners.Default with { Player = _ => new IntentControl(), Cpu = _ => new IntentControl() };
        // 意識（ConcussionSpin を無限大に）に加え、腹の打撲による体の張りの低下（CoreToneFloor）も切る: 的の相手は胴を打たれ続けて倒れる。
        // 倒れて待つ間に打つ側のスタミナが戻ると全力が延びてしまう。測るのは打つ側の拳とスタミナ。
        using var game = new BoxingGame(FighterTuning.Default with { ConcussionSpin = float.PositiveInfinity, CoreToneFloor = 1f }, seed: 3, corners: corners);
        setup?.Invoke(game);
        var control = (IntentControl)game.PlayerControl!;
        ((IntentControl)game.CpuControl!).Input = new FighterIntent(Vector2.Zero, Evade: true);
        var rushing = 0f;
        var clock = 0f;
        var tap = 0f;
        var second = 0f;
        var peak = 0f;
        var windows = new (double Sum, int Count)[(int)(seconds / 5f)];
        while (rushing < seconds - 1e-3f && clock < 3f * seconds)
        {
            var standing = game.Player.State == FighterState.Standing && game.Cpu.State == FighterState.Standing;
            var phase = tap / 0.2014f;
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

            if (second >= 0.8333f)
            {
                var window = Math.Min(windows.Length - 1, (int)((rushing - 1e-3f) / 5f));
                windows[window] = (windows[window].Sum + peak, windows[window].Count + 1);
                second = 0f;
                peak = 0f;
            }
        }

        Assert.IsTrue(windows.All(w => w.Count >= 4), "どの 5 秒も連打できている");
        return [.. windows.Select(w => w.Count > 0 ? w.Sum / w.Count : 0.0)];
    }

    [TestMethod]
    public void AlternatingRush_FullForFiveSeconds_ThenFadesOverTheNextFive_AndSettlesAtTheSupply()
    {
        var mean = RushWindows(20f);
        Console.WriteLine(string.Create(CultureInfo.InvariantCulture,
            $"rush fist peak per 0.83 s: 0-5 s {mean[0]:F2}, 5-10 s {mean[1]:F2}, 10-15 s {mean[2]:F2}, 15-20 s {mean[3]:F2} m/s"));
        Assert.IsGreaterThan(4.5, mean[0], "始めの 5 秒は全力（体重を乗せた満タンの拳）");
        Assert.IsTrue(mean[1] < 0.8 * mean[0] && mean[1] > 0.4 * mean[0], "次の 5 秒で落ちていく");
        Assert.IsTrue(mean[3] > 0.25 * mean[0] && mean[3] < 0.6 * mean[0], "その後は酸素の供給と釣り合ったノロノロのパンチ（満タンの 3〜6 割）");
        Assert.IsTrue(Math.Abs(mean[2] - mean[3]) < 0.3 * mean[3], "10 秒を過ぎたら速さが落ち続けない（供給と釣り合って止まる）");
    }
}
