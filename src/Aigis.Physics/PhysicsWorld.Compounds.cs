using System.Numerics;
using BepuPhysics;
using BepuPhysics.Collidables;
using BepuPhysics.Constraints;

namespace Aigis.Physics;

/// <summary>One box of a <see cref="PhysicsWorld.AddDynamicCompound"/> body, in the body's authored local frame.</summary>
/// <param name="LocalCenter">Where the box's centre sits, relative to the position the compound is created at.</param>
/// <param name="LocalOrientation">The box's rotation relative to the compound's own.</param>
/// <param name="HalfExtents">Half the box's size along its own axes.</param>
public readonly record struct CompoundBoxPart(Vector3 LocalCenter, Quaternion LocalOrientation, Vector3 HalfExtents);

/// <summary>
/// Rigid bodies made of several boxes, and the drive that spins one (2026-10-28, Triage's revolving door:
/// 「扉は位置ではなく、モーメントをかけて回る物理オブジェクトとして設計」).
/// <para><b>Why a compound and not welded boxes.</b> <see cref="AddWeld"/> is a soft constraint: three door
/// leaves welded to a hub flex apart under exactly the load a jammed door exists to carry, and the flex is a
/// spring that rings. A compound is one body with one inertia — its leaves cannot move relative to each other
/// at all, which is what glass in a frame does.</para>
/// </summary>
public sealed partial class PhysicsWorld
{
    /// <summary>
    /// Adds ONE dynamic body made of <paramref name="parts"/>. Mass is shared between the parts by volume, so
    /// disjoint parts come out a uniform material whatever their sizes (where parts overlap, the overlap is counted
    /// once per part — a rotor's leaves meeting at its axis carry a sliver more mass there than glass would).
    /// <para><b>The body's origin is its centre of mass</b>, which is what the backend integrates about:
    /// <paramref name="centreOfMassLocal"/> says where that landed in the authored frame, and the body is placed
    /// so that every part is exactly where it was authored. A symmetric set of parts (a rotor) reports zero and
    /// its <see cref="GetState"/> position is the authored one.</para>
    /// <para><see cref="BodyShape"/> records the parts' bounding box (<see cref="ShapeKind.Compound"/>), which
    /// is what observation and probes need of a shape.</para>
    /// </summary>
    public int AddDynamicCompound(
        Vector3 position, Quaternion orientation, IReadOnlyList<CompoundBoxPart> parts, float mass,
        out Vector3 centreOfMassLocal, int? group = null, int subgroup = 0)
    {
        ThrowIfDisposed();
        ArgumentNullException.ThrowIfNull(parts);
        if (parts.Count == 0)
        {
            throw new ArgumentException("A compound needs at least one part.", nameof(parts));
        }

        if (!(mass > 0f) || !float.IsFinite(mass))
        {
            throw new ArgumentOutOfRangeException(nameof(mass), mass, "A dynamic compound needs a positive, finite mass.");
        }

        if (parts.Any(p => !(p.HalfExtents.X > 0f && p.HalfExtents.Y > 0f && p.HalfExtents.Z > 0f)))
        {
            throw new ArgumentException("Every part needs positive half extents.", nameof(parts));
        }

        var totalVolume = parts.Sum(p => p.HalfExtents.X * p.HalfExtents.Y * p.HalfExtents.Z);
        using var builder = new CompoundBuilder(_pool, _simulation.Shapes, parts.Count);
        foreach (var part in parts)
        {
            var box = new Box(part.HalfExtents.X * 2f, part.HalfExtents.Y * 2f, part.HalfExtents.Z * 2f);
            var share = part.HalfExtents.X * part.HalfExtents.Y * part.HalfExtents.Z / totalVolume;
            builder.Add(box, new RigidPose(part.LocalCenter, part.LocalOrientation), mass * share);
        }

        builder.BuildDynamicCompound(out var children, out var inertia, out var centre);
        var shapeIndex = _simulation.Shapes.Add(new Compound(children));
        var handle = _simulation.Bodies.Add(BodyDescription.CreateDynamic(
            new RigidPose(position + Vector3.Transform(centre, orientation), orientation),
            inertia,
            new CollidableDescription(shapeIndex, 0.1f),
            new BodyActivityDescription(0.01f)));
        var id = RegisterBody(handle, Filter(group, subgroup));
        RegisterBodyMetadata(id, mass, new BodyShape(ShapeKind.Compound, BoundingHalfExtents(parts, centre)));
        _lastDynamicInertia[id] = (mass, inertia);
        centreOfMassLocal = centre;
        return id;
    }

