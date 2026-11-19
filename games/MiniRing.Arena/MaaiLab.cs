using System.Globalization;
using System.Text;
using System.Text.Json;
using MiniRing.Bots;
using MiniRing.Characters;

namespace MiniRing.Arena;

/// <summary>
/// 技ごとの間合いとダメージの曲線（名簿の総当たり）。
/// <para>
/// 1 打 = ボットが技を出した 1 回（当たらなかった打ちもダメージ 0 として数える）。その打ちを「打ち出した瞬間」の
/// 2 人の胸と胸の水平距離 d（拳は溜めを終えて前へ出し始めた歩、蹴り・武器は技を始めた歩）を間合いとし、
/// その打ちの間に当たった打撃（<see cref="BoxingGame.RecentHits"/>・<see cref="BoxingGame.WeaponHits"/>）のダメージを合計する。
/// 当たりは、打った手（蹴りは脚、武器は握った武器）の今出している技へ付ける。技の時計が終わってから
/// <see cref="Grace"/> 秒までの当たりもその打ちに数える（BoxerBot の LandWindow と同じ）。倒れた相手への打ち下ろしは別の技として数える。
/// </para>
/// <para>測るだけで試合には触れない（ボットの状態を読むだけ）。</para>
/// </summary>
internal static class MaaiLab
{
    internal const float StepDt = 1f / 60f;

    /// <summary>技の時計が終わってから、当たりをその打ちに数える長さ [s]（BoxerBot.LandWindow と同じ）。</summary>
    private const float Grace = 0.15f;

    /// <summary>曲線の帯の幅（自分の腕の長さ倍）。</summary>
    private const float BinWidth = 0.1f;

    /// <summary>最適な間合いを選ぶときに要る、帯の打った数の下限。</summary>
    private const int MinBinThrows = 8;

    /// <summary>得意・不得意の距離の帯の幅 [cm]（胸と胸の水平距離）。</summary>
    internal const float RangeBinCm = 2f;

    /// <summary>得意・不得意を選ぶときに要る、その帯にいた時間の合計の下限 [s]（全試合）。</summary>
    internal const double MinRangeSeconds = 30;

    /// <summary>1 試合の、1 人の、1 つの距離の帯での時間・与えた・受けたダメージ。</summary>
    internal sealed record RangeCell(string Fighter, string Opponent, int Bin, double Seconds, double Dealt, double Taken, int Bout = -1);

    private static readonly CultureInfo Inv = CultureInfo.InvariantCulture;

    /// <summary>1 打の記録。<paramref name="LaunchTime"/> は打ち出した時刻（拳は溜めを終えた歩、蹴り・武器は技を始めた歩）、
    /// <paramref name="EndTime"/> は技の時計が終わった時刻 [s]（試合の始めから）。</summary>
    internal sealed record Throw(string Attacker, string Opponent, string Technique, string Kind, float Reach, float LaunchD, float ImpactD, int Hits, int Clean, float Damage, float MaxHit,
        float LaunchTime = float.NaN, float EndTime = float.NaN);

    private sealed class Open(string label, string kind, float now)
    {
        public string Label { get; } = label;
        public string Kind { get; } = kind;
        public float Started { get; } = now;
        public float? LaunchD;
        public float? LaunchAt;
        public float LastSeen = now;
        public float LastTime = float.NegativeInfinity;
        public float? EndedAt;
        public int Hits;
        public int Clean;
        public float Damage;
        public float MaxHit;
        public float? ImpactD;
    }

    /// <summary>1 人の打ちの追跡（コーナーごと）。</summary>
    private sealed class Tracker(string id, string opponent, float reach, List<Throw> sink)
    {
        private readonly Dictionary<string, Open> _open = [];
        private int _swordUsed = -1;
        private SwordsmanBot? _swordBot;
        private object? _clubSwing;
        private int _groundStarted = -1;

