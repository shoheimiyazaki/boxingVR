using System.Numerics;
using MiniRing.Bots;

namespace MiniRing;

/// <summary>相手基準の方向（X は右、Y は前）と行動。キー名を持たないホスト境界。</summary>
public readonly record struct FighterIntent(Vector2 Direction, bool Light = false, bool Heavy = false, bool Evade = false);

/// <summary>操作の時間・間合いの設定。威力の倍率は持たない。</summary>
public sealed record IntentTuning
{
    /// <summary>バネを引ききる保持時間 [s]。ボットの最大予備動作と揃える。</summary>
    public float FullLoadSeconds { get; init; } = BotStyle.MaxTelegraph;
    /// <summary>短い技をボディへ読み替える胸間距離 [腕の長さ倍]。</summary>
    public float CloseRange { get; init; } = 1.05f;
    /// <summary>先行入力の寿命 [技の時間倍]。一つの技を越えて居残らせない。</summary>
    public float BufferDurationShare { get; init; } = 1f;
    /// <summary>回避の反発を打ち返しとして選ぶ猶予 [s]。</summary>
    public float CounterSeconds { get; init; } = 0.3f;
    /// <summary>動き出した拳を読む速さ [腕の長さ/s]。</summary>
    public float ReadSpeed { get; init; } = 0.8f;

    internal IntentTuning Validated()
    {
        if (!float.IsFinite(FullLoadSeconds) || FullLoadSeconds <= 0f || FullLoadSeconds > BotStyle.MaxTelegraph
            || !float.IsFinite(CloseRange) || CloseRange <= 0f
            || !float.IsFinite(BufferDurationShare) || BufferDurationShare <= 0f || BufferDurationShare > 1f
            || !float.IsFinite(CounterSeconds) || CounterSeconds < 0f
            || !float.IsFinite(ReadSpeed) || ReadSpeed <= 0f)
        {
            throw new ArgumentOutOfRangeException(nameof(IntentTuning), "操作設定の時間・距離・速さが範囲外です。");
        }

        return this;
    }
}

/// <summary>意図の選択だけを担当する。技の経路とバネへの受け渡しはボットと共有する。</summary>
public sealed class IntentControl : IFighterControl
{
    private readonly IntentTuning _tuning;
    private readonly BotStyle _style;
    private readonly OpponentRead _read = new();
    private FighterIntent _input;
    private FighterIntent _previous;
    private PunchAction _left;
    private PunchAction _right;
    private Hand? _charging;
    private float _held;
    private Pending? _pending;
    private float _clock;
    private float _defenseClock;
    private float _counterUntil = float.NegativeInfinity;
    private float _defenseSide = 1f;
    private float? _stanceY;
    private PunchKind _kind;
    private Defense _lastDefense;
    private Technique? _lastTechnique;

    public IntentControl(IntentTuning? tuning = null, BotStyle? style = null)
    {
        _tuning = (tuning ?? new IntentTuning()).Validated();
        _style = (style ?? BotCatalog.Boxer).Validated();
    }

    public FighterIntent Input
    {
        get => _input;
        set
        {
            if (!float.IsFinite(value.Direction.X) || !float.IsFinite(value.Direction.Y))
            {
                throw new ArgumentOutOfRangeException(nameof(value), "方向は有限値で指定してください。");
            }

            var direction = Vector2.Clamp(value.Direction, -Vector2.One, Vector2.One);
            _input = value with { Direction = direction.LengthSquared() > 1f ? Vector2.Normalize(direction) : direction };
        }
    }

    public Technique? CurrentTechnique { get; private set; }
    public Defense CurrentDefense { get; private set; }
    public int AttacksStarted { get; private set; }
    public string? LastCombination { get; private set; }

