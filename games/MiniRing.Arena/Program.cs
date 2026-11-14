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
//   attacklab [--combos 1,2,1-2] [--telegraphs 0,0.1,0.2] [--ranges 0.9,1.05,1.2] [--bouts N] [--seconds S] [--log f.md]
//                                                                                   攻撃のドリル: 単発・連打 × 溜めの長さで、拳の速さ・体重の乗り・威力
//   karatelab [--only shapes,parry,kicks] [--cues 0,0.06] [--bouts N] [--seconds S] [--seed X] [--log f.md]
//                                                                                   空手のドリル: 手の形 × 急所、パリー（拳の曲がり）、5 種の蹴り
//   counterlab [--book f.json] [--defense a,b|evasions] [--bouts N] [--ranges 1.15,1.3] [--log f.md] [--out book.json]
//                                                                                   回避からのカウンターの研究（型 × 打ち始めの総当たり）
//   basics [--punch 2] [--slot Duck] [--style swarmer] [--guards 0.6,1.0,1.5] [--guard H] [--gens G] [--pop P] [--log f.md] [--out f.json]
//                                                                                   回避の基本動作の最適化（1 つの型だけを打たせて、外す形を磨く）
//   video [--red A] [--blue B] [--seed X] [--seconds S] [--width W --height H --fps F] [--out file.mp4]
//                                                                                   ボット同士の試合を字幕・打撃音つきの mp4 に（ffmpeg が必要）
//   weaponlab [--out file.mp4] [--table file.md] [--width W --height H --fps F] [--slow X] [--video on|off]
//                                                                                   武器の実験場: 鉄の剣と木の棍棒で斬る・打つ・突く・打ち合う（スロー動画と結果の表）
//   sparlab [--gens G] [--lambda L] [--bouts N] [--seconds S] [--seed X] [--start techniques.json] [--best style.json] [--out report.md]
//           [--final-bouts N] [--final-seconds S] [--video f.mp4]
//   swordlab [--gens G] [--lambda L] [--seed X] [--bouts N] [--seconds S] [--out report.md] [--best techniques.json] [--techniques kote,men]
//            [--fight only] [--video f.mp4]
//   swinglab [--gens G] [--lambda L] [--seed X] [--bouts N] [--seconds S] [--out report.md] [--best swings.json] [--video f.mp4]
//                                                                                   棍棒の振りの研究（型ごとに威力を進化戦略で上げ、要素を外して比べ、拳のボットと戦わせる）
//   visionlab [--only start,aim] [--bouts N] [--seconds S] [--seed X] [--log f.md]  目を潰されて始める・目を狙うスタイルの測定
//   faces [--out f.png] [--names a,b|f.json] [--columns N]                                全キャラの顔（正面と斜め）を並べた画像
//   voxexport [--out boxer.vox] [--from current|generated]                         ボクサーの中身を .vox へ（Vengi・MagicaVoxel で直す）
//   voxcheck --in boxer.vox [--install yes]                                        直した .vox を確かめ、生成との差を出す（yes でゲームに組み込む）
// ボットは名前（legacy, boxer, outboxer, swarmer, counter, slugger, champion）か、スタイルの JSON ファイルのパス。
CultureInfo.DefaultThreadCurrentCulture = CultureInfo.InvariantCulture;
var argv = args.Length == 0 ? ["league"] : args;
var command = argv[0];
try
{
    return command switch
    {
        "league" => League(Options.Parse(argv[1..], "bots", "bouts", "seconds", "seed", "out")),
        "staminalab" => StaminaLabCommand(Options.Parse(argv[1..], "variants", "bots", "bouts", "seconds", "seed", "tv", "out")),
        "learnlab" => LearnLabCommand(Options.Parse(argv[1..], "first", "second", "seeds", "seconds", "seed", "prior", "scale", "combos", "defender", "out")),
        "rangelab" => RangeLabCommand(Options.Parse(argv[1..], "variants", "bots", "bouts", "seconds", "seed", "weapons", "out")),
        "tune" => Tune(Options.Parse(argv[1..], "base", "gens", "pop", "bouts", "seconds", "gauntlet", "seed", "log", "out", "focus")),
        "drill" => Drill(Options.Parse(argv[1..], "book", "bouts", "seconds", "seed", "out")),
        "drilltune" => DrillTune(Options.Parse(argv[1..], "defense", "book", "gens", "pop", "bouts", "seconds", "seed", "log", "out", "ranges", "counters", "combos")),
        "attacklab" => AttackLabCommand(Options.Parse(argv[1..], "combos", "telegraphs", "ranges", "bouts", "seconds", "seed", "log")),
        "karatelab" => KarateLabCommand(Options.Parse(argv[1..], "only", "bouts", "seconds", "seed", "log", "cues")),
        "counterlab" => CounterLabCommand(Options.Parse(argv[1..], "book", "defense", "bouts", "seconds", "seed", "ranges", "log", "out", "combos")),
        "basics" => Basics(Options.Parse(argv[1..], "punch", "slot", "style", "guards", "widths", "reaches", "guard", "cue", "cues", "move", "telegraph", "predict", "gens", "pop", "bouts", "seconds", "seed", "ranges", "log", "out")),
        "aimlab" => AimLabCommand(Options.Parse(argv[1..], "library", "library-out", "tuning-out", "out", "quick", "maxshift")),
        "guardspar" => GuardSparCommand(Options.Parse(argv[1..], "trainee", "opponents", "gens", "lambda", "bouts", "seconds", "seed", "start", "best", "out")),
        "awaylab" => AwayLabCommand(Options.Parse(argv[1..], "trainee", "opponents", "gens", "lambda", "bouts", "seconds", "seed", "start", "best", "out")),
        "breakdemo" => BreakDemoCommand(Options.Parse(argv[1..], "out", "seconds")),
        "guardlab" => GuardLabCommand(Options.Parse(argv[1..], "seeds", "seconds", "out", "only", "variants", "video", "try")),
        "bladeprobe" => BladeProbeCommand(Options.Parse(argv[1..], "pairs", "bouts", "seconds", "out")),
        "versus" => VersusCommand(Options.Parse(argv[1..], "bouts", "seconds", "seed", "out", "defense", "video", "red", "blue", "hitaway")),
        "swordlib" => SwordLibCommand(Options.Parse(argv[1..], "out", "best", "bouts", "seconds", "seed", "fight", "video", "red", "blue")),
        "imitate" => ImitateCommand(Options.Parse(argv[1..], "out", "rate", "slow", "width", "height", "fps", "only", "measure", "wrist", "wrist-hz", "self", "lead")),
        "mocap" => MocapCommand(Options.Parse(argv[1..], "bvh", "out", "from", "to", "speed", "width", "height", "fps", "strikes")),
        "sparlab" => SparLabCommand(Options.Parse(argv[1..], "gens", "lambda", "bouts", "seconds", "seed", "start", "best", "out", "final-bouts", "final-seconds", "video", "width", "height", "fps")),
        "swordlab" => SwordLabCommand(Options.Parse(argv[1..], "gens", "lambda", "seed", "bouts", "seconds", "out", "best", "fight", "video", "width", "height", "fps", "slow", "techniques")),
        "swinglab" => SwingLabCommand(Options.Parse(argv[1..], "gens", "lambda", "seed", "bouts", "seconds", "out", "best", "fight", "video", "width", "height", "fps", "slow", "families")),
        "weaponlab" => WeaponLabCommand(Options.Parse(argv[1..], "out", "table", "width", "height", "fps", "slow", "video")),
        "intentvideo" => IntentVideo(Options.Parse(argv[1..], "script", "blue", "seed", "seconds", "width", "height", "out")),
        "video" => Video(Options.Parse(argv[1..], "red", "blue", "seed", "seconds", "width", "height", "fps", "out", "break-sound", "hit-sound", "facecam", "until-ko", "speed", "tv", "blind-red", "blind-blue", "red-wear", "blue-wear", "drill", "defense", "move", "guard", "cue", "range", "telegraph", "predict", "width-guard", "reach-guard", "style", "counters")),
        "visionlab" => VisionLabCommand(Options.Parse(argv[1..], "only", "bouts", "seconds", "seed", "log")),
        "faces" => Faces(Options.Parse(argv[1..], "out", "names", "columns")),
        "voxexport" => VoxExport(Options.Parse(argv[1..], "out", "from")),
        "voxcheck" => VoxCheck(Options.Parse(argv[1..], "in", "install")),
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

static int StaminaLabCommand(Options o)
{
    var variants = o.List("variants", ["off", "on", "hard"]);
    var bots = o.List("bots", [.. BotCatalog.Names.Where(n => n != BotCatalog.Legacy)]).Select(Check).ToList();
    var bouts = o.Int("bouts", 4, min: 1);
    // --tv on: テレビ中継の決まり（30 秒 × 5 ラウンド、間に 10 秒の休み）。スタミナの配分・休みでの戻りが効くのはこちら。
    var rules = o.Get("tv") == "on" ? MiniRing.MatchRules.Tv : null;
    var seconds = rules?.TotalSeconds ?? o.Float("seconds", 60f, min: 1f);
    var seed = o.Int("seed", 1);
    var watch = Stopwatch.StartNew();
    var results = StaminaLab.Run(variants, bots, bouts, seconds, seed, rules);
    var text = StaminaLab.Report(results, variants, bots, bouts, seconds, seed);
    Console.WriteLine(text);
    Console.Error.WriteLine($"{results.Count} bouts in {watch.Elapsed.TotalSeconds:0.0}s");
    if (o.Get("out") is { } path)
    {
        File.WriteAllText(path, text);
    }

    return 0;
}

// 拳の種類の名前（Straight / Hook / Uppercut / Body）。数字や知らない名前は引数の誤りとして知らせる。
static PunchKind Kind(string name) => Enum.TryParse<PunchKind>(name, ignoreCase: true, out var kind) && Enum.IsDefined(kind) && !int.TryParse(name, out _)
    ? kind
    : throw new ArgumentException($"拳の種類 '{name}' はありません（{string.Join(", ", Enum.GetNames<PunchKind>())}）。");

static int RangeLabCommand(Options o)
{
    if (o.Get("weapons") == "on")
    {
        // 拳（間合いの層なし・あり）と武器の相手（versus と同じ棍棒・バット・日本刀・ロングソード）。
        var weapons = VersusLab.Contenders("docs/arena/sword_library.json", "docs/arena/club_swings.json", "docs/arena/bat_swings.json",
            MiniRing.Bots.SwordDefense.Adopted, File.Exists("docs/arena/hit_and_away.json") ? AwayLab.Load("docs/arena/hit_and_away.json") : new MiniRing.Bots.HitAndAway(), AimDefaults.Tuning)
            .Where(c => c.Name != "拳").ToList();
        var report = RangeLab.RunWeapons(weapons, o.Int("bouts", 4, min: 1), o.Float("seconds", 45f, min: 1f), o.Int("seed", 1));
        Console.WriteLine(report);
        if (o.Get("out") is { } weaponOut)
        {
            File.WriteAllText(weaponOut, report);
        }

        return 0;
    }

    var variants = o.List("variants", ["off", "guard", "head", "guess", "exit", "all"]);
    foreach (var v in variants)
    {
        _ = RangeLab.Variant(v); // 打ち間違いを先に知らせる
    }

    var bots = o.List("bots", [.. BotCatalog.Names.Where(n => n != BotCatalog.Legacy)]).Select(Check).ToList();
    var watch = Stopwatch.StartNew();
    var text = RangeLab.Run(variants, bots, o.Int("bouts", 2, min: 1), o.Float("seconds", 60f, min: 1f), o.Int("seed", 1));
    Console.WriteLine(text);
    Console.Error.WriteLine($"done in {watch.Elapsed.TotalSeconds:0.0}s");
    if (o.Get("out") is { } path)
    {
        File.WriteAllText(path, text);
    }

    return 0;
}

static int LearnLabCommand(Options o)
{
    var first = Kind(o.Get("first") ?? "Straight");
    var second = Kind(o.Get("second") ?? "Hook");
    var seeds = o.Int("seeds", 6, min: 1);
    var seconds = o.Float("seconds", 120f, min: 2f);
    var seed = o.Int("seed", 1);
    var prior = o.Float("prior", 4f, min: 0.5f);
    var scale = o.Float("scale", 10f, min: 0.5f);
    var combos = o.Get("combos") == "on";
    // 受け手: スタイルの名前（自分からは攻めない形にして使う）か "drill"（防御ドリルの受け手。反応だけで守る）。
    var defenderName = o.Get("defender") ?? "counter";
    var defender = defenderName == "drill"
        ? DefenseDrill.Defender(DefenseBook.Default, defend: true)
        : BotCatalog.Style(Check(defenderName)) with { Attacks = false, ClinchRate = 0f, ThrowRate = 0f };
    var watch = Stopwatch.StartNew();
    var results = LearnLab.Run(defender, first, second, seeds, seconds, seed, prior, scale, combos);
    var text = $"受け手: {defenderName}\n\n" + LearnLab.Report(results, first, second, seeds, seconds, seed, prior, scale, combos);
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
    var techniques = o.Get("defense") switch
    {
        null or "all" => DefenseBook.Techniques.ToList(),
        "evasions" => DefenseBook.Evasions.ToList(),
        var list => list.Split(',').Select(n => Enum.Parse<Defense>(n, ignoreCase: true)).ToList(),
    };
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
    var ranges = o.List("ranges", [DefenseDrill.DefaultRange.ToString(CultureInfo.InvariantCulture)]).Select(r => float.Parse(r, CultureInfo.InvariantCulture)).ToList();
    var counters = bool.Parse(o.Get("counters") ?? "true");
    var combos = bool.Parse(o.Get("combos") ?? "false");
    var tuner = new DefenseTuner(o.Int("bouts", 8, min: 1), o.Float("seconds", 20f, min: 1f), seed, Log, ranges, counters, combos);
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

static int KarateLabCommand(Options o)
{
    var only = o.List("only", ["shapes", "parry", "kicks"]);
    var logPath = o.Get("log");
    void Log(string line)
    {
        Console.WriteLine(line);
        if (logPath is not null)
        {
            File.AppendAllText(logPath, line + "\n");
        }
    }

    new KarateLab(o.Int("bouts", 2, min: 1), o.Float("seconds", 20f, min: 1f), o.Int("seed", 1), Log,
        o.List("cues", ["0", "0.06"]).Select(c => float.Parse(c, CultureInfo.InvariantCulture)).ToList())
        .Run(only.Contains("shapes"), only.Contains("parry"), only.Contains("kicks"));
    return 0;
}

static int AttackLabCommand(Options o)
{
    var combos = o.List("combos", ["1", "2", "3", "4", "5", "6", "1b", "2b", "3b", "4b", "1-2", "1-2-3", "2-3", "3-2", "1-1-2", "6-3", "6-3-2", "3-4", "5-2"]);
    var telegraphs = o.List("telegraphs", ["0", "0.05", "0.1", "0.15", "0.2"]).Select(t => float.Parse(t, CultureInfo.InvariantCulture)).ToList();
    var logPath = o.Get("log");
    void Log(string line)
    {
        Console.WriteLine(line);
        if (logPath is not null)
        {
            File.AppendAllText(logPath, line + "\n");
        }
    }

    var ranges = o.List("ranges", ["0.9", "1.05", "1.2"]).Select(t => float.Parse(t, CultureInfo.InvariantCulture)).ToList();
    new AttackLab(o.Int("bouts", 3, min: 1), o.Float("seconds", 20f, min: 1f), o.Int("seed", 1), Log).Run(combos, telegraphs, ranges);
    return 0;
}

static int CounterLabCommand(Options o)
{
    var book = LoadBook(o.Get("book"));
    var evasions = o.Get("defense") is { } list && list != "evasions"
        ? list.Split(',').Select(n => Enum.Parse<Defense>(n, ignoreCase: true)).ToList()
        : DefenseBook.Evasions.ToList();
    if (evasions.Any(d => !DefenseBook.Evasions.Contains(d)))
    {
        throw new ArgumentException("counterlab は回避技だけを調べます（ブロックは守り終えてから打ち返す）。");
    }

    var ranges = o.List("ranges", ["1.15", "1.3"]).Select(r => float.Parse(r, CultureInfo.InvariantCulture)).ToList();
    var logPath = o.Get("log");
    void Log(string line)
    {
        Console.WriteLine(line);
        if (logPath is not null)
        {
            File.AppendAllText(logPath, line + "\n");
        }
    }

    var lab = new CounterLab(o.Int("bouts", 6, min: 1), o.Float("seconds", 20f, min: 1f), o.Int("seed", 1), ranges, Log, bool.Parse(o.Get("combos") ?? "false"));
    var studied = lab.Run(book, evasions);
    if (o.Get("out") is { } outPath)
    {
        File.WriteAllText(outPath, System.Text.Json.JsonSerializer.Serialize(studied, new System.Text.Json.JsonSerializerOptions { WriteIndented = true }));
    }

    return 0;
}

static int Basics(Options o)
{
    var punch = o.Get("punch") ?? "2";
    _ = Combo.Named(punch);
    var slot = Enum.Parse<Defense>(o.Get("slot") ?? "Duck", ignoreCase: true);
    if (!DefenseBook.Evasions.Contains(slot))
    {
        throw new ArgumentException("basics は回避技を磨きます（ブロックは除く）。");
    }

    var style = LoadStyle(o.Get("style") ?? "swarmer");
    var ranges = o.List("ranges", ["1.0", "1.15"]).Select(r => float.Parse(r, CultureInfo.InvariantCulture)).ToList();
    var guards = o.List("guards", ["0.6", "0.8", "1.0", "1.2", "1.5"]).Select(r => float.Parse(r, CultureInfo.InvariantCulture)).ToList();
    var seed = o.Int("seed", 1);
    var logPath = o.Get("log");
    void Log(string line)
    {
        Console.WriteLine(line);
        if (logPath is not null)
        {
            File.AppendAllText(logPath, line + "\n");
        }
    }

    var cueList = o.Get("cue") is { } c && c != "none" ? c.Split(',').Select(x => float.Parse(x, CultureInfo.InvariantCulture)).ToList() : null;
    var start = o.Get("move") is { } movePath ? LoadMove(movePath) : slot == Defense.Slip ? style.Defenses.Of(Defense.Slip) : style.Defenses.Of(slot);
    var lab = new BasicsLab(punch, slot, o.Int("bouts", 6, min: 1), o.Float("seconds", 20f, min: 1f), seed, ranges, Log, cueList, o.Float("telegraph", 0f, min: 0f), o.Float("predict", 0f, min: 0f));
    if (o.Get("cues") is not null)
    {
        var delays = o.List("cues", []).Select(d => float.Parse(d, CultureInfo.InvariantCulture)).ToList();
        var guardHeight = o.Get("guard") is { } gh ? float.Parse(gh, CultureInfo.InvariantCulture) : style.GuardHeight;
        Log($"## 合図の遅れと回避（相手の型 {punch}）\n");
        lab.CueSurvey("外へ外す形", start, guardHeight, delays);
        lab.CueSurvey($"{style.Name} の {slot}", style.Defenses.Of(slot), style.GuardHeight, delays);
        lab.CueSurvey($"{style.Name} の Slip", style.Defenses.Of(Defense.Slip), style.GuardHeight, delays);
        return 0;
    }

    Log($"## 構えの高さ（守らない受け手、相手の型 {punch}）\n");
    lab.GuardSurvey(guards,
        o.List("widths", ["1"]).Select(r => float.Parse(r, CultureInfo.InvariantCulture)).ToList(),
        o.List("reaches", ["1"]).Select(r => float.Parse(r, CultureInfo.InvariantCulture)).ToList());
    if (o.Int("gens", 15, min: 0) == 0)
    {
        return 0; // 構えだけを比べる
    }

    var guard = o.Get("guard") is { } g ? float.Parse(g, CultureInfo.InvariantCulture) : style.GuardHeight;
    Log(string.Create(CultureInfo.InvariantCulture, $"## 出発点（{style.Name} の構え {style.GuardHeight:0.0} と、磨く構え {guard:0.0}）\n"));
    Log(BasicsLab.Header);
    lab.Measure($"{style.Name} の {slot}（今）", style.Defenses.Of(slot), style.GuardHeight, seed);
    lab.Measure($"{style.Name} の Slip（今）", style.Defenses.Of(Defense.Slip), style.GuardHeight, seed);
    lab.Measure("外へ外す形（出発点）", start, guard, seed);
    Log("");
    var tuned = lab.Optimize(start, guard, o.Int("gens", 15, min: 1), o.Int("pop", 8, min: 1));
    Log("## 確認（まだ使っていない種）\n");
    Log(BasicsLab.Header);
    var confirmSeed = seed + 4_000_000;
    lab.Measure($"{style.Name} の {slot}（前）", style.Defenses.Of(slot), style.GuardHeight, confirmSeed);
    lab.Measure("磨いた形（後）", tuned, guard, confirmSeed);
    Log("");
    if (o.Get("out") is { } outPath)
    {
        File.WriteAllText(outPath, System.Text.Json.JsonSerializer.Serialize(tuned, new System.Text.Json.JsonSerializerOptions { WriteIndented = true }));
    }

    return 0;
}

static DefenseMove LoadMove(string path)
    => (System.Text.Json.JsonSerializer.Deserialize<DefenseMove>(File.ReadAllText(path)) ?? throw new System.Text.Json.JsonException("空の動きです。")).Validated();

static DefenseBook LoadBook(string? path) => path is null ? DefenseBook.Default
    : (System.Text.Json.JsonSerializer.Deserialize<DefenseBook>(File.ReadAllText(path)) ?? throw new System.Text.Json.JsonException("空の本です。")).Validated();

static int SwingLabCommand(Options o)
{
    if (o.Get("video") is { } video)
    {
        // 研究の出発点の振りと、見つけた振り（--best の JSON）を動画に。
        var found = SwingLab.Load(o.Get("best") ?? "docs/arena/club_swings.json");
        var list = found.Select(f => ($"{SwingLab.Name(f.Swing.Family)} — 出発点", MiniRing.Weapons.WeaponSwing.Default(f.Swing.Family), 0.38f))
            .Concat(found.Select(f => ($"{SwingLab.Name(f.Swing.Family)} — 研究で見つけた振り", f.Swing, f.Range))).ToList();
        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(video))!);
        new SwingVideo(o.Int("width", 1280, min: 160), o.Int("height", 720, min: 90), o.Int("fps", 30, min: 10), o.Float("slow", 4f, min: 1f)).Make(list, video, Console.Out);
        return 0;
    }

    var lab = new SwingLab(o.Int("gens", 10, min: 1), o.Int("lambda", 8, min: 1), o.Int("seed", 1));
    var bat = (o.Get("families") ?? "").Contains("bat", StringComparison.OrdinalIgnoreCase);
    var best = o.Get("best") ?? (bat ? "docs/arena/bat_swings.json" : "docs/arena/club_swings.json");
    if (o.Get("fight") == "only")
    {
        Console.WriteLine(lab.FightOnly(best, Console.Out, o.Int("bouts", 6, min: 1), o.Float("seconds", 60f, min: 1f)));
        return 0;
    }

    // --families bat / chop,side,thrust（既定: 片手の 3 型）。片手と両手は握り方が違うので、同じ研究（同じボット）には混ぜない。
    var families = (o.Get("families") ?? "chop,side,thrust").Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
        .Select(f => Enum.Parse<MiniRing.Weapons.SwingFamily>(f, ignoreCase: true)).ToList();
    if (families.Select(f => f == MiniRing.Weapons.SwingFamily.Bat).Distinct().Count() > 1)
    {
        throw new ArgumentException("片手の型と両手の型（bat）は同じ研究に混ぜられません。");
    }

    var report = lab.Run(best, Console.Out, o.Int("bouts", 6, min: 1), o.Float("seconds", 60f, min: 1f), families);
    Console.WriteLine(report);
    if (o.Get("out") is { } path)
    {
        File.WriteAllText(path, report);
    }

    return 0;
}

static int VersusCommand(Options o)
{
    // 武器ごとの練習試合（拳・棍棒・バット・日本刀・ロングソードの総当たり）。
    // --defense: off・legacy（はじめの守り）・step（足と頭で外す）・parry（受け流し＋足と頭）。既定は守りのドリルで選んだもの。
    var defense = o.Get("defense") switch
    {
        "off" => MiniRing.Bots.SwordDefense.None,
        "legacy" => new MiniRing.Bots.SwordDefense(),
        "step" => new MiniRing.Bots.SwordDefense(Mode: MiniRing.Bots.DefenseMode.Step),
        "parry" => MiniRing.Bots.SwordDefense.Parrying,
        null => DefenseDefaults.Value,
        var other => throw new ArgumentException($"--defense は off・legacy・step・parry のどれか（{other}）。"),
    };
    // --hitaway: off（打って離れない）・default（始めの値）・JSON のパス（awaylab で育てた値）。既定は育てた値があればそれ。
    var hitAway = (o.Get("hitaway") ?? (File.Exists("docs/arena/hit_and_away.json") ? "docs/arena/hit_and_away.json" : "default")) switch
    {
        "off" => null,
        "default" => new MiniRing.Bots.HitAndAway(),
        var file => AwayLab.Load(file),
    };
    var contenders = VersusLab.Contenders("docs/arena/sword_library.json", "docs/arena/club_swings.json", "docs/arena/bat_swings.json", defense, hitAway, AimDefaults.Tuning);
    if (o.Get("video") is { } video)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(video))!);
        var red = contenders.Single(c => c.Name == (o.Get("red") ?? "拳"));
        var blue = contenders.Single(c => c.Name == (o.Get("blue") ?? "日本刀"));
        new SparVideo(1280, 720, 30).MakeVersus(red, blue, o.Int("seed", 1), o.Float("seconds", 30f, min: 1f), video, Console.Out);
        return 0;
    }

    var report = VersusLab.Run(contenders, o.Int("bouts", 4, min: 2), o.Float("seconds", 30f, min: 1f), o.Int("seed", 1), Console.Out);
    Console.WriteLine(report);
    if (o.Get("out") is { } path)
    {
        File.WriteAllText(path, report);
    }

    return 0;
}

