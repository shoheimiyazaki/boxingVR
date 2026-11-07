using System.Numerics;

namespace MiniRing.Tests;

[TestClass]
public sealed class ConditionAndZoneTests
{
    [TestMethod]
    public void Zones_AreFoundFromWhereOnThePartTheGloveLanded()
    {
        Assert.AreEqual(HitZone.Chin, HitZones.Classify(BodyPartKind.Head, new Vector3(0f, -0.5f, 0.9f)));
        Assert.AreEqual(HitZone.Chin, HitZones.Classify(BodyPartKind.Head, new Vector3(0f, -1f, 0.3f)), "an uppercut from below lands on the jaw, not the neck");
        Assert.AreEqual(HitZone.Neck, HitZones.Classify(BodyPartKind.Head, new Vector3(0f, -1f, -0.4f)));
        Assert.AreEqual(HitZone.Temple, HitZones.Classify(BodyPartKind.Head, new Vector3(0.9f, 0.2f, 0f)));
        Assert.AreEqual(HitZone.Head, HitZones.Classify(BodyPartKind.Head, new Vector3(0f, 0.6f, 0.9f)));
        Assert.AreEqual(HitZone.Neck, HitZones.Classify(BodyPartKind.Chest, new Vector3(0f, 0.9f, 0.5f)));
        // 右手系で +Z を向いた体: 本人の左は +X。心臓は前面の左、肝臓は右下。
        Assert.AreEqual(HitZone.Heart, HitZones.Classify(BodyPartKind.Chest, new Vector3(0.4f, 0.3f, 1f)));
        Assert.AreEqual(HitZone.Liver, HitZones.Classify(BodyPartKind.Chest, new Vector3(-0.7f, -0.5f, 0.6f)));
        Assert.AreEqual(HitZone.SolarPlexus, HitZones.Classify(BodyPartKind.Chest, new Vector3(0f, -0.3f, 1f)));
        Assert.AreEqual(HitZone.Body, HitZones.Classify(BodyPartKind.Chest, new Vector3(-0.8f, 0.4f, 0f)));
        // 左腕（+X 側）: 体側の端（−X）が肩、外側の端が肘。前腕の外側はグローブ（ガード）。
        Assert.AreEqual(HitZone.Shoulder, HitZones.Classify(BodyPartKind.UpperArm, new Vector3(-0.7f, 0f, 0f), armSide: 1f));
        Assert.AreEqual(HitZone.Elbow, HitZones.Classify(BodyPartKind.UpperArm, new Vector3(0.7f, 0f, 0f), armSide: 1f));
        Assert.AreEqual(HitZone.Shoulder, HitZones.Classify(BodyPartKind.UpperArm, new Vector3(0.7f, 0f, 0f), armSide: -1f));
        Assert.AreEqual(HitZone.Guard, HitZones.Classify(BodyPartKind.Forearm, new Vector3(0.8f, 0f, 0f), armSide: 1f));
        Assert.AreEqual(HitZone.Knee, HitZones.Classify(BodyPartKind.Thigh, new Vector3(0f, -0.8f, 0.5f)));
        Assert.AreEqual(HitZone.Leg, HitZones.Classify(BodyPartKind.Shin, new Vector3(0f, -0.2f, 0.5f)));
    }

    [TestMethod]
    public void Chin_HurtsTheMindMoreThanTheForehead_AndEmptyMindIsKO()
    {
        var chin = new BodyCondition();
        var forehead = new BodyCondition();
        chin.ApplyHit(HitZone.Chin, Hand.Left, 20f);
        forehead.ApplyHit(HitZone.Head, Hand.Left, 20f);

        Assert.IsLessThan(forehead.Consciousness, chin.Consciousness);
        Assert.IsGreaterThan(new BodyCondition().ReactionDelay, chin.ReactionDelay, "a dazed fighter reacts late");

        for (var i = 0; i < 6; i++)
        {
            chin.ApplyHit(HitZone.Chin, Hand.Left, 40f);
        }

        Assert.IsTrue(chin.IsKnockedOut);
        Assert.IsFalse(chin.CanStand);
    }

    [TestMethod]
    public void Liver_KicksInLate_ThenTheLegsGo()
    {
        var c = new BodyCondition();
        c.ApplyHit(HitZone.Liver, Hand.Left, 40f);
        Assert.AreEqual(1f, c.Legs, "nothing yet right after the punch");

        var kicked = false;
        for (var i = 0; i < 100; i++)
        {
            c.Tick(0.01f, 0f);
            kicked |= c.LiverKicked;
        }

        Assert.IsTrue(kicked);
        Assert.IsLessThan(0.5f, c.Legs);
    }

    [TestMethod]
    public void Heart_FreezesForAMoment()
    {
        var c = new BodyCondition();
        var before = c.ReactionDelay;
        c.ApplyHit(HitZone.Heart, Hand.Left, 20f);

        Assert.IsGreaterThan(before + 0.2f, c.ReactionDelay);
        Assert.IsFalse(c.CanGetUp);
        for (var i = 0; i < 200; i++)
        {
            c.Tick(0.01f, 0f);
        }

        Assert.AreEqual(0f, c.HeartStun);
    }

    [TestMethod]
    public void Shoulder_OnlyThatArmDropsAndStopsCarryingTheBody()
    {
        var c = new BodyCondition();
        c.ApplyHit(HitZone.Shoulder, Hand.Right, 40f);

        Assert.IsLessThan(c.ArmRaiseLimit(Hand.Left), c.ArmRaiseLimit(Hand.Right));
        Assert.IsLessThan(c.JointIntegrity(Hand.Left), c.JointIntegrity(Hand.Right));
        // 反対の腕はスタミナを少し失った分しか下がらない（スタミナは全身で共有）。
        Assert.AreEqual(new BodyCondition().ArmRaiseLimit(Hand.Left), c.ArmRaiseLimit(Hand.Left), 0.05f);
    }

    [TestMethod]
    public void Knees_TakeAwayStanding()
    {
        var c = new BodyCondition();
        c.ApplyHit(HitZone.Knee, Hand.Left, 40f);
        Assert.IsTrue(c.CanStand);
        c.ApplyHit(HitZone.Knee, Hand.Left, 40f);

        Assert.IsFalse(c.CanStand);
        Assert.IsFalse(c.CanGetUp);
    }

    [TestMethod]
    public void Stamina_DrainsWithWork_RecoversWithRest_AndLowersTheArms()
    {
        var c = new BodyCondition();
        for (var i = 0; i < 100; i++)
        {
            c.Tick(0.01f, 2f); // 激しく打ち続ける
        }

        var tired = c.Stamina;
        Assert.IsLessThan(0.5f, tired);
        Assert.IsLessThan(new BodyCondition().ArmRaiseLimit(Hand.Left), c.ArmRaiseLimit(Hand.Left));
        for (var i = 0; i < 300; i++)
        {
            c.Tick(0.01f, 0f);
        }

        Assert.IsGreaterThan(tired, c.Stamina);
    }
}
