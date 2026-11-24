using System.Numerics;
using MiniRing.Motion;
using MiniRing.Weapons;

namespace MiniRing.Tests;

/// <summary>
/// 調査用: VR の一人称で刀を片手（右）で持ち、コントローラの向きを止めた・変えたときの刀の追い方（狙いのずれ・切っ先の震え）を手首の調整値ごとに測る。
/// </summary>
[TestClass]
[TestCategory("Probe")]
public sealed class VrOneHandSwordProbe
{
    private const float Dt = 1f / 240f;
    private static readonly Vector3 Head = new(0f, 1.62f, 0f);
    private static readonly Vector3 Left = new(-0.15f, 1.3f, -0.3f);
    private static readonly Vector3 Right = new(0.15f, 1.3f, -0.35f);

    private static VrRawFrame Raw(Quaternion rotation, float leftGrip, float rightGrip) => new(
        Head, Quaternion.Identity, Left, Right, true, true,
        Vector2.Zero, Vector2.Zero, leftGrip, rightGrip, false, true, rotation, rotation);

    internal static (float HoldError, float TipJitter, float Settle, float Overshoot, float TipSpeed) Measure(FighterTuning tuning, WeaponKind kind = WeaponKind.Katana)
    {
        using var game = new BoxingGame(tuning, corners: SwordTraining.Corners(kind, 2.535f)) { HidePlayerHead = true };
        var mapper = new VrPlayerMapper(weaponMode: true) { GripHolds = true, FirstPerson = true, HeldWeapon = kind };
        var aim = Quaternion.CreateFromAxisAngle(Vector3.UnitX, 0.35f); // 切っ先を 20° 上げて前へ
        void Run(Quaternion r, float l, float rr, float seconds, Action<float>? each = null)
        {
            for (var i = 0; i < (int)(seconds * 240f); i++)
            {
                game.Step(mapper.Read(Raw(r, l, rr), game.PlayerSense, Dt), Dt);
                each?.Invoke(i * Dt);
            }
        }

        var weapon = game.WeaponOf(game.Player)!;
        Vector3 Axis() => Vector3.Transform(Vector3.UnitX, game.World.GetState(weapon.Body.BodyId).Orientation);
        Vector3 Tip() { var s = game.World.GetState(weapon.Body.BodyId); return s.Position + Vector3.Transform(Weapon.TipOf(kind), s.Orientation); }
        float Error(Quaternion r)
        {
            var want = Vector3.Transform(-Vector3.UnitZ, mapper.Placement!.Value.Rotation(r));
            return MathF.Acos(Math.Clamp(Vector3.Dot(Vector3.Normalize(want), Vector3.Normalize(Axis())), -1f, 1f)) * 180f / MathF.PI;
        }

        Run(aim, 1f, 1f, 1.7f);
        Run(aim, 0f, 1f, 0.5f); // 左を離して片手
        Assert.IsTrue(game.Player.Wields(Hand.Right) && !game.Player.Wields(Hand.Left), "片手で持っている");
        var series = new List<string>();
        var k = 0;
        Run(aim, 0f, 1f, 1.5f, t => { if (k++ % 12 == 0) { series.Add($"{Error(aim):0.0}"); } });
        Console.WriteLine($"  after release (every 50 ms): {string.Join(" ", series)}");
        var errors = new List<float>();
        var tips = new List<Vector3>();
        var handErr = new List<float>();
        var rel = new List<Vector3>();
        Run(aim, 0f, 1f, 1f, _ => { errors.Add(Error(aim)); tips.Add(Tip()); rel.Add(Tip() - game.Player.GloveCenter(Hand.Right)); handErr.Add(Vector3.Distance(game.Player.GloveCenter(Hand.Right), mapper.Placement!.Value.Point(Right))); });
        Console.WriteLine($"  hand from controller {handErr.Average() * 100f:0.0} cm");
        var mean = tips.Aggregate(Vector3.Zero, (a, b) => a + b) / tips.Count;
        var jitter = MathF.Sqrt(tips.Average(t => (t - mean).LengthSquared()));
        var meanRel = rel.Aggregate(Vector3.Zero, (a, b) => a + b) / rel.Count;
        var relJitter = MathF.Sqrt(rel.Average(t => (t - meanRel).LengthSquared()));
        Console.WriteLine($"  hold: err {errors.Min():0.0}..{errors.Max():0.0} deg, tip jitter {jitter * 1000f:0.0} mm, tip-from-fist jitter {relJitter * 1000f:0.0} mm");

        var turned = Quaternion.CreateFromAxisAngle(Vector3.UnitY, -0.7f) * aim; // 右へ 40°
        var settle = float.NaN;
        var after = new List<float>();
        var speeds = new List<float>();
        var last = Tip();
        var gloveSpeeds = new List<float>();
        var errLate = new List<float>();
        Run(turned, 0f, 1f, 2f, t =>
        {
            var e = Error(turned);
            if (float.IsNaN(settle) && e < 5f) { settle = t; }
            if (!float.IsNaN(settle)) { after.Add(e); }
            var tip = Tip();
            if (t > 1.5f) { speeds.Add((tip - last).Length() / Dt); gloveSpeeds.Add(game.World.GetState(game.Player.ForearmId(Hand.Right)).LinearVelocity.Length()); errLate.Add(e); }
            last = tip;
        });
        Console.WriteLine($"  late: forearm speed {gloveSpeeds.Average():0.000} m/s, err {errLate.Min():0.0}..{errLate.Max():0.0} deg");
        return (errors.Average(), jitter, settle, after.Count == 0 ? float.NaN : after.Max(), speeds.Count == 0 ? 0f : speeds.Average());
    }

