using System.Text.Json.Nodes;
using MiniRing.Arena;
using MiniRing.Arena.Circuit;
using MiniRing.Characters;
using MiniRing.Weapons;

namespace MiniRing.Tests;

/// <summary>
/// IVF の新人リーグ（docs/plans/IVF_PROSPECTS_PLAN.md）: 種から作る新人の決定性と軸（違いは体格・教科書・教育の揺らぎだけ・計量で階級に入る・国の割合）、
/// 引退の規則の単調性、組み合わせ、記録の読み書き、名簿のプロフィールの戦績 = 基の戦績 + 記録の集計。試合を回す決定性は <see cref="CircuitBoutTests"/>。
/// </summary>
[TestClass]
public sealed class CircuitTests
{
    private static Organization Ivf => Organization.Of("ivf");

    private static readonly Lazy<IReadOnlyList<Prospect>> AllProspects = new(() => ProspectFactory.All(CircuitConfig.Of("ivf"), Ivf, ProspectFactory.ReservedIds(CharacterRoster.All)));

    [TestMethod]
    public void Generation_IsDeterministic_AndTheSeedChangesIt()
    {
        var config = CircuitConfig.Of("ivf");
        var taken = new HashSet<string>(ProspectFactory.ReservedIds(CharacterRoster.All));
        for (var i = 0; i < 6; i++)
        {
            var slot = ProspectFactory.Slots(config)[i];
            var a = ProspectFactory.Make(config, Ivf, i, slot.Division, slot.Country, new HashSet<string>(taken));
            var b = ProspectFactory.Make(config, Ivf, i, slot.Division, slot.Country, new HashSet<string>(taken));
            Assert.AreEqual(a.Json, b.Json, $"番号 {i} が同じ種で違うファイルになった");
            var other = ProspectFactory.Make(config with { Seed = config.Seed + 1 }, Ivf, i, slot.Division, slot.Country, new HashSet<string>(taken));
            Assert.AreNotEqual(a.Json, other.Json, "種を変えても同じ新人");
        }

        CollectionAssert.AreEqual(ProspectFactory.Slots(config).ToList(), ProspectFactory.Slots(config).ToList());
    }

    [TestMethod]
    public void EveryProspect_WeighsIntoItsDivision_HasUniqueId_AndOnlyBodyTextbookAndSmallEducationDiffer()
    {
        var config = CircuitConfig.Of("ivf");
        var all = AllProspects.Value;
        Assert.AreEqual(config.Total, all.Count);
        Assert.AreEqual(all.Count, all.Select(p => p.Character.Id).Distinct().Count(), "id が重なった");
        var reserved = ProspectFactory.ReservedIds(CharacterRoster.All).ToHashSet();
        Assert.IsFalse(all.Any(p => reserved.Contains(p.Character.Id)), "名簿の id と重なった");
        var allowedKeys = config.Education.Keys.Select(k => k.Key).Append("Southpaw").ToHashSet();
        foreach (var p in all)
        {
            var c = p.Character;
            Assert.AreEqual(p.Division, Ivf.DivisionOf(CharacterProfile.WeightOf(c.Build)).Id, $"{c.Id} の計量が階級に入らない");
            Assert.IsNull(OrgBouts.Ineligible(Ivf, c), $"{c.Id} が IVF に出られない");
            Assert.IsTrue(c.Build.Muscle >= config.Body.MinMuscle && c.Build.Muscle <= config.Body.MaxMuscle, $"{c.Id} の太さ {c.Build.Muscle}");
            Assert.AreEqual(HandWear.Glove, c.Wear);
            Assert.IsNull(c.Weapon);
            Assert.IsTrue(config.Textbooks.ContainsKey(c.StyleName), c.StyleName);

            // 教育は FighterCommand を出す側の決まった鍵だけ（筋力・速さ・反応の倍率は無い）、教科書の値から小さく揺らぐだけ。
            Assert.AreEqual(0, c.Education.Vs.Count);
            foreach (var (key, value) in c.Education.Style)
            {
                Assert.IsTrue(allowedKeys.Contains(key), $"{c.Id} の教育に許していない鍵 {key}");
                if (key == "Southpaw")
                {
                    continue;
                }

                var book = JsonNode.Parse(Bots.BotCatalog.Style(c.StyleName).ToJson())![key]!.GetValue<float>();
                var v = value!.GetValue<float>();
                var spec = config.Education.Keys.First(k => k.Key == key);
                Assert.IsTrue(v >= spec.Min - 1e-4f && v <= spec.Max + 1e-4f, $"{c.Id}.{key} = {v} が幅の外");
                Assert.IsTrue(MathF.Abs(v - Math.Clamp(book, spec.Min, spec.Max)) <= (2.5f * config.Education.Sd * MathF.Abs(book)) + 1e-3f, $"{c.Id}.{key} = {v} が教科書 {book} から離れすぎ");
            }
        }
    }

