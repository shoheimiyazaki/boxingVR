using System.Numerics;
using BepuPhysics;
using BepuPhysics.Collidables;
using BepuPhysics.Constraints;
using BepuUtilities.Memory;

namespace Aigis.Physics;

/// <summary>
/// A fixed-step rigid-body world backed by BepuPhysics v2.
/// <para>
/// Stepping is single-threaded, which makes it <b>deterministic run-to-run on one
/// machine</b> — the property the AI-agent verify loop depends on. The public
/// surface uses plain <c>int</c> ids and <see cref="System.Numerics"/> types so the
/// physics backend never leaks into the agent/harness layers and can be swapped.
/// </para>
/// </summary>
public sealed partial class PhysicsWorld : IDisposable
{
    private readonly BufferPool _pool;
    private readonly Simulation _simulation;
    private readonly CollidableProperty<SubgroupCollisionFilter> _collisionFilters;
    private readonly CollidableProperty<bool> _collisionEnabled;
    private readonly CollidableProperty<byte> _passesThroughImmovables;
    private readonly PairExclusions _excludedPairs = new();
    private readonly ContactPairBudget _contactPairBudget = new(ContactPairsPerBody);
    private readonly CollidableProperty<float> _bodyFriction;
    private readonly CollidableProperty<float> _contactGrip;
    private readonly CollidableProperty<bool> _softContact;
    private readonly CollidableProperty<BodyDamping> _bodyDamping;
    private readonly List<BodyHandle> _bodies = new();

    /// <summary>Each body's altitude on a <see cref="PlanarMotion"/> world, by handle value. Empty
    /// (and never written) on every other world, so it costs nothing to a world that does not lock.
    /// <para>See <see cref="PlanarMotion"/> for the contract: on such a world Y is not a dynamic
    /// variable at all, so this is written when a body is created and by the two APIs that move one
    /// deliberately (<see cref="SetDynamicPose"/>, <see cref="TranslateBody"/>), and read back by
    /// <see cref="ApplyPlanarLock"/> every step.</para></summary>
    private readonly Dictionary<int, float> _planarY = new();

    private readonly float _frictionCoefficient;
    private int _nextGroupId;
    private bool _disposed;

    /// <summary>Per-body linear/angular drag (G5): <c>v *= 1/(1 + damping*dt)</c>, applied once per
    /// <see cref="Step(float)"/> -- see that method's comment for why once-per-step (not per-substep,
    /// unlike gravity in <see cref="PoseIntegratorCallbacks"/>) is the more faithful port of Unity's
    /// once-per-FixedUpdate drag. Zero for every body by default (a no-op).</summary>
    /// <param name="Linear">Linear drag coefficient, in reciprocal seconds.</param>
    /// <param name="Angular">Angular drag coefficient, in reciprocal seconds.</param>
    /// <param name="FrameVelocity">The velocity drag is measured against — see
    /// <see cref="SetFrameVelocity"/>. Carried in the same record as the coefficients so
    /// <see cref="ApplyDamping"/>, which runs over every body every step, gathers one property
    /// instead of two.</param>
    private readonly record struct BodyDamping(float Linear, float Angular, Vector3 FrameVelocity);

    /// <summary>How many constraint batches the solver may make before it falls back to its sequential batch, which
    /// this world never lets it reach — see <see cref="ContactPairBudget"/>. Bepu's default is 64; a body with more
    /// constraints than that is a vehicle in a crowd, not a mistake. Batches are made only as they are needed, so an
    /// ordinary scene costs the same as at 64.</summary>
    public const int SolverBatchLimit = 1024;

    /// <summary>The most non-static contact pairs one dynamic body takes part in per step; past it, a pair is not
    /// collided that step (<see cref="ContactPairsRefused"/>). Sized against <see cref="SolverBatchLimit"/> with room
    /// left for joints — see <see cref="ContactPairBudget"/> for the arithmetic.</summary>
    public const int ContactPairsPerBody = 192;

    /// <summary>Contact pairs refused since construction because a body was at <see cref="ContactPairsPerBody"/>.</summary>
    public long ContactPairsRefused => _contactPairBudget.Refused;

    /// <summary>Number of fixed steps advanced since construction.</summary>
    public int StepCount { get; private set; }

    /// <summary>Gravity used by the world (m/s^2). Set at construction.</summary>
    public Vector3 Gravity { get; }

    /// <summary>Terminal (maximum downward) fall speed in m/s, or <c>null</c> (the default) to
    /// disable clamping entirely -- a world that never sets this behaves exactly as before this
    /// knob existed. When set, applied every substep in <see cref="PoseIntegratorCallbacks.IntegrateVelocity"/>
    /// immediately after gravity integration: <c>velocity.Linear.Y</c> is clamped to never go below
    /// <c>-TerminalVelocity.Value</c>. Only the downward (falling) component is clamped; upward
    /// motion and X/Z are untouched. Does not alter <see cref="Gravity"/> itself.</summary>
    public float? TerminalVelocity { get; }

    /// <summary>
    /// Whether every DYNAMIC body in this world is confined to a horizontal plane -- three degrees of
    /// freedom (X, Z and yaw) instead of six. <see langword="false"/> is the default and leaves the
    /// world byte-for-byte identical to before this knob existed.
    ///
    /// <para><b>Enforced twice, on purpose.</b> Bepu's order within a step is integrate-velocity,
    /// solve, integrate-position. <see cref="PoseIntegratorCallbacks"/> zeroes the out-of-plane
    /// components in the first of those, which is what stops gravity and applied forces ever building
    /// a vertical velocity; the solver can still hand one back from a contact impulse, so
    /// <see cref="ApplyPlanarLock"/> zeroes them again after <see cref="Simulation.Timestep"/>. Doing
    /// only the first leaks a contact's vertical push into the position integration of that same step;
    /// doing only the second lets the solver spend its whole iteration budget resolving a vertical
    /// motion that is about to be discarded.</para>
    ///
    /// <para><b>Kinematic bodies are deliberately exempt.</b> Their velocity is whatever the caller
    /// assigned (<see cref="SetKinematicVelocity"/>/<c>SetKinematicPose</c>), and a lift, a door or a
    /// carried platform on such a world must keep working. The exemption is free rather than coded:
    /// <see cref="PoseIntegratorCallbacks.IntegrateVelocityForKinematics"/> is false, and
    /// <see cref="ApplyPlanarLock"/> shares <see cref="ApplyDamping"/>'s own positive-mass guard.</para>
    ///
    /// <para><b>What it does NOT do: pin a body to one shared altitude.</b> Each body simply keeps
    /// whatever Y it was created at, so a ragdoll's thirteen parts stay at their own thirteen heights
    /// and keep their shape. A world where everything had to share one Y would flatten every jointed
    /// assembly in it.</para>
    ///
    /// <para><b>READ THIS BEFORE ADDING A MECHANIC TO A PLANAR WORLD: out-of-plane position control
    /// is impossible here, and it fails SILENTLY.</b> This lock discards vertical velocity, so any
    /// mechanism that reaches a height by pushing — a spring, a PD hold, a force-driven carry — is
    /// reduced to its horizontal component with no error and no diagnostic. Measured on Triage's own
    /// weightless stage: its carry spring aims a patient's chest at
    /// <c>PlayerController.CarryHeight</c> = 3 m and the patient rose <b>0.000 m</b>, staying at its
    /// own datum while the horizontal half of the same spring worked perfectly (held 0.11 m ahead and
    /// followed the carrier for 2.2 m). That was judged CORRECT for weightlessness — there is no
    /// "overhead" without gravity — but it was judged, not noticed, and the next mechanic may want the
    /// other answer.
    /// <para>A deliberate vertical placement is still available and is the intended route: move the
    /// body with <see cref="SetDynamicPose"/> or <see cref="TranslateBody"/>, both of which update the
    /// datum. Do not add a vertical FORCE and expect it to arrive.</para></para>
    /// </summary>
    public bool PlanarMotion { get; }

