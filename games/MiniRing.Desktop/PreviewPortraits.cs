using MiniRing.Characters;

namespace MiniRing.Desktop;

/// <summary>
/// メニューの見込みの全身の絵（作る画面の体・減量 / 増量の今の体と目標の体）。名簿の絵（<see cref="PortraitSet"/>）と同じく、試合の体のボクセルから写す
/// （<see cref="CharacterPortraits.Build"/>。絵を描き起こさない: §1.7）。1 枚は物理のワールドを作って写すので重い: 背景で 1 枚ずつ作り、鍵（体を決める値）ごとに覚える。
/// 描画の時計だけで動き、試合の計算には入らない。
/// </summary>
internal sealed class PreviewPortraits : IDisposable
{
    /// <summary>覚えておく枚数（スライダーで動かした体重の分。超えたら古い物から捨てる）。</summary>
    private const int Capacity = 48;

    private readonly bool _enabled;
    private readonly object _gate = new();
    private readonly Dictionary<string, CharacterPortrait> _ready = new(StringComparer.Ordinal);
    private readonly Queue<string> _order = new();
    private readonly HashSet<string> _failed = new(StringComparer.Ordinal);
    private string? _building;
    private Task? _task;

    /// <param name="enabled">false なら作らない（テストで重い構築を避けるとき。<see cref="Get"/> はいつも null）。</param>
    public PreviewPortraits(bool enabled) => _enabled = enabled;

    /// <summary>その場で作り終えるまで待つ（画面の撮影・テスト用）。</summary>
    public bool Synchronous { get; set; }

    /// <summary>もうできている絵（作り始めない）。</summary>
    public CharacterPortrait? Ready(string key)
    {
        lock (_gate) { return _ready.GetValueOrDefault(key); }
    }

    /// <summary>
    /// 鍵 <paramref name="key"/> の絵（できていれば）。無ければ、ほかを作っていない間に <paramref name="make"/> の体で作り始めて null（次の描画で出る）。
    /// </summary>
    public CharacterPortrait? Get(string key, Func<FighterCharacter> make)
    {
        ArgumentNullException.ThrowIfNull(make);
        if (!_enabled) { return null; }
        lock (_gate)
        {
            if (_ready.TryGetValue(key, out var ready)) { return ready; }
            if (_building is not null || _failed.Contains(key)) { return null; }
            _building = key;
        }

        if (Synchronous)
        {
            Build(key, make);
            lock (_gate) { return _ready.GetValueOrDefault(key); }
        }

        _task = Task.Run(() => Build(key, make));
        return null;
    }

    private void Build(string key, Func<FighterCharacter> make)
    {
        CharacterPortrait? portrait = null;
        try
        {
            portrait = CharacterPortraits.Build(make());
        }
        catch (Exception ex)
        {
            // 1 枚の失敗でメニューを止めない（その鍵は作り直さない）。理由は標準エラーへ。
            Console.Error.WriteLine($"見込みの絵を作れません（{key}）: {ex}");
        }

        lock (_gate)
        {
            _building = null;
            if (portrait is null)
            {
                _failed.Add(key);
                return;
            }

            _ready[key] = portrait;
            _order.Enqueue(key);
            while (_order.Count > Capacity) { _ready.Remove(_order.Dequeue()); }
        }
    }

    public void Dispose()
    {
        try
        {
            _task?.GetAwaiter().GetResult();
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"見込みの絵の作成が異常終了しました: {ex}"); // 終了処理では投げない
        }
    }
}
