using System.Diagnostics;
using System.Globalization;
using System.Numerics;
using Aigis.Render;
using MiniRing.Audio;
using MiniRing.Characters;
using MiniRing.Replay;

namespace MiniRing.Arena;

/// <summary>
/// VFC のバトルロイヤル（docs/plans/VFC_ROYALE_PLAN.md）: 名簿の VFC の選手 N 人（既定 6 人）を六角形の檻に入れ、最後の 1 人まで（か時間切れまで）回す。
/// 下見（脱落の順・狙いの偏り・動けない時間・計算の重さ）、記録（<c>--replay</c>）、記録の再現（<c>--verify</c>）、動画（<c>--out *.mp4</c>）。
/// </summary>
internal static class RoyaleCommand
{
    /// <summary>既定の 6 人: 武器 4 人（刀・ロングソード・棍棒・剣と盾）と、はめる物 2 人（メリケンサック・カタール）。</summary>
    public static readonly string[] DefaultFighters = ["jin", "konrad", "sean", "edgar", "joao", "vikram"];

    private const float Dt = 1f / 60f;

    public static int Run(Options o)
    {
        if (o.Get("verify") is { } verifyPath)
        {
            var verification = ReplayPlayer.Verify(ReplayFile.Read(verifyPath));
            Console.WriteLine(string.Create(CultureInfo.InvariantCulture,
                $"verify {verifyPath}: steps {verification.StepsPlayed}, checksums matched {verification.ChecksumsMatched}, {(verification.Reproduced ? "REPRODUCED" : $"DIVERGED at step {verification.FirstDivergence}")}"));
            return verification.Reproduced ? 0 : 1;
        }

        var ids = o.List("fighters", DefaultFighters);
        var count = o.Int("count", ids.Count, min: 2); // 2 なら追加の席の無い試合（計算の重さの比べ）
        if (count > ids.Count)
        {
            throw new ArgumentException($"--count {count} は --fighters の人数（{ids.Count}）以下にしてください。");
        }

        var characters = ids.Take(count).Select(CharacterRoster.Get).ToList();
        var seed = o.Int("seed", 1);
        var seconds = o.Float("seconds", 300f, min: 1f);
        var org = Organizations.Get("vfc");
        var rules = TvShowRules.Rules(o.Int("rounds", 5, min: 1), o.Float("break", 15f, min: 0f), org) with { IntroSeconds = 1.5f, EndSeconds = 0f };
        // 同じキャラを何人も出すときは、画面の名前に A・B… を付けて見分ける。
        var seats = characters.Select(RoyaleEntrant.From).ToList();
        seats = [.. seats.Select((s, i) => characters.Count(c => c.Id == characters[i].Id) > 1
            ? s with { Label = $"{s.Label} {(char)('A' + characters.Take(i).Count(c => c.Id == characters[i].Id))}" }
            : s)];
        // チーム（--teams 0,0,1,1,1,1: 席ごとのチームの番号）。味方は狙わない。無ければ全員が敵。
        IReadOnlyList<int>? teams = o.Get("teams") is { } teamText
            ? [.. teamText.Split(',').Select(t => int.Parse(t, CultureInfo.InvariantCulture))]
            : null;
        var corners = RoyaleCorners.From(seats) with { SettleSeconds = 0.5f, BreakActions = false, Teams = teams };
        var teamNames = o.List("team-names", []);
        using var game = new BoxingGame(FighterTuning.Default, seed, corners, rules, org.ArenaMood);
        ReplayRecorder? recorder = null;
        Stream? replayStream = null;
        if (o.Get("replay") is { } replayPath)
        {
            replayStream = File.Create(replayPath);
            recorder = new ReplayRecorder(replayStream, game, "arena-royale");
        }

        try
        {
            var watch = new RoyaleWatch(game);
            var tvCorners = characters.Select(TvCorner.Of).ToList();
            // 実況・解説の資料（--events、または動画の横に <名前>.events.json と .events.md）。
            var eventsPath = o.Get("events") ?? (o.Get("out") is { } video ? Path.ChangeExtension(video, ".events.json") : null);
            var log = eventsPath is null ? null : new RoyaleLog(game, characters, tvCorners);
            var trace = o.Float("trace", 0f, min: 0f); // 何秒ごとに全員の様子を 1 行で出すか（0 なら出さない）
            Func<BoxingGame, float, bool> step = (g, t) =>
            {
                if (recorder is not null)
                {
                    recorder.Step(g, default, Dt);
                }
                else
                {
                    g.Step(default, Dt);
                }

                watch.Observe(Dt);
                log?.Observe(t + Dt);
                if (trace > 0f && (int)((t + Dt) / trace) != (int)(t / trace))
                {
                    Console.WriteLine(RoyaleWatch.Trace(g, t + Dt));
                }

                return !g.MatchOver;
            };

            var clock = Stopwatch.StartNew();
            if (o.Get("out") is { } outPath)
            {
                new RoyaleVideo(o.Int("width", 1280), o.Int("height", 720), o.Int("fps", 30), tvCorners, teamNames).Make(game, seconds, step, outPath, Console.Out);
            }
            else
            {
                for (var t = 0f; t < seconds && step(game, t); t += Dt)
                {
                }
            }

            clock.Stop();
            if (log is not null && eventsPath is not null)
            {
                log.Write(eventsPath);
                Console.WriteLine($"events: {eventsPath}");
            }

            Console.Write(watch.Report(clock.Elapsed.TotalSeconds));
        }
        finally
        {
            recorder?.Dispose();
            replayStream?.Dispose();
        }

        return 0;
    }
}

