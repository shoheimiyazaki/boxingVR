using System.Diagnostics;
using System.Globalization;
using MiniRing.Arena;
using MiniRing.Bots;

// ボット同士の対戦場。使い方（コマンドと引数）は Usage.txt にある: `miniring-arena help [コマンド]`。
// コマンドを足したら、下の switch と Usage.txt の両方に足す（ArenaUsageTests が確かめる）。
CultureInfo.DefaultThreadCurrentCulture = CultureInfo.InvariantCulture;
var argv = args.Length == 0 ? ["league"] : args;
var command = argv[0];
try
{
    return command switch
    {
        "help" or "--help" or "-h" => Help(argv.Length > 1 ? argv[1] : null),
        "league" => League(Options.Parse(argv[1..], "bots", "bouts", "seconds", "seed", "out", "hz", "passes", "sample", "referee")),
        "handlelab" => HandleLabCommand(Options.Parse(argv[1..], "pairs", "fist", "bouts", "seconds", "seed", "referee", "parallel", "out", "style-katana", "style-longsword", "style-club", "style-staff", "style-swordshield", "style-bat", "title")),
        "handlespar" => HandleSparCommand(Options.Parse(argv[1..], "trainee", "opponents", "held-out", "gens", "lambda", "bouts", "seconds", "seed", "referee", "parallel", "start", "best", "out")),
        "maailab" => MaaiLabCommand(Options.Parse(argv[1..], "chars", "focus", "bouts", "seconds", "seed", "referee", "parallel", "out", "json", "csv", "ranges")),
        "rosterleague" => RosterLeagueCommand(Options.Parse(argv[1..], "chars", "bouts", "seconds", "seed", "referee", "out", "parallel", "set", "ab", "layered", "plain", "wear", "matrix", "gameplan")),
        "staminalab" => StaminaLabCommand(Options.Parse(argv[1..], "variants", "bots", "bouts", "seconds", "seed", "tv", "out")),
        "learnlab" => LearnLabCommand(Options.Parse(argv[1..], "first", "second", "seeds", "seconds", "seed", "prior", "scale", "combos", "defender", "out")),
        "rangelab" => RangeLabCommand(Options.Parse(argv[1..], "variants", "bots", "bouts", "seconds", "seed", "weapons", "out")),
        "tune" => Tune(Options.Parse(argv[1..], "base", "gens", "pop", "bouts", "seconds", "gauntlet", "seed", "log", "out", "focus")),
        "drill" => Drill(Options.Parse(argv[1..], "book", "bouts", "seconds", "seed", "out")),
        "drilltune" => DrillTune(Options.Parse(argv[1..], "defense", "book", "gens", "pop", "bouts", "seconds", "seed", "log", "out", "ranges", "counters", "combos")),
        "finishlab" => FinishLabCommand(Options.Parse(argv[1..], "attackers", "victims", "levels", "bouts", "seconds", "set", "log", "aim", "victim-set")),
        "attacklab" => AttackLabCommand(Options.Parse(argv[1..], "combos", "telegraphs", "ranges", "bouts", "seconds", "seed", "log")),
        "karatelab" => KarateLabCommand(Options.Parse(argv[1..], "only", "bouts", "seconds", "seed", "log", "cues", "video", "handsheet")),
        "groundlab" => GroundLabCommand(Options.Parse(argv[1..], "style", "moves", "bouts", "seconds", "seed", "log", "video", "practice", "candidates", "sigma", "validate")),
        "kickbalance" => KickBalanceCommand(Options.Parse(argv[1..], "style", "kicks", "teachings", "bouts", "seconds", "seed", "confirm", "log", "video")),
        "stick" => StickCommand(Options.Parse(StickSwingLab.WithYes(argv[1..]), "style", "kicks", "teachings", "png", "design", "candidates", "seed", "log", "approach", "ground", "moves", "path", "out", "forms", "detail", "swing", "planes", "samples", "weights", "video", "polish", "iterations", "character", "limits", "bvh", "bvh-scale")),
        "kicklab" => KickLabCommand(Options.Parse(argv[1..], "body", "kick", "distances", "seeds", "confirm-seeds", "shin", "overshoot", "side", "log")),
        "kickvideo" => KickVideoCommand(Options.Parse(argv[1..], "body", "kick", "distances", "side", "slow", "width", "height", "fps", "out")),
        "kickmocap" => KickMocapCommand(Options.Parse(argv[1..], "dir", "export", "log", "video", "doll", "durations", "distances", "export-mid", "body")),
        "kickshin" => KickShinCommand(Options.Parse(argv[1..], "body", "kick", "standoff", "seconds", "diag", "log", "heights", "practice", "gens", "lambda", "parallel", "json", "seed", "video", "victim", "dir")),
        "kneelab" => KneeLabCommand(Options.Parse(argv[1..], "stage", "bodies", "body", "distances", "distance", "confirm", "azimuths", "elevations", "twists", "sides", "log", "impact-az", "impact-el", "impact-twist", "foot", "gens", "lambda", "video")),
        "practicelab" => PracticeLabCommand(Options.Parse(argv[1..], "only", "candidates", "bouts", "seconds", "seed", "validate", "sigma", "log")),
        "seoilab" => SeoiLabCommand(Options.Parse(argv[1..], "candidates", "bouts", "seconds", "seed", "validate", "log", "compare", "reference", "victims")),
        "swaylab" => SwayLabCommand(Options.Parse(argv[1..], "hz", "ki", "cycles", "push", "bouts", "seconds", "seed", "log")),
        "balancelab" => BalanceLabCommand(Options.Parse(argv[1..], "only", "seed", "log")),
        "throwlab" => ThrowLabCommand(Options.Parse(argv[1..], "only", "victims", "bouts", "seconds", "seed", "log", "trace", "video", "speed", "width", "height")),
        "counterlab" => CounterLabCommand(Options.Parse(argv[1..], "book", "defense", "bouts", "seconds", "seed", "ranges", "log", "out", "combos")),
        "basics" => Basics(Options.Parse(argv[1..], "punch", "slot", "style", "guards", "widths", "reaches", "guard", "cue", "cues", "move", "telegraph", "predict", "gens", "pop", "bouts", "seconds", "seed", "ranges", "log", "out")),
        "formlab" => FormLabCommand(Options.Parse(argv[1..], "punch", "slot", "style", "move", "set", "cue", "bouts", "seconds", "seed", "log")),
        "aimlab" => AimLabCommand(Options.Parse(argv[1..], "library", "library-out", "tuning-out", "out", "quick", "maxshift", "forms", "only")),
        "cmatune" => CmaTuneCommand(Options.Parse(argv[1..], "target", "defenses", "gens", "lambda", "sigma", "samples", "seconds", "seed", "resume", "out", "json", "confirm-n", "match-n", "match-seconds", "parallel")),
        "defconfirm" => DefConfirm.Run(Options.Parse(argv[1..], "defense", "book", "seed", "seconds", "confirm-n", "export-champion")),
        "noiselab" => NoiseLabCommand(Options.Parse(argv[1..], "only", "match-n", "drill-n", "match-seconds", "drill-seconds", "match-ref", "drill-ref", "seed", "parallel", "out")),
        "guardspar" => GuardSparCommand(Options.Parse(argv[1..], "trainee", "opponents", "gens", "lambda", "bouts", "seconds", "seed", "start", "best", "out")),
        "awaylab" => AwayLabCommand(Options.Parse(argv[1..], "trainee", "opponents", "gens", "lambda", "bouts", "seconds", "seed", "start", "best", "out")),
        "breakdemo" => BreakDemoCommand(Options.Parse(argv[1..], "out", "seconds", "only", "fps")),
        "guardlab" => GuardLabCommand(Options.Parse(argv[1..], "seeds", "seed", "parallel", "seconds", "out", "only", "variants", "video", "try", "defender", "forms", "techniques", "compare")),
        "selfcuts" => SelfCutCommand(Options.Parse(argv[1..], "pairs", "seeds", "seconds", "out", "hitaway", "passes", "clearance", "margin", "tilt", "pacing", "slack", "angle")),
        "selfpath" => SelfPathCommand(Options.Parse(argv[1..], "margin", "from", "tilt", "out", "forms")),
        "jointlab" => JointLabCommand(Options.Parse(argv[1..], "joints", "offsets", "speeds", "bend", "parallel", "out", "video", "joint", "dir", "offset", "speed", "axis", "bridges", "slow", "dims")),
        "necklab" => NeckLabCommand(Options.Parse(argv[1..], "mode", "heights", "offsets", "arms", "speeds", "speed", "dir", "bridges", "tilt", "out", "video", "height", "side", "offset", "slow")),
        "tunnel" => TunnelCommand(Options.Parse(argv[1..], "pairs", "bouts", "seconds", "out", "passes", "hz", "sample")),
        "bladeprobe" => BladeProbeCommand(Options.Parse(argv[1..], "pairs", "bouts", "seconds", "out")),
        "versus" => VersusCommand(Options.Parse(argv[1..], "bouts", "seconds", "seed", "out", "defense", "video", "red", "blue", "hitaway", "clearance", "passes", "hz", "sample", "margin", "tilt", "pacing", "slack", "angle")),
        "swordlib" => SwordLibCommand(Options.Parse(argv[1..], "out", "best", "bouts", "seconds", "seed", "fight", "video", "red", "blue")),
        "imitate" => ImitateCommand(Options.Parse(argv[1..], "out", "rate", "slow", "width", "height", "fps", "only", "measure", "wrist", "wrist-hz", "self", "lead", "weapon", "vrmotion")),
        "vrmotion-synth" => VrMotionSynthCommand(Options.Parse(argv[1..], "out")),
        "mocap" => MocapCommand(Options.Parse(argv[1..], "bvh", "out", "from", "to", "speed", "width", "height", "fps", "strikes")),
        "sparlab" => SparLabCommand(Options.Parse(argv[1..], "gens", "lambda", "bouts", "seconds", "seed", "start", "best", "out", "final-bouts", "final-seconds", "video", "width", "height", "fps")),
        "swordlab" => SwordLabCommand(Options.Parse(argv[1..], "gens", "lambda", "seed", "bouts", "seconds", "out", "best", "fight", "video", "width", "height", "fps", "slow", "techniques")),
        "swinglab" => SwingLabCommand(Options.Parse(argv[1..], "gens", "lambda", "seed", "bouts", "seconds", "out", "best", "fight", "video", "width", "height", "fps", "slow", "families")),
        "weaponlab" => WeaponLabCommand(Options.Parse(argv[1..], "out", "table", "width", "height", "fps", "slow", "video")),
        "intentvideo" => IntentVideo(Options.Parse(argv[1..], "script", "blue", "seed", "seconds", "width", "height", "out")),
        "version" => Print($"versions physics {MiniRing.Fighter.PhysicsVersion} logic {MiniRing.Bots.BoxerBot.LogicVersion}"),
        "tvdraft" => TvDraftCommand(Options.Parse(argv[1..], "events", "out", "org")),
        "tvmerge" => TvMergeCommand(Options.Parse(argv[1..], "draft", "extras", "out")),
        "tvlint" => TvLintCommand(Options.Parse(argv[1..], "script", "events", "manifest", "org")),
        "tvdigest" => TvDigestCommand(Options.Parse(argv[1..], "events", "out", "org")),
        "tvscout" => TvScoutCommand(Options.Parse(argv[1..], "red", "blue", "red-wear", "blue-wear", "red-weapon", "blue-weapon", "rounds", "seeds", "speed", "fps", "red-char", "blue-char", "referee", "break", "org")),
        "video" => Video(Options.Parse(argv[1..], "red", "blue", "seed", "seconds", "width", "height", "fps", "out", "break-sound", "hit-sound", "facecam", "until-ko", "speed", "tv", "blind-red", "blind-blue", "red-wear", "blue-wear", "drill", "defense", "move", "guard", "cue", "range", "telegraph", "predict", "width-guard", "reach-guard", "style", "attacker-style", "counters", "red-weapon", "blue-weapon", "rounds", "event", "bout", "red-char", "blue-char", "referee", "break", "set", "render", "live", "voices", "encoder", "bitrate", "wait", "delay", "program", "org", "belt")),
        "visionlab" => VisionLabCommand(Options.Parse(argv[1..], "only", "bouts", "seconds", "seed", "log")),
        "faces" => Faces(Options.Parse(argv[1..], "out", "names", "columns")),
        "portraits" => Portraits(Options.Parse(argv[1..], "out", "scale")),
        "voxexport" => VoxExport(Options.Parse(argv[1..], "out", "from")),
        "voxcheck" => VoxCheck(Options.Parse(argv[1..], "in", "install")),
        "weaponexport" => WeaponExport(Options.Parse(argv[1..], "out", "from")),
        "weaponcheck" => WeaponCheck(Options.Parse(argv[1..], "in", "install")),
        _ => Usage($"知らないコマンド: {command}"),
    };
}
catch (Exception e) when (e is ArgumentException or IOException or UnauthorizedAccessException or System.Text.Json.JsonException or FormatException or OverflowException)
{
    Console.Error.WriteLine($"error: {e.Message}");
    return 2;
}

