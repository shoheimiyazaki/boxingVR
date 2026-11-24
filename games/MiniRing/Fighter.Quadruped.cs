using System.Numerics;
using Aigis.Physics;

namespace MiniRing;

public sealed partial class Fighter
{
    private sealed record RearRig(HumanoidRagdoll.Parts Parts, HumanoidRagdoll.JointGroup[] Joints, ToeParts Toes)
    {
        public int[] Bodies => [Parts.LeftThigh, Parts.RightThigh, Parts.LeftShin, Parts.RightShin,
            Parts.LeftFoot, Parts.RightFoot, Toes.AigisLeft, Toes.AigisRight];
        public int[] PlusX => [Parts.RightThigh, Parts.RightShin, Parts.RightFoot, Toes.AigisRight];
    }

    private readonly Leg[] _rearLegs;
    private readonly Footwork? _rearFootwork;
    private bool _rearStepsNext;

    private IEnumerable<Leg> AllLegs()
    {
        foreach (var leg in _legParts)
        {
            yield return leg;
        }
        foreach (var leg in _rearLegs)
        {
            yield return leg;
        }
    }

    private (Leg Leg, FootPose Pose) StanceLeg(int index) => index < _legParts.Length
        ? (_legParts[index], _footwork.Pose(index))
        : (_rearLegs[index - _legParts.Length], _rearFootwork!.Pose(index - _legParts.Length));

    private IEnumerable<(Leg Leg, FootPose Pose)> StanceLegs()
    {
        for (var i = 0; i < _legParts.Length; i++)
        {
            yield return (_legParts[i], _footwork.Pose(i));
        }
        if (_rearFootwork is { } rear)
        {
            for (var i = 0; i < _rearLegs.Length; i++)
            {
                yield return (_rearLegs[i], rear.Pose(i));
            }
        }
    }

    private BodyPartKind? RearKindOf(int part)
    {
        foreach (var leg in _rearLegs)
        {
            if (part == leg.Thigh)
            {
                return BodyPartKind.Thigh;
            }
            if (part == leg.Shin)
            {
                return BodyPartKind.Shin;
            }
            if (part == leg.Heel)
            {
                return BodyPartKind.Foot;
            }
            if (part == leg.Toe)
            {
                return BodyPartKind.Toe;
            }
        }

        return null;
    }

    // Both pairs use the original leg geometry, constraints and muscle servos.
    private static RearRig AddRearRig(PhysicsWorld world, FighterTuning tuning, string name, HumanoidRagdoll.Parts parts,
        HumanoidRagdoll.HalfExtents half, ToeParts frontToes, ref IReadOnlyList<HumanoidRagdoll.JointGroup> joints)
    {
        var spread = Vector3.UnitZ * (half.Pelvis.Z * 0.76f);
        var frontJoints = joints.ToArray();
        var rearJoints = new List<HumanoidRagdoll.JointGroup>();
        int Copy(int source, Vector3 size, string label)
        {
            var state = world.GetState(source);
            var id = world.AddDynamicBox(state.Position - spread, size, world.GetMass(source));
            world.SetName(id, name + ".hind_" + label);
            return id;
        }

        (int Thigh, int Shin, int Heel) CopyLeg(bool left)
        {
            var side = left ? "l_" : "r_";
            var front = left ? new[] { parts.LeftThigh, parts.LeftShin, parts.LeftFoot, frontToes.AigisLeft }
                : new[] { parts.RightThigh, parts.RightShin, parts.RightFoot, frontToes.AigisRight };
            var rear = new[] { Copy(front[0], half.Thigh, side + "thigh"), Copy(front[1], half.Shin, side + "shin"),
                Copy(front[2], half.Foot, side + "foot") };

            for (var i = 0; i < rear.Length; i++)
            {
                var index = Array.FindIndex(frontJoints, j => j.ChildPart == front[i]);
                var joint = frontJoints[index];
                var parent = world.GetState(joint.ParentPart).Position;
                var child = world.GetState(joint.ChildPart).Position;
                var anchor = world.GetJointAnchors(joint.PrimaryConstraintId)?.AnchorA
                    ?? throw new InvalidOperationException($"Missing leg anchor for {joint.Joint}.");
                var shift = i == 0 ? new Vector3(child.X - anchor.X, 0f, 0f) : Vector3.Zero;
                var local = anchor + shift - child;
                var rearParent = i == 0 ? parts.Pelvis : rear[i - 1];
                rearJoints.Add(HumanoidRagdoll.AttachJointAt(world, joint.Joint, rearParent, rear[i],
                    anchor + shift - parent - (i == 0 ? spread : Vector3.Zero), local, tuning.TorqueScale));
                if (i == 0)
                {
                    foreach (var id in joint.AllConstraintIds)
                    {
                        world.RemoveConstraint(id, forget: true);
                    }
                    frontJoints[index] = HumanoidRagdoll.AttachJointAt(world, joint.Joint, parts.Pelvis, front[0],
                        anchor + shift - parent + spread, local, tuning.TorqueScale);
                }
            }

            foreach (var id in front)
            {
                var state = world.GetState(id);
                world.SetDynamicPose(id, state.Position + spread, state.Orientation);
            }

            return (rear[0], rear[1], rear[2]);
        }

        var left = CopyLeg(true);
        var right = CopyLeg(false);
        var hind = parts with
        {
            LeftThigh = left.Thigh, LeftShin = left.Shin, LeftFoot = left.Heel,
            RightThigh = right.Thigh, RightShin = right.Shin, RightFoot = right.Heel,
        };
        var toes = AddToes(world, tuning, name + "_hind", hind, half);
        var rig = new RearRig(hind, [.. rearJoints], toes);
        int[] frontBodies = [.. parts.All, frontToes.AigisLeft, frontToes.AigisRight];
        foreach (var body in rig.Bodies)
        {
            foreach (var other in frontBodies.Concat(rig.Bodies))
            {
                if (body != other)
                {
                    world.SetPairCollisionEnabled(body, other, false);
                }
            }
        }

        joints = [.. frontJoints, .. rig.Joints];
        return rig;
    }

