using System.Globalization;
using System.Text;
using MiniRing.Bots;
using MiniRing.Characters;

namespace MiniRing.Arena;

/// <summary>
/// 攻めのリズムと隙の調査（名簿の総当たり）。打ち（<see cref="MaaiLab.Throw"/>）の時刻と、毎歩の両者の腕の伸び・守り・当たりから、
/// 「打った後のどの時間に打たれやすいか（隙）」「相手が打った後のどの時間に打つと当たり、返されないか（機）」を測る。
/// <para>
/// 測るだけで試合には触れない（<see cref="MaaiLab.RunBout"/> の歩ごとの読み取り）。曲線は両者が立っている歩だけで数える。
/// 打ちの結果: 空振り（当たり 0）・受けられた（当たりは守りの上だけ）・入った（守りの外に 1 つ以上）。
/// </para>
/// </summary>
internal static class RhythmLab
{
    /// <summary>時間の帯の幅 [s]。</summary>
    internal const float Bin = 0.05f;

    /// <summary>帯の数（0〜1.2 秒）。</summary>
    internal const int Bins = 24;

    /// <summary>この間より短い間で続いた打ちを 1 つの連打とみなす [s]（OpponentRead.ComboGap と同じ）。</summary>
    internal const float BurstGap = 0.6f;

    /// <summary>打ち出しから、返しの打撃（打たれた分）を数える長さ [s]。</summary>
    internal const float CounterWindow = 0.5f;

    /// <summary>空振りの後に何をしたかを見る長さ [s]（技の時計の終わりから）。</summary>
    internal const float FollowWindow = 0.4f;

    /// <summary>空振りの後の損得を数える長さ [s]（技の時計の終わりから）。</summary>
    internal const float AfterWindow = 1.0f;

    /// <summary>腕の伸び（肩から拳の中心まで / 腕の長さ）の帯の幅と数。</summary>
    internal const float ExtBin = 0.1f;

    internal const int ExtBins = 12;

    internal static readonly string[] Outcomes = ["miss", "blocked", "landed"];

    /// <summary>自分が打ち出した時までに、相手の最後の打ちが自分に触れていたか（ボットが打ち出す時に分かること）: 触れていない・守りの上だけ・守りの外に入った。</summary>
    internal static readonly string[] Contacts = ["none", "guard", "clean"];

    private static readonly CultureInfo Inv = CultureInfo.InvariantCulture;

    /// <summary>1 つの当たり（Weapon: 武器の当たり。ボットは OnPunchTaken で感じないので触れ方には数えない、ダメージには数える）。</summary>
    internal readonly record struct HitLog(int Attacker, int Step, float Damage, bool Clean, bool Weapon = false);

    /// <summary>1 試合の記録（red = 0、blue = 1）。</summary>
    internal sealed class BoutLog(string red, string blue, int steps)
    {
        public string[] Ids { get; } = [red, blue];
        public List<MaaiLab.Throw> Throws { get; set; } = [];
        public List<HitLog> Hits { get; } = [];
        public bool[] Standing { get; } = new bool[steps];
        public float[][] Extension { get; } = [new float[steps], new float[steps]];
        public bool[][] Defending { get; } = [new bool[steps], new bool[steps]];
        public int Steps => Standing.Length;
    }