// 簡素化の測定（docs/arena/gen13_simplify.md）: 既定は変えず、試合を作る前に静的な既定だけを変える。
static void ApplySimplify(Options o)
{
    MiniRing.BoxingGame.DefaultSubstepRate = o.Float("hz", 240f, min: 30f);
    MiniRing.BoxingGame.DefaultMaxContactPasses = o.Int("passes", 3, min: 1);
    Aigis.Physics.Voxels.VoxelContact.SampleSpacingScale = o.Float("sample", 0.5f, min: 0.1f);
}

// レフェリーの有無（--referee on|off、既定 on）。off ではダウンのカウント中も攻撃でき、立ち直った後の一呼吸も無い。
static bool Referee(Options o) => (o.Get("referee") ?? "on") switch
{
    "on" => true,
    "off" => false,
    _ => throw new ArgumentException("--referee は on か off にしてください。"),
};

static int League(Options o)
{
    ApplySimplify(o);
    var contenders = o.List("bots", [.. BotCatalog.Names]).Select(Load).ToList();
    var bouts = o.Int("bouts", 6, min: 1);
    var seconds = o.Float("seconds", 60f, min: 1f);
    if (contenders.Count < 2)
    {
        throw new ArgumentException("--bots には 2 人以上が必要です。");
    }

    var watch = Stopwatch.StartNew();
    var referee = Referee(o);
    var results = BotArena.RunAll([.. BotArena.RoundRobin(contenders, bouts, seconds, o.Int("seed", 1)).Select(s => s with { Referee = referee })]);
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

// --set "A=0.5,B=1": BotStyle の値を全員に上書きして試す（体格を読む層の係数を探すとき）。
static IReadOnlyDictionary<string, float> SetOf(Options o)
    => o.List("set", []).Select(p => p.Split('=')).ToDictionary(p => p.Length == 2 ? p[0] : throw new ArgumentException("--set は 名前=値 をカンマで区切ってください。"), p => float.Parse(p[1], CultureInfo.InvariantCulture));

// 技ごとの間合いとダメージの曲線（名簿の総当たり）。--out は Markdown、--json は集計（HTML のレポート用）、--csv は 1 打ずつの生の記録。
static int MaaiLabCommand(Options o)
{
    var all = MiniRing.Characters.CharacterRoster.All;
    var roster = o.Get("chars") is null ? all : [.. o.List("chars", []).Select(MiniRing.Characters.CharacterRoster.Get)];
    if (roster.Count < 2)
    {
        throw new ArgumentException("--chars には 2 人以上が必要です。");
    }

    var bouts = o.Int("bouts", 2, min: 1);
    var seconds = o.Float("seconds", 60f, min: 1f);
    var seed = o.Int("seed", 1);
    var referee = Referee(o);
    var watch = Stopwatch.StartNew();
    var cells = new List<MaaiLab.RangeCell>();
    // --focus a,b: その選手が入る組だけ（名簿に足した選手の表を、ほかの選手の組を打ち直さずに測る）。
    var focus = o.Get("focus") is null ? null : o.List("focus", []).ToHashSet(StringComparer.Ordinal);
    var throws = MaaiLab.Collect(roster, bouts, seconds, seed, referee, o.Int("parallel", 2, min: 1), Console.Error, cells, focus);
    var others = focus is null ? 0 : roster.Count(c => !focus.Contains(c.Id));
    var pairs = (roster.Count * (roster.Count - 1) / 2) - (others * (others - 1) / 2);
    var boutCount = pairs * bouts;
    var summary = MaaiLab.Summarize(roster, throws);
    var (rangeBins, rangeSummary) = MaaiLab.Ranges(roster, cells, throws);
    var text = MaaiLab.Markdown(summary, bouts, seconds, seed, referee, boutCount, throws.Count) + MaaiLab.RangesMarkdown(rangeSummary);
    // --ranges <接頭辞>: 得意・不得意の距離（<接頭辞>_bins.csv が帯ごと、<接頭辞>_summary.csv が選手ごと）。
    if (o.Get("ranges") is { } ranges)
    {
        File.WriteAllText(ranges + "_bins.csv", MaaiLab.RangesCsv(rangeBins));
        File.WriteAllText(ranges + "_summary.csv", MaaiLab.RangeSummaryCsv(rangeSummary));
    }

    Console.Error.WriteLine($"maailab done in {watch.Elapsed.TotalSeconds:0.0}s, {throws.Count} throws");
    if (o.Get("out") is { } path)
    {
        File.WriteAllText(path, text);
    }
    else
    {
        Console.WriteLine(text);
    }

    if (o.Get("json") is { } json)
    {
        File.WriteAllText(json, MaaiLab.Json(summary, new { bouts, seconds, seed, referee, boutCount, throws = throws.Count, physics = MiniRing.Fighter.PhysicsVersion, logic = MiniRing.Bots.BoxerBot.LogicVersion, binWidth = 0.1 }));
    }

    if (o.Get("csv") is { } csv)
    {
        File.WriteAllText(csv, MaaiLab.Csv(throws));
    }

    return 0;
}

static int RosterLeagueCommand(Options o)
{
    ApplySimplify(o);
    var all = MiniRing.Characters.CharacterRoster.All;
    var roster = o.Get("chars") is null ? all : [.. o.List("chars", []).Select(MiniRing.Characters.CharacterRoster.Get)];
    // --wear id=glove|none|tonfa|...: そのキャラのはめる物だけを替えて比べる（比べもの。体・教科書・武器はそのまま）。
    var wearOf = o.List("wear", []).Select(p => p.Split('=')).ToDictionary(p => p.Length == 2 ? p[0] : throw new ArgumentException("--wear は 名前=はめる物 をカンマで区切ってください。"), p => MiniRing.Weapons.HandWears.Parse(p[1]));
    roster = [.. roster.Select(c => wearOf.TryGetValue(c.Id, out var w) ? c with { Wear = w } : c)];
    if (roster.Count < 2)
    {
        throw new ArgumentException("--chars には 2 人以上が必要です。");
    }

    var watch = Stopwatch.StartNew();
    // --gameplan on|off|ab: 戦略の層（教育の GamePlan）を教育のまま・全員外す・A/B の層の側（a）だけに残す（ab は --ab on と同じ組み方になる）。
    var gamePlan = (o.Get("gameplan") ?? "on") switch
    {
        "on" => RosterLeague.GamePlanUse.On,
        "off" => RosterLeague.GamePlanUse.Off,
        "ab" => RosterLeague.GamePlanUse.Layered,
        var other => throw new ArgumentException($"--gameplan は on・off・ab のどれかです（{other}）。"),
    };
    var text = RosterLeague.Run(roster, o.Int("bouts", 2, min: 1), o.Float("seconds", 40f, min: 1f), o.Int("seed", 1), Referee(o), o.Int("parallel", 2, min: 1), Console.Error, SetOf(o), o.Get("ab") == "on" || gamePlan == RosterLeague.GamePlanUse.Layered, o.Get("layered"), o.Get("plain"), o.Get("matrix"), gamePlan);
    Console.WriteLine(text);
    Console.Error.WriteLine($"rosterleague done in {watch.Elapsed.TotalSeconds:0.0}s");
    if (o.Get("out") is { } path)
    {
        File.WriteAllText(path, text);
    }

    return 0;
}

// 武器の扱いの物差し（docs/arena/weapon_handling_*.md）。--style-katana 等は、その武器を握る全員に渡す WeaponStyle の JSON。
static int HandleLabCommand(Options o)
{
    var matchups = o.Get("pairs") is { } pairs
        ? [.. pairs.Split(',', StringSplitOptions.RemoveEmptyEntries).Select(p => p.Split(':') is { Length: 2 } ab ? new HandleLab.Matchup(ab[0], ab[1]) : throw new ArgumentException($"--pairs は a:b,c:d の形です（{p}）。"))]
        : HandleLab.DefaultMatchups(o.Get("fist") ?? "marcus");
    static MiniRing.Bots.WeaponStyle? Style(string? path) => path is null ? null : MiniRing.Bots.WeaponStyle.FromJson(File.ReadAllText(path));
    var overrides = new HandleLab.Overrides(Style(o.Get("style-katana")), Style(o.Get("style-longsword")), Style(o.Get("style-club")), Staff: Style(o.Get("style-staff")), SwordShield: Style(o.Get("style-swordshield")), Bat: Style(o.Get("style-bat")));
    var bouts = o.Int("bouts", 6, min: 1);
    var seconds = o.Float("seconds", 40f, min: 1f);
    var seed = o.Int("seed", 1);
    var watch = Stopwatch.StartNew();
    var results = HandleLab.Run(matchups, bouts, seconds, seed, Referee(o), o.Int("parallel", 2, min: 1), overrides, Console.Error);
    var text = HandleLab.Report(results, seconds, seed, o.Get("title") ?? "武器の扱いの物差し");
    Console.WriteLine(text);
    Console.Error.WriteLine($"handlelab done in {watch.Elapsed.TotalSeconds:0.0}s");
    if (o.Get("out") is { } path)
    {
        File.WriteAllText(path, text);
    }

    return 0;
}

// 武器の教科書を練習試合で育てる。--trainee は武器を握る名簿のキャラ（jin・konrad・sean）。採った（または始めの）値は --best（既定 docs/arena/weapon_<id>_best.json）へ。
static int HandleSparCommand(Options o)
{
    var trainee = o.Get("trainee") ?? throw new ArgumentException("--trainee jin|konrad|sean を指定してください。");
    var character = MiniRing.Characters.CharacterRoster.Get(trainee);
    var start = o.Get("start") is { } from ? MiniRing.Bots.WeaponStyle.FromJson(File.ReadAllText(from)) : character.WeaponStyle ?? throw new ArgumentException($"{trainee} は武器のボットを持ちません。");
    var (best, report) = HandleSpar.Run(trainee, start, o.List("opponents", ["konrad", "sean", "jin", "marcus"]).Where(x => x != trainee).ToList(),
        o.List("held-out", ["dmitri", "albert", "vikram", "gaston"]), o.Int("gens", 4, min: 1), o.Int("lambda", 4, min: 1), o.Int("bouts", 2, min: 2), o.Float("seconds", 30f, min: 1f),
        o.Int("seed", 1), Referee(o), o.Int("parallel", 2, min: 1), Console.Out);
    Console.WriteLine(report);
    File.WriteAllText(o.Get("best") ?? $"docs/arena/weapon_{trainee}_best.json", best.ToJson());
    if (o.Get("out") is { } path)
    {
        File.WriteAllText(path, report);
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
    var meterStart = DrillMeter.Total;
    var before = tuner.Matrix(book, seed + 500);
    Log("## 最適化の前の表\n\n" + DefenseTuner.MatrixTable(before.Results));
    var tuned = tuner.Optimize(book, techniques, o.Int("gens", 6, min: 0), o.Int("pop", 8, min: 1));
    var after = tuner.Matrix(tuned, seed + 500);
    Log("## 最適化の後の表（守りを選ぶ重みに使う）\n\n" + DefenseTuner.MatrixTable(after.Results));
    var meterEnd = DrillMeter.Total;
    Log(string.Create(CultureInfo.InvariantCulture,
        $"## 予算（`DefenseDrill.Run`・`DefenseDrill.Punch`・`MeasureBaseline` を呼んだ回数 × 秒数。キャッシュで呼ばなかった分は数えない）\n\n{meterEnd.Calls - meterStart.Calls} 回、合計 {meterEnd.Seconds - meterStart.Seconds:0} 秒（シミュレーション上）。\n"));
    var json = System.Text.Json.JsonSerializer.Serialize(after.Book, new System.Text.Json.JsonSerializerOptions { WriteIndented = true });
    if (o.Get("out") is { } outPath)
    {
        File.WriteAllText(outPath, json);
    }

    return 0;
}

static int CmaTuneCommand(Options o)
{
    var targets = o.List("target", []);
    if (targets.Count == 0)
    {
        throw new ArgumentException("--target に punch:<型> か defense:<技> を渡してください（例: --target punch:2）。");
    }

    var defenses = o.Get("defenses") is { } list
        ? (IReadOnlyList<Defense>)list.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).Select(n => Enum.Parse<Defense>(n, ignoreCase: true)).ToList()
        : null;
    var config = new CmaTuneConfig(targets, defenses, o.Int("gens", 40, min: 1), o.Int("lambda", 0, min: 0), o.Float("sigma", 0.15f, min: 0.001f), o.Int("samples", 48, min: 1),
        o.Float("seconds", 40f, min: 1f), o.Int("seed", 1), o.Int("confirm-n", 96, min: 0), o.Int("match-n", 16, min: 0), o.Float("match-seconds", 45f, min: 1f),
        o.Get("parallel") is null ? null : o.Int("parallel", 1, min: 1));
    if (config.Lambda is > 0 and < 4)
    {
        throw new ArgumentException("--lambda は 0（自動）か 4 以上にしてください。");
    }

    var report = new CmaTune(config, line => Console.Error.WriteLine(line)).Run(o.Get("resume"), o.Get("json"));
    Console.WriteLine(report);
    if (o.Get("out") is { } outPath)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(outPath))!);
        File.WriteAllText(outPath, report);
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

    if (o.Get("handsheet") is { } handSheet)
    {
        KarateShapeVideo.HandSheet(handSheet, Log);
    }

    if (o.Get("video") is { } video)
    {
        KarateShapeVideo.Make(video, o.Float("seconds", 30f, min: 1f), o.Int("seed", 1), Log);
    }

    new KarateLab(o.Int("bouts", 2, min: 1), o.Float("seconds", 20f, min: 1f), o.Int("seed", 1), Log,
        o.List("cues", ["0", "0.06"]).Select(c => float.Parse(c, CultureInfo.InvariantCulture)).ToList())
        .Run(only.Contains("shapes"), only.Contains("parry"), only.Contains("kicks"));
    return 0;
}