/// <summary>バトルロイヤルの下見の物差し（読むだけ: 試合には響かない）。</summary>
internal sealed class RoyaleWatch(BoxingGame game)
{
    private readonly float[,] _targetTime = new float[game.AllFighters.Count, game.AllFighters.Count];
    private readonly float[] _stillTime = new float[game.AllFighters.Count];
    private readonly float[] _longestStill = new float[game.AllFighters.Count];
    private readonly float[] _runStill = new float[game.AllFighters.Count];
    private readonly float[] _dealt = new float[game.AllFighters.Count];
    private readonly int[] _hits = new int[game.AllFighters.Count];
    private readonly int[] _backHits = new int[game.AllFighters.Count];
    private readonly float[,] _dealtTo = new float[game.AllFighters.Count, game.AllFighters.Count];
    private float _time;
    private float _liveTime;

    private void Count(Fighter attacker, Fighter victim, float damage)
    {
        var a = game.SeatOf(attacker);
        var v = game.SeatOf(victim);
        if (a < 0 || v < 0 || a == v)
        {
            return;
        }

        _hits[a]++;
        _dealt[a] += damage;
        _dealtTo[a, v] += damage;
        var fromAttacker = attacker.ChestPosition - victim.ChestPosition;
        fromAttacker.Y = 0f;
        if (fromAttacker.LengthSquared() > 1e-6f && Vector3.Dot(victim.Frame.Forward, Vector3.Normalize(fromAttacker)) < 0f)
        {
            _backHits[a]++; // 相手の背中の側から当てた（相手が見ていない向きから）
        }
    }

    public void Observe(float dt)
    {
        _time += dt;
        var fighters = game.AllFighters;
        foreach (var hit in game.RecentHits)
        {
            Count(hit.Attacker, hit.Victim, hit.Damage);
        }

        foreach (var hit in game.WeaponHits)
        {
            Count(hit.Attacker, hit.Victim, hit.Damage); // 武器・はめる物（拳のグローブ・素手も）の打撃
        }

        if (game.Resting || game.MatchOver)
        {
            return;
        }

        _liveTime += dt;
        for (var i = 0; i < fighters.Count; i++)
        {
            var f = fighters[i];
            if (!game.InPlay(f))
            {
                continue;
            }

            _targetTime[i, game.SeatOf(game.TargetOf(f))] += dt;
            var moving = f.CenterOfMassVelocity.Length() > 0.15f || f.GloveVelocity(Hand.Left).Length() > 0.6f || f.GloveVelocity(Hand.Right).Length() > 0.6f;
            if (moving)
            {
                _runStill[i] = 0f;
            }
            else
            {
                _runStill[i] += dt;
                _stillTime[i] += dt;
                _longestStill[i] = MathF.Max(_longestStill[i], _runStill[i]);
            }
        }
    }

