using System.Numerics;

namespace Aigis.Physics;

/// <summary>
/// Re-creating ONE anatomical joint on a ragdoll that has already been built — the inverse of the
/// sever a game performs by removing a <see cref="HumanoidRagdoll.JointGroup"/>'s constraints.
///
/// <para><b>Why this belongs in the engine rather than in the game that wants it.</b>
/// <see cref="HumanoidRagdoll.BuildCore"/> derives every joint's anchor from the body proportions
/// along the axis convention <see cref="HumanoidRagdoll.HalfExtents.Default"/> documents (arms along
/// X, feet along Z), and it does so in WORLD space using the rest positions it just computed. A game
/// re-attaching a limb has no rest positions to subtract — the host body is lying in an arbitrary
/// pose — so it would have to re-derive the same anchors by hand in local space. That is precisely
/// the "silently convert a convention in a second place" failure hard rule 4 exists to prevent, and
/// getting it wrong produces a limb welded on at a plausible-looking but wrong offset, which reads as
/// a physics bug rather than as a maths error.</para>
///
/// <para><b>BuildCore is deliberately NOT refactored to call this.</b> Every settle baseline, golden
/// and joint-force bound in the repo depends on the exact float arithmetic of its world-space anchor
/// expressions, and routing them through a local-space form would perturb them all by ~1e-7 for no
/// gain. Instead <see cref="JointAnchors"/> states the same anchors analytically in local space and
/// <c>HumanoidRagdollJointAnchorTests</c> pins the two forms against each other for all 12 joints, so
/// the duplication cannot drift unnoticed.</para>
///
/// <para><b>Half-extents are taken PER BODY, not from the assembled <see cref="HumanoidRagdoll.HalfExtents"/>.</b>
/// The caller that motivated this is a graft — a part attached where a different part used to be (a
/// foot on a shoulder). Its box is not the box the recipe would have expected there, and the anchor
/// still has to land on the two boxes' actual touching faces, so the formulas below are written in
/// terms of the two bodies' own half-extents and nothing else.</para>
/// </summary>
public static partial class HumanoidRagdoll
{
    /// <summary>
    /// Where <paramref name="joint"/>'s anchor sits in each of its two bodies' own local frames, given
    /// the two boxes' half-extents.
    /// <para>The returned offsets are in TREE terms — <c>ParentLocal</c> belongs to the
    /// body closer to the chest — which is not always the constraint's <c>bodyA</c>: see
    /// <see cref="ChildIsConstraintBodyA"/>.</para>
    /// </summary>
    /// <param name="joint">Which anatomical joint.</param>
    /// <param name="parentHalfExtents">Half-extents of the body on the CHEST side of the joint
    /// (chest for a shoulder, upper arm for an elbow, shin for an ankle, …).</param>
    /// <param name="childHalfExtents">Half-extents of the body on the far side — the one a graft
    /// replaces.</param>
    public static (Vector3 ParentLocal, Vector3 ChildLocal) JointAnchors(
        RagdollJoint joint, Vector3 parentHalfExtents, Vector3 childHalfExtents)
    {
        // Each expression below is the local-space form of the corresponding `anchorWorld - posA` /
        // `anchorWorld - posB` pair in BuildCore. Derivations, in BuildCore's own terms (O = Overlap):
        //
        //   Spine   anchor = pelvisPos + (0, ph.Y, 0)
        //           chest side  = anchor - chestPos  = (0, O - ch.Y, 0)      [chest is bodyB]
        //           pelvis side = anchor - pelvisPos = (0, ph.Y, 0)          [pelvis is bodyA]
        //   Neck    anchor = chestPos + (0, ch.Y, 0)
        //           chest side = (0, ch.Y, 0);  head side = (0, O - hh.Y, 0)
        //   Shoulder anchor = upperArmPos - (s*(uh.X - O), 0, 0)
        //           chest side = (s*ch.X, ch.Y*0.5, 0);  arm side = (-s*(uh.X - O), 0, 0)
        //   Elbow   anchor = forearmPos - (s*(fh.X - O), 0, 0)
        //           upper-arm side = (s*uh.X, 0, 0);  forearm side = (-s*(fh.X - O), 0, 0)
        //   Hip     anchor = pelvisPos + (0, -ph.Y, 0)
        //           pelvis side = (0, -ph.Y, 0);  thigh side = (-s*hipX, th.Y - O, 0), hipX = ph.X*0.65
        //   Knee    anchor = shinPos + (0, sh.Y - O, 0)
        //           thigh side = (0, -th.Y, 0);  shin side = (0, sh.Y - O, 0)
        //   Ankle   anchor = footPos + (0, fo.Y - O, -(fo.Z - FootForwardOffset))
        //           shin side = (0, -sh.Y, 0);  foot side = (0, fo.Y - O, -(fo.Z - FootForwardOffset))
        var side = SideSign(joint);
        return joint switch
        {
            RagdollJoint.Spine => (
                new Vector3(0f, Overlap - parentHalfExtents.Y, 0f),
                new Vector3(0f, childHalfExtents.Y, 0f)),
            RagdollJoint.Neck => (
                new Vector3(0f, parentHalfExtents.Y, 0f),
                new Vector3(0f, Overlap - childHalfExtents.Y, 0f)),
            RagdollJoint.LeftShoulder or RagdollJoint.RightShoulder => (
                new Vector3(side * parentHalfExtents.X, parentHalfExtents.Y * 0.5f, 0f),
                new Vector3(-side * (childHalfExtents.X - Overlap), 0f, 0f)),
            RagdollJoint.LeftElbow or RagdollJoint.RightElbow => (
                new Vector3(side * parentHalfExtents.X, 0f, 0f),
                new Vector3(-side * (childHalfExtents.X - Overlap), 0f, 0f)),
            RagdollJoint.LeftHip or RagdollJoint.RightHip => (
                new Vector3(0f, -parentHalfExtents.Y, 0f),
                new Vector3(-side * parentHalfExtents.X * HipLateralFraction, childHalfExtents.Y - Overlap, 0f)),
            RagdollJoint.LeftKnee or RagdollJoint.RightKnee => (
                new Vector3(0f, -parentHalfExtents.Y, 0f),
                new Vector3(0f, childHalfExtents.Y - Overlap, 0f)),
            RagdollJoint.LeftAnkle or RagdollJoint.RightAnkle => (
                new Vector3(0f, -parentHalfExtents.Y, 0f),
                new Vector3(0f, childHalfExtents.Y - Overlap, -(childHalfExtents.Z - FootForwardOffset))),
            _ => throw new ArgumentOutOfRangeException(nameof(joint), joint, "Unknown ragdoll joint."),
        };
    }