// 投げ技の練習（docs/GRAPPLING.md §15）: 教科書の技の経路を少しずつ変えて、良くなったら採る。
static int PracticeLabCommand(Options o)
{
    var logPath = o.Get("log");
    void Log(string line)
    {
        Console.WriteLine(line);
        if (logPath is not null)
        {
            File.AppendAllText(logPath, line + Environment.NewLine);
        }
    }

    foreach (var name in o.List("only", ["seoi-nage"]))
    {
        new PracticeLab(o.Int("seed", 1), Log).Run(MiniRing.Bots.GrappleBook.Jujutsu.Technique(name), o.Int("candidates", 20, min: 1), o.Int("bouts", 2, min: 1),
            o.Float("seconds", 10f, min: 1f), o.Int("validate", 301), o.Float("sigma", 0.06f, min: 0.001f));
    }

    return 0;
}

// 背負い投げを鍛える（docs/GRAPPLING.md §11）。
static int SeoiLabCommand(Options o)
{
    var logPath = o.Get("log");
    void Log(string line)
    {
        Console.WriteLine(line);
        if (logPath is not null)
        {
            File.AppendAllText(logPath, line + Environment.NewLine);
        }
    }

    if (o.Get("reference") == "train")
    {
        new SeoiLab(o.Int("seed", 1), Log).TrainToReference(o.Int("candidates", 24, min: 1), o.Int("bouts", 2, min: 1), o.Float("seconds", 10f, min: 1f), o.Int("validate", 101));
        return 0;
    }

    if (o.Get("reference") == "yes")
    {
        var technique = MiniRing.Bots.GrappleBook.Jujutsu.Technique("seoi-nage");
        foreach (var victim in o.List("victims", ["stand", "champion"]))
        {
            SeoiReference.Run(technique, victim, o.Int("validate", 101), o.Int("bouts", 4, min: 1), o.Float("seconds", 12f, min: 1f), Log);
        }

        return 0;
    }

    if (o.Get("compare") == "yes")
    {
        var p0 = SeoiLab.Params.Initial;
        new SeoiLab(o.Int("seed", 1), Log).Compare([
            ("深さ 0.30", p0 with { Depth = 0.3f }), ("深さ 0.15", p0), ("深さ 0.05", p0 with { Depth = 0.05f }), ("深さ -0.05", p0 with { Depth = -0.05f })],
            o.Int("validate", 101), o.Int("bouts", 2, min: 1), o.Float("seconds", 10f, min: 1f));
        return 0;
    }

    new SeoiLab(o.Int("seed", 1), Log).Train(o.Int("candidates", 24, min: 1), o.Int("bouts", 2, min: 1), o.Float("seconds", 10f, min: 1f), o.Int("validate", 101));
    return 0;
}

// 揺さぶりのドリル（docs/GRAPPLING.md §9。2026-11-19 に rhythmlab から改名: master の rhythmlab は間の層の測り）。
static int SwayLabCommand(Options o)
{
    var logPath = o.Get("log");
    void Log(string line)
    {
        Console.WriteLine(line);
        if (logPath is not null)
        {
            File.AppendAllText(logPath, line + Environment.NewLine);
        }
    }

    static float F(string v) => float.Parse(v, CultureInfo.InvariantCulture);
    new SwayLab(o.Int("bouts", 2, min: 1), o.Float("seconds", 15f, min: 1f), o.Int("seed", 1), Log)
        .Run([.. o.List("hz", ["0", "0.75", "1", "1.5", "2", "2.5", "3"]).Select(F)], [.. o.List("ki", ["0", "3"]).Select(F)], o.Int("cycles", 3, min: 1), o.Float("push", 0.25f, min: 0f));
    return 0;
}

