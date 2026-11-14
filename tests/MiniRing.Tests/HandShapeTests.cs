using System.Numerics;
using Aigis.Foundation.Voxels;
using Aigis.Physics;
using MiniRing.Weapons;

namespace MiniRing.Tests;

[TestClass]
public sealed class HandShapeTests
{
    [TestMethod]
    public void GameInput_ChangesBareHandShapeWithoutHealingDamage()
    {
        var corners = BoxingCorners.Default with { PlayerWear = HandWear.None };
        using var game = new BoxingGame(corners: corners);
        var hand = game.GloveOf(game.Player, Hand.Left)!;
        var grid = hand.Body.Grid;
        var index = Enumerable.Range(0, grid.CellCount).First(i => grid.MaterialAt(i) != 0);
        grid.Break(index, new List<RemovedVoxel>());
        var remaining = grid.FilledCount;
        var body = hand.Body.BodyId;
        var command = new FighterCommand(Vector3.Zero, Vector3.UnitZ,
            new HandCommand(Vector3.Zero, false, Shape: HandShape.SpearHand),
            new HandCommand(Vector3.Zero, false));

        game.Step(new BoxingInput(command), 1f / 60f);

        Assert.AreEqual(HandShape.SpearHand, hand.HandState!.Shape);
        Assert.AreEqual(remaining, hand.Body.Grid.FilledCount);
        Assert.AreEqual(body, hand.Body.BodyId);
    }

    [TestMethod]
    public void ShapeRoundTrip_PreservesTheLocationAndAmountOfDifferentDamage()
    {
        using var world = new PhysicsWorld();
        var hand = Weapon.CreateWorn(world, WeaponKind.Fist, Voxels.BoxerAnatomy.RedGlove, Vector3.Zero, Quaternion.Identity);
        var original = hand.Body.Grid;
        var skin = Enumerable.Range(0, original.CellCount).First(i => original.MaterialAt(i) == Voxels.BoxerAnatomy.Skin);
        var bone = Enumerable.Range(0, original.CellCount).First(i => original.MaterialAt(i) == Voxels.BoxerAnatomy.Bone);
        var tissue = Enumerable.Range(0, original.CellCount).Where(i => original.MaterialAt(i) != 0).ToArray();
        var removed = new List<RemovedVoxel>();
        original.Break(skin, removed);
        original.Wear(bone, 0.25f, removed);
        var expected = tissue.Select(original.CellState).ToArray();

        foreach (var shape in Enum.GetValues<HandShape>())
        {
            hand.SetHandShape(shape);
            hand.SetHandShape(HandShape.Seiken);
            var restored = hand.Body.Grid;
            CollectionAssert.AreEqual(expected, tissue.Select(restored.CellState).ToArray(), shape.ToString());
        }
    }

    [TestMethod]
    public void Shapes_PreserveCellsAndPartialWear_AndNeverHealBrokenCells()
    {
        using var world = new PhysicsWorld();
        var hand = Weapon.CreateWorn(world, WeaponKind.Fist, Voxels.BoxerAnatomy.RedGlove, Vector3.Zero, Quaternion.Identity);
        var grid = hand.Body.Grid;
        var index = Enumerable.Range(0, grid.CellCount).First(i => grid.MaterialAt(i) != 0);
        var removed = new List<RemovedVoxel>();
        grid.Wear(index, 0.25f, removed);
        var initial = grid.FilledCount;
        var worn = grid.TotalHealth;
        foreach (var shape in Enum.GetValues<HandShape>())
        {
            hand.SetHandShape(shape);
            Assert.AreEqual(initial, hand.Body.Grid.FilledCount, shape.ToString());
            Assert.AreEqual(worn, hand.Body.Grid.TotalHealth, 0.001f, shape.ToString());
        }

        grid = hand.Body.Grid;
        index = Enumerable.Range(0, grid.CellCount).First(i => grid.MaterialAt(i) != 0);
        grid.Break(index, removed);
        foreach (var shape in Enum.GetValues<HandShape>())
        {
            hand.SetHandShape(shape);
            Assert.AreEqual(initial - 1, hand.Body.Grid.FilledCount, shape.ToString());
        }
    }

    [TestMethod]
    public void FistPiece_PreservesItsExistingGridAndDamage()
    {
        using var world = new PhysicsWorld();
        var hand = Weapon.CreateWorn(world, WeaponKind.Fist, Voxels.BoxerAnatomy.RedGlove, Vector3.Zero, Quaternion.Identity);
        var grid = hand.Body.Grid;
        var index = Enumerable.Range(0, grid.CellCount).First(i => grid.MaterialAt(i) != 0);
        grid.Break(index, new List<RemovedVoxel>());
        var count = grid.FilledCount;

        var piece = Weapon.PieceOf(WeaponKind.Fist, hand.Body);

        Assert.AreSame(grid, piece.Body.Grid);
        Assert.AreEqual(count, piece.Body.Grid.FilledCount);
        Assert.IsNull(piece.HandState);
    }

    [TestMethod]
    public void Shapes_ChangeContactGeometry_AndRejectUnknownShape()
    {
        using var world = new PhysicsWorld();
        var hand = Weapon.CreateWorn(world, WeaponKind.Fist, Voxels.BoxerAnatomy.RedGlove, Vector3.Zero, Quaternion.Identity);
        var closed = hand.Body.Grid.OccupiedBounds()!.Value.HalfExtents;
        hand.SetHandShape(HandShape.SpearHand);
        Assert.IsGreaterThan(closed.X, hand.Body.Grid.OccupiedBounds()!.Value.HalfExtents.X);
        Assert.ThrowsExactly<ArgumentOutOfRangeException>(() => hand.SetHandShape((HandShape)999));
    }
}
