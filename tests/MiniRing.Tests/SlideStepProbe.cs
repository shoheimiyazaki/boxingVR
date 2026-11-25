using System.Numerics;
using MiniRing.Motion;
using MiniRing.Weapons;

namespace MiniRing.Tests;

/// <summary>
/// 調査用（docs/plans/FORM_DECAY_STUDY.md §9）: SlideTests の VR の刀の構えで、骨盤の流れがどの踏み直しで増えたか（時刻・その歩の前後の流れ）と、
/// 刀の握りまわりの慣性モーメント。
/// </summary>
[TestClass]
[TestCategory("Probe")]
public sealed class SlideStepProbe
{
    private const float Dt = 1f / 240f;
    private static readonly Quaternion Facing = Quaternion.CreateFromAxisAngle(Vector3.UnitY, MathF.PI);

    private static VrRawFrame Raw(float time, float sway, float headYaw = 0f)
    {
        var look = Quaternion.CreateFromAxisAngle(Vector3.UnitY, MathF.PI + headYaw);
        var s = sway * MathF.Sin(2f * MathF.PI * 0.3f * time);
        var c = sway * MathF.Cos(2f * MathF.PI * 0.23f * time);
        return new VrRawFrame(new Vector3(s, 1.58f + (0.5f * c), c), look,
            new Vector3(0.12f + s, 1.25f, 0.25f + c), new Vector3(-0.12f + s, 1.30f, 0.30f + c), true, true,
            Vector2.Zero, Vector2.Zero, 0f, 0f, false, true, Facing, Facing, 0f, 0f);
    }

    [TestMethod]
    [TestCategory("Probe")]
    public void StepsAndDrift()
    {
        foreach (var (sway, yaw, torque) in new[] { (0f, 0f, 30f), (0.02f, 0f, 30f), (0f, 0.7f, 30f), (0f, 0f, 20f), (0.02f, 0f, 20f), (0f, 0.7f, 20f) })
        {
            using var game = new BoxingGame(FighterTuning.Default with { PlayerFatigue = false, GripWristMaxTorque = torque }, corners: SwordTraining.Corners(WeaponKind.Katana, 2.535f));
            var mapper = new VrPlayerMapper(weaponMode: true);
            var start = game.Player.PelvisPosition with { Y = 0f };
            var steps = game.Player.Footwork.Steps;
            var lines = new List<string>();
            for (var i = 1; i <= (int)(33.57f / Dt); i++)
            {
                game.Step(mapper.Read(Raw(i * Dt, sway, yaw), game.PlayerSense, Dt), Dt);
                if (game.Player.Footwork.Steps != steps)
                {
                    steps = game.Player.Footwork.Steps;
                    lines.Add($"{i * Dt:0.00}s 歩 {steps} 流れ {((game.Player.PelvisPosition with { Y = 0f }) - start).Length() * 100f:0.0} cm");
                }
            }

            Console.WriteLine($"揺れ {sway * 100f:0} cm・頭の向き {yaw:0.0}・手首 {torque:0} N·m: 最後の流れ {((game.Player.PelvisPosition with { Y = 0f }) - start).Length() * 100f:0.0} cm");
            Console.WriteLine(string.Join("\n", lines));
        }

        foreach (var kind in new[] { WeaponKind.Katana })
        {
            var g = Weapon.Build(kind);
            var grip = Weapon.GripOf(kind);
            double inertia = 0, mass = 0;
            for (var x = 0; x < g.Dimensions.X; x++)
            {
                for (var y = 0; y < g.Dimensions.Y; y++)
                {
                    for (var z = 0; z < g.Dimensions.Z; z++)
                    {
                        var m = g.At(x, y, z);
                        if (m == 0)
                        {
                            continue;
                        }

                        var cellMass = g.CellVolume * g.Palette[m].MechanicsOrDefault.Density;
                        var p = g.CellCenter(x, y, z) - grip;
                        inertia += cellMass * ((p.X * p.X) + (p.Z * p.Z));
                        mass += cellMass;
                    }
                }
            }

            Console.WriteLine($"{kind}: 質量 {mass:0.000} kg、握りのまわりの慣性モーメント（刃の面に直角の軸）{inertia:0.0000} kg·m²");
        }
    }
}
