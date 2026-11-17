using MiniRing.Break;

namespace MiniRing;

/// <summary>
/// ラウンドの間の休みの動作（水を飲む・汗を拭く・電話・深呼吸…）。休みの間、各選手の入力元を <see cref="IBreakSource"/> に差し替え、
/// 休みの構え（<see cref="RestCommand"/>）へ手・頭・胴・向きの目標の経路を重ねる。小物（<see cref="BreakProp"/>）は描くだけの物（剛体ではない）。
/// </summary>
public sealed partial class BoxingGame
{
    private readonly Dictionary<Fighter, IBreakSource> _breakSources = [];
    private readonly Dictionary<Fighter, BreakProp> _breakProps = [];

    /// <summary>いま選手が手に持っている小物（休みの間だけ。描くため）。</summary>
    public IEnumerable<BreakProp> BreakProps => InBreak ? Fighters.Where(f => _breakProps.ContainsKey(f)).Select(f => _breakProps[f]) : [];

    /// <summary>休みの入力元（休みの間だけ。調べもの・テスト用）。</summary>
    public IBreakSource? BreakSourceOf(Fighter fighter) => _breakSources.GetValueOrDefault(fighter);

    /// <summary>休みの間の指令: 休みの構えに、休みの入力元の目標を重ねる（立っている選手だけ。倒れている選手は構えのまま）。</summary>
    private FighterCommand BreakCommand(Fighter self, Fighter opponent, float dt)
    {
        var rest = RestCommand(self, opponent);
        if (!InBreak || !_corners.BreakActions || self.State != FighterState.Standing)
        {
            _breakProps.Remove(self);
            return rest;
        }

        if (!_breakSources.TryGetValue(self, out var source))
        {
            var seed = unchecked((_seed * 7919) + (MatchRound * 131) + (self == Player ? 1 : 2));
            var created = _corners.BreakSource is { } factory
                ? factory(seed, self)
                : new BreakRoutine(seed, self == Cpu ? _corners.CpuStyle : _corners.RedBotStyle ?? _corners.PlayerStyle);
            if (created is null)
            {
                return rest;
            }

            source = created;
            _breakSources[self] = source;
        }

        var command = source.Read(ArmedSense(self, opponent), rest, BreakLeft, dt);
        if (source.Holding is { } holding)
        {
            if (!_breakProps.TryGetValue(self, out var prop) || prop.Kind != holding.Kind || prop.Hand != holding.Hand)
            {
                _breakProps[self] = BreakProp.Create(holding.Kind, self, holding.Hand, holding.Hold);
            }
        }
        else
        {
            _breakProps.Remove(self); // 動作の間は小物を返す（セコンドへ）
        }

        return command;
    }

    /// <summary>休みが終わった（または世界を作り直す）: 休みの入力元と小物を捨てる。</summary>
    private void EndBreakActions()
    {
        if (_breakSources.Count > 0 || _breakProps.Count > 0)
        {
            _breakSources.Clear();
            _breakProps.Clear();
        }
    }
}
