using System.Numerics;
using MiniRing.Break;

namespace MiniRing;

/// <summary>
/// 休み・試合の始め・試合の終わりの選手の動作（水を飲む・手を振る・両手を挙げる・電話・深呼吸…。エモート）。その間、各選手の入力元を <see cref="IBreakSource"/> に差し替え、
/// 構え（休みはコーナーへ歩く <see cref="RestCommand"/>、始め・終わりはその場に立つ <see cref="StandCommand"/>）へ手・頭・胴・向きの目標の経路を重ねる。
/// 小物（<see cref="BreakProp"/>）は描くだけの物（剛体ではない）。
/// </summary>
public sealed partial class BoxingGame
{
    private readonly Dictionary<Fighter, IBreakSource> _breakSources = [];
    private readonly Dictionary<Fighter, BreakProp> _breakProps = [];

    /// <summary>いま選手が手に持っている小物（動作の間だけ。描くため）。</summary>
    public IEnumerable<BreakProp> BreakProps => EmoteScene is not null ? Fighters.Where(f => _breakProps.ContainsKey(f)).Select(f => _breakProps[f]) : [];

    /// <summary>いま口から出ている煙・飛沫（見た目だけ）。</summary>
    public IEnumerable<(Fighter Fighter, VaporState Vapor)> BreakVapors => EmoteScene is null ? [] : Fighters.Where(f => _breakSources.ContainsKey(f) && _breakSources[f].Vapor is not null).Select(f => (f, _breakSources[f].Vapor!.Value));

    /// <summary>動作の入力元（動作の間だけ。調べもの・テスト用）。</summary>
    public IBreakSource? BreakSourceOf(Fighter fighter) => _breakSources.GetValueOrDefault(fighter);

    /// <summary>
    /// いまの動作の場面（試合の始め・休み・試合の終わり）。動作の場面でなければ null。終わりは <see cref="MatchRules.EndSeconds"/> が正のときだけ。
    /// </summary>
    public BreakScene? EmoteScene
        => InIntro ? BreakScene.Start
        : InBreak ? BreakScene.Break
        : MatchOver && Rules is { EndSeconds: > 0f } ? BreakScene.End
        : null;

    /// <summary>
    /// 試合の終わりの気分（勝ち負けの量）: 勝者 +1・敗者 −1・引き分け 0。勝ち数の差の符号（K.O.・判定とも勝ち数に数えてある）。ゲームの結果の値で、
    /// 動作の選び方の重み（<see cref="BreakWeights.Joy"/>）にだけ使う。
    /// </summary>
    public float EndMood(Fighter fighter)
    {
        var lead = fighter == Player ? PlayerWins - CpuWins : CpuWins - PlayerWins;
        return MathF.Sign(lead);
    }

    /// <summary>動作の場面の指令: 構えに、入力元の目標を重ねる（立っている選手だけ。倒れている選手は構えのまま）。</summary>
    private FighterCommand BreakCommand(Fighter self, Fighter opponent, float dt)
    {
        var scene = EmoteScene;
        // 休み・試合の終わり（動作が無いとき）はコーナーへ歩く休みの構え、始めと終わりの動作はその場に立つ構え。
        var rest = scene is BreakScene.Start or BreakScene.End ? StandCommand(self, opponent) : RestCommand(self, opponent);
        if (scene is null || !_corners.BreakActions || self.State != FighterState.Standing)
        {
            _breakProps.Remove(self);
            return rest;
        }

        if (!_breakSources.TryGetValue(self, out var source))
        {
            var salt = scene.Value switch { BreakScene.Start => 3, BreakScene.End => 5, _ => 0 };
            var seed = unchecked((_seed * 7919) + (MatchRound * 131) + (self == Player ? 1 : 2) + (salt * 104729));
            var style = self == Cpu ? _corners.CpuStyle : _corners.RedBotStyle ?? _corners.PlayerStyle;
            var created = scene == BreakScene.Break && _corners.BreakSource is { } factory
                ? factory(seed, self)
                : new BreakRoutine(seed, style, scene: scene.Value, mood: scene == BreakScene.End ? EndMood(self) : 0f);
            if (created is null)
            {
                return rest;
            }

            source = created;
            _breakSources[self] = source;
        }

        var left = scene switch
        {
            BreakScene.Start => IntroLeft,
            BreakScene.End => Rules!.EndSeconds,
            _ => BreakLeft,
        };
        var command = source.Read(ArmedSense(self, opponent), rest, left, dt);
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

    /// <summary>試合の始め・終わりの構え: その場に立って（歩かない）相手を向き、ガードを休みと同じだけ下げる。</summary>
    private FighterCommand StandCommand(Fighter self, Fighter opponent)
    {
        var rules = Rules ?? MatchRules.Tv;
        var low = rules.RestGuardDrop * self.ArmReach;
        return new FighterCommand(Vector3.Zero, opponent.ChestPosition - self.ChestPosition,
            new HandCommand(AiControl.Guard(Hand.Left, self.ArmReach) + low, false),
            new HandCommand(AiControl.Guard(Hand.Right, self.ArmReach) + low, false));
    }

    /// <summary>動作が終わった（または世界を作り直す）: 入力元と小物を捨てる。</summary>
    private void EndBreakActions()
    {
        if (_breakSources.Count > 0 || _breakProps.Count > 0)
        {
            _breakSources.Clear();
            _breakProps.Clear();
        }
    }
}
