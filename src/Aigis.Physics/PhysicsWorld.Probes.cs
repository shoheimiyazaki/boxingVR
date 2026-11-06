using System.Numerics;
using BepuUtilities;

namespace Aigis.Physics;

public sealed partial class PhysicsWorld
{
    private const int AnomalyCapacity = 100;

    /// <summary><see cref="Anomaly.Kind"/> for a body that has fallen out of the play area.</summary>
    public const string LeftWorldKind = "left_world";

    private readonly List<Anomaly> _anomalies = new();
    private readonly HashSet<int> _leftWorld = [];
    private float _maxExpectedSpeed = 100f;
    private float _leftWorldY = -50f;

    /// <summary>
    /// Below this Y a body counts as having left the play area: it is recorded once as a
    /// <see cref="LeftWorldKind"/> anomaly and then stops being speed-monitored.
    /// <para>
    /// Without this, anything knocked off a platform floods the anomaly list — free fall alone
    /// passes <see cref="MaxExpectedSpeed"/> after about ten seconds, which says nothing about
    /// solver health and is often the game's own goal (Wrecking Ball scores by knocking targets
    /// off the edge). Consumers that care about "did anything leave the world" read the
    /// <see cref="LeftWorldKind"/> entries; consumers checking "did the simulation explode" read
    /// the rest. The default (-50 m) is far below any play area this engine's games use.
    /// </para>
    /// </summary>
    public float LeftWorldY
    {
        get => _leftWorldY;
        set => _leftWorldY = float.IsFinite(value)
            ? value
            : throw new ArgumentOutOfRangeException(nameof(value), "Left-world Y must be finite.");
    }

    public float MaxExpectedSpeed
    {
        get => _maxExpectedSpeed;
        set => _maxExpectedSpeed = value > 0f && float.IsFinite(value)
            ? value
            : throw new ArgumentOutOfRangeException(nameof(value), "Expected speed must be finite and greater than zero.");
    }

    public IReadOnlyList<Anomaly> Anomalies
    {
        get
        {
            ThrowIfDisposed();
            return Array.AsReadOnly(_anomalies.ToArray());
        }
    }

    public IReadOnlyList<ProbeResult> RunProbes(ProbeOptions? options = null)
    {
        ThrowIfDisposed();
        var settings = options ?? new ProbeOptions();
        ValidateOptions(settings);
        var states = DynamicStates();
        var invalidCount = states.Count(state => !IsFinite(state));
        var maxSpeed = states.Length == 0 ? 0f : states.Max(state => state.LinearVelocity.Length());
        var minClearance = states.Length == 0
            ? 0f
            : states.Min(state => SupportPointY(state) - settings.FloorTopY);
        var maxAnchorDrift = MaxAnchorDrift();
        var maxLimitExcess = MaxJointLimitExcess();
        var unsettledCount = states.Count(state => !state.IsSettled());

        return Array.AsReadOnly(new[]
        {
            new ProbeResult("finite", invalidCount == 0, invalidCount, 0f, $"invalid bodies={invalidCount}"),
            new ProbeResult("speed_bound", maxSpeed < MaxExpectedSpeed, maxSpeed, MaxExpectedSpeed, $"max speed={maxSpeed}"),
            new ProbeResult("floor_clearance", minClearance >= -settings.ClearanceEpsilon, minClearance,
                -settings.ClearanceEpsilon, $"minimum support clearance={minClearance}"),
            new ProbeResult("anchor_drift", maxAnchorDrift <= settings.AnchorEpsilon, maxAnchorDrift,
                settings.AnchorEpsilon, $"maximum anchor separation={maxAnchorDrift}"),
            new ProbeResult("joint_limits", maxLimitExcess <= settings.LimitEpsilonRadians, maxLimitExcess,
                settings.LimitEpsilonRadians, $"maximum limit excess={maxLimitExcess}"),
            new ProbeResult("settled", unsettledCount == 0, unsettledCount, 0f, $"unsettled bodies={unsettledCount}"),
        });
    }

    public float TotalEnergy(float referenceY = 0f)
    {
        ThrowIfDisposed();
        var energy = 0f;
        var gravityMagnitude = Gravity.Length();
        foreach (var handle in _bodies)
        {
            var body = _simulation.Bodies[handle];
            var inertia = body.LocalInertia;
            if (inertia.InverseMass <= 0f)
            {
                continue;
            }

            var mass = 1f / inertia.InverseMass;
            var velocity = body.Velocity;
            var pose = body.Pose;
            var angularLocal = Vector3.Transform(velocity.Angular, Quaternion.Conjugate(pose.Orientation));
            Symmetric3x3.Invert(in inertia.InverseInertiaTensor, out var inertiaTensor);
            Symmetric3x3.TransformWithoutOverlap(in angularLocal, in inertiaTensor, out var angularMomentum);
            energy += 0.5f * mass * velocity.Linear.LengthSquared();
            energy += 0.5f * Vector3.Dot(angularLocal, angularMomentum);
            energy += mass * gravityMagnitude * (pose.Position.Y - referenceY);
        }

        return energy;
    }

