using System.Numerics;
using Aigis.Physics;
using Aigis.Render.Voxels;
using MiniRing.Bots;
using MiniRing.Characters;
using MiniRing.Weapons;

namespace MiniRing.Arena;

/// <summary>Bind geometry from the roster model. Sampling works entirely on copied bone and mesh data.</summary>
internal sealed class GalvaRig
{
    internal readonly record struct Bone(string Name, int Parent, Vector3 Bind, Vector3 End, VoxelVolumeDraw Draw);
    internal readonly record struct Pose(Vector3 At, Quaternion Rotation)
    {
        public Matrix4x4 Matrix => Matrix4x4.CreateFromQuaternion(Rotation) * Matrix4x4.CreateTranslation(At);
    }
    private readonly record struct Chain(int Upper, int Lower, int End);
    private readonly record struct Leg(int Root, Chain Chain, int Toe, bool Front, float Side, Vector3 Plant);

    private readonly List<Bone> _bones = [];
    private readonly List<Leg> _legs = [];
    private readonly Chain[] _arms = new Chain[2];
    private readonly Quaternion[] _bladeBind = new Quaternion[2];
    private int _chest;
    private int _head;

    public IReadOnlyList<Bone> Bones => _bones;
    public int FrontRightToe => _legs[1].Toe;

    public static GalvaRig Create()
    {
        var character = CharacterRoster.Get("galva");
        using var game = new BoxingGame(corners: BotCatalog.Corners("boxer") with
        {
            PlayerBody = character.Build, PlayerWear = character.Wear, PlayerLook = character.Look, PlayerFaceSpec = character.Face,
            ExternalBlue = true, Referee = false,
        });
        var rig = new GalvaRig();
        var f = game.Player;
        var world = game.World;
        var parts = f.Parts;
        var origin = new Vector3(f.PelvisPosition.X, BoxingGame.RingSurfaceHeight, f.PelvisPosition.Z);
        var joints = f.BridgeJoints().ToDictionary(j => j.Child);
        Vector3 Pivot(int body)
        {
            var joint = joints[body];
            var parent = world.GetState(joint.Parent);
            return parent.Position + Vector3.Transform(joint.AnchorOnParent, parent.Orientation);
        }
        int Add(string name, int parent, Vector3 pivot, Vector3 end, VoxelVolumeDraw draw)
        {
            rig._bones.Add(new Bone(name, parent, pivot - origin, end - origin,
                draw with { CellToWorld = draw.CellToWorld * Matrix4x4.CreateTranslation(-pivot) }));
            return rig._bones.Count - 1;
        }
        int Part(string name, int parent, int body, Vector3 pivot, Vector3? end = null) =>
            Add(name, parent, pivot, end ?? world.GetState(body).Position, f.Voxels!.PartOf(body)!.Draw(0f, null));
        int Child(int parent, BodyPartKind kind) => joints.Values.Single(j => j.Parent == parent && f.KindOf(j.Child) == kind).Child;

        var pelvis = Part("Thorax", -1, parts.Pelvis, f.PelvisPosition);
        rig._chest = Part("Chest", pelvis, parts.Chest, Pivot(parts.Pelvis));
        rig._head = Part("Head", rig._chest, parts.Head, Pivot(parts.Head),
            f.HeadPosition + Vector3.UnitZ * f.HalfExtents.Head.Z);

        foreach (var hand in new[] { Hand.Left, Hand.Right })
        {
            var plus = hand == Hand.Left;
            var side = plus ? ".L" : ".R";
            var upper = plus ? parts.RightUpperArm : parts.LeftUpperArm;
            var lower = plus ? parts.RightForearm : parts.LeftForearm;
            var claw = game.GloveOf(f, hand) ?? throw new InvalidOperationException("The Galva model is missing a claw.");
            var state = world.GetState(claw.Body.BodyId);
            var grip = state.Position + Vector3.Transform(Weapon.GripOf(claw.Kind, Weapon.MirroredOn(claw.Kind, hand)), state.Orientation);
            var tip = ClawSegment.FromWrist(hand, grip, state.Orientation).Tip;
            var s = Part("UpperArm" + side, rig._chest, upper, Pivot(upper));
            var e = Part("Forearm" + side, s, lower, Pivot(lower));
            var wrist = Add("Claw" + side, e, grip, tip, claw.Draw());
            rig._arms[(int)hand] = new Chain(s, e, wrist);
            rig._bladeBind[(int)hand] = state.Orientation;
        }

        var coxae = f.AllParts.Where(id => f.KindOf(id) == BodyPartKind.Coxa)
            .OrderByDescending(id => world.GetState(id).Position.Z)
            .Chunk(2).SelectMany(pair => pair.OrderByDescending(id => world.GetState(id).Position.X)).ToArray();
        var hingeJoints = world.JointStates().Where(j => j.Type == JointType.Hinge).ToArray();
        foreach (var coxa in coxae)
        {
            var thigh = Child(coxa, BodyPartKind.Thigh);
            var shin = Child(thigh, BodyPartKind.Shin);
            var foot = Child(shin, BodyPartKind.Foot);
            var toeLink = hingeJoints.Single(j => j.BodyA == foot && f.KindOf(j.BodyB) == BodyPartKind.Toe);
            var toe = toeLink.BodyB;
            var toeJoint = world.GetJointAnchors(toeLink.Id)!.Value.AnchorA;
            var tip = world.GetState(toe).Position - Vector3.UnitY * f.HalfExtentsOf(toe).Y;
            var coxaPosition = world.GetState(coxa).Position - origin;
            var front = coxaPosition.Z > 0f;
            var side = MathF.CopySign(1f, coxaPosition.X);
            var label = (front ? "Front" : "Hind") + (side > 0 ? ".L" : ".R");
            var root = Part("Coxa" + label, pelvis, coxa, Pivot(coxa));
            var h = Part("Femur" + label, root, thigh, Pivot(thigh));
            var k = Part("Tibia" + label, h, shin, Pivot(shin));
            var a = Part("Tarsus" + label, k, foot, Pivot(foot));
            var t = Part("Tip" + label, a, toe, toeJoint, tip);
            var plant = (tip - origin) with { Y = 0.005f };
            plant += new Vector3(side * 0.14f, 0f, front ? 0.07f : -0.09f);
            rig._legs.Add(new Leg(root, new Chain(h, k, a), t, front, side, plant));
        }
        return rig;
    }

