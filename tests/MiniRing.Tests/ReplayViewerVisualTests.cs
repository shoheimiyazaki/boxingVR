using System.Numerics;
using Aigis.Foundation;
using Aigis.Render;
using MiniRing.Bots;
using MiniRing.Desktop;
using MiniRing.Presentation;
using MiniRing.Replay;

namespace MiniRing.Tests;

/// <summary>
/// リプレイの画面を GPU で描いて PNG にする（目で見る: CLAUDE.md §4.7）。下のパネル（展開の見えるシークのバー）、操作の一覧、
/// FPS のカメラ、真上の定位置、印の上のホバー、場面の間の補間。出力: <c>.visualcheck/replay_*.png</c>。
/// </summary>
[TestClass]
[TestCategory("slow")]
[TestCategory("visual")]
public sealed class ReplayViewerVisualTests
{
    private const int Width = 1280;
    private const int Height = 720;

    [TestMethod]
    public void ReplayViewer_RendersPanelHelpAndCameras()
    {
        var outDir = Path.Combine(FindRepoRoot(), ".visualcheck");
        Directory.CreateDirectory(outDir);
        var corners = BotCatalog.Corners("swarmer") with { RedBotStyle = BotCatalog.Style("champion") };
        ReplayData data;
        using (var game = new BoxingGame(corners: corners.StartingAtCorners(), seed: 3, rules: MatchRules.Tv))
        using (var stream = new MemoryStream())
        {
            using (var recorder = new ReplayRecorder(stream, game, "test"))
            {
                for (var i = 0; i < 240 * 45; i++) { recorder.Step(game, default, 1f / 240f); }
            }

            data = ReplayFile.Read(new MemoryStream(stream.ToArray()));
        }

        using var viewer = new ReplayViewerGame(data, faceCam: null) { HelpRows = [("Space / K", "再生 / 一時停止"), ("Tab", "カメラ: 回転中心 ⇔ FPS"), ("[ / ]", "前 / 次の見どころへ")] };
        var waited = 0;
        while (!viewer.Scanner.Completed && viewer.Scanner.Fault is null && waited++ < 6000) { Thread.Sleep(20); }
        Assert.IsTrue(viewer.Scanner.Completed, "走査が終わる");
        Assert.IsNull(viewer.Scanner.FirstDivergence);
        using var renderer = new HeadlessRenderer(Width, Height);
        var camera = new ReplayCamera();
        var screen = new Vector2(Width, Height);

        void Shot(string name)
        {
            camera.Update(viewer.Focus, 1f); // 追う点へ寄せきる
            viewer.CameraLabel = camera.Label;
            var vp = camera.ViewProjection((float)Width / Height);
            var (instances, lines) = viewer.BuildRenderables();
            var text = viewer.BuildOverlayText(vp, screen);
            var shapes = viewer.BuildOverlayShapes(vp, screen);
            var rgba = renderer.RenderPrimitiveScene(ArenaLook.Background, vp, instances, lines, text, particles: viewer.BuildParticles(),
                lighting: viewer.BuildLighting(), shadow: viewer.BuildShadow(), cameraPosition: camera.Eye, shapes: shapes,
                postProcess: viewer.BuildPostProcess(), voxels: viewer.BuildVoxelVolumes());
            PngWriter.WriteRgba(Path.Combine(outDir, $"replay_{name}.png"), Width, Height, rgba);
        }

        var t = viewer.Transport;
        var (_, marks, _) = viewer.Timeline.Snapshot();
        var big = marks.Where(m => m.Kind == ReplayMarkKind.Highlight).OrderByDescending(ReplayTimeline.Weighted).FirstOrDefault();
        t.Seek(big.Kind == ReplayMarkKind.Highlight ? big.Time : 20f, viewer.Scanner.ScannedTime);
        t.Pause();
        viewer.BuildRenderables();
        camera.Reset(viewer.Focus);
        Shot("panel");

        // バーの上の、いちばん大きな見どころの印にマウスを置く。
        var layout = ReplayHud.Measure(screen);
        viewer.Hover = new Vector2(layout.X(big.Time, t.Duration), layout.MarkTop + 15f);
        Shot("hover");
        viewer.Hover = null;

        viewer.ShowHelp = true;
        Shot("help");
        viewer.ShowHelp = false;

        camera.SetMode(ReplayCameraMode.Free);
        camera.Move(new Vector3(0f, 0f, 1f), 0.25f, 1f);
        camera.Rotate(new Vector2(0f, -40f));
        Shot("fps");

        camera.Preset(1, viewer.Focus);
        Shot("top");

        // 2 枚の場面のちょうど間（補間）をスローで。
        camera.Preset(2, viewer.Focus);
        t.SetSpeed(0.25f);
        t.Seek(t.Time + (viewer.Scanner.Frames.Interval * 0.5f), viewer.Scanner.ScannedTime);
        Shot("between_frames");
        Assert.IsTrue(File.Exists(Path.Combine(outDir, "replay_panel.png")));
    }

    private static string FindRepoRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "MiniRing.slnx")))
        {
            dir = dir.Parent;
        }

        return dir?.FullName ?? throw new InvalidOperationException("MiniRing.slnx が見つからない");
    }
}
