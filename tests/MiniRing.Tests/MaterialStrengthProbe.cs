using System.Collections.Concurrent;
using System.Globalization;
using MiniRing.Arena;
using MiniRing.Characters;
using MiniRing.Weapons;

namespace MiniRing.Tests;

/// <summary>
/// 材料の強さの測定（2026-11-25、団体の見えない倍率を 1 に戻す・木を実物の樫へ）。
/// <list type="bullet">
/// <item>WVC の決まりで、グローブどうしの 1 ラウンドに骨が折れる割合（<see cref="FractureRateProbe"/> と同じ数え方。団体の決まり・場を通す）。
/// 環境変数 MATERIAL_LIMB_SCALES（既定 "0.6,1"）で腕・脚の骨の曲げ強さの倍率を並べ、同じビルドで比べる。MATERIAL_SEEDS（既定 2）で種の数。</item>
/// <item>木の武器（六尺棒・トンファー・棍棒）の選手が、1 ラウンドのうちに何回の打ち合いで木を折られるか（折れた欠片の数・最初に折れた時刻と、それまでの当たりの数）。</item>
/// </list>
/// </summary>
[TestClass]
[TestCategory("Probe")]
public sealed class MaterialStrengthProbe
{
    private const float Dt = 1f / 60f;

    public TestContext TestContext { get; set; } = null!;

    [TestMethod]
    public void Wvc_GlovedFractureRatePerRound()
    {
        var scales = (Environment.GetEnvironmentVariable("MATERIAL_LIMB_SCALES") ?? "0.6,1")
            .Split(',').Select(s => float.Parse(s, CultureInfo.InvariantCulture)).ToArray();
        var seeds = int.TryParse(Environment.GetEnvironmentVariable("MATERIAL_SEEDS"), out var n) ? n : 2;
        var gloved = CharacterRoster.All.Where(c => c.Weapon is null && c.Wear == HandWear.Glove).ToList();
        var wvc = Organizations.Get("wvc");
        var jobs = new List<(float Scale, FighterCharacter Red, FighterCharacter Blue, int Seed)>();
        foreach (var scale in scales)
        {
            for (var i = 0; i < gloved.Count; i++)
            {
                for (var j = i + 1; j < gloved.Count; j++)
                {
                    for (var seed = 1; seed <= seeds; seed++)
                    {
                        jobs.Add((scale, gloved[i], gloved[j], seed));
                    }
                }
            }
        }

        var results = new ConcurrentBag<(float Scale, int Broken)>();
        Parallel.ForEach(jobs, new ParallelOptions { MaxDegreeOfParallelism = 28 }, job =>
        {
            // 1 ラウンドだけ（入場・終わりのエモートは骨に響かないので省く）。打撃の調整（意識）は WVC のまま、骨の倍率だけ並べる。
            var rules = TvShowRules.Rules(1, MatchRules.Tv.BreakSeconds, wvc) with { LimbBendStrengthScale = job.Scale, IntroSeconds = 0f, EndSeconds = 0f };
            using var game = new BoxingGame(seed: job.Seed, corners: Corners(job.Red, job.Blue, job.Seed, wvc), rules: rules, mood: wvc.ArenaMood);
            var broken = 0;
            for (var t = 0f; t < rules.TotalSeconds + 2f && !game.MatchOver; t += Dt)
            {
                game.Step(default, Dt);
                broken += game.BrokenBones.Count;
            }

            results.Add((job.Scale, broken));
        });

        foreach (var scale in scales)
        {
            var group = results.Where(r => r.Scale == scale).ToList();
            TestContext.WriteLine(string.Create(CultureInfo.InvariantCulture,
                $"WVC gloves, limb bend x{scale}: {group.Count} rounds ({gloved.Count} fighters, {seeds} seeds), with a broken bone {100.0 * group.Count(r => r.Broken > 0) / group.Count:0.0} %, {(double)group.Sum(r => r.Broken) / group.Count:0.00} bones/round"));
        }
    }

