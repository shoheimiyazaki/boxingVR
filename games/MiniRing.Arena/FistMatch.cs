using System.Globalization;
using System.Text;
using System.Text.Json;
using MiniRing.Bots;

namespace MiniRing.Arena;

/// <summary>
/// 拳の打ち合いの物差し（<c>fistmatch</c>）: ボットの組 × 種の試合を回し、拳の当たりごとに当たる前の歩の拳の速さ（当たった瞬間の拳）・
/// 相対の当たる速さ・乗る質量・エネルギー（<see cref="HitEvent.Energy"/>）・ダメージ・光（<see cref="BoxingGame.HitStrength"/>）、
/// 技の打ち出しから終わりまでの拳の速さの最大（打ち出しの最高速）、倒れた回数（1 秒以内に拳を受けていない倒れは「打たれずに」）、
/// あざ（<see cref="Voxels.BoxerBody.SkinBruiseTotal"/>、ラウンドごとの終わりの値の合計）を 1 試合 1 行の JSON に書く。
/// <c>--compare a.jsonl b.jsonl</c> は 2 つの記録（版の前後）を同じ試合どうしで対にして表と比べ直し（<see cref="PairedEvaluation"/>）を出す。
/// </summary>
internal static class FistMatch
{
    private const float StepDt = 1f / 60f;

    /// <summary>1 発の当たり。</summary>
    internal sealed record Hit(string Tech, float Fist, float Speed, float Mass, float Energy, float Damage, float Flash, string Zone);

    /// <summary>1 試合の記録。</summary>
    internal sealed record Bout(string Pair, int Seed, string Red, string Blue, int Winner, float RedDamage, float BlueDamage,
        List<Hit> Hits, List<float> Peaks, int Falls, int UnhitFalls, int Knockdowns, float Bruises, string Versions);

    public static Bout Run(Contender red, Contender blue, string pair, int seed, float seconds, bool referee)
    {
        var redBots = new List<IFighterControl>();
        var blueBots = new List<IFighterControl>();
        var corners = new BoxingCorners(blue.Name, s => Track(blueBots, blue.Create(s)), s => Track(redBots, red.Create(s)),
            PlayerName: red.Name, PlayerBody: red.Build, CpuBody: blue.Build, PlayerFace: red.Face, CpuFace: blue.Face, Referee: referee);
        using var game = new BoxingGame(null, seed, corners);
        var hits = new List<Hit>();
        var peaks = new List<float>();
        var redDamage = 0f;
        var blueDamage = 0f;
        var falls = 0;
        var unhit = 0;
        var knockdowns = 0;
        var bruises = 0f;
        var clock = 0f;
        // 歩の始めの拳の速さ（当たった瞬間の拳は、接触で速度がそろう前のこの値）・打ち出し中の最高速・最後に拳を受けた時刻・倒れていたか
        var gloveSpeed = new float[2, 2];
        var peak = new float[2, 2];
        var techOf = new Technique?[2, 2];
        var lastHit = new[] { -10f, -10f };
        var wasDown = new bool[2];
        var wasCounted = new bool[2];
        Fighter?[] owners = [null, null];
        var lastBruise = new float[2];
        var steps = (int)MathF.Ceiling(seconds / StepDt);
        for (var i = 0; i < steps; i++)
        {
            Fighter[] fighters = [game.Player, game.Cpu];
            for (var f = 0; f < 2; f++)
            {
                if (!ReferenceEquals(owners[f], fighters[f]))
                {
                    bruises += lastBruise[f];
                    lastBruise[f] = 0f;
                    owners[f] = fighters[f];
                    wasDown[f] = false;
                    wasCounted[f] = false;
                    for (var h = 0; h < 2; h++)
                    {
                        FlushPeak(f, h);
                    }
                }

                for (var h = 0; h < 2; h++)
                {
                    gloveSpeed[f, h] = fighters[f].GloveVelocity((Hand)h).Length();
                }
            }

            game.Step(default, StepDt);
            clock += StepDt;
            fighters = [game.Player, game.Cpu];
            var redBot = redBots.Count > 0 ? redBots[^1] as BoxerBot : null;
            var blueBot = blueBots.Count > 0 ? blueBots[^1] as BoxerBot : null;
            BoxerBot?[] brains = [redBot, blueBot];
            foreach (var hit in game.RecentHits)
            {
                var a = hit.Attacker == game.Player ? 0 : 1;
                if (a == 0)
                {
                    redDamage += hit.Damage;
                }
                else
                {
                    blueDamage += hit.Damage;
                }

                lastHit[1 - a] = clock;
                if (hit.Kick || hit.Elbow)
                {
                    continue;
                }

                var tech = brains[a]?.CurrentTechnique(hit.Hand)?.ToString() ?? "-";
                hits.Add(new Hit(tech, gloveSpeed[a, (int)hit.Hand], hit.Speed, hit.ImpactMass, hit.Energy, hit.Damage, BoxingGame.HitStrength(hit.Damage), hit.Zone.ToString()));
            }

            foreach (var hit in game.WeaponHits)
            {
                var a = hit.Attacker == game.Player ? 0 : 1;
                if (a == 0)
                {
                    redDamage += hit.Damage;
                }
                else
                {
                    blueDamage += hit.Damage;
                }

                lastHit[1 - a] = clock;
            }

            for (var f = 0; f < 2; f++)
            {
                var fighter = fighters[f];
                var down = fighter.State == FighterState.Down;
                if (down && !wasDown[f])
                {
                    falls++;
                    unhit += clock - lastHit[f] > 1f ? 1 : 0;
                }

                wasDown[f] = down;
                var counted = fighter.IsKnockedDown;
                knockdowns += counted && !wasCounted[f] ? 1 : 0;
                wasCounted[f] = counted;
                lastBruise[f] = fighter.Voxels?.SkinBruiseTotal() ?? 0f;
                for (var h = 0; h < 2; h++)
                {
                    var tech = brains[f]?.CurrentTechnique((Hand)h);
                    if (!Equals(tech, techOf[f, h]))
                    {
                        FlushPeak(f, h);
                        techOf[f, h] = tech;
                    }

                    if (tech is not null)
                    {
                        peak[f, h] = MathF.Max(peak[f, h], fighter.GloveVelocity((Hand)h).Length());
                    }
                }
            }
        }

        bruises += lastBruise[0] + lastBruise[1];
        for (var f = 0; f < 2; f++)
        {
            for (var h = 0; h < 2; h++)
            {
                FlushPeak(f, h);
            }
        }

        var winner = MathF.Abs(redDamage - blueDamage) <= MathF.Max(1f, 0.05f * (redDamage + blueDamage) / 2f) ? 0 : MathF.Sign(redDamage - blueDamage);
        return new Bout(pair, seed, red.Name, blue.Name, winner, redDamage, blueDamage, hits, peaks, falls, unhit, knockdowns, bruises,
            $"physics {Fighter.PhysicsVersion} logic {BoxerBot.LogicVersion}");

        void FlushPeak(int f, int h)
        {
            if (techOf[f, h] is not null && peak[f, h] > 0f)
            {
                peaks.Add(peak[f, h]);
            }

            peak[f, h] = 0f;
            techOf[f, h] = null;
        }
    }

