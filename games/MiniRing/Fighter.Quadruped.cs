using System.Numerics;
using Aigis.Physics;

namespace MiniRing;

public sealed partial class Fighter
{
    private readonly record struct CoxaRig(int Body, int Servo);
    private readonly record struct Coxae(CoxaRig Left, CoxaRig Right);

    private sealed record RearRig(HumanoidRagdoll.Parts Parts, HumanoidRagdoll.JointGroup[] Joints, ToeParts Toes,
        Coxae ForeCoxae, Coxae HindCoxae)
    {
        public int[] Bodies => [Parts.LeftThigh, Parts.RightThigh, Parts.LeftShin, Parts.RightShin,
            Parts.LeftFoot, Parts.RightFoot, Toes.AigisLeft, Toes.AigisRight, ForeCoxae.Left.Body, ForeCoxae.Right.Body, HindCoxae.Left.Body, HindCoxae.Right.Body];
        public int[] PlusX => [Parts.RightThigh, Parts.RightShin, Parts.RightFoot, Toes.AigisRight, ForeCoxae.Right.Body, HindCoxae.Right.Body];
    }

    // The small coxa takes the weight of the thorax. Its anchor springs must be firmer than the angular drives.
    private const float CoxaAnchorHz = 45f;
    private const float ThoraxWeldHz = 35f;

    private readonly Leg[] _rearLegs;
    private readonly Footwork? _rearFootwork;
    private bool _rearStepsNext;

    /// <summary>後ろ脚の本数（四足の体だけ 2、人は 0）。観測の道具（motioncard の記録）が読むだけで、試合の計算には使わない。</summary>
    public int RearLegCount => _rearLegs.Length;

    /// <summary>後ろ脚の腿・すね・かかと・つま先の剛体（観測だけ）。<paramref name="leg"/> は前の脚と同じ側の番号（0 = <see cref="ToeId"/>(0) と同じ側）。</summary>
    public (int Thigh, int Shin, int Heel, int Toe) RearLegBodies(int leg)
    {
        var rear = _rearLegs.First(r => r.AigisLeft == _legParts[leg].AigisLeft);
        return (rear.Thigh, rear.Shin, rear.Heel, rear.Toe);
    }

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
            if (part == leg.Coxa)
            {
                return BodyPartKind.Coxa;
            }
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

    private static void JoinThorax(PhysicsWorld world, HumanoidRagdoll.Parts parts, Span<HumanoidRagdoll.JointGroup> joints, Vector3 advance)
    {
        foreach (var id in (int[])[parts.Chest, parts.Head, parts.LeftUpperArm, parts.RightUpperArm, parts.LeftForearm, parts.RightForearm])
        {
            var state = world.GetState(id);
            world.SetDynamicPose(id, state.Position + advance, state.Orientation);
        }

        for (var i = 0; i < joints.Length; i++)
        {
            var spine = joints[i];
            if (spine.Joint != HumanoidRagdoll.RagdollJoint.Spine)
            {
                continue;
            }

            foreach (var id in spine.AllConstraintIds)
            {
                world.RemoveConstraint(id, forget: true);
            }

            var weld = world.AddWeld(parts.Pelvis, parts.Chest, frequency: ThoraxWeldHz);
            joints[i] = spine with { PrimaryConstraintId = weld, TorqueConstraintId = weld, AllConstraintIds = [weld] };
            return;
        }
        throw new InvalidOperationException("Quadruped thorax requires the original spine joint.");
    }