        public void Observe(IFighterControl? control, float d, float now)
        {
            foreach (var o in _open.Values)
            {
                o.LastSeen = now;
            }

            var seen = new HashSet<string>();
            var inner = control;
            if (inner is GroundStriker g)
            {
                inner = g.Inner;
                var ground = g.Ground;
                if (ground.Striking)
                {
                    seen.Add("G");
                    if (!_open.TryGetValue("G", out var o) || ground.Started != _groundStarted)
                    {
                        Close("G");
                        o = Start("G", "倒れた相手へ:" + (ground.CurrentName ?? "?"), "ground", now, d);
                        _groundStarted = ground.Started;
                    }

                    o.EndedAt = null;
                }
            }

            var fists = inner as BoxerBot;
            if (inner is DisarmedFallback df)
            {
                fists = df.Fists as BoxerBot;
                ObserveWeapon(df.Disarmed ? null : df.Armed, d, now, seen);
            }
            else
            {
                ObserveWeapon(inner, d, now, seen);
            }

            if (fists is not null)
            {
                ObservePunch(fists, Hand.Left, "L", d, now, seen);
                ObservePunch(fists, Hand.Right, "R", d, now, seen);
                ObserveKick(fists, d, now, seen);
            }

            // 見えなくなった打ちは、Grace 秒の後で閉じる。
            foreach (var slot in _open.Keys.ToList())
            {
                if (seen.Contains(slot))
                {
                    continue;
                }

                var o = _open[slot];
                o.EndedAt ??= now;
                if (now - o.EndedAt.Value > Grace)
                {
                    Close(slot);
                }
            }
        }

        private void ObservePunch(BoxerBot bot, Hand hand, string slot, float d, float now, HashSet<string> seen)
        {
            var a = bot.ActionOf(hand);
            _open.TryGetValue(slot, out var o);
            if (a.Technique is { } t && a.Duration > 0f && a.Time < a.Duration)
            {
                var label = PunchName(t);
                if (o is null || o.Label != label || a.Time < o.LastTime - 1e-5f)
                {
                    Close(slot);
                    o = Start(slot, label, "punch", now, null);
                }

                o.LastTime = a.Time;
                o.EndedAt = null;
                o.LaunchD ??= a.Time >= 0f ? d : null;
                o.LaunchAt ??= a.Time >= 0f ? now : null;
                seen.Add(slot);
            }
            else if (o is not null && a.Technique is null && a.Duration > 0f && a.Time >= o.LastTime && a.Time < a.Duration)
            {
                // 当たった後（BoxerBot が Technique を消す）も、同じ拳の時計が進む間は同じ打ち。
                o.LastTime = a.Time;
                o.EndedAt = null;
                o.LaunchD ??= a.Time >= 0f ? d : null;
                o.LaunchAt ??= a.Time >= 0f ? now : null;
                seen.Add(slot);
            }
        }

        private void ObserveKick(BoxerBot bot, float d, float now, HashSet<string> seen)
        {
            if (bot.CurrentKick is not { } k)
            {
                return;
            }

            var label = KickName(k);
            _open.TryGetValue("K", out var o);
            if (o is null || o.Label != label || bot.KickTime < o.LastTime - 1e-5f)
            {
                Close("K");
                o = Start("K", label, "kick", now, d);
            }

            o.LastTime = bot.KickTime;
            o.EndedAt = null;
            seen.Add("K");
        }

        private void ObserveWeapon(IFighterControl? control, float d, float now, HashSet<string> seen)
        {
            switch (control)
            {
                case SwordsmanBot sword:
                    if (!ReferenceEquals(sword, _swordBot))
                    {
                        // ラウンドが替わるとボットは作り直され、出した技の数え（Used）は 0 から始まる。
                        _swordUsed = _swordBot is null ? sword.Used.Count : 0;
                        _swordBot = sword;
                    }

                    if (sword.Used.Count != _swordUsed && _swordUsed >= 0 && sword.Used.Count > 0)
                    {
                        Close("W");
                        Start("W", sword.Used[^1], "weapon", now, d);
                    }

                    _swordUsed = sword.Used.Count;
                    if (sword.Current is not null && _open.TryGetValue("W", out var so))
                    {
                        so.EndedAt = null;
                        seen.Add("W");
                    }

                    break;
                case ClubBot club:
                    var swing = club.Current;
                    if (swing is not null && !ReferenceEquals(swing, _clubSwing))
                    {
                        Close("W");
                        Start("W", ClubName(swing.Family), "weapon", now, d);
                    }

                    _clubSwing = swing;
                    if (swing is not null && _open.TryGetValue("W", out var co))
                    {
                        co.EndedAt = null;
                        seen.Add("W");
                    }

                    break;
            }
        }

        private Open Start(string slot, string label, string kind, float now, float? d)
        {
            var o = new Open(label, kind, now) { LaunchD = d, LaunchAt = d is null ? null : now };
            _open[slot] = o;
            return o;
        }

