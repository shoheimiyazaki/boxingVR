using System.Numerics;

namespace Aigis.Physics;

/// <summary>
/// Assembles a full 13-part humanoid ragdoll — pelvis/chest/head, two arms (upper+fore),
/// two legs (thigh+shin+foot) — entirely from <b>boxes</b> and entirely through the public
/// <see cref="PhysicsWorld"/> surface (no Bepu types), with anatomically-limited joints
/// (ball-and-socket with swing/twist cones for shoulders/hips/spine/neck/ankles; 1-DOF hinges with
/// a bend-angle limit for elbows/knees). Independent of, and unrelated to,
/// <see cref="Ragdoll"/> (the older 5-part capsule/sphere ragdoll used by the agent-
/// perception golden tests) — this type exists for a box-only look and a full leg set (the
/// parts a future walking controller needs), not to replace it.
/// </summary>
public static partial class HumanoidRagdoll
{
    /// <summary>How many subgroup bits <see cref="BuildCore"/> hands out to a single ragdoll's own
    /// 13 parts (bits 0..12 — see its <c>AddDynamicBox(..., group, N)</c> calls). A caller that joins
    /// a ragdoll's <see cref="Parts.GroupId"/> with its own body (e.g. <c>Game.Triage</c>'s
    /// <c>TriageGame.CarryCollision.cs</c>, or <c>Game.RagdollPlayground</c>'s mouse-grab cursor) MUST
    /// pick a subgroup bit at or above this count, or it will silently collide with one of the
    /// ragdoll's own parts — the exact silent-no-op class of bug this file's own collision-filter
    /// docs warn about. Machine-checked by
    /// <c>TriageHeldPatientCollisionTests.HeldPlayerSubgroupBit_IsAboveEveryRagdollPartBit</c> rather
    /// than left as a comment a future added part could silently invalidate.</summary>
    public const int PartSubgroupCount = 13;

    /// <summary>The body ids of an assembled humanoid, root first, plus the shared collision
    /// group id (a caller adding its own body — e.g. a mouse-grab cursor — that must not
    /// collide with this ragdoll needs to join this same group and disable collision against
    /// each part; a different group would not suppress collision, per <see cref="PhysicsWorld.DisableSelfCollision"/>).</summary>
    public readonly record struct Parts(
        int Pelvis, int Chest, int Head,
        int LeftUpperArm, int RightUpperArm, int LeftForearm, int RightForearm,
        int LeftThigh, int RightThigh, int LeftShin, int RightShin, int LeftFoot, int RightFoot,
        int GroupId)
    {
        public int[] All =>
        [
            Pelvis, Chest, Head, LeftUpperArm, RightUpperArm, LeftForearm, RightForearm,
            LeftThigh, RightThigh, LeftShin, RightShin, LeftFoot, RightFoot,
        ];
    }

    /// <summary>What a <see cref="RenderPart"/> is FOR, so a caller that recolours parts by meaning
    /// (Triage's jacket/pants/skin/necrosis split) can classify one without pattern-matching ids.</summary>
    public enum RenderPartRole
    {
        /// <summary>A physics part drawn at its own body transform — the 13 real bodies.</summary>
        Body,

        /// <summary>
        /// 2026-08-16: a HAND, rigidly attached to the forearm at the wrist — a decorative stub with
        /// no body of its own, the same shape of thing as <see cref="ChestFrontPanel"/>.
        /// <para>
        /// <b>Why not a real body, having tried one.</b> A hand jointed by a real wrist was built
        /// first and measured: it forced fifteen subgroups, a pose-solver entry (without which the
        /// hands stayed in the T-pose while the arms moved and the wrist constraint threw whole
        /// patients metres into the air), and — after all that — a resting shoulder at 459 N against
        /// this game's own 450 N "a settled patient must not be near tearing" bound, plus a wrist
        /// that tore off during an ordinary drag. Two more bodies per patient bought a joint whose
        /// only gameplay was that it broke when nobody wanted it to. Rigid is what a blocky hand
        /// looks like anyway at this game's framing, and a severed forearm still takes its hand with
        /// it because the hand is drawn ON the forearm.
        /// </para>
        /// </summary>
        Hand,

        /// <summary>TASK-160: the chest's ANTERIOR panel — a decorative slab rigidly attached to the
        /// chest body at a local <c>+Z</c> offset, with no body of its own. It exists because a
        /// <see cref="HumanoidRagdoll"/> was otherwise front/back symmetric in every rendered pixel
        /// (single-colour untextured boxes, no face, no flexion cue in a straight-limbed pose), which
        /// made <c>frame-reviewer</c> structurally unable to judge supine-vs-prone from an image — see
        /// <c>TriageOperatingTableRenderTests</c>.</summary>
        ChestFrontPanel,
    }

    /// <summary>Renderable primitive metadata for one part (always <see cref="ShapeKind.Box"/> here).
    /// <para><b>Compose the model matrix with <see cref="ModelMatrix(Vector3, Quaternion)"/>, not by
    /// hand.</b> <see cref="LocalOffset"/> is the part's centre expressed in the BODY's local frame, so a
    /// hand-written composition that forgets it draws <see cref="RenderPartRole.ChestFrontPanel"/> buried
    /// inside the chest — and because the offset is exactly <see cref="Vector3.Zero"/> for all 13 real
    /// bodies, forgetting it fails SILENTLY (the cue simply vanishes) rather than visibly. The helper
    /// exists so that cannot happen; every renderer in the repo goes through it (code review, TASK-160).
    /// </para></summary>
    public readonly record struct RenderPart(
        int Id,
        BodyShape Shape,
        Vector4 Color,
        Vector3 LocalOffset = default,
        RenderPartRole Role = RenderPartRole.Body)
    {
        /// <summary>This part's world model matrix, given the transform of the BODY it rides
        /// (<see cref="Id"/>'s own <c>position</c>/<c>orientation</c> from the physics world).</summary>
        public Matrix4x4 ModelMatrix(Vector3 bodyPosition, Quaternion bodyOrientation) =>
            ModelMatrix(bodyPosition, bodyOrientation, Quaternion.Identity);

        /// <summary>As <see cref="ModelMatrix(Vector3, Quaternion)"/>, plus a purely cosmetic rotation
        /// applied in the part's OWN local frame before the body orientation (Triage's seizure twitch).
        /// <para><paramref name="localRotation"/> rotates the offset too, via
        /// <see cref="Quaternion.Concatenate"/> — which is exactly
        /// <c>CreateFromQuaternion(localRotation) * CreateFromQuaternion(bodyOrientation)</c> under
        /// System.Numerics' row-vector convention (measured, transform-auditor 2026-08-09). A decorative
        /// part that used a DIFFERENT cosmetic rotation than the body it rides would visibly detach from
        /// it, so a caller must pass the parent body's rotation, not one derived per render part.</para>
        /// <para><see cref="Quaternion.Identity"/> makes every added term a bit-exact no-op, so the 13
        /// real bodies render byte-identically to the pre-TASK-160 composition.</para></summary>
        public Matrix4x4 ModelMatrix(Vector3 bodyPosition, Quaternion bodyOrientation, Quaternion localRotation) =>
            Matrix4x4.CreateScale(Shape.Dimensions * 2f)
            * Matrix4x4.CreateFromQuaternion(localRotation)
            * Matrix4x4.CreateFromQuaternion(bodyOrientation)
            * Matrix4x4.CreateTranslation(
                bodyPosition + Vector3.Transform(LocalOffset, Quaternion.Concatenate(localRotation, bodyOrientation)));
    }

