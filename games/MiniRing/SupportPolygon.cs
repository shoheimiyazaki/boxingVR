using System.Numerics;

namespace MiniRing;

/// <summary>
/// 床に着いている所（足の裏・手・膝・尻…）の水平面の凸包（支持多角形）。床が体を押せる力の作用点（圧力中心 CoP）は
/// この中にしか来ない。点は XZ 平面（Vector2 の X = ワールド X、Y = ワールド Z）。点の数は少ない（1 体で 100 程度）ので、
/// 毎歩作り直す。配列は使い回す（決定的: 入力の順に依らず同じ凸包になる。並べ替えは座標の全順序）。
/// </summary>
public sealed class SupportPolygon
{
    private readonly List<Vector2> _points = [];
    private readonly List<Vector2> _hull = [];

    /// <summary>凸包の頂点（反時計回り）。0 個なら床に何も着いていない。</summary>
    public IReadOnlyList<Vector2> Hull => _hull;

    public bool IsEmpty => _hull.Count == 0;

    public void Clear()
    {
        _points.Clear();
        _hull.Clear();
    }

    public void Add(Vector2 point) => _points.Add(point);

    /// <summary>足した点から凸包を作る（Andrew の単調連鎖）。</summary>
    public void Build()
    {
        _hull.Clear();
        _points.Sort(static (a, b) => a.X != b.X ? a.X.CompareTo(b.X) : a.Y.CompareTo(b.Y));
        if (_points.Count <= 2)
        {
            foreach (var p in _points)
            {
                if (_hull.Count == 0 || _hull[^1] != p)
                {
                    _hull.Add(p);
                }
            }

            return;
        }

        // 下側の鎖
        foreach (var p in _points)
        {
            while (_hull.Count >= 2 && Cross(_hull[^2], _hull[^1], p) <= 0f)
            {
                _hull.RemoveAt(_hull.Count - 1);
            }

            _hull.Add(p);
        }

        // 上側の鎖
        var lower = _hull.Count + 1;
        for (var i = _points.Count - 2; i >= 0; i--)
        {
            var p = _points[i];
            while (_hull.Count >= lower && Cross(_hull[^2], _hull[^1], p) <= 0f)
            {
                _hull.RemoveAt(_hull.Count - 1);
            }

            _hull.Add(p);
        }

        _hull.RemoveAt(_hull.Count - 1); // 始点が最後に重なる
    }

    /// <summary>点が凸包の中（辺の上を含む）か。頂点が 3 つ未満（点・線分）なら中は無い。</summary>
    public bool Contains(Vector2 p)
    {
        if (_hull.Count < 3)
        {
            return false;
        }

        for (var i = 0; i < _hull.Count; i++)
        {
            if (Cross(_hull[i], _hull[(i + 1) % _hull.Count], p) < 0f)
            {
                return false;
            }
        }

        return true;
    }

    /// <summary>凸包の中で <paramref name="p"/> に最も近い点（中なら p そのもの）。凸包が空なら p。</summary>
    public Vector2 Clamp(Vector2 p)
    {
        if (_hull.Count == 0 || Contains(p))
        {
            return p;
        }

        if (_hull.Count == 1)
        {
            return _hull[0];
        }

        var best = _hull[0];
        var bestDistance = float.PositiveInfinity;
        var edges = _hull.Count == 2 ? 1 : _hull.Count;
        for (var i = 0; i < edges; i++)
        {
            var a = _hull[i];
            var b = _hull[(i + 1) % _hull.Count];
            var ab = b - a;
            var t = Math.Clamp(Vector2.Dot(p - a, ab) / MathF.Max(1e-12f, ab.LengthSquared()), 0f, 1f);
            var q = a + (ab * t);
            var d = Vector2.DistanceSquared(p, q);
            if (d < bestDistance)
            {
                bestDistance = d;
                best = q;
            }
        }

        return best;
    }

    /// <summary>
    /// 点が凸包の外へ出ている距離（中なら負: 最も近い辺までの距離に負号）。凸包が空なら +∞。
    /// 重心がどれだけ支えの外へ出たか（崩れの深さ）を測るのに使う。
    /// </summary>
    public float SignedDistance(Vector2 p)
    {
        if (_hull.Count == 0)
        {
            return float.PositiveInfinity;
        }

        if (!Contains(p))
        {
            return Vector2.Distance(p, Clamp(p));
        }

        var inside = float.PositiveInfinity;
        for (var i = 0; i < _hull.Count; i++)
        {
            var a = _hull[i];
            var b = _hull[(i + 1) % _hull.Count];
            var ab = b - a;
            var length = ab.Length();
            if (length > 1e-9f)
            {
                inside = MathF.Min(inside, Cross(a, b, p) / length);
            }
        }

        return -inside;
    }

    private static float Cross(Vector2 o, Vector2 a, Vector2 b) => ((a.X - o.X) * (b.Y - o.Y)) - ((a.Y - o.Y) * (b.X - o.X));
}

/// <summary>立たせる力の制限のしかた（docs/GRAPPLING.md §7）。</summary>
public enum SupportModel
{
    /// <summary>立っている間、起こすねじりを CoP が支持多角形の中で出せる分に限る（硬い制限）。</summary>
    Cop,

    /// <summary>立たせる力すべてに、捕捉点の外れから出す支えの残り（<see cref="Fighter.Balance"/>）を掛ける（連続、どの状態でも）。</summary>
    Soft,

    /// <summary>Soft の後に、立っている間だけ Cop の制限もかける。</summary>
    SoftCop,
}

/// <summary>連続の釣り合いの支えの領域。</summary>
public enum SupportRegion
{
    /// <summary>床に着いた所の凸包 ＋ 踏み出している足の着地点。</summary>
    Polygon,

    /// <summary>立っている間、両足の母指球を直径の両端とする円（足の長さの半分だけ大きく）。ほかの状態は凸包。</summary>
    Disk,
}
