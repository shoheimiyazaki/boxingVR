using System.Numerics;
using Aigis.Foundation.Voxels;
using Aigis.Physics;
using Aigis.Render.Voxels;
using MiniRing.Bots;
using MiniRing.Voxels;
using MiniRing.Weapons;

namespace MiniRing.Tests;

/// <summary>
/// 速くするための近道が、答えを変えないこと（docs/PERFORMANCE.md）: 覚えておいた答え・差分だけの書き直し・キャッシュは、作り直した物と同じで、
/// 時間の計測（<see cref="StepProfile"/>）は試合の結果に響かない。前の版との結果の一致は <c>miniring-arena perf</c> のチェックサムで確かめた（文書に記録）。
/// </summary>
[TestClass]
[TestCategory("L0")]
[TestCategory("slow")]
public sealed class PerformanceTests
{
    private static BoxingGame StandingGame()
    {
        var game = new BoxingGame(FighterTuning.Default, 1, new BoxingCorners("stand", _ => new StandGuard(), _ => new StandGuard()));
        for (var i = 0; i < 101; i++)
        {
            game.Step(default, 1f / 60f);
        }

        return game;
    }

    private static void Tilt(BoxingGame game, float degrees)
    {
        var p = game.Cpu.Parts;
        var chest = game.World.GetState(p.Chest);
        var joint = chest.Position + (Vector3.Transform(Vector3.UnitY, chest.Orientation) * game.Cpu.HalfExtentsOf(p.Chest).Y);
        var q = Quaternion.CreateFromAxisAngle(game.Cpu.Frame.Right, degrees * MathF.PI / 180f);
        var head = game.World.GetState(p.Head);
        game.World.SetDynamicPose(p.Head, joint + Vector3.Transform(head.Position - joint, q), Quaternion.Concatenate(head.Orientation, q));
    }

    private static byte[] Cells(VoxelGrid grid) => [.. Enumerable.Range(0, grid.CellCount).Select(grid.MaterialAt)];

    /// <summary>
    /// 関節のつなぎは、同じ入力（両側の格子と相対の姿勢）なら前の答えを使い、親の側の答えは親の格子が変わるまで使う。何度も作り直した後のつなぎは、
    /// 同じ時に新しく作ったつなぎと同じセルを持つ（姿勢が変わった後も、親・子の格子が削れた後も）。描くためのつなぎも同じ。
    /// </summary>
    [TestMethod]
    public void AReusedJointBridge_MatchesAFreshOne_AfterPoseAndDamageChanges()
    {
        using var game = StandingGame();
        var neck = game.Cpu.Voxels!.Bridges.Single(b => b.Joint == HumanoidRagdoll.RagdollJoint.Neck);
        var anchor = game.Cpu.BridgeJoints().Single(j => j.Joint == HumanoidRagdoll.RagdollJoint.Neck).AnchorOnParent;
        JointBridge Fresh() => new(neck.Joint, neck.Parent, neck.Child, anchor, neck.ParentLayers, neck.ChildLayers);

        void Check(string when)
        {
            neck.Refresh(_ => true);
            neck.Refresh(_ => true); // 2 度目は覚えた答えから
            var fresh = Fresh();
            fresh.Refresh(_ => true);
            CollectionAssert.AreEqual(Cells(fresh.Body.Grid), Cells(neck.Body.Grid), $"{when}: the contact bridge");
            Assert.AreEqual(fresh.ActiveCells, neck.ActiveCells, $"{when}: active cells");
            CollectionAssert.AreEqual(Cells(fresh.View().Body.Grid), Cells(neck.View().Body.Grid), $"{when}: the drawn bridge");
        }

        Tilt(game, 20f);
        Check("tilted 20°");
        Assert.IsGreaterThan(10, neck.ActiveCells, "the test looks at a real gap");
        Tilt(game, 15f);
        Check("tilted on to 35°");

        // 親（胸）と子（頭）の、つなぎに近いセルを壊す（格子の形が変わる: 覚えた答えは使えない）。
        foreach (var part in new[] { neck.Parent, neck.Child })
        {
            var grid = part.Body.Grid;
            var near = Enumerable.Range(0, grid.CellCount).Where(i => grid.MaterialAt(i) != 0)
                .OrderBy(i => Vector3.Distance(Vector3.Transform(grid.CellCenter(grid.Coordinates(i).X, grid.Coordinates(i).Y, grid.Coordinates(i).Z), part.Body.GridToWorld()), Vector3.Transform(Vector3.Zero, neck.Body.GridToWorld())))
                .Take(12).ToList();
            foreach (var i in near)
            {
                grid.Break(i, new List<RemovedVoxel>());
            }
        }

        Check("after cells near the joint broke");
    }

