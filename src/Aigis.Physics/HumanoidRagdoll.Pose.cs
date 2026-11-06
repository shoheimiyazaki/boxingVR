using System.Numerics;

namespace Aigis.Physics;

/// <summary>
/// One-shot pose authoring for <see cref="HumanoidRagdoll"/>: compute every part's world
/// position/orientation from a small set of per-joint rotations (forward kinematics), validate
/// those rotations against <see cref="Build"/>'s own real joint limits, and pick a root height
/// that rests the intended contacts on the ground — all before ever calling
/// <see cref="PhysicsWorld.SetDynamicPose"/>. This is the promoted, tested form of a tool
/// developed and validated across ten one-shot poses in <c>sandbox/Aigis.Lab</c> (see that
/// project's README for the pose-by-pose history); every quantitative decision below (the
/// compose order, the joint reference axes, the grounding math) was checked against a real
/// render or a real <see cref="Build"/> constraint at least once before landing here. Two
/// intended consumers: an agent that needs to reason about or verify a skeleton's pose
/// numerically (not just watch a render), and animation/cutscene code that wants to specify a
/// pose by joint angle rather than by hand-placing 13 world transforms.
/// </summary>
public static partial class HumanoidRagdoll
{
    /// <summary>One posed part: world position/orientation, ready for <see cref="PhysicsWorld.SetDynamicPose"/>.</summary>
    public readonly record struct PartPose(Vector3 Position, Quaternion Orientation);

    /// <summary>
    /// A pose recipe: one quaternion per joint, each expressed RELATIVE TO ITS OWN PARENT's
    /// already-rotated frame (intrinsic composition) — e.g. <see cref="LShoulder"/> is "how the
    /// upper arm sits relative to the chest's current orientation", not relative to world
    /// identity. This is what lets a joint use whichever axis reads most naturally for a given
    /// motion (Z to raise an arm overhead, Y to swing it fore/aft or twist the spine) without the
    /// caller having to pre-sum angles by hand. <see cref="Pelvis"/> is the one exception: since
    /// the pelvis IS the kinematic root (nothing is its parent), it is a WORLD orientation
    /// directly. <see cref="GroundedContacts"/> names which <see cref="ContactPoint"/>s this pose
    /// intends to rest on the ground — <see cref="SolveGroundedPelvisY"/> uses it to pick the
    /// pelvis height; leave it empty and set <see cref="RootHeightOverride"/> instead for a pose
    /// that touches nothing (e.g. mid-air).
    /// </summary>
    public sealed record PoseSpec(
        string Name,
        Quaternion Pelvis,
        Quaternion Chest,
        Quaternion Head,
        Quaternion LShoulder, Quaternion LElbow,
        Quaternion RShoulder, Quaternion RElbow,
        Quaternion LHip, Quaternion LKnee, Quaternion LAnkle,
        Quaternion RHip, Quaternion RKnee, Quaternion RAnkle,
        string[] GroundedContacts,
        float? RootHeightOverride = null)
    {
        /// <summary>Upright standing root orientation — the right default for <see cref="Pelvis"/>
        /// on any pose that doesn't need the whole torso to pitch as a rigid unit.</summary>
        public static readonly Quaternion Neutral = Quaternion.Identity;
    }

