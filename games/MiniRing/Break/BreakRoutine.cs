using System.Numerics;
using MiniRing.Bots;

namespace MiniRing.Break;

/// <summary>いま手に持っている小物（種類と手）。</summary>
public readonly record struct BreakHold(PropKind Kind, Hand Hand, PropHold Hold);

/// <summary>
/// 休み（ラウンドの間）の過ごし方の入力元。ボットの頭と同じ、差し替えられる選択部: <see cref="BoxingGame"/> は休みの間、
/// コーナーへ歩く休みの指令（<c>rest</c>）を渡し、この入力元が手・頭・胴・向きの目標を重ねた <see cref="FighterCommand"/> を返す。
/// 出せるのは <see cref="FighterCommand"/> だけ（CLAUDE.md §1.1）。
/// </summary>
public interface IBreakSource
{
    /// <summary>今の休みの指令。<paramref name="rest"/> は休みの構え（コーナーへ歩く・相手の方を向く・ガードを下げる）、<paramref name="breakLeft"/> は休みの残り [s]。</summary>
    public FighterCommand Read(in FighterSense sense, in FighterCommand rest, float breakLeft, float dt);

    /// <summary>いま手に持っている小物。無ければ null。</summary>
    public BreakHold? Holding { get; }
}

/// <summary>
/// 既定の休みの過ごし方: コーナーへ着いたら、残りの休みの中に 2〜4 個の動作（<see cref="BreakBook"/>）を、間に休みを挟んで順に行う。
/// 並びは種付きの乱数と、そのときの体の状態（スタミナの少なさ・意識の薄れ）から決まる重み（連続の値。種類での分岐ではない）で選ぶ。
/// 個性は <see cref="BotStyle.BreakLikes"/>（動作の名前 → 倍率。キャラのファイルの値）。決定的: 同じ種・同じ入力なら同じ並び（試合の記録・再生が一致する）。
/// 動作の中身は目標の経路だけで、体がバネでそれを追う。
/// </summary>
public sealed class BreakRoutine : IBreakSource
{
    /// <summary>動作の数の上限と下限（1 回の休みの中。残りの時間が足りなければ減らす）。</summary>
    public const int MinActions = 2;

    public const int MaxActions = 4;

    /// <summary>動作と動作の間の休みの最短 [s]。</summary>
    public const float MinGap = 0.5f;

    /// <summary>着いたとみなす、休みの歩きの指令の大きさ（0..1。コーナーの 3〜4 cm 手前で止まることがあるので、ぴったり 0 を待たない）と、歩く時間の上限 [s]。</summary>
    public const float ArrivedMove = 0.3f;

    public const float MaxWalk = 2.5f;

    /// <summary>休みの終わりに残す余白 [s]（最後の動作を終えて構えへ戻る）。</summary>
    public const float EndMargin = 0.6f;

    private readonly Random _random;
    private readonly BotStyle? _style;
    private readonly bool _rightHanded;
    private readonly IReadOnlyList<string>? _only;
    private List<Scheduled>? _plan;
    private float _clock;
    private float _walked;

    /// <param name="seed">試合の種から決めた種。</param>
    /// <param name="only">指定すると、くじ引きをせずこの名前の動作をこの順に行う（調べもの・テスト用。"drink:L" のように手を指定できる）。</param>
    /// <param name="style">ボットのスタイル（<see cref="BotStyle.BreakLikes"/> と利き手）。無ければ好みは全部 1。</param>
    public BreakRoutine(int seed, BotStyle? style = null, IReadOnlyList<string>? only = null)
    {
        _random = new Random(seed);
        _style = style;
        _rightHanded = style is null || !style.Southpaw;
        _only = only;
    }

    /// <summary>決めた並び（コーナーへ着いて計画を立てるまでは null）。</summary>
    public IReadOnlyList<(float Start, BreakAction Action, Hand Hand)>? Plan => _plan?.Select(s => (s.Start, s.Action, s.Hand)).ToList();

    /// <summary>いま行っている動作の名前。動作の間は null。</summary>
    public string? CurrentName { get; private set; }

    public BreakHold? Holding { get; private set; }

    public FighterCommand Read(in FighterSense sense, in FighterCommand rest, float breakLeft, float dt)
    {
        if (_plan is null)
        {
            _walked += dt;
            if (rest.ClampedMove.Length() > ArrivedMove && _walked < MaxWalk)
            {
                return rest; // コーナーへ歩いている間は、動作を始めない
            }

            _plan = MakePlan(sense, breakLeft);
        }

        var time = _clock;
        _clock += dt;
        CurrentName = null;
        Holding = null;
        foreach (var scheduled in _plan)
        {
            var action = scheduled.Action;
            var s = time - scheduled.Start;
            if (s < 0f || s > action.Seconds)
            {
                continue;
            }

            CurrentName = action.Name;
            Holding = action.Prop is { } kind ? new BreakHold(kind, scheduled.Hand, action.Hold) : null;
            return Overlay(sense, rest, action, scheduled.Hand, s);
        }

        return rest;
    }

