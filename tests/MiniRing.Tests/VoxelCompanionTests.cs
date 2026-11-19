using System.Numerics;
using Aigis.Foundation.Voxels;
using Aigis.Physics;
using Aigis.Physics.Voxels;
using MiniRing.Weapons;

namespace MiniRing.Tests;

/// <summary>
/// 1 つの剛体に複数の格子（<see cref="VoxelBody.AddCompanion"/>）: 質量は格子ごとの質量の和、当たりの箱は全ての格子を覆い、
/// 一方の格子への打撃がもう一方の寄与を消さず、切り離しは物の格子だけに効く。
/// </summary>
[TestClass]
public sealed class VoxelCompanionTests
{
    private const byte Steel = 1;
    private const byte Wood = 2;

    private static readonly VoxelPalette Palette = new(
    [
        new VoxelMaterial("empty", VoxelTissue.Empty, 1f, false, Vector4.Zero, Vector4.Zero),
        new VoxelMaterial("steel", VoxelTissue.Solid, 50f, false, Vector4.One, Vector4.One, Mechanics: new VoxelMechanics(7800f, 600e6f, 0.0002f, Tensile: 1.5e9f, Stiffness: 200e9f)),
        new VoxelMaterial("wood", VoxelTissue.Solid, 5f, false, Vector4.One, Vector4.One, Mechanics: new VoxelMechanics(650f, 8e6f, 0.001f, Tensile: 60e6f, Stiffness: 10e9f)),
    ]);

    private static readonly Vector3 SteelHalf = new(0.03f);
    private static readonly Vector3 WoodHalf = new(0.01f);
    private static readonly Vector3 WoodAt = new(0.08f, 0f, 0f);

    private static VoxelGrid Block(byte material, Vector3 half, float cell) => new(Palette, half, cell, (_, _) => material);

    private static float MassOf(VoxelGrid grid) => grid.FilledCount * grid.CellVolume * Palette[grid.MaterialAt(FirstFilled(grid))].Mechanics!.Density;

    private static int FirstFilled(VoxelGrid grid)
    {
        for (var i = 0; i < grid.CellCount; i++)
        {
            if (grid.MaterialAt(i) != 0)
            {
                return i;
            }
        }

        return 0;
    }

    /// <summary>鋼の箱（物。原点・5 mm のセル）に、木の小さな箱（手の代わり。+X 8 cm・2 mm のセル）を重ねた剛体。</summary>
    private static (VoxelBody Steel, VoxelBody Wood) Pair(PhysicsWorld world)
    {
        var steel = Block(Steel, SteelHalf, 0.005f);
        var id = world.AddDynamicBox(Vector3.Zero, SteelHalf, MassOf(steel));
        var body = new VoxelBody(world, id, steel, materialMass: true);
        var wood = body.AddCompanion(Block(Wood, WoodHalf, 0.002f), WoodAt);
        return (body, wood);
    }

    [TestMethod]
    public void TheMass_IsTheSumOverEveryGrid_AndTheBoxCoversThemAll()
    {
        using var world = new PhysicsWorld(gravity: Vector3.Zero);
        var (steel, wood) = Pair(world);

        Assert.AreEqual(steel.BodyId, wood.BodyId, "one rigid body");
        Assert.AreSame(steel, wood.Owner);
        Assert.AreEqual(MassOf(steel.Grid) + MassOf(wood.Grid), world.GetMass(steel.BodyId), 1e-4f);

        // 箱は鋼（−3 cm）から木（+9 cm）まで。両端を +X・−X から撃ったレイが当たる。
        var far = world.Raycast(new Vector3(1f, 0f, 0f), -Vector3.UnitX, 2f);
        var near = world.Raycast(new Vector3(-1f, 0f, 0f), Vector3.UnitX, 2f);
        Assert.AreEqual(WoodAt.X + WoodHalf.X, far!.Value.Point.X, 1e-3f, "the box reaches the far end of the companion");
        Assert.AreEqual(-SteelHalf.X, near!.Value.Point.X, 1e-3f, "and the owner's far side");
    }

