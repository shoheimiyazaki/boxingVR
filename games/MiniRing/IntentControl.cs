using System.Numerics;
using MiniRing.Bots;

namespace MiniRing;

/// <summary>
/// 方向と行動。<paramref name="Direction"/> は相手基準（X は右、Y は前）。<paramref name="World"/> を指定すると、
/// 方向はワールドの水平面（X = ワールド X、Y = ワールド Z）で与えたものとして扱い、移動はそのまま、技の選択（前後・横）は
/// 体が相手へ向いた座標に直して読む。カメラ基準のキー入力はホストが入力の段階でワールドへ直して渡す（記録は指令に残るので、再生はカメラに依らず同じ）。
/// </summary>
public readonly record struct FighterIntent(Vector2 Direction, bool Light = false, bool Heavy = false, bool Evade = false, Vector2? World = null);

/// <summary>操作の時間・間合いの設定。威力の倍率は持たない。</summary>
public sealed record IntentTuning
{
    /// <summary>バネを引ききる保持時間 [s]。ボットの最大予備動作と揃える。</summary>
    public float FullLoadSeconds { get; init; } = BotStyle.MaxTelegraph;
    /// <summary>短い技をボディへ読み替える胸間距離 [腕の長さ倍]。</summary>
    public float CloseRange { get; init; } = 1.05f;
    /// <summary>
    /// 旧: 先行入力の寿命 [技の時間倍]。先行入力は <see cref="BufferSeconds"/> の秒数で決めるようになったので、検証の互換のためだけに残す（使わない）。
    /// </summary>
    public float BufferDurationShare { get; init; } = 1f;
    /// <summary>先行入力の寿命 [s]。押した順に並べて待つ（ジャブ・ジャブ・ストレートが押した順に出る長さ。ワンツーを続けて打てる程度で、長く居残らせない）。</summary>
    public float BufferSeconds { get; init; } = 0.5f;
    /// <summary>先行入力を並べておける数。</summary>
    public int BufferCapacity { get; init; } = 4;
    /// <summary>
    /// 溜めなしの短押しの技の時間の倍率（溜めの量 <c>held / FullLoadSeconds</c> に応じて 1 まで連続に戻る）。軽くはたくように速く出て、速く戻る。
    /// 溜めて押し出すほど（バネにエネルギーを貯めるほど）経路はゆっくり重くなる。威力は掛けず、物理（体のバネ）が決める。
    /// </summary>
    public float TapDurationScale { get; init; } = 0.62f;
    /// <summary>反対の手の次の技を始められる、前の技の進み具合 [0..1]。前の拳が出ている間に次の手が動き出す。</summary>
    public float AlternateGap { get; init; } = 0.5f;
    /// <summary>同じ手の次の技を始められる、前の技の進み具合 [0..1]。前の拳が戻り始めたら（<see cref="PunchSpec.OutboundShare"/>）次を打ち出せる。</summary>
    public float SameHandGap { get; init; } = PunchSpec.OutboundShare;
    /// <summary>回避の反発を打ち返しとして選ぶ猶予 [s]。</summary>
    public float CounterSeconds { get; init; } = 0.3f;
    /// <summary>動き出した拳を読む速さ [腕の長さ/s]。</summary>
    public float ReadSpeed { get; init; } = 0.8f;
    /// <summary>回避で頭を沈める深さの倍率（守りの表の値に掛ける）。ダッキング・ウィービングを深く落とす。</summary>
    public float DropDepth { get; init; } = 1.45f;
    /// <summary>回避で頭を横へずらす量の倍率。</summary>
    public float SlipReach { get; init; } = 1.3f;
    /// <summary>スリップ・ウィービングで胴を反対の手側へ巻き込む角度 [rad]。解くとき逆の手のフックへ体のバネとして移る。</summary>
    public float CoilRadians { get; init; } = 22f * MathF.PI / 180f;
    /// <summary>巻き込みが満ちるまでの時間 [s]。</summary>
    public float CoilSeconds { get; init; } = 0.12f;
    /// <summary>回避を押し続けたとき、守りの時間（守りの表の Duration）を過ぎてから頭と胴の目標を元の構えへ戻しきる時間 [s]。深く沈んだ姿勢は体が保てない（上体が前へ倒れてダウンになる）ので、押し続けても構えへ戻る。</summary>
    public float RecoverSeconds { get; init; } = 0.3f;

