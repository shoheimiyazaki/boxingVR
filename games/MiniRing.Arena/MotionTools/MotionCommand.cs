using System.Diagnostics;
using System.Globalization;
using MiniRing.Bots;
using MiniRing.Characters;
using MiniRing.Motion;
using MiniRing.Replay;
using MiniRing.Weapons;

namespace MiniRing.Arena.MotionTools;

/// <summary>
/// motioncard / motionaudit（docs/plans/MOTION_TOOLS_PLAN.md）: 場面を 1 つ回して動きを記録し（<see cref="MotionTrace"/>）、不自然な瞬間を一覧にし
/// （<see cref="MotionAudit"/>）、言葉の説明（<see cref="MotionWords"/>）と自然さの物差しを添えて JSON に書き、tools/motioncard.py で 1 枚の絵にする。
/// 場面は今ある道具の 1 回の試行をそのまま使う（剣は StrikeDrill、蹴りは KickMocap.Doll、鎌は ClawLab.Run、試合・リプレイは BoxingGame）。
/// </summary>
internal static class MotionCommand
{
    private const float Dt = 1f / 240f;

    public static readonly string[] OptionNames =
    [
        "scene", "style", "weapon", "form", "kick", "body", "victim", "rear", "opponent", "aim", "hand", "red", "blue", "red-char", "blue-char",
        "gap", "who", "seed", "seconds", "replay", "from", "to", "hit", "out", "card", "audit-cards", "every",
    ];

    public static int Run(Options o, bool audit)
    {
        ArgumentNullException.ThrowIfNull(o);
        var scene = o.Get("scene") ?? (o.Get("replay") is not null ? "replay" : "jab");
        var seed = o.Int("seed", 1);
        var trace = scene switch
        {
            "jab" or "punch" => Bout(o, seed, punchOnly: true),
            "bout" => Bout(o, seed, punchOnly: false),
            "sword" => Sword(o),
            "kick" => Kick(o, seed),
            "claw" => Claw(o, seed),
            "replay" => Replay(o),
            _ => throw new ArgumentException($"--scene は jab / bout / sword / kick / claw / replay のどれかです（{scene}）。"),
        };

        var findings = MotionAudit.Run(trace);
        var captions = MotionWords.Captions(trace);
        var (from, to) = Window(o, trace, scene);
        var hand = o.Get("hand") ?? (scene is "jab" or "punch" ? "Left" : null);
        var natural = MotionWords.Natural(trace, from, to, hand is null || scene == "claw" ? null : hand.StartsWith('L') || hand.StartsWith('l') ? "handL" : "handR");
        var output = o.Get("out") ?? Path.Combine("media_tmp", "motion", $"{scene}_{trace.Subject}.motion.json");
        if (!output.EndsWith(".json", StringComparison.OrdinalIgnoreCase))
        {
            output += ".motion.json";
        }

        var c = CultureInfo.InvariantCulture;
        trace.Save(output, findings, captions, new Dictionary<string, string>
        {
            ["windowFrom"] = from.ToString("0.000", c),
            ["windowTo"] = to.ToString("0.000", c),
            ["natural"] = natural.Format(),
            ["command"] = (audit ? "motionaudit " : "motioncard ") + string.Join(' ', Environment.GetCommandLineArgs().Skip(2)),
        });
        Console.WriteLine(trace.Summary());
        Console.WriteLine($"version physics {Fighter.PhysicsVersion} logic {BoxerBot.LogicVersion}, seed {trace.Seed}");
        Console.WriteLine(string.Create(c, $"window {from:0.00}-{to:0.00} s"));
        Console.WriteLine(natural.Format());
        Console.WriteLine(MotionAudit.Format(findings));
        Console.WriteLine(output);
        if (o.Get("card") is not ("no" or "0" or "off"))
        {
            Card(output, audit ? o.Int("audit-cards", 6, min: 0) : 0);
        }

        return 0;
    }

    /// <summary>tools/motioncard.py を呼ぶ（Python と matplotlib が要る）。</summary>
    private static void Card(string json, int auditCards)
    {
        var script = Path.Combine("tools", "motioncard.py");
        if (!File.Exists(script))
        {
            Console.WriteLine($"{script} が見つかりません（リポジトリの根で実行してください）。絵は作りません。");
            return;
        }

        var start = new ProcessStartInfo("py", $"-3 \"{script}\" \"{json}\" --audit-cards {auditCards}") { UseShellExecute = false, RedirectStandardOutput = true, RedirectStandardError = true };
        using var process = Process.Start(start) ?? throw new IOException("py を起動できません（Python 3 が PATH にありますか）。");
        Console.Write(process.StandardOutput.ReadToEnd());
        var error = process.StandardError.ReadToEnd();
        process.WaitForExit();
        if (process.ExitCode != 0)
        {
            throw new IOException($"motioncard.py が失敗しました: {error}");
        }
    }