    public FighterCommand Read(in FighterSense sense, float dt)
    {
        if (!float.IsFinite(dt) || dt <= 0f || dt > Replay.ReplayFile.MaxDt)
        {
            throw new ArgumentOutOfRangeException(nameof(dt), "操作の時間刻みが範囲外です。");
        }

        _clock += dt;
        var reach = sense.ArmReach;
        var self = sense.Self;
        var headLocal = sense.SelfHead == default ? new Vector3(0f, 0.3f * reach, 0f) : self.WorldToLocal(sense.SelfHead);
        _stanceY ??= sense.SelfHead.Y;
        _read.Observe(sense, dt);
        var face = sense.OpponentChest - self.Origin;
        var facing = new BodyFrame(self.Origin, BodyFrame.YawOf(face, self.YawRadians));
        var move = (facing.Right * Input.Direction.X) + (facing.Forward * Input.Direction.Y);
        if (!sense.OpponentAlive)
        {
            CancelAttack();
            CurrentDefense = Defense.None;
            _previous = Input;
            return new FighterCommand(move, face, new(Guard(Hand.Left, reach, headLocal, sense.SelfFaintness), false), new(Guard(Hand.Right, reach, headLocal, sense.SelfFaintness), false));
        }

        _left = Advance(_left, dt);
        _right = Advance(_right, dt);
        if (Input.Evade)
        {
            if (!_previous.Evade)
            {
                CancelAttack();
                StartDefense(sense);
            }

            _defenseClock += dt;
            _counterUntil = _clock + _tuning.CounterSeconds;
        }
        else
        {
            if (CurrentDefense != Defense.None)
            {
                _defenseClock += dt;
                if (_defenseClock >= _style.Defenses.Of(CurrentDefense).Duration)
                {
                    CurrentDefense = Defense.None;
                }
            }

            var heavyPressed = Input.Heavy && !_previous.Heavy;
            var lightPressed = Input.Light && !_previous.Light;
            if (heavyPressed || lightPressed)
            {
                var request = new Pending(Select(sense, heavyPressed), heavyPressed, 0f, false, _clock);
                if (!_left.Active && !_right.Active)
                {
                    Start(request, sense, dt);
                }
                else
                {
                    _pending = request;
                }
            }

            UpdateCharge(dt);
            UpdatePending(sense, dt);
        }

        var left = Guard(Hand.Left, reach, headLocal, sense.SelfFaintness);
        var right = Guard(Hand.Right, reach, headLocal, sense.SelfFaintness);
        var head = Vector3.Zero;
        foreach (var hand in new[] { Hand.Left, Hand.Right })
        {
            var action = Action(hand);
            if (!action.Active)
            {
                continue;
            }

            var target = TechniqueExecution.Punch(action, hand, _style.Lead, hand == Hand.Left ? left : right,
                self.WorldToLocal(action.AimWorld), reach);
            if (hand == Hand.Left)
            {
                left = target;
            }
            else
            {
                right = target;
            }

            head += TechniqueExecution.HeadShift(action, _style.Lead, reach);
            move += facing.Forward * (action.Spec.StepIn * TechniqueExecution.Envelope(action));
        }

        if (CurrentDefense != Defense.None)
        {
            var defense = _style.Defenses.Of(CurrentDefense);
            head = TechniqueExecution.DefenseHead(defense, _defenseSide, _defenseClock / defense.Duration) * reach;
            move += (facing.Forward * defense.Move) + (facing.Rotate(Vector3.UnitX) * (defense.Step * _defenseSide));
            var stanceY = _stanceY.Value - self.Origin.Y;
            if (!_left.Active)
            {
                left = TechniqueExecution.DefenseGlove(defense, _kind, _defenseSide, Hand.Left, headLocal, stanceY, reach, left);
            }

            if (!_right.Active)
            {
                right = TechniqueExecution.DefenseGlove(defense, _kind, _defenseSide, Hand.Right, headLocal, stanceY, reach, right);
            }
        }

        _previous = Input;
        return new FighterCommand(move, face, new(left, false, TechniqueExecution.Spring(_left)), new(right, false, TechniqueExecution.Spring(_right)), head,
            TechniqueExecution.Twist(_left, Hand.Left, _style.Lead) + TechniqueExecution.Twist(_right, Hand.Right, _style.Lead));
    }

    private Vector3 Guard(Hand hand, float reach, Vector3 head, float faintness)
        => TechniqueExecution.Guard(hand, _style.Lead, reach, head, _style.GuardHeight, _style.GuardWidth, _style.GuardReach, faintness);

    private PunchAction Action(Hand hand) => hand == Hand.Left ? _left : _right;

    private void Set(Hand hand, PunchAction action)
    {
        if (hand == Hand.Left)
        {
            _left = action;
        }
        else
        {
            _right = action;
        }
    }

    private PunchAction Advance(PunchAction action, float dt)
        => action.Active && _charging != (action.Spec.Lead ? _style.Lead : Opposite(_style.Lead)) ? action with { Time = action.Time + dt } : action;

    private static Hand Opposite(Hand hand) => hand == Hand.Left ? Hand.Right : Hand.Left;

    private Technique Select(in FighterSense sense, bool heavy)
    {
        if (_clock <= _counterUntil && _lastDefense != Defense.None)
        {
            var counter = _style.Defenses.Of(_lastDefense).CounterFor(_kind);
            var combo = counter is { } selected ? Combo.Named(selected.Combo)
                : CounterResponse.CounterFor(_lastDefense, _defenseSide, _style.Lead, 0f);
            var response = combo.Steps.Cast<Technique?>().FirstOrDefault(t => PunchSpec.Of(t!.Value).Lead != heavy);
            if (response is { } technique)
            {
                return technique;
            }
        }

        var close = Vector3.Distance(sense.OpponentChest, sense.Self.Origin) < sense.ArmReach * _tuning.CloseRange;
        if (MathF.Abs(Input.Direction.X) > MathF.Abs(Input.Direction.Y))
        {
            return (heavy, close) switch
            {
                (false, false) => Technique.LeadHook,
                (true, false) => Technique.RearHook,
                (false, true) => Technique.LeadBodyHook,
                _ => Technique.RearBodyHook,
            };
        }

        if (Input.Direction.Y < 0f)
        {
            return heavy ? Technique.RearUppercut : Technique.LeadUppercut;
        }

        return heavy ? close ? Technique.BodyCross : Technique.Cross : close ? Technique.BodyJab : Technique.Jab;
    }

