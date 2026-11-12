using System.Diagnostics;
using System.Globalization;
using System.Numerics;
using Aigis.Render;
using MiniRing.Weapons;

namespace MiniRing.Arena;

/// <summary>
/// 剣同士の 1 試合を mp4 にする（2 人を横から、ふつうの速さ）。画面の隅に、それぞれが今出している技と最後に出した技、残っているボクセルの割合、
/// 胸と胸の間合い。
/// </summary>
internal sealed class SparVideo(int width, int height, int fps)
{
    private static readonly Vector4 White = new(1f, 1f, 1f, 1f);
    private static readonly Vector4 Red = new(1f, 0.55f, 0.5f, 1f);
    private static readonly Vector4 Blue = new(0.55f, 0.7f, 1f, 1f);
    private static readonly Vector4 Grey = new(0.75f, 0.78f, 0.85f, 1f);

    /// <summary>技でない、剣士の今の様子（「最後の技」には残さない）。</summary>
    private static readonly HashSet<string> States = ["受け", "外し", "詰める", "下がる"];

    /// <summary>片手の剣の流儀同士の稽古（<see cref="SwordSparring"/>）。</summary>
    public void Make(SwordStyle style, int seed, float seconds, string outPath, TextWriter log) =>
        Render("剣同士の稽古（片手の剣）", step =>
        {
            var r = SwordSparring.Run(style, style, seed, seconds, (game, t, red, blue) => step(game, t, red?.Current?.Title, blue?.Current?.Title));
            return string.Create(CultureInfo.InvariantCulture, $"red dealt {r.DealtByRed:0}, blue dealt {r.DealtByBlue:0}, died {r.RedDied}/{r.BlueDied}");
        }, outPath, log);

    /// <summary>両手の剣士同士の 1 試合（<see cref="SwordLibraryLab.Bout"/>）。</summary>
    public void MakeBout(SwordLibraryLab.Library library, WeaponKind red, WeaponKind blue, int seed, float seconds, string title, string outPath, TextWriter log) =>
        Render(title, step =>
        {
            var r = SwordLibraryLab.Bout(library, red, blue, seed, seconds, (game, t, redBot, blueBot) => step(game, t, Now(redBot, game.Player), Now(blueBot, game.Cpu)));
            return string.Create(CultureInfo.InvariantCulture, $"mean distance {r.MeanDistance:0.00} m, red lost {r.RedLost}, blue lost {r.BlueLost}, died {r.RedDied}/{r.BlueDied}");
        }, outPath, log);

    /// <summary>練習試合の 1 試合（<see cref="VersusLab"/> の戦い手同士）。画面には剣士の技・受け・外し、武器を落としたら「拳」。</summary>
    public void MakeVersus(Bots.Contender red, Bots.Contender blue, int seed, float seconds, string outPath, TextWriter log) =>
        Render($"練習試合（赤 {red.Name} 対 青 {blue.Name}）", step =>
        {
            Bots.DisarmedFallback? redBot = null, blueBot = null;
            IFighterControl Make(Bots.Contender c, int s, Action<Bots.DisarmedFallback> keep)
            {
                var control = c.Create(s);
                if (c.Weapon is null)
                {
                    return control;
                }

                var f = Bots.DisarmedFallback.WithFists(control, s);
                keep(f);
                return f;
            }

            using var game = new BoxingGame(VersusLab.Tuning, seed, new BoxingCorners(blue.Name, s => Make(blue, s, f => blueBot = f), s => Make(red, s, f => redBot = f),
                CpuWeapon: blue.Weapon, PlayerWeapon: red.Weapon, CpuTwoHanded: blue.TwoHanded, PlayerTwoHanded: red.TwoHanded));
            static string? Label(Bots.DisarmedFallback? f, Fighter fighter) =>
                f is null ? null : f.Disarmed ? "拳（武器を落とした）" : Now(f.Armed as Bots.SwordsmanBot, fighter);
            for (var t = 0f; t < seconds; t += 1f / 60f)
            {
                game.Step(default, 1f / 60f);
                step(game, t, Label(redBot, game.Player), Label(blueBot, game.Cpu));
            }

            return string.Create(CultureInfo.InvariantCulture, $"red wins {game.PlayerWins}, blue wins {game.CpuWins}");
        }, outPath, log);

