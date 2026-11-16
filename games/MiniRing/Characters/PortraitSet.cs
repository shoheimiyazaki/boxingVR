namespace MiniRing.Characters;

/// <summary>
/// 名簿の全員の肖像（<see cref="CharacterPortrait"/>）。作るのは重い（体の格子を人数分作る）ので、背景のスレッドで 1 人ずつ作り、
/// できた人から <see cref="TryGet"/> で取れる（メニューはできるまで空の枠を描く）。1 スレッドで順に作るのでメモリの山は 1 人分。
/// </summary>
public sealed class PortraitSet : IDisposable
{
    private readonly IReadOnlyList<FighterCharacter> _roster;
    private readonly CharacterPortrait?[] _ready;
    private readonly CancellationTokenSource _cancel = new();
    private Task? _task;

    public PortraitSet(IReadOnlyList<FighterCharacter> roster)
    {
        ArgumentNullException.ThrowIfNull(roster);
        _roster = roster;
        _ready = new CharacterPortrait?[roster.Count];
    }

    /// <summary>作るのを背景で始める（2 度目以降は何もしない）。</summary>
    public PortraitSet Start()
    {
        _task ??= Task.Run(() =>
        {
            for (var i = 0; i < _roster.Count && !_cancel.IsCancellationRequested; i++)
            {
                try
                {
                    Volatile.Write(ref _ready[i], CharacterPortraits.Build(_roster[i]));
                }
                catch (Exception ex)
                {
                    // 1 人の失敗（どんな例外でも）で残りの人を止めない。その人は空の枠のまま、理由は標準エラーへ。
                    Console.Error.WriteLine($"肖像を作れません（{_roster[i].Id}）: {ex}");
                }
            }
        });
        return this;
    }

    /// <summary>全員を同じスレッドで作り終えるまで待つ（テスト・画面の撮影用）。</summary>
    public PortraitSet Complete()
    {
        Start();
        _task!.GetAwaiter().GetResult();
        return this;
    }

    /// <summary>できていれば肖像、まだなら null。</summary>
    public CharacterPortrait? TryGet(int index) => index >= 0 && index < _ready.Length ? Volatile.Read(ref _ready[index]) : null;

    public int ReadyCount => _ready.Count(p => p is not null);

    public void Dispose()
    {
        _cancel.Cancel();
        try
        {
            _task?.GetAwaiter().GetResult();
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"肖像の作成が異常終了しました: {ex}"); // 終了処理では投げない
        }
        finally
        {
            _cancel.Dispose();
        }
    }
}