    /// <summary>
    /// Spherically interpolates every joint quaternion between two poses (each joint's own
    /// rotation, independently — NOT the resulting world orientation, so the interpolation stays
    /// correct regardless of how the parent chain composes) to produce an in-between animation
    /// frame. <see cref="Quaternion.Slerp"/> already takes the shortest arc between each pair, so
    /// a joint animating through a large angle (e.g. cycle 9/10's ~90-degree shoulder swings)
    /// won't visibly take the long way around.
    /// <para>
    /// <paramref name="groundedContacts"/> and <paramref name="rootHeightOverride"/> are NOT
    /// interpolated from the two endpoints — deliberately. If <paramref name="a"/> and
    /// <paramref name="b"/> ground different contacts (a walk cycle's alternating feet) or one is
    /// airborne, there is no single correct answer for what an in-between frame should ground; the
    /// caller (who knows what the animation is actually depicting) must decide, e.g. by lerping
    /// each endpoint's OWN already-solved pelvis height and passing that as
    /// <paramref name="rootHeightOverride"/>, or by re-solving a contact set that's valid at every
    /// frame. Baking a guess in here would silently hide that decision instead of forcing it.
    /// </para>
    /// </summary>
    public static PoseSpec Lerp(PoseSpec a, PoseSpec b, float t, string[] groundedContacts, float? rootHeightOverride = null) => new(
        Name: $"{a.Name}->{b.Name}@{t:0.00}",
        Pelvis: Quaternion.Slerp(a.Pelvis, b.Pelvis, t),
        Chest: Quaternion.Slerp(a.Chest, b.Chest, t),
        Head: Quaternion.Slerp(a.Head, b.Head, t),
        LShoulder: Quaternion.Slerp(a.LShoulder, b.LShoulder, t),
        LElbow: Quaternion.Slerp(a.LElbow, b.LElbow, t),
        RShoulder: Quaternion.Slerp(a.RShoulder, b.RShoulder, t),
        RElbow: Quaternion.Slerp(a.RElbow, b.RElbow, t),
        LHip: Quaternion.Slerp(a.LHip, b.LHip, t),
        LKnee: Quaternion.Slerp(a.LKnee, b.LKnee, t),
        LAnkle: Quaternion.Slerp(a.LAnkle, b.LAnkle, t),
        RHip: Quaternion.Slerp(a.RHip, b.RHip, t),
        RKnee: Quaternion.Slerp(a.RKnee, b.RKnee, t),
        RAnkle: Quaternion.Slerp(a.RAnkle, b.RAnkle, t),
        GroundedContacts: groundedContacts,
        RootHeightOverride: rootHeightOverride);

    /// <summary>
    /// Composes a parent's world orientation with a child's PARENT-RELATIVE orientation, i.e.
    /// "rotate by <paramref name="localRelative"/> using axes as they exist AFTER
    /// <paramref name="parentWorld"/> has already been applied" (intrinsic/body-frame
    /// composition — the standard skeletal-rig rule bone_world = parent_world * bone_local). The
    /// multiplication order below is not a convention choice made here — it is proven correct by
    /// <see cref="VerifyComposeConvention"/>, which every caller of <see cref="Solve"/> is
    /// expected to have run once (e.g. via a unit test) rather than re-deriving it.
    /// </summary>
    public static Quaternion ComposeIntrinsic(Quaternion parentWorld, Quaternion localRelative)
        => parentWorld * localRelative;

    /// <summary>
    /// Proves which multiplication order <see cref="Quaternion"/> actually uses for intrinsic
    /// (parent-then-local) composition, instead of assuming it from memory. Setup: rotate 90
    /// degrees about world X (this carries the ORIGINAL +Y axis onto +Z), then apply a second
    /// rotation of 90 degrees about Y meant to mean "about whatever the local Y axis now is" —
    /// since the first rotation carried local Y onto world +Z, that second rotation is
    /// intrinsically "90 degrees about world Z, applied after the first". Tracking local +X
    /// (untouched by the first rotation, since rotating about X fixes the X axis) through both
    /// steps by hand gives (1,0,0) -&gt; (1,0,0) -&gt; (0,1,0). Whichever of parent*local /
    /// local*parent reproduces that is the correct order.
    /// </summary>
    public static bool VerifyComposeConvention(out string detail)
    {
        var parent = Quaternion.CreateFromAxisAngle(Vector3.UnitX, MathF.PI / 2f);
        var local = Quaternion.CreateFromAxisAngle(Vector3.UnitY, MathF.PI / 2f);
        var probe = Vector3.UnitX;
        var expected = Vector3.UnitY;

        var viaParentTimesLocal = Vector3.Transform(probe, parent * local);
        var viaLocalTimesParent = Vector3.Transform(probe, local * parent);
        var parentTimesLocalMatches = Vector3.Distance(viaParentTimesLocal, expected) < 1e-4f;
        var localTimesParentMatches = Vector3.Distance(viaLocalTimesParent, expected) < 1e-4f;

        detail =
            $"parent*local -> {viaParentTimesLocal:0.000} (matches expected {parentTimesLocalMatches}), " +
            $"local*parent -> {viaLocalTimesParent:0.000} (matches expected {localTimesParentMatches})";

        if (parentTimesLocalMatches == localTimesParentMatches)
        {
            throw new InvalidOperationException(
                "Convention probe is degenerate (both or neither order matched) -- fix the probe, don't trust ComposeIntrinsic.");
        }

        return parentTimesLocalMatches;
    }

