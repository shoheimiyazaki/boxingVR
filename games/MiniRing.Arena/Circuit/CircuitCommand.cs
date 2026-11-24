using System.Globalization;
using System.Text.Json;
using System.Text.Json.Nodes;
using MiniRing.Characters;

namespace MiniRing.Arena.Circuit;

/// <summary>
/// <c>circuit</c> のコマンド（docs/plans/IVF_PROSPECTS_PLAN.md）:
/// season（新人を作り、興行を回し、引退を判定）・fight（子のプロセス: カードの一部を回す）・register（上位を名簿とランキングへ）・
/// challenge（登録した新人がランキングを上る挑戦試合）・profiles（記録から名簿のプロフィールの戦績・経歴を書き直す）・report・verify。
/// </summary>
internal static class CircuitCommand
{
    private const string RosterDir = "games/MiniRing/Characters/roster";

    public static int Run(string[] argv)
    {
        if (argv.Length == 0)
        {
            throw new ArgumentException("circuit の後に season・fight・register・challenge・profiles・report・verify のどれかを書いてください。");
        }

        var o = Options.Parse(argv[1..], "org", "league", "workers", "events", "only", "limit", "card", "shard", "of", "out", "bout", "standings");
        var org = Organizations.Get(o.Get("org") ?? "ivf");
        var config = CircuitConfig.Of(org.Id);
        var league = new CircuitLeague(o.Get("league") ?? Path.Combine("league", org.Id), config, org);
        return argv[0] switch
        {
            "season" => Season(league, o),
            "fight" => Fight(league, o),
            "register" => Register(league, o),
            "challenge" => Challenge(league, o),
            "profiles" => Profiles(league),
            "report" => Report(league, o),
            "verify" => Verify(league, o),
            _ => throw new ArgumentException($"circuit {argv[0]} は知りません。"),
        };
    }

    // ── season ────────────────────────────────────────────────────────────

    private static int Season(CircuitLeague league, Options o)
    {
        var state = league.LoadState() ?? Generate(league, o);
        var lastEvent = Math.Min(o.Int("events", league.Config.Season.Events, min: 1), league.Config.Season.Events);
        var workers = o.Int("workers", 1, min: 1);
        var commit = CircuitLeague.Commit();
        for (var evt = state.EventsDone + 1; evt <= lastEvent; evt++)
        {
            Console.Error.WriteLine($"興行 {evt}（シーズン {state.Season}）");
            var ledger = league.Ledger();
            var specs = new List<BoutSpec>();
            var byes = new Dictionary<string, int>(state.Byes);
            foreach (var division in league.Config.Divisions)
            {
                var active = state.Fighters.Where(f => f.Value.Division == division.Id && f.Value.Status == LeagueEntry.Active).Select(f => f.Key).ToList();
                if (active.Count < 2)
                {
                    continue;
                }

                var ordered = CircuitRules.Order(active.Select(id => FighterTally.Of(id, ledger, league.Org.Ranking)));
                var (pairs, bye) = CircuitRules.Pair(ordered, ledger, byes);
                if (bye is not null)
                {
                    byes[bye] = byes.GetValueOrDefault(bye) + 1;
                }

                for (var k = 0; k < pairs.Count; k++)
                {
                    var (red, blue) = pairs[k];
                    var (redFile, redHash) = league.Snapshot(Path.Combine(league.ProspectsDir, $"{red}.json"), keepCopy: false);
                    var (blueFile, blueHash) = league.Snapshot(Path.Combine(league.ProspectsDir, $"{blue}.json"), keepCopy: false);
                    specs.Add(new BoutSpec(string.Create(CultureInfo.InvariantCulture, $"s{state.Season}-e{evt:00}-{division.Id}-{k + 1:00}"), state.Season, evt, "prospect",
                        division.Id, league.SeedOf(state.Season, evt, division.Id, k), league.Config.Season.ProspectRounds, red, blue, redFile, blueFile, redHash, blueHash));
                }
            }

            var card = new BoutCard(league.Org.Id, commit, league.Config.Season.BreakSeconds, specs);
            var results = league.Run(card, workers, string.Create(CultureInfo.InvariantCulture, $"s{state.Season}-e{evt:00}"), Console.Error);
            BoutLedger.Append(league.LedgerPath, results);
            ledger = league.Ledger();
            var fighters = new Dictionary<string, LeagueEntry>(state.Fighters);
            foreach (var (id, entry) in state.Fighters.Where(f => f.Value.Status == LeagueEntry.Active))
            {
                if (CircuitRules.RetireReason(FighterTally.Of(id, ledger, league.Org.Ranking), league.Config.Retire) is { } reason)
                {
                    fighters[id] = entry with { Status = LeagueEntry.Retired, Since = evt, Reason = reason };
                }
            }

            state = state with { EventsDone = evt, Fighters = fighters, Byes = byes };
            league.SaveState(state);
            Console.Error.WriteLine($"  現役 {fighters.Count(f => f.Value.Status == LeagueEntry.Active)}・引退 {fighters.Count(f => f.Value.Status == LeagueEntry.Retired)}");
        }

        return 0;
    }

