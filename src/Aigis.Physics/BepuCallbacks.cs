using System.Numerics;
using System.Runtime.CompilerServices;
using BepuPhysics;
using BepuPhysics.Collidables;
using BepuPhysics.CollisionDetection;
using BepuPhysics.Constraints;
using BepuUtilities;

// Bepu requires these callback structs to expose public fields and never uses
// value-equality on them; the analyzer rules that fight that are suppressed here
// only, so the rest of the engine keeps the strict day-one policy.
#pragma warning disable CA1051 // Do not declare visible instance fields
#pragma warning disable CA1815 // Override equals/operator== on value types

namespace Aigis.Physics;

/// <summary>
/// Narrow-phase behaviour: contact springiness/recovery velocity are still one shared setting,
/// but friction (G4) is now per-collider -- <see cref="BodyFriction"/> holds each body's own
/// coefficient (defaulted to <see cref="FrictionCoefficient"/> by <see cref="PhysicsWorld.RegisterBody"/>
/// so a world that never calls <see cref="PhysicsWorld.SetFriction"/> is unaffected), combined per
/// contact with Unity's <c>PhysicsMaterialCombine.Minimum</c> semantics in
/// <see cref="ConfigureContactManifold{TManifold}"/>. A static side has no per-body property and
/// always contributes <see cref="FrictionCoefficient"/> directly.
/// </summary>
internal struct NarrowPhaseCallbacks : INarrowPhaseCallbacks
{
    public SpringSettings ContactSpringiness;
    public float MaximumRecoveryVelocity;

    /// <summary>The world's default/fallback friction coefficient -- used directly for statics
    /// (which carry no per-body <see cref="BodyFriction"/> entry) and as every dynamic/kinematic
    /// body's initial value in <see cref="BodyFriction"/>.</summary>
    public float FrictionCoefficient;

    /// <summary>Per-body collision filters (owned by <see cref="PhysicsWorld"/>); every non-static body has one.</summary>
    public CollidableProperty<SubgroupCollisionFilter> CollisionFilters;
    /// <summary>Per-body contact-generation switches (owned by <see cref="PhysicsWorld"/>).</summary>
    public CollidableProperty<bool> CollisionEnabled;

    /// <summary>Per-body "passes through everything immovable" switches (owned by <see cref="PhysicsWorld"/>) — see
    /// <c>PhysicsWorld.SetPassesThroughImmovables</c>.</summary>
    public CollidableProperty<byte> PassesThroughImmovables;

    /// <summary><see cref="PassesThroughImmovables"/> bit: the body passes through statics.</summary>
    public const byte PassesStatics = 1;

    /// <summary><see cref="PassesThroughImmovables"/> bit: the body passes through kinematic bodies.</summary>
    public const byte PassesKinematics = 2;

    /// <summary><see cref="PassesThroughImmovables"/> bit: a body passing through statics still meets one it lands on from
    /// above — see <c>PhysicsWorld.SetMeetsFloors</c>.</summary>
    public const byte MeetsFloors = 4;

    /// <summary>Per-body friction coefficient (owned by <see cref="PhysicsWorld"/>); every non-static
    /// body has one, defaulted to <see cref="FrictionCoefficient"/> at registration.</summary>
    public CollidableProperty<float> BodyFriction;

    /// <summary>Per-body contact grip (owned by <see cref="PhysicsWorld"/>), 0 for none — see
    /// <c>PhysicsWorld.SetContactGrip</c>.</summary>
    public CollidableProperty<float> ContactGrip;

    /// <summary>Per-body "soft contact" flag (owned by <see cref="PhysicsWorld"/>, see
    /// <see cref="PhysicsWorld.SetSoftContact"/>): a pair with either side set is solved with
    /// <see cref="SoftContactSpringiness"/> and <see cref="SoftContactMaximumRecoveryVelocity"/>
    /// instead of the world's own values.</summary>
    public CollidableProperty<bool> SoftContact;

    /// <summary>The contact spring of a soft pair: a low stiffness, over-damped so it cannot ring.</summary>
    public static readonly SpringSettings SoftContactSpringiness = new(PhysicsWorld.SoftContactFrequencyHz, PhysicsWorld.SoftContactDampingRatio);

    /// <summary>How fast a soft pair is pushed apart out of penetration — the "bounce" of a contact.</summary>
    public const float SoftContactMaximumRecoveryVelocity = PhysicsWorld.SoftContactMaximumRecoveryVelocity;

    /// <summary>Optional sink for touching contact pairs (owned by <see cref="PhysicsWorld"/>) so an
    /// agent can observe collisions. Null disables collection. Written during the (single-threaded)
    /// narrow phase, read after Step.</summary>
    public ContactCollector? Contacts;