    /// <summary>Named points other code (mainly <see cref="SolveGroundedPelvisY"/>) can ground or
    /// inspect. <see cref="PelvisBottom"/> (local -Y) only means "the side facing the
    /// ground" while the pelvis is near-upright — once a pose pitches the root far enough (e.g.
    /// fully prone, 90 degrees) it no longer does, which is why <see cref="PelvisFront"/> (local
    /// +Z, the pelvis's front face) exists: for a body pitched 90 degrees forward, local +Z is
    /// what now points straight down. Pick whichever of the two actually faces the ground for a
    /// given pose's own root tilt — don't assume "Bottom" always means "down".</summary>
    public enum ContactPoint { LFootSole, RFootSole, LKneePivot, RKneePivot, LWristPivot, RWristPivot, PelvisBottom, PelvisFront, HeadTop }

    /// <summary>
    /// Runs the full forward-kinematics chain for one <see cref="PoseSpec"/> and returns every
    /// part's world pose plus every named <see cref="ContactPoint"/>, given a candidate pelvis
    /// position. Every point returned is an AFFINE function of <paramref name="pelvisPos"/> (pure
    /// translation at the root, rotations chosen independently of it) — <see cref="SolveGroundedPelvisY"/>
    /// relies on that to solve for pelvis height algebraically instead of by hand.
    /// </summary>
    public static (IReadOnlyDictionary<string, PartPose> Parts, IReadOnlyDictionary<ContactPoint, Vector3> Contacts)
        Solve(HalfExtents h, Vector3 pelvisPos, PoseSpec spec)
    {
        var pelvisWorld = spec.Pelvis;
        var chestWorld = ComposeIntrinsic(pelvisWorld, spec.Chest);
        var headWorld = ComposeIntrinsic(chestWorld, spec.Head);

        // Two pivots, like every limb chain below: the pelvis-side offset (center to its own top
        // surface) is transformed by pelvisWorld; the chest-side offset (that pivot to the
        // chest's own center) is transformed by chestWorld — collapsing these into one
        // chestWorld-only step only works when pelvisWorld is Identity.
        var spinePivot = pelvisPos + Vector3.Transform(new Vector3(0f, h.Pelvis.Y, 0f), pelvisWorld);
        var chestPos = spinePivot + Vector3.Transform(new Vector3(0f, h.Chest.Y - Overlap, 0f), chestWorld);
        var neckPivot = chestPos + Vector3.Transform(new Vector3(0f, h.Chest.Y, 0f), chestWorld);
        var headPos = neckPivot + Vector3.Transform(new Vector3(0f, h.Head.Y - Overlap, 0f), headWorld);
        var headTop = neckPivot + Vector3.Transform(new Vector3(0f, 2f * h.Head.Y - Overlap, 0f), headWorld);

        (PartPose Upper, PartPose Fore, PartPose Hand, Vector3 WristPivot) ArmChain(float side, Quaternion rShoulderRel, Quaternion rElbowRel)
        {
            var shoulderWorld = ComposeIntrinsic(chestWorld, rShoulderRel);
            var forearmWorld = ComposeIntrinsic(shoulderWorld, rElbowRel);
            // Overlap subtracts exactly ONCE per JOINT (matching Build's own single-step formulas,
            // e.g. forearmPos = upperArmPos + (upperArmHalf.X + forearmHalf.X - Overlap)) -- a
            // pivot marks the true, un-shrunk edge of the segment it terminates (no Overlap), and
            // the NEXT segment's center is reached by subtracting Overlap from ITS OWN half-extent.
            // Subtracting Overlap on both sides of a pivot (as an earlier version of this code did)
            // silently shrinks the limb by an extra Overlap at every such joint -- caught by
            // HumanoidRagdollPoseTests.Solve_AllNeutralPose_MatchesBuildsOwnTPoseExactly, which
            // compares this method's output directly against Build's own placement.
            var shoulderPivot = chestPos + Vector3.Transform(new Vector3(side * h.Chest.X, h.Chest.Y * 0.5f, 0f), chestWorld);
            var upperArmPos = shoulderPivot + Vector3.Transform(new Vector3(side * (h.UpperArm.X - Overlap), 0f, 0f), shoulderWorld);
            var elbowPivot = upperArmPos + Vector3.Transform(new Vector3(side * h.UpperArm.X, 0f, 0f), shoulderWorld);
            var forearmPos = elbowPivot + Vector3.Transform(new Vector3(side * (h.Forearm.X - Overlap), 0f, 0f), forearmWorld);
            var wristPivot = elbowPivot + Vector3.Transform(new Vector3(side * (2f * h.Forearm.X - Overlap), 0f, 0f), forearmWorld);
            // The hand is a rigid stub on the forearm, not a joint (see RenderPartRole.Hand for why
            // it is not a body). Its centre is placed with DescribeForRender's own formula -- offset
            // from the FOREARM CENTRE along the forearm's length axis by one wrist -- rather than
            // from wristPivot, so a caller that renders Solve's output and a caller that renders a
            // live ragdoll put the hand in the same place instead of two formulas drifting apart.
            var handPos = forearmPos + Vector3.Transform(
                new Vector3(side * (h.Forearm.X + HandHalfExtents(h).X - Overlap), 0f, 0f), forearmWorld);
            return (new PartPose(upperArmPos, shoulderWorld), new PartPose(forearmPos, forearmWorld),
                new PartPose(handPos, forearmWorld), wristPivot);
        }

        (PartPose Thigh, PartPose Shin, PartPose Foot, Vector3 KneePivot, Vector3 FootSole) LegChain(
            float side, Quaternion rHipRel, Quaternion rKneeRel, Quaternion rAnkleRel)
        {
            var hipWorld = ComposeIntrinsic(pelvisWorld, rHipRel);
            var shinWorld = ComposeIntrinsic(hipWorld, rKneeRel);
            var footWorld = ComposeIntrinsic(shinWorld, rAnkleRel);

            // Unlike every other joint here, the hip's WORLD anchor doesn't sit on the thigh's own
            // centerline: Build's BallJoint pins both hips at the pelvis's OWN centerline (X=0
            // relative to pelvis), not offset sideways by HipPivotOffsetX -- the sideways offset
            // lives entirely in the thigh-side LOCAL anchor (see HipPivotOffsetX's own doc comment:
            // "how far sideways of the pelvis centerline the hip ball-socket's thigh-side anchor
            // sits"), which ROTATES with the thigh instead of staying fixed in world space. Treating
            // the anchor as if it were fixed at a sideways-offset point (an earlier version of this
            // code did) coincides with Build only for pure sagittal (X-axis) hip rotations, where X
            // is invariant -- transform-auditor caught this producing a real ~6-7 cm discrepancy for
            // cycle 3's hip-abduction (Z-axis) pose, one this file's own regression tests didn't
            // cover since none of the ten cycles asserted a rotated pose against Build's real anchor.
            var hipAnchorWorld = pelvisPos + Vector3.Transform(new Vector3(0f, -h.Pelvis.Y, 0f), pelvisWorld);
            var thighLocalAnchor = new Vector3(-side * HipPivotOffsetX(h), h.Thigh.Y - Overlap, 0f);
            var thighPos = hipAnchorWorld - Vector3.Transform(thighLocalAnchor, hipWorld);
            var kneePivot = thighPos + Vector3.Transform(new Vector3(0f, -h.Thigh.Y, 0f), hipWorld);
            var shinPos = kneePivot + Vector3.Transform(new Vector3(0f, -(h.Shin.Y - Overlap), 0f), shinWorld);
            var anklePivot = shinPos + Vector3.Transform(new Vector3(0f, -h.Shin.Y, 0f), shinWorld);
            var footPos = anklePivot + Vector3.Transform(new Vector3(0f, -(h.Foot.Y - Overlap), h.Foot.Z - FootForwardOffset), footWorld);
            var footBottomLocal = new Vector3(0f, -(2f * h.Foot.Y - Overlap), h.Foot.Z - FootForwardOffset);
            var footSole = anklePivot + Vector3.Transform(footBottomLocal, footWorld);
            return (new PartPose(thighPos, hipWorld), new PartPose(shinPos, shinWorld), new PartPose(footPos, footWorld), kneePivot, footSole);
        }

        var (lUpper, lFore, lHand, lWrist) = ArmChain(-1f, spec.LShoulder, spec.LElbow);
        var (rUpper, rFore, rHand, rWrist) = ArmChain(1f, spec.RShoulder, spec.RElbow);
        var (lThigh, lShin, lFoot, lKneePivot, lFootSole) = LegChain(-1f, spec.LHip, spec.LKnee, spec.LAnkle);
        var (rThigh, rShin, rFoot, rKneePivot, rFootSole) = LegChain(1f, spec.RHip, spec.RKnee, spec.RAnkle);

        var parts = new Dictionary<string, PartPose>
        {
            ["Pelvis"] = new(pelvisPos, pelvisWorld),
            ["Chest"] = new(chestPos, chestWorld),
            ["Head"] = new(headPos, headWorld),
            ["LeftUpperArm"] = lUpper,
            ["RightUpperArm"] = rUpper,
            ["LeftForearm"] = lFore,
            ["RightForearm"] = rFore,
            // 2026-09-17: the hands make this dictionary 1:1 with the set of SKELETON parts a
            // character renderer actually draws (Triage's PlayerVisualRig draws these fifteen plus
            // three coat panels, which are garment, not skeleton). Before this, a caller retargeting
            // a solved pose onto a renderer had to re-derive the wrist offset itself -- the exact
            // kind of duplicated convention that put the forearm behind the upper arm on every
            // humanoid in the game for weeks.
            ["LeftHand"] = lHand,
            ["RightHand"] = rHand,
            ["LeftThigh"] = lThigh,
            ["RightThigh"] = rThigh,
            ["LeftShin"] = lShin,
            ["RightShin"] = rShin,
            ["LeftFoot"] = lFoot,
            ["RightFoot"] = rFoot,
        };
        var contacts = new Dictionary<ContactPoint, Vector3>
        {
            [ContactPoint.LFootSole] = lFootSole,
            [ContactPoint.RFootSole] = rFootSole,
            [ContactPoint.LKneePivot] = lKneePivot,
            [ContactPoint.RKneePivot] = rKneePivot,
            [ContactPoint.LWristPivot] = lWrist,
            [ContactPoint.RWristPivot] = rWrist,
            [ContactPoint.PelvisBottom] = pelvisPos + Vector3.Transform(new Vector3(0f, -h.Pelvis.Y, 0f), pelvisWorld),
            [ContactPoint.PelvisFront] = pelvisPos + Vector3.Transform(new Vector3(0f, 0f, h.Pelvis.Z), pelvisWorld),
            [ContactPoint.HeadTop] = headTop,
        };
        return (parts, contacts);
    }