    private static LeagueState Generate(CircuitLeague league, Options o)
    {
        var only = o.Get("only");
        var limit = o.Int("limit", int.MaxValue, min: 2);
        var all = ProspectFactory.All(league.Config, league.Org, [.. CharacterRoster.All.Select(c => c.Id)]);
        var chosen = all.Where(p => only is null || p.Division == only).GroupBy(p => p.Division).SelectMany(g => g.Take(limit)).ToList();
        Directory.CreateDirectory(league.ProspectsDir);
        foreach (var p in chosen)
        {
            File.WriteAllText(Path.Combine(league.ProspectsDir, $"{p.Character.Id}.json"), p.Json);
        }

        var state = new LeagueState
        {
            Org = league.Org.Id,
            Fighters = chosen.ToDictionary(p => p.Character.Id, p => new LeagueEntry(p.Division, p.Character.Country.Id, LeagueEntry.Active)),
        };
        league.SaveState(state);
        Console.Error.WriteLine($"新人 {chosen.Count} 人を作りました → {league.ProspectsDir}");
        return state;
    }

    // ── fight（子のプロセス）────────────────────────────────────────────────

    private static int Fight(CircuitLeague league, Options o)
    {
        var card = JsonSerializer.Deserialize<BoutCard>(File.ReadAllText(o.Get("card") ?? throw new ArgumentException("--card を渡してください。")), CircuitLeague.Json)
            ?? throw new InvalidDataException("カードが空です。");
        var shard = o.Int("shard", 0, min: 0);
        var of = o.Int("of", 1, min: 1);
        var output = o.Get("out") ?? throw new ArgumentException("--out を渡してください。");
        for (var i = shard; i < card.Bouts.Count; i += of)
        {
            var r = league.Fight(card.Bouts[i], card.Commit, card.BreakSeconds);
            BoutLedger.Append(output, [r]); // 1 本ずつ書く（途中で止まっても済んだ分は残る）
        }

        return 0;
    }

    // ── register ─────────────────────────────────────────────────────────

