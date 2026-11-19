using System.Diagnostics;
using System.Globalization;
using System.Numerics;
using Aigis.Render;
using MiniRing.Bots;

namespace MiniRing.Arena;

/// <summary>右ストレートの拳の目標の動かし方（punchlab の比べる軸）。</summary>
internal enum PunchLabPath
{
    /// <summary>今の技の経路（<see cref="PunchSpec.Evaluate"/>: 構え → 打ち抜く先を等速で）。</summary>
    Standard,

    /// <summary>打ち出しの瞬間に、拳の目標を狙いの点（既定は相手のあごの中心）へ一度に置く（経路なし）。</summary>
    Instant,

    /// <summary>打ち出しの瞬間に、拳の目標を今の経路の終点（あごの奥へ打ち抜く点）へ一度に置く。</summary>
    InstantThrough,
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
    private const float ApproachSeconds = 6f;
    private const float SettleSeconds = 0.5f;

    /// <summary>打ち出してから見ている時間 [s]（倒れるか・脳しんとうを見る）。</summary>
    private const float WatchSeconds = 2f;

    /// <summary>1 発の拳とみなす、打ち出しからの時間 [s]（この間の右の拳の当たりのうち、いちばん強いもの）。</summary>
    private const float StrikeWindow = 0.45f;

    /// <summary>反事実の目標の速度の上限 [m/s]（実質なし）。</summary>
    internal const float NoCapSpeed = 1000f;

    internal readonly record struct Setup(PunchLabPath Path, string Defender, string RangeName, float Range, bool NoCap, AimGoal Aim = AimGoal.Chin)
    {
        public string Label => $"{PathName(Path)}{(Aim == AimGoal.Chin ? "" : "（狙い: 頭の真ん中）")}{(NoCap ? "（上限なし）" : "")} / {(Defender == "still" ? "守らない" : "ガード")} / {(RangeName == "reach" ? "届く" : "半歩遠い")}";
    }

    /// <summary>1 本の測定。</summary>
    internal readonly record struct Trial(int Seed, float Distance, bool Hit, HitZone Zone, float TimeToHit, float Peak, float FistAtHit, float Closing, float Mass,
        float Energy, float Damage, float HeadTwist, float Concussion, float Glow, float Bruise, bool Fell, IReadOnlyList<(float T, float Speed)> Series);