    public static BoutLog RunBout(FighterCharacter red, FighterCharacter blue, int seed, float seconds, bool referee)
    {
        var steps = (int)MathF.Ceiling(seconds / MaaiLab.StepDt);
        var log = new BoutLog(red.Id, blue.Id, steps);
        void OnStep(BoxingGame game, float now)
        {
            var i = (int)MathF.Round(now / MaaiLab.StepDt);
            if (i < 0 || i >= steps)
            {
                return;
            }

            log.Standing[i] = game.Player.State == FighterState.Standing && game.Cpu.State == FighterState.Standing;
            log.Extension[0][i] = Extension(game.Player);
            log.Extension[1][i] = Extension(game.Cpu);
            log.Defending[0][i] = FistsOf(game.PlayerControl)?.ProbeState.Defending ?? false;
            log.Defending[1][i] = FistsOf(game.CpuControl)?.ProbeState.Defending ?? false;
            foreach (var hit in game.RecentHits)
            {
                log.Hits.Add(new HitLog(hit.Attacker == game.Player ? 0 : 1, i, hit.Damage, hit.Zone != HitZone.Guard));
            }

            foreach (var hit in game.WeaponHits)
            {
                log.Hits.Add(new HitLog(hit.Attacker == game.Player ? 0 : 1, i, hit.Damage, hit.Zone != HitZone.Guard, Weapon: true));
            }
        }

        log.Throws = MaaiLab.RunBout(red, blue, seed, seconds, referee, onStep: OnStep, keepRhythm: false);
        return log;
    }

    /// <summary>両腕のうち伸びている方の、肩から拳の中心までの長さ / 腕の長さ（見えている腕の形）。</summary>
    internal static float Extension(Fighter f)
    {
        var reach = MathF.Max(1e-4f, f.ArmReach);
        var left = (f.GloveCenter(Hand.Left) - f.ShoulderPosition(Hand.Left)).Length();
        var right = (f.GloveCenter(Hand.Right) - f.ShoulderPosition(Hand.Right)).Length();
        return MathF.Max(left, right) / reach;
    }

    internal static BoxerBot? FistsOf(IFighterControl? control)
    {
        var inner = control is GroundStriker g ? g.Inner : control;
        return inner is DisarmedFallback df ? df.Fists as BoxerBot : inner as BoxerBot;
    }

    public static List<BoutLog> Collect(IReadOnlyList<FighterCharacter> roster, int bouts, float seconds, int seed, bool referee, int parallel, TextWriter progress)
    {
        var jobs = new List<(FighterCharacter Red, FighterCharacter Blue, int Seed)>();
        for (var a = 0; a < roster.Count; a++)
        {
            for (var b = a + 1; b < roster.Count; b++)
            {
                for (var i = 0; i < bouts; i++)
                {
                    var s = seed + (1000 * a) + (37 * b) + (i / 2);
                    jobs.Add(i % 2 == 0 ? (roster[a], roster[b], s) : (roster[b], roster[a], s));
                }
            }
        }

        var results = new BoutLog[jobs.Count];
        var done = 0;
        var watch = System.Diagnostics.Stopwatch.StartNew();
        Parallel.For(0, jobs.Count, new ParallelOptions { MaxDegreeOfParallelism = parallel }, i =>
        {
            results[i] = RunBout(jobs[i].Red, jobs[i].Blue, jobs[i].Seed, seconds, referee);
            var n = Interlocked.Increment(ref done);
            lock (progress)
            {
                progress.WriteLine(string.Create(Inv, $"[{n}/{jobs.Count}] {jobs[i].Red.Id} vs {jobs[i].Blue.Id}: {results[i].Throws.Count} throws (total {watch.Elapsed.TotalSeconds:0}s)"));
            }
        });
        return [.. results];
    }

    // ── 集計 ──

    /// <summary>時間の帯ごとの、さらされた時間と、受けた・与えたダメージと入った数。</summary>
    internal sealed class Curve(int bins)
    {
        public double[] Seconds { get; } = new double[bins];
        public double[] Taken { get; } = new double[bins];
        public double[] TakenHits { get; } = new double[bins];
        public double[] Dealt { get; } = new double[bins];
    }

    /// <summary>打ち出した時の相手の様子の帯ごとの、打った数・入った数・与えた・返された（打ち出しから CounterWindow 秒に受けた）ダメージ。</summary>
    internal sealed class Chance(int bins)
    {
        public double[] Throws { get; } = new double[bins];
        public double[] Landed { get; } = new double[bins];
        public double[] Dealt { get; } = new double[bins];
        public double[] Countered { get; } = new double[bins];
    }