    private void Start(Pending request, in FighterSense sense, float dt)
    {
        var spec = PunchSpec.Of(request.Technique);
        var hand = spec.Lead ? _style.Lead : Opposite(_style.Lead);
        var held = MathF.Min(request.Held, _tuning.FullLoadSeconds);
        _held = held;
        _charging = request.Released ? null : hand;
        var load = request.Released ? held : TechniqueExecution.FullLoad;
        var time = request.Released ? dt : -load + MathF.Min(held, load - 1e-6f);
        Set(hand, new PunchAction(request.Technique, spec, time, spec.Duration, _clock <= _counterUntil,
            AimWorld: spec.ToBody ? sense.OpponentChest : sense.OpponentHead, Load: load));
        var previous = _lastTechnique;
        var combination = previous is { } before
            ? Combo.All.FirstOrDefault(c => c.Steps.Count >= 2 && c.Steps[0] == before && c.Steps[1] == request.Technique)
            : null;
        LastCombination = combination?.Name;
        _lastTechnique = request.Technique;
        CurrentTechnique = request.Technique;
        AttacksStarted++;
    }

    private void UpdateCharge(float dt)
    {
        if (_charging is not { } hand)
        {
            return;
        }

        var action = Action(hand);
        var down = action.Spec.Lead ? Input.Light : Input.Heavy;
        if (down)
        {
            _held = MathF.Min(_tuning.FullLoadSeconds, _held + dt);
            Set(hand, action with { Time = -TechniqueExecution.FullLoad + MathF.Min(_held, TechniqueExecution.FullLoad - 1e-6f) });
        }
        else
        {
            Set(hand, action with { Time = dt, Load = _held });
            _charging = null;
        }
    }

    private void UpdatePending(in FighterSense sense, float dt)
    {
        if (_pending is not { } pending)
        {
            return;
        }

        var down = pending.Heavy ? Input.Heavy : Input.Light;
        pending = pending with { Held = pending.Released ? pending.Held : pending.Held + (down ? dt : 0f), Released = pending.Released || !down };
        _pending = pending;
        var previous = _lastTechnique is { } technique ? PunchSpec.Of(technique) : default;
        if (_clock - pending.At > previous.Duration * _tuning.BufferDurationShare)
        {
            _pending = null;
            return;
        }

        var nextHand = PunchSpec.Of(pending.Technique).Lead ? _style.Lead : Opposite(_style.Lead);
        var combo = Combo.All.FirstOrDefault(c => c.Steps.Count >= 2 && c.Steps[0] == _lastTechnique && c.Steps[1] == pending.Technique);
        var gap = combo?.Gap ?? 1f;
        var active = _left.Active ? _left : _right;
        if (_charging is null && !Action(nextHand).Active && (!active.Active || active.Time >= active.Duration * gap))
        {
            _pending = null;
            Start(pending, sense, dt);
        }
    }

    private void StartDefense(in FighterSense sense)
    {
        var forecast = _read.Predict();
        var glove = sense.OpponentLeft.Velocity.LengthSquared() >= sense.OpponentRight.Velocity.LengthSquared() ? sense.OpponentLeft : sense.OpponentRight;
        _kind = glove.Velocity.Length() >= _tuning.ReadSpeed * sense.ArmReach
            ? BoxerBot.Classify(sense.Self.InverseRotate(glove.Velocity), glove.Position.Y < sense.OpponentChest.Y)
            : forecast.Kind;
        _defenseSide = sense.Self.WorldToLocal(glove.Position).X >= 0f ? -1f : 1f;
        CurrentDefense = Input.Direction == Vector2.Zero ? Defense.Block
            : Input.Direction.Y < 0f ? Defense.Sway
            : DefenseBook.Evasions.Where(d => d is Defense.Slip or Defense.Duck or Defense.Weave)
                .OrderByDescending(d => _style.Defenses.Of(d).Against(_kind))
                .ThenBy(d => _kind == PunchKind.Hook && d == Defense.Weave ? 0 : 1).First();
        _lastDefense = CurrentDefense;
        _defenseClock = 0f;
    }

    private void CancelAttack()
    {
        _left = default;
        _right = default;
        _charging = null;
        _pending = null;
        CurrentTechnique = null;
        _lastTechnique = null;
    }

    private readonly record struct Pending(Technique Technique, bool Heavy, float Held, bool Released, float At);
}
