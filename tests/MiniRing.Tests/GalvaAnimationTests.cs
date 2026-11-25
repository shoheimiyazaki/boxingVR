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
        Assert.HasCount(6, GalvaClips.All);
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
