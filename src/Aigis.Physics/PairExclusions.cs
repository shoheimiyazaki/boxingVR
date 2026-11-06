namespace Aigis.Physics;

/// <summary>
/// The set of body pairs that never collide with each other, regardless of collision group.
///
/// <para><b>Why this exists alongside the subgroup filter.</b> <see cref="PhysicsWorld.DisableSelfCollision"/>
/// is Bepu's <c>SubgroupCollisionFilter</c>, and that mechanism is only expressive WITHIN one group: two
/// bodies in different groups always collide, and a body can be in exactly one group at a time. That is
/// the right shape for a ragdoll (one group, one bit per part) and the wrong shape for "this loose object
/// never touches that particular body" — the object and the body each already belong to groups of their
/// own that they cannot leave (see <c>TriageGame.CarryCollision.cs</c>, where the player's group is
/// already spoken for by whatever it is carrying).
/// </para>
///
/// <para><b>Kept as a separate object rather than a field on the callbacks struct</b> so that
/// <see cref="PhysicsWorld"/> owns the mutable state and the struct Bepu copies holds only a reference —
/// the same shape <c>ContactCollector</c> already uses.</para>
///
/// <para><b>Threading and determinism.</b> Reads happen in the narrow phase, which this engine steps
/// single-threaded (hard rule 5); writes must happen OUTSIDE a step, which is how every caller uses it
/// (a sever, a pickup). Membership is order-independent, so nothing here can make two runs of one seed
/// diverge.</para>
/// </summary>
internal sealed class PairExclusions
{
    private readonly HashSet<long> _pairs = [];

    /// <summary>Whether anything has been excluded at all — the fast path for the common case of a
    /// world that never uses this (every non-Triage game), so the narrow phase pays one bool check
    /// rather than a hash lookup per pair.</summary>
    public bool IsEmpty => _pairs.Count == 0;

    public void Set(int bodyA, int bodyB, bool collides)
    {
        var key = Key(bodyA, bodyB);
        if (collides)
        {
            _pairs.Remove(key);
        }
        else
        {
            _pairs.Add(key);
        }
    }

    public bool Excluded(int bodyA, int bodyB) => _pairs.Contains(Key(bodyA, bodyB));

    /// <summary>Order-independent key: a pair is a pair whichever way round it is asked about.</summary>
    private static long Key(int bodyA, int bodyB)
    {
        var low = Math.Min(bodyA, bodyB);
        var high = Math.Max(bodyA, bodyB);
        return ((long)high << 32) | (uint)low;
    }
}