    /// <summary>
    /// Per-part masses in kg. Exists because <b>the distribution matters independently of the total</b>:
    /// a body is a multi-body system, so what governs how limbs whip, how the whole thing settles and
    /// how hard each joint is stressed is the RATIO between neighbouring parts, not the sum.
    /// <para>
    /// <see cref="Default"/> keeps the values this type has always used, so every existing caller —
    /// other games, the golden state/observation baselines, the parked balance track — is unaffected by
    /// this parameter existing. A caller that has a reference body to match passes its own instead
    /// (Triage does; see <c>TriageGame.Spawning.cs</c>).
    /// </para>
    /// <para>
    /// Note <c>Foot</c>: feet are this ragdoll's own addition — a 13-part body where several references
    /// model 11 — so a caller porting an 11-part mass table has to choose that one value itself rather
    /// than finding it in its source.
    /// </para></summary>
    public readonly record struct MassDistribution(
        float Pelvis, float Chest, float Head, float UpperArm, float Forearm, float Thigh, float Shin, float Foot)
    {
        /// <summary>The historical values, kept as the default so this parameter changes nothing for
        /// callers that do not pass one.</summary>
        public static MassDistribution Default { get; } =
            new(Pelvis: 10f, Chest: 20f, Head: 5f, UpperArm: 2f, Forearm: 1.3f, Thigh: 8f, Shin: 4f, Foot: 1f);

        /// <summary>Total mass of the assembled 13-part body (the four limb parts counted twice).</summary>
        public float Total => Pelvis + Chest + Head + 2f * (UpperArm + Forearm + Thigh + Shin + Foot);

        /// <summary>Throws if any part is not a positive, finite mass — a zero would silently make the
        /// part kinematic (<see cref="PhysicsWorld.IsKinematic"/>'s own convention) rather than fail.</summary>
        public void Validate()
        {
            foreach (var (name, mass) in new[]
            {
                (nameof(Pelvis), Pelvis), (nameof(Chest), Chest), (nameof(Head), Head), (nameof(UpperArm), UpperArm),
                (nameof(Forearm), Forearm), (nameof(Thigh), Thigh), (nameof(Shin), Shin), (nameof(Foot), Foot),
            })
            {
                if (!(mass > 0f) || !float.IsFinite(mass))
                {
                    throw new ArgumentOutOfRangeException(nameof(MassDistribution), mass,
                        $"{name} mass must be positive and finite.");
                }
            }
        }
    }

    /// <summary>The 12 anatomical joints <see cref="Build"/> creates (Pelvis is the tree root and
    /// so has no joint of its own). TASK-055 (limb detachment) — see <see cref="JointGroup"/>.</summary>
    public enum RagdollJoint
    {
        Spine, Neck,
        LeftShoulder, RightShoulder, LeftElbow, RightElbow,
        LeftHip, RightHip, LeftKnee, RightKnee, LeftAnkle, RightAnkle,
    }

    /// <summary>One anatomical joint's full constraint group -- TASK-055's read of the joint
    /// structure <see cref="BuildDetachable"/> exposes for limb detachment (docs/plans/
    /// triage-full-port.md §4 "Limb detachment": "Aigis's <c>HumanoidRagdoll</c> exposes the joint
    /// structure; walk it rather than hardcoding pairs"). <see cref="Build"/> itself already
    /// decomposes one anatomical joint into 3-4 separate Bepu constraints (a position-pinning
    /// constraint plus one or two angular-limit constraints plus a damping motor) -- a caller
    /// severing "the shoulder" must remove all of them together, not just whichever one exceeded
    /// its break threshold, or the leftover constraints would still rigidly link the two parts.
    /// </summary>
    /// <param name="Joint">Which anatomical joint this is.</param>
    /// <param name="ParentPart">The body id closer to the root (<see cref="Parts.Pelvis"/>).</param>
    /// <param name="ChildPart">The body id further from the root -- severing this joint detaches
    /// this part and everything <see cref="DownstreamParts"/> finds hanging off it.</param>
    /// <param name="PrimaryConstraintId">The position-pinning constraint (a <c>BallSocket</c> or
    /// <c>Hinge</c>) -- carries the joint's linear reaction force, so this is the constraint a
    /// caller should call <see cref="PhysicsWorld.MakeBreakable"/> on with a FORCE threshold.</param>
    /// <param name="TorqueConstraintId">The joint's primary angular-limit constraint (a
    /// <c>SwingLimit</c> for a ball-and-socket joint, a <c>TwistLimit</c> for a hinge) -- carries
    /// the joint's rotational reaction, so this is the constraint to make breakable with a TORQUE
    /// threshold.</param>
    /// <param name="AllConstraintIds">Every constraint belonging to this anatomical joint
    /// (<see cref="PrimaryConstraintId"/>, <see cref="TorqueConstraintId"/>, any remaining
    /// limit, and the damping motor) -- once either of the two breakable constraints above
    /// actually breaks, a caller must remove ALL of these (via <see cref="PhysicsWorld.RemoveConstraint"/>)
    /// to fully sever the joint, not just the one Bepu removed on its own.</param>
    /// <param name="IsSpineOrNeck">True for <see cref="RagdollJoint.Spine"/>/<see cref="RagdollJoint.Neck"/>
    /// -- the design doc's "×5 for Head and Pelvis joints" (docs/plans/triage-full-port.md §4):
    /// these two joints use a 5x stronger break threshold than every limb joint.</param>
    public readonly record struct JointGroup(
        RagdollJoint Joint, int ParentPart, int ChildPart,
        int PrimaryConstraintId, int TorqueConstraintId, IReadOnlyList<int> AllConstraintIds, bool IsSpineOrNeck);

    /// <summary>Floods the joint graph downstream of a just-severed <paramref name="severed"/>
    /// joint -- a plain BFS over the OTHER 11 joints in <paramref name="allJoints"/> (the tree
    /// minus the one edge being cut), starting from <see cref="JointGroup.ChildPart"/> and
    /// including it. This is a generic graph walk, not a hardcoded per-joint table: e.g. severing
    /// <see cref="RagdollJoint.LeftShoulder"/> discovers {LeftUpperArm, LeftForearm} by literally
    /// walking the remaining shoulder->elbow edge, exactly matching the design doc's "severing an
    /// upper arm takes the forearm with it" without this method needing to know that fact by name.
    /// <para><b>The CHEST is the tree root</b>, so severing <see cref="RagdollJoint.Spine"/>
    /// (chest-pelvis) detaches the pelvis and both legs and leaves the chest/head/arms standing as
    /// the body. That is a deliberate choice, not an accident of BFS direction: the chest is the
    /// part a game anchors a body to (Triage carries, bed-holds and drags by the chest), so it is
    /// the one part that must never itself become a downstream, detachable fragment. Every OTHER
    /// part — head, pelvis, both legs, both arms — is severable. See <see cref="BuildCore"/>'s own
    /// comment on the spine for why the constraint is still built pelvis-first.</para></summary>
    public static IReadOnlyList<int> DownstreamParts(IReadOnlyList<JointGroup> allJoints, JointGroup severed)
    {
        var visited = new HashSet<int> { severed.ChildPart };
        var queue = new Queue<int>();
        queue.Enqueue(severed.ChildPart);
        while (queue.Count > 0)
        {
            var current = queue.Dequeue();
            foreach (var joint in allJoints)
            {
                if (joint.Joint == severed.Joint)
                {
                    continue; // this is the edge being cut -- never cross it.
                }

                if (joint.ParentPart == current && visited.Add(joint.ChildPart))
                {
                    queue.Enqueue(joint.ChildPart);
                }

                if (joint.ChildPart == current && visited.Add(joint.ParentPart))
                {
                    queue.Enqueue(joint.ParentPart);
                }
            }
        }

        return visited.ToArray();
    }

