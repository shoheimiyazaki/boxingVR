using System.Diagnostics;
using System.Globalization;
using System.Text;
using Aigis.Foundation.Voxels;
using MiniRing.Bots;
using MiniRing.Weapons;

namespace MiniRing.Tests;

/// <summary>
/// 調査用（通常の実行から外す: Probe）。固定 5 シードの刀試合を 240 Hz の 1 歩ずつ進め、接触のあった歩の CPU 時間と内訳（<see cref="StepProfile"/>）を取る。
/// 環境変数 STEPCOST_OUT に書き出し先、STEPCOST_LABEL に表の見出し、STEPCOST_STEPS に 1 シードの歩数（既定 2400 = 10 秒）。
/// 最終チェックサムは計測を切った別の走りでも取り、計測が結果に響かないことを確かめる。
/// </summary>
[TestClass]
[TestCategory("Probe")]
public sealed class KatanaStepCostProbe
{
    private static readonly BoxingCorners KatanaCorners = BoxingCorners.Default with
    {
        Player = SwordBot.Basic, Cpu = SwordBot.Basic,
        PlayerWeapon = WeaponKind.Katana, CpuWeapon = WeaponKind.Katana,
        PlayerTwoHanded = true, CpuTwoHanded = true,
    };

    /// <summary>拳の試合（champion 対 swarmer。SimplifyProbe と同じ）。</summary>
    private static readonly BoxingCorners FistCorners = BoxingCorners.Default with
    {
        CpuName = "swarmer", Cpu = s => BotCatalog.Create("swarmer", s), Player = s => BotCatalog.Create("champion", s),
    };

    private static BoxingCorners Corners => Environment.GetEnvironmentVariable("STEPCOST_MODE") == "fist" ? FistCorners : KatanaCorners;

    private sealed record Sample(int Seed, int Step, double Total, int Calls, int Touches, double[] Slots, long Alloc, long[] Allocs, int Gcs);

