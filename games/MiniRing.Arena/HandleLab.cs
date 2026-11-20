using System.Globalization;
using System.Numerics;
using System.Text;
using MiniRing.Bots;
using MiniRing.Characters;
using MiniRing.Weapons;

namespace MiniRing.Arena;

/// <summary>
/// 武器の扱いの物差し（handlelab、2026-11-16。docs/arena/weapon_handling_baseline.md）。名簿のキャラクターの試合を、ゲームと同じ道（UseWeaponBots・
/// <see cref="WeaponStyle"/>）で回し、振りの質・守り・自傷・勝敗を数える。
/// <list type="bullet">
/// <item><b>振り</b>: 剣士（<see cref="SwordsmanBot.Current"/>）・こんぼう（<see cref="ClubBot.Current"/>）が技を出している間を 1 振りとし、その間（と余韻 <see cref="Tail"/> 秒）の
/// 握った武器の当たり（<see cref="BoxingGame.WeaponHits"/>）を 1 振りに数える。当たった振りの割合、当たった時の刃筋（刃の動きの向きが、刃の面内
/// （長さ方向 × 刃の幅）からどれだけ外れているか。0° = 刃で切る向き、90° = 平らな面で叩く向き）、刃先の速さ、当たった所の刃の位置（0 = 握り、1 = 切っ先）、
/// 壊したセル、切り落とし・折れ。刀は、刃（−Y の縁）が先に当たったか、峰（+Y）が先かも。</item>
/// <item><b>守り</b>: 受けた回数・足と頭で外した回数（<see cref="SwordsmanBot.Blocks"/>・<see cref="SwordsmanBot.Evasions"/>）と、相手の振りのうち当たった割合。</item>
/// <item><b>自傷</b>: 握った武器が自分の体のセルを削った連続した接触の数（<see cref="BoxingGame.SelfTouches"/>）。</item>
/// <item><b>試合</b>: 勝ち・K.O.・与えた/受けた打撃の得点・失ったセル。</item>
/// </list>
/// 値の上書き: 武器ごとの <see cref="WeaponStyle"/> を渡すと（<c>--style-katana</c> など）、その武器を握る全員がその値で戦う（候補の教科書を測る）。
/// </summary>
internal static class HandleLab
{
    private const float Dt = 1f / 60f;

    /// <summary>技を出し終えてから、まだ 1 振りの当たりに数える余韻 [s]（振り切りの最後に当たる・切り落としが 1 歩遅れる）。</summary>
    public const float Tail = 0.252f;

    /// <summary>1 振り。</summary>
    internal sealed class Swing
    {
        public WeaponKind Weapon;
        public string Technique = "";
        public float Start;
        public float Distance;       // 始めの胸と胸の水平の距離 [m]
        public float Range;          // 技の間合い [m]（こんぼうは振り始めの間合い）
        public bool Hit;
        public float HitSpeed;       // 最初の当たりの刃先の速さ [m/s]
        public float OffPlane = float.NaN;  // 刃筋のずれ [度]
        public float BladeFraction = float.NaN;
        public int EdgeFirst = -1;   // 刀: 刃が先 1・峰が先 0（それ以外 -1）
        public int RearEnd = -1;     // 棒: 後ろの端（武器のローカル −X 側）で当たった 1・前の端（+X 側）で当たった 0（それ以外 -1）
        public int Cells;
        public float Damage;
        public int Severs;
        public int Fractures;
        public int SelfCells;        // この振りの間に自分の体を削った武器のセル数
        public int SelfCuts;         // 同じく、削った連続した接触の数
    }

    /// <summary>自傷 1 回（連続した接触）。</summary>
    internal sealed record SelfEpisode(WeaponKind Weapon, string Phase, string Technique, BodyPartKind Part, int Cells);

    internal sealed class Side
    {
        public required string Id;
        public required string Opponent;
        public required string OpponentClass;
        public WeaponKind? Weapon;
        public List<Swing> Swings = [];
        public int OutsideHits;       // 技を出していない間の当たり（受け流し・構え・倒れた相手への打ち下ろし）
        public int Blocks, Evasions;
        public int HitsTaken;
        public int SelfCuts, SelfCells;
        public List<SelfEpisode> SelfEpisodes = [];
        public float DamageDealt, DamageTaken;
        public int CellsLost;
        public int Result;            // 1 勝ち・0 分け・-1 負け
        public bool Died, OpponentDied;
        public int Knockdowns;
        public int OpponentSwings;    // 相手の振りの数（食らった割合の分母）
        public int OpponentHitSwings; // 相手の振りのうち、当たった振りの数
    }

