using System.Numerics;

namespace MiniRing.Bots;

/// <summary>
/// 相手の読み（相手モデル）: 試合の中で相手を見て覚え、「まもなく（<see cref="Window"/> 秒以内に）打ってくるか」と
/// 「どちらの手で、どの種類か」を見積もる。実際のボクサーが拳を見てからではなく、間合い・リズム・体の動き・癖から
/// 次の拳を予測して避けるのと同じ考え方（拳は 0.2 秒足らずで届き、見てからの反応では間に合わない）。
/// <para>
/// 手がかり（特徴）は 5 つ: 間合い、相手の踏み込みの速さ、胴のひねりの速さ（片方の肩が前へ出る）、肩の前への傾きの速さ
/// （体重移動）、前の拳からの時間（リズム・連打の間）。それぞれを区間に分け、「その区間にいた時、<see cref="Window"/> 秒以内に
/// 拳が出たか」を数えて、素朴ベイズで合わせる。相手が溜めを隠せば（短い溜め）体の手がかりは頭の振りと見分けにくくなり、
/// 読みの確からしさが自然に下がる。乱数は使わない（決定的）。
/// </para>
/// </summary>
internal sealed class OpponentRead
{
    /// <summary>予測する先の時間 [s]。溜め（0.05〜0.1 秒）から拳が出るまでを含む長さ。</summary>
    public const float Window = 0.15f;

    /// <summary>拳が出たとみなす、相手の胸に対する拳の外向きの速さ [腕の長さ/s]。</summary>
    private const float OnsetSpeed = 2f;

    /// <summary>拳が戻ったとみなす速さ [腕の長さ/s] と、その速さを下回り続ける時間 [s]。</summary>
    private const float RestSpeed = 0.8f;
    private const float RestTime = 0.08f;

    /// <summary>連打の 2 発目とみなす、前の拳からの時間 [s]。</summary>
    private const float ComboGap = 0.6f;

    /// <summary>手がかりの速さをならす時定数 [s]。</summary>
    private const float Smoothing = 0.015f;

    /// <summary>予測を出すのに必要な、覚えた拳の数（少ないうちは当て推量になる）。</summary>
    private const int MinOnsets = 6;

    /// <summary>区間の数え始めの重み（どの区間も少しは起こりうる）。</summary>
    private const float Prior = 0.5f;

    private static readonly Hand[] Hands = [Hand.Left, Hand.Right];

    private readonly Feature[] _features =
    [
        new("distance", 0.6f, 0.1f, 16),
        new("closing", -1f, 0.5f, 8),
        new("twist", -3.2f, 0.4f, 16, body: true),
        new("lean", -1.2f, 0.4f, 9, body: true),
        new("since", 0f, 0.1f, 26),
    ];

    private readonly Queue<Sample> _pending = new();
    private readonly Queue<float> _onsetTimes = new();
    private readonly float[,] _handByTwist = new float[3, 2];
    private readonly float[,] _handAfter = new float[2, 2];
    private readonly float[,] _kindByHand = new float[2, 4];
    private readonly GloveState[] _gloves = new GloveState[2];
    private float _positives;
    private float _negatives;
    private float _time;
    private bool _primed;
    private float _distance;
    private float _twistValue;
    private float _leanValue;
    private float _closing;
    private float _twistRate;
    private float _leanRate;
    private float _lastOnset = float.NegativeInfinity;
    private Hand _lastHand;
    private float _distanceSum;
    private int _openings;

    /// <summary>いまの手がかり（調査用）: 踏み込みの速さ・ひねりの速さ・肩の前傾の速さ [腕の長さ/s]。</summary>
    internal (float Closing, float Twist, float Lean) Cues => (_closing, _twistRate, _leanRate);

    /// <summary>覚えた相手の拳の数。</summary>
    public int Onsets { get; private set; }

    /// <summary>相手が打ち始める間合いの平均（胸と胸、腕の長さ倍）。少なくとも数発見るまでは null。</summary>
    public float? AttackDistance => _openings >= MinOnsets ? _distanceSum / _openings : null;

