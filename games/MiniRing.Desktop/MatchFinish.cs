using System.Globalization;

namespace MiniRing.Desktop;

/// <summary>
/// 試合の決着を読む（新しい判定は作らない: <see cref="BoxingGame"/> の状態をそのまま読む）。画面の流れのためだけで、試合の計算には入らない（CLAUDE.md §1.6）。
/// <list type="bullet">
/// <item>決まりのある試合（団体・中継の決まり）は <see cref="BoxingGame.MatchOver"/>（K.O.・TKO・死・判定・引き分け）。</item>
/// <item>決まりの無い練習試合は、ラウンドが決まった瞬間（<see cref="BoxingGame.RoundOver"/>）。放っておけば次のラウンドへ作り直す試合なので、最初の決着を試合の決着とする。</item>
/// </list>
/// </summary>
internal static class MatchFinish
{
    /// <summary>試合が決着したか。</summary>
    public static bool Settled(BoxingGame game)
    {
        ArgumentNullException.ThrowIfNull(game);
        return game.Rules is null ? game.RoundOver : game.MatchOver;
    }

    /// <summary>決着した試合の結果（画面に出す分）。</summary>
    /// <param name="game">決着した試合。</param>
    /// <param name="campaign">キャンペーンの試合か（結果の画面のボタンが変わる）。</param>
    public static BoutSummary Summary(BoxingGame game, bool campaign)
    {
        ArgumentNullException.ThrowIfNull(game);
        int? winner = game.PlayerWins > game.CpuWins ? 0 : game.CpuWins > game.PlayerWins ? 1 : null;
        var method = game.Decision is { } decision
            ? winner is null ? $"引き分け（{decision}）" : "判定"
            : game.FinishReason ?? (winner is null ? "引き分け" : "K.O.");
        var (round, clock) = game.Rules is { } rules
            ? (game.MatchRound, Clock(rules.RoundSeconds - game.RoundLeft))
            : (0, string.Empty);
        return new BoutSummary(game.RedLabel, game.BlueLabel, winner, method, round, clock, campaign);
    }

    /// <summary>ラウンドの経過秒を 1:23 の形に（切り捨て）。</summary>
    internal static string Clock(float seconds)
    {
        var whole = (int)MathF.Floor(MathF.Max(0f, seconds));
        return string.Create(CultureInfo.InvariantCulture, $"{whole / 60}:{whole % 60:00}");
    }
}

/// <summary>決着の後の見せ方の段（<see cref="FinishGate"/>）。</summary>
internal enum FinishStage
{
    /// <summary>まだ決着していない。</summary>
    Playing,

    /// <summary>決着の瞬間を見せている（まだ進めない）。</summary>
    Showing,

    /// <summary>決着を見せている。Enter / クリックで結果へ進める。</summary>
    Ready,

    /// <summary>結果の画面へ進む。</summary>
    Advance,
}

/// <summary>
/// 決着の瞬間を数秒見せてから結果の画面へ進む門（純粋: 時刻と押したかを外から入れる）。
/// 決着の直後の <see cref="LockSeconds"/> は押しても進まない（打っていた手のクリック・Enter の勢いで決着を見逃さない）。
/// <see cref="AutoSeconds"/> たてば押さなくても進む。
/// </summary>
internal sealed class FinishGate
{
    /// <summary>決着の直後に押しを受けない時間 [s]: 決まり手の文字（"K.O. — OUT COLD" など）を読める長さ。</summary>
    public const double LockSeconds = 1.5;

    /// <summary>押さなくても結果へ進むまで [s]: 決まりの無い練習試合が次のラウンドへ作り直す 5.874 秒（<c>BoxingGame.RoundResetDelay</c>）より前。</summary>
    public const double AutoSeconds = 5.0;

    private double? _settledAt;

    /// <summary>決着した時刻（まだなら null）。</summary>
    public double? SettledAt => _settledAt;

    /// <summary>今の段を進める。</summary>
    /// <param name="settled">試合が決着しているか。</param>
    /// <param name="pressed">進める押し（Enter・A・クリック・Esc）があったか。</param>
    /// <param name="now">今の時刻 [s]。</param>
    public FinishStage Update(bool settled, bool pressed, double now)
    {
        if (_settledAt is null)
        {
            if (!settled) { return FinishStage.Playing; }
            _settledAt = now;
        }

        var held = now - _settledAt.Value;
        if (held >= AutoSeconds || (pressed && held >= LockSeconds)) { return FinishStage.Advance; }
        return held >= LockSeconds ? FinishStage.Ready : FinishStage.Showing;
    }

    /// <summary>次の試合のために戻す。</summary>
    public void Reset() => _settledAt = null;
}
