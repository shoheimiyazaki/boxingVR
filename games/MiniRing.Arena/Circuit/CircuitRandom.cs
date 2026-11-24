namespace MiniRing.Arena.Circuit;

/// <summary>
/// 新人リーグの種付きの乱数（SplitMix64）。プロセス・実行環境によらず同じ種なら同じ並び（CLAUDE.md §1.6。<see cref="System.Random"/> の実装や
/// <c>string.GetHashCode</c> のような、版・プロセスで変わり得る物を使わない）。
/// </summary>
internal sealed class CircuitRandom
{
    private ulong _state;

    public CircuitRandom(ulong seed) => _state = seed;

    /// <summary>いくつかの値を混ぜた種（生成の番号・興行の番号・階級…から 1 つの種を作る）。</summary>
    public static ulong Mix(params ulong[] values)
    {
        var h = 0x9E3779B97F4A7C15UL;
        foreach (var v in values)
        {
            h = Scramble(h ^ Scramble(v + 0x9E3779B97F4A7C15UL));
        }

        return h;
    }

    /// <summary>文字列の安定な値（FNV-1a）。</summary>
    public static ulong Hash(string text)
    {
        var h = 14695981039346656037UL;
        foreach (var ch in text)
        {
            h = (h ^ ch) * 1099511628211UL;
        }

        return h;
    }

    public ulong NextULong()
    {
        _state += 0x9E3779B97F4A7C15UL;
        return Scramble(_state);
    }

    /// <summary>[0, 1) の一様。</summary>
    public double NextDouble() => (NextULong() >> 11) * (1.0 / (1UL << 53));

    /// <summary>[min, max] の整数（両端を含む）。</summary>
    public int Range(int min, int max) => min + (int)(NextULong() % (ulong)(max - min + 1));

    /// <summary>[min, max) の実数。</summary>
    public double Range(double min, double max) => min + ((max - min) * NextDouble());

    /// <summary>標準正規（Box–Muller）。</summary>
    public double Normal()
    {
        var u1 = 1.0 - NextDouble();
        var u2 = NextDouble();
        return Math.Sqrt(-2.0 * Math.Log(u1)) * Math.Cos(2.0 * Math.PI * u2);
    }

    /// <summary>±<paramref name="cut"/> で切った標準正規（外れたら引き直す）。</summary>
    public double TruncatedNormal(double cut)
    {
        while (true)
        {
            var z = Normal();
            if (Math.Abs(z) <= cut)
            {
                return z;
            }
        }
    }

    public T Pick<T>(IReadOnlyList<T> items) => items[Range(0, items.Count - 1)];

    /// <summary>重み付きで 1 つ（鍵の順 = 序数の順で並べてから引く。辞書の並びに依らない）。</summary>
    public string Weighted(IReadOnlyDictionary<string, float> weights)
    {
        var ordered = weights.OrderBy(p => p.Key, StringComparer.Ordinal).ToList();
        var total = ordered.Sum(p => (double)p.Value);
        var x = NextDouble() * total;
        foreach (var (key, w) in ordered)
        {
            x -= w;
            if (x < 0)
            {
                return key;
            }
        }

        return ordered[^1].Key;
    }

    /// <summary>並びを混ぜた写し（Fisher–Yates）。</summary>
    public IReadOnlyList<T> Shuffle<T>(IEnumerable<T> items)
    {
        var list = items.ToList();
        for (var i = list.Count - 1; i > 0; i--)
        {
            var j = Range(0, i);
            (list[i], list[j]) = (list[j], list[i]);
        }

        return list;
    }

    private static ulong Scramble(ulong z)
    {
        z = (z ^ (z >> 30)) * 0xBF58476D1CE4E5B9UL;
        z = (z ^ (z >> 27)) * 0x94D049BB133111EBUL;
        return z ^ (z >> 31);
    }
}
