using System.Globalization;
using System.Text.Json;
using MiniRing.Bots;
using MiniRing.Characters;

namespace MiniRing.Arena;

/// <summary>倒れてから終わるまでの 1 回（<see cref="GroundTime"/>）。</summary>
/// <param name="Victim">倒れた側（"red" / "blue"）。</param>
/// <param name="Start">ラウンドの始めからの時刻 [s]。</param>
/// <param name="Round">ラウンド。</param>
/// <param name="Seconds">倒れてから終わるまで [s]。</param>
/// <param name="End">終わり方: death（倒れた側が死んだ）・rose（立ち上がった）・bell（ラウンドが終わった）・other（倒れた側でない決着など）。</param>
/// <param name="AttackerStanding">倒れた瞬間に攻める側が立っていたか（倒れた相手を攻められる場面か）。</param>
/// <param name="AttackerDownShare">この間に攻める側も倒れていた時間の割合（床でもつれた割合）。</param>
/// <param name="Strikes">この間に攻める側が打ち始めた倒れた相手への攻めの数。</param>
/// <param name="Damage">この間に倒れた側が受けたダメージ（<see cref="BodyCondition.TotalDamage"/> の増分）。</param>
/// <param name="Moves">打ち始めた攻めの名前ごとの数。</param>
internal sealed record GroundEpisode(string Victim, float Start, int Round, float Seconds, string End, bool AttackerStanding, float AttackerDownShare,
    int Strikes, float Damage, IReadOnlyDictionary<string, int> Moves);

/// <summary>1 試合の結果（<see cref="GroundTime"/>）。</summary>
internal sealed record GroundBout(string Red, string Blue, int Seed, string Finish, bool Death, int Round, float RoundTime, IReadOnlyList<GroundEpisode> Episodes, string Versions)
{
    /// <summary>この試合の最初の「倒れた相手を攻められる場面」（攻める側が立っていた回）。無ければ null。</summary>
    public GroundEpisode? FirstChance => Episodes.FirstOrDefault(e => e.AttackerStanding);
}

/// <summary>
/// VFC（レフェリーなし・ダウンを数えない・死だけが決着）の名簿の組で、相手が倒れてから決着（死）・起き上がり・ゴングまでの時間を測る（2026-11-24）。
/// 両コーナーは中継の下見（<see cref="TvScout"/>）と同じ作り・同じ歩の幅。倒れた相手への攻め（<see cref="GroundStrike"/>）だけを変えた版どうしなら、
/// 最初に倒れる所までは同じ試合になるので、同じ組・同じ種で対にして比べる（<c>--compare</c>、<see cref="PairedEvaluation"/>）。
/// </summary>
internal static class GroundTime
{
    /// <summary>VFC の名簿（2026-11-24、org = vfc の 9 人）。</summary>
    public static readonly string[] VfcRoster = ["daichi", "edgar", "gaston", "jin", "joao", "konrad", "sean", "tsuyoshi", "vikram"];

    private static readonly JsonSerializerOptions Json = new() { WriteIndented = false };

