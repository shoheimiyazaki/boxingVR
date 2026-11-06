using System.Numerics;
using BepuPhysics;
using BepuPhysics.Collidables;
using BepuPhysics.Constraints;
using BepuUtilities;

namespace Aigis.Physics;

/// <summary>
/// Constraints (joints), breakable joints, and kinematic control — the building blocks of
/// ragdolls and scripted movers. Constraint ids are engine-owned (a stable id → Bepu
/// <see cref="ConstraintHandle"/> map), because Bepu recycles handle values on remove
/// (see docs/anti-patterns.md A2) — critical once joints break.
/// </summary>
public sealed partial class PhysicsWorld
{
    private readonly Dictionary<int, ConstraintHandle> _constraints = new();
    private readonly Dictionary<int, float> _breakable = new();
    private readonly Dictionary<int, JointDescriptor> _jointDescriptors = new();

    /// <summary>The mass + local inertia a body had the last time it was dynamic, stashed by
    /// <see cref="MakeKinematic"/> so <see cref="MakeDynamic"/> can restore it EXACTLY.
    /// <para>
    /// Why stash rather than recompute from <c>_shapes</c> (both are possible — the shape metadata
    /// registered at creation is enough to call <c>ComputeInertia</c> again): the stash is the body's
    /// actual inertia, so the round trip is bit-identical by construction rather than by relying on
    /// <c>ComputeInertia</c> being reproducible AND on <c>BodyShape</c> remaining a lossless
    /// description of the collidable. It also survives a body whose inertia was never shape-derived
    /// in the first place. Recompute-from-shape is used in exactly one place — the
    /// <c>mass</c>-overriding path of <see cref="MakeDynamic"/>, where there is no stash to restore.
    /// </para>
    /// A body created by <c>AddKinematicBox</c> has no entry until it has been dynamic once, which is
    /// what makes <see cref="MakeDynamic"/> demand an explicit mass for it instead of inventing one.
    /// <para>
    /// The scalar mass is stored ALONGSIDE the inertia rather than recovered from it:
    /// <c>1f / BodyInertia.InverseMass</c> is not the identity in binary32 (a body created at 1.75 kg
    /// comes back as 1.7499999), and <see cref="GetMass"/> is documented to report the mass the
    /// factory was given.
    /// </para></summary>
    private readonly Dictionary<int, (float Mass, BodyInertia Inertia)> _lastDynamicInertia = new();
    private int _nextConstraintId;

    private sealed record JointDescriptor(
        int Id,
        JointType Type,
        int BodyA,
        int BodyB,
        Vector3 LocalOffsetA,
        Vector3 LocalOffsetB,
        Vector3 AxisLocalA,
        Vector3 AxisLocalB,
        float MaxSwingRadians,
        Quaternion BasisA,
        Quaternion BasisB,
        float MinTwistRadians,
        float MaxTwistRadians,
        // Per-type meaning, deliberately not unified: for a BallSocket it is the MEASURED centre-to-centre
        // distance at creation (a bone's rest length, which is what Skeleton()'s bone list reports); for a
        // DistanceLimit it is the MAXIMUM PERMITTED anchor separation, a limit rather than a measurement,
        // and that type's minDistance is not round-tripped at all. Nothing agent-facing reads it for the
        // latter (Skeleton() filters bones to BallSocket), so the ambiguity is contained here.
        float RestLength,
        bool Broken);

    /// <summary>Invoked with the constraint id when a breakable joint exceeds its force threshold and is removed.</summary>
    public Action<int>? OnJointBroken { get; set; }

    /// <summary>Pins two bodies together at a point (the socket of a ball-and-socket joint).</summary>
    public int AddBallSocket(int bodyA, int bodyB, Vector3 localOffsetA, Vector3 localOffsetB, float frequency = 15f, float dampingRatio = 1f)
    {
        var id = Track(_simulation.Solver.Add(new BodyHandle(bodyA), new BodyHandle(bodyB), new BallSocket
        {
            LocalOffsetA = localOffsetA,
            LocalOffsetB = localOffsetB,
            SpringSettings = new SpringSettings(frequency, dampingRatio),
        }));
        _jointDescriptors[id] = EmptyDescriptor(id, JointType.BallSocket, bodyA, bodyB) with
        {
            LocalOffsetA = localOffsetA,
            LocalOffsetB = localOffsetB,
            RestLength = Vector3.Distance(GetState(bodyA).Position, GetState(bodyB).Position),
        };
        return id;
    }

    /// <summary>
    /// Holds <paramref name="bodyB"/> rigidly to <paramref name="bodyA"/> where they stand now — position AND orientation —
    /// so two bodies move and turn as one: a vehicle's parts (an ambulance's chassis and bumper, a car's two tiers)
    /// that must stay separate bodies, with their own ids and contacts, yet roll over together.
    /// <para>A stiff spring (Bepu's mass-normalised <paramref name="frequency"/>), not an infinitely rigid bond; at the
    /// 30 Hz default two parts of a few hundred kilograms and up hold to millimetres under a car's own forces.</para>
    /// </summary>
    public int AddWeld(int bodyA, int bodyB, float frequency = 30f, float dampingRatio = 1f)
    {
        ThrowIfDisposed();
        var a = GetState(bodyA);
        var b = GetState(bodyB);
        var inverseA = Quaternion.Conjugate(a.Orientation);
        var id = Track(_simulation.Solver.Add(new BodyHandle(bodyA), new BodyHandle(bodyB), new Weld
        {
            LocalOffset = Vector3.Transform(b.Position - a.Position, inverseA),
            LocalOrientation = Quaternion.Normalize(Quaternion.Concatenate(b.Orientation, inverseA)),
            SpringSettings = new SpringSettings(frequency, dampingRatio),
        }));
        _jointDescriptors[id] = EmptyDescriptor(id, JointType.Weld, bodyA, bodyB);
        return id;
    }

    /// <summary>Moves a live <see cref="AddBallSocket"/> joint's two anchors in place (no remove/re-add,
    /// so the solver keeps its warm start) — how a controller drags a pinned point around with an
    /// animated attachment, e.g. a jaw that shakes its head while it holds on.</summary>
    /// <exception cref="ArgumentOutOfRangeException">Either offset is not finite.</exception>
    /// <exception cref="ArgumentException"><paramref name="id"/> is not a live ball-socket joint —
    /// reading another joint type's description as a <c>BallSocket</c> would be a type-confused read
    /// deep inside the solver.</exception>
    public void SetBallSocketOffsets(int id, Vector3 localOffsetA, Vector3 localOffsetB)
    {
        ThrowIfDisposed();
        if (!IsFinitePose(localOffsetA))
        {
            throw new ArgumentOutOfRangeException(nameof(localOffsetA));
        }

        if (!IsFinitePose(localOffsetB))
        {
            throw new ArgumentOutOfRangeException(nameof(localOffsetB));
        }

        if (!_constraints.TryGetValue(id, out var handle)
            || !_jointDescriptors.TryGetValue(id, out var joint)
            || joint.Type != JointType.BallSocket)
        {
            throw new ArgumentException("Not a live ball-socket joint.", nameof(id));
        }

        _simulation.Solver.GetDescription(handle, out BallSocket description);
        description.LocalOffsetA = localOffsetA;
        description.LocalOffsetB = localOffsetB;
        _simulation.Solver.ApplyDescription(handle, description);
        _jointDescriptors[id] = joint with { LocalOffsetA = localOffsetA, LocalOffsetB = localOffsetB };
    }