    /// <summary>
    /// 1 サブステップぶん相手を見る: 手がかりを更新し、拳の出始めを見つけたら覚え、<see cref="Window"/> 秒経った手がかりに
    /// 「その後に拳が出たか」の答えを付けて数える。
    /// </summary>
    public void Observe(in FighterSense sense, float dt)
    {
        _time += dt;
        var reach = MathF.Max(1e-4f, sense.ArmReach);
        var body = sense.OpponentBody;
        if (body.Pelvis == default || dt <= 0f)
        {
            return; // 胴の見えないホスト（古い FighterSense）では読まない
        }

        // 相手から自分への水平な向き。相手の肩・腰の動きはこの向きで測る（自分の回転に揺らされない）。
        var toSelf = sense.Self.Origin - body.Pelvis;
        toSelf.Y = 0f;
        var distance = MathF.Max(1e-4f, toSelf.Length());
        var u = toSelf / distance;
        var twist = Vector3.Dot(body.RightShoulder - body.LeftShoulder, u) / reach; // 正: 相手の右肩が前
        var lean = Vector3.Dot(((body.LeftShoulder + body.RightShoulder) * 0.5f) - body.Pelvis, u) / reach;
        var chestDistance = Vector3.Distance(sense.OpponentChest with { Y = 0f }, sense.Self.Origin with { Y = 0f }) / reach;
        var k = 1f - MathF.Exp(-dt / Smoothing);
        // 踏み込みは相手の胴の速度だけで測る（距離の変化で測ると、自分の踏み込みや後退まで相手の踏み込みに見える。レビュー指摘）。
        _closing += ((Vector3.Dot(sense.OpponentVelocity with { Y = 0f }, u) / reach) - _closing) * k;
        if (_primed)
        {
            _twistRate += (((twist - _twistValue) / dt) - _twistRate) * k;
            _leanRate += (((lean - _leanValue) / dt) - _leanRate) * k;
        }

        _primed = true;
        _distance = chestDistance;
        _twistValue = twist;
        _leanValue = lean;

        DetectOnsets(sense, reach, dt);
        var since = _time - _lastOnset;
        _pending.Enqueue(new Sample(_time, Bins(since), _twistRate));
        Label();
    }

    /// <summary>
    /// 相手を見られない間（相手が倒れている・数えている間）。次に見た時に、空いた時間をまたいで速さを測らない
    /// （1 サブステップの差で割ると速さが跳ね、外れた区間を覚えてしまう。レビュー指摘）。覚えたことは残す。
    /// </summary>
    public void Pause()
    {
        _primed = false;
        _closing = 0f;
        _twistRate = 0f;
        _leanRate = 0f;
        Array.Clear(_gloves);
        _pending.Clear();
        _onsetTimes.Clear();
        _lastOnset = float.NegativeInfinity;
    }

    /// <summary>
    /// <see cref="Window"/> 秒以内に相手が打ってくる確からしさと、来そうな手・種類。覚えた拳が少ないうちは確からしさ 0。
    /// <see cref="Forecast.BodyEvidence"/> は体の手がかり（ひねり・前傾）だけの証拠の強さ（尤度比の対数。0 = 何も言っていない）。
    /// </summary>
    public Forecast Predict()
    {
        var since = _time - _lastOnset;
        var hand = LikelyHand(since);
        var kind = LikelyKind(hand);
        if (Onsets < MinOnsets || _positives <= 0f || _negatives <= 0f)
        {
            return new Forecast(0f, 0f, hand, kind);
        }

        var bins = Bins(since);
        var logit = MathF.Log(_positives / _negatives);
        var body = 0f;
        for (var f = 0; f < _features.Length; f++)
        {
            var feature = _features[f];
            var b = bins[f];
            var pos = (feature.Positive[b] + Prior) / (_positives + (Prior * feature.Count));
            var neg = (feature.Negative[b] + Prior) / (_negatives + (Prior * feature.Count));
            var evidence = MathF.Log(pos / neg);
            logit += evidence;
            body += feature.Body ? evidence : 0f;
        }

        return new Forecast(1f / (1f + MathF.Exp(-logit)), body, hand, kind);
    }

    /// <summary>予測: 確からしさ・体の手がかりの証拠・来そうな手と種類。</summary>
    internal readonly record struct Forecast(float Likelihood, float BodyEvidence, Hand Hand, PunchKind Kind);

    private int[] Bins(float since)
    {
        var values = new[] { _distance, _closing, _twistRate, _leanRate, float.IsFinite(since) ? since : float.MaxValue };
        var bins = new int[_features.Length];
        for (var f = 0; f < _features.Length; f++)
        {
            bins[f] = _features[f].Bin(values[f]);
        }

        return bins;
    }

    /// <summary>拳の出始め: 構えで止まっていた拳が、相手の胸から外へ速く動き出した。</summary>
    private void DetectOnsets(in FighterSense sense, float reach, float dt)
    {
        foreach (var hand in Hands)
        {
            var seen = sense.Opponent(hand);
            if (seen.Position == default)
            {
                continue;
            }

            var relative = seen.Velocity - sense.OpponentVelocity;
            var speed = relative.Length() / reach;
            var outward = Vector3.Dot(relative, seen.Position - sense.OpponentChest) > 0f;
            var state = _gloves[(int)hand];
            if (state.Busy)
            {
                var rest = speed < RestSpeed ? state.Rest + dt : 0f;
                _gloves[(int)hand] = rest >= RestTime ? default : state with { Rest = rest };
                continue;
            }

            if (speed > OnsetSpeed && outward)
            {
                _gloves[(int)hand] = new GloveState(true, 0f);
                var toBody = seen.Position.Y < sense.Self.Origin.Y - (0.2f * reach);
                Onset(hand, BoxerBot.Classify(sense.Self.InverseRotate(relative), toBody));
            }
        }
    }

