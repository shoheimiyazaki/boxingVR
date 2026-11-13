using System.Diagnostics;
using System.Globalization;
using System.Numerics;
using Aigis.Render;
using MiniRing.Audio;
using MiniRing.Bots;

namespace MiniRing.Arena;

/// <summary>
/// ボット同士の試合を mp4 にする（GPU のヘッドレス描画 → ffmpeg）。
/// 構成: タイトル → 試合（技・守り・クリーンヒットの字幕、打撃音つき）→ 結果。
/// 試合は種で決まるので、同じ引数なら同じ動画になる。
/// </summary>
/// <param name="width">映像の幅 [px]。</param>
/// <param name="height">映像の高さ [px]。</param>
/// <param name="fps">毎秒のコマ数。</param>
/// <param name="breakStyle">ボクセルが砕ける音の方式。</param>
/// <param name="hitSounds">パンチが当たる音を入れるか。</param>
/// <param name="speed">試合の速さ（<see cref="GameSpeed"/>。標準 0.75 倍）。試合そのものを遅く進めて撮るので、映像は滑らかなまま遅くなる
/// （出来た動画を引き伸ばすのと違い、コマが重複しない）。音は映像の時刻に置き、音そのものの速さ・高さは変えない。</param>
/// <param name="tv">テレビ中継風（<see cref="MatchRules.Tv"/> の 30 秒 × 5 ラウンド、得点表と時計、会場の歓声、実況用の出来事の記録 *.events.json）。</param>
/// <param name="faceCam">試合中、画面の両脇に両コーナーの顔の窓を出す（<see cref="FaceCam"/>）。null なら出さない。</param>
internal sealed class VideoMaker(int width, int height, int fps, BreakStyle breakStyle = VoxelBreakSound.DefaultStyle, bool hitSounds = false, FaceCam? faceCam = null, float speed = GameSpeed.Standard, bool tv = false)
{
    /// <summary>試合を 1 歩進める時間の上限 [s]（ホストの 1 歩と同じ）。速さを落とすと 1 歩はこれより短くなる。</summary>
    private const float MaxStepDt = 1f / 60f;
    private const float TitleSeconds = 3.5f;
    private const float ResultSeconds = 5f;

    /// <summary>K.O. の後、結果へ進むまでに撮る長さ [s]。ゲームが次のラウンドを始める（3.5 s）より短く。</summary>
    private const float AftermathSeconds = 3f;

    /// <summary>字幕に出す「重い一撃」のダメージ（1 発でクリーンヒットと言える程度）。</summary>
    private const float BigHitDamage = 15f;

    private static readonly Vector4 White = new(1f, 1f, 1f, 1f);
    private static readonly Vector4 Red = new(1f, 0.45f, 0.4f, 1f);
    private static readonly Vector4 Blue = new(0.5f, 0.68f, 1f, 1f);
    private static readonly Vector4 Gold = new(1f, 0.85f, 0.3f, 1f);
    private static readonly Vector4 Grey = new(0.75f, 0.78f, 0.85f, 1f);

