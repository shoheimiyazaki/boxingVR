using System.Diagnostics;
using System.Globalization;
using System.Numerics;
using Aigis.Render;
using MiniRing.Bots;

namespace MiniRing.Arena;

/// <summary>右ストレートの拳の目標の動かし方（punchlab の比べる軸）。</summary>
internal enum PunchLabPath
{
    /// <summary>今の技の経路（<see cref="PunchSpec.Evaluate"/>。LogicVersion 57 までは構え → 打ち抜く先を等速、58 から最小躍度の曲線）。</summary>
    Standard,

    /// <summary>打ち出しの瞬間に、拳の目標を狙いの点（既定は相手のあごの中心）へ一度に置く（経路なし）。</summary>
    Instant,

    /// <summary>打ち出しの瞬間に、拳の目標を今の経路の終点（あごの奥へ打ち抜く点）へ一度に置く。</summary>
    InstantThrough,

    /// <summary>打ち出しの瞬間に、拳の目標を頭の向こう側（打つ手の肩から頭の中心への向きに、頭の中心から <see cref="PunchLab.Setup.Depth"/> の奥）へ一度に置く。</summary>
    Beyond,
}

/// <summary>
/// 拳の経路の実験（punchlab、2026-11-20）: 右ストレートを 1 本だけ、立って構えた相手へ打つ。比べるのは拳の目標の動かし方だけ
/// （<see cref="PunchLabPath"/>）。胴のひねり・溜め・頭のずれ・踏み込み・体のバネを拳へ移す割合・肘の締めは、ボットのストレートと同じ
/// 共有の実行部（<see cref="TechniqueExecution"/>）から出す。出すのは <see cref="FighterCommand"/> だけで、物理の値は変えない
/// （<c>--nocap</c> の反事実だけ、目標の速度の上限 <see cref="FighterTuning.MaxTargetSpeed"/> を外した体で測る。採らない）。
/// 1 本ごとに新しい試合（種で決まる）を作り、相手と決めた間合いへ歩いてから打つ。守らない相手（still）は手を腰の高さへ下げて立つ
/// （<see cref="GuardPosture.LowGuard"/>。顔の前に手があると、右ストレートが相手の左の拳に当たって顔まで届かなかった）。
/// </summary>
internal sealed class PunchLab(float telegraph, Action<string> log, AimGoal aim = AimGoal.Chin)
{
    private const float Dt = 1f / 240f;

    /// <summary>間合いへ寄せる最長の時間 [s]と、寄せた後に構えを落ち着かせる時間 [s]。</summary>
    private const float ApproachSeconds = 10.07f;
    private const float SettleSeconds = 0.8392f;

    /// <summary>打ち出してから見ている時間 [s]（倒れるか・脳しんとうを見る）。</summary>
    private const float WatchSeconds = 3.357f;

    /// <summary>1 発の拳とみなす、打ち出しからの時間 [s]（この間の右の拳の当たりのうち、いちばん強いもの）。</summary>
    private const float StrikeWindow = 0.7553f;

    /// <summary>反事実の目標の速度の上限 [m/s]（実質なし）。</summary>
    internal const float NoCapSpeed = 1000f;

    /// <summary>1 つの条件（経路・相手・間合い）。</summary>
    /// <param name="Path">拳の目標の動かし方。</param>
    /// <param name="Defender">still（手を下げて立つ守らない相手）か guard（ブロックで守るボット）。</param>
    /// <param name="Range">胸と胸の水平の間 [腕]（reach・far のとき）。</param>
    /// <param name="NoCap">目標の速度の上限を外した反事実（測るだけ）。</param>
    /// <param name="Aim">狙いの点（あごか頭の真ん中）。</param>
    /// <param name="RangeName">reach（届く）・far（半歩遠い）は胸と胸の間 <paramref name="Range"/> [腕]。chin は「腕が伸びきったときに拳の先がちょうどあごに触れる」間合い
    /// （右肩からあごまで = 腕の長さ + グローブの半径。<paramref name="Range"/> は使わない）。</param>
    /// <param name="Depth">頭の向こう側へ置く奥行き [m]（<see cref="PunchLabPath.Beyond"/>）。</param>
    /// <param name="Clamp">拳を引く指令の目標を肩から腕の長さの球へ縮める前の体（PhysicsVersion 62 まで、<see cref="FighterTuning.ClampHandTarget"/> = true。測るだけ）。</param>
    internal readonly record struct Setup(PunchLabPath Path, string Defender, string RangeName, float Range, bool NoCap, AimGoal Aim = AimGoal.Chin, float Depth = 0f, bool Clamp = false)
    {
        public string Label => $"{PathName(Path)}{(Path == PunchLabPath.Beyond ? $" {Depth * 100f:0} cm" : "")}{(Aim == AimGoal.Chin || Path == PunchLabPath.Beyond ? "" : "（狙い: 頭の真ん中）")}{(NoCap ? "（上限なし）" : "")}{(Clamp ? "（縮める）" : "")} / {(Defender == "still" ? "守らない" : "ガード")} / {RangeName switch { "reach" => "届く", "chin" => "伸びきりであご", _ => "半歩遠い" }}";
    }