static int GuardLabCommand(Options o)
{
    // 守りのドリル: 日本刀の受け手へ、いろいろな武器の打ち手が 1 本ずつ打ち込み、守り方ごとに失ったものを比べる。
    // --only 名前の一部（打ち手を絞る）、--variants 0,4,7（守り方を番号で選ぶ）。
    var library = SwordLibraryLab.Load("docs/arena/sword_library.json");
    var attacks = GuardLab.Attacks(library, SwingLab.Load("docs/arena/club_swings.json"), SwingLab.Load("docs/arena/bat_swings.json"));
    if (o.Get("only") is { } only)
    {
        attacks = [.. attacks.Where(a => only.Split(',').Any(x => a.Name.Contains(x, StringComparison.Ordinal)))];
    }

    var variants = o.Get("variants") is { } picked
        ? [.. picked.Split(',').Select(s => GuardLab.Variants[int.Parse(s, System.Globalization.CultureInfo.InvariantCulture)])]
        : GuardLab.Variants;
    // --try "Parry:lead:deflect:forte;ParryAndStep:..."（受け流しの値を試す。値は腕の長さ・rad・刀の割合）。
    if (o.Get("try") is { } tries)
    {
        variants = [.. variants, .. tries.Split(';').Select(t =>
        {
            var x = t.Split(':');
            if (x.Length < 4)
            {
                throw new ArgumentException($"--try の 1 つは Mode:lead:deflect:forte[:drive[:horizon[:bind[:flat]]]]（{t}）。");
            }

            float F(int i) => float.Parse(x[i], System.Globalization.CultureInfo.InvariantCulture);
            return new GuardLab.Variant(t,
                new MiniRing.Bots.SwordDefense(Counter: false, Mode: Enum.Parse<MiniRing.Bots.DefenseMode>(x[0]), Lead: F(1), Deflect: F(2), Forte: F(3), Drive: x.Length > 4 ? F(4) : 0f, Horizon: x.Length > 5 ? F(5) : 0.25f, Bind: x.Length > 6 ? F(6) : 1f, Flat: x.Length > 7 && x[7] == "flat"));
        })];
    }

    if (o.Get("video") is { } video)
    {
        // 動画: 選んだ打ち手を、選んだ守り方（最後の 1 つ。--try があればそれ）で続けて撮る。
        new SparVideo(1280, 720, 30).MakeGuard(library, attacks, variants[^1], 1, o.Float("seconds", 4.5f), video, Console.Out);
        return 0;
    }

    var report = GuardLab.Run(library, attacks, variants, o.Int("seeds", 2), o.Float("seconds", 4.5f), Console.Out, out _);
    Console.WriteLine(report);
    if (o.Get("out") is { } path)
    {
        File.WriteAllText(path, "# 守りのドリル（guardlab）\n\n" + report);
    }

    return 0;
}

