using System.Numerics;
using Aigis.Foundation;
using Aigis.Render;
using MiniRing.Arena;
using MiniRing.Arena.Shows.Wvc;
using MiniRing.Characters;

namespace MiniRing.Tests;

/// <summary>WVC だけの番組の部品を PNG に描く（<c>.visualcheck/wvcparts_*.png</c>）。PNG があること ≠ 正しいので、人が中身を見る。</summary>
[TestClass]
[TestCategory("visual")]
public sealed class WvcShowPartsVisualTests
{
    private const int W = 1280;
    private const int H = 720;

    private static string OutDir()
    {
        var dir = Path.Combine(OrganizationTests.RepoRoot(), ".visualcheck");
        Directory.CreateDirectory(dir);
        return dir;
    }

    private static void Write(HeadlessRenderer renderer, string name, Vector4 back, IReadOnlyList<ScreenTextDraw> text, IReadOnlyList<UiShapeDraw> shapes)
        => PngWriter.WriteRgba(Path.Combine(OutDir(), name), W, H, renderer.RenderPrimitiveScene(back, Matrix4x4.Identity, [], [], text, shapes: shapes));

    [TestMethod]
    public void Parts_RenderOnGrey()
    {
        using var renderer = new HeadlessRenderer(W, H);
        var back = new Vector4(0.2f, 0.19f, 0.2f, 1f);
        var red = TvCorner.Of(CharacterRoster.Get("marcus"));
        var blue = TvCorner.Of(CharacterRoster.Get("yoandry"));

        foreach (var (name, corner, side, t) in new[] { ("call_red", red, 1, 5f), ("call_blue", blue, -1, 5f), ("call_red_early", red, 1, 1.5f) })
        {
            var (text, shapes) = WvcShowParts.RingAnnouncerCall(corner, side, t, W, H);
            Write(renderer, $"wvcparts_{name}.png", back, text, shapes);
        }

        WvcRoundTally[] rounds = [new(120.4f, 90f, 14, 11), new(80f, 150.6f, 9, 17), new(210f, 60f, 22, 8)];
        var (ct, cs) = WvcShowParts.RoundScoreCard(rounds, red.Name, blue.Name, 4f, W, H);
        Write(renderer, "wvcparts_roundcard.png", back, ct, cs);

        var (bt, bs) = WvcShowParts.ChampionBelt(true, 1.5f, new Vector2(W * 0.5f, H * 0.5f), 10f);
        Write(renderer, "wvcparts_belt.png", back, bt, bs);

        // 勝敗の画面に重ねる（今の WVC の画面と並べて見る）。
        var g = new TvShowGraphics(W, H);
        var card = new TvCard("ファイトナイト", "第1試合", red, blue, TvShowRules.Rules(5, 25f));
        var ko = new TvResult(1, "K.O. — BRAIN DESTROYED  RED WINS", 4, 18f, [100f, 120f, 90f, 80f], [90f, 100f, 110f, 70f]);
        var (rt, rs) = g.Result(card, ko, 5f);
        var beltCell = 4f;
        var (bt2, bs2) = WvcShowParts.ChampionBelt(true, 3.2f, new Vector2(W * 0.5f, 0.5f * H - 64f - 40f), beltCell);
        Write(renderer, "wvcparts_result_with_belt.png", back, [.. rt, .. bt2], [.. rs, .. bs2]);
        Assert.IsTrue(bs.Count > 0 && cs.Count > 0);
    }
}
