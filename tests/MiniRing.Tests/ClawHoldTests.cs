using System.Numerics;
using MiniRing.Bots;
using MiniRing.Characters;
using MiniRing.Weapons;

namespace MiniRing.Tests;

/// <summary>
/// はめた鎌を止めて構えたとき、刃が目標の向きに落ち着く（2026-11-25 の再発防止）。手首の遅れを腕の目標へ足し戻す帰還が、固めた手首と発振して
/// 前腕が 10〜30 rad/s で回り、横向きの構えで刃が 80° 外れていた（ClawAimProbe）。
/// </summary>
[TestClass]
[TestCategory("slow")]
public sealed class ClawHoldTests
{
    [TestMethod]
    [DataRow(-0.6f, 0.3f, 0.7f, 1f, 0f, 0f, 0f, 1f, 0f, DisplayName = "level across", IgnoreMessage = "既知: 鎌の向きと前腕の揺れ・BASE_BODY_PLAN §10.8")]
    [DataRow(-0.5f, 0.3f, 0.4f, 0f, 1f, 0.3f, 0f, 0f, 1f, DisplayName = "up", IgnoreMessage = "既知: 鎌の向きと前腕の揺れ・BASE_BODY_PLAN §10.8")]
    public void HeldClaw_SettlesOnTheCommandedBlade(float rx, float ry, float rz, float ax, float ay, float az, float nx, float ny, float nz)
    {
        var creature = CharacterRoster.Get("galva");
        var target = CharacterRoster.Get("marcus");
        var corners = BotCatalog.Corners("boxer") with
        {
            PlayerBody = creature.Build, PlayerWear = creature.Wear, PlayerLook = creature.Look, PlayerFaceSpec = creature.Face,
            CpuBody = target.Build, CpuWear = HandWear.None, CpuLook = target.Look, CpuFaceSpec = target.Face,
            ExternalBlue = true, Referee = false, Ring = RingLayout.Boxing with { StartGap = 4f, FromCorners = false },
        };
        using var game = new BoxingGame(new FighterTuning(), seed: 1, corners: corners);
        var f = game.Player;
        var root = new Vector3(rx, ry, rz) * f.ArmReach;
        var want = new ClawSegment(root, root + (Vector3.Normalize(new Vector3(ax, ay, az)) * ClawSegment.Length), new Vector3(nx, ny, nz));
        var command = new FighterCommand(Vector3.Zero, Vector3.UnitZ, ClawStroke.Guard(Hand.Left, f.ArmReach).ToCommand(Hand.Left), want.ToCommand(Hand.Right), ClawBot.Stance(f.ArmReach));
        var blue = new FighterCommand(Vector3.Zero, -Vector3.UnitZ, new HandCommand(AiControl.Guard(Hand.Left, 0.7f), false), new HandCommand(AiControl.Guard(Hand.Right, 0.7f), false));
        for (var i = 0; i < 240; i++)
        {
            game.Step(new BoxingInput(command, Blue: blue), 1f / 240f);
        }

        var claw = game.GloveOf(f, Hand.Right)!;
        var state = game.World.GetState(claw.Body.BodyId);
        var self = game.PlayerSense.Self;
        var gotRoot = self.WorldToLocal(state.Position + Vector3.Transform(Weapon.GripOf(claw.Kind), state.Orientation));
        var gotTip = self.WorldToLocal(state.Position + Vector3.Transform(Weapon.GripOf(claw.Kind) + SickleClawShape.Edge(Hand.Right), state.Orientation));
        var angle = MathF.Acos(Math.Clamp(Vector3.Dot(Vector3.Normalize(want.Tip - want.Root), Vector3.Normalize(gotTip - gotRoot)), -1f, 1f)) * 180f / MathF.PI;
        Assert.IsLessThan(15f, angle, $"刃の向きのずれ {angle:0.0}°");
        Assert.IsLessThan(0.08f, Vector3.Distance(gotRoot, want.Root), "刃元が目標に着く");
        Assert.IsLessThan(8f, state.AngularVelocity.Length(), "構えたまま鎌が回り続けない");
    }
}
