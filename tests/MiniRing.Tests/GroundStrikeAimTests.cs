using System.Numerics;
using MiniRing.Bots;

namespace MiniRing.Tests;

/// <summary>
/// 倒れた相手への攻め（<see cref="GroundStrike"/>）の判断だけを、作った状況（<see cref="FighterSense"/>）で確かめる（物理は回さない。2026-11-24）:
/// 自分も倒れていれば攻めない・狙う範囲（<see cref="GroundMove.AimFrom"/>）で首を狙う・足の技を続けて選ぶ・蹴りは後ろから前へ振り抜く。
/// </summary>
[TestClass]
public sealed class GroundStrikeAimTests
{
    private const float Reach = 0.6f;
    private const float Dt = 1f / 240f;

    /// <summary>自分は原点の高さ 1 m で +Z を向く。倒れた相手は X 軸に沿って横たわる（腰 → 肩 → 頭が +X）。</summary>
    private static FighterSense Sense(float z, float y = 0.05f, bool selfDowned = false)
    {
        var pelvis = new Vector3(-0.6f, y, z);
        var shoulders = new Vector3(-0.2f, y, z);
        var head = new Vector3(0.1f, y, z);
        return new FighterSense(new BodyFrame(new Vector3(0f, 1f, 0f), 0f), Reach, head, Vector3.Lerp(pelvis, shoulders, 0.7f), OpponentAlive: false,
            OpponentBody: new BodySense(shoulders + new Vector3(0f, 0f, 0.1f), shoulders - new Vector3(0f, 0f, 0.1f), pelvis),
            OpponentDowned: true, MayStrikeDowned: true, SelfArmed: false, SelfDowned: selfDowned);
    }

    [TestMethod]
    public void SelfDowned_DoesNotStrikeTheDownedOpponent()
    {
        Assert.IsTrue(GroundStrike.Applies(Sense(0.5f)));
        Assert.IsFalse(GroundStrike.Applies(Sense(0.5f, selfDowned: true)), "倒れている間は倒れた相手を攻めない（起き上がるのが先）");
    }

    [TestMethod]
    public void AimRange_NeckOnly_FacesTheNeck_WholeBody_FacesTheNearestPoint()
    {
        // 狙う範囲が首だけ（1.5 = 両肩の真ん中と頭の真ん中）なら、自分の真正面（x = 0）が胴でも首（x = −0.05）を向く。
        var neck = GroundTuning.Default with { Hammer = GroundTuning.Default.Hammer with { AimFrom = 1.5f, AimTo = 1.5f } };
        var style = BotCatalog.Boxer with { GroundStomp = 0f, GroundCut = 1f, GroundDrive = 1f };
        var sense = Sense(0.8f) with { OpponentHead = new Vector3(0.6f, 0.05f, 0.8f) };
        var toNeck = new GroundStrike(style, GroundTool.Fist, 1, tuning: neck).Read(sense, default, Dt, steer: false).FaceDirection;
        var whole = new GroundStrike(style, GroundTool.Fist, 1).Read(sense, default, Dt, steer: false).FaceDirection;
        // 首 = 両肩の真ん中（x −0.2）と頭（x 0.6）の真ん中 = x 0.2。
        Assert.AreEqual(MathF.Atan2(0.2f, 0.8f), MathF.Atan2(toNeck.X, toNeck.Z), 1e-3f, "首を向く");
        Assert.AreEqual(0f, whole.X, 1e-3f, "範囲が体の全部なら、いちばん近い所（真正面）を向く");
    }