    /// <summary>
    /// A ceiling on every body's effective linear and angular damping, or <c>null</c> (the default)
    /// to leave each body's own value alone -- which is byte-for-byte the behaviour from before this
    /// knob existed.
    ///
    /// <para><b>Damping is a property of the MEDIUM, and this is where a medium belongs.</b> A body's
    /// own damping figure in this engine is a per-object drag coefficient, and callers set it to model
    /// things like "a body resting on a floor stops sliding" -- reasonable in air over ground, and
    /// simply wrong in vacuum or free fall. Rather than making every one of those call sites ask
    /// "which world am I on", a world that has thin air states its own ceiling once and every body in
    /// it is bounded by it.</para>
    ///
    /// <para>A CEILING rather than a replacement, so a body that was deliberately left undamped stays
    /// undamped: <c>min(own, ceiling)</c> can only ever reduce drag, never introduce it.</para>
    /// </summary>
    public float? MaxDamping { get; }

    /// <param name="gravity">Gravity vector (m/s^2); defaults to Earth gravity along -Y.</param>
    /// <param name="frictionCoefficient">The world's default Coulomb friction coefficient. Every
    /// body defaults to this value (see <see cref="AddDynamicBox(Vector3, Vector3, float, int?, int)"/>
    /// et al. via <see cref="RegisterBody"/>) unless overridden per-body with <see cref="SetFriction"/>,
    /// so a world where nobody calls <see cref="SetFriction"/> behaves exactly as before this knob
    /// existed. Statics carry no per-body value and always use this coefficient directly (see
    /// <see cref="NarrowPhaseCallbacks"/>). Combined per-contact using Unity's
    /// <c>PhysicsMaterialCombine.Minimum</c> semantics (<c>min(a, b)</c>).</param>
    /// <param name="terminalVelocity">Optional terminal fall speed (m/s); see <see cref="TerminalVelocity"/>.
    /// <c>null</c> (the default) disables clamping, leaving free-fall behavior unchanged.</param>
    /// <param name="planarMotion">Confine dynamic bodies to a horizontal plane; see
    /// <see cref="PlanarMotion"/>. <see langword="false"/> (the default) leaves motion unchanged.</param>
    /// <param name="maxDamping">Optional ceiling on every body's damping; see <see cref="MaxDamping"/>.
    /// <c>null</c> (the default) leaves each body's own value alone.</param>
    public PhysicsWorld(
        Vector3? gravity = null,
        float frictionCoefficient = 1f,
        float? terminalVelocity = null,
        bool planarMotion = false,
        float? maxDamping = null)
    {
        // transform-auditor finding (TASK-180 re-review): an unvalidated non-positive/non-finite
        // value here silently inverts gravity for every dynamic body. IntegrateVelocity clamps to
        // `Vector.Max(velocity.Linear.Y, -terminalVelocity)` -- a negative terminalVelocity makes
        // that floor POSITIVE (e.g. terminalVelocity=-5 -> floor=+5), so every falling body gets
        // shoved upward at 5 m/s instead of merely failing to clamp. NaN/Infinity would similarly
        // corrupt every subsequent step's velocity. Fail fast at the boundary (CLAUDE.md) rather
        // than let a bad call site discover this via an inverted-gravity bug report.
        if (terminalVelocity is { } tv && (!float.IsFinite(tv) || tv <= 0f))
        {
            throw new ArgumentOutOfRangeException(nameof(terminalVelocity), tv, "Must be finite and positive when set.");
        }

        // Same boundary reasoning as terminalVelocity above: a negative ceiling would turn every
        // body's `min(own, ceiling)` into a NEGATIVE damping, i.e. `1/(1 + d*dt)` with d < 0, which
        // AMPLIFIES velocity every step until the world goes non-finite. NaN would poison it in one.
        if (maxDamping is { } md && (!float.IsFinite(md) || md < 0f))
        {
            throw new ArgumentOutOfRangeException(nameof(maxDamping), md, "Must be finite and non-negative when set.");
        }

        Gravity = gravity ?? new Vector3(0f, -9.81f, 0f);
        TerminalVelocity = terminalVelocity;
        PlanarMotion = planarMotion;
        MaxDamping = maxDamping;
        _frictionCoefficient = frictionCoefficient;
        _pool = new BufferPool();
        _collisionFilters = new CollidableProperty<SubgroupCollisionFilter>();
        _collisionEnabled = new CollidableProperty<bool>();
        _passesThroughImmovables = new CollidableProperty<byte>();
        _bodyFriction = new CollidableProperty<float>();
        _contactGrip = new CollidableProperty<float>();
        _softContact = new CollidableProperty<bool>();
        _bodyDamping = new CollidableProperty<BodyDamping>();
        _simulation = Simulation.Create(
            _pool,
            new NarrowPhaseCallbacks
            {
                CollisionFilters = _collisionFilters,
                CollisionEnabled = _collisionEnabled,
                PassesThroughImmovables = _passesThroughImmovables,
                BodyFriction = _bodyFriction,
                ContactGrip = _contactGrip,
                SoftContact = _softContact,
                ContactSpringiness = new SpringSettings(30, 1),
                MaximumRecoveryVelocity = 2f,
                FrictionCoefficient = frictionCoefficient,
                Contacts = _contactCollector,
                ExcludedPairs = _excludedPairs,
                PairBudget = _contactPairBudget,
            },
            new PoseIntegratorCallbacks(Gravity, TerminalVelocity, PlanarMotion),
            new SolveDescription(velocityIterationCount: 8, substepCount: 1, fallbackBatchThreshold: SolverBatchLimit));
        // _bodyDamping is not read by any INarrowPhaseCallbacks/IPoseIntegratorCallbacks struct (G5
        // is applied once per Step, not per-substep -- see Step's comment), so nothing calls its
        // Initialize automatically the way Simulation.Create does for CollisionFilters/BodyFriction
        // above; it must be initialized against the simulation explicitly, here.
        _bodyDamping.Initialize(_simulation);
    }

    /// <summary>A fresh collision group id. Bodies in different groups always collide.</summary>
    public int NewCollisionGroup() => _nextGroupId++;

    /// <summary>Adds an immovable, axis-aligned box (e.g. the ground/a wall). Statics need no collision filter.</summary>
    public void AddStaticBox(Vector3 position, Vector3 halfExtents)
        => AddStaticBox(position, Quaternion.Identity, halfExtents);

