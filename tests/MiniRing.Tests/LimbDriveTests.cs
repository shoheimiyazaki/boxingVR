using System.Numerics;

namespace MiniRing.Tests;

/// <summary>
/// 体の質量が当たりに乗るのは、腕・脚が打つ点を当たりへ押し出している分だけ（<see cref="PunchMechanics.LimbDrive"/>、2026-11-17）。
/// 当たる速さを相対速度にした（<see cref="ClosingSpeedTests"/>）だけでは、構えて置いた拳に相手が顔から突っ込むと、置いた拳に体の 8〜9 割が乗った
/// 「重いパンチ」になり、打ち返さない受け手が 22〜70 点を与えた（直す前は 0。docs/arena/rush_physics.md）。
/// </summary>
[TestClass]
public sealed class LimbDriveTests
{
    [TestMethod]
    public void LimbDrive_IsTheShareOfTheClosingSpeedMadeByTheLimbExtending()
    {
        var into = Vector3.UnitZ;
        Assert.AreEqual(1f, PunchMechanics.LimbDrive(new Vector3(0f, 0f, 3f), Vector3.Zero, Vector3.Zero, into), 1e-6f, "体は止まって腕だけ伸ばして当てた: 全部が腕の押し出し");
        Assert.AreEqual(0f, PunchMechanics.LimbDrive(Vector3.Zero, Vector3.Zero, new Vector3(0f, 0f, -2f), into), 1e-6f, "置いた拳に相手が突っ込んだ: 腕は押し出していない");
        Assert.AreEqual(0f, PunchMechanics.LimbDrive(new Vector3(0f, 0f, 0.6f), new Vector3(0f, 0f, 0.6f), Vector3.Zero, into), 1e-6f, "体ごと歩いて押し付けた: 拳は肩と一緒に動く");
        Assert.AreEqual(0f, PunchMechanics.LimbDrive(new Vector3(0f, 0f, 0.4f), new Vector3(0f, 0f, 0.9f), Vector3.Zero, into), 1e-6f, "前へ出ながら腕を戻す途中で触れた");
        Assert.AreEqual(0.75f, PunchMechanics.LimbDrive(new Vector3(0f, 0f, 3f), Vector3.Zero, new Vector3(0f, 0f, -1f), into), 1e-6f, "出てくる相手へ伸ばした拳: 近づく速さ 4 のうち 3 が腕");
        Assert.AreEqual(0f, PunchMechanics.LimbDrive(new Vector3(0f, 0f, 1f), Vector3.Zero, new Vector3(0f, 0f, 2f), into), 1e-6f, "離れていく相手には当たる速さが無い");
    }

    [TestMethod]
    public void ImpactMass_WithoutDrive_IsTheArmAlone()
    {
        using var game = new BoxingGame(seed: 3);
        var f = game.Player;
        var dir = Vector3.UnitZ;
        Assert.AreEqual(f.ArmMass(Hand.Right), f.ImpactMass(Hand.Right, dir, out var share, bodyFactor: 0f), 1e-5f, "押し出していない腕には体の質量が乗らない");
        Assert.AreEqual(0f, share, 1e-6f);
        Assert.IsGreaterThan(f.ArmMass(Hand.Right), f.ImpactMass(Hand.Right, dir, out _, bodyFactor: 1f), "押し出している腕には体の質量が乗る");
    }
}
