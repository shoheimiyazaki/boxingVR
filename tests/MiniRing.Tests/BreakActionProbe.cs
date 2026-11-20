using System.Numerics;
using Aigis.Foundation;
using Aigis.Render;
using MiniRing.Bots;
using MiniRing.Break;

namespace MiniRing.Tests;

/// <summary>
/// 調査用: 休みの動作ごとに、動作の途中のコマを 2 つの向き（前斜め・真横）から撮って PNG にする（環境変数 BREAK_PROBE_DIR、
/// 動作を選ぶなら BREAK_PROBE_ONLY=drink,call、見る選手は BREAK_PROBE_BLUE=1 で青）。目で見て、動作が動作に見えるかを確かめる（docs/BREAK_ROUTINE.md）。
/// </summary>
[TestClass]
[TestCategory("Probe")]
public sealed class BreakActionProbe
{
    private const int Width = 640;
    private const int Height = 480;
    private const float Dt = 1f / 60f;

    public TestContext TestContext { get; set; } = null!;

    [TestMethod]
    public void RenderEachAction()
    {
        var dir = Environment.GetEnvironmentVariable("BREAK_PROBE_DIR");
        if (string.IsNullOrEmpty(dir))
        {
            Assert.Inconclusive("BREAK_PROBE_DIR を指定すると撮る");
            return;
        }

        Directory.CreateDirectory(dir);
        var blue = Environment.GetEnvironmentVariable("BREAK_PROBE_BLUE") == "1";
        var only = (Environment.GetEnvironmentVariable("BREAK_PROBE_ONLY") ?? string.Join(",", BreakBook.All(0.5f).Select(a => a.Name))).Split(',');

        foreach (var name in only)
        {
            using var renderer = new HeadlessRenderer(Width, Height);
            var rules = MatchRules.Tv with { Rounds = 2, RoundSeconds = 1.678f, BreakSeconds = 23.5f };
            var corners = new BoxingCorners("cpu", s => BotCatalog.Create("champion", s), s => BotCatalog.Create("swarmer", s),
                BreakSource: (seed, _) => new BreakRoutine(seed, null, [name]));
            using var game = new BoxingGame(seed: 3, corners: corners, rules: rules);
            var subject = blue ? game.Cpu : game.Player;
            var frame = 0;
            var planFrame = -1;
            var shots = new List<int>();
            var taken = 0;
            var sheet = new List<byte[]>();
            while (frame < 4028)
            {
                game.Step(default, Dt);
                frame++;
                if (!game.InBreak)
                {
                    if (planFrame >= 0)
                    {
                        break;
                    }

                    continue;
                }

                if (frame % 120 == 0)
                {
                    TestContext.WriteLine($"  f{frame} left={game.BreakLeft:F1} src={game.BreakSourceOf(subject)} state={subject.State} pos={subject.PelvisPosition}");
                }

                if (game.BreakSourceOf(subject) is BreakRoutine { Plan: { Count: > 0 } plan })
                {
                    if (planFrame < 0)
                    {
                        planFrame = frame;
                        var (start, action, _) = plan[0];
                        foreach (var f in new[] { 0.15f, 0.3f, 0.45f, 0.6f, 0.75f, 0.9f })
                        {
                            shots.Add(planFrame + (int)((start + (action.Seconds * f)) / Dt));
                        }
                    }

                    if (shots.Contains(frame))
                    {
                        taken++;
                        sheet.Add(Shoot(game, subject, renderer, Path.Combine(dir, $"{name.Replace(':', '_')}_{taken}"), Environment.GetEnvironmentVariable("BREAK_PROBE_VIEW") ?? "body"));
                        var hand = plan[0].Hand;
                        var q = game.World.GetState(subject.ForearmId(hand)).Orientation;
                        var fr = subject.Frame;
                        string Axis(Vector3 a) => fr.InverseRotate(Vector3.Transform(a, q)).ToString("F2", System.Globalization.CultureInfo.InvariantCulture);
                        TestContext.WriteLine($"  {name}#{taken} hand={hand} X={Axis(Vector3.UnitX)} Y={Axis(Vector3.UnitY)} Z={Axis(Vector3.UnitZ)} glove(head-rel)={fr.InverseRotate(subject.GloveCenter(hand) - subject.HeadPosition)}");
                    }
                }
            }

            WriteSheet(Path.Combine(dir, $"{name.Replace(':', '_')}_sheet.png"), sheet);
            TestContext.WriteLine($"{name}: plan frame {planFrame}, shots {taken}");
        }
    }

    /// <summary>6 コマを 3 列に並べた 1 枚の絵（動作の流れを一目で見るため）。</summary>
    private static void WriteSheet(string path, List<byte[]> frames)
    {
        if (frames.Count == 0)
        {
            return;
        }

        const int columns = 3;
        var rows = (frames.Count + columns - 1) / columns;
        var sheetWidth = columns * Width;
        var rgba = new byte[sheetWidth * rows * Height * 4];
        for (var i = 0; i < frames.Count; i++)
        {
            var x0 = (i % columns) * Width;
            var y0 = (i / columns) * Height;
            for (var row = 0; row < Height; row++)
            {
                Array.Copy(frames[i], row * Width * 4, rgba, (((y0 + row) * sheetWidth) + x0) * 4, Width * 4);
            }
        }

        PngWriter.WriteRgba(path, sheetWidth, rows * Height, rgba);
    }

    private static byte[] Shoot(BoxingGame game, Fighter subject, HeadlessRenderer renderer, string path, string sheetView)
    {
        byte[]? picked = null;
        var frame = subject.Frame;
        var head = subject.HeadPosition;
        var forward = frame.Forward;
        var right = frame.Right;
        var views = new[]
        {
            (head + (forward * 0.9577f) + (right * 0.4507f) + new Vector3(0f, 0.0563f, 0f), head + (forward * 0.0563f) - new Vector3(0f, 0.0986f, 0f), "front"),
            (head - (right * 1.07f) + (forward * 0.169f) + new Vector3(0f, 0.0563f, 0f), head + (forward * 0.1127f) - new Vector3(0f, 0.0986f, 0f), "side"),
            // 体ぜんたい（腕を挙げる・体を折る動作が見える距離）: 正面やや斜めから、頭の高さの 2.1 m 前（物理の空間）。
            (head + (forward * 2.113f) + (right * 0.338f) + new Vector3(0f, 0.1127f, 0f), head - new Vector3(0f, 0.338f, 0f), "body"),
        };
        foreach (var (eye, target, label) in views)
        {
            var (viewProjection, displayEye) = game.Display.Camera(eye, target, Vector3.UnitY, 55f * MathF.PI / 180f, (float)Width / Height, 0.02f, 30f);
            var instances = new List<Aigis.Render.PrimitiveInstance>();
            var lines = new List<Aigis.Render.DebugLine>();
            BoxingRenderer.Fill(game, false, instances, lines);
            var rgba = renderer.RenderPrimitiveScene(ArenaLook.Background, viewProjection, instances, lines, [], particles: game.BuildParticles(), lighting: game.BuildLighting(),
                shadow: game.BuildShadow(), cameraPosition: displayEye, postProcess: game.BuildPostProcess(), voxels: game.BuildVoxelVolumes());
            PngWriter.WriteRgba($"{path}_{label}.png", Width, Height, rgba);
            if (label == sheetView)
            {
                picked = rgba;
            }
        }

        return picked ?? [];
    }
}