    /// <summary>Adds an immovable, oriented box (e.g. a wall segment at arbitrary yaw).
    /// <paramref name="orientation"/> is a world-space rotation applied to the box's local axes
    /// -- the same convention as <see cref="AddDynamicBox(Vector3, Quaternion, Vector3, float, int?, int)"/>'s
    /// oriented overload, just for an immovable body. Statics need no collision filter.</summary>
    public void AddStaticBox(Vector3 position, Quaternion orientation, Vector3 halfExtents)
    {
        ThrowIfDisposed();
        var shape = _simulation.Shapes.Add(new Box(halfExtents.X * 2f, halfExtents.Y * 2f, halfExtents.Z * 2f));
        _simulation.Statics.Add(new StaticDescription(position, orientation, shape));
    }

    /// <summary>
    /// Adds a dynamic box and returns its stable body id.
    /// CAUTION: the id is currently the raw Bepu <see cref="BodyHandle"/> value, which is
    /// stable ONLY because bodies are never removed. Before implementing any remove/despawn
    /// verb, introduce an engine-owned id → handle map here (Bepu recycles handle values).
    /// </summary>
    public int AddDynamicBox(Vector3 position, Vector3 halfExtents, float mass, int? group = null, int subgroup = 0)
        => AddDynamicBox(position, Quaternion.Identity, halfExtents, mass, group, subgroup);

    /// <summary>Adds an oriented dynamic box.</summary>
    public int AddDynamicBox(Vector3 position, Quaternion orientation, Vector3 halfExtents, float mass, int? group = null, int subgroup = 0)
    {
        ThrowIfDisposed();
        var box = new Box(halfExtents.X * 2f, halfExtents.Y * 2f, halfExtents.Z * 2f);
        var shapeIndex = _simulation.Shapes.Add(box);
        var handle = _simulation.Bodies.Add(BodyDescription.CreateDynamic(
            new RigidPose(position, orientation),
            box.ComputeInertia(mass),
            new CollidableDescription(shapeIndex, 0.1f),
            new BodyActivityDescription(0.01f)));
        var id = RegisterBody(handle, Filter(group, subgroup));
        RegisterBodyMetadata(id, mass, new BodyShape(ShapeKind.Box, halfExtents));
        return id;
    }

    /// <summary>Adds a dynamic capsule (axis = local Y). Pass a shared <paramref name="group"/> + distinct
    /// <paramref name="subgroup"/> bit for parts that should not self-collide (ragdolls).</summary>
    public int AddDynamicCapsule(Vector3 position, Quaternion orientation, float radius, float length, float mass, int? group = null, int subgroup = 0)
    {
        ThrowIfDisposed();
        var capsule = new Capsule(radius, length);
        var shapeIndex = _simulation.Shapes.Add(capsule);
        var handle = _simulation.Bodies.Add(BodyDescription.CreateDynamic(
            new RigidPose(position, orientation),
            capsule.ComputeInertia(mass),
            new CollidableDescription(shapeIndex, 0.1f),
            new BodyActivityDescription(0.01f)));
        var id = RegisterBody(handle, Filter(group, subgroup));
        RegisterBodyMetadata(id, mass, new BodyShape(ShapeKind.Capsule, new Vector3(radius, length * 0.5f, 0f)));
        return id;
    }

    /// <summary>Adds a dynamic sphere.</summary>
    public int AddDynamicSphere(Vector3 position, float radius, float mass, int? group = null, int subgroup = 0)
    {
        ThrowIfDisposed();
        var sphere = new Sphere(radius);
        var shapeIndex = _simulation.Shapes.Add(sphere);
        var handle = _simulation.Bodies.Add(BodyDescription.CreateDynamic(
            new RigidPose(position),
            sphere.ComputeInertia(mass),
            new CollidableDescription(shapeIndex, 0.1f),
            new BodyActivityDescription(0.01f)));
        var id = RegisterBody(handle, Filter(group, subgroup));
        RegisterBodyMetadata(id, mass, new BodyShape(ShapeKind.Sphere, new Vector3(radius, 0f, 0f)));
        return id;
    }

    // The static/kinematic counterparts of the sphere and capsule factories above. They exist
    // because the model format (Aigis.Content.Model) lets any shape be static, kinematic or
    // dynamic, and a format that had to forbid "static sphere" purely because this class lacked one
    // overload would be an engine limitation leaking into an authoring contract. Each mirrors its
    // dynamic sibling exactly, minus the inertia (both are infinitely massive by definition).

    /// <summary>Adds an immovable sphere (no body, no mass — level geometry).</summary>
    public void AddStaticSphere(Vector3 position, float radius)
    {
        ThrowIfDisposed();
        var shape = _simulation.Shapes.Add(new Sphere(radius));
        _simulation.Statics.Add(new StaticDescription(position, Quaternion.Identity, shape));
    }

    /// <summary>Adds an immovable capsule (axis = local Y). <paramref name="length"/> is the straight
    /// segment between the caps, as in <see cref="AddDynamicCapsule"/>.</summary>
    public void AddStaticCapsule(Vector3 position, Quaternion orientation, float radius, float length)
    {
        ThrowIfDisposed();
        var shape = _simulation.Shapes.Add(new Capsule(radius, length));
        _simulation.Statics.Add(new StaticDescription(position, orientation, shape));
    }

    /// <summary>Adds a kinematic sphere (infinite mass; moves via velocity and pushes dynamics).</summary>
    public int AddKinematicSphere(Vector3 position, float radius, int? group = null, int subgroup = 0)
    {
        ThrowIfDisposed();
        var shapeIndex = _simulation.Shapes.Add(new Sphere(radius));
        var handle = _simulation.Bodies.Add(BodyDescription.CreateKinematic(
            new RigidPose(position),
            new CollidableDescription(shapeIndex, 0.1f),
            new BodyActivityDescription(0.01f)));
        var id = RegisterBody(handle, Filter(group, subgroup));
        RegisterBodyMetadata(id, 0f, new BodyShape(ShapeKind.Sphere, new Vector3(radius, 0f, 0f)));
        return id;
    }

    /// <summary>Adds a kinematic capsule (axis = local Y; infinite mass, pushes dynamics).</summary>
    public int AddKinematicCapsule(Vector3 position, Quaternion orientation, float radius, float length, int? group = null, int subgroup = 0)
    {
        ThrowIfDisposed();
        var shapeIndex = _simulation.Shapes.Add(new Capsule(radius, length));
        var handle = _simulation.Bodies.Add(BodyDescription.CreateKinematic(
            new RigidPose(position, orientation),
            new CollidableDescription(shapeIndex, 0.1f),
            new BodyActivityDescription(0.01f)));
        var id = RegisterBody(handle, Filter(group, subgroup));
        RegisterBodyMetadata(id, 0f, new BodyShape(ShapeKind.Capsule, new Vector3(radius, length * 0.5f, 0f)));
        return id;
    }

    /// <summary>Adds a dynamic cylinder (axis = local Y), the collider behind a modelled column,
    /// pipe, wheel or drum. <paramref name="length"/> is the FULL height, matching
    /// <see cref="AddDynamicCapsule"/>'s parameter; <see cref="BodyShape.Dimensions"/> records the
    /// half height, also matching it.</summary>
    public int AddDynamicCylinder(Vector3 position, Quaternion orientation, float radius, float length, float mass, int? group = null, int subgroup = 0)
    {
        ThrowIfDisposed();
        var cylinder = new Cylinder(radius, length);
        var shapeIndex = _simulation.Shapes.Add(cylinder);
        var handle = _simulation.Bodies.Add(BodyDescription.CreateDynamic(
            new RigidPose(position, orientation),
            cylinder.ComputeInertia(mass),
            new CollidableDescription(shapeIndex, 0.1f),
            new BodyActivityDescription(0.01f)));
        var id = RegisterBody(handle, Filter(group, subgroup));
        RegisterBodyMetadata(id, mass, new BodyShape(ShapeKind.Cylinder, new Vector3(radius, length * 0.5f, 0f)));
        return id;
    }