    private void MonitorAnomalies()
    {
        foreach (var handle in _bodies)
        {
            if (_masses[handle.Value] <= 0f)
            {
                continue;
            }

            var state = GetState(handle.Value);
            if (!IsFinite(state))
            {
                AddAnomaly(new Anomaly(StepCount, state.Id, "nan", 0f));
                continue;
            }

            if (state.Position.Y < LeftWorldY)
            {
                if (_leftWorld.Add(state.Id))
                {
                    AddAnomaly(new Anomaly(StepCount, state.Id, LeftWorldKind, state.Position.Y));
                }

                continue;
            }

            // A body that comes back (a mover carrying it up, a lowered LeftWorldY) is monitored
            // again — and reported again if it leaves a second time.
            _leftWorld.Remove(state.Id);

            var speed = state.LinearVelocity.Length();
            if (speed > MaxExpectedSpeed)
            {
                AddAnomaly(new Anomaly(StepCount, state.Id, "speed", speed));
            }
        }
    }

    private void AddAnomaly(Anomaly anomaly)
    {
        if (_anomalies.Count == AnomalyCapacity)
        {
            _anomalies.RemoveAt(0);
        }

        _anomalies.Add(anomaly);
    }

    private float SupportPointY(BodyState state)
    {
        var shape = _shapes[state.Id];
        var extentY = shape.Kind switch
        {
            ShapeKind.Box or ShapeKind.Compound => RotatedBoxExtent(shape.Dimensions, state.Orientation).Y,
            ShapeKind.Capsule => shape.Dimensions.X + shape.Dimensions.Y *
                MathF.Abs(Vector3.Transform(Vector3.UnitY, state.Orientation).Y),
            ShapeKind.Sphere => shape.Dimensions.X,
            ShapeKind.Cylinder => RotatedBoxExtent(
                new Vector3(shape.Dimensions.X, shape.Dimensions.Y, shape.Dimensions.X), state.Orientation).Y,
            _ => throw new InvalidOperationException($"Unsupported shape kind: {shape.Kind}."),
        };
        return state.Position.Y - extentY;
    }

    private float MaxAnchorDrift()
    {
        var max = 0f;
        foreach (var descriptor in _jointDescriptors.Values.Where(j => j.Type == JointType.BallSocket && !j.Broken))
        {
            var a = GetState(descriptor.BodyA);
            var b = GetState(descriptor.BodyB);
            var anchorA = a.Position + Vector3.Transform(descriptor.LocalOffsetA, a.Orientation);
            var anchorB = b.Position + Vector3.Transform(descriptor.LocalOffsetB, b.Orientation);
            max = MathF.Max(max, Vector3.Distance(anchorA, anchorB));
        }

        return max;
    }

    private float MaxJointLimitExcess()
    {
        var max = 0f;
        foreach (var descriptor in _jointDescriptors.Values.Where(j => !j.Broken))
        {
            var excess = descriptor.Type switch
            {
                JointType.SwingLimit => SwingAngle(descriptor) - descriptor.MaxSwingRadians,
                JointType.TwistLimit => TwistLimitExcess(descriptor),
                _ => 0f,
            };
            max = MathF.Max(max, excess);
        }

        return MathF.Max(0f, max);
    }

    private static bool IsFinite(BodyState state)
        => IsFinite(state.Position) && IsFinite(state.LinearVelocity) && IsFinite(state.AngularVelocity) &&
           float.IsFinite(state.Orientation.X) && float.IsFinite(state.Orientation.Y) &&
           float.IsFinite(state.Orientation.Z) && float.IsFinite(state.Orientation.W);

    private static bool IsFinite(Vector3 value)
        => float.IsFinite(value.X) && float.IsFinite(value.Y) && float.IsFinite(value.Z);

    private BodyState[] DynamicStates()
        => _bodies.Where(handle => _masses[handle.Value] > 0f).Select(handle => GetState(handle.Value)).ToArray();

    private float TwistLimitExcess(JointDescriptor descriptor)
    {
        var angle = TwistAngle(descriptor);
        return MathF.Max(descriptor.MinTwistRadians - angle, angle - descriptor.MaxTwistRadians);
    }

    private static void ValidateOptions(ProbeOptions options)
    {
        if (!float.IsFinite(options.FloorTopY) || options.AnchorEpsilon < 0f ||
            options.LimitEpsilonRadians < 0f || options.ClearanceEpsilon < 0f ||
            !float.IsFinite(options.AnchorEpsilon) || !float.IsFinite(options.LimitEpsilonRadians) ||
            !float.IsFinite(options.ClearanceEpsilon))
        {
            throw new ArgumentOutOfRangeException(nameof(options), "Probe options must be finite and tolerances nonnegative.");
        }
    }
}
