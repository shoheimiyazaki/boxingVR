using Aigis.Render;
using MiniRing.Arena.Shows.Wvc;

namespace MiniRing.Arena;

/// <summary>試合の前の場面の背景（試合の 3D の絵）。カメラの置き方は <see cref="VideoMaker"/> が決める。</summary>
internal enum ShowBackdrop
{
    /// <summary>部屋ごとリングを引きで見下ろし、ゆっくり回り込む（WVC の冒頭）。</summary>
    Wide,

    /// <summary>檻の外をゆっくり回る。</summary>
    CageOrbit,

    /// <summary>赤コーナーの選手を低い所（床の高さ）から映す。</summary>
    RedLow,

    /// <summary>青コーナーの選手を低い所から映す。</summary>
    BlueLow,

    /// <summary>画面いっぱいの 2 つの顔の窓（左が赤、右が青）。</summary>
    FaceOff,

    /// <summary>3D の絵を使わず、場面が画面を全部塗る（背景は暗い）。</summary>
    Flat,
}

/// <summary>試合の前の番組の 1 場面（団体の番組の型が決める並びの 1 つ）。</summary>
/// <param name="Id">場面の id（番組の型の中で描き分ける）。</param>
/// <param name="EventType">中継の記録（*.events.json）に書く出来事の種類。<c>prefight: true</c> を付けて書く。</param>
/// <param name="Seconds">場面の長さ [s]。</param>
/// <param name="Description">記録に書く画面の説明（場面の名前: 中身）。台本を書く側が「いまの画面」を知る材料。</param>
/// <param name="Backdrop">背景の絵。</param>
/// <param name="StingAt">場面の中でホーンを鳴らす時刻 [s]（鳴らさないなら null）。</param>
/// <param name="Who">場面の主役の呼び名（入場の選手。記録の <c>who</c>）。無ければ空。</param>
/// <param name="Stage">場面の間、影の試合（<see cref="BoxingGame.StepStage"/>）で両者が取る立ち方。生の絵（<see cref="Backdrop"/> が <see cref="ShowBackdrop.Flat"/> 以外）の場面だけが使う。</param>
internal sealed record ShowScene(string Id, string EventType, float Seconds, string Description, ShowBackdrop Backdrop = ShowBackdrop.Wide, float? StingAt = null, string Who = "", StagePose Stage = StagePose.Center);

/// <summary>画面に出す文字と図形。</summary>
internal readonly record struct ShowDraw(IReadOnlyList<ScreenTextDraw> Text, IReadOnlyList<UiShapeDraw> Shapes)
{
    public void Deconstruct(out IReadOnlyList<ScreenTextDraw> text, out IReadOnlyList<UiShapeDraw> shapes)
    {
        text = Text;
        shapes = Shapes;
    }

    public static ShowDraw Of((IReadOnlyList<ScreenTextDraw> Text, IReadOnlyList<UiShapeDraw> Shapes) pair) => new(pair.Text, pair.Shapes);

    public static ShowDraw Empty { get; } = new([], []);
}

/// <summary>
/// 団体ごとの「番組の型」: 試合の前の場面の並び・試合中の画面・休み・リプレイの出入り・決着の画面を、団体のファイルの
/// <c>show</c>（<see cref="ShowFormats"/> の id）で差し替える。団体名の分岐は書かず、型ごとのクラスが描く（CLAUDE.md §1.3 と同じ考え方）。
/// 描画と音だけで、試合には響かない。出す体の状態は見えている欠損だけ（<see cref="ShowBodies"/>、CLAUDE.md §1.7）。
/// </summary>
internal interface ITvShow
{
    /// <summary>型の id（団体のファイルの <c>show</c>）。</summary>
    public string Id { get; }

    /// <summary>試合の前の場面の並び（順に流れる）。<paramref name="hasProfiles"/> は両者が名簿のキャラクターでプロフィールを持つか。</summary>
    public IReadOnlyList<ShowScene> PreFight(TvCard card, TvProgram? program, bool hasProfiles);

    /// <summary>場面 <paramref name="scene"/> の <paramref name="t"/> 秒目（場面の頭から）の画面。</summary>
    public ShowDraw Scene(ShowScene scene, TvCard card, TvProgram? program, float t);

    /// <summary>顔の窓の置き場（大きさと位置）をこの番組の型の画面に合わせる。</summary>
    public FaceCam Arrange(FaceCam cam, int width, int height);

    /// <summary>フェイスオフ（<see cref="ShowBackdrop.FaceOff"/>）の顔の窓の置き場。その場面の無い型は <paramref name="cam"/> のまま。</summary>
    public FaceCam ArrangeFaceOff(FaceCam cam, int width, int height);

