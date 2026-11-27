using System.Numerics;
using MiniRing.Weapons;

namespace MiniRing.Motion;

/// <summary>開始時の体フレームで表した通過点・進行方向・振る平面。距離は m、時間は秒、刃の傾きは rad。</summary>
public readonly record struct BladeCutRequest(Vector3 Point, Vector3 Travel, Vector3 PlaneNormal,
    float Runup = 0.42f, float Follow = 0.30f, float Rake = 0f, float Preparation = 0.22f, float Swing = 0.28f);

/// <summary>腕の付け根と、握りを置ける距離の下限・上限。</summary>
public readonly record struct BladeArm(Vector3 Shoulder, float Min, float Max)
{
    internal static BladeArm From(Fighter fighter, Hand hand)
    {
        var (upper, lower) = fighter.ArmLengths(hand);
        return new BladeArm(fighter.Frame.WorldToLocal(fighter.ShoulderPosition(hand)), MathF.Abs(upper - lower), upper + lower);
    }

    internal bool Valid => BladeCutPlan.Finite(Shoulder) && Min >= 0f && Max > Min && float.IsFinite(Max);
    internal bool Includes(float squared) => squared >= Min * Min - 1e-5f && squared <= Max * Max + 1e-5f;
}

/// <summary>握りの位置と武器の向き。Point の引数は武器ローカルでの握りからの相対位置。</summary>
public readonly record struct BladeKeyPose(Vector3 Grip, Quaternion Orientation)
{
    public Vector3 Point(Vector3 gripOffset) => Grip + Vector3.Transform(gripOffset, Orientation);

    internal static BladeKeyPose Between(BladeKeyPose start, BladeKeyPose end, float fraction) =>
        new(Vector3.Lerp(start.Grip, end.Grip, fraction), Quaternion.Slerp(start.Orientation, end.Orientation, fraction));
}

/// <summary>武器ローカルでの刃の弦、刃が向く側、通過させる点、添え手の位置をまとめた型。</summary>
public readonly record struct BladeCutGeometry(Vector3 Chord, Vector3 Edge, Vector3 ContactOffset, Hand MainHand, Vector3? SupportOffset = null)
{
    public static bool Supports(WeaponKind kind) =>
        kind is WeaponKind.Katana or WeaponKind.Longsword or WeaponKind.IronSword or WeaponKind.SickleClaw;

    public static bool TryFromWeapon(WeaponKind kind, Hand hand, bool twoHanded, out BladeCutGeometry geometry,
        float contactFraction = 0.8f, float remainingFraction = 1f, Vector3? gripLocal = null, Vector3? supportOffset = null)
    {
        geometry = default;
        if (!Supports(kind) || hand is not (Hand.Left or Hand.Right) ||
            !(contactFraction > 0f && contactFraction <= 1f && remainingFraction > 0f && remainingFraction <= 1f) ||
            (twoHanded && kind is not (WeaponKind.Katana or WeaponKind.Longsword)) ||
            (gripLocal is { } grip && !BladeCutPlan.Finite(grip)) ||
            (supportOffset is { } offset && (!twoHanded || !BladeCutPlan.Finite(offset))))
        {
            return false;
        }

        var claw = kind == WeaponKind.SickleClaw;
        var chord = claw ? SickleClawShape.Edge(hand) : Weapon.TipOf(kind) - (gripLocal ?? Weapon.GripOf(kind));
        var edge = claw ? SickleClawShape.ConcaveSide(hand) : -Vector3.UnitY;
        Vector3? support = null;
        if (twoHanded)
        {
            var (top, bottom) = Weapon.TwoHandGripsOf(kind);
            support = supportOffset ?? (bottom - (gripLocal ?? top));
        }

        geometry = new BladeCutGeometry(chord, edge, chord * (contactFraction * remainingFraction), hand, support);
        return true;
    }
}

/// <summary>構え、通過姿勢、振り抜きを持つ、物理サーボ向けの位置・向きの目標列。</summary>
public sealed class BladeCutPlan
{
    private const float MaxArcAngle = 1.35f;
    private readonly Vector3 _normal;
    private readonly float _beforeAngle;
    private readonly float _afterAngle;
    private readonly float _swing;

    private BladeCutPlan(BladeCutGeometry geometry, BladeKeyPose start, BladeKeyPose contact,
        Vector3 center, Vector3 normal, float before, float after, float preparation, float swing)
    {
        Geometry = geometry;
        Start = start;
        Contact = contact;
        ArcCenter = center;
        _normal = normal;
        _beforeAngle = before;
        _afterAngle = after;
        _swing = swing;
        PreparationSeconds = preparation;
        ContactTime = preparation + swing * before / (before + after);
    }

