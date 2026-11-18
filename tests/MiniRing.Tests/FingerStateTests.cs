using System.Numerics;
using Aigis.Physics;
using MiniRing.Voxels;
using MiniRing.Weapons;

namespace MiniRing.Tests;

/// <summary>指ごと・節ごとの親データ: どの手形で削れても、同じ指・同じ節の欠けとして数えること。</summary>
[TestClass]
public sealed class FingerStateTests
{
    private static Weapon NewHand(PhysicsWorld world)
        => Weapon.CreateWorn(world, WeaponKind.Fist, BoxerAnatomy.RedGlove, Vector3.Zero, Quaternion.Identity);

    private static int BoneCell(Weapon hand, int finger, FingerSegment segment, int nth = 0)
        => Enumerable.Range(0, HandSkeleton.Parents.Count)
            .Where(i => HandSkeleton.Parents[i] is { } p && p.Finger == finger && p.Segment == segment && p.Material == BoxerAnatomy.HandBone)
            .Select(i => hand.HandState!.Placement[i]).ElementAt(nth);

    [TestMethod]
    public void EveryCell_BelongsToExactlyOneFingerSegment_AndEveryFingerSegmentHasBone()
    {
        var state = new BareHandState();
        var count = 0;
        for (var finger = 0; finger < BareHandState.FingerCount; finger++)
        {
            for (var segment = 0; segment < 3; segment++)
            {
                var part = state.Part(finger, (FingerSegment)segment);
                count += part.Cells;
                Assert.AreEqual(1f, part.Bone, 1e-6f, "an unharmed hand has every bone");
                Assert.AreEqual(1f, part.BoneWeakest, 1e-6f);
                Assert.AreEqual(0f, part.Cracked, 1e-6f);
                Assert.IsGreaterThan(0, part.Cells, $"finger {finger} segment {segment} has cells");
            }
        }

        count += state.Part(BareHandState.FingerCount, FingerSegment.Palm).Cells;
        Assert.AreEqual(state.Cells.Count, count, "no cell is left out or counted twice");
        Assert.AreEqual(HandSkeleton.Parents.Count, state.Cells.Count);
    }

    [TestMethod]
    public void AWoundedPhalanx_ShowsAsTheSameWoundedFingerInEveryOtherShape()
    {
        using var world = new PhysicsWorld();
        var hand = NewHand(world);
        var grid = hand.Body.Grid;
        // 人さし指の基節の骨を半分削る。
        var cell = BoneCell(hand, 0, FingerSegment.Proximal);
        grid.Wear(cell, 0.5f, []);
        var reference = hand.HandState!.Capture(grid);
        var proximal = reference.Part(0, FingerSegment.Proximal);
        Assert.IsLessThan(1f, proximal.Bone);
        Assert.IsGreaterThan(0f, proximal.Cracked);
        Assert.IsLessThan(1f, proximal.BoneWeakest);
        foreach (var other in new[] { (1, FingerSegment.Proximal), (0, FingerSegment.Middle), (0, FingerSegment.Distal), (3, FingerSegment.Proximal) })
        {
            Assert.AreEqual(1f, reference.Part(other.Item1, other.Item2).Bone, 1e-6f, "the other phalanges are untouched");
        }

        foreach (var shape in Enum.GetValues<HandShape>())
        {
            hand.SetHandShape(shape);
            var now = hand.HandState!.Capture(hand.Body.Grid);
            Assert.AreEqual(proximal.Bone, now.Part(0, FingerSegment.Proximal).Bone, 1e-5f, $"{shape}: same index proximal phalanx");
            Assert.AreEqual(1f, now.Part(1, FingerSegment.Proximal).Bone, 1e-6f, $"{shape}: the middle finger is still whole");
            Assert.AreEqual(reference.FingerBone(0), now.FingerBone(0), 1e-5f, shape.ToString());
        }
    }

    [TestMethod]
    public void OneMissingBoneCell_BreaksTheSegment_BecauseBoneCarriesForceInSeries()
    {
        using var world = new PhysicsWorld();
        var hand = NewHand(world);
        hand.Body.Grid.Break(BoneCell(hand, 2, FingerSegment.Proximal, 1), []);
        var now = hand.HandState!.Capture(hand.Body.Grid);
        Assert.AreEqual(0f, now.Part(2, FingerSegment.Proximal).BoneWeakest, 1e-6f, "the chain is cut");
        Assert.IsGreaterThan(0.5f, now.Part(2, FingerSegment.Proximal).Bone, "most of the bone is still there (the mean hides the break)");
        Assert.AreEqual(1f, now.Part(2, FingerSegment.Middle).BoneWeakest, 1e-6f);
    }
}