        /// <summary>当たりを、その手・脚・武器の今の打ちへ付ける。打ちが無ければ「技の外」の打ちとして残す。</summary>
        public void Hit(string slot, float damage, bool clean, float d, float now)
        {
            if (_open.ContainsKey("G"))
            {
                slot = "G";
            }

            if (!_open.TryGetValue(slot, out var o))
            {
                o = Start(slot + "?", GuardLabel(slot), "other", now, d);
                slot += "?";
            }

            o.Hits++;
            o.Clean += clean ? 1 : 0;
            o.Damage += damage;
            o.MaxHit = MathF.Max(o.MaxHit, damage);
            o.ImpactD ??= d;
            if (o.Kind == "other")
            {
                Close(slot);
            }
        }

        public void CloseAll()
        {
            foreach (var slot in _open.Keys.ToList())
            {
                Close(slot);
            }
        }

        private void Close(string slot)
        {
            if (!_open.Remove(slot, out var o))
            {
                return;
            }

            // 溜めの途中でやめた拳（打ち出さず、当たりも無い）は打ちに数えない。
            if (o.LaunchD is null && o.Hits == 0)
            {
                return;
            }

            var launch = o.LaunchD ?? o.ImpactD ?? 0f;
            sink.Add(new Throw(id, opponent, o.Label, o.Kind, reach, launch, o.ImpactD ?? float.NaN, o.Hits, o.Clean, o.Damage, o.MaxHit,
                o.LaunchAt ?? o.Started, o.EndedAt ?? o.LastSeen));
        }
    }

    /// <summary>技を出していない間（構え・守り・詰められた所）の当たり。技ではないので最適な間合いは出さない。</summary>
    private static string GuardLabel(string slot) => slot switch
    {
        "W" => "(構えの武器が当たった)",
        "K" => "(構えの脚が当たった)",
        _ => "(構えの拳が当たった)",
    };

    internal static string PunchName(Technique t) => t switch
    {
        Technique.Jab => "ジャブ",
        Technique.Cross => "ストレート",
        Technique.LeadHook => "リードフック",
        Technique.RearHook => "リアフック",
        Technique.LeadUppercut => "リードアッパー",
        Technique.RearUppercut => "リアアッパー",
        Technique.BodyJab => "ボディジャブ",
        Technique.BodyCross => "ボディストレート",
        Technique.LeadBodyHook => "リードボディフック",
        Technique.RearBodyHook => "リアボディフック",
        Technique.Overhand => "オーバーハンド",
        Technique.Feint => "フェイント",
        _ => t.ToString(),
    };

    internal static string KickName(KickTechnique k) => k switch
    {
        KickTechnique.Front => "前蹴り",
        KickTechnique.Low => "下段蹴り",
        KickTechnique.Middle => "中段蹴り",
        KickTechnique.High => "上段蹴り",
        KickTechnique.Knee => "膝蹴り",
        KickTechnique.Roundhouse => "回し蹴り",
        KickTechnique.Side => "横蹴り",
        KickTechnique.Back => "後ろ蹴り",
        KickTechnique.Axe => "かかと落とし",
        KickTechnique.RoundhouseHigh => "上段回し蹴り",
        _ => k.ToString(),
    };

    private static string ClubName(MiniRing.Weapons.SwingFamily f) => f switch
    {
        MiniRing.Weapons.SwingFamily.Chop => "縦振り",
        MiniRing.Weapons.SwingFamily.Side => "横振り",
        MiniRing.Weapons.SwingFamily.Thrust => "突き",
        _ => f.ToString(),
    };