    internal sealed class FighterRhythm(string id)
    {
        public string Id { get; } = id;
        public double StandingSeconds;
        public double TakenTotal;
        public double DealtTotal;
        public double QuietSeconds;
        public double QuietTaken;
        public double QuietDealt;

        /// <summary>自分の打ち出しからの時間（次の自分の打ち出しで打ち切る）× 結果。</summary>
        public Curve[] AfterLaunch { get; } = [.. Outcomes.Select(_ => new Curve(Bins))];

        /// <summary>連打の最後の打ちの技の時計の終わりからの時間（次の自分の打ち出しで打ち切る）× 連打の結果（どれかが入ったか）。</summary>
        public Curve[] AfterBurst { get; } = [new Curve(Bins), new Curve(Bins)];

        /// <summary>自分の腕の伸びの帯ごと（今の歩）。</summary>
        public Curve ByOwnExtension { get; } = new(ExtBins);

        /// <summary>相手の最後の打ち出しからの時間（Bins 番目の帯 = 1.2 秒以内に相手の打ちが無い）× 相手のその打ちの結果（後で分かった結果）。</summary>
        public Chance[] AfterOpponentLaunch { get; } = [.. Outcomes.Select(_ => new Chance(Bins + 1))];

        /// <summary>相手の最後の打ち出しからの時間 × 自分が打ち出した時までにその打ちが自分に触れていたか（<see cref="Contacts"/>。ボットに分かること）。</summary>
        public Chance[] AfterOpponentContact { get; } = [.. Contacts.Select(_ => new Chance(Bins + 1))];

        /// <summary>打ち出した歩の相手の腕の伸びの帯ごと。</summary>
        public Chance ByOpponentExtension { get; } = new(ExtBins);

        /// <summary>空振りの後（技の時計の終わりから FollowWindow 秒）に先にしたこと: 0 = 次を打った、1 = 守った、2 = どちらも無し。</summary>
        public double[] FollowCount { get; } = new double[3];

        public double[] FollowDealt { get; } = new double[3];

        public double[] FollowTaken { get; } = new double[3];

        /// <summary>打ちの数・技の時計の長さ・腕が構えに戻るまでの時間 × 結果。</summary>
        public double[] ThrowCount { get; } = new double[3];

        public double[] ClockSum { get; } = new double[3];

        public double[] RecoverSum { get; } = new double[3];

        public double[] RecoverCount { get; } = new double[3];

        public double BurstCount;

        public double BurstLength;
    }

    internal static int OutcomeOf(MaaiLab.Throw t) => t.Hits == 0 ? 0 : t.Clean == 0 ? 1 : 2;

    private static bool Counts(MaaiLab.Throw t) => t.Kind is "punch" or "kick" or "weapon" && !float.IsNaN(t.LaunchTime);

    public static Dictionary<string, FighterRhythm> Analyze(IEnumerable<BoutLog> logs)
    {
        var all = new Dictionary<string, FighterRhythm>();
        foreach (var log in logs)
        {
            for (var me = 0; me < 2; me++)
            {
                var id = log.Ids[me];
                if (!all.TryGetValue(id, out var r))
                {
                    r = new FighterRhythm(id);
                    all[id] = r;
                }

                AnalyzeSide(log, me, r);
            }
        }

        return all;
    }