    internal sealed record Bout(Side Red, Side Blue, double Seconds);

    /// <summary>武器ごとの値の上書き（null の武器は教科書または教育のまま）。</summary>
    internal sealed record Overrides(WeaponStyle? Katana = null, WeaponStyle? Longsword = null, WeaponStyle? Club = null, string? OnlyFor = null, WeaponStyle? Staff = null, WeaponStyle? SwordShield = null, WeaponStyle? Bat = null)
    {
        public static Overrides None { get; } = new();

        /// <summary>このキャラが握る武器の上書き（<see cref="OnlyFor"/> があれば、そのキャラだけ）。</summary>
        public WeaponStyle? For(FighterCharacter character) => OnlyFor is not null && OnlyFor != character.Id ? null : For(character.Weapon);

        public WeaponStyle? For(WeaponKind? weapon) => weapon switch
        {
            WeaponKind.Katana => Katana,
            WeaponKind.Longsword => Longsword,
            WeaponKind.WoodenClub => Club,
            WeaponKind.Staff or WeaponKind.Jo => Staff,
            WeaponKind.IronSword => SwordShield,
            WeaponKind.Bat => Bat,
            _ => null,
        };
    }

    private sealed class Tracker(Fighter Fighter, Side Side, WeaponKind? Weapon)
    {
        private Swing? _open;
        private float _closeAt = float.PositiveInfinity;
        private object? _bot;
        private int _used;
        private bool _wasActive;
        private int _lastBlocks, _lastEvasions;

        /// <summary>ラウンドごとに作り直したボットを合わせた、受け・外しの回数。</summary>
        public int Blocks { get; private set; }

        public int Evasions { get; private set; }

        public Fighter Fighter { get; set; } = Fighter;

        public Side Side { get; } = Side;

        public Swing? Open => _open;

        /// <summary>この歩のボットの状態を見て、振りの始まりと終わりを決める。</summary>
        public void Observe(IFighterControl? control, float t, float distance)
        {
            var bot = Unwrap(control);
            string? name = null;
            float range = 0f;
            var active = false;
            var count = 0;
            switch (bot)
            {
                case SwordsmanBot sword:
                    active = sword.Current is not null;
                    name = sword.Current?.Name;
                    range = sword.Current?.Range ?? 0f;
                    count = sword.Used.Count;
                    break;
                case ClubBot club:
                    active = club.Current is not null;
                    name = club.Current?.Family.ToString();
                    range = club.Range;
                    break;
            }

            var sameBot = ReferenceEquals(bot, _bot);
            if (!sameBot)
            {
                Blocks += _lastBlocks;
                Evasions += _lastEvasions;
                (_lastBlocks, _lastEvasions) = (0, 0);
            }

            if (bot is SwordsmanBot counted)
            {
                (_lastBlocks, _lastEvasions) = (counted.Blocks, counted.Evasions);
            }

            var started = active && (!_wasActive || !sameBot || (bot is SwordsmanBot && count != _used));
            if (started)
            {
                Finish();
                _open = new Swing { Weapon = Weapon ?? WeaponKind.IronSword, Technique = name ?? "?", Start = t, Distance = distance, Range = range };
                _closeAt = float.PositiveInfinity;
            }
            else if (!active && _wasActive && sameBot && _open is not null)
            {
                _closeAt = t + Tail;
            }

            if (_open is not null && t >= _closeAt)
            {
                Finish();
            }

            _wasActive = active;
            _bot = bot;
            _used = count;
        }

        public (int Blocks, int Evasions) Totals() => (Blocks + _lastBlocks, Evasions + _lastEvasions);

        public void Finish()
        {
            if (_open is not null)
            {
                Side.Swings.Add(_open);
                _open = null;
                _closeAt = float.PositiveInfinity;
            }
        }
    }

