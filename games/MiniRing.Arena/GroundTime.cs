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

/// <summary>
/// 立っている相手に来たチャンスの 1 回（<see cref="GroundTime"/>、2026-11-24）。チャンスは測る側の物差しで、受け手の体の量から決める（ボットの判断には使わない）:
/// 両者が立っていて、受け手のスタミナが <see cref="GroundTime.ChanceStamina"/> 未満・意識が <see cref="GroundTime.ChanceConsciousness"/> 未満・
/// 見えている傷み（<see cref="Fighter.Injury"/>）が <see cref="GroundTime.ChanceInjury"/> 以上のどれか。
/// </summary>
/// <param name="Victim">チャンスを作った側（"red" / "blue"）。</param>
/// <param name="Round">ラウンド。</param>
/// <param name="Start">ラウンドの始めからの時刻 [s]。</param>
/// <param name="Seconds">始まりから終わりまで [s]。</param>
/// <param name="End">down（受け手が倒れた・死んだ: 決めた）・escaped（チャンスでなくなって <see cref="GroundTime.EscapeSeconds"/> 続いた）・bell・other。</param>
/// <param name="Damage">この間に受け手が受けたダメージ。</param>
internal sealed record ChanceEpisode(string Victim, int Round, float Start, float Seconds, string End, float Damage);

/// <summary>1 試合の結果（<see cref="GroundTime"/>）。</summary>
internal sealed record GroundBout(string Red, string Blue, int Seed, string Finish, bool Death, int Round, float RoundTime, IReadOnlyList<GroundEpisode> Episodes, string Versions)
{
    /// <summary>立っている相手に来たチャンスの場面（2026-11-24、フィニッシュの物差し。古い記録には無い）。</summary>
    public IReadOnlyList<ChanceEpisode>? Chances { get; init; }

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

    /// <summary>チャンスの物差し: 受け手のスタミナがこれ未満。</summary>
    public const float ChanceStamina = 0.25f;

    /// <summary>チャンスの物差し: 受け手の意識がこれ未満（打たれて薄れている）。</summary>
    public const float ChanceConsciousness = 0.7f;

    /// <summary>チャンスの物差し: 受け手の見えている傷み（折れた腕・削れた筋）がこれ以上。</summary>
    public const float ChanceInjury = 0.25f;

    /// <summary>チャンスの物差しを外れてこれだけ続いたら、逃げられたとみなす [s]。</summary>
    public const float EscapeSeconds = 1.678f;

    public static GroundBout Run(string redId, string blueId, int seed, int rounds, Organization org, IReadOnlyDictionary<string, float>? set = null)
    {
        var redCharacter = CharacterRoster.Get(redId);
        var blueCharacter = CharacterRoster.Get(blueId);
        // set: 両者のスタイルの値の上書き（--set GroundHeel=0 などで、倒れた相手への攻めを全員そろえて比べる）。
        var red = RosterLeague.ContenderOf(redCharacter, blueId, set);
        var blue = RosterLeague.ContenderOf(blueCharacter, redId, set);
        var rules = TvShowRules.Rules(rounds, org.Match.Referee ? MiniRing.MatchRules.Tv.BreakSeconds : MiniRing.MatchRules.Vfc.BreakSeconds, org);
        // 歩の幅は中継の下見と同じ（30 fps・0.75 倍の速さ → 1/80 s を 1/60 s 以下に割る）。
        var frameDt = MiniRing.GameSpeed.Standard / 30f;
        var stepsPerFrame = Math.Max(1, (int)MathF.Ceiling((frameDt / (1f / 60f)) - 1e-3f));
        var stepDt = frameDt / stepsPerFrame;
        var corners = TvShowRules.Stage(VideoMaker.WithCharacters(
            VideoMaker.Corners(red, blue, seed, redCharacter.Wear, blueCharacter.Wear, redCharacter.Weapon, blueCharacter.Weapon, s => blue.Create(s), s => red.Create(s), referee: org.Match.Referee),
            redCharacter, blueCharacter), org);
        using var game = new BoxingGame(seed: seed, corners: corners, rules: rules);
        var episodes = new List<GroundEpisode>();
        var open = new Dictionary<string, Open>();
        var chances = new List<ChanceEpisode>();
        var chanceOpen = new Dictionary<string, (int Round, float Start, float Seconds, float Off, float Damage0)>();
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
            TrackChance("red", game.Player, game.Cpu);
            TrackChance("blue", game.Cpu, game.Player);
        }