    /// <summary>1 試合を回し、両者の打ちを返す。</summary>
    /// <param name="red">赤（プレイヤー側）。</param>
    /// <param name="blue">青（CPU 側）。</param>
    /// <param name="seed">試合の種。</param>
    /// <param name="seconds">試合の長さ [s]。</param>
    /// <param name="referee">審判あり。</param>
    /// <param name="cells">距離の帯ごとの記録の入れ先（null なら取らない）。</param>
    /// <param name="onStep">毎歩、歩を進めた後に呼ぶ（試合の時刻・その歩の始めの距離。読むだけで試合に触れないこと）。</param>
    public static List<Throw> RunBout(FighterCharacter red, FighterCharacter blue, int seed, float seconds, bool referee, List<RangeCell>? cells = null,
        Action<BoxingGame, float>? onStep = null)
    {
        var range = new Dictionary<(string Fighter, string Opponent, int Bin), RangeCell>();
        void AddRange(string me, string opp, int bin, float dt, float dealt, float taken)
        {
            var key = (me, opp, bin);
            var c = range.GetValueOrDefault(key) ?? new RangeCell(me, opp, bin, 0, 0, 0);
            range[key] = c with { Seconds = c.Seconds + dt, Dealt = c.Dealt + dealt, Taken = c.Taken + taken };
        }

        var redContender = RosterLeague.ContenderOf(red, blue.Id, null);
        var blueContender = RosterLeague.ContenderOf(blue, red.Id, null);
        var corners = VideoMaker.Corners(redContender, blueContender, seed, red.Wear, blue.Wear, red.Weapon, blue.Weapon,
            s => blueContender.Create(s), s => redContender.Create(s), referee);
        corners = VideoMaker.WithCharacters(corners, red, blue);
        using var game = new BoxingGame(seed: seed, corners: corners);
        var throws = new List<Throw>();
        var redT = new Tracker(red.Id, blue.Id, game.Player.ArmReach, throws);
        var blueT = new Tracker(blue.Id, red.Id, game.Cpu.ArmReach, throws);
        var steps = (int)MathF.Ceiling(seconds / StepDt);
        for (var i = 0; i < steps; i++)
        {
            var now = i * StepDt;
            var d = Flat(game.Player.ChestPosition - game.Cpu.ChestPosition);
            redT.Observe(game.PlayerControl, d, now);
            blueT.Observe(game.CpuControl, d, now);
            game.Step(default, StepDt);
            onStep?.Invoke(game, now);
            var after = Flat(game.Player.ChestPosition - game.Cpu.ChestPosition);
            // 距離ごとの時間・与えた・受けた（両方が立っている歩だけ。帯は歩の始めの距離）。
            if (cells is not null && game.Player.State == FighterState.Standing && game.Cpu.State == FighterState.Standing)
            {
                var redDealt = game.RecentHits.Where(h => h.Attacker == game.Player).Sum(h => h.Damage) + game.WeaponHits.Where(h => h.Attacker == game.Player).Sum(h => h.Damage);
                var blueDealt = game.RecentHits.Where(h => h.Attacker == game.Cpu).Sum(h => h.Damage) + game.WeaponHits.Where(h => h.Attacker == game.Cpu).Sum(h => h.Damage);
                var bin = (int)MathF.Floor(d * 100f / RangeBinCm);
                AddRange(red.Id, blue.Id, bin, StepDt, redDealt, blueDealt);
                AddRange(blue.Id, red.Id, bin, StepDt, blueDealt, redDealt);
            }

            foreach (var hit in game.RecentHits)
            {
                var t = hit.Attacker == game.Player ? redT : blueT;
                t.Hit(hit.Kick ? "K" : hit.Hand == Hand.Left ? "L" : "R", hit.Damage, hit.Zone != HitZone.Guard, after, now);
            }

            foreach (var hit in game.WeaponHits)
            {
                var t = hit.Attacker == game.Player ? redT : blueT;
                t.Hit("W", hit.Damage, hit.Zone != HitZone.Guard, after, now);
            }
        }

        redT.CloseAll();
        blueT.CloseAll();
        cells?.AddRange(range.Values);
        return throws;
    }

    private static float Flat(System.Numerics.Vector3 v) => MathF.Sqrt((v.X * v.X) + (v.Z * v.Z));

    // focus: 与えたら、このキャラが入る組だけを打つ（名簿に足した 1 人の表だけを測るとき。種は総当たりのときと同じ）。
    public static List<Throw> Collect(IReadOnlyList<FighterCharacter> roster, int bouts, float seconds, int seed, bool referee, int parallel, TextWriter progress, List<RangeCell>? cells = null, IReadOnlySet<string>? focus = null)
    {
        var jobs = new List<(FighterCharacter Red, FighterCharacter Blue, int Seed)>();
        for (var a = 0; a < roster.Count; a++)
        {
            for (var b = a + 1; b < roster.Count; b++)
            {
                if (focus is not null && !focus.Contains(roster[a].Id) && !focus.Contains(roster[b].Id))
                {
                    continue;
                }

                for (var i = 0; i < bouts; i++)
                {
                    var s = seed + (1000 * a) + (37 * b) + (i / 2);
                    jobs.Add(i % 2 == 0 ? (roster[a], roster[b], s) : (roster[b], roster[a], s));
                }
            }
        }

        var results = new List<Throw>[jobs.Count];
        var rangeResults = new List<RangeCell>[jobs.Count];
        var done = 0;
        var watch = System.Diagnostics.Stopwatch.StartNew();
        Parallel.For(0, jobs.Count, new ParallelOptions { MaxDegreeOfParallelism = parallel }, i =>
        {
            rangeResults[i] = [];
            results[i] = RunBout(jobs[i].Red, jobs[i].Blue, jobs[i].Seed, seconds, referee, rangeResults[i]);
            var n = Interlocked.Increment(ref done);
            lock (progress)
            {
                progress.WriteLine(string.Create(Inv, $"[{n}/{jobs.Count}] {jobs[i].Red.Id} vs {jobs[i].Blue.Id}: {results[i].Count} throws (total {watch.Elapsed.TotalSeconds:0}s)"));
            }
        });
        for (var i = 0; i < jobs.Count; i++)
        {
            // 試合ごとに分けて残す（帯の値のばらつきを試合の間で見るため）。
            cells?.AddRange(rangeResults[i].Select(c => c with { Bout = i }));
        }

        return [.. results.SelectMany(r => r)];
    }