    /// <summary>字幕（コンビネーション・守りの名前）を置く高さ [px]。</summary>
    public float CaptionY(int height);

    /// <summary>試合中の画面（時計・ラウンド・名札・スタミナ・体の図など）。顔の窓の位置は <see cref="Arrange"/> が決める。</summary>
    public ShowDraw MatchHud(BoxingGame game, RenderViewport redPanel, RenderViewport bluePanel, string red, string blue, ShowBodies bodies);

    /// <summary>休みの看板（札）を出す長さ [s]（休みの頭から。看板の無い型は 0）。</summary>
    public float IntervalBoardSeconds { get; }

    /// <summary>
    /// 休み（ラウンドの間）の画面。<paramref name="breakTime"/> は休みの頭からの秒数、<paramref name="rounds"/> は終わったラウンドごとの集計
    /// （与えたダメージ・当てた数。採点の札が使う）。何も足さない型は空。
    /// </summary>
    public ShowDraw Interval(BoxingGame game, string red, string blue, ShowBodies bodies, float breakTime, IReadOnlyList<WvcRoundTally> rounds);

    /// <summary>休みの看板（札）の、中継の記録に書く画面の説明（台本を書く側が「いまの画面」を知る材料）。看板の無い型は空。</summary>
    public string IntervalNote(BoxingGame game, string red, string blue, ShowBodies bodies, IReadOnlyList<WvcRoundTally> rounds);

    /// <summary>リプレイ中の画面。</summary>
    public ShowDraw ReplayOverlay(int round, string description, float videoTime);

    /// <summary>リプレイへ入る・出るワイプ（<paramref name="progress"/> 0..1）。</summary>
    public IReadOnlyList<UiShapeDraw> Wipe(float progress);

    /// <summary>決着の画面の長さ [s]。</summary>
    public float ResultSeconds { get; }

    /// <summary>決着の画面。<paramref name="game"/> は決着後の試合（体の最後の状態を出す型が読む）。</summary>
    public ShowDraw Result(TvCard card, TvResult result, float t, BoxingGame game, ShowBodies bodies);
}

/// <summary>番組の型の一覧と作り方。</summary>
internal static class ShowFormats
{
    private static readonly IReadOnlyDictionary<string, Func<int, int, Organization, ITvShow>> Makers = new Dictionary<string, Func<int, int, Organization, ITvShow>>(StringComparer.Ordinal)
    {
        ["wvc"] = (w, h, org) => new WvcShow(w, h, org),
        ["vfc"] = (w, h, org) => new VfcShow(w, h, org),
    };

    /// <summary>型ごとの試合の前の場面の出来事の種類（団体のファイルの <c>showSkip</c> が名指しできるもの）。</summary>
    private static readonly Dictionary<string, IReadOnlyList<string>> Scenes = new Dictionary<string, IReadOnlyList<string>>(StringComparer.Ordinal)
    {
        ["wvc"] = ["program", "opening", "tape", "call"],
        ["vfc"] = ["fight_card", "cold_open", "poster", "walkout", "faceoff"],
    };

    /// <summary>型 <paramref name="showId"/> の試合の前の場面の出来事の種類。</summary>
    public static IReadOnlyList<string> SceneTypes(string showId) => Scenes.TryGetValue(showId, out var types) ? types : [];

    /// <summary><paramref name="types"/> のうち、型 <paramref name="showId"/> に無い場面の種類。</summary>
    public static IReadOnlyList<string> UnknownScenes(string showId, IEnumerable<string> types) => [.. types.Where(t => !SceneTypes(showId).Contains(t, StringComparer.Ordinal))];

    /// <summary>試合の前の場面の並びから、団体が流さない場面（<see cref="Organization.SkippedScenes"/>）を除く。</summary>
    public static IReadOnlyList<ShowScene> PreFight(ITvShow show, Organization org, TvCard card, TvProgram? program, bool hasProfiles)
    {
        ArgumentNullException.ThrowIfNull(show);
        ArgumentNullException.ThrowIfNull(org);
        return [.. show.PreFight(card, program, hasProfiles).Where(s => !org.SkippedScenes.Contains(s.EventType, StringComparer.Ordinal))];
    }

    /// <summary>型の id の一覧。</summary>
    public static IReadOnlyList<string> Ids { get; } = [.. Makers.Keys.Order(StringComparer.Ordinal)];

    /// <summary>団体のファイルの <c>show</c> から番組の型を作る。知らない id は例外。</summary>
    public static ITvShow Create(int width, int height, Organization org)
    {
        ArgumentNullException.ThrowIfNull(org);
        return Makers.TryGetValue(org.Show, out var make)
            ? make(width, height, org)
            : throw new ArgumentException($"番組の型 '{org.Show}' はありません（{string.Join(", ", Ids)}）。", nameof(org));
    }
}