    /// <summary>全員の今の様子を 1 行で: 席・立つ / 倒れ・中心からの距離・狙い・重心の速さ・体の残り。</summary>
    public static string Trace(BoxingGame game, float t)
    {
        var center = game.Layout.Center;
        var parts = game.AllFighters.Select((f, i) =>
        {
            var flat = f.PelvisPosition - center;
            flat.Y = 0f;
            var state = !game.InPlay(f) ? "OUT" : f.State == FighterState.Standing ? "up" : f.State.ToString();
            return string.Create(CultureInfo.InvariantCulture,
                $"{i}:{state} r{flat.Length():0.0} ->{game.SeatOf(game.TargetOf(f))} v{f.CenterOfMassVelocity.Length():0.00} w{(f.Wields(BoxingGame.WeaponHand) ? 1 : 0)}{Ground(game, f)}{(game.InPlay(f) && f.State == FighterState.Standing ? $" st{f.Stamina:0.00} lo{MathF.Min(f.LegOxygen(Hand.Left), f.LegOxygen(Hand.Right)):0.00} lc{MathF.Min(f.LegCondition(true), f.LegCondition(false)):0.00}{(f.LegSevered ? " SEV" : "")}{(f.IsGrabbed ? " GRAB" : "")}" : game.InPlay(f) ? $" bls{(f.BodyLetsStand ? 1 : 0)} rise{f.Rise:0.00} reach{f.StandReach:0.00} con{f.Consciousness:0.00} cc{f.Concussion:0.00} canst{(f.CanStand ? 1 : 0)}{Touch(game, f)}" : "")}");
        });
        return string.Create(CultureInfo.InvariantCulture, $"t={t,6:0.0} R{game.MatchRound}{(game.InBreak ? "b" : "")} | {string.Join(" | ", parts)}");
    }

    /// <summary>この人の体が触れている、ほかの人の体・物（部位ごとの接触の数。引っかかりの調べ）。</summary>
    private static string Touch(BoxingGame game, Fighter f)
    {
        var touching = new SortedDictionary<string, int>(StringComparer.Ordinal);
        foreach (var contact in game.World.Contacts())
        {
            if (contact.StaticA || contact.StaticB)
            {
                continue;
            }

            var (mine, other) = f.Owns(contact.A) ? (contact.A, contact.B) : f.Owns(contact.B) ? (contact.B, contact.A) : (-1, -1);
            if (mine < 0 || f.Owns(other))
            {
                continue;
            }

            var owner = game.AllFighters.FirstOrDefault(x => x.Owns(other));
            var key = $"{(mine == f.Parts.LeftShin || mine == f.Parts.RightShin || mine == f.Parts.LeftThigh || mine == f.Parts.RightThigh ? "leg" : mine == f.Parts.LeftFoot || mine == f.Parts.RightFoot ? "foot" : "body")}-{(owner is not null ? game.SeatOf(owner).ToString(CultureInfo.InvariantCulture) : game.WeaponOf(f)?.Body.BodyId == other ? "ownweapon" : game.Weapons.FirstOrDefault(w => w.Body.BodyId == other) is { } wpn ? $"weapon{game.SeatOf(game.WielderOf(wpn) ?? f)}" : "obj")}";
            touching[key] = touching.GetValueOrDefault(key) + 1;
        }

        return touching.Count == 0 ? "" : $" touch[{string.Join(",", touching.Select(p => $"{p.Key}:{p.Value}"))}]";
    }