static int AimLabCommand(Options o)
{
    // 狙いのドリル: 技 × 狙う部位 × 的の構えで、狙った部位を打てるかを測り、狙いの付け方を選んで、技ごとの打てた割合を一覧へ書く。
    var library = o.Get("library") ?? "docs/arena/sword_library.json";
    // --maxshift 0.6,1,1.4: 前の回で選んだ付け方（aim_tuning.json）のまま、ずらせる上限だけを変えて比べる。
    List<MiniRing.Motion.AimTuning> candidates = o.Get("maxshift") is { } shifts
        ? [.. shifts.Split(',').Select(s => AimDefaults.Tuning with { MaxShift = float.Parse(s, System.Globalization.CultureInfo.InvariantCulture) })]
        : o.Get("quick") == "yes"
        ? [new()]
        : [.. from gain in AimGrid.Gains from lead in AimGrid.Leads from forward in AimGrid.Forwards
              select new MiniRing.Motion.AimTuning(gain, lead, 0.6f, forward)];
    var report = AimLab.Run(SwordLibraryLab.Load(library), candidates, o.Get("library-out") ?? library, o.Get("tuning-out") ?? "docs/arena/aim_tuning.json", Console.Out);
    Console.WriteLine(report);
    if (o.Get("out") is { } path)
    {
        File.WriteAllText(path, report);
    }

    return 0;
}