    /// <summary>How far out from the pelvis centre a hip anchor sits, as a fraction of the pelvis's own
    /// half-width — <see cref="BuildCore"/>'s <c>hipX = pelvisHalf.X * 0.65f</c>, named so
    /// <see cref="JointAnchors"/> and <see cref="BuildCore"/> cannot disagree about it.</summary>
    public const float HipLateralFraction = 0.65f;

    /// <summary>
    /// Whether this joint's TREE CHILD is the constraint's <c>bodyA</c> rather than its <c>bodyB</c>.
    /// True for <see cref="RagdollJoint.Spine"/> alone: the chest is the tree root, so the pelvis is
    /// its child, but the constraint is still built pelvis-first (see <see cref="DownstreamParts"/>'s
    /// own note on why). Every anchor and limit below is expressed against that original ordering, so
    /// a re-attach must honour it or the two anchors swap bodies.
    /// </summary>
    public static bool ChildIsConstraintBodyA(RagdollJoint joint) => joint == RagdollJoint.Spine;

    /// <summary>Which side of the body <paramref name="joint"/> is on: −1 for the left limb chain,
    /// +1 for the right, 0 for the two mid-line joints (<see cref="RagdollJoint.Spine"/>/
    /// <see cref="RagdollJoint.Neck"/>) — matching <see cref="BuildCore"/>'s own
    /// <c>ArmChain</c>/<c>LegChain</c> <c>sideSign</c>.</summary>
    public static float SideSign(RagdollJoint joint) => joint switch
    {
        RagdollJoint.LeftShoulder or RagdollJoint.LeftElbow
            or RagdollJoint.LeftHip or RagdollJoint.LeftKnee or RagdollJoint.LeftAnkle => -1f,
        RagdollJoint.RightShoulder or RagdollJoint.RightElbow
            or RagdollJoint.RightHip or RagdollJoint.RightKnee or RagdollJoint.RightAnkle => 1f,
        _ => 0f,
    };

