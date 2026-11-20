using System.Numerics;
using Aigis.Foundation.Voxels;
using Aigis.Physics.Voxels;
using MiniRing.Weapons;

namespace MiniRing.Tests;

/// <summary>
/// 削られて途中のセルが無くなった武器は、つながっていない部分が別の剛体として分かれる
/// （<see cref="VoxelBody.SeverDisconnected"/>、<c>BoxingGame.SplitDisconnectedWeapons</c>）。
/// </summary>
[TestClass]
public sealed class WeaponSplitTests
{
    private const float TableTop = 2.817f;

    /// <summary>卓の +X 側の縁の x（物理の空間の部屋の床 x -7.6〜7.6、z -5.6〜12.4 の内側に置く）。</summary>
    private const float TableEdge = 5.634f;

    /// <summary>卓の縁（+X 側）から棍棒の頭が突き出して載っている状態を作る。棍棒の重心は卓の上にあり、動かずに止まる。</summary>
    private static (BoxingGame Game, Weapon Club, Vector3 Origin) ClubOnTableEdge()
    {
        var game = new BoxingGame(seed: 1);
        var table = new Vector3(4.225f, TableTop - 1.408f, 8.451f);
        game.World.AddStaticBox(table, new Vector3(1.408f, 1.408f, 1.408f));
        var origin = new Vector3(table.X + 1.408f - 0.0845f, TableTop + 0.05634f, table.Z);
        var club = game.AddWeapon(WeaponKind.WoodenClub, origin, Quaternion.Identity);
        for (var i = 0; i < 403; i++)
        {
            game.Step(default, 1f / 240f);
        }

        return (game, club, origin);
    }

    /// <summary>棍棒の格子の、棒に沿った（X）スライス <paramref name="from"/>〜<paramref name="to"/> のセルを消す。<paramref name="keepColumn"/> の（Y, Z）の 1 列だけは残す。</summary>
    private static void CarveBand(VoxelBody body, int from, int to, (int Y, int Z)? keepColumn = null)
    {
        var grid = body.Grid;
        var removed = new List<RemovedVoxel>();
        for (var z = 0; z < grid.Dimensions.Z; z++)
        {
            for (var y = 0; y < grid.Dimensions.Y; y++)
            {
                if (keepColumn is { } k && (k.Y, k.Z) == (y, z))
                {
                    continue;
                }

                for (var x = from; x <= to; x++)
                {
                    grid.Break(grid.Index(x, y, z), removed);
                }
            }
        }

        body.UpdateMass();
    }

    private static int SliceOfWorldX(VoxelBody body, float worldX)
    {
        var state = body.World.GetState(body.BodyId);
        var local = worldX - state.Position.X - body.GridCenterLocal.X;
        return (int)MathF.Floor((local + body.Grid.HalfExtents.X) / body.Grid.CellSize.X);
    }

    [TestMethod]
    public void CarvedThroughTheMiddle_TheTipIsAnotherBody_AndFalls()
    {
        var (game, club, _) = ClubOnTableEdge();
        using var _ = game;
        var count = game.Weapons.Count;
        var stump = club.Body;
        var massBefore = game.World.GetMass(stump.BodyId);
        var stumpY = game.World.GetState(stump.BodyId).Position.Y;
        var edge = TableEdge;
        // 卓の縁から 2〜4 cm 外の断面（頭の突き出した所）を丸ごと消す: 先はつながりを失う。
        CarveBand(stump, SliceOfWorldX(stump, edge + 0.05634f), SliceOfWorldX(stump, edge + 0.09859f));
        game.Step(default, 1f / 240f);

        Assert.AreEqual(count + 1, game.Weapons.Count, "the club split into two bodies");
        var tip = game.Weapons[^1];
        Assert.AreNotEqual(stump.BodyId, tip.Body.BodyId);
        Assert.AreEqual(WeaponKind.WoodenClub, tip.Kind, "a piece of the same club");
        Assert.AreEqual(1, stump.Grid.ConnectedComponents().Count, "what stays is one piece");
        Assert.AreEqual(1, tip.Body.Grid.ConnectedComponents().Count, "what broke off is one piece");
        Assert.AreEqual(massBefore, game.World.GetMass(stump.BodyId) + game.World.GetMass(tip.Body.BodyId), massBefore * 0.35f, "the carved band took its mass; the two pieces share the rest");

        var tipStart = game.World.GetState(tip.Body.BodyId).Position.Y;
        for (var i = 0; i < 403; i++)
        {
            game.Step(default, 1f / 240f);
        }

        Assert.IsLessThan(tipStart - 0.8451f, game.World.GetState(tip.Body.BodyId).Position.Y, "the tip fell off the table edge (it did not hang in the air)");
        Assert.AreEqual(stumpY, game.World.GetState(stump.BodyId).Position.Y, 0.02817f, "the stump stayed on the table");
    }