    /// <summary>Cross-group pair exclusions (owned by <see cref="PhysicsWorld"/>) — see
    /// <see cref="PairExclusions"/> for why the subgroup filter cannot express these.</summary>
    public PairExclusions? ExcludedPairs;

    /// <summary>Per-body contact-pair cap (owned by <see cref="PhysicsWorld"/>) that keeps the solver out of its
    /// fallback batch — see <see cref="ContactPairBudget"/>. Null admits every pair.</summary>
    public ContactPairBudget? PairBudget;

    /// <summary>Bepu emits speculative contacts for nearby pairs (negative depth up to the speculative
    /// margin); only record a pair once its deepest contact is within this tolerance of the surface,
    /// so an observed contact means "actually touching", not merely "close".</summary>
    private const float ContactRecordDepthThreshold = -0.01f;

    // ContactSpringiness/MaximumRecoveryVelocity/FrictionCoefficient are always set by the sole
    // caller (PhysicsWorld's constructor) before this runs, so there's no default to lazily
    // fill in here anymore -- previously this method itself supplied those defaults, which
    // silently discarded any FrictionCoefficient the caller had already set.
    public readonly void Initialize(Simulation simulation)
    {
        CollisionFilters.Initialize(simulation);
        CollisionEnabled.Initialize(simulation);
        PassesThroughImmovables.Initialize(simulation);
        BodyFriction.Initialize(simulation);
        ContactGrip.Initialize(simulation);
        SoftContact.Initialize(simulation);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public readonly bool AllowContactGeneration(int workerIndex, CollidableReference a, CollidableReference b, ref float speculativeMargin)
    {
        var aStatic = a.Mobility == CollidableMobility.Static;
        var bStatic = b.Mobility == CollidableMobility.Static;
        if (!aStatic && !bStatic)
        {
            // Both are bodies (dynamic/kinematic) — consult the subgroup filter, then the cross-group
            // pair exclusions (see PairExclusions for what the subgroup filter cannot express). The
            // IsEmpty check keeps a world that never excludes a pair — every game but Triage — at one
            // bool test rather than a hash lookup per pair per step.
            if (!CollisionEnabled[a.BodyHandle] || !CollisionEnabled[b.BodyHandle]
                || !SubgroupCollisionFilter.AllowCollision(CollisionFilters[a.BodyHandle], CollisionFilters[b.BodyHandle]))
            {
                return false;
            }

            // A body that passes through the immovable passes through kinematic bodies as well as statics.
            if ((a.Mobility == CollidableMobility.Kinematic && (PassesThroughImmovables[b.BodyHandle] & PassesKinematics) != 0)
                || (b.Mobility == CollidableMobility.Kinematic && (PassesThroughImmovables[a.BodyHandle] & PassesKinematics) != 0))
            {
                return false;
            }

            return (ExcludedPairs is null || ExcludedPairs.IsEmpty
                    || !ExcludedPairs.Excluded(a.BodyHandle.Value, b.BodyHandle.Value))
                && Admit(a, b, withStatic: false);
        }

        // At least one static: allow only if the other can move (static-static never generates). A body passing through
        // statics is let through here when it meets floors; ConfigureContactManifold then keeps only an upward contact.
        if (a.Mobility == CollidableMobility.Dynamic && (!CollisionEnabled[a.BodyHandle] || PassesStaticsEntirely(a.BodyHandle)))
        {
            return false;
        }

        if (b.Mobility == CollidableMobility.Dynamic && (!CollisionEnabled[b.BodyHandle] || PassesStaticsEntirely(b.BodyHandle)))
        {
            return false;
        }

        return (a.Mobility == CollidableMobility.Dynamic || b.Mobility == CollidableMobility.Dynamic) && Admit(a, b, withStatic: true);
    }

    private readonly bool PassesStaticsEntirely(BodyHandle body)
        => (PassesThroughImmovables[body] & (PassesStatics | MeetsFloors)) == PassesStatics;

    /// <summary>Whether a static pair is one a floor-meeting pass-through body (<see cref="MeetsFloors"/>) has with a
    /// static it is NOT standing on: no contact faces at least <c>PhysicsWorld.FloorNormalMinUp</c> up. ANY floor contact
    /// keeps the whole manifold — a mesh or compound static (a kerb, a ramp beside a wall) can hold floor and wall contacts
    /// in one manifold, and dropping the lot for one wall contact let the road go with it. Bepu's normal points from B to
    /// A, so it is the up direction from the static as it stands when the static is B. Charged to the pair budget before
    /// this runs, a wall pair it then drops still counts for that step (only while the vehicle is tipping).</summary>
    private readonly bool HasNoFloorContact<TManifold>(CollidablePair pair, ref TManifold manifold)
        where TManifold : unmanaged, IContactManifold<TManifold>
    {
        CollidableReference body;
        float sign;
        if (pair.B.Mobility == CollidableMobility.Static && pair.A.Mobility == CollidableMobility.Dynamic)
        {
            (body, sign) = (pair.A, 1f);
        }
        else if (pair.A.Mobility == CollidableMobility.Static && pair.B.Mobility == CollidableMobility.Dynamic)
        {
            (body, sign) = (pair.B, -1f);
        }
        else
        {
            return false;
        }

        if ((PassesThroughImmovables[body.BodyHandle] & (PassesStatics | MeetsFloors)) != (PassesStatics | MeetsFloors))
        {
            return false;
        }

        for (var i = 0; i < manifold.Count; i++)
        {
            manifold.GetContact(i, out _, out var normal, out _, out _);
            if (normal.Y * sign >= PhysicsWorld.FloorNormalMinUp)
            {
                return false;
            }
        }

        return true;
    }

    /// <summary>The last test a pair passes: charged to <see cref="PairBudget"/> only once everything else has let it
    /// through, so a filtered pair never uses up a body's budget.</summary>
    private readonly bool Admit(CollidableReference a, CollidableReference b, bool withStatic)
        => PairBudget is null || PairBudget.TryAdmit(DynamicHandle(a), DynamicHandle(b), withStatic);

    private static int DynamicHandle(CollidableReference collidable)
        => collidable.Mobility == CollidableMobility.Dynamic ? collidable.BodyHandle.Value : -1;

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public readonly bool AllowContactGeneration(int workerIndex, CollidablePair pair, int childIndexA, int childIndexB) => true;

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public readonly bool ConfigureContactManifold<TManifold>(int workerIndex, CollidablePair pair, ref TManifold manifold, out PairMaterialProperties pairMaterial)
        where TManifold : unmanaged, IContactManifold<TManifold>
    {
        if (HasNoFloorContact(pair, ref manifold))
        {
            // A wall (or anything met side-on) a floor-meeting pass-through body goes on passing through: no
            // constraint, and no recorded contact either.
            pairMaterial = default;
            return false;
        }

        // The lower of the two, as Unity combines them — raised to either side's contact grip, when it has one (an
        // overlay over the frictions, never a change to them: see PhysicsWorld.SetContactGrip).
        pairMaterial.FrictionCoefficient = MathF.Max(
            MathF.Min(FrictionOf(pair.A), FrictionOf(pair.B)),
            MathF.Max(GripOf(pair.A), GripOf(pair.B)));
        var soft = IsSoft(pair.A) || IsSoft(pair.B);
        pairMaterial.MaximumRecoveryVelocity = soft ? SoftContactMaximumRecoveryVelocity : MaximumRecoveryVelocity;
        pairMaterial.SpringSettings = soft ? SoftContactSpringiness : ContactSpringiness;

        if (Contacts is not null && manifold.Count > 0)
        {
            // Record the deepest contact's normal + depth as the pair's summary. Single-threaded
            // stepping means appending to the shared list here is safe and deterministic.
            var maxDepth = float.NegativeInfinity;
            var normal = default(Vector3);
            for (var i = 0; i < manifold.Count; i++)
            {
                manifold.GetContact(i, out _, out var contactNormal, out var depth, out _);
                if (depth > maxDepth)
                {
                    maxDepth = depth;
                    normal = contactNormal;
                }
            }

            if (maxDepth >= ContactRecordDepthThreshold)
            {
                var (a, staticA) = Identify(pair.A);
                var (b, staticB) = Identify(pair.B);

                // Bepu's manifold normal points from collidable B to A, but which side Bepu assigns to
                // A/B is arbitrary — so the raw sign isn't tied to static-vs-dynamic. Canonicalize:
                // when exactly one side is static, report the dynamic side as A and the static as B and
                // flip the normal to match, so a reported normal always points away from the static
                // surface (e.g. +Y for a foot resting on the ground) — a predictable support direction.
                if (staticA && !staticB)
                {
                    (a, staticA, b, staticB) = (b, staticB, a, staticA);
                    normal = -normal;
                }

                Contacts.Records.Add(new PhysicsWorld.ContactPoint(a, staticA, b, staticB, normal, maxDepth, manifold.Count));
            }
        }

        return true;
    }

    private static (int Id, bool IsStatic) Identify(CollidableReference collidable)
        => collidable.Mobility == CollidableMobility.Static
            ? (collidable.StaticHandle.Value, true)
            : (collidable.BodyHandle.Value, false);

    /// <summary>A collidable's own friction contribution for the <c>Minimum</c> combine: the
    /// world default for a static (it carries no per-body <see cref="BodyFriction"/> entry), else
    /// that body's own value.</summary>
    private readonly float FrictionOf(CollidableReference collidable)
        => collidable.Mobility == CollidableMobility.Static ? FrictionCoefficient : BodyFriction[collidable.BodyHandle];

    /// <summary>A body's contact grip (0 for a static, or a body with none).</summary>
    private readonly float GripOf(CollidableReference collidable)
        => collidable.Mobility == CollidableMobility.Static ? 0f : ContactGrip[collidable.BodyHandle];

    private readonly bool IsSoft(CollidableReference collidable)
        => collidable.Mobility != CollidableMobility.Static && SoftContact[collidable.BodyHandle];

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    // Per-child manifolds pass through: the floor test (HasNoFloorContact) is made once on the pair's whole manifold.
    public readonly bool ConfigureContactManifold(int workerIndex, CollidablePair pair, int childIndexA, int childIndexB, ref ConvexContactManifold manifold) => true;

    public readonly void Dispose()
    {
        CollisionFilters.Dispose();
        CollisionEnabled.Dispose();
        PassesThroughImmovables.Dispose();
        BodyFriction.Dispose();
        ContactGrip.Dispose();
        SoftContact.Dispose();
    }
}

/// <summary>
/// Applies uniform gravity per substep. Kept as the canonical Bepu form so the
/// vectorized integration path (and its determinism) is preserved.
/// </summary>
internal struct PoseIntegratorCallbacks : IPoseIntegratorCallbacks
{
    public Vector3 Gravity;