    private static readonly Vector4 Torso = new(0.80f, 0.55f, 0.45f, 1f);
    private static readonly Vector4 Skin = new(0.90f, 0.70f, 0.58f, 1f);
    private static readonly Vector4 Pants = new(0.30f, 0.35f, 0.55f, 1f);

    // TwistLimit bounds rotation around each basis's local Z axis. Every joint below wants
    // that reference axis to be a specific WORLD axis at rest (every part is spawned at
    // Quaternion.Identity): the spine/neck/hip joints, the ankle twist axis, AND the elbow hinge
    // axis twist around Y; the knee hinge axis and the shoulder joints twist around X. Rotating
    // local Z by -90 degrees about X lands exactly on +Y; rotating local Z by +90 degrees
    // about Y lands exactly on +X (both directly checkable by the right-hand rule) — since
    // every connected body pair starts at identity, the same fixed basis works for both
    // sides of every joint that uses it EXCEPT the elbow (see YAxisTwistBasisMirrored below):
    // the hip/knee/spine limbs all extend along the SAME axis (Y, downward) for both sides, so
    // a shared basis is correct there, but the UPPER ARM (and so the forearm hanging off it) is
    // the first limb whose rest direction itself mirrors between sides (left extends toward -X,
    // right toward +X, per ArmChain's sideSign). The shoulder's OWN axial twist about that same
    // mirrored X axis is a dormant instance of this same class of bug -- it isn't wrong today only
    // because its passive TwistLimit is symmetric (+/-80deg) and it's driven by a properly
    // per-side-mirrored AddAngularServo (LeftArmDownTarget/RightArmDownTarget), not a signed
    // AddTwistServo; if a future change ever adds a signed shoulder-twist action, it needs the
    // exact same per-side basis mirroring as the elbow below, or it silently reintroduces this bug.
    // The ELBOW is where this actually surfaced, because its active TwistServo commands a signed
    // angle and its passive limit is asymmetric (-10..145deg): a shared, unmirrored basis there
    // measures/drives the "same" signed angle as opposite world-space bend directions per side
    // (PPO/RL code review F-01/F-02, 2026-07-23; verified geometrically in
    // PhysicsJointTests.ElbowRecipe_MirroredBasisMakesBothArmsCurlTheSameWorldDirection).
    // Public (transform-auditor, 2026-07-23): BalanceEpisode.AddArmHoldServos' active elbow
    // TwistServo must use the EXACT same per-side basis as this file's passive elbow TwistLimit, or
    // the active/passive conventions silently drift apart (an F-02-class bug). Exposed here as the
    // single source of truth rather than letting BalanceEpisode.cs re-declare its own copies of
    // these literals with nothing enforcing they stay equal.
    public static readonly Quaternion YAxisTwistBasis = Quaternion.CreateFromAxisAngle(Vector3.UnitX, -MathF.PI / 2f);
    // Mirrors YAxisTwistBasis's rotation sign (+90deg instead of -90deg) so that, applied to the
    // RIGHT elbow only, a shared positive twist-servo target curls BOTH forearms toward the same
    // world direction despite their mirrored rest directions. Left elbow keeps YAxisTwistBasis
    // unchanged (arbitrary choice of which side to mirror; either works, this one minimizes the
    // diff). Do not use this for the left elbow or for any other Y-axis joint (hip/spine/ankle),
    // whose limbs are not mirrored along the twist axis and would be broken by this basis.
    public static readonly Quaternion YAxisTwistBasisMirrored = Quaternion.CreateFromAxisAngle(Vector3.UnitX, MathF.PI / 2f);
    // Public (transform-auditor, 2026-07-24): PPO joint-limit-widening regression tests need this
    // exact basis to independently compute the shoulder's TwistAngle (the same source-of-truth
    // reasoning YAxisTwistBasis's own doc comment above already established for the elbow).
    public static readonly Quaternion XAxisTwistBasis = Quaternion.CreateFromAxisAngle(Vector3.UnitY, MathF.PI / 2f);

    /// <summary>Maximum torque (N·m) each anatomical joint's damping <see cref="PhysicsWorld.AddAngularMotor"/>
    /// may apply — the value <see cref="PhysicsWorld.AddAngularMotor"/>'s own default has always
    /// produced here, named so a caller scaling it (see <c>jointMotorScale</c> on <see cref="Build"/>)
    /// can see what it is scaling.
    /// <para><b>This is the single strongest term governing whether a ragdoll can visibly writhe.</b>
    /// The motor targets zero relative angular velocity across the joint, so it actively fights any
    /// attempt to move one part relative to its neighbour. On a limb whose moment of inertia is on the
    /// order of 0.1 kg·m², 75 N·m is hundreds of rad/s² of authority — enough that a body driven by
    /// ordinary liveliness torques behaves as one rigid block. That is the right default for a ragdoll
    /// that should flop convincingly and settle quickly, and the wrong one for a body that is supposed
    /// to thrash; hence the scale rather than a new constant.</para></summary>
    public const float DefaultJointMotorForce = 75f;

    /// <summary>The ankle's own, deliberately weaker damping motor (see <see cref="AnkleJoint"/>).
    /// Scaled by the same <c>jointMotorScale</c> so a caller loosening the body loosens it uniformly
    /// rather than leaving the ankles as the one stiff joint.</summary>
    public const float AnkleJointMotorForce = 35f;

    /// <summary>Small deliberate overlap so a joint anchor isn't spanning an empty gap. Public
    /// so a caller re-deriving a part's pivot from its center (e.g. the forward-kinematics pose
    /// solve in HumanoidRagdoll.Pose.cs) uses the exact same constant <see cref="Build"/> placed
    /// every joint with, instead of an independently-guessed copy that would silently drift out
    /// of sync if this value ever changed.</summary>
    /// <summary>
    /// How far a shoulder may leave the arm's T-pose axis (degrees) — ABDUCTION, the rotation that
    /// lifts the arm sideways out of the body's plane.
    ///
    /// <para><b>This is the narrow one, and it is narrow in a real shoulder too</b> (user, 2026-09-18:
    /// 「Y軸で肩を回すと90度ぐらいしか肩は曲がらず肩の高さより肘をあげるのが難しい」). An arm hanging at
    /// the side is already 82 degrees off the T-pose, so 110 leaves a little headroom and no more.
    /// Widening THIS to 150 was tried first and was the wrong lever: it let a wind-up be built by
    /// carrying the arm past vertical in the frontal plane, which rotates the whole limb's frame and
    /// takes the elbow's hinge axis with it — the forearm then folds the wrong way, which is exactly
    /// what the render showed.</para>
    /// </summary>
    public const float ShoulderSwingLimitDegrees = 140f;

    /// <summary>
    /// How far a shoulder may rotate about the arm's own T-pose axis (degrees).
    ///
    /// <para><b>Despite the name this is the FLEXION range</b>, and it is the one that needed to be
    /// wide. The joint's reference axis is the arm's rest direction, which in a T-pose runs sideways
    /// — so once the arm has been dropped to the side, a rotation about that same axis sweeps it
    /// forward and up through the sagittal plane. That is how a person actually raises an arm
    /// overhead, it is the motion with the large range (the user's figure: 「X軸回転だと270度ぐらい」),
    /// and at the old ±80 it was the binding constraint on every wind-up in the library.</para>
    ///
    /// <para><b>A physics change, measured as one.</b> Every patient in the game settles through
    /// these joints. 190 was tried and made a dropped ragdoll settle 0.01 degrees outside the limit the
    /// settle probe allows — so 175, which still clears the 160 the deepest wind-up in the library
    /// asks for. Every step of that was measured, not guessed.</para>
    /// </summary>
    public const float ShoulderTwistLimitDegrees = 175f;