    /// <summary>Adds a kinematic cylinder (infinite mass; moves via velocity and pushes dynamics).</summary>
    public int AddKinematicCylinder(Vector3 position, Quaternion orientation, float radius, float length, int? group = null, int subgroup = 0)
    {
        ThrowIfDisposed();
        var shapeIndex = _simulation.Shapes.Add(new Cylinder(radius, length));
        var handle = _simulation.Bodies.Add(BodyDescription.CreateKinematic(
            new RigidPose(position, orientation),
            new CollidableDescription(shapeIndex, 0.1f),
            new BodyActivityDescription(0.01f)));
        var id = RegisterBody(handle, Filter(group, subgroup));
        RegisterBodyMetadata(id, 0f, new BodyShape(ShapeKind.Cylinder, new Vector3(radius, length * 0.5f, 0f)));
        return id;
    }

    /// <summary>Adds an immovable cylinder (no body, no mass — level geometry).</summary>
    public void AddStaticCylinder(Vector3 position, Quaternion orientation, float radius, float length)
    {
        ThrowIfDisposed();
        var shape = _simulation.Shapes.Add(new Cylinder(radius, length));
        _simulation.Statics.Add(new StaticDescription(position, orientation, shape));
    }

    /// <summary>Adds a kinematic box (infinite mass; moves via velocity and pushes dynamics).</summary>
    public int AddKinematicBox(Vector3 position, Vector3 halfExtents, int? group = null, int subgroup = 0)
        => AddKinematicBox(position, Quaternion.Identity, halfExtents, group, subgroup);

    /// <summary>Adds an oriented kinematic box -- TASK-049 part B's own G2-shaped gap: the
    /// non-oriented overload above (the only one that existed before this task) always builds
    /// at <see cref="Quaternion.Identity"/>, which cannot represent a door segment authored at
    /// an arbitrary yaw (two of Level4's three <c>DOOR_AUTO</c> segments are diagonal, not
    /// axis-aligned). Mirrors <see cref="AddStaticBox(Vector3, Quaternion, Vector3)"/>'s existing
    /// oriented-overload pattern exactly, just for a movable (kinematic) body instead of an
    /// immovable one. <see cref="SetKinematicPose(int, System.Numerics.Vector3)"/> only ever reassigns <c>Pose.Position</c>
    /// (see that method's own implementation), so the orientation set here is retained for the
    /// body's whole lifetime -- a sliding door only ever needs to translate, never re-orient,
    /// once built.</summary>
    public int AddKinematicBox(Vector3 position, Quaternion orientation, Vector3 halfExtents, int? group = null, int subgroup = 0)
    {
        ThrowIfDisposed();
        var box = new Box(halfExtents.X * 2f, halfExtents.Y * 2f, halfExtents.Z * 2f);
        var shapeIndex = _simulation.Shapes.Add(box);
        var handle = _simulation.Bodies.Add(BodyDescription.CreateKinematic(
            new RigidPose(position, orientation),
            new CollidableDescription(shapeIndex, 0.1f),
            new BodyActivityDescription(0.01f)));
        var id = RegisterBody(handle, Filter(group, subgroup));
        RegisterBodyMetadata(id, 0f, new BodyShape(ShapeKind.Box, halfExtents));
        return id;
    }

    /// <summary>Stops two bodies from colliding with each other (e.g. adjacent ragdoll parts).</summary>
    public void DisableSelfCollision(int bodyA, int bodyB)
    {
        ThrowIfDisposed();
        ref var a = ref _collisionFilters[new BodyHandle(bodyA)];
        ref var b = ref _collisionFilters[new BodyHandle(bodyB)];
        SubgroupCollisionFilter.DisableCollision(ref a, ref b);
    }

    /// <summary>Undoes <see cref="DisableSelfCollision"/> for one pair — the two bodies collide again.
    /// <para>
    /// Exists because a ragdoll that can lose a limb needs it: a whole body disables collision between
    /// all of its own parts, and once a part is severed it is no longer part of that body, so it must
    /// start colliding with the torso it fell off. Without this the detached arm passes through the body
    /// forever (port spec 04 §12's invariant #8, which this port violated until the inverse existed —
    /// the missing API was the reason, not an oversight in the game code).
    /// </para>
    /// <para>
    /// <b>Precision caveat, inherited from the bitmask representation.</b> Filtering is per-subgroup-BIT,
    /// not per-pair, so this is exact only when each body owns a distinct bit — i.e. when both were
    /// created with an explicit <c>subgroup</c> (<see cref="HumanoidRagdoll"/> does: one bit per part).
    /// If two bodies share a bit, re-enabling one pair also re-enables every pair that shares it. Callers
    /// wanting per-pair precision must allocate distinct subgroups, which is bounded at 16 per group.
    /// </para></summary>
    public void EnableSelfCollision(int bodyA, int bodyB)
    {
        ThrowIfDisposed();
        ref var a = ref _collisionFilters[new BodyHandle(bodyA)];
        ref var b = ref _collisionFilters[new BodyHandle(bodyB)];
        SubgroupCollisionFilter.EnableCollision(ref a, ref b);
    }

    /// <summary>Whether these two bodies are currently allowed to collide with each other, i.e. the
    /// state <see cref="DisableSelfCollision"/>/<see cref="EnableSelfCollision"/> write.
    ///
    /// <para>Exists because the filter is otherwise only observable through its CONSEQUENCES, and for
    /// a jointed assembly those consequences are swamped: a ragdoll's own joints pull a part back into
    /// place within a step or two whether or not contact was what moved it, so "did these two collide"
    /// cannot be answered by watching them (measured — a forearm shoved into its own chest spends
    /// exactly one step inside either way). Reading the filter directly is the only honest test of a
    /// filter, and it is a plain read of state this class already owns.</para></summary>
    public bool CanCollide(int bodyA, int bodyB)
    {
        ThrowIfDisposed();
        ref var a = ref _collisionFilters[new BodyHandle(bodyA)];
        ref var b = ref _collisionFilters[new BodyHandle(bodyB)];
        return SubgroupCollisionFilter.AllowCollision(a, b);
    }