    /// <param name="red">赤コーナー（プレイヤー側）。</param>
    /// <param name="blue">青コーナー（CPU 側）。</param>
    /// <param name="seed">試合の種。</param>
    /// <param name="seconds">試合の長さ [s]。</param>
    /// <param name="outPath">書き出す mp4 のパス。</param>
    /// <param name="log">進み具合の書き出し先。</param>
    /// <param name="afterStep">試合を 1 歩進めるたびに呼ぶ（時刻 [s]）。ドリルの合図などに使う。</param>
    /// <param name="untilKo">
    /// K.O.（臓器が壊れる・10 カウント・リングアウト）で試合を終える。<paramref name="seconds"/> は打ち切りの上限になり、
    /// 上の数字は残り時間でなく経過時間、K.O. の後 <see cref="AftermathSeconds"/> 秒の倒れた様子まで撮って結果へ進む。
    /// </param>
    /// <param name="redWear">赤コーナーが手にはめる物（既定 グローブ）。</param>
    /// <param name="blueWear">青コーナーが手にはめる物（既定 グローブ）。</param>
    public void Make(Contender red, Contender blue, int seed, float seconds, string outPath, TextWriter log, Action<float>? afterStep = null, bool untilKo = false,
        MiniRing.Weapons.HandWear redWear = MiniRing.Weapons.HandWear.Glove, MiniRing.Weapons.HandWear blueWear = MiniRing.Weapons.HandWear.Glove)
    {
        var redBots = new List<IFighterControl>();
        var blueBots = new List<IFighterControl>();
        var corners = new BoxingCorners(blue.Name, s => Keep(blueBots, blue.Create(s)), s => Keep(redBots, red.Create(s)), PlayerWear: redWear, CpuWear: blueWear, PlayerName: red.Name);
        // 画面と実況ではリングネームで呼ぶ（ドリルで生まれた個体にもそれぞれの名前がある）。
        var redRing = BotNames.RingNameOf(red.Name);
        var blueRing = BotNames.RingNameOf(blue.Name);
        using var game = new BoxingGame(seed: seed, corners: corners, rules: tv ? MatchRules.Tv : null);
        if (tv)
        {
            seconds = MathF.Max(seconds, MatchRules.Tv.TotalSeconds + AftermathSeconds + 1f); // テレビ中継は試合の終わりまで撮る（--seconds が短くても）
        }

        var broadcast = tv ? new Broadcast(redRing, blueRing, red.Name, blue.Name) : null;
        using var renderer = new HeadlessRenderer((uint)width, (uint)height);
        var screen = new Vector2(width, height);
        var panel = faceCam?.Panel(screen, FaceSide.Left) ?? default; // 左右の窓は同じ大きさ
        // 顔の窓は窓の大きさで別に描いて書き写す（ヘッドレスの描画は 1 つのカメラで 1 枚。ホストでは重ねる窓として描く）。
        using var paneRenderer = faceCam is null ? null : new HeadlessRenderer((uint)panel.WidthPx, (uint)panel.HeightPx);
        var audio = new float[(int)((TitleSeconds + (seconds / speed) + ResultSeconds + 1f) * HitSound.SampleRate)];
        var videoPath = Path.ChangeExtension(outPath, ".video.mp4");
        var wavPath = Path.ChangeExtension(outPath, ".wav");
        var instances = new List<PrimitiveInstance>();
        var lines = new List<DebugLine>();
        var redCaption = new Caption();
        var blueCaption = new Caption();
        var flashes = new List<(string Text, Vector4 Color, float Until)>();
        var redCombos = new Dictionary<string, int>();
        var blueCombos = new Dictionary<string, int>();
        float redDamage = 0f, blueDamage = 0f;
        var frame = 0;
        string? finish = null;
        int? koWinner = null;
        var watch = Stopwatch.StartNew();

        // 途中で失敗しても、作りかけの映像・音声ファイルと ffmpeg を残さない。
        try
        {
            using (var ffmpeg = StartFfmpeg(videoPath))
            {
                try
                {
                    var stdin = ffmpeg.StandardInput.BaseStream;
                    void Emit(Matrix4x4 viewProjection, Vector3 eye, IReadOnlyList<ScreenTextDraw> text, IReadOnlyList<UiShapeDraw> shapes, bool withFace = false)
                    {
                        var face = withFace ? faceCam : null;
                        var rgba = renderer.RenderPrimitiveScene(ArenaLook.Background, viewProjection, instances, lines, text, particles: game.BuildParticles(), lighting: game.BuildLighting(),
                            shadow: game.BuildShadow(), cameraPosition: eye, shapes: face is null ? shapes : [.. shapes, .. face.Frames(game, screen)], postProcess: game.BuildPostProcess(), voxels: game.BuildVoxelVolumes());
                        if (face is not null && paneRenderer is not null)
                        {
                            foreach (var facePane in face.Panes(game, screen))
                            {
                                var at = facePane.Panel;
                                var (faceViewProjection, faceEye) = face.Camera(game, facePane.Fighter, at.AspectRatio);
                                var pane = paneRenderer.RenderPrimitiveScene(ArenaLook.Background, faceViewProjection, instances, lines, [], particles: game.BuildParticles(), lighting: game.BuildLighting(),
                                    shadow: game.BuildShadow(), cameraPosition: faceEye, postProcess: game.BuildPostProcess(), voxels: game.BuildVoxelVolumes());
                                for (var row = 0; row < at.HeightPx; row++)
                                {
                                    Array.Copy(pane, row * at.WidthPx * 4, rgba, (((at.YPx + row) * width) + at.XPx) * 4, at.WidthPx * 4);
                                }
                            }
                        }

                        stdin.Write(rgba);
                        frame++;
                    }

                    // タイトル: 試合前の構えを背景に。
                    BoxingRenderer.Fill(game, showTargetGhosts: false, instances, lines);
                    for (var t = 0f; t < TitleSeconds; t += 1f / fps)
                    {
                        var (vp, eye) = CameraAt(game, t * 0.15f);
                        Emit(vp, eye, Title(redRing, blueRing, MathF.Min(1f, t / 0.6f)), Dim(0.6f));
                    }

                    // 試合。
                    // 1 コマで進める試合の時間（速さ / fps）を、MaxStepDt 以下の等しい歩に分ける（30 fps なら 0.75 倍で 1/80 s × 2 歩）。
                    var frameDt = speed / fps;
                    var stepsPerFrame = Math.Max(1, (int)MathF.Ceiling((frameDt / MaxStepDt) - 1e-3f));
                    var stepDt = frameDt / stepsPerFrame;
                    var time = 0f;
                    float? endAt = null;
                    for (var step = 0; time < seconds && (endAt is null || time < endAt); step++)
                    {
                        game.Step(default, stepDt);
                        time += stepDt;
                        var at = TitleSeconds + (time / speed); // 映像の時刻（音を置く所）
                        broadcast?.Observe(game, at, step);
                        if (tv && endAt is null && game.MatchOver)
                        {
                            // テレビ中継: K.O. か最終ラウンドの判定で試合が終わる。少し余韻を撮って結果へ。
                            finish = game.Decision ?? game.Message;
                            koWinner = game.PlayerWins > game.CpuWins ? 1 : game.CpuWins > game.PlayerWins ? -1 : 0;
                            endAt = time + AftermathSeconds;
                        }

                        if (GongSound.Synthesize(game.RecentBell) is { } gong)
                        {
                            Mix(audio, gong, at, 1f);
                        }

                        foreach (var graze in game.RecentGrazes)
                        {
                            Mix(audio, GrazeSound.Synthesize(graze.Speed, step), at, 1f);
                        }

                        foreach (var sever in game.RecentSevers)
                        {
                            Mix(audio, SeverSound.Synthesize(sever.Strength, step), at, 1f);
                        }

                        // 骨が折れた（肉でぶら下がる）: 画面に出し、記録に残す（どの打撃で・どこが折れたか、動画のどこを見ればよいか）。
                        foreach (var broken in game.BrokenBones)
                        {
                            flashes.Add(("BROKEN!", Gold, time + (1.2f * speed)));
                            log.WriteLine(string.Create(System.Globalization.CultureInfo.InvariantCulture,
                                $"bone broken at {time:0.0} s (video {at:0.0} s): {(broken.Victim == game.Player ? "red" : "blue")} {broken.Part}"));
                        }

                        foreach (var contact in game.RecentContacts)
                        {
                            Mix(audio, ContactSound.Synthesize(contact.Pair, contact.Strength, step), at, 1f);
                        }
                        if (untilKo && endAt is null && game.RoundOver)
                        {
                            // ラウンドが終わった理由（K.O. の種類と勝者）。ゲームは RoundResetDelay の後に次のラウンドを始めるので、その前に止める。
                            finish = game.Message;
                            koWinner = game.PlayerWins > game.CpuWins ? 1 : -1;
                            endAt = time + AftermathSeconds;
                            flashes.Add(("K.O.", Gold, endAt.Value));
                        }
                        if (VoxelBreakSound.Landings(game.RecentLandings, step, breakStyle) is { } landed)
                        {
                            Mix(audio, landed, at, 1f);
                        }

                        foreach (var swing in game.RecentSwings)
                        {
                            if (SwingSound.Synthesize(swing.Speed, step) is { } whoosh)
                            {
                                Mix(audio, whoosh, at, 1f);
                            }
                        }

                        foreach (var shatter in game.RecentShatters)
                        {
                            if (shatter.Grains.Count > 0)
                            {
                                Mix(audio, VoxelBreakSound.Synthesize(shatter, step, breakStyle), at, 0.8f);
                            }

                            if (shatter.Fatal)
                            {
                                Mix(audio, VoxelBreakSound.Fatal(seed: 7, breakStyle), at, 1f);
                                flashes.Add(("FATAL", Gold, time + (1.2f * speed))); // 表示の長さは映像の秒で
                            }
                        }

                        afterStep?.Invoke(time);
                        foreach (var hit in game.RecentHits)
                        {
                            var redHit = hit.Attacker == game.Player;
                            if (redHit)
                            {
                                redDamage += hit.Damage;
                            }
                            else
                            {
                                blueDamage += hit.Damage;
                            }

                            var tone = HitTone.Of(hit);
                            if (hitSounds)
                            {
                                Mix(audio, HitSound.Synthesize(tone, step), at, 0.55f + (0.45f * tone.Intensity));
                            }

                            if (hit.Damage >= BigHitDamage)
                            {
                                flashes.Add(($"{HitZones.Label(hit.Zone)}  {hit.Damage:0}", redHit ? Red : Blue, time + 0.7f));
                            }
                        }

                        UpdateCaption(redCaption, redBots, redCombos, time);
                        UpdateCaption(blueCaption, blueBots, blueCombos, time);
                        if (broadcast is not null)
                        {
                            // 技・守りの名前（ボットの頭の中から）を、実況の資料に足す。
                            foreach (var (caption, ring) in new[] { (redCaption, redRing), (blueCaption, blueRing) })
                            {
                                if (caption.ComboTime == time && caption.Combo is { } combo)
                                {
                                    broadcast.Note(at, "technique", ring, $"{ring} が {combo}");
                                }

                                if (caption.DefenseTime == time && caption.Defense is { } defense && defense != caption.LastNotedDefense)
                                {
                                    broadcast.Note(at, "defense", ring, $"{ring} が {defense}");
                                }

                                caption.LastNotedDefense = caption.DefenseTime == time ? caption.Defense : null;
                            }
                        }
                        flashes.RemoveAll(f => f.Until < time);
                        if (step % stepsPerFrame != 0)
                        {
                            continue;
                        }

                        BoxingRenderer.Fill(game, showTargetGhosts: false, instances, lines);
                        var (vp, eye) = CameraAt(game, (TitleSeconds * 0.15f) + (time / speed * 0.15f));
                        Emit(vp, eye, MatchOverlay(redRing, blueRing, redCaption, blueCaption, flashes, redDamage, blueDamage, time, untilKo || tv ? null : seconds, game, vp),
                            [.. Bars(), .. tv ? TvHud.Shapes(game, screen) : StaminaBars(game), .. DownCountHud.Shapes(game, vp, screen)], withFace: true);
                        if (frame % (fps * 5) == 0)
                        {
                            log.WriteLine(string.Create(CultureInfo.InvariantCulture, $"  {time:0}s / {seconds:0}s ({watch.Elapsed.TotalSeconds:0}s elapsed)"));
                        }
                    }

                    // 結果。
                    var winner = koWinner ?? (MathF.Abs(redDamage - blueDamage) <= MathF.Max(1f, 0.05f * (redDamage + blueDamage) / 2f) ? 0 : MathF.Sign(redDamage - blueDamage));
                    BoxingRenderer.Fill(game, showTargetGhosts: false, instances, lines);
                    for (var t = 0f; t < ResultSeconds; t += 1f / fps)
                    {
                        var (vp, eye) = CameraAt(game, ((TitleSeconds + (time / speed)) * 0.15f) + (t * 0.15f));
                        Emit(vp, eye, Result(redRing, blueRing, redDamage, blueDamage, winner, MathF.Min(1f, t / 0.6f), finish), Dim(0.6f * MathF.Min(1f, t / 0.6f)));
                    }

                    stdin.Close();
                    ffmpeg.WaitForExit();
                    if (ffmpeg.ExitCode != 0)
                    {
                        throw new IOException($"ffmpeg (video) failed with exit code {ffmpeg.ExitCode}.");
                    }
                }
                catch
                {
                    if (!ffmpeg.HasExited)
                    {
                        ffmpeg.Kill();
                        ffmpeg.WaitForExit();
                    }

                    throw;
                }
            }

            if (broadcast is not null)
            {
                broadcast.MixInto(audio, HitSound.SampleRate, seed);
                broadcast.WriteEvents(Path.ChangeExtension(outPath, ".events.json"));
            }

            WriteWav(wavPath, audio, (float)frame / fps);
            Run("ffmpeg", $"-y -loglevel error -i \"{videoPath}\" -i \"{wavPath}\" -c:v copy -c:a aac -b:a 160k -shortest \"{outPath}\"");
        }
        finally
        {
            File.Delete(videoPath);
            File.Delete(wavPath);
        }

        log.WriteLine(string.Create(CultureInfo.InvariantCulture, $"{outPath}: {frame} frames ({(float)frame / fps:0.0}s) in {watch.Elapsed.TotalSeconds:0}s"));
    }

