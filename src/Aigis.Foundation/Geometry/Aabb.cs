using System.Numerics;

namespace Aigis.Foundation.Geometry;

/// <summary>
/// Axis-aligned bounding box queries. Pure geometry — no rendering, no physics.
/// <para>
/// Exists because the slab test below had been hand-written three times over in Triage's highlight
/// code — the occluder set the shot solver reads and two of the instruments that review it — and a
/// hand-written copy is where a subtle difference hides: one of those copies returning the wrong
/// answer for a ray parallel to an axis, or for a start point already inside the box, is invisible
/// until a camera is looking at a wall it was told was not there. Two of the three now call this; the
/// third (<c>TriageHighlightReelRenderTests</c>'s centre-ray probe) wants the hit DISTANCE rather than
/// a yes/no and is left alone until this type grows that.
/// </para>
/// </summary>
public static class Aabb
{
    /// <summary>
    /// Whether the closed segment <paramref name="from"/>..<paramref name="to"/> touches the box.
    /// <para>
    /// The standard slab method, with two behaviours worth stating because callers depend on them:
    /// a segment that STARTS inside the box counts as intersecting, and a segment exactly parallel to
    /// an axis is handled by the degenerate branch rather than by dividing by zero.
    /// </para>
    /// </summary>
    public static bool IntersectsSegment(Vector3 from, Vector3 to, Vector3 min, Vector3 max)
    {
        var direction = to - from;
        var length = direction.Length();
        if (!float.IsFinite(length) || length < 1e-6f)
        {
            return Contains(from, min, max);
        }

        return IntersectsRay(from, direction / length, length, min, max);
    }

    /// <summary>Whether the ray from <paramref name="origin"/> along the unit vector
    /// <paramref name="direction"/> reaches the box within <paramref name="length"/> metres.</summary>
    public static bool IntersectsRay(Vector3 origin, Vector3 direction, float length, Vector3 min, Vector3 max)
    {
        var near = 0f;
        var far = length;
        for (var axis = 0; axis < 3; axis++)
        {
            var o = Component(origin, axis);
            var d = Component(direction, axis);
            var lo = Component(min, axis);
            var hi = Component(max, axis);
            if (MathF.Abs(d) < 1e-6f)
            {
                // Parallel to this slab: the segment can only intersect if it already lies between
                // the two faces, and no t-range can be derived from it.
                if (o < lo || o > hi)
                {
                    return false;
                }

                continue;
            }

            var t0 = (lo - o) / d;
            var t1 = (hi - o) / d;
            if (t0 > t1)
            {
                (t0, t1) = (t1, t0);
            }

            near = MathF.Max(near, t0);
            far = MathF.Min(far, t1);
            if (near > far)
            {
                return false;
            }
        }

        return true;
    }

    /// <summary>Whether the point is inside (or exactly on) the box.</summary>
    public static bool Contains(Vector3 point, Vector3 min, Vector3 max) =>
        point.X >= min.X && point.X <= max.X &&
        point.Y >= min.Y && point.Y <= max.Y &&
        point.Z >= min.Z && point.Z <= max.Z;

    private static float Component(Vector3 value, int axis) =>
        axis == 0 ? value.X : axis == 1 ? value.Y : value.Z;
}
