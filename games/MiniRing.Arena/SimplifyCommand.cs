using System.Collections.Concurrent;
using System.Diagnostics;
using System.Globalization;
using System.Text;
using Aigis.Physics.Voxels;

namespace MiniRing.Arena;

/// <summary>
/// 物理を単純化して速くする手（結果が変わる）の限界を探す（docs/PERFORMANCE.md §7）: 案 × 場面 × 種の試合を並列に回し、
/// 物理の 1 フレーム（<c>Step(1/60)</c>）の時間と、試合の中身（打撃・削れたセル・切断・骨折・死・脱落・暴れ）を案ごとに平均して、既定（最初の案）と比べる。
/// 案の書き方: <c>名前:hz=240/sample=0.5/passes=3/bridges=1</c>（案どうしは「,」で区切る。省いた値は既定）。試合の中身は道筋が変わると 1 試合ずつはばらつくので、種を何本も回した平均で見る。
/// </summary>
internal static class SimplifyCommand
{
    private const float Dt = 1f / 60f;

    private sealed record Variant(string Name, float Hz, float Sample, int Passes, bool Bridges);

    private sealed record Result(string Variant, string Scene, int Seed, double MeanMs, double P95Ms, double MaxMs, int Frames, int FistHits, int WeaponHits,
        int Removed, int Severs, int Breaks, int Deaths, int Eliminations, int Blowups, bool Finished);

    public static int Run(Options o)
    {
        var variants = o.List("variants", ["base:"]).Select(Parse).ToList();
        var scenes = o.List("scenes", ["katana", "vfc2", "royale"]);
        var seeds = o.List("seeds", ["1", "2", "3", "4"]).Select(s => int.Parse(s, CultureInfo.InvariantCulture)).ToList();
        var seconds = o.Float("seconds", 40f, min: 1f);
        var parallel = o.Int("parallel", Math.Max(1, Environment.ProcessorCount / 2), min: 1);
        var jobs = (from v in variants from s in scenes from seed in seeds select (v, s, seed)).ToList();
        foreach (var scene in scenes)
        {
            Play(variants[0], scene, 100, 2f); // 暖機（JIT）。測らない
        }

        var results = new ConcurrentBag<Result>();
        var clock = Stopwatch.StartNew();
        Parallel.ForEach(jobs, new ParallelOptions { MaxDegreeOfParallelism = parallel }, job =>
        {
            var run = Play(job.v, job.s, job.seed, seconds);
            results.Add(run);
            Console.WriteLine(string.Create(CultureInfo.InvariantCulture,
                $"{run.Variant} {run.Scene} s{run.Seed}: {run.MeanMs:F2} ms, hits {run.FistHits}+{run.WeaponHits}, removed {run.Removed}, severs {run.Severs}, breaks {run.Breaks}, deaths {run.Deaths}, elim {run.Eliminations}, blowups {run.Blowups} ({clock.Elapsed.TotalMinutes:F1} min)"));
        });

        var text = Report([.. results], variants, scenes, seeds, seconds, parallel);
        Console.Write(text);
        if (o.Get("out") is { } path)
        {
            File.AppendAllText(path, text);
        }

        return 0;
    }

    private static Variant Parse(string spec)
    {
        var colon = spec.IndexOf(':');
        var name = colon < 0 ? spec : spec[..colon];
        var (hz, sample, passes, bridges) = (240f, 0.5f, 3, true);
        foreach (var pair in (colon < 0 ? string.Empty : spec[(colon + 1)..]).Split('/', StringSplitOptions.RemoveEmptyEntries))
        {
            var kv = pair.Split('=');
            switch (kv[0])
            {
                case "hz": hz = float.Parse(kv[1], CultureInfo.InvariantCulture); break;
                case "sample": sample = float.Parse(kv[1], CultureInfo.InvariantCulture); break;
                case "passes": passes = int.Parse(kv[1], CultureInfo.InvariantCulture); break;
                case "bridges": bridges = kv[1] != "0"; break;
                default: throw new ArgumentException($"案の値が分からない: {pair}（hz・sample・passes・bridges）");
            }
        }

        return new Variant(name, hz, sample, passes, bridges);
    }

