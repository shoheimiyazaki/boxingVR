using MiniRing.Voxels;

namespace MiniRing.Tests;

/// <summary>組織の働きの値は体の材質から（2026-11-25）: 人の材質は 1、エイリアンの材質は人より強い。種族で分けない。</summary>
[TestClass]
[TestCategory("L3a")]
public sealed class TissuePhysiologyTests
{
    [TestMethod]
    public void HumanTissue_IsTheIdentity()
    {
        Assert.AreEqual(TissuePhysiology.Human, TissuePhysiology.Of(BoxerAnatomy.Palette));
        Assert.AreEqual(TissuePhysiology.Human, TissuePhysiology.Of(BoxerBody.PaletteFor(BodyBuild.Standard)));
        var tuning = new FighterTuning();
        Assert.AreEqual(tuning.PassiveTorqueScale, tuning.TorqueScale, "人の筋肉では関節の力の倍率は今まで通り");
    }

    [TestMethod]
    public void AlienTissue_ComesFromThePaletteTheBodyIsBuiltWith()
    {
        var alien = TissuePhysiology.Of(BoxerBody.PaletteFor(BodyBuild.Standard with { FourLegged = true }));
        Assert.IsGreaterThan(1f, alien.MuscleStress);
        Assert.IsGreaterThan(1f, alien.HeartPower);
        Assert.IsGreaterThan(1f, alien.LungPower);
        Assert.IsGreaterThan(1f, alien.OxygenStore);
        var tuning = new FighterTuning() with { Tissue = alien };
        Assert.AreEqual(tuning.PassiveTorqueScale * alien.MuscleStress, tuning.TorqueScale, 1e-6f, "筋の倍率は筋の力にだけ掛かる（靭帯は掛からない）");
    }
}