    private static IFighterControl Track(List<IFighterControl> list, IFighterControl bot)
    {
        list.Add(bot);
        return bot;
    }

    /// <summary>組 × 種の試合を並列に回し、1 試合 1 行の JSON を <paramref name="outPath"/> に書く（順は組・種の順）。</summary>
    public static IReadOnlyList<Bout> Collect(IReadOnlyList<(Contender Red, Contender Blue)> pairs, IReadOnlyList<int> seeds, float seconds, bool referee,
        int parallel, string? outPath)
    {
        var specs = pairs.SelectMany(p => seeds.Select(s => (p.Red, p.Blue, s))).ToList();
        var results = new Bout[specs.Count];
        Parallel.For(0, specs.Count, new ParallelOptions { MaxDegreeOfParallelism = parallel }, i =>
        {
            var (r, b, s) = specs[i];
            results[i] = Run(r, b, $"{r.Name}:{b.Name}", s, seconds, referee);
            Console.Error.WriteLine($"  {r.Name}:{b.Name} seed {s} done");
        });
        if (outPath is not null)
        {
            File.WriteAllLines(outPath, results.Select(r => JsonSerializer.Serialize(r)));
        }

        return results;
    }

    public static IReadOnlyList<Bout> Load(string path)
        => [.. File.ReadAllLines(path).Where(l => l.Length > 0).Select(l => JsonSerializer.Deserialize<Bout>(l) ?? throw new InvalidDataException(path))];