    private static IFighterControl? Unwrap(IFighterControl? control)
    {
        while (true)
        {
            switch (control)
            {
                case GroundStriker g:
                    control = g.Inner;
                    break;
                case DisarmedFallback d:
                    return d.Disarmed ? null : d.Armed;
                default:
                    return control;
            }
        }
    }

    private static float Flat(Vector3 v) => MathF.Sqrt((v.X * v.X) + (v.Z * v.Z));

    internal static string ClassOf(FighterCharacter me, FighterCharacter other) =>
        other.Weapon is null ? "拳" : other.Weapon == me.Weapon ? "同じ武器" : "別の武器";

    /// <summary>1 試合。<paramref name="red"/> が赤（プレイヤー側）、<paramref name="blue"/> が青（CPU 側）。</summary>
    public static Bout RunBout(FighterCharacter red, FighterCharacter blue, int seed, float seconds, bool referee, Overrides overrides)
    {
        var watch = System.Diagnostics.Stopwatch.StartNew();
        var redContender = RosterLeague.ContenderOf(red, blue.Id, null);
        var blueContender = RosterLeague.ContenderOf(blue, red.Id, null);
        var corners = VideoMaker.Corners(redContender, blueContender, seed, red.Wear, blue.Wear, red.Weapon, blue.Weapon,
            s => blueContender.Create(s), s => redContender.Create(s), referee);
        corners = VideoMaker.WithCharacters(corners, red, blue);
        corners = corners with
        {
            PlayerWeaponStyle = overrides.For(red) ?? corners.PlayerWeaponStyle,
            CpuWeaponStyle = overrides.For(blue) ?? corners.CpuWeaponStyle,
        };
        using var game = new BoxingGame(seed: seed, corners: corners);
        var redSide = new Side { Id = red.Id, Opponent = blue.Id, OpponentClass = ClassOf(red, blue), Weapon = red.Weapon };
        var blueSide = new Side { Id = blue.Id, Opponent = red.Id, OpponentClass = ClassOf(blue, red), Weapon = blue.Weapon };
        var redTrack = new Tracker(game.Player, redSide, red.Weapon);
        var blueTrack = new Tracker(game.Cpu, blueSide, blue.Weapon);
        var redStart = game.Player.Voxels!.Parts.Sum(p => p.Body.Grid.FilledCount);
        var blueStart = game.Cpu.Voxels!.Parts.Sum(p => p.Body.Grid.FilledCount);
        var round = game.Round;
        var redDown = false;
        var blueDown = false;
        var lastSelf = new Dictionary<Fighter, (float At, BodyPartKind Part)>();
        var steps = (int)MathF.Ceiling(seconds / Dt);
        for (var i = 0; i < steps; i++)
        {
            var t = i * Dt;
            var before = new Dictionary<Fighter, Aigis.Physics.BodyState>();
            foreach (var f in new[] { game.Player, game.Cpu })
            {
                if (game.WeaponOf(f) is { } w)
                {
                    before[f] = game.World.GetState(w.Body.BodyId);
                }
            }

            game.Step(default, Dt);
            if (game.Round != round)
            {
                round = game.Round;
                redDown = blueDown = false;
                redTrack.Fighter = game.Player;
                blueTrack.Fighter = game.Cpu;
            }

            var distance = Flat(game.Player.ChestPosition - game.Cpu.ChestPosition);
            redTrack.Observe(game.PlayerControl, t, distance);
            blueTrack.Observe(game.CpuControl, t, distance);

            foreach (var hit in game.RecentHits)
            {
                var (mine, theirs) = hit.Attacker == game.Player ? (redSide, blueSide) : (blueSide, redSide);
                mine.DamageDealt += hit.Damage;
                theirs.DamageTaken += hit.Damage;
                theirs.HitsTaken++;
            }

            foreach (var hit in game.WeaponHits)
            {
                var attackerIsRed = hit.Attacker == game.Player;
                var (mine, theirs, track) = attackerIsRed ? (redSide, blueSide, redTrack) : (blueSide, redSide, blueTrack);
                mine.DamageDealt += hit.Damage;
                theirs.DamageTaken += hit.Damage;
                theirs.HitsTaken++;
                if (track.Open is not { } swing)
                {
                    mine.OutsideHits++;
                    continue;
                }

                swing.Cells += hit.Cells;
                swing.Damage += hit.Damage;
                if (!swing.Hit)
                {
                    swing.Hit = true;
                    swing.HitSpeed = hit.Speed;
                    if (before.TryGetValue(hit.Attacker, out var state) && game.WeaponOf(hit.Attacker) is { } weapon)
                    {
                        Edge(swing, weapon.Kind, state, hit.Point);
                    }
                }
            }

            foreach (var sever in game.RecentSevers)
            {
                var track = sever.Victim == game.Cpu ? redTrack : sever.Victim == game.Player ? blueTrack : null;
                if (track?.Open is { } swing)
                {
                    swing.Severs++;
                }
            }

            foreach (var broken in game.BrokenBones)
            {
                var track = broken.Victim == game.Cpu ? redTrack : broken.Victim == game.Player ? blueTrack : null;
                if (track?.Open is { } swing)
                {
                    swing.Fractures++;
                }
            }

            foreach (var touch in game.SelfTouches)
            {
                if (touch.Removed <= 0 || (touch.Fighter != game.Player && touch.Fighter != game.Cpu))
                {
                    continue;
                }

                var side = touch.Fighter == game.Player ? redSide : blueSide;
                var swingNow = (touch.Fighter == game.Player ? redTrack : blueTrack).Open;
                side.SelfCells += touch.Removed;
                if (swingNow is not null)
                {
                    swingNow.SelfCells += touch.Removed;
                }

                if (!lastSelf.TryGetValue(touch.Fighter, out var last) || t - last.At > 2.5f * Dt || last.Part != touch.Part)
                {
                    side.SelfCuts++;
                    if (swingNow is not null)
                    {
                        swingNow.SelfCuts++;
                    }

                    side.SelfEpisodes.Add(new SelfEpisode(side.Weapon ?? WeaponKind.IronSword, swingNow is null ? "技の外（構え・守り・歩き）" : "技の最中", swingNow?.Technique ?? "-", touch.Part, 0));
                }

                if (side.SelfEpisodes.Count > 0)
                {
                    var lastEpisode = side.SelfEpisodes[^1];
                    side.SelfEpisodes[^1] = lastEpisode with { Cells = lastEpisode.Cells + touch.Removed };
                }

                lastSelf[touch.Fighter] = (t, touch.Part);
            }

            var blueNow = game.Cpu.IsKnockedDown;
            var redNow = game.Player.IsKnockedDown;
            redSide.Knockdowns += !blueDown && blueNow ? 1 : 0;
            blueSide.Knockdowns += !redDown && redNow ? 1 : 0;
            redDown = redNow;
            blueDown = blueNow;
            if (game.Player.IsDead || game.Cpu.IsDead)
            {
                break;
            }
        }

        redTrack.Finish();
        blueTrack.Finish();
        redSide.CellsLost = redStart - game.Player.Voxels!.Parts.Sum(p => p.Body.Grid.FilledCount);
        blueSide.CellsLost = blueStart - game.Cpu.Voxels!.Parts.Sum(p => p.Body.Grid.FilledCount);
        redSide.Died = game.Player.IsDead;
        blueSide.Died = game.Cpu.IsDead;
        (redSide.Blocks, redSide.Evasions) = redTrack.Totals();
        (blueSide.Blocks, blueSide.Evasions) = blueTrack.Totals();
        redSide.OpponentDied = blueSide.Died;
        blueSide.OpponentDied = redSide.Died;
        redSide.OpponentSwings = blueSide.Swings.Count;
        blueSide.OpponentSwings = redSide.Swings.Count;
        redSide.OpponentHitSwings = blueSide.Swings.Count(s => s.Hit);
        blueSide.OpponentHitSwings = redSide.Swings.Count(s => s.Hit);
        var winner = game.PlayerWins == game.CpuWins ? 0 : game.PlayerWins > game.CpuWins ? 1 : -1;
        // 死んだ側が負け（ラウンドの勝ち数が並んでいても）。
        if (winner == 0 && (redSide.Died != blueSide.Died))
        {
            winner = redSide.Died ? -1 : 1;
        }

        redSide.Result = winner;
        blueSide.Result = -winner;
        return new Bout(redSide, blueSide, watch.Elapsed.TotalSeconds);
    }