    private void Onset(Hand hand, PunchKind kind)
    {
        // 直前の手がかりのひねりの向き（相手のどちらの肩が前へ出たか）と出た手を結びつける。
        var twist = 0f;
        foreach (var s in _pending)
        {
            if (_time - s.Time <= Window && MathF.Abs(s.TwistRate) > MathF.Abs(twist))
            {
                twist = s.TwistRate;
            }
        }

        _handByTwist[TwistSign(twist), (int)hand]++;
        if (_time - _lastOnset < ComboGap)
        {
            _handAfter[(int)_lastHand, (int)hand]++;
        }

        _kindByHand[(int)hand, (int)kind]++;
        if (_time - _lastOnset >= ComboGap)
        {
            // 打ち始めの間合いは連打の最初の拳だけで測る（2 発目以降は踏み込んだ後の近い間合いから出る。レビュー指摘）。
            _distanceSum += _distance;
            _openings++;
        }

        _lastOnset = _time;
        _lastHand = hand;
        _onsetTimes.Enqueue(_time);
        Onsets++;
    }

    /// <summary><see cref="Window"/> 秒経った手がかりに答え（その間に拳が出たか）を付けて数える。</summary>
    private void Label()
    {
        while (_pending.Count > 0 && _time - _pending.Peek().Time > Window)
        {
            var sample = _pending.Dequeue();
            while (_onsetTimes.Count > 0 && _onsetTimes.Peek() <= sample.Time)
            {
                _onsetTimes.Dequeue();
            }

            var positive = _onsetTimes.Count > 0 && _onsetTimes.Peek() <= sample.Time + Window;
            for (var f = 0; f < _features.Length; f++)
            {
                (positive ? _features[f].Positive : _features[f].Negative)[sample.Bins[f]]++;
            }

            if (positive)
            {
                _positives++;
            }
            else
            {
                _negatives++;
            }
        }
    }

    /// <summary>来そうな手: 連打の途中なら前の手からのつながり、ほかは肩のひねりの向き、どちらも無ければよく打つ手。</summary>
    private Hand LikelyHand(float since)
    {
        if (since < ComboGap && _handAfter[(int)_lastHand, 0] + _handAfter[(int)_lastHand, 1] > 0f)
        {
            return _handAfter[(int)_lastHand, 1] > _handAfter[(int)_lastHand, 0] ? Hand.Right : Hand.Left;
        }

        var sign = TwistSign(_twistRate);
        var left = _handByTwist[sign, 0];
        var right = _handByTwist[sign, 1];
        if (left + right <= 0f)
        {
            left = _kindByHand[0, 0] + _kindByHand[0, 1] + _kindByHand[0, 2] + _kindByHand[0, 3];
            right = _kindByHand[1, 0] + _kindByHand[1, 1] + _kindByHand[1, 2] + _kindByHand[1, 3];
        }

        return right > left ? Hand.Right : Hand.Left;
    }

    private PunchKind LikelyKind(Hand hand)
    {
        var best = PunchKind.Straight;
        var most = 0f;
        for (var k = 0; k < 4; k++)
        {
            if (_kindByHand[(int)hand, k] > most)
            {
                most = _kindByHand[(int)hand, k];
                best = (PunchKind)k;
            }
        }

        return best;
    }

    /// <summary>ひねりの向き: 0 = 相手の左肩が前へ、1 = 右肩が前へ、2 = どちらでもない。</summary>
    private static int TwistSign(float twistRate) => twistRate < -0.4f ? 0 : twistRate > 0.4f ? 1 : 2;

    private readonly record struct Sample(float Time, int[] Bins, float TwistRate);

    private readonly record struct GloveState(bool Busy, float Rest);

    /// <summary>手がかり 1 つ: <see cref="Start"/> から <see cref="Width"/> 刻みの区間（両端の区間は外側を含む）。</summary>
    private sealed class Feature(string name, float start, float width, int count, bool body = false)
    {
        public string Name { get; } = name;
        public bool Body { get; } = body;
        public float Start { get; } = start;
        public float Width { get; } = width;
        public int Count { get; } = count;
        public float[] Positive { get; } = new float[count];
        public float[] Negative { get; } = new float[count];

        public int Bin(float value) => float.IsFinite(value) ? Math.Clamp((int)MathF.Floor((value - Start) / Width), 0, Count - 1) : Count - 1;
    }
}