    /// <summary>
    /// Where a body must be placed for <see cref="AttachJoint"/> to constrain it to
    /// <paramref name="parentPosition"/>/<paramref name="parentOrientation"/> without the joint having
    /// to yank it there.
    ///
    /// <para>Derived, not guessed: the anchor is one point expressed twice, so
    /// <c>parent + R·parentLocal == child + R·childLocal</c> at rest, and every part in
    /// <see cref="BuildCore"/> rests at the SAME orientation as its parent (no part carries a local
    /// rotation), so the child's rest orientation is simply the parent's. A caller that skips this and
    /// drops the graft wherever it was lying gets a constraint spanning metres, which Bepu resolves by
    /// firing both bodies across the level.</para>
    /// </summary>
    public static (Vector3 Position, Quaternion Orientation) JointChildRestPose(
        RagdollJoint joint, Vector3 parentHalfExtents, Vector3 childHalfExtents,
        Vector3 parentPosition, Quaternion parentOrientation)
    {
        var (parentLocal, childLocal) = JointAnchors(joint, parentHalfExtents, childHalfExtents);
        return (parentPosition + Vector3.Transform(parentLocal - childLocal, parentOrientation), parentOrientation);
    }

    /// <summary>
    /// Builds <paramref name="joint"/>'s full constraint group between two bodies that are already in
    /// the world — the same 3–4 constraints, anchors, limits and damping motor
    /// <see cref="BuildDetachable"/> would have created for that joint, so the resulting
    /// <see cref="JointGroup"/> is severable by exactly the same code path as an original one.
    ///
    /// <para><b>Pose the child first</b> (see <see cref="JointChildRestPose"/>) — this method only
    /// creates constraints; it deliberately does not move either body, because a caller may want the
    /// graft to arrive with a specific orientation and because moving bodies is not a joint's job.</para>
    ///
    /// <para><b>Collision filtering is also the caller's job.</b> A grafted body is typically in a
    /// different collision group from its new host (it came off another ragdoll, or was created
    /// standalone), and <see cref="PhysicsWorld.DisableSelfCollision"/> is a silent no-op across
    /// groups — see its own doc comment. Use <see cref="PhysicsWorld.SetPairCollisionEnabled"/> against
    /// each of the host's parts.</para>
    /// </summary>
    /// <param name="world">The world both bodies live in.</param>
    /// <param name="joint">Which anatomical joint to rebuild.</param>
    /// <param name="treeParentBody">The body on the chest side of the joint.</param>
    /// <param name="treeChildBody">The body being attached.</param>
    /// <param name="parentHalfExtents">Half-extents of <paramref name="treeParentBody"/>.</param>
    /// <param name="childHalfExtents">Half-extents of <paramref name="treeChildBody"/>.</param>
    /// <param name="jointMotorScale">Same meaning as <see cref="BuildDetachable"/>'s parameter of the
    /// same name — scales the damping motor's maximum force.</param>
    public static JointGroup AttachJoint(
        PhysicsWorld world,
        RagdollJoint joint,
        int treeParentBody,
        int treeChildBody,
        Vector3 parentHalfExtents,
        Vector3 childHalfExtents,
        float jointMotorScale = 1f)
    {
        ArgumentNullException.ThrowIfNull(world);
        if (!(jointMotorScale > 0f) || !float.IsFinite(jointMotorScale))
        {
            throw new ArgumentOutOfRangeException(nameof(jointMotorScale), jointMotorScale,
                "Joint motor scale must be positive and finite.");
        }

        var (parentLocal, childLocal) = JointAnchors(joint, parentHalfExtents, childHalfExtents);
        var childIsBodyA = ChildIsConstraintBodyA(joint);
        var bodyA = childIsBodyA ? treeChildBody : treeParentBody;
        var bodyB = childIsBodyA ? treeParentBody : treeChildBody;
        var localA = childIsBodyA ? childLocal : parentLocal;
        var localB = childIsBodyA ? parentLocal : childLocal;

        var group = joint switch
        {
            RagdollJoint.Spine => BallGroup(
                world, joint, bodyA, bodyB, localA, localB,
                Vector3.UnitY, YAxisTwistBasis, Radians(30f), Radians(-30f), Radians(30f),
                isSpineOrNeck: true, jointMotorScale),
            RagdollJoint.Neck => BallGroup(
                world, joint, bodyA, bodyB, localA, localB,
                Vector3.UnitY, YAxisTwistBasis, Radians(45f), Radians(-70f), Radians(70f),
                isSpineOrNeck: true, jointMotorScale),
            RagdollJoint.LeftShoulder or RagdollJoint.RightShoulder => BallGroup(
                world, joint, bodyA, bodyB, localA, localB,
                Vector3.UnitX, XAxisTwistBasis, Radians(100f), Radians(-80f), Radians(80f),
                isSpineOrNeck: false, jointMotorScale),
            RagdollJoint.LeftElbow => HingeGroup(
                world, joint, bodyA, bodyB, localA, localB,
                Vector3.UnitY, YAxisTwistBasis, Radians(-10f), Radians(145f), jointMotorScale),
            RagdollJoint.RightElbow => HingeGroup(
                world, joint, bodyA, bodyB, localA, localB,
                Vector3.UnitY, YAxisTwistBasisMirrored, Radians(-10f), Radians(145f), jointMotorScale),
            RagdollJoint.LeftHip or RagdollJoint.RightHip => BallGroup(
                world, joint, bodyA, bodyB, localA, localB,
                Vector3.UnitY, YAxisTwistBasis, Radians(90f), Radians(-40f), Radians(40f),
                isSpineOrNeck: false, jointMotorScale),
            RagdollJoint.LeftKnee or RagdollJoint.RightKnee => HingeGroup(
                world, joint, bodyA, bodyB, localA, localB,
                Vector3.UnitX, XAxisTwistBasis, Radians(-10f), Radians(140f), jointMotorScale),
            RagdollJoint.LeftAnkle or RagdollJoint.RightAnkle => AnkleGroup(
                world, joint, bodyA, bodyB, localA, localB, jointMotorScale),
            _ => throw new ArgumentOutOfRangeException(nameof(joint), joint, "Unknown ragdoll joint."),
        };

        // The tree relationship is what a caller severs by, and for the spine it is the reverse of the
        // constraint ordering above -- exactly the `spineConstraints with { ... }` correction BuildCore
        // applies to its own spine group.
        return group with { ParentPart = treeParentBody, ChildPart = treeChildBody };
    }

