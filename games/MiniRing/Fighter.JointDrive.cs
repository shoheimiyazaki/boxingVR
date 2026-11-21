using System.Numerics;

namespace MiniRing;

public sealed partial class Fighter
{
    private Vector3? _jointStanceTarget;
    private readonly List<JointWork> _jointWork = [];

    private readonly record struct JointWork(int Parent, int Child, Quaternion Before, Vector3 Torque, MuscleRegion Region);

    internal bool UsesJointDrive => _tuning.JointDrive;

    private void DriveRelativeTorso(int parent, int child, Quaternion parentTarget, Quaternion childTarget, float yawRate, float scale, float dt)
    {
        var a = _world.GetState(parent);
        var b = _world.GetState(child);
        var inverse = Quaternion.Conjugate(a.Orientation);
        var relative = Quaternion.Concatenate(b.Orientation, inverse);
        var target = Quaternion.Concatenate(childTarget, Quaternion.Conjugate(parentTarget));
        var error = FighterMath.RotationError(relative, target);
        var speed = Vector3.Transform(b.AngularVelocity - a.AngularVelocity, inverse);
        var limit = FormValue(_tuning.TrunkRotatorTorque, FighterTuning.DynamicPunch.TrunkRotatorTorque) * _tuning.TorqueScale;
        var stiffness = limit / MathF.Max(0.1f, PunchTwistLimit);
        var damping = 2f * MathF.Sqrt(stiffness * _world.GetLocalInertiaDiagonal(child).Y);
        var targetSpeed = Vector3.Transform(Vector3.UnitY * (_tuning.TrunkTwistFeedForward * yawRate), inverse);
        var torque = ClampLength((error * stiffness) + ((targetSpeed - speed) * damping), limit)
            * (scale * VigorOf(MuscleRegion.Trunk));
        ApplyMuscleCouple(parent, child, Vector3.Transform(torque, a.Orientation) * dt, MuscleRegion.Trunk, dt);
    }

    private void ApplyMuscleCouple(int parent, int child, Vector3 impulse, MuscleRegion region, float dt)
    {
        if (impulse.LengthSquared() <= 0f)
        {
            return;
        }

        var a = _world.GetState(parent);
        var b = _world.GetState(child);
        var linear = MathF.Max(0f, Vector3.Dot(impulse, b.AngularVelocity - a.AngularVelocity));
        var quadratic = 0.5f * (ImpulseInertiaTerm(parent, a.Orientation) + ImpulseInertiaTerm(child, b.Orientation));
        var index = (int)region;
        var scale = FatigueEnabled ? MuscleWork.ForceScale(linear, quadratic, 0f, _budget[index]) : 1f;
        var applied = impulse * scale;
        var reserved = (linear * scale) + (quadratic * scale * scale);
        _budget[index] = MathF.Max(0f, _budget[index] - reserved);
        StepMuscleAuthorized += reserved;
        var inverse = Quaternion.Conjugate(a.Orientation);
        _jointWork.Add(new JointWork(parent, child, Quaternion.Concatenate(b.Orientation, inverse),
            Vector3.Transform(applied / dt, inverse), region));
        _world.ApplyAngularImpulse(child, applied);
        _world.ApplyAngularImpulse(parent, -applied);

        float ImpulseInertiaTerm(int body, Quaternion orientation)
        {
            var local = Vector3.Transform(impulse, Quaternion.Conjugate(orientation));
            return Vector3.Dot(local, local / _world.GetLocalInertiaDiagonal(body));
        }
    }

    private void SettleJointWork()
    {
        // The constraints have now reacted to the applied torque. Use actual rotation at the
        // joint, including when the foot held it nearly still. Negative work does not refill fuel.
        foreach (var motor in _jointWork)
        {
            var parent = _world.GetState(motor.Parent);
            var child = _world.GetState(motor.Child);
            var after = Quaternion.Concatenate(child.Orientation, Quaternion.Conjugate(parent.Orientation));
            var displacement = FighterMath.RotationError(motor.Before, after);
            Draw((int)motor.Region, MathF.Max(0f, Vector3.Dot(motor.Torque, displacement)));
        }

        _jointWork.Clear();
    }

    private void SetJointLegStrength(float scale, float dt)
    {
        var index = (int)MuscleRegion.Legs;
        var unassigned = 2 * 4;
        foreach (var leg in _legParts)
        {
            var strength = scale * _tuning.TorqueScale * LegStrength(leg.AigisLeft) * VigorOf(MuscleRegion.Legs);
            var speed = KneeKickSpeed(leg.AigisLeft);
            Set(leg.HipServo, Parts.Pelvis, leg.Thigh, _tuning.HipMaxTorque, angular: true);
            Set(leg.KneeServo, leg.Thigh, leg.Shin, _tuning.KneeMaxTorque);
            Set(leg.AnkleServo, leg.Shin, leg.Heel, _tuning.AnkleMaxTorque);
            Set(leg.ToeServo, leg.Heel, leg.Toe, _tuning.ToeMaxTorque);

            void Set(int joint, int parent, int child, float torque, bool angular = false)
            {
                var allowance = _budget[index] / unassigned--;
                if (!_world.HasConstraint(joint))
                {
                    return;
                }

                var limit = ReserveServo(MuscleRegion.Legs, joint, parent, child, torque * strength, allowance, dt);
                if (angular)
                {
                    SetAngularStrength(joint, _tuning.LegServoHz, limit, speed);
                }
                else
                {
                    SetTwistStrength(joint, _tuning.LegServoHz, limit, speed);
                }
            }
        }
    }

    private void DriveThroughFeet(in FighterCommand command, float height, float groundY, float dt)
    {
        // A step moves the support plan and the leg IK target. The floor supplies the net wrench.
        var intent = ClampLength(command.ClampedMove + command.ClampedStep, 1f);
        StepFeet(command with { MoveWorld = intent }, height, dt);
        var pelvis = PelvisPosition;
        var center = CenterOfMass with { Y = 0f };
        var velocity = CenterOfMassVelocity with { Y = 0f };
        var lead = _footwork.Pose(Footwork.Lead);
        var rear = _footwork.Pose(Footwork.Rear);
        var support = lead.Swinging ? rear.Ball : rear.Swinging ? lead.Ball : (lead.Ball + rear.Ball) * 0.5f;
        support -= Frame.Forward * HalfExtents.Foot.Z;
        var requested = support + (intent * (0.12f * _standHeight));
        _jointStanceTarget = Vector3.Lerp(_jointStanceTarget ?? requested, requested, 1f - MathF.Exp(-dt / 0.14f));
        var horizontal = 1.8f * (_jointStanceTarget.Value - center) - (0.38f * velocity);
        var target = pelvis + ClampLength(horizontal, 0.18f * _standHeight);
        target.Y = groundY + (StandTargetHeight * (1f + _crouch))
            - Math.Clamp(0.20f * _world.GetState(Parts.Pelvis).LinearVelocity.Y, -0.08f, 0.08f);
        ApplyLegs(groundY, dt, target);
    }
}