    /// <summary>Reassigns <paramref name="bodyId"/>'s collision filter after construction — the only
    /// way to move a body INTO another body's existing group (or back out) once both already exist.
    /// <para>
    /// <b>Why this exists alongside <see cref="DisableSelfCollision"/>/<see cref="EnableSelfCollision"/>
    /// rather than those two being enough.</b> <see cref="SubgroupCollisionFilter.AllowCollision"/>
    /// returns true immediately whenever the two bodies' <c>GroupId</c>s differ — filtering only ever
    /// applies WITHIN one shared group (see that method's own remarks, and this engine's own
    /// <c>DisableSelfCollision</c> doc comment). So disabling collision between two bodies built in
    /// two different exclusive groups (<see cref="AddDynamicBox(Vector3, Vector3, float, int?, int)"/>
    /// et al.'s default when <c>group</c> is null) is a silent no-op — the two bodies keep colliding
    /// regardless. A caller that needs to suppress collision between a body built long ago in its own
    /// group and a body built in someone else's group has no choice but to first move one of them into
    /// the other's group, which is exactly what this method is for.
    /// </para>
    /// <para>
    /// Passing <paramref name="group"/><c>: null</c> gives the body a brand-new, freshly-allocated
    /// exclusive group (same as never passing a group at construction) — the body then collides with
    /// everything again, including whatever it had just been filtered against. This is a real reset,
    /// not merely "no-op": the numeric group id itself is never observed by anything else in this
    /// engine, only the collide-with-everything BEHAVIOUR is, so a fresh id restores that behaviour
    /// exactly.
    /// </para></summary>
    public void SetCollisionGroup(int bodyId, int? group, int subgroup = 0)
    {
        ThrowIfDisposed();
        if (!_masses.ContainsKey(bodyId))
        {
            throw new ArgumentException("Unknown body id.", nameof(bodyId));
        }

        _collisionFilters[new BodyHandle(bodyId)] = Filter(group, subgroup);
    }

    /// <summary>
    /// Enables or disables collision between ONE PAIR of bodies, regardless of their collision groups.
    ///
    /// <para><b>When to reach for this instead of <see cref="DisableSelfCollision"/>.</b> That one is
    /// Bepu's subgroup filter, which can only express exclusions inside a single group and therefore
    /// requires both bodies to BE in one — fine for a ragdoll's own parts, impossible when each body
    /// already belongs to a group it cannot leave. This is the cross-group case: "these two, never,
    /// whatever else they are each doing". Its cost is a hash lookup per candidate pair per step, paid
    /// only by worlds that have excluded at least one pair.</para>
    ///
    /// <para>Call it OUTSIDE <see cref="Step(float)"/> — the narrow phase reads the set (see
    /// <see cref="PairExclusions"/>). Excluding the same pair twice is a no-op, and a pair may be
    /// re-enabled by passing <paramref name="collides"/><c>: true</c>.</para>
    /// </summary>
    public void SetPairCollisionEnabled(int bodyA, int bodyB, bool collides)
    {
        ThrowIfDisposed();
        if (!_masses.ContainsKey(bodyA))
        {
            throw new ArgumentException("Unknown body id.", nameof(bodyA));
        }

        if (!_masses.ContainsKey(bodyB))
        {
            throw new ArgumentException("Unknown body id.", nameof(bodyB));
        }

        _excludedPairs.Set(bodyA, bodyB, collides);
    }

    /// <summary>Whether <see cref="SetPairCollisionEnabled"/> currently lets these two collide. Exposed
    /// so a test can assert the FILTER rather than infer it from where two bodies drifted to.</summary>
    public bool IsPairCollisionEnabled(int bodyA, int bodyB)
    {
        ThrowIfDisposed();
        // Validated like its setter and like every other per-body query here: an unknown id would
        // otherwise read back as a cheerful "yes, these collide", which is the answer a caller asking
        // about a body that does not exist is least able to notice is wrong.
        if (!_masses.ContainsKey(bodyA))
        {
            throw new ArgumentException("Unknown body id.", nameof(bodyA));
        }

        if (!_masses.ContainsKey(bodyB))
        {
            throw new ArgumentException("Unknown body id.", nameof(bodyB));
        }

        return !_excludedPairs.Excluded(bodyA, bodyB);
    }

    /// <summary>Enables or disables contact generation for one dynamic/kinematic body. A disabled
    /// body remains present and poseable, but does not collide with statics or other bodies until
    /// re-enabled. This is used for carried props whose colliders behave as triggers while held.</summary>
    public void SetCollisionEnabled(int bodyId, bool enabled)
    {
        ThrowIfDisposed();
        if (!_masses.ContainsKey(bodyId))
        {
            throw new ArgumentException("Unknown body id.", nameof(bodyId));
        }

        _collisionEnabled[new BodyHandle(bodyId)] = enabled;
    }

    /// <summary>
    /// Makes a dynamic body pass through everything IMMOVABLE — every static collider (walls, floors, level geometry)
    /// and every kinematic body (doors, lifts, decks) — while still colliding with every other dynamic body. A
    /// kinematic body has always behaved this way (kinematic-vs-static and kinematic-vs-kinematic never generate a
    /// contact), so this is what lets a body that used to be kinematic, and was routed on that understanding (a
    /// vehicle driven in from beyond the stage's walls, through the automatic doors on its lane), become dynamic
    /// without being stopped by the level. On a kinematic body the flag changes nothing (it already passes through).
    /// </summary>
    public void SetPassesThroughImmovables(int bodyId, bool ignores) =>
        SetPassesThroughImmovables(bodyId, statics: ignores, kinematics: ignores);

    /// <summary>The same switch, one half at a time: <paramref name="statics"/> — through every static collider;
    /// <paramref name="kinematics"/> — through every kinematic body. A vehicle's WHEEL is the case for the split: it
    /// rolls on the static floor and passes through the kinematic walls round the map (2026-10-21).</summary>
    public void SetPassesThroughImmovables(int bodyId, bool statics, bool kinematics)
    {
        ThrowIfDisposed();
        if (!_masses.ContainsKey(bodyId))
        {
            throw new ArgumentException("Unknown body id.", nameof(bodyId));
        }

        var handle = new BodyHandle(bodyId);
        _passesThroughImmovables[handle] = (byte)(
            (_passesThroughImmovables[handle] & NarrowPhaseCallbacks.MeetsFloors)
            | (statics ? NarrowPhaseCallbacks.PassesStatics : 0)
            | (kinematics ? NarrowPhaseCallbacks.PassesKinematics : 0));
    }

    /// <summary>The upward-facing contact a body's normal must make with a static to count as a FLOOR for
    /// <see cref="SetMeetsFloors"/>: cos 45°, so a slope or a box's top meets it and a wall's side does not.</summary>
    public const float FloorNormalMinUp = 0.70710677f;

    /// <summary>
    /// On a body that passes through statics (<see cref="SetPassesThroughImmovables(int, bool, bool)"/>), still MEETS a
    /// static where it lands on it from above — a contact whose normal, pointing from the static to the body, is at least
    /// <see cref="FloorNormalMinUp"/> up — while still passing through every static it meets side-on. A vehicle is the
    /// case (2026-10-22): its route runs in through the stage's walls, so it passes through statics, and the same switch
    /// let a tipping car turn about its own centre and bury its nose in the road instead of pivoting on its bumper.
    /// Without the pass-through this changes nothing (the body meets every static anyway).
    /// </summary>
    public void SetMeetsFloors(int bodyId, bool meets)
    {
        ThrowIfDisposed();
        if (!_masses.ContainsKey(bodyId))
        {
            throw new ArgumentException("Unknown body id.", nameof(bodyId));
        }

        var handle = new BodyHandle(bodyId);
        var flags = _passesThroughImmovables[handle];
        _passesThroughImmovables[handle] = (byte)(meets
            ? flags | NarrowPhaseCallbacks.MeetsFloors
            : flags & ~NarrowPhaseCallbacks.MeetsFloors);
    }