    /// <summary>
    /// Re-tunes a live position joint's spring in place — a <see cref="AddBallSocket"/>, <see cref="AddHinge"/> or
    /// <see cref="AddDistanceLimit"/> — and returns the settings it had, so a caller can put them back.
    /// <para>Why it exists (2026-10-14): Bepu's springs are mass-normalised, so a joint between two light bodies is
    /// soft in newtons however high its frequency reads. A ragdoll arm carrying a second body's weight (Triage's
    /// rooftop hang) stretched 2.5 m at the 15 Hz it is built with; the caller raises it for exactly as long as
    /// that load lasts. No remove/re-add, so the solver keeps its warm start.</para>
    /// </summary>
    /// <exception cref="ArgumentOutOfRangeException">The frequency or damping ratio is not positive and finite.</exception>
    /// <exception cref="ArgumentException"><paramref name="id"/> is not a live joint of one of those three types.</exception>
    public (float Frequency, float DampingRatio) SetJointSpring(int id, float frequency, float dampingRatio)
    {
        ThrowIfDisposed();
        if (!(frequency > 0f) || !float.IsFinite(frequency))
        {
            throw new ArgumentOutOfRangeException(nameof(frequency));
        }

        if (!(dampingRatio > 0f) || !float.IsFinite(dampingRatio))
        {
            throw new ArgumentOutOfRangeException(nameof(dampingRatio));
        }

        if (!_constraints.TryGetValue(id, out var handle) || !_jointDescriptors.TryGetValue(id, out var joint))
        {
            throw new ArgumentException("Not a live joint.", nameof(id));
        }

        var spring = new SpringSettings(frequency, dampingRatio);
        SpringSettings previous;
        switch (joint.Type)
        {
            case JointType.BallSocket:
            {
                _simulation.Solver.GetDescription(handle, out BallSocket description);
                previous = description.SpringSettings;
                description.SpringSettings = spring;
                _simulation.Solver.ApplyDescription(handle, description);
                break;
            }

            case JointType.Hinge:
            {
                _simulation.Solver.GetDescription(handle, out Hinge description);
                previous = description.SpringSettings;
                description.SpringSettings = spring;
                _simulation.Solver.ApplyDescription(handle, description);
                break;
            }

            case JointType.DistanceLimit:
            {
                _simulation.Solver.GetDescription(handle, out DistanceLimit description);
                previous = description.SpringSettings;
                description.SpringSettings = spring;
                _simulation.Solver.ApplyDescription(handle, description);
                break;
            }

            default:
                throw new ArgumentException($"Joint type {joint.Type} has no position spring to re-tune.", nameof(id));
        }

        return (previous.Frequency, previous.DampingRatio);
    }

    /// <summary>Keeps two anchor points between <paramref name="minDistance"/> and
    /// <paramref name="maxDistance"/> apart, springing them back into that band when they leave it
    /// and leaving them completely free while inside it — a leash, not a rod.
    /// <para>
    /// This is the direct equivalent of Unity's <c>SpringJoint</c> (ball-and-socket plus a distance
    /// spring that only engages outside <c>minDistance</c>..<c>maxDistance</c>), which is why it
    /// exists: it is what a "grab and drag another body around" mechanic needs. The critical
    /// property, and the reason a real constraint is used rather than a per-step force applied to
    /// the grabbed body, is that a constraint is inherently <b>bidirectional</b> — the solver applies
    /// equal and opposite impulses, so a heavy grabbed body pulls the grabber back just as hard.
    /// A one-way "spring the target toward my hand" force instead produces the classic bug of the
    /// grabber's reach stretching without limit, because nothing ever resists it.
    /// </para>
    /// <para>
    /// <paramref name="frequency"/>/<paramref name="dampingRatio"/> are Bepu's mass-normalised
    /// spring description (target oscillation frequency in Hz and damping ratio), NOT a stiffness in
    /// N/m. A caller porting a literal N/m stiffness and N·s/m damper must convert using the mass it
    /// will actually act on — <c>f = sqrt(k/m)/(2*pi)</c>, <c>zeta = c/(2*sqrt(k*m))</c>.
    /// </para></summary>
    public int AddDistanceLimit(int bodyA, int bodyB, Vector3 localOffsetA, Vector3 localOffsetB,
        float minDistance, float maxDistance, float frequency = 15f, float dampingRatio = 1f)
    {
        if (!(minDistance >= 0f) || !(maxDistance >= minDistance) || !float.IsFinite(maxDistance))
        {
            throw new ArgumentOutOfRangeException(nameof(maxDistance),
                "Require 0 <= minDistance <= maxDistance, both finite.");
        }

        var id = Track(_simulation.Solver.Add(new BodyHandle(bodyA), new BodyHandle(bodyB), new DistanceLimit
        {
            LocalOffsetA = localOffsetA,
            LocalOffsetB = localOffsetB,
            MinimumDistance = minDistance,
            MaximumDistance = maxDistance,
            SpringSettings = new SpringSettings(frequency, dampingRatio),
        }));
        _jointDescriptors[id] = EmptyDescriptor(id, JointType.DistanceLimit, bodyA, bodyB) with
        {
            LocalOffsetA = localOffsetA,
            LocalOffsetB = localOffsetB,
            RestLength = maxDistance,
        };
        return id;
    }

    /// <summary>
    /// Moves a live <see cref="AddDistanceLimit"/> joint's anchor on body A and its maximum length,
    /// in place (no remove/re-add, so the solver keeps its warm start) — a crane's rope paying out
    /// from a boom tip that slews (2026-10-10, the lunar truck). Wakes both bodies first: a changed
    /// constraint does not wake a sleeping island, and the write must land after the wake moves the
    /// constraint into the active set (see <c>PhysicsWorld.Wheels.cs</c>).
    /// </summary>
    public void SetDistanceLimit(int id, Vector3 localOffsetA, float maxDistance)
    {
        ThrowIfDisposed();
        var handle = _constraints[id];
        _simulation.Solver.GetDescription(handle, out DistanceLimit description);
        if (description.LocalOffsetA == localOffsetA && description.MaximumDistance == maxDistance)
        {
            return;
        }

        if (!(maxDistance >= description.MinimumDistance) || !float.IsFinite(maxDistance))
        {
            throw new ArgumentOutOfRangeException(nameof(maxDistance), "Require minDistance <= maxDistance, finite.");
        }

        var joint = _jointDescriptors[id];
        _simulation.Awakener.AwakenBody(new BodyHandle(joint.BodyA));
        _simulation.Awakener.AwakenBody(new BodyHandle(joint.BodyB));
        description.LocalOffsetA = localOffsetA;
        description.MaximumDistance = maxDistance;
        _simulation.Solver.ApplyDescription(handle, description);
        _jointDescriptors[id] = joint with { LocalOffsetA = localOffsetA, RestLength = maxDistance };
    }