    /// <summary>Part names <see cref="Solve"/> returns that are rigid stubs rather than physics
    /// bodies — drawn by a renderer, skipped by <see cref="ApplyPose"/>.</summary>
    public static readonly IReadOnlySet<string> BodilessParts =
        new HashSet<string>(StringComparer.Ordinal) { "LeftHand", "RightHand" };

    /// <summary>Maps every <see cref="PartPose"/> key <see cref="Solve"/> returns onto the body id
    /// <see cref="Build"/> assigned it, and poses each one via <see cref="PhysicsWorld.SetDynamicPose"/>.
    /// The convenience entry point for a caller that just wants a ragdoll to assume a pose, without
    /// re-deriving the part-name-to-id mapping itself.</summary>
    public static void ApplyPose(PhysicsWorld world, Parts parts, IReadOnlyDictionary<string, PartPose> solved)
    {
        ArgumentNullException.ThrowIfNull(world);
        var idByName = new Dictionary<string, int>
        {
            ["Pelvis"] = parts.Pelvis, ["Chest"] = parts.Chest, ["Head"] = parts.Head,
            ["LeftUpperArm"] = parts.LeftUpperArm, ["RightUpperArm"] = parts.RightUpperArm,
            ["LeftForearm"] = parts.LeftForearm, ["RightForearm"] = parts.RightForearm,
            ["LeftThigh"] = parts.LeftThigh, ["RightThigh"] = parts.RightThigh,
            ["LeftShin"] = parts.LeftShin, ["RightShin"] = parts.RightShin,
            ["LeftFoot"] = parts.LeftFoot, ["RightFoot"] = parts.RightFoot,
        };
        foreach (var (name, pose) in solved)
        {
            // The hands Solve returns have no body of their own (RenderPartRole.Hand) — they ride
            // the forearm, so posing the forearm already places them. Skipping by an explicit
            // membership test rather than TryGetValue: a name that is neither a real body NOR a
            // known bodiless part is a caller error and must still throw, not be silently dropped.
            if (BodilessParts.Contains(name))
            {
                continue;
            }

            world.SetDynamicPose(idByName[name], pose.Position, pose.Orientation);
        }
    }

