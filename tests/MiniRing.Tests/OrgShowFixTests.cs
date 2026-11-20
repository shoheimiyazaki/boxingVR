using System.Numerics;
using Aigis.Render;
using MiniRing.Arena;
using MiniRing.Arena.Shows.Wvc;
using MiniRing.Characters;

namespace MiniRing.Tests;

/// <summary>
/// 団体ごとの番組の仕上げ（作業 E）: 試合の前の場面の影の試合（T ポーズを映さない・本番を変えない）、VFC の窓が夜、
/// 決着の画面の体の図（死んだ側は沈み、頭が離れた図は頭が輪郭だけ）、WVC の王座のベルト・採点の札・コール。
/// </summary>
[TestClass]
[TestCategory("slow")]
public sealed class OrgShowFixTests
{
    private const int Width = 1280;
    private const int Height = 720;
    private const float Dt = 1f / 30f;

    private static TvCard WvcCard(bool belt = false)
        => new("WVC 王座決定戦", "第1試合", TvCorner.Of(CharacterRoster.Get("marcus")), TvCorner.Of(CharacterRoster.Get("ramon")), TvShowRules.Rules(3, 33.57f), belt);

    private static float GloveSpread(BoxingGame game) => MathF.Abs(game.Player.GloveCenter(Hand.Left).X - game.Player.GloveCenter(Hand.Right).X);

    [TestMethod]
    public void StageGame_StandsInGuard_AndDoesNotTouchTheRealMatch()
    {
        // 影の試合を回しながら本番を同じだけ進めた試合と、影の試合なしの試合で、本番のチェックサムが同じ。
        using var lone = new BoxingGame(seed: 7, rules: TvShowRules.Rules(3, 33.57f));
        using var accompanied = new BoxingGame(seed: 7, rules: TvShowRules.Rules(3, 33.57f));
        using var stage = new BoxingGame(seed: 7, rules: TvShowRules.Rules(3, 33.57f));
        var spawnSpread = GloveSpread(stage);
        for (var i = 0; i < 151; i++) // 約 5 秒
        {
            stage.StepStage(Dt, StagePose.Center);
            lone.Step(default, 1f / 60f);
            accompanied.Step(default, 1f / 60f);
        }

        Assert.AreEqual(lone.StateChecksum(), accompanied.StateChecksum(), "影の試合は本番の試合に響かない");
        Assert.AreNotEqual(stage.StateChecksum(), accompanied.StateChecksum(), "影の試合は本番とは別の状態（ボットを読まず構えて立つだけ）");

        // 生成直後は腕を真横に広げた姿勢。構えの指令で 5 秒進めると、拳は顔の前に寄っている（姿勢を書き込まず、目標をバネが追った結果）。
        Assert.IsGreaterThan(1.127f, spawnSpread, "生成直後は拳が体の横に離れている（T ポーズ）");
        Assert.IsLessThan(spawnSpread * 0.6f, GloveSpread(stage), "構えて立つと両拳が体の前へ寄る");
        Assert.IsGreaterThan(stage.Player.ChestPosition.Z, stage.Player.GloveCenter(Hand.Left).Z, "拳は相手の側（胸の前）にある");
    }

    [TestMethod]
    public void StageGame_IsDeterministic_AndWalksToTheCorners()
    {
        using var a = new BoxingGame(seed: 5, rules: TvShowRules.Rules(3, 33.57f));
        using var b = new BoxingGame(seed: 5, rules: TvShowRules.Rules(3, 33.57f));
        var corner = a.Layout.RestCorner(true, (a.Rules ?? MatchRules.Tv).CornerInset);
        corner.Y = 0f;
        float RedDistance(BoxingGame g)
        {
            var p = g.Player.PelvisPosition;
            return Vector2.Distance(new Vector2(p.X, p.Z), new Vector2(corner.X, corner.Z));
        }

        var before = RedDistance(a);
        for (var i = 0; i < 201; i++) // 約 6.7 秒
        {
            a.StepStage(Dt, StagePose.Corners);
            b.StepStage(Dt, StagePose.Corners);
        }

        Assert.AreEqual(a.StateChecksum(), b.StateChecksum(), "同じ種・同じ指令なら同じ（決定的）");
        Assert.IsLessThan(before * 0.5f, RedDistance(a), "赤は自分のコーナーへ歩いている");
    }