    private static void AnalyzeSide(BoutLog log, int me, FighterRhythm r)
    {
        var dt = MaaiLab.StepDt;
        var op = 1 - me;
        var taken = new double[log.Steps];
        var takenHits = new double[log.Steps];
        var takenGuard = new double[log.Steps];
        var contactClean = new double[log.Steps];
        var dealt = new double[log.Steps];
        foreach (var h in log.Hits)
        {
            if (h.Step < 0 || h.Step >= log.Steps)
            {
                continue;
            }

            if (h.Attacker == op)
            {
                taken[h.Step] += h.Damage;
                takenHits[h.Step] += h.Clean ? 1 : 0;
                // 触れ方（ボットが感じる物）: 拳・蹴りの当たりだけ（武器の当たりは OnPunchTaken に来ない）。
                takenGuard[h.Step] += h.Clean || h.Weapon ? 0 : 1;
                contactClean[h.Step] += h.Clean && !h.Weapon ? 1 : 0;
            }
            else
            {
                dealt[h.Step] += h.Damage;
            }
        }

        var mine = log.Throws.Where(t => t.Attacker == log.Ids[me] && Counts(t)).OrderBy(t => t.LaunchTime).ToList();
        var theirs = log.Throws.Where(t => t.Attacker == log.Ids[op] && Counts(t)).OrderBy(t => t.LaunchTime).ToList();
        var theirPunches = theirs.Where(t => t.Kind == "punch").ToList();
        var stepsPerBin = (int)MathF.Round(Bin / dt);
        int StepOf(float time) => Math.Clamp((int)MathF.Round(time / dt), 0, log.Steps - 1);

        // 全体と、静かな時（どちらも 1.2 秒以内に打っていない）。
        var lastAny = float.NegativeInfinity;
        var launches = mine.Concat(theirs).Select(t => t.LaunchTime).Order().ToList();
        var li = 0;
        for (var i = 0; i < log.Steps; i++)
        {
            var now = i * dt;
            while (li < launches.Count && launches[li] <= now)
            {
                lastAny = launches[li++];
            }

            if (!log.Standing[i])
            {
                continue;
            }

            r.StandingSeconds += dt;
            r.TakenTotal += taken[i];
            r.DealtTotal += dealt[i];
            if (now - lastAny > Bins * Bin)
            {
                r.QuietSeconds += dt;
                r.QuietTaken += taken[i];
                r.QuietDealt += dealt[i];
            }

            var eb = Math.Min(ExtBins - 1, (int)(log.Extension[me][i] / ExtBin));
            Add(r.ByOwnExtension, eb, dt, taken[i], takenHits[i], dealt[i]);
        }

        // 自分の打ち出しからの時間。
        for (var k = 0; k < mine.Count; k++)
        {
            var t = mine[k];
            var o = OutcomeOf(t);
            var next = k + 1 < mine.Count ? mine[k + 1].LaunchTime : float.PositiveInfinity;
            r.ThrowCount[o]++;
            r.ClockSum[o] += t.EndTime - t.LaunchTime;
            Sweep(log, StepOf(t.LaunchTime), t.LaunchTime, next, r.AfterLaunch[o], taken, takenHits, dealt);

            // 腕が構えに戻るまで: 打ち出しから腕の伸びが山を越え、打ち出しの時の伸び + 0.05 以下へ戻った時刻。
            var s0 = StepOf(t.LaunchTime);
            var rest = log.Extension[me][s0];
            var peak = rest;
            for (var i = s0; i < log.Steps && (i - s0) * dt < 1.5f; i++)
            {
                var e = log.Extension[me][i];
                peak = MathF.Max(peak, e);
                if (peak > rest + 0.15f && e <= rest + 0.05f)
                {
                    r.RecoverSum[o] += (i - s0) * dt;
                    r.RecoverCount[o]++;
                    break;
                }
            }
        }

        // 連打の終わりから。
        var start = 0;
        while (start < mine.Count)
        {
            var end = start;
            while (end + 1 < mine.Count && mine[end + 1].LaunchTime - mine[end].LaunchTime < BurstGap)
            {
                end++;
            }

            var any = false;
            for (var k = start; k <= end; k++)
            {
                any |= OutcomeOf(mine[k]) == 2;
            }

            r.BurstCount++;
            r.BurstLength += end - start + 1;
            var last = mine[end];
            var nextLaunch = end + 1 < mine.Count ? mine[end + 1].LaunchTime : float.PositiveInfinity;
            Sweep(log, StepOf(last.EndTime), last.EndTime, nextLaunch, r.AfterBurst[any ? 1 : 0], taken, takenHits, dealt);
            start = end + 1;
        }

        // 相手の様子と、自分の打ちの当たり・返され。
        foreach (var t in mine)
        {
            var s0 = StepOf(t.LaunchTime);
            if (!log.Standing[s0])
            {
                continue;
            }

            var prev = theirs.LastOrDefault(x => x.LaunchTime <= t.LaunchTime);
            // 帯は歩の数で決める（float の割り算で帯の境の手前へ落ちた。レビュー指摘）。
            var sinceSteps = prev is null ? int.MaxValue : s0 - StepOf(prev.LaunchTime);
            var bin = sinceSteps < Bins * stepsPerBin ? sinceSteps / stepsPerBin : Bins;
            var cat = prev is null ? 0 : OutcomeOf(prev);
            var countered = 0.0;
            for (var i = s0; i < log.Steps && (i - s0) * dt < CounterWindow; i++)
            {
                countered += taken[i];
            }

            var landed = OutcomeOf(t) == 2 ? 1 : 0;
            if (bin == Bins)
            {
                // 相手が打っていない時は、結果の区別が無いので全部「空振り」の欄の最後の帯へ入れる。
                cat = 0;
            }

            AddChance(r.AfterOpponentLaunch[cat], bin, landed, t.Damage, countered);
            // ボットが分かること: 相手の最後の拳（ボットの出始めの読みは拳だけを見る）からの時間と、自分が打ち出すまでにそれが触れたか。
            var fist = theirPunches.LastOrDefault(x => x.LaunchTime <= t.LaunchTime);
            var fistSteps = fist is null ? int.MaxValue : s0 - StepOf(fist.LaunchTime);
            var fistBin = fistSteps < Bins * stepsPerBin ? fistSteps / stepsPerBin : Bins;
            var contact = 0;
            if (fist is not null && fistBin < Bins)
            {
                for (var i = StepOf(fist.LaunchTime); i < s0; i++)
                {
                    contact = Math.Max(contact, contactClean[i] > 0 ? 2 : takenGuard[i] > 0 ? 1 : 0);
                }
            }

            AddChance(r.AfterOpponentContact[contact], fistBin, landed, t.Damage, countered);
            var eb = Math.Min(ExtBins - 1, (int)(log.Extension[op][s0] / ExtBin));
            AddChance(r.ByOpponentExtension, eb, landed, t.Damage, countered);
        }

        // 空振りの後に何をしたか。
        foreach (var t in mine.Where(t => t.Kind == "punch" && OutcomeOf(t) == 0))
        {
            var e0 = StepOf(t.EndTime);
            var nextLaunch = mine.FirstOrDefault(x => x.LaunchTime > t.LaunchTime)?.LaunchTime ?? float.PositiveInfinity;
            var what = 2;
            for (var i = e0; i < log.Steps && (i - e0) * dt < FollowWindow; i++)
            {
                if (nextLaunch <= i * dt)
                {
                    what = 0;
                    break;
                }

                if (log.Defending[me][i])
                {
                    what = 1;
                    break;
                }
            }

            r.FollowCount[what]++;
            for (var i = e0; i < log.Steps && (i - e0) * dt < AfterWindow; i++)
            {
                r.FollowDealt[what] += dealt[i];
                r.FollowTaken[what] += taken[i];
            }
        }
    }

