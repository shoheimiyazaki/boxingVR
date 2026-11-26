namespace MiniRing.Desktop;

/// <summary>
/// 決めた順のメニューの操作（<c>--menu-script "down,enter,..."</c>、2026-11-26）: 実際のウィンドウでメニューを動かして画面を確かめるための道具。
/// 外からキーを送らず、ゲームの中で <see cref="MenuInput"/> を 1 つずつ渡す。<see cref="FramesPerStep"/> フレームに 1 つ進み、尽きたら手の入力に戻る。
/// 語: left・right・up・down・enter・back・wait・scroll-up・scroll-down（大文字・小文字は問わない）。
/// </summary>
internal sealed class MenuScript
{
    /// <summary>1 つの操作の間のフレーム数（60 fps で約 0.5 秒。画面を撮る間を取る）。</summary>
    public const int FramesPerStep = 30;

    private readonly Queue<MenuInput> _steps;
    private int _frame;

    private MenuScript(Queue<MenuInput> steps) => _steps = steps;

    /// <summary>語の並びから作る。知らない語は語を書いた例外。</summary>
    public static MenuScript Parse(string script)
    {
        ArgumentNullException.ThrowIfNull(script);
        var steps = new Queue<MenuInput>();
        foreach (var word in script.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            steps.Enqueue(word.ToLowerInvariant() switch
            {
                "left" => new MenuInput(X: -1),
                "right" => new MenuInput(X: 1),
                "up" => new MenuInput(Y: -1),
                "down" => new MenuInput(Y: 1),
                "enter" => new MenuInput(Confirm: true),
                "back" => new MenuInput(Cancel: true),
                "wait" => default,
                "scroll-up" => new MenuInput(Scroll: -1),
                "scroll-down" => new MenuInput(Scroll: 1),
                _ => throw new ArgumentException($"--menu-script の語 '{word}' は知りません（left, right, up, down, enter, back, wait, scroll-up, scroll-down）。", nameof(script)),
            });
        }

        return new MenuScript(steps);
    }

    /// <summary>残りの操作の数。</summary>
    public int Remaining => _steps.Count;

    /// <summary>このフレームの操作（間のフレーム・尽きた後は null = 手の入力を使う）。</summary>
    public MenuInput? Next()
    {
        if (_steps.Count == 0) { return null; }
        _frame++;
        if (_frame < FramesPerStep) { return default(MenuInput); }
        _frame = 0;
        return _steps.Dequeue();
    }
}