    [TestMethod]
    public void ABlowOnOneGrid_KeepsTheOtherGridsMassAndReach()
    {
        using var world = new PhysicsWorld(gravity: Vector3.Zero);
        var (steel, wood) = Pair(world);
        var steelMass = MassOf(steel.Grid);
        var woodBefore = MassOf(wood.Grid);

        var chips = new List<VoxelChip>();
        var carved = wood.Impact(WoodAt + new Vector3(0.01f, 0f, 0f), -Vector3.UnitX, 40f, 2f, () => 0f, chips);

        Assert.IsGreaterThan(0, carved, "the blow carves the companion");
        var woodAfter = MassOf(wood.Grid);
        Assert.IsLessThan(woodBefore, woodAfter);
        Assert.AreEqual(steelMass + woodAfter, world.GetMass(steel.BodyId), 1e-4f, "the owner still counts in full");
        var near = world.Raycast(new Vector3(-1f, 0f, 0f), Vector3.UnitX, 2f);
        Assert.AreEqual(-SteelHalf.X, near!.Value.Point.X, 1e-3f, "the collider still covers the owner");

        var steelCarved = steel.Impact(new Vector3(0f, SteelHalf.Y, 0f), -Vector3.UnitY, 2e4f, 2f, () => 0f, chips);
        Assert.IsGreaterThan(0, steelCarved, "and a blow on the owner works as before");
        Assert.AreEqual(MassOf(steel.Grid) + woodAfter, world.GetMass(steel.BodyId), 1e-4f, "the companion keeps its share");
    }

    [TestMethod]
    public void OnlyTheOwnerGrid_IsSeveredOrSplit()
    {
        using var world = new PhysicsWorld(gravity: Vector3.Zero);
        var (steel, wood) = Pair(world);

        Assert.IsNull(wood.Sever(0, distalHigh: true));
        Assert.IsNull(wood.SplitAt(0, 2, takeHigh: true));
        Assert.AreEqual(0, wood.SeverDisconnected().Count);
        Assert.AreEqual(0, steel.SeverDisconnected().Count, "a whole block is one piece");
        Assert.AreEqual(1, steel.Companions.Count);
        Assert.AreEqual(0, wood.Companions.Count);

        // 物の格子を半分に割ると、物の半分だけが破片へ行き、手の質量は元の剛体に残る。
        var piece = steel.SplitAt(0, steel.Grid.Dimensions.X / 2, takeHigh: true);
        Assert.IsNotNull(piece);
        Assert.IsEmpty(piece.Companions, "the piece carries no companion");
        Assert.AreEqual(MassOf(piece.Grid), world.GetMass(piece.BodyId), 1e-3f);
        Assert.AreEqual(MassOf(steel.Grid) + MassOf(wood.Grid), world.GetMass(steel.BodyId), 1e-3f);
    }

    [TestMethod]
    public void AWeapon_WithoutHands_HasOnlyItsBodyAsAGrid_AndWithOne_ListsObjectFirst()
    {
        using var world = new PhysicsWorld(gravity: Vector3.Zero);
        var weapon = Weapon.Create(world, WeaponKind.IronSword, Vector3.Zero, Quaternion.Identity);
        var massAlone = world.GetMass(weapon.Body.BodyId);

        Assert.IsEmpty(weapon.Hands);
        Assert.HasCount(1, weapon.Grids);
        Assert.AreSame(weapon.Body, weapon.Grids[0]);
        var draws = new List<Aigis.Render.Voxels.VoxelVolumeDraw>();
        weapon.AddDraws(draws);
        Assert.HasCount(1, draws);

        var handGrid = Block(Wood, WoodHalf, 0.002f);
        var hand = weapon.AddHand(handGrid, new BareHandState(), new Vector3(-0.05f, 0f, 0f));

        Assert.HasCount(2, weapon.Grids);
        Assert.AreSame(weapon.Body, weapon.Grids[0], "the object comes first, then the hands in the order added");
        Assert.AreSame(hand.Body, weapon.Grids[1]);
        Assert.AreEqual(weapon.Body.BodyId, hand.Body.BodyId);
        Assert.AreEqual(massAlone + (handGrid.FilledCount * (massAlone / weapon.Body.Grid.FilledCount)), world.GetMass(weapon.Body.BodyId), massAlone * 1e-3f,
            "without a mass per cell the hand cells weigh as the object's cells do");
        draws.Clear();
        weapon.AddDraws(draws);
        Assert.HasCount(2, draws);
    }

    [TestMethod]
    public void ACompanion_CannotCarryCompanions()
    {
        using var world = new PhysicsWorld(gravity: Vector3.Zero);
        var (_, wood) = Pair(world);

        Assert.Throws<InvalidOperationException>(() => wood.AddCompanion(Block(Wood, WoodHalf, 0.002f), Vector3.Zero));
    }
}