    [TestMethod]
    public void Window_IsNightWhenTheMoodDarkensIt_AndTheSameForTheDefaultRoom()
    {
        Assert.AreEqual(new Vector4(0.72f, 0.86f, 1f, 1f), LivingRoom.SkyColor(1f), "WVC の部屋（窓 1）は今までの昼の空");
        Assert.AreEqual(LivingRoom.SkyColor(1f), LivingRoom.SkyColor(3f), "1 を超えても昼の空のまま");
        var night = LivingRoom.SkyColor(Organization.Of("vfc").ArenaMood.Window);
        Assert.IsLessThan(0.2f, MathF.Max(night.X, MathF.Max(night.Y, night.Z)), $"VFC（窓 0.1）の窓の外は夜（暗い）: {night}");

        // 描画のデータにも出る: 窓の光る面（Emissive の箱）の色が団体で違う。
        using var vfc = new BoxingGame(seed: 1, mood: Organization.Of("vfc").ArenaMood);
        using var wvc = new BoxingGame(seed: 1);
        float Brightest(BoxingGame g)
        {
            var instances = new List<PrimitiveInstance>();
            BoxingRenderer.Fill(g, showTargetGhosts: false, instances, []);
            return instances.Where(i => i.Material == MaterialKind.Emissive && MathF.Abs(i.Model.Translation.Z - LivingRoom.WindowCenter.Z) < 0.1f && MathF.Abs(i.Model.Translation.Y - LivingRoom.WindowCenter.Y) < 0.01f)
                .Max(i => i.Color.X + i.Color.Y + i.Color.Z);
        }

        Assert.IsLessThan(Brightest(wvc) * 0.25f, Brightest(vfc), "VFC の窓の光る面は WVC より十分暗い");
    }

    [TestMethod]
    public void ResultFigure_SinksTheLoserOfADeath_AndTheSeveredHeadIsOutlineOnly()
    {
        using var game = new BoxingGame(seed: 3);
        var healthy = ShowBodies.None.Of(game.Cpu);
        Assert.IsFalse(healthy[FigurePart.Head].Gone);

        // 首を断たれて赤の勝ち: 負けた青の図は頭が「失った」で、全体が沈む。勝った赤は無傷のまま。
        var neck = new TvResult(1, "K.O. — NECK DESTROYED  RED WINS", 2, 26.85f, [], []);
        var (loser, loserSunk) = VfcShow.ResultFigure(healthy, loser: true, neck);
        Assert.IsTrue(loser[FigurePart.Head].Gone, "首を断たれた体の図は頭が失われている");
        Assert.IsTrue(loserSunk);
        Assert.IsTrue(loser.Wounds().Contains("首から先を失った"));
        var (winner, winnerSunk) = VfcShow.ResultFigure(healthy, loser: false, neck);
        Assert.IsFalse(winner[FigurePart.Head].Gone);
        Assert.IsFalse(winnerSunk);

        // 脳を壊されての死は、頭は残るが負けた側は沈む。判定・引き分けでは誰も沈まない。
        var brain = new TvResult(-1, "K.O. — BRAIN DESTROYED  BLUE WINS", 3, 8.392f, [], []);
        var (brainLoser, brainSunk) = VfcShow.ResultFigure(healthy, loser: true, brain);
        Assert.IsFalse(brainLoser[FigurePart.Head].Gone);
        Assert.IsTrue(brainSunk);
        Assert.IsFalse(VfcShow.ResultFigure(healthy, loser: true, new TvResult(0, "DRAW", 5, 50.35f, [], [])).Sunk);

        // 元の図は変えない（不変）。
        Assert.IsFalse(healthy[FigurePart.Head].Gone);
    }

