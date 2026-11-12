using System.Numerics;
using Aigis.Foundation.Voxels;
using MiniRing.Voxels;
using MiniRing.Weapons;

namespace MiniRing.Tests;

/// <summary>
/// 試合の世界に置いた鉄の剣と木の棍棒（<see cref="Weapon"/>）。実験場の各項目（<see cref="WeaponDrills.Trials"/>）を走らせ、
/// 刃は斬り・腹は止められ・強い振りは腕を落とし・切っ先は腹の中まで届き・棍棒は斬れず・鉄は木を欠き・木は鉄を欠けないことを確かめる。
/// どれも、触れた形と材質（<see cref="BoxerAnatomy"/> の力学の値）だけから出てくる。
/// </summary>
[TestClass]
public sealed class WeaponTests
{
    private static WeaponOutcome Run(string name, int frames = 20)
    {
        var trial = WeaponDrills.Trials.Single(t => t.Name == name);
        using var game = WeaponDrills.StillMatch();
        var shot = trial.Setup(game);
        return WeaponDrills.Run(trial, game, shot, frames, 1f / 60f);
    }

    /// <summary>1 フレーム後の、打った武器の飛ぶ向きの速さ（止められたか）。</summary>
    private static float SpeedAfterOneFrame(string name)
    {
        var trial = WeaponDrills.Trials.Single(t => t.Name == name);
        using var game = WeaponDrills.StillMatch();
        var shot = trial.Setup(game);
        WeaponDrills.Run(trial, game, shot, 1, 1f / 60f);
        return Vector3.Dot(game.World.GetState(shot.Weapon.Body.BodyId).LinearVelocity, shot.Heading);
    }

    [TestMethod]
    public void TheSwordsEdge_CutsIntoTheForearm_ButItsFlat_IsStopped()
    {
        var edge = Run("sword-edge");
        var flat = Run("sword-flat");

        Assert.IsGreaterThan(5, edge.Lost(VoxelTissue.Flesh), "the edge cuts skin and muscle");
        // 平らな面そのものの当たり（最初の接触）は、体の 5mm のボクセルの角に斜めに当たって皮膚が少し擦れる程度。
        // （手に持っていない剣は、中心から外れて腹を打たれると長さの軸まわりに速く回り、回ってきた刃が後から当たる。それは刃の仕事。）
        Assert.IsLessThan(edge.FirstContactLost / 3, flat.FirstContactLost, "the flat itself breaks little");
        Assert.IsLessThan(3f, SpeedAfterOneFrame("sword-flat"), "the arm stops the flat");
        Assert.AreEqual(0, edge.WeaponLost + flat.WeaponLost, "flesh does not hurt iron");
    }

    [TestMethod]
    public void AnOrdinarySlash_StopsInTheBone_AHardOne_TakesTheArmOff()
    {
        var ordinary = Run("sword-edge");
        Assert.IsGreaterThan(0, ordinary.Lost(VoxelTissue.Flesh));
        Assert.IsFalse(ordinary.Severed, "the bone stops an ordinary slash");

        var hard = Run("sword-hard");
        Assert.IsGreaterThan(0, hard.Lost(VoxelTissue.Bone));
        Assert.IsTrue(hard.Severed, "a hard slash cuts through the bone");
    }

    [TestMethod]
    public void AThrust_ReachesTheGuts_WhereTheClubDoesNot()
    {
        Assert.IsGreaterThan(0, Run("sword-thrust").Lost(VoxelTissue.Organ), "the point goes in to the gut");
        Assert.AreEqual(0, Run("club-thrust").Lost(VoxelTissue.Organ), "the club's end does not");
    }