    /// <summary>Reads the contact-generation switch for one dynamic/kinematic body.</summary>
    public bool IsCollisionEnabled(int bodyId)
    {
        ThrowIfDisposed();
        if (!_masses.ContainsKey(bodyId))
        {
            throw new ArgumentException("Unknown body id.", nameof(bodyId));
        }

        return _collisionEnabled[new BodyHandle(bodyId)];
    }

    /// <summary>A dynamic/kinematic body's own friction coefficient, as <see cref="SetFriction"/> last set it (the world's
    /// default until then) — its contact grip (<see cref="SetContactGrip"/>) is not part of it.</summary>
    public float GetFriction(int id)
    {
        ThrowIfDisposed();
        if (!_masses.ContainsKey(id))
        {
            throw new ArgumentException("Unknown body id.", nameof(id));
        }

        return _bodyFriction[new BodyHandle(id)];
    }

    /// <summary>
    /// Gives <paramref name="bodyId"/> a contact GRIP: every contact it is in takes at least this friction coefficient,
    /// whatever the two frictions would combine to (and a static floor's side is the world's own coefficient, so this
    /// is the one way a body grips the floor harder than the world default). 0 removes it.
    /// <para><b>An overlay, not a friction.</b> It leaves <see cref="SetFriction"/>'s value alone, so code that owns a
    /// body's friction (a patient being dragged, crawling, hanging) keeps owning it while a grip comes and goes — a
    /// vehicle's tyres and underside on what they run over, and a body pinned under them (2026-10-21).</para>
    /// </summary>
    public void SetContactGrip(int bodyId, float grip)
    {
        ThrowIfDisposed();
        if (!_masses.ContainsKey(bodyId))
        {
            throw new ArgumentException("Unknown body id.", nameof(bodyId));
        }

        if (!(grip >= 0f) || !float.IsFinite(grip))
        {
            throw new ArgumentOutOfRangeException(nameof(grip), grip, "A grip is a non-negative friction coefficient.");
        }

        _contactGrip[new BodyHandle(bodyId)] = grip;
    }

    /// <summary>Sets a dynamic/kinematic body's own friction coefficient (G4). Combined per-contact
    /// with the other side using Unity's <c>PhysicsMaterialCombine.Minimum</c> semantics (see
    /// <see cref="NarrowPhaseCallbacks"/>) -- e.g. one frictionless body (friction 0) makes every
    /// contact it takes part in frictionless, regardless of the other side's value — unless either side has a contact
    /// grip (<see cref="SetContactGrip"/>), which raises the contact to at least that. Every body
    /// defaults to the world's <c>frictionCoefficient</c> (constructor parameter) until this is
    /// called, so a world where nobody calls this behaves exactly as before this API existed.
    /// <para><b>Never pass a <see cref="RaycastHit.Id"/> whose <see cref="RaycastHit.IsStatic"/> was
    /// true</b> (nor a <see cref="ContactPoint"/>'s A/B whose Static flag was set). Static handles
    /// live in a disjoint id space from body ids, so a static's handle value can numerically equal
    /// an unrelated dynamic body's id — the <c>_masses</c> guard below would accept it and this
    /// would silently retune the wrong body rather than throwing. The same caveat applies to
    /// <see cref="SetDamping"/>, <see cref="GetDamping"/>, <see cref="GetMass"/> and
    /// <see cref="ApplyLinearImpulse"/>.</para></summary>
    public void SetFriction(int id, float friction)
    {
        ThrowIfDisposed();
        if (friction < 0f || !float.IsFinite(friction))
        {
            throw new ArgumentOutOfRangeException(nameof(friction));
        }

        if (!_masses.ContainsKey(id))
        {
            throw new ArgumentException("Unknown body id.", nameof(id));
        }

        _bodyFriction[new BodyHandle(id)] = friction;
    }

    /// <summary>Contact stiffness of a pair with a soft side, Hz (the world's own is 30).
    /// <para>8 until 2026-09-29: that soft a spring let a closing claw push straight through the body
    /// it was shutting on (「掴むと爪を患者が貫通して外に出る」). The bounce the softness was for is
    /// removed by the over-damping and the slow recovery below, not by the stiffness.</para></summary>
    public const float SoftContactFrequencyHz = 30f;

    /// <summary>...its damping ratio. Over-damped, so a soft contact settles instead of ringing.</summary>
    public const float SoftContactDampingRatio = 3f;

    /// <summary>...and how fast it pushes a penetrating pair apart, m/s (the world's own is 2).</summary>
    public const float SoftContactMaximumRecoveryVelocity = 0.5f;

    /// <summary>
    /// Makes every contact this body takes part in SOFT: a low, over-damped contact spring and a slow
    /// recovery out of penetration, so what it touches is cushioned rather than bounced off
    /// (2026-09-28, the ship arm's claw: 「患者とアームの弾性率をごく小さく。爪に当たってバウンドしない
    /// ように」). The pair is soft if EITHER side is, so flagging the tool is enough and the bodies it
    /// handles keep their ordinary contacts with everything else. Same id caveat as
    /// <see cref="SetFriction"/>: never a static's handle.
    /// </summary>
    public void SetSoftContact(int id, bool soft)
    {
        ThrowIfDisposed();
        if (!_masses.ContainsKey(id))
        {
            throw new ArgumentException("Unknown body id.", nameof(id));
        }

        _softContact[new BodyHandle(id)] = soft;
    }

    /// <summary>Sets a dynamic body's linear/angular drag (G5): each is applied once per
    /// <see cref="Step(float)"/> as <c>v *= 1/(1 + damping*dt)</c> (Unity's own once-per-FixedUpdate
    /// formulation). Every body defaults to zero damping (a no-op) until this is called.</summary>
    public void SetDamping(int id, float linearDamping, float angularDamping)
    {
        ThrowIfDisposed();
        if (linearDamping < 0f || !float.IsFinite(linearDamping))
        {
            throw new ArgumentOutOfRangeException(nameof(linearDamping));
        }

        if (angularDamping < 0f || !float.IsFinite(angularDamping))
        {
            throw new ArgumentOutOfRangeException(nameof(angularDamping));
        }

        if (!_masses.ContainsKey(id))
        {
            throw new ArgumentException("Unknown body id.", nameof(id));
        }

        // `with`, not a fresh record: the frame velocity is set independently and must survive this.
        // Triage changes a patient's damping on eight different state transitions (picked up, thrown,
        // dropped, dragged, damaged, landed, grafted, released by a dog), and a SetDamping that reset
        // the frame would put a carried body back into world-relative drag on the next one of them.
        _bodyDamping[new BodyHandle(id)] = _bodyDamping[new BodyHandle(id)] with
        {
            Linear = linearDamping,
            Angular = angularDamping,
        };
    }

