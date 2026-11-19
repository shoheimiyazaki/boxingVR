using System.Numerics;

namespace MiniRing.Tests;

/// <summary>
/// 体の質量が当たりへ運べるのは、体自身が当たりへ動いている分だけ（<see cref="PunchMechanics.BodyCarry"/>・<see cref="PunchMechanics.BodyClosingSpeed"/>、
/// 2026-11-18、PhysicsVersion 45）。前は「連結度 × 体の有効質量 × 拳の速さ」で、体は拳ほど速く動いていない（当たった瞬間の肩の近づく速さは 0〜0.2 m/s）のに
/// 運動量が湧いていた。そのため体を止めて腕だけ素早く伸ばす連打にも体重の 6〜8 割が乗り、前へ押しながらの高速連打が最適になった（docs/arena/rush_physics.md）。
/// </summary>
[TestClass]
public sealed class BodyCarryTests
{
    [TestMethod]
    public void BodyCarry_IsTheBodysOwnClosingSpeedOverTheFists()
    {
        Assert.AreEqual(0f, PunchMechanics.BodyCarry(0f, 3f), 1e-6f, "体が止まって腕だけ伸ばした: 体は何も運ばない");
        Assert.AreEqual(0.5f, PunchMechanics.BodyCarry(1.5f, 3f), 1e-6f, "体が 1.5 m/s で近づく: 半分");
        Assert.AreEqual(1f, PunchMechanics.BodyCarry(4f, 3f), 1e-6f, "体の方が速くても 1 まで（腕が縮む分は運ばない）");
        Assert.AreEqual(0f, PunchMechanics.BodyCarry(-1f, 3f), 1e-6f, "下がりながら打つ体は運ばない");
        Assert.AreEqual(0f, PunchMechanics.BodyCarry(1f, 0f), 1e-6f, "当たる速さが無ければ 0");
    }

    /// <summary>
    /// 体自身の近づく速さは、突っ張り（付け根の速度、重み a²）と梃子（手足を胴に固めて当たる点が動く速度、重み 0.55·(1 − a²)）の重み平均。
    /// まっすぐの拳（手足の線が当たる向き）は付け根だけ、横から振るフック（線が直交）は当たる点の回転だけで決まる。
    /// </summary>
    [TestMethod]
    public void BodyClosingSpeed_BlendsTheStrutAndTheLeverLikeTheCoupling()
    {
        var into = Vector3.UnitZ;
        var shoulder = new Vector3(0f, 0f, 0.4f);   // クロス: 後ろの肩が回転で前へ
        var atFist = new Vector3(-1.5f, 0f, 0f);    // 拳の所の胴の回転の速度は横向き
        Assert.AreEqual(0.4f, PunchMechanics.BodyClosingSpeed(shoulder, atFist, Vector3.Zero, into, into), 1e-5f, "まっすぐの拳は付け根（肩）の速さ");
        var hookAtFist = new Vector3(0f, 0f, 1.5f); // フック: 腕は横、胴の回転で拳の所が前へ
        Assert.AreEqual(1.5f, PunchMechanics.BodyClosingSpeed(Vector3.Zero, hookAtFist, Vector3.Zero, Vector3.UnitX, into), 1e-5f, "腕の線が当たる向きに直交するフックは、当たる点の回転の速さ");
        var diagonal = Vector3.Normalize(new Vector3(1f, 0f, 1f)); // a² = 0.5: 重み 0.5 と 0.275
        var expected = ((0.5f * 0.4f) + (0.275f * 1.5f)) / 0.775f;
        Assert.AreEqual(expected, PunchMechanics.BodyClosingSpeed(shoulder, hookAtFist, Vector3.Zero, diagonal, into), 1e-5f, "間は連結度と同じ重みで混ぜる");
    }

    /// <summary>
    /// 運動量が湧かない: 体の分の運動量（乗る質量 − 腕）× 当たる速さは、連結度 × 体の有効質量 × 体自身の近づく速さに等しい。
    /// </summary>
    [TestMethod]
    public void TheBodysShareOfTheMomentum_IsTheBodysOwnMomentum()
    {
        using var game = new BoxingGame(seed: 3);
        var f = game.Player;
        var dir = Vector3.UnitZ;
        const float fist = 3f;
        const float body = 0.6f;
        var carry = PunchMechanics.BodyCarry(body, fist);
        var arm = f.ArmMass(Hand.Right);
        var full = f.ImpactMass(Hand.Right, dir, out _, bodyFactor: 1f) - arm; // 連結度 × 体の有効質量
        var carried = (f.ImpactMass(Hand.Right, dir, out _, bodyFactor: carry) - arm) * fist;
        Assert.AreEqual(full * body, carried, 1e-4f, "体の分の運動量 = 連結度 × 体の有効質量 × 体自身の速さ");
    }
}