    /// <summary>当たった瞬間の刃筋・刃の位置。<paramref name="state"/> は打つ前の歩の武器の状態。</summary>
    private static void Edge(Swing swing, WeaponKind kind, Aigis.Physics.BodyState state, Vector3 point)
    {
        var v = state.LinearVelocity + Vector3.Cross(state.AngularVelocity, point - state.Position);
        if (v.Length() < 0.0839f)
        {
            return;
        }

        var inverse = Quaternion.Inverse(state.Orientation);
        var local = Vector3.Transform(v, inverse);
        // 刃（刀・ロングソード）は、長さ X・幅 Y・厚さ Z。面内（X-Y）を動けば切る向き、Z へ動けば平らな面で叩く向き。
        swing.OffPlane = MathF.Asin(Math.Clamp(MathF.Abs(local.Z) / local.Length(), 0f, 1f)) * 180f / MathF.PI;
        if (kind == WeaponKind.Katana && MathF.Abs(local.Y) > MathF.Abs(local.X))
        {
            swing.EdgeFirst = local.Y < 0f ? 1 : 0; // 刃は −Y の縁（Weapon.Katana）
        }

        var at = Vector3.Transform(point - state.Position, inverse);
        var grip = Weapon.GripOf(kind).X;
        var tip = Weapon.TipOf(kind).X;
        swing.BladeFraction = Math.Clamp((at.X - grip) / MathF.Max(1e-4f, tip - grip), -0.5f, 1.5f);
        if (kind == WeaponKind.Bat)
        {
            swing.RearEnd = at.X < grip ? 1 : 0; // バットは握りの端（グリップエンド）も打つ: 上の手の握りより後ろなら後ろの端
        }
        else if (Weapon.IsRod(kind))
        {
            // 棒は両端が打つ: 当たった点が棒の真ん中（握りの間）より −X 側なら後ろの端。刃の位置は、当たった端の先からの近さ（0 = 握り・1 = 端）にする。
            swing.RearEnd = at.X < 0f ? 1 : 0;
            swing.BladeFraction = Math.Clamp((MathF.Abs(at.X) - (0.5f * Weapon.StaffHandSpacing)) / MathF.Max(1e-4f, tip - (0.5f * Weapon.StaffHandSpacing)), -0.5f, 1.5f);
        }
    }