    /// <summary>台本つきの場面（<see cref="BreakDemo"/>）を続けて撮る。画面の青の行に、その場面で今起きていること。</summary>
    public void MakeBreakDemo(IReadOnlyList<BreakDemo.Scene> scenes, float seconds, string outPath, TextWriter log) =>
        Render("骨が断たれる・折れる・武器が折れる", step =>
        {
            var offset = 0f;
            foreach (var scene in scenes)
            {
                using var game = scene.Create();
                var done = false;
                for (var t = 0f; t < seconds; t += 1f / 60f)
                {
                    if (!done && t >= scene.At)
                    {
                        scene.Act(game);
                        done = true;
                    }

                    game.Step(default, 1f / 60f);
                    step(game, offset + t, scene.Title, scene.Describe(game, done));
                }

                offset += seconds;
            }

            return $"{scenes.Count} scenes";
        }, outPath, log, plainLabels: true);

    /// <summary>守りのドリル（<see cref="GuardLab"/>）の何本かを続けて撮る。画面には打ち手と、受け手の今の守り（受け・外し）。</summary>
    public void MakeGuard(SwordLibraryLab.Library library, IReadOnlyList<GuardLab.Attack> attacks, GuardLab.Variant variant, int seed, float seconds, string outPath, TextWriter log) =>
        Render($"守りのドリル（青: 日本刀・{variant.Name}）", step =>
        {
            var summary = new List<string>();
            var offset = 0f;
            foreach (var attack in attacks)
            {
                var r = GuardLab.Run(attack, library, variant, seed, seconds, (game, t, bot) => step(game, offset + t, attack.Name, bot.Blocking ? "受け" : bot.Defending ? "外し" : null));
                offset += seconds;
                summary.Add(string.Create(CultureInfo.InvariantCulture, $"{attack.Name}: lost {r.Cells} (self {r.SelfCells}), clash steps {r.ClashSteps}"));
            }

            return string.Join("; ", summary);
        }, outPath, log);

    private static string? Now(Bots.SwordsmanBot? bot, Fighter fighter) =>
        bot is null ? null : !fighter.Wields(BoxingGame.WeaponHand) ? "拳（武器を落とした）" : bot.Blocking ? "受け" : bot.Defending ? "外し"
            : bot.Current is { } now ? Short(now.Name) : bot.Attacking ? "詰める" : bot.Retreating ? "下がる" : null;

    /// <summary>技の名前の、切り出した所を除いた前半（「小手 / BasicKendo@43.17」→「小手」）。</summary>
    private static string? Short(string? name) => name?.Split(" / ")[0];