    /// <summary>名前とダメージの下のスタミナのメーター（左右）。</summary>
    private IEnumerable<UiShapeDraw> StaminaBars(BoxingGame game)
        => [.. TvHud.StaminaBar(new Vector2(28f, 92f), 140f, 8f, game.Player), .. TvHud.StaminaBar(new Vector2(width - 28f - 140f, 92f), 140f, 8f, game.Cpu)];

    /// <summary>タイトル・結果の文字を読めるよう、画面全体を暗くする。</summary>
    private UiShapeDraw[] Dim(float alpha) => [new(new Vector2(width * 0.5f, height * 0.5f), new Vector2(width, height), new Vector4(0f, 0f, 0f, alpha))];

    /// <summary>試合中の上下の帯（名前・ダメージと、技・守りの字幕の下地）。</summary>
    private UiShapeDraw[] Bars() =>
    [
        new(new Vector2(width * 0.5f, 45f), new Vector2(width, 90f), new Vector4(0f, 0f, 0f, 0.55f), FillColorEnd: new Vector4(0f, 0f, 0f, 0f), GradientAxis: Vector2.UnitY),
        new(new Vector2(width * 0.5f, height - 70f), new Vector2(width, 140f), new Vector4(0f, 0f, 0f, 0f), FillColorEnd: new Vector4(0f, 0f, 0f, 0.6f), GradientAxis: Vector2.UnitY),
    ];