    private static int Register(CircuitLeague league, Options o)
    {
        var state = league.LoadState() ?? throw new InvalidOperationException("リーグがまだありません（circuit season）。");
        var ledger = league.Ledger();
        var standings = OrgStandings.Of(league.Org);
        SaveBaseline(league, standings);
        var fighters = new Dictionary<string, LeagueEntry>(state.Fighters);
        foreach (var division in league.Config.Divisions)
        {
            var top = Contenders(league, state, ledger, division.Id).Take(league.Config.Register.PerDivision).ToList();
            var seat = standings.Divisions.First(d => d.Division == division.Id);
            var ranking = seat.Ranking.ToList();
            foreach (var t in top)
            {
                File.Copy(Path.Combine(league.ProspectsDir, $"{t.Id}.json"), Path.Combine(RosterDir, $"{t.Id}.json"), overwrite: true);
                fighters[t.Id] = fighters[t.Id] with { Status = LeagueEntry.Registered, Since = state.EventsDone };
                if (!ranking.Contains(t.Id))
                {
                    ranking.Add(t.Id);
                }

                Console.Error.WriteLine($"{division.Id}: {t.Id} {t.Text} を登録");
            }

            standings = standings.WithSeat(seat with { Ranking = ranking });
        }

        league.SaveState(state with { Fighters = fighters });
        var output = o.Get("standings") ?? Path.Combine("games", "MiniRing.Arena", "Orgs", "standings", $"{league.Org.Id}.json");
        File.WriteAllText(output, (standings with { AsOf = Today(), Basis = standings.Basis + $"。新人リーグ シーズン {state.Season} の上位を末尾に登録" }).ToJson());
        Console.Error.WriteLine($"→ {RosterDir}・{output}（組み込みなので、挑戦の前にビルドし直す）");
        return 0;
    }

    /// <summary>登録の候補（現役で試合数が足りる人を順位の並びで）。</summary>
    internal static IReadOnlyList<FighterTally> Contenders(CircuitLeague league, LeagueState state, IReadOnlyList<BoutRecord> ledger, string division)
        => CircuitRules.Order(state.Fighters.Where(f => f.Value.Division == division && f.Value.Status != LeagueEntry.Retired)
            .Select(f => FighterTally.Of(f.Key, ledger.Where(b => b.Kind == "prospect"), league.Org.Ranking))
            .Where(t => t.Bouts >= league.Config.Register.MinBouts));

    private static void SaveBaseline(CircuitLeague league, OrgStandings standings)
    {
        if (File.Exists(league.BaselinePath))
        {
            return;
        }

        var node = new JsonObject();
        foreach (var id in standings.Divisions.SelectMany(d => (d.Champion is { } c ? [c] : Array.Empty<string>()).Concat(d.Ranking)))
        {
            var profile = JsonNode.Parse(File.ReadAllText(Path.Combine(RosterDir, $"{id}.json")))!["profile"];
            node[id] = new JsonObject
            {
                ["fightRecord"] = profile?["fightRecord"]?.DeepClone() ?? new JsonObject { ["wins"] = 0, ["losses"] = 0, ["draws"] = 0, ["kos"] = 0 },
                ["career"] = profile?["career"]?.DeepClone() ?? new JsonArray(),
            };
        }

        File.WriteAllText(league.BaselinePath, node.ToJsonString(new JsonSerializerOptions { WriteIndented = true, Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping }) + "\n");
    }

    // ── challenge ────────────────────────────────────────────────────────

