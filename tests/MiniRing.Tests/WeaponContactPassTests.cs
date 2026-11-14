namespace MiniRing.Tests;

/// <summary>
/// 武器の接触を、刃が押し戻されたら全部位で掃き直す（<see cref="BoxingGame.MaxContactPasses"/>）。肩の上腕と胸のつなぎ目に刃を押し込むと、
/// 押し戻された刃が隣の部位（すでに解き終えた部位）の中へ入って、その歩は刃が体の中にあるのに何も起きなかった（刃が肩と胴のすき間を
/// すり抜けたように見える）。
/// </summary>
[TestClass]
public sealed class WeaponContactPassTests
{
    /// <summary>
    /// 掃き直しが 1 回（以前の解き方）のとき、刃が体の中に残った押し込み（<c>ShoulderSeamProbe.EmbeddedBladesAtTheShoulder</c> の格子から拾った）:
    /// 刃の向き番号、手、関節からの横のずれ [mm]。刃は 2 m/s で押し込む。
    /// </summary>
    private static readonly (int Cut, Hand Hand, float OffsetMm)[] Cases =
    [
        (0, Hand.Left, 6f), (0, Hand.Left, 9f), (0, Hand.Right, 9f), (1, Hand.Left, 9f),
        (4, Hand.Left, 6f), (4, Hand.Right, -3f), (6, Hand.Right, -12f), (6, Hand.Right, 9f),
    ];

    [TestMethod]
    public void ABladePushedIntoTheShoulderSeam_IsNeverLeftInsideABodyPartWithNoContact()
    {
        var left = new int[Cases.Length];
        Parallel.For(0, Cases.Length, i =>
            left[i] = ShoulderSeamProbe.EmbeddedSteps(Cases[i].Cut, Cases[i].Hand, Cases[i].OffsetMm / 1000f, 2f, passes: BoxingGame.DefaultMaxContactPasses));
        for (var i = 0; i < left.Length; i++)
        {
            Assert.AreEqual(0, left[i], $"cut {Cases[i].Cut}, {Cases[i].Hand}, {Cases[i].OffsetMm} mm: the blade was inside a body part for a step with no contact");
        }
    }

    [TestMethod]
    public void WithASingleSweep_TheSameBladesAreLeftInsideTheNextPart()
    {
        // 以前の解き方（掃き直さない）で同じ押し込みをすると、刃が体の中に残る歩がある: 掃き直しが効いていることの確かめ。
        var before = new int[Cases.Length];
        Parallel.For(0, Cases.Length, i =>
            before[i] = ShoulderSeamProbe.EmbeddedSteps(Cases[i].Cut, Cases[i].Hand, Cases[i].OffsetMm / 1000f, 2f, passes: 1));
        Assert.IsGreaterThan(0, before.Sum(), "one sweep per step leaves a blade inside the neighbouring part");
    }

    [TestMethod]
    public void TheContactPasses_AreBounded()
    {
        using var game = new BoxingGame(seed: 1);
        Assert.AreEqual(3, game.MaxContactPasses);
    }
}
