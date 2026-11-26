using System.Numerics;
using Aigis.Foundation;
using Aigis.Render;
using MiniRing.Arena.Campaign;
using MiniRing.Bots;
using MiniRing.Desktop;
using BoutResult = MiniRing.Arena.Campaign.BoutResult;

namespace MiniRing.Tests;

/// <summary>
/// 試合の決着から次へ進む流れ（2026-11-26）を PNG に描く: 決着の瞬間（試合の画面＋「結果へ」の 1 行）・結果の画面（キャンペーン・フリーバトル）・本拠地に戻った札。
/// 出力: <c>.visualcheck/flow_*.png</c>。PNG があること ≠ 正しい。中身を見て確かめる。
/// </summary>
[TestClass]
[TestCategory("visual")]
public sealed class MatchFlowVisualTests
{
    private const int Width = 960;
    private const int Height = 540;

    private static string OutDir()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "MiniRing.slnx"))) { dir = dir.Parent; }
        var path = Path.Combine(dir?.FullName ?? AppContext.BaseDirectory, ".visualcheck");
        Directory.CreateDirectory(path);
        return path;
    }

    private static void RenderMenu(HeadlessRenderer renderer, MatchMenu menu, string name)
    {
        var size = new Vector2(Width, Height);
        var pixels = renderer.RenderPrimitiveScene(Vector4.Zero, Matrix4x4.Identity, [], [], menu.Text(size), shapes: menu.Shapes(size));
        PngWriter.WriteRgba(Path.Combine(OutDir(), name), Width, Height, pixels);
    }

    [TestMethod]
    public void FinishThenResultThenHome_Renders()
    {
        using var renderer = new HeadlessRenderer(Width, Height);

        // 1. 決着の瞬間: 中継の決まりで 1 ラウンドの試合を時計切れの判定まで回し、試合の画面に「結果へ」の 1 行を重ねる（MenuGame が Ready の段で足す物と同じ）。
        var still = BotCatalog.Boxer with { Attacks = false, DefenseRate = 0f, Prediction = 0f, Aggression = 0f };
        using (var game = new BoxingGame(seed: 1, corners: new BoxingCorners("still", s => new BoxerBot(still, s), s => new BoxerBot(still, s)),
            rules: MatchRules.Tv with { Rounds = 1, RoundSeconds = 1.678f, IntroSeconds = 0f, EndSeconds = 0f }))
        {
            for (var t = 0f; t < 4f && !game.MatchOver; t += 1f / 60f) { game.Step(default, 1f / 60f); }
            for (var i = 0; i < 60; i++) { game.Step(default, 1f / 60f); } // 決着から 1 秒
            Assert.IsTrue(MatchFinish.Settled(game));
            var size = new Vector2(Width, Height);
            var mid = (game.Player.ChestPosition + game.Cpu.ChestPosition) * 0.5f;
            var (vp, eye) = game.Display.Camera(mid + new Vector3(0f, 0.9f, 2.6f), mid, Vector3.UnitY, 50f * MathF.PI / 180f, (float)Width / Height, 0.05f, 60f);
            var instances = new List<PrimitiveInstance>();
            var lines = new List<DebugLine>();
            BoxingRenderer.Fill(game, false, instances, lines);
            ScreenTextDraw[] text = [.. game.BuildOverlayText(vp, size),
                UiFont.Text("Enter / A / クリック: 結果へ", new Vector2(size.X * 0.5f, size.Y - 48f), 26f, new Vector4(1f, 0.85f, 0.35f, 1f), TextAnchor.Center)];
            var pixels = renderer.RenderPrimitiveScene(ArenaLook.Background, vp, instances, lines, text, shapes: game.BuildOverlayShapes(vp, size),
                lighting: game.BuildLighting(), shadow: game.BuildShadow(), cameraPosition: eye, voxels: game.BuildVoxelVolumes());
            PngWriter.WriteRgba(Path.Combine(OutDir(), "flow_1_finish.png"), Width, Height, pixels);
        }

        var dir = Path.Combine(Path.GetTempPath(), $"miniring-flow-visual-{Guid.NewGuid():N}");
        var menu = new MatchMenu("none", Path.Combine(dir, "settings.json"), loadPortraits: false, campaignDirectory: Path.Combine(dir, "campaign"));
        try
        {
            var now = 5.0;
            menu.Clock = () => now;

            // 2. キャンペーン: 作る → 次の試合 → K.O. 勝ちの結果 → 本拠地の札。
            menu.Handle(new MenuInput(X: 1));
            menu.Handle(new MenuInput(Confirm: true));
            menu.Handle(new MenuInput(Confirm: true));
            menu.Handle(new MenuInput(Y: 3));
            menu.Handle(new MenuInput(Confirm: true));
            var (match, _) = menu.Handle(new MenuInput(Confirm: true));
            Assert.IsNotNull(match?.Campaign);
            menu.Finish(new BoutSummary(match.Red.HudName, match.Blue.HudName, 0, "K.O. — OUT COLD", 2, "1:07", Campaign: true),
                new BoutOutcome(BoutResult.Win, Finish: true));
            RenderMenu(renderer, menu, "flow_2_result_campaign.png");
            menu.Handle(new MenuInput(Confirm: true));
            Assert.AreEqual(MenuPage.Boxer, menu.State.Page);
            RenderMenu(renderer, menu, "flow_3_home_card.png");
            menu.Dispose();
            menu = new MatchMenu("none", Path.Combine(dir, "settings.json"), loadPortraits: false, campaignDirectory: Path.Combine(dir, "campaign"));

            // 3. フリーバトル: 判定負けの結果（3 つのボタン）。
            MatchSelection? free = null;
            for (var i = 0; i < 6 && free is null; i++) { free = menu.Handle(new MenuInput(Confirm: true)).Match; }
            Assert.IsNotNull(free);
            Assert.IsNull(free.Campaign);
            menu.Finish(new BoutSummary(free.Red.HudName, free.Blue.HudName, 1, "判定", 3, "3:00", Campaign: false), null);
            RenderMenu(renderer, menu, "flow_4_result_free.png");
        }
        finally
        {
            menu.Dispose();
            if (Directory.Exists(dir)) { Directory.Delete(dir, recursive: true); }
        }
    }
}
