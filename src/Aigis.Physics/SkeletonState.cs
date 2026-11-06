using System.Numerics;

namespace Aigis.Physics;

public enum ShapeKind
{
    Box,
    Capsule,
    Sphere,

    /// <summary>
    /// A right circular cylinder whose axis is its local +Y, matching <see cref="Capsule"/>'s axis
    /// convention. <see cref="BodyShape.Dimensions"/> packs it as (radius, HALF height, unused) —
    /// again the same packing as <see cref="Capsule"/>, so a caller reading either only has to know
    /// one rule.
    /// </summary>
    Cylinder,

    /// <summary>
    /// Several boxes rigidly joined into one body (<c>PhysicsWorld.AddDynamicCompound</c>).
    /// <see cref="BodyShape.Dimensions"/> holds the half extents of the box, centred on the body's origin and
    /// in its own frame, that contains every part — read exactly like a <see cref="Box"/>'s.
    /// </summary>
    Compound,
}

public enum JointType
{
    BallSocket,
    SwingLimit,
    TwistLimit,
    AngularMotor,
    Hinge,
    AngularServo,
    TwistServo,
    DistanceLimit,

    /// <summary>One of the constraints a vehicle wheel is hung by (see <c>PhysicsWorld.AddWheel</c>).</summary>
    Wheel,

    /// <summary>Two bodies held rigidly together, in position and orientation (see <c>PhysicsWorld.AddWeld</c>).</summary>
    Weld,

    /// <summary>A torque-capped spin drive about one axis (see <c>PhysicsWorld.AddAxisMotor</c>).</summary>
    AxisMotor,
}

public readonly record struct BodyShape(ShapeKind Kind, Vector3 Dimensions);

public readonly record struct MarkerState(int Id, string Name, Vector3 Position, Vector3 LinearVelocity);

public readonly record struct BoneState(
    int JointId,
    string From,
    string To,
    float Length,
    float RestLength,
    float AnchorSeparation);

public readonly record struct JointState(
    int Id,
    JointType Type,
    int BodyA,
    int BodyB,
    float? AngleRadians,
    float? LimitRadians,
    bool Broken);

public readonly record struct SkeletonSummary(
    Vector3 AabbMin,
    Vector3 AabbMax,
    Vector3 CenterOfMass,
    Vector3 CenterOfMassVelocity,
    float? Uprightness,
    bool AllSettled);

public sealed record SkeletonState(
    IReadOnlyList<MarkerState> Markers,
    IReadOnlyList<BoneState> Bones,
    IReadOnlyList<JointState> Joints,
    SkeletonSummary Summary);
