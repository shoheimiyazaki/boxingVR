using System.Numerics;
using BepuPhysics;
using BepuPhysics.Collidables;

namespace Aigis.Physics;

/// <summary>
/// Changing a live body (2026-11, voxel damage: a part carved through keeps only its near piece, the far piece
/// becomes a new body). Bodies are still never removed (see <see cref="AddDynamicBox(Vector3, Vector3, float, int?, int)"/>):
/// a body that loses part of itself is RESHAPED in place, so its id, its joints and everything that refers to it stay valid.
/// </summary>
public sealed partial class PhysicsWorld
{
    /// <summary>
    /// Replaces a dynamic body's collider with one box of <paramref name="halfExtents"/> centred at
    /// <paramref name="localCenter"/> in the body's own frame, and its mass with <paramref name="mass"/>.
    /// <para><b>The body's origin does not move</b> — joints anchored on it keep their offsets — so a box off the origin
    /// is built as a one-child compound, and the inertia is taken about the origin (parallel axis included). Pose and
    /// velocity are kept. The previous shape is released.</para>
    /// </summary>
    public void ReshapeDynamicBox(int id, Vector3 localCenter, Vector3 halfExtents, float mass)
    {
        ThrowIfDisposed();
        if (!_masses.TryGetValue(id, out var current) || current <= 0f)
        {
            throw new ArgumentException("Body must be a live dynamic body.", nameof(id));
        }

        if (!(halfExtents.X > 0f && halfExtents.Y > 0f && halfExtents.Z > 0f) || !float.IsFinite(halfExtents.X + halfExtents.Y + halfExtents.Z))
        {
            throw new ArgumentOutOfRangeException(nameof(halfExtents), halfExtents, "Half extents must be positive and finite.");
        }

        if (!(mass > 0f) || !float.IsFinite(mass))
        {
            throw new ArgumentOutOfRangeException(nameof(mass), mass, "A dynamic body needs a positive, finite mass.");
        }

        if (!float.IsFinite(localCenter.X + localCenter.Y + localCenter.Z))
        {
            throw new ArgumentOutOfRangeException(nameof(localCenter), localCenter, "The centre must be finite.");
        }

        var box = new Box(halfExtents.X * 2f, halfExtents.Y * 2f, halfExtents.Z * 2f);
        TypedIndex shape;
        BodyInertia inertia;
        if (localCenter.LengthSquared() < 1e-12f)
        {
            shape = _simulation.Shapes.Add(box);
            inertia = box.ComputeInertia(mass);
        }
        else
        {
            using var builder = new CompoundBuilder(_pool, _simulation.Shapes, 1);
            builder.Add(box, new RigidPose(localCenter), mass);
            // Inertia about the body's own origin (not re-centred): the joints are anchored relative to that origin.
            builder.BuildDynamicCompound(out var children, out inertia);
            shape = _simulation.Shapes.Add(new Compound(children));
        }

        var body = _simulation.Bodies[new BodyHandle(id)];
        var old = body.Collidable.Shape;
        body.SetShape(shape);
        body.LocalInertia = inertia;
        body.Awake = true;
        _simulation.Shapes.RecursivelyRemoveAndDispose(old, _pool);
        _masses[id] = mass;
        _shapes[id] = localCenter.LengthSquared() < 1e-12f
            ? new BodyShape(ShapeKind.Box, halfExtents)
            : new BodyShape(ShapeKind.Compound, Vector3.Abs(localCenter) + halfExtents);
        _lastDynamicInertia[id] = (mass, inertia);
    }

    /// <summary>
    /// Gives <paramref name="toBody"/> exactly the collision filter of <paramref name="fromBody"/>: the same group, subgroup
    /// and self-collision exclusions. A piece broken off a ragdoll part keeps not colliding with that part's neighbours.
    /// </summary>
    public void CopyCollisionFilter(int fromBody, int toBody)
    {
        ThrowIfDisposed();
        if (!_masses.ContainsKey(fromBody) || !_masses.ContainsKey(toBody))
        {
            throw new ArgumentException("Unknown body id.");
        }

        _collisionFilters[new BodyHandle(toBody)] = _collisionFilters[new BodyHandle(fromBody)];
    }

    /// <summary>
    /// Where a live joint pins its two bodies, in world space, read from the offsets it was created with
    /// (ball-and-socket, hinge and distance joints). Null for a joint with no anchor (limits, servos, motors).
    /// </summary>
    public (Vector3 AnchorA, Vector3 AnchorB)? GetJointAnchors(int constraintId)
    {
        ThrowIfDisposed();
        if (!_constraints.ContainsKey(constraintId) || !_jointDescriptors.TryGetValue(constraintId, out var d)
            || d.Type is not (JointType.BallSocket or JointType.Hinge or JointType.DistanceLimit))
        {
            return null;
        }

        var a = GetState(d.BodyA);
        var b = GetState(d.BodyB);
        return (a.Position + Vector3.Transform(d.LocalOffsetA, a.Orientation), b.Position + Vector3.Transform(d.LocalOffsetB, b.Orientation));
    }

    /// <summary>The bodies a live constraint joins, or null if it is gone.</summary>
    public (int BodyA, int BodyB)? GetConstraintBodies(int constraintId)
    {
        ThrowIfDisposed();
        return _constraints.ContainsKey(constraintId) && _jointDescriptors.TryGetValue(constraintId, out var d) ? (d.BodyA, d.BodyB) : null;
    }
}