    [TestMethod]
    public void Wvc_Belt_AppearsOnlyForABeltBoutWithAWinner_AndDoesNotCoverTheMethodWord()
    {
        var show = ShowFormats.Create(Width, Height, Organization.Of("wvc"));
        using var game = new BoxingGame(seed: 3);
        var win = new TvResult(1, "K.O. — BRAIN DESTROYED  RED WINS", 2, 26.85f, [100f], [90f]);
        var draw = new TvResult(0, "DRAW", 3, 50.35f, [100f], [100f]);
        var without = show.Result(WvcCard(), win, 4f, game, ShowBodies.None);
        var with = show.Result(WvcCard(belt: true), win, 4f, game, ShowBodies.None);
        Assert.IsGreaterThan(without.Shapes.Count + 100, with.Shapes.Count, "ベルト（金のボクセルの板）が足される");

        var drawWith = show.Result(WvcCard(belt: true), draw, 4f, game, ShowBodies.None);
        var drawWithout = show.Result(WvcCard(), draw, 4f, game, ShowBodies.None);
        CollectionAssert.AreEqual(drawWithout.Shapes.ToList(), drawWith.Shapes.ToList(), "引き分けではベルトを出さない");
        CollectionAssert.AreEqual(drawWithout.Text.ToList(), drawWith.Text.ToList());

        // 決まり手の大きな字（K.O.）の下端が、ベルトの上端（時刻 4 秒では降り切っている）より上。
        var method = with.Text.Where(t => t.Text == "K.O." && t.Color.W > 0.5f).OrderByDescending(t => t.PixelHeight).First();
        var beltTop = with.Shapes.Where(s => s.FillColor.X > 0.7f && s.FillColor.Y > 0.5f && s.FillColor.Z < 0.5f && s.CenterPixels.X > 300f && s.CenterPixels.X < 980f && s.CenterPixels.Y > 150f && s.CenterPixels.Y < 340f && s.SizePixels.X < 30f)
            .Min(s => s.CenterPixels.Y - (s.SizePixels.Y * 0.5f));
        Assert.IsLessThan(beltTop, method.ScreenPosPixels.Y + (method.PixelHeight * 1.05f), $"K.O. の字の下 {method.ScreenPosPixels.Y + (method.PixelHeight * 1.05f):0} がベルトの上 {beltTop:0} より上");
    }

    [TestMethod]
    public void Wvc_RoundCard_SitsAboveTheBlueFaceWindow_AndDescribesTheTally()
    {
        var show = ShowFormats.Create(Width, Height, Organization.Of("wvc"));
        var cam = show.Arrange(FaceCam.Default, Width, Height);
        var bluePanel = cam.Panel(new Vector2(Width, Height), FaceSide.Right);
        using var game = new BoxingGame(seed: 3);
        WvcRoundTally[] rounds = [new(112f, 80f, 14, 9), new(60f, 131f, 7, 16), new(88f, 40f, 11, 6)];
        var draw = show.Interval(game, "マーカス", "ラモン", ShowBodies.None, 4f, rounds);
        Assert.IsGreaterThan(0, draw.Shapes.Count);
        var bottom = draw.Shapes.Max(s => s.CenterPixels.Y + (s.SizePixels.Y * 0.5f));
        Assert.IsLessThanOrEqualTo(bluePanel.YPx, bottom, $"札の下端 {bottom:0} が右の顔の窓の上端 {bluePanel.YPx} より上");
        Assert.IsLessThanOrEqualTo(Width, draw.Shapes.Max(s => s.CenterPixels.X + (s.SizePixels.X * 0.5f)) - 1f, "画面の右にはみ出さない");
        Assert.AreEqual(0, show.Interval(game, "マーカス", "ラモン", ShowBodies.None, WvcShowParts.RoundCardSeconds + 1f, rounds).Shapes.Count, "7 秒を過ぎたら消える");

        var note = show.IntervalNote(game, "マーカス", "ラモン", ShowBodies.None, rounds);
        Assert.IsTrue(note.Contains("第3ラウンド", StringComparison.Ordinal) && note.Contains("88", StringComparison.Ordinal) && note.Contains("11発", StringComparison.Ordinal), note);
        Assert.IsTrue(note.Contains("260", StringComparison.Ordinal) && note.Contains("251", StringComparison.Ordinal), "ここまでの合計（赤 260、青 251）: " + note);
        Assert.AreEqual("", show.IntervalNote(game, "A", "B", ShowBodies.None, []));
    }
}
