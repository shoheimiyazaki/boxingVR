namespace MiniRing;

/// <summary>
/// 反応遅れ用の指令の履歴（固定長のリングバッファ）。時刻は単調増加で追加される前提。
/// 毎サブステップ（240Hz）呼ばれるので、配列の詰め直しやアロケーションをしない。
/// </summary>
internal sealed class CommandHistory
{
    private readonly float[] _times;
    private readonly FighterCommand[] _commands;
    private int _next;
    private int _count;

    public CommandHistory(int capacity)
    {
        _times = new float[capacity];
        _commands = new FighterCommand[capacity];
    }

    public void Add(float time, in FighterCommand command)
    {
        _times[_next] = time;
        _commands[_next] = command;
        _next = (_next + 1) % _times.Length;
        _count = Math.Min(_count + 1, _times.Length);
    }

    /// <summary>時刻 <paramref name="time"/> 以前で最新の指令。それより古いものが無ければ、残っている最古の指令。</summary>
    public FighterCommand At(float time)
    {
        if (_count == 0)
        {
            return default;
        }

        var oldest = default(FighterCommand);
        for (var k = 1; k <= _count; k++)
        {
            var i = (_next - k + _times.Length) % _times.Length;
            if (_times[i] <= time)
            {
                return _commands[i];
            }

            oldest = _commands[i];
        }

        return oldest;
    }
}