    /// <summary>倒れた相手への攻め（<see cref="Bots.GroundStriker"/>）の様子: 攻めているか・立つ所までの距離（腕の長さ）・打ち始めた回数。</summary>
    private static string Ground(BoxingGame game, Fighter f)
    {
        if (game.ControlFor(f) is not Bots.GroundStriker striker || !game.InPlay(f))
        {
            return "";
        }

        var sense = game.SenseFor(f);
        return Bots.GroundStrike.Applies(sense)
            ? string.Create(CultureInfo.InvariantCulture, $" g{striker.Ground.Distance:0.0}/{striker.Ground.Started} m{striker.Last.MoveWorld.Length():0.0}{Touch(game, f)}")
            : sense.OpponentDowned ? $" g-({(sense.SelfDowned ? "selfdown" : "nope")})" : "";
    }

    public string Report(double wallSeconds)
    {
        var fighters = game.AllFighters;
        var w = new StringWriter(CultureInfo.InvariantCulture);
        w.WriteLine($"royale: {fighters.Count} fighters, seed {game.Seed}, sim {_time:0.0} s (live {_liveTime:0.0} s), wall {wallSeconds:0.0} s ({wallSeconds / Math.Max(1e-3, _time):0.00} s per sim s)");
        w.WriteLine($"result: {(game.Winner is { } winner ? $"winner {game.LabelOf(winner)}" : game.WinningTeam is { } team && game.MatchOver && game.Decision is null ? $"team {team} wins ({string.Join(", ", fighters.Where(game.InPlay).Select(game.LabelOf))})" : game.Decision ?? "unfinished")}");
        foreach (var e in game.Eliminations)
        {
            var v = game.SeatOf(e.Fighter);
            var received = Enumerable.Range(0, fighters.Count).Sum(a => _dealtTo[a, v]);
            var share = e.By is { } killer && received > 0f ? _dealtTo[game.SeatOf(killer), v] / received : 0f;
            w.WriteLine($"  out R{e.Round} t={e.Time:0.0}s {game.LabelOf(e.Fighter)} — {e.Cause}{(e.By is { } by ? $" (by {game.LabelOf(by)}, who dealt {share:P0} of what the victim took)" : "")}");
        }

        w.WriteLine("seat  name          hits  dealt  backHits  stillTotal  stillLongest  aimedAtShare");
        var aimedAt = new float[fighters.Count];
        var total = 0f;
        for (var i = 0; i < fighters.Count; i++)
        {
            for (var j = 0; j < fighters.Count; j++)
            {
                aimedAt[j] += _targetTime[i, j];
                total += _targetTime[i, j];
            }
        }

        for (var i = 0; i < fighters.Count; i++)
        {
            w.WriteLine($"{i,4}  {game.LabelOf(fighters[i]),-12}  {_hits[i],4}  {_dealt[i],5:0}  {_backHits[i],8}  {_stillTime[i],9:0.0}s  {_longestStill[i],11:0.0}s  {(total > 0 ? aimedAt[i] / total : 0f),11:P0}");
        }

        return w.ToString();
    }
}

/// <summary>バトルロイヤルの動画: 全員を見下ろし、いちばん激しい所へ寄せるカメラ。頭の上の名前の札・生き残りの一覧・脱落の字幕・打撃音。</summary>
internal sealed class RoyaleVideo(int width, int height, int fps, IReadOnlyList<TvCorner> corners, IReadOnlyList<string>? teamNames = null)
{
    private static readonly Vector4 White = new(1f, 1f, 1f, 1f);
    private static readonly Vector4 Cream = new(0.95f, 0.91f, 0.84f, 1f);
    private static readonly Vector4 Grey = new(0.6f, 0.58f, 0.55f, 1f);
    private static readonly Vector4 Red = new(0.93f, 0.2f, 0.16f, 1f);

    private Vector3 _look;
    private Vector3 _eye;
    private float _radius;
    private Vector3 _heat;
    private float _heatWeight;
    private readonly RoyaleDirector _director = new(new DirectorTuning());
    private int _cuts;
    private readonly RoyaleOverlay _overlay = new(width, height, corners, teamNames);
    private bool _cutPending; // 描かない刻み（60 Hz の判断と 30 fps の絵）で起きたカットを、次に描く絵まで覚える

