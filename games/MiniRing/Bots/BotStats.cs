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

    /// <summary>守りを出さずに（出せずに）受けた打撃の数。</summary>
    public int HitsTakenOpen { get; private set; }

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

    internal void DefenseEnded(Defense defense, bool hit)
    {
        var c = _defenses.GetValueOrDefault(defense);
        _defenses[defense] = c with { Attempts = c.Attempts + 1, Failed = c.Failed + (hit ? 1 : 0) };
    }

    internal void TookOpenHit() => HitsTakenOpen++;

    internal void Clinched() => Clinches++;

    internal void TriedThrow() => ThrowAttempts++;

    internal void Pivoted() => Pivots++;

    public void Merge(BotStats other)
    {
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
