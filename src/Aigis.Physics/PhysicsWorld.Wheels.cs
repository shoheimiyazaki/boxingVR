using System.Numerics;
using BepuPhysics;
using BepuPhysics.Constraints;

namespace Aigis.Physics;

/// <summary>
/// Where a wheel hangs on its chassis — every vector in the CHASSIS's local frame.
/// </summary>
/// <param name="Anchor">The top of the suspension travel on the chassis.</param>
/// <param name="Down">Unit direction the suspension extends (normally chassis -Y).</param>
/// <param name="RestLength">Anchor-to-wheel-centre distance the spring holds at rest.</param>
/// <param name="MinLength">Shortest the suspension compresses to (bump stop).</param>
/// <param name="MaxLength">Longest it extends to (droop).</param>
/// <param name="Axle">Unit axle direction when the wheel points straight ahead.</param>
/// <param name="SpringFrequency">Suspension stiffness as a frequency (Hz): ~2–4 is a soft rover.</param>
/// <param name="SpringDamping">Damping ratio: 1 is critical; below it bounces.</param>
public readonly record struct WheelMount(
    Vector3 Anchor,
    Vector3 Down,
    float RestLength,
    float MinLength,
    float MaxLength,
    Vector3 Axle,
    float SpringFrequency = 3f,
    float SpringDamping = 0.7f);

/// <summary>The constraint ids one wheel is made of — opaque to callers, passed back to
/// <see cref="PhysicsWorld.SetWheelSteer"/> / <see cref="PhysicsWorld.SetWheelDrive"/>.</summary>
public readonly record struct WheelJoint(int Chassis, int Wheel, int Line, int Spring, int Travel, int Axle, int Drive, Vector3 RestAxle, Vector3 Down);

/// <summary>
/// Wheels on a dynamic chassis (2026-10-10, the lunar rovers: 「ローバーは物理ベースの挙動に…崖や段差で
/// 無理をするとローバーが転倒…段差でのスタック、高速すぎて段差などで空を舞う」).
/// <para>
/// <b>A real wheel, not a ray.</b> Each wheel is its own dynamic body hung from the chassis by five
/// constraints — the backend's own car recipe: the centre is held on the suspension line, a spring
/// holds it at rest length along that line, a limit bounds the travel, a hinge keeps it turning only
/// about its axle (the axle is rotated about the suspension line to steer), and a motor drives its
/// spin. Because the wheel is a body that touches the ground, the things the user asked for happen by
/// themselves: a wheel meets a ledge and stops (stuck), a high centre of mass on a slope goes over
/// (rollover), a rim taken fast throws the whole vehicle (airborne). A raycast suspension could not
/// get stuck on a step, which is why it is not used.
/// </para>
/// <para>The wheel body's own local Y must be its axle (a cylinder's axis), which is how
/// <see cref="AddWheel"/> orients the hinge's wheel side.</para>
/// </summary>
public sealed partial class PhysicsWorld
{
    /// <summary>
    /// Hangs <paramref name="wheel"/> (a dynamic body whose local Y is its axle, e.g. a cylinder)
    /// from <paramref name="chassis"/> as described by <paramref name="mount"/>. The wheel should
    /// already be posed at the rest position; the constraints are soft, so a small error is pulled in
    /// over the first steps rather than snapped.
    /// </summary>
    public WheelJoint AddWheel(int chassis, int wheel, WheelMount mount)
    {
        ThrowIfDisposed();
        var down = Vector3.Normalize(mount.Down);
        var axle = Vector3.Normalize(mount.Axle);
        var a = new BodyHandle(chassis);
        var b = new BodyHandle(wheel);
        var stiff = new SpringSettings(30f, 1f);

        var line = Track(_simulation.Solver.Add(a, b, new PointOnLineServo
        {
            LocalOffsetA = mount.Anchor,
            LocalOffsetB = Vector3.Zero,
            LocalDirection = down,
            ServoSettings = ServoSettings.Default,
            SpringSettings = stiff,
        }));
        var spring = Track(_simulation.Solver.Add(a, b, new LinearAxisServo
        {
            LocalOffsetA = mount.Anchor,
            LocalOffsetB = Vector3.Zero,
            LocalPlaneNormal = down,
            TargetOffset = mount.RestLength,
            ServoSettings = ServoSettings.Default,
            SpringSettings = new SpringSettings(mount.SpringFrequency, mount.SpringDamping),
        }));
        var travel = Track(_simulation.Solver.Add(a, b, new LinearAxisLimit
        {
            LocalOffsetA = mount.Anchor,
            LocalOffsetB = Vector3.Zero,
            LocalAxis = down,
            MinimumOffset = mount.MinLength,
            MaximumOffset = mount.MaxLength,
            SpringSettings = stiff,
        }));
        var hinge = Track(_simulation.Solver.Add(a, b, new AngularHinge
        {
            LocalHingeAxisA = axle,
            LocalHingeAxisB = Vector3.UnitY,
            SpringSettings = stiff,
        }));
        // The motor rides on the WHEEL's own axle (body A = wheel), so steering — which turns the
        // chassis-side hinge axis — never has to re-aim it.
        var drive = Track(_simulation.Solver.Add(b, a, new AngularAxisMotor
        {
            LocalAxisA = Vector3.UnitY,
            TargetVelocity = 0f,
            Settings = new MotorSettings(0f, 1e-4f),
        }));

        foreach (var id in (int[])[line, spring, travel, hinge, drive])
        {
            _jointDescriptors[id] = EmptyDescriptor(id, JointType.Wheel, chassis, wheel);
        }

        return new WheelJoint(chassis, wheel, line, spring, travel, hinge, drive, axle, down);
    }

