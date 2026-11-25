using System.Numerics;
using MiniRing.Motion;
using MiniRing.Weapons;

namespace MiniRing.Tests;

/// <summary>調査用（docs/plans/FORM_DECAY_STUDY.md）: SlideTests の「胴をひねった VR の刀の構え」の流れを、握り込んだ手首の値ごとに測る。</summary>
[TestClass]
[TestCategory("Probe")]
public sealed class WristSlideProbe
{
    private const float Dt = 1f / 240f;
    private static readonly Quaternion Facing = Quaternion.CreateFromAxisAngle(Vector3.UnitY, MathF.PI);

    private static VrRawFrame Raw(float headYaw)
    {
        var look = Quaternion.CreateFromAxisAngle(Vector3.UnitY, MathF.PI + headYaw);
        return new VrRawFrame(new Vector3(0f, 1.58f, 0f), look, new Vector3(0.12f, 1.25f, 0.25f), new Vector3(-0.12f, 1.30f, 0.30f), true, true,
            Vector2.Zero, Vector2.Zero, 0f, 0f, false, true, Facing, Facing, 0f, 0f);
    }

    private static (float Drift, int Steps) Run(FighterTuning tuning)
    {
        using var game = new BoxingGame(tuning with { PlayerFatigue = false }, corners: SwordTraining.Corners(WeaponKind.Katana, 2.535f));
        var mapper = new VrPlayerMapper(weaponMode: true);
        var start = game.Player.PelvisPosition with { Y = 0f };
        var steps0 = 0;
        for (var i = 1; i <= (int)(33.57f / Dt); i++)
        {
            game.Step(mapper.Read(Raw(0.7f), game.PlayerSense, Dt), Dt);
            if (i == (int)(5f / Dt))
            {
                steps0 = game.Player.Footwork.Steps;
            }
        }

        return (((game.Player.PelvisPosition with { Y = 0f }) - start).Length(), game.Player.Footwork.Steps - steps0);
    }

    [TestMethod]
    [TestCategory("Probe")]
    public void DriftByWrist()
    {
        var b = FighterTuning.Default;
        var variants = new (string Name, FighterTuning T)[]
        {
            ("減衰 3・45 N·m", b with { GripWristDampingRatio = 3f, GripWristMaxTorque = 45f }),
            ("減衰 3・60 N·m", b with { GripWristDampingRatio = 3f, GripWristMaxTorque = 60f }),
            ("減衰 3・90 N·m", b with { GripWristDampingRatio = 3f, GripWristMaxTorque = 90f }),
            ("減衰 4・30 N·m", b with { GripWristDampingRatio = 4f }),
            ("減衰 3・30 N·m・片手 0.5", b with { GripWristDampingRatio = 3f, OneHandWristShare = 0.5f }),
            ("減衰 3・腕 Hz ×1.5", b with { GripWristDampingRatio = 3f, ArmServoHz = b.ArmServoHz * 1.5f }),
        };
        var rows = new (float, int)[variants.Length];
        Parallel.For(0, variants.Length, i => rows[i] = Run(variants[i].T));
        for (var i = 0; i < variants.Length; i++)
        {
            var one = VrOneHandSwordProbe.Measure(variants[i].T with { PlayerFatigue = false });
            Console.WriteLine($"{variants[i].Name}: 流れ {rows[i].Item1 * 100f:0.0} cm、踏み直し {rows[i].Item2} 歩 / 片手の刀: 垂れ {one.HoldError:0.0}°・入るまで {one.Settle:0.00} s・振れ戻り {one.Overshoot:0.0}°");
        }
    }
}