    /// <summary>
    /// Sets the velocity this body's linear drag is measured AGAINST — the velocity of the frame it is
    /// riding in. Linear damping becomes <c>v = f + (v − f)/(1 + damping·dt)</c>; angular damping is
    /// untouched.
    ///
    /// <para><b>Why this has to exist in the engine at all.</b> Drag toward world zero is not Galilean
    /// invariant, so a body co-moving with a moving platform is dragged backwards off it for no
    /// physical reason. Measured with Triage's own floor profile (1.6/s) on a body with no gravity and
    /// no contacts: a body at rest holds 0.000 m/s relative to its frame after one second, while the
    /// same body co-moving at 14 m/s has lost <b>11.114 m/s</b> relative to the frame it is standing
    /// on — 22 m/s² of pure fiction, more than half this game's gravity. A moving platform therefore
    /// could not carry anything, which is why the callers that need one all grew a workaround instead
    /// (see the train stage's own history).</para>
    ///
    /// <para><b>Why not in the game layer.</b> <see cref="ApplyDamping"/> runs inside
    /// <see cref="Step(float)"/>, so a caller cannot interpose. Reimplementing drag outside would mean
    /// zeroing every body's damping and applying <c>−(v−f)·d·m·dt</c> as an impulse each step: that is
    /// a first-order approximation of the exact <c>1/(1+d·dt)</c> the engine applies, and every
    /// settle baseline in this repository is calibrated against the exact form. It would silently
    /// re-tune the whole game.</para>
    ///
    /// <para><b>Translation only, by design.</b> A frame with an angular velocity would need
    /// <c>ω × r</c> terms and a per-body lever arm, which is a different feature; the platforms in
    /// this repository (escalators, gondolas, wagons) translate. A rotating one should widen this to a
    /// record rather than reinterpret this vector.</para>
    ///
    /// <para>Defaults to <see cref="Vector3.Zero"/>, which reproduces the previous behaviour exactly —
    /// so every level that never calls this is bit-identical.</para>
    /// </summary>
    /// <exception cref="ArgumentOutOfRangeException">Any component is not finite.</exception>
    /// <exception cref="ArgumentException">No such body.</exception>
    public void SetFrameVelocity(int id, Vector3 velocity)
    {
        ThrowIfDisposed();
        if (!float.IsFinite(velocity.X) || !float.IsFinite(velocity.Y) || !float.IsFinite(velocity.Z))
        {
            throw new ArgumentOutOfRangeException(nameof(velocity), velocity, "Frame velocity must be finite.");
        }

        if (!_masses.ContainsKey(id))
        {
            throw new ArgumentException("Unknown body id.", nameof(id));
        }

        _bodyDamping[new BodyHandle(id)] = _bodyDamping[new BodyHandle(id)] with { FrameVelocity = velocity };
    }

    /// <summary>Reads back the frame velocity set by <see cref="SetFrameVelocity"/>
    /// (<see cref="Vector3.Zero"/> if never set).</summary>
    public Vector3 GetFrameVelocity(int id)
    {
        ThrowIfDisposed();
        if (!_masses.ContainsKey(id))
        {
            throw new ArgumentException("Unknown body id.", nameof(id));
        }

        return _bodyDamping[new BodyHandle(id)].FrameVelocity;
    }

    /// <summary>Reads back a body's current linear/angular drag as set by <see cref="SetDamping"/>
    /// (zero for both if never set).</summary>
    public (float Linear, float Angular) GetDamping(int id)
    {
        ThrowIfDisposed();
        if (!_masses.ContainsKey(id))
        {
            throw new ArgumentException("Unknown body id.", nameof(id));
        }

        var damping = _bodyDamping[new BodyHandle(id)];
        return (damping.Linear, damping.Angular);
    }

    /// <summary>Advances the world by one fixed step (single-threaded => deterministic).</summary>
    public void Step(float dt)
    {
        ThrowIfDisposed();
        _contactCollector.Clear();
        _contactPairBudget.BeginStep();
        _simulation.Timestep(dt);
        ApplyDamping(dt);
        // AFTER the damping pass, and that order is load-bearing rather than arbitrary (an earlier
        // revision had it the other way round with a comment claiming the opposite -- transform-auditor
        // finding, 2026-09-05). ApplyDamping computes `v = f + (v - f)*k` against a body's FRAME
        // velocity f, so with the lock first a body whose frame has a vertical component comes out at
        // `f.Y*(1 - k)` -- not zero. Locking last is unconditional whatever the frame is.
        //
        // Nothing in this repository sets a vertical frame velocity today (SetFrameVelocity's own
        // callers are escalators, gondolas and wagons, all of which translate horizontally), so the
        // old order was latent rather than broken. It would have stopped being latent the moment a
        // planar world was put on a rising platform.
        ApplyPlanarLock();
        StepCount++;
        ProcessBreakableJoints(dt);
        MonitorAnomalies();
    }

    /// <summary>Applies G5 per-body linear/angular drag once per fixed step (not per substep).
    /// Unity applies drag exactly once per <c>FixedUpdate</c> as <c>v *= 1/(1 + drag*dt)</c>, so
    /// doing this once around <see cref="Simulation.Timestep"/> is a more faithful port than
    /// hooking it into <see cref="PoseIntegratorCallbacks.IntegrateVelocity"/> (which runs per
    /// substep) -- and keeps that vectorized, per-substep path free of a per-lane gather against a
    /// per-body property, preserving the determinism/substepping contract (Hard rule 5). Kinematic
    /// bodies are skipped: their velocity is driven externally (<see cref="SetKinematicVelocity"/>)
    /// and must stay exactly what the caller set, not decay.</summary>
    private void ApplyDamping(float dt)
    {
        foreach (var handle in _bodies)
        {
            if (!_masses.TryGetValue(handle.Value, out var mass) || mass <= 0f)
            {
                continue;
            }

            var damping = _bodyDamping[handle];
            var (linearDamping, angularDamping) = MaxDamping is { } ceiling
                ? (MathF.Min(damping.Linear, ceiling), MathF.Min(damping.Angular, ceiling))
                : (damping.Linear, damping.Angular);
            if (linearDamping == 0f && angularDamping == 0f)
            {
                continue;
            }

            var body = _simulation.Bodies[handle];

            // Measured against the body's FRAME, not against world zero (SetFrameVelocity says why).
            // With the default zero frame this is algebraically the old expression, but it is NOT
            // bit-identical for a non-zero frame -- which is the point.
            //
            // ANGULAR is deliberately left alone: this engine's frames translate, and a translating
            // frame does not change an angular velocity at all. Subtracting anything here would be
            // wrong rather than merely unnecessary.
            var linear = 1f / (1f + (linearDamping * dt));
            body.Velocity.Linear = damping.FrameVelocity
                + ((body.Velocity.Linear - damping.FrameVelocity) * linear);
            body.Velocity.Angular *= 1f / (1f + (angularDamping * dt));
        }
    }

    /// <summary>Records a body's new altitude on a <see cref="PlanarMotion"/> world; a no-op on every
    /// other world. See <see cref="_planarY"/>.</summary>
    private void SetPlanarDatum(int id, float y)
    {
        if (PlanarMotion)
        {
            _planarY[id] = y;
        }
    }