    /// <summary>
    /// 測定用の入力の差し替え（駆動の DOE、docs/plans/DRIVE_DOE_ANALYSIS.md）。FighterCommand の中身を変えるだけで、物理の値は変えない。
    /// 省略（null）なら従来と同じ。<paramref name="TwistBias"/> = 打ち出しの間に足す胴のひねりの目標 [rad]、<paramref name="StepInScale"/> = 踏み込みの倍、
    /// <paramref name="Spring"/> = 体から拳への移送の割合（0..1、既定 1）。
    /// </summary>
    internal sealed record DriveKnobs(float TwistBias = 0f, float StepInScale = 1f, float Spring = 1f, float TwistLead = 0f, float HandDelay = 0f, float FootDrive = 0f);

    /// <summary>拳を目標へ引いた 1 歩（打ち出しからの時刻・縮める前と後の目標と拳の距離・欲しかった力・力の上限・実際の力・拳の速さ）。</summary>
    internal readonly record struct DriveStep(float T, float RawGap, float ClampedGap, float Desired, float Cap, float Force, float Speed);

    /// <summary>1 本の測定。</summary>
    internal readonly record struct Trial(int Seed, float Distance, bool Hit, HitZone Zone, float TimeToHit, float Peak, float FistAtHit, float Closing, float Mass,
        float Energy, float Damage, float HeadTwist, float Concussion, float Glow, float Bruise, bool Fell, IReadOnlyList<(float T, float Speed)> Series, bool Settled = true,
        float ChinGap = float.NaN, IReadOnlyList<DriveStep>? Drive = null, float Lunge = 0f, bool AttackerFell = false);

    internal static string PathName(PunchLabPath path) => path switch
    {
        PunchLabPath.Standard => "標準",
        PunchLabPath.Instant => "0 秒で狙いへ",
        PunchLabPath.Beyond => "0 秒で頭の奥",
        _ => "0 秒で打ち抜く点",
    };

    public static BotStyle GuardStyle(float range) => BotCatalog.Boxer with
    {
        Name = "guard",
        Attacks = false,
        CounterRate = 0f,
        ClinchRate = 0f,
        Circling = 0f,
        HeadMovement = 0f,
        DefenseRate = 1f,
        BlockWeight = 1f,
        DuckWeight = 0f,
        SlipWeight = 0f,
        SwayWeight = 0f,
        StepBackWeight = 0f,
        AttackRange = range,
        NeutralRange = range,
        EngageRange = range,
        EdgeRange = 0f,
    };

    /// <summary>1 本の試合: 相手（守らない的か、ガードするボクサー）と、間合い・経路を決めた打ち手。</summary>
    internal sealed class Bout : IDisposable
    {
        private readonly Puncher _puncher;
        private readonly float _range;
        private float _clock;
        private int _stable;
        private float? _lastDistance;

        public Bout(Setup setup, int seed, float telegraph, FighterTuning? measuredTuning = null, DriveKnobs? knobs = null)
        {
            _range = setup.Range;
            Setup = setup;
            ControlFactory defender = setup.Defender == "still" ? _ => new StandGuard(GuardPosture.LowGuard) : s => new BoxerBot(GuardStyle(setup.Range).Validated(), s);
            var tuning = (measuredTuning ?? FighterTuning.Default) with { MaxTargetSpeed = setup.NoCap ? NoCapSpeed : (measuredTuning ?? FighterTuning.Default).MaxTargetSpeed, ClampHandTarget = setup.Clamp };
            Game = new BoxingGame(tuning, seed, new BoxingCorners(setup.Defender, defender));
            _puncher = new Puncher(setup.Path, telegraph, setup.Aim, setup.Depth, knobs);
        }

        public Setup Setup { get; }

        /// <summary>間合いに寄せ終わってから打ち始めたか（偽なら寄せる時間が尽きて、その場の間合いで打った）。</summary>
        public bool Settled { get; private set; }

        public BoxingGame Game { get; }

        /// <summary>打ち出しからの時間 [s]（溜めの間は負、寄せている間は null）。</summary>
        public float? PunchTime => _puncher.Started ? _puncher.Elapsed : null;

        public Vector3? TargetWorld { get; private set; }