    private static int Challenge(CircuitLeague league, Options o)
    {
        var state = league.LoadState() ?? throw new InvalidOperationException("リーグがまだありません。");
        if (state.ChallengesDone)
        {
            Console.Error.WriteLine("挑戦は済んでいます。");
            return 0;
        }

        var workers = o.Int("workers", 1, min: 1);
        var commit = CircuitLeague.Commit();
        var standings = OrgStandings.Of(league.Org);
        var climbers = league.Config.Divisions.ToDictionary(d => d.Id, d => new Queue<string>(
            standings.Divisions.First(s => s.Division == d.Id).Ranking.Where(id => state.Fighters.TryGetValue(id, out var e) && e.Status == LeagueEntry.Registered)));
        var climbed = new Dictionary<string, int>();
        var vacantHeld = new HashSet<string>();
        var met = new HashSet<(string, string)>();
        var evt = league.Config.Season.Events;
        for (var step = 1; step < 100; step++)
        {
            var specs = new List<BoutSpec>();
            foreach (var division in league.Config.Divisions)
            {
                var seat = standings.Divisions.First(s => s.Division == division.Id);
                var queue = climbers[division.Id];
                (string Red, string Blue, string Kind)? bout = null;
                while (queue.Count > 0 && bout is null)
                {
                    var climber = queue.Peek();
                    var rank = seat.Ranking.ToList().IndexOf(climber);
                    var above = rank > 0 ? seat.Ranking[rank - 1] : rank == 0 ? seat.Champion : null;
                    if (above is null || climbed.GetValueOrDefault(climber) >= league.Config.Register.MaxChallenges || met.Contains((climber, above)))
                    {
                        queue.Dequeue();
                        continue;
                    }

                    bout = (above, climber, rank == 0 ? "title" : "challenge"); // 赤 = 席を持つ側
                }

                if (bout is null && queue.Count == 0 && seat.Champion is null && seat.Ranking.Count >= 2 && vacantHeld.Add(division.Id))
                {
                    bout = (seat.Ranking[0], seat.Ranking[1], "vacant"); // 王座決定戦
                }

                if (bout is { } b)
                {
                    var (redFile, redHash) = league.Snapshot(Path.Combine(RosterDir, $"{b.Red}.json"), keepCopy: true);
                    var (blueFile, blueHash) = league.Snapshot(Path.Combine(RosterDir, $"{b.Blue}.json"), keepCopy: true);
                    specs.Add(new BoutSpec(string.Create(CultureInfo.InvariantCulture, $"s{state.Season}-c{step:00}-{division.Id}"), state.Season, evt + step, b.Kind == "challenge" ? "challenge" : "title",
                        division.Id, league.SeedOf(state.Season, 100 + step, division.Id, 0), league.Config.Season.ChallengeRounds, b.Red, b.Blue, redFile, blueFile, redHash, blueHash));
                    met.Add((b.Blue, b.Red));
                    met.Add((b.Red, b.Blue));
                }
            }

            if (specs.Count == 0)
            {
                break;
            }

            Console.Error.WriteLine($"挑戦 {step}");
            var results = league.Run(new BoutCard(league.Org.Id, commit, league.Config.Season.BreakSeconds, specs), workers, string.Create(CultureInfo.InvariantCulture, $"s{state.Season}-c{step:00}"), Console.Error);
            BoutLedger.Append(league.LedgerPath, results);
            foreach (var r in results)
            {
                var division = league.Org.Division(r.Division);
                var seat = standings.For(division);
                var vacant = seat.Champion is null && r.Kind == "title";
                if (r.WinnerId is { } winner)
                {
                    standings = vacant
                        ? standings.WithSeat(new DivisionStanding(division.Id, winner, [.. seat.Ranking.Where(id => id != winner)]))
                        : standings.AfterBout(division, winner, r.LoserId!);
                }

                var climber = climbers[division.Id].Count > 0 ? climbers[division.Id].Peek() : null;
                if (climber is not null && r.Involves(climber))
                {
                    climbed[climber] = climbed.GetValueOrDefault(climber) + 1;
                    if (r.WinnerId != climber)
                    {
                        climbers[division.Id].Dequeue(); // 負け・引き分けで止まる
                    }
                }
            }
        }

        var output = o.Get("standings") ?? Path.Combine("games", "MiniRing.Arena", "Orgs", "standings", $"{league.Org.Id}.json");
        File.WriteAllText(output, (standings with { AsOf = Today() }).ToJson());
        league.SaveState(state with { ChallengesDone = true });
        Console.Error.WriteLine($"→ {output}");
        return 0;
    }

    // ── profiles ─────────────────────────────────────────────────────────