    // ---- 集計 ----

    internal sealed record Matchup(string A, string B);

    /// <summary>武器 3 種 × {同じ・別・拳}。拳の相手は <paramref name="fist"/>。</summary>
    public static IReadOnlyList<Matchup> DefaultMatchups(string fist = "marcus") =>
    [
        new("jin", "jin"), new("konrad", "konrad"), new("sean", "sean"),
        new("jin", "konrad"), new("jin", "sean"), new("konrad", "sean"),
        new("jin", fist), new("konrad", fist), new("sean", fist),
    ];

    /// <summary>名簿の id、または "bat"（名簿の jin の体と教科書で、日本刀の代わりにバットを両手で握る試験用のキャラ。名簿には足さない）。</summary>
    internal static FighterCharacter Resolve(string id) => id == "bat"
        ? CharacterRoster.Get("jin") with { Id = "bat", Name = "バット", HudName = "バット", Weapon = WeaponKind.Bat }
        : CharacterRoster.Get(id);

    public static IReadOnlyList<Bout> Run(IReadOnlyList<Matchup> matchups, int bouts, float seconds, int seed, bool referee, int parallel, Overrides overrides, TextWriter progress)
    {
        var jobs = new List<(FighterCharacter Red, FighterCharacter Blue, int Seed)>();
        for (var m = 0; m < matchups.Count; m++)
        {
            var a = Resolve(matchups[m].A);
            var b = Resolve(matchups[m].B);
            for (var i = 0; i < bouts; i++)
            {
                var s = seed + (1000 * m) + (i / 2);
                jobs.Add(i % 2 == 0 ? (a, b, s) : (b, a, s));
            }
        }

        var results = new Bout[jobs.Count];
        var done = 0;
        var watch = System.Diagnostics.Stopwatch.StartNew();
        Parallel.For(0, jobs.Count, new ParallelOptions { MaxDegreeOfParallelism = parallel }, i =>
        {
            results[i] = RunBout(jobs[i].Red, jobs[i].Blue, jobs[i].Seed, seconds, referee, overrides);
            var n = Interlocked.Increment(ref done);
            lock (progress)
            {
                progress.WriteLine(string.Create(CultureInfo.InvariantCulture, $"[{n}/{jobs.Count}] {jobs[i].Red.Id} vs {jobs[i].Blue.Id} seed {jobs[i].Seed}: {results[i].Seconds:0.0}s (total {watch.Elapsed.TotalSeconds:0}s)"));
            }
        });
        return results;
    }