    /// <summary>
    /// Whether a <see cref="PoseSpec"/> joint's PARENT-RELATIVE rotation stays within the real
    /// limit <see cref="Build"/> constructs for that joint. A pose that fails this can still be
    /// applied on purpose (grading how far off a real ragdoll joint would have to cheat to reach
    /// it is itself useful information) — this is a validation query, not an enforced constraint.
    /// </summary>
    public enum JointKind { BallSwingTwist, HingeBend }

    /// <param name="Kind">Whether this joint is a ball-and-socket (swing + twist) or a 1-DOF hinge (bend only).</param>
    /// <param name="ReferenceAxis">The joint's own reference axis at rest (its swing-cone axis for
    /// ball joints, its bend/hinge axis for hinges) — matches the axis argument <see cref="Build"/>
    /// itself passes to AddSwingLimit/the hinge's AddTwistLimit for that joint.</param>
    /// <param name="MaxSwingDeg">Maximum swing cone half-angle in degrees (ball joints only; ignored for hinges).</param>
    /// <param name="MinTwistDeg">Minimum twist/bend angle in degrees.</param>
    /// <param name="MaxTwistDeg">Maximum twist/bend angle in degrees.</param>
    public readonly record struct JointLimits(JointKind Kind, Vector3 ReferenceAxis, float MaxSwingDeg, float MinTwistDeg, float MaxTwistDeg);

