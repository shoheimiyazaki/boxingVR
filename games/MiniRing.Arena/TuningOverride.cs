using System.Globalization;
using System.Reflection;
using MiniRing.Bots;

namespace MiniRing.Arena;

/// <summary>
/// 調整値を実行時に差し替える実験用の道具: <c>--tune ConcussionSpin=18,StrikeShare=0.6</c>（<c>Preset=AdaptivePunch</c> で設定ごと選べる。先頭に書く）。
/// <see cref="FighterTuning.Default"/> の複製に、名前の合う float / bool / int のプロパティを書き込む（コードを直さずに A/B を比べるため。採るときは値を <c>FighterTuning</c> に書く）。
/// 試合の計算そのものは変えない（渡した調整値で <see cref="BoxingGame"/> を作るだけ）ので、決定性は保たれる。
/// </summary>
internal static class TuningOverride
{
    public static FighterTuning? Parse(string? text)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return null;
        }

        var tuning = FighterTuning.Default with { };
        foreach (var item in text.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            var parts = item.Split('=', 2);
            if (parts.Length != 2)
            {
                throw new ArgumentException($"--tune は 名前=値 をカンマで区切ってください（{item}）。");
            }

            if (parts[0] == "Preset")
            {
                tuning = (FighterTuning?)typeof(FighterTuning).GetProperty(parts[1], BindingFlags.Public | BindingFlags.Static)?.GetValue(null)
                    ?? throw new ArgumentException($"FighterTuning にプリセット {parts[1]} がありません。");
                continue;
            }

            if (parts[0].StartsWith("Footwork.", StringComparison.Ordinal))
            {
                var footwork = tuning.Footwork with { };
                var leaf = typeof(FootworkTuning).GetProperty(parts[0]["Footwork.".Length..], BindingFlags.Public | BindingFlags.Instance)
                    ?? throw new ArgumentException($"FootworkTuning に {parts[0]} がありません。");
                leaf.SetValue(footwork, Convert(leaf.PropertyType, parts[0], parts[1]));
                tuning = tuning with { Footwork = footwork };
                continue;
            }

            var property = typeof(FighterTuning).GetProperty(parts[0], BindingFlags.Public | BindingFlags.Instance)
                ?? throw new ArgumentException($"FighterTuning に {parts[0]} がありません。");
            property.SetValue(tuning, Convert(property.PropertyType, parts[0], parts[1]));
        }

        return tuning;
    }

    private static object Convert(Type type, string name, string text) =>
        type == typeof(float) ? float.Parse(text, CultureInfo.InvariantCulture)
        : type == typeof(int) ? int.Parse(text, CultureInfo.InvariantCulture)
        : type == typeof(bool) ? bool.Parse(text)
        : throw new ArgumentException($"{name} は {type.Name} 型で、--tune では差し替えられません。");
}

/// <summary>
/// ボットのスタイルの値を実行時に差し替える実験用の道具: <c>--style DefenseRate*0.7,counter.ReactionTime=0.3</c>
/// （<c>キー=値</c> は置き換え、<c>キー*倍率</c> は各スタイルの値の倍率。名前のないものは全ボット、<c>名前.</c>付きはそのボットだけ）。
/// 倍率なら各スタイルの個性（値の差）を保ったまま全体の傾向だけを動かせる。採るときは値を各スタイルの JSON に書く。
/// </summary>
internal static class StyleOverride
{
    public static Contender Contender(string name, string? spec)
    {
        if (string.IsNullOrWhiteSpace(spec))
        {
            return MiniRing.Bots.Contender.Named(name);
        }

        var style = BotCatalog.Style(name);
        foreach (var item in spec.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            var op = item.IndexOfAny(['=', '*']);
            if (op < 0)
            {
                throw new ArgumentException($"--style は キー=値 か キー*倍率 をカンマで区切ってください（{item}）。");
            }

            var key = item[..op];
            var number = item[(op + 1)..];
            var dot = key.IndexOf('.', StringComparison.Ordinal);
            if (dot >= 0)
            {
                if (key[..dot] != name)
                {
                    continue;
                }

                key = key[(dot + 1)..];
            }

            var property = typeof(BotStyle).GetProperty(key, BindingFlags.Public | BindingFlags.Instance)
                ?? throw new ArgumentException($"BotStyle に {key} がありません。");
            if (property.PropertyType != typeof(float))
            {
                throw new ArgumentException($"{key} は {property.PropertyType.Name} 型で、--style では float だけ差し替えられます。");
            }

            var amount = float.Parse(number, CultureInfo.InvariantCulture);
            var current = (float)property.GetValue(style)!;
            style = style with { };
            property.SetValue(style, item[op] == '*' ? current * amount : amount);
        }

        return MiniRing.Bots.Contender.Of(style);
    }
}

/// <summary>
/// 打たれずに（意識が 90 % 以上のまま）立ちから倒れた回数を数える（面白さの測定の副産物。転倒を「ダウン」「見込みの動き」で稼いでいないかの見張り）。
/// <c>ExcitementRun.Bout</c> の <c>afterStep</c> に渡す。
/// </summary>
internal sealed class FallCounter
{
    private FighterState _red = FighterState.Standing;
    private FighterState _blue = FighterState.Standing;

    public int Falls { get; private set; }

    /// <summary>数え・休み・ラウンドの間の待ち（試合が動いていない）秒数。</summary>
    public float PausedSeconds { get; private set; }

    public void Observe(BoxingGame game, float time)
    {
        ArgumentNullException.ThrowIfNull(game);
        Falls += Count(game.Player, _red) + Count(game.Cpu, _blue);
        if (game.Resting || game.Counting || game.Holding || game.RoundOver)
        {
            PausedSeconds += MiniRing.Highlights.ExcitementRun.StepDt;
        }

        _red = game.Player.State;
        _blue = game.Cpu.State;
    }

    private static int Count(Fighter fighter, FighterState before)
        => before == FighterState.Standing && fighter.State != FighterState.Standing && fighter.Consciousness > 0.9f ? 1 : 0;
}
