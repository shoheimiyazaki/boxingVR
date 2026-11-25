using System.Numerics;
using MiniRing.Motion;
using MiniRing.Voxels;
using MiniRing.Weapons;

namespace MiniRing.Tests;

/// <summary>
/// 2026-11-25（PhysicsVersion 100）: 武器を握る手は、素手と同じボクセルの握り拳（<see cref="GripHand"/>）。柄の上に置く位置の決め方。
/// </summary>
[TestClass]
public sealed class GripHandTests
{
    [TestMethod]
    public void TheFistSitsWithItsWristAtTheGrip_UnlessTheGuardIsInTheWay()
    {
        var span = Weapon.HandleSpanOf(WeaponKind.IronSword);
        var grip = Weapon.GripOf(WeaponKind.IronSword).X;
        var wrist = GripHand.Wrists(span, [grip])[0];
        Assert.IsLessThanOrEqualTo(span.Max + 1e-5f, wrist + GripHand.Length, "the fist stops behind the guard");
        Assert.IsLessThanOrEqualTo(grip, wrist, "the fist is behind (or at) the grip");
        Assert.IsGreaterThanOrEqualTo(grip, wrist + GripHand.Length, "and still holds the grip point");

        // 棒（鍔が無い）: 手首は握る所そのもの（素手の拳と同じく前腕の先に続く）。
        var staff = Weapon.HandleSpanOf(WeaponKind.Staff);
        Assert.AreEqual(0.1f, GripHand.Wrists(staff, [0.1f])[0], 1e-6f);
    }

    [TestMethod]
    public void TwoFistsOnOneHandle_DoNotOverlap_AndEachHoldsItsGrip()
    {
        foreach (var kind in new[] { WeaponKind.Katana, WeaponKind.Longsword, WeaponKind.WoodenClub, WeaponKind.Staff })
        {
            var (top, bottom) = Weapon.TwoHandGripsOf(kind);
            var span = Weapon.HandleSpanOf(kind);
            var wrists = GripHand.Wrists(span, [top.X, bottom.X]);
            var length = GripHand.Length;
            Assert.IsGreaterThanOrEqualTo(wrists[1] + length - 1e-5f, wrists[0], $"{kind}: the top fist is above the bottom fist");
            for (var i = 0; i < 2; i++)
            {
                var grip = i == 0 ? top.X : bottom.X;
                Assert.IsTrue(wrists[i] <= grip + 1e-5f && grip <= wrists[i] + length + 1e-5f, $"{kind}: fist {i} holds its grip");
                Assert.IsTrue(wrists[i] >= span.Min - 1e-5f && wrists[i] + length <= span.Max + 1e-5f, $"{kind}: fist {i} stays on the handle");
            }
        }
    }

    [TestMethod]
    public void TheFistIsAsHeavyAsTheBareFist()
    {
        // 握る拳の質量 = 素手の拳と同じ組織の、密度 × 体積（見た目と質量が同じ: CLAUDE.md §1.7）。
        _ = new BareHandState().Placed(GripHand.Pose, out var grip, GripHand.Held(Weapon.HandleRadiusAt(WeaponKind.IronSword, 0f)));
        var bare = Weapon.Build(WeaponKind.Fist);
        Assert.AreEqual(bare.FilledCount, grip.FilledCount, "the handle pushes tissue aside, it does not remove any");
        Assert.AreEqual(Weapon.MassOf(WeaponKind.Fist), grip.FilledCount * GripHand.MassPerCell, Weapon.MassOf(WeaponKind.Fist) * 0.01f);
    }
}

/// <summary>試合の中の握る拳: 両手の刀・放す・傷の引き継ぎ。</summary>
[TestClass]
[TestCategory("slow")]
public sealed class GripHandMatchTests
{
    private const float Dt = 1f / 240f;
    private static readonly Vector3 Head = new(0f, 1.62f, 0f);

    private static float Bones(Aigis.Foundation.Voxels.VoxelGrid grid) => grid.Remaining(BoxerAnatomy.HandBone);

    [TestMethod]
    public void BothHandsOnTheKatana_AreVoxelFists_ThatAddTheirMass_AndLettingGoKeepsTheirWounds()
    {
        using var game = new BoxingGame(FighterTuning.Default with { PlayerFatigue = false }, corners: SwordTraining.Corners(WeaponKind.Katana, 2.535f));
        var mapper = new VrPlayerMapper(weaponMode: true) { GripHolds = true, FirstPerson = true, HeldWeapon = WeaponKind.Katana };
        void Run(float left, float right, float seconds)
        {
            var frame = new VrRawFrame(Head, Quaternion.Identity, new Vector3(-0.1f, 1.3f, -0.3f), new Vector3(0.1f, 1.3f, -0.35f), true, true,
                Vector2.Zero, Vector2.Zero, left, right, false, true, Quaternion.Identity, Quaternion.Identity);
            for (var i = 0; i < (int)(seconds * 240f); i++)
            {
                game.Step(mapper.Read(frame, game.PlayerSense, Dt), Dt);
            }
        }

        Run(1f, 1f, 1.7f);
        Assert.IsTrue(game.Player.Wields(Hand.Left) && game.Player.Wields(Hand.Right), "両手で持つ");
        var katana = game.WeaponOf(game.Player)!;
        var fists = katana.Hands.Where(h => h.Occupied).ToList();
        Assert.HasCount(2, fists, "両手とも柄を握る拳");
        Assert.IsTrue(game.Player.FistCovered(Hand.Left) && game.Player.FistCovered(Hand.Right), "球は描かない");
        var bare = Bones(Weapon.Build(WeaponKind.Fist));
        foreach (var fist in fists)
        {
            Assert.AreEqual(bare, Bones(fist.Body.Grid), "素手と同じ手の骨");
        }

        var expected = Weapon.MassOf(WeaponKind.Katana) + fists.Sum(f => f.Body.Grid.FilledCount * GripHand.MassPerCell);
        Assert.AreEqual(expected, game.World.GetMass(katana.Body.BodyId), expected * 0.02f, "刀の剛体は刀と両手の拳の重さ");

        // 拳は柄の上で重ならない。
        var x = fists.Select(f => f.GridCenterLocal.X).Order().ToList();
        Assert.IsGreaterThanOrEqualTo(GripHand.Length - (0.5f * HandSkeleton.Cell), x[1] - x[0]);

        // 右の拳の骨を 1 つ削ってから両手を放す: 右手にはめ直す素手の拳は、削れたまま。
        var right = katana.GripHandFor(game.Player, Hand.Right, create: false)!;
        var grid = right.Body.Grid;
        var bone = Enumerable.Range(0, grid.CellCount).First(i => grid.MaterialAt(i) == BoxerAnatomy.HandBone);
        grid.SetCellState(bone, (0, 0f, false, 0f));
        Run(0f, 0f, 1.5f);
        Assert.IsFalse(game.Player.Wields(Hand.Right) || game.Player.Wields(Hand.Left), "放した");
        Assert.IsFalse(right.Occupied, "放した手の拳は刀から外れる");
        Assert.AreEqual(Weapon.MassOf(WeaponKind.Katana), game.World.GetMass(katana.Body.BodyId), Weapon.MassOf(WeaponKind.Katana) * 0.02f, "刀は刀の重さに戻る");
        var bareFist = game.GloveOf(game.Player, Hand.Right);
        Assert.IsNotNull(bareFist, "放した手には素手の拳");
        Assert.AreEqual(WeaponKind.Fist, bareFist.Kind);
        Assert.AreEqual(bare - 1, Bones(bareFist.Body.Grid), "手の傷は手と一緒");
    }
}