// 姿勢のモデルの比べもの（docs/GRAPPLING.md §7）。
static int BalanceLabCommand(Options o)
{
    var logPath = o.Get("log");
    void Log(string line)
    {
        Console.WriteLine(line);
        if (logPath is not null)
        {
            File.AppendAllText(logPath, line + "\n");
        }
    }

    new BalanceLab(o.Int("seed", 1), Log).Run(o.List("only", []));
    return 0;
}

// 組み技のドリル: 柔術家が 1 つの技だけを受けにかけ続け、投げ・掴めず・取りの転倒・受けの肘の荷重と靭帯を数える（docs/GRAPPLING.md）。
static int ThrowLabCommand(Options o)
{
    var logPath = o.Get("log");
    void Log(string line)
    {
        Console.WriteLine(line);
        if (logPath is not null)
        {
            File.AppendAllText(logPath, line + "\n");
        }
    }

    var techniques = o.List("only", [.. MiniRing.Bots.GrappleBook.Jujutsu.Techniques.Select(t => t.Name)]);
    if (o.Get("video") is { } video)
    {
        // 1 つの技だけをかけ続ける柔術家（青、素手）と、構えて立つ受け（赤）を撮る。
        var only = techniques[0];
        var book = MiniRing.Bots.GrappleBook.Jujutsu with { Rate = 50f };
        var victim = o.List("victims", ["stand"])[0];
        var red = new Contender(victim, s => ThrowLab.Victim(victim, s));
        var blue = new Contender("jujutsu", s => new MiniRing.Bots.JujutsuBot(new MiniRing.Bots.Closer(), book, s, only: only));
        var output = Path.GetFullPath(video);
        Directory.CreateDirectory(Path.GetDirectoryName(output)!);
        new VideoMaker(o.Int("width", 1280, min: 160), o.Int("height", 720, min: 90), 30, speed: o.Float("speed", MiniRing.GameSpeed.Standard, min: 0.1f))
            .Make(red, blue, o.Int("seed", 1), o.Float("seconds", 12f, min: 1f), output, Console.Out, blueWear: MiniRing.Weapons.HandWear.None);
        return 0;
    }

    new ThrowLab(o.Int("bouts", 2, min: 1), o.Float("seconds", 20f, min: 1f), o.Int("seed", 1), Log, o.Get("trace") == "yes")
        .Run(techniques, o.List("victims", ["stand", "champion"]));
    return 0;
}

// 倒れた相手への攻めのドリル: 受け手を気絶させて倒したまま、攻め手が 1 つの技を選んで打ち下ろす（鉄槌 hammer・突き下ろし thrust・踏みつけ stomp）。
static int GroundLabCommand(Options o)
{
    var logPath = o.Get("log");
    void Log(string line)
    {
        Console.WriteLine(line);
        if (logPath is not null)
        {
            File.AppendAllText(logPath, line + "\n");
        }
    }

    var style = o.Get("style") ?? "karate";
    var moves = o.List("moves", ["hammer", "thrust", "stomp"]);
    if (o.Get("video") is { } video)
    {
        var output = Path.GetFullPath(video);
        Directory.CreateDirectory(Path.GetDirectoryName(output)!);
        GroundLab.Video(style, moves[0], o.Int("seed", 1), o.Float("seconds", 12f, min: 2f), output, Console.Out);
        return 0;
    }

    var lab = new GroundLab(o.Int("bouts", 3, min: 1), o.Float("seconds", 12f, min: 2f), o.Int("seed", 1), Log);
    if (o.Get("practice") == "yes")
    {
        foreach (var move in moves)
        {
            lab.Practice(style, move, o.Int("candidates", 20, min: 1), o.Float("sigma", 0.08f, min: 0.001f), o.Int("validate", 301));
        }

        return 0;
    }

    lab.Run(style, moves);
    return 0;
}

// 軸足に乗って蹴る練習: 1 種の蹴りだけを出し続け、蹴り 1 本ごとに倒れた・当たった・渡したエネルギーを、教育の値の組（体重を移す間・頭の寄せ）ごとに数える。
static int KickBalanceCommand(Options o)
{
    var logPath = o.Get("log");
    void Log(string line)
    {
        Console.WriteLine(line);
        if (logPath is not null)
        {
            File.AppendAllText(logPath, line + "\n");
        }
    }

    var kinds = o.List("kicks", ["Front", "Side"]).Select(k => Enum.Parse<MiniRing.Bots.KickTechnique>(k, ignoreCase: true)).ToList();
    var teachings = o.List("teachings", ["0:0:0"]).Select(t =>
    {
        var v = t.Split(':').Select(x => float.Parse(x, CultureInfo.InvariantCulture)).ToArray();
        return new KickBalanceLab.Teaching(v[0], v.Length > 1 ? v[1] : 0f, v.Length > 2 ? v[2] : 0f);
    }).ToList();
    if (o.Get("video") is { } video)
    {
        var output = Path.GetFullPath(video);
        Directory.CreateDirectory(Path.GetDirectoryName(output)!);
        KickBalanceLab.Video(o.Get("style") ?? "karate", kinds[0], teachings[0], o.Int("seed", 1), o.Float("seconds", 12f, min: 2f), output, Console.Out);
        return 0;
    }

    new KickBalanceLab(o.Int("bouts", 3, min: 1), o.Float("seconds", 20f, min: 1f), o.Int("seed", 1), Log)
        .Run(o.Get("style") ?? "karate", kinds, teachings, o.Int("confirm", 0));
    return 0;
}

// 棒人形でモーションを考える（docs/STICK_MOTION.md）: 蹴りの経路を評価する（--kicks）・体重移しの値を探す（--design）。
static int StickCommand(Options o)
{
    var logPath = o.Get("log");
    void Log(string line)
    {
        Console.WriteLine(line);
        if (logPath is not null)
        {
            File.AppendAllText(logPath, line + "\n");
        }
    }

    if (o.Get("swing") is not null)
    {
        StickSwingLab.Run(o, Log);
        return 0;
    }

    var lab = new StickLab(Log);
    var style = o.Get("style") ?? "karate";
    if (o.Get("forms") is { } formChar)
    {
        lab.Forms(formChar, o.Get("png"), o.Get("detail") == "yes", o.Get("bvh"), StickSwingLab.BvhScale(o));
        return 0;
    }

#if STICK_GROUND // 倒れた相手への攻め（GroundLab・BotStyle.Ground）は投げの枝 claude/throwing-grappling-mechanics-de662d にだけある。その枝を master へ入れたらこの #if を外す。
    if (o.Get("ground") is { } who)
    {
        if (o.Get("design") is { } designMove)
        {
            lab.DesignGround(who, designMove, o.Int("candidates", 40, min: 1), o.Int("seed", 1), o.Get("out"));
            return 0;
        }

        lab.Ground(who, o.List("moves", ["cut", "thrust-down"]), o.Get("png"), o.Get("path"));
        return 0;
    }
#endif
    if (o.Get("design") is { } design)
    {
        lab.Design(style, Enum.Parse<MiniRing.Bots.KickTechnique>(design, ignoreCase: true), o.Int("candidates", 40, min: 1), o.Int("seed", 1), o.Float("approach", 0f, min: 0f));
        return 0;
    }

    var kinds = o.List("kicks", ["Front", "Low", "Middle", "High", "Knee", "Side", "Back", "Axe"]).Select(k => Enum.Parse<MiniRing.Bots.KickTechnique>(k, ignoreCase: true)).ToList();
    var teachings = o.List("teachings", ["0:0:0"]).Select(t =>
    {
        var v = t.Split(':').Select(x => float.Parse(x, CultureInfo.InvariantCulture)).ToArray();
        return new StickKick.Teaching(v[0], v.Length > 1 ? v[1] : 0f, v.Length > 2 ? v[2] : 0f);
    }).ToList();
    lab.Kicks(style, kinds, teachings, o.Get("png"), o.Float("approach", 0f, min: 0f));
    return 0;
}

// 蹴りの間合いのドリル: テコンドー家の体が、立つだけの相手へ間合いを刻んで回し蹴りを蹴り、当たり・すねの当たった点・渡したエネルギーを表にする（docs/TAEKWONDO.md）。
static int KickLabCommand(Options o)
{
    var logPath = o.Get("log");
    void Log(string line)
    {
        Console.WriteLine(line);
        if (logPath is not null)
        {
            File.AppendAllText(logPath, line + "\n");
        }
    }

    float[] Floats(string key, string[] fallback) => o.List(key, fallback).Select(t => float.Parse(t, CultureInfo.InvariantCulture)).ToArray();
    int[] Ints(string key, string[] fallback) => o.List(key, fallback).Select(t => int.Parse(t, CultureInfo.InvariantCulture)).ToArray();
    KickLab.Report(o.Get("body") ?? "taeyun", Enum.Parse<MiniRing.Bots.KickTechnique>(o.Get("kick") ?? "Roundhouse", ignoreCase: true),
        Floats("distances", ["1.0", "1.05", "1.1", "1.15", "1.2", "1.25", "1.3", "1.4"]), Ints("seeds", ["1", "2", "3", "4", "5", "6"]),
        Ints("confirm-seeds", ["11", "12", "13", "14", "15", "16"]),
        o.Get("shin") is { } shin ? float.Parse(shin, CultureInfo.InvariantCulture) : null,
        o.Get("overshoot") is { } overshoot ? float.Parse(overshoot, CultureInfo.InvariantCulture) : null,
        o.Get("side") is { } side ? Enum.Parse<MiniRing.Hand>(side, ignoreCase: true) : null, Log);
    return 0;
}