    internal IntentTuning Validated()
    {
        if (!float.IsFinite(FullLoadSeconds) || FullLoadSeconds <= 0f || FullLoadSeconds > BotStyle.MaxTelegraph
            || !float.IsFinite(CloseRange) || CloseRange <= 0f
            || !float.IsFinite(BufferDurationShare) || BufferDurationShare <= 0f || BufferDurationShare > 1f
            || !float.IsFinite(BufferSeconds) || BufferSeconds <= 0f || BufferSeconds > 2f
            || BufferCapacity is < 1 or > 16
            || !float.IsFinite(TapDurationScale) || TapDurationScale < 0.3f || TapDurationScale > 1f
            || !float.IsFinite(AlternateGap) || AlternateGap <= 0f || AlternateGap > 1f
            || !float.IsFinite(SameHandGap) || SameHandGap <= 0f || SameHandGap > 1f
            || !float.IsFinite(CounterSeconds) || CounterSeconds < 0f
            || !float.IsFinite(ReadSpeed) || ReadSpeed <= 0f
            || !float.IsFinite(DropDepth) || DropDepth < 0.5f || DropDepth > 3f
            || !float.IsFinite(SlipReach) || SlipReach < 0.5f || SlipReach > 3f
            || !float.IsFinite(CoilRadians) || CoilRadians < 0f || CoilRadians > 1f
            || !float.IsFinite(CoilSeconds) || CoilSeconds <= 0f
            || !float.IsFinite(RecoverSeconds) || RecoverSeconds <= 0f)
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
    private readonly List<Pending> _queue = [];
    private readonly List<Technique> _started = [];
    private FighterIntent _input;
    private FighterIntent _previous;
    private Vector2 _local;
    private PunchAction _left;
    private PunchAction _right;
    private Hand? _charging;
    private bool _chargeHeavy;
    private Hand _lastHand;
    private float _held;
    private float _clock;
    private float _defenseClock;
    private float _counterUntil = float.NegativeInfinity;
    private float _defenseSide = 1f;
    private float? _stanceY;
    private PunchKind _kind;
    private Defense _lastDefense;
    private float _lastStartClock = float.NegativeInfinity;
    private float _lastStartDuration;
    private readonly List<Technique> _chain = [];

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
            if (!float.IsFinite(value.Direction.X) || !float.IsFinite(value.Direction.Y)
                || (value.World is { } w && (!float.IsFinite(w.X) || !float.IsFinite(w.Y))))
            {
                throw new ArgumentOutOfRangeException(nameof(value), "方向は有限値で指定してください。");
            }

            static Vector2 Limit(Vector2 v)
            {
                var clamped = Vector2.Clamp(v, -Vector2.One, Vector2.One);
                return clamped.LengthSquared() > 1f ? Vector2.Normalize(clamped) : clamped;
            }

            _input = value with { Direction = Limit(value.Direction), World = value.World is { } world ? Limit(world) : null };
        }
    }

    public Technique? CurrentTechnique { get; private set; }
    public Defense CurrentDefense { get; private set; }
    public int AttacksStarted { get; private set; }
    public string? LastCombination { get; private set; }

    /// <summary>打ち出した技を、打ち出した順に。</summary>
    public IReadOnlyList<Technique> StartedTechniques => _started;

    /// <summary>先行入力に今並んでいる数。</summary>
    public int QueuedCount => _queue.Count;

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
        Vector3 move;
        if (Input.World is { } world)
        {
            // カメラ基準: 移動はワールドの向きのまま。技の選択は相手へ向いた体の座標（前後・左右）へ直して読む。
            move = new Vector3(world.X, 0f, world.Y);
            _local = new Vector2(Vector3.Dot(move, facing.Right), Vector3.Dot(move, facing.Forward));
        }
        else
        {
            _local = Input.Direction;
            move = (facing.Right * Input.Direction.X) + (facing.Forward * Input.Direction.Y);
        }

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
        else if (CurrentDefense != Defense.None)
        {
            _defenseClock += dt;
            if (_defenseClock >= _style.Defenses.Of(CurrentDefense).Duration)
            {
                CurrentDefense = Defense.None;
            }
        }

        // 回避を押している間の攻撃は、その回避の姿勢からの打ち返しになる（ダッキングならボディ、スリップなら逆の手のフック）。
        var heavyPressed = Input.Heavy && !_previous.Heavy;
        var lightPressed = Input.Light && !_previous.Light;
        if (lightPressed)
        {
            Press(new Pending(Select(sense, false), false, 0f, false, _clock), sense, dt);
        }

        if (heavyPressed)
        {
            Press(new Pending(Select(sense, true), true, 0f, false, _clock), sense, dt);
        }

        UpdateCharge(dt);
        UpdateQueue(sense, dt);

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

        var coil = 0f;
        if (CurrentDefense != Defense.None)
        {
            var defense = _style.Defenses.Of(CurrentDefense);
            var path = TechniqueExecution.DefenseHead(defense, _defenseSide, _defenseClock / defense.Duration) * reach;
            // 頭を深く落とす・大きくずらす（ダッキングは膝を折って腰ごと沈む。脚の力は Fighter が抜く）。
            // 押し続けても、守りの時間を過ぎたら頭を構えへ戻していく（深い姿勢は体が保てず、上体が倒れてダウンになる）。
            var hold = Math.Clamp(1f - ((_defenseClock - defense.Duration) / _tuning.RecoverSeconds), 0f, 1f);
            head = new Vector3(path.X * _tuning.SlipReach, path.Y < 0f ? path.Y * _tuning.DropDepth : path.Y, path.Z) * hold;
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

            coil = _left.Active || _right.Active ? 0f : Coil(); // 打ち出したら巻き込みは解ける（拳のひねりへ移る）
        }

        _previous = Input;
        return new FighterCommand(move, face, new(left, false, TechniqueExecution.Spring(_left)), new(right, false, TechniqueExecution.Spring(_right)), head,
            TechniqueExecution.Twist(_left, Hand.Left, _style.Lead) + TechniqueExecution.Twist(_right, Hand.Right, _style.Lead) + (CoilHand() is { } hand0 ? CoilTwist(hand0) * coil : 0f));
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

    private Hand HandOf(Technique technique) => PunchSpec.Of(technique).Lead ? _style.Lead : Opposite(_style.Lead);

    /// <summary>回避の巻き込みの満ち具合（0..1）。ダッキングの沈み・スリップのひねりが満ちるほど、打ち返しへ移るバネが大きい。</summary>
    private float Coil()
        => CurrentDefense is Defense.Duck or Defense.Slip or Defense.SlipIn or Defense.Weave
            ? Math.Clamp(_defenseClock / _tuning.CoilSeconds, 0f, 1f) * Math.Clamp(1f - ((_defenseClock - _style.Defenses.Of(CurrentDefense).Duration) / _tuning.RecoverSeconds), 0f, 1f)
            : 0f;

    /// <summary>スリップ・ウィービングの体の巻き込みの手（頭を寄せた側と反対の手のフックへ解く）。巻き込まない回避は null。</summary>
    private Hand? CoilHand()
        => CurrentDefense is Defense.Slip or Defense.SlipIn or Defense.Weave ? (_defenseSide > 0f ? Hand.Right : Hand.Left) : null;

    /// <summary>手を後ろへ引く向きのひねり（打つとき解いて前へ回る）。</summary>
    private float CoilTwist(Hand hand) => (hand == Hand.Right ? -1f : 1f) * _tuning.CoilRadians;

    private Technique Select(in FighterSense sense, bool heavy)
    {
        if (_clock <= _counterUntil && _lastDefense != Defense.None)
        {
            if (_lastDefense == Defense.Duck)
            {
                // 沈んだ姿勢からはボディ（膝のバネを乗せる）。
                return heavy ? Technique.RearBodyHook : Technique.LeadBodyHook;
            }

            if (_lastDefense is Defense.Slip or Defense.SlipIn)
            {
                // 頭を寄せた側と反対の手のフック（外した拳の外から、巻き込んだ胴を解いて打つ）。
                var hand = _defenseSide > 0f ? Hand.Right : Hand.Left;
                return hand == _style.Lead ? Technique.LeadHook : Technique.RearHook;
            }

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
        if (MathF.Abs(_local.X) > MathF.Abs(_local.Y))
        {
            return (heavy, close) switch
            {
                (false, false) => Technique.LeadHook,
                (true, false) => Technique.RearHook,
                (false, true) => Technique.LeadBodyHook,
                _ => Technique.RearBodyHook,
            };
        }

        if (_local.Y < 0f)
        {
            return heavy ? Technique.RearUppercut : Technique.LeadUppercut;
        }

        return heavy ? close ? Technique.BodyCross : Technique.Cross : close ? Technique.BodyJab : Technique.Jab;
    }

    /// <summary>押された技を、空いていればすぐ、そうでなければ押した順に並べて待たせる。</summary>
    private void Press(Pending request, in FighterSense sense, float dt)
    {
        if (_queue.Count == 0 && !_left.Active && !_right.Active)
        {
            Start(request, sense, dt);
            return;
        }

        if (_queue.Count >= _tuning.BufferCapacity)
        {
            _queue.RemoveAt(0); // 古い先行入力から捨てる（新しい押下を優先）
        }

        _queue.Add(request);
    }

    /// <summary>技の時間: 溜めのない短押しは速く（軽くはたく）、溜めるほど本来の長さへ連続に戻る。</summary>
    private float DurationFor(in PunchSpec spec, float held)
        => spec.Duration * float.Lerp(_tuning.TapDurationScale, 1f, Math.Clamp(held / _tuning.FullLoadSeconds, 0f, 1f));

    private void Start(Pending request, in FighterSense sense, float dt)
    {
        var spec = PunchSpec.Of(request.Technique);
        var hand = HandOf(request.Technique);
        var held = MathF.Min(request.Held, _tuning.FullLoadSeconds);
        _held = held;
        _charging = request.Released ? null : hand;
        _chargeHeavy = request.Heavy;
        var load = request.Released ? held : TechniqueExecution.FullLoad;
        if (request.Released && CurrentDefense != Defense.None && _clock <= _counterUntil)
        {
            // 回避で巻き込んだ体（沈み・ひねり）のバネを、この拳へ解く。巻き込みが大きいほど大きい（体が貯めた分だけ。物理のバネが実際の量を決める）。
            load = MathF.Max(load, Coil() * TechniqueExecution.FullLoad);
        }

        var time = request.Released ? dt : -load + MathF.Min(held, load - 1e-6f);
        var duration = DurationFor(spec, request.Released ? MathF.Max(held, load) : held);
        Set(hand, new PunchAction(request.Technique, spec, time, duration, _clock <= _counterUntil,
            AimWorld: spec.ToBody ? sense.OpponentChest : sense.OpponentHead, Load: load));
        if (_clock - _lastStartClock > _lastStartDuration + 0.4f)
        {
            _chain.Clear(); // 間が空いたら別の連打
        }

        _chain.Add(request.Technique);
        LastCombination = NameOfChain();
        _lastHand = hand;
        _lastStartClock = _clock;
        _lastStartDuration = duration;
        CurrentTechnique = request.Technique;
        _started.Add(request.Technique);
        AttacksStarted++;
    }

    /// <summary>連打の末尾が、名前のある型（<see cref="Combo.All"/>）のどれと一致するか（最も長いもの）。</summary>
    private string? NameOfChain()
    {
        for (var take = Math.Min(_chain.Count, 4); take >= 2; take--)
        {
            var tail = _chain.GetRange(_chain.Count - take, take);
            var match = Combo.All.FirstOrDefault(c => c.Steps.Count == take && c.Steps.SequenceEqual(tail));
            if (match is not null)
            {
                return match.Name;
            }
        }

        return null;
    }

    private void UpdateCharge(float dt)
    {
        if (_charging is not { } hand)
        {
            return;
        }

        var action = Action(hand);
        var down = _chargeHeavy ? Input.Heavy : Input.Light;
        if (down)
        {
            _held = MathF.Min(_tuning.FullLoadSeconds, _held + dt);
            Set(hand, action with { Time = -TechniqueExecution.FullLoad + MathF.Min(_held, TechniqueExecution.FullLoad - 1e-6f) });
        }
        else
        {
            Set(hand, action with { Time = dt, Load = _held, Duration = DurationFor(action.Spec, _held) });
            _charging = null;
        }
    }

    private void UpdateQueue(in FighterSense sense, float dt)
    {
        if (_queue.Count == 0)
        {
            return;
        }

        for (var i = 0; i < _queue.Count; i++)
        {
            var entry = _queue[i];
            var down = entry.Heavy ? Input.Heavy : Input.Light;
            // 後から押された同じボタンの押下は、先の拳の「押し続け」に数えない（先の拳は、離した時点で離したことになっている）。
            _queue[i] = entry with
            {
                Held = entry.Released ? entry.Held : entry.Held + (down ? dt : 0f),
                Released = entry.Released || !down,
            };
        }

        _queue.RemoveAll(entry => _clock - entry.At > _tuning.BufferSeconds);
        if (_queue.Count == 0 || _charging is not null)
        {
            return;
        }

        var next = _queue[0];
        var nextHand = HandOf(next.Technique);
        var busy = Action(nextHand);
        var handFree = !busy.Active || busy.Time >= busy.Duration * PunchSpec.OutboundShare; // 戻り始めた拳は、もう次を打ち出せる
        var recent = Action(_lastHand);
        var gap = nextHand == _lastHand ? _tuning.SameHandGap : _tuning.AlternateGap;
        var chained = !recent.Active || recent.Time >= recent.Duration * gap;
        if (handFree && chained)
        {
            _queue.RemoveAt(0);
            Start(next, sense, dt);
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
        if (_local == Vector2.Zero)
        {
            CurrentDefense = Defense.Block;
        }
        else if (MathF.Abs(_local.X) > MathF.Abs(_local.Y))
        {
            // 横＋回避: 押した側へ素早く足を運ぶ（足の運びの目標。体のローカル +X は左なので、右へ押したら側は −1）。
            CurrentDefense = Defense.SideStep;
            _defenseSide = _local.X > 0f ? -1f : 1f;
        }
        else if (_local.Y < 0f)
        {
            CurrentDefense = Defense.Sway;
        }
        else
        {
            CurrentDefense = DefenseBook.Evasions.Where(d => d is Defense.Slip or Defense.Duck or Defense.Weave)
                .OrderByDescending(d => _style.Defenses.Of(d).Against(_kind))
                .ThenBy(d => _kind == PunchKind.Hook && d == Defense.Weave ? 0 : 1).First();
        }

        _lastDefense = CurrentDefense;
        _defenseClock = 0f;
    }

    private void CancelAttack()
    {
        _left = default;
        _right = default;
        _charging = null;
        _queue.Clear();
        _chain.Clear();
        CurrentTechnique = null;
    }

    private readonly record struct Pending(Technique Technique, bool Heavy, float Held, bool Released, float At);
}