        public float Distance => Vector3.Distance(Game.Player.ChestPosition with { Y = 0f }, Game.Cpu.ChestPosition with { Y = 0f }) / Game.Player.ArmReach;

        /// <summary>
        /// 右肩からあごまでの距離 − (腕の長さ + グローブの半径) [m]: 0 なら、腕を肩からあごへ伸ばしきったときにグローブの先がちょうどあごに触れる。
        /// あごの点は相手の体のボクセルの目印（顔の前の皮膚の上）。
        /// </summary>
        public float ChinGap
        {
            get
            {
                var shoulder = Game.Player.ShoulderPosition(Hand.Right);
                var chin = Game.PlayerSense.OpponentTargets?.Point(AimGoal.Chin, shoulder) ?? Game.Cpu.HeadPosition;
                return Vector3.Distance(shoulder, chin) - (Game.Player.ArmReach + Game.Player.GloveRadius);
            }
        }

        /// <summary>寄せる間合いの誤差 [腕]（正 = 遠い）。</summary>
        private float RangeError => Setup.RangeName == "chin" ? ChinGap / Game.Player.ArmReach : Distance - _range;

        /// <summary>1 歩進める。寄せ終わって落ち着いたら打ち始める。</summary>
        public void Step()
        {
            var sense = Game.PlayerSense;
            var toward = Vector3.Normalize((Game.Cpu.ChestPosition - Game.Player.ChestPosition) with { Y = 0f });
            if (!_puncher.Started)
            {
                var error = RangeError;
                _stable = MathF.Abs(error) < (Setup.RangeName == "chin" ? 0.02f : 0.04f) ? _stable + 1 : 0;
                // 誤差に比例した歩きに、間合いの変わる速さで制動を掛ける（比例だけだと足の運びの遅れで ±0.17 腕揺れ続けた）。
                var closing = _lastDistance is { } last ? (error - last) / Dt : 0f;
                _lastDistance = error;
                var move = toward * Math.Clamp((2f * error) + (1.007f * closing), -1f, 1f);
                _clock += Dt;
                if (_stable * Dt >= SettleSeconds || _clock > ApproachSeconds)
                {
                    Settled = _stable * Dt >= SettleSeconds;
                    _puncher.Start(sense, Game.Player.ShoulderPosition(Hand.Right));
                }

                Game.Step(new BoxingInput(_puncher.Command(sense, move, Dt)), Dt);
            }
            else
            {
                var command = _puncher.Command(sense, toward * _puncher.StepIn, Dt);
                TargetWorld = sense.Self.LocalToWorld(command.Right.TargetLocal);
                Game.Step(new BoxingInput(command), Dt);
            }
        }

        public void Dispose() => Game.Dispose();
    }

    /// <summary>
    /// 右ストレートだけを打つ入力元。体の目標はボットと同じ共有の実行部から出し、拳の目標だけを <see cref="PunchLabPath"/> で差し替える。
    /// </summary>
    internal sealed class Puncher(PunchLabPath path, float telegraph, AimGoal aim, float depth = 0f, DriveKnobs? knobs = null)
    {
        private static readonly BotStyle Style = BotCatalog.Boxer;
        private readonly PunchSpec _spec = PunchBook.Default.Of(Technique.Cross);
        private Vector3 _head;

        public bool Started { get; private set; }

        public PunchAction Action { get; private set; }

        /// <summary>打ち出しからの時間 [s]（溜めの間は負）。技が終わった後も進む（<see cref="PunchAction.Time"/> は技の間だけ）。</summary>
        public float Elapsed { get; private set; }

        /// <summary>今の踏み込み（移動指令の前向き成分、ボットと同じ <c>StepIn × Envelope</c>）。</summary>
        public float StepIn => Action.Active ? _spec.StepIn * (knobs?.StepInScale ?? 1f) * TechniqueExecution.Envelope(Action) : 0f;

        public void Start(in FighterSense sense, Vector3 punchingShoulder)
        {
            var from = sense.Self.LocalToWorld(Vector3.Zero);
            // あご（既定）か頭の真ん中（Center）。この体は首が短く、あごは胸の上端から拳 1 つぶんほどしか上に無い。
            var point = aim == AimGoal.Chin ? sense.OpponentTargets?.Point(AimGoal.Chin, from) ?? sense.OpponentHead : sense.OpponentHead;
            if (path == PunchLabPath.Beyond)
            {
                // 頭の向こう側: 打つ手の肩から頭の中心への向き（拳の通り道の延長）に、頭の中心から depth だけ奥。
                var head = sense.OpponentHead;
                var toward = head - punchingShoulder;
                point = head + ((toward.LengthSquared() > 1e-8f ? Vector3.Normalize(toward) : sense.Self.Forward) * depth);
            }
            Action = new PunchAction(Technique.Cross, _spec, -telegraph, _spec.Duration, false, AimWorld: point, Load: telegraph, Goal: aim);
            Started = true;
            Elapsed = -telegraph;
        }