    /// <summary>絵と自然さを測る区間: --from/--to、無ければ N 回目の当たり（--hit N、--hand で手を絞る）の前 0.7 s〜後 0.4 s、当たりが無ければ末端の速さの山の前後。</summary>
    private static (float From, float To) Window(Options o, MotionTrace trace, string scene)
    {
        var frames = trace.Frames;
        if (o.Get("from") is not null || o.Get("to") is not null)
        {
            return (o.Float("from", frames[0].Time), o.Float("to", frames[^1].Time));
        }

        var hand = o.Get("hand") ?? (scene is "jab" or "punch" ? "Left" : null);
        var nth = o.Int("hit", 1, min: 1);
        var hits = frames.Where(f => f.Hit is { } h && (hand is null || h.Contains(hand + ":", StringComparison.OrdinalIgnoreCase))).ToList();
        float at;
        if (hits.Count >= nth)
        {
            at = hits[nth - 1].Time;
        }
        else
        {
            var names = new[] { "handL", "handR", "tip", "toeL", "toeR" };
            var speeds = names.Select(n => MotionMetrics.Speeds(trace.Series(n), trace.Dt)).ToList();
            var best = Enumerable.Range(0, frames.Count).MaxBy(i => speeds.Max(s => s[i]) * (frames[i].Down ? 0f : 1f));
            at = frames[best].Time;
        }

        return (MathF.Max(frames[0].Time, at - 0.7f), MathF.Min(frames[^1].Time, at + 0.4f));
    }

    private static FighterCharacter? Character(string? id) => id is null ? null
        : id.EndsWith(".json", StringComparison.OrdinalIgnoreCase) ? CharacterRoster.LoadFile(id) : CharacterRoster.Get(id);

    /// <summary>
    /// 試合の場面: --scene jab は赤（--style、既定 champion）がその場で構えて立つ相手を打つ。--scene bout は --red/--blue（スタイル）か --red-char/--blue-char
    /// （名簿のキャラ）の試合。記録するのは --who（red / blue、既定 red）。
    /// </summary>
    private static MotionTrace Bout(Options o, int seed, bool punchOnly)
    {
        var seconds = o.Float("seconds", punchOnly ? 6f : 20f, min: 0.5f);
        BoxingCorners corners;
        string label;
        if (punchOnly)
        {
            var style = BotCatalog.Style(o.Get("style") ?? "champion");
            corners = new BoxingCorners("stand", _ => new StandGuard(), s => BotCatalog.Bot(style, s), PlayerBody: style.Build, PlayerFace: style.Face);
            label = $"jab {style.Name} vs stand";
        }
        else
        {
            var redChar = Character(o.Get("red-char"));
            var blueChar = Character(o.Get("blue-char"));
            if (redChar is not null || blueChar is not null)
            {
                var red = redChar is not null ? RosterLeague.ContenderOf(redChar, blueChar?.Id, null) : throw new ArgumentException("--red-char と --blue-char は両方渡してください。");
                var blue = blueChar is not null ? RosterLeague.ContenderOf(blueChar, redChar.Id, null) : throw new ArgumentException("--red-char と --blue-char は両方渡してください。");
                corners = VideoMaker.WithCharacters(VideoMaker.Corners(red, blue, seed, redChar.Wear, blueChar.Wear, redChar.Weapon, blueChar.Weapon, s => blue.Create(s), s => red.Create(s), referee: false), redChar, blueChar);
                label = $"bout {redChar.Id} vs {blueChar.Id}";
            }
            else
            {
                var red = BotCatalog.Style(o.Get("red") ?? "champion");
                corners = BotCatalog.Corners(o.Get("blue") ?? "swarmer") with { RedBotStyle = red };
                corners = corners.StartingAtCorners();
                label = $"bout {red.Name} vs {o.Get("blue") ?? "swarmer"}";
            }
        }

        using var game = new BoxingGame(seed: seed, corners: corners);
        var me = o.Get("who") == "blue" ? game.Cpu : game.Player;
        var trace = new MotionTrace(label, me == game.Player ? "red" : "blue", seed, me.LegLength, me.ArmReach, o.Int("every", 2, min: 1));
        var steps = (int)MathF.Round(seconds / Dt);
        for (var i = 0; i < steps; i++)
        {
            game.Step(default, Dt);
            trace.Record(game, me, (i + 1) * Dt);
        }

        return trace;
    }

