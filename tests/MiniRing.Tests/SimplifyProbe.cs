using System.Diagnostics;
using System.Globalization;
using System.Numerics;
using System.Text;
using Aigis.Foundation.Voxels;
using Aigis.Physics.Voxels;
using MiniRing.Bots;
using MiniRing.Weapons;

namespace MiniRing.Tests;

/// <summary>
/// 調査用（Probe: 通常の実行から外す）。物理を軽くする案（歩の速さ・掃き直しの回数・標本間隔）を、既定との比で測る
/// （<c>docs/arena/gen13_simplify.md</c>）。固定 5 シードの刀試合とボクシング試合を物理の 1 歩（= 1 サブステップ）ずつ進め、
/// 結果（ダウン・折れ・切断・削れたセル・打撃数・打撃エネルギー）と 1 歩の時間・試合時間 1 秒あたりの CPU 時間・静止した構えの震えを取る。
/// 環境変数: SIMPLIFY_VARIANTS（既定 base,hz180,hz120,passes2,passes1,sample2）、SIMPLIFY_OUT（追記先）、SIMPLIFY_KATANA_SECONDS（既定 20）、
/// SIMPLIFY_BOX_SECONDS（既定 20）。変更は静的な既定（<see cref="BoxingGame.DefaultSubstepRate"/> など）で、終わったら戻す。
/// </summary>
[TestClass]
public sealed class SimplifyProbe
{
    private sealed record Variant(string Name, float Hz, int Passes, float Sample);

    private static readonly Variant[] All =
    [
        new("base", 240f, 3, 0.5f),
        new("hz180", 180f, 3, 0.5f),
        new("hz120", 120f, 3, 0.5f),
        new("passes2", 240f, 2, 0.5f),
        new("passes1", 240f, 1, 0.5f),
        new("sample2", 240f, 3, 1.0f),
        new("combo", 120f, 2, 1.0f),
    ];

    private static readonly BoxingCorners KatanaCorners = BoxingCorners.Default with
    {
        Player = SwordBot.Basic, Cpu = SwordBot.Basic,
        PlayerWeapon = WeaponKind.Katana, CpuWeapon = WeaponKind.Katana,
        PlayerTwoHanded = true, CpuTwoHanded = true,
    };

    private sealed class Tally
    {
        public int Knockdowns;
        public int Breaks;
        public int Severs;
        public int Removed;
        public int Hits;
        public double Energy;
        public string Checksum = "";
        public double SimSeconds;
        public readonly List<double> Touched = [];
        public readonly List<double> Untouched = [];
        public double TotalMs;
    }

    [TestMethod]
    public void Measure()
    {
        var names = (Environment.GetEnvironmentVariable("SIMPLIFY_VARIANTS") ?? "base,hz180,hz120,passes2,passes1,sample2").Split(',');
        var katanaSeconds = float.TryParse(Environment.GetEnvironmentVariable("SIMPLIFY_KATANA_SECONDS"), CultureInfo.InvariantCulture, out var ks) ? ks : 20f;
        var boxSeconds = float.TryParse(Environment.GetEnvironmentVariable("SIMPLIFY_BOX_SECONDS"), CultureInfo.InvariantCulture, out var bs) ? bs : 20f;
        var sb = new StringBuilder();
        var savedHz = BoxingGame.DefaultSubstepRate;
        var savedPasses = BoxingGame.DefaultMaxContactPasses;
        var savedSample = VoxelContact.SampleSpacingScale;
        try
        {
            StepProfile.Enabled = false;
            Apply(All[0]);
            using (var warm = new BoxingGame(seed: 100, corners: KatanaCorners))
            {
                for (var i = 0; i < 1200; i++) { warm.Step(default, 1f / 240f); }
            }

            foreach (var name in names)
            {
                var v = All.Single(a => a.Name == name);
                Apply(v);
                var katana = new List<Tally>();
                var boxing = new List<Tally>();
                for (var seed = 0; seed < 5; seed++)
                {
                    katana.Add(RunMatch(KatanaCorners, seed, katanaSeconds, v.Hz));
                }

                var box = BoxingCorners.Default with
                {
                    CpuName = "swarmer", Cpu = s => BotCatalog.Create("swarmer", s), Player = s => BotCatalog.Create("champion", s),
                };
                for (var seed = 0; seed < 5; seed++)
                {
                    boxing.Add(RunMatch(box, seed, boxSeconds, v.Hz));
                }

                var (speed, accel) = Tremor(v.Hz);
                sb.AppendLine(Row(v, "katana", katana, katanaSeconds));
                sb.AppendLine(Row(v, "boxing", boxing, boxSeconds));
                sb.AppendLine(string.Create(CultureInfo.InvariantCulture, $"TREMOR | {v.Name} | speedRMS {speed:F4} m/s | accelRMS {accel:F2} m/s2"));
                Console.WriteLine(sb.ToString());
            }
        }
        finally
        {
            BoxingGame.DefaultSubstepRate = savedHz;
            BoxingGame.DefaultMaxContactPasses = savedPasses;
            VoxelContact.SampleSpacingScale = savedSample;
            StepProfile.Enabled = false;
        }

        if (Environment.GetEnvironmentVariable("SIMPLIFY_OUT") is { Length: > 0 } path)
        {
            File.AppendAllText(path, sb.ToString());
        }
    }