    /// <summary>Cone limit: how far body B's axis may tilt away from body A's axis.</summary>
    public int AddSwingLimit(int bodyA, int bodyB, Vector3 axisLocalA, Vector3 axisLocalB, float maxSwingRadians, float frequency = 15f, float dampingRatio = 1f)
    {
        var id = Track(_simulation.Solver.Add(new BodyHandle(bodyA), new BodyHandle(bodyB), new SwingLimit
        {
            AxisLocalA = axisLocalA,
            AxisLocalB = axisLocalB,
            MaximumSwingAngle = maxSwingRadians,
            SpringSettings = new SpringSettings(frequency, dampingRatio),
        }));
        _jointDescriptors[id] = EmptyDescriptor(id, JointType.SwingLimit, bodyA, bodyB) with
        {
            AxisLocalA = axisLocalA,
            AxisLocalB = axisLocalB,
            MaxSwingRadians = maxSwingRadians,
        };
        return id;
    }

    /// <summary>Bounds the twist between two bodies around their basis Z axes.</summary>
    public int AddTwistLimit(int bodyA, int bodyB, Quaternion basisA, Quaternion basisB, float minRadians, float maxRadians, float frequency = 15f, float dampingRatio = 1f)
    {
        var id = Track(_simulation.Solver.Add(new BodyHandle(bodyA), new BodyHandle(bodyB), new TwistLimit
        {
            LocalBasisA = basisA,
            LocalBasisB = basisB,
            MinimumAngle = minRadians,
            MaximumAngle = maxRadians,
            SpringSettings = new SpringSettings(frequency, dampingRatio),
        }));
        _jointDescriptors[id] = EmptyDescriptor(id, JointType.TwistLimit, bodyA, bodyB) with
        {
            BasisA = basisA,
            BasisB = basisB,
            MinTwistRadians = minRadians,
            MaxTwistRadians = maxRadians,
        };
        return id;
    }

    /// <summary>A 1-DOF hinge (e.g. an elbow/knee/ankle): pins a point (like <see cref="AddBallSocket"/>)
    /// and additionally constrains the two bodies' hinge axes to stay coincident, leaving only rotation
    /// about that shared axis free — unlike a ball-and-socket, which leaves all 3 rotational DOF free.
    /// Pair with <see cref="AddTwistLimit"/> sharing the same axis to bound how far that one free
    /// rotation may go (a real hinge's bend limit).</summary>
    public int AddHinge(int bodyA, int bodyB, Vector3 localOffsetA, Vector3 localOffsetB, Vector3 hingeAxisLocalA, Vector3 hingeAxisLocalB, float frequency = 15f, float dampingRatio = 1f)
    {
        var id = Track(_simulation.Solver.Add(new BodyHandle(bodyA), new BodyHandle(bodyB), new Hinge
        {
            LocalOffsetA = localOffsetA,
            LocalOffsetB = localOffsetB,
            LocalHingeAxisA = hingeAxisLocalA,
            LocalHingeAxisB = hingeAxisLocalB,
            SpringSettings = new SpringSettings(frequency, dampingRatio),
        }));
        _jointDescriptors[id] = EmptyDescriptor(id, JointType.Hinge, bodyA, bodyB) with
        {
            LocalOffsetA = localOffsetA,
            LocalOffsetB = localOffsetB,
            AxisLocalA = hingeAxisLocalA,
            AxisLocalB = hingeAxisLocalB,
        };
        return id;
    }

    /// <summary>Drives a ball-and-socket joint's full relative orientation toward a target
    /// quaternion — an active "muscle" (e.g. a hip/shoulder), layered <i>alongside</i> the passive
    /// <see cref="AddBallSocket"/>/<see cref="AddSwingLimit"/>/<see cref="AddTwistLimit"/> recipe
    /// (those safety bounds stay in effect; the servo actively drives within them).</summary>
    public int AddAngularServo(int bodyA, int bodyB, Quaternion targetRelativeRotationLocalA,
        float frequency = 15f, float dampingRatio = 1f, float maxSpeed = 10f, float baseSpeed = 0f, float maxForce = 50f)
    {
        var id = Track(_simulation.Solver.Add(new BodyHandle(bodyA), new BodyHandle(bodyB), new AngularServo
        {
            TargetRelativeRotationLocalA = targetRelativeRotationLocalA,
            SpringSettings = new SpringSettings(frequency, dampingRatio),
            ServoSettings = new ServoSettings(maxSpeed, baseSpeed, maxForce),
        }));
        _jointDescriptors[id] = EmptyDescriptor(id, JointType.AngularServo, bodyA, bodyB);
        return id;
    }

    /// <summary>Re-tunes a live <see cref="AddAngularServo"/> joint's spring and force ceiling in
    /// place, leaving its target where it is.
    /// <para>The twin of <see cref="SetAngularServoTarget"/>, and needed for the same reason at a
    /// different timescale: a target changes every step, while stiffness and force are usually chosen
    /// once — but "usually" is not "always". A tuning surface that can only move the target can only
    /// ask a muscle to go somewhere else, never to be stronger or softer, so the two settings that
    /// decide whether a limb ARRIVES at its target were reachable only by destroying and re-creating
    /// the constraint (which drops the solver's accumulated impulses and shows up as a visible jolt).
    /// Added for <c>Game.Triage</c>'s writhe lab, which tunes exactly those two live.</para>
    /// <para><paramref name="dampingRatio"/> defaults to 1 (critically damped) to match
    /// <see cref="AddAngularServo"/>'s own default, so a caller that only wants to move the frequency
    /// or the force does not silently re-tune the damping as a side effect. Both speed limits
    /// (<c>maxSpeed</c>/<c>baseSpeed</c>) are deliberately left alone: this game never sets them.</para></summary>
    public void SetAngularServoSettings(int id, float frequency, float maxForce, float dampingRatio = 1f)
    {
        ThrowIfDisposed();
        var handle = _constraints[id];
        _simulation.Solver.GetDescription(handle, out AngularServo description);
        description.SpringSettings = new SpringSettings(frequency, dampingRatio);
        description.ServoSettings = new ServoSettings(
            description.ServoSettings.MaximumSpeed, description.ServoSettings.BaseSpeed, maxForce);
        _simulation.Solver.ApplyDescription(handle, description);
    }