    /// <summary>休みの残りに収まるよう動作を重み付きでくじ引きし、間の休みを均等に空けて並べる。</summary>
    private List<Scheduled> MakePlan(in FighterSense sense, float breakLeft)
    {
        var stamina = sense.SelfStamina;
        var faintness = sense.SelfFaintness;
        var available = MathF.Max(0f, breakLeft - EndMargin);
        var actions = BreakBook.All(1f - Math.Clamp(stamina, 0f, 1f)).ToList();
        var count = MinActions + _random.Next(MaxActions - MinActions + 1);
        var chosen = new List<BreakAction>();
        var forced = new Dictionary<int, Hand>();
        if (_only is not null)
        {
            foreach (var entry in _only)
            {
                var parts = entry.Split(':');
                if (parts.Length > 1)
                {
                    forced[chosen.Count] = parts[1] == "L" ? Hand.Left : Hand.Right;
                }

                chosen.Add(actions.First(a => a.Name == parts[0]));
            }

            actions.Clear();
        }

        while (chosen.Count < count && actions.Count > 0)
        {
            var weights = actions.Select(a => a.Weights.Of(stamina, faintness) * Like(a.Name)).ToList();
            var total = weights.Sum();
            if (total <= 0f)
            {
                break; // 選べる動作が無い（重みが全部 0）
            }

            var pick = actions.Count - 1;
            var roll = _random.NextSingle() * total;
            for (var i = 0; i < actions.Count; i++)
            {
                roll -= weights[i];
                if (roll < 0f)
                {
                    pick = i;
                    break;
                }
            }

            var action = actions[pick];
            actions.RemoveAt(pick);
            if (chosen.Sum(a => a.Seconds) + action.Seconds + ((chosen.Count + 2) * MinGap) > available)
            {
                continue; // 入らない動作は飛ばして、短い動作を探す
            }

            chosen.Add(action);
        }

        var plan = new List<Scheduled>();
        if (chosen.Count == 0)
        {
            return plan;
        }

        var gap = MathF.Max(0f, (available - chosen.Sum(a => a.Seconds)) / (chosen.Count + 1));
        var cursor = gap;
        for (var i = 0; i < chosen.Count; i++)
        {
            // 小物を持つ手は利き手が多く、ときどきもう一方（種付き）。
            var action = chosen[i];
            var dominant = (_random.NextSingle() < 0.75f) == _rightHanded ? Hand.Right : Hand.Left;
            var hand = forced.TryGetValue(i, out var chosenHand) ? chosenHand : dominant;
            plan.Add(new Scheduled(cursor, action, hand));
            cursor += action.Seconds + gap;
        }

        return plan;
    }

    private float Like(string name) => _style?.BreakLikes is { } likes && likes.TryGetValue(name, out var factor) ? factor : 1f;

    /// <summary>動作の経路から、いまの手・頭・胴・向きの目標を出し、休みの構えへ重ねる。</summary>
    private static FighterCommand Overlay(in FighterSense sense, in FighterCommand rest, BreakAction action, Hand main, float s)
    {
        var inner = action.Seconds - action.Lead - action.Release;
        var t01 = inner > 0f ? Math.Clamp((s - action.Lead) / inner, 0f, 1f) : 1f;
        var weight = MathF.Min(Smooth(s / MathF.Max(action.Lead, 1e-3f)), Smooth((action.Seconds - s) / MathF.Max(action.Release, 1e-3f)));
        var reach = sense.ArmReach;
        var head = sense.Self.WorldToLocal(sense.SelfHead);
        var mainSide = main.LocalSide();
        var restCommand = rest;

        HandCommand Aim(Hand hand, HandPath? path)
        {
            var baseCommand = restCommand.For(hand);
            if (path is null)
            {
                return baseCommand;
            }

            var v = path.Path.At(t01);
            var local = new Vector3(v.X * hand.LocalSide(), v.Y, v.Z) * reach;
            var target = (path.Anchor == HandAnchor.Head ? head : Vector3.Zero) + local;
            return baseCommand with { TargetLocal = Vector3.Lerp(baseCommand.TargetLocal, target, weight) };
        }

        var headValue = action.Head.At(t01);
        var twist = action.Twist.At(t01).X;
        var yaw = action.FaceYaw.At(t01).X * mainSide * weight;
        var face = rest.FaceDirection;
        if (MathF.Abs(yaw) > 1e-6f)
        {
            var (sin, cos) = MathF.SinCos(yaw);
            face = new Vector3((face.X * cos) + (face.Z * sin), face.Y, (-face.X * sin) + (face.Z * cos));
        }

        var left = Aim(Hand.Left, main == Hand.Left ? action.Main : action.Other);
        var right = Aim(Hand.Right, main == Hand.Right ? action.Main : action.Other);
        return rest with
        {
            FaceDirection = face,
            Left = left,
            Right = right,
            HeadOffset = rest.HeadOffset + (new Vector3(headValue.X * mainSide, headValue.Y, headValue.Z) * reach * weight),
            TwistBias = rest.TwistBias + (-mainSide * twist * weight),
        };
    }

    private static float Smooth(float u)
    {
        var x = Math.Clamp(u, 0f, 1f);
        return x * x * (3f - (2f * x));
    }

    private readonly record struct Scheduled(float Start, BreakAction Action, Hand Hand);
}
