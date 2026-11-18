using System.Globalization;
using System.Numerics;
using MiniRing.Bots;

namespace MiniRing.Arena;

/// <summary>
/// 背負い投げを、論文の数値のお手本（data/reference/seoi_nage_papers.json）と並べて比べる（docs/GRAPPLING.md §14）。
/// 投げのたびに、回り込みの終わり（取りが背を向けきった時刻）と受けが床に着いた時刻で、受けの重心の変位（技を始めたときの受けの身長に対する割合、
/// 前 = 取りが投げる向き）と速さ、取りの重心の速さを測り、人の大きさへ直す（長さは身長の比、速さは身長の比の平方根: Froude 相似）。
/// </summary>
internal static class SeoiReference
{
    private const float Dt = 1f / 60f;

    /// <summary>1 回の投げの測定（人の大きさへ直した値）。</summary>
    internal readonly record struct Sample(
        float TurnForward, float TurnUp, float LandForward, float LandUp, float LandSpeed, float LandDown,
        float ToriForwardAtThrow, float ToriDownAtThrow, float TurnSeconds, float ThrowSeconds);

    public static void Run(GrappleTechnique technique, string victim, int seed, int bouts, float seconds, Action<string> log)
    {
        var (samples, falls, attempts) = MeasureAll(technique, victim, seed, bouts, seconds);

        log($"### 背負い投げとお手本（受け: {victim}、種 {seed}〜、{bouts} 本 × {seconds:0} 秒、受けが床に着いた投げ {samples.Count} 回の平均。人の大きさへ直した値）");
        log("");
        log("| 量 | お手本（論文） | 人形 |");
        log("|---|---|---|");
        string M(Func<Sample, float> f, string format) => samples.Count == 0 ? "-" : samples.Average(f).ToString(format, CultureInfo.InvariantCulture);
        log($"| 回り込みの終わり: 受けの重心の前への変位 [% 身長] | 9 | {M(s => s.TurnForward * 100f, "0")} |");
        log($"| 回り込みの終わり: 受けの重心の上への変位 [% 身長] | 2 | {M(s => s.TurnUp * 100f, "0")} |");
        log($"| 床に着いた時: 受けの重心の前への変位 [% 身長] | 37 | {M(s => s.LandForward * 100f, "0")} |");
        log($"| 床に着いた時: 受けの重心の上への変位 [% 身長] | −31 | {M(s => s.LandUp * 100f, "0")} |");
        log($"| 床に着いた時: 受けの重心の速さ [m/s] | 2.53 | {M(s => s.LandSpeed, "0.00")} |");
        log($"| 床に着いた時: 受けの重心の下への速さ [m/s] | 2.38 | {M(s => s.LandDown, "0.00")} |");
        log($"| 投げの始め: 取りの重心の前への速さ [m/s] | 1.21〜1.30（学生 0.89） | {M(s => s.ToriForwardAtThrow, "0.00")} |");
        log($"| 投げの始め: 取りの重心の下への速さ [m/s] | 1.11〜1.44（学生 0.88） | {M(s => s.ToriDownAtThrow, "0.00")} |");
        log($"| 回り込みの時間 / 投げの時間 [s] | （論文に値なし） | {M(s => s.TurnSeconds, "0.00")} / {M(s => s.ThrowSeconds, "0.00")} |");
        log($"| かけた / 取りが倒れた / お手本からのずれ | | {attempts} / {falls} / {Distance(samples, falls, attempts):0.00} |");
        log("");
    }

    /// <summary>技の中で、体の向きが最大の 95% に達した最初の時刻（回り込みの終わり、論文の E3 に当たる）。</summary>
    internal static float TurnEnd(GrappleTechnique technique)
    {
        var max = technique.Keys.Max(k => MathF.Abs(k.Turn));
        return max <= 0f ? 0f : technique.Keys.First(k => MathF.Abs(k.Turn) >= 0.95f * max).T;
    }

    /// <summary>
    /// お手本からのずれ（小さいほど良い）: 測れた量ごとに (人形 − お手本) / 幅 の 2 乗を平均し、取りが倒れた割合と、床に着かせられなかった分を足す。
    /// 幅は論文の値のばらつきとゲームで許したい差から手で決めた値。
    /// </summary>
    internal static float Distance(IReadOnlyList<Sample> samples, int toriFalls, int attempts)
    {
        if (samples.Count == 0)
        {
            return 10f + toriFalls;
        }

        float Sq(float x) => x * x;
        var d = samples.Average(s =>
            Sq((s.TurnForward - 0.09f) / 0.05f) + Sq((s.LandForward - 0.37f) / 0.15f) + Sq((s.LandUp + 0.31f) / 0.1f)
            + Sq((s.LandDown - 2.38f) / 0.8f) + Sq((s.ToriForwardAtThrow - 1.25f) / 0.5f) + Sq((s.ToriDownAtThrow - 1.25f) / 0.5f)) / 6f;
        var landedShare = samples.Count / (float)Math.Max(1, attempts);
        return d + (2f * toriFalls / Math.Max(1, attempts)) + (2f * (1f - Math.Clamp(landedShare, 0f, 1f)));
    }