    /// <summary>Re-targets a live <see cref="AddAngularServo"/> joint in place (no remove/re-add) —
    /// how a controller drives a joint every step.</summary>
    public void SetAngularServoTarget(int id, Quaternion targetRelativeRotationLocalA)
    {
        ThrowIfDisposed();
        var handle = _constraints[id];
        _simulation.Solver.GetDescription(handle, out AngularServo description);
        description.TargetRelativeRotationLocalA = targetRelativeRotationLocalA;
        _simulation.Solver.ApplyDescription(handle, description);
    }

    /// <summary>Drives a 1-DOF hinge's bend angle toward a target — an active "muscle" (e.g. a
    /// knee/ankle/elbow), layered <i>alongside</i> the passive <see cref="AddHinge"/>/
    /// <see cref="AddTwistLimit"/> recipe (the hard bend-angle limit stays in effect; the servo
    /// actively drives within it). Uses the same basis convention as <see cref="AddTwistLimit"/> —
    /// verified empirically against it (see <c>PhysicsJointTests.cs</c>) rather than trusted from
    /// Bepu's doc comments alone, since <c>TwistServo</c>'s and <c>TwistLimit</c>'s doc text
    /// describe the twist axis with opposite signs.</summary>
    public int AddTwistServo(int bodyA, int bodyB, Quaternion basisA, Quaternion basisB, float targetAngleRadians,
        float frequency = 15f, float dampingRatio = 1f, float maxSpeed = 10f, float baseSpeed = 0f, float maxForce = 50f)
    {
        var id = Track(_simulation.Solver.Add(new BodyHandle(bodyA), new BodyHandle(bodyB), new TwistServo
        {
            LocalBasisA = basisA,
            LocalBasisB = basisB,
            TargetAngle = targetAngleRadians,
            SpringSettings = new SpringSettings(frequency, dampingRatio),
            ServoSettings = new ServoSettings(maxSpeed, baseSpeed, maxForce),
        }));
        _jointDescriptors[id] = EmptyDescriptor(id, JointType.TwistServo, bodyA, bodyB) with
        {
            BasisA = basisA,
            BasisB = basisB,
        };
        return id;
    }

    /// <summary>Re-targets a live <see cref="AddTwistServo"/> joint in place (no remove/re-add).</summary>
    public void SetTwistServoTarget(int id, float targetAngleRadians)
    {
        ThrowIfDisposed();
        var handle = _constraints[id];
        _simulation.Solver.GetDescription(handle, out TwistServo description);
        description.TargetAngle = targetAngleRadians;
        _simulation.Solver.ApplyDescription(handle, description);
    }

    /// <summary>A soft angular damper between two bodies (bleeds joint energy so limbs don't jitter).</summary>
    public int AddAngularMotor(int bodyA, int bodyB, float maximumForce = 75f, float softness = 0.01f)
    {
        var id = Track(_simulation.Solver.Add(new BodyHandle(bodyA), new BodyHandle(bodyB), new AngularMotor
        {
            TargetVelocityLocalA = Vector3.Zero,
            Settings = new MotorSettings(maximumForce, softness),
        }));
        _jointDescriptors[id] = EmptyDescriptor(id, JointType.AngularMotor, bodyA, bodyB);
        return id;
    }

    /// <summary>Marks a joint breakable: removed (and <see cref="OnJointBroken"/> raised) once its
    /// per-step accumulated force exceeds <paramref name="breakForce"/> newtons.</summary>
    public void MakeBreakable(int constraintId, float breakForce)
    {
        if (_constraints.ContainsKey(constraintId))
        {
            _breakable[constraintId] = breakForce;
        }
    }

    /// <summary>Removes a constraint by id. Returns false if it was already gone. By default the
    /// descriptor is kept (marked <c>Broken</c>) so <see cref="JointStates"/> can still report a
    /// breakable joint's history; pass <paramref name="forget"/> for a short-lived constraint (e.g.
    /// a mouse-grab spring that gets added and removed repeatedly over a long interactive session)
    /// where keeping that history forever would grow <see cref="JointStates"/> unboundedly.</summary>
    public bool RemoveConstraint(int constraintId, bool forget = false)
    {
        if (!_constraints.TryGetValue(constraintId, out var handle))
        {
            return false;
        }

        _simulation.Solver.Remove(handle);
        _constraints.Remove(constraintId);
        _breakable.Remove(constraintId);
        if (forget)
        {
            _jointDescriptors.Remove(constraintId);
        }
        else if (_jointDescriptors.TryGetValue(constraintId, out var descriptor))
        {
            _jointDescriptors[constraintId] = descriptor with { Broken = true };
        }

        return true;
    }

    public IReadOnlyList<JointState> JointStates()
    {
        ThrowIfDisposed();
        return Array.AsReadOnly(_jointDescriptors.Values.OrderBy(j => j.Id).Select(ToJointState).ToArray());
    }

    /// <summary>True if <paramref name="id"/> is a kinematic body (infinite mass; moved by velocity, not force).</summary>
    public bool IsKinematic(int id) => _masses.TryGetValue(id, out var mass) && mass <= 0f;

    /// <summary>True if <paramref name="id"/> refers to a constraint that is still present (not removed/broken).</summary>
    public bool HasConstraint(int id) => _constraints.ContainsKey(id);

    /// <summary>The constraint's accumulated impulse magnitude over the last solved step — the
    /// same quantity <see cref="ProcessBreakableJoints"/> already compares against a break
    /// threshold, exposed here for callers that want a real applied-force/torque reading (e.g. an
    /// energy-cost reward term) instead of a synthetic proxy. Divide by the fixed step size for
    /// an approximate force/torque magnitude — the unit of the result depends on the constraint's
    /// own kind: N·s (linear impulse) for a translational constraint, N·m·s (angular impulse) for
    /// a rotational one such as <see cref="AddAngularServo"/>/<see cref="AddTwistServo"/>.</summary>
    public float GetConstraintImpulseMagnitude(int id)
    {
        ThrowIfDisposed();
        return _simulation.Solver.GetAccumulatedImpulseMagnitude(_constraints[id]);
    }