    [TestMethod]
    public void StoredProspectFiles_AreWhatTheSeedGenerates()
    {
        // 記録の新人のファイルは、決まりの種から作り直すと 1 バイトも違わない（生成の式を変えたら generatorVersion を上げる。古いファイルは記録のハッシュで守る）。
        var dir = Path.Combine(RepoRoot(), "league", "ivf", "prospects");
        if (!Directory.Exists(dir))
        {
            Assert.Inconclusive("リーグの新人がまだいません。");
        }

        var generated = AllProspects.Value.ToDictionary(p => p.Character.Id);
        var files = Directory.GetFiles(dir, "*.json");
        Assert.IsTrue(files.Length > 0);
        foreach (var path in files)
        {
            var id = Path.GetFileNameWithoutExtension(path);
            var version = System.Text.RegularExpressions.Regex.Match(File.ReadAllText(path), "\"generatorVersion\": *([0-9]+)");
            if (version.Success && int.Parse(version.Groups[1].Value, System.Globalization.CultureInfo.InvariantCulture) != CircuitConfig.Of("ivf").GeneratorVersion)
            {
                continue; // 前の版の生成の新人（記録のハッシュで守るファイル）。作り直しと比べない
            }

            Assert.IsTrue(generated.TryGetValue(id, out var p), $"{id} は今の種からは作られない");
            Assert.AreEqual(ProspectFactory.HashOf(p.Json), ProspectFactory.HashOf(File.ReadAllText(path)), $"{id} のファイルが作り直しと違う");
        }
    }

    [TestMethod]
    public void Countries_FollowTheWeights_ByLargestRemainder()
    {
        var config = CircuitConfig.Of("ivf");
        var counts = ProspectFactory.Slots(config).GroupBy(s => s.Country).ToDictionary(g => g.Key, g => g.Count());
        var sum = config.Countries.Values.Sum(c => c.Weight);
        foreach (var (id, c) in config.Countries)
        {
            var exact = config.Total * c.Weight / sum;
            Assert.IsTrue(MathF.Abs(counts.GetValueOrDefault(id) - exact) < 1f, $"{id}: {counts.GetValueOrDefault(id)} 人（比で {exact:0.0}）");
            Assert.IsTrue(CharacterRoster.Countries.ContainsKey(id), $"国 {id} の旗が countries.json にない");
        }

        var divisions = ProspectFactory.Slots(config).GroupBy(s => s.Division).ToDictionary(g => g.Key, g => g.Count());
        foreach (var d in config.Divisions)
        {
            Assert.AreEqual(d.Count, divisions[d.Id]);
        }
    }

    [TestMethod]
    public void Names_AreNotOnTheDenyList()
    {
        var config = CircuitConfig.Of("ivf");
        var deny = config.DenyNames.ToHashSet();
        foreach (var p in AllProspects.Value)
        {
            var latin = p.Character.Id.Replace('-', ' ');
            Assert.IsFalse(deny.Any(d => latin.StartsWith(d, StringComparison.Ordinal)), $"{p.Character.Id} が実在の選手の名前");
        }
    }