    // ── 集計 ──

    internal sealed record Stats(int N, double Mean, double Median, double Sd, double Min, double Max, double P10, double P25, double P75, double P90)
    {
        public static Stats Of(IEnumerable<double> values)
        {
            var v = values.Where(x => !double.IsNaN(x)).Order().ToArray();
            if (v.Length == 0)
            {
                return new Stats(0, double.NaN, double.NaN, double.NaN, double.NaN, double.NaN, double.NaN, double.NaN, double.NaN, double.NaN);
            }

            var mean = v.Average();
            var sd = v.Length > 1 ? Math.Sqrt(v.Sum(x => (x - mean) * (x - mean)) / (v.Length - 1)) : 0;
            return new Stats(v.Length, mean, Q(v, 0.5), sd, v[0], v[^1], Q(v, 0.1), Q(v, 0.25), Q(v, 0.75), Q(v, 0.9));
        }

        private static double Q(double[] sorted, double p)
        {
            var x = p * (sorted.Length - 1);
            var lo = (int)Math.Floor(x);
            var hi = Math.Min(sorted.Length - 1, lo + 1);
            return sorted[lo] + ((sorted[hi] - sorted[lo]) * (x - lo));
        }
    }

    internal sealed record Bin(double From, double To, int Throws, int Landed, double HitRate, double DamagePerThrow, Stats PerHit, double CleanRate = 0);

    internal sealed record TechniqueSummary(string Fighter, string Technique, string Kind, int Throws, int Landed, double HitRate, double CleanRate, double TotalDamage, double Share,
        Stats LaunchCm, Stats LaunchRatio, Stats LandedLaunchCm, Stats ImpactCm, Stats PerHit, double DamagePerThrow, double? BestFrom, double? BestTo, double BestDamagePerThrow, IReadOnlyList<Bin> Curve);

    internal sealed record FighterSummary(string Id, string Name, string Style, string Equipment, double ReachCm, int Throws, int Landed, double TotalDamage, IReadOnlyList<TechniqueSummary> Techniques);

    public static List<FighterSummary> Summarize(IReadOnlyList<FighterCharacter> roster, IReadOnlyList<Throw> throws)
    {
        var list = new List<FighterSummary>();
        foreach (var c in roster)
        {
            var mine = throws.Where(t => t.Attacker == c.Id).ToList();
            if (mine.Count == 0)
            {
                continue;
            }

            var reach = mine[0].Reach;
            var total = mine.Sum(t => (double)t.Damage);
            var techniques = mine.GroupBy(t => t.Technique)
                .Select(g => SummarizeTechnique(c.Id, g.ToList(), total))
                .OrderByDescending(t => t.Throws)
                .ToList();
            list.Add(new FighterSummary(c.Id, c.Name, c.StyleName, c.EquipmentName, reach * 100, mine.Count, mine.Count(t => t.Hits > 0), total, techniques));
        }

        return list;
    }

    private static TechniqueSummary SummarizeTechnique(string fighter, List<Throw> g, double fighterTotal)
    {
        var landed = g.Where(t => t.Hits > 0).ToList();
        var bins = new List<Bin>();
        foreach (var bin in g.GroupBy(t => (int)Math.Floor(t.LaunchD / t.Reach / BinWidth)).OrderBy(b => b.Key))
        {
            var items = bin.ToList();
            var hit = items.Where(t => t.Hits > 0).ToList();
            bins.Add(new Bin(bin.Key * BinWidth, (bin.Key + 1) * BinWidth, items.Count, hit.Count, (double)hit.Count / items.Count,
                items.Average(t => (double)t.Damage), Stats.Of(hit.Select(t => (double)t.Damage)), (double)items.Count(t => t.Clean > 0) / items.Count));
        }

        var best = g[0].Kind == "other" ? null : bins.Where(b => b.Throws >= MinBinThrows).OrderByDescending(b => b.DamagePerThrow).FirstOrDefault();
        var total = g.Sum(t => (double)t.Damage);
        return new TechniqueSummary(fighter, g[0].Technique, g[0].Kind, g.Count, landed.Count, (double)landed.Count / g.Count, (double)g.Count(t => t.Clean > 0) / g.Count, total,
            fighterTotal > 0 ? total / fighterTotal : 0,
            Stats.Of(g.Select(t => t.LaunchD * 100.0)), Stats.Of(g.Select(t => (double)(t.LaunchD / t.Reach))),
            Stats.Of(landed.Select(t => t.LaunchD * 100.0)), Stats.Of(landed.Select(t => t.ImpactD * 100.0)),
            Stats.Of(landed.Select(t => (double)t.Damage)), total / g.Count, best?.From, best?.To, best?.DamagePerThrow ?? double.NaN, bins);
    }