    internal static string PathName(PunchLabPath path) => path switch
    {
        PunchLabPath.Standard => "標準",
        PunchLabPath.Instant => "0 秒で狙いへ",
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

        public Bout(Setup setup, int seed, float telegraph)
        {
            _range = setup.Range;
            Setup = setup;
            ControlFactory defender = setup.Defender == "still" ? _ => new StandGuard(GuardPosture.LowGuard) : s => new BoxerBot(GuardStyle(setup.Range).Validated(), s);
            var tuning = setup.NoCap ? new FighterTuning { MaxTargetSpeed = NoCapSpeed } : new FighterTuning();
            Game = new BoxingGame(tuning, seed, new BoxingCorners(setup.Defender, defender));
            _puncher = new Puncher(setup.Path, telegraph, setup.Aim);
        }

        public Setup Setup { get; }

        public BoxingGame Game { get; }

        /// <summary>打ち出しからの時間 [s]（溜めの間は負、寄せている間は null）。</summary>
        public float? PunchTime => _puncher.Started ? _puncher.Elapsed : null;

        public Vector3? TargetWorld { get; private set; }

        public float Distance => Vector3.Distance(Game.Player.ChestPosition with { Y = 0f }, Game.Cpu.ChestPosition with { Y = 0f }) / Game.Player.ArmReach;

        /// <summary>1 歩進める。寄せ終わって落ち着いたら打ち始める。</summary>
        public void Step()
        {
            var sense = Game.PlayerSense;
            var toward = Vector3.Normalize((Game.Cpu.ChestPosition - Game.Player.ChestPosition) with { Y = 0f });
            if (!_puncher.Started)
            {
                var error = Distance - _range;
                _stable = MathF.Abs(error) < 0.04f ? _stable + 1 : 0;
                // 誤差に比例した歩きに、間合いの変わる速さで制動を掛ける（比例だけだと足の運びの遅れで ±0.17 腕揺れ続けた）。
                var closing = _lastDistance is { } last ? (Distance - last) / Dt : 0f;
                _lastDistance = Distance;
                var move = toward * Math.Clamp((2f * error) + (0.6f * closing), -1f, 1f);
                _clock += Dt;
                if (_stable * Dt >= SettleSeconds || _clock > ApproachSeconds)
                {
                    _puncher.Start(sense);
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
    internal sealed class Puncher(PunchLabPath path, float telegraph, AimGoal aim)
    {
        private static readonly BotStyle Style = BotCatalog.Boxer;
        private readonly PunchSpec _spec = PunchBook.Default.Of(Technique.Cross);
        private Vector3 _head;

        public bool Started { get; private set; }

        public PunchAction Action { get; private set; }

        /// <summary>打ち出しからの時間 [s]（溜めの間は負）。技が終わった後も進む（<see cref="PunchAction.Time"/> は技の間だけ）。</summary>
        public float Elapsed { get; private set; }

        /// <summary>今の踏み込み（移動指令の前向き成分、ボットと同じ <c>StepIn × Envelope</c>）。</summary>
        public float StepIn => Action.Active ? _spec.StepIn * TechniqueExecution.Envelope(Action) : 0f;

        public void Start(in FighterSense sense)
        {
            var from = sense.Self.LocalToWorld(Vector3.Zero);
            // あご（既定）か頭の真ん中（Center）。この体は首が短く、あごは胸の上端から拳 1 つぶんほどしか上に無い。
            var point = aim == AimGoal.Chin ? sense.OpponentTargets?.Point(AimGoal.Chin, from) ?? sense.OpponentHead : sense.OpponentHead;
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
                right = Target(aim, Guard(Hand.Right), reach);
                spring = TechniqueExecution.Spring(Action);
                twist = TechniqueExecution.Twist(Action, Hand.Right, Style.Lead);
                headTarget = TechniqueExecution.HeadShift(Action, Style.Lead, reach);
            }

            _head = Vector3.Lerp(_head, headTarget, 1f - MathF.Exp(-12f * dt)); // ボットの頭の目標の追い方と同じ
            var command = new FighterCommand(move, face.LengthSquared() > 1e-6f ? Vector3.Normalize(face) : Vector3.UnitZ,
                new HandCommand(Guard(Hand.Left), false, Tuck: Style.ElbowTuck),
                new HandCommand(right, false, spring, Tuck: TechniqueExecution.Tuck(Action, Style.ElbowTuck, false)),
                _head, twist);
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

        private Vector3 Target(Vector3 aim, Vector3 guard, float reach)
        {
            if (path == PunchLabPath.Standard || Action.Time < 0f)
            {
                return TechniqueExecution.Punch(Action, Hand.Right, Style.Lead, guard, aim, reach);
            }

            // 打ち出しの瞬間に一度に置いた目標（あご、または打ち抜く点）。狙いはワールドの点で保つ（体フレームは体と一緒に動く）。
            var outbound = Action.Duration * PunchSpec.OutboundShare;
            var back = MathF.Max(0f, (Action.Time - outbound) / (Action.Duration - outbound));
            var placed = path == PunchLabPath.Instant ? aim : PunchPath.Through(aim, _spec.Overshoot, reach);
            return Vector3.Lerp(placed, guard, back);
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
        var victim = game.Cpu;
        var attacker = game.Player;
        var bruise0 = victim.Voxels?.SkinBruiseTotal() ?? 0f;
        var concussion0 = victim.Concussion;
        var points = new List<(float, float)>();
        HitEvent? best = null;
        float bestEnergy = -1f, timeToHit = -1f, fistAtHit = 0f, peak = 0f, twist = 0f, concussion = 0f;
        var fell = false;
        var outbound = PunchBook.Default.Of(Technique.Cross).Duration * PunchSpec.OutboundShare;
        while (bout.PunchTime is { } t && t < WatchSeconds)
        {
            var fist = attacker.GloveVelocity(Hand.Right).Length();
            if (t >= 0f && t <= outbound + 0.05f)
            {
                peak = MathF.Max(peak, fist);
            }

            if (series && t >= -0.1f && t <= 0.4f)
            {
                points.Add((t, fist));
            }

            bout.Step();
            foreach (var hit in game.RecentHits.Where(h => h.Attacker == attacker && h.Hand == Hand.Right && !h.Kick && !h.Elbow))
            {
                if (t >= 0f && t <= StrikeWindow && hit.Energy > bestEnergy)
                {
                    best = hit;
                    bestEnergy = hit.Energy;
                    timeToHit = t;
                    fistAtHit = fist;
                    twist = victim.LastHeadTwist;
                }
            }

            concussion = MathF.Max(concussion, victim.Concussion - concussion0);
            fell |= victim.State != FighterState.Standing;
        }

        var bruise = (victim.Voxels?.SkinBruiseTotal() ?? 0f) - bruise0;
        return best is { } h
            ? new Trial(seed, distance, true, h.Zone, timeToHit, peak, fistAtHit, h.Speed, h.ImpactMass, h.Energy, h.Damage, twist, concussion, BoxingGame.HitStrength(h.Damage), bruise, fell, points)
            : new Trial(seed, distance, false, HitZone.Body, -1f, peak, 0f, 0f, 0f, 0f, 0f, 0f, concussion, 0f, bruise, fell, points);
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
        log("| 条件 | 間合い [腕] | 当たり / 本 | 当たった所 | 当たるまで [s] | 打ち出しの最高速 [m/s] | 当たった瞬間の拳 [m/s] | 当たる速さ（相対）[m/s] | 乗る質量 [kg] | エネルギー [J] | ダメージ | 頭の回転 [rad/s] | 脳しんとう | 光 | あざ | 倒れた |");
        log("|---|---|---|---|---|---|---|---|---|---|---|---|---|---|---|---|");
        foreach (var (setup, trials) in results)
        {
            var hits = trials.Where(t => t.Hit).ToList();
            float Mean(Func<Trial, float> f) => hits.Count > 0 ? hits.Average(f) : 0f;
            log($"| {setup.Label} | {F(trials.Average(t => t.Distance))} | {hits.Count} / {trials.Count} | {string.Join(" ", hits.GroupBy(h => h.Zone).Select(g => $"{g.Key} {g.Count()}"))} | {F(Mean(t => t.TimeToHit), "0.000")} | {F(trials.Average(t => t.Peak))} | {F(Mean(t => t.FistAtHit))} | {F(Mean(t => t.Closing))} | {F(Mean(t => t.Mass))} | {F(Mean(t => t.Energy))} | {F(Mean(t => t.Damage), "0.0")} | {F(Mean(t => t.HeadTwist), "0.0")} | {F(trials.Average(t => t.Concussion), "0.000")} | {F(Mean(t => t.Glow))} | {F(trials.Average(t => t.Bruise), "0.0")} | {trials.Count(t => t.Fell)} |");
        }

        log("\n拳の速さの時系列（最初の種、10 ms ごと、[m/s]。t = 0 が打ち出し、負は溜め）:\n");
        var first = results.Where(r => r.Trials.Count > 0).Select(r => (r.Setup, r.Trials[0])).ToList();
        log("| t [s] | " + string.Join(" | ", first.Select(f => f.Setup.Label)) + " |");
        log("|---|" + string.Concat(first.Select(_ => "---|")));
        for (var t = -0.1f; t <= 0.4001f; t += 0.01f)
        {
            var row = first.Select(f => f.Item2.Series.Count == 0 ? "" : F(f.Item2.Series.MinBy(p => MathF.Abs(p.T - t)).Speed));
            log($"| {F(t)} | {string.Join(" | ", row)} |");
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
        float slowFrom = -0.12f, float slowTo = 0.4f, float after = 1.6f)
    {
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
                Write(ffmpeg, frame, width, height, half, bouts.Select((b, i) => Render(renderer, half, height, b, [
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
                    if (b.PunchTime is >= 0f and <= 0.3f)
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
                    if (b.TargetWorld is { } target && b.PunchTime is { } pt && pt <= 0.4f)
                    {
                        lines.Add(new DebugLine(b.Game.Player.GloveCenter(Hand.Right), target, Gold));
                    }

                    return Render(renderer, half, height, b, [
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

    private static byte[] Render(HeadlessRenderer renderer, int width, int height, Bout bout, IReadOnlyList<ScreenTextDraw> text, float dim, IReadOnlyList<DebugLine>? extra = null)
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
        var middle = ((a + v) * 0.5f) + new Vector3(0f, -0.05f, 0f);
        var direction = Vector3.Normalize((v - a) with { Y = 0f });
        var side = Vector3.Cross(Vector3.UnitY, direction);
        var eye = middle - (side * 0.7f) + new Vector3(0f, 0.04f, 0f); // 打ち手の右手の側から真横に
        var vp = Camera.ViewProjection(eye, middle, Vector3.UnitY, 45f * MathF.PI / 180f, (float)width / height, 0.02f, 30f);
        UiShapeDraw[] shapes = dim > 0f ? [new UiShapeDraw(new Vector2(width * 0.5f, height * 0.5f), new Vector2(width, height), new Vector4(0f, 0f, 0f, dim))] : [];
        return renderer.RenderPrimitiveScene(ArenaLook.Background, vp, instances, lines, text, particles: game.BuildParticles(), lighting: game.BuildLighting(),
            shadow: game.BuildShadow(), cameraPosition: eye, shapes: shapes, postProcess: game.BuildPostProcess(), voxels: game.BuildVoxelVolumes());
    }
}