    [TestMethod]
    public void WoodenWeapons_HowManyExchangesUntilTheWoodSnaps()
    {
        // 木の武器の選手 × 相手、1 ラウンド（50.35 s、K.O. で終わる。ラウンドが変わると世界ごと作り直されて新しい武器が並ぶので、1 ラウンドだけ見る）。
        // 数えるもの: 木の武器が触れた回数（切られた・潰された・弾いた接触の始まり = 1 回の打ち合い）、折れ（元の 5 % 以上の欠片が離れた）、
        // 削られた木のセル。最初に折れるまでの打ち合いの数。
        var seeds = int.TryParse(Environment.GetEnvironmentVariable("MATERIAL_SEEDS"), out var n) ? n : 5;
        string[] wooden = ["daichi", "tsuyoshi", "sean"];
        string[] opponents = ["daichi", "tsuyoshi", "sean", "jin", "konrad", "edgar", "ramon", "albert"];
        var jobs = (from w in wooden from o in opponents where w != o from s in Enumerable.Range(1, seeds) select (Wood: w, Other: o, Seed: s)).ToList();
        if (Environment.GetEnvironmentVariable("MATERIAL_CASES") is { } cases)
        {
            jobs = cases.Split(',').Select(c => c.Split(':')).Select(c => (Wood: c[0], Other: c[1], Seed: int.Parse(c[2], CultureInfo.InvariantCulture))).ToList();
            wooden = jobs.Select(j => j.Wood).Distinct().ToArray();
        }

        var results = new ConcurrentBag<(string Wood, string Other, int Seed, int Snaps, float FirstSnap, int ContactsBefore, int Contacts, int Carved, float Seconds)>();
        Parallel.ForEach(jobs, new ParallelOptions { MaxDegreeOfParallelism = 28 }, job =>
        {
            var a = CharacterRoster.Get(job.Wood);
            var b = CharacterRoster.Get(job.Other);
            var rules = TvShowRules.Rules(1, MatchRules.Tv.BreakSeconds) with { IntroSeconds = 0f, EndSeconds = 0f };
            using var game = new BoxingGame(seed: job.Seed, corners: Corners(a, b, job.Seed, org: null), rules: rules);
            // 赤（木の武器の選手）の木の武器。
            var mine = Hands.Select(h => game.GloveOf(game.Player, h)).Append(game.WeaponOf(game.Player)).OfType<Weapon>().Where(w => IsWooden(w.Kind)).Distinct().ToList();
            var bodies = mine.Select(w => w.Body).ToHashSet();
            var whole = mine.ToDictionary(w => w.Body, w => w.Body.Grid.FilledCount);
            var wood0 = mine.Sum(w => w.Body.Grid.Remaining(Voxels.BoxerAnatomy.Wood));
            var known = new HashSet<Weapon>(game.Weapons);
            var snaps = 0;
            var firstSnap = float.NaN;
            var contacts = 0;
            var contactsBefore = 0;
            var touched = new HashSet<(int, int)>();
            var t = 0f;
            for (; t < rules.TotalSeconds + 2f && !game.MatchOver; t += Dt)
            {
                game.Step(default, Dt);
                // 打ち合い 1 回 = 木の武器と他の物の組が、前の歩で触れていなかったのに触れた（触れ続けている歩は数えない）。
                var touching = game.RecentCuts.Where(c => bodies.Contains(c.A) || bodies.Contains(c.B)).Select(c => (c.A.BodyId, c.B.BodyId)).ToHashSet();
                contacts += touching.Count(p => !touched.Contains(p));
                touched = touching;
                foreach (var w in game.Weapons)
                {
                    if (!known.Add(w) || !IsWooden(w.Kind))
                    {
                        continue;
                    }

                    // 赤の武器から離れた欠片（同じ種類。相手が同じ種類の木の武器を持つ組は並べていない: 同じ選手どうしは外し、木の武器の選手は種類が皆違う）。元の 5 % 未満は削れて離れた木屑で、折れではない。
                    var source = mine.FirstOrDefault(m => m.Kind == w.Kind);
                    if (source is null || w.Body.Grid.FilledCount < 0.05f * whole[source.Body])
                    {
                        continue;
                    }

                    snaps++;
                    if (float.IsNaN(firstSnap))
                    {
                        firstSnap = t;
                        contactsBefore = contacts;
                    }
                }
            }

            var carved = wood0 - mine.Sum(w => w.Body.Grid.Remaining(Voxels.BoxerAnatomy.Wood));
            results.Add((job.Wood, job.Other, job.Seed, snaps, firstSnap, float.IsNaN(firstSnap) ? contacts : contactsBefore, contacts, carved, t));
        });

        foreach (var wood in wooden)
        {
            var group = results.Where(r => r.Wood == wood).ToList();
            var snapped = group.Where(r => r.Snaps > 0).ToList();
            TestContext.WriteLine(string.Create(CultureInfo.InvariantCulture,
                $"{wood}: {group.Count} rounds ({group.Average(r => r.Seconds):0.0} s), wood broke in {snapped.Count} ({100.0 * snapped.Count / group.Count:0} %), breaks {(double)group.Sum(r => r.Snaps) / group.Count:0.00}/round, wood contacts {group.Average(r => r.Contacts):0}/round, carved {group.Average(r => r.Carved):0} cells/round; first break after {(snapped.Count > 0 ? snapped.Average(r => r.ContactsBefore) : double.NaN):0} contacts ({(snapped.Count > 0 ? snapped.Average(r => r.FirstSnap) : double.NaN):0.0} s)"));
            foreach (var r in group.OrderBy(r => r.Other).ThenBy(r => r.Seed))
            {
                TestContext.WriteLine(string.Create(CultureInfo.InvariantCulture,
                    $"  vs {r.Other} seed {r.Seed}: breaks {r.Snaps}, first {(float.IsNaN(r.FirstSnap) ? "-" : r.FirstSnap.ToString("0.0", CultureInfo.InvariantCulture))} s after {r.ContactsBefore} of {r.Contacts} contacts, carved {r.Carved} cells, {r.Seconds:0.0} s"));
            }
        }
    }