static int GuardSparCommand(Options o)
{
    // 守りを練習試合で育てる: 育てる剣士（既定は日本刀）の守りの値を変えながら相手たちと戦わせ、最後にロングソードでも比べ直す。
    const string Library = "docs/arena/sword_library.json";
    IReadOnlyList<Contender> With(MiniRing.Bots.SwordDefense d) =>
        VersusLab.Contenders(Library, "docs/arena/club_swings.json", "docs/arena/bat_swings.json", d, HitAwayDefaults.Value, AimDefaults.Tuning);
    var traineeName = o.Get("trainee") ?? "日本刀";
    if (traineeName is not ("日本刀" or "ロングソード"))
    {
        throw new ArgumentException($"--trainee は剣士（日本刀・ロングソード）だけ（守りの値は剣士だけが使う）: {traineeName}");
    }

    var checkName = traineeName == "日本刀" ? "ロングソード" : "日本刀";
    var opponents = With(DefenseDefaults.Value).Where(c => o.List("opponents", ["日本刀", "ロングソード", "バット", "棍棒", "拳"]).Contains(c.Name)).ToList();
    var start = o.Get("start") is { } from ? GuardSpar.Load(from) : DefenseDefaults.Value;
    var trainer = new MatchTrainer<MiniRing.Bots.SwordDefense>(d => With(d).Single(c => c.Name == traineeName), GuardSpar.Mutate, GuardSpar.Describe);
    var (best, report) = trainer.Run($"守りを練習試合で育てる（{traineeName}）", opponents, start, o.Int("gens", 5, min: 1), o.Int("lambda", 4, min: 1),
        o.Int("bouts", 4, min: 2), o.Float("seconds", 30f, min: 1f), o.Int("seed", 1), Console.Out,
        [(checkName, d => With(d).Single(c => c.Name == checkName))]);
    Console.WriteLine(report);
    GuardSpar.Save(o.Get("best") ?? "docs/arena/sword_defense.json", best);
    if (o.Get("out") is { } path)
    {
        File.WriteAllText(path, report);
    }

    return 0;
}

