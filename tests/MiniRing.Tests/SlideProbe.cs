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

    private static VrRawFrame Raw(float grip, float trigger) => new(
        new Vector3(0f, 1.58f, 0f), Facing,
        new Vector3(0.12f, 1.25f, 0.25f), new Vector3(-0.12f, 1.30f, 0.30f), true, true,
        Vector2.Zero, Vector2.Zero, grip, grip, false, true, Facing, Facing, trigger, trigger);

    [TestMethod]
    public void Table()
    {
        TestContext.WriteLine("| 条件 | 骨盤 5s [cm] | 10s | 20s | 左足 20s | 右足 20s | 骨盤の最大の速さ [m/s] | 向きの変化 20s [deg] |");
        TestContext.WriteLine("|---|---|---|---|---|---|---|---|");
        foreach (var (name, weapon, mode, grip, trigger) in new (string, WeaponKind?, int, float, float)[]
        {
            ("katana 空の入力", WeaponKind.Katana, 0, 0, 0),
            ("katana VR 武器 ボタン無し", WeaponKind.Katana, 1, 0, 0),
            ("katana VR 武器 トリガー", WeaponKind.Katana, 1, 0, 1),
            ("katana VR 武器 グリップ", WeaponKind.Katana, 1, 1, 0),
            ("katana VR 武器 両方", WeaponKind.Katana, 1, 1, 1),
            ("素手 空の入力", null, 0, 0, 0),
            ("素手 VR ボタン無し", null, 2, 0, 0),
            ("素手 VR グリップ", null, 2, 1, 0),
        })
        {
            Run(name, weapon, mode, grip, trigger);
        }
    }

    private void Run(string name, WeaponKind? weapon, int mode, float grip, float trigger)
    {
        var corners = weapon is { } w
            ? SwordTraining.Corners(w, 0.9f)
            : new BoxingCorners("idle", _ => new IdleFoe(), Referee: false, BreakActions: false, Ring: RingLayout.DrillBoxing);
        using var game = new BoxingGame(corners: corners);
        var mapper = new VrPlayerMapper(weaponMode: mode == 1);
        var raw = Raw(grip, trigger);
        const float dt = 1f / 240f;
        var p = game.Player;
        var start = Flat(p.PelvisPosition);
        var l0 = Flat(p.AnklePosition(Hand.Left));
        var r0 = Flat(p.AnklePosition(Hand.Right));
        var fwd0 = Yaw(p);
        var cells = new float[3];
        var maxSpeed = 0f;
        var last = start;
        for (var i = 1; i <= 20 * 240; i++)
        {
            var input = mode == 0 ? default : mapper.Read(raw, game.PlayerSense, dt);
            game.Step(input, dt);
            var now = Flat(p.PelvisPosition);
            maxSpeed = MathF.Max(maxSpeed, (now - last).Length() / dt);
            last = now;
            if (i == 5 * 240)
            {
                cells[0] = (now - start).Length() * 100f;
            }

            if (i == 10 * 240)
            {
                cells[1] = (now - start).Length() * 100f;
            }
        }

        cells[2] = (Flat(p.PelvisPosition) - start).Length() * 100f;
        var dl = (Flat(p.AnklePosition(Hand.Left)) - l0).Length() * 100f;
        var dr = (Flat(p.AnklePosition(Hand.Right)) - r0).Length() * 100f;
        var dyaw = (Yaw(p) - fwd0) * 180f / MathF.PI;
        TestContext.WriteLine($"| {name} | {cells[0]:0.0} | {cells[1]:0.0} | {cells[2]:0.0} | {dl:0.0} | {dr:0.0} | {maxSpeed:0.00} | {dyaw:0.0} |");
    }

    private static Vector3 Flat(Vector3 v) => new(v.X, 0f, v.Z);

    private static float Yaw(Fighter f)
    {
        var d = f.ChestPosition - f.PelvisPosition;
        return MathF.Atan2(d.X, d.Z);
    }
}