    /// <summary>Turns a dynamic body kinematic in place: it stops responding to gravity, contacts and
    /// impulses, still pushes dynamic bodies it collides with, and is moved only by
    /// <see cref="SetKinematicVelocity"/>/<see cref="SetKinematicPose(int, System.Numerics.Vector3)"/>. The inverse of
    /// <see cref="MakeDynamic"/>, which restores exactly the mass/inertia recorded here.
    /// <para>
    /// This is the "carried object" primitive (06 §130): while an object is held it should track the
    /// hand without fighting the solver. Teleporting a still-dynamic body with
    /// <see cref="SetDynamicPose"/> every step also tracks a hand, but the body stays a full
    /// participant in the solver — it keeps resolving contacts and can be shoved out of the hand.
    /// </para>
    /// <para>
    /// <b>Velocity is zeroed.</b> Nothing decays a kinematic body's velocity (no gravity, and
    /// <see cref="ApplyDamping"/> deliberately skips kinematics), so a body converted while falling
    /// would otherwise coast in a straight line forever. Zeroing matches
    /// <see cref="SetKinematicPose(int, System.Numerics.Vector3)"/> and leaves the caller in sole control of the motion.
    /// </para>
    /// <para>
    /// <b>Constraints:</b> a constraint joining this body to a still-dynamic body SURVIVES the
    /// conversion and keeps solving (the kinematic side simply becomes the immovable one). A
    /// constraint whose OTHER body is also kinematic cannot survive — Bepu drops any constraint left
    /// with no dynamic body in it — so those are removed here, deliberately and up front, rather than
    /// being dropped inside Bepu and leaving this class holding a recycled <c>ConstraintHandle</c>
    /// (docs/anti-patterns.md A2). They are removed the same way a broken joint is, so
    /// <see cref="JointStates"/> still reports them with <c>Broken == true</c>;
    /// <see cref="OnJointBroken"/> is NOT raised, because nothing exceeded a break force.
    /// </para>
    /// <para>Safe to call between fixed steps like every other mutator here — it is pure state
    /// mutation on the single stepping thread, so determinism (Hard rule 5) is unaffected.</para></summary>
    /// <exception cref="ArgumentException"><paramref name="id"/> is unknown, or the body is already
    /// kinematic (a no-op conversion is a caller bug worth hearing about, the same way
    /// <see cref="SetKinematicPose(int, System.Numerics.Vector3)"/> rejects a dynamic body).</exception>
    public void MakeKinematic(int id)
    {
        ThrowIfDisposed();
        if (!_masses.TryGetValue(id, out var mass))
        {
            throw new ArgumentException("Unknown body id.", nameof(id));
        }

        if (mass <= 0f)
        {
            throw new ArgumentException("Body is already kinematic.", nameof(id));
        }

        var body = _simulation.Bodies[new BodyHandle(id)];
        _lastDynamicInertia[id] = (mass, body.LocalInertia);
        DropConstraintsLeftWithoutADynamicBody(id);
        body.BecomeKinematic();
        body.Velocity.Linear = Vector3.Zero;
        body.Velocity.Angular = Vector3.Zero;
        body.Awake = true;
        _masses[id] = 0f;
    }

    /// <summary>Turns a kinematic body dynamic again: it responds to gravity, contacts and impulses
    /// from the next <see cref="Step(float)"/> on. The inverse of <see cref="MakeKinematic"/>.
    /// <para>
    /// With <paramref name="mass"/> left null this restores the EXACT mass and inertia tensor the body
    /// had when <see cref="MakeKinematic"/> took it over (see <c>_lastDynamicInertia</c> for why that
    /// is stashed rather than recomputed), so a carry round trip cannot silently re-weight an object.
    /// Pass <paramref name="mass"/> only to give a body that was CREATED kinematic
    /// (<see cref="AddKinematicBox(Vector3, Vector3, int?, int)"/>) a mass for the first time — there
    /// is nothing to restore in that case, so the inertia is computed from the shape registered at
    /// creation. It then becomes the value a later <see cref="MakeKinematic"/>/<c>MakeDynamic</c>
    /// round trip restores.
    /// </para>
    /// <para>
    /// <b>Velocity is preserved</b> (unlike <see cref="MakeKinematic"/>, which zeroes it): releasing a
    /// carried object while the hand is moving should throw it, which is exactly what handing the
    /// current velocity over to the solver does. Zero the velocity explicitly with
    /// <see cref="SetLinearVelocity"/>/<see cref="SetAngularVelocity"/> if a dead drop is wanted.
    /// </para></summary>
    /// <param name="id">A kinematic body's id.</param>
    /// <param name="mass">Null to restore the body's original mass; a positive, finite mass to give a
    /// born-kinematic body one (required in that case — there is no sensible mass to invent).</param>
    /// <exception cref="ArgumentException"><paramref name="id"/> is unknown; the body is already
    /// dynamic; or it was created kinematic and no <paramref name="mass"/> was supplied.</exception>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="mass"/> is not positive and finite.</exception>
    public void MakeDynamic(int id, float? mass = null)
    {
        ThrowIfDisposed();
        if (mass is { } requested && (!(requested > 0f) || !float.IsFinite(requested)))
        {
            throw new ArgumentOutOfRangeException(nameof(mass), "Mass must be positive and finite.");
        }

        if (!_masses.TryGetValue(id, out var currentMass))
        {
            throw new ArgumentException("Unknown body id.", nameof(id));
        }

        if (currentMass > 0f)
        {
            throw new ArgumentException("Body is already dynamic.", nameof(id));
        }

        float restoredMass;
        BodyInertia inertia;
        if (mass is { } explicitMass)
        {
            restoredMass = explicitMass;
            inertia = ComputeInertia(_shapes[id], explicitMass);
        }
        else if (_lastDynamicInertia.TryGetValue(id, out var stashed))
        {
            restoredMass = stashed.Mass;
            inertia = stashed.Inertia;
        }
        else
        {
            throw new ArgumentException(
                "Body was created kinematic and has no mass to restore; pass an explicit mass.", nameof(id));
        }

        var body = _simulation.Bodies[new BodyHandle(id)];
        // SetLocalInertia (not a raw LocalInertia assignment) is what correctly re-registers the body
        // as a solver-visible dynamic and wakes it -- a kinematic body does not block constraint-batch
        // membership, so the batches its constraints live in have to be revalidated on the way back.
        body.SetLocalInertia(inertia);
        body.Awake = true;
        _masses[id] = restoredMass;
        _lastDynamicInertia[id] = (restoredMass, inertia);
    }

    /// <summary>Removes every tracked constraint that would be left with no dynamic body once
    /// <paramref name="id"/> goes kinematic — see <see cref="MakeKinematic"/>'s remarks. Bepu would
    /// drop exactly these itself; doing it here first keeps <c>_constraints</c> free of handles the
    /// solver has already recycled.</summary>
    private void DropConstraintsLeftWithoutADynamicBody(int id)
    {
        var orphaned = _jointDescriptors.Values
            .Where(j => !j.Broken && (j.BodyA == id || j.BodyB == id))
            .Where(j => IsKinematic(j.BodyA == id ? j.BodyB : j.BodyA))
            .Select(j => j.Id)
            .ToArray();
        foreach (var constraintId in orphaned)
        {
            RemoveConstraint(constraintId);
        }
    }