    // ── 書き出し ──

    public static string Markdown(IReadOnlyList<FighterSummary> fighters, int bouts, float seconds, int seed, bool referee, int boutCount, int throwCount)
    {
        var sb = new StringBuilder();
        sb.AppendLine(string.Create(Inv, $"# 技ごとの間合いとダメージ（名簿の総当たり: 各組 {bouts} 試合 × {seconds:0} 秒、{boutCount} 試合、種 {seed}、レフェリー {(referee ? "あり" : "なし")}、PhysicsVersion {Fighter.PhysicsVersion}・LogicVersion {BoxerBot.LogicVersion}）"));
        sb.AppendLine();
        sb.AppendLine(string.Create(Inv, $"打ち {throwCount} 回。間合い d = 打ち出した瞬間の 2 人の胸と胸の水平距離（拳は溜めを終えて前へ出し始めた歩、蹴り・武器は技を始めた歩）。"));
        sb.AppendLine("d/L は自分の腕の長さ L で割った値。ダメージは試合の得点の物差し（`HitEvent.Damage`、急所の倍率込み）で、1 打の間の当たりの合計。当たり率はガードの上も含む、クリーン率はガード以外に 1 度でも当たった割合。");
        sb.AppendLine("「1 打あたり」は空振りを 0 として含む期待値、「当たり」は 1 回以上当たった打ちだけ。最適な間合いは、打った数が " + MinBinThrows + " 以上の帯で 1 打あたりが最大の帯。");
        sb.AppendLine();
        foreach (var f in fighters)
        {
            sb.AppendLine(string.Create(Inv, $"## {f.Id}（{f.Name}、{f.Style}、{f.Equipment}、腕 {f.ReachCm:0.0} cm）"));
            sb.AppendLine();
            sb.AppendLine(string.Create(Inv, $"打ち {f.Throws}・当たり {f.Landed}（{(double)f.Landed / f.Throws:P0}）・与えた合計 {f.TotalDamage:0.0}"));
            sb.AppendLine();
            sb.AppendLine("| 技 | 打ち | 当たり率 | クリーン率 | 割合 | 間合い 平均 / 中央 / 最小〜最大 [cm] | P10〜P90 [cm] | d/L 中央 | 当たった打ちの間合い 中央 [cm] | 当たり 平均 / 中央 / 最大 / P90 | 1 打あたり | 最適な間合い d/L（1 打あたり） |");
            sb.AppendLine("|---|---|---|---|---|---|---|---|---|---|---|---|");
            foreach (var t in f.Techniques)
            {
                var best = t.BestFrom is { } bf ? string.Create(Inv, $"{bf:0.0}〜{t.BestTo:0.0}（{t.BestDamagePerThrow:0.00}）") : "-";
                sb.AppendLine(string.Create(Inv,
                    $"| {t.Technique} | {t.Throws} | {t.HitRate:P0} | {t.CleanRate:P0} | {t.Share:P0} | {t.LaunchCm.Mean:0.0} / {t.LaunchCm.Median:0.0} / {t.LaunchCm.Min:0.0}〜{t.LaunchCm.Max:0.0} | {t.LaunchCm.P10:0.0}〜{t.LaunchCm.P90:0.0} | {t.LaunchRatio.Median:0.00} | {Num(t.LandedLaunchCm.Median, "0.0")} | {Num(t.PerHit.Mean, "0.00")} / {Num(t.PerHit.Median, "0.00")} / {Num(t.PerHit.Max, "0.00")} / {Num(t.PerHit.P90, "0.00")} | {t.DamagePerThrow:0.00} | {best} |"));
            }

            sb.AppendLine();
            sb.AppendLine("<details><summary>距離ごとの曲線</summary>");
            sb.AppendLine();
            foreach (var t in f.Techniques.Where(t => t.Throws >= MinBinThrows))
            {
                sb.AppendLine(string.Create(Inv, $"**{t.Technique}**"));
                sb.AppendLine();
                sb.AppendLine("| d/L | d [cm] | 打ち | 当たり率 | クリーン率 | 1 打あたり | 当たり 平均 | 中央 | 最大 |");
                sb.AppendLine("|---|---|---|---|---|---|---|---|---|");
                foreach (var b in t.Curve)
                {
                    sb.AppendLine(string.Create(Inv,
                        $"| {b.From:0.0}〜{b.To:0.0} | {b.From * f.ReachCm:0}〜{b.To * f.ReachCm:0} | {b.Throws} | {b.HitRate:P0} | {b.CleanRate:P0} | {b.DamagePerThrow:0.00} | {Num(b.PerHit.Mean, "0.00")} | {Num(b.PerHit.Median, "0.00")} | {Num(b.PerHit.Max, "0.00")} |"));
                }

                sb.AppendLine();
            }

            sb.AppendLine("</details>");
            sb.AppendLine();
        }

        return sb.ToString();
    }