static int AwayLabCommand(Options o)
{
    // 打って離れる剣士を練習試合で育てる。
    var defense = DefenseDefaults.Value;
    IReadOnlyList<Contender> Contenders(MiniRing.Bots.HitAndAway? h) =>
        VersusLab.Contenders("docs/arena/sword_library.json", "docs/arena/club_swings.json", "docs/arena/bat_swings.json", defense, h, AimDefaults.Tuning);
    var trainee = o.Get("trainee") ?? "日本刀";
    var start = o.Get("start") is { } from ? AwayLab.Load(from) : new MiniRing.Bots.HitAndAway();
    var (best, report) = AwayLab.Run(Contenders, trainee, o.List("opponents", ["日本刀", "ロングソード", "バット", "棍棒", "拳"]), start,
        o.Int("gens", 6, min: 0), o.Int("lambda", 6, min: 1), o.Int("bouts", 2, min: 2), o.Float("seconds", 30f, min: 1f), o.Int("seed", 1), Console.Out);
    Console.WriteLine(report);
    AwayLab.Save(o.Get("best") ?? "docs/arena/hit_and_away.json", best);
    if (o.Get("out") is { } path)
    {
        File.WriteAllText(path, report);
    }

    return 0;
}

static int BladeProbeCommand(Options o)
{
    // 調査: 刃が相手の体にめり込んだまま何も起きない場面を数える。
    var contenders = VersusLab.Contenders("docs/arena/sword_library.json", "docs/arena/club_swings.json", "docs/arena/bat_swings.json", DefenseDefaults.Value);
    var report = BladeProbe.Run(contenders, [.. o.List("pairs", ["日本刀-日本刀", "ロングソード-日本刀", "日本刀-拳", "バット-日本刀"])], o.Int("bouts", 2, min: 1), o.Float("seconds", 20f, min: 1f), Console.Out);
    Console.WriteLine(report);
    if (o.Get("out") is { } path)
    {
        File.WriteAllText(path, report);
    }

    return 0;
}