    private static void Apply(Variant v)
    {
        BoxingGame.DefaultSubstepRate = v.Hz;
        BoxingGame.DefaultMaxContactPasses = v.Passes;
        VoxelContact.SampleSpacingScale = v.Sample;
    }

    private static int Filled(BoxingGame game) =>
        game.Player.Voxels!.Parts.Sum(p => p.Body.Grid.FilledCount) + game.Cpu.Voxels!.Parts.Sum(p => p.Body.Grid.FilledCount);

    private static Tally RunMatch(BoxingCorners corners, int seed, float seconds, float hz)
    {
        var t = new Tally();
        var dt = 1f / hz;
        var steps = (int)MathF.Round(seconds * hz);
        using var game = new BoxingGame(seed: seed, corners: corners);
        var round = game.Round;
        var filled = Filled(game);
        bool redDown = false, blueDown = false;
        StepProfile.Enabled = true;
        for (var i = 0; i < steps; i++)
        {
            StepProfile.Reset();
            var start = Stopwatch.GetTimestamp();
            game.Step(default, dt);
            var ms = Stopwatch.GetElapsedTime(start).TotalMilliseconds;
            t.TotalMs += ms;
            (StepProfile.Touches > 0 ? t.Touched : t.Untouched).Add(ms);
            t.SimSeconds += dt;
            t.Breaks += game.BrokenBones.Count;
            t.Severs += game.RecentSevers.Count;
            t.Hits += game.RecentHits.Count + game.WeaponHits.Count;
            t.Energy += game.RecentHits.Sum(h => h.Energy) + game.WeaponHits.Sum(h => 0.5 * h.Momentum * h.Speed);
            if (game.Round != round)
            {
                round = game.Round;
                filled = Filled(game);
                redDown = blueDown = false;
                continue;
            }

            var now = Filled(game);
            t.Removed += Math.Max(0, filled - now);
            filled = now;
            t.Knockdowns += (!redDown && game.Player.IsKnockedDown ? 1 : 0) + (!blueDown && game.Cpu.IsKnockedDown ? 1 : 0);
            redDown = game.Player.IsKnockedDown;
            blueDown = game.Cpu.IsKnockedDown;
        }

        StepProfile.Enabled = false;
        t.Checksum = $"{game.StateChecksum():x}";
        return t;
    }

    private static (double Speed, double Accel) Tremor(float hz)
    {
        var style = BotCatalog.Style("counter") with { Attacks = false, ClinchRate = 0f, ThrowRate = 0f };
        var contender = Contender.Of(style);
        var corners = BoxingCorners.Default with { CpuName = contender.Name, Cpu = s => contender.Create(s), Player = s => contender.Create(s) };
        using var game = new BoxingGame(seed: 3, corners: corners);
        var dt = 1f / hz;
        var settle = (int)(1.5f * hz);
        var window = (int)(3f * hz);
        double s2 = 0, a2 = 0;
        var n = 0;
        Vector3[]? last = null;
        for (var i = 0; i < settle + window; i++)
        {
            game.Step(default, dt);
            var vs = new[] { game.Player.ChestVelocity, game.Player.GloveVelocity(Hand.Left), game.Player.GloveVelocity(Hand.Right),
                game.Cpu.ChestVelocity, game.Cpu.GloveVelocity(Hand.Left), game.Cpu.GloveVelocity(Hand.Right) };
            if (i >= settle && last is not null)
            {
                for (var k = 0; k < vs.Length; k++)
                {
                    s2 += vs[k].LengthSquared();
                    a2 += ((vs[k] - last[k]) / dt).LengthSquared();
                    n++;
                }
            }

            last = vs;
        }

        return (Math.Sqrt(s2 / n), Math.Sqrt(a2 / n));
    }

    private static double Pct(List<double> xs, double p) => xs.Count == 0 ? 0 : xs.Order().ElementAt(Math.Min(xs.Count - 1, (int)(xs.Count * p)));

    private static string Row(Variant v, string kind, List<Tally> ts, float seconds)
    {
        double Avg(Func<Tally, double> f) => ts.Average(f);
        var touched = ts.SelectMany(t => t.Touched).ToList();
        var untouched = ts.SelectMany(t => t.Untouched).ToList();
        var all = touched.Concat(untouched).ToList();
        var simSec = ts.Sum(t => t.SimSeconds);
        return string.Create(CultureInfo.InvariantCulture,
            $"ROW | {v.Name} | {kind} | down {Avg(t => t.Knockdowns):F2} | break {Avg(t => t.Breaks):F2} | sever {Avg(t => t.Severs):F2} | removed {Avg(t => t.Removed):F1} | hits {Avg(t => t.Hits):F1} | energy {Avg(t => t.Energy):F1} | " +
            $"touched n={touched.Count} med {Pct(touched, 0.5):F3} p95 {Pct(touched, 0.95):F3} max {(touched.Count == 0 ? 0 : touched.Max()):F2} | untouched n={untouched.Count} med {Pct(untouched, 0.5):F3} p95 {Pct(untouched, 0.95):F3} max {(untouched.Count == 0 ? 0 : untouched.Max()):F2} | " +
            $"all med {Pct(all, 0.5):F3} p95 {Pct(all, 0.95):F3} | cpu-ms-per-simsec {ts.Sum(t => t.TotalMs) / simSec:F1} | sums {string.Join(" ", ts.Select(t => t.Checksum))} | perMatchRemoved {string.Join(",", ts.Select(t => t.Removed))} | perMatchHits {string.Join(",", ts.Select(t => t.Hits))}");
    }
}