// 人の回し蹴りのモーションキャプチャ: 読み替えの表と、手本の書き出し（--export games/MiniRing/Bots/kicks）。
static int KickMocapCommand(Options o)
{
    var logPath = o.Get("log");
    void Log(string line)
    {
        Console.WriteLine(line);
        if (logPath is not null)
        {
            File.AppendAllText(logPath, line + "\n");
        }
    }

    if (o.Get("video") is { } videoPath)
    {
        KickMocap.Video(o.Get("dir") ?? "data/mocap/kyokushin", videoPath, Log);
        return 0;
    }

    if (o.Get("export-mid") is { } midDir)
    {
        KickMocap.ExportMid(o.Get("dir") ?? "data/mocap/kyokushin", midDir, o.Get("body") ?? "standard", Log);
        return 0;
    }

    if (o.Get("doll") is { } doll)
    {
        float[] Floats(string key, string[] fallback) => o.List(key, fallback).Select(t => float.Parse(t, CultureInfo.InvariantCulture)).ToArray();
        KickMocap.Compare(doll, Floats("durations", ["0.5", "0.6", "0.75", "0.9"]), Floats("distances", ["1.0", "1.2", "1.4"]), Log);
        return 0;
    }

    KickMocap.Report(o.Get("dir") ?? "data/mocap/kyokushin", o.Get("export"), Log);
    return 0;
}

// 回し蹴りの段階練習: 段 1 = 膝を空中に止めて伸ばすだけで蹴り、当てる所（膝の位置・ねじり）を探す。
static int KneeLabCommand(Options o)
{
    var logPath = o.Get("log");
    void Log(string line)
    {
        Console.WriteLine(line);
        if (logPath is not null)
        {
            File.AppendAllText(logPath, line + "\n");
        }
    }

    float[] Floats(string key, string[] fallback) => o.List(key, fallback).Select(t => float.Parse(t, CultureInfo.InvariantCulture)).ToArray();
    float[] Radians(string key, string[] fallback) => Floats(key, fallback).Select(d => d * MathF.PI / 180f).ToArray();
    var sides = o.List("sides", ["Left", "Right"]).Select(s => Enum.Parse<MiniRing.Hand>(s, ignoreCase: true)).ToArray();
    if (o.Int("stage", 1) == 2)
    {
        var impact = new KneeLab.Spot(o.Float("impact-az", 20f) * MathF.PI / 180f, o.Float("impact-el", 10f) * MathF.PI / 180f, o.Float("impact-twist", 0f));
        KneePractice.Stage2(o.Get("body") ?? "taeyun", impact, o.Float("foot", MiniRing.Bots.KickSpec.Of(MiniRing.Bots.KickTechnique.Roundhouse).FootPitch),
            Floats("distances", ["1.2", "1.4"]), Floats("confirm", ["1.1", "1.3"]), o.Int("gens", 12, min: 1), o.Int("lambda", 8, min: 4), o.Get("video"), Log);
        return 0;
    }

    if (o.Get("video") is { } video1)
    {
        var spot = new KneeLab.Spot(o.Float("impact-az", 20f) * MathF.PI / 180f, o.Float("impact-el", 10f) * MathF.PI / 180f, o.Float("impact-twist", 0f));
        KneeLab.Stage1Video(o.Get("body") ?? "taeyun", spot, sides[0], o.Float("distance", 1.2f), video1, Log);
        return 0;
    }

    KneeLab.Stage1(o.List("bodies", ["taeyun", "standard"]), Floats("distances", ["1.2"]), Radians("azimuths", ["20", "50", "80"]),
        Radians("elevations", ["0", "25", "50"]), Floats("twists", ["-1.5", "0", "1.5", "3"]), sides, Log);
    return 0;
}

// 膝の制御の回し蹴りの動画: 間合いを変えて 1 本ずつ、真横から撮る（金の線 = 膝の制御点、隅に曲げ・ねじり・当たった点とエネルギー）。
static int KickVideoCommand(Options o)
{
    var distances = o.List("distances", ["1.05", "1.25", "1.5"]).Select(t => float.Parse(t, CultureInfo.InvariantCulture)).ToArray();
    KickVideo.Make(o.Get("body") ?? "taeyun", Enum.Parse<MiniRing.Bots.KickTechnique>(o.Get("kick") ?? "Roundhouse", ignoreCase: true), distances,
        Enum.Parse<MiniRing.Hand>(o.Get("side") ?? "Left", ignoreCase: true), o.Float("slow", 0.25f, min: 0.05f), o.Int("width", 1280, min: 320), o.Int("height", 720, min: 240),
        o.Int("fps", 30, min: 5), o.Get("out") ?? "kick.mp4", Console.Out);
    return 0;
}