    [TestMethod]
    public void TheClub_DoesNotCut_ButIsStoppedByTheArm()
    {
        var blow = Run("club-swing", frames: 1);
        Assert.IsLessThan(3, blow.BodyLost.Values.Sum(), "no cut (a scuff of skin at most)");
        Assert.AreEqual(0, blow.Lost(VoxelTissue.Bone));
        // 頭の側で当たるので、当たった点は止まっても棍棒は回りながら重心の速さを少し残す。
        Assert.IsLessThan(4.5f, SpeedAfterOneFrame("club-swing"), "the arm takes a good part of the club's momentum");
    }

    [TestMethod]
    public void TheIronEdge_NotchesTheClub_AndStaysWhole()
    {
        var clash = Run("edge-on-club", frames: 6);
        Assert.IsGreaterThan(0, clash.TargetLost, "the edge bites into the wood");
        Assert.AreEqual(0, clash.WeaponLost, "the iron is not hurt");
    }

    [TestMethod]
    public void TheClub_CannotNotchTheSwordsFlat()
    {
        var clash = Run("club-on-flat", frames: 6);
        Assert.IsGreaterThan(0f, clash.PeakPressure, "they meet");
        Assert.AreEqual(0, clash.TargetLost, "wood cannot cut iron");
    }

    [TestMethod]
    public void Weapons_AreDrawnAsVoxelVolumes()
    {
        using var game = WeaponDrills.StillMatch();
        var before = game.BuildVoxelVolumes().Count;
        game.AddWeapon(WeaponKind.IronSword, BoxingGame.RingCenter + new Vector3(0.3f, 0.3f, 0f), Quaternion.Identity);
        game.AddWeapon(WeaponKind.WoodenClub, BoxingGame.RingCenter + new Vector3(-0.3f, 0.3f, 0f), Quaternion.Identity);
        Assert.AreEqual(before + 2, game.BuildVoxelVolumes().Count);
    }

    [TestMethod]
    public void TheSword_IsIronWithAWoodenGrip_AndTheClubIsAllWood()
    {
        var sword = Weapon.Build(WeaponKind.IronSword);
        var club = Weapon.Build(WeaponKind.WoodenClub);
        Assert.IsGreaterThan(sword.Remaining(BoxerAnatomy.Wood) * 3, sword.Remaining(BoxerAnatomy.Iron));
        Assert.IsGreaterThan(0, sword.Remaining(BoxerAnatomy.Wood));
        Assert.AreEqual(0, club.Remaining(BoxerAnatomy.Iron));
        Assert.IsGreaterThan(1000, club.Remaining(BoxerAnatomy.Wood));
    }

    [TestMethod]
    public void EveryMaterial_StatesHowItStandsUpToAContact()
    {
        var palette = BoxerAnatomy.Palette;
        for (var m = 1; m < palette.Count; m++)
        {
            Assert.IsNotNull(palette[(byte)m].Mechanics, palette[(byte)m].Name);
        }

        Assert.IsNotNull(palette[BoxerAnatomy.Iron].Mechanics!.Tensile, "iron can snap");
        Assert.IsNotNull(palette[BoxerAnatomy.Wood].Mechanics!.Tensile, "wood can snap");
        Assert.IsNotNull(palette[BoxerAnatomy.Bone].Mechanics!.Tensile, "bone carries a limb's bending: it snaps where a blow bends it too far");
        Assert.IsNull(palette[BoxerAnatomy.Muscle].Mechanics!.Tensile, "flesh only adds its mass to the limb as a beam");
        Assert.IsGreaterThan(palette[BoxerAnatomy.Muscle].Mechanics!.Strength, palette[BoxerAnatomy.Skin].Mechanics!.Strength, "skin is tougher than muscle");
        Assert.IsGreaterThan(palette[BoxerAnatomy.Skin].Mechanics!.Strength * 10f, palette[BoxerAnatomy.Bone].Mechanics!.Strength);
        Assert.IsGreaterThan(palette[BoxerAnatomy.Wood].Mechanics!.Strength * 10f, palette[BoxerAnatomy.Iron].Mechanics!.Strength);
    }
}