    private static int Profiles(CircuitLeague league)
    {
        var ledger = league.Ledger();
        var baseline = File.Exists(league.BaselinePath) ? JsonNode.Parse(File.ReadAllText(league.BaselinePath))!.AsObject() : [];
        var names = Names(league);
        var state = league.LoadState();
        foreach (var path in Directory.GetFiles(RosterDir, "*.json").Order(StringComparer.Ordinal))
        {
            var id = Path.GetFileNameWithoutExtension(path);
            if (!ledger.Any(b => b.Involves(id)))
            {
                continue;
            }

            var node = JsonNode.Parse(File.ReadAllText(path))!.AsObject();
            var mine = ledger.Where(b => b.Involves(id)).ToList();
            var tally = FighterTally.Of(id, mine, league.Org.Ranking);
            var basis = baseline[id];
            var career = basis?["career"]?.AsArray().Select(c => c!.GetValue<string>()).ToList() ?? [];
            career.AddRange(mine.Select(b => CareerLine(league, id, b, names)));
            if (basis is null)
            {
                node["profile"] = ProspectProfile(league, node, id, mine, tally, state, career);
            }
            else
            {
                var profile = node["profile"]?.AsObject() ?? throw new InvalidDataException($"{id} にプロフィールがありません。");
                profile["fightRecord"] = RecordNode(basis["fightRecord"], tally);
                profile["career"] = Strings(career);
            }

            File.WriteAllText(path, CharacterFile.Format(node));
            Console.Error.WriteLine($"{id}: {tally.Text}（記録 {mine.Count} 試合）");
        }

        return 0;
    }

    /// <summary>プロフィールの戦績: 基の戦績（レギュラーのリーグ前の数）＋ 記録の集計。</summary>
    internal static JsonObject RecordNode(JsonNode? basis, FighterTally t)
    {
        int Base(string key) => basis?[key]?.GetValue<int>() ?? 0;
        return new JsonObject { ["wins"] = Base("wins") + t.Wins, ["losses"] = Base("losses") + t.Losses, ["draws"] = Base("draws") + t.Draws, ["kos"] = Base("kos") + t.Kos };
    }

    private static JsonArray Strings(IEnumerable<string> items) => new([.. items.Select(s => (JsonNode)JsonValue.Create(s)!)]);

    private static string CareerLine(CircuitLeague league, string id, BoutRecord b, IReadOnlyDictionary<string, string> names)
    {
        var opponent = names.GetValueOrDefault(b.OpponentOf(id), b.OpponentOf(id));
        var division = league.Org.Division(b.Division).Name;
        var result = b.WinnerId is null ? "引き分け" : b.WinnerId == id ? "勝ち" : "負け";
        var how = b.Method switch { "KO" => $"{b.Round}R KO ", "DEC" => "判定", _ => "" };
        var stage = b.Kind switch
        {
            "prospect" => $"{league.Org.LogoText} 新人リーグ 第{b.Event}興行",
            "title" => $"{league.Org.LogoText} {division}王座戦",
            _ => $"{league.Org.LogoText} {division}ランキング挑戦",
        };
        return $"シーズン{b.Season}・{stage}: {opponent}に{how}{result}";
    }