        foreach (var (victim, c) in chanceOpen)
        {
            var self = victim == "red" ? game.Player : game.Cpu;
            chances.Add(new ChanceEpisode(victim, c.Round, c.Start, c.Seconds, self.IsDead || self.State != FighterState.Standing ? "down" : "other", self.Condition.TotalDamage - c.Damage0));
        }

        // 試合の終わりで閉じていない回を閉じる。
        foreach (var (victim, o) in open)
        {
            var self = victim == "red" ? game.Player : game.Cpu;
            Close(victim, o, self.IsDead ? "death" : "other");
        }

        return new GroundBout(redId, blueId, seed, game.Decision ?? game.Message, game.Decision is null && TvScoutResult.IsDeath(game.Message),
            game.MatchRound, rules.RoundSeconds - game.RoundLeft, episodes, $"physics {Fighter.PhysicsVersion} logic {BoxerBot.LogicVersion}") { Chances = chances };

        void TrackChance(string victim, Fighter self, Fighter attacker)
        {
            var isOpen = chanceOpen.TryGetValue(victim, out var c);
            var chance = !game.Resting && self.State == FighterState.Standing && attacker.State == FighterState.Standing && !self.IsDead
                && (self.Stamina < ChanceStamina || self.Consciousness < ChanceConsciousness || self.Injury(felt: false) >= ChanceInjury);
            if (!isOpen)
            {
                if (chance)
                {
                    chanceOpen[victim] = (game.MatchRound, roundClock, 0f, 0f, self.Condition.TotalDamage);
                }

                return;
            }

            string? end = self.IsDead || self.State != FighterState.Standing ? "down" : game.Resting ? (game.MatchOver ? "other" : "bell") : null;
            c = (c.Round, c.Start, c.Seconds + stepDt, chance ? 0f : c.Off + stepDt, c.Damage0);
            end ??= c.Off >= EscapeSeconds ? "escaped" : null;
            if (end is null)
            {
                chanceOpen[victim] = c;
                return;
            }

            chances.Add(new ChanceEpisode(victim, c.Round, c.Start, end == "escaped" ? c.Seconds - c.Off : c.Seconds, end, self.Condition.TotalDamage - c.Damage0));
            chanceOpen.Remove(victim);
        }

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
    /// <summary>
    /// 試合ごとの比べ（2026-11-24、比べ直しの種を見る前に決めた物差し）: 倒れた相手が倒れたまま死んで決着したか（1/0）と、攻め手が立っていて相手が倒れていた秒の合計
    /// （床の時間。速く殺せば試合も早く終わり、短くなる）。同じ組・同じ種の試合を対にする。
    /// </summary>
    public static (PairedResult KilledOnGround, PairedResult GroundSeconds, int N) CompareBouts(IReadOnlyList<GroundBout> before, IReadOnlyList<GroundBout> after)
    {
        var map = before.ToDictionary(b => (b.Red, b.Blue, b.Seed));
        var pairs = after.Where(b => map.ContainsKey((b.Red, b.Blue, b.Seed))).Select(b => (A: b, B: map[(b.Red, b.Blue, b.Seed)])).ToList();
        static double Killed(GroundBout b) => b.Episodes.Any(e => e.AttackerStanding && e.End == "death") ? 1d : 0d;
        static double Floor(GroundBout b) => b.Episodes.Where(e => e.AttackerStanding).Sum(e => (double)e.Seconds);
        return (PairedEvaluation.Compute([.. pairs.Select(p => Killed(p.A))], [.. pairs.Select(p => Killed(p.B))]),
            PairedEvaluation.Compute([.. pairs.Select(p => Floor(p.A))], [.. pairs.Select(p => Floor(p.B))]), pairs.Count);
    }

