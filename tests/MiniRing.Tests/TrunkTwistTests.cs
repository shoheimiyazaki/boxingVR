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
    internal static (float ChestRate, float ShoulderForward) Punch(FighterTuning tuning, float range = 1.05f)
    {
        using var bout = new PunchLab.Bout(new(PunchLabPath.Standard, "still", "reach", range, false), 1, 0.1343f, tuning with { HipTwistShare = 0.45f, ChestTwistResponseSeconds = 0.025f }, new PunchLab.DriveKnobs());
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
        var both = FighterTuning.Default;

        // 間合いを振った平均で比べる（寄り終わった間合い 1 点の 1 本は、足の置き方のわずかな違いで胸の速さが 3 割揺れる: docs/arena/foot_snag.md）。
        static (float ChestRate, float ShoulderForward) Mean(FighterTuning tuning)
        {
            var runs = PunchPathTests.Ranges.Select(r => Punch(tuning, r)).ToArray();
            return (runs.Average(r => r.ChestRate), runs.Average(r => r.ShoulderForward));
        }

        var baseline = Mean(legacy);
        var strong = Mean(stronger);
        var ff = Mean(feedForward);
        var all = Mean(both);

        // 2026-11-27（基準の体）: 変更前の体の胸は 3.5 rad/s（前は 3 未満）。人の 8〜12 には遠いまま。上限 3 → 4。
        Assert.IsLessThan(4f, baseline.ChestRate, $"変更前の体の胸は遅い（人 8〜12 rad/s）: {baseline.ChestRate:F2}");
        Assert.IsGreaterThan(baseline.ChestRate * 1.4f, strong.ChestRate, $"トルクを上げると胸が速く回る: {strong.ChestRate:F2} 対 {baseline.ChestRate:F2}");
        // 2026-11-27（基準の体）: 先行で 4.27 対 3.45（1.24 倍）。変更前の体の胸が速くなった（3.45）ので、1.4 倍 → 1.2 倍で「先行で速くなる」順序を見る。
        Assert.IsGreaterThan(baseline.ChestRate * 1.2f, ff.ChestRate, $"目標の速さの先行で胸が速く回る: {ff.ChestRate:F2} 対 {baseline.ChestRate:F2}");
        Assert.IsGreaterThan(ff.ChestRate, all.ChestRate, $"先行にトルクを足すとさらに速い: {all.ChestRate:F2} 対 {ff.ChestRate:F2}");
        Assert.IsGreaterThan(baseline.ShoulderForward, all.ShoulderForward, $"胸が速く回ると肩が前へ出る: {all.ShoulderForward:F2} 対 {baseline.ShoulderForward:F2}");
        Assert.IsGreaterThan(4f, all.ChestRate, $"既定の体の胸は 4 rad/s を超えて回る: {all.ChestRate:F2}");
    }

    [TestMethod]
    public void LoadedCross_ReversesThePelvisBeforeTheChest()
    {
        var tuning = FighterTuning.Default;
        // 腰の最高角速度は間合いを振った平均で比べる（1 本では 1 割の揺れで大小が入れ替わる: 直す前の足の運びでも 9 通りのうち 1 つで逆転、docs/arena/foot_snag.md）。
        var standard = PunchPathTests.Ranges.Average(r => LoadedCross(tuning, r));
        var stronger = PunchPathTests.Ranges.Average(r => LoadedCross(tuning with { PelvisRotatorTorque = tuning.TrunkRotatorTorque }, r));
        Assert.IsGreaterThan(standard, stronger, "腰のトルクを上げると腰の最高角速度が上がる");
    }

    internal static float LoadedCross(FighterTuning tuning, float range = 1.05f)
    {
        using var bout = new PunchLab.Bout(new(PunchLabPath.Standard, "still", "reach", range, false), 1, Bots.BotStyle.MaxTelegraph, tuning with { HipTwistShare = 0.45f, ChestTwistResponseSeconds = 0.025f }, new PunchLab.DriveKnobs());
        for (var guard = 0; bout.PunchTime is null; guard++)
        {
            Assert.IsLessThan(6000, guard);
            bout.Step();
        }

        var fighter = bout.Game.Player;
        var world = bout.Game.World;
        float Yaw(int part) => BodyFrame.DeltaAngle(fighter.Frame.YawRadians,
            BodyFrame.YawOf(Vector3.Transform(Vector3.UnitZ, world.GetState(part).Orientation), fighter.Frame.YawRadians));
        var startPelvis = Yaw(fighter.Parts.Pelvis);
        var startChest = Yaw(fighter.Parts.Chest);
        var loadedPelvis = startPelvis;
        var loadedChest = startChest;
        var pelvisForward = float.PositiveInfinity;
        var chestForward = float.PositiveInfinity;
        var peakFist = 0f;
        var maxPelvis = float.NegativeInfinity;
        var maxChest = float.NegativeInfinity;
        var maxPelvisRate = 0f;
        var maxChestRate = 0f;
        while (bout.PunchTime is { } time && time < 0.25f)
        {
            var pelvis = Yaw(fighter.Parts.Pelvis);
            var chest = Yaw(fighter.Parts.Chest);
            if (time < 0f)
            {
                loadedPelvis = pelvis;
                loadedChest = chest;
            }
            else
            {
                if (pelvis >= startPelvis)
                {
                    pelvisForward = MathF.Min(pelvisForward, time);
                }

                if (chest >= startChest)
                {
                    chestForward = MathF.Min(chestForward, time);
                }

                peakFist = MathF.Max(peakFist, fighter.GloveVelocity(Hand.Right).Length());
                maxPelvis = MathF.Max(maxPelvis, pelvis);
                maxChest = MathF.Max(maxChest, chest);
                maxPelvisRate = MathF.Max(maxPelvisRate, world.GetState(fighter.Parts.Pelvis).AngularVelocity.Y);
                maxChestRate = MathF.Max(maxChestRate, world.GetState(fighter.Parts.Chest).AngularVelocity.Y);
            }

            bout.Step();
        }

        Console.WriteLine($"load pelvis={loadedPelvis - startPelvis:F3} rad chest={loadedChest - startChest:F3} rad; return pelvis={pelvisForward:F4}s chest={chestForward:F4}s; fistPeak={peakFist:F3} m/s");
        var pelvisSweep = (maxPelvis - loadedPelvis) * 180f / MathF.PI;
        var chestSweep = (maxChest - loadedChest) * 180f / MathF.PI;
        Console.WriteLine($"sweep pelvis={pelvisSweep:F2}° chest={chestSweep:F2}° sum={pelvisSweep + chestSweep:F2}°; peak yaw pelvis={maxPelvisRate:F2} chest={maxChestRate:F2} rad/s");
        Assert.IsLessThan(startPelvis, loadedPelvis, "腰が構えから逆回転して溜める");
        Assert.IsLessThan(startChest, loadedChest, "胸が構えから逆回転して溜める");
        Assert.IsTrue(float.IsFinite(chestForward), "胸が前方へ戻る");
        Assert.IsLessThan(chestForward, pelvisForward, "腰が胸より先に構えの向きを通過する");
        return maxPelvisRate;
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