    [TestMethod]
    public void Feet_MayStompAgainRightAfterAStomp()
    {
        var style = BotCatalog.Boxer with { GroundStomp = 1f, GroundKick = 0f, GroundDrive = 1f, GroundRest = 0.1678f };
        var ground = new GroundStrike(style, GroundTool.Fist, 3);
        var sense = Sense(0.5f);
        ground.Read(sense, default, Dt, steer: false);
        Assert.AreEqual("stomp", ground.CurrentName);
        ground.Read(sense, default, Dt, GroundCall.Stomp, steer: false); // 今踏む
        Assert.IsTrue(ground.Striking);
        for (var t = 0f; t < 4f && ground.Striking; t += Dt)
        {
            ground.Read(sense, default, Dt, GroundCall.Hold, steer: false);
        }

        Assert.IsFalse(ground.Striking);
        ground.Read(sense, default, Dt, steer: false);
        Assert.AreEqual("stomp", ground.CurrentName, "踏んだ直後でも、足を好むなら続けて踏む（手の技を挟まない）");
    }

    [TestMethod]
    public void Kick_SwingsTheFootForwardThroughTheTarget()
    {
        var style = BotCatalog.Boxer with { GroundStomp = 1f, GroundKick = 1f, GroundDrive = 1f, GroundLoad = 0f };
        var ground = new GroundStrike(style, GroundTool.Fist, 5);
        // 首〜頭（x −0.05〜0.1）が自分の正面の床（ローカルで下 0.95 m・前 0.25 m）: 蹴る足の届く所。
        var sense = Sense(0.25f);
        var feet = new List<Vector3>();
        for (var t = 0f; t < 3f; t += Dt)
        {
            var c = ground.Read(sense, default, Dt, steer: false);
            if (ground.Striking && ground.CurrentName == "kick" && (c.LeftLeg ?? c.RightLeg) is { } leg)
            {
                feet.Add(leg.TargetLocal);
            }
            else if (feet.Count > 0)
            {
                break;
            }
        }

        Assert.IsTrue(feet.Count > 10, "蹴りを打ち始める");
        var t0 = GroundTuning.Default;
        var kick = t0.Kick;
        var wound = feet[(int)(feet.Count * t0.WindShare) - 1];
        var struck = feet[(int)(feet.Count * t0.StrikeShare) - 1];
        // 振りかぶりの足は当てる所より Pull だけ手前、当てる所は狙いより Through だけ先。
        Assert.IsTrue(struck.Z - wound.Z > 0.8f * kick.Pull * Reach, $"足は手前（z {wound.Z:0.00}）から前（z {struck.Z:0.00}）へ振り抜く");
        Assert.IsTrue(struck.Z > 0.25f + (0.5f * kick.Through * Reach), "狙い（z 0.25）より前まで抜ける");
    }

    [TestMethod]
    public void BladeTextbooks_AimTheNeck_OldPathsWithoutAimKeepTheWholeBody()
    {
        // 刃の教科書（刀・ロングソード・片手剣）は、とどめの斬り下ろし・突き下ろしで首（1.5）を狙う。
        foreach (var weapon in new[] { Weapons.WeaponKind.Katana, Weapons.WeaponKind.Longsword, Weapons.WeaponKind.IronSword })
        {
            var ground = WeaponCatalog.Textbook(weapon).Ground!.Validated();
            Assert.AreEqual(1.5f, ground.Cut.AimFrom, weapon.ToString());
            Assert.AreEqual(1.5f, ground.Cut.AimTo, weapon.ToString());
        }

        // 狙う範囲を書いていない古い経路（JSON）は、体の全部を狙う（範囲 0..2）。
        var node = System.Text.Json.Nodes.JsonNode.Parse(BotCatalog.Boxer.ToJson())!.AsObject();
        node["Ground"] = System.Text.Json.Nodes.JsonNode.Parse("""{"Hammer":{"Name":"hammer","Limb":0,"Duration":0.8,"Stand":0.3,"Reach":1,"Rise":0.6,"Pull":0.1,"WindElevation":0,"StrikeElevation":0,"Overshoot":0.1,"Dip":0.3,"Lean":0.1,"Spring":1}}""");
        var json = node.ToJsonString();
        var hammer = BotStyle.FromJson(json).Ground!.Validated().Hammer;
        Assert.AreEqual(0f, hammer.AimFrom);
        Assert.AreEqual(2f, hammer.AimTo);
        Assert.AreEqual(0.8f, hammer.Duration);
    }
}