static int SwordLibCommand(Options o)
{
    // 両手の剣の技の一覧（モーションキャプチャと型から）を作り、間合いを決め、剣士同士を戦わせる。
    var lab = new SwordLibraryLab(o.Int("seed", 1));
    var best = o.Get("best") ?? "docs/arena/sword_library.json";
    if (o.Get("video") is { } video)
    {
        // 剣士同士の 1 試合（--red / --blue: katana か longsword）。
        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(video))!);
        var red = Enum.Parse<MiniRing.Weapons.WeaponKind>(o.Get("red") ?? "Katana", ignoreCase: true);
        var blue = Enum.Parse<MiniRing.Weapons.WeaponKind>(o.Get("blue") ?? "Katana", ignoreCase: true);
        static string Ja(MiniRing.Weapons.WeaponKind k) => k == MiniRing.Weapons.WeaponKind.Katana ? "日本刀" : "ロングソード";
        new SparVideo(1280, 720, 30).MakeBout(SwordLibraryLab.Load(best), red, blue, o.Int("seed", 1), o.Float("seconds", 30f, min: 1f),
            $"剣士同士（赤 {Ja(red)} 対 青 {Ja(blue)}、両手持ち）", video, Console.Out);
        return 0;
    }

    var report = o.Get("fight") == "only"
        ? lab.FightOnly(best, Console.Out, o.Int("bouts", 4, min: 1), o.Float("seconds", 30f, min: 1f))
        : lab.Run(best, Console.Out, o.Int("bouts", 4, min: 1), o.Float("seconds", 30f, min: 1f));
    Console.WriteLine(report);
    if (o.Get("out") is { } path)
    {
        File.WriteAllText(path, report);
    }

    return 0;
}

static int ImitateCommand(Options o)
{
    // 日本刀（剣道のモーションキャプチャ）とロングソード（型のキー）の基本を、ラグドールがなぞる動画。
    var output = o.Get("out") ?? "videos/sword_basics.mp4";
    Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(output))!);
    var rate = o.Float("rate", 1.77f, min: 0.1f); // 体の大きさの比 0.32 の 1/√（同じ重力の下でつり合う速さ）
    var shots = SwordBasics.Shots(rate).Where(s => o.Get("only") is not { } only || s.Title.Contains(only, StringComparison.Ordinal)).ToList();
    // 調べもの: 手首の強さ・固さを変えて、なぞれ方がどう変わるか（--measure なら動画を作らずに数字だけ）。
    var tuning = ImitationVideo.SwordStance with
    {
        WristMaxTorque = ImitationVideo.SwordStance.WristMaxTorque * o.Float("wrist", 1f, min: 0.01f),
        WristServoHz = o.Float("wrist-hz", ImitationVideo.SwordStance.WristServoHz, min: 1f),
    };
    var video = new ImitationVideo(o.Int("width", 1280, min: 160), o.Int("height", 720, min: 90), o.Int("fps", 30, min: 10), o.Float("slow", 1f, min: 0.25f), tuning, hitSelf: o.Get("self") != "0", anticipation: o.Float("lead", 0f, min: 0f));
    if (o.Get("measure") is not null)
    {
        var results = new ImitationVideo.Tracking[shots.Count];
        if (o.Get("measure") == "trace")
        {
            results[0] = video.Run(shots[0], null, Console.Out);
            return 0;
        }

        Parallel.For(0, shots.Count, i => results[i] = video.Run(shots[i], null));
        for (var i = 0; i < shots.Count; i++)
        {
            Console.WriteLine(string.Create(System.Globalization.CultureInfo.InvariantCulture, $"{shots[i].Title}: tip mean {results[i].MeanTipError:0.00} max {results[i].MaxTipError:0.00} hand {results[i].MeanHandError:0.00}"));
        }

        return 0;
    }

    video.Make(shots, output, Console.Out);
    return 0;
}

static int MocapCommand(Options o)
{
    if (o.Get("strikes") is not null)
    {
        // 打ちの一覧: どの時刻に、どんな打ちが、どれだけ速く。
        foreach (var file in Directory.GetFiles("data/mocap/sfu_kendo", "*.bvh"))
        {
            var clip = MiniRing.Motion.BvhClip.Load(file);
            var motion = MiniRing.Motion.MotionRetarget.FromBvh(clip, Path.GetFileName(file), 0f, clip.Duration);
            Console.WriteLine($"== {Path.GetFileName(file)}");
            foreach (var s in MiniRing.Motion.StrikeFinder.Find(motion))
            {
                Console.WriteLine(string.Create(System.Globalization.CultureInfo.InvariantCulture,
                    $"{s.Kind,-8} {s.Start,6:0.00}-{s.End,6:0.00} peak {s.Peak,6:0.00} speed {s.PeakSpeed,5:0.0} arm/s  tip@peak ({s.TipAtPeak.X:0.00},{s.TipAtPeak.Y:0.00},{s.TipAtPeak.Z:0.00}) end ({s.TipAtEnd.X:0.00},{s.TipAtEnd.Y:0.00},{s.TipAtEnd.Z:0.00}) advance {s.Advance:0.00}"));
            }
        }

        return 0;
    }

    // モーションキャプチャ（BVH）を棒人間の動画にして、読めているか・何が入っているかを見る。
    var bvh = o.Get("bvh") ?? throw new ArgumentException("--bvh が要ります。");
    var output = o.Get("out") ?? "videos/mocap.mp4";
    Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(output))!);
    new MocapVideo(o.Int("width", 960, min: 160), o.Int("height", 540, min: 90), o.Int("fps", 30, min: 10))
        .Make(bvh, output, o.Float("from", 0f, min: 0f), o.Float("to", 1e6f, min: 0f), o.Float("speed", 1f, min: 0.05f), Console.Out);
    return 0;
}

static int SparLabCommand(Options o)
{
    var best = o.Get("best") ?? "docs/arena/sword_style.json";
    if (o.Get("video") is { } video)
    {
        // 磨いた流儀同士の稽古を 1 試合、ふつうの速さで。
        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(video))!);
        var style = SwordSparLab.Load(best);
        new SparVideo(o.Int("width", 1280, min: 160), o.Int("height", 720, min: 90), o.Int("fps", 30, min: 10))
            .Make(style, o.Int("seed", 1), o.Float("seconds", 30f, min: 1f), video, Console.Out);
        return 0;
    }

    var lab = new SwordSparLab(o.Int("gens", 10, min: 1), o.Int("lambda", 6, min: 1), o.Int("bouts", 4, min: 2), o.Float("seconds", 20f, min: 1f), o.Int("seed", 1));
    var report = lab.Run(o.Get("start") ?? "docs/arena/sword_techniques.json", best, Console.Out, o.Int("final-bouts", 6, min: 2), o.Float("final-seconds", 45f, min: 1f));
    Console.WriteLine(report);
    if (o.Get("out") is { } path)
    {
        File.WriteAllText(path, report);
    }

    return 0;
}

