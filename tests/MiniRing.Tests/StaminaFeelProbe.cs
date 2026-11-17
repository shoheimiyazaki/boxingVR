using System.Globalization;
using System.Numerics;

namespace MiniRing.Tests;

/// <summary>
/// スタミナの手触りの調べ（2026-11-17、ユーザーの目標: 弱・強を交互に連打する全力のラッシュが 10 秒、力が落ちながらさらに 10 秒まともに打てる。
/// 1 ラウンド目は全力で続けられ、3 ラウンド目は今ぐらい。打ち合っていないときは減らない）。
/// 人の操作（意図操作）で、立っている相手へ連打する・足だけ動かす、の 2 つを 1 秒ごとに記録する。
/// </summary>
[TestClass]
[TestCategory("Probe")]
public sealed class StaminaFeelProbe
{
    private const float Dt = 1f / 60f;

    /// <summary>連打の前に前へ詰める秒数（0 なら離れたまま空を打つ）。</summary>
    private static float Approach { get; set; }

    /// <summary>環境変数 STAMINA_TUNE="StaminaCapacity=270,StaminaCriticalShare=0.4" で調整値を上書きする。</summary>
    internal static FighterTuning Tuning()
    {
        var tuning = FighterTuning.Default with { };
        foreach (var pair in (Environment.GetEnvironmentVariable("STAMINA_TUNE") ?? "").Split(',', StringSplitOptions.RemoveEmptyEntries))
        {
            var kv = pair.Split('=');
            typeof(FighterTuning).GetProperty(kv[0].Trim())!.SetValue(tuning, float.Parse(kv[1], CultureInfo.InvariantCulture));
        }

        return tuning;
    }

    private static BoxingGame NewGame(string? red = null, string? blue = null)
    {
        var corners = BoxingCorners.Default with
        {
            Player = _ => new IntentControl(),
            Cpu = _ => new IntentControl(),
        };
        if (red is not null || blue is not null)
        {
            corners = corners with
            {
                PlayerBody = red is null ? null : Characters.CharacterRoster.Get(red).Build,
                CpuBody = blue is null ? null : Characters.CharacterRoster.Get(blue).Build,
            };
        }

        // 脳しんとうを切る: 連打の途中で相手が倒れて止まらないように（調べたいのは打つ側のスタミナ）。
        return new BoxingGame(Tuning() with { ConcussionGain = 0f }, seed: 3, corners: corners);
    }

    /// <summary>弱・強を交互に連打（押す 0.08 秒・離す 0.04 秒）。始めの 1.5 秒で前へ詰める。</summary>
    private static FighterIntent Rush(float t)
    {
        if (t < Approach)
        {
            return new FighterIntent(new Vector2(0f, 1f));
        }

        var phase = (t - Approach) / 0.12f;
        var index = (int)MathF.Floor(phase);
        var down = phase - index < 0.67f;
        return new FighterIntent(Vector2.Zero, Light: down && index % 2 == 0, Heavy: down && index % 2 == 1);
    }

    /// <summary>打たずに足だけ: 相手の周りを回り、時々前後に出入りする。</summary>
    private static FighterIntent Footwork(float t)
    {
        var side = MathF.Sign(MathF.Sin(t * 0.7f));
        var inOut = MathF.Sin(t * 1.9f) * 0.6f;
        return new FighterIntent(new Vector2(side, inOut));
    }

    private static void Run(string name, Func<float, FighterIntent> plan, float seconds, string? red = null)
    {
        using var game = NewGame(red);
        var control = (IntentControl)game.PlayerControl!;
        ((IntentControl)game.CpuControl!).Input = new FighterIntent(Vector2.Zero, Evade: true);
        var peak = 0f;
        var started = 0;
        Console.WriteLine($"== {name} ({red ?? "base"}) 容量 {game.Player.Build.MassRatio * Tuning().StaminaCapacity:0} J");
        Console.WriteLine("  t   stamina  P[W]  supply[W]  拳の最大[m/s]  打った数");
        for (var step = 1; step <= (int)(seconds / Dt); step++)
        {
            var t = step * Dt;
            control.Input = plan(t);
            game.Step(new BoxingInput(default), Dt);
            foreach (var hand in new[] { Hand.Left, Hand.Right })
            {
                peak = MathF.Max(peak, (game.Player.GloveVelocity(hand) - game.Player.ChestVelocity).Length());
            }

            if (step % 60 == 0)
            {
                Console.WriteLine(string.Create(CultureInfo.InvariantCulture,
                    $"{t,4:0}  {game.Player.Stamina,6:P0}  {game.Player.RecentMusclePower,5:0.0}  {game.Player.RecoveryPower,6:0.0}  {peak,6:0.00}  {control.AttacksStarted - started,3}  E {game.Player.Endurance:0.00} {game.Cpu.State} {game.Player.State} w{game.PlayerWins}{game.CpuWins} {Vector3.Distance(game.Player.ChestPosition, game.Cpu.ChestPosition):0.00}"));
                peak = 0f;
                started = control.AttacksStarted;
            }
        }
    }

    [TestMethod]
    public void RushThenRest()
    {
        Run("rush 40 s", t => t < 40f ? Rush(t) : default, 45f);
        Run("rush 25 s", t => t < 25f ? Rush(t) : default, 35f, "marcus");
    }

    [TestMethod]
    public void FootworkOnly() => Run("footwork 30 s", Footwork, 30f);
}
