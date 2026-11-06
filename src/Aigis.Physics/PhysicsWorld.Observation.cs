using System.Numerics;

namespace Aigis.Physics;

public sealed partial class PhysicsWorld
{
    private readonly Dictionary<int, string> _names = new();
    private readonly Dictionary<string, int> _idsByName = new(StringComparer.Ordinal);
    private readonly Dictionary<int, float> _masses = new();
    private readonly Dictionary<int, BodyShape> _shapes = new();
    private (int From, int To)? _uprightAxis;

    public void SetName(int id, string name)
    {
        ThrowIfDisposed();
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        _ = GetState(id);

        if (_idsByName.TryGetValue(name, out var existing) && existing != id)
        {
            throw new ArgumentException($"Body name '{name}' is already assigned to body {existing}.", nameof(name));
        }

        if (_names.TryGetValue(id, out var previous))
        {
            _idsByName.Remove(previous);
        }

        _names[id] = name;
        _idsByName[name] = id;
    }

    public string? TryGetName(int id)
    {
        ThrowIfDisposed();
        return _names.GetValueOrDefault(id);
    }

    public int? FindByName(string name)
    {
        ThrowIfDisposed();
        return _idsByName.TryGetValue(name, out var id) ? id : null;
    }

    public void EnsureNamesAvailable(IEnumerable<string> names)
    {
        ThrowIfDisposed();
        var requested = names.ToArray();
        if (requested.Any(string.IsNullOrWhiteSpace) || requested.Distinct(StringComparer.Ordinal).Count() != requested.Length)
        {
            throw new ArgumentException("Body names must be non-empty and unique.", nameof(names));
        }

        var duplicate = requested.FirstOrDefault(_idsByName.ContainsKey);
        if (duplicate is not null)
        {
            throw new ArgumentException($"Body name '{duplicate}' is already assigned.", nameof(names));
        }
    }

    public void SetUprightAxis(int fromId, int toId)
    {
        _ = GetState(fromId);
        _ = GetState(toId);
        _uprightAxis = (fromId, toId);
    }

    public SkeletonState Skeleton()
    {
        ThrowIfDisposed();
        var markers = _names.OrderBy(pair => pair.Key)
            .Select(pair =>
            {
                var state = GetState(pair.Key);
                return new MarkerState(pair.Key, pair.Value, state.Position, state.LinearVelocity);
            })
            .ToArray();
        var markerIds = _names.Keys.ToHashSet();
        var bones = _jointDescriptors.Values
            .Where(j => j.Type == JointType.BallSocket && markerIds.Contains(j.BodyA) && markerIds.Contains(j.BodyB))
            .OrderBy(j => j.Id)
            .Select(ToBoneState)
            .ToArray();
        var joints = JointStates().Where(j => markerIds.Contains(j.BodyA) && markerIds.Contains(j.BodyB)).ToArray();

        if (markers.Length == 0)
        {
            return new SkeletonState(Array.AsReadOnly(markers), Array.AsReadOnly(bones), Array.AsReadOnly(joints),
                new SkeletonSummary(Vector3.Zero, Vector3.Zero, Vector3.Zero, Vector3.Zero, null, true));
        }

        var aabbMin = new Vector3(float.PositiveInfinity);
        var aabbMax = new Vector3(float.NegativeInfinity);
        var weightedPosition = Vector3.Zero;
        var weightedVelocity = Vector3.Zero;
        var totalMass = 0f;
        var allSettled = true;
        foreach (var marker in markers)
        {
            var state = GetState(marker.Id);
            var extent = WorldExtent(_shapes[marker.Id], state.Orientation);
            aabbMin = Vector3.Min(aabbMin, marker.Position - extent);
            aabbMax = Vector3.Max(aabbMax, marker.Position + extent);
            var mass = _masses[marker.Id];
            if (mass > 0f)
            {
                weightedPosition += marker.Position * mass;
                weightedVelocity += marker.LinearVelocity * mass;
                totalMass += mass;
            }
            allSettled &= GetState(marker.Id).IsSettled();
        }

        float? uprightness = null;
        if (_uprightAxis is { } axis && Gravity.LengthSquared() > 1e-10f)
        {
            var direction = GetState(axis.To).Position - GetState(axis.From).Position;
            if (direction.LengthSquared() > 1e-10f)
            {
                uprightness = Vector3.Dot(Vector3.Normalize(direction), Vector3.Normalize(-Gravity));
            }
        }

        var centerOfMass = totalMass > 0f
            ? weightedPosition / totalMass
            : markers.Aggregate(Vector3.Zero, (sum, marker) => sum + marker.Position) / markers.Length;
        var centerOfMassVelocity = totalMass > 0f
            ? weightedVelocity / totalMass
            : markers.Aggregate(Vector3.Zero, (sum, marker) => sum + marker.LinearVelocity) / markers.Length;
        return new SkeletonState(Array.AsReadOnly(markers), Array.AsReadOnly(bones), Array.AsReadOnly(joints),
            new SkeletonSummary(aabbMin, aabbMax, centerOfMass, centerOfMassVelocity, uprightness, allSettled));
    }

