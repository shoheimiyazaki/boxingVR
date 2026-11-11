using System.Diagnostics;
using System.Globalization;
using System.Numerics;
using Aigis.Render;
using MiniRing.Weapons;

namespace MiniRing.Arena;

/// <summary>
/// 武器の振り（棍棒の振り・剣の技）を mp4 にする: 振りごとに題名の画面と、2 人を横から撮った振り（<c>slow</c> 倍のスロー）。
/// 画面の隅に武器の先の速さと、相手に溜まった脳しんとう・相手の体から壊したボクセルの数を出す。
/// </summary>
internal sealed class SwingVideo(int width, int height, int fps, float slow)
{
    /// <summary>
    /// 撮る 1 振り: 見出し・題名・題名の下の説明と、振りを試す関数（物理の 1 歩ごとに呼ぶ関数を受け取り、ログに出す結果の 1 行を返す）。
    /// </summary>
    internal sealed record Shot(string Heading, string Title, string Subtitle, Func<Action<BoxingGame, float, bool>, string> Run);

    /// <summary>棍棒の振り（<see cref="SwingTest.Run"/>）を撮る 1 振り。</summary>
    internal static Shot Club(string title, WeaponSwing swing, float range) => new("棍棒の振り", title,
        string.Create(CultureInfo.InvariantCulture, $"手首のタメ {swing.WristDelay:0.00}・ひねり {swing.Twist * 180f / MathF.PI:0}°・踏み込み {swing.Step:0.00}・間合い {range * 100f:0}cm"),
        afterStep =>
        {
            var r = SwingTest.Run(swing, range, afterStep);
            return string.Create(CultureInfo.InvariantCulture, $"power {r.Power:0.000} tip {r.TipSpeed:0.00}");
        });

    /// <summary>剣の技（<see cref="SwordDrill.Run"/>）を撮る 1 振り。</summary>
    internal static Shot Sword(string title, SwordTechnique technique) => new("剣の技", title,
        string.Create(CultureInfo.InvariantCulture, $"{SwordTechnique.MaaiName(technique.Maai)} {technique.Range * 100f:0}cm・刃筋 {technique.Swing.Roll * 180f / MathF.PI:0}°・手首のタメ {technique.Swing.WristDelay:0.00}・踏み込み {technique.Swing.Step:0.00}"),
        afterStep =>
        {
            var r = SwordDrill.Run(technique, afterStep);
            return string.Create(CultureInfo.InvariantCulture, $"effect {r.Effect:0.00} target {r.TargetCells:0} severed {r.Severed:0} killed {r.Killed:0} tip {r.TipSpeed:0.00}");
        });

    public void Make(IReadOnlyList<(string Title, WeaponSwing Swing, float Range)> swings, string outPath, TextWriter log) =>
        Make([.. swings.Select(s => Club(s.Title, s.Swing, s.Range))], outPath, log);

    private const float TitleSeconds = 1.5f;
    private static readonly Vector4 White = new(1f, 1f, 1f, 1f);
    private static readonly Vector4 Grey = new(0.75f, 0.78f, 0.85f, 1f);
    private static readonly Vector4 Gold = new(1f, 0.85f, 0.3f, 1f);

