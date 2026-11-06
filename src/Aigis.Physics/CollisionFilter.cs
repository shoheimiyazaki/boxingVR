namespace Aigis.Physics;

// Internal, no Bepu types — stays trivially within the physics assembly.
#pragma warning disable CA1815 // Override equals on value types (never compared)

/// <summary>
/// Per-body collision filter (the RagdollDemo pattern). Bodies in different groups always
/// collide; within a group, membership/mask bits let connected parts (e.g. adjacent ragdoll
/// limbs) opt out of colliding with each other while still colliding with the world.
/// </summary>
internal struct SubgroupCollisionFilter
{
    public ushort SubgroupMembership;
    public ushort CollidableSubgroups;
    public int GroupId;

    /// <summary>A body that collides with everything (its own unique group).</summary>
    public SubgroupCollisionFilter(int groupId)
    {
        GroupId = groupId;
        SubgroupMembership = ushort.MaxValue;
        CollidableSubgroups = ushort.MaxValue;
    }

    /// <summary>A body in a shared group, occupying one subgroup bit (0..15).</summary>
    public SubgroupCollisionFilter(int groupId, int subgroupId)
    {
        // Out of range would shift the membership bit to 0 (collides with nothing) — reject it.
        ArgumentOutOfRangeException.ThrowIfNegative(subgroupId);
        ArgumentOutOfRangeException.ThrowIfGreaterThanOrEqual(subgroupId, 16);
        GroupId = groupId;
        SubgroupMembership = (ushort)(1 << subgroupId);
        CollidableSubgroups = ushort.MaxValue;
    }

    public static void DisableCollision(ref SubgroupCollisionFilter a, ref SubgroupCollisionFilter b)
    {
        a.CollidableSubgroups &= (ushort)~b.SubgroupMembership;
        b.CollidableSubgroups &= (ushort)~a.SubgroupMembership;
    }

    /// <summary>The inverse of <see cref="DisableCollision"/> — puts each side's membership bit back in
    /// the other's mask.
    /// <para>
    /// <b>Exact only when each body owns a distinct membership bit</b>, which is what the
    /// <c>(groupId, subgroupId)</c> constructor gives (one bit per body). Two bodies sharing a bit — or
    /// one built with the group-only constructor, whose membership is all 16 bits — cannot be separated
    /// again by this operation, because the bit that would be restored is not uniquely theirs. That is a
    /// property of the bitmask representation, not of this method: the disable was never per-PAIR, it was
    /// per-bit, so the inverse can only be as precise as the bits are.
    /// </para></summary>
    public static void EnableCollision(ref SubgroupCollisionFilter a, ref SubgroupCollisionFilter b)
    {
        a.CollidableSubgroups |= b.SubgroupMembership;
        b.CollidableSubgroups |= a.SubgroupMembership;
    }

    public static bool AllowCollision(in SubgroupCollisionFilter a, in SubgroupCollisionFilter b)
        => a.GroupId != b.GroupId || (a.CollidableSubgroups & b.SubgroupMembership) > 0;
}

#pragma warning restore CA1815