// 詰めのドリル: 意識を止めた相手へ名簿のボットが詰めて打ち、拳ごとに顔・胴・ガード・空振り（外れた向き）を数える（docs/BOT_PDCA.md「詰めのドリル」）。
static int FinishLabCommand(Options o)
{
    var logPath = o.Get("log");
    void Log(string line)
    {
        Console.WriteLine(line);
        if (logPath is not null)
        {
            File.AppendAllText(logPath, line + "\n");
        }
    }

    var levels = o.List("levels", ["0.45", "0.6"]).Select(t => float.Parse(t, CultureInfo.InvariantCulture)).ToList();
    FinishLab.Report(o.List("attackers", ["ramon", "yoandry", "marcus"]), o.List("victims", ["dmitri", "joao", "ramon"]), levels,
        o.Int("bouts", 2, min: 1), o.Float("seconds", 20f, min: 1f), SetOf(o), Log,
        o.Get("aim") is { } aim ? Enum.Parse<MiniRing.AimGoal>(aim, ignoreCase: true) : (MiniRing.AimGoal?)null,
        o.List("victim-set", []).Select(p => p.Split('=')).ToDictionary(p => p[0], p => float.Parse(p[1], CultureInfo.InvariantCulture)));
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

// 回避の形を体の動きとして測る（頭の沈み・横・前後、腰の沈み、拳と頭の距離）。--set で動きの値を試しに書き換える。
static int FormLabCommand(Options o)
{
    var punch = o.Get("punch") ?? "2";
    _ = Combo.Named(punch);
    var slot = Enum.Parse<Defense>(o.Get("slot") ?? "Slip", ignoreCase: true);
    var style = LoadStyle(o.Get("style") ?? "swarmer");
    var move = FormLab.Apply(o.Get("move") is { } path ? LoadMove(path) : style.Defenses.Of(slot), o.Get("set"));
    // 省略は 0 秒の合図（拳と同時に回避）。none は受け手自身の目と反応。
    float? cue = o.Get("cue") switch { null => 0f, "none" => null, var c => float.Parse(c, CultureInfo.InvariantCulture) };
    var text = FormLab.Run(punch, slot, style, move, cue, o.Int("bouts", 3, min: 1), o.Float("seconds", 20f, min: 1f), o.Int("seed", 1));
    Console.WriteLine(text);
    if (o.Get("log") is { } log)
    {
        File.AppendAllText(log, text + "\n");
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
    ApplySimplify(o);
    MiniRing.BoxingGame.DefaultMaxContactPasses = o.Int("passes", 3, min: 1);
    SwordLibraryLab.Pacing = o.Get("pacing") == "on" ? new MiniRing.Motion.SwingPacing(o.Float("slack", 0.25f, min: 0.01f), o.Float("angle", 0.6f, min: 0.01f)) : null;
    SwordLibraryLab.Clearance = o.Get("clearance") == "on" ? new MiniRing.Motion.SelfClearance(o.Float("margin", MiniRing.Motion.SelfClearance.Adopted.Margin, min: 0f), MiniRing.Motion.SelfClearance.Adopted.FromGrip, o.Float("tilt", MiniRing.Motion.SelfClearance.Adopted.MaxTilt, min: 0f)) : null;
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
    // --defender swordshield: 受け手を片手剣と盾にする（--variants は GuardLab.ShieldVariants の番号）。
    GuardLab.Shielded = o.Get("defender") == "swordshield";
    var library = SwordLibraryLab.Load("docs/arena/sword_library.json");
    var attacks = o.Get("forms") == "yes"
        ? GuardLab.DirectionalAttacks(SwingLab.Load("docs/arena/club_swings.json"))
        : GuardLab.Attacks(library, SwingLab.Load("docs/arena/club_swings.json"), SwingLab.Load("docs/arena/bat_swings.json"));
    if (o.Get("techniques") is { } names)
    {
        var selected = names.Split(',', StringSplitOptions.RemoveEmptyEntries);
        foreach (var name in selected)
        {
            if (!attacks.Any(a => a.Technique == name))
            {
                throw new ArgumentException($"技がありません: {name}（--forms yes と型の技名を指定）。");
            }
        }

        attacks = [.. attacks.Where(a => selected.Contains(a.Technique, StringComparer.Ordinal))];
    }
    if (o.Get("only") is { } only)
    {
        attacks = [.. attacks.Where(a => only.Split(',').Any(x => a.Name.Contains(x, StringComparison.Ordinal)))];
    }

    // --compare cycle2: 2 周目の比較表（GuardLab.Cycle2。--variants はその番号）。
    var pool = o.Get("compare") == "cycle2" ? GuardLab.Cycle2 : GuardLab.Shielded ? GuardLab.ShieldVariants : GuardLab.Variants;
    var variants = o.Get("variants") is { } picked
        ? [.. picked.Split(',').Select(s => pool[int.Parse(s, System.Globalization.CultureInfo.InvariantCulture)])]
        : o.Get("compare") == "yes" ? GuardLab.Comparison : pool;
    if (attacks.Count == 0 || variants.Count == 0)
    {
        throw new ArgumentException("打ち手と守り方を 1 つ以上選んでください。");
    }
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
        new SparVideo(1280, 720, 30).MakeGuard(library, attacks, variants[^1], o.Int("seed", 1), o.Float("seconds", 4.5f), video, Console.Out);
        return 0;
    }

    var report = GuardLab.Run(library, attacks, variants, o.Int("seeds", 2), o.Float("seconds", 4.5f), Console.Out, out _, o.Int("seed", 1), o.Int("parallel", 2, min: 1));
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
    var input = o.Get("forms") == "yes" ? SwordFormLab.Library() : SwordLibraryLab.Load(library);
    if (o.Get("only") is { } only)
    {
        var names = only.Split(',').ToHashSet(StringComparer.Ordinal);
        input = input with { Techniques = [.. input.Techniques.Where(t => names.Contains(t.Name))] };
        if (input.Techniques.Count == 0) { throw new ArgumentException("指定された型がありません。"); }
    }
    var report = AimLab.Run(input, candidates, o.Get("library-out") ?? library, o.Get("tuning-out") ?? "docs/arena/aim_tuning.json", Console.Out);
    Console.WriteLine(report);
    if (o.Get("out") is { } path)
    {
        File.WriteAllText(path, report);
    }

    return 0;
}

static int NoiseLabCommand(Options o)
{
    // 評価の雑音を測る（docs/arena/evo_noise.md）: 種による揺れ・小さな変更での揺れ・差を言うのに要る標本の数・1 標本の長さ。
    var only = o.List("only", ["drill", "match"]).ToHashSet();
    var unknown = only.FirstOrDefault(x => x is not ("drill" or "match"));
    if (unknown is not null)
    {
        throw new ArgumentException($"--only は drill, match のどちらか（{unknown}）。");
    }

    float[] Floats(string name, float[] fallback) => o.List(name, fallback.Select(f => f.ToString(CultureInfo.InvariantCulture)).ToList()).Select(s => float.Parse(s, CultureInfo.InvariantCulture)).ToArray();
    var matchSeconds = Floats("match-seconds", [30f, 45f, 60f]);
    var drillSeconds = Floats("drill-seconds", [10f, 20f, 40f]);
    var lab = new NoiseLab(o.Int("match-n", 12, min: 4), o.Int("drill-n", 48, min: 4), o.Int("seed", 1), o.Get("parallel") is null ? null : o.Int("parallel", 1, min: 1),
        line => Console.Error.WriteLine($"{DateTime.Now:HH:mm:ss} {line}"));
    var report = lab.Run(only, matchSeconds, drillSeconds, o.Float("match-ref", 45f, min: 1f), o.Float("drill-ref", 20f, min: 1f));
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

static int TunnelCommand(Options o)
{
    // 調査: 刃が体のセルを通ったのに接触が何も言わなかった歩（すり抜け）を数える。
    var contenders = VersusLab.Contenders("docs/arena/sword_library.json", "docs/arena/club_swings.json", "docs/arena/bat_swings.json", DefenseDefaults.Value,
        File.Exists("docs/arena/hit_and_away.json") ? AwayLab.Load("docs/arena/hit_and_away.json") : new MiniRing.Bots.HitAndAway(), AimDefaults.Tuning);
    TunnelProbe.Passes = o.Int("passes", 3, min: 1);
    TunnelProbe.Hz = o.Float("hz", 240f, min: 30f);
    Aigis.Physics.Voxels.VoxelContact.SampleSpacingScale = o.Float("sample", 0.5f, min: 0.1f);
    var report = TunnelProbe.Run(contenders, [.. o.List("pairs", ["日本刀-日本刀", "ロングソード-日本刀", "日本刀-拳"])], o.Int("bouts", 4, min: 1), o.Float("seconds", 20f, min: 1f), Console.Out);
    Console.WriteLine(report);
    if (o.Get("out") is { } path)
    {
        File.WriteAllText(path, report);
    }

    return 0;
}

static int JointLabCommand(Options o)
{
    // どの関節でも、刃が隙間を通り抜けるか（docs/VOXEL_DAMAGE.md「関節のつなぎ」）。
    if (o.Get("dims") is not null)
    {
        Console.WriteLine(JointLab.Dimensions());
        return 0;
    }

    if (o.Get("video") is { } video)
    {
        JointLab.Video(JointLab.Parse(o.Get("joint") ?? "LeftElbow"), o.Get("dir") ?? "side", o.Float("offset", 0f), o.Float("speed", 3.4f, min: 0.1f), o.Get("bridges") != "off",
            o.Float("bend", 0f), o.Get("axis") ?? "right", o.Float("slow", 8f, min: 0.25f), video);
        return 0;
    }

    var joints = o.Get("joints") is { } named
        ? [.. named.Split(',').Select(JointLab.Parse)]
        : Enum.GetValues<Aigis.Physics.HumanoidRagdoll.RagdollJoint>().ToArray();
    var report = JointLab.Table(joints,
        [.. o.List("offsets", ["-0.01", "-0.005", "0", "0.005", "0.01"]).Select(s => float.Parse(s, CultureInfo.InvariantCulture))],
        [.. o.List("speeds", ["9"]).Select(s => float.Parse(s, CultureInfo.InvariantCulture))],
        o.Float("bend", 40f), (int)o.Float("parallel", 3f, min: 1f));
    Console.WriteLine(report);
    if (o.Get("out") is { } path)
    {
        File.WriteAllText(path, report);
    }

    return 0;
}

static int NeckLabCommand(Options o)
{
    // 関節の隙間の調査（geom）と、首・肩の高さの薙ぎ（cut。既定）。
    NeckLab.ArmsDown = o.Get("arms") == "down";
    if (o.Get("video") is { } slashVideo && o.Get("mode") == "slash")
    {
        NeckLab.SlashVideo(o.Get("dir") ?? "back", o.Float("offset", -0.003f), o.Float("speed", 3.4f, min: 0.1f), o.Get("bridges") != "off", o.Get("arms") == "down", o.Float("slow", 8f, min: 0.25f), slashVideo, o.Float("tilt", 0f));
        return 0;
    }

    if (o.Get("video") is { } videoPath)
    {
        NeckLab.Video(o.Float("height", 0.3f), o.Float("side", 1f), o.Float("offset", 0f), o.Float("slow", 4f, min: 0.25f), videoPath);
        return 0;
    }

    var report = o.Get("mode") == "geom"
        ? NeckLab.Geometry()
        : o.Get("mode") == "slash"
        ? NeckLab.Slash([.. o.List("offsets", ["-0.012", "-0.009", "-0.006", "-0.003", "0", "0.003", "0.006", "0.009", "0.012", "0.016", "0.02", "0.025"]).Select(s => float.Parse(s, CultureInfo.InvariantCulture))],
            [.. o.List("speeds", ["3.4", "9"]).Select(s => float.Parse(s, CultureInfo.InvariantCulture))], o.Get("bridges") != "off", o.Float("tilt", 0f))
        : NeckLab.Cut([.. o.List("heights", ["0.2", "0.3", "0.4", "0.5", "0.6", "0.7"]).Select(s => float.Parse(s, CultureInfo.InvariantCulture))],
            [.. o.List("offsets", ["-0.04", "0", "0.04"]).Select(s => float.Parse(s, CultureInfo.InvariantCulture))]);
    Console.WriteLine(report);
    if (o.Get("out") is { } path)
    {
        File.WriteAllText(path, report);
    }

    return 0;
}

static int SelfPathCommand(Options o)
{
    // 技の目標の通り道が自分の体を通るか（判断の側の自傷）。
    var clearance = new MiniRing.Motion.SelfClearance(o.Float("margin", 0.1f, min: 0f), o.Float("from", 0.45f, min: 0f), o.Float("tilt", 0.6f, min: 0f));
    var report = SelfPathLab.Run(o.Get("forms") == "yes" ? SwordFormLab.Library() : SwordLibraryLab.Load("docs/arena/sword_library.json"), clearance);
    Console.WriteLine(report);
    if (o.Get("out") is { } path)
    {
        File.WriteAllText(path, report);
    }

    return 0;
}

static int SelfCutCommand(Options o)
{
    // 自傷の物差し: 握った武器が自分の体に触れた回数を、技・姿勢で原因に分けて数える（--hitaway off で打って離れるのをやめる）。
    var hitAway = o.Get("hitaway") == "off" ? null : File.Exists("docs/arena/hit_and_away.json") ? AwayLab.Load("docs/arena/hit_and_away.json") : new MiniRing.Bots.HitAndAway();
    var contenders = VersusLab.Contenders("docs/arena/sword_library.json", "docs/arena/club_swings.json", "docs/arena/bat_swings.json", DefenseDefaults.Value, hitAway, AimDefaults.Tuning);
    var seconds = o.Float("seconds", 30f, min: 1f);
    SelfCutLab.Passes = o.Int("passes", 3, min: 1);
    MiniRing.BoxingGame.DefaultMaxContactPasses = SelfCutLab.Passes;
    SwordLibraryLab.Pacing = o.Get("pacing") == "on" ? new MiniRing.Motion.SwingPacing(o.Float("slack", 0.25f, min: 0.01f), o.Float("angle", 0.6f, min: 0.01f)) : null;
    SwordLibraryLab.Clearance = o.Get("clearance") == "on" ? new MiniRing.Motion.SelfClearance(o.Float("margin", MiniRing.Motion.SelfClearance.Adopted.Margin, min: 0f), MiniRing.Motion.SelfClearance.Adopted.FromGrip, o.Float("tilt", MiniRing.Motion.SelfClearance.Adopted.MaxTilt, min: 0f)) : null;
    var bouts = SelfCutLab.Collect(contenders, [.. o.List("pairs", ["日本刀-日本刀", "ロングソード-ロングソード", "ロングソード-日本刀", "日本刀-拳"])], o.Int("seeds", 8, min: 1), seconds);
    var report = SelfCutLab.Report(bouts, seconds);
    Console.WriteLine(report);
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

static int VrMotionSynthCommand(Options o)
{
    // VR の実機なしで、記録 → なぞりの道を確かめる合成の記録（棒の突き）を書く。
    var output = o.Get("out") ?? "motions/synthetic_staff_thrust.vrmotion";
    var clip = MiniRing.Motion.VrMotionSynth.StaffThrust();
    clip.Save(output);
    Console.WriteLine($"wrote {output}: {clip.Frames.Count} frames, {clip.Duration:0.00} s, arm {clip.Header.PlayerArmReach:0.000} m");
    return 0;
}

static int ImitateCommand(Options o)
{
    if (o.Get("vrmotion") is { } vrPath)
    {
        // 記録した VR の動き（.vrmotion）を、人形がなぞる動画。武器は見出しのもの（--weapon で上書き）。
        var vrClip = MiniRing.Motion.VrMotionClip.Load(vrPath);
        var weaponName = o.Get("weapon") ?? vrClip.Header.Weapon;
        MiniRing.Weapons.WeaponKind? vrWeapon = weaponName is null ? null
            : Enum.TryParse<MiniRing.Weapons.WeaponKind>(weaponName, ignoreCase: true, out var kind) ? kind
            : throw new ArgumentException($"Unknown weapon '{weaponName}'.");
        var vrOut = o.Get("out") ?? "videos/vrmotion.mp4";
        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(vrOut))!);
        var vrVideo = new VrMotionVideo(o.Int("width", 1280, min: 160), o.Int("height", 720, min: 90), o.Int("fps", 30, min: 10), o.Float("slow", 1f, min: 0.25f));
        var vrRate = o.Float("rate", 1f, min: 0.1f);
        var vrTitle = Path.GetFileNameWithoutExtension(vrPath);
        if (o.Get("measure") is not null)
        {
            var r = vrVideo.Run(vrClip, vrWeapon, vrRate, vrTitle, null);
            Console.WriteLine(string.Create(System.Globalization.CultureInfo.InvariantCulture, $"{vrTitle}: right hand error {r.MeanRightError:0.00} left {r.MeanLeftError:0.00} ({r.Steps} steps)"));
            return 0;
        }

        vrVideo.Make(vrClip, vrWeapon, vrRate, vrTitle, vrOut, Console.Out);
        return 0;
    }

    // 日本刀（剣道のモーションキャプチャ）とロングソード（型のキー）の基本を、ラグドールがなぞる動画。
    var output = o.Get("out") ?? "videos/sword_basics.mp4";
    Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(output))!);
    var rate = o.Float("rate", 1.77f, min: 0.1f); // 体の大きさの比 0.32 の 1/√（同じ重力の下でつり合う速さ）
    var shots = (o.Get("weapon") switch
    {
        "katana" => MiniRing.Bots.FormSwordsman.Forms(MiniRing.Weapons.WeaponKind.Katana).Select(f => new ImitationVideo.Shot(f.Name, MiniRing.Weapons.WeaponKind.Katana, f, rate)).ToList(),
        "longsword" => MiniRing.Bots.FormSwordsman.Forms(MiniRing.Weapons.WeaponKind.Longsword).Select(f => new ImitationVideo.Shot(f.Name, MiniRing.Weapons.WeaponKind.Longsword, f, rate)).ToList(),
        "bat" => MiniRing.Bots.FormSwordsman.Forms(MiniRing.Weapons.WeaponKind.Bat).Select(f => new ImitationVideo.Shot(f.Name, MiniRing.Weapons.WeaponKind.Bat, f, rate)).ToList(),
        "staff" => SwordBasics.StaffShots(rate), "jo" => SwordBasics.JoShots(rate), _ => SwordBasics.Shots(rate),
    }).Where(s => o.Get("only") is not { } only || (o.Get("weapon") is "katana" or "longsword" or "bat" ? s.Title == only : s.Title.Contains(only, StringComparison.Ordinal))).ToList();
    if (shots.Count == 0)
    {
        throw new ArgumentException("指定された型がありません。");
    }
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
    var only = o.Get("only");
    var scenes = BreakDemo.Scenes().Where(s => only is null || s.Title.Contains(only, StringComparison.Ordinal)).ToList();
    new SparVideo(1280, 720, o.Int("fps", 30, min: 10)).MakeBreakDemo(scenes, o.Float("seconds", 6f, min: 2f), video, Console.Out);
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