    private static string Num(double v, string format) => double.IsNaN(v) ? "-" : v.ToString(format, Inv);

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        NumberHandling = System.Text.Json.Serialization.JsonNumberHandling.AllowNamedFloatingPointLiterals,
        Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
    };

    public static string Json(IReadOnlyList<FighterSummary> fighters, object meta) => JsonSerializer.Serialize(new { meta, fighters }, JsonOptions);

    public static string Csv(IReadOnlyList<Throw> throws)
    {
        var sb = new StringBuilder("attacker,opponent,technique,kind,reach_m,launch_m,impact_m,hits,clean,damage,max_hit\n");
        foreach (var t in throws)
        {
            sb.AppendLine(string.Create(Inv, $"{t.Attacker},{t.Opponent},{t.Technique},{t.Kind},{t.Reach:0.0000},{t.LaunchD:0.0000},{(float.IsNaN(t.ImpactD) ? "" : t.ImpactD.ToString("0.0000", Inv))},{t.Hits},{t.Clean},{t.Damage:0.0000},{t.MaxHit:0.0000}"));
        }

        return sb.ToString();
    }

    // ── 得意・不得意の距離 ──

    internal sealed record RangeBin(string Fighter, double FromCm, double ToCm, double RatioFrom, double RatioTo, double Seconds, double Share, int Bouts,
        double DealtPerSecond, double TakenPerSecond, double NetPerSecond, double NetSe);

    internal sealed record RangeSummary(string Fighter, double ReachCm, double TotalSeconds, double MedianCm, RangeBin? Best, RangeBin? Worst, RangeBin? MostDealt, RangeBin? MostTaken);

    public static (List<RangeBin> Bins, List<RangeSummary> Summary) Ranges(IReadOnlyList<FighterCharacter> roster, IReadOnlyList<RangeCell> cells, IReadOnlyList<Throw> throws)
    {
        var bins = new List<RangeBin>();
        var summary = new List<RangeSummary>();
        foreach (var c in roster)
        {
            var mine = cells.Where(x => x.Fighter == c.Id).ToList();
            if (mine.Count == 0)
            {
                continue;
            }

            var reachCm = (throws.FirstOrDefault(t => t.Attacker == c.Id)?.Reach ?? float.NaN) * 100.0;
            var total = mine.Sum(x => x.Seconds);
            var list = new List<RangeBin>();
            foreach (var g in mine.GroupBy(x => x.Bin).OrderBy(g => g.Key))
            {
                var perBout = g.GroupBy(x => x.Bout).Select(b => (T: b.Sum(x => x.Seconds), Net: b.Sum(x => x.Dealt - x.Taken))).ToList();
                var t = perBout.Sum(p => p.T);
                var net = perBout.Sum(p => p.Net) / t;
                // 比の推定の標準誤差（試合を単位に）: sqrt(n/(n-1) Σ(netᵢ − r tᵢ)²) / Σtᵢ。
                var n = perBout.Count;
                var se = n > 1 ? Math.Sqrt(n / (n - 1.0) * perBout.Sum(p => Math.Pow(p.Net - (net * p.T), 2))) / t : double.NaN;
                var from = g.Key * RangeBinCm;
                var to = from + RangeBinCm;
                list.Add(new RangeBin(c.Id, from, to, from / reachCm, to / reachCm, t, t / total, n, g.Sum(x => x.Dealt) / t, g.Sum(x => x.Taken) / t, net, se));
            }

            bins.AddRange(list);
            var enough = list.Where(b => b.Seconds >= MinRangeSeconds).ToList();
            var cumulative = 0.0;
            var median = double.NaN;
            foreach (var b in list)
            {
                cumulative += b.Seconds;
                if (cumulative >= total / 2)
                {
                    median = (b.FromCm + b.ToCm) / 2;
                    break;
                }
            }

            summary.Add(new RangeSummary(c.Id, reachCm, total, median,
                enough.MaxBy(b => b.NetPerSecond), enough.MinBy(b => b.NetPerSecond), enough.MaxBy(b => b.DealtPerSecond), enough.MaxBy(b => b.TakenPerSecond)));
        }

        return (bins, summary);
    }

    public static string RangesMarkdown(IReadOnlyList<RangeSummary> summary)
    {
        static string Band(RangeBin? b) => b is null ? "-" : string.Create(Inv, $"{b.FromCm:0}〜{b.ToCm:0} cm（腕の {b.RatioFrom:0.00}〜{b.RatioTo:0.00} 倍）");
        static string Net(RangeBin? b) => b is null ? "-" : string.Create(Inv, $"{b.NetPerSecond:+0.00;-0.00} ± {b.NetSe:0.00}");
        var sb = new StringBuilder();
        sb.AppendLine("## 得意な距離・不得意な距離");
        sb.AppendLine();
        sb.AppendLine(string.Create(Inv, $"両方が立っている間だけ、胸と胸の水平距離を {RangeBinCm:0} cm の帯に分け、その帯にいた時間あたりの「与えたダメージ − 受けたダメージ」（差し引き / 秒）を出した。得意 = 差し引きが最大の帯、不得意 = 最小の帯（全試合でその帯に {MinRangeSeconds:0} 秒以上いた帯だけ）。± は試合を単位にした標準誤差。"));
        sb.AppendLine();
        sb.AppendLine("| 選手 | 腕 [cm] | よくいる距離（中央） [cm] | 得意な距離 | 差し引き / 秒 | 不得意な距離 | 差し引き / 秒 | いちばん与える距離 | いちばん受ける距離 |");
        sb.AppendLine("|---|---|---|---|---|---|---|---|---|");
        foreach (var s in summary)
        {
            sb.AppendLine(string.Create(Inv, $"| {s.Fighter} | {s.ReachCm:0.0} | {s.MedianCm:0} | {Band(s.Best)} | {Net(s.Best)} | {Band(s.Worst)} | {Net(s.Worst)} | {Band(s.MostDealt)} | {Band(s.MostTaken)} |"));
        }

        sb.AppendLine();
        return sb.ToString();
    }

    public static string RangesCsv(IReadOnlyList<RangeBin> bins)
    {
        var sb = new StringBuilder("fighter,from_cm,to_cm,mid_cm,ratio_from,ratio_to,seconds,share,bouts,dealt_per_s,taken_per_s,net_per_s,net_se\n");
        foreach (var b in bins)
        {
            sb.AppendLine(string.Create(Inv, $"{b.Fighter},{b.FromCm:0},{b.ToCm:0},{(b.FromCm + b.ToCm) / 2:0},{b.RatioFrom:0.000},{b.RatioTo:0.000},{b.Seconds:0.00},{b.Share:0.0000},{b.Bouts},{b.DealtPerSecond:0.0000},{b.TakenPerSecond:0.0000},{b.NetPerSecond:0.0000},{Se(b.NetSe)}"));
        }

        return sb.ToString();
    }

    public static string RangeSummaryCsv(IReadOnlyList<RangeSummary> summary)
    {
        static string B(RangeBin? b) => b is null ? ",,," : string.Create(Inv, $"{b.FromCm:0},{b.ToCm:0},{b.NetPerSecond:0.0000},{Se(b.NetSe)}");
        var sb = new StringBuilder("fighter,reach_cm,seconds,median_cm,best_from_cm,best_to_cm,best_net_per_s,best_net_se,worst_from_cm,worst_to_cm,worst_net_per_s,worst_net_se,dealt_from_cm,dealt_to_cm,dealt_net_per_s,dealt_net_se,taken_from_cm,taken_to_cm,taken_net_per_s,taken_net_se\n");
        foreach (var s in summary)
        {
            sb.AppendLine(string.Create(Inv, $"{s.Fighter},{s.ReachCm:0.0},{s.TotalSeconds:0},{s.MedianCm:0},{B(s.Best)},{B(s.Worst)},{B(s.MostDealt)},{B(s.MostTaken)}"));
        }

        return sb.ToString();
    }

    private static string Se(double v) => double.IsNaN(v) ? "" : v.ToString("0.0000", Inv);
}
