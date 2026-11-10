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
internal sealed class VideoMaker(int width, int height, int fps)
{
    private const float SimDt = 1f / 60f;
    private const float TitleSeconds = 3.5f;
    private const float ResultSeconds = 5f;

    /// <summary>字幕に出す「重い一撃」のダメージ（1 発でクリーンヒットと言える程度）。</summary>
    private const float BigHitDamage = 15f;

    private static readonly Vector4 Clear = new(0.07f, 0.08f, 0.11f, 1f);
    private static readonly Vector4 White = new(1f, 1f, 1f, 1f);
    private static readonly Vector4 Red = new(1f, 0.45f, 0.4f, 1f);
    private static readonly Vector4 Blue = new(0.5f, 0.68f, 1f, 1f);
    private static readonly Vector4 Gold = new(1f, 0.85f, 0.3f, 1f);
    private static readonly Vector4 Grey = new(0.75f, 0.78f, 0.85f, 1f);

    public void Make(Contender red, Contender blue, int seed, float seconds, string outPath, TextWriter log)
    {
        var redBots = new List<IFighterControl>();
        var blueBots = new List<IFighterControl>();
        var corners = new BoxingCorners(blue.Name, s => Keep(blueBots, blue.Create(s)), s => Keep(redBots, red.Create(s)));
        using var game = new BoxingGame(seed: seed, corners: corners);
        using var renderer = new HeadlessRenderer((uint)width, (uint)height);
        var audio = new float[(int)((TitleSeconds + seconds + ResultSeconds + 1f) * HitSound.SampleRate)];
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
        var watch = Stopwatch.StartNew();

        using (var ffmpeg = StartFfmpeg(videoPath))
        {
            var stdin = ffmpeg.StandardInput.BaseStream;
            void Emit(Matrix4x4 viewProjection, Vector3 eye, IReadOnlyList<ScreenTextDraw> text, IReadOnlyList<UiShapeDraw> shapes)
            {
                var rgba = renderer.RenderPrimitiveScene(Clear, viewProjection, instances, lines, text, cameraPosition: eye, shapes: shapes);
                stdin.Write(rgba);
                frame++;
            }

            // タイトル: 試合前の構えを背景に。
            BoxingRenderer.Fill(game, showTargetGhosts: false, instances, lines);
            for (var t = 0f; t < TitleSeconds; t += 1f / fps)
            {
                var (vp, eye) = CameraAt(game, t * 0.15f);
                Emit(vp, eye, Title(red.Name, blue.Name, MathF.Min(1f, t / 0.6f)), Dim(0.6f));
            }

            // 試合。
            var stepsPerFrame = Math.Max(1, (int)MathF.Round(1f / (fps * SimDt)));
            var time = 0f;
            for (var step = 0; time < seconds; step++)
            {
                game.Step(default, SimDt);
                time += SimDt;
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
                    Mix(audio, HitSound.Synthesize(tone, step), TitleSeconds + time, 0.55f + (0.45f * tone.Intensity));
                    if (hit.Damage >= BigHitDamage)
                    {
                        flashes.Add(($"{HitZones.Label(hit.Zone)}  {hit.Damage:0}", redHit ? Red : Blue, time + 0.7f));
                    }
                }

                UpdateCaption(redCaption, redBots, redCombos, time);
                UpdateCaption(blueCaption, blueBots, blueCombos, time);
                flashes.RemoveAll(f => f.Until < time);
                if (step % stepsPerFrame != 0)
                {
                    continue;
                }

                BoxingRenderer.Fill(game, showTargetGhosts: false, instances, lines);
                var (vp, eye) = CameraAt(game, (TitleSeconds * 0.15f) + (time * 0.15f));
                Emit(vp, eye, MatchOverlay(red.Name, blue.Name, redCaption, blueCaption, flashes, redDamage, blueDamage, time, seconds), Bars());
                if (frame % (fps * 5) == 0)
                {
                    log.WriteLine(string.Create(CultureInfo.InvariantCulture, $"  {time:0}s / {seconds:0}s ({watch.Elapsed.TotalSeconds:0}s elapsed)"));
                }
            }

            // 結果。
            var winner = MathF.Abs(redDamage - blueDamage) <= MathF.Max(1f, 0.05f * (redDamage + blueDamage) / 2f) ? 0 : MathF.Sign(redDamage - blueDamage);
            BoxingRenderer.Fill(game, showTargetGhosts: false, instances, lines);
            for (var t = 0f; t < ResultSeconds; t += 1f / fps)
            {
                var (vp, eye) = CameraAt(game, ((TitleSeconds + seconds) * 0.15f) + (t * 0.15f));
                Emit(vp, eye, Result(red.Name, blue.Name, redDamage, blueDamage, winner, MathF.Min(1f, t / 0.6f)), Dim(0.6f * MathF.Min(1f, t / 0.6f)));
            }

            stdin.Close();
            ffmpeg.WaitForExit();
            if (ffmpeg.ExitCode != 0)
            {
                throw new IOException($"ffmpeg (video) failed with exit code {ffmpeg.ExitCode}.");
            }
        }

        WriteWav(wavPath, audio, (float)frame / fps);
        Run("ffmpeg", $"-y -loglevel error -i \"{videoPath}\" -i \"{wavPath}\" -c:v copy -c:a aac -b:a 160k -shortest \"{outPath}\"");
        File.Delete(videoPath);
        File.Delete(wavPath);
        log.WriteLine(string.Create(CultureInfo.InvariantCulture, $"{outPath}: {frame} frames ({(float)frame / fps:0.0}s) in {watch.Elapsed.TotalSeconds:0}s"));
    }

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
        List<(string Text, Vector4 Color, float Until)> flashes, float redDamage, float blueDamage, float time, float seconds)
    {
        var w = width;
        var h = height;
        var list = new List<ScreenTextDraw>
        {
            new(red, new Vector2(28f, 22f), 30f, Red),
            new(blue, new Vector2(w - 28f, 22f), 30f, Blue, TextAnchor.Right),
            new(string.Create(CultureInfo.InvariantCulture, $"DAMAGE {redDamage:0}"), new Vector2(28f, 60f), 22f, Red),
            new(string.Create(CultureInfo.InvariantCulture, $"DAMAGE {blueDamage:0}"), new Vector2(w - 28f, 60f), 22f, Blue, TextAnchor.Right),
            new(string.Create(CultureInfo.InvariantCulture, $"{MathF.Max(0f, seconds - time):0}"), new Vector2(w * 0.5f, 22f), 34f, White, TextAnchor.Center),
        };
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

    private IReadOnlyList<ScreenTextDraw> Result(string red, string blue, float redDamage, float blueDamage, int winner, float fade)
    {
        var w = width;
        var h = height;
        var verdict = winner == 0 ? "DRAW" : $"{(winner > 0 ? red : blue)} WINS";
        return
        [
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
        _ => $"{name}  " + string.Join(" → ", Combo.Named(name).Steps.Select(TechniqueLabel)),
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
    }
}
