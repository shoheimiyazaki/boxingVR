namespace MiniRing.Replay;

/// <summary>照合の結果: 何ステップ再生したか、チェックサムをいくつ照合したか、最初にずれたステップ（無ければ null）。</summary>
public readonly record struct ReplayVerification(int StepsPlayed, int ChecksumsMatched, int? FirstDivergence)
{
    public bool Reproduced => FirstDivergence is null;
}

/// <summary>
/// 記録から試合を作り直して 1 ステップずつ再生する。記録時と同じ種・同じ調整値・同じ CPU で <see cref="BoxingGame"/> を作り、
/// 同じ入力と時間刻みを順に与える。途中のチェックサムと照合して、記録と同じ動きかを確かめる。
/// </summary>
public sealed class ReplayPlayer : IDisposable
{
    private readonly ReplayData _data;

    public ReplayPlayer(ReplayData data)
    {
        ArgumentNullException.ThrowIfNull(data);
        _data = data;
        Game = new BoxingGame(data.Header.Tuning, data.Header.Seed, data.Header.Corners, data.Header.MatchSettings?.Rules);
    }

    public BoxingGame Game { get; }
    public ReplayData Data => _data;
    public int Position { get; private set; }
    public float Time { get; private set; }
    public bool Finished => Position >= _data.Steps.Count;
    public int ChecksumsMatched { get; private set; }
    public int? FirstDivergence { get; private set; }

    /// <summary>次の 1 ステップを再生する。終わっていれば何もしない。</summary>
    public void StepOnce()
    {
        if (Finished)
        {
            return;
        }

        var step = _data.Steps[Position];
        Game.Step(step.Input, step.Dt);
        Position++;
        Time += step.Dt;
        if (_data.Checksums.TryGetValue(Position, out var expected))
        {
            if (Game.StateChecksum() == expected)
            {
                ChecksumsMatched++;
            }
            else
            {
                FirstDivergence ??= Position;
            }
        }
    }

    /// <summary>試合の時計が <paramref name="time"/> に追いつくまで再生する（実時間での再生用）。</summary>
    /// <param name="time">ここまで進める [s]。</param>
    /// <param name="afterStep">1 ステップ進めるたびに呼ぶ（そのステップの打撃を拾う、など）。</param>
    public void PlayUntil(float time, Action<BoxingGame>? afterStep = null)
    {
        while (!Finished && Time + _data.Steps[Position].Dt <= time)
        {
            StepOnce();
            afterStep?.Invoke(Game);
        }
    }

    /// <summary>最後まで最速で再生して、記録どおりに再現できたかを返す。</summary>
    public static ReplayVerification Verify(ReplayData data)
    {
        using var player = new ReplayPlayer(data);
        while (!player.Finished)
        {
            player.StepOnce();
        }

        return new ReplayVerification(player.Position, player.ChecksumsMatched, player.FirstDivergence);
    }

    public void Dispose() => Game.Dispose();
}