    /// <summary>剣の型を構えて立つ相手へ 1 回（StrikeDrill と同じ。--weapon katana / longsword / bat、--form 型の名前、既定 katana の kesa）。</summary>
    private static MotionTrace Sword(Options o)
    {
        var weapon = Enum.Parse<WeaponKind>(o.Get("weapon") ?? "katana", ignoreCase: true);
        var name = o.Get("form") ?? "kesa";
        var motion = FormSwordsman.Forms(weapon).FirstOrDefault(f => f.Name == name)
            ?? throw new ArgumentException($"{weapon} に型 {name} がありません（{string.Join(", ", FormSwordsman.Forms(weapon).Select(f => f.Name))}）。");
        float armReach;
        using (var probe = new BoxingGame(SwordStance.Tuning))
        {
            armReach = probe.Player.ArmReach;
        }

        var technique = new MotionTechnique(motion.Name, weapon, motion, 0f);
        technique = technique with { Range = StrikeDrill.ReachOf(technique, armReach) };
        MotionTrace? trace = null;
        StrikeDrill.Run(technique, FormSwordsman.Guard(weapon), SwordStance.Rate, SwordStance.Tuning, (game, t, _) =>
        {
            trace ??= new MotionTrace($"sword {weapon} {name} vs stand", "red", 1, game.Player.LegLength, game.Player.ArmReach, o.Int("every", 2, min: 1));
            trace.Record(game, game.Player, t);
        });
        return trace!;
    }

    /// <summary>蹴りを 1 本（KickShin の練習の前の差分 = 教科書。--kick roundhouse / roundhouse_low / roundhouse_high / front、--body・--victim は standard か名簿の id、--rear yes で後ろ足）。</summary>
    private static MotionTrace Kick(Options o, int seed)
    {
        var file = o.Get("kick") ?? "roundhouse_mid";
        var height = KickShin.Heights.FirstOrDefault(h => h.File == file || h.File == file + "_mid" || h.Technique.ToString().Equals(file, StringComparison.OrdinalIgnoreCase))
            ?? throw new ArgumentException($"--kick は {string.Join(" / ", KickShin.Heights.Select(h => h.File))} のどれかです。");
        var tune = KickShin.Baseline(height);
        var spec = KickSpec.Of(height.Technique) with { Tunes = new KickTunes(tune, tune) };
        var rear = o.Get("rear") is not ("no" or "0");
        static BodyBuild Body(string id) => id == "standard" ? BodyBuild.Standard : CharacterRoster.Get(id).Build;
        MotionTrace? trace = null;
        KickMocap.Doll(Body(o.Get("body") ?? "standard"), spec, rear ? Hand.Right : Hand.Left, tune.Standoff, tune.Seconds, seed, (game, _, t) =>
        {
            trace ??= new MotionTrace($"kick {height.File} {(rear ? "rear" : "front")} leg vs stand", "red", seed, game.Player.LegLength, game.Player.ArmReach, o.Int("every", 2, min: 1));
            trace.Record(game, game.Player, t);
        }, victimBody: Body(o.Get("victim") ?? "standard"));
        return trace!;
    }

    /// <summary>ガルヴァの鎌の 1 振り（ClawLab と同じ。--opponent、--aim 0 首 / 1 腕 / 2 胴、--hand left / right）。</summary>
    private static MotionTrace Claw(Options o, int seed)
    {
        var hand = (o.Get("hand") ?? "right").Equals("left", StringComparison.OrdinalIgnoreCase) ? Hand.Left : Hand.Right;
        MotionTrace? trace = null;
        ClawLab.Run(o.Get("opponent") ?? "jin", seed, o.Float("gap", 0.94f), o.Int("aim", 0, min: 0), hand, 0.31f, 1f, afterStep: (game, t) =>
        {
            trace ??= new MotionTrace($"claw galva {hand} aim {o.Int("aim", 0)} vs {o.Get("opponent") ?? "jin"}", "red", seed, game.Player.LegLength, game.Player.ArmReach, o.Int("every", 2, min: 1));
            trace.Record(game, game.Player, t);
        });
        return trace!;
    }

    /// <summary>記録（.mrrep）を今のビルドで再生しながら記録する（--who red / blue、--seconds で途中まで）。</summary>
    private static MotionTrace Replay(Options o)
    {
        var path = o.Get("replay") ?? throw new ArgumentException("--replay <x.mrrep> を渡してください。");
        using var player = new ReplayPlayer(ReplayFile.Read(path));
        var game = player.Game;
        var me = o.Get("who") == "blue" ? game.Cpu : game.Player;
        var trace = new MotionTrace($"replay {Path.GetFileName(path)}", me == game.Player ? "red" : "blue", 0, me.LegLength, me.ArmReach, o.Int("every", 2, min: 1));
        var limit = o.Float("seconds", float.MaxValue, min: 0.1f);
        while (!player.Finished && player.Time < limit)
        {
            player.StepOnce();
            trace.Record(game, me, player.Time);
        }

        if (player.FirstDivergence is { } step)
        {
            Console.WriteLine($"注意: 記録と今のビルドが {step} 歩目でずれた（別の版で撮った記録）。動きは今のビルドのもの。");
        }

        return trace;
    }
}