    /// <summary>Copied from <see cref="Build"/>'s own BallJoint/HingeJoint call sites — if that
    /// method's limits ever change, update this table to match rather than trusting these numbers
    /// forever.</summary>
    public static readonly IReadOnlyDictionary<string, JointLimits> JointLimitTable = new Dictionary<string, JointLimits>
    {
        ["Spine"] = new(JointKind.BallSwingTwist, Vector3.UnitY, 30f, -30f, 30f),
        ["Neck"] = new(JointKind.BallSwingTwist, Vector3.UnitY, 45f, -70f, 70f),
        ["Shoulder"] = new(JointKind.BallSwingTwist, Vector3.UnitX, ShoulderSwingLimitDegrees, -ShoulderTwistLimitDegrees, ShoulderTwistLimitDegrees),
        ["Elbow"] = new(JointKind.HingeBend, Vector3.UnitY, 0f, -10f, 145f),
        ["Hip"] = new(JointKind.BallSwingTwist, Vector3.UnitY, 90f, -40f, 40f),
        ["Knee"] = new(JointKind.HingeBend, Vector3.UnitX, 0f, -10f, 140f),
        ["Ankle"] = new(JointKind.BallSwingTwist, Vector3.UnitY, AnkleSwingLimitDegrees, -12f, 12f),
    };

    public readonly record struct JointLimitCheck(string Joint, float SwingDeg, float TwistOrBendDeg, bool WithinLimits, string Detail);

