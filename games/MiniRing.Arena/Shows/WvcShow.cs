using System.Globalization;
using System.Numerics;
using Aigis.Render;
using MiniRing.Arena.Shows.Wvc;

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

    /// <summary>配置を済ませた顔の窓（休みの採点の札を、青の窓の上に置くのに使う）。</summary>
    private FaceCam? _arranged;

    public string Id => "wvc";

    public float ResultSeconds => TvShowGraphics.ResultSeconds;

    public float IntervalBoardSeconds => WvcShowParts.RoundCardSeconds;

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

        // リングアナウンサーのコール（赤 → 青）: 下は生の絵（そのコーナーの選手を低い所から。両者はそれぞれ自分のコーナーで構えて立つ）。
        foreach (var (corner, side, id, backdrop) in new[] { (card.Red, 1, "call_red", ShowBackdrop.RedLow), (card.Blue, -1, "call_blue", ShowBackdrop.BlueLow) })
        {
            var label = side > 0 ? "赤" : "青";
            var facts = new List<string>();
            if (WvcShowParts.Title(corner) is { Length: > 0 } title)
            {
                facts.Add(title);
            }

            if (WvcShowParts.Record(corner) is { Length: > 0 } record)
            {
                facts.Add(record);
            }

            if (corner.Profile is { WeightKg: > 0 } p)
            {
                facts.Add(string.Create(CultureInfo.InvariantCulture, $"体重 {p.WeightKg} kg"));
            }

            facts.Add(corner.FullName.Length > 0 ? $"本名 {corner.FullName}、呼び名 {corner.Name}" : $"呼び名 {corner.Name}");
            scenes.Add(new ShowScene(id, "call", WvcShowParts.CallSeconds,
                $"リングアナウンサーが{label}コーナーの{corner.Name}をコール（{WvcShowParts.CallSeconds:0} 秒）: 画面の下の板に{string.Join("、", facts)}が順に出る。{corner.Name}はコーナーで構えて立って映る",
                backdrop, null, corner.Name, StagePose.Corners));
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
            "call_red" => ShowDraw.Of(WvcShowParts.RingAnnouncerCall(card.Red, 1, t, width, height, org)),
            "call_blue" => ShowDraw.Of(WvcShowParts.RingAnnouncerCall(card.Blue, -1, t, width, height, org)),
            _ => throw new ArgumentException($"WVC の番組に場面 '{scene.Id}' はありません。", nameof(scene)),
        };
    }

    public FaceCam Arrange(FaceCam cam, int width, int height)
    {
        ArgumentNullException.ThrowIfNull(cam);
        var arranged = cam with
        {
            SizeOfScreenHeight = FaceSize,
            CenterOfScreenHeight = (height - TvShowGraphics.CornerHudBelowPanel - FaceGap - (FaceSize * height * 0.5f)) / height,
        };
        _arranged = arranged;
        return arranged;
    }

    public FaceCam ArrangeFaceOff(FaceCam cam, int width, int height) => cam;

    public float CaptionY(int height) => height - 96f;

    public ShowDraw MatchHud(BoxingGame game, RenderViewport redPanel, RenderViewport bluePanel, string red, string blue, ShowBodies bodies)
        => ShowDraw.Of(_graphics.MatchHud(game, redPanel, bluePanel, red, blue));

    public ShowDraw Interval(BoxingGame game, string red, string blue, ShowBodies bodies, float breakTime, IReadOnlyList<WvcRoundTally> rounds)
    {
        ArgumentNullException.ThrowIfNull(rounds);
        // 採点の札: 右下。右の顔の窓の上（窓・窓の下の名札・字幕に重ならない）の右の縁にそろえる。
        var panel = (_arranged ?? Arrange(FaceCam.Default, width, height)).Panel(new Vector2(width, height), FaceSide.Right);
        var k = height / 720f;
        return ShowDraw.Of(WvcShowParts.RoundScoreCard(rounds, red, blue, breakTime, width, height, org, right: width - (28f * k), bottom: panel.YPx - (14f * k)));
    }

    public string IntervalNote(BoxingGame game, string red, string blue, ShowBodies bodies, IReadOnlyList<WvcRoundTally> rounds)
    {
        ArgumentNullException.ThrowIfNull(rounds);
        if (rounds.Count == 0)
        {
            return "";
        }

        var last = rounds[^1];
        var redTotal = rounds.Sum(r => r.RedDamage);
        var blueTotal = rounds.Sum(r => r.BlueDamage);
        return string.Create(CultureInfo.InvariantCulture,
            $"ラウンドの採点の札（{WvcShowParts.RoundCardSeconds:0} 秒、画面の右下）: 第{rounds.Count}ラウンドまでの与えたダメージの表。このラウンドは{red}が{last.RedDamage:0}（{last.RedHits}発）、{blue}が{last.BlueDamage:0}（{last.BlueHits}発）、"
            + $"ここまでの合計は{red}が{redTotal:0}、{blue}が{blueTotal:0}");
    }

    public ShowDraw ReplayOverlay(int round, string description, float videoTime) => ShowDraw.Of(_graphics.ReplayOverlay(round, description, videoTime));

    public IReadOnlyList<UiShapeDraw> Wipe(float progress) => _graphics.Wipe(progress);

    public ShowDraw Result(TvCard card, TvResult result, float t, BoxingGame game, ShowBodies bodies) => ShowDraw.Of(_graphics.Result(card, result, t));
}
