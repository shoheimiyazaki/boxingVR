using System.Numerics;

namespace Aigis.Physics;

/// <summary>
/// An immutable snapshot of one dynamic body, in world space. This is the unit of
/// observation handed to the agent driver (as JSON) and to the verification harness.
/// </summary>
/// <param name="Id">Stable body id (unique within a <see cref="PhysicsWorld"/>).</param>
/// <param name="Position">World-space center of mass.</param>
/// <param name="Orientation">World-space orientation.</param>
/// <param name="LinearVelocity">Linear velocity (m/s).</param>
/// <param name="AngularVelocity">Angular velocity (rad/s).</param>
/// <param name="Awake">Whether the physics solver currently considers the body active.</param>
public readonly record struct BodyState(
    int Id,
    Vector3 Position,
    Quaternion Orientation,
    Vector3 LinearVelocity,
    Vector3 AngularVelocity,
    bool Awake)
{
    /// <summary>True when the body has effectively stopped moving.</summary>
    public bool IsSettled(float linearEpsilon = 0.05f, float angularEpsilon = 0.05f)
        => !Awake || (LinearVelocity.Length() < linearEpsilon && AngularVelocity.Length() < angularEpsilon);
}
