using System.Diagnostics;
using System.Globalization;
using MiniRing.Arena;
using MiniRing.Bots;

// ボット同士の対戦場。
//   league [--bots a,b,...] [--bouts N] [--seconds S] [--seed X] [--out file.md]   総当たりの順位表と技の内訳
//   tune --base NAME [--gens G] [--pop P] [--bouts N] [--seconds S] [--gauntlet a,b] [--seed X] [--log file.md] [--out style.json]
//                                                                                   スタイルの自動調整（PDCA を世代ごとに記録）
//   drill [--book f.json] [--bouts N] [--seconds S]                                防御ドリル: 防御技 × 拳の種類の効き目の表
//   drilltune [--defense a,b|all] [--book f.json] [--gens G] [--pop P] [--log f.md] [--out book.json]
//                                                                                   防御技ごとの最適化（ドリルで PDCA）
//   video [--red A] [--blue B] [--seed X] [--seconds S] [--width W --height H --fps F] [--out file.mp4]
//                                                                                   ボット同士の試合を字幕・打撃音つきの mp4 に（ffmpeg が必要）
// ボットは名前（legacy, boxer, outboxer, swarmer, counter, slugger, champion）か、スタイルの JSON ファイルのパス。
CultureInfo.DefaultThreadCurrentCulture = CultureInfo.InvariantCulture;
var argv = args.Length == 0 ? ["league"] : args;
var command = argv[0];
try
{
    return command switch
    {
        "league" => League(Options.Parse(argv[1..], "bots", "bouts", "seconds", "seed", "out")),
        "tune" => Tune(Options.Parse(argv[1..], "base", "gens", "pop", "bouts", "seconds", "gauntlet", "seed", "log", "out", "focus")),
        "drill" => Drill(Options.Parse(argv[1..], "book", "bouts", "seconds", "seed", "out")),
        "drilltune" => DrillTune(Options.Parse(argv[1..], "defense", "book", "gens", "pop", "bouts", "seconds", "seed", "log", "out")),
        "video" => Video(Options.Parse(argv[1..], "red", "blue", "seed", "seconds", "width", "height", "fps", "out", "break-sound", "hit-sound")),
        _ => Usage($"知らないコマンド: {command}"),
    };
}
catch (Exception e) when (e is ArgumentException or IOException or UnauthorizedAccessException or System.Text.Json.JsonException or FormatException or OverflowException)
{
    Console.Error.WriteLine($"error: {e.Message}");
    return 2;
}

static int League(Options o)
{
    var contenders = o.List("bots", [.. BotCatalog.Names]).Select(Load).ToList();
    var bouts = o.Int("bouts", 6, min: 1);
    var seconds = o.Float("seconds", 60f, min: 1f);
    if (contenders.Count < 2)
    {
        throw new ArgumentException("--bots には 2 人以上が必要です。");
    }

    var watch = Stopwatch.StartNew();
    var results = BotArena.RunAll(BotArena.RoundRobin(contenders, bouts, seconds, o.Int("seed", 1)));
    var rows = Standing.From(results);
    var text = $"### 総当たり（各組 {bouts} 試合 × {seconds:0} 秒、種 {o.Int("seed", 1)}）\n\n" +
        Report.Standings(rows) + "\n" + Report.Matrix(results) + "\n" + Report.Breakdown(rows);
    Console.WriteLine(text);
    Console.Error.WriteLine($"{results.Count} bouts in {watch.Elapsed.TotalSeconds:0.0}s");
    if (o.Get("out") is { } path)
    {
        File.WriteAllText(path, text);
    }

    return 0;
}

