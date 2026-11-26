namespace MiniRing.Desktop;

/// <summary>結果の画面のボタン。</summary>
internal enum ResultChoice
{
    /// <summary>キャンペーン: 本拠地へ（試合の後の札を見せる）。</summary>
    Home,

    /// <summary>フリーバトル: 同じ組み合わせでもう一度。</summary>
    Again,

    /// <summary>フリーバトル: キャラクターセレクトへ（ゲームモードはそのまま）。</summary>
    Select,

    /// <summary>フリーバトル: 最初の画面へ。</summary>
    Menu,
}

/// <summary>
/// 決着した試合の結果（画面に出す分だけ。試合の計算には入らない）。
/// </summary>
/// <param name="Red">赤の名前。</param>
/// <param name="Blue">青の名前。</param>
/// <param name="Winner">勝った側（0 = 赤、1 = 青、null = 引き分け）。</param>
/// <param name="Method">決まり手（"K.O. — OUT COLD"・"判定" など）。</param>
/// <param name="Round">決まったラウンド（ラウンドの無い試合は 0）。</param>
/// <param name="Clock">決まったときのラウンドの時刻（"1:23"。分からなければ空）。</param>
/// <param name="Campaign">キャンペーンの試合か（ボタンが「本拠地へ」だけになる）。</param>
internal sealed record BoutSummary(string Red, string Blue, int? Winner, string Method, int Round, string Clock, bool Campaign)
{
    /// <summary>この結果の画面のボタン（並びのまま）。</summary>
    public IReadOnlyList<ResultChoice> Choices => Campaign
        ? [ResultChoice.Home]
        : [ResultChoice.Again, ResultChoice.Select, ResultChoice.Menu];
}

/// <summary>結果の画面（<see cref="MenuPage.Result"/>）: 試合が決着した後に出し、ボタン（キー・パッド・クリック）で次へ進む。</summary>
internal sealed partial class MenuState
{
    private MenuPage _beforeResult = MenuPage.Top;

    /// <summary>今の結果（結果の画面の間だけ）。</summary>
    public BoutSummary? Result { get; private set; }

    /// <summary>結果の画面のカーソル（<see cref="BoutSummary.Choices"/> の添字）。</summary>
    public int ResultIndex { get; private set; }

    /// <summary>結果の画面を開く（試合の前のページを覚えておき、選んだボタンでそこへ戻る）。</summary>
    public void ShowResult(BoutSummary summary)
    {
        ArgumentNullException.ThrowIfNull(summary);
        _beforeResult = Page == MenuPage.Result ? _beforeResult : Page;
        Result = summary;
        ResultIndex = 0;
        Page = MenuPage.Result;
    }

    private MenuOutcome ApplyResult(MenuInput input)
    {
        if (Result is not { } result)
        {
            Page = MenuPage.Top;
            return new();
        }

        var choices = result.Choices;
        var old = ResultIndex;
        var confirm = input.Confirm;
        if (input.Pointer is { } point && MenuLayout.HitCenters(point, choices.Count, i => MenuLayout.ResultButtonCenter(i, choices.Count), MenuLayout.ResultButtonSize) is { } hit)
        {
            ResultIndex = hit;
            confirm |= input.Click;
        }

        ResultIndex = Wrap(ResultIndex + input.X + input.Y, choices.Count);
        if (input.Cancel)
        {
            // 戻る = いちばん控えめな行き先（キャンペーンは本拠地、フリーバトルは最初の画面）。
            return Choose(choices[^1]);
        }

        return confirm ? Choose(choices[ResultIndex]) : new(Cue: ResultIndex != old ? MenuCue.Move : MenuCue.None);
    }

    private MenuOutcome Choose(ResultChoice choice)
    {
        Result = null;
        RedLocked = false;
        BlueLocked = false;
        Page = choice switch
        {
            ResultChoice.Home => _beforeResult == MenuPage.Boxer && Boxer is not null ? MenuPage.Boxer : MenuPage.Top,
            ResultChoice.Select => MenuPage.Select,
            ResultChoice.Again => _beforeResult,
            _ => MenuPage.Top,
        };
        return new(choice == ResultChoice.Again ? MenuAction.Rematch : MenuAction.None, Cue: MenuCue.Confirm);
    }
}

