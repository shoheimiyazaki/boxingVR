namespace MiniRing.Replay;

/// <summary>
/// リプレイの再生ヘッド: 今の時刻・再生/一時停止・向き（前へ / 巻き戻し）・速さ・コマ送り・シーク・A-B 区間の繰り返し。
/// 試合には触れない（時刻を決めるだけ。描く場面は <see cref="ReplayFrameCache"/> から引く）。ホストに依らないのでテストで確かめる。
/// </summary>
public sealed class ReplayTransport
{
    /// <summary>選べる速さ（倍）。1 より下がスロー。</summary>
    public static IReadOnlyList<float> Speeds { get; } = [0.05f, 0.1f, 0.25f, 0.5f, 1f, 2f, 4f, 8f];

    /// <summary>コマ送り 1 回の長さ [s]（描画の 1 コマ = 60 分の 1 秒）。</summary>
    public const float FrameStep = 1f / 60f;

    /// <summary>A-B 区間の最短 [s]（同じ所に A と B を置いて止まり続けないように）。</summary>
    public const float MinimumLoop = 0.1f;

    private int _speedIndex = 4;

    public ReplayTransport(float duration)
    {
        if (!(duration >= 0f) || !float.IsFinite(duration)) { throw new ArgumentOutOfRangeException(nameof(duration)); }
        Duration = duration;
    }

    public float Duration { get; }

    /// <summary>再生ヘッドの時刻 [s]。</summary>
    public float Time { get; private set; }

    public bool Playing { get; private set; } = true;

    /// <summary>+1 で前へ、−1 で巻き戻し。</summary>
    public int Direction { get; private set; } = 1;

    /// <summary>速さの大きさ（倍）。</summary>
    public float Speed => Speeds[_speedIndex];

    public int SpeedIndex => _speedIndex;

    /// <summary>試合の秒 / 実時間の秒（止まっていれば 0、巻き戻しなら負）。</summary>
    public float Rate => Playing ? Direction * Speed : 0f;

    public float? LoopStart { get; private set; }

    public float? LoopEnd { get; private set; }

    /// <summary>A と B の両方があって、繰り返している。</summary>
    public bool Looping => LoopStart is not null && LoopEnd is not null;

    /// <summary>実時間を <paramref name="realSeconds"/> 進める。<paramref name="available"/> は走査が済んだ時刻（そこより先へは進まない）。</summary>
    public void Advance(float realSeconds, float available)
    {
        if (!Playing || !(realSeconds > 0f)) { Time = Clamp(Time, available); return; }
        var next = Time + (Rate * realSeconds);
        if (Looping)
        {
            var a = LoopStart!.Value;
            var b = LoopEnd!.Value;
            if (Direction > 0 && next >= b) { next = a + ((next - b) % MathF.Max(MinimumLoop, b - a)); }
            else if (Direction < 0 && next <= a) { next = b - ((a - next) % MathF.Max(MinimumLoop, b - a)); }
        }

        if (Direction > 0 && next >= Duration) { next = Duration; Playing = false; }
        if (Direction < 0 && next <= 0f) { next = 0f; Playing = false; }
        Time = Clamp(next, available); // まだ走査していない所では待つ（再生は続ける）
    }

    /// <summary>再生と一時停止を切り替える。終わりで再生を押したら頭から。</summary>
    public void TogglePlay()
    {
        if (Playing) { Playing = false; return; }
        if (Direction > 0 && Time >= Duration - 1e-4f) { Time = 0f; }
        if (Direction < 0 && Time <= 1e-4f) { Direction = 1; }
        Playing = true;
    }

    public void Pause() => Playing = false;

    /// <summary>今の位置から、今の向きと速さで再生を続ける（頭へは戻さない）。</summary>
    public void Resume() => Playing = true;

    /// <summary>前へ再生（すでに前へ再生中なら速くする）。</summary>
    public void ShuttleForward() => Shuttle(+1);

    /// <summary>巻き戻し再生（すでに巻き戻し中なら速くする）。</summary>
    public void ShuttleBackward() => Shuttle(-1);

    private void Shuttle(int direction)
    {
        if (Playing && Direction == direction)
        {
            _speedIndex = Math.Min(_speedIndex + 1, Speeds.Count - 1);
            return;
        }

        if (direction > 0 && Time >= Duration - 1e-4f) { Time = 0f; }
        Direction = direction;
        Playing = true;
        _speedIndex = Math.Max(_speedIndex, Index(1f)); // 止まった所から巻き戻すときは等速から
    }

    /// <summary>向きを反転する（再生中なら同じ速さで逆へ）。</summary>
    public void Reverse() => Direction = -Direction;

    public void Faster() => _speedIndex = Math.Min(_speedIndex + 1, Speeds.Count - 1);

    public void Slower() => _speedIndex = Math.Max(_speedIndex - 1, 0);

    /// <summary>速さを選ぶ（<see cref="Speeds"/> の何番目か）。</summary>
    public void SetSpeedIndex(int index) => _speedIndex = Math.Clamp(index, 0, Speeds.Count - 1);

    /// <summary>速さを倍の値で選ぶ（いちばん近い段）。</summary>
    public void SetSpeed(float speed) => _speedIndex = Index(speed);

    /// <summary>時刻へ飛ぶ（再生の状態はそのまま）。</summary>
    public void Seek(float time, float available) => Time = Clamp(time, available);

    /// <summary>今から <paramref name="seconds"/> 秒ずらす。</summary>
    public void SeekBy(float seconds, float available) => Seek(Time + seconds, available);

    /// <summary>一時停止して <paramref name="frames"/> コマ（負なら戻る）動かす。</summary>
    public void StepFrames(int frames, float available)
    {
        Playing = false;
        Seek(Time + (frames * FrameStep), available);
    }

    /// <summary>A（繰り返しの始め）を今の時刻に置く。B より後なら B を外す。</summary>
    public void MarkLoopStart()
    {
        LoopStart = Time;
        if (LoopEnd is { } b && b < Time + MinimumLoop) { LoopEnd = null; }
    }

    /// <summary>B（繰り返しの終わり）を今の時刻に置く。A より前なら A を外す。置いたら A へ戻って繰り返す。</summary>
    public void MarkLoopEnd()
    {
        LoopEnd = Time;
        if (LoopStart is { } a && a > Time - MinimumLoop) { LoopStart = null; }
        if (Looping) { Time = LoopStart!.Value; }
    }

    public void ClearLoop()
    {
        LoopStart = null;
        LoopEnd = null;
    }

    private float Clamp(float time, float available)
    {
        var limit = Math.Clamp(float.IsFinite(available) ? available : 0f, 0f, Duration);
        return Math.Clamp(float.IsFinite(time) ? time : 0f, 0f, limit);
    }

    private static int Index(float speed)
    {
        var best = 0;
        for (var i = 1; i < Speeds.Count; i++)
        {
            if (MathF.Abs(MathF.Log(Speeds[i] / speed)) < MathF.Abs(MathF.Log(Speeds[best] / speed))) { best = i; }
        }

        return best;
    }
}
