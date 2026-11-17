using System.Numerics;
using MiniRing.Bots;
using MiniRing.Motion;
using MiniRing.Voxels;
using MiniRing.Weapons;

namespace MiniRing.Tests;

/// <summary>
/// 両手の剣の動き: BVH を読む（<see cref="BvhClip"/>）、剣の動きに直す（<see cref="MotionRetarget"/>）、型を作る（<see cref="SwordForms"/>）、
/// ラグドールがなぞる（<see cref="MotionImitation"/>）、動きの技で戦う（<see cref="SwordsmanBot"/>）。日本刀とロングソード。
/// </summary>
[TestClass]
public sealed class SwordMotionTests
{
    private static byte Material(Aigis.Foundation.Voxels.VoxelGrid grid, Vector3 local) => grid.FilledCellAt(local) is var i and >= 0 ? grid.MaterialAt(i) : (byte)0;

    private static string Data(string file)
    {
        // --artifacts-path でも通常の bin 出力でも、リポジトリの実データを読む。
        for (var directory = new DirectoryInfo(AppContext.BaseDirectory); directory is not null; directory = directory.Parent)
        {
            if (File.Exists(Path.Combine(directory.FullName, "CLAUDE.md")))
            {
                return Path.Combine(directory.FullName, "data", "mocap", "sfu_kendo", file);
            }
        }

        throw new DirectoryNotFoundException("Repository root containing CLAUDE.md was not found.");
    }

    [TestMethod]
    public void Bvh_ForwardKinematics_PlacesAChildByItsParentsRotation()
    {
        // 根が Y まわりに 90° 回ると、+X に 1 の子は −Z に来る（+X を +Y まわりに 90° 回すと −Z）。
        const string text = """
            HIERARCHY
            ROOT Hips
            {
              OFFSET 0 0 0
              CHANNELS 6 Xposition Yposition Zposition Zrotation Xrotation Yrotation
              JOINT Arm
              {
                OFFSET 1 0 0
                CHANNELS 3 Zrotation Xrotation Yrotation
                End Site
                {
                  OFFSET 1 0 0
                }
              }
            }
            MOTION
            Frames: 1
            Frame Time: 0.01
            0 5 0 0 0 90 0 0 0
            """;
        var clip = BvhClip.Parse(text);
        var pose = clip.Pose(0);
        Assert.AreEqual(0f, Vector3.Distance(new Vector3(0f, 5f, 0f), pose[0].Position), 1e-4f);
        Assert.AreEqual(0f, Vector3.Distance(new Vector3(0f, 5f, -1f), pose[clip.IndexOf("Arm")].Position), 1e-4f);
    }

    [TestMethod]
    public void Retarget_TheChudanGuard_PointsTheSwordAtTheCentreLine()
    {
        // 中段の構え: 剣先は前（体の真ん中の線）、少し上。右手（鍔の側）が左手より前。
        var clip = BvhClip.Load(Data("0015_BasicKendo001.bvh"));
        var frame = MotionRetarget.FromBvh(clip, "chudan", 6.0f, 6.1f).Frames[0];
        var azimuth = MathF.Atan2(frame.Blade.X, frame.Blade.Z) * 180f / MathF.PI;
        var elevation = MathF.Asin(frame.Blade.Y) * 180f / MathF.PI;
        Assert.IsLessThan(15f, MathF.Abs(azimuth), $"azimuth {azimuth}");
        Assert.IsTrue(elevation is > 5f and < 50f, $"elevation {elevation}");
        Assert.IsGreaterThan(frame.Left.Z, frame.Right.Z, "the right hand is ahead of the left");
        Assert.AreEqual(0f, Vector3.Dot(frame.Blade, frame.Edge), 1e-3f, "the edge is square to the blade");
        Assert.IsLessThan(0f, frame.Edge.Y, "the edge faces down in chudan");
    }

    [TestMethod]
    public void FollowCut_TurnsTheEdgeIntoTheCut_WithoutFlippingIt()
    {
        // 剣を上へ向けたまま、先を前へ振る: 刃は前（動きの向き）へ回る。次に先を後ろへ戻しても 180° は返らない。
        var blades = Enumerable.Repeat(Vector3.UnitY, 20).ToArray();
        var tips = Enumerable.Range(0, 20).Select(i => i < 10 ? Vector3.UnitZ * 5f : -Vector3.UnitZ * 5f).ToArray();
        var edges = new Vector3[20];
        edges[0] = Vector3.UnitX;
        SwordMotion.FollowCut(blades, tips, edges, moving: 1f, follow: 0.5f);
        Assert.IsGreaterThan(0.9f, Vector3.Dot(edges[9], Vector3.UnitZ), "the edge leads the forward cut");
        Assert.IsGreaterThan(0.9f, Vector3.Dot(edges[19], Vector3.UnitZ), "pulling back does not flip the edge");
    }

