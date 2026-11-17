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
        using var renderer = new HeadlessRenderer(Width, Height);
        foreach (var name in only)
        {
            var rules = MatchRules.Tv with { Rounds = 2, RoundSeconds = 1f, BreakSeconds = 14f };
            var corners = new BoxingCorners("cpu", s => BotCatalog.Create("champion", s), s => BotCatalog.Create("swarmer", s),
                BreakSource: (seed, _) => new BreakRoutine(seed, null, [name]));
            using var game = new BoxingGame(seed: 3, corners: corners, rules: rules);
            var subject = blue ? game.Cpu : game.Player;
            var frame = 0;
            var planFrame = -1;
            var shots = new List<int>();
            var taken = 0;
            while (frame < 60 * 40)
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
                        Shoot(game, subject, renderer, Path.Combine(dir, $"{name.Replace(':', '_')}_{taken}"));
                        var hand = plan[0].Hand;
                        var q = game.World.GetState(subject.ForearmId(hand)).Orientation;
                        var fr = subject.Frame;
                        string Axis(Vector3 a) => fr.InverseRotate(Vector3.Transform(a, q)).ToString("F2", System.Globalization.CultureInfo.InvariantCulture);
                        TestContext.WriteLine($"  {name}#{taken} hand={hand} X={Axis(Vector3.UnitX)} Y={Axis(Vector3.UnitY)} Z={Axis(Vector3.UnitZ)} glove(head-rel)={fr.InverseRotate(subject.GloveCenter(hand) - subject.HeadPosition)}");
                    }
                }
            }

            TestContext.WriteLine($"{name}: plan frame {planFrame}, shots {taken}");
        }
    }

    private static void Shoot(BoxingGame game, Fighter subject, HeadlessRenderer renderer, string path)
    {
        var frame = subject.Frame;
        var head = subject.HeadPosition;
        var forward = frame.Forward;
        var right = frame.Right;
        var views = new[]
        {
            (head + (forward * 0.34f) + (right * 0.16f) + new Vector3(0f, 0.02f, 0f), head + (forward * 0.02f) - new Vector3(0f, 0.035f, 0f), "front"),
            (head - (right * 0.38f) + (forward * 0.06f) + new Vector3(0f, 0.02f, 0f), head + (forward * 0.04f) - new Vector3(0f, 0.035f, 0f), "side"),
        };
        foreach (var (eye, target, label) in views)
        {
            var viewProjection = Camera.ViewProjection(eye, target, Vector3.UnitY, 55f * MathF.PI / 180f, (float)Width / Height, 0.02f, 30f);
            var instances = new List<Aigis.Render.PrimitiveInstance>();
            var lines = new List<Aigis.Render.DebugLine>();
            BoxingRenderer.Fill(game, false, instances, lines);
            var rgba = renderer.RenderPrimitiveScene(ArenaLook.Background, viewProjection, instances, lines, [], particles: game.BuildParticles(), lighting: game.BuildLighting(),
                shadow: game.BuildShadow(), cameraPosition: eye, postProcess: game.BuildPostProcess(), voxels: game.BuildVoxelVolumes());
            PngWriter.WriteRgba($"{path}_{label}.png", Width, Height, rgba);
        }
    }
}
