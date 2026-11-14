using System.Numerics;
using Aigis.Foundation.Voxels;
using Aigis.Physics;
using MiniRing.Voxels;
using MiniRing.Weapons;

namespace MiniRing.Tests;

/// <summary>指ごと・節ごとの親データ: どの手形で削れても、同じ指・同じ節の欠けとして数えること。</summary>
[TestClass]
public sealed class FingerStateTests
{
    [TestMethod]
    public void EveryCell_BelongsToExactlyOneFingerSegment_AndFourFingersHaveBone()
    {
        var state = new BareHandState();
        var count = 0;
        for (var finger = 0; finger < BareHandState.FingerCount; finger++)
        {
            foreach (var segment in new[] { FingerSegment.Knuckle, FingerSegment.Folded })
            {
                var part = state.Part(finger, segment);
                count += part.Cells;
                Assert.AreEqual(1f, part.Bone, 1e-6f, "an unharmed hand has every bone");
                Assert.AreEqual(0f, part.Cracked, 1e-6f);
            }
        }

        count += state.Part(BareHandState.FingerCount, FingerSegment.Palm).Cells;
        Assert.AreEqual(state.Cells.Count, count, "no cell is left out or counted twice");
        Assert.IsGreaterThan(0, state.Part(0, FingerSegment.Knuckle).Cells, "the index knuckle leads");
        Assert.IsGreaterThan(0, state.Part(1, FingerSegment.Knuckle).Cells, "so does the middle knuckle");
    }

    [TestMethod]
    public void AKnuckleWornInOneShape_ShowsAsTheSameWornFingerInEveryOtherShape()
    {
        using var world = new PhysicsWorld();
        var hand = Weapon.CreateWorn(world, WeaponKind.Fist, BoxerAnatomy.RedGlove, Vector3.Zero, Quaternion.Identity);
        var grid = hand.Body.Grid;
        // 人さし指（z が最も手前の帯）の拳頭の骨を半分削る。
        var removed = new List<RemovedVoxel>();
        var worn = 0;
        for (var i = 0; i < grid.CellCount; i++)
        {
            if (grid.MaterialAt(i) != BoxerAnatomy.Bone)
            {
                continue;
            }

            var c = grid.Coordinates(i);
            var p = grid.CellCenter(c.X, c.Y, c.Z);
            if (p.Z < -0.007f && p.X > 0.012f)
            {
                grid.Wear(i, 0.5f, removed);
                worn++;
            }
        }

        Assert.IsGreaterThan(0, worn, "the index knuckle has bone to wear");
        var reference = hand.HandState!.Capture(grid);
        var knuckle = reference.Part(0, FingerSegment.Knuckle);
        Assert.IsLessThan(1f, knuckle.Bone);
        Assert.IsGreaterThan(0f, knuckle.Cracked);
        foreach (var other in new[] { 1, 2, 3 })
        {
            Assert.AreEqual(1f, reference.Part(other, FingerSegment.Knuckle).Bone, 1e-6f, "the other fingers are untouched");
        }

        foreach (var shape in Enum.GetValues<HandShape>())
        {
            hand.SetHandShape(shape);
            var now = hand.HandState!.Capture(hand.Body.Grid);
            Assert.AreEqual(knuckle.Bone, now.Part(0, FingerSegment.Knuckle).Bone, 1e-5f, $"{shape}: same index knuckle");
            Assert.AreEqual(1f, now.Part(1, FingerSegment.Knuckle).Bone, 1e-6f, $"{shape}: the middle knuckle is still whole");
            Assert.AreEqual(reference.FingerBone(0), now.FingerBone(0), 1e-5f, shape.ToString());
        }
    }
}