    public BladeCutGeometry Geometry { get; }
    public BladeKeyPose Start { get; }
    public BladeKeyPose Windup => AlongArc(-_beforeAngle);
    public BladeKeyPose Contact { get; }
    public BladeKeyPose FollowThrough => AlongArc(_afterAngle);
    public Vector3 ArcCenter { get; }
    public float PreparationSeconds { get; }
    public float ContactTime { get; }
    public float Duration => PreparationSeconds + _swing;

    /// <summary>到達できる円弧を有限個の候補から選ぶ。距離を短縮する場合も通過点と向きは保つ。</summary>
    public static bool TryCreate(BladeCutRequest request, BladeCutGeometry geometry, BladeKeyPose start,
        BladeArm main, BladeArm? support, [System.Diagnostics.CodeAnalysis.NotNullWhen(true)] out BladeCutPlan? plan)
    {
        plan = null;
        if (!main.Valid || (geometry.SupportOffset is { } supportOffset && (!Finite(supportOffset) || support is not { Valid: true })) ||
            geometry.MainHand is not (Hand.Left or Hand.Right) || !Finite(start.Grip) || !Finite(geometry.ContactOffset) ||
            !Finite(request.Point) || !Positive(request.Runup) || !Positive(request.Follow) ||
            !Positive(request.Preparation) || !Positive(request.Swing) || !float.IsFinite(request.Preparation + request.Swing) ||
            !float.IsFinite(request.Rake) || MathF.Abs(request.Rake) >= MathF.PI / 2 ||
            !Unit(request.Travel, out var travel) || !Unit(request.PlaneNormal, out var normal) ||
            MathF.Abs(Vector3.Dot(normal, travel)) > 1e-4f ||
            !Unit(geometry.Chord, out var localChord) || !Unit(Reject(geometry.Edge, localChord), out var localEdge))
        {
            return false;
        }

        var startLength = start.Orientation.LengthSquared();
        if (!float.IsFinite(startLength) || startLength < 1e-8f)
        {
            return false;
        }

        start = start with { Orientation = Quaternion.Normalize(start.Orientation) };
        travel = Vector3.Normalize(Reject(travel, normal));
        var radial = Vector3.Cross(travel, normal);
        var chord = Vector3.Transform(radial, Quaternion.CreateFromAxisAngle(normal, request.Rake));
        if (!Unit(Reject(travel, chord), out var edge))
        {
            return false;
        }

        var rotation = Quaternion.Normalize(Quaternion.Concatenate(
            Quaternion.Conjugate(Basis(localChord, localEdge)), Basis(chord, edge)));
        var contact = new BladeKeyPose(request.Point - Vector3.Transform(geometry.ContactOffset, rotation), rotation);

        foreach (var fraction in (ReadOnlySpan<float>)[1f, 0.75f, 0.5f])
        {
            foreach (var radiusScale in (ReadOnlySpan<float>)[0.75f, 1.25f, 2f])
            {
                var radius = main.Max * radiusScale;
                var before = request.Runup * fraction / radius;
                var after = request.Follow * fraction / radius;
                if (before < 1e-5f || after < 1e-5f || before > MaxArcAngle || after > MaxArcAngle ||
                    !float.IsFinite(before + after))
                {
                    continue;
                }

                var center = request.Point - radial * radius;
                if (!FitsArc(main, contact.Grip, center, normal, before, after) ||
                    (geometry.SupportOffset is { } offset && !FitsArc(support!.Value, contact.Point(offset), center, normal, before, after)))
                {
                    continue;
                }

                var candidate = new BladeCutPlan(geometry, start, contact, center, normal, before, after, request.Preparation, request.Swing);
                if (FitsPreparation(candidate, main, support))
                {
                    plan = candidate;
                    return true;
                }
            }
        }

        return false;
    }

    /// <summary>構えまで補間し、続いて同じ平面で円弧を振る。通過時に目標点の速さが最大。</summary>
    public BladeKeyPose Sample(float seconds)
    {
        if (float.IsNaN(seconds) || seconds <= 0f)
        {
            return Start;
        }
        if (seconds >= Duration)
        {
            return FollowThrough;
        }
        if (seconds < PreparationSeconds)
        {
            var t = seconds / PreparationSeconds;
            return BladeKeyPose.Between(Start, Windup, t * t * (3f - 2f * t));
        }

        // ∫(1-cos(πt)) dt。両側の時間を角度に比例させるので、通過前後で速度が連続する。
        var phase = (seconds - PreparationSeconds) / _swing;
        var split = _beforeAngle / (_beforeAngle + _afterAngle);
        return phase <= split
            ? AlongArc(-_beforeAngle * (1f - Ramp(phase / split)))
            : AlongArc(_afterAngle * (1f - Ramp((1f - phase) / (1f - split))));
    }

    public FighterCommand ToCommand(float seconds, in FighterCommand basis, BodyFrame anchor, BodyFrame live) =>
        ToCommand(Sample(seconds), basis, anchor, live);