    /// <summary>時刻 <paramref name="from"/> から、<paramref name="until"/>（自分の次の打ち出し）まで、帯ごとに足す（両者が立っている歩だけ）。</summary>
    private static void Sweep(BoutLog log, int s0, float from, float until, Curve curve, double[] taken, double[] takenHits, double[] dealt)
    {
        var dt = MaaiLab.StepDt;
        for (var i = s0; i < log.Steps; i++)
        {
            var since = (i * dt) - from;
            if (since >= Bins * Bin || i * dt >= until)
            {
                break;
            }

            if (since < 0f || !log.Standing[i])
            {
                continue;
            }

            Add(curve, (int)(since / Bin), dt, taken[i], takenHits[i], dealt[i]);
        }
    }

    private static void Add(Curve c, int bin, double dt, double taken, double takenHits, double dealt)
    {
        c.Seconds[bin] += dt;
        c.Taken[bin] += taken;
        c.TakenHits[bin] += takenHits;
        c.Dealt[bin] += dealt;
    }

    private static void AddChance(Chance c, int bin, int landed, double dealt, double countered)
    {
        c.Throws[bin]++;
        c.Landed[bin] += landed;
        c.Dealt[bin] += dealt;
        c.Countered[bin] += countered;
    }

    // ── 出力 ──