    /// <summary>
    /// 2 人の中点を、リングの横から少し見下ろす。<paramref name="orbit"/> [rad] でゆっくり回り込む（横 ±35°）。
    /// </summary>
    private (Matrix4x4 ViewProjection, Vector3 Eye) CameraAt(BoxingGame game, float orbit)
    {
        var mid = (game.Player.ChestPosition + game.Cpu.ChestPosition) * 0.5f;
        var axis = game.Cpu.ChestPosition - game.Player.ChestPosition;
        axis.Y = 0f;
        // 赤（プレイヤー側）が画面の左、青が右に映る側から見る（字幕の左右と合わせる）。
        var side = axis.LengthSquared() > 1e-6f ? Vector3.Normalize(Vector3.Cross(axis, Vector3.UnitY)) : -Vector3.UnitX;
        var angle = 0.6f * MathF.Sin(orbit);
        var direction = Vector3.Transform(side, Quaternion.CreateFromAxisAngle(Vector3.UnitY, angle));
        var eye = mid + (direction * 0.9f) + new Vector3(0f, 0.38f, 0f); // 手前のロープ越しに見下ろす高さ
        var target = mid - new Vector3(0f, 0.06f, 0f);
        return (Camera.ViewProjection(eye, target, Vector3.UnitY, 45f * MathF.PI / 180f, (float)width / height, 0.02f, 30f), eye);
    }