    /// <summary>
    /// How far an ankle may swing (degrees). Left at 40 — widening it was TRIED in the same pass as
    /// the shoulder and reverted.
    /// <para>55 degrees would have let a stance foot stay level under a leg opened further, which is
    /// what puts a walking toe through the floor. But it also let a dropped ragdoll settle 7.2
    /// degrees outside its own limit, against the 5.7 the settle probe allows — measured, and the
    /// shoulder widening on its own does not do that. The ankle is the joint a settled body rests
    /// its whole weight through, so a softer one is a different and larger change than a softer
    /// shoulder, and it is not made here on the way to a sword pose.</para>
    /// </summary>
    public const float AnkleSwingLimitDegrees = 40f;

    public const float Overlap = 0.03f;

    /// <summary>How far the foot's center sits forward of the ankle. See <see cref="Overlap"/>
    /// for why this is public.</summary>
    public const float FootForwardOffset = 0.05f;

    /// <summary>The drawn hand's half-extents, derived from the forearm it hangs off rather than
    /// authored: a third of its length, about as thick, and deeper front-to-back — which is what
    /// makes a hand readable as a hand from directly above rather than as one more forearm segment.
    /// Derived so that a caller re-proportioning the arm (Triage's patient has both longer and
    /// thicker ones) gets a matching hand without having to know this exists.</summary>
    public static Vector3 HandHalfExtents(HalfExtents halfExtents) => new(
        halfExtents.Forearm.X * 0.34f,
        halfExtents.Forearm.Y * 1.02f,
        halfExtents.Forearm.Z * 1.3f);

    public static IReadOnlyList<RenderPart> DescribeForRender(Parts parts, HalfExtents halfExtents) =>
    [
        new RenderPart(parts.Pelvis, new BodyShape(ShapeKind.Box, halfExtents.Pelvis), Torso),
        new RenderPart(parts.Chest, new BodyShape(ShapeKind.Box, halfExtents.Chest), Torso),
        new RenderPart(parts.Head, new BodyShape(ShapeKind.Box, halfExtents.Head), Skin),
        new RenderPart(parts.LeftUpperArm, new BodyShape(ShapeKind.Box, halfExtents.UpperArm), Skin),
        new RenderPart(parts.RightUpperArm, new BodyShape(ShapeKind.Box, halfExtents.UpperArm), Skin),
        new RenderPart(parts.LeftForearm, new BodyShape(ShapeKind.Box, halfExtents.Forearm), Skin),
        new RenderPart(parts.RightForearm, new BodyShape(ShapeKind.Box, halfExtents.Forearm), Skin),
        new RenderPart(parts.LeftThigh, new BodyShape(ShapeKind.Box, halfExtents.Thigh), Pants),
        new RenderPart(parts.RightThigh, new BodyShape(ShapeKind.Box, halfExtents.Thigh), Pants),
        new RenderPart(parts.LeftShin, new BodyShape(ShapeKind.Box, halfExtents.Shin), Pants),
        new RenderPart(parts.RightShin, new BodyShape(ShapeKind.Box, halfExtents.Shin), Pants),
        new RenderPart(parts.LeftFoot, new BodyShape(ShapeKind.Box, halfExtents.Foot), Torso),
        new RenderPart(parts.RightFoot, new BodyShape(ShapeKind.Box, halfExtents.Foot), Torso),
        // TASK-160's anterior cue. LAST on purpose: TriageDeathRenderMaterialTests locates a patient's
        // render block by relative offsets 0..12, so appending keeps every existing offset valid.
        new RenderPart(
            parts.Chest,
            new BodyShape(ShapeKind.Box, ChestFrontPanelHalfExtents(halfExtents.Chest)),
            AnteriorPanelColor(Torso),
            ChestFrontPanelLocalOffset(halfExtents.Chest),
            RenderPartRole.ChestFrontPanel),
        // The hands (2026-08-16) — see RenderPartRole.Hand for why they ride the forearm instead of
        // being bodies. Offset along the forearm's OWN local X, which is the arm's length axis, by
        // one wrist: half the forearm plus half the hand, less the same Overlap every real joint in
        // this body is placed with. Left is -X because that is the side Build puts the left arm on.
        // LAST, after the chest panel, for the same reason the panel itself is appended: callers
        // index this list by position.
        new RenderPart(
            parts.LeftForearm,
            new BodyShape(ShapeKind.Box, HandHalfExtents(halfExtents)),
            Skin,
            new Vector3(-(halfExtents.Forearm.X + HandHalfExtents(halfExtents).X - Overlap), 0f, 0f),
            RenderPartRole.Hand),
        new RenderPart(
            parts.RightForearm,
            new BodyShape(ShapeKind.Box, HandHalfExtents(halfExtents)),
            Skin,
            new Vector3(halfExtents.Forearm.X + HandHalfExtents(halfExtents).X - Overlap, 0f, 0f),
            RenderPartRole.Hand),
    ];

    /// <summary>TASK-160: half-extents of the chest's anterior panel, derived from the chest's own so a
    /// caller with different torso dimensions still gets a proportionate panel. Wide and tall enough
    /// (~72%/70% of the chest face) that it is tens of pixels — not the handful
    /// <see cref="FootForwardOffset"/> was worth — at the ~640x360 framing agent filmstrips use.
    /// <para>Private, with <see cref="RenderPart.Shape"/>/<see cref="RenderPart.LocalOffset"/> as the only
    /// public read of the panel's geometry (code review, TASK-160): publishing the construction would
    /// freeze it as API that a future 2-part or textured cue could not change.</para></summary>
    private static Vector3 ChestFrontPanelHalfExtents(Vector3 chestHalfExtents) =>
        new(chestHalfExtents.X * 0.72f, chestHalfExtents.Y * 0.70f, ChestFrontPanelHalfThickness);

    /// <summary>TASK-160: where that panel sits in the CHEST's local frame. <c>+Z</c> is the anterior
    /// direction of every part in this file — <see cref="Build"/> places each foot's centre at
    /// <c>+footHalf.Z</c> of its ankle, and Triage's own supine assertion measures the same convention
    /// (<c>Dot(Transform(UnitZ, chestOrientation), UnitY) &gt; 0</c> means face-UP). The panel straddles
    /// the chest's front face so its inner half is buried in the chest (no coplanar faces to z-fight) and
    /// its outer half protrudes.</summary>
    private static Vector3 ChestFrontPanelLocalOffset(Vector3 chestHalfExtents) =>
        new(0f, 0f, chestHalfExtents.Z + ChestFrontPanelHalfThickness * 0.6f);

    /// <summary>Half-thickness of the anterior panel slab (metres).</summary>
    private const float ChestFrontPanelHalfThickness = 0.025f;

    /// <summary>TASK-160: the anterior panel's colour, derived from whatever colour the torso itself is
    /// drawn in rather than being a fixed literal — a caller that recolours the torso per-character
    /// (Triage's per-patient jacket) keeps its identity and still gets a legible front/back split.
    /// A darkened shade, not a hue shift, so it reads as a garment panel in gameplay rather than as a
    /// debug gizmo (this ships; it is not test-only).</summary>
    public static Vector4 AnteriorPanelColor(Vector4 torsoColor) =>
        new(torsoColor.X * 0.55f, torsoColor.Y * 0.55f, torsoColor.Z * 0.55f, torsoColor.W);