    public static GroundBout Run(string redId, string blueId, int seed, int rounds, Organization org)
    {
        var redCharacter = CharacterRoster.Get(redId);
        var blueCharacter = CharacterRoster.Get(blueId);
        var red = RosterLeague.ContenderOf(redCharacter, blueId, null);
        var blue = RosterLeague.ContenderOf(blueCharacter, redId, null);
        var rules = TvShowRules.Rules(rounds, MiniRing.MatchRules.Vfc.BreakSeconds, org);
        // 歩の幅は中継の下見と同じ（30 fps・0.75 倍の速さ → 1/80 s を 1/60 s 以下に割る）。
        var frameDt = MiniRing.GameSpeed.Standard / 30f;
        var stepsPerFrame = Math.Max(1, (int)MathF.Ceiling((frameDt / (1f / 60f)) - 1e-3f));
        var stepDt = frameDt / stepsPerFrame;
        var corners = TvShowRules.Stage(VideoMaker.WithCharacters(
            VideoMaker.Corners(red, blue, seed, redCharacter.Wear, blueCharacter.Wear, redCharacter.Weapon, blueCharacter.Weapon, s => blue.Create(s), s => red.Create(s), referee: false),
            redCharacter, blueCharacter), org);
        using var game = new BoxingGame(seed: seed, corners: corners, rules: rules);
        var episodes = new List<GroundEpisode>();
        var open = new Dictionary<string, Open>();
        var time = 0f;
        var roundClock = 0f;
        var lastRound = game.MatchRound;
        while (!game.MatchOver && time < rules.TotalSeconds + 8.392f)
        {
            game.Step(default, stepDt);
            time += stepDt;
            if (game.MatchRound != lastRound)
            {
                lastRound = game.MatchRound;
                roundClock = 0f;
            }

            roundClock += game.Resting ? 0f : stepDt;
            Track("red", game.Player, game.Cpu, game.CpuControl as GroundStriker);
            Track("blue", game.Cpu, game.Player, game.PlayerControl as GroundStriker);
        }

        // 試合の終わりで閉じていない回を閉じる。
        foreach (var (victim, o) in open)
        {
            var self = victim == "red" ? game.Player : game.Cpu;
            Close(victim, o, self.IsDead ? "death" : "other");
        }

        return new GroundBout(redId, blueId, seed, game.Decision ?? game.Message, game.Decision is null && TvScoutResult.IsDeath(game.Message),
            game.MatchRound, rules.RoundSeconds - game.RoundLeft, episodes, $"physics {Fighter.PhysicsVersion} logic {BoxerBot.LogicVersion}");

        void Track(string victim, Fighter self, Fighter attacker, GroundStriker? striker)
        {
            var down = self.State != FighterState.Standing && !game.Resting;
            var started = striker?.Ground.Started ?? 0;
            if (open.TryGetValue(victim, out var o))
            {
                // 攻める側の入力元はラウンドごとに作り直される（数え直す）。
                if (!ReferenceEquals(striker, o.Striker))
                {
                    o = o with { Striker = striker, LastStarted = 0 };
                }

                if (started > o.LastStarted && striker?.Ground.CurrentName is { } name)
                {
                    o.Moves[name] = o.Moves.GetValueOrDefault(name) + (started - o.LastStarted);
                }

                o = o with
                {
                    Seconds = o.Seconds + (down ? stepDt : 0f),
                    AttackerDown = o.AttackerDown + (down && attacker.State != FighterState.Standing ? stepDt : 0f),
                    Strikes = o.Strikes + Math.Max(0, started - o.LastStarted),
                    LastStarted = started,
                };
                open[victim] = o;
                if (self.IsDead)
                {
                    Close(victim, o, "death");
                }
                else if (self.State == FighterState.Standing)
                {
                    Close(victim, o, "rose");
                }
                else if (game.Resting)
                {
                    Close(victim, o, game.MatchOver ? "other" : "bell");
                }

                return;
            }

            if (down && !self.IsDead)
            {
                open[victim] = new Open(game.MatchRound, roundClock, 0f, 0f, attacker.State == FighterState.Standing, 0, started, striker, self.Condition.TotalDamage, []);
            }
        }

        void Close(string victim, Open o, string end)
        {
            var self = victim == "red" ? game.Player : game.Cpu;
            episodes.Add(new GroundEpisode(victim, o.Start, o.Round, o.Seconds, end, o.AttackerStanding, o.Seconds > 0f ? o.AttackerDown / o.Seconds : 0f,
                o.Strikes, self.Condition.TotalDamage - o.Damage0, o.Moves));
            open.Remove(victim);
        }
    }

    private sealed record Open(int Round, float Start, float Seconds, float AttackerDown, bool AttackerStanding, int Strikes, int LastStarted,
        GroundStriker? Striker, float Damage0, Dictionary<string, int> Moves);

    /// <summary>組（赤・青）の一覧: 名簿の全員の総当たり（並びの前が赤）。</summary>
    public static IReadOnlyList<(string Red, string Blue)> Pairs(IReadOnlyList<string> chars)
    {
        var pairs = new List<(string, string)>();
        for (var i = 0; i < chars.Count; i++)
        {
            for (var j = i + 1; j < chars.Count; j++)
            {
                pairs.Add((chars[i], chars[j]));
            }
        }

        return pairs;
    }

    public static string ToJson(GroundBout bout) => JsonSerializer.Serialize(bout, Json);

    public static IReadOnlyList<GroundBout> Read(string path)
        => [.. File.ReadAllLines(path).Where(l => l.Trim().Length > 0).Select(l => JsonSerializer.Deserialize<GroundBout>(l, Json)!)];