    /// <summary>The local inertia a <c>ComputeInertia</c> call on the shape registered at body
    /// creation produces for <paramref name="mass"/>. Mirrors the <c>AddDynamic*</c> factories'
    /// own shape construction, including <c>BodyShape.Dimensions</c>'s per-kind packing (box
    /// half-extents; capsule radius + HALF length; sphere radius).</summary>
    private static BodyInertia ComputeInertia(BodyShape shape, float mass) => shape.Kind switch
    {
        ShapeKind.Box => new Box(shape.Dimensions.X * 2f, shape.Dimensions.Y * 2f, shape.Dimensions.Z * 2f).ComputeInertia(mass),
        ShapeKind.Capsule => new Capsule(shape.Dimensions.X, shape.Dimensions.Y * 2f).ComputeInertia(mass),
        ShapeKind.Sphere => new Sphere(shape.Dimensions.X).ComputeInertia(mass),
        ShapeKind.Cylinder => new Cylinder(shape.Dimensions.X, shape.Dimensions.Y * 2f).ComputeInertia(mass),
        // A compound's inertia depends on how its parts are laid out, which BodyShape does not keep: a
        // compound made kinematic is restored from the inertia it was created with, never re-derived.
        ShapeKind.Compound => throw new ArgumentException(
            "A compound's inertia cannot be re-derived from its bounding box; restore it without an explicit mass.", nameof(shape)),
        _ => throw new ArgumentOutOfRangeException(nameof(shape)),
    };

    /// <summary>Drives a kinematic body by velocity (it then pushes dynamics correctly).</summary>
    public void SetKinematicVelocity(int id, Vector3 linear, Vector3 angular)
    {
        ThrowIfDisposed();
        var body = _simulation.Bodies[new BodyHandle(id)];
        body.Velocity.Linear = linear;
        body.Velocity.Angular = angular;
        body.Awake = true;
    }

    /// <summary>Teleports a kinematic body directly to <paramref name="position"/> and zeroes its
    /// velocity — for re-anchoring a reusable kinematic mover (e.g. a mouse-grab cursor) instantly,
    /// without the one-step "chase" that driving it via <see cref="SetKinematicVelocity"/> implies.</summary>
    public void SetKinematicPose(int id, Vector3 position)
    {
        ThrowIfDisposed();
        if (!IsKinematic(id))
        {
            throw new ArgumentException("Body must be kinematic.", nameof(id));
        }

        var body = _simulation.Bodies[new BodyHandle(id)];
        body.Pose.Position = position;
        body.Velocity.Linear = Vector3.Zero;
        body.Velocity.Angular = Vector3.Zero;
        body.Awake = true;
    }

    /// <summary>Teleports a kinematic body to <paramref name="position"/> AND
    /// <paramref name="orientation"/>, zeroing both velocity components.
    ///
    /// <para><b>Why this overload exists.</b> The position-only one above is enough for a mover whose
    /// motion is a translation (a grab cursor, an elevator cabin, a sinking door), and until this was
    /// added that was the whole vocabulary a kinematic fixture had — which is why a caller wanting a
    /// door on a HINGE had to fake it. A hinge is a rotation about an axis offset from the body's own
    /// centre, i.e. a coupled position AND orientation change per step, and neither half is a valid
    /// pose on its own: writing the position without the orientation drags the panel sideways, and
    /// writing the orientation without the position spins it about its middle. Both, together, once
    /// per step, is the primitive.</para>
    ///
    /// <para>Every other property of <see cref="SetKinematicPose(int, Vector3)"/> carries over
    /// unchanged: the caller stays in sole control of the motion (Bepu integrates nothing for an
    /// infinite-mass body), contacts against the new pose resolve in the step that follows, and the
    /// zeroed velocity means the body reports no momentum to a dynamic it pushes — see this file's
    /// callers, and <c>Game.Triage</c>'s <c>TriageGame.Doors.cs</c> class doc comment, for why a
    /// door deliberately wants pose rather than velocity control.</para></summary>
    /// <exception cref="ArgumentException"><paramref name="id"/> is not a kinematic body.</exception>
    /// <exception cref="ArgumentOutOfRangeException">The pose is not finite.</exception>
    public void SetKinematicPose(int id, Vector3 position, Quaternion orientation)
    {
        ThrowIfDisposed();
        if (!IsKinematic(id))
        {
            throw new ArgumentException("Body must be kinematic.", nameof(id));
        }

        if (!IsFinitePose(position) || !IsFinitePose(orientation))
        {
            throw new ArgumentOutOfRangeException(nameof(position));
        }

        var body = _simulation.Bodies[new BodyHandle(id)];
        body.Pose.Position = position;
        body.Pose.Orientation = Quaternion.Normalize(orientation);
        body.Velocity.Linear = Vector3.Zero;
        body.Velocity.Angular = Vector3.Zero;
        body.Awake = true;
    }

    /// <summary>Reposes a dynamic body and clears its velocity. Intended for controlled
    /// curriculum fixtures that temporarily pin a body while another subsystem is trained.</summary>
    public void SetDynamicPose(int id, Vector3 position, Quaternion orientation)
    {
        ThrowIfDisposed();
        if (!IsFinitePose(position) || !IsFinitePose(orientation))
        {
            throw new ArgumentOutOfRangeException(nameof(position));
        }

        if (!_masses.TryGetValue(id, out var mass) || mass <= 0f)
        {
            throw new ArgumentException("Body must be dynamic.", nameof(id));
        }

        var body = _simulation.Bodies[new BodyHandle(id)];
        body.Pose.Position = position;
        body.Pose.Orientation = Quaternion.Normalize(orientation);
        body.Velocity.Linear = Vector3.Zero;
        body.Velocity.Angular = Vector3.Zero;
        body.Awake = true;
        // A DELIBERATE move is the one thing that may change an altitude on a planar world -- see
        // PhysicsWorld.PlanarMotion. Without this, the lock would drag the body straight back to
        // wherever it was created, which is the opposite of what the caller just asked for.
        SetPlanarDatum(id, position.Y);
    }