    /// <summary>チャンスの場面の要約: 回数・決めた（倒した）割合・決めるまでの中央値・すべての回の中央値。</summary>
    public static string ChanceSummary(string label, IReadOnlyList<GroundBout> bouts)
    {
        var c = bouts.SelectMany(b => b.Chances ?? []).ToList();
        var down = c.Where(x => x.End == "down").ToList();
        return string.Create(CultureInfo.InvariantCulture,
            $"| {label} | {c.Count} | {down.Count} ({100f * down.Count / Math.Max(1, c.Count):0}%) | {Median(down.Select(x => x.Seconds)):0.0} | {Median(c.Select(x => x.Seconds)):0.0} | {c.Count(x => x.End == "escaped")} | {c.Count(x => x.End == "bell")} | {(c.Count > 0 ? c.Average(x => x.Damage) : 0f):0.0} |");
    }

    public static readonly string ChanceHeader = "| 版 | チャンス | 倒した（決めた） | 決めるまでの中央値 [s] | すべての回の中央値 [s] | 逃げられた | ゴング | 1 回のダメージ |" + Environment.NewLine + "|---|---|---|---|---|---|---|---|";

    /// <summary>
    /// 試合ごとのチャンスの比べ（同じ組・同じ種、両方の版でチャンスのあった試合）: その試合のチャンスのうち倒した割合と、最初のチャンスが終わるまでの秒。
    /// </summary>
    public static (PairedResult DownShare, PairedResult FirstSeconds, int N) CompareChances(IReadOnlyList<GroundBout> before, IReadOnlyList<GroundBout> after)
    {
        var map = before.ToDictionary(b => (b.Red, b.Blue, b.Seed));
        var pairs = after.Where(b => map.TryGetValue((b.Red, b.Blue, b.Seed), out var o) && (b.Chances?.Count ?? 0) > 0 && (o.Chances?.Count ?? 0) > 0)
            .Select(b => (A: b, B: map[(b.Red, b.Blue, b.Seed)])).ToList();
        static double Share(GroundBout b) => b.Chances!.Count(x => x.End == "down") / (double)b.Chances!.Count;
        static double First(GroundBout b) => b.Chances![0].Seconds;
        return (PairedEvaluation.Compute([.. pairs.Select(p => Share(p.A))], [.. pairs.Select(p => Share(p.B))]),
            PairedEvaluation.Compute([.. pairs.Select(p => First(p.A))], [.. pairs.Select(p => First(p.B))]), pairs.Count);
    }

    /// <summary>3 秒を超えて倒れていた（滑って手をついただけでない）攻められる場面の、終わるまでの中央値と死の割合。</summary>
    public static string LongDowns(IReadOnlyList<GroundBout> bouts)
    {
        var e = bouts.SelectMany(b => b.Episodes).Where(x => x.AttackerStanding && x.Seconds > 3f).ToList();
        return string.Create(CultureInfo.InvariantCulture, $"{e.Count} 回・中央値 {Median(e.Select(x => x.Seconds)):0.0} s・死 {e.Count(x => x.End == "death")}（{100f * e.Count(x => x.End == "death") / Math.Max(1, e.Count):0}%）・起きた {e.Count(x => x.End == "rose")}・ゴング {e.Count(x => x.End == "bell")}");
    }

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

/// <summary>とどめのドリルの 1 本（<see cref="FinishDrill"/>）。</summary>
/// <param name="Attacker">攻め手（名簿の id）。</param>
/// <param name="Victim">受け手（名簿の id。気を失って倒れたまま、武器は持たない）。</param>
/// <param name="Seed">種。</param>
/// <param name="Killed">受け手が死んだか。</param>
/// <param name="Seconds">受け手が倒れてから死ぬまで [s]（死ななければ上限の秒）。</param>
/// <param name="Cause">死因（死んだとき）。</param>
/// <param name="AttackerFalls">攻め手が倒れた回数。</param>
/// <param name="Strikes">打ち始めた数。</param>
/// <param name="Moves">打ち始めた攻めの名前ごとの数。</param>
/// <param name="Cells">受け手の体から欠けたセルの数（倒れてから）。死ななかった本の進み具合の目安。</param>
internal sealed record FinishRun(string Attacker, string Victim, int Seed, bool Killed, float Seconds, string Cause, int AttackerFalls, int Strikes, IReadOnlyDictionary<string, int> Moves, int Cells);

/// <summary>
/// とどめのドリル（2026-11-24）: VFC の名簿のキャラ（体・教育・武器・はめる物）が攻め手、名簿のキャラの体の受け手を気絶させて倒しておき、
/// 倒れてから死ぬまでの時間を測る。檻・レフェリーなしは試合と同じ。教育の値（経路・好み）を変えて練習するのに使う。
/// </summary>
internal static class FinishDrill
{
    private const float StepDt = 1f / 60f;