    private static Leg[] CreateRearLegs(PhysicsWorld world, FighterTuning tuning, HumanoidRagdoll.HalfExtents half, RearRig rear)
    {
        var s = HumanoidRagdoll.AddLegServos(world, rear.Parts, tuning.LegServoHz,
            anklePitchMaxForce: tuning.AnkleMaxTorque * tuning.TorqueScale, kneeMaxForce: tuning.KneeMaxTorque * tuning.TorqueScale);
        return [
            Leg.Create(world, rear.Parts, rear.Joints, half, false, rear.Toes.AigisRight, rear.Toes.AigisRightServo, s.RightHip, s.RightKnee, s.RightAnkle),
            Leg.Create(world, rear.Parts, rear.Joints, half, true, rear.Toes.AigisLeft, rear.Toes.AigisLeftServo, s.LeftHip, s.LeftKnee, s.LeftAnkle),
        ];
    }

    private void UpdateQuadrupedGait(Footwork rear, FootworkInput input, float dt, float twistLimit)
    {
        // A walk uses one swing at a time, so the other three feet can bear the load.
        var mayStep = _legParts[0].Command is null && _legParts[1].Command is null;
        var first = _rearStepsNext ? rear : _footwork;
        var second = _rearStepsNext ? _footwork : rear;
        var firstWasSwinging = first.Swinging >= 0;
        UpdateLegPair(first, _rearStepsNext ? _rearLegs : _legParts, input, dt, twistLimit, mayStep && second.Swinging < 0);
        UpdateLegPair(second, _rearStepsNext ? _legParts : _rearLegs, input, dt, twistLimit,
            mayStep && !firstWasSwinging && first.Swinging < 0);
        if (_footwork.Swinging >= 0 || rear.Swinging >= 0)
        {
            _rearStepsNext = _footwork.Swinging >= 0;
        }
    }

    private void UpdateLegPair(Footwork gait, Leg[] pair, FootworkInput input, float dt, float twistLimit, bool allowStep)
    {
        var forward = new Vector2(MathF.Sin(_facingYaw), MathF.Cos(_facingYaw));
        var offset = forward * pair[0].PivotOnPelvis.Z;
        // Translate the two-foot planner to each pair's hips. A held front leg leaves the other three available for support.
        gait.Update(input with
        {
            Body = input.Body + offset,
            Com = input.Com is { } com ? com + offset : null,
            Twist = 0f,
            Weight = 0f,
            HipOffset = MathF.Abs(pair[0].PivotOnPelvis.X),
            LeadBall = BallOnGround(pair[0]),
            RearBall = BallOnGround(pair[1]),
            LeadOverreach = pair[0].Command is null && pair[0].ReachRatio > 0.995f,
            RearOverreach = pair[1].Command is null && pair[1].ReachRatio > 0.995f,
            LeadFree = pair[0].Command is not null,
            RearFree = pair[1].Command is not null,
            LegHeld = _legParts.Any(l => l.Command is not null),
        }, dt, twistLimit, canShiftLoad: false, allowStep: allowStep);
    }

    private Vector2 MultiLegSupport(Vector2 fallback)
    {
        var sum = Vector2.Zero;
        var count = 0;
        foreach (var (leg, pose) in StanceLegs())
        {
            if (pose.Swinging || leg.Command is not null || _detached.Contains(leg.Heel))
            {
                continue;
            }
            sum += new Vector2(pose.Ball.X, pose.Ball.Z);
            count++;
        }

        return count == 0 ? fallback : sum / count;
    }

    private bool HasFullLengthRearSupport() => RearLegStrength(_rearLegs[0]) > 0f || RearLegStrength(_rearLegs[1]) > 0f;

    private float RearLegStrength(Leg leg) =>
        _detached.Contains(leg.Heel) ? 0f : 0.5f * (Strength(leg.Thigh) + Strength(leg.Shin)) * _tuning.Build.LegTorqueRatio;

    private float MultiLegStrength()
    {
        var strength = LegStrength(true) + LegStrength(false);
        foreach (var leg in _rearLegs)
        {
            strength += RearLegStrength(leg);
        }
        return strength / (_legParts.Length + _rearLegs.Length);
    }

    private void TuneRearLegs(float scale)
    {
        foreach (var leg in _rearLegs)
        {
            var strength = _tuning.TorqueScale * MathF.Max(0.01f, scale * RearLegStrength(leg));
            SetAngularStrength(leg.HipServo, _tuning.LegServoHz, _tuning.HipMaxTorque * strength, LegServoSpeed);
            SetTwistStrength(leg.KneeServo, _tuning.LegServoHz, _tuning.KneeMaxTorque * strength);
            SetTwistStrength(leg.AnkleServo, _tuning.LegServoHz, _tuning.AnkleMaxTorque * strength);
            SetTwistStrength(leg.ToeServo, _tuning.LegServoHz, _tuning.ToeMaxTorque * strength);
        }
    }
}