    /// <summary>Optional terminal (maximum downward) fall speed in m/s, mirroring
    /// <see cref="PhysicsWorld.TerminalVelocity"/> -- <c>null</c> disables clamping entirely, which
    /// is the default and leaves free-fall behavior byte-for-byte identical to before this knob
    /// existed. Only <c>velocity.Linear.Y</c> is clamped (never below <c>-TerminalVelocity</c>);
    /// gravity itself (<see cref="Gravity"/>) is never altered.</summary>
    public float? TerminalVelocity;

    /// <summary>Whether this world's dynamic bodies are confined to a horizontal plane, mirroring
    /// <see cref="PhysicsWorld.PlanarMotion"/>. <see langword="false"/> is the default and leaves
    /// integration byte-for-byte identical to before this knob existed.
    /// <para>Kinematic bodies are untouched, because <see cref="IntegrateVelocityForKinematics"/> is
    /// false -- a door or a lift driven by an external pose still works on such a world.</para></summary>
    public bool PlanarMotion;

    private Vector3Wide _gravityDt;
    private Vector<float> _minLinearY;

    public PoseIntegratorCallbacks(Vector3 gravity, float? terminalVelocity = null, bool planarMotion = false) : this()
    {
        Gravity = gravity;
        TerminalVelocity = terminalVelocity;
        PlanarMotion = planarMotion;
    }