    public void Make(BoxingGame game, float seconds, Func<BoxingGame, float, bool> step, string outPath, TextWriter log)
    {
        var impacts = new ImpactAudio();
        var audioTimeline = new ImpactTimeline();
        var clips = new List<(double At, byte[] Pcm)>();
        var audioSeed = 0;
        var wavPath = Path.ChangeExtension(outPath, ".wav");
        var silentPath = Path.ChangeExtension(outPath, ".silent.mp4");
        var frames = 0;
        using var renderer = new HeadlessRenderer((uint)width, (uint)height);
        using var ffmpeg = Process.Start(new ProcessStartInfo("ffmpeg",
            $"-y -loglevel error -f rawvideo -pix_fmt rgba -s {width}x{height} -r {fps} -i - -c:v libx264 -pix_fmt yuv420p -crf 20 -preset medium \"{silentPath}\"")
        {
            RedirectStandardInput = true,
            UseShellExecute = false,
        }) ?? throw new IOException("ffmpeg を起動できません（PATH にありますか）。");
        var instances = new List<PrimitiveInstance>();
        var lines = new List<DebugLine>();
        var nextFrame = 0f;
        var startCells = game.AllFighters.Select(Cells).ToArray();
        _look = game.Layout.Center + new Vector3(0f, 2.5f, 0f);
        _radius = game.Layout.Circumradius;
        var t = 0f;
        var tail = 2.5f; // 決着の後も少し映す
        while (t < seconds && (step(game, t) || (tail -= BoxingGameDt) > 0f))
        {
            t += BoxingGameDt;
            audioSeed++;
            foreach (var voice in impacts.Push(t, ImpactAudio.Gather(game.RecentHits, game.RecentContacts, game.RecentShatters, weaponHits: [.. game.WeaponHits, .. game.SelfWeaponHits])))
            {
                clips.Add((voice.OccurredAt, voice.Pcm));
                audioTimeline.Add(voice, voice.OccurredAt, frames / (double)fps);
            }

            if (VoxelBreakSound.Landings(game.RecentLandings, audioSeed) is { } landingPcm && impacts.LimitBackground(t, landingPcm) is { } landed)
            {
                clips.Add((t, landed));
            }

            foreach (var swing in game.RecentSwings)
            {
                if (SwingSound.Synthesize(swing.Speed, audioSeed) is { } whoosh)
                {
                    clips.Add((t, whoosh));
                }
            }

            foreach (var sever in game.RecentSevers)
            {
                clips.Add((t, SeverSound.Synthesize(sever.Strength, audioSeed)));
            }

            foreach (var shatter in game.RecentShatters.Where(s => s.Fatal))
            {
                clips.Add((t, VoxelBreakSound.Fatal(audioSeed)));
            }

            if (GongSound.Synthesize(game.RecentBell) is { } gong)
            {
                clips.Add((t, gong));
            }

            Heat(game);
            Direct(game, t);
            if (t < nextFrame)
            {
                continue;
            }

            nextFrame += 1f / fps;
            BoxingRenderer.Fill(game, showTargetGhosts: false, instances, lines);
            var (eye, look) = Camera(game);
            var (vp, displayEye) = DisplaySpace.Standard.Camera(eye, look, Vector3.UnitY, 45f * MathF.PI / 180f, (float)width / height, 0.05f, 60f);
            var (text, shapes) = _overlay.Draw(game, vp, t, startCells, _director.Current, _director.ShotStart);
            var rgba = renderer.RenderPrimitiveScene(ArenaLook.Background, vp, instances, lines, text, particles: game.BuildParticles(), lighting: game.BuildLighting(),
                shadow: game.BuildShadow(), cameraPosition: displayEye, shapes: shapes, postProcess: game.BuildPostProcess(), voxels: game.BuildVoxelVolumes());
            ffmpeg.StandardInput.BaseStream.Write(rgba);
            frames++;
        }

        ffmpeg.StandardInput.Close();
        ffmpeg.WaitForExit();
        if (ffmpeg.ExitCode != 0)
        {
            throw new IOException($"ffmpeg failed with exit code {ffmpeg.ExitCode}.");
        }

        clips.AddRange(impacts.Flush().Select(v => (v.OccurredAt, v.Pcm)));
        var duration = (float)frames / fps;
        var track = new float[(int)(duration * HitSound.SampleRate)];
        foreach (var (at, pcm) in clips)
        {
            VideoMaker.Mix(track, pcm, (float)at, 1f);
        }

        VideoMaker.WriteWav(wavPath, track, duration);
        audioTimeline.Write(outPath);
        using var mux = Process.Start(new ProcessStartInfo("ffmpeg",
            $"-y -loglevel error -i \"{silentPath}\" -i \"{wavPath}\" -c:v copy -c:a aac -b:a 96k -t {duration.ToString(CultureInfo.InvariantCulture)} \"{outPath}\"") { UseShellExecute = false })
            ?? throw new IOException("音声を結合する ffmpeg を起動できません。");
        mux.WaitForExit();
        if (mux.ExitCode != 0)
        {
            throw new IOException($"音声の結合に失敗しました: {mux.ExitCode}");
        }

        File.Delete(silentPath);
        File.Delete(wavPath);
        log.WriteLine(string.Create(CultureInfo.InvariantCulture, $"{outPath}: {frames} frames ({duration:0.0}s), {_cuts} camera cuts"));
    }