    /// <summary>
    /// Drives <paramref name="body"/>'s spin about <paramref name="axisLocal"/> (in <paramref name="body"/>'s own
    /// frame), relative to <paramref name="reference"/>, toward <paramref name="targetSpeed"/> rad/s with at most
    /// <paramref name="maxTorque"/> N·m. <b>Right-handed</b>: a positive speed about +Y turns the body
    /// counter-clockwise seen from above. A torque of zero lets it free-wheel; a target of zero with torque brakes.
    /// <para>A motor rather than an applied torque because what a powered door needs is both halves at once: a
    /// governor (it never spins up past its speed however long it runs unobstructed) and a ceiling (against a
    /// load it pushes with exactly <paramref name="maxTorque"/> and no more, and stalls when that is not
    /// enough).</para>
    /// <para>The motor's target does not wake a sleeping body: a caller driving something that can come to rest
    /// against a load keeps it awake (<see cref="SetSleepThreshold"/> below zero).</para>
    /// </summary>
    public int AddAxisMotor(int body, int reference, Vector3 axisLocal, float targetSpeed, float maxTorque)
    {
        ThrowIfDisposed();
        ValidateMotor(targetSpeed, maxTorque);
        // The backend's motor holds (ω_A − ω_B)·axis at its target, so with the DRIVEN body as A the target is the
        // body's own right-handed spin. Measured, not read off the backend's docs (the wheel drive beside this one
        // documented the opposite reading for two weeks): AnAxisMotor_ReachesItsTargetSpeed_RightHandedAboutItsAxis.
        var id = Track(_simulation.Solver.Add(new BodyHandle(body), new BodyHandle(reference), new AngularAxisMotor
        {
            LocalAxisA = Vector3.Normalize(axisLocal),
            TargetVelocity = targetSpeed,
            Settings = new MotorSettings(maxTorque, 1e-4f),
        }));
        _jointDescriptors[id] = EmptyDescriptor(id, JointType.AxisMotor, body, reference);
        return id;
    }

    /// <summary>Re-tunes a live <see cref="AddAxisMotor"/> in place, with the same sign and units.</summary>
    public void SetAxisMotor(int id, float targetSpeed, float maxTorque)
    {
        ThrowIfDisposed();
        ValidateMotor(targetSpeed, maxTorque);
        if (!_constraints.TryGetValue(id, out var handle)
            || !_jointDescriptors.TryGetValue(id, out var descriptor) || descriptor.Type != JointType.AxisMotor)
        {
            throw new ArgumentException($"Constraint {id} is not a live axis motor.", nameof(id));
        }

        _simulation.Solver.GetDescription(handle, out AngularAxisMotor description);
        description.TargetVelocity = targetSpeed;
        description.Settings = new MotorSettings(maxTorque, 1e-4f);
        _simulation.Solver.ApplyDescription(handle, description);
    }

    private static void ValidateMotor(float targetSpeed, float maxTorque)
    {
        if (!float.IsFinite(targetSpeed))
        {
            throw new ArgumentOutOfRangeException(nameof(targetSpeed), targetSpeed, "A motor needs a finite speed.");
        }

        if (!(maxTorque >= 0f) || !float.IsFinite(maxTorque))
        {
            throw new ArgumentOutOfRangeException(nameof(maxTorque), maxTorque, "A motor needs a non-negative, finite torque.");
        }
    }

    /// <summary>Half the size of the box, centred on the body's origin (its centre of mass), that holds every part.</summary>
    private static Vector3 BoundingHalfExtents(IReadOnlyList<CompoundBoxPart> parts, Vector3 centre)
    {
        var extent = Vector3.Zero;
        foreach (var part in parts)
        {
            var local = part.LocalCenter - centre;
            var reach = RotatedBoxExtent(part.HalfExtents, part.LocalOrientation);
            extent = Vector3.Max(extent, Vector3.Abs(local) + reach);
        }

        return extent;
    }
}