    /// <summary>
    /// 登録した新人のプロフィール: 数字・経歴・戦い方・強み・弱み・中継の小ネタは記録と体から作り、人柄・趣味・家族・エピソードなどの物語は
    /// <c>league/&lt;org&gt;/stories/&lt;id&gt;.json</c>（手で書いた架空の話）から入れる。物語が無ければ、どの欄が要るかを書いて止める。
    /// </summary>
    private static JsonObject ProspectProfile(CircuitLeague league, JsonObject node, string id, IReadOnlyList<BoutRecord> mine, FighterTally t, LeagueState? state, IReadOnlyList<string> career)
    {
        var bare = node.DeepClone().AsObject();
        bare.Remove("profile");
        var c = CharacterRoster.FromFiles([($"{id}.json", bare.ToJsonString())], [.. CharacterRoster.All.Select(x => x.Id)]).Single();
        var generated = c.Record.FirstOrDefault(r => r["kind"]?.GetValue<string>() == "generated")
            ?? throw new InvalidDataException($"{id} に生成の記録（record の kind: generated）がありません。");
        var storyPath = Path.Combine(league.Root, "stories", $"{id}.json");
        var story = File.Exists(storyPath) ? JsonNode.Parse(File.ReadAllText(storyPath))!.AsObject()
            : throw new InvalidDataException($"{id} の物語 {storyPath} がありません（hometown・nicknameStory・trainer・personality・hobbies・family・episodes・quote）。");
        var b = c.Build;
        var prospects = mine.Where(x => x.Kind == "prospect").ToList();
        var pt = FighterTally.Of(id, prospects, league.Org.Ranking);
        var division = league.Org.DivisionOf(CharacterProfile.WeightOf(b)).Name;
        var stance = generated["stance"]?.GetValue<string>() ?? "オーソドックス";
        var southpaw = stance == "サウスポー";
        var strengths = new List<string>();
        var weaknesses = new List<string>();
        if (b.Arms >= 1.03f)
        {
            strengths.Add("長い腕で遠くから届く");
        }
        else if (b.Arms <= 0.97f)
        {
            weaknesses.Add("腕が短く、懐へ入らないと届かない");
        }

        if (b.Muscle >= 1.12f)
        {
            strengths.Add("太い体の重さが拳に乗る");
        }
        else if (b.Muscle <= 0.93f)
        {
            weaknesses.Add("細い体で、もらうと流される");
        }

        if (t.Kos > 0 && t.Kos * 2 >= t.Wins)
        {
            strengths.Add($"倒し切る決定力（{t.Wins}勝のうち{t.Kos}KO）");
        }

        if (t.Losses == 0)
        {
            strengths.Add("負けを知らない勢い");
        }
        else
        {
            weaknesses.Add($"{t.Losses}つの負けから学ぶ途中");
        }

        if (southpaw)
        {
            strengths.Add("サウスポーの構えで相手の読みを外す");
        }

        if (t.Dealt > t.Taken * 1.5f)
        {
            strengths.Add("打ち合いで相手より多く当てる");
        }
        else if (t.Taken > t.Dealt)
        {
            weaknesses.Add("もらう数が当てる数より多い");
        }

        if (strengths.Count == 0)
        {
            strengths.Add("教科書どおりの崩れない基本");
        }

        weaknesses.Add("5 ラウンドの長い試合の経験が浅い");
        var styleText = league.Config.StyleText.GetValueOrDefault(c.StyleName, c.StyleName);
        var notes = new List<string>
        {
            $"{league.Org.LogoText} 新人リーグ シーズン{state?.Season ?? 1}で{pt.Text}。{division}の上位で正式に登録された",
            $"{c.Country.Name}出身、{generated["age"]?.GetValue<int>()}歳",
        };
        var best = prospects.Where(x => x.WinnerId == id && x.Method == "KO").OrderBy(x => x.Round).ThenBy(x => x.Time).FirstOrDefault();
        if (best is not null)
        {
            notes.Add(string.Create(CultureInfo.InvariantCulture, $"新人リーグで一番早い KO は {best.Round}R {best.Time:0} 秒"));
        }

        foreach (var n in story["commentaryNotes"]?.AsArray() ?? [])
        {
            notes.Add(n!.GetValue<string>());
        }

        string Story(string key) => story[key]?.GetValue<string>() is { Length: > 0 } s ? s : throw new InvalidDataException($"{storyPath}: {key} がありません。");
        JsonArray StoryList(string key) => story[key]?.AsArray() is { Count: > 0 } a ? (JsonArray)a.DeepClone() : throw new InvalidDataException($"{storyPath}: {key} が空です。");
        node["tagline"] = story["tagline"]?.GetValue<string>() ?? $"{league.Org.LogoText} {division}の新鋭";
        return new JsonObject
        {
            ["age"] = generated["age"]?.GetValue<int>() ?? 20,
            ["heightCm"] = CharacterProfile.HeightOf(b),
            ["weightKg"] = CharacterProfile.WeightOf(b),
            ["birthplace"] = story["birthplace"]?.GetValue<string>() ?? c.Country.Name,
            ["hometown"] = Story("hometown"),
            ["stance"] = stance,
            ["nicknameStory"] = Story("nicknameStory"),
            ["fightRecord"] = RecordNode(null, t),
            ["career"] = Strings(career),
            ["trainer"] = Story("trainer"),
            ["fightingStyle"] = string.Create(CultureInfo.InvariantCulture,
                $"{styleText}。{CharacterProfile.HeightOf(b)} cm・{CharacterProfile.WeightOf(b)} kg の{division}で、腕は基準の {b.Arms:0.00} 倍。{(southpaw ? "サウスポー。" : "")}"),
            ["strengths"] = Strings(strengths),
            ["weaknesses"] = Strings(weaknesses),
            ["personality"] = Story("personality"),
            ["hobbies"] = StoryList("hobbies"),
            ["family"] = Story("family"),
            ["episodes"] = StoryList("episodes"),
            ["quote"] = Story("quote"),
            ["commentaryNotes"] = Strings(notes),
        };
    }