    [TestMethod]
    public void Retirement_IsMonotone_AddingALossNeverUnretires()
    {
        var rules = CircuitConfig.Of("ivf").Retire;
        var rng = new CircuitRandom(7);
        for (var trial = 0; trial < 2000; trial++)
        {
            var bouts = new List<BoutRecord>();
            var n = rng.Range(0, 10);
            for (var i = 0; i < n; i++)
            {
                bouts.Add(Bout($"b{i}", "me", $"o{i}", rng.Range(0, 2) switch { 0 => "red", 1 => "blue", _ => null }, rng.NextDouble() < 0.5 ? "KO" : "DEC"));
            }

            var before = CircuitRules.RetireReason(FighterTally.Of("me", bouts, Ivf.Ranking), rules) is not null;
            var lost = bouts.Append(Bout("x", "me", "ox", "blue", rng.NextDouble() < 0.5 ? "KO" : "DEC")).ToList();
            var after = CircuitRules.RetireReason(FighterTally.Of("me", lost, Ivf.Ranking), rules) is not null;
            Assert.IsTrue(!before || after, $"負けを足して引退が解けた（試合 {n}）");

            // 規則を緩めれば、引退は増えない。
            var looser = new RetireRules(rules.NetLosses + 1, rules.LosingStreak + 1, rules.KoLosses + 1);
            var loose = CircuitRules.RetireReason(FighterTally.Of("me", bouts, Ivf.Ranking), looser) is not null;
            Assert.IsTrue(!loose || before, "規則を緩めて引退が増えた");
        }

        Assert.IsNull(CircuitRules.RetireReason(FighterTally.Of("me", [], Ivf.Ranking), rules));
        var three = Enumerable.Range(0, rules.LosingStreak).Select(i => Bout($"l{i}", "me", $"o{i}", "blue", "DEC")).ToList();
        StringAssert.Contains(CircuitRules.RetireReason(FighterTally.Of("me", three, Ivf.Ranking), rules), "負け");

        // 試合で死んだ選手は、それまでの戦績によらず引退（戻らない）。
        var died = new[] { Bout("w1", "me", "o1", "red", "KO"), Bout("d", "o2", "me", "red", "KO") with { Death = true } };
        Assert.AreEqual("試合で死亡", CircuitRules.RetireReason(FighterTally.Of("me", died, Ivf.Ranking), rules));
        Assert.IsNull(CircuitRules.RetireReason(FighterTally.Of("o2", died, Ivf.Ranking), rules), "勝った側は死んでいない");
    }

    [TestMethod]
    public void Tally_CountsWinsLossesKosStreakAndPoints()
    {
        var bouts = new[]
        {
            Bout("1", "me", "a", "red", "KO"),
            Bout("2", "b", "me", "red", "DEC"),
            Bout("3", "me", "c", null, "DRAW"),
            Bout("4", "d", "me", "blue", "DEC"),
            Bout("5", "e", "me", "red", "KO"),
            Bout("6", "f", "me", "red", "DEC"),
        };
        var t = FighterTally.Of("me", bouts, Ivf.Ranking);
        Assert.AreEqual((2, 3, 1, 1, 1, 2), (t.Wins, t.Losses, t.Draws, t.Kos, t.KoLosses, t.LosingStreak));
        Assert.AreEqual((3f + 1f) + 1f + 3f, t.Points, "勝ち 3・倒して +1・引き分け 1");
    }