    private const float BoxingGameDt = 1f / 60f;

    private string TeamName(int team) => teamNames is { } names && team < names.Count ? names[team] : $"チーム {team + 1}";

    private static int Cells(Fighter fighter) => fighter.Voxels?.Parts.Sum(p => p.Body.Grid.FilledCount) ?? 1;

    internal static string Cause(string cause) => cause switch
    {
        "OUT OF THE CAGE" => "檻の外へ",
        _ => cause.Replace(" DESTROYED", "", StringComparison.Ordinal).ToLowerInvariant().Replace("alien ", "", StringComparison.Ordinal) switch
        {
            "brain" => "脳",
            "heart" => "心臓",
            "liver" => "肝臓",
            "neck" => "首",
            "lungs" or "lung" => "肺",
            "spine" or "cord" => "脊髄",
            "gut" => "腸",
            "artery" => "動脈",
            "eye" => "目",
            var other => other,
        },
    };

    /// <summary>直近の打撃の所を、強さで重み付けした「激しい所」として覚える（1 秒ほどで薄れる）。</summary>
    private void Heat(BoxingGame game)
    {
        _heat *= 0.97f;
        _heatWeight *= 0.97f;
        foreach (var hit in game.RecentHits)
        {
            _heat += hit.Point * hit.Damage;
            _heatWeight += hit.Damage;
        }

        foreach (var hit in game.WeaponHits)
        {
            _heat += hit.Point * hit.Damage;
            _heatWeight += hit.Damage;
        }
    }

    /// <summary>カメラの切り替え（<see cref="RoyaleDirector"/>）へ、この刻みの様子と当たった打撃を渡す。</summary>
    private void Direct(BoxingGame game, float t)
    {
        var (view, hits) = DirectorInput(game);
        _director.Update(t, BoxingGameDt, view, hits, wholeCage: game.InIntro || game.InBreak);
        if (_director.Cut)
        {
            _cuts++;
            _cutPending = true;
        }
    }