// --live の設定。--live が無ければ null。--render off とは一緒に使えない。
static LiveOptions? LiveOf(Options o, bool render)
{
    if (o.Get("live") is not { } url)
    {
        if (o.Get("voices") is not null || o.Get("encoder") is not null || o.Get("bitrate") is not null || o.Get("wait") is not null || o.Get("delay") is not null)
        {
            throw new ArgumentException("--voices / --encoder / --bitrate / --wait / --delay は --live と一緒に使います。");
        }

        return null;
    }

    if (!render)
    {
        throw new ArgumentException("--live と --render off は一緒に使えません。");
    }

    var voices = o.Get("voices");
    if (voices is not null && !File.Exists(voices))
    {
        throw new ArgumentException($"--voices の manifest.json がありません: {voices}");
    }

    var encoder = o.Get("encoder") ?? LiveOutput.DefaultEncoder;
    LiveOutput.EncoderArguments(encoder, LiveOutput.DefaultBitrate, 30); // 知らないエンコーダは、ここで（試合を回す前に）はねる
    var target = url == "on" ? LiveOutput.DefaultUrl : url;
    LiveOutput.Format(target); // 形式の分からない出口も、ここではねる
    LiveOutput.ValidateDelay(o.Float("delay", (float)LiveOutput.DefaultDelaySeconds, min: 0f), o.Get("bitrate") ?? LiveOutput.DefaultBitrate); // 緩衝に収まらない遅延は、試合を回す前にはねる
    return new LiveOptions(target, encoder, o.Get("bitrate") ?? LiveOutput.DefaultBitrate, voices is null ? null : Path.GetFullPath(voices),
        o.Float("wait", (float)LiveOutput.DefaultWaitSeconds, min: 1f),
        o.Float("delay", (float)LiveOutput.DefaultDelaySeconds, min: 0f)); // 配信の遅延 [s]: この秒数ぶんのコマを作ってから送り側を始める // 受け手が繋ぐのを待つ上限 [s]。過ぎたら ffmpeg を止めて失敗にする
}