    [TestMethod]
    public void Pairing_UsesEveryoneOnce_AvoidsRematches_AndGivesTheByeToTheFewestByes()
    {
        var ids = Enumerable.Range(0, 7).Select(i => $"p{i}").ToList();
        var ordered = ids.Select(id => new FighterTally(id, 0, 0, 0, 0, 0, 0, 0, 0, 0)).ToList();
        var history = new[] { Bout("h", "p0", "p1", "red", "DEC") };
        var byes = new Dictionary<string, int> { ["p6"] = 1 };
        var (pairs, bye) = CircuitRules.Pair(ordered, history, byes);
        Assert.AreEqual("p5", bye, "休みは休んだ回数の少ない人のうち一番下");
        var used = pairs.SelectMany(p => new[] { p.Red, p.Blue }).Append(bye!).ToList();
        CollectionAssert.AreEquivalent(ids, used);
        Assert.IsFalse(pairs.Any(p => (p.Red, p.Blue) is ("p0", "p1") or ("p1", "p0")), "避けられる再戦を組んだ");
        Assert.AreEqual(("p0", "p2"), pairs[0], "上位から、まだ当たっていない一番近い相手");
    }

    [TestMethod]
    public void Ledger_WritesAndReadsBack_AndRefusesDuplicates()
    {
        var dir = Directory.CreateTempSubdirectory("circuit-ledger");
        try
        {
            var path = Path.Combine(dir.FullName, "bouts.jsonl");
            var a = Bout("s1-e01-middle-01", "x", "y", "red", "KO") with { Finish = "K.O. — OUT COLD  エックス WIN", Checksum = "0x0123456789abcdef", Drama = 7.5f };
            var b = Bout("s1-e01-middle-02", "z", "w", null, "DRAW");
            BoutLedger.Append(path, [a]);
            BoutLedger.Append(path, [b]);
            var read = BoutLedger.Read(path);
            Assert.AreEqual(2, read.Count);
            Assert.AreEqual(a, read[0]);
            Assert.AreEqual(b, read[1]);
            Assert.AreEqual(a, BoutLedger.FromLine(BoutLedger.ToLine(a)));
            Assert.ThrowsExactly<InvalidOperationException>(() => BoutLedger.Append(path, [a]));
            Assert.AreEqual(0, BoutLedger.Read(Path.Combine(dir.FullName, "none.jsonl")).Count);
        }
        finally
        {
            dir.Delete(recursive: true);
        }
    }

    [TestMethod]
    public void RandomStream_IsStableAcrossRuns()
    {
        // 種付きの乱数の並びはプロセス・版によらない（SplitMix64）。値を写すのでなく、同じ種の 2 本が同じで、違う種で違うことと、混ぜた種が入力の順に依ることを見る。
        var a = new CircuitRandom(42);
        var b = new CircuitRandom(42);
        Assert.IsTrue(Enumerable.Range(0, 100).All(_ => a.NextULong() == b.NextULong()));
        Assert.AreNotEqual(new CircuitRandom(1).NextULong(), new CircuitRandom(2).NextULong());
        Assert.AreNotEqual(CircuitRandom.Mix(1, 2), CircuitRandom.Mix(2, 1));
        Assert.AreEqual(CircuitRandom.Hash("middle"), CircuitRandom.Hash("middle"));
    }

    [TestMethod]
    public void RosterProfiles_FightRecordEqualsBaselinePlusLedger()
    {
        var root = Path.Combine(RepoRoot(), "league", "ivf");
        var ledgerPath = Path.Combine(root, "bouts.jsonl");
        if (!File.Exists(ledgerPath))
        {
            Assert.Inconclusive("リーグの記録がまだありません。");
        }

        var ledger = BoutLedger.Read(ledgerPath);
        var baselinePath = Path.Combine(root, "baseline.json");
        var baseline = File.Exists(baselinePath) ? JsonNode.Parse(File.ReadAllText(baselinePath))!.AsObject() : [];
        var checkedAny = false;
        foreach (var c in CharacterRoster.All.Where(c => ledger.Any(b => b.Involves(c.Id)) && c.Profile is not null))
        {
            var expected = CircuitCommand.RecordNode(baseline[c.Id]?["fightRecord"], FighterTally.Of(c.Id, ledger, Ivf.Ranking));
            var r = c.Profile!.FightRecord;
            Assert.AreEqual((expected["wins"]!.GetValue<int>(), expected["losses"]!.GetValue<int>(), expected["draws"]!.GetValue<int>(), expected["kos"]!.GetValue<int>()),
                (r.Wins, r.Losses, r.Draws, r.Kos), $"{c.Id} のプロフィールの戦績が記録と合わない（circuit profiles で書き直す）");
            checkedAny = true;
        }

        if (!checkedAny)
        {
            Assert.Inconclusive("記録に出てくる名簿の選手がまだいません。");
        }
    }