        public FighterCommand Command(in FighterSense sense, Vector3 move, float dt)
        {
            var self = sense.Self;
            var reach = sense.ArmReach;
            var headLocal = self.WorldToLocal(sense.SelfHead);
            var face = sense.OpponentChest - self.Origin;
            face.Y = 0f;
            Vector3 Guard(Hand hand) => TechniqueExecution.Guard(hand, Style.Lead, reach, headLocal, Style.GuardHeight, Style.GuardWidth, Style.GuardReach, 0f);
            var right = Guard(Hand.Right);
            var spring = 0f;
            var twist = 0f;
            var headTarget = Vector3.Zero;
            if (Started && Action.Active)
            {
                var aim = self.WorldToLocal(Action.AimWorld);
                var handAction = TechniqueExecution.Shift(Action, knobs is { } handSettings ? -handSettings.HandDelay : TechniqueExecution.PunchHandAdvanceSeconds);
                right = Target(handAction, aim, Guard(Hand.Right), reach);
                spring = TechniqueExecution.Spring(handAction) * (knobs?.Spring ?? 1f);
                twist = TechniqueExecution.Twist(TechniqueExecution.Shift(Action, knobs?.TwistLead ?? 0f), Hand.Right) + ((knobs?.TwistBias ?? 0f) * (Action.Time >= 0f ? MathF.Max(0f, 1f - (2f * Action.Time / Action.Duration)) : 0f));
                headTarget = TechniqueExecution.HeadShift(Action, Style.Lead, reach);
            }

            _head = Vector3.Lerp(_head, headTarget, 1f - MathF.Exp(-7.150f * dt)); // ボットの頭の目標の追い方と同じ
            var command = new FighterCommand(move, face.LengthSquared() > 1e-6f ? Vector3.Normalize(face) : Vector3.UnitZ,
                new HandCommand(Guard(Hand.Left), false, Tuck: Style.ElbowTuck),
                new HandCommand(right, false, spring, Tuck: TechniqueExecution.Tuck(Action, Style.ElbowTuck, false)),
                _head, twist, Step: TechniqueExecution.GroundDrive(Action, face.LengthSquared() > 1e-6f ? Vector3.Normalize(face) : Vector3.UnitZ,
                    knobs?.TwistLead ?? 0f, knobs?.FootDrive ?? TechniqueExecution.PunchGroundDrive));
            if (Started)
            {
                Elapsed += dt;
                if (Action.Active)
                {
                    Action = Action with { Time = Action.Time + dt };
                }
            }

            return command;
        }

        private Vector3 Target(in PunchAction handAction, Vector3 aim, Vector3 guard, float reach)
        {
            if (path == PunchLabPath.Standard || Action.Time < 0f)
            {
                return TechniqueExecution.Punch(handAction, Hand.Right, Style.Lead, guard, aim, reach);
            }

            // 打ち出しの瞬間に一度に置いた目標（あご、または打ち抜く点）。狙いはワールドの点で保つ（体フレームは体と一緒に動く）。
            var outbound = Action.Duration * PunchSpec.OutboundShare;
            var back = MathF.Max(0f, (Action.Time - outbound) / (Action.Duration - outbound));
            var placed = path is PunchLabPath.Instant or PunchLabPath.Beyond ? aim : PunchPath.Through(aim, _spec.Overshoot, reach);
            return Vector3.Lerp(placed, guard, PunchPath.MinimumJerk(back)); // 戻りは今の経路と同じ曲線
        }
    }