    /// <summary>Half-extents for every part — returned by <see cref="Build"/> so a caller can
    /// pass the exact same dimensions to <see cref="DescribeForRender"/> without duplicating them.</summary>
    public readonly record struct HalfExtents(
        Vector3 Pelvis, Vector3 Chest, Vector3 Head, Vector3 UpperArm, Vector3 Forearm,
        Vector3 Thigh, Vector3 Shin, Vector3 Foot)
    {
        /// <summary>The dimensions this type has always built — kept as the default so a caller that
        /// passes no <c>halfExtents</c> to <see cref="Build"/>/<see cref="BuildDetachable"/> gets a
        /// bit-identical body (every golden/settle baseline in the repo depends on that).
        /// <para>Half-extents convention: X=sideways, Y=vertical, Z=forward/back — except the arms
        /// (length along X, a T-pose) and the feet (length along Z, pointing forward). A caller
        /// re-proportioning a body should respect that, because <see cref="BuildCore"/>'s joint
        /// anchors are derived from these along those same axes.</para></summary>
        public static HalfExtents Default { get; } = new(
            Pelvis: new Vector3(0.14f, 0.09f, 0.10f),
            Chest: new Vector3(0.15f, 0.18f, 0.10f),
            Head: new Vector3(0.09f, 0.12f, 0.09f),
            UpperArm: new Vector3(0.13f, 0.055f, 0.055f),
            Forearm: new Vector3(0.12f, 0.045f, 0.045f),
            Thigh: new Vector3(0.08f, 0.20f, 0.09f),
            Shin: new Vector3(0.06f, 0.19f, 0.07f),
            Foot: new Vector3(0.06f, 0.045f, 0.13f));

        /// <summary>Throws if any component of any part is not a positive, finite length — a zero or
        /// negative extent produces a degenerate box whose inertia tensor is not invertible, which
        /// Bepu surfaces far from the call site that caused it.</summary>
        public void Validate()
        {
            foreach (var (name, extent) in new[]
            {
                (nameof(Pelvis), Pelvis), (nameof(Chest), Chest), (nameof(Head), Head),
                (nameof(UpperArm), UpperArm), (nameof(Forearm), Forearm),
                (nameof(Thigh), Thigh), (nameof(Shin), Shin), (nameof(Foot), Foot),
            })
            {
                if (!(extent.X > 0f) || !(extent.Y > 0f) || !(extent.Z > 0f)
                    || !float.IsFinite(extent.X) || !float.IsFinite(extent.Y) || !float.IsFinite(extent.Z))
                {
                    throw new ArgumentOutOfRangeException(nameof(HalfExtents), extent,
                        $"{name} half-extents must be positive and finite on every axis.");
                }
            }
        }
    }

    /// <summary>The thigh's effective hip-pivot-to-knee-pivot length (metres) — twice the box
    /// half-extent minus the deliberate joint-anchor <see cref="Overlap"/> <see cref="Build"/>
    /// itself places the knee hinge at, not the raw half-extent. Exposed so task-space leg IK
    /// (a caller solving "where should the foot go" rather than "what joint angle") can stay in
    /// sync with <see cref="Build"/>'s own joint placement instead of re-deriving the constant.</summary>
    public static float ThighPivotLength(HalfExtents halfExtents) => 2f * halfExtents.Thigh.Y - Overlap;

    /// <summary>The shin's effective knee-pivot-to-ankle-pivot length (metres). See
    /// <see cref="ThighPivotLength"/>.</summary>
    public static float ShinPivotLength(HalfExtents halfExtents) => 2f * halfExtents.Shin.Y - Overlap;

    /// <summary>How far sideways of the pelvis centerline the hip ball-socket's thigh-side
    /// anchor sits (metres) — <see cref="Build"/>'s own <c>hipX</c> joint placement constant,
    /// unsigned. A caller solving task-space leg IK (e.g. <c>Aigis.Evolve.LegIk</c>) needs this
    /// signed per leg (negative for the left leg, positive for the right — <see cref="Build"/>'s
    /// own <c>sideSign</c> convention) as its <c>hipOffsetX</c> parameter, since the true
    /// hip-pivot-to-knee-pivot direction is tilted sideways by this amount, not exactly the
    /// thigh's own -Y axis.</summary>
    public static float HipPivotOffsetX(HalfExtents halfExtents) => halfExtents.Pelvis.X * 0.65f;

    /// <summary>Builds a T-posed humanoid ragdoll with its pelvis center at <paramref name="pelvisPosition"/>.</summary>
    public static (Parts Parts, HalfExtents HalfExtents) Build(PhysicsWorld world, Vector3 pelvisPosition, string namePrefix = "", MassDistribution? masses = null, HalfExtents? halfExtents = null, float jointMotorScale = 1f, bool selfCollision = false)
    {
        var (parts, builtHalfExtents, _) = BuildCore(world, pelvisPosition, namePrefix, masses ?? MassDistribution.Default, halfExtents ?? HalfExtents.Default, jointMotorScale, selfCollision);
        return (parts, builtHalfExtents);
    }

    /// <summary>Same as <see cref="Build"/>, plus the 12 anatomical <see cref="JointGroup"/>s a
    /// caller needs for limb detachment (TASK-055, docs/plans/triage-full-port.md §4) --
    /// <see cref="Build"/>'s own signature is unchanged (dozens of existing callers deconstruct
    /// its 2-tuple positionally) so this is a separate opt-in method rather than a breaking change,
    /// exactly the same "don't disturb Build itself" posture <see cref="AddLegServos"/> already
    /// established for active leg control.</summary>
    /// <remarks><c>overlap</c> is how far each joint's child box reaches back past the joint into its parent (metres), and
    /// <c>footForwardOffset</c> is subtracted from the foot's half-length to place its centre forward of the ankle. The defaults
    /// (<see cref="Overlap"/>, <see cref="FootForwardOffset"/>) are sized for a ~1.7 m body; a much smaller body should scale both
    /// with its size, or every child box visibly juts out behind its joint (a 3 cm overlap on a 60 cm figure's 12 cm forearm sticks a
    /// quarter of it out behind the elbow). <see cref="JointAnchors"/> and the pose helpers assume the defaults.</remarks>
    public static (Parts Parts, HalfExtents HalfExtents, IReadOnlyList<JointGroup> Joints) BuildDetachable(PhysicsWorld world, Vector3 pelvisPosition, string namePrefix = "", MassDistribution? masses = null, HalfExtents? halfExtents = null, float jointMotorScale = 1f, bool selfCollision = false,
        float overlap = Overlap, float footForwardOffset = FootForwardOffset)
        => BuildCore(world, pelvisPosition, namePrefix, masses ?? MassDistribution.Default, halfExtents ?? HalfExtents.Default, jointMotorScale, selfCollision, overlap, footForwardOffset);