    internal static BoutRecord Bout(string id, string red, string blue, string? winner, string method) => new()
    {
        Id = id,
        Kind = "prospect",
        Org = "ivf",
        Division = "middle",
        Commit = "test",
        Red = red,
        Blue = blue,
        RedFile = $"{red}.json",
        BlueFile = $"{blue}.json",
        RedHash = "sha256:0",
        BlueHash = "sha256:0",
        Winner = winner,
        Method = winner is null ? "DRAW" : method,
        Checksum = "0x0",
        RedDealt = 10f,
        BlueDealt = 5f,
    };

    private static string RepoRoot()
    {
        for (var dir = new DirectoryInfo(AppContext.BaseDirectory); dir is not null; dir = dir.Parent)
        {
            if (File.Exists(Path.Combine(dir.FullName, "CLAUDE.md")) && Directory.Exists(Path.Combine(dir.FullName, "games")))
            {
                return dir.FullName;
            }
        }

        throw new InvalidOperationException("リポジトリの根が見つからない。");
    }
}

/// <summary>新人リーグの 1 試合を 2 度回すと、終わりの状態のチェックサムまで同じ（記録から再現できる）。</summary>
[TestClass]
[TestCategory("slow")]
public sealed class CircuitBoutTests
{
    [TestMethod]
    public void SameSpec_GivesTheSameChecksumAndResult()
    {
        var dir = Directory.CreateTempSubdirectory("circuit-bout");
        try
        {
            var config = CircuitConfig.Of("ivf");
            var org = Organization.Of("ivf");
            var league = new CircuitLeague(dir.FullName, config, org);
            var slots = ProspectFactory.Slots(config);
            var taken = new HashSet<string>(ProspectFactory.ReservedIds(CharacterRoster.All));
            var picks = Enumerable.Range(0, slots.Count).Where(i => slots[i].Division == "middle").Take(2).ToList();
            var files = picks.Select(i =>
            {
                var p = ProspectFactory.Make(config, org, i, slots[i].Division, slots[i].Country, taken);
                taken.Add(p.Character.Id);
                var path = Path.Combine(dir.FullName, $"{p.Character.Id}.json");
                File.WriteAllText(path, p.Json);
                return (p.Character.Id, Path: path, Hash: ProspectFactory.HashOf(p.Json));
            }).ToList();
            var spec = new BoutSpec("t-1", 1, 1, "prospect", "middle", 12345, 1, files[0].Id, files[1].Id, files[0].Path, files[1].Path, files[0].Hash, files[1].Hash);
            var a = league.Fight(spec, "test", 16.78f);
            var b = league.Fight(spec, "test", 16.78f);
            Assert.AreEqual(a, b, "同じ申し込みで違う試合になった");
            Assert.AreNotEqual("0x0000000000000000", a.Checksum);

            // ファイルが変わっていれば、写しが無い限り読まない（記録と違う選手で回さない）。
            File.AppendAllText(files[0].Path, " ");
            Assert.ThrowsExactly<InvalidDataException>(() => league.Fight(spec, "test", 16.78f));
        }
        finally
        {
            dir.Delete(recursive: true);
        }
    }
}