    private IReadOnlyList<ScreenTextDraw> Title(string red, string blue, float fade)
    {
        var w = width;
        var h = height;
        return
        [
            new("MINI RING", new Vector2(w * 0.5f, h * 0.24f), 84f, Fade(White, fade), TextAnchor.Center),
            new("ボット対ボット", new Vector2(w * 0.5f, h * 0.38f), 40f, Fade(Grey, fade), TextAnchor.Center),
            new($"{red}  (赤)", new Vector2(w * 0.5f - 40f, h * 0.62f), 48f, Fade(Red, fade), TextAnchor.Right),
            new("vs", new Vector2(w * 0.5f, h * 0.62f), 36f, Fade(Grey, fade), TextAnchor.Center),
            new($"(青)  {blue}", new Vector2(w * 0.5f + 40f, h * 0.62f), 48f, Fade(Blue, fade), TextAnchor.Left),
            new("技・守り・打撃音はすべてボットと物理が決めたもの", new Vector2(w * 0.5f, h * 0.84f), 30f, Fade(Grey, fade), TextAnchor.Center),
        ];
    }

    private List<ScreenTextDraw> MatchOverlay(string red, string blue, Caption redCaption, Caption blueCaption,
        List<(string Text, Vector4 Color, float Until)> flashes, float redDamage, float blueDamage, float time, float? seconds, BoxingGame game, Matrix4x4 viewProjection)
    {
        var w = width;
        var h = height;
        var list = new List<ScreenTextDraw>
        {
            new(red, new Vector2(28f, 22f), 30f, Red),
            new(blue, new Vector2(w - 28f, 22f), 30f, Blue, TextAnchor.Right),
            new(string.Create(CultureInfo.InvariantCulture, $"DAMAGE {redDamage:0}"), new Vector2(28f, 60f), 22f, Red),
            new(string.Create(CultureInfo.InvariantCulture, $"DAMAGE {blueDamage:0}"), new Vector2(w - 28f, 60f), 22f, Blue, TextAnchor.Right),
            new(seconds is { } limit ? string.Create(CultureInfo.InvariantCulture, $"{MathF.Max(0f, limit - time):0}") : string.Create(CultureInfo.InvariantCulture, $"{(int)time / 60}:{(int)time % 60:00}"), new Vector2(w * 0.5f, 22f), 34f, White, TextAnchor.Center),
        };
        list.AddRange(DownCountHud.Text(game, viewProjection, new Vector2(w, h), f => f == game.Player ? red : blue, f => f == game.Player ? Red : Blue));
        if (game.Rules is not null)
        {
            // テレビ中継: 上の名前・ダメージ・時間の代わりに、得点表と時計。
            list.RemoveRange(0, 5);
            list.InsertRange(0, TvHud.Text(game, new Vector2(w, h), red, blue));
        }

        AddCaption(list, redCaption, new Vector2(28f, h - 96f), TextAnchor.Left, Red, time);
        AddCaption(list, blueCaption, new Vector2(w - 28f, h - 96f), TextAnchor.Right, Blue, time);
        for (var i = 0; i < flashes.Count; i++)
        {
            var (text, color, until) = flashes[i];
            var age = 0.7f - (until - time);
            list.Add(new ScreenTextDraw(text, new Vector2(w * 0.5f, (h * 0.22f) + (i * 44f) - (age * 30f)), 40f, Fade(color, MathF.Min(1f, (until - time) / 0.3f)), TextAnchor.Center));
        }

        return list;
    }

