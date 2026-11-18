using System.Numerics;
using MiniRing.Motion;
using MiniRing.Weapons;

namespace MiniRing.Tests;

/// <summary>調べもの: 何も入力しないとき（空の入力・静止した VR の姿勢）プレイヤーの体がどれだけ水平に流れるか。</summary>
[TestClass]
public sealed class SlideProbe
{
    public TestContext TestContext { get; set; } = null!;

    private sealed class IdleFoe : IFighterControl
    {
        public FighterCommand Read(in FighterSense sense, float dt)
        {
            var face = sense.OpponentChest - sense.Self.Origin;
            face.Y = 0f;
            return new FighterCommand(Vector3.Zero, face, new HandCommand(new Vector3(0.1f, 0f, 0.15f), false), new HandCommand(new Vector3(-0.1f, 0f, 0.15f), false));
        }
    }

    private static readonly Quaternion Facing = Quaternion.CreateFromAxisAngle(Vector3.UnitY, MathF.PI);

    private static VrRawFrame Raw(float time, float headYaw, float handZ, float sway)
    {
        var look = Quaternion.CreateFromAxisAngle(Vector3.UnitY, MathF.PI + headYaw);
        var s = sway * MathF.Sin(2f * MathF.PI * 0.3f * time);
        var c = sway * MathF.Cos(2f * MathF.PI * 0.23f * time);
        var head = new Vector3(s, 1.58f + (0.5f * c), c);
        return new VrRawFrame(head, look,
            new Vector3(0.12f + s, 1.25f, handZ - 0.05f + c), new Vector3(-0.12f + s, 1.30f, handZ + c), true, true,
            Vector2.Zero, Vector2.Zero, 0f, 0f, false, true, Facing, Facing, 0f, 0f);
    }

    [TestMethod]
    public void Table()
    {
        TestContext.WriteLine("| 条件 | 骨盤 5s [cm] | 10s | 20s | 30s | 左足 30s | 右足 30s | 骨盤の最大の速さ [m/s] | 向きの変化 [deg] |");
        TestContext.WriteLine("|---|---|---|---|---|---|---|---|---|");
        foreach (var (name, weapon, mode, handZ, headYaw, sway, vr) in new (string, WeaponKind?, int, float, float, float, bool)[]
        {
            ("katana 空の入力", WeaponKind.Katana, 0, 0.3f, 0f, 0f, false),
            ("katana VR 構え", WeaponKind.Katana, 1, 0.3f, 0f, 0f, false),
            ("katana VR 構え vrTuning", WeaponKind.Katana, 1, 0.3f, 0f, 0f, true),
            ("katana VR 腕を前へ 0.5", WeaponKind.Katana, 1, 0.5f, 0f, 0f, true),
            ("katana VR 頭 40 度横", WeaponKind.Katana, 1, 0.3f, 0.7f, 0f, true),
            ("katana VR 頭のゆれ 2 cm", WeaponKind.Katana, 1, 0.3f, 0f, 0.02f, true),
            ("katana VR 頭のゆれ 5 cm", WeaponKind.Katana, 1, 0.3f, 0f, 0.05f, true),
            ("素手 空の入力", null, 0, 0.3f, 0f, 0f, false),
            ("素手 VR 構え", null, 2, 0.3f, 0f, 0f, true),
            ("素手 VR 頭のゆれ 2 cm", null, 2, 0.3f, 0f, 0.02f, true),
            ("素手 VR 頭 40 度横", null, 2, 0.3f, 0.7f, 0f, true),
        })
        {
            Run(name, weapon, mode, handZ, headYaw, sway, vr);
        }
    }

    private void Run(string name, WeaponKind? weapon, int mode, float handZ, float headYaw, float sway, bool vr)
    {
        var corners = weapon is { } w
            ? SwordTraining.Corners(w, 0.9f)
            : new BoxingCorners("idle", _ => new IdleFoe(), Referee: false, BreakActions: false, Ring: RingLayout.DrillBoxing);
        using var game = new BoxingGame(vr ? FighterTuning.Default with { PlayerFatigue = false } : null, corners: corners);
        var mapper = new VrPlayerMapper(weaponMode: mode == 1);
        const float dt = 1f / 240f;
        var p = game.Player;
        var start = Flat(p.PelvisPosition);
        var l0 = Flat(p.AnklePosition(Hand.Left));
        var r0 = Flat(p.AnklePosition(Hand.Right));
        var fwd0 = Yaw(p);
        var cells = new float[4];
        var maxSpeed = 0f;
        var last = start;
        for (var i = 1; i <= 30 * 240; i++)
        {
            var input = mode == 0 ? default : mapper.Read(Raw(i * dt, headYaw, handZ, sway), game.PlayerSense, dt);
            game.Step(input, dt);
            var now = Flat(p.PelvisPosition);
            maxSpeed = MathF.Max(maxSpeed, (now - last).Length() / dt);
            last = now;
            foreach (var (k, sec) in new[] { (0, 5), (1, 10), (2, 20), (3, 30) })
            {
                if (i == sec * 240)
                {
                    cells[k] = (now - start).Length() * 100f;
                }
            }
        }

        var dl = (Flat(p.AnklePosition(Hand.Left)) - l0).Length() * 100f;
        var dr = (Flat(p.AnklePosition(Hand.Right)) - r0).Length() * 100f;
        var dyaw = (Yaw(p) - fwd0) * 180f / MathF.PI;
        TestContext.WriteLine($"| {name} | {cells[0]:0.0} | {cells[1]:0.0} | {cells[2]:0.0} | {cells[3]:0.0} | {dl:0.0} | {dr:0.0} | {maxSpeed:0.00} | {dyaw:0.0} |");
    }

    private static Vector3 Flat(Vector3 v) => new(v.X, 0f, v.Z);

    private static float Yaw(Fighter f)
    {
        var d = f.ChestPosition - f.PelvisPosition;
        return MathF.Atan2(d.X, d.Z);
    }
}