    /// <summary>受け手を気絶させる時刻 [s] と、その脳しんとう（GroundLab と同じ値）。</summary>
    private const float KnockAt = 2.518f;

    private const float Out = 0.97f;

    private static BotStyle Still => BotCatalog.Boxer with { Attacks = false, DefenseRate = 0f, Prediction = 0f, Aggression = 0f, FinishTempo = 0f, FinishPress = 0f, FinishSpend = 0f };

    /// <summary>攻め手の教育を差し替える: スタイル（拳の側の好み・経路）と、武器を握るなら武器の教科書の経路。</summary>
    public delegate (BotStyle Style, GroundTuning? WeaponGround) Teach(BotStyle style, GroundTuning? weaponGround);

    /// <summary>攻め手の今の教育（スタイルと武器の教科書の経路）。</summary>
    public static (BotStyle Style, GroundTuning? WeaponGround) Current(string attackerId, string victimId)
    {
        var c = CharacterRoster.Get(attackerId);
        var weapon = WeaponCatalog.HasBot(c.Weapon) ? (c.WeaponStyle ?? WeaponCatalog.Textbook(c.Weapon!.Value)).Ground : null;
        return (c.Resolve(victimId), weapon);
    }

    public static FinishRun Run(string attackerId, string victimId, int seed, float seconds, Teach? teach = null)
    {
        var a = CharacterRoster.Get(attackerId);
        var v = CharacterRoster.Get(victimId);
        var (style, weaponGround) = Current(attackerId, victimId);
        (style, weaponGround) = teach?.Invoke(style, weaponGround) ?? (style, weaponGround);
        var blue = new Contender(style.Name, s => BotCatalog.Bot(style, s));
        var red = new Contender("victim", s => new BoxerBot(Still, s));
        var corners = VideoMaker.Corners(red, blue, seed, v.Wear, a.Wear, null, a.Weapon, s => blue.Create(s), s => red.Create(s), referee: false);
        corners = VideoMaker.WithCharacters(corners, v, a) with { PlayerStyle = Still };
        if (WeaponCatalog.HasBot(a.Weapon))
        {
            corners = corners with { CpuWeaponStyle = (a.WeaponStyle ?? WeaponCatalog.Textbook(a.Weapon!.Value)) with { Ground = weaponGround } };
        }

        corners = TvShowRules.Stage(corners, Organizations.Get("vfc"));
        // 檻の中央から始める（コーナーから始めると、狭い檻では攻め手が角の壁に押し付けられて動けないことがあった。檻の開始位置は別の作業で直す）。
        corners = corners with { Ring = corners.Ring! with { FromCorners = false } };
        using var game = new BoxingGame(seed: seed, corners: corners, rules: MiniRing.MatchRules.Vfc);
        var victim = game.Player;
        var falls = 0;
        var wasStanding = true;
        var moves = new Dictionary<string, int>(StringComparer.Ordinal);
        var lastStarted = 0;
        GroundStriker? lastStriker = null;
        float? downAt = null;
        var cells0 = 0;
        var t = 0f;
        for (; t < KnockAt + seconds && !victim.IsDead && !game.RoundOver; t += StepDt)
        {
            if (t > KnockAt)
            {
                victim.Concussion = MathF.Max(victim.Concussion, Out);
            }

            game.Step(default, StepDt);
            if (downAt is null && victim.State != FighterState.Standing && t > KnockAt)
            {
                downAt = t;
                cells0 = Filled(victim);
            }

            var striker = game.CpuControl as GroundStriker;
            if (!ReferenceEquals(striker, lastStriker))
            {
                (lastStriker, lastStarted) = (striker, 0);
            }

            var started = striker?.Ground.Started ?? 0;
            if (started > lastStarted && striker?.Ground.CurrentName is { } name)
            {
                moves[name] = moves.GetValueOrDefault(name) + (started - lastStarted);
            }

            lastStarted = started;
            if (GroundLab.Trace && ((int)(t / StepDt)) % 30 == 0)
            {
                var g = striker?.Ground;
                Console.WriteLine(string.Create(CultureInfo.InvariantCulture, $"  t={t:0.0} victim={victim.State} attacker={game.Cpu.State} ctrl={game.CpuControl.GetType().Name} plan={g?.CurrentName} dist={g?.Distance:0.00} gap={g?.Gap:0.00} sink={g?.Sink:0.00} started={started} cpu={game.Cpu.PelvisPosition} vic={victim.PelvisPosition} move={striker?.Last.MoveWorld} stam={game.Cpu.Stamina:0.00}"));
            }
            var standing = game.Cpu.State == FighterState.Standing;
            falls += wasStanding && !standing ? 1 : 0;
            wasStanding = standing;
        }

        var from = downAt ?? KnockAt;
        return new FinishRun(attackerId, victimId, seed, victim.IsDead, victim.IsDead ? t - from : seconds, victim.DeathCause?.ToString() ?? "-", falls, moves.Values.Sum(), moves, downAt is null ? 0 : cells0 - Filled(victim));
    }