    /// <summary>
    /// Moves a body by <paramref name="delta"/> without touching its velocity or its orientation —
    /// the primitive a MOVING PLATFORM needs and this engine did not have.
    ///
    /// <para><b>Why not <see cref="SetDynamicPose"/>.</b> That one clears velocity (its own doc says
    /// so, and its callers depend on it: it exists to pin a body while another subsystem is trained).
    /// Carrying a rider on a platform is the opposite requirement — the rider's own motion has to
    /// survive being carried, or a patient rolling across a wagon deck stops dead every step the train
    /// moves. It also rejects kinematic bodies, and a platform is usually kinematic.</para>
    ///
    /// <para><b>Why a platform needs this at all, rather than friction.</b> Friction can only carry a
    /// rider by first letting it SLIP: the contact has to see relative motion before it produces any
    /// force. On a platform whose velocity changes, that shows up as the rider lagging when the
    /// platform accelerates and overshooting when it slows — measured on Triage's train map as objects
    /// visibly sliding off their wagon and drifting back again (user report, 2026-08-31:
    /// 「オブジェクトが列車の上からずれていったり戻ったりします」). Translating the platform and everything
    /// riding it by the same delta is the only way to make them share a coordinate system exactly.</para>
    ///
    /// <para>Works on dynamic and kinematic bodies alike. Wakes the body, because a sleeping one would
    /// otherwise be left behind by its own floor.</para>
    /// </summary>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="delta"/> is not finite.</exception>
    /// <exception cref="ArgumentException">No such body.</exception>
    public void TranslateBody(int id, Vector3 delta)
    {
        ThrowIfDisposed();
        if (!IsFinitePose(delta))
        {
            throw new ArgumentOutOfRangeException(nameof(delta));
        }

        if (!_masses.ContainsKey(id))
        {
            throw new ArgumentException("No such body.", nameof(id));
        }

        if (delta == Vector3.Zero)
        {
            return;
        }

        var body = _simulation.Bodies[new BodyHandle(id)];
        body.Pose.Position += delta;
        body.Awake = true;
        // Carrying a rider on a platform is a deliberate move, so on a planar world it moves the
        // rider's datum with it -- by the DELTA, not to an absolute, because that is what this method
        // is (see SetDynamicPose's twin of this comment).
        SetPlanarDatum(id, _planarY.TryGetValue(id, out var y) ? y + delta.Y : body.Pose.Position.Y);
    }

    /// <summary>
    /// Fills <paramref name="into"/> with every movable (dynamic or kinematic) body id, in insertion
    /// order.
    /// <para>Fills a caller-owned list rather than returning one, in the shape this repo already uses
    /// for per-frame work (<c>TriageGame.BuildRenderables</c>, <c>ScreenText.BuildQuads</c>): a
    /// moving-platform carry runs every step, and <see cref="SnapshotDynamics"/> — which is the
    /// closest existing thing — allocates a list of full <see cref="BodyState"/>s to answer a question
    /// about ids.</para>
    /// </summary>
    public void CollectMovableBodyIds(List<int> into)
    {
        ThrowIfDisposed();
        ArgumentNullException.ThrowIfNull(into);

        into.Clear();
        foreach (var handle in _bodies)
        {
            into.Add(handle.Value);
        }
    }

    /// <summary>Sets a dynamic body's linear velocity and wakes it.</summary>
    public void SetLinearVelocity(int id, Vector3 velocity)
    {
        ThrowIfDisposed();
        if (!float.IsFinite(velocity.X) || !float.IsFinite(velocity.Y) || !float.IsFinite(velocity.Z))
        {
            throw new ArgumentOutOfRangeException(nameof(velocity));
        }

        if (!_masses.TryGetValue(id, out var mass) || mass <= 0f)
        {
            throw new ArgumentException("Body must be dynamic.", nameof(id));
        }

        var body = _simulation.Bodies[new BodyHandle(id)];
        body.Velocity.Linear = velocity;
        body.Awake = true;
    }

    /// <summary>Sets a dynamic body's angular velocity and wakes it. Needed alongside
    /// <see cref="SetLinearVelocity"/> whenever code hands a dynamic body off between two
    /// control schemes (e.g. a "carry" velocity-follow releasing back to free physics) —
    /// zeroing linear velocity alone leaves any residual spin picked up while sliding/
    /// rolling against a surface, which friction then quietly converts back into linear
    /// motion (a resting body that "shouldn't" be moving slowly rolls away).</summary>
    public void SetAngularVelocity(int id, Vector3 angular)
    {
        ThrowIfDisposed();
        if (!float.IsFinite(angular.X) || !float.IsFinite(angular.Y) || !float.IsFinite(angular.Z))
        {
            throw new ArgumentOutOfRangeException(nameof(angular));
        }

        if (!_masses.TryGetValue(id, out var mass) || mass <= 0f)
        {
            throw new ArgumentException("Body must be dynamic.", nameof(id));
        }

        var body = _simulation.Bodies[new BodyHandle(id)];
        body.Velocity.Angular = angular;
        body.Awake = true;
    }

    /// <summary>Applies an instantaneous angular impulse to a dynamic body and wakes it.
    /// The argument is angular momentum (N·m·s), so a controller which produces a torque
    /// should multiply that torque by the fixed simulation step before calling this method.
    /// Applying equal and opposite impulses to the two bodies at a joint preserves the
    /// system's angular momentum while allowing an explicit residual actuator to be layered
    /// on top of a position servo.</summary>
    public void ApplyAngularImpulse(int id, Vector3 angularImpulse)
    {
        ThrowIfDisposed();
        if (!float.IsFinite(angularImpulse.X) || !float.IsFinite(angularImpulse.Y) || !float.IsFinite(angularImpulse.Z))
        {
            throw new ArgumentOutOfRangeException(nameof(angularImpulse));
        }

        if (!_masses.TryGetValue(id, out var mass) || mass <= 0f)
        {
            throw new ArgumentException("Body must be dynamic.", nameof(id));
        }

        var body = _simulation.Bodies[new BodyHandle(id)];
        body.ApplyAngularImpulse(angularImpulse);
        body.Awake = true;
    }

    /// <summary>Applies an instantaneous linear impulse (N·s) to a dynamic body and wakes it.
    /// A caller expressing Unity's <c>ForceMode.Acceleration</c> (a continuous, mass-independent
    /// acceleration -- what nearly every Triage force uses) computes
    /// <c>impulse = acceleration * mass * dt</c> using <see cref="GetMass"/> and the fixed step
    /// size. There is deliberately no <c>ApplyAcceleration</c> convenience that hides <paramref
    /// name="impulse"/>'s <c>dt</c> term inside this method: making the fixed step explicit at the
    /// call site keeps the fixed-step contract (Hard rule 5) visible rather than implicit.</summary>
    public void ApplyLinearImpulse(int id, Vector3 impulse)
    {
        ThrowIfDisposed();
        if (!float.IsFinite(impulse.X) || !float.IsFinite(impulse.Y) || !float.IsFinite(impulse.Z))
        {
            throw new ArgumentOutOfRangeException(nameof(impulse));
        }

        if (!_masses.TryGetValue(id, out var mass) || mass <= 0f)
        {
            throw new ArgumentException("Body must be dynamic.", nameof(id));
        }

        var body = _simulation.Bodies[new BodyHandle(id)];
        body.ApplyLinearImpulse(impulse);
        body.Awake = true;
    }