    private void RegisterBodyMetadata(int id, float mass, BodyShape shape)
    {
        _masses[id] = mass;
        _shapes[id] = shape;
    }

    private BoneState ToBoneState(JointDescriptor descriptor)
    {
        var a = GetState(descriptor.BodyA);
        var b = GetState(descriptor.BodyB);
        var anchorA = a.Position + Vector3.Transform(descriptor.LocalOffsetA, a.Orientation);
        var anchorB = b.Position + Vector3.Transform(descriptor.LocalOffsetB, b.Orientation);
        return new BoneState(
            descriptor.Id,
            _names[descriptor.BodyA],
            _names[descriptor.BodyB],
            Vector3.Distance(a.Position, b.Position),
            descriptor.RestLength,
            Vector3.Distance(anchorA, anchorB));
    }

    private static Vector3 WorldExtent(BodyShape shape, Quaternion orientation) => shape.Kind switch
    {
        ShapeKind.Box or ShapeKind.Compound => RotatedBoxExtent(shape.Dimensions, orientation),
        ShapeKind.Capsule => new Vector3(shape.Dimensions.X) + Vector3.Abs(Vector3.Transform(Vector3.UnitY, orientation)) * shape.Dimensions.Y,
        ShapeKind.Sphere => new Vector3(shape.Dimensions.X),
        // Conservative: the tight world extent of a rotated cylinder is an ellipse projection, but
        // its bounding box (radius, halfHeight, radius) rotated the same way as a box's is both
        // correct as an upper bound and consistent with how every other observation consumer here
        // reads an extent.
        ShapeKind.Cylinder => RotatedBoxExtent(
            new Vector3(shape.Dimensions.X, shape.Dimensions.Y, shape.Dimensions.X), orientation),
        _ => throw new ArgumentOutOfRangeException(nameof(shape)),
    };

    private static Vector3 RotatedBoxExtent(Vector3 halfExtents, Quaternion orientation)
    {
        var rotation = Matrix4x4.CreateFromQuaternion(orientation);
        return new Vector3(
            MathF.Abs(rotation.M11) * halfExtents.X + MathF.Abs(rotation.M21) * halfExtents.Y + MathF.Abs(rotation.M31) * halfExtents.Z,
            MathF.Abs(rotation.M12) * halfExtents.X + MathF.Abs(rotation.M22) * halfExtents.Y + MathF.Abs(rotation.M32) * halfExtents.Z,
            MathF.Abs(rotation.M13) * halfExtents.X + MathF.Abs(rotation.M23) * halfExtents.Y + MathF.Abs(rotation.M33) * halfExtents.Z);
    }
}