    /// <summary>
    /// Re-applies <see cref="PlanarMotion"/> to every dynamic body after the solver has run --
    /// the second half of the enforcement that property's own doc comment describes. A no-op, and one
    /// branch, on every world that did not ask for it.
    ///
    /// <para><b>It cannot be folded into <see cref="ApplyDamping"/>.</b> That loop skips any body
    /// whose damping is zero, which is exactly the set of bodies most likely to have been given a
    /// vertical push by a contact.</para>
    ///
    /// <para><b>The POSITION is restored too, and that was measured rather than assumed.</b> The first
    /// version of this pass zeroed velocity only, on the reasoning that a body with no vertical
    /// velocity has nothing for the next step's position integration to act on. That is true BETWEEN
    /// steps and false within one: the solver runs after <c>IntegrateVelocity</c> and before
    /// <c>IntegratePosition</c>, so a contact's vertical push moves the body that same step and this
    /// pass only sees the aftermath. Measured on two interpenetrating spheres separating over 300
    /// steps: <b>0.280 m of vertical creep</b>, which on a ward is a patient sinking through the deck
    /// over a round. Writing the altitude back from <see cref="_planarY"/> is exact and costs one
    /// dictionary lookup per body.</para>
    /// </summary>
    private void ApplyPlanarLock()
    {
        if (!PlanarMotion)
        {
            return;
        }

        foreach (var handle in _bodies)
        {
            // The same positive-mass guard ApplyDamping uses, and for the same reason: a kinematic
            // body's velocity is the caller's, not ours.
            if (!_masses.TryGetValue(handle.Value, out var mass) || mass <= 0f)
            {
                continue;
            }

            var body = _simulation.Bodies[handle];
            body.Velocity.Linear.Y = 0f;
            body.Velocity.Angular.X = 0f;
            body.Velocity.Angular.Z = 0f;
            if (_planarY.TryGetValue(handle.Value, out var y))
            {
                body.Pose.Position.Y = y;
            }
        }
    }

    /// <summary>The sleep threshold every body is created with — Bepu's own squared-velocity figure.
    /// Named so <see cref="SetSleepThreshold"/>'s callers and its tests can say what they are
    /// restoring rather than restating the literal at each site.</summary>
    public const float DefaultSleepThreshold = 0.01f;

    /// <summary>
    /// Sets the squared-velocity threshold below which this body may go to sleep, or a NEGATIVE value
    /// to keep it awake indefinitely.
    ///
    /// <para><b>Why it is exposed.</b> Bepu compares the threshold against
    /// <c>dot(v,v) + dot(ω,ω)</c> in world terms, so sleeping is not Galilean invariant and cannot be
    /// made so from outside the engine. Measured on a box settling onto a floor at the default
    /// <see cref="DefaultSleepThreshold"/>: at rest it slept on step 32, while the same box co-moving
    /// with a 14 m/s platform never slept in 600 steps. Two anchors therefore resolve one situation's
    /// contacts differently, which is exactly what a frame-invariance test has to be able to rule
    /// out.</para>
    ///
    /// <para><b>A threshold and not a boolean</b>, because there are two different callers: a test
    /// comparing two frames wants sleeping OFF on both sides, and a game carrying bodies on a moving
    /// platform wants the figure RAISED to what those bodies actually read. A flag can only express
    /// the first.</para>
    ///
    /// <para>Defaults to <see cref="DefaultSleepThreshold"/>, so a body this is never called on
    /// behaves exactly as before.</para>
    /// </summary>
    /// <exception cref="ArgumentOutOfRangeException">The threshold is not finite. Negative IS
    /// meaningful here (it is how Bepu spells "never sleep"), so only NaN/∞ are rejected.</exception>
    /// <exception cref="ArgumentException">No such body.</exception>
    public void SetSleepThreshold(int id, float squaredSpeed)
    {
        ThrowIfDisposed();
        if (!float.IsFinite(squaredSpeed))
        {
            throw new ArgumentOutOfRangeException(
                nameof(squaredSpeed), squaredSpeed, "Sleep threshold must be finite.");
        }

        if (!_masses.ContainsKey(id))
        {
            throw new ArgumentException("Unknown body id.", nameof(id));
        }

        _simulation.Bodies[new BodyHandle(id)].Activity.SleepThreshold = squaredSpeed;
    }

    /// <summary>Reads back this body's sleep threshold (<see cref="DefaultSleepThreshold"/> unless
    /// <see cref="SetSleepThreshold"/> changed it).</summary>
    public float GetSleepThreshold(int id)
    {
        ThrowIfDisposed();
        if (!_masses.ContainsKey(id))
        {
            throw new ArgumentException("Unknown body id.", nameof(id));
        }

        return _simulation.Bodies[new BodyHandle(id)].Activity.SleepThreshold;
    }

    /// <summary>Advances the world by <paramref name="count"/> fixed steps.</summary>
    public void Step(int count, float dt)
    {
        for (var i = 0; i < count; i++)
        {
            Step(dt);
        }
    }

    /// <summary>Reads the current state of a body by id.</summary>
    public BodyState GetState(int id)
    {
        ThrowIfDisposed();
        var body = _simulation.Bodies[new BodyHandle(id)];
        ref var pose = ref body.Pose;
        ref var velocity = ref body.Velocity;
        return new BodyState(id, pose.Position, pose.Orientation, velocity.Linear, velocity.Angular, body.Awake);
    }

    /// <summary>Snapshots every movable body (dynamic + kinematic) in insertion order.</summary>
    public IReadOnlyList<BodyState> SnapshotDynamics()
    {
        ThrowIfDisposed();
        var result = new List<BodyState>(_bodies.Count);
        foreach (var handle in _bodies)
        {
            result.Add(GetState(handle.Value));
        }

        return result;
    }

    private SubgroupCollisionFilter Filter(int? group, int subgroup)
        => group is { } g ? new SubgroupCollisionFilter(g, subgroup) : new SubgroupCollisionFilter(_nextGroupId++);

    private int RegisterBody(BodyHandle handle, SubgroupCollisionFilter filter)
    {
        _collisionFilters.Allocate(handle) = filter;
        _collisionEnabled.Allocate(handle) = true;
        _passesThroughImmovables.Allocate(handle) = 0;
        // Default every body to the world's global friction/zero damping so a caller that never
        // touches SetFriction/SetDamping sees exactly the pre-G4/G5 behavior (the safety property
        // both gaps' briefs call out explicitly).
        _bodyFriction.Allocate(handle) = _frictionCoefficient;
        _contactGrip.Allocate(handle) = 0f;
        _softContact.Allocate(handle) = false;
        _bodyDamping.Allocate(handle) = default;
        _bodies.Add(handle);
        if (PlanarMotion)
        {
            // The altitude a body is CREATED at is its altitude for the rest of the world's life --
            // see PlanarMotion. Read from the simulation rather than taken as a parameter so every
            // Add* overload funnels through this one place and none can forget.
            _planarY[handle.Value] = _simulation.Bodies[handle].Pose.Position.Y;
        }

        return handle.Value;
    }

    private void ThrowIfDisposed()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        // NOTE: do NOT dispose _collisionFilters/_collisionEnabled/_bodyFriction/_softContact here. Simulation.Dispose() disposes
        // all three via NarrowPhaseCallbacks.Dispose() (the struct fields alias these same instances);
        // an explicit call would double-return their pooled buffers.
        _simulation.Dispose();
        // _bodyDamping is NOT owned by NarrowPhaseCallbacks (see the constructor's comment), so
        // nothing above disposes it -- unlike _collisionFilters/_collisionEnabled/_bodyFriction, it must be disposed
        // explicitly here, before the pool it was allocated from is cleared.
        _bodyDamping.Dispose();
        _pool.Clear();
    }
}