    [TestMethod]
    public void OneHandedIronSwordWithTheOldWrist()
    {
        var b = FighterTuning.Default with { PlayerFatigue = false };
        foreach (var (name, t) in new (string, FighterTuning)[] { ("old wrist", b with { GripWristServoHz = 8.94f, GripWristMaxTorque = 10f, GripWristDampingRatio = 1f }), ("grip wrist", b) })
        {
            var m = Measure(t, WeaponKind.IronSword);
            Console.WriteLine($"iron sword {name,-12} hold err {m.HoldError,5:0.0} deg  tip jitter {m.TipJitter * 1000f,6:0.0} mm  settle<5deg {m.Settle,5:0.00} s  max err after {m.Overshoot,5:0.0} deg");
        }
    }

    [TestMethod]
    public void OneHandedKatanaTracking()
    {
        var b = FighterTuning.Default with { PlayerFatigue = false };
        foreach (var (name, t) in new (string, FighterTuning)[]
        {
            ("default", b),
            ("damp2", b with { GripWristDampingRatio = 2f }),
            ("torque30", b with { GripWristMaxTorque = 30f }),
            ("share1", b with { OneHandWristShare = 1f }),
            ("torque30 damp2", b with { GripWristMaxTorque = 30f, GripWristDampingRatio = 2f }),
            ("share1 damp2", b with { OneHandWristShare = 1f, GripWristDampingRatio = 2f }),
            ("torque30 hz15 damp1.5", b with { GripWristMaxTorque = 30f, GripWristServoHz = 15f, GripWristDampingRatio = 1.5f }),
            ("hz18 damp2", b with { GripWristServoHz = 18f, GripWristDampingRatio = 2f }),
            ("hz25 damp2", b with { GripWristServoHz = 25f, GripWristDampingRatio = 2f }),
            ("hz25 damp2 torque20", b with { GripWristServoHz = 25f, GripWristDampingRatio = 2f, GripWristMaxTorque = 20f }),
            ("hz35 damp2 torque20", b with { GripWristServoHz = 35f, GripWristDampingRatio = 2f, GripWristMaxTorque = 20f }),
            ("hz25 damp3 torque20", b with { GripWristServoHz = 25f, GripWristDampingRatio = 3f, GripWristMaxTorque = 20f }),
        })
        {
            var m = Measure(t);
            Console.WriteLine($"{name,-24} hold err {m.HoldError,5:0.0} deg  tip jitter {m.TipJitter * 1000f,6:0.0} mm  settle<5deg {m.Settle,5:0.00} s  max err after {m.Overshoot,5:0.0} deg  tip speed at rest {m.TipSpeed:0.000} m/s");
        }
    }
}
