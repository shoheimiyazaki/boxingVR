namespace MiniRing.Replay;

/// <summary>
/// 走査が写した場面（<see cref="ReplayFrame"/>）の置き場。走査のスレッドが足し、描画のスレッドが時刻で引く。
/// <para>
/// 使うメモリに上限を置く: 見積もりが上限を越えたら、1 枚おきに捨てて間隔を倍にする（捨てた場面の音は次の場面へ移すので、音は失わない）。
/// 長い試合ほど場面の間が粗くなるが、描くときに前後の場面の姿勢を補間するので、動きは途切れて見えない。
/// </para>
/// </summary>
public sealed class ReplayFrameCache
{
    private readonly object _gate = new();
    private readonly List<ReplayFrame> _frames = [];
    private readonly List<long> _bytes = [];
    private readonly HashSet<object> _texels = new(ReferenceEqualityComparer.Instance);
    private long _total;

    /// <param name="interval">場面を写す試合時間の間隔 [s]。</param>
    /// <param name="budgetBytes">場面に使うメモリの見積もりの上限 [byte]。</param>
    public ReplayFrameCache(float interval, long budgetBytes)
    {
        if (!(interval > 0f) || !float.IsFinite(interval)) { throw new ArgumentOutOfRangeException(nameof(interval)); }
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(budgetBytes);
        Interval = interval;
        BudgetBytes = budgetBytes;
    }

    /// <summary>いまの場面の間隔 [s]（間引くたびに倍になる）。</summary>
    public float Interval { get; private set; }

    public long BudgetBytes { get; }

    public int Count { get { lock (_gate) { return _frames.Count; } } }

    public long EstimatedBytes { get { lock (_gate) { return _total; } } }

    /// <summary>写し終えた最後の場面の時刻 [s]（まだ無ければ −∞）。</summary>
    public float LastTime { get { lock (_gate) { return _frames.Count > 0 ? _frames[^1].Time : float.NegativeInfinity; } } }

    /// <summary>
    /// 場面を足す（時刻は前の場面より後）。上限を越えたら間引く。<paramref name="estimatedBytes"/> はボクセルの色の写しを除いた大きさで、
    /// 色の写しは場面どうしで共有されるので、ここで同じ写しを 1 回だけ数える。
    /// </summary>
    public void Add(ReplayFrame frame, long estimatedBytes)
    {
        ArgumentNullException.ThrowIfNull(frame);
        lock (_gate)
        {
            if (_frames.Count > 0 && frame.Time < _frames[^1].Time)
            {
                throw new ArgumentException("場面は時刻の順に足す。", nameof(frame));
            }

            _frames.Add(frame);
            _bytes.Add(Math.Max(0, estimatedBytes));
            _total += Math.Max(0, estimatedBytes) + frame.Voxels.CountTexelBytes(_texels);
            while (_total > BudgetBytes && _frames.Count > 4)
            {
                Decimate();
            }
        }
    }

    /// <summary><paramref name="time"/> を挟む 2 枚と、その間の位置（0 = <paramref name="from"/>、1 = <paramref name="to"/>）。場面が無ければ false。</summary>
    public bool TryGetPair(float time, out ReplayFrame from, out ReplayFrame to, out float blend)
    {
        lock (_gate)
        {
            if (_frames.Count == 0)
            {
                from = to = null!;
                blend = 0f;
                return false;
            }

            var after = UpperBound(time); // time より後の最初の場面
            if (after == 0)
            {
                from = to = _frames[0];
                blend = 0f;
                return true;
            }

            if (after >= _frames.Count)
            {
                from = to = _frames[^1];
                blend = 0f;
                return true;
            }

            from = _frames[after - 1];
            to = _frames[after];
            var span = to.Time - from.Time;
            blend = span > 0f ? Math.Clamp((time - from.Time) / span, 0f, 1f) : 0f;
            return true;
        }
    }

    /// <summary><paramref name="time"/> にいちばん近い場面（無ければ null）。</summary>
    public ReplayFrame? Nearest(float time) => TryGetPair(time, out var a, out var b, out var t) ? (t < 0.5f ? a : b) : null;

    /// <summary>時刻が (<paramref name="after"/>, <paramref name="upTo"/>] の場面（前へ再生したときに鳴らす音を拾う）。</summary>
    public IReadOnlyList<ReplayFrame> Between(float after, float upTo)
    {
        lock (_gate)
        {
            if (!(upTo > after))
            {
                return [];
            }

            var list = new List<ReplayFrame>();
            for (var i = UpperBound(after); i < _frames.Count && _frames[i].Time <= upTo; i++)
            {
                list.Add(_frames[i]);
            }

            return list;
        }
    }

    /// <summary>1 枚おきに捨てて間隔を倍にする。最初と最後の場面は残し、捨てた場面の音は次に残る場面の先頭へ移す。</summary>
    private void Decimate()
    {
        var frames = new List<ReplayFrame>(_frames.Count / 2 + 2);
        var bytes = new List<long>(_frames.Count / 2 + 2);
        ReplayFrameSound? carried = null;
        for (var i = 0; i < _frames.Count; i++)
        {
            var keep = i % 2 == 0 || i == _frames.Count - 1;
            if (!keep)
            {
                carried = carried is null ? _frames[i].Sound : carried.Then(_frames[i].Sound);
                continue;
            }

            var frame = carried is null ? _frames[i] : _frames[i] with { Sound = carried.Then(_frames[i].Sound) };
            carried = null;
            frames.Add(frame);
            bytes.Add(_bytes[i]);
        }

        _frames.Clear();
        _frames.AddRange(frames);
        _bytes.Clear();
        _bytes.AddRange(bytes);
        // 残った場面がまだ指している色の写しだけを数え直す（捨てた場面だけが持っていた写しは解放される）。
        _texels.Clear();
        _total = bytes.Sum() + frames.Sum(f => f.Voxels.CountTexelBytes(_texels));
        Interval *= 2f;
    }

    private int UpperBound(float time)
    {
        int lo = 0, hi = _frames.Count;
        while (lo < hi)
        {
            var mid = (lo + hi) >> 1;
            if (_frames[mid].Time <= time) { lo = mid + 1; } else { hi = mid; }
        }

        return lo;
    }
}