    [TestMethod]
    public void Staff_BreakingForce_AgainstFreeBeamTheory()
    {
        // 自由な丸棒の端を横に押すと、最大の曲げモーメントは押した端から L/3 で 4FL/27（慣性が受ける）。折れる力 F = σ Z · 27 / (4 L)、Z = π r³ / 4。
        foreach (var kind in new[] { WeaponKind.Staff, WeaponKind.Jo, WeaponKind.WoodenClub })
        {
            var grid = Weapon.Build(kind);
            var wood = grid.Palette[Voxels.BoxerAnatomy.Wood].MechanicsOrDefault;
            var half = grid.HalfExtents;
            float Break(System.Numerics.Vector3 at)
            {
                var lo = 1f;
                var hi = 1e6f;
                for (var i = 0; i < 60; i++)
                {
                    var f = MathF.Sqrt(lo * hi);
                    if (Aigis.Physics.Voxels.VoxelBending.WorstBend(grid, at, new System.Numerics.Vector3(0f, f, 0f)) is { Ratio: >= 1f })
                    {
                        hi = f;
                    }
                    else
                    {
                        lo = f;
                    }
                }

                return hi;
            }

            var r = half.Y;
            var length = 2f * half.X;
            var theory = wood.Tensile!.Value * MathF.PI * r * r * r / 4f * 27f / (4f * length);
            TestContext.WriteLine(string.Create(CultureInfo.InvariantCulture,
                $"{kind}: L {length:0.000} m r {r * 1000f:0.0} mm, tensile {wood.Tensile / 1e6f:0.0} MPa; end push breaks at {Break(new System.Numerics.Vector3(half.X * 0.95f, 0f, 0f)):0} N (free-beam theory {theory:0} N), middle push {Break(System.Numerics.Vector3.Zero):0} N"));
        }
    }

    private static readonly Hand[] Hands = [Hand.Left, Hand.Right];

    /// <summary>木でできた武器（折れた欠片も同じ種類を持つ）。盾は木の板に鉄の縁。剣の柄の木は剣の種類なので数えない。</summary>
    private static bool IsWooden(WeaponKind kind) => kind is WeaponKind.Staff or WeaponKind.Tonfa or WeaponKind.WoodenClub or WeaponKind.Jo or WeaponKind.Shield;

    private static BoxingCorners Corners(FighterCharacter a, FighterCharacter b, int seed, Organization? org)
    {
        var red = RosterLeague.ContenderOf(a, b.Id, null);
        var blue = RosterLeague.ContenderOf(b, a.Id, null);
        var corners = VideoMaker.WithCharacters(VideoMaker.Corners(red, blue, seed, a.Wear, b.Wear, a.Weapon, b.Weapon, blue.Create.Invoke, red.Create.Invoke), a, b);
        return org is null ? corners : TvShowRules.Stage(corners, org);
    }
}