    internal static (List<Sample> Samples, int ToriFalls, int Attempts) MeasureAll(GrappleTechnique technique, string victim, int seed, int bouts, float seconds)
    {
        var samples = new List<Sample>();
        var (falls, attempts) = (0, 0);
        var turnEnd = TurnEnd(technique);
        for (var b = 0; b < bouts; b++)
        {
            var (s, f, a) = Measure(technique, turnEnd, victim, seed + b, seconds);
            samples.AddRange(s);
            falls += f;
            attempts += a;
        }

        return (samples, falls, attempts);
    }

    private static (List<Sample> Samples, int ToriFalls, int Attempts) Measure(GrappleTechnique technique, float turnEnd, string victim, int seed, float seconds)
    {
        var samples = new List<Sample>();
        var toriFalls = 0;
        var toriWasUp = true;
        var book = GrappleBook.Jujutsu with { Rate = 50f, Techniques = [technique] };
        JujutsuBot? bot = null;
        var corners = new BoxingCorners("seoi", s => bot = new JujutsuBot(new Closer(), book, s, only: technique.Name), s => ThrowLab.Victim(victim, s),
            CpuWear: MiniRing.Weapons.HandWear.None);
        using var game = new BoxingGame(seed: seed, corners: corners);
        // 1 回の技の間の記録。
        Vector3? start = null;
        Vector3 forward = Vector3.Zero;
        var height = 0f;
        var scale = 1f;
        float? turnForward = null, turnUp = null, toriForward = null, toriDown = null;
        var landed = false;
        var startTime = 0f;
        var time = 0f;
        var previousUkeVelocity = Vector3.Zero;
        game.Landed += impact => landed |= impact.Fighter == game.Player;
        for (; time < seconds; time += Dt)
        {
            var uke = game.Player;
            var tori = game.Cpu;
            previousUkeVelocity = uke.CenterOfMassVelocity;
            landed = false;
            game.Step(default, Dt);
            var toriUp = tori.State == FighterState.Standing;
            toriFalls += toriWasUp && !toriUp ? 1 : 0;
            toriWasUp = toriUp;
            var active = bot?.Current is not null;
            if (active && start is null && uke.State == FighterState.Standing)
            {
                start = uke.CenterOfMass;
                var toUke = uke.ChestPosition - tori.ChestPosition;
                forward = Vector3.Normalize(new Vector3(toUke.X, 0f, toUke.Z)); // 投げる向き = 取りから見た受けの向き（受けは取りの前へ、さらに先へ落ちる）
                forward = -forward; // 背負いは受けを取りの側へ引き出して投げる: 受けの「前」は取りへ向かう向き
                height = MathF.Max(0.05f, uke.HeadPosition.Y + uke.HalfExtentsOf(uke.Parts.Head).Y - BoxingGame.RingSurfaceHeight);
                scale = MathF.Sqrt(1.65f / height);
                startTime = time;
                (turnForward, turnUp, toriForward, toriDown) = (null, null, null, null);
            }

            if (start is not { } c0)
            {
                continue;
            }

            var d = uke.CenterOfMass - c0;
            if (turnForward is null && bot?.Current is not null && bot.Elapsed >= turnEnd)
            {
                turnForward = Vector3.Dot(d, forward) / height;
                turnUp = d.Y / height;
                var tv = tori.CenterOfMassVelocity;
                toriForward = Vector3.Dot(tv, forward) * scale; // 取りも投げる向き（背を向けた取りの前）へ出る
                toriDown = -tv.Y * scale;
            }

            if (landed && turnForward is not null)
            {
                var v = previousUkeVelocity;
                samples.Add(new Sample(turnForward.Value, turnUp!.Value, Vector3.Dot(d, forward) / height, d.Y / height,
                    v.Length() * scale, -v.Y * scale, toriForward!.Value, toriDown!.Value, turnEnd, time - startTime - turnEnd));
                start = null;
            }
            else if (!active && time - startTime > 3f)
            {
                start = null; // 床に着かずに終わった技
            }
        }

        return (samples, toriFalls, bot?.Attempts.GetValueOrDefault(technique.Name) ?? 0);
    }
}