    [TestMethod]
    public void MeasureStepCost()
    {
        var steps = int.TryParse(Environment.GetEnvironmentVariable("STEPCOST_STEPS"), out var n) ? n : 4028;
        var label = Environment.GetEnvironmentVariable("STEPCOST_LABEL") ?? "計測";
        var slots = Enum.GetValues<StepProfile.Slot>();

        // 暖機（JIT）。計測は切る。
        StepProfile.Enabled = false;
        using (var warm = new BoxingGame(seed: 100, corners: Corners))
        {
            for (var i = 0; i < 1200; i++) { warm.Step(default, 1f / 240f); }
        }

        var plainSums = new List<string>();
        for (var seed = 0; seed < 5; seed++)
        {
            using var game = new BoxingGame(seed: seed, corners: Corners);
            for (var i = 0; i < steps; i++) { game.Step(default, 1f / 240f); }
            plainSums.Add($"{game.StateChecksum():x}");
        }

        StepProfile.Enabled = true;
        StepProfile.TrackAllocations = Environment.GetEnvironmentVariable("STEPCOST_ALLOC") == "1";
        var samples = new List<Sample>();
        var sums = new List<string>();
        for (var seed = 0; seed < 5; seed++)
        {
            using var game = new BoxingGame(seed: seed, corners: Corners);
            for (var i = 0; i < steps; i++)
            {
                StepProfile.Reset();
                var gcBefore = GC.CollectionCount(0);
                var start = Stopwatch.GetTimestamp();
                game.Step(default, 1f / 240f);
                var total = Stopwatch.GetElapsedTime(start).TotalMilliseconds;
                var alloc = StepProfile.AllocatedSinceReset;
                samples.Add(new Sample(seed, i, total, StepProfile.ResolveCalls, StepProfile.Touches, slots.Select(StepProfile.Milliseconds).ToArray(), alloc, slots.Select(StepProfile.Allocated).ToArray(), GC.CollectionCount(0) - gcBefore));
            }

            sums.Add($"{game.StateChecksum():x}");
        }

        StepProfile.Enabled = false;
        StepProfile.TrackAllocations = false;

        var sb = new StringBuilder();
        sb.AppendLine(CultureInfo.InvariantCulture, $"## {label}");
        sb.AppendLine();
        sb.AppendLine(CultureInfo.InvariantCulture, $"5 シード × {steps} 歩（240 Hz の 1 歩ずつ）、日本刀・両手持ち・SwordBot.Basic 同士。時間は 1 歩（`game.Step(1/240)`）の CPU 時間 [ms]。");
        sb.AppendLine();
        sb.AppendLine(CultureInfo.InvariantCulture, $"最終チェックサム（計測あり）: {string.Join(" ", sums)}  ");
        sb.AppendLine(CultureInfo.InvariantCulture, $"最終チェックサム（計測なし）: {string.Join(" ", plainSums)}");
        Assert.AreEqual(string.Join(" ", plainSums), string.Join(" ", sums), "計測が結果を変えた");
        sb.AppendLine();
        sb.AppendLine("| 歩の種類 | 歩数 | 中央値 | p95 | 最大 | 割り当て 中央値 [B] | 平均 [B] | 最大 [B] |");
        sb.AppendLine("|---|---:|---:|---:|---:|---:|---:|---:|");
        foreach (var (name, set) in new[]
        {
            ("全部", samples),
            ("接触検査あり（Resolve が呼ばれた）", samples.Where(s => s.Calls > 0).ToList()),
            ("触れた（Touched）", samples.Where(s => s.Touches > 0).ToList()),
            ("触れなかった", samples.Where(s => s.Touches == 0).ToList()),
        })
        {
            if (set.Count == 0) { continue; }
            var t = set.Select(s => s.Total).Order().ToList();
            var a = set.Select(s => s.Alloc).Order().ToList();
            sb.AppendLine(CultureInfo.InvariantCulture, $"| {name} | {t.Count} | {t[t.Count / 2]:F3} | {t[(int)(t.Count * 0.95)]:F3} | {t[^1]:F3} | {a[a.Count / 2]} | {a.Average():F0} | {a[^1]} |");
        }

        sb.AppendLine();
        sb.AppendLine("### 内訳（触れた歩の合計に対する平均 [ms/歩]。入れ子の枠は外側に含まれる）");
        sb.AppendLine();
        var touched = samples.Where(s => s.Touches > 0).ToList();
        var all = samples;
        var untouched = samples.Where(s => s.Touches == 0).ToList();
        sb.AppendLine("| 枠 | 触れた歩の平均 | 触れなかった歩の平均 | 全歩の平均 | 割り当て 触れた [B/歩] | 触れなかった [B/歩] |");
        sb.AppendLine("|---|---:|---:|---:|---:|---:|");
        for (var k = 0; k < slots.Length; k++)
        {
            var a = touched.Count == 0 ? 0 : touched.Average(s => s.Slots[k]);
            var b = all.Average(s => s.Slots[k]);
            var u = untouched.Count == 0 ? 0 : untouched.Average(s => s.Slots[k]);
            var at = touched.Count == 0 ? 0 : touched.Average(s => s.Allocs[k]);
            var au = untouched.Count == 0 ? 0 : untouched.Average(s => s.Allocs[k]);
            sb.AppendLine(CultureInfo.InvariantCulture, $"| {slots[k]} | {a:F3} | {u:F3} | {b:F4} | {at:F0} | {au:F0} |");
        }

        sb.AppendLine();
        sb.AppendLine("### 重い歩の上位 20（ms）");
        sb.AppendLine();
        sb.AppendLine("| # | seed:歩 | 合計 | Resolve 呼 / 触 | GC | " + string.Join(" | ", slots.Select(s => s.ToString())) + " |");
        sb.AppendLine("|---:|---|---:|---|---:|" + string.Join("|", slots.Select(_ => "---:")) + "|");
        var rank = 1;
        foreach (var s in samples.OrderByDescending(s => s.Total).Take(20))
        {
            sb.AppendLine(CultureInfo.InvariantCulture, $"| {rank++} | {s.Seed}:{s.Step} | {s.Total:F2} | {s.Calls} / {s.Touches} | {s.Gcs} | {string.Join(" | ", s.Slots.Select(v => v.ToString("F2", CultureInfo.InvariantCulture)))} |");
        }

        sb.AppendLine();
        Console.WriteLine(sb.ToString());
        if (Environment.GetEnvironmentVariable("STEPCOST_OUT") is { Length: > 0 } path)
        {
            File.AppendAllText(path, sb.ToString());
        }
    }
}