    private IReadOnlyList<ScreenTextDraw> Result(string red, string blue, float redDamage, float blueDamage, int winner, float fade, string? finish = null)
    {
        var w = width;
        var h = height;
        var verdict = winner == 0 ? "DRAW" : $"{(winner > 0 ? red : blue)} WINS";
        return
        [
            // K.O. の種類（"K.O. — BRAIN DESTROYED" / "10 COUNT K.O." / "RING OUT"。勝者の部分は上の大きな文字が言うので落とす）。
            .. finish is { Length: > 0 }
                ? new ScreenTextDraw[] { new(finish.Split("  ")[0], new Vector2(w * 0.5f, h * 0.14f), 40f, Fade(Gold, fade), TextAnchor.Center) }
                : [],
            new(verdict, new Vector2(w * 0.5f, h * 0.26f), 72f, Fade(winner > 0 ? Red : winner < 0 ? Blue : Gold, fade), TextAnchor.Center),
            new(string.Create(CultureInfo.InvariantCulture, $"{red}  {redDamage:0}"), new Vector2(w * 0.5f - 30f, h * 0.45f), 40f, Fade(Red, fade), TextAnchor.Right),
            new(string.Create(CultureInfo.InvariantCulture, $"{blueDamage:0}  {blue}"), new Vector2(w * 0.5f + 30f, h * 0.45f), 40f, Fade(Blue, fade), TextAnchor.Left),
            new("与えたダメージ", new Vector2(w * 0.5f, h * 0.55f), 30f, Fade(Grey, fade), TextAnchor.Center),
            new("ボット同士を戦わせて PDCA で改善", new Vector2(w * 0.5f, h * 0.82f), 30f, Fade(Grey, fade), TextAnchor.Center),
        ];
    }