static int Tune(Options o)
{
    var start = LoadStyle(o.Get("base") ?? "boxer");
    var gauntlet = o.List("gauntlet", ["legacy", "boxer", "outboxer", "swarmer", "counter", "slugger"]).Select(Load).ToList();
    if (gauntlet.Count == 0)
    {
        throw new ArgumentException("--gauntlet が空です。");
    }

    var logPath = o.Get("log");
    void Log(string line)
    {
        Console.WriteLine(line);
        if (logPath is not null)
        {
            File.AppendAllText(logPath, line + "\n");
        }
    }

    // 採用のたびに書き出す（途中で止めても、そこまでの王者は残る）。
    var outPath = o.Get("out");
    void Save(BotStyle style)
    {
        if (outPath is not null)
        {
            File.WriteAllText(outPath, style.ToJson());
        }
    }

    var focus = Enum.Parse<TuneFocus>(o.Get("focus") ?? "all", ignoreCase: true);
    var tuner = new StyleTuner(gauntlet, o.Int("bouts", 4, min: 1), o.Float("seconds", 45f, min: 1f), o.Int("seed", 1), Log, Save, focus);
    var tuned = tuner.Run(start, o.Int("gens", 6, min: 0), o.Int("pop", 8, min: 1));
    Save(tuned);
    Console.WriteLine(tuned.ToJson());

    return 0;
}

static int Drill(Options o)
{
    var book = LoadBook(o.Get("book"));
    var tuner = new DefenseTuner(o.Int("bouts", 8, min: 1), o.Float("seconds", 20f, min: 1f), o.Int("seed", 1), Console.WriteLine);
    var (_, results) = tuner.Matrix(book, o.Int("seed", 1) + 500);
    var text = DefenseTuner.MatrixTable(results);
    Console.WriteLine(text);
    if (o.Get("out") is { } path)
    {
        File.WriteAllText(path, text);
    }

    return 0;
}

static int DrillTune(Options o)
{
    var book = LoadBook(o.Get("book"));
    var techniques = o.Get("defense") is { } list && list != "all"
        ? list.Split(',').Select(n => Enum.Parse<Defense>(n, ignoreCase: true)).ToList()
        : DefenseBook.Techniques.ToList();
    if (techniques.Contains(Defense.None))
    {
        throw new ArgumentException("--defense に None は使えません。");
    }

    var logPath = o.Get("log");
    void Log(string line)
    {
        Console.WriteLine(line);
        if (logPath is not null)
        {
            File.AppendAllText(logPath, line + "\n");
        }
    }

    var seed = o.Int("seed", 1);
    var tuner = new DefenseTuner(o.Int("bouts", 8, min: 1), o.Float("seconds", 20f, min: 1f), seed, Log);
    var before = tuner.Matrix(book, seed + 500);
    Log("## 最適化の前の表\n\n" + DefenseTuner.MatrixTable(before.Results));
    var tuned = tuner.Optimize(book, techniques, o.Int("gens", 6, min: 0), o.Int("pop", 8, min: 1));
    var after = tuner.Matrix(tuned, seed + 500);
    Log("## 最適化の後の表（守りを選ぶ重みに使う）\n\n" + DefenseTuner.MatrixTable(after.Results));
    var json = System.Text.Json.JsonSerializer.Serialize(after.Book, new System.Text.Json.JsonSerializerOptions { WriteIndented = true });
    if (o.Get("out") is { } outPath)
    {
        File.WriteAllText(outPath, json);
    }

    return 0;
}

static DefenseBook LoadBook(string? path) => path is null ? DefenseBook.Default
    : (System.Text.Json.JsonSerializer.Deserialize<DefenseBook>(File.ReadAllText(path)) ?? throw new System.Text.Json.JsonException("空の本です。")).Validated();

static int Video(Options o)
{
    var red = Load(o.Get("red") ?? "champion");
    var blue = Load(o.Get("blue") ?? "jabber");
    var output = Path.GetFullPath(o.Get("out") ?? $"miniring_{red.Name}_vs_{blue.Name}.mp4");
    Directory.CreateDirectory(Path.GetDirectoryName(output)!);
    var breakName = o.Get("break-sound") ?? MiniRing.Audio.VoxelBreakSound.DefaultStyle.ToString();
    if (!MiniRing.Audio.BreakSoundOption.TryName(breakName, out var breakStyle))
    {
        throw new ArgumentException($"--break-sound '{breakName}' はありません（{string.Join(", ", Enum.GetNames<MiniRing.Audio.BreakStyle>().Select(n => n.ToLowerInvariant()))}）。");
    }

    if (!MiniRing.Audio.HitSoundOption.TryValue(o.Get("hit-sound") ?? "off", out var hitSounds))
    {
        throw new ArgumentException("--hit-sound は on か off にしてください。");
    }

    new VideoMaker(o.Int("width", 1280, min: 160), o.Int("height", 720, min: 90), o.Int("fps", 30, min: 10), breakStyle, hitSounds)
        .Make(red, blue, o.Int("seed", 4), o.Float("seconds", 45f, min: 1f), output, Console.Out);
    return 0;
}