    /// <summary>Turns a wheel's axle by <paramref name="angleRadians"/> about its suspension line —
    /// counter-clockwise seen from above the chassis when the suspension points down, so a positive
    /// angle turns a wheel facing chassis -Z to the left.</summary>
    public void SetWheelSteer(WheelJoint wheel, float angleRadians)
    {
        ThrowIfDisposed();
        var handle = _constraints[wheel.Axle];
        _simulation.Solver.GetDescription(handle, out AngularHinge description);
        var axis = Vector3.Transform(wheel.RestAxle, Quaternion.CreateFromAxisAngle(-wheel.Down, angleRadians));
        if (axis == description.LocalHingeAxisA)
        {
            return;
        }

        WakeVehicle(wheel);
        description.LocalHingeAxisA = axis;
        _simulation.Solver.ApplyDescription(handle, description);
    }

    /// <summary>
    /// Drives a wheel's spin toward <paramref name="angularSpeed"/> (rad/s about the wheel's own
    /// axle) with at most <paramref name="maxTorque"/> (N·m). A torque of 0 lets the wheel free-wheel;
    /// a target of 0 with torque is a brake.
    /// <para><b>The sign is LEFT-handed about the wheel's local +Y.</b> The motor is the chassis's
    /// spin relative to the wheel (see <see cref="AddWheel"/>), so a positive target turns the wheel
    /// the negative way: with the axle along the chassis's +X, a positive speed rolls it toward -Z
    /// (measured on the lunar rovers, LunarRover.cs). This doc said right-handed until 2026-10-21.</para>
    /// </summary>
    public void SetWheelDrive(WheelJoint wheel, float angularSpeed, float maxTorque)
    {
        ThrowIfDisposed();
        if (!float.IsFinite(angularSpeed) || !(maxTorque >= 0f))
        {
            throw new ArgumentOutOfRangeException(nameof(angularSpeed), $"Drive needs a finite speed and a non-negative torque ({angularSpeed}, {maxTorque}).");
        }

        var handle = _constraints[wheel.Drive];
        _simulation.Solver.GetDescription(handle, out AngularAxisMotor description);
        // Body A is the wheel, so the motor's target is the CHASSIS's spin relative to the wheel —
        // the negative of the wheel's own.
        var settings = new MotorSettings(maxTorque, 1e-4f);
        if (description.TargetVelocity == -angularSpeed && description.Settings.MaximumForce == settings.MaximumForce)
        {
            return;
        }

        WakeVehicle(wheel);
        description.TargetVelocity = -angularSpeed;
        description.Settings = settings;
        _simulation.Solver.ApplyDescription(handle, description);
    }

    /// <summary>
    /// A changed control has to wake the vehicle: a constraint's new target does not, and a rover
    /// parked for a second is asleep. Measured, not guessed — the first build steered and drove only
    /// if nothing had let it settle first (every wheel at exactly zero spin under full throttle).
    /// Waking the chassis wakes its whole island, wheels included. Only on a CHANGE, so a parked
    /// rover with its brake held still goes to sleep.
    /// <para><b>Wake FIRST, then write.</b> Waking moves the island's constraints from the sleeping set
    /// back into the solver's active batches; a description written into the sleeping copy before that
    /// move was measured lost — a rover boarded after it had settled sat at exactly zero speed under
    /// full throttle, every value identical step after step.</para>
    /// </summary>
    private void WakeVehicle(WheelJoint wheel) => WakeBody(wheel.Chassis);

    /// <summary>
    /// Wakes <paramref name="id"/> and everything constrained to it (its island) for this step.
    /// <para>A one-off wake is not enough to start something from rest: the backend keeps counting the
    /// steps an island has spent below its sleep threshold, and a motor that needs a few steps to get
    /// a parked vehicle above it loses that race and the island is put straight back to sleep. A
    /// caller that is actively driving something wakes it every step it is driving it.</para>
    /// </summary>
    public void WakeBody(int id)
    {
        ThrowIfDisposed();
        _simulation.Awakener.AwakenBody(new BodyHandle(id));
    }
}