    public void Make(IReadOnlyList<Shot> shots, string outPath, TextWriter log)
    {
        var watch = Stopwatch.StartNew();
        using var renderer = new HeadlessRenderer((uint)width, (uint)height);
        using var ffmpeg = Process.Start(new ProcessStartInfo("ffmpeg",
            $"-y -loglevel error -f rawvideo -pix_fmt rgba -s {width}x{height} -r {fps} -i - -c:v libx264 -pix_fmt yuv420p -crf 18 -preset medium \"{outPath}\"")
        {
            RedirectStandardInput = true,
            UseShellExecute = false,
        }) ?? throw new IOException("ffmpeg を起動できません（PATH にありますか）。");
        var instances = new List<PrimitiveInstance>();
        var lines = new List<DebugLine>();
        var simPerFrame = 1f / (fps * slow);
        foreach (var shot in shots)
        {
            var nextFrame = 0f;
            var titled = false;
            var peak = 0f;
            var cellsAtStart = -1;
            var summary = shot.Run((game, t, swinging) =>
            {
                var weapon = game.WeaponOf(game.Player)!;
                var s = game.World.GetState(weapon.Body.BodyId);
                var tip = s.Position + Vector3.Transform(Weapon.TipOf(weapon.Kind), s.Orientation);
                var torso = game.World.GetState(game.Player.Parts.Chest);
                var speed = (s.LinearVelocity + Vector3.Cross(s.AngularVelocity, tip - s.Position) - torso.LinearVelocity).Length();
                var cells = game.Cpu.Voxels!.Parts.Concat(game.Cpu.Voxels.Fragments).Sum(p => p.Body.Grid.FilledCount);
                if (cellsAtStart < 0)
                {
                    cellsAtStart = cells;
                }

                if (!titled)
                {
                    titled = true;
                    for (var k = 0f; k < TitleSeconds; k += 1f / fps)
                    {
                        Emit(game, [
                            new(shot.Heading, new Vector2(width * 0.5f, height * 0.3f), 34f, Grey, TextAnchor.Center),
                            new(shot.Title, new Vector2(width * 0.5f, height * 0.46f), 56f, White, TextAnchor.Center),
                            new(shot.Subtitle, new Vector2(width * 0.5f, height * 0.6f), 28f, Gold, TextAnchor.Center),
                        ], 0.55f);
                    }
                }

                peak = MathF.Max(peak, speed);
                // 振っている間はスロー、間合いを詰める歩きはふつうの速さで撮る。
                if (t >= nextFrame)
                {
                    var lost = cellsAtStart - game.Cpu.Voxels!.Parts.Concat(game.Cpu.Voxels.Fragments).Sum(p => p.Body.Grid.FilledCount);
                    Emit(game, [
                        new(shot.Title, new Vector2(40f, 40f), 36f, White, TextAnchor.Left),
                        new(string.Create(CultureInfo.InvariantCulture, $"武器の先 {speed:0.0} m/s（最高 {peak:0.0}）"), new Vector2(40f, 86f), 26f, Gold, TextAnchor.Left),
                        new(string.Create(CultureInfo.InvariantCulture, $"相手の脳しんとう {game.Cpu.Concussion:0.000}・相手から失われたボクセル {lost}{(game.Cpu.IsDead ? "（死亡）" : "")}"), new Vector2(40f, 120f), 26f, Gold, TextAnchor.Left),
                        new(string.Create(CultureInfo.InvariantCulture, $"{t:0.00} s  （{slow:0} 倍スロー）"), new Vector2(width - 40f, height - 40f), 26f, Grey, TextAnchor.Right),
                    ], 0f);
                    nextFrame = t + (swinging ? simPerFrame : 1f / fps);
                }
            });
            log.WriteLine(string.Create(CultureInfo.InvariantCulture, $"{shot.Title}: {summary} ({watch.Elapsed.TotalSeconds:0}s)"));
        }

        ffmpeg.StandardInput.Close();
        ffmpeg.WaitForExit();
        if (ffmpeg.ExitCode != 0)
        {
            throw new IOException($"ffmpeg failed with exit code {ffmpeg.ExitCode}.");
        }

        void Emit(BoxingGame game, IReadOnlyList<ScreenTextDraw> text, float dim)
        {
            BoxingRenderer.Fill(game, showTargetGhosts: false, instances, lines);
            var mid = (game.Player.ChestPosition + game.Cpu.ChestPosition) * 0.5f;
            var axis = game.Cpu.ChestPosition - game.Player.ChestPosition;
            axis.Y = 0f;
            var side = axis.LengthSquared() > 1e-6f ? Vector3.Normalize(Vector3.Cross(axis, Vector3.UnitY)) : -Vector3.UnitX;
            var eye = mid + (side * 0.75f) + new Vector3(0f, 0.12f, 0f);
            var vp = Camera.ViewProjection(eye, mid + new Vector3(0f, 0.05f, 0f), Vector3.UnitY, 45f * MathF.PI / 180f, (float)width / height, 0.02f, 30f);
            UiShapeDraw[] shapes = dim > 0f ? [new UiShapeDraw(new Vector2(width * 0.5f, height * 0.5f), new Vector2(width, height), new Vector4(0f, 0f, 0f, dim))] : [];
            var rgba = renderer.RenderPrimitiveScene(ArenaLook.Background, vp, instances, lines, text, particles: game.BuildParticles(), lighting: game.BuildLighting(),
                shadow: game.BuildShadow(), cameraPosition: eye, shapes: shapes, postProcess: game.BuildPostProcess(), voxels: game.BuildVoxelVolumes());
            ffmpeg.StandardInput.BaseStream.Write(rgba);
        }
    }
}
