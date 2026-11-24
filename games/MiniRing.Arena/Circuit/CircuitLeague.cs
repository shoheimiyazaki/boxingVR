using System.Diagnostics;
using System.Globalization;
using System.Text.Json;
using System.Text.Json.Serialization;
using MiniRing.Characters;

namespace MiniRing.Arena.Circuit;

/// <summary>1 人のリーグでの状態。</summary>
internal sealed record LeagueEntry(string Division, string Country, string Status, int Since = 0, string? Reason = null)
{
    public const string Active = "active";
    public const string Retired = "retired";
    public const string Registered = "registered";
}

/// <summary>リーグの状態（<c>league/&lt;org&gt;/state.json</c>）: 済んだ興行・全員の現役 / 引退 / 登録・休んだ回数・挑戦の進み。</summary>
internal sealed record LeagueState
{
    public required string Org { get; init; }
    public int Season { get; init; } = 1;
    public int EventsDone { get; init; }
    public required Dictionary<string, LeagueEntry> Fighters { get; init; }
    public Dictionary<string, int> Byes { get; init; } = [];

    /// <summary>挑戦（登録の後）が済んだか。</summary>
    public bool ChallengesDone { get; init; }
}

/// <summary>1 試合の申し込み（カードの 1 行）。</summary>
internal sealed record BoutSpec(string Id, int Season, int Event, string Kind, string Division, int Seed, int Rounds,
    string Red, string Blue, string RedFile, string BlueFile, string RedHash, string BlueHash);

/// <summary>子のプロセスへ渡すカード。</summary>
internal sealed record BoutCard(string Org, string Commit, float BreakSeconds, IReadOnlyList<BoutSpec> Bouts);

/// <summary>
/// リーグの置き場所（<c>league/&lt;org&gt;/</c>）と、試合を回す口。試合は <see cref="TvScout.Run"/>（中継の下見と同じ試合・同じ歩の幅）。
/// 同じプロセスで試合を並列に回すと結果が変わる（<see cref="TvScout"/> の注記）ので、並列は別のプロセス（<c>circuit fight</c>）に分ける。
/// </summary>
internal sealed class CircuitLeague
{
    /// <summary>歩の幅の元（<see cref="OrgLeague"/> と同じ: 1 倍速・30 fps → 1/60 s）。</summary>
    public const float Speed = GameSpeed.Standard;

    public const int Fps = 30;

