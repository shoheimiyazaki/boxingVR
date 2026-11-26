using System.Numerics;
using MiniRing.Arena;
using MiniRing.Motion;

namespace MiniRing.Tests;

[TestClass]
public sealed class GalvaAnimationTests
{
    [TestMethod]
    public void EveryClipPreservesTheBranchModelBoneLengths()
    {
        var rig = GalvaRig.Create();
        Assert.HasCount(29, rig.Bones);
        Assert.HasCount(7, GalvaClips.All);
        foreach (var clip in GalvaClips.All)
        {
            for (var i = 0; i <= 30; i++)
            {
                var pose = rig.Sample(clip, clip.Seconds * i / 30f);
                for (var j = 1; j < rig.Bones.Count; j++)
                {
                    var bone = rig.Bones[j];
                    var parent = bone.Parent;
                    var offset = rig.Bones[j].Bind - rig.Bones[parent].Bind;
                    var expected = pose[parent].At + Vector3.Transform(offset, pose[parent].Rotation);
                    Assert.IsLessThan(0.0002f, Vector3.Distance(expected, pose[j].At), $"{clip.Id}: {bone.Name}");
                }
            }
        }
    }

    [TestMethod]
    public void AttackKeysCarryWristVelocityThroughTheStrike()
    {
        foreach (var id in new[] { "sweep", "kesa", "cleave", "claw-thrust" })
        {
            var clip = GalvaClips.All.Single(c => c.Id == id);
            var useLeft = id == "claw-thrust";
            var key = clip.Keys.Where(k => k.Time >= 0.40f && k.Time < 0.58f).First().Time;
            Vector3 Wrist(float t) => useLeft ? clip.At(t).Left.Wrist : clip.At(t).Right.Wrist;
            const float step = 0.001f;
            var arriving = (Wrist(key) - Wrist(key - step)) / step;
            var leaving = (Wrist(key + step) - Wrist(key)) / step;
            Assert.IsGreaterThan(0.30f, arriving.Length(), id);
            Assert.IsLessThan(0.16f, Vector3.Distance(arriving, leaving), id);
        }
    }

    [TestMethod]
    public void FoldedClawsTurnTheirInnerEdgeTowardTheUpperArm()
    {
        var rig = GalvaRig.Create();
        var clip = GalvaClips.All.Single(c => c.Id == "stalk");
        var pose = rig.Sample(clip, 0f);
        foreach (var hand in new[] { Hand.Left, Hand.Right })
        {
            var suffix = hand == Hand.Left ? ".L" : ".R";
            int Bone(string name) => Enumerable.Range(0, rig.Bones.Count).Single(i => rig.Bones[i].Name == name + suffix);
            var wrist = rig.ClawBone(hand);
            var forearm = Bone("Forearm");
            var shoulder = pose[Bone("UpperArm")].At;
            var elbow = pose[forearm].At;
            var towardArm = Vector3.Lerp(shoulder, elbow, 0.55f) - pose[wrist].At;
            var tip = Vector3.Transform(rig.Bones[wrist].End - rig.Bones[wrist].Bind, pose[wrist].Rotation);
            Assert.IsGreaterThan(0.10f, Vector3.Dot(Vector3.Normalize(tip), Vector3.Normalize(towardArm)), suffix);
            Assert.IsGreaterThan(0.10f, Vector3.Dot(rig.ClawBend(hand, pose[wrist]), Vector3.Normalize(towardArm)), suffix);

            var blade = rig.Bones[wrist];
            var bindForearm = Vector3.Normalize(blade.Bind - rig.Bones[forearm].Bind);
            Assert.IsLessThan(-0.25f, Vector3.Dot(bindForearm, Vector3.Normalize(blade.End - blade.Bind)), suffix);
        }
    }

