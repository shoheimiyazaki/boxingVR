using System.Numerics;
using MiniRing.Bots;

namespace MiniRing.Tests;

/// <summary>
/// 脳しんとうは 1 つの頭のモデル（2026-11-19、PhysicsVersion 60、docs/plans/EXCHANGE_REGRESSION_2026-11-19.md）: 頭が得た回転の速さ ω で (ω ÷ <see cref="FighterTuning.ConcussionSpin"/>)²。
/// 前は拳が ω² × 0.0035、床に頭を打つ衝撃が ω² × 0.000035 と 100 倍違う係数で、拳の側の 100 倍（77be881）は体の乗った当たりの意識を飛ばしすぎた
/// （1 発の脳しんとうの最大 3.9 → 10〜20、意識を飛ばした 1 発のダメージ 29 → 11）。
/// </summary>
[TestClass]
public sealed class ConcussionScaleTests
{
    private const float Dt = 1f / 240f;

    /// <summary>同じ頭の回転の速さは、拳でも床でも同じ脳しんとう（どの軸まわりでも）。ConcussionSpin で回れば意識が全部飛ぶ。</summary>
    [TestMethod]
    public void TheSameHeadSpin_GivesTheSameConcussion_WhateverTurnedTheHead()
    {
        foreach (var axis in new[] { Vector3.UnitX, Vector3.UnitY, Vector3.UnitZ })
        {
            using var game = new BoxingGame(seed: 1);
            var f = game.Cpu;
            var head = game.World.GetState(f.Parts.Head);
            var inertia = game.World.GetLocalInertiaDiagonal(f.Parts.Head);
            var spin = FighterTuning.Default.ConcussionSpin;
            // 頭のローカル軸 axis まわりに ConcussionSpin で回す角運動量（ワールド）。
            var impulse = Vector3.Transform(axis * (spin * Vector3.Dot(axis, inertia)), head.Orientation);
            var before = f.Concussion;
            f.TwistHead(impulse);
            Assert.AreEqual(spin, f.LastHeadTwist, 1e-3f * spin, $"axis {axis}");
            Assert.AreEqual(1f, f.Concussion - before, 1e-3f, $"spinning the head at ConcussionSpin about {axis} knocks the lights out");
            f.TwistHead(impulse * 0.5f);
            Assert.AreEqual(1.25f, f.Concussion - before, 1e-3f, "half the spin is a quarter of the concussion");
        }
    }

    /// <summary>
    /// 試合のボットの強めの 1 発（0.7 kg × 2 m/s。ボットの拳に乗る質量の p90 は 1.2 kg、当たる速さの平均 1.7 m/s）はあごに当たっても意識を全部は飛ばさない（測った値 0.13）。体を乗せて速くあごを振り抜く 1 発（1.5 kg × 3 m/s）は 1 発で飛ばす（4.5）。
    /// </summary>
    [TestMethod]
    [TestCategory("slow")]
    public void AnOrdinaryHitStaysBelowAKnockout_AFastBodyLoadedShotToTheChinIsOne()
    {
        var ordinary = ChinConcussion(mass: 0.7f, speed: 2.0f);
        var big = ChinConcussion(mass: 1.5f, speed: 3.0f);
        Console.WriteLine($"ordinary {ordinary:0.00}, big {big:0.00}");
        Assert.IsLessThan(1f, ordinary, "an ordinary hit leaves him partly conscious");
        Assert.IsGreaterThanOrEqualTo(1f, big, "a fast, body-loaded shot through the chin is a one-punch knockout");
    }

    private static float ChinConcussion(float mass, float speed)
    {
        var still = BotCatalog.Boxer with { Attacks = false, DefenseRate = 0f, Prediction = 0f, Aggression = 0f };
        var corners = new BoxingCorners("still", s => new BoxerBot(still, s), s => new BoxerBot(still, s));
        using var game = new BoxingGame(seed: 1, corners: corners);
        for (var t = 0f; t < 1.5f; t += Dt)
        {
            game.Step(default, Dt);
        }

        var victim = game.Cpu;
        var chin = victim.HeadPosition + (victim.Frame.Forward * victim.HalfExtents.Head.Z) - new Vector3(0f, 0.8f * victim.HalfExtents.Head.Y, 0f);
        var damage = PunchMechanics.Damage(mass * speed) * PunchMechanics.Sharpness(speed);
        var before = victim.Concussion;
        game.ApplyHit(new HitEvent(game.Player, Hand.Right, victim, HitZone.Chin, chin, speed, mass, 0.5f, damage), victim.Parts.Head, -victim.Frame.Forward * speed);
        return victim.Concussion - before;
    }
}