    /// <summary>1 本の動画を撮る: <paramref name="run"/> が試合を進め、歩ごとに渡された関数を呼ぶ（フレームの間隔ごとに 1 枚描く）。</summary>
    /// <param name="title">画面の上の見出し。</param>
    /// <param name="run">試合を進める（引数の関数を歩ごとに: 試合・時刻・赤の説明・青の説明）。最後に記録へ残す要約を返す。</param>
    /// <param name="outPath">書き出す mp4。</param>
    /// <param name="log">要約を書く先。</param>
    /// <param name="plainLabels">赤・青の行に、渡した説明だけを書く（技の名前と「最後の技」を出さない。台本つきの場面）。</param>
    private void Render(string title, Func<Action<BoxingGame, float, string?, string?>, string> run, string outPath, TextWriter log, bool plainLabels = false)
    {
        using var renderer = new HeadlessRenderer((uint)width, (uint)height);
        using var ffmpeg = Process.Start(new ProcessStartInfo("ffmpeg",
            $"-y -loglevel error -f rawvideo -pix_fmt rgba -s {width}x{height} -r {fps} -i - -c:v libx264 -pix_fmt yuv420p -crf 18 -preset medium \"{outPath}\"")
        {
            RedirectStandardInput = true,
            UseShellExecute = false,
        }) ?? throw new IOException("ffmpeg を起動できません（PATH にありますか）。");
        var instances = new List<PrimitiveInstance>();
        var lines = new List<DebugLine>();
        var nextFrame = 0f;
        int? redStart = null, blueStart = null;
        string redLast = "—", blueLast = "—";
        var summary = run((game, t, redNow, blueNow) =>
        {
            if (t < nextFrame)
            {
                return;
            }

            nextFrame = t + (1f / fps);
            var redCells = game.Player.Voxels!.Parts.Sum(p => p.Body.Grid.FilledCount);
            var blueCells = game.Cpu.Voxels!.Parts.Sum(p => p.Body.Grid.FilledCount);
            redStart ??= redCells;
            blueStart ??= blueCells;
            redLast = redNow is { } r && !States.Contains(r) ? r : redLast;
            blueLast = blueNow is { } b && !States.Contains(b) ? b : blueLast;
            BoxingRenderer.Fill(game, showTargetGhosts: false, instances, lines);
            var mid = (game.Player.ChestPosition + game.Cpu.ChestPosition) * 0.5f;
            var axis = game.Cpu.ChestPosition - game.Player.ChestPosition;
            axis.Y = 0f;
            var distance = axis.Length();
            var side = axis.LengthSquared() > 1e-6f ? Vector3.Normalize(Vector3.Cross(axis, Vector3.UnitY)) : -Vector3.UnitX;
            var eye = mid + (side * MathF.Max(0.85f, 1.6f * distance)) + new Vector3(0f, 0.15f, 0f);
            var vp = Camera.ViewProjection(eye, mid + new Vector3(0f, 0.05f, 0f), Vector3.UnitY, 45f * MathF.PI / 180f, (float)width / height, 0.02f, 30f);
            ScreenTextDraw[] text =
            [
                new(title, new Vector2(40f, 40f), 34f, White, TextAnchor.Left),
                new(plainLabels ? redNow ?? "" : string.Create(CultureInfo.InvariantCulture, $"赤: {redNow ?? "構え"}（最後の技 {redLast}）  体 {100f * redCells / redStart.Value:0}%{(game.Player.IsDead ? " 死亡" : "")}"), new Vector2(40f, 84f), 26f, plainLabels ? White : Red, TextAnchor.Left),
                new(plainLabels ? $"青: {blueNow}" : string.Create(CultureInfo.InvariantCulture, $"青: {blueNow ?? "構え"}（最後の技 {blueLast}）  体 {100f * blueCells / blueStart.Value:0}%{(game.Cpu.IsDead ? " 死亡" : "")}"), new Vector2(40f, 118f), 26f, Blue, TextAnchor.Left),
                new(string.Create(CultureInfo.InvariantCulture, $"間合い（胸と胸） {distance * 100f:0} cm"), new Vector2(40f, 152f), 26f, Grey, TextAnchor.Left),
                new(string.Create(CultureInfo.InvariantCulture, $"{t:0.0} s"), new Vector2(width - 40f, height - 40f), 26f, Grey, TextAnchor.Right),
            ];
            var rgba = renderer.RenderPrimitiveScene(ArenaLook.Background, vp, instances, lines, text, particles: game.BuildParticles(), lighting: game.BuildLighting(),
                shadow: game.BuildShadow(), cameraPosition: eye, postProcess: game.BuildPostProcess(), voxels: game.BuildVoxelVolumes());
            ffmpeg.StandardInput.BaseStream.Write(rgba);
        });
        ffmpeg.StandardInput.Close();
        ffmpeg.WaitForExit();
        if (ffmpeg.ExitCode != 0)
        {
            throw new IOException($"ffmpeg failed with exit code {ffmpeg.ExitCode}.");
        }

        log.WriteLine($"{title}: {summary}");
    }
}