    /// <summary>The body's mass in kg, as passed to whichever <c>AddDynamic*</c>/<c>AddKinematicBox</c>
    /// factory created it. A kinematic body (see <see cref="IsKinematic"/>) reports exactly
    /// <c>0</c> here -- Bepu's own "infinite mass" convention, matching <see cref="IsKinematic"/>'s
    /// <c>mass &lt;= 0f</c> check -- never a small-but-positive approximation of infinite mass.</summary>
    public float GetMass(int id)
    {
        ThrowIfDisposed();
        if (!_masses.TryGetValue(id, out var mass))
        {
            throw new ArgumentException("Unknown body id.", nameof(id));
        }

        return mass;
    }

    /// <summary>The body's principal moments of inertia (kg·m^2) about its own LOCAL axes, as
    /// computed by whichever <c>AddDynamic*</c> factory created it (<c>Box/Capsule/Sphere.ComputeInertia</c>).
    /// Diagonal-only — no off-diagonal cross terms — because every shape those factories create has
    /// its local axes aligned with its own principal axes; Bepu's <c>ComputeInertia</c> never produces
    /// cross terms for these symmetric primitives, so this is exactly recoverable as a plain
    /// <see cref="Vector3"/> rather than needing a full 3x3 tensor type (which would also leak
    /// Bepu-shaped math into the public surface, Hard rule 1).
    /// <para>
    /// This is G6's read-primitive — the angular-side counterpart to <see cref="GetMass"/>.
    /// Reproducing Unity's <c>ForceMode.Acceleration</c> (mass/inertia-INDEPENDENT) for an angular
    /// acceleration <c>α</c> needs the angular-momentum impulse <c>L = I·α·dt</c>. Unlike G1's linear
    /// case, where a scalar mass makes <c>impulse = acceleration * mass * dt</c> trivial at any call
    /// site, the angular version needs the (diagonal) inertia, not just a scalar, and it needs a
    /// local/world rotation on both sides of the multiply (a diagonal tensor is only diagonal in the
    /// body's OWN frame). The caller: rotate <c>α</c> into local space (multiply by the inverse of the
    /// body's current orientation), multiply component-wise by this vector, rotate the result back to
    /// world space, multiply by <c>dt</c>, and pass that to <see cref="ApplyAngularImpulse"/> — e.g.:
    /// <code>
    /// var local = Vector3.Transform(accelerationWorld, Quaternion.Conjugate(orientation));
    /// var impulseWorld = Vector3.Transform(local * world.GetLocalInertiaDiagonal(id) * dt, orientation);
    /// world.ApplyAngularImpulse(id, impulseWorld);
    /// </code>
    /// Deliberately NOT bundled into a single "ApplyAngularAcceleration" convenience, for the same
    /// reason <see cref="ApplyLinearImpulse"/>'s own doc comment gives: keeping <c>dt</c> (and here,
    /// the local/world rotation) explicit at the call site keeps the fixed-step contract (Hard rule 5)
    /// visible rather than implicit.
    /// </para></summary>
    public Vector3 GetLocalInertiaDiagonal(int id)
    {
        ThrowIfDisposed();
        if (!_masses.TryGetValue(id, out var mass) || mass <= 0f)
        {
            throw new ArgumentException("Body must be dynamic.", nameof(id));
        }

        var body = _simulation.Bodies[new BodyHandle(id)];
        Symmetric3x3.Invert(in body.LocalInertia.InverseInertiaTensor, out var inertiaTensor);
        return new Vector3(inertiaTensor.XX, inertiaTensor.YY, inertiaTensor.ZZ);
    }

    private int Track(ConstraintHandle handle)
    {
        var id = _nextConstraintId++;
        _constraints[id] = handle;
        return id;
    }

    private static JointDescriptor EmptyDescriptor(int id, JointType type, int bodyA, int bodyB)
        => new(id, type, bodyA, bodyB, Vector3.Zero, Vector3.Zero, Vector3.Zero, Vector3.Zero,
            0f, Quaternion.Identity, Quaternion.Identity, 0f, 0f, 0f, false);

    private JointState ToJointState(JointDescriptor descriptor)
    {
        float? angle = descriptor.Type switch
        {
            JointType.SwingLimit => SwingAngle(descriptor),
            JointType.TwistLimit or JointType.TwistServo => TwistAngle(descriptor),
            _ => null,
        };
        float? limit = descriptor.Type switch
        {
            JointType.SwingLimit => descriptor.MaxSwingRadians,
            JointType.TwistLimit => MathF.Max(MathF.Abs(descriptor.MinTwistRadians), MathF.Abs(descriptor.MaxTwistRadians)),
            _ => null,
        };
        return new JointState(descriptor.Id, descriptor.Type, descriptor.BodyA, descriptor.BodyB, angle, limit, descriptor.Broken);
    }

    private float SwingAngle(JointDescriptor descriptor)
    {
        var a = GetState(descriptor.BodyA);
        var b = GetState(descriptor.BodyB);
        var axisA = Vector3.Normalize(Vector3.Transform(descriptor.AxisLocalA, a.Orientation));
        var axisB = Vector3.Normalize(Vector3.Transform(descriptor.AxisLocalB, b.Orientation));
        return MathF.Acos(Math.Clamp(Vector3.Dot(axisA, axisB), -1f, 1f));
    }

    private float TwistAngle(JointDescriptor descriptor)
    {
        var a = GetState(descriptor.BodyA);
        var b = GetState(descriptor.BodyB);
        var worldBasisA = Quaternion.Normalize(a.Orientation * descriptor.BasisA);
        var worldBasisB = Quaternion.Normalize(b.Orientation * descriptor.BasisB);
        var relative = Quaternion.Normalize(Quaternion.Conjugate(worldBasisA) * worldBasisB);
        var projected = relative.Z;
        var angle = 2f * MathF.Atan2(projected, relative.W);
        return MathF.IEEERemainder(angle, 2f * MathF.PI);
    }

    private void ProcessBreakableJoints(float dt)
    {
        if (_breakable.Count == 0)
        {
            return;
        }

        // Accumulated impulse (N·s) over the step ≈ force·dt, so compare against breakForce·dt.
        List<int>? broken = null;
        foreach (var (id, breakForce) in _breakable)
        {
            var impulse = _simulation.Solver.GetAccumulatedImpulseMagnitude(_constraints[id]);
            if (impulse > breakForce * dt)
            {
                (broken ??= new List<int>()).Add(id);
            }
        }

        if (broken is null)
        {
            return;
        }

        foreach (var id in broken)
        {
            RemoveConstraint(id);
            OnJointBroken?.Invoke(id);
        }
    }

    private static bool IsFinitePose(Vector3 value) =>
        float.IsFinite(value.X) && float.IsFinite(value.Y) && float.IsFinite(value.Z);

    private static bool IsFinitePose(Quaternion value) =>
        float.IsFinite(value.X) && float.IsFinite(value.Y) && float.IsFinite(value.Z) && float.IsFinite(value.W)
        && value.LengthSquared() > 1e-12f;
}
