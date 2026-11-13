namespace MiniRing.Bots;

/// <summary>
/// ボットが試合中に数える成績（改善の材料）: 技ごとの手数・命中・ダメージ、守りごとの成否、カウンター・クリンチ・投げ。
/// 1 ボット 1 ラウンドぶんを数え、<see cref="Merge"/> で足し合わせて集計する。
/// </summary>
public sealed class BotStats
{
    private readonly Dictionary<Technique, TechniqueCount> _techniques = [];
    private readonly Dictionary<Defense, DefenseCount> _defenses = [];
    private readonly Dictionary<string, int> _combos = [];

    public IReadOnlyDictionary<Technique, TechniqueCount> Techniques => _techniques;
    public IReadOnlyDictionary<Defense, DefenseCount> Defenses => _defenses;
    public IReadOnlyDictionary<string, int> Combos => _combos;
    public int CountersThrown { get; private set; }
    public int CountersLanded { get; private set; }
    public int Clinches { get; private set; }
    public int ThrowAttempts { get; private set; }
    public int Pivots { get; private set; }

    /// <summary>予測で（拳が出る前に）始めた守りの数・そのうち打たれた数・拳が来なかった（空振りの）数。</summary>
    public int Predictions { get; private set; }
    public int PredictionsHit { get; private set; }
    public int PredictionsIdle { get; private set; }

    /// <summary>守りを出さずに（出せずに）受けた打撃の数。</summary>
    public int HitsTakenOpen { get; private set; }

    /// <summary>立っていた時間と、そのうち頭を沈めて（しゃがんで）いた時間 [s]、続けてしゃがんでいた最長の時間 [s]。</summary>
    public float Time { get; private set; }
    public float CrouchTime { get; private set; }
    public float LongestCrouch { get; private set; }
    private float _crouchRun;

    /// <summary>しゃがんでいた時間の割合。</summary>
    public float CrouchShare => Time > 0f ? CrouchTime / Time : 0f;

    /// <summary>しゃがんでいた時間のうち、守りの最中・打っている最中だった時間 [s]（どこで沈んでいるかを見る）。</summary>
    public float CrouchDefending { get; private set; }
    public float CrouchPunching { get; private set; }

    /// <summary>危ない所（間合いの層の危険度）にいた時間 [s]（危険度で重み付け）。</summary>
    public float DangerTime { get; private set; }

    /// <summary>そのうち、自分から打っていない間に危ない所にいた時間 [s]（間合いの層の立ち位置が変えるのはここ）。</summary>
    public float IdleDangerTime { get; private set; }

    /// <summary>当てずっぽうで入れた回避の数（間合いの層）。</summary>
    public int Guesses { get; private set; }

    internal void InRange(float danger, bool attacking, float dt)
    {
        DangerTime += danger * dt;
        IdleDangerTime += attacking ? 0f : danger * dt;
    }

    internal void Guessed() => Guesses++;

    internal void Tick(float dt, bool crouched, bool defending = false, bool punching = false)
    {
        Time += dt;
        CrouchTime += crouched ? dt : 0f;
        CrouchDefending += crouched && defending ? dt : 0f;
        CrouchPunching += crouched && punching && !defending ? dt : 0f;
        _crouchRun = crouched ? _crouchRun + dt : 0f;
        LongestCrouch = MathF.Max(LongestCrouch, _crouchRun);
    }

    internal void Thrown(Technique technique, bool counter)
    {
        _techniques[technique] = Get(technique) with { Thrown = Get(technique).Thrown + 1 };
        CountersThrown += counter ? 1 : 0;
    }

    internal void Landed(Technique technique, float damage, bool counter)
    {
        var c = Get(technique);
        _techniques[technique] = c with { Landed = c.Landed + 1, Damage = c.Damage + damage };
        CountersLanded += counter ? 1 : 0;
    }

    internal void ComboStarted(string name) => _combos[name] = _combos.GetValueOrDefault(name) + 1;

    private readonly Dictionary<AimGoal, int> _aims = [];

    /// <summary>狙いどころごとの、狙って出した拳の数（真ん中以外）。</summary>
    public IReadOnlyDictionary<AimGoal, int> Aims => _aims;

    internal void Aimed(AimGoal goal) => _aims[goal] = _aims.GetValueOrDefault(goal) + 1;

    internal void DefenseEnded(Defense defense, bool hit)
    {
        var c = _defenses.GetValueOrDefault(defense);
        _defenses[defense] = c with { Attempts = c.Attempts + 1, Failed = c.Failed + (hit ? 1 : 0) };
    }

    internal void TookOpenHit() => HitsTakenOpen++;

    internal void Predicted(bool hit, bool punchCame)
    {
        Predictions++;
        PredictionsHit += hit ? 1 : 0;
        PredictionsIdle += punchCame ? 0 : 1;
    }

    internal void Clinched() => Clinches++;

    internal void TriedThrow() => ThrowAttempts++;

    internal void Pivoted() => Pivots++;

    public void Merge(BotStats other)
    {
        ArgumentNullException.ThrowIfNull(other);
        foreach (var (goal, count) in other._aims)
        {
            _aims[goal] = _aims.GetValueOrDefault(goal) + count;
        }

        ArgumentNullException.ThrowIfNull(other);
        foreach (var (k, v) in other._techniques)
        {
            var c = Get(k);
            _techniques[k] = new TechniqueCount(c.Thrown + v.Thrown, c.Landed + v.Landed, c.Damage + v.Damage);
        }

        foreach (var (k, v) in other._defenses)
        {
            var c = _defenses.GetValueOrDefault(k);
            _defenses[k] = new DefenseCount(c.Attempts + v.Attempts, c.Failed + v.Failed);
        }

        DangerTime += other.DangerTime;
        IdleDangerTime += other.IdleDangerTime;
        Guesses += other.Guesses;

        foreach (var (k, v) in other._combos)
        {
            _combos[k] = _combos.GetValueOrDefault(k) + v;
        }

        CountersThrown += other.CountersThrown;
        CountersLanded += other.CountersLanded;
        Clinches += other.Clinches;
        ThrowAttempts += other.ThrowAttempts;
        Pivots += other.Pivots;
        HitsTakenOpen += other.HitsTakenOpen;
        Predictions += other.Predictions;
        PredictionsHit += other.PredictionsHit;
        PredictionsIdle += other.PredictionsIdle;
        Time += other.Time;
        CrouchTime += other.CrouchTime;
        CrouchDefending += other.CrouchDefending;
        CrouchPunching += other.CrouchPunching;
        LongestCrouch = MathF.Max(LongestCrouch, other.LongestCrouch);
    }

    private TechniqueCount Get(Technique technique) => _techniques.GetValueOrDefault(technique);
}

public readonly record struct TechniqueCount(int Thrown, int Landed, float Damage)
{
    public float Accuracy => Thrown > 0 ? (float)Landed / Thrown : 0f;
}

/// <summary><paramref name="Failed"/> は守りの最中に打たれた回数。</summary>
public readonly record struct DefenseCount(int Attempts, int Failed)
{
    public float SuccessRate => Attempts > 0 ? 1f - ((float)Failed / Attempts) : 0f;
}