    /// <summary>カメラの判断に渡す今の刻みの様子（選手と、当たった打撃）。動画と記録（<see cref="RoyaleLog"/>）が同じものを渡す。</summary>
    internal static (DirectorFighter[] View, (int, int, float)[] Hits) DirectorInput(BoxingGame game)
    {
        int Seat(Fighter f) => game.SeatOf(f);
        var view = game.AllFighters.Select(f => new DirectorFighter(f.ChestPosition, game.InPlay(f), f.State != FighterState.Standing,
            game.TargetSeatOf(f))).ToArray();
        (int, int, float)[] hits = [.. game.RecentHits.Select(h => (Seat(h.Attacker), Seat(h.Victim), h.Damage))
            .Concat(game.WeaponHits.Select(h => (Seat(h.Attacker), Seat(h.Victim), h.Damage)))];
        return (view, hits);
    }

    /// <summary>
    /// 今の絵のカメラ。全体の絵: 生きている人の重心（と激しい所）を見下ろし、全員の広がりが入る所から。戦いの絵: 2 人（とそばにいる人）を、
    /// 2 人を結ぶ線の横から（どちらの顔も見える、ボクシング中継の向き）。線の同じ側に留まり（向きが飛ばない）、絵が切り替わった瞬間はカメラを飛ばす（カット）。
    /// </summary>
    private (Vector3 Eye, Vector3 Look) Camera(BoxingGame game)
    {
        var (eye, look) = _director.Current.IsWide ? WideCamera(game) : FightCamera(game, _director.Current);
        var display = DisplaySpace.Standard;
        // 目は表示の空間で部屋の中へ寄せる（天井の上・壁の外へ出ると天井の裏しか映らない: LivingRoom.KeepInside）。
        eye = display.ToPhysics(LivingRoom.KeepInside(display.Point(eye), 0.15f));
        var snap = _cutPending || _eye == Vector3.Zero;
        _cutPending = false;
        // 追う速さは秒あたり（fps に依らない）: 1 秒でずれの約 88 %（見る点）・約 79 %（目）を詰める。
        _look = snap ? look : Vector3.Lerp(_look, look, 1f - MathF.Pow(0.12f, 1f / fps));
        _eye = snap ? eye : Vector3.Lerp(_eye, eye, 1f - MathF.Pow(0.21f, 1f / fps));
        return (_eye, _look);
    }

    private (Vector3 Eye, Vector3 Look) WideCamera(BoxingGame game)
    {
        var alive = game.AllFighters.Where(game.InPlay).ToList();
        if (alive.Count == 0)
        {
            alive = [.. game.AllFighters];
        }

        var center = alive.Aggregate(Vector3.Zero, (s, f) => s + f.ChestPosition) / alive.Count;
        if (_heatWeight > 1f)
        {
            var hot = _heat / _heatWeight;
            var share = MathF.Min(0.5f, _heatWeight / 40f);
            center = Vector3.Lerp(center, hot, share);
        }

        var spread = alive.Max(f => Vector3.Distance(new Vector3(f.ChestPosition.X, center.Y, f.ChestPosition.Z), center));
        _radius = _cutPending ? MathF.Max(1.6f, spread) : _radius + ((MathF.Max(1.6f, spread) - _radius) * 0.04f);
        var back = Vector3.Normalize(new Vector3(0f, 0.6f, -0.8f));
        var distance = Math.Clamp((_radius * 1.6f) + 2.5f, 4f, 11f);
        return (center + (back * distance), center);
    }