static int SwordLabCommand(Options o)
{
    var best = o.Get("best") ?? "docs/arena/sword_techniques.json";
    if (o.Get("video") is { } video)
    {
        // 基本の技と、磨いた技（--best の JSON）を、技ごとに並べて動画に。
        var found = File.Exists(best) ? SwordLab.Load(best) : [];
        var shots = MiniRing.Weapons.SwordTechnique.Basics.SelectMany(b =>
        {
            var list = new List<SwingVideo.Shot> { SwingVideo.Sword($"{b.Title} — 基本", b) };
            if (found.FirstOrDefault(t => t.Name == b.Name) is { } t)
            {
                list.Add(SwingVideo.Sword($"{b.Title} — 磨いた技", t));
            }

            return list;
        }).ToList();
        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(video))!);
        new SwingVideo(o.Int("width", 1280, min: 160), o.Int("height", 720, min: 90), o.Int("fps", 30, min: 10), o.Float("slow", 4f, min: 1f)).Make(shots, video, Console.Out);
        return 0;
    }

    var lab = new SwordLab(o.Int("gens", 10, min: 1), o.Int("lambda", 8, min: 1), o.Int("seed", 1));
    if (o.Get("fight") == "only")
    {
        Console.WriteLine(lab.FightOnly(best, Console.Out, o.Int("bouts", 6, min: 1), o.Float("seconds", 60f, min: 1f)));
        return 0;
    }

    var only = o.Get("techniques")?.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).ToHashSet(StringComparer.Ordinal);
    if (only is not null && only.Except(MiniRing.Weapons.SwordTechnique.Basics.Select(t => t.Name)).FirstOrDefault() is { } unknown)
    {
        throw new ArgumentException($"知らない技: {unknown}（使えるもの: {string.Join(", ", MiniRing.Weapons.SwordTechnique.Basics.Select(t => t.Name))}）");
    }

    var report = lab.Run(best, Console.Out, o.Int("bouts", 6, min: 1), o.Float("seconds", 60f, min: 1f), only);
    Console.WriteLine(report);
    if (o.Get("out") is { } path)
    {
        File.WriteAllText(path, report);
    }

    return 0;
}

static int BreakDemoCommand(Options o)
{
    var video = Path.GetFullPath(o.Get("out") ?? "videos/breakdemo.mp4");
    Directory.CreateDirectory(Path.GetDirectoryName(video)!);
    new SparVideo(1280, 720, 30).MakeBreakDemo(BreakDemo.Scenes(), o.Float("seconds", 6f, min: 2f), video, Console.Out);
    Console.WriteLine(video);
    return 0;
}

static int WeaponLabCommand(Options o)
{
    var video = (o.Get("video") ?? "on") == "off" ? null : Path.GetFullPath(o.Get("out") ?? "videos/weaponlab.mp4");
    if (video is not null)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(video)!);
    }

    var lab = new WeaponLab(o.Int("width", 1280, min: 160), o.Int("height", 720, min: 90), o.Int("fps", 30, min: 10), o.Float("slow", 4f, min: 1f));
    var table = lab.Run(video, Console.Out);
    Console.WriteLine(table);
    if (o.Get("table") is { } path)
    {
        File.WriteAllText(path, table);
    }

    return 0;
}

static int IntentVideo(Options o)
{
    var script = MiniRing.IntentScript.Load(o.Get("script") ?? "docs/arena/intent_demo.json");
    var blue = Load(o.Get("blue") ?? "champion");
    if (blue.Create(1) is not BoxerBot)
    {
        throw new ArgumentException("意図操作の動画の相手は記録できるボクシングボットを指定してください。");
    }

    var output = Path.GetFullPath(o.Get("out") ?? "docs/video/gen10_intent.mp4");
    Directory.CreateDirectory(Path.GetDirectoryName(output)!);
    new VideoMaker(o.Int("width", 1280, min: 160), o.Int("height", 720, min: 90), 30,
        faceCam: MiniRing.FaceCam.Default, speed: 1f)
        .Make(Contender.Named("intent"), blue, o.Int("seed", 31), o.Float("seconds", 14f, min: 1f), output, Console.Out, intentScript: script);
    var recording = MiniRing.Replay.ReplayFile.Read(Path.ChangeExtension(output, ".mrrep"));
    var result = MiniRing.Replay.ReplayPlayer.Verify(recording);
    Console.WriteLine($"意図操作の再生: {result.ChecksumsMatched}/{recording.Checksums.Count} チェックサム一致");
    return result.Reproduced ? 0 : 2;
}

static int Video(Options o)
{
    if (o.Get("drill") is { } punch)
    {
        return DrillVideo(o, punch);
    }

    var red = Load(o.Get("red") ?? "champion");
    var blue = Load(o.Get("blue") ?? "jabber");
    var output = Path.GetFullPath(o.Get("out") ?? $"miniring_{red.Name}_vs_{blue.Name}.mp4");
    Directory.CreateDirectory(Path.GetDirectoryName(output)!);
    var breakName = o.Get("break-sound") ?? MiniRing.Audio.VoxelBreakSound.DefaultStyle.ToString();
    if (!MiniRing.Audio.BreakSoundOption.TryName(breakName, out var breakStyle))
    {
        throw new ArgumentException($"--break-sound '{breakName}' はありません（{string.Join(", ", Enum.GetNames<MiniRing.Audio.BreakStyle>().Select(n => n.ToLowerInvariant()))}）。");
    }

    if (!MiniRing.Audio.HitSoundOption.TryValue(o.Get("hit-sound") ?? "on", out var hitSounds))
    {
        throw new ArgumentException("--hit-sound は on か off にしてください。");
    }

    var faceCam = (o.Get("facecam") ?? "off") switch
    {
        "on" => MiniRing.FaceCam.Default,
        "off" => null,
        _ => throw new ArgumentException("--facecam は on か off にしてください。"),
    };
    var speed = o.Get("speed") is { } speedText
        ? MiniRing.GameSpeed.Parse(speedText) ?? throw new ArgumentException("--speed は 0.5 / 0.75 / 1 のどれかです。")
        : MiniRing.GameSpeed.Standard;
    var tv = (o.Get("tv") ?? "off") switch
    {
        "on" => true,
        "off" => false,
        _ => throw new ArgumentException("--tv は on か off にしてください。"),
    };
    new VideoMaker(o.Int("width", 1280, min: 160), o.Int("height", 720, min: 90), o.Int("fps", 30, min: 10), breakStyle, hitSounds, faceCam, speed, tv)
        .Make(red, blue, o.Int("seed", 4), o.Float("seconds", 45f, min: 1f), output, Console.Out, untilKo: (o.Get("until-ko") ?? "no") switch { "yes" => true, "no" => false, _ => throw new ArgumentException("--until-ko は yes か no にしてください。") },
            redWear: MiniRing.Weapons.HandWears.Parse(o.Get("red-wear") ?? "glove"), blueWear: MiniRing.Weapons.HandWears.Parse(o.Get("blue-wear") ?? "glove"),
            setup: game =>
            {
                BlindStart(game.Player, o.Get("blind-red"));
                BlindStart(game.Cpu, o.Get("blind-blue"));
            });
    return 0;
}

