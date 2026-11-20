using System.Numerics;

namespace MiniRing.Arena;

/// <summary>
/// 棒人形を立体で描く CPU のレイキャスト（2026-11-18、ユーザーの指定: 棒を円柱で、陰影と透視投影で立体的に）。
/// 体の節・剣・軌跡は両端の丸い円柱（カプセル）、関節は球。画素ごとに視線と形の交点を解析的に解き（Inigo Quilez のカプセルの交差）、
/// 深さのバッファで前後を決め、拡散 + 鏡面 + 空の光と、縁を暗くする陰影を付ける。床は市松で、体の影（光の向きに床へ写した円柱）を落とす。
/// 断面は半透明の円盤で重ねる。縦横 <c>supersample</c> 倍で描いて平均し、縁のギザギザを消す。GPU は使わない（見た目だけ。試合に響かない）。
/// </summary>
internal sealed class StickSolidRenderer
{
    private const float FarAway = float.PositiveInfinity;

    private readonly int _width;
    private readonly int _height;
    private readonly int _supersample;
    private readonly Vector3[] _color;
    private readonly float[] _depth;
    private Vector3 _eye;
    private Vector3 _forward;
    private Vector3 _right;
    private Vector3 _up;
    private float _focal;

    public StickSolidRenderer(int width, int height, int supersample = 2)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(width, 1);
        ArgumentOutOfRangeException.ThrowIfLessThan(height, 1);
        ArgumentOutOfRangeException.ThrowIfLessThan(supersample, 1);
        _supersample = supersample;
        _width = width * supersample;
        _height = height * supersample;
        _color = new Vector3[_width * _height];
        _depth = new float[_width * _height];
    }

    /// <summary>光の来る向き（面から光へ。右前の上）。</summary>
    public Vector3 Light { get; } = Vector3.Normalize(new Vector3(-0.35f, 1f, 0.55f));

    /// <summary>カメラを置いて、背景（上が明るい灰色の階調）と深さを消す。<paramref name="fovDegrees"/> は縦の画角。</summary>
    public void Begin(Vector3 eye, Vector3 target, float fovDegrees)
    {
        _eye = eye;
        _forward = Vector3.Normalize(target - eye);
        var hint = MathF.Abs(_forward.Y) > 0.95f ? Vector3.UnitZ : Vector3.UnitY;
        _right = Vector3.Normalize(Vector3.Cross(_forward, hint));
        _up = Vector3.Cross(_right, _forward);
        _focal = _height * 0.5f / MathF.Tan(fovDegrees * MathF.PI / 360f);
        for (var y = 0; y < _height; y++)
        {
            var shade = Vector3.Lerp(new Vector3(0.93f, 0.94f, 0.96f), new Vector3(0.80f, 0.81f, 0.84f), y / (float)_height);
            Array.Fill(_color, shade, y * _width, _width);
        }

        Array.Fill(_depth, FarAway);
    }

    /// <summary>両端の丸い円柱（<paramref name="a"/> から <paramref name="b"/>、半径 <paramref name="radius"/>）。a = b なら球。</summary>
    public void Capsule(Vector3 a, Vector3 b, float radius, Vector3 color, float gloss = 0.25f)
    {
        if (!Bounds(a, b, radius, out var x0, out var x1, out var y0, out var y1))
        {
            return;
        }

        for (var y = y0; y <= y1; y++)
        {
            for (var x = x0; x <= x1; x++)
            {
                var rd = Ray(x, y);
                var t = IntersectCapsule(_eye, rd, a, b, radius);
                var at = (y * _width) + x;
                if (t <= 0f || t >= _depth[at])
                {
                    continue;
                }

                var p = _eye + (rd * t);
                _depth[at] = t;
                _color[at] = Shade(CapsuleNormal(p, a, b, radius), rd, color, gloss);
            }
        }
    }

    public void Sphere(Vector3 centre, float radius, Vector3 color, float gloss = 0.35f) => Capsule(centre, centre, radius, color, gloss);

    /// <summary>
    /// 高さ <paramref name="height"/> の床（市松、1.41 m の升）を、まだ何も描いていない画素と床より奥の画素へ描き、<paramref name="casters"/> の影を落とす。
    /// 影 = 円柱の両端を光の向きに床へ写した線分からの距離が半径より近い所（縁は柔らかく）。
    /// </summary>
    public void Floor(float height, IReadOnlyList<(Vector3 A, Vector3 B, float Radius)> casters)
    {
        ArgumentNullException.ThrowIfNull(casters);
        var shadows = casters.Select(c => (A: OnFloor(c.A, height), B: OnFloor(c.B, height), c.Radius)).ToArray();
        Parallel.For(0, _height, y =>
        {
            for (var x = 0; x < _width; x++)
            {
                var rd = Ray(x, y);
                if (rd.Y >= -1e-4f)
                {
                    continue;
                }

                var t = (height - _eye.Y) / rd.Y;
                var at = (y * _width) + x;
                if (t <= 0f || t >= _depth[at])
                {
                    continue;
                }

                var p = _eye + (rd * t);
                var checker = ((int)MathF.Floor(p.X * 0.71f) + (int)MathF.Floor(p.Z * 0.71f)) % 2 == 0; // 1.41 m 角の市松
                var floor = checker ? new Vector3(0.84f, 0.83f, 0.80f) : new Vector3(0.76f, 0.75f, 0.72f);
                var shadow = 0f;
                var q = new Vector2(p.X, p.Z);
                foreach (var (sa, sb, r) in shadows)
                {
                    var d = DistanceToSegment(q, sa, sb);
                    shadow = MathF.Max(shadow, 1f - SmoothStep(r * 0.6f, r * 1.3f, d));
                }

                floor *= 1f - (0.45f * shadow);
                var fade = SmoothStep(11.27f, 25.35f, t); // 遠い床は背景へ溶かす
                _color[at] = Vector3.Lerp(floor, _color[at], fade);
                _depth[at] = t;
            }
        });
    }

    /// <summary>半透明の円盤（断面）。手前に物があればその物は覆わない。0.704 m ごとの同心の輪と縁を少し濃く。</summary>
    public void Disc(Vector3 centre, Vector3 normal, float radius, Vector3 tint, float alpha)
    {
        var n = Vector3.Normalize(normal);
        Parallel.For(0, _height, y =>
        {
            for (var x = 0; x < _width; x++)
            {
                var rd = Ray(x, y);
                var denom = Vector3.Dot(rd, n);
                if (MathF.Abs(denom) < 1e-5f)
                {
                    continue;
                }

                var t = Vector3.Dot(centre - _eye, n) / denom;
                var at = (y * _width) + x;
                if (t <= 0f || t >= _depth[at])
                {
                    continue;
                }

                var r = Vector3.Distance(_eye + (rd * t), centre);
                if (r > radius)
                {
                    continue;
                }

                var ring = MathF.Abs((r / 0.704f) - MathF.Round(r / 0.704f)) < 0.02f || r > radius * 0.985f;
                _color[at] = Vector3.Lerp(_color[at], tint, ring ? alpha * 2.5f : alpha);
            }
        });
    }

    /// <summary>平均して縮め、RGB の画素の並び（幅 <paramref name="stride"/> 画素）の (<paramref name="left"/>, <paramref name="top"/>) へ写す。</summary>
    public void CopyTo(byte[] rgb, int stride, int left, int top)
    {
        ArgumentNullException.ThrowIfNull(rgb);
        var s = _supersample;
        var w = _width / s;
        var h = _height / s;
        var weight = 1f / (s * s);
        for (var y = 0; y < h; y++)
        {
            for (var x = 0; x < w; x++)
            {
                var sum = Vector3.Zero;
                for (var dy = 0; dy < s; dy++)
                {
                    for (var dx = 0; dx < s; dx++)
                    {
                        sum += _color[(((y * s) + dy) * _width) + (x * s) + dx];
                    }
                }

                var c = Vector3.Clamp(sum * weight, Vector3.Zero, Vector3.One) * 255f;
                var at = (((top + y) * stride) + left + x) * 3;
                (rgb[at], rgb[at + 1], rgb[at + 2]) = ((byte)c.X, (byte)c.Y, (byte)c.Z);
            }
        }
    }

    private Vector3 Ray(int x, int y)
        => Vector3.Normalize((_forward * _focal) + (_right * (x + 0.5f - (_width * 0.5f))) + (_up * ((_height * 0.5f) - y - 0.5f)));

    /// <summary>円柱を写す画面の矩形（端の点を写し、写した半径だけ広げる）。カメラの後ろにあれば描かない。</summary>
    private bool Bounds(Vector3 a, Vector3 b, float radius, out int x0, out int x1, out int y0, out int y1)
    {
        (x0, x1, y0, y1) = (0, -1, 0, -1);
        var za = Vector3.Dot(a - _eye, _forward);
        var zb = Vector3.Dot(b - _eye, _forward);
        var near = MathF.Min(za, zb) - radius;
        if (near < 0.141f)
        {
            return false;
        }

        Vector2 P(Vector3 p, float z) => new((_width * 0.5f) + (Vector3.Dot(p - _eye, _right) / z * _focal), (_height * 0.5f) - (Vector3.Dot(p - _eye, _up) / z * _focal));
        var pa = P(a, za);
        var pb = P(b, zb);
        var pad = (radius * _focal / near) + 2f;
        x0 = Math.Max(0, (int)MathF.Floor(MathF.Min(pa.X, pb.X) - pad));
        x1 = Math.Min(_width - 1, (int)MathF.Ceiling(MathF.Max(pa.X, pb.X) + pad));
        y0 = Math.Max(0, (int)MathF.Floor(MathF.Min(pa.Y, pb.Y) - pad));
        y1 = Math.Min(_height - 1, (int)MathF.Ceiling(MathF.Max(pa.Y, pb.Y) + pad));
        return x0 <= x1 && y0 <= y1;
    }

    private Vector3 Shade(Vector3 n, Vector3 rd, Vector3 color, float gloss)
    {
        var diffuse = MathF.Max(0f, Vector3.Dot(n, Light));
        var sky = 0.5f + (0.5f * n.Y);
        var half = Vector3.Normalize(Light - rd);
        var specular = gloss * MathF.Pow(MathF.Max(0f, Vector3.Dot(n, half)), 32f);
        var edge = 0.55f + (0.45f * MathF.Abs(Vector3.Dot(n, rd))); // 縁を暗くして輪郭を出す
        return (color * (0.22f + (0.18f * sky) + (0.7f * diffuse)) * edge) + new Vector3(specular);
    }

    private Vector2 OnFloor(Vector3 p, float height)
    {
        var drop = (p.Y - height) / Light.Y;
        var q = p - (Light * drop);
        return new Vector2(q.X, q.Z);
    }

    /// <summary>視線（原点 <paramref name="ro"/>、単位の向き <paramref name="rd"/>）とカプセルの最初の交点までの距離。無ければ −1。</summary>
    internal static float IntersectCapsule(Vector3 ro, Vector3 rd, Vector3 a, Vector3 b, float radius)
    {
        var ba = b - a;
        var oa = ro - a;
        var baba = Vector3.Dot(ba, ba);
        if (baba < 1e-10f)
        {
            return IntersectSphere(ro, rd, a, radius);
        }

        var bard = Vector3.Dot(ba, rd);
        var baoa = Vector3.Dot(ba, oa);
        var rdoa = Vector3.Dot(rd, oa);
        var oaoa = Vector3.Dot(oa, oa);
        var qa = baba - (bard * bard);
        var qb = (baba * rdoa) - (baoa * bard);
        var qc = (baba * oaoa) - (baoa * baoa) - (radius * radius * baba);
        var h = (qb * qb) - (qa * qc);
        if (qa > 1e-10f && h >= 0f)
        {
            var t = (-qb - MathF.Sqrt(h)) / qa;
            var y = baoa + (t * bard);
            if (y > 0f && y < baba)
            {
                return t;
            }
        }

        var t0 = IntersectSphere(ro, rd, a, radius);
        var t1 = IntersectSphere(ro, rd, b, radius);
        return t0 < 0f ? t1 : t1 < 0f ? t0 : MathF.Min(t0, t1);
    }

    private static float IntersectSphere(Vector3 ro, Vector3 rd, Vector3 c, float radius)
    {
        var oc = ro - c;
        var b = Vector3.Dot(oc, rd);
        var h = (b * b) - (Vector3.Dot(oc, oc) - (radius * radius));
        return h < 0f ? -1f : -b - MathF.Sqrt(h);
    }

    private static Vector3 CapsuleNormal(Vector3 p, Vector3 a, Vector3 b, float radius)
    {
        var ba = b - a;
        var pa = p - a;
        var baba = Vector3.Dot(ba, ba);
        var h = baba < 1e-10f ? 0f : Math.Clamp(Vector3.Dot(pa, ba) / baba, 0f, 1f);
        return (pa - (ba * h)) / radius;
    }

    private static float DistanceToSegment(Vector2 p, Vector2 a, Vector2 b)
    {
        var ab = b - a;
        var len = Vector2.Dot(ab, ab);
        var h = len < 1e-10f ? 0f : Math.Clamp(Vector2.Dot(p - a, ab) / len, 0f, 1f);
        return Vector2.Distance(p, a + (ab * h));
    }

    private static float SmoothStep(float edge0, float edge1, float x)
    {
        var t = Math.Clamp((x - edge0) / (edge1 - edge0), 0f, 1f);
        return t * t * (3f - (2f * t));
    }
}
