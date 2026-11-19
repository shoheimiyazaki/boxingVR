using System.Numerics;
using MiniRing.Arena;

namespace MiniRing.Tests;

[TestClass]
public sealed class StickSolidRendererTests
{
    [TestMethod]
    public void ARayAtTheSideOfACapsule_HitsItsSurface_AndARayBesideItMisses()
    {
        var a = new Vector3(0f, -1f, 5f);
        var b = new Vector3(0f, 1f, 5f);
        Assert.AreEqual(4.9f, StickSolidRenderer.IntersectCapsule(Vector3.Zero, Vector3.UnitZ, a, b, 0.1f), 1e-4f, "胴の横から: 中心までの距離 − 半径");
        Assert.IsTrue(StickSolidRenderer.IntersectCapsule(new Vector3(0.2f, 0f, 0f), Vector3.UnitZ, a, b, 0.1f) < 0f, "半径の外は当たらない");
    }

    [TestMethod]
    public void ARayAlongTheAxis_HitsTheRoundEnd_AndAPointCapsuleIsASphere()
    {
        var a = new Vector3(0f, 0f, 3f);
        var b = new Vector3(0f, 0f, 6f);
        Assert.AreEqual(2.8f, StickSolidRenderer.IntersectCapsule(Vector3.Zero, Vector3.UnitZ, a, b, 0.2f), 1e-4f, "軸に沿うと手前の丸い端");
        Assert.AreEqual(2.8f, StickSolidRenderer.IntersectCapsule(Vector3.Zero, Vector3.UnitZ, a, a, 0.2f), 1e-4f, "長さ 0 は球");
    }

    [TestMethod]
    public void TheNearerCapsule_Wins_AndTheLitSideIsBrighterThanTheShadedSide()
    {
        var r = new StickSolidRenderer(64, 64, supersample: 1);
        r.Begin(new Vector3(0f, 0f, -3f), Vector3.Zero, 40f);
        r.Capsule(new Vector3(-1f, 0f, 1f), new Vector3(1f, 0f, 1f), 0.3f, new Vector3(1f, 0f, 0f), 0f);
        r.Capsule(new Vector3(0f, -1f, 0f), new Vector3(0f, 1f, 0f), 0.3f, new Vector3(0f, 0f, 1f), 0f);
        var image = new byte[64 * 64 * 3];
        r.CopyTo(image, 64, 0, 0);
        var centre = ((32 * 64) + 32) * 3;
        Assert.IsTrue(image[centre + 2] > image[centre], "手前の青い円柱が奥の赤を隠す");

        // 光は上から来るので、横の円柱は上の面が下の面より明るい。
        var lit = new StickSolidRenderer(64, 64, supersample: 1);
        lit.Begin(new Vector3(0f, 0f, -3f), Vector3.Zero, 40f);
        lit.Capsule(new Vector3(-1f, 0f, 0f), new Vector3(1f, 0f, 0f), 0.4f, Vector3.One, 0f);
        var picture = new byte[64 * 64 * 3];
        lit.CopyTo(picture, 64, 0, 0);
        int Brightness(int y) => picture[((y * 64) + 32) * 3];
        Assert.IsTrue(Brightness(26) > Brightness(38), $"上の面 {Brightness(26)} > 下の面 {Brightness(38)}");
    }
}
