using System.Numerics;

namespace Aigis.Foundation.Geometry;

/// <summary>
/// Centripetal Catmull-Rom spline through a set of control points. Pure geometry.
/// Centripetal parameterisation (alpha = 0.5) avoids the cusps / self-intersections
/// the uniform variant produces on unevenly spaced points.
/// </summary>
public static class CatmullRomSpline
{
    /// <summary>
    /// Sample the spline through <paramref name="pts"/>. The control points are
    /// included; each segment gets <paramref name="samplesPerSegment"/>
    /// interior steps. 0/1 points return as-is; 2 points return the straight
    /// segment (a single span needs no curvature). <c>null</c> is treated the
    /// same as an empty list (returns an empty result, does not throw).
    /// </summary>
    public static List<Vector3> Sample(IReadOnlyList<Vector3> pts, int samplesPerSegment)
    {
        var outp = new List<Vector3>();
        if (pts is null || pts.Count == 0)
        {
            return outp;
        }

        if (pts.Count <= 2)
        {
            foreach (var p in pts)
            {
                outp.Add(p);
            }

            return outp;
        }

        int perSeg = Math.Max(1, samplesPerSegment);
        outp.Add(pts[0]);
        for (int i = 0; i < pts.Count - 1; i++)
        {
            Vector3 p0 = pts[i == 0 ? 0 : i - 1];
            Vector3 p1 = pts[i];
            Vector3 p2 = pts[i + 1];
            Vector3 p3 = pts[Math.Min(i + 2, pts.Count - 1)];

            float t0 = 0f;
            float t1 = t0 + Knot(p0, p1);
            float t2 = t1 + Knot(p1, p2);
            float t3 = t2 + Knot(p2, p3);

            for (int s = 1; s <= perSeg; s++)
            {
                float u = (float)s / perSeg;
                float t = t1 + u * (t2 - t1);
                outp.Add(Interp(p0, p1, p2, p3, t0, t1, t2, t3, t));
            }
        }

        return outp;
    }

    /// <summary>
    /// Arc length of the spline through <paramref name="pts"/>, summed over the
    /// sampled chords. Higher <paramref name="samplesPerSegment"/> converges to
    /// the true length.
    /// </summary>
    public static float ArcLength(IReadOnlyList<Vector3> pts, int samplesPerSegment = 24)
    {
        var s = Sample(pts, samplesPerSegment);
        float len = 0f;
        for (int i = 1; i < s.Count; i++)
        {
            len += Vector3.Distance(s[i - 1], s[i]);
        }

        return len;
    }

    // Centripetal knot spacing: dt = |dp|^0.5.
    private static float Knot(Vector3 a, Vector3 b)
        => MathF.Sqrt(MathF.Max(Vector3.Distance(a, b), 1e-6f));

    // Barry-Goldman pyramidal evaluation of the non-uniform Catmull-Rom segment.
    private static Vector3 Interp(Vector3 p0, Vector3 p1, Vector3 p2, Vector3 p3,
                                  float t0, float t1, float t2, float t3, float t)
    {
        Vector3 a1 = Lerp(p0, p1, t0, t1, t);
        Vector3 a2 = Lerp(p1, p2, t1, t2, t);
        Vector3 a3 = Lerp(p2, p3, t2, t3, t);
        Vector3 b1 = Lerp(a1, a2, t0, t2, t);
        Vector3 b2 = Lerp(a2, a3, t1, t3, t);
        return Lerp(b1, b2, t1, t2, t);
    }

    private static Vector3 Lerp(Vector3 a, Vector3 b, float ta, float tb, float t)
    {
        if (MathF.Abs(tb - ta) < 1e-9f)
        {
            return a;
        }

        float f = (t - ta) / (tb - ta);
        return a + (b - a) * f;
    }
}