    [TestMethod]
    public void CarvedThroughOnASlant_TheTipStillSplits()
    {
        var (game, club, _) = ClubOnTableEdge();
        using var _ = game;
        var count = game.Weapons.Count;
        var stump = club.Body;
        var from = SliceOfWorldX(stump, TableEdge + 0.04225f);
        // 斜めの切り口: 高さ（Y のセル）が上がるほど切る断面が先へずれる（どの断面も消すのは 2 セルぶんの幅の斜めの帯）。
        var grid = stump.Grid;
        var removed = new List<RemovedVoxel>();
        for (var z = 0; z < grid.Dimensions.Z; z++)
        {
            for (var y = 0; y < grid.Dimensions.Y; y++)
            {
                var start = from + (y / 2);
                for (var x = start; x < start + 3; x++)
                {
                    grid.Break(grid.Index(Math.Clamp(x, 0, grid.Dimensions.X - 1), y, z), removed);
                }
            }
        }

        stump.UpdateMass();
        game.Step(default, 1f / 240f);

        Assert.AreEqual(count + 1, game.Weapons.Count, "a slanted cut splits it too (connected pieces, not a slice of an axis)");
    }

    [TestMethod]
    public void OneColumnLeftConnected_DoesNotSplit()
    {
        var (game, club, _) = ClubOnTableEdge();
        using var _ = game;
        var count = game.Weapons.Count;
        var body = club.Body;
        var (cy, cz) = (body.Grid.Dimensions.Y / 2, body.Grid.Dimensions.Z / 2);
        CarveBand(body, SliceOfWorldX(body, TableEdge + 0.05634f), SliceOfWorldX(body, TableEdge + 0.09859f), keepColumn: (cy, cz));
        Assert.AreEqual(1, body.Grid.ConnectedComponents().Count, "the one column joins the tip to the rest");
        game.Step(default, 1f / 240f);
        Assert.AreEqual(count, game.Weapons.Count, "a single column of cells still connecting keeps it one body");
    }

    [TestMethod]
    public void TheBrokenOffTip_IsAWeaponOfTheSameMaterial_ThatHitsTheGround()
    {
        var (game, club, _) = ClubOnTableEdge();
        using var _ = game;
        var stump = club.Body;
        CarveBand(stump, SliceOfWorldX(stump, TableEdge + 0.05634f), SliceOfWorldX(stump, TableEdge + 0.09859f));
        game.Step(default, 1f / 240f);
        var tip = game.Weapons[^1];
        Assert.AreEqual(tip.Body.Grid.Remaining(Voxels.BoxerAnatomy.Wood), tip.Body.Grid.FilledCount, "the piece is all wood, as the club is");
        for (var i = 0; i < 806; i++)
        {
            game.Step(default, 1f / 240f);
        }

        Assert.IsLessThan(TableTop - 1.408f, game.World.GetState(tip.Body.BodyId).Position.Y, "it landed below the table top and lies there (the floor and the room hold it)");
        Assert.IsGreaterThan(-1.408f, game.World.GetState(tip.Body.BodyId).Position.Y, $"and did not fall through the floor (y {game.World.GetState(tip.Body.BodyId).Position.Y})");
    }
}