    [TestMethod]
    public void BladeBoneTracksItsChordAndRollWithoutFlipping()
    {
        var rig = GalvaRig.Create();
        foreach (var clip in GalvaClips.All.Where(clip => !clip.Walk))
        {
            var previous = rig.Sample(clip, 0f);
            for (var frame = 1; frame <= 132; frame++)
            {
                var phase = frame / 132f;
                var pose = rig.Sample(clip, phase * clip.Seconds);
                var target = clip.At(phase);
                foreach (var hand in new[] { Hand.Left, Hand.Right })
                {
                    var index = rig.ClawBone(hand);
                    var bone = rig.Bones[index];
                    var direction = Vector3.Normalize(Vector3.Transform(bone.End - bone.Bind, pose[index].Rotation));
                    var key = hand == Hand.Left ? target.Left : target.Right;
                    var bend = Vector3.Normalize(key.Bend - Vector3.Dot(direction, key.Bend) * direction);
                    Assert.IsGreaterThan(0.999f, Vector3.Dot(Vector3.Normalize(key.Blade), direction), clip.Id);
                    Assert.IsGreaterThan(0.999f, Vector3.Dot(bend, rig.ClawBend(hand, pose[index])), clip.Id);
                    Assert.IsGreaterThan(0.98f, MathF.Abs(Quaternion.Dot(previous[index].Rotation, pose[index].Rotation)), $"{clip.Id} {hand} {phase:F3}");
                }
                for (var i = 0; i < pose.Length; i++)
                {
                    var name = rig.Bones[i].Name;
                    if (name.StartsWith("UpperArm", StringComparison.Ordinal) || name.StartsWith("Forearm", StringComparison.Ordinal))
                    {
                        Assert.IsGreaterThan(0.94f, MathF.Abs(Quaternion.Dot(previous[i].Rotation, pose[i].Rotation)),
                            $"{clip.Id} {name} {phase:F3}");
                    }
                }
                previous = pose;
            }
        }
    }

    [TestMethod]
    public void BothWalksLoopWithTravelAndDifferentContactRhythms()
    {
        var rig = GalvaRig.Create();
        var stalk = GalvaClips.All.Single(clip => clip.Id == "stalk");
        var trot = GalvaClips.All.Single(clip => clip.Id == "trot");
        foreach (var clip in new[] { stalk, trot })
        {
            var start = rig.Sample(clip, 0f);
            var end = rig.Sample(clip, clip.Seconds);
            var beforeLift = rig.Sample(clip, clip.Seconds * 0.025f);
            var toe = rig.FrontRightToe;
            var offset = rig.Bones[toe].End - rig.Bones[toe].Bind;
            var planted = start[toe].At + Vector3.Transform(offset, start[toe].Rotation);
            var stillPlanted = beforeLift[toe].At + Vector3.Transform(offset, beforeLift[toe].Rotation);
            Assert.IsLessThan(0.002f, Vector3.Distance(planted, stillPlanted), clip.Id);
            Assert.IsLessThan(0.008f, MathF.Abs(planted.Y), clip.Id);
            var travel = end[0].At - start[0].At;
            Assert.IsGreaterThan(0.5f, travel.Z);
            for (var i = 0; i < start.Length; i++)
            {
                Assert.IsLessThan(0.001f, Vector3.Distance(start[i].At + travel, end[i].At));
                Assert.IsGreaterThan(0.999f, MathF.Abs(Quaternion.Dot(start[i].Rotation, end[i].Rotation)));
            }
        }
        Assert.IsGreaterThan(0.05f, Vector3.Distance(rig.Sample(stalk, 0f)[rig.FrontRightToe].At, rig.Sample(trot, trot.Seconds * 0.16f)[rig.FrontRightToe].At));
    }

    [TestMethod]
    public void BvhRoundTripsTheSameBonePose()
    {
        var rig = GalvaRig.Create();
        foreach (var clip in GalvaClips.All)
        {
            var imported = BvhClip.Parse(GalvaBvh.Write(rig, clip, 60));
            var frame = imported.FrameCount / 2;
            var read = imported.Pose(frame);
            var pose = rig.Sample(clip, clip.Seconds * frame / (imported.FrameCount - 1));
            for (var i = 0; i < pose.Length; i++)
            {
                var actual = read[imported.IndexOf(rig.Bones[i].Name)];
                Assert.IsLessThan(0.001f, Vector3.Distance(pose[i].At, actual.Position));
                Assert.IsGreaterThan(0.999f, MathF.Abs(Quaternion.Dot(pose[i].Rotation, actual.Rotation)));
            }
        }
    }
}
