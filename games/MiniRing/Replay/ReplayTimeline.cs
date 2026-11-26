using MiniRing.Highlights;

namespace MiniRing.Replay;

/// <summary>片方の選手の、見えている状態（シークのバーのグラフ）。どれも 0..1。</summary>
/// <param name="Consciousness">意識（<see cref="Fighter.Consciousness"/>）。</param>
/// <param name="Stamina">スタミナ（<see cref="Fighter.Stamina"/>）。</param>
/// <param name="Body">体の残り（始めのセルに対する今のセルの割合）。</param>
/// <param name="Down">倒れている（<see cref="Fighter.IsKnockedDown"/>）。</param>
public readonly record struct ReplaySideSample(float Consciousness, float Stamina, float Body, bool Down);

/// <summary>試合の展開の 1 点（<see cref="ReplayTimeline.SampleInterval"/> 秒ごと）。<c>RedWinChance</c> は観客の見込み p_red（<see cref="WinModel"/>、docs/EXCITEMENT.md §2）。</summary>
public readonly record struct ReplaySample(float Time, int Round, bool Break, ReplaySideSample Red, ReplaySideSample Blue, float RedWinChance);

/// <summary>シークのバーの印の種類。</summary>
public enum ReplayMarkKind
{
    /// <summary>当たった打撃（強さ = エネルギー [J]）。</summary>
    Hit,

    /// <summary>見どころ（<see cref="HighlightKind"/>、docs/EXCITEMENT.md §1）。</summary>
    Highlight,

    /// <summary>ラウンドの始まり（強さ = ラウンドの番号）。</summary>
    RoundStart,
}

/// <summary>シークのバーの 1 つの印。<paramref name="Red"/> は主役（打った側・見どころの主役）が赤か。</summary>
public readonly record struct ReplayMark(float Time, ReplayMarkKind Kind, HighlightKind Highlight, bool Red, float Strength, string Note);

/// <summary>
/// 走査が拾った試合の展開: 両者の状態の推移と、打撃・見どころ・ラウンドの印。走査のスレッドが足し、描画のスレッドが写しを読む
/// （<see cref="Version"/> が変わったときだけ写し直せばよい）。
/// </summary>
public sealed class ReplayTimeline
{
    /// <summary>状態を記録する間隔 [s]。シークのバーの 1 画素より細かければ足りる。</summary>
    public const float SampleInterval = 0.1f;

    private readonly object _gate = new();
    private readonly List<ReplaySample> _samples = [];
    private readonly List<ReplayMark> _marks = [];
    private ReplaySample[] _sampleCopy = [];
    private ReplayMark[] _markCopy = [];
    private int _copiedVersion = -1;
    private int _version;

    /// <summary>足されるたびに増える。</summary>
    public int Version { get { lock (_gate) { return _version; } } }

    public void AddSample(ReplaySample sample)
    {
        lock (_gate)
        {
            _samples.Add(sample);
            _version++;
        }
    }

    public void AddMark(ReplayMark mark)
    {
        lock (_gate)
        {
            // 見どころは数秒後に確定するものがある（打ち合いの鎖が閉じたとき）ので、時刻の順に差し込む。
            var index = _marks.Count;
            while (index > 0 && _marks[index - 1].Time > mark.Time)
            {
                index--;
            }

            _marks.Insert(index, mark);
            _version++;
        }
    }

    /// <summary>今までの記録の写し（同じ版なら前の写しを返す）。</summary>
    public (IReadOnlyList<ReplaySample> Samples, IReadOnlyList<ReplayMark> Marks, int Version) Snapshot()
    {
        lock (_gate)
        {
            if (_copiedVersion != _version)
            {
                _sampleCopy = [.. _samples];
                _markCopy = [.. _marks];
                _copiedVersion = _version;
            }

            return (_sampleCopy, _markCopy, _version);
        }
    }

    /// <summary><paramref name="time"/> より後の最初の見どころ（無ければ null）。打撃とラウンドの印は飛ばす。</summary>
    public ReplayMark? NextHighlight(float time, float minimumStrength = 0f) =>
        Snapshot().Marks.Where(m => m.Kind == ReplayMarkKind.Highlight && m.Time > time + 1e-3f && Weighted(m) >= minimumStrength)
            .Select(m => (ReplayMark?)m).FirstOrDefault();

    /// <summary><paramref name="time"/> より前の最後の見どころ（無ければ null）。</summary>
    public ReplayMark? PreviousHighlight(float time, float minimumStrength = 0f) =>
        Snapshot().Marks.Where(m => m.Kind == ReplayMarkKind.Highlight && m.Time < time - 1e-3f && Weighted(m) >= minimumStrength)
            .Select(m => (ReplayMark?)m).LastOrDefault();

    /// <summary>ラウンドの始まりの時刻の並び。</summary>
    public IReadOnlyList<float> RoundStarts() => [.. Snapshot().Marks.Where(m => m.Kind == ReplayMarkKind.RoundStart).Select(m => m.Time)];

    /// <summary>見どころの目立ち（種類の重み × 強さ。総合点と同じ重み: <see cref="ExcitementTuning.Weights"/>）。</summary>
    public static float Weighted(ReplayMark mark) => mark.Kind != ReplayMarkKind.Highlight ? 0f
        : ExcitementTuning.Default.Weights.TryGetValue(mark.Highlight, out var w) ? w * MathF.Min(mark.Strength, 3f) : mark.Strength;
}
