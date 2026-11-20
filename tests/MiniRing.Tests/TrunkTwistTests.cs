using System.Numerics;
using MiniRing.Arena;

namespace MiniRing.Tests;

/// <summary>
/// 胴のひねりの駆動（PhysicsVersion 68、docs/plans/DRIVE_DOE_ANALYSIS.md）: 胸を鉛直軸まわりに回すバネは、回旋筋のトルク（<see cref="FighterTuning.TrunkRotatorTorque"/>）と
/// ひねりの目標の速さの先行（<see cref="FighterTuning.TrunkTwistFeedForward"/>）で決まる。傾きのバネ（<see cref="FighterTuning.UprightKp"/>）は変えていない。
/// </summary>
[TestClass]
[TestCategory("slow")]
public sealed class TrunkTwistTests
{
    /// <summary>右ストレートを 1 本打たせ、打ち出しから当たる頃（0.13 秒）までの胸の鉛直軸まわりの角速度の最大 [rad/s] と、肩の前進の最大 [m/s] を返す。</summary>
    private static (float ChestRate, float ShoulderForward) Punch(FighterTuning tuning)
    {
        using var bout = new PunchLab.Bout(new(PunchLabPath.Standard, "still", "reach", 1.05f, false), 1, 0.1343f, tuning);
        for (var guard = 0; bout.PunchTime is null; guard++)
        {
            Assert.IsLessThan(6000, guard, "打ち出しまで寄せられる");
            bout.Step();
        }

        var f = bout.Game.Player;
        var world = bout.Game.World;
        var chestRate = 0f;
        var shoulderForward = 0f;
        while (bout.PunchTime is { } t && t < 0.13f)
        {
            var chest = world.GetState(f.Parts.Chest);
            var shoulder = f.ShoulderPosition(Hand.Right);
            var shoulderVelocity = chest.LinearVelocity + Vector3.Cross(chest.AngularVelocity, shoulder - chest.Position);
            if (t >= 0f)
            {
                chestRate = MathF.Max(chestRate, MathF.Abs(chest.AngularVelocity.Y));
                shoulderForward = MathF.Max(shoulderForward, Vector3.Dot(shoulderVelocity, f.Frame.Forward));
            }

            bout.Step();
        }

        return (chestRate, shoulderForward);
    }

    /// <summary>
    /// 回旋筋のトルクを上げる・目標の速さの先行を入れると、同じ指令（右ストレート）で胸が速く回り、肩が前へ出る（物理: 力が大きいほど・目標の動きに合わせて回すほど速い）。
    /// 変更前の体（28.2 N·m・先行なし）はバネの Kp が傾きと同じ 213 /s²（可動域の端のトルクが人の約 1/4〜1/5）で、胸は 2 rad/s ほどしか回らなかった。
    /// </summary>
    [TestMethod]
    public void TrunkRotation_IsFasterWithStrongerRotatorsAndTheTargetsSpeedFedForward()
    {
        var legacy = FighterTuning.Default with { TrunkRotatorTorque = 28.2f, TrunkTwistFeedForward = 0f };
        var stronger = legacy with { TrunkRotatorTorque = 120f };
        var feedForward = legacy with { TrunkTwistFeedForward = 1f };
        var both = FighterTuning.Default; // 既定 = 70 N·m + 先行 1

        var baseline = Punch(legacy);
        var strong = Punch(stronger);
        var ff = Punch(feedForward);
        var all = Punch(both);

        Assert.IsLessThan(3f, baseline.ChestRate, $"変更前の体の胸は遅い（人 8〜12 rad/s）: {baseline.ChestRate:F2}");
        Assert.IsGreaterThan(baseline.ChestRate * 1.4f, strong.ChestRate, $"トルクを上げると胸が速く回る: {strong.ChestRate:F2} 対 {baseline.ChestRate:F2}");
        Assert.IsGreaterThan(baseline.ChestRate * 1.4f, ff.ChestRate, $"目標の速さの先行で胸が速く回る: {ff.ChestRate:F2} 対 {baseline.ChestRate:F2}");
        Assert.IsGreaterThan(ff.ChestRate, all.ChestRate, $"先行にトルクを足すとさらに速い: {all.ChestRate:F2} 対 {ff.ChestRate:F2}");
        Assert.IsGreaterThan(baseline.ShoulderForward, all.ShoulderForward, $"胸が速く回ると肩が前へ出る: {all.ShoulderForward:F2} 対 {baseline.ShoulderForward:F2}");
        Assert.IsGreaterThan(4f, all.ChestRate, $"既定の体の胸は 4 rad/s を超えて回る: {all.ChestRate:F2}");
    }

    /// <summary>傾き（前後・左右）のバネは変えていない: 回旋筋のトルクを変えても、立っている体の傾きの保ち方（胸が起きている）は同じ。</summary>
    [TestMethod]
    public void RotatorTorque_DoesNotTiltTheStandingBody()
    {
        foreach (var torque in new[] { 28.2f, 120f, 250f })
        {
            using var bout = new PunchLab.Bout(new(PunchLabPath.Standard, "still", "reach", 1.05f, false), 1, 0.1343f, FighterTuning.Default with { TrunkRotatorTorque = torque });
            while (bout.PunchTime is null)
            {
                bout.Step();
            }

            var f = bout.Game.Player;
            var maxTilt = 0f;
            while (bout.PunchTime is { } t && t < 0.6f)
            {
                var up = Vector3.Transform(Vector3.UnitY, bout.Game.World.GetState(f.Parts.Chest).Orientation);
                maxTilt = MathF.Max(maxTilt, MathF.Acos(Math.Clamp(up.Y, -1f, 1f)));
                bout.Step();
            }

            Assert.AreEqual(FighterState.Standing, f.State, $"トルク {torque} N·m でも立ったまま");
            Assert.IsLessThan(35f * MathF.PI / 180f, maxTilt, $"トルク {torque} N·m でも胸は起きている（傾き {maxTilt * 180f / MathF.PI:F1}°）");
        }
    }
}