    private static (Parts Parts, HalfExtents HalfExtents, IReadOnlyList<JointGroup> Joints) BuildCore(PhysicsWorld world, Vector3 pelvisPosition, string namePrefix, MassDistribution masses, HalfExtents dimensions, float jointMotorScale, bool selfCollision,
        float overlap = Overlap, float footForwardOffset = FootForwardOffset)
    {
        ArgumentNullException.ThrowIfNull(world);
        if (!float.IsFinite(overlap) || overlap < 0f)
        {
            throw new ArgumentOutOfRangeException(nameof(overlap), overlap, "Joint overlap must be finite and non-negative.");
        }

        if (!float.IsFinite(footForwardOffset))
        {
            throw new ArgumentOutOfRangeException(nameof(footForwardOffset), footForwardOffset, "The foot offset must be finite.");
        }
        string Name(string part) => string.IsNullOrEmpty(namePrefix) ? part : $"{namePrefix}.{part}";
        var names = new[]
        {
            Name("pelvis"), Name("chest"), Name("head"),
            Name("l_upperarm"), Name("r_upperarm"), Name("l_forearm"), Name("r_forearm"),
            Name("l_thigh"), Name("r_thigh"), Name("l_shin"), Name("r_shin"), Name("l_foot"), Name("r_foot"),
        };
        // Validate BEFORE the first mutation, not just before first use: NewCollisionGroup() below
        // increments a counter, so throwing after it would consume a group id for a ragdoll that was
        // never built (code review). EnsureNamesAvailable above is a read-only check, so it is safe
        // either side of this.
        masses.Validate();
        dimensions.Validate();
        world.EnsureNamesAvailable(names);
        var group = world.NewCollisionGroup();

        // Half-extents: see HalfExtents.Default for the axis convention every derivation below
        // assumes (arms run along X, feet along Z). Caller-supplied since a game may want its own
        // proportions (Game.Triage's patient has longer arms and a larger head).
        var pelvisHalf = dimensions.Pelvis;
        var chestHalf = dimensions.Chest;
        var headHalf = dimensions.Head;
        var upperArmHalf = dimensions.UpperArm;
        var forearmHalf = dimensions.Forearm;
        var thighHalf = dimensions.Thigh;
        var shinHalf = dimensions.Shin;
        var footHalf = dimensions.Foot;

        var pelvisPos = pelvisPosition;
        var chestPos = pelvisPos + new Vector3(0f, pelvisHalf.Y + chestHalf.Y - overlap, 0f);
        var headPos = chestPos + new Vector3(0f, chestHalf.Y + headHalf.Y - overlap, 0f);
        var shoulderY = chestPos.Y + chestHalf.Y * 0.5f;
        var hipX = pelvisHalf.X * 0.65f;

        // BUGFIX (TASK-048, found by transform-auditor-style investigation): X and Z here used
        // to be plain offsets from the origin (`sideSign * (...)`, Z hardcoded to 0f) instead of
        // offsets from pelvisPos.X/pelvisPos.Z -- only Y was ever actually relative to
        // pelvisPosition. A caller building AT a non-origin X/Z did exist before this task
        // (HumanoidRagdollTests.Build_TwoInstances_DoNotShareNamesOrIds builds at X=-2/+2), but
        // it only ever asserted name/id uniqueness, never position -- so the bug was latent
        // rather than genuinely untriggered, and no test caught it. TriageGame (TASK-048) is the
        // first caller to both build off-origin AND check position, which is what surfaced it --
        // every limb (arms/legs) was being placed relative to WORLD (0,0,0) instead of the
        // requested pelvisPosition, while the torso (pelvis/chest/head, whose own math already
        // threaded pelvisPos through) stayed put; the ragdoll's joints then had to violently
        // snap the two groups together across tens of metres the first time the world stepped,
        // scattering the limbs across the map instead of settling into a coherent pile.
        void ArmChain(float sideSign, out Vector3 upperArmPos, out Vector3 forearmPos)
        {
            upperArmPos = new Vector3(pelvisPos.X + sideSign * (chestHalf.X + upperArmHalf.X - overlap), shoulderY, pelvisPos.Z);
            forearmPos = upperArmPos + new Vector3(sideSign * (upperArmHalf.X + forearmHalf.X - overlap), 0f, 0f);
        }

        void LegChain(float sideSign, out Vector3 thighPos, out Vector3 shinPos, out Vector3 footPos)
        {
            thighPos = new Vector3(pelvisPos.X + sideSign * hipX, pelvisPos.Y - pelvisHalf.Y - thighHalf.Y + overlap, pelvisPos.Z);
            shinPos = thighPos + new Vector3(0f, -(thighHalf.Y + shinHalf.Y - overlap), 0f);
            footPos = shinPos + new Vector3(0f, -(shinHalf.Y + footHalf.Y - overlap), footHalf.Z - footForwardOffset);
        }

        ArmChain(-1f, out var lUpperArmPos, out var lForearmPos);
        ArmChain(1f, out var rUpperArmPos, out var rForearmPos);
        LegChain(-1f, out var lThighPos, out var lShinPos, out var lFootPos);
        LegChain(1f, out var rThighPos, out var rShinPos, out var rFootPos);

        var pelvis = world.AddDynamicBox(pelvisPos, pelvisHalf, masses.Pelvis, group, 0);
        var chest = world.AddDynamicBox(chestPos, chestHalf, masses.Chest, group, 1);
        var head = world.AddDynamicBox(headPos, headHalf, masses.Head, group, 2);
        var lUpperArm = world.AddDynamicBox(lUpperArmPos, upperArmHalf, masses.UpperArm, group, 3);
        var rUpperArm = world.AddDynamicBox(rUpperArmPos, upperArmHalf, masses.UpperArm, group, 4);
        var lForearm = world.AddDynamicBox(lForearmPos, forearmHalf, masses.Forearm, group, 5);
        var rForearm = world.AddDynamicBox(rForearmPos, forearmHalf, masses.Forearm, group, 6);
        var lThigh = world.AddDynamicBox(lThighPos, thighHalf, masses.Thigh, group, 7);
        var rThigh = world.AddDynamicBox(rThighPos, thighHalf, masses.Thigh, group, 8);
        var lShin = world.AddDynamicBox(lShinPos, shinHalf, masses.Shin, group, 9);
        var rShin = world.AddDynamicBox(rShinPos, shinHalf, masses.Shin, group, 10);
        var lFoot = world.AddDynamicBox(lFootPos, footHalf, masses.Foot, group, 11);
        var rFoot = world.AddDynamicBox(rFootPos, footHalf, masses.Foot, group, 12);

        world.SetName(pelvis, Name("pelvis"));
        world.SetName(chest, Name("chest"));
        world.SetName(head, Name("head"));
        world.SetName(lUpperArm, Name("l_upperarm"));
        world.SetName(rUpperArm, Name("r_upperarm"));
        world.SetName(lForearm, Name("l_forearm"));
        world.SetName(rForearm, Name("r_forearm"));
        world.SetName(lThigh, Name("l_thigh"));
        world.SetName(rThigh, Name("r_thigh"));
        world.SetName(lShin, Name("l_shin"));
        world.SetName(rShin, Name("r_shin"));
        world.SetName(lFoot, Name("l_foot"));
        world.SetName(rFoot, Name("r_foot"));
        world.SetUprightAxis(pelvis, head);

        var deg = MathF.PI / 180f;

        // Spine (pelvis-chest) and neck (chest-head): ball-and-socket, twist/swing around Y.
        // Both are IsSpineOrNeck=true (TASK-055: the design doc's "×5 for Head and Pelvis joints").
        // The CHEST is the tree root (see DownstreamParts), so the spine's parent/child are recorded
        // reversed relative to the constraint's own bodyA/bodyB: the constraint is still built
        // pelvis-first (its anchors and every existing settle baseline depend on that ordering), but
        // for detachment purposes the pelvis is the CHILD hanging off the chest. Severing the spine
        // therefore drops the pelvis and both legs and leaves the chest/head/arms intact, which is
        // the anatomically sensible direction and what a caller marking "the torso is the body" wants.
        var spineConstraints = BallJoint(world, RagdollJoint.Spine, pelvis, chest, pelvisPos + new Vector3(0f, pelvisHalf.Y, 0f), pelvisPos, chestPos, Vector3.UnitY, YAxisTwistBasis, 30f * deg, -30f * deg, 30f * deg, isSpineOrNeck: true, jointMotorScale);
        var spine = spineConstraints with { ParentPart = chest, ChildPart = pelvis };
        var neck = BallJoint(world, RagdollJoint.Neck, chest, head, chestPos + new Vector3(0f, chestHalf.Y, 0f), chestPos, headPos, Vector3.UnitY, YAxisTwistBasis, 45f * deg, -70f * deg, 70f * deg, isSpineOrNeck: true, jointMotorScale);

        // Shoulders (chest-upperarm): ball-and-socket, twist/swing around X (the arm's own length axis).
        var lShoulder = BallJoint(world, RagdollJoint.LeftShoulder, chest, lUpperArm, lUpperArmPos - new Vector3(-1f * (upperArmHalf.X - overlap), 0f, 0f), chestPos, lUpperArmPos, Vector3.UnitX, XAxisTwistBasis, ShoulderSwingLimitDegrees * deg, -ShoulderTwistLimitDegrees * deg, ShoulderTwistLimitDegrees * deg, isSpineOrNeck: false, jointMotorScale);
        var rShoulder = BallJoint(world, RagdollJoint.RightShoulder, chest, rUpperArm, rUpperArmPos - new Vector3(1f * (upperArmHalf.X - overlap), 0f, 0f), chestPos, rUpperArmPos, Vector3.UnitX, XAxisTwistBasis, ShoulderSwingLimitDegrees * deg, -ShoulderTwistLimitDegrees * deg, ShoulderTwistLimitDegrees * deg, isSpineOrNeck: false, jointMotorScale);

        // Elbows (upperarm-forearm): hinge, bend axis Y (curls the forearm forward), 0..~145deg.
        // Right uses the mirrored basis (see YAxisTwistBasisMirrored) so a shared positive servo
        // target curls both forearms toward the same world direction despite the mirrored rest.
        var lElbow = HingeJoint(world, RagdollJoint.LeftElbow, lUpperArm, lForearm, lForearmPos - new Vector3(-1f * (forearmHalf.X - overlap), 0f, 0f), lUpperArmPos, lForearmPos, Vector3.UnitY, YAxisTwistBasis, -10f * deg, 145f * deg, jointMotorScale);
        var rElbow = HingeJoint(world, RagdollJoint.RightElbow, rUpperArm, rForearm, rForearmPos - new Vector3(1f * (forearmHalf.X - overlap), 0f, 0f), rUpperArmPos, rForearmPos, Vector3.UnitY, YAxisTwistBasisMirrored, -10f * deg, 145f * deg, jointMotorScale);

        // Hips (pelvis-thigh): ball-and-socket, twist/swing around Y (the leg's own length axis).
        var lHip = BallJoint(world, RagdollJoint.LeftHip, pelvis, lThigh, pelvisPos + new Vector3(0f, -pelvisHalf.Y, 0f), pelvisPos, lThighPos, Vector3.UnitY, YAxisTwistBasis, 90f * deg, -40f * deg, 40f * deg, isSpineOrNeck: false, jointMotorScale);
        var rHip = BallJoint(world, RagdollJoint.RightHip, pelvis, rThigh, pelvisPos + new Vector3(0f, -pelvisHalf.Y, 0f), pelvisPos, rThighPos, Vector3.UnitY, YAxisTwistBasis, 90f * deg, -40f * deg, 40f * deg, isSpineOrNeck: false, jointMotorScale);

        // Knees (thigh-shin): hinge, bend axis X (swings the shin backward), 0..~140deg.
        var lKnee = HingeJoint(world, RagdollJoint.LeftKnee, lThigh, lShin, lShinPos + new Vector3(0f, shinHalf.Y - overlap, 0f), lThighPos, lShinPos, Vector3.UnitX, XAxisTwistBasis, -10f * deg, 140f * deg, jointMotorScale);
        var rKnee = HingeJoint(world, RagdollJoint.RightKnee, rThigh, rShin, rShinPos + new Vector3(0f, shinHalf.Y - overlap, 0f), rThighPos, rShinPos, Vector3.UnitX, XAxisTwistBasis, -10f * deg, 140f * deg, jointMotorScale);

        // Ankles (shin-foot): passive 2-DOF swing (dorsi/plantar-flexion + inversion/eversion)
        // around the shin's long axis, with yaw/twist bounded separately. The AngularMotor is
        // damping only; the existing ankle TwistServo, added by AddLegServos, remains the sole
        // active pitch actuator and does not control the newly-free roll direction.
        var lAnkle = AnkleJoint(world, RagdollJoint.LeftAnkle, lShin, lFoot, lFootPos + new Vector3(0f, footHalf.Y - overlap, -(footHalf.Z - footForwardOffset)), lShinPos, lFootPos, deg, jointMotorScale);
        var rAnkle = AnkleJoint(world, RagdollJoint.RightAnkle, rShin, rFoot, rFootPos + new Vector3(0f, footHalf.Y - overlap, -(footHalf.Z - footForwardOffset)), rShinPos, rFootPos, deg, jointMotorScale);

        var parts = new Parts(pelvis, chest, head, lUpperArm, rUpperArm, lForearm, rForearm, lThigh, rThigh, lShin, rShin, lFoot, rFoot, group);
        var halfExtents = new HalfExtents(pelvisHalf, chestHalf, headHalf, upperArmHalf, forearmHalf, thighHalf, shinHalf, footHalf);
        IReadOnlyList<JointGroup> joints = [spine, neck, lShoulder, rShoulder, lElbow, rElbow, lHip, rHip, lKnee, rKnee, lAnkle, rAnkle];
        ConfigureSelfCollision(world, parts, joints, selfCollision);
        return (parts, halfExtents, joints);
    }