    public readonly AngularIntegrationMode AngularIntegrationMode => AngularIntegrationMode.Nonconserving;
    public readonly bool AllowSubstepsForUnconstrainedBodies => false;
    public readonly bool IntegrateVelocityForKinematics => false;

    public readonly void Initialize(Simulation simulation) { }

    public void PrepareForIntegration(float dt)
    {
        _gravityDt = Vector3Wide.Broadcast(Gravity * dt);
        if (TerminalVelocity is { } terminalVelocity)
        {
            _minLinearY = new Vector<float>(-terminalVelocity);
        }
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public readonly void IntegrateVelocity(
        Vector<int> bodyIndices,
        Vector3Wide position,
        QuaternionWide orientation,
        BodyInertiaWide localInertia,
        Vector<int> integrationMask,
        int workerIndex,
        Vector<float> dt,
        ref BodyVelocityWide velocity)
    {
        velocity.Linear += _gravityDt;
        if (TerminalVelocity.HasValue)
        {
            velocity.Linear.Y = Vector.Max(velocity.Linear.Y, _minLinearY);
        }

        // Applied LAST so it also swallows the gravity term above. A planar world authored with a
        // non-zero gravity would otherwise accumulate a vertical velocity here that nothing removes
        // until PhysicsWorld's own post-step pass -- i.e. after the solver has already spent its
        // iterations resolving contacts against a motion that is about to be discarded.
        //
        // ANGULAR keeps only Y. A body free to spin about a horizontal axis is one whose parts leave
        // the plane, which for a jointed ragdoll means the joints and this lock pull against each
        // other every step; the rotational half is not an extra, it is what makes the linear half
        // stable.
        if (PlanarMotion)
        {
            velocity.Linear.Y = Vector<float>.Zero;
            velocity.Angular.X = Vector<float>.Zero;
            velocity.Angular.Z = Vector<float>.Zero;
        }
    }
}

#pragma warning restore CA1815
#pragma warning restore CA1051