    public Trial Measure(Setup setup, int seed, bool series)
    {
        using var bout = new Bout(setup, seed, telegraph);
        var game = bout.Game;
        while (bout.PunchTime is null)
        {
            bout.Step();
        }

        var distance = bout.Distance;
        var chinGap = bout.ChinGap;
        var drive = new List<DriveStep>();
        var victim = game.Cpu;
        var attacker = game.Player;
        var bruise0 = victim.Voxels?.SkinBruiseTotal() ?? 0f;
        var concussion0 = victim.Concussion;
        var points = new List<(float, float)>();
        HitEvent? best = null;
        float bestEnergy = -1f, timeToHit = -1f, fistAtHit = 0f, peak = 0f, twist = 0f, concussion = 0f;
        var fell = false;
        // つんのめり: 打ち手の骨盤が打ち出しの所から相手の向きへ出た水平の距離の最大、と打ち手が倒れたか。
        var pelvis0 = attacker.PelvisPosition;
        var toward = victim.PelvisPosition - pelvis0;
        toward = toward with { Y = 0f };
        toward = toward.LengthSquared() > 1e-8f ? Vector3.Normalize(toward) : attacker.Frame.Forward;
        var lunge = 0f;
        var attackerFell = false;
        var outbound = PunchBook.Default.Of(Technique.Cross).Duration * PunchSpec.OutboundShare;
        while (bout.PunchTime is { } t && t < WatchSeconds)
        {
            var fist = attacker.GloveVelocity(Hand.Right).Length();
            if (t >= 0f && t <= outbound + 0.08392f)
            {
                peak = MathF.Max(peak, fist);
            }

            if (series && t >= -0.1678f && t <= 0.6714f)
            {
                points.Add((t, fist));
            }

            bout.Step();
            var after = bout.PunchTime ?? t; // 当たりはこの歩で起きた: 時刻は歩の終わり、拳の速さは当たる前の歩の値（当たった後は接触で揃ってしまう）
            if (t >= 0f && best is null && t <= outbound)
            {
                var trace = attacker.HandTrace(Hand.Right);
                drive.Add(new DriveStep(after, Vector3.Distance(trace.RawTarget, trace.Glove), Vector3.Distance(trace.Target, trace.Glove), trace.Desired, trace.Cap, trace.Force, trace.GloveSpeed));
            }

            foreach (var hit in game.RecentHits.Where(h => h.Attacker == attacker && h.Hand == Hand.Right && !h.Kick && !h.Elbow))
            {
                if (after > 0f && after <= StrikeWindow && hit.Energy > bestEnergy)
                {
                    best = hit;
                    bestEnergy = hit.Energy;
                    timeToHit = after;
                    fistAtHit = fist;
                    twist = victim.LastHeadTwist;
                }
            }

            concussion = MathF.Max(concussion, victim.Concussion - concussion0);
            fell |= victim.State != FighterState.Standing;
            lunge = MathF.Max(lunge, Vector3.Dot(attacker.PelvisPosition - pelvis0, toward));
            attackerFell |= attacker.State != FighterState.Standing;
        }

        var bruise = (victim.Voxels?.SkinBruiseTotal() ?? 0f) - bruise0;
        return best is { } h
            ? new Trial(seed, distance, true, h.Zone, timeToHit, peak, fistAtHit, h.Speed, h.ImpactMass, h.Energy, h.Damage, twist, concussion, BoxingGame.HitStrength(h.Damage), bruise, fell, points, bout.Settled, chinGap, drive, lunge, attackerFell)
            : new Trial(seed, distance, false, HitZone.Body, -1f, peak, 0f, 0f, 0f, 0f, 0f, 0f, concussion, 0f, bruise, fell, points, bout.Settled, chinGap, drive, lunge, attackerFell);
    }

    public IReadOnlyList<(Setup Setup, IReadOnlyList<Trial> Trials)> Run(IReadOnlyList<Setup> setups, IReadOnlyList<int> seeds)
    {
        var jobs = setups.SelectMany(s => seeds.Select(seed => (Setup: s, Seed: seed))).ToArray();
        var trials = new Trial[jobs.Length];
        // この PC はメモリが少ないので並びを抑える（試合 1 本で数百 MB）。
        Parallel.For(0, jobs.Length, new ParallelOptions { MaxDegreeOfParallelism = Math.Clamp(Environment.ProcessorCount / 2, 1, 3) },
            i => trials[i] = Measure(jobs[i].Setup, jobs[i].Seed, series: jobs[i].Seed == seeds[0]));
        var results = setups.Select(s => (s, (IReadOnlyList<Trial>)trials.Where((_, i) => jobs[i].Setup == s).ToList())).ToList();
        Report(results);
        return results;
    }

    private static string F(float v, string format = "0.00") => v.ToString(format, CultureInfo.InvariantCulture);