    private static string Label(WeaponKind? weapon) => weapon switch
    {
        WeaponKind.Katana => "日本刀",
        WeaponKind.Longsword => "ロングソード",
        WeaponKind.WoodenClub => "こんぼう",
        WeaponKind.Staff => "棒",
        WeaponKind.Jo => "杖",
        WeaponKind.Bat => "バット",
        WeaponKind.IronSword => "剣盾",
        _ => "拳",
    };

    private static string F(double v, string format = "0.0") => double.IsNaN(v) ? "-" : v.ToString(format, CultureInfo.InvariantCulture);

    private static double Mean(IEnumerable<double> values) => values.Where(v => !double.IsNaN(v)).DefaultIfEmpty(double.NaN).Average();

    /// <summary>武器を握る側の組（武器 × 相手の種類）ごとの表。</summary>
    public static string Report(IReadOnlyList<Bout> bouts, float seconds, int seed, string heading)
    {
        var sides = bouts.SelectMany(b => new[] { b.Red, b.Blue }).Where(s => s.Weapon is WeaponKind.Katana or WeaponKind.Longsword or WeaponKind.WoodenClub or WeaponKind.Staff or WeaponKind.Jo or WeaponKind.IronSword or WeaponKind.Bat).ToList();
        var groups = sides.GroupBy(s => (s.Weapon, s.OpponentClass)).OrderBy(g => g.Key.Weapon).ThenBy(g => g.Key.OpponentClass).ToList();
        var sb = new StringBuilder();
        sb.AppendLine(string.Create(CultureInfo.InvariantCulture, $"### {heading}（{bouts.Count} 試合 × {seconds:0} 秒、種 {seed}、LogicVersion {BoxerBot.LogicVersion}）"));
        sb.AppendLine();
        sb.AppendLine("#### 要約（武器ごと、相手を全部合わせて）");
        sb.AppendLine();
        sb.AppendLine("| 武器 | 試合（握る側） | 振り | 当たった振り | 1 振りで相手から欠けたセル | 自傷のセル / 振り | 自傷のセル / 試合 | セル差 / 試合（相手が失った − 自分が失った） | 勝-分-負 | 与えた / 受けた得点（1 試合） |");
        sb.AppendLine("|---|---:|---:|---:|---:|---:|---:|---:|---|---|");
        foreach (var g in sides.GroupBy(s => s.Weapon).OrderBy(g => g.Key))
        {
            var swings = g.SelectMany(s => s.Swings).ToList();
            var n = g.Count();
            var cellDiff = bouts.SelectMany(b => new[] { (b.Red, b.Blue), (b.Blue, b.Red) }).Where(p => p.Item1.Weapon == g.Key).Average(p => (double)(p.Item2.CellsLost - p.Item1.CellsLost));
            sb.AppendLine(string.Create(CultureInfo.InvariantCulture,
                $"| {Label(g.Key)} | {n} | {swings.Count} | {F(100.0 * swings.Count(s => s.Hit) / Math.Max(1, swings.Count), "0")}% | {F(swings.Average(s => s.Cells))} | {F(g.Sum(s => s.SelfCells) / (double)Math.Max(1, swings.Count))} | {g.Average(s => s.SelfCells):0} | {cellDiff:0} | {g.Count(s => s.Result > 0)}-{g.Count(s => s.Result == 0)}-{g.Count(s => s.Result < 0)} | {g.Average(s => s.DamageDealt):0} / {g.Average(s => s.DamageTaken):0} |"));
        }

        sb.AppendLine();
        sb.AppendLine("#### 振りの質");
        sb.AppendLine();
        sb.AppendLine("| 武器 | 相手 | 振り | 当たった振り | 刃筋のずれ [°]（当たった時） | 峰が先（刀） | 当たった刃の位置（0 握り・1 切っ先） | 刃先の速さ [m/s]（当たった時） | 1 振りで欠けたセル | 切断 / 折れ（100 振りあたり） | 外れた振りの間合いのずれ [cm] | 当たった振りの間合いのずれ [cm] | 後ろの端で当たった（棒） |");
        sb.AppendLine("|---|---|---:|---:|---:|---:|---:|---:|---:|---:|---:|---:|---:|");
        foreach (var g in groups)
        {
            var swings = g.SelectMany(s => s.Swings).ToList();
            var hits = swings.Where(s => s.Hit).ToList();
            var misses = swings.Where(s => !s.Hit).ToList();
            var edged = hits.Where(s => s.EdgeFirst >= 0).ToList();
            sb.AppendLine(string.Create(CultureInfo.InvariantCulture,
                $"| {Label(g.Key.Weapon)} | {g.Key.OpponentClass} | {swings.Count} | {F(swings.Count == 0 ? double.NaN : 100.0 * hits.Count / swings.Count, "0")}% | {F(Mean(hits.Select(s => (double)s.OffPlane)))} | {(edged.Count == 0 ? "-" : F(100.0 * edged.Count(s => s.EdgeFirst == 0) / edged.Count, "0") + "%")} | {F(Mean(hits.Select(s => (double)s.BladeFraction)), "0.00")} | {F(Mean(hits.Select(s => (double)s.HitSpeed)))} | {F(swings.Count == 0 ? double.NaN : swings.Average(s => s.Cells))} | {F(swings.Count == 0 ? double.NaN : 100.0 * swings.Sum(s => s.Severs + s.Fractures) / swings.Count, "0.0")} | {F(Mean(misses.Select(s => 100.0 * (s.Distance - s.Range))), "0")} | {F(Mean(hits.Select(s => 100.0 * (s.Distance - s.Range))), "0")} | {(hits.Any(s => s.RearEnd >= 0) ? F(100.0 * hits.Count(s => s.RearEnd == 1) / hits.Count(s => s.RearEnd >= 0), "0") + "%" : "-")} |"));
        }

        sb.AppendLine();
        sb.AppendLine("振りの数・当たりは、その武器を握る全員（両コーナー）の合計。刃筋のずれは「当たる直前の刃の動く向き」が刃の面内から外れた角（0° = 刃で切る向き）。");
        sb.AppendLine();
        sb.AppendLine("#### 技ごと（当たった振りの割合・1 振りで欠けたセル）");
        sb.AppendLine();
        sb.AppendLine("| 武器 | 技 | 振り | 当たった | 欠けたセル/振り | 刃筋のずれ [°] |");
        sb.AppendLine("|---|---|---:|---:|---:|---:|");
        foreach (var g in sides.SelectMany(s => s.Swings).GroupBy(s => (s.Weapon, s.Technique)).OrderBy(g => g.Key.Weapon).ThenBy(g => g.Key.Technique, StringComparer.Ordinal))
        {
            var hits = g.Where(s => s.Hit).ToList();
            sb.AppendLine(string.Create(CultureInfo.InvariantCulture,
                $"| {Label(g.Key.Weapon)} | {g.Key.Technique} | {g.Count()} | {F(100.0 * hits.Count / g.Count(), "0")}% | {F(g.Average(s => s.Cells))} | {F(Mean(hits.Select(s => (double)s.OffPlane)))} |"));
        }

        sb.AppendLine();
        sb.AppendLine("#### 自傷（自分の武器が自分の体を削った連続した接触。技の最中は、その技ごとの 1 振りあたり）");
        sb.AppendLine();
        sb.AppendLine("| 武器 | 技 | 振り | 技の最中に削った回数 | 削ったセル | 1 振りあたりのセル |");
        sb.AppendLine("|---|---|---:|---:|---:|---:|");
        foreach (var g in sides.SelectMany(s => s.Swings).GroupBy(s => (s.Weapon, s.Technique)).OrderBy(g => g.Key.Weapon).ThenBy(g => g.Key.Technique, StringComparer.Ordinal))
        {
            sb.AppendLine(string.Create(CultureInfo.InvariantCulture, $"| {Label(g.Key.Weapon)} | {g.Key.Technique} | {g.Count()} | {g.Sum(s => s.SelfCuts)} | {g.Sum(s => s.SelfCells)} | {g.Average(s => s.SelfCells):0.0} |"));
        }

        sb.AppendLine();
        sb.AppendLine("| 武器 | 時 | 部位 | 回数 | セル |");
        sb.AppendLine("|---|---|---|---:|---:|");
        foreach (var g in sides.SelectMany(s => s.SelfEpisodes).GroupBy(e => (e.Weapon, e.Phase, e.Part)).OrderBy(g => g.Key.Weapon).ThenBy(g => g.Key.Phase, StringComparer.Ordinal).ThenByDescending(g => g.Sum(e => e.Cells)))
        {
            sb.AppendLine(string.Create(CultureInfo.InvariantCulture, $"| {Label(g.Key.Weapon)} | {g.Key.Phase} | {g.Key.Part} | {g.Count()} | {g.Sum(e => e.Cells)} |"));
        }

        sb.AppendLine();
        sb.AppendLine("#### 守り・自傷・試合（1 試合あたり）");
        sb.AppendLine();
        sb.AppendLine("| 武器 | 相手 | 試合 | 勝-分-負 | K.O.で勝ち | 与えた得点 | 受けた得点 | 失ったセル | 受け | 外し | 食らった打撃（回） | 相手の振りのうち当たった（振り単位） | 自傷（削った回数 / セル） |");
        sb.AppendLine("|---|---|---:|---|---:|---:|---:|---:|---:|---:|---:|---:|---|");
        foreach (var g in groups)
        {
            var n = g.Count();
            var oppSwings = g.Sum(s => s.OpponentSwings);
            var oppHits = g.Sum(s => s.OpponentHitSwings);
            sb.AppendLine(string.Create(CultureInfo.InvariantCulture,
                $"| {Label(g.Key.Weapon)} | {g.Key.OpponentClass} | {n} | {g.Count(s => s.Result > 0)}-{g.Count(s => s.Result == 0)}-{g.Count(s => s.Result < 0)} | {g.Count(s => s.Result > 0 && s.OpponentDied)} | {g.Average(s => s.DamageDealt):0} | {g.Average(s => s.DamageTaken):0} | {g.Average(s => s.CellsLost):0} | {g.Average(s => s.Blocks):0.0} | {g.Average(s => s.Evasions):0.0} | {g.Average(s => s.HitsTaken):0.0} | {F(oppSwings == 0 ? double.NaN : 100.0 * oppHits / oppSwings, "0")}% | {g.Average(s => s.SelfCuts):0.0} / {g.Average(s => s.SelfCells):0} |"));
        }

        return sb.ToString();
    }

