using Aigis.Render;

namespace MiniRing.Arena;

/// <summary>
/// WVC の番組の型: 格式のある地上波の中継（金・白・濃紺、中央揃えの板、本日のプログラムの表 → 冒頭のタイトル → 選手の比較の表 → 試合 →
/// 勝敗・与えたダメージの表）。描画は <see cref="TvShowGraphics"/>（今までの中継そのまま。出力は変えない）。
/// </summary>
internal sealed class WvcShow(int width, int height, Organization org) : ITvShow
{
    /// <summary>顔の窓の一辺（画面の高さに対する割合）。0.27 では大きすぎた（ユーザーの評価）ので 2/3 に。</summary>
    private const float FaceSize = 0.18f;

    /// <summary>顔の窓の下の名札・メーターの余白と、窓と余白のすき間 [px]。</summary>
    private const float FaceGap = 12f;

    private readonly TvShowGraphics _graphics = new(width, height, org);

    public string Id => "wvc";

    public float ResultSeconds => TvShowGraphics.ResultSeconds;

    public float IntervalBoardSeconds => 0f;

    public IReadOnlyList<ShowScene> PreFight(TvCard card, TvProgram? program, bool hasProfiles)
    {
        ArgumentNullException.ThrowIfNull(card);
        var scenes = new List<ShowScene>();
        if (program is not null)
        {
            scenes.Add(new ShowScene("program", "program", program.Seconds, $"本日のプログラムの表（{program.Seconds:0} 秒）: {program.Title}。"
                + string.Join("。", program.Bouts.Select(b => $"{b.Bout} {b.Red} 対 {b.Blue}{(b.WinnerName is { } wn ? $"（{wn}の勝ち {b.Method}）" : b.Current ? "（この試合）" : "")}"))));
        }

        scenes.Add(new ShowScene("opening", "opening", TvShowGraphics.OpeningSeconds,
            $"冒頭のタイトル（{TvShowGraphics.OpeningSeconds:0} 秒）: {card.EventTitle} {card.Bout}。赤 {card.Red.Name}、青 {card.Blue.Name} の紹介"));
        if (hasProfiles)
        {
            scenes.Add(new ShowScene("tape", "tape", TvShowGraphics.TapeSeconds,
                $"選手の比較の表（{TvShowGraphics.TapeSeconds:0} 秒）: 年齢・身長・体重・戦績・出身・拠点・構え。両者のプロフィールを紹介する時間"));
        }

        return scenes;
    }

    public ShowDraw Scene(ShowScene scene, TvCard card, TvProgram? program, float t)
    {
        ArgumentNullException.ThrowIfNull(scene);
        return scene.Id switch
        {
            "program" => ShowDraw.Of(_graphics.Program(program ?? throw new ArgumentNullException(nameof(program)), t)),
            "opening" => ShowDraw.Of(_graphics.Opening(card, t)),
            "tape" => ShowDraw.Of(_graphics.Tape(card, t)),
            _ => throw new ArgumentException($"WVC の番組に場面 '{scene.Id}' はありません。", nameof(scene)),
        };
    }

    public FaceCam Arrange(FaceCam cam, int width, int height)
    {
        ArgumentNullException.ThrowIfNull(cam);
        return cam with
        {
            SizeOfScreenHeight = FaceSize,
            CenterOfScreenHeight = (height - TvShowGraphics.CornerHudBelowPanel - FaceGap - (FaceSize * height * 0.5f)) / height,
        };
    }

    public FaceCam ArrangeFaceOff(FaceCam cam, int width, int height) => cam;

    public float CaptionY(int height) => height - 96f;

    public ShowDraw MatchHud(BoxingGame game, RenderViewport redPanel, RenderViewport bluePanel, string red, string blue, ShowBodies bodies)
        => ShowDraw.Of(_graphics.MatchHud(game, redPanel, bluePanel, red, blue));

    public ShowDraw Interval(BoxingGame game, string red, string blue, ShowBodies bodies, float breakTime) => ShowDraw.Empty;

    public ShowDraw ReplayOverlay(int round, string description, float videoTime) => ShowDraw.Of(_graphics.ReplayOverlay(round, description, videoTime));

    public IReadOnlyList<UiShapeDraw> Wipe(float progress) => _graphics.Wipe(progress);

    public ShowDraw Result(TvCard card, TvResult result, float t, BoxingGame game, ShowBodies bodies) => ShowDraw.Of(_graphics.Result(card, result, t));
}