    public Pose[] Sample(GalvaClip clip, float time)
    {
        var phase = Math.Clamp(time / clip.Seconds, 0f, 1f);
        var motion = clip.At(phase);
        var poses = new Pose[_bones.Count];
        var torso = motion.Thorax;
        poses[0] = new Pose(_bones[0].Bind + motion.Root - Vector3.UnitY * 0.15f,
            Quaternion.CreateFromYawPitchRoll(torso.X, torso.Y, torso.Z));
        Follow(_chest, poses[0].Rotation);
        Follow(_head, Quaternion.Normalize(Quaternion.Concatenate(
            Quaternion.CreateFromYawPitchRoll(-0.28f * torso.X, -0.6f * torso.Y, 0f), poses[_chest].Rotation)));

        for (var i = 0; i < _legs.Count; i++)
        {
            var leg = _legs[i];
            var attack = clip.Id == "foreleg-thrust" && i == 1;
            var footAxis = attack ? Vector3.Normalize(motion.ForelegAxis) : -Vector3.UnitY;
            var footRotation = Aim(-Vector3.UnitY, footAxis, Vector3.UnitX, Vector3.UnitX);
            var step = clip.Walk ? clip.Foot(i, phase) + Vector3.UnitZ * motion.Root.Z : Vector3.Zero;
            var tip = leg.Plant + step + (attack ? motion.Foreleg : Vector3.Zero);
            var offset = _bones[leg.Toe].End - _bones[leg.Chain.End].Bind;
            var ankle = tip - Vector3.Transform(offset, footRotation);
            var tilt = -leg.Side * (0.12f + 1.1f * step.Y + (attack ? 0.32f * motion.Foreleg.Y : 0f));
            Follow(leg.Root, Quaternion.Normalize(Quaternion.Concatenate(Quaternion.CreateFromAxisAngle(Vector3.UnitZ, tilt), poses[0].Rotation)));
            Solve(leg.Chain, ankle, new Vector3(leg.Side, 0f, leg.Front ? 0.68f : -0.85f), footRotation);
            Follow(leg.Toe, footRotation);
        }

        Arm(Hand.Left, motion.Left);
        Arm(Hand.Right, motion.Right);
        return poses;

        void Arm(Hand hand, GalvaHandPose goal)
        {
            var index = (int)hand;
            var direction = Vector3.Normalize(goal.Blade);
            var bladeGoal = new ClawSegment(Vector3.Zero, direction * ClawSegment.Length, Vector3.UnitY).ToCommand(hand).Orientation!.Value;
            var rotation = Quaternion.Normalize(Quaternion.Concatenate(Quaternion.Conjugate(_bladeBind[index]), bladeGoal));
            var travel = clip.Walk ? Vector3.UnitZ * motion.Root.Z : Vector3.Zero;
            Solve(_arms[index], goal.Wrist + travel, new Vector3(hand.LocalSide(), -0.39f, -0.35f), rotation);
        }

        Vector3 JointAt(int index)
        {
            var bone = _bones[index];
            var parent = poses[bone.Parent];
            return parent.At + Vector3.Transform(bone.Bind - _bones[bone.Parent].Bind, parent.Rotation);
        }

        void Follow(int index, Quaternion rotation) => poses[index] = new Pose(JointAt(index), rotation);

        void Solve(Chain chain, Vector3 goal, Vector3 pole, Quaternion endRotation)
        {
            var restUpper = _bones[chain.Lower].Bind - _bones[chain.Upper].Bind;
            var restLower = _bones[chain.End].Bind - _bones[chain.Lower].Bind;
            var root = JointAt(chain.Upper);
            var a = restUpper.Length();
            var b = restLower.Length();
            var delta = goal - root;
            var axis = Vector3.Normalize(delta);
            var side = Vector3.Normalize(pole - Vector3.Dot(pole, axis) * axis);
            var distance = Math.Clamp(delta.Length(), MathF.Abs(a - b) + 0.002f, a + b - 0.002f);
            var along = (a * a - b * b + distance * distance) / (2f * distance);
            var upper = axis * along + side * MathF.Sqrt(MathF.Max(0f, a * a - along * along));
            var lower = axis * distance - upper;
            poses[chain.Upper] = new Pose(root, Aim(restUpper, upper, Vector3.UnitZ, pole));
            Follow(chain.Lower, Aim(restLower, lower, Vector3.UnitZ, pole));
            Follow(chain.End, endRotation);
        }
    }

    private static Quaternion Aim(Vector3 from, Vector3 to, Vector3 fromPole, Vector3 toPole)
    {
        var rest = LegIk.Basis(Vector3.Normalize(from), fromPole);
        var desired = LegIk.Basis(Vector3.Normalize(to), toPole);
        return Quaternion.Normalize(Quaternion.Concatenate(Quaternion.Conjugate(rest), desired));
    }
}