    private (Vector3 Eye, Vector3 Look) FightCamera(BoxingGame game, Shot shot)
    {
        var a = game.AllFighters[shot.A].ChestPosition;
        var b = game.AllFighters[shot.B].ChestPosition;
        var mid = (a + b) * 0.5f;
        // そばにいる 3 人目も絵に入れる（割って入る・背中から打つ人）。
        var near = game.AllFighters.Where(f => game.InPlay(f) && Vector3.Distance(f.ChestPosition, mid) < 1.6f).Select(f => f.ChestPosition).Append(a).Append(b).ToList();
        var center = near.Aggregate(Vector3.Zero, (s, p) => s + p) / near.Count;
        var extent = near.Max(p => Vector2.Distance(new Vector2(p.X, p.Z), new Vector2(center.X, center.Z)));
        var line = new Vector3(b.X - a.X, 0f, b.Z - a.Z);
        line = line.LengthSquared() > 1e-6f ? Vector3.Normalize(line) : Vector3.UnitX;
        var side = Vector3.Cross(Vector3.UnitY, line);
        var distance = Math.Clamp((extent * 1.7f) + 2.2f, 2.8f, 6.5f);
        var flat = distance * MathF.Cos(24f * MathF.PI / 180f);
        // 撮る向き: 2 人を結ぶ線の真横（両方の顔が見える）を好み、金網の内側から撮れる向きを選ぶ（外からだと網越しになる）。
        // 候補は真横から ±25°・±50° ずつ、線の両側。切り替えの瞬間は全部から、絵の途中は今の向きの近く（±25° まで）から選ぶ（向きが飛ばない）。
        var ring = game.Layout.Center;
        var inside = (game.Layout.IsCage ? game.Layout.Circumradius * MathF.Cos(MathF.PI / 6f) : game.Layout.Half) - 0.35f; // 金網・ロープの面（内接円）の少し内側
        var fresh = _cutPending || _eye == Vector3.Zero;
        var now = new Vector3(_eye.X - center.X, 0f, _eye.Z - center.Z);
        now = now.LengthSquared() > 1e-6f ? Vector3.Normalize(now) : side;
        var best = (Dir: side, Reach: 0f, Score: float.NegativeInfinity);
        // 2 人の中心が縁の外寄りにいるときは、縁の内側の点から測る（金網に押し付けられた 2 人でも、中へ向く向きは撮れる）。
        var from = new Vector2(center.X - ring.X, center.Z - ring.Z);
        from = from.Length() > inside * 0.98f ? Vector2.Normalize(from) * (inside * 0.98f) : from;
        for (var pass = 0; pass < 2 && float.IsNegativeInfinity(best.Score); pass++)
        {
            // 1 回目は今の向きの近くだけ。どれも外れたら（2 人が速く回った・3 人目が出入りした）、全部から選び直す。
            var any = fresh || pass == 1;
            foreach (var sign in (ReadOnlySpan<float>)[1f, -1f])
            {
                foreach (var degrees in (ReadOnlySpan<float>)[0f, 25f, -25f, 50f, -50f])
                {
                    var dir = Vector3.Transform(side * sign, Quaternion.CreateFromAxisAngle(Vector3.UnitY, degrees * MathF.PI / 180f));
                    if (!any && Vector3.Dot(dir, now) < MathF.Cos(26f * MathF.PI / 180f))
                    {
                        continue;
                    }

                    var reach = MathF.Min(flat, ReachInside(from, new Vector2(dir.X, dir.Z), inside));
                    // 金網の内側に収まる距離を一番に、真横からのずれを二番に、今の向きに近いことを三番に。
                    var score = reach - (MathF.Abs(degrees) * 0.01f) + (Vector3.Dot(dir, now) * 0.05f);
                    if (score > best.Score)
                    {
                        best = (dir, reach, score);
                    }
                }
            }
        }

        // 内側に収まらない分は、高く上がって見下ろす（同じ距離のまま角度を立てる: 金網の上から撮る）。
        var horizontal = MathF.Max(0.6f, best.Reach);
        var height = MathF.Sqrt(MathF.Max(0f, (distance * distance) - (horizontal * horizontal)));
        height = MathF.Max(height, distance * MathF.Sin(24f * MathF.PI / 180f));
        return (center + (best.Dir * horizontal) + (Vector3.UnitY * height), center);
    }

    /// <summary>円の中の点 <paramref name="from"/> から向き <paramref name="dir"/>（単位）へ、半径 <paramref name="radius"/> の円の縁まで進める距離（外にいれば 0）。</summary>
    private static float ReachInside(Vector2 from, Vector2 dir, float radius)
    {
        var b = Vector2.Dot(from, dir);
        var c = from.LengthSquared() - (radius * radius);
        var disc = (b * b) - c;
        return c > 0f || disc < 0f ? 0f : -b + MathF.Sqrt(disc);
    }
}