    /// <summary>
    /// Decomposes <paramref name="relativeRotation"/> (a <see cref="PoseSpec"/> joint quaternion,
    /// parent-relative) into swing and twist about <paramref name="jointName"/>'s reference axis
    /// (per <see cref="JointLimitTable"/>), and checks both against that joint's real limit.
    /// Standard swing-twist decomposition: for unit quaternion q=(v,w) and unit axis a,
    /// twistAngle = 2*atan2(dot(v,a), w) directly gives the signed rotation about a; the swing
    /// quaternion is q composed with that twist's inverse, and its own angle is the swing
    /// magnitude.
    /// </summary>
    public static JointLimitCheck CheckJointLimit(string jointName, Quaternion relativeRotation)
    {
        var limits = JointLimitTable[jointName];
        var a = Vector3.Normalize(limits.ReferenceAxis);
        var q = Quaternion.Normalize(relativeRotation);
        var v = new Vector3(q.X, q.Y, q.Z);

        var twistAngleRad = 2f * MathF.Atan2(Vector3.Dot(v, a), q.W);
        var twistQuat = Quaternion.CreateFromAxisAngle(a, twistAngleRad);
        var swingQuat = Quaternion.Normalize(q * Quaternion.Conjugate(twistQuat));
        var swingAngleRad = 2f * MathF.Atan2(new Vector3(swingQuat.X, swingQuat.Y, swingQuat.Z).Length(), swingQuat.W);

        var swingDeg = swingAngleRad * (180f / MathF.PI);
        var twistDeg = twistAngleRad * (180f / MathF.PI);

        var within = limits.Kind switch
        {
            JointKind.BallSwingTwist => swingDeg <= limits.MaxSwingDeg && twistDeg >= limits.MinTwistDeg && twistDeg <= limits.MaxTwistDeg,
            JointKind.HingeBend => twistDeg >= limits.MinTwistDeg && twistDeg <= limits.MaxTwistDeg,
            _ => throw new ArgumentOutOfRangeException(nameof(jointName)),
        };

        var detail = limits.Kind == JointKind.HingeBend
            ? $"bend {twistDeg:+0.0;-0.0} deg (limit {limits.MinTwistDeg:0}..{limits.MaxTwistDeg:0})"
            : $"swing {swingDeg:0.0} deg (limit {limits.MaxSwingDeg:0}), twist {twistDeg:+0.0;-0.0} deg (limit {limits.MinTwistDeg:0}..{limits.MaxTwistDeg:0})";

        return new JointLimitCheck(jointName, swingDeg, twistDeg, within, detail);
    }

    /// <summary>Result of <see cref="SolveGroundedPelvisY"/>: the chosen pelvis height, and (for a
    /// grounded pose) each named contact's leftover residual in metres — positive means still
    /// floating above the ground, negative means penetrating it. Empty for an airborne pose (see
    /// <see cref="PoseSpec.RootHeightOverride"/>), since there is nothing to compare against 0
    /// when nothing is meant to touch it.</summary>
    public readonly record struct PoseGroundFit(float PelvisY, IReadOnlyDictionary<ContactPoint, float> ResidualMeters);

    private static readonly Dictionary<string, ContactPoint> ContactsByName = Enum
        .GetValues<ContactPoint>()
        .ToDictionary(c => c.ToString(), c => c);

