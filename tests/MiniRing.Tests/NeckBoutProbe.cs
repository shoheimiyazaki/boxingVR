using System.Globalization;
using Aigis.Physics.Voxels;
using MiniRing.Arena;
using MiniRing.Bots;

namespace MiniRing.Tests;

/// <summary>
/// 調べもの: グローブの試合（動画と同じ経路: <see cref="VideoMaker.Corners"/>・コーナーから・標準の速さ 30 fps の歩）で、首・腕・脚の梁が曲げで
/// 折れる数と、折れなかった打撃も含めた首の曲げの比（骨の応力 / 骨の強さ。1 を超えると折れる）の上位を数える。
/// 環境変数 NECKBOUT_RED / NECKBOUT_BLUE（既定 champion / swarmer）、NECKBOUT_SEEDS（既定 8-27）、NECKBOUT_SECONDS（既定 75.5）。
/// </summary>
[TestClass]
[TestCategory("Probe")]
public sealed class NeckBoutProbe
{
    public TestContext TestContext { get; set; } = null!;

    [TestMethod]
    public void Probe_NeckAndLimbBendsInGloveBouts()
    {
        var red = Environment.GetEnvironmentVariable("NECKBOUT_RED") ?? "champion";
        var blue = Environment.GetEnvironmentVariable("NECKBOUT_BLUE") ?? "swarmer";
        var range = (Environment.GetEnvironmentVariable("NECKBOUT_SEEDS") ?? "8-27").Split('-');
        var (first, last) = (int.Parse(range[0], CultureInfo.InvariantCulture), int.Parse(range[^1], CultureInfo.InvariantCulture));
        var trace = float.TryParse(Environment.GetEnvironmentVariable("NECKBOUT_TRACE"), CultureInfo.InvariantCulture, out var tr) ? tr : 0f;
        var seconds = float.TryParse(Environment.GetEnvironmentVariable("NECKBOUT_SECONDS"), CultureInfo.InvariantCulture, out var s) ? s : 75.5f;
        var frameDt = GameSpeed.Standard / 30f;
        var steps = Math.Max(1, (int)MathF.Ceiling((frameDt / (1f / 60f)) - 1e-3f));
        var stepDt = frameDt / steps;
        var neckRatios = new List<(float Ratio, int Seed, float Time, string Who, float Force, float Tie, string Struck)>();
        var breaks = new Dictionary<string, int>();
        var bouts = 0;
        var necksBroken = 0;
        for (var seed = first; seed <= last; seed++)
        {
            var redC = Contender.Named(red);
            var blueC = Contender.Named(blue);
            var corners = TvShowRules.Stage(VideoMaker.Corners(redC, blueC, seed, Weapons.HandWear.Glove, Weapons.HandWear.Glove, null, null,
                sd => blueC.Create(sd), sd => redC.Create(sd)), Organization.Default);
            using var game = new BoxingGame(seed: seed, corners: corners, mood: Organization.Default.ArenaMood);
            var time = 0f;
            var seedNeck = 0f;
            var gate = new object();
            VoxelContact.BendObserved = sample =>
            {
                lock (gate)
                {
                var who = game.Player.Voxels?.PartOf(sample.Beam.BodyId) is { } p ? ("red", p) : game.Cpu.Voxels?.PartOf(sample.Beam.BodyId) is { } q ? ("blue", q) : ("?", null);
                if (who.Item2 is { Kind: BodyPartKind.Head } && sample.Worst is { } w)
                {
                    neckRatios.Add((w.Ratio, seed, time, who.Item1, sample.LocalForce.Length(), sample.Beam.TieActivation, sample.Struck == sample.Beam ? "grid" : "layer"));
                    seedNeck = MathF.Max(seedNeck, w.Ratio);
                    if (trace > 0f && w.Ratio >= trace)
                    {
                        TestContext.WriteLine(string.Create(CultureInfo.InvariantCulture,
                            $"    neck {w.Ratio:0.000} slice {w.Slice} seed {seed} {time,5:0.00}s {who.Item1} point {sample.LocalPoint} force {sample.LocalForce} tie {sample.Beam.TieActivation:0.00} via {(sample.Struck == sample.Beam ? "grid" : "layer")}"));
                    }
                }
                }
            };
            var seen = new HashSet<int>();
            try
            {
                for (; time < seconds; time += stepDt)
                {
                    game.Step(default, stepDt);
                    foreach (var b in game.BrokenBones)
                    {
                        if (b.Bent && seen.Add(b.Body))
                        {
                            var key = b.Part.ToString();
                            breaks[key] = breaks.GetValueOrDefault(key) + 1;
                            necksBroken += b.Part == BodyPartKind.Head ? 1 : 0;
                            TestContext.WriteLine(string.Create(CultureInfo.InvariantCulture, $"  seed {seed} {time,5:0.00}s {(b.Victim == game.Player ? "red" : "blue")} {b.Part} bent"));
                        }
                    }
                }
            }
            finally
            {
                VoxelContact.BendObserved = null;
            }

            bouts++;
            TestContext.WriteLine(string.Create(CultureInfo.InvariantCulture, $"seed {seed}: ended {time:0.0}s, worst neck bend ratio {seedNeck:0.000}"));
        }

        TestContext.WriteLine($"{red} vs {blue}, {bouts} bouts: necks broken {necksBroken}; bent breaks {string.Join(", ", breaks.OrderBy(kv => kv.Key).Select(kv => $"{kv.Key} {kv.Value}"))}");
        TestContext.WriteLine("top neck bend ratios:");
        foreach (var r in neckRatios.OrderByDescending(r => r.Ratio).Take(10))
        {
            TestContext.WriteLine(string.Create(CultureInfo.InvariantCulture, $"  {r.Ratio:0.000} seed {r.Seed} {r.Time,5:0.00}s {r.Who} force {r.Force:0.0} N tie {r.Tie:0.00} via {r.Struck}"));
        }
    }
}