    /// <summary>曲線を 1 行 1 帯の CSV にする（fighter,curve,category,bin,from,to,seconds_or_throws,taken,taken_hits,dealt_or_landed,dealt,countered）。</summary>
    public static string Csv(IEnumerable<FighterRhythm> rhythms)
    {
        var sb = new StringBuilder("fighter,curve,category,bin,from,to,seconds,taken,taken_hits,dealt,throws,landed,countered\n");
        foreach (var r in rhythms.OrderBy(r => r.Id, StringComparer.Ordinal))
        {
            for (var o = 0; o < Outcomes.Length; o++)
            {
                CurveRows(sb, r.Id, "after_launch", Outcomes[o], r.AfterLaunch[o], Bin);
                ChanceRows(sb, r.Id, "after_opponent_launch", Outcomes[o], r.AfterOpponentLaunch[o], Bin);
                ChanceRows(sb, r.Id, "after_opponent_contact", Contacts[o], r.AfterOpponentContact[o], Bin);
            }

            CurveRows(sb, r.Id, "after_burst", "none_landed", r.AfterBurst[0], Bin);
            CurveRows(sb, r.Id, "after_burst", "landed", r.AfterBurst[1], Bin);
            CurveRows(sb, r.Id, "own_extension", "-", r.ByOwnExtension, ExtBin);
            ChanceRows(sb, r.Id, "opponent_extension", "-", r.ByOpponentExtension, ExtBin);
        }

        return sb.ToString();
    }

    private static void CurveRows(StringBuilder sb, string id, string curve, string cat, Curve c, float width)
    {
        for (var b = 0; b < c.Seconds.Length; b++)
        {
            sb.Append(string.Create(Inv, $"{id},{curve},{cat},{b},{b * width:0.00},{(b + 1) * width:0.00},{c.Seconds[b]:0.###},{c.Taken[b]:0.###},{c.TakenHits[b]:0.###},{c.Dealt[b]:0.###},,,\n"));
        }
    }

    private static void ChanceRows(StringBuilder sb, string id, string curve, string cat, Chance c, float width)
    {
        for (var b = 0; b < c.Throws.Length; b++)
        {
            var to = b == Bins && curve.StartsWith("after_opponent", StringComparison.Ordinal) ? "inf" : ((b + 1) * width).ToString("0.00", Inv);
            sb.Append(string.Create(Inv, $"{id},{curve},{cat},{b},{b * width:0.00},{to},,,,{c.Dealt[b]:0.###},{c.Throws[b]:0},{c.Landed[b]:0},{c.Countered[b]:0.###}\n"));
        }
    }