    /// <summary>試合の束の要約（全部の回・最初の場面）を Markdown の表の行にする。</summary>
    public static string Summary(string label, IReadOnlyList<GroundBout> bouts)
    {
        var chances = bouts.SelectMany(b => b.Episodes).Where(e => e.AttackerStanding).ToList();
        var all = bouts.SelectMany(b => b.Episodes).ToList();
        var deaths = chances.Count(e => e.End == "death");
        var rose = chances.Count(e => e.End == "rose");
        var bell = chances.Count(e => e.End == "bell");
        var strikes = chances.Sum(e => e.Strikes);
        var tangled = chances.Sum(e => e.Seconds) > 0f ? chances.Sum(e => e.Seconds * e.AttackerDownShare) / chances.Sum(e => e.Seconds) : 0f;
        var matchDeaths = bouts.Count(b => b.Death);
        return string.Create(CultureInfo.InvariantCulture,
            $"| {label} | {bouts.Count} | {matchDeaths} ({100f * matchDeaths / Math.Max(1, bouts.Count):0}%) | {chances.Count} / {all.Count} | {Median(chances.Select(e => e.Seconds)):0.0} | {Median(chances.Where(e => e.End == "death").Select(e => e.Seconds)):0.0} | {deaths} ({100f * deaths / Math.Max(1, chances.Count):0}%) | {rose} | {bell} | {(chances.Count > 0 ? (float)strikes / chances.Count : 0f):0.0} | {(chances.Sum(e => e.Seconds) > 0f ? strikes / chances.Sum(e => e.Seconds) : 0f):0.00} | {100f * tangled:0}% |");
    }

    public const string SummaryHeader = "| 版 | 試合 | 死で決着 | 攻められる回 / 倒れた回 | 倒れてから終わるまで 中央値 [s] | 死までの中央値 [s] | 死 | 起きた | ゴング | 打ち始め / 回 | 打ち始め / s | 攻め手も倒れていた割合 |\n|---|---|---|---|---|---|---|---|---|---|---|---|";

    /// <summary>攻めの名前ごとの打ち始めの数（全部の攻められる回）。</summary>
    public static string MoveCounts(IReadOnlyList<GroundBout> bouts)
    {
        var counts = new SortedDictionary<string, int>(StringComparer.Ordinal);
        foreach (var e in bouts.SelectMany(b => b.Episodes).Where(e => e.AttackerStanding))
        {
            foreach (var (k, v) in e.Moves)
            {
                counts[k] = counts.GetValueOrDefault(k) + v;
            }
        }

        return string.Join(", ", counts.Select(kv => $"{kv.Key} {kv.Value}"));
    }

    public static float Median(IEnumerable<float> values)
    {
        var v = values.OrderBy(x => x).ToArray();
        return v.Length == 0 ? float.NaN : v.Length % 2 == 1 ? v[v.Length / 2] : 0.5f * (v[(v.Length / 2) - 1] + v[v.Length / 2]);
    }

    /// <summary>
    /// 前（<paramref name="before"/>）と後（<paramref name="after"/>）を同じ組・同じ種で対にして比べる。点は試合ごとに、最初の攻められる場面の
    /// 「倒れてから終わるまでの秒（ゴングで切れたらそこまで）」と「死で終わったか（1/0）」。どちらの版でも場面があった試合だけを対にする。
    /// </summary>
    public static (PairedResult Seconds, PairedResult Finished, int N) Compare(IReadOnlyList<GroundBout> before, IReadOnlyList<GroundBout> after)
    {
        var map = before.ToDictionary(b => (b.Red, b.Blue, b.Seed));
        var a = new List<(GroundEpisode After, GroundEpisode Before)>();
        foreach (var b in after)
        {
            if (map.TryGetValue((b.Red, b.Blue, b.Seed), out var old) && b.FirstChance is { } x && old.FirstChance is { } y)
            {
                a.Add((x, y));
            }
        }

        var seconds = PairedEvaluation.Compute([.. a.Select(p => (double)p.After.Seconds)], [.. a.Select(p => (double)p.Before.Seconds)]);
        var finished = PairedEvaluation.Compute([.. a.Select(p => p.After.End == "death" ? 1d : 0d)], [.. a.Select(p => p.Before.End == "death" ? 1d : 0d)]);
        return (seconds, finished, a.Count);
    }
}