static int Video(Options o)
{
    if (o.Get("drill") is { } punch)
    {
        return DrillVideo(o, punch);
    }

    // 名簿のキャラクター（--red-char / --blue-char <id>。games/MiniRing/Characters/roster/<id>.json）: ボットのスタイル・武器・はめる物・体格・顔・表示名をそのキャラクターの値にする。
    var redCharacter = o.Get("red-char") is { } redId ? MiniRing.Characters.CharacterRoster.Get(redId) : null;
    var blueCharacter = o.Get("blue-char") is { } blueId ? MiniRing.Characters.CharacterRoster.Get(blueId) : null;
    var set = SetOf(o);
    // キャラクターは教科書に教育を重ねた解決済みのスタイル（相手ごとの備えは、もう片方がキャラクターのときだけ効く）。--set はその上にさらに重ねる。
    var red = redCharacter is not null ? RosterLeague.ContenderOf(redCharacter, blueCharacter?.Id, set) : LoadWith(o.Get("red") ?? "champion", set);
    var blue = blueCharacter is not null ? RosterLeague.ContenderOf(blueCharacter, redCharacter?.Id, set) : LoadWith(o.Get("blue") ?? "jabber", set);
    // 描かずに出来事だけ作る下回し（--render off）と、実時間で描いて流す生配信（--live <url> | on。--voices は実況の声の manifest.json）。
    var render = (o.Get("render") ?? "on") switch
    {
        "on" => true,
        "off" => false,
        _ => throw new ArgumentException("--render は on か off にしてください。"),
    };
    var live = LiveOf(o, render);
    // 生配信の既定の --out は出来事の記録（*.events.json）の置き場所を決めるだけ（映像のファイルは書かない）。
    var output = Path.GetFullPath(o.Get("out") ?? (live is not null ? "videos/live.ts" : $"miniring_{redCharacter?.Id ?? red.Name}_vs_{blueCharacter?.Id ?? blue.Name}.mp4"));
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
    new VideoMaker(o.Int("width", 1280, min: 160), o.Int("height", 720, min: 90), o.Int("fps", 30, min: 10), breakStyle, hitSounds, faceCam, speed, tv,
        tvRounds: o.Int("rounds", MiniRing.MatchRules.Tv.Rounds, min: 1), tvEvent: o.Get("event") ?? "ファイトナイト", tvBout: o.Get("bout") ?? "",
        tvBreak: o.Float("break", MiniRing.MatchRules.Tv.BreakSeconds, min: 1f), render: render, live: live,
        tvProgram: o.Get("program") is { } programPath ? TvProgram.FromJson(File.ReadAllText(programPath), TvProgram.RosterName) : null,
        org: OrgOption(o), belt: (o.Get("belt") ?? "off") switch { "on" => true, "off" => false, _ => throw new ArgumentException("--belt は on か off にしてください。") })
        .Make(red, blue, o.Int("seed", 4), o.Float("seconds", 45f, min: 1f), output, Console.Out, untilKo: (o.Get("until-ko") ?? "no") switch { "yes" => true, "no" => false, _ => throw new ArgumentException("--until-ko は yes か no にしてください。") },
            redWear: o.Get("red-wear") is { } rwear ? MiniRing.Weapons.HandWears.Parse(rwear) : redCharacter?.Wear ?? MiniRing.Weapons.HandWear.Glove,
            blueWear: o.Get("blue-wear") is { } bwear ? MiniRing.Weapons.HandWears.Parse(bwear) : blueCharacter?.Wear ?? MiniRing.Weapons.HandWear.Glove,
            referee: Referee(o),
            redWeapon: o.Get("red-weapon") is { } rw ? Enum.Parse<MiniRing.Weapons.WeaponKind>(rw, ignoreCase: true) : redCharacter?.Weapon,
            blueWeapon: o.Get("blue-weapon") is { } bw ? Enum.Parse<MiniRing.Weapons.WeaponKind>(bw, ignoreCase: true) : blueCharacter?.Weapon,
            redCharacter: redCharacter, blueCharacter: blueCharacter,
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
    if (o.Get("attacker-style") is { } attackerStyleName)
    {
        // 攻め手の技の表だけを、そのスタイル（champion に磨いた技を重ねた *.style.json など）の物にする。攻め手の構え・単発の型・間合いはドリルのまま。
        attacker = (attacker with { Punches = LoadStyle(attackerStyleName).Punches }).Validated();
    }

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

// テレビ中継の動画の下見: 動画と同じ試合を描画せずに種ごとに回し、決着とダウンを並べる（--seeds 1,2,3）。
static int TvScoutCommand(Options o)
{
    var speed = o.Get("speed") is { } speedText
        ? MiniRing.GameSpeed.Parse(speedText) ?? throw new ArgumentException("--speed は 0.5 / 0.75 / 1 のどれかです。")
        : MiniRing.GameSpeed.Standard;
    // 名簿のキャラクター（--red-char / --blue-char）は video と同じ決め方（スタイル・武器・はめる物・体格・顔）。
    var redCharacter = o.Get("red-char") is { } redId ? MiniRing.Characters.CharacterRoster.Get(redId) : null;
    var blueCharacter = o.Get("blue-char") is { } blueId ? MiniRing.Characters.CharacterRoster.Get(blueId) : null;
    var rounds = o.Int("rounds", MiniRing.MatchRules.Tv.Rounds, min: 1);
    var red = redCharacter is not null ? RosterLeague.ContenderOf(redCharacter, blueCharacter?.Id, null) : Load(o.Get("red") ?? "champion");
    var blue = blueCharacter is not null ? RosterLeague.ContenderOf(blueCharacter, redCharacter?.Id, null) : Load(o.Get("blue") ?? "karate");
    var redWear = o.Get("red-wear") is { } rwear ? MiniRing.Weapons.HandWears.Parse(rwear) : redCharacter?.Wear ?? MiniRing.Weapons.HandWear.Glove;
    var blueWear = o.Get("blue-wear") is { } bwear ? MiniRing.Weapons.HandWears.Parse(bwear) : blueCharacter?.Wear ?? MiniRing.Weapons.HandWear.Glove;
    var redWeapon = o.Get("red-weapon") is { } rw ? Enum.Parse<MiniRing.Weapons.WeaponKind>(rw, ignoreCase: true) : redCharacter?.Weapon;
    var blueWeapon = o.Get("blue-weapon") is { } bw ? Enum.Parse<MiniRing.Weapons.WeaponKind>(bw, ignoreCase: true) : blueCharacter?.Weapon;
    Console.WriteLine($"{redCharacter?.Id ?? red.Name}({redWear}{(redWeapon is null ? "" : "+" + redWeapon)}) vs {blueCharacter?.Id ?? blue.Name}({blueWear}{(blueWeapon is null ? "" : "+" + blueWeapon)}), {rounds} R");
    var org = OrgOption(o) ?? Organization.Default;
    var results = TvScout.Run(red, blue, redWear, blueWear, redWeapon, blueWeapon,
        rounds, o.List("seeds", ["1", "2", "3", "4", "5", "6", "7", "8"]).Select(s => int.Parse(s, CultureInfo.InvariantCulture)), speed, o.Int("fps", 30, min: 10),
        redCharacter, blueCharacter, Referee(o), o.Float("break", MiniRing.MatchRules.Tv.BreakSeconds, min: 1f), org);
    var roundSeconds = MiniRing.MatchRules.Tv.RoundSeconds;
    Console.WriteLine($"org {org.Id}");
    TvScout.Print(results, rounds, roundSeconds, Console.Out, org.Drama);
    // 最後の 2 行は機械が読む（tools/tvshow/tvshow.ps1 が種を選び、どの版の物理・ボットで選んだかを残す）。
    Console.WriteLine($"versions physics {MiniRing.Fighter.PhysicsVersion} logic {MiniRing.Bots.BoxerBot.LogicVersion}");
    Console.WriteLine($"best seed {TvShowRules.Best(results, rounds, roundSeconds, org.Drama).Seed}");
    return 0;
}

static int Print(string line)
{
    Console.WriteLine(line);
    return 0;
}

// テレビ中継の実況の骨組みの下書き（events.json → script.json）。書き手はこれに解説との掛け合いを足す。
static int TvDraftCommand(Options o)
{
    var events = o.Get("events") ?? throw new ArgumentException("--events <x.events.json> を渡してください。");
    var output = o.Get("out") ?? events.Replace(".events.json", ".draft.json", StringComparison.Ordinal);
    // 中身が同じなら書かない（ファイルの時刻が変わると、司令塔が台本を作り直して声を入れ直してしまう）。
    // テレビ中継の標準は実況と解説の 2 人: 実況の骨組みに、解説の台詞と休みの問いと答え（TvAnalyst）を重ねたものを下書きにする。
    var draft = TvDraft.BuildBroadcast(File.ReadAllText(events), OrgOption(o));
    if (!File.Exists(output) || File.ReadAllText(output) != draft)
    {
        File.WriteAllText(output, draft);
    }

    Console.WriteLine(output);
    return 0;
}

// 下書き（骨組み）に書き手の足す台詞（extras.json）を重ねて台本にする。入らなかった台詞を知らせる。
static int TvMergeCommand(Options o)
{
    var draft = o.Get("draft") ?? throw new ArgumentException("--draft <x.draft.json> を渡してください。");
    var extras = o.Get("extras") ?? draft.Replace(".draft.json", ".extras.json", StringComparison.Ordinal);
    var output = o.Get("out") ?? draft.Replace(".draft.json", ".script.json", StringComparison.Ordinal);
    var dropped = new List<string>();
    File.WriteAllText(output, TvDraft.Merge(File.ReadAllText(draft), File.ReadAllText(extras), dropped));
    foreach (var line in dropped)
    {
        Console.WriteLine($"入らなかった台詞: {line}");
    }

    Console.WriteLine(output);
    return 0;
}

// テレビ中継の台本の点検（主語・長さ・言わない言葉・言い直し・掛け合い・コール・ずれ）。見つからなければ「OK」、あれば 1 行ずつ。
static int TvLintCommand(Options o)
{
    var script = o.Get("script") ?? throw new ArgumentException("--script <x.script.json> を渡してください。");
    var events = o.Get("events") ?? script.Replace(".script.json", ".events.json", StringComparison.Ordinal);
    var manifest = o.Get("manifest") is { } m && File.Exists(m) ? File.ReadAllText(m) : null;
    var issues = TvLint.Check(File.ReadAllText(script), File.ReadAllText(events), manifest, OrgOption(o));
    foreach (var issue in issues)
    {
        Console.WriteLine(string.Create(CultureInfo.InvariantCulture, $"{issue.At,6:0.0}s [{issue.Rule}] {issue.Message}"));
    }

    Console.WriteLine(issues.Count == 0 ? "OK" : $"{issues.Count} issues");
    return 0;
}

// テレビ中継の台本を書くための要約（events.json → Markdown）。実況・解説の台本を書く人（エージェント）はこれを読む。
static int TvDigestCommand(Options o)
{
    var events = o.Get("events") ?? throw new ArgumentException("--events <x.events.json> を渡してください。");
    var output = o.Get("out") ?? Path.ChangeExtension(events, null).Replace(".events", "", StringComparison.Ordinal) + ".digest.md";
    File.WriteAllText(output, TvDigest.Build(File.ReadAllText(events), OrgOption(o)));
    Console.WriteLine(output);
    return 0;
}

// 中継の主催団体（--org wvc|vfc）。指定が無ければ null（動画・下見は WVC、台本の道具は記録の org に従う）。
static Organization? OrgOption(Options o) => o.Get("org") is { } id ? Organizations.Get(id) : null;

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

static Contender LoadWith(string nameOrPath, IReadOnlyDictionary<string, float> set)
    => set.Count == 0 ? Load(nameOrPath) : Contender.Of(StyleSet.Apply(LoadStyle(nameOrPath), set));

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

// 名簿の肖像（メニューの顔・全身の絵）を PNG に（--out フォルダ、--scale 1 ピクセルの拡大、既定 4）。
static int Portraits(Options o) => PortraitExport.Run(o.Get("out") ?? "portraits", MiniRing.Characters.CharacterRoster.All, o.Int("scale", 4, min: 1));

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

static int WeaponExport(Options o)
{
    var from = o.Get("from") ?? "current";
    return from is "current" or "generated"
        ? VoxelTool.ExportWeapons(o.Get("out") ?? "weapons.vox", generated: from == "generated")
        : throw new ArgumentException($"--from は current か generated です（{from}）。");
}

static int WeaponCheck(Options o)
{
    var input = o.Get("in") ?? throw new ArgumentException("--in に直した .vox を渡してください。");
    var install = o.Get("install") ?? "no";
    return install is "yes" or "no" ? VoxelTool.CheckWeapons(input, install == "yes") : throw new ArgumentException($"--install は yes か no です（{install}）。");
}

static int KickShinCommand(Options o)
{
    var logPath = o.Get("log");
    void Log(string line)
    {
        Console.WriteLine(line);
        if (logPath is not null)
        {
            File.AppendAllText(logPath, line + "\n");
        }
    }

    var kind = Enum.Parse<MiniRing.Bots.KickTechnique>(o.Get("kick") ?? "Roundhouse", ignoreCase: true);
    var spec = MiniRing.Bots.KickSpec.Of(kind);
    if (o.Get("diag") is not null)
    {
        KickShin.Diag(o.Get("body") ?? "standard", spec, o.Float("standoff", spec.Standoff), o.Float("seconds", spec.Seconds ?? 0.65f), Log);
        return 0;
    }

    var wanted = o.List("heights", ["low", "mid", "high"]);
    var heights = KickShin.Heights.Where(h => wanted.Contains(h.File.Replace("roundhouse_", "", StringComparison.Ordinal))).ToList();
    if (o.Get("video") is { } videoOut)
    {
        KickShin.Video(o.Get("dir") ?? "data/mocap/kyokushin", videoOut, o.Get("body") ?? "taeyun", o.Get("victim") ?? "standard", heights, Log);
        return 0;
    }

    KickShin.Run(heights,o.Get("practice") is "yes", o.Int("gens", 8, min: 1), o.Int("lambda", 12, min: 4), o.Int("parallel", 6, min: 1), o.Get("json"), Log, o.Int("seed", 7, min: 1));
    return 0;
}

static int Usage(string message)
{
    Console.Error.WriteLine(message);
    Console.Error.WriteLine("commands: " + string.Join(' ', UsageText.Commands()));
    Console.Error.WriteLine("usage of one command: miniring-arena help <command>");
    return 2;
}

static int Help(string? command)
{
    if (command is null)
    {
        Console.WriteLine(UsageText.All);
        return 0;
    }

    var entry = UsageText.Of(command);
    if (entry is null)
    {
        return Usage($"知らないコマンド: {command}");
    }

    Console.WriteLine(entry);
    return 0;
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