    /// <summary>Active "muscles" for the 6 leg joints a walking controller drives (hip x2,
    /// knee x2, ankle x2), each a live constraint id a controller re-targets every step via
    /// <see cref="PhysicsWorld.SetAngularServoTarget"/>/<see cref="PhysicsWorld.SetTwistServoTarget"/>.</summary>
    public readonly record struct LegServos(int LeftHip, int RightHip, int LeftKnee, int RightKnee, int LeftAnkle, int RightAnkle);

    /// <summary>Adds the 6 leg servos on top of the passive joints <see cref="Build"/> already
    /// created (those remain in place as anatomical safety bounds/dampers — this only adds
    /// active drive alongside them). A separate, opt-in method — NOT called by
    /// <see cref="Build"/> itself — so every existing consumer (e.g. the passive-flop ragdoll
    /// in games/Game.RagdollPlayground) is entirely unaffected; only a caller that explicitly
    /// wants an actively-controllable ragdoll (a walking-gait trainer) calls this too.
    /// <para>
    /// Hips are simplified to a single sagittal (forward/back) flexion angle via
    /// <see cref="PhysicsWorld.AddAngularServo"/>, targeting a pure rotation about world X —
    /// the same sagittal axis the knee and active ankle pitch already bend around — not full 3-DOF hip
    /// control; true lateral/frontal-plane balance (hip abduction) is a later extension.
    /// Knees and the ankle's pitch are driven via <see cref="PhysicsWorld.AddTwistServo"/>.
    /// The ankle's newly-free roll/swing direction is deliberately passive.
    /// </para></summary>
    public static LegServos AddLegServos(
        PhysicsWorld world,
        Parts parts,
        float frequency = 15f,
        float anklePitchMaxForce = 100f,
        float kneeMaxForce = 100f)
    {
        ArgumentNullException.ThrowIfNull(world);
        if (frequency <= 0f || !float.IsFinite(frequency))
        {
            throw new ArgumentOutOfRangeException(nameof(frequency), "Servo frequency must be finite and positive.");
        }

        if (anklePitchMaxForce <= 0f || !float.IsFinite(anklePitchMaxForce))
        {
            throw new ArgumentOutOfRangeException(nameof(anklePitchMaxForce), "Ankle pitch maximum force must be finite and positive.");
        }
        if (kneeMaxForce <= 0f || !float.IsFinite(kneeMaxForce))
        {
            throw new ArgumentOutOfRangeException(nameof(kneeMaxForce), "Knee maximum force must be finite and positive.");
        }
        // frequency is the servo SpringSettings stiffness (Hz): how fast it tracks its target. The
        // 15Hz default is stiff -- it follows setpoint jitter faithfully (the ServoBench jit_gain),
        // which the balance track can lower to soften the servos without touching maxForce (holding
        // strength). Walk callers keep 15Hz via the default.
        var leftHip = world.AddAngularServo(parts.Pelvis, parts.LeftThigh, Quaternion.Identity, frequency: frequency, maxForce: 120f);
        var rightHip = world.AddAngularServo(parts.Pelvis, parts.RightThigh, Quaternion.Identity, frequency: frequency, maxForce: 120f);
        var leftKnee = world.AddTwistServo(parts.LeftThigh, parts.LeftShin, XAxisTwistBasis, XAxisTwistBasis, 0f, frequency: frequency, maxForce: kneeMaxForce);
        var rightKnee = world.AddTwistServo(parts.RightThigh, parts.RightShin, XAxisTwistBasis, XAxisTwistBasis, 0f, frequency: frequency, maxForce: kneeMaxForce);
        // The ankle roll axis remains passive (AnkleJoint's spring/damper), but pitch must hold the
        // foot under the support polygon during hip-only balance. 60 N·m let the heel collapse
        // long before the 100 N·m knee reference servo saturated, so match that holding budget.
        var leftAnkle = world.AddTwistServo(parts.LeftShin, parts.LeftFoot, XAxisTwistBasis, XAxisTwistBasis, 0f, frequency: frequency, maxForce: anklePitchMaxForce);
        var rightAnkle = world.AddTwistServo(parts.RightShin, parts.RightFoot, XAxisTwistBasis, XAxisTwistBasis, 0f, frequency: frequency, maxForce: anklePitchMaxForce);
        return new LegServos(leftHip, rightHip, leftKnee, rightKnee, leftAnkle, rightAnkle);
    }