    private static int Filled(Fighter f) => f.Voxels is { } v ? v.Parts.Sum(p => p.Body.Grid.FilledCount) : 0;
}

/// <summary>とどめのドリルの動画（groundtime --drill a --victims v --video out.mp4）: 名簿のキャラが、気を失って倒れた名簿のキャラを攻める。檻・コーナーから。</summary>
internal static class FinishDrillVideo
{
    public static void Make(string attackerId, string victimId, int seed, float seconds, string output, TextWriter console, FinishDrill.Teach? teach = null)
    {
        var a = CharacterRoster.Get(attackerId);
        var v = CharacterRoster.Get(victimId);
        var style = a.Resolve(victimId);
        style = teach?.Invoke(style, null).Style ?? style; // 素手のキャラの教えだけ（武器の経路は名簿の値）
        var still = BotCatalog.Boxer with { Attacks = false, DefenseRate = 0f, Prediction = 0f, Aggression = 0f, FinishTempo = 0f, FinishPress = 0f, FinishSpend = 0f };
        var red = new Contender("victim", s => new BoxerBot(still, s));
        var blue = new Contender(style.Name, s => BotCatalog.Bot(style, s));
        BoxingGame? game = null;
        new VideoMaker(1280, 720, 30, speed: 0.5f, org: Organizations.Get("vfc"), rules: MiniRing.MatchRules.Vfc).Make(red, blue, seed, seconds, output, console,
            afterStep: time =>
            {
                if (time > 2.518f && game is not null)
                {
                    game.Player.Concussion = MathF.Max(game.Player.Concussion, 0.97f);
                }
            },
            redWear: v.Wear, blueWear: a.Wear, setup: g => game = g, blueWeapon: a.Weapon, redCharacter: v, blueCharacter: a, referee: false,
            editCorners: c => c with { Ring = c.Ring! with { FromCorners = false } });
    }
}