    /// <summary>
    /// Solves for the pelvis height that best rests every contact named in
    /// <paramref name="spec"/>.<see cref="PoseSpec.GroundedContacts"/> on the ground (world
    /// Y = 0). With one contact this is exact; with more than one it is the unweighted
    /// least-squares average, and <see cref="PoseGroundFit.ResidualMeters"/> reports how far each
    /// named contact still sits above (positive) or below (negative) the ground at that
    /// compromise height. A pose with NO grounded contacts (airborne) instead requires
    /// <see cref="PoseSpec.RootHeightOverride"/> — there is nothing to solve algebraically when
    /// nothing is meant to touch the ground, so "the right height" has to be an explicit,
    /// committed authorial choice rather than a silent default.
    /// </summary>
    public static PoseGroundFit SolveGroundedPelvisY(HalfExtents h, PoseSpec spec)
    {
        if (spec.GroundedContacts.Length == 0)
        {
            if (spec.RootHeightOverride is not { } overrideY)
            {
                throw new ArgumentException(
                    $"Pose '{spec.Name}' declares no grounded contacts and no RootHeightOverride -- " +
                    "either ground something or commit to an explicit airborne height.", nameof(spec));
            }

            return new PoseGroundFit(overrideY, new Dictionary<ContactPoint, float>());
        }

        var (_, contactsAtZero) = Solve(h, Vector3.Zero, spec);

        var offsets = new List<(ContactPoint Contact, float OffsetY)>();
        foreach (var name in spec.GroundedContacts)
        {
            if (!ContactsByName.TryGetValue(name, out var contact))
            {
                throw new ArgumentException($"Pose '{spec.Name}' names unknown grounded contact '{name}'.", nameof(spec));
            }

            offsets.Add((contact, contactsAtZero[contact].Y));
        }

        // contact.Y(pelvisY) = pelvisY + offsetY; to plant each contact exactly at 0 we'd need
        // pelvisY = -offsetY. The least-squares compromise across all of them is their mean.
        var bestPelvisY = -offsets.Average(o => o.OffsetY);

        var residuals = offsets.ToDictionary(o => o.Contact, o => bestPelvisY + o.OffsetY);
        return new PoseGroundFit(bestPelvisY, residuals);
    }

    /// <summary>
    /// Solves for the LOWEST pelvis height at which none of <paramref name="candidateContacts"/>
    /// penetrates the ground (world Y &lt; 0) — the tightest such height, so whichever candidate
    /// sits lowest for this exact pose touches at exactly 0 (reported as that contact's residual
    /// being 0 in the returned <see cref="PoseGroundFit"/>) and every other candidate floats by
    /// whatever positive residual its own geometry demands — never negative, unlike
    /// <see cref="SolveGroundedPelvisY"/>'s unweighted MEAN, which the "Ground" name it shares is
    /// otherwise identical to. That MEAN is the right trade-off for a single static pose (all its
    /// contacts are usually meant to be grounded, so a small symmetric float/penetrate split is an
    /// acceptable compromise to report as a residual); this method is for an ANIMATION frame's
    /// candidate contacts, "things that must not clip through the floor while the pose is
    /// transitioning", where even a small penetration is a visible floor-clip, not an abstract
    /// number. Cycle 12's <c>stand-to-kneel</c> animation hit exactly this: lerping the two
    /// keyframes' own MEAN-solved heights let both feet penetrate up to ~10-15 cm at the
    /// in-between frames despite each keyframe individually being correct. Calling this once per
    /// animation frame (with the same candidate set across the whole clip — e.g. every contact
    /// either keyframe grounds) fixes that by construction, at the cost of visible floating on
    /// whichever contact isn't currently the deepest.
    /// <para>
    /// Takes <see cref="ContactPoint"/> values directly rather than the string names
    /// <see cref="PoseSpec.GroundedContacts"/> uses: an animation's per-frame candidate set isn't
    /// tied to any one <see cref="PoseSpec"/> the way a static pose's grounded contacts are (it's
    /// typically the union of what several keyframes ground), so there is no single spec to
    /// validate the names against the way <see cref="SolveGroundedPelvisY"/> does.
    /// </para>
    /// </summary>
    public static PoseGroundFit SolveGroundClearancePelvisY(HalfExtents h, PoseSpec spec, IReadOnlyList<ContactPoint> candidateContacts)
    {
        if (candidateContacts.Count == 0)
        {
            throw new ArgumentException($"Pose '{spec.Name}' gave no candidate contacts -- nothing to keep clear of the ground.", nameof(candidateContacts));
        }

        var (_, contactsAtZero) = Solve(h, Vector3.Zero, spec);

        // contact.Y(pelvisY) = pelvisY + offsetY >= 0 for every candidate requires
        // pelvisY >= -offsetY for every candidate; the tightest (lowest) pelvisY satisfying all of
        // them simultaneously is the max of those per-candidate minimums.
        var bestPelvisY = candidateContacts.Max(c => -contactsAtZero[c].Y);
        var residuals = candidateContacts.ToDictionary(c => c, c => bestPelvisY + contactsAtZero[c].Y);
        return new PoseGroundFit(bestPelvisY, residuals);
    }
}