    /// <summary>格子の一部が変わった後の 3D テクスチャは、変わったセルだけを書き直しても、全部を書き直したのと同じで、送る範囲は変わったセルを囲む。</summary>
    [TestMethod]
    public void AVoxelVolume_RewritesOnlyChangedCells_ToTheSameTexels()
    {
        var grid = new VoxelGrid(BoxerAnatomy.Palette, new Vector3(0.05634f), (8, 8, 8), (_, n) => n.Length() < 0.9f ? BoxerAnatomy.Muscle : BoxerAnatomy.Skin);
        var volume = VoxelVolume.For(grid);
        var before = volume.Revision;
        var texels = volume.Rgba.ToArray();

        grid.Break(grid.Index(4, 4, 7), new List<RemovedVoxel>(), neighbourStain: 0.5f); // 壊したセルと、まわりの染み
        volume.Sync(grid);
        var full = VoxelVolume.For(grid);
        CollectionAssert.AreEqual(full.Rgba, volume.Rgba, "the same texels as a full rewrite");
        Assert.AreEqual(before + 1, volume.Revision);
        var bounds = volume.GetUploadBounds(before);
        for (var cell = 0; cell < grid.CellCount; cell++)
        {
            if (!volume.Rgba.AsSpan(cell * 4, 4).SequenceEqual(texels.AsSpan(cell * 4, 4)))
            {
                var (x, y, z) = grid.Coordinates(cell);
                Assert.IsTrue(x >= bounds.X && x < bounds.X + bounds.Width && y >= bounds.Y && y < bounds.Y + bounds.Height && z >= bounds.Z && z < bounds.Z + bounds.Depth,
                    $"changed cell ({x}, {y}, {z}) lies in the upload bounds {bounds}");
            }
        }

        Assert.IsLessThan(grid.CellCount, bounds.Width * bounds.Height * bounds.Depth, "only the changed corner is sent");
        var revision = volume.Revision;
        grid.SetCellState(grid.Index(0, 0, 0), grid.CellState(grid.Index(0, 0, 0)));
        volume.Sync(grid);
        Assert.AreEqual(revision, volume.Revision, "a new version with no visible change sends nothing");
    }

    /// <summary>埋まったセルを囲む箱は、形の版が変わるまで覚えておき、セルが減れば作り直す。</summary>
    [TestMethod]
    public void TheOccupiedBounds_FollowTheCells()
    {
        var grid = new VoxelGrid(BoxerAnatomy.Palette, new Vector3(0.05634f), (8, 8, 8), (_, _) => BoxerAnatomy.Muscle);
        var whole = grid.OccupiedBounds()!.Value;
        Assert.AreEqual(whole, grid.OccupiedBounds()!.Value);
        for (var y = 0; y < 8; y++)
        {
            for (var z = 0; z < 8; z++)
            {
                grid.Break(grid.Index(7, y, z), new List<RemovedVoxel>());
            }
        }

        var cut = grid.OccupiedBounds()!.Value;
        Assert.AreEqual(whole.HalfExtents.X - (grid.CellSize.X / 2f), cut.HalfExtents.X, 1e-6f, "one column of cells gone: the box is a cell shorter");
        Assert.AreEqual(whole.Center.X - (grid.CellSize.X / 2f), cut.Center.X, 1e-6f);
        Assert.AreEqual(whole.HalfExtents.Y, cut.HalfExtents.Y, 1e-6f);
    }

    /// <summary>時間の計測（<see cref="StepProfile"/>、数える量と割り当てを含む）は、刀の試合の結果を変えない。</summary>
    [TestMethod]
    public void Profiling_DoesNotChangeAKatanaMatch()
    {
        var corners = BoxingCorners.Default with
        {
            Player = SwordBot.Basic, Cpu = SwordBot.Basic,
            PlayerWeapon = WeaponKind.Katana, CpuWeapon = WeaponKind.Katana,
            PlayerTwoHanded = true, CpuTwoHanded = true,
        };
        ulong Run(bool profile)
        {
            using var game = new BoxingGame(seed: 3, corners: corners);
            for (var i = 0; i < 240; i++)
            {
                if (profile)
                {
                    StepProfile.Enabled = true;
                    StepProfile.TrackAllocations = true;
                    StepProfile.Reset();
                }

                try
                {
                    game.Step(default, 1f / 60f);
                }
                finally
                {
                    StepProfile.Enabled = false;
                    StepProfile.TrackAllocations = false;
                }
            }

            return game.StateChecksum();
        }

        Assert.AreEqual(Run(profile: false), Run(profile: true));
    }
}