    // The femur through the tip reuse the original leg controller. The coxa adds a lateral pivot.
    private static RearRig AddRearRig(PhysicsWorld world, FighterTuning tuning, string name, HumanoidRagdoll.Parts parts,
        HumanoidRagdoll.HalfExtents half, ToeParts frontToes, ref IReadOnlyList<HumanoidRagdoll.JointGroup> joints)
    {
        var spread = Vector3.UnitZ * (half.Pelvis.Z * 0.76f);
        var frontJoints = joints.ToArray();
        JoinThorax(world, parts, frontJoints, spread);
        var coxaJoints = new List<HumanoidRagdoll.JointGroup>();
        var rearJoints = new List<HumanoidRagdoll.JointGroup>();
        var coxaHalf = tuning.CoxaHalf;
        var coxaMass = tuning.CoxaMass;
        var pelvis = world.GetState(parts.Pelvis).Position;

        CoxaRig AddCoxa(string label, HumanoidRagdoll.RagdollJoint kind, Vector3 outer, float side)
        {
            var center = outer - (coxaHalf.X * side * Vector3.UnitX);
            var inner = center - (coxaHalf.X * side * Vector3.UnitX);
            var id = world.AddDynamicBox(center, coxaHalf, coxaMass);
            world.SetName(id, name + "." + label + "_coxa");
            var hinge = world.AddHinge(parts.Pelvis, id, inner - pelvis, inner - center, Vector3.UnitZ, Vector3.UnitZ, CoxaAnchorHz);
            var limit = world.AddTwistLimit(parts.Pelvis, id, Quaternion.Identity, Quaternion.Identity, -0.30f, 0.30f, tuning.ToeStopHz);
            var spring = world.AddTwistServo(parts.Pelvis, id, Quaternion.Identity, Quaternion.Identity, 0f,
                tuning.LegServoHz, 1f, 11.92f, 0f, tuning.HipMaxTorque * tuning.TorqueScale);
            coxaJoints.Add(new HumanoidRagdoll.JointGroup(kind, parts.Pelvis, id, hinge, limit, [hinge, limit, spring], false));
            return new CoxaRig(id, spring);
        }

        int Copy(int source, Vector3 size, string label, Vector3 translation)
        {
            var state = world.GetState(source);
            var id = world.AddDynamicBox(state.Position + translation, size, world.GetMass(source));
            world.SetName(id, name + ".hind_" + label);
            return id;
        }

        (int Thigh, int Shin, int Heel, CoxaRig ForeCoxa, CoxaRig HindCoxa) CopyLeg(bool left)
        {
            var side = left ? "l_" : "r_";
            var sign = left ? -1f : 1f;
            var lateral = sign * 2f * coxaHalf.X * Vector3.UnitX;
            var front = left ? new[] { parts.LeftThigh, parts.LeftShin, parts.LeftFoot, frontToes.AigisLeft }
                : new[] { parts.RightThigh, parts.RightShin, parts.RightFoot, frontToes.AigisRight };
            var rear = new[] { Copy(front[0], half.Thigh, side + "thigh", lateral - spread), Copy(front[1], half.Shin, side + "shin", lateral - spread),
                Copy(front[2], half.Foot, side + "foot", lateral - spread) };

            CoxaRig foreCoxa = default;
            CoxaRig hindCoxa = default;
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
                if (i == 0)
                {
                    var outer = anchor + shift + lateral;
                    foreCoxa = AddCoxa(side + "fore", joint.Joint, outer + spread, sign);
                    hindCoxa = AddCoxa(side + "hind", joint.Joint, outer - spread, sign);
                    var coxaPivot = sign * coxaHalf.X * Vector3.UnitX;
                    var hindHip = HumanoidRagdoll.AttachJointAt(world, joint.Joint, hindCoxa.Body, rear[i], coxaPivot, local, tuning.TorqueScale);
                    world.SetJointSpring(hindHip.PrimaryConstraintId, CoxaAnchorHz, dampingRatio: 1f);
                    rearJoints.Add(hindHip);
                    foreach (var id in joint.AllConstraintIds)
                    {
                        world.RemoveConstraint(id, forget: true);
                    }
                    frontJoints[index] = HumanoidRagdoll.AttachJointAt(world, joint.Joint, foreCoxa.Body, front[0], coxaPivot, local, tuning.TorqueScale);
                    world.SetJointSpring(frontJoints[index].PrimaryConstraintId, CoxaAnchorHz, dampingRatio: 1f);
                }
                else
                {
                    rearJoints.Add(HumanoidRagdoll.AttachJointAt(world, joint.Joint, rear[i - 1], rear[i], anchor - parent, local, tuning.TorqueScale));
                }
            }

            foreach (var id in front)
            {
                var state = world.GetState(id);
                world.SetDynamicPose(id, state.Position + lateral + spread, state.Orientation);
            }

            return (rear[0], rear[1], rear[2], foreCoxa, hindCoxa);
        }

        var left = CopyLeg(true);
        var right = CopyLeg(false);
        var hind = parts with
        {
            LeftThigh = left.Thigh, LeftShin = left.Shin, LeftFoot = left.Heel,
            RightThigh = right.Thigh, RightShin = right.Shin, RightFoot = right.Heel,
        };
        var toes = AddToes(world, tuning, name + "_hind", hind, half);
        var rig = new RearRig(hind, [.. rearJoints], toes, new Coxae(left.ForeCoxa, right.ForeCoxa), new Coxae(left.HindCoxa, right.HindCoxa));
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

        joints = [.. frontJoints, .. rig.Joints, .. coxaJoints];
        return rig;
    }

    private static Leg[] CreateRearLegs(PhysicsWorld world, FighterTuning tuning, HumanoidRagdoll.HalfExtents half, RearRig rear)
    {
        var s = HumanoidRagdoll.AddLegServos(world, rear.Parts, tuning.LegServoHz,
            anklePitchMaxForce: tuning.AnkleMaxTorque * tuning.TorqueScale, kneeMaxForce: tuning.KneeMaxTorque * tuning.TorqueScale,
            leftHipParent: rear.HindCoxae.Left.Body, rightHipParent: rear.HindCoxae.Right.Body);
        return [
            Leg.Create(world, rear.Parts, rear.Joints, half, false, rear.Toes.AigisRight, rear.Toes.AigisRightServo, s.RightHip, s.RightKnee, s.RightAnkle, rear.HindCoxae.Right),
            Leg.Create(world, rear.Parts, rear.Joints, half, true, rear.Toes.AigisLeft, rear.Toes.AigisLeftServo, s.LeftHip, s.LeftKnee, s.LeftAnkle, rear.HindCoxae.Left),
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