    /// <summary>コーナーの字幕: 新しく始めたコンビネーション（カウンターを含む）と、いま出している守り。</summary>
    private static void UpdateCaption(Caption caption, List<IFighterControl> bots, Dictionary<string, int> seen, float time)
    {
        if (bots.Count == 0 || bots[^1] is not BoxerBot bot)
        {
            return;
        }

        foreach (var (name, count) in bot.Stats.Combos)
        {
            var key = $"{bots.Count}:{name}";
            if (seen.GetValueOrDefault(key) < count)
            {
                seen[key] = count;
                caption.Combo = ComboLabel(name);
                caption.ComboTime = time;
            }
        }

        if (bot.CurrentDefense != Defense.None)
        {
            caption.Defense = DefenseLabel(bot.CurrentDefense);
            caption.DefenseTime = time;
        }
    }

    private static void AddCaption(List<ScreenTextDraw> list, Caption caption, Vector2 at, TextAnchor anchor, Vector4 color, float time)
    {
        if (caption.Combo is { } combo && time - caption.ComboTime < 1.1f)
        {
            list.Add(new ScreenTextDraw(combo, at, 34f, Fade(color, 1f - MathF.Max(0f, (time - caption.ComboTime - 0.8f) / 0.3f)), anchor));
        }

        if (caption.Defense is { } defense && time - caption.DefenseTime < 0.6f)
        {
            list.Add(new ScreenTextDraw(defense, at + new Vector2(0f, 46f), 30f, Fade(Gold, 1f - MathF.Max(0f, (time - caption.DefenseTime - 0.3f) / 0.3f)), anchor));
        }
    }

    private static string ComboLabel(string name) => name switch
    {
        "slip-counter" => "スリップからカウンター",
        "duck-counter" => "ダッキングからカウンター",
        "pull-counter" => "スウェーからプルカウンター",
        "block-counter" => "ブロックからカウンター",
        "weave-counter" => "ウィービングからカウンター",
        "angle-counter" => "サイドステップからカウンター",
        "step-counter" => "バックステップからカウンター",
        "stop-jab" => "ストップジャブ",
        // 名簿の型なら技を並べる。知らない名前（ボットが後から足した型）は名前のまま出す。
        _ => Combo.All.FirstOrDefault(c => c.Name == name) is { } combo
            ? $"{name}  " + string.Join(" → ", combo.Steps.Select(TechniqueLabel))
            : name,
    };