    internal static readonly JsonSerializerOptions Json = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = true,
        WriteIndented = true,
        Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
        UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow,
    };

    public CircuitLeague(string root, CircuitConfig config, Organization org)
    {
        Root = root;
        Config = config;
        Org = org;
    }

    public string Root { get; }
    public CircuitConfig Config { get; }
    public Organization Org { get; }

    public string ProspectsDir => Path.Combine(Root, "prospects");
    public string FilesDir => Path.Combine(Root, "files");
    public string LedgerPath => Path.Combine(Root, "bouts.jsonl");
    public string StatePath => Path.Combine(Root, "state.json");
    public string BaselinePath => Path.Combine(Root, "baseline.json");
    public string WorkDir => Path.Combine(Root, "work");

    /// <summary>新人のファイルのパス（リポジトリの根から、/ で区切る。記録に書く形）。</summary>
    public string ProspectPath(string id) => ToRecordPath(Path.Combine(ProspectsDir, $"{id}.json"));

    public static string ToRecordPath(string path) => Path.GetRelativePath(Directory.GetCurrentDirectory(), path).Replace('\\', '/');

    public LeagueState? LoadState() => File.Exists(StatePath)
        ? JsonSerializer.Deserialize<LeagueState>(File.ReadAllText(StatePath), Json) ?? throw new InvalidDataException($"{StatePath} が空です。")
        : null;

    public void SaveState(LeagueState state)
    {
        Directory.CreateDirectory(Root);
        File.WriteAllText(StatePath, JsonSerializer.Serialize(state, Json) + "\n");
    }

    public IReadOnlyList<BoutRecord> Ledger() => BoutLedger.Read(LedgerPath);

    /// <summary>ファイルの今の中身とハッシュ。名簿のように後で変わり得るファイルは、中身を <c>files/&lt;hash&gt;.json</c> にも残す（記録から再現できるように）。</summary>
    public (string Path, string Hash) Snapshot(string path, bool keepCopy)
    {
        var text = File.ReadAllText(path);
        var hash = ProspectFactory.HashOf(text);
        if (keepCopy)
        {
            Directory.CreateDirectory(FilesDir);
            var copy = Path.Combine(FilesDir, hash["sha256:".Length..] + ".json");
            if (!File.Exists(copy))
            {
                File.WriteAllText(copy, text);
            }
        }

        return (ToRecordPath(path), hash);
    }

    /// <summary>記録のファイルとハッシュから選手を読む（今のファイルが違えば <c>files/</c> の写しから）。どちらも無ければ例外。</summary>
    public FighterCharacter LoadFighter(string id, string path, string hash)
    {
        string? text = File.Exists(path) ? File.ReadAllText(path) : null;
        if (text is null || ProspectFactory.HashOf(text) != hash)
        {
            var copy = Path.Combine(FilesDir, hash["sha256:".Length..] + ".json");
            text = File.Exists(copy) ? File.ReadAllText(copy)
                : throw new InvalidDataException($"選手 {id} のファイル {path} は記録のハッシュ {hash} と違い、写し {copy} もありません。");
        }

        return CharacterRoster.FromFiles([($"{id}.json", text)], [.. CharacterRoster.All.Select(c => c.Id)]).Single();
    }

    /// <summary>試合の種（決まりの種・興行・階級・番号から）。正の int。</summary>
    public int SeedOf(int season, int evt, string division, int index)
        => (int)(CircuitRandom.Mix(Config.Seed, (ulong)season, (ulong)evt, CircuitRandom.Hash(division), (ulong)index) & 0x7FFFFFFF) | 1;

    /// <summary>1 試合を回して記録にする（このプロセスの中で 1 本）。</summary>
    public BoutRecord Fight(BoutSpec spec, string commit, float breakSeconds)
    {
        var a = LoadFighter(spec.Red, spec.RedFile, spec.RedHash);
        var b = LoadFighter(spec.Blue, spec.BlueFile, spec.BlueHash);
        var (red, blue, division) = OrgBouts.Enter(Org, a, b);
        if (division.Id != spec.Division)
        {
            throw new InvalidDataException($"試合 {spec.Id}: 計量の階級 {division.Id} がカードの {spec.Division} と違います。");
        }

        var r = TvScout.Run(RosterLeague.ContenderOf(red, blue.Id, null), RosterLeague.ContenderOf(blue, red.Id, null), red.Wear, blue.Wear, red.Weapon, blue.Weapon,
            spec.Rounds, [spec.Seed], Speed, Fps, red, blue, referee: true, breakSeconds, Org).Single();
        var rules = TvShowRules.Rules(spec.Rounds, breakSeconds, Org);
        return new BoutRecord
        {
            Id = spec.Id,
            Season = spec.Season,
            Event = spec.Event,
            Kind = spec.Kind,
            Org = Org.Id,
            Division = spec.Division,
            Commit = commit,
            Physics = Fighter.PhysicsVersion,
            Logic = Bots.BoxerBot.LogicVersion,
            Seed = spec.Seed,
            Rounds = spec.Rounds,
            RoundSeconds = rules.RoundSeconds,
            BreakSeconds = breakSeconds,
            Speed = Speed,
            Fps = Fps,
            Red = spec.Red,
            Blue = spec.Blue,
            RedFile = spec.RedFile,
            BlueFile = spec.BlueFile,
            RedHash = spec.RedHash,
            BlueHash = spec.BlueHash,
            Winner = r.Winner > 0 ? "red" : r.Winner < 0 ? "blue" : null,
            Method = r.Winner == 0 ? "DRAW" : r.Ko ? "KO" : "DEC",
            Finish = r.Finish,
            Round = r.Round,
            Time = MathF.Round(r.RoundTime, 2),
            RedDealt = MathF.Round(r.RedDealt, 1),
            BlueDealt = MathF.Round(r.BlueDealt, 1),
            RedDowns = r.RedDowns,
            BlueDowns = r.BlueDowns,
            Drama = MathF.Round(TvShowRules.Drama(r, spec.Rounds, rules.RoundSeconds, Org.Drama), 2),
            Death = r.Death,
            Checksum = "0x" + r.Checksum.ToString("x16", CultureInfo.InvariantCulture),
        };
    }

    /// <summary>
    /// カードの全試合を回す: <paramref name="workers"/> が 1 ならこのプロセスで順に、2 以上なら子のプロセス（<c>circuit fight</c>）に分けて並べて回す。
    /// 結果はカードの順。
    /// </summary>
    public IReadOnlyList<BoutRecord> Run(BoutCard card, int workers, string tag, TextWriter log)
    {
        if (card.Bouts.Count == 0)
        {
            return [];
        }

        var watch = Stopwatch.StartNew();
        IReadOnlyList<BoutRecord> results;
        if (workers <= 1)
        {
            results = [.. card.Bouts.Select(s =>
            {
                var r = Fight(s, card.Commit, card.BreakSeconds);
                log.WriteLine(Line(r));
                return r;
            })];
        }
        else
        {
            Directory.CreateDirectory(WorkDir);
            var cardPath = Path.Combine(WorkDir, $"{tag}.card.json");
            File.WriteAllText(cardPath, JsonSerializer.Serialize(card, Json));
            var n = Math.Min(workers, card.Bouts.Count);
            var procs = Enumerable.Range(0, n).Select(i =>
            {
                var output = Path.Combine(WorkDir, $"{tag}.{i}.jsonl");
                File.Delete(output);
                var psi = ChildProcess(["circuit", "fight", "--league", Root, "--card", cardPath, "--shard", i.ToString(CultureInfo.InvariantCulture),
                    "--of", n.ToString(CultureInfo.InvariantCulture), "--out", output]);
                return (Process: Process.Start(psi) ?? throw new InvalidOperationException("子のプロセスを起こせません。"), Output: output);
            }).ToList();
            foreach (var (p, _) in procs)
            {
                p.WaitForExit();
            }

            var failed = procs.Where(p => p.Process.ExitCode != 0).ToList();
            if (failed.Count > 0)
            {
                throw new InvalidOperationException($"子のプロセス {failed.Count} 本が失敗しました（終了コード {string.Join(",", failed.Select(f => f.Process.ExitCode))}）。");
            }

            var byId = procs.SelectMany(p => BoutLedger.Read(p.Output)).ToDictionary(r => r.Id, StringComparer.Ordinal);
            results = [.. card.Bouts.Select(s => byId.TryGetValue(s.Id, out var r) ? r : throw new InvalidOperationException($"試合 {s.Id} の結果がありません。"))];
            foreach (var r in results)
            {
                log.WriteLine(Line(r));
            }
        }

        log.WriteLine(string.Create(CultureInfo.InvariantCulture, $"  {card.Bouts.Count} 試合 {watch.Elapsed.TotalMinutes:0.0} 分（{workers} 本）"));
        return results;
    }

    /// <summary>子のプロセス（このプログラム自身）の起こし方。<c>dotnet x.dll</c> で動いているなら dll を先に渡す。</summary>
    private static ProcessStartInfo ChildProcess(IEnumerable<string> args)
    {
        var exe = Environment.ProcessPath ?? throw new InvalidOperationException("このプログラムのパスが分かりません。");
        var psi = new ProcessStartInfo(exe) { UseShellExecute = false, WorkingDirectory = Directory.GetCurrentDirectory() };
        if (Path.GetFileNameWithoutExtension(exe).Equals("dotnet", StringComparison.OrdinalIgnoreCase))
        {
            psi.ArgumentList.Add(typeof(CircuitLeague).Assembly.Location);
        }

        foreach (var a in args)
        {
            psi.ArgumentList.Add(a);
        }

        return psi;
    }

    public static string Line(BoutRecord r) => string.Create(CultureInfo.InvariantCulture,
        $"  {r.Id}: {r.Red} vs {r.Blue} → {r.WinnerId ?? "引き分け"} {r.Method} R{r.Round} {r.Time:0.0}s（{r.Finish}）drama {r.Drama:0.0}");

    /// <summary>今のコミット（作業ツリーに変更があれば +dirty）。</summary>
    public static string Commit()
    {
        static string Git(string args)
        {
            var psi = new ProcessStartInfo("git", args) { RedirectStandardOutput = true, UseShellExecute = false };
            using var p = Process.Start(psi) ?? throw new InvalidOperationException("git を起こせません。");
            var text = p.StandardOutput.ReadToEnd().Trim();
            p.WaitForExit();
            return p.ExitCode == 0 ? text : "";
        }

        var head = Git("rev-parse HEAD");
        if (head.Length == 0)
        {
            return "unknown";
        }

        // 試合に響くコードと資源だけを見る（リーグの記録・名簿のプロフィール・王座の記録は試合に響かず、選手のファイルはハッシュで守る）。
        return Git("status --porcelain --untracked-files=no -- src games :!games/MiniRing/Characters/roster :!games/MiniRing.Arena/Orgs/standings").Length > 0
            ? head + "+dirty" : head;
    }
}