// 基本動作のドリルを動画に: 青（攻め手）は 1 つの型だけを打ち、赤（受け手）は 1 つの回避で外す。
// --move に basics の --out（動きの JSON）、--cue に合図の遅れ（none で受け手自身の目と反応）。
// --telegraph に攻め手の打つ前の溜め [s]、--predict に受け手の予測の度合い（--cue none と合わせて、読んで避ける様子を見る）、
// --width-guard / --reach-guard に受け手の構えの開きと出し。
// --style に受け手のスタイル（その守りの技の動き・返しと構えを使う）、--counters true で回避から打ち返す。
// --blind-red / --blind-blue: 始めから目を潰しておく（left / right / both。本人の左 = +X）。
static void BlindStart(MiniRing.Fighter fighter, string? which)
{
    switch (which)
    {
        case null or "none":
            return;
        case "left" or "both":
            fighter.Voxels!.DestroyEye(left: true);
            break;
    }

    if (which is "right" or "both")
    {
        fighter.Voxels!.DestroyEye(left: false);
    }
    else if (which is not ("left" or "both"))
    {
        throw new ArgumentException("--blind-red / --blind-blue は none / left / right / both のどれかです。");
    }
}

static int DrillVideo(Options o, string punch)
{
    var slot = Enum.Parse<Defense>(o.Get("defense") ?? "Duck", ignoreCase: true);
    if (!DefenseBook.Evasions.Contains(slot))
    {
        throw new ArgumentException("ドリルの動画は回避技を見せます（ブロックは除く）。");
    }

    // --style を渡すと、そのスタイルの守りの技（動き・返し）と構えを使う。渡さなければ、外へ外す形（--move で差し替え）。
    var style = o.Get("style") is { } styleName ? LoadStyle(styleName) : null;
    var move = o.Get("move") is { } path ? LoadMove(path) : style?.Defenses.Of(slot) ?? BasicsLab.SlipOutside;
    var book = (style?.Defenses ?? DefenseBook.Default).With(slot, move);
    var range = o.Float("range", DefenseDrill.DefaultRange, min: 0.8f);
    float? cue = o.Get("cue") is { } c && c != "none" ? float.Parse(c, CultureInfo.InvariantCulture) : 0.04f;
    var attacker = DefenseDrill.PunchAttacker(punch, range, telegraph: o.Float("telegraph", 0f, min: 0f));
    var defender = DefenseDrill.Defender(book, defend: true, range, counters: bool.Parse(o.Get("counters") ?? "false")) with
    {
        Name = style?.Name ?? "slip",
        GuardHeight = o.Float("guard", style?.GuardHeight ?? 0.8f, min: 0f),
        GuardWidth = o.Float("width-guard", style?.GuardWidth ?? 1f, min: 0.2f),
        GuardReach = o.Float("reach-guard", style?.GuardReach ?? 1f, min: 0.3f),
        Prediction = o.Float("predict", 0f, min: 0f),
    };
    defender = (cue is null ? defender : defender with { DefenseRate = 0f }).Validated();
    var attackers = new List<BoxerBot>();
    var defenders = new List<BoxerBot>();
    var cues = cue is { } d ? DefenseDrill.DrillCue.For(punch, attacker, d) : null;
    var red = new Contender(defender.Name, s => Keep(defenders, new BoxerBot(defender, s, slot)));
    var blue = new Contender(attacker.Name, s => Keep(attackers, new BoxerBot(attacker, s)));
    var output = Path.GetFullPath(o.Get("out") ?? $"miniring_drill_{punch}_{slot}.mp4");
    Directory.CreateDirectory(Path.GetDirectoryName(output)!);
    new VideoMaker(o.Int("width", 1280, min: 160), o.Int("height", 720, min: 90), o.Int("fps", 30, min: 10))
        .Make(red, blue, o.Int("seed", 4), o.Float("seconds", 20f, min: 1f), output, Console.Out,
            t => { if (defenders.Count > 0) { cues?.Update(t, attackers, defenders[^1]); } });
    return 0;

    static BoxerBot Keep(List<BoxerBot> list, BoxerBot bot)
    {
        list.Add(bot);
        return bot;
    }
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

static int VisionLabCommand(Options o)
{
    var only = o.List("only", ["start", "aim"]);
    var logPath = o.Get("log");
    void Log(string line)
    {
        Console.WriteLine(line);
        if (logPath is not null)
        {
            File.AppendAllText(logPath, line + "\n");
        }
    }

    new VisionLab(o.Int("bouts", 4, min: 1), o.Float("seconds", 30f, min: 1f), o.Int("seed", 1), Log)
        .Run(only.Contains("start"), only.Contains("aim"));
    return 0;
}

static int Faces(Options o)
{
    var names = o.List("names", [.. BotCatalog.Names]).Select(Load).ToList();
    return FaceSheet.Run(o.Get("out") ?? "docs/images/gen13_faces.png", names, o.Int("columns", 3, min: 1));
}

static int VoxExport(Options o)
{
    var from = o.Get("from") ?? "current";
    return from is "current" or "generated"
        ? VoxelTool.Export(o.Get("out") ?? "boxer.vox", generated: from == "generated")
        : throw new ArgumentException($"--from は current か generated です（{from}）。");
}

static int VoxCheck(Options o)
{
    var input = o.Get("in") ?? throw new ArgumentException("--in に直した .vox を渡してください。");
    var install = o.Get("install") ?? "no";
    return install is "yes" or "no" ? VoxelTool.Check(input, install == "yes") : throw new ArgumentException($"--install は yes か no です（{install}）。");
}

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

/// <summary>狙いのドリルで比べる、狙いの付け方の格子。</summary>
internal static class AimGrid
{
    public static readonly float[] Gains = [0.6f, 1f];
    public static readonly float[] Leads = [0.4f, 0.7f];
    public static readonly float[] Forwards = [0.5f, 1f];
}

/// <summary>狙いのドリル（aimlab）で選んだ狙いの付け方（まだ無ければ既定）。</summary>
internal static class AimDefaults
{
    private static readonly Lazy<MiniRing.Motion.AimTuning> Loaded = new(() => File.Exists("docs/arena/aim_tuning.json") ? AimLab.Load("docs/arena/aim_tuning.json") : new());

    public static MiniRing.Motion.AimTuning Tuning => Loaded.Value;
}

/// <summary>打って離れる剣士の値（awaylab で育てた docs/arena/hit_and_away.json があればそれ、無ければ始めの値）。</summary>
internal static class HitAwayDefaults
{
    private static readonly Lazy<MiniRing.Bots.HitAndAway> Loaded = new(() =>
        File.Exists("docs/arena/hit_and_away.json") ? AwayLab.Load("docs/arena/hit_and_away.json") : new MiniRing.Bots.HitAndAway());

    public static MiniRing.Bots.HitAndAway Value => Loaded.Value;
}

/// <summary>
/// 剣士の守り（guardspar で練習試合から育てて採った docs/arena/sword_defense.json があればそれ、無ければ守りのドリルで選んだ
/// <see cref="MiniRing.Bots.SwordDefense.Adopted"/>）。
/// </summary>
internal static class DefenseDefaults
{
    private static readonly Lazy<MiniRing.Bots.SwordDefense> Loaded = new(() =>
        File.Exists("docs/arena/sword_defense.json") ? GuardSpar.Load("docs/arena/sword_defense.json") : MiniRing.Bots.SwordDefense.Adopted);

    public static MiniRing.Bots.SwordDefense Value => Loaded.Value;
}