    private void Report(IReadOnlyList<(Setup Setup, IReadOnlyList<Trial> Trials)> results)
    {
        log($"狙い {(aim == AimGoal.Chin ? "あご" : "頭の真ん中")}、溜め {F(telegraph)} s、右ストレート（{F(PunchBook.Default.Of(Technique.Cross).Duration)} s）。値は当たった本の平均（最高速だけは全部の本）。\n");
        log("| 条件 | 間合い [腕] | 当たり / 本 | 当たった所 | 当たるまで [s] | 打ち出しの最高速 [m/s] | 当たった瞬間の拳 [m/s] | 当たる速さ（相対）[m/s] | 乗る質量 [kg] | エネルギー [J] | ダメージ | 頭の回転 [rad/s] | 脳しんとう | 光 | あざ | 倒れた | 打ち手の骨盤が前へ [cm] | 打ち手が倒れた |");
        log("|---|---|---|---|---|---|---|---|---|---|---|---|---|---|---|---|---|---|");
        foreach (var (setup, trials) in results)
        {
            var hits = trials.Where(t => t.Hit).ToList();
            float Mean(Func<Trial, float> f) => hits.Count > 0 ? hits.Average(f) : 0f;
            var unsettled = trials.Count(t => !t.Settled);
            log($"| {setup.Label} | {F(trials.Average(t => t.Distance))}{(unsettled > 0 ? $"（寄せきれず {unsettled}）" : "")} | {hits.Count} / {trials.Count} | {string.Join(" ", hits.GroupBy(h => h.Zone).Select(g => $"{g.Key} {g.Count()}"))} | {F(Mean(t => t.TimeToHit), "0.000")} | {F(trials.Average(t => t.Peak))} | {F(Mean(t => t.FistAtHit))} | {F(Mean(t => t.Closing))} | {F(Mean(t => t.Mass))} | {F(Mean(t => t.Energy))} | {F(Mean(t => t.Damage), "0.0")} | {F(Mean(t => t.HeadTwist), "0.0")} | {F(trials.Average(t => t.Concussion), "0.000")} | {F(Mean(t => t.Glow))} | {F(trials.Average(t => t.Bruise), "0.0")} | {trials.Count(t => t.Fell)} | {F(trials.Average(t => t.Lunge * 100f), "0.0")} | {trials.Count(t => t.AttackerFell)} |");
        }

        if (results.Any(r => r.Setup.RangeName == "chin"))
        {
            log("\n伸びきりであごの間合い: 打ち出しの瞬間の「右肩からあごまで − (腕の長さ + グローブの半径)」[cm]（0 で伸びきりの拳の先がちょうどあご）: "
                + string.Join("、", results.Where(r => r.Setup.RangeName == "chin").Take(1).SelectMany(r => r.Trials).Select(t => F(t.ChinGap * 100f, "0.0"))));
        }

        log("\n拳を引くバネの中身（打ち出しから当たる歩まで、または振り出しの終わりまで。種の平均）。縮める前 / 後 = 目標（肩から腕の長さの球へ縮める前と後）と拳の距離、"
            + "欲しい力 = HandKp・減衰の PD の加速度 × 腕の質量、上限 = 腕の筋力 × 体を借りられる倍率、上限に当たった歩 = 欲しい力が上限を越えた歩の割合:\n");
        log("| 条件 | 歩 | 最初の歩: 縮める前 / 後 [cm] | 欲しい力 最大 / 平均 [N] | 上限 平均 [N] | 掛けた力 最大 / 平均 [N] | 上限に当たった歩 | 拳の速さ 最大 [m/s] |");
        log("|---|---|---|---|---|---|---|---|");
        foreach (var (setup, trials) in results)
        {
            var steps = trials.Where(t => t.Drive is { Count: > 0 }).ToList();
            if (steps.Count == 0)
            {
                continue;
            }

            float M(Func<IReadOnlyList<DriveStep>, float> f) => steps.Average(t => f(t.Drive!));
            log($"| {setup.Label} | {F(M(d => d.Count), "0")} | {F(M(d => d[0].RawGap) * 100f, "0.0")} / {F(M(d => d[0].ClampedGap) * 100f, "0.0")} | {F(M(d => d.Max(s => s.Desired)), "0")} / {F(M(d => d.Average(s => s.Desired)), "0")} | {F(M(d => d.Average(s => s.Cap)), "0")} | {F(M(d => d.Max(s => s.Force)), "0")} / {F(M(d => d.Average(s => s.Force)), "0")} | {F(M(d => d.Count(s => s.Desired > s.Cap * 1.001f) / (float)d.Count) * 100f, "0")} % | {F(M(d => d.Max(s => s.Speed)))} |");
        }

        log("\n歩ごとの中身（最初の種、4 ms ごと。縮める前 / 後 [cm]・欲しい力 / 上限 / 掛けた力 [N]・拳の速さ [m/s]）:\n");
        foreach (var (setup, trials) in results.Where(r => r.Trials.Count > 0 && r.Trials[0].Drive is { Count: > 0 }))
        {
            log($"- {setup.Label}: " + string.Join("  ", trials[0].Drive!.Select(s => $"{F(s.T, "0.000")}s {F(s.RawGap * 100f, "0.0")}/{F(s.ClampedGap * 100f, "0.0")}cm {F(s.Desired, "0")}/{F(s.Cap, "0")}/{F(s.Force, "0")}N {F(s.Speed)}")));
        }

        log("\n拳の速さの時系列（最初の種、約 17 ms ごと、[m/s]。t = 0 が打ち出し、負は溜め）:\n");
        var first = results.Where(r => r.Trials.Count > 0).Select(r => (r.Setup, r.Trials[0])).ToList();
        log("| t [s] | " + string.Join(" | ", first.Select(f => f.Setup.Label)) + " |");
        log("|---|" + string.Concat(first.Select(_ => "---|")));
        for (var t = -0.1678f; t <= 0.6715f; t += 0.01678f)
        {
            var row = first.Select(f => f.Item2.Series.Count == 0 ? "" : F(f.Item2.Series.MinBy(p => MathF.Abs(p.T - t)).Speed));
            log($"| {F(t, "0.000")} | {string.Join(" | ", row)} |");
        }
    }