    public FighterCommand ToCommand(BladeKeyPose pose, in FighterCommand basis, BodyFrame anchor, BodyFrame live)
    {
        Vector3 LivePosition(Vector3 planned) => live.WorldToLocal(anchor.LocalToWorld(planned));
        var orientation = Quaternion.Concatenate(Quaternion.Concatenate(pose.Orientation, anchor.Rotation),
            Quaternion.Conjugate(live.Rotation));
        var main = basis.For(Geometry.MainHand) with
        {
            TargetLocal = LivePosition(pose.Grip), Orientation = orientation, Grip = true, LetGo = false, Slide = false,
        };
        var otherHand = Geometry.MainHand == Hand.Right ? Hand.Left : Hand.Right;
        var other = basis.For(otherHand);
        if (Geometry.SupportOffset is { } offset)
        {
            other = other with { TargetLocal = LivePosition(pose.Point(offset)), Grip = true, LetGo = false, Slide = false };
        }

        return Geometry.MainHand == Hand.Right ? basis with { Right = main, Left = other } : basis with { Left = main, Right = other };
    }

    private BladeKeyPose AlongArc(float angle)
    {
        var turn = Quaternion.CreateFromAxisAngle(_normal, angle);
        return new BladeKeyPose(ArcCenter + Vector3.Transform(Contact.Grip - ArcCenter, turn),
            Quaternion.Concatenate(Contact.Orientation, turn));
    }

    // 距離の二乗は C + A cos θ + B sin θ。端と区間内の停留点を検査する。
    private static bool FitsArc(BladeArm arm, Vector3 contact, Vector3 center, Vector3 normal, float before, float after)
    {
        var offset = contact - center;
        var planar = Reject(offset, normal);
        var shoulderToAxis = center + (offset - planar) - arm.Shoulder;
        var constant = shoulderToAxis.LengthSquared() + planar.LengthSquared();
        var a = 2f * Vector3.Dot(shoulderToAxis, planar);
        var b = 2f * Vector3.Dot(shoulderToAxis, Vector3.Cross(normal, planar));
        var maxAt = MathF.Atan2(b, a);
        var minAt = MathF.IEEERemainder(maxAt + MathF.PI, 2f * MathF.PI);
        bool At(float angle) => arm.Includes(constant + a * MathF.Cos(angle) + b * MathF.Sin(angle));
        bool InteriorFits(float angle) => angle < -before || angle > after || At(angle);
        return At(-before) && At(after) && InteriorFits(maxAt) && InteriorFits(minAt);
    }

    private static bool FitsPreparation(BladeCutPlan plan, BladeArm main, BladeArm? support)
    {
        if (!FitsGripPreparation(plan, main, Vector3.Zero))
        {
            return false;
        }

        return plan.Geometry.SupportOffset is not { } offset || FitsGripPreparation(plan, support!.Value, offset);
    }

    private static bool FitsGripPreparation(BladeCutPlan plan, BladeArm arm, Vector3 offset)
    {
        var windup = plan.Windup;
        // 物理中の実測の握りは拘束誤差で腕長の外に出ることがある。構えに入るまでにそのずれを解消する。
        var startDistance = Vector3.Distance(plan.Start.Point(offset), arm.Shoulder);
        var startMin = MathF.Min(arm.Min, startDistance);
        var startMax = MathF.Max(arm.Max, startDistance);
        for (var i = 1; i <= 8; i++)
        {
            var progress = i / 8f;
            var pose = BladeKeyPose.Between(plan.Start, windup, progress);
            var allowed = arm with
            {
                Min = startMin + (arm.Min - startMin) * progress,
                Max = startMax + (arm.Max - startMax) * progress,
            };
            if (!allowed.Includes(Vector3.DistanceSquared(pose.Point(offset), arm.Shoulder)))
            {
                return false;
            }
        }
        return true;
    }

    private static float Ramp(float t) => t - MathF.Sin(MathF.PI * t) / MathF.PI;
    private static Vector3 Reject(Vector3 v, Vector3 axis) => v - Vector3.Dot(v, axis) * axis;
    private static bool Positive(float value) => value > 0f && float.IsFinite(value);
    internal static bool Finite(Vector3 value) => float.IsFinite(value.X) && float.IsFinite(value.Y) && float.IsFinite(value.Z);

    private static bool Unit(Vector3 value, out Vector3 unit)
    {
        var squared = value.LengthSquared();
        unit = squared > 1e-8f && float.IsFinite(squared) ? value / MathF.Sqrt(squared) : default;
        return unit != default;
    }

    private static Quaternion Basis(Vector3 x, Vector3 y)
    {
        var z = Vector3.Cross(x, y);
        return Quaternion.CreateFromRotationMatrix(new Matrix4x4(
            x.X, x.Y, x.Z, 0f, y.X, y.Y, y.Z, 0f, z.X, z.Y, z.Z, 0f, 0f, 0f, 0f, 1f));
    }
}
