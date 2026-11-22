using System.Numerics;
using MiniRing.Arena;

namespace MiniRing.Tests;

/// <summary>
/// 調べもの: 右ストレート 1 本（守らない相手、届く間合い）で、腰の回旋が胸・肩・拳へどれだけ伝わり、拳に体重が乗るか。
/// 体ごと（従来の体・標準の体・腰の弱い標準の体ほか）に、骨盤・腹・胸の鉛直軸まわりの角速度の最大、右肩と拳の前への速さの最大、
/// 骨盤の前への移動、当たった拳の速さ・乗った質量・体の分の割合・エネルギーを並べる。
/// </summary>
[TestClass]
[TestCategory("Probe")]
[DoNotParallelize]
public sealed class PunchWeightProbe
{
    public TestContext TestContext { get; set; } = null!;

    [TestMethod]
    public void Measure()
    {
        var standard = FighterTuning.Default;
        var cases = new (string Name, FighterTuning Tuning)[]
        {
            ("legacy", FighterTuning.Legacy),
            ("standard", standard),
            ("weak-hips", standard with { PelvisRotatorTorque = 60f }),
            ("strong-hips", standard with { PelvisRotatorTorque = 600f }),
            ("weak-trunk", standard with { TrunkRotatorTorque = 120f }),
        };
        foreach (var telegraph in new[] { 0.06f, 0.25f })
        {
            foreach (var (name, tuning) in cases)
            {
                TestContext.WriteLine(Line(name, tuning, telegraph));
            }
        }
    }

    private static string Line(string name, FighterTuning tuning, float telegraph)
    {
        var path = Environment.GetEnvironmentVariable("MINIRING_PUNCH_PATH") == "through" ? PunchLabPath.InstantThrough : PunchLabPath.Standard;
        using var bout = new PunchLab.Bout(new(path, "still", "reach", 1.05f, false), 1, telegraph, tuning);
        while (bout.PunchTime is null)
        {
            bout.Step();
        }

        var f = bout.Game.Player;
        var world = bout.Game.World;
        var startPelvis = f.PelvisPosition;
        float pelvisYaw = 0f, abdomenYaw = 0f, chestYaw = 0f, shoulderPeak = 0f, fistPeak = 0f, pelvisForward = 0f;
        HitEvent? first = null;
        while (bout.PunchTime is { } t && t < 0.45f && first is null)
        {
            var dir = f.Frame.Forward;
            var chest = world.GetState(f.Parts.Chest);
            pelvisYaw = MathF.Max(pelvisYaw, MathF.Abs(world.GetState(f.Parts.Pelvis).AngularVelocity.Y));
            abdomenYaw = f.Parts.Abdomen is { } a ? MathF.Max(abdomenYaw, MathF.Abs(world.GetState(a).AngularVelocity.Y)) : abdomenYaw;
            chestYaw = MathF.Max(chestYaw, MathF.Abs(chest.AngularVelocity.Y));
            var shoulder = f.ShoulderPosition(Hand.Right);
            var shoulderV = chest.LinearVelocity + Vector3.Cross(chest.AngularVelocity, shoulder - chest.Position);
            shoulderPeak = MathF.Max(shoulderPeak, Vector3.Dot(shoulderV, dir));
            fistPeak = MathF.Max(fistPeak, Vector3.Dot(f.GloveVelocity(Hand.Right), dir));
            pelvisForward = MathF.Max(pelvisForward, Vector3.Dot(f.PelvisPosition - startPelvis, dir));
            bout.Step();
            foreach (var hit in bout.Game.RecentHits.Where(h => h.Attacker == f && h.Hand == Hand.Right && !h.Kick && !h.Elbow))
            {
                first ??= hit;
            }
        }

        var h = first;
        var motion = FormattableString.Invariant(
            $"{name,-12} load={telegraph:F2} yaw pelvis/abdomen/chest={pelvisYaw:F1}/{abdomenYaw:F1}/{chestYaw:F1} rad/s shoulder={shoulderPeak:F2} fist={fistPeak:F2} m/s pelvisFwd={pelvisForward * 100f:F1} cm ");
        var impact = h is { } x ? FormattableString.Invariant($"HIT speed={x.Speed:F2} mass={x.ImpactMass:F2} body={x.BodyShare:F2} energy={x.Energy:F1} J") : "no hit";
        if (h is { } y)
        {
            // 当たった瞬間（歩の後）の、拳の向きの両者の関節で連なった有効質量と、腕の伸び（肩 → 拳 ÷ 腕の長さ）・腕の線と拳の向きの揃い。
            var dir = f.GloveVelocity(Hand.Right).LengthSquared() > 1e-6f ? Vector3.Normalize(f.GloveVelocity(Hand.Right)) : f.Frame.Forward;
            var arm = f.GloveCenter(Hand.Right) - f.ShoulderPosition(Hand.Right);
            var striker = f.ArticulatedMass(f.ForearmId(Hand.Right), y.Point, dir, y.Speed);
            var victim = bout.Game.Cpu;
            var struck = victim.ArticulatedMass(victim.Parts.Head, y.Point, dir, y.Speed);
            impact += FormattableString.Invariant($" | m_striker={striker:F2} m_head={struck:F2} extension={arm.Length() / f.ArmReach:F2} align={Vector3.Dot(Vector3.Normalize(arm), dir):F2}");
        }
        return motion + impact;
    }
}