    private static float Radians(float degrees) => degrees * (MathF.PI / 180f);

    /// <summary>The local-anchor twin of <see cref="BallJoint"/> — same four constraints, in the same
    /// order, with the anchors already in local space.</summary>
    private static JointGroup BallGroup(
        PhysicsWorld world, RagdollJoint joint, int bodyA, int bodyB, Vector3 localA, Vector3 localB,
        Vector3 axis, Quaternion twistBasis, float maxSwingRadians, float minTwistRadians, float maxTwistRadians,
        bool isSpineOrNeck, float motorScale)
    {
        var ballSocket = world.AddBallSocket(bodyA, bodyB, localA, localB);
        var swing = world.AddSwingLimit(bodyA, bodyB, axis, axis, maxSwingRadians);
        var twist = world.AddTwistLimit(bodyA, bodyB, twistBasis, twistBasis, minTwistRadians, maxTwistRadians);
        var motor = world.AddAngularMotor(bodyA, bodyB, maximumForce: DefaultJointMotorForce * motorScale);
        return new JointGroup(joint, bodyA, bodyB, ballSocket, swing, [ballSocket, swing, twist, motor], isSpineOrNeck);
    }

    /// <summary>The local-anchor twin of <see cref="HingeJoint"/>.</summary>
    private static JointGroup HingeGroup(
        PhysicsWorld world, RagdollJoint joint, int bodyA, int bodyB, Vector3 localA, Vector3 localB,
        Vector3 hingeAxis, Quaternion twistBasis, float minBendRadians, float maxBendRadians, float motorScale)
    {
        var hinge = world.AddHinge(bodyA, bodyB, localA, localB, hingeAxis, hingeAxis);
        var twist = world.AddTwistLimit(bodyA, bodyB, twistBasis, twistBasis, minBendRadians, maxBendRadians);
        var motor = world.AddAngularMotor(bodyA, bodyB, maximumForce: DefaultJointMotorForce * motorScale);
        return new JointGroup(joint, bodyA, bodyB, hinge, twist, [hinge, twist, motor], false);
    }

    /// <summary>The local-anchor twin of <see cref="AnkleJoint"/>, including its own softer
    /// frequencies and its reduced motor force.</summary>
    private static JointGroup AnkleGroup(
        PhysicsWorld world, RagdollJoint joint, int bodyA, int bodyB, Vector3 localA, Vector3 localB, float motorScale)
    {
        var deg = MathF.PI / 180f;
        var ball = world.AddBallSocket(bodyA, bodyB, localA, localB, frequency: 18f, dampingRatio: 1f);
        var swing = world.AddSwingLimit(bodyA, bodyB, Vector3.UnitY, Vector3.UnitY, 40f * deg, frequency: 8f, dampingRatio: 1f);
        var twist = world.AddTwistLimit(bodyA, bodyB, YAxisTwistBasis, YAxisTwistBasis, -12f * deg, 12f * deg, frequency: 12f, dampingRatio: 1f);
        var motor = world.AddAngularMotor(bodyA, bodyB, maximumForce: AnkleJointMotorForce * motorScale, softness: 0.02f);
        return new JointGroup(joint, bodyA, bodyB, ball, swing, [ball, swing, twist, motor], false);
    }
}