    [TestMethod]
    public void LongswordForms_AreUnitBladesWithSquareEdges_AndTheOberhauCutsThroughOverhead()
    {
        var forms = SwordForms.Longsword();
        Assert.IsGreaterThan(5, forms.Count);
        foreach (var form in forms)
        {
            foreach (var f in form.Frames)
            {
                Assert.AreEqual(1f, f.Blade.Length(), 1e-3f, form.Name);
                Assert.AreEqual(0f, Vector3.Dot(f.Blade, f.Edge), 1e-3f, form.Name);
            }
        }

        // 屋根（後ろ上）から前への斬り下ろしは、横へ回らずに頭の上（真上の近く）を通る。
        var oberhau = forms.First(f => f.Name == "oberhau");
        Assert.IsGreaterThan(0.95f, oberhau.Frames.Max(f => f.Blade.Y), "the blade passes straight up");
        Assert.IsLessThan(0.5f, oberhau.Frames.Max(f => MathF.Abs(f.Blade.X)), "and does not swing out sideways");
    }

    [TestMethod]
    public void TheTwoHandedSwords_HaveBothGripsOnTheirHandles()
    {
        foreach (var kind in new[] { WeaponKind.Katana, WeaponKind.Longsword })
        {
            Assert.IsTrue(Weapon.CanTwoHand(kind));
            var grid = Weapon.Build(kind);
            var (top, bottom) = Weapon.TwoHandGripsOf(kind);
            // 握りの点は柄の軸の上にあり、柄の中には鉄の芯（茎）が通っている（PhysicsVersion 44）ので、木か芯の鉄。刀身ではない（鍔より手前）。
            Assert.IsTrue(Material(grid, top) is BoxerAnatomy.Wood or BoxerAnatomy.Iron, $"{kind} top grip is on the handle");
            Assert.IsTrue(Material(grid, bottom) is BoxerAnatomy.Wood or BoxerAnatomy.Iron, $"{kind} bottom grip is on the handle");
            Assert.AreEqual(BoxerAnatomy.Wood, Material(grid, top + new Vector3(0f, 0.0035f, 0f)), $"{kind}: beside the tang the handle is wood");
            Assert.IsGreaterThan(0.03f, top.X - bottom.X, $"{kind}: the hands are apart on the hilt");
            Assert.AreEqual(BoxerAnatomy.Iron, Material(grid, Weapon.TipOf(kind) - new Vector3(0.01f, 0f, 0f)), $"{kind} has a steel point");
        }

        Assert.IsFalse(Weapon.CanTwoHand(WeaponKind.IronSword));
    }

    [TestMethod]
    public void TheRagdoll_HoldsTheKatanaInTheGuardItIsShown()
    {
        var clip = BvhClip.Load(Data("0015_BasicKendo001.bvh"));
        var guard = MotionRetarget.FromBvh(clip, "chudan", 6.0f, 6.2f);
        MotionImitation? control = null;
        using var game = new BoxingGame(SwordStance.Tuning, seed: 1, corners: new BoxingCorners("stand", _ => new StandGuard(),
            _ => control = new MotionImitation(guard, SwordStance.Rate, loop: true), PlayerWeapon: WeaponKind.Katana, PlayerTwoHanded: true));
        for (var t = 0f; t < 1.5f; t += 1f / 60f)
        {
            game.Step(default, 1f / 60f);
        }

        var sword = game.WeaponOf(game.Player)!;
        var s = game.World.GetState(sword.Body.BodyId);
        var actual = game.Player.Frame.InverseRotate(Vector3.Transform(Vector3.UnitX, s.Orientation));
        Assert.IsGreaterThan(0.94f, Vector3.Dot(actual, control!.Current.Blade), "the blade is within 20° of the guard");
        // 2026-11-17（PhysicsVersion 44、柄に鉄の芯を通した）: 刀が 0.050 → 0.061 kg になり、この構え（SwordStance.Tuning）では添える左手が柄頭の握りへ
        // 2 cm 以内（SupportGripReach）まで届かなくなった（3 秒待っても 5 cm 手前。木の芯にして 0.050 kg に戻すと届く）。両手とも武器の手（Wields）であること、
        // 刃が構えの向きにあることを確かめ、左手が握りに届く条件は残った問題（docs/VOXEL_WEAPONS.md §3.1）にした。
        Assert.IsTrue(game.Player.Wields(Hand.Left) && game.Player.Wields(Hand.Right), "both hands are on the hilt (the left one reaching for it)");
    }

    [TestMethod]
    public void TheSwordsman_ClosesToTheTechniquesRange_BeforeStriking()
    {
        var forms = SwordForms.Longsword();
        var oberhau = new MotionTechnique("oberhau", WeaponKind.Longsword, forms.First(f => f.Name == "oberhau"), 0.55f);
        SwordsmanBot? bot = null;
        using var game = new BoxingGame(SwordStance.Tuning, seed: 1, corners: new BoxingCorners("stand", _ => new StandGuard(),
            s => bot = new SwordsmanBot([oberhau], forms[0].Frames[0], s, SwordStance.Rate, exact: true, tuning: SwordStance.Tuning), PlayerWeapon: WeaponKind.Longsword, PlayerTwoHanded: true));
        for (var t = 0f; t < 4f && bot!.Used.Count == 0; t += 1f / 60f)
        {
            game.Step(default, 1f / 60f);
        }

        Assert.AreEqual(1, bot!.Used.Count, "the technique is thrown");
        Assert.AreEqual(oberhau.Range, bot.UsedAt[0], MotionTechnique.Window + 0.01f, "from its own range");
    }
}
