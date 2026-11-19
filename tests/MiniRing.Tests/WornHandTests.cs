using System.Numerics;
using Aigis.Foundation.Voxels;
using Aigis.Physics;
using MiniRing.Voxels;
using MiniRing.Weapons;

namespace MiniRing.Tests;

/// <summary>
/// はめる物（メリケンサック・カタール・籠手・トンファー・盾）の中の拳は、物の格子の球ではなく、同じ剛体に重ねた手の格子
/// （5 mm、素手と同じ手のモデル）。質量は 2 つの格子の材質の質量の和、手と物のセルは重ならず、腕の先の握る所は手首、打つ所は手の前にある。
/// </summary>
[TestClass]
public sealed class WornHandTests
{
    private static readonly WeaponKind[] Kinds = [WeaponKind.KnuckleDuster, WeaponKind.Katar, WeaponKind.Gauntlet, WeaponKind.Tonfa, WeaponKind.Shield];

    private static float MassOf(VoxelGrid grid)
    {
        var mass = 0f;
        for (var i = 0; i < grid.CellCount; i++)
        {
            if (grid.MaterialAt(i) != 0)
            {
                mass += grid.Palette[grid.MaterialAt(i)].MechanicsOrDefault.Density * grid.CellVolume;
            }
        }

        return mass;
    }

    private static Weapon Make(PhysicsWorld world, WeaponKind kind, bool mirrored = false)
        => Weapon.CreateWorn(world, kind, BoxerAnatomy.RedGlove, Vector3.Zero, Quaternion.Identity, mirrored);

    [TestMethod]
    public void EveryWornThing_CarriesAHandGrid_AndTheMassIsTheSumOfBothGrids()
    {
        foreach (var kind in Kinds)
        {
            using var world = new PhysicsWorld(gravity: Vector3.Zero);
            var weapon = Make(world, kind);

            Assert.HasCount(1, weapon.Hands, $"{kind}");
            var hand = weapon.Hands[0];
            Assert.AreEqual(weapon.Body.BodyId, hand.Body.BodyId, $"{kind}: one rigid body");
            Assert.AreEqual(HandSkeleton.Parents.Count, hand.Body.Grid.FilledCount, $"{kind}: the bare hand's cells (5 mm)");
            Assert.AreEqual(5f, hand.Body.Grid.CellSize.X * 1000f, 1e-3f, $"{kind}: the hand keeps its 5 mm cells");
            var expected = MassOf(weapon.Body.Grid) + MassOf(hand.Body.Grid);
            Assert.AreEqual(expected, world.GetMass(weapon.Body.BodyId), expected * 1e-4f, $"{kind}: mass = object + hand, from the materials");
            Assert.IsGreaterThan(MassOf(weapon.Body.Grid), world.GetMass(weapon.Body.BodyId), $"{kind}: the hand weighs something");
        }
    }

    [TestMethod]
    public void TheObjectCells_NeverOverlapTheHandCells_AndTheGripIsTheWrist()
    {
        foreach (var kind in Kinds)
        {
            foreach (var mirrored in kind == WeaponKind.Tonfa ? new[] { false, true } : [false])
            {
                using var world = new PhysicsWorld(gravity: Vector3.Zero);
                var weapon = Make(world, kind, mirrored);
                var hand = weapon.Hands[0];
                var objectGrid = weapon.Body.Grid;
                var overlap = 0;
                for (var i = 0; i < objectGrid.CellCount; i++)
                {
                    if (objectGrid.MaterialAt(i) == 0)
                    {
                        continue;
                    }

                    var (x, y, z) = objectGrid.Coordinates(i);
                    // 物の格子のローカル → 手の格子のローカル。
                    if (hand.Body.Grid.FilledCellAt(objectGrid.CellCenter(x, y, z) - hand.GridCenterLocal) >= 0)
                    {
                        overlap++;
                    }
                }

                Assert.AreEqual(0, overlap, $"{kind} (mirrored {mirrored}): the thing leaves the hand's cells alone");
                Assert.AreEqual(HandSkeleton.Wrist + hand.GridCenterLocal, Weapon.GripOf(kind, mirrored), "the arm holds the hand at its wrist");
            }
        }
    }

    [TestMethod]
    public void TheStrikingPoint_IsAheadOfTheHand_OnASolidFace()
    {
        foreach (var kind in Kinds)
        {
            using var world = new PhysicsWorld(gravity: Vector3.Zero);
            var weapon = Make(world, kind);
            var hand = weapon.Hands[0];
            var bounds = hand.Body.Grid.OccupiedBounds()!.Value;
            var handFront = bounds.Center.X + bounds.HalfExtents.X;
            var tipInHand = Weapon.TipOf(kind) - hand.GridCenterLocal; // 手の座標

            Assert.IsGreaterThanOrEqualTo(handFront - 1e-4f, tipInHand.X, $"{kind}: the point that strikes is not behind the knuckles");
            Assert.IsTrue(Weapon.HasRigidFace(kind), $"{kind}: iron or wood at the striking point");
        }
    }

    [TestMethod]
    public void TheGrippedBar_PassesThroughTheHandsLoop_WithoutTakingTheHandsCells()
    {
        // 棒を握る物（カタール・トンファー・盾）の手は握る形（GripPose）で、棒の通る所には手の組織が無い。
        foreach (var kind in new[] { WeaponKind.Katar, WeaponKind.Tonfa, WeaponKind.Shield })
        {
            using var world = new PhysicsWorld(gravity: Vector3.Zero);
            var weapon = Make(world, kind);
            var hand = weapon.Hands[0];
            var axis = HandSkeleton.GripCenter + hand.GridCenterLocal; // 物の格子のローカル（Z に沿う棒の軸）
            var onAxis = 0;
            var handOnAxis = 0;
            for (var k = -6; k <= 6; k++)
            {
                var along = new Vector3(axis.X, axis.Y, axis.Z + (0.002f * k));
                onAxis += weapon.Body.Grid.FilledCellAt(along) >= 0 ? 1 : 0;
                handOnAxis += hand.Body.Grid.FilledCellAt(along - hand.GridCenterLocal) >= 0 ? 1 : 0;
            }

            Assert.IsGreaterThan(8, onAxis, $"{kind}: the bar is there (iron or wood), axis {axis} on {onAxis} dims {weapon.Body.Grid.Dimensions} cell {weapon.Body.Grid.CellSize} gridlocal {weapon.Body.GridCenterLocal}");
            Assert.AreEqual(0, handOnAxis, $"{kind}: the hand's tissue goes around the bar");
        }
    }
}
