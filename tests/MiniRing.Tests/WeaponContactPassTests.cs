namespace MiniRing.Tests;

/// <summary>
/// 武器の接触を、刃が押し戻されたら全部位で掃き直す（<see cref="BoxingGame.MaxContactPasses"/>）。肩の上腕と胸のつなぎ目に刃を押し込むと、
/// 押し戻された刃が隣の部位（すでに解き終えた部位）の中へ入って、その歩は刃が体の中にあるのに何も起きなかった（刃が肩と胴のすき間を
/// すり抜けたように見える）。
/// </summary>
[TestClass]
[TestCategory("slow")]
public sealed class WeaponContactPassTests
{
    /// <summary>
    /// 掃き直しが 1 回（以前の解き方）のとき、刃が体の中に残った押し込み（<c>ShoulderSeamProbe.EmbeddedBladesAtTheShoulder</c> の格子から拾った。PhysicsVersion 44 で刀の柄に鉄の芯を通して重さが変わったので、拾い直した）:
    /// 刃の向き番号、手、関節からの横のずれ [mm]。刃は 3.36 m/s で押し込む（人形の単位で拾った押し込みを ×2.817 の位置・×1.678 の速さへ換算した）。
    /// </summary>
    private static readonly (int Cut, Hand Hand, float OffsetMm)[] Cases =
    [
        (0, Hand.Left, 33.80f), (1, Hand.Left, 16.90f), (1, Hand.Left, 33.80f), (1, Hand.Right, 16.90f), (2, Hand.Left, 0f),
        (4, Hand.Right, -25.35f), (4, Hand.Right, 16.90f), (6, Hand.Left, -8.451f), (6, Hand.Right, -16.90f), (6, Hand.Right, 16.90f),
        // PhysicsVersion 59（顔・手の層とのマージ）で拾い直した 3 つ（`ShoulderSeamProbe.EmbeddedBladesAtTheShoulder`: 126 通りのうち掃き直し 1 回で
        // 刃が体の中に残ったのは 3 通りで、3 回では 0。前の 10 件は立ち方が変わって 1 回でも残らなくなった。残らないのは物理として良い変化で、3 回のテストには残してある）。
        (0, Hand.Left, 25.35f), (0, Hand.Right, 25.35f), (4, Hand.Left, -8.451f),
        // PhysicsVersion 93（セルを奥行きを斬り抜けた分だけ断つ）で拾い直した 1 つ（126 通りのうち掃き直し 1 回で残ったのはこれだけ、3 回では 0）。
        (0, Hand.Right, 33.80f),
    ];

    [TestMethod]
    public void ABladePushedIntoTheShoulderSeam_IsNeverLeftInsideABodyPartWithNoContact()
    {
        var left = new int[Cases.Length];
        Parallel.For(0, Cases.Length, i =>
            left[i] = ShoulderSeamProbe.EmbeddedSteps(Cases[i].Cut, Cases[i].Hand, Cases[i].OffsetMm / 1000f, 3.357f, passes: BoxingGame.DefaultMaxContactPasses));
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
            before[i] = ShoulderSeamProbe.EmbeddedSteps(Cases[i].Cut, Cases[i].Hand, Cases[i].OffsetMm / 1000f, 3.357f, passes: 1));
        Assert.IsGreaterThan(0, before.Sum(), "one sweep per step leaves a blade inside the neighbouring part");
    }

    [TestMethod]
    public void TheContactPasses_AreBounded()
    {
        using var game = new BoxingGame(seed: 1);
        Assert.AreEqual(3, game.MaxContactPasses);
    }
}