static Contender Load(string nameOrPath)
{
    if (!nameOrPath.EndsWith(".json", StringComparison.OrdinalIgnoreCase))
    {
        return Contender.Named(Check(nameOrPath));
    }

    // JSON に Name が無ければ既定の "boxer" になり、本物の boxer と 1 行に混ざる。名簿の名前と重なるならファイル名で呼ぶ。
    var style = LoadStyle(nameOrPath);
    return Contender.Of(BotCatalog.Names.Contains(style.Name) ? style with { Name = Path.GetFileNameWithoutExtension(nameOrPath) } : style);
}

static BotStyle LoadStyle(string nameOrPath)
    => nameOrPath.EndsWith(".json", StringComparison.OrdinalIgnoreCase) ? BotStyle.FromJson(File.ReadAllText(nameOrPath)) : BotCatalog.Style(nameOrPath);

static string Check(string name) => BotCatalog.Names.Contains(name) ? name : throw new ArgumentException($"ボット '{name}' はありません（{string.Join(", ", BotCatalog.Names)}）。");

static int Usage(string message)
{
    Console.Error.WriteLine(message);
    Console.Error.WriteLine("usage: miniring-arena league [--bots a,b] [--bouts N] [--seconds S] [--seed X] [--out f.md]");
    Console.Error.WriteLine("       miniring-arena tune --base NAME [--gens G] [--pop P] [--bouts N] [--seconds S] [--gauntlet a,b] [--log f.md] [--out f.json]");
    return 2;
}

/// <summary>
/// --name value 形式の引数。値が空白で区切られて複数来たら（run.cmd 経由では cmd がカンマで区切る）カンマでつなぐ。
/// 知らない名前は誤字として拒む。
/// </summary>
internal sealed record Options(IReadOnlyDictionary<string, string> Values)
{
    public static Options Parse(string[] args, params string[] known)
    {
        var values = new Dictionary<string, string>();
        string? current = null;
        foreach (var arg in args)
        {
            if (arg.StartsWith("--", StringComparison.Ordinal))
            {
                current = arg[2..];
                if (!known.Contains(current))
                {
                    throw new ArgumentException($"知らない引数 --{current}（使えるもの: {string.Join(", ", known.Select(k => "--" + k))}）。");
                }

                values[current] = "";
            }
            else if (current is null)
            {
                throw new ArgumentException($"引数 '{arg}' の前に --name がありません。");
            }
            else
            {
                values[current] = values[current].Length == 0 ? arg : values[current] + "," + arg;
            }
        }

        var empty = values.FirstOrDefault(p => p.Value.Length == 0);
        return empty.Key is null ? new Options(values) : throw new ArgumentException($"--{empty.Key} に値がありません。");
    }

    public string? Get(string name) => Values.GetValueOrDefault(name);

    public int Int(string name, int fallback, int min = int.MinValue)
    {
        var value = Get(name) is { } v ? int.Parse(v, CultureInfo.InvariantCulture) : fallback;
        return value >= min ? value : throw new ArgumentException($"--{name} は {min} 以上にしてください（{value}）。");
    }

    public float Float(string name, float fallback, float min = float.MinValue)
    {
        var value = Get(name) is { } v ? float.Parse(v, CultureInfo.InvariantCulture) : fallback;
        return float.IsFinite(value) && value >= min ? value : throw new ArgumentException($"--{name} は {min} 以上にしてください（{value}）。");
    }

    public IReadOnlyList<string> List(string name, IReadOnlyList<string> fallback)
        => Get(name) is { } v ? v.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries) : fallback;
}
