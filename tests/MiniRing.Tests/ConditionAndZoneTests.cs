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
}