    /// <summary>Sets up which of a ragdoll's own 78 part pairs may touch each other.
    ///
    /// <para><b>Default (<paramref name="selfCollision"/> false): none of them.</b> That is the
    /// long-standing simplification most ragdolls make and every existing caller depends on — see the
    /// note below for the real reason it is not merely laziness.</para>
    ///
    /// <para><b>Enabled: every pair EXCEPT the ones that share a joint.</b> Two parts joined by a
    /// shoulder or a knee are built deliberately overlapping — <see cref="Overlap"/> exists so a joint
    /// anchor is not spanning an empty gap — so letting a jointed pair collide sets the contact solver
    /// and the joint solver fighting over the same two bodies every step, at a penetration that never
    /// resolves because the joint is what holds it there. That is the failure the blanket rule was
    /// avoiding, and adjacency is the precise statement of it: NON-adjacent parts are never built
    /// overlapping, so they can collide freely.</para>
    ///
    /// <para><b>Adjacency is read off <paramref name="joints"/>, not hardcoded.</b> The joint list is
    /// already the authoritative statement of which parts are connected to which
    /// (<see cref="JointGroup.ParentPart"/>/<see cref="JointGroup.ChildPart"/>), so a future added part
    /// or re-rooted tree updates this automatically instead of silently leaving a stale pair enabled.</para>
    ///
    /// <para><b>What this buys, and the one thing to watch.</b> Limbs stop passing through the torso
    /// and through each other, which is what makes a body read as a body rather than as a bag of
    /// independent sticks. The thing to watch is any pose a caller APPLIES (rather than lets the solver
    /// find) that puts two non-adjacent parts inside each other: with collision on, the solver will
    /// push them apart on the first step. Triage's own prone spawn pose was measured against this.</para></summary>
    private static void ConfigureSelfCollision(PhysicsWorld world, Parts parts, IReadOnlyList<JointGroup> joints, bool selfCollision)
    {
        var all = parts.All;
        for (var i = 0; i < all.Length; i++)
        {
            for (var j = i + 1; j < all.Length; j++)
            {
                if (!selfCollision || SharesAJoint(joints, all[i], all[j]))
                {
                    world.DisableSelfCollision(all[i], all[j]);
                }
            }
        }
    }

    /// <summary>True when these two parts are the two ends of one anatomical joint — i.e. they are
    /// built overlapping and must never be handed to the contact solver. See
    /// <see cref="ConfigureSelfCollision"/>.</summary>
    private static bool SharesAJoint(IReadOnlyList<JointGroup> joints, int a, int b)
    {
        foreach (var joint in joints)
        {
            if ((joint.ParentPart == a && joint.ChildPart == b) || (joint.ParentPart == b && joint.ChildPart == a))
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>Ball-and-socket with a swing cone and twist range — shoulders/hips/spine/neck.
    /// Returns the <see cref="JointGroup"/> (TASK-055) so <see cref="BuildCore"/> can hand it to a
    /// caller wanting limb detachment; <paramref name="bodyA"/> is always the parent (closer to
    /// <see cref="Parts.Pelvis"/>), <paramref name="bodyB"/> the child, matching every existing
    /// call site's own convention.</summary>
    private static JointGroup BallJoint(PhysicsWorld world, RagdollJoint joint, int bodyA, int bodyB, Vector3 anchorWorld, Vector3 posA, Vector3 posB,
        Vector3 axis, Quaternion twistBasis, float maxSwingRadians, float minTwistRadians, float maxTwistRadians, bool isSpineOrNeck, float motorScale)
    {
        var ballSocket = world.AddBallSocket(bodyA, bodyB, anchorWorld - posA, anchorWorld - posB);
        var swing = world.AddSwingLimit(bodyA, bodyB, axis, axis, maxSwingRadians);
        var twist = world.AddTwistLimit(bodyA, bodyB, twistBasis, twistBasis, minTwistRadians, maxTwistRadians);
        var motor = world.AddAngularMotor(bodyA, bodyB, maximumForce: DefaultJointMotorForce * motorScale);
        return new JointGroup(joint, bodyA, bodyB, ballSocket, swing, [ballSocket, swing, twist, motor], isSpineOrNeck);
    }

    /// <summary>1-DOF hinge with a bend-angle limit — elbows/knees. Never spine/neck (only
    /// <see cref="BallJoint"/> joints can be), so <see cref="JointGroup.IsSpineOrNeck"/> is always
    /// false here.</summary>
    private static JointGroup HingeJoint(PhysicsWorld world, RagdollJoint joint, int bodyA, int bodyB, Vector3 anchorWorld, Vector3 posA, Vector3 posB,
        Vector3 hingeAxis, Quaternion twistBasis, float minBendRadians, float maxBendRadians, float motorScale)
    {
        var hinge = world.AddHinge(bodyA, bodyB, anchorWorld - posA, anchorWorld - posB, hingeAxis, hingeAxis);
        var twist = world.AddTwistLimit(bodyA, bodyB, twistBasis, twistBasis, minBendRadians, maxBendRadians);
        var motor = world.AddAngularMotor(bodyA, bodyB, maximumForce: DefaultJointMotorForce * motorScale);
        return new JointGroup(joint, bodyA, bodyB, hinge, twist, [hinge, twist, motor], false);
    }

    private static JointGroup AnkleJoint(PhysicsWorld world, RagdollJoint joint, int bodyA, int bodyB, Vector3 anchorWorld, Vector3 posA, Vector3 posB, float radians, float motorScale)
    {
        var ball = world.AddBallSocket(bodyA, bodyB, anchorWorld - posA, anchorWorld - posB, frequency: 18f, dampingRatio: 1f);
        var swing = world.AddSwingLimit(bodyA, bodyB, Vector3.UnitY, Vector3.UnitY, AnkleSwingLimitDegrees * radians, frequency: 8f, dampingRatio: 1f);
        var twist = world.AddTwistLimit(bodyA, bodyB, YAxisTwistBasis, YAxisTwistBasis, -12f * radians, 12f * radians, frequency: 12f, dampingRatio: 1f);
        var motor = world.AddAngularMotor(bodyA, bodyB, maximumForce: AnkleJointMotorForce * motorScale, softness: 0.02f);
        return new JointGroup(joint, bodyA, bodyB, ball, swing, [ball, swing, twist, motor], false);
    }
}