    // ---------------- 動画 ----------------

    private static readonly Vector4 White = new(1f, 1f, 1f, 1f);
    private static readonly Vector4 Grey = new(0.75f, 0.78f, 0.85f, 1f);
    private static readonly Vector4 Gold = new(1f, 0.85f, 0.3f, 1f);

    /// <summary>
    /// 左右に並べた動画: 1 つの場面ごとに、左が標準のストレート、右が比べる経路。同じ種・同じ間合いの 2 つの試合を同じ歩で進め、
    /// 打ち出しの前後（<paramref name="slowFrom"/>〜<paramref name="slowTo"/> s）を 1/<paramref name="slow"/> のスローで撮る。
    /// 金の線は拳の目標（拳から目標へ）。
    /// </summary>
    public static void Video(IReadOnlyList<(Setup Left, Setup Right)> scenes, int seed, float telegraph, float slow, int width, int height, int fps, string outPath, Action<string> log,
        float slowFrom = -0.2014f, float slowTo = 0.6714f, float after = 2.685f, bool closeUp = false)
    {
        width -= width % 4; // 左右の半分がそれぞれ偶数の幅（yuv420p は奇数の幅を受けない）
        var half = width / 2;
        using var renderer = new HeadlessRenderer((uint)half, (uint)height);
        using var ffmpeg = Process.Start(new ProcessStartInfo("ffmpeg",
            $"-y -loglevel error -f rawvideo -pix_fmt rgba -s {width}x{height} -r {fps} -i - -c:v libx264 -pix_fmt yuv420p -crf 18 -preset medium \"{outPath}\"")
        {
            RedirectStandardInput = true,
            UseShellExecute = false,
        }) ?? throw new IOException("ffmpeg を起動できません（PATH にありますか）。");
        var frame = new byte[width * height * 4];
        foreach (var (leftSetup, rightSetup) in scenes)
        {
            using var left = new Bout(leftSetup, seed, telegraph);
            using var right = new Bout(rightSetup, seed, telegraph);
            var bouts = new[] { left, right };
            var notes = new string[2];
            var peaks = new float[2];
            // 寄せる間は撮らない。打ち始め（溜めの始まり）で両方を揃える（同じ種・同じ間合いなので、寄せ終わるまでの動きは左右で同じ）。
            foreach (var b in bouts)
            {
                while (b.PunchTime is null)
                {
                    b.Step();
                }
            }

            for (var k = 0f; k < 1.0f; k += 1f / fps)
            {
                Write(ffmpeg, frame, width, height, half, bouts.Select((b, i) => Render(renderer, half, height, b, closeUp, [
                    new((i == 0 ? "左: " : "右: ") + b.Setup.Label, new Vector2(half * 0.5f, height * 0.4f), 26f, White, TextAnchor.Center),
                    new($"打ち出しの前後は 1/{F(slow, "0.#")} のスロー", new Vector2(half * 0.5f, height * 0.5f), 22f, Gold, TextAnchor.Center),
                ], 0.55f)).ToArray());
            }

            var nextFrame = float.NegativeInfinity;
            while (bouts.Any(b => b.PunchTime is not { } t || t < after))
            {
                for (var i = 0; i < 2; i++)
                {
                    var b = bouts[i];
                    if (b.PunchTime is { } t0 && t0 >= after)
                    {
                        continue;
                    }

                    var fist = b.Game.Player.GloveVelocity(Hand.Right).Length();
                    if (b.PunchTime is >= 0f and <= 0.5035f)
                    {
                        peaks[i] = MathF.Max(peaks[i], fist);
                    }

                    b.Step();
                    foreach (var hit in b.Game.RecentHits.Where(h => h.Attacker == b.Game.Player && h.Hand == Hand.Right && !h.Kick && !h.Elbow))
                    {
                        notes[i] = $"当たり {hit.Zone}  {F(hit.Speed)} m/s  {F(hit.ImpactMass)} kg  {F(hit.Energy)} J  ダメージ {F(hit.Damage, "0.0")}  頭の回転 {F(b.Game.Cpu.LastHeadTwist, "0.0")} rad/s";
                    }
                }

                var clock = left.PunchTime ?? 0f;
                if (clock < nextFrame)
                {
                    continue;
                }

                var isSlow = clock >= slowFrom && clock <= slowTo;
                nextFrame = clock + ((isSlow ? 1f / slow : 1f) / fps);
                Write(ffmpeg, frame, width, height, half, bouts.Select((b, i) =>
                {
                    var lines = new List<DebugLine>();
                    if (b.TargetWorld is { } target && b.PunchTime is { } pt && pt <= 0.6714f)
                    {
                        lines.Add(new DebugLine(b.Game.Player.GloveCenter(Hand.Right), target, Gold));
                    }

                    return Render(renderer, half, height, b, closeUp, [
                        new(b.Setup.Label, new Vector2(24f, 34f), 22f, White, TextAnchor.Left),
                        new($"拳 {F(b.Game.Player.GloveVelocity(Hand.Right).Length())} m/s  最高 {F(peaks[i])} m/s", new Vector2(24f, 66f), 20f, Gold, TextAnchor.Left),
                        new(notes[i] ?? "", new Vector2(24f, 96f), 16f, Grey, TextAnchor.Left),
                        new($"{F(b.PunchTime ?? 0f)} s{(isSlow ? $"  1/{F(slow, "0.#")} のスロー" : "")}", new Vector2(half - 24f, height - 30f), 20f, Grey, TextAnchor.Right),
                    ], 0f, lines);
                }).ToArray());
            }

            log($"scene: {leftSetup.Label} | {rightSetup.Label}  peaks {F(peaks[0])} / {F(peaks[1])}");
        }

        ffmpeg.StandardInput.Close();
        ffmpeg.WaitForExit();
        if (ffmpeg.ExitCode != 0)
        {
            throw new IOException($"ffmpeg failed with exit code {ffmpeg.ExitCode}.");
        }
    }

    private static void Write(Process ffmpeg, byte[] frame, int width, int height, int half, byte[][] halves)
    {
        for (var y = 0; y < height; y++)
        {
            for (var i = 0; i < halves.Length; i++)
            {
                Buffer.BlockCopy(halves[i], y * half * 4, frame, ((y * width) + (i * half)) * 4, half * 4);
            }
        }

        ffmpeg.StandardInput.BaseStream.Write(frame);
    }

    private static byte[] Render(HeadlessRenderer renderer, int width, int height, Bout bout, bool closeUp, IReadOnlyList<ScreenTextDraw> text, float dim, IReadOnlyList<DebugLine>? extra = null)
    {
        var game = bout.Game;
        var instances = new List<PrimitiveInstance>();
        var lines = new List<DebugLine>();
        BoxingRenderer.Fill(game, showTargetGhosts: false, instances, lines);
        if (extra is not null)
        {
            lines.AddRange(extra);
        }

        var a = game.Player.ChestPosition;
        var v = game.Cpu.ChestPosition;
        var direction = Vector3.Normalize((v - a) with { Y = 0f });
        var side = Vector3.Cross(Vector3.UnitY, direction);
        Vector3 middle, eye;
        if (closeUp)
        {
            // 拳とあごを大きく: 打ち手の右肩と相手の頭の間を、あごの高さの真横から（右手の側）。
            middle = (game.Player.ShoulderPosition(Hand.Right) + game.Cpu.HeadPosition) * 0.5f + new Vector3(0f, -0.05634f, 0f);
            eye = middle - (side * 1.070f) + new Vector3(0f, 0.02817f, 0f);
        }
        else
        {
            middle = ((a + v) * 0.5f) + new Vector3(0f, -0.1408f, 0f);
            eye = middle - (side * 1.972f) + new Vector3(0f, 0.1127f, 0f); // 打ち手の右手の側から真横に
        }

        var (vp, displayEye) = DisplaySpace.Standard.Camera(eye, middle, Vector3.UnitY, 45f * MathF.PI / 180f, (float)width / height, 0.02f, 30f);
        UiShapeDraw[] shapes = dim > 0f ? [new UiShapeDraw(new Vector2(width * 0.5f, height * 0.5f), new Vector2(width, height), new Vector4(0f, 0f, 0f, dim))] : [];
        return renderer.RenderPrimitiveScene(ArenaLook.Background, vp, instances, lines, text, particles: game.BuildParticles(), lighting: game.BuildLighting(),
            shadow: game.BuildShadow(), cameraPosition: displayEye, shapes: shapes, postProcess: game.BuildPostProcess(), voxels: game.BuildVoxelVolumes());
    }
}