    /// <summary>
    /// 候補の値の点: 勝ち − 負け + 0.45 tanh((与えた − 受けた) / 400) + 0.45 tanh(1 試合あたりの（相手が失ったセル − 自分が失ったセル）/ 300)。
    /// 前の項は <see cref="MatchTrainer{T}"/> と同じ物差し（打撃の得点）、後ろの項は材料で見た差し引き（自分の武器で自分を削った分も、相手に斬られた分も数える。§1.4）。
    /// <paramref name="weapon"/> を握る側だけ数える。
    /// </summary>
    public static (float Value, int Wins, int Losses, int Draws, float Dealt, float Taken, float CellDiff) Score(IReadOnlyList<Bout> bouts, WeaponKind weapon)
    {
        int wins = 0, losses = 0, draws = 0, n = 0;
        float dealt = 0f, taken = 0f, cells = 0f;
        foreach (var bout in bouts)
        {
            foreach (var (mine, other) in new[] { (bout.Red, bout.Blue), (bout.Blue, bout.Red) })
            {
                if (mine.Weapon != weapon)
                {
                    continue;
                }

                n++;
                wins += mine.Result > 0 ? 1 : 0;
                losses += mine.Result < 0 ? 1 : 0;
                draws += mine.Result == 0 ? 1 : 0;
                dealt += mine.DamageDealt;
                taken += mine.DamageTaken;
                cells += other.CellsLost - mine.CellsLost;
            }
        }

        var perBout = n == 0 ? 0f : cells / n;
        return (wins - losses + (0.45f * MathF.Tanh((dealt - taken) / 400f)) + (0.45f * MathF.Tanh(perBout / 300f)), wins, losses, draws, dealt, taken, perBout);
    }
}