    private static Dictionary<string, string> Names(CircuitLeague league)
    {
        var names = CharacterRoster.All.ToDictionary(c => c.Id, c => c.Name);
        if (Directory.Exists(league.ProspectsDir))
        {
            foreach (var path in Directory.GetFiles(league.ProspectsDir, "*.json"))
            {
                var node = JsonNode.Parse(File.ReadAllText(path))!;
                names.TryAdd(node["id"]!.GetValue<string>(), node["name"]!.GetValue<string>());
            }
        }

        return names;
    }

    // ── report ───────────────────────────────────────────────────────────

    private static int Report(CircuitLeague league, Options o)
    {
        var text = CircuitReport.Build(league, Names(league));
        var output = o.Get("out") ?? Path.Combine("docs", "arena", $"{league.Org.Id}_prospects.md");
        File.WriteAllText(output, text);
        Console.Error.WriteLine($"→ {output}");
        return 0;
    }

    // ── verify ───────────────────────────────────────────────────────────

    private static int Verify(CircuitLeague league, Options o)
    {
        var id = o.Get("bout") ?? throw new ArgumentException("--bout <試合の id> を渡してください。");
        var record = league.Ledger().FirstOrDefault(b => b.Id == id) ?? throw new ArgumentException($"試合 {id} は記録にありません。");
        var commit = CircuitLeague.Commit();
        if (record.Commit != commit)
        {
            Console.Error.WriteLine($"注意: 記録のコミット {record.Commit} と今のコミット {commit} が違います（物理・ボットが同じなら一致するはず）。");
        }

        if (record.Physics != Fighter.PhysicsVersion || record.Logic != Bots.BoxerBot.LogicVersion)
        {
            Console.Error.WriteLine($"記録の版（physics {record.Physics}・logic {record.Logic}）が今（{Fighter.PhysicsVersion}・{Bots.BoxerBot.LogicVersion}）と違います。記録のコミットで回してください。");
            return 2;
        }

        var spec = new BoutSpec(record.Id, record.Season, record.Event, record.Kind, record.Division, record.Seed, record.Rounds,
            record.Red, record.Blue, record.RedFile, record.BlueFile, record.RedHash, record.BlueHash);
        var again = league.Fight(spec, record.Commit, record.BreakSeconds);
        var same = again.Checksum == record.Checksum && again.Winner == record.Winner && again.Finish == record.Finish && again.Round == record.Round;
        Console.WriteLine($"{id}: 記録 {record.Checksum}（{record.WinnerId ?? "引き分け"} {record.Method} R{record.Round}）/ 再現 {again.Checksum}（{again.WinnerId ?? "引き分け"} {again.Method} R{again.Round}）→ {(same ? "一致" : "不一致")}");
        return same ? 0 : 1;
    }

    private static string Today() => DateTime.Now.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
}