    private static Result Play(Variant v, string scene, int seed, float seconds)
    {
        // 静的な既定は非同期の流れごと（AsyncLocal）: この試合を回す流れだけに効く。
        BoxingGame.DefaultSubstepRate = v.Hz;
        BoxingGame.DefaultMaxContactPasses = v.Passes;
        VoxelContact.SampleSpacingScale = v.Sample;
        using var game = PerfCommand.Create(scene, seed);
        game.JointBridges = v.Bridges;
        var frames = (int)MathF.Round(seconds / Dt);
        var times = new List<double>(frames);
        int fist = 0, weapon = 0, removed = 0, severs = 0, breaks = 0, blowups = 0;
        for (var i = 0; i < frames && !game.MatchOver; i++)
        {
            var start = Stopwatch.GetTimestamp();
            game.Step(default, Dt);
            times.Add(Stopwatch.GetElapsedTime(start).TotalMilliseconds);
            fist += game.RecentHits.Count;
            weapon += game.WeaponHits.Count;
            severs += game.RecentSevers.Count;
            breaks += game.BrokenBones.Count;
            foreach (var cut in game.RecentCuts)
            {
                removed += cut.RemovedA.Count + cut.RemovedB.Count;
            }

            foreach (var f in game.AllFighters)
            {
                // 暴れ: 胸の位置が数でない・胸が 30 m/s を超えて飛ぶ（人の体ではありえない速さ）。
                var chest = game.World.GetState(f.Parts.Chest);
                if (!float.IsFinite(chest.Position.X + chest.Position.Y + chest.Position.Z) || chest.LinearVelocity.Length() > 30f)
                {
                    blowups++;
                }
            }
        }

        times.Sort();
        return new Result(v.Name, scene, seed, times.Average(), times[(int)(0.95 * (times.Count - 1))], times[^1], times.Count, fist, weapon, removed, severs, breaks,
            game.AllFighters.Count(f => f.IsDead), game.Eliminations.Count, blowups, game.MatchOver);
    }

    private static string Report(List<Result> runs, List<Variant> variants, IReadOnlyList<string> scenes, List<int> seeds, float seconds, int parallel)
    {
        var inv = CultureInfo.InvariantCulture;
        var sb = new StringBuilder();
        sb.AppendLine(inv, $"## simplify（{DateTime.Now:yyyy-MM-dd HH:mm}、{Environment.MachineName}、物理 {Fighter.PhysicsVersion}）");
        sb.AppendLine();
        sb.AppendLine(inv, $"種 {string.Join(",", seeds)} × {seconds:0} 秒、{parallel} 本並列（時間は並列の負荷込み。案どうしは同じ条件で比べる）。中身は種の平均、括弧は既定（{variants[0].Name}）との比。");
        sb.AppendLine();
        foreach (var scene in scenes)
        {
            sb.AppendLine(inv, $"### {scene}");
            sb.AppendLine();
            sb.AppendLine("| 案 | hz | 標本 | 掃き直し | つなぎ | ms/フレーム | p95 | 拳の打撃 | 武器の打撃 | 削れたセル | 切断 | 骨折 | 死 | 脱落 | 暴れ | 決着 |");
            sb.AppendLine("|---|---:|---:|---:|---|---:|---:|---:|---:|---:|---:|---:|---:|---:|---:|---:|");
            var baseRuns = runs.Where(r => r.Scene == scene && r.Variant == variants[0].Name).ToList();
            double Mean(IEnumerable<Result> set, Func<Result, double> f) => set.Any() ? set.Average(f) : 0;
            string Cell(List<Result> set, Func<Result, double> f, string format)
            {
                var value = Mean(set, f);
                var reference = Mean(baseRuns, f);
                var ratio = reference > 1e-9 ? $" ({value / reference * 100:F0}%)" : string.Empty;
                return value.ToString(format, inv) + ratio;
            }

            foreach (var v in variants)
            {
                var set = runs.Where(r => r.Scene == scene && r.Variant == v.Name).ToList();
                sb.AppendLine(inv, $"| {v.Name} | {v.Hz:0} | {v.Sample:0.##} | {v.Passes} | {(v.Bridges ? "あり" : "なし")} | {Cell(set, r => r.MeanMs, "F2")} | {Cell(set, r => r.P95Ms, "F1")} | {Cell(set, r => r.FistHits, "F1")} | {Cell(set, r => r.WeaponHits, "F1")} | {Cell(set, r => r.Removed, "F0")} | {Cell(set, r => r.Severs, "F1")} | {Cell(set, r => r.Breaks, "F1")} | {Mean(set, r => r.Deaths):F1} | {Mean(set, r => r.Eliminations):F1} | {set.Sum(r => r.Blowups)} | {set.Count(r => r.Finished)}/{set.Count} |");
            }

            sb.AppendLine();
        }

        return sb.ToString();
    }
}