    /// <summary>記録の 1 行ぶんの表の値（種の範囲・記録の名前ごと）。</summary>
    public static string Row(string label, string seedsLabel, IReadOnlyList<Bout> bouts)
    {
        var hits = bouts.SelectMany(b => b.Hits).ToList();
        var peaks = bouts.SelectMany(b => b.Peaks).ToList();
        string Wins(string pair)
        {
            var mine = bouts.Where(b => b.Pair == pair).ToList();
            return mine.Count == 0 ? "-" : string.Create(CultureInfo.InvariantCulture,
                $"{mine.Count(b => b.Winner > 0)}/{mine.Count}（{mine.Average(b => b.RedDamage - b.BlueDamage):+0;-0;0}）");
        }

        var pairs = bouts.Select(b => b.Pair).Distinct().ToList();
        return string.Create(CultureInfo.InvariantCulture,
            $"| {seedsLabel} | {label} | {hits.Count} | {Pct(hits.Select(h => h.Fist), 50):0.00} / {Pct(hits.Select(h => h.Fist), 90):0.00} | {Pct(hits.Select(h => h.Speed), 50):0.00} | " +
            $"{Pct(peaks, 50):0.00} / {Pct(peaks, 90):0.00} | {Pct(hits.Select(h => h.Mass), 50):0.00} | {Mean(hits.Select(h => h.Energy)):0.00} | {Mean(hits.Select(h => h.Damage)):0.00} | " +
            $"{100f * hits.Count(h => h.Flash > 0f) / Math.Max(1, hits.Count):0} % | {Mean(hits.Select(h => h.Flash)):0.000} | {bouts.Average(b => b.Bruises / 2f):0} | " +
            $"{bouts.Sum(b => b.Falls)} | {bouts.Sum(b => b.UnhitFalls)} | {bouts.Sum(b => b.Knockdowns)} | {string.Join(" | ", pairs.Select(Wins))} |");
    }

    public static string Header(IReadOnlyList<string> pairs)
        => "| 種 | 版 | 当たり | 当たった瞬間の拳 p50 / p90 [m/s] | 当たる速さ p50 | 打ち出しの最高速 p50 / p90 | 乗る質量 p50 [kg] | エネルギー平均 [J] | ダメージ平均 | 光が出る割合 | 光の平均 | あざ（1 人・1 試合） | 倒れた回数 | 打たれずに倒れた | ダウン | " +
           string.Join(" | ", pairs.Select(p => $"{p} 赤の勝ち（ダメージ差）")) + " |\n|" + string.Concat(Enumerable.Repeat("---|", 15 + pairs.Count));

    /// <summary>技ごとの打ち出しの最高速は記録に技の名前を持たないので、当たりの技ごとに当たった瞬間の拳とエネルギー。</summary>
    public static string ByTechnique(string label, IReadOnlyList<Bout> bouts)
    {
        var sb = new StringBuilder($"== {label}\n");
        foreach (var g in bouts.SelectMany(b => b.Hits).GroupBy(h => h.Tech).OrderBy(g => g.Key, StringComparer.Ordinal))
        {
            sb.AppendLine(string.Create(CultureInfo.InvariantCulture,
                $"  {g.Key,-14} hits={g.Count(),4} fist p50={Pct(g.Select(h => h.Fist), 50):0.00} p90={Pct(g.Select(h => h.Fist), 90):0.00} energy={Mean(g.Select(h => h.Energy)):0.00} flash={100f * g.Count(h => h.Flash > 0f) / g.Count():0}%"));
        }

        return sb.ToString();
    }

    /// <summary>同じ試合（組・種）どうしを対にして、1 発のエネルギーの平均と光が出る割合を比べ直す（A = 後、B = 前）。</summary>
    public static IEnumerable<(string Metric, PairedResult Result)> Confirm(IReadOnlyList<Bout> before, IReadOnlyList<Bout> after)
    {
        var keyed = before.ToDictionary(b => (b.Pair, b.Seed));
        var pairs = after.Where(a => keyed.ContainsKey((a.Pair, a.Seed))).Select(a => (A: a, B: keyed[(a.Pair, a.Seed)])).ToList();
        static double Energy(Bout b) => b.Hits.Count == 0 ? 0d : b.Hits.Average(h => h.Energy);
        static double Flash(Bout b) => b.Hits.Count == 0 ? 0d : 100d * b.Hits.Count(h => h.Flash > 0f) / b.Hits.Count;
        static double Peak(Bout b) => b.Peaks.Count == 0 ? 0d : Pct(b.Peaks, 50);
        yield return ("拳 1 発のエネルギーの平均 [J]", PairedEvaluation.Compute([.. pairs.Select(p => Energy(p.A))], [.. pairs.Select(p => Energy(p.B))]));
        yield return ("光が出る割合 [%]", PairedEvaluation.Compute([.. pairs.Select(p => Flash(p.A))], [.. pairs.Select(p => Flash(p.B))]));
        yield return ("打ち出しの最高速 p50 [m/s]", PairedEvaluation.Compute([.. pairs.Select(p => Peak(p.A))], [.. pairs.Select(p => Peak(p.B))]));
    }

    private static float Mean(IEnumerable<float> values)
    {
        var list = values.ToList();
        return list.Count == 0 ? 0f : list.Average();
    }

    private static float Pct(IEnumerable<float> values, int p)
    {
        var sorted = values.OrderBy(v => v).ToList();
        return sorted.Count == 0 ? 0f : sorted[Math.Clamp((int)MathF.Round(p / 100f * (sorted.Count - 1)), 0, sorted.Count - 1)];
    }
}