    private static string TechniqueLabel(Technique t) => t switch
    {
        Technique.Jab => "ジャブ",
        Technique.Cross => "ストレート",
        Technique.LeadHook => "左フック",
        Technique.RearHook => "右フック",
        Technique.LeadUppercut => "左アッパー",
        Technique.RearUppercut => "右アッパー",
        Technique.BodyJab => "ボディジャブ",
        Technique.BodyCross => "ボディストレート",
        Technique.LeadBodyHook => "左ボディフック",
        Technique.RearBodyHook => "右ボディフック",
        Technique.Overhand => "オーバーハンド",
        Technique.Feint => "フェイント",
        _ => t.ToString(),
    };

    private static string DefenseLabel(Defense d) => d switch
    {
        Defense.Block => "ブロック",
        Defense.Duck => "ダッキング",
        Defense.Slip => "スリップ",
        Defense.Sway => "スウェー",
        Defense.StepBack => "バックステップ",
        Defense.Weave => "ウィービング",
        Defense.SideStep => "サイドステップ",
        _ => "",
    };

    private static Vector4 Fade(Vector4 color, float alpha) => color with { W = Math.Clamp(alpha, 0f, 1f) };

    /// <summary>16-bit モノラル PCM を <paramref name="at"/> 秒の位置に足し込む。</summary>
    private static void Mix(float[] audio, byte[] pcm, float at, float gain)
    {
        var start = (int)(at * HitSound.SampleRate);
        for (var i = 0; i + 1 < pcm.Length && start + (i / 2) < audio.Length; i += 2)
        {
            audio[start + (i / 2)] += BitConverter.ToInt16(pcm, i) / 32768f * gain;
        }
    }

    private static void WriteWav(string path, float[] audio, float seconds)
    {
        var count = Math.Min(audio.Length, (int)(seconds * HitSound.SampleRate));
        using var writer = new BinaryWriter(File.Create(path));
        writer.Write("RIFF"u8);
        writer.Write(36 + (count * 2));
        writer.Write("WAVEfmt "u8);
        writer.Write(16);
        writer.Write((short)1);
        writer.Write((short)1);
        writer.Write(HitSound.SampleRate);
        writer.Write(HitSound.SampleRate * 2);
        writer.Write((short)2);
        writer.Write((short)16);
        writer.Write("data"u8);
        writer.Write(count * 2);
        for (var i = 0; i < count; i++)
        {
            writer.Write((short)(MathF.Tanh(audio[i]) * 32000f)); // 重なった打撃音は柔らかくクリップ
        }
    }

    private Process StartFfmpeg(string videoPath)
    {
        var info = new ProcessStartInfo("ffmpeg",
            $"-y -loglevel error -f rawvideo -pix_fmt rgba -s {width}x{height} -r {fps} -i - -c:v libx264 -pix_fmt yuv420p -crf 18 -preset medium \"{videoPath}\"")
        {
            RedirectStandardInput = true,
            UseShellExecute = false,
        };
        return Process.Start(info) ?? throw new IOException("ffmpeg を起動できません（PATH にありますか）。");
    }

    private static void Run(string file, string arguments)
    {
        using var process = Process.Start(new ProcessStartInfo(file, arguments) { UseShellExecute = false })
            ?? throw new IOException($"{file} を起動できません。");
        process.WaitForExit();
        if (process.ExitCode != 0)
        {
            throw new IOException($"{file} failed with exit code {process.ExitCode}.");
        }
    }

    private static IFighterControl Keep(List<IFighterControl> list, IFighterControl control)
    {
        list.Add(control);
        return control;
    }

    private sealed class Caption
    {
        public string? Combo { get; set; }
        public float ComboTime { get; set; }
        public string? Defense { get; set; }
        public float DefenseTime { get; set; }

        /// <summary>最後に実況の資料へ書いた守り（続いている間は 1 回だけ書く）。</summary>
        public string? LastNotedDefense { get; set; }
    }
}