    /// <summary>選手ごとの要約（Markdown）。</summary>
    public static string Report(IReadOnlyList<FighterRhythm> rhythms, string header)
    {
        var sb = new StringBuilder();
        sb.AppendLine("# 攻めのリズムと隙（rhythmlab）");
        sb.AppendLine();
        sb.AppendLine(header);
        sb.AppendLine();
        sb.AppendLine("被ダメ率 = 受けたダメージ / 両者が立っていた秒。「静か」= どちらも 1.2 秒以内に打っていない時。打った後の窓は、打ち出しから（次の自分の打ち出しで打ち切る）。");
        sb.AppendLine();
        sb.AppendLine("| 選手 | 被ダメ率 全体 | 静か | 打ち出し 0-0.3 s 空振り / 受けられ / 入った | 0.3-0.6 s 空振り / 受けられ / 入った | 連打の後 0-0.4 s 入らず / 入った | 打ち: 空振り% / 受けられ% / 入った% | 技の時計 [s] | 腕が戻るまで [s] 空振り / 入った | 連打の長さ |");
        sb.AppendLine("|---|---:|---:|---|---|---|---|---:|---|---:|");
        foreach (var r in rhythms.OrderBy(r => r.Id, StringComparer.Ordinal))
        {
            var n = r.ThrowCount.Sum();
            if (n == 0)
            {
                continue;
            }

            string Rate(Curve c, int from, int to)
            {
                var s = 0.0;
                var d = 0.0;
                for (var b = from; b < to; b++)
                {
                    s += c.Seconds[b];
                    d += c.Taken[b];
                }

                return s > 0.5 ? (d / s).ToString("0.0", Inv) : "-";
            }

            sb.Append(string.Create(Inv, $"| {r.Id} | {r.TakenTotal / Math.Max(1e-9, r.StandingSeconds):0.0} | {(r.QuietSeconds > 0.5 ? (r.QuietTaken / r.QuietSeconds).ToString("0.0", Inv) : "-")} | "));
            sb.Append(Inv, $"{Rate(r.AfterLaunch[0], 0, 6)} / {Rate(r.AfterLaunch[1], 0, 6)} / {Rate(r.AfterLaunch[2], 0, 6)} | ");
            sb.Append(Inv, $"{Rate(r.AfterLaunch[0], 6, 12)} / {Rate(r.AfterLaunch[1], 6, 12)} / {Rate(r.AfterLaunch[2], 6, 12)} | ");
            sb.Append(Inv, $"{Rate(r.AfterBurst[0], 0, 8)} / {Rate(r.AfterBurst[1], 0, 8)} | ");
            sb.Append(string.Create(Inv, $"{100 * r.ThrowCount[0] / n:0} / {100 * r.ThrowCount[1] / n:0} / {100 * r.ThrowCount[2] / n:0} | {r.ClockSum.Sum() / n:0.00} | "));
            sb.Append(string.Create(Inv, $"{(r.RecoverCount[0] > 0 ? r.RecoverSum[0] / r.RecoverCount[0] : double.NaN):0.00} / {(r.RecoverCount[2] > 0 ? r.RecoverSum[2] / r.RecoverCount[2] : double.NaN):0.00} | {r.BurstLength / Math.Max(1, r.BurstCount):0.00} |\n"));
        }

        sb.AppendLine();
        sb.AppendLine("## 空振りの後（技の時計の終わりから 0.4 秒に先にしたこと、その後 1 秒の与えた − 受けた）");
        sb.AppendLine();
        sb.AppendLine("| 選手 | 次を打った: 数 / 損得 | 守った: 数 / 損得 | 何もしない: 数 / 損得 |");
        sb.AppendLine("|---|---|---|---|");
        foreach (var r in rhythms.OrderBy(r => r.Id, StringComparer.Ordinal))
        {
            if (r.FollowCount.Sum() == 0)
            {
                continue;
            }

            string Cell(int k) => r.FollowCount[k] > 0
                ? string.Create(Inv, $"{r.FollowCount[k]:0} / {(r.FollowDealt[k] - r.FollowTaken[k]) / r.FollowCount[k]:+0.0;-0.0}")
                : "0 / -";
            sb.AppendLine(Inv, $"| {r.Id} | {Cell(0)} | {Cell(1)} | {Cell(2)} |");
        }

        return sb.ToString();
    }
}
