using System.Numerics;
using MiniRing.Bots;

namespace MiniRing.Tests;

/// <summary>狙いどころ（戦略が選ぶ点）と、無理のない範囲（パンチに速さと体重が乗る範囲）への収め方。</summary>
[TestClass]
public sealed class TargetingTests
{
    [TestMethod]
    public void Clamp_KeepsTheAimWithinReachableShiftAndAngle()
    {
        var shoulder = Vector3.Zero;
        var natural = new Vector3(0f, 0f, 0.3f);
        const float Reach = 0.2f;
        var near = natural + new Vector3(0.02f, -0.03f, 0f);
        Assert.AreEqual(near, Targeting.Clamp(natural, near, shoulder, Reach), "a point close to the natural aim is taken as is");

        var far = natural + new Vector3(0.5f, -0.5f, 0f);
        var clamped = Targeting.Clamp(natural, far, shoulder, Reach);
        Assert.IsLessThanOrEqualTo(Targeting.MaxShift * Reach + 1e-5f, Vector3.Distance(clamped, natural), "no further than MaxShift arm lengths");
        var angle = MathF.Acos(Vector3.Dot(Vector3.Normalize(natural - shoulder), Vector3.Normalize(clamped - shoulder))) * 180f / MathF.PI;
        Assert.IsLessThanOrEqualTo(Targeting.MaxAngleDegrees + 0.01f, angle, "the punch line stays near the body's line of force");
        Assert.IsGreaterThan(0f, Vector3.Dot(clamped - natural, far - natural), "moved towards the wanted point");
    }

    [TestMethod]
    public void Pick_ChoosesOnlyGoalsThatFitThePunch_AndAreAvailable()
    {
        using var game = new BoxingGame(seed: 1);
        game.Step(default, 1f / 60f);
        var map = game.Cpu.AsTarget;
        Assert.IsFalse(map.Available(AimGoal.ExposedBrain), "the skull is whole");
        var brainOnly = new AimWeights(ExposedBrain: 10f);
        Assert.AreEqual(AimGoal.Center, Targeting.Pick(brainOnly, toBody: false, map, 0.5f), "an unavailable goal is never picked");
        var liverOnly = new AimWeights(Liver: 9f);
        Assert.AreEqual(AimGoal.Center, Targeting.Pick(liverOnly, toBody: false, map, 0.95f), "a head punch never goes for the liver");
        Assert.AreEqual(AimGoal.Liver, Targeting.Pick(liverOnly, toBody: true, map, 0.95f));
        Assert.AreEqual(AimGoal.Center, Targeting.Pick(AimWeights.None, toBody: true, map, 0.99f), "no weights: the middle, as before");
    }

    [TestMethod]
    public void TargetMap_FindsTheChinBelowTheFace_TheLiverLow_AndTheExposedBrain()
    {
        using var game = new BoxingGame(seed: 1);
        for (var i = 0; i < 30; i++)
        {
            game.Step(default, 1f / 60f);
        }

        var cpu = game.Cpu;
        var map = cpu.AsTarget;
        var from = game.Player.HeadPosition;
        var chin = map.Point(AimGoal.Chin, from)!.Value;
        Assert.IsLessThan(cpu.HeadPosition.Y, chin.Y, "the chin is below the middle of the head");
        Assert.IsLessThan(Vector3.Distance(from, cpu.HeadPosition) + 0.01f, Vector3.Distance(from, chin), "and on the face side (no further than the middle of the head)");
        var liver = map.Point(AimGoal.Liver, from)!.Value;
        Assert.IsLessThan(cpu.ChestPosition.Y, liver.Y, "the liver is low in the trunk");

        // 頭の正面を繰り返し深く削ると、脳が外に出る。
        for (var i = 0; i < 6 && !map.Available(AimGoal.ExposedBrain); i++)
        {
            game.Strike(cpu, cpu.Parts.Head, cpu.HeadPosition + (cpu.Frame.Forward * cpu.HalfExtents.Head.Z), -cpu.Frame.Forward, 30f, carve: 120f);
        }

        Assert.IsTrue(map.Available(AimGoal.ExposedBrain), "the carved skull shows the brain");
        var brain = map.Point(AimGoal.ExposedBrain, from)!.Value;
        Assert.IsLessThan(cpu.HalfExtents.Head.Length() * 1.5f, Vector3.Distance(brain, cpu.HeadPosition), "inside the head");
    }
}
