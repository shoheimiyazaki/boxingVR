using System.Numerics;
using MiniRing.Bots;
using MiniRing.Characters;
using MiniRing.Weapons;

namespace MiniRing.Tests;

[TestClass]
[TestCategory("Probe")]
public sealed class ClawAimProbe
{
    [TestMethod]
    public void GuardTipError()
    {
        var creature = CharacterRoster.Get("galva");
        var target = CharacterRoster.Get("marcus");
        var corners = BotCatalog.Corners("boxer") with
        {
            PlayerBody = creature.Build, PlayerWear = creature.Wear, PlayerLook = creature.Look, PlayerFaceSpec = creature.Face,
            CpuBody = target.Build, CpuWear = HandWear.None, CpuLook = target.Look, CpuFaceSpec = target.Face,
            ExternalBlue = true, Referee = false, Ring = RingLayout.Boxing with { StartGap = 4f, FromCorners = false },
        };
        using var game = new BoxingGame(seed: 1, corners: corners);
        var f = game.Player;
        var guard = new FighterCommand(Vector3.Zero, Vector3.UnitZ, ClawStroke.Guard(Hand.Left, f.ArmReach).ToCommand(Hand.Left),
            ClawStroke.Guard(Hand.Right, f.ArmReach).ToCommand(Hand.Right), ClawBot.Stance(f.ArmReach));
        var blue = new FighterCommand(Vector3.Zero, -Vector3.UnitZ, new HandCommand(AiControl.Guard(Hand.Left, 0.7f), false), new HandCommand(AiControl.Guard(Hand.Right, 0.7f), false));
        for (var i = 0; i < 216; i++)
        {
            game.Step(new BoxingInput(guard, Blue: blue), 1f / 240f);
        }

        var hand = Hand.Right;
        var want = ClawStroke.Guard(hand, f.ArmReach);
        var claw = game.GloveOf(f, hand)!;
        var state = game.World.GetState(claw.Body.BodyId);
        var self = game.PlayerSense.Self;
        var root = self.WorldToLocal(state.Position + Vector3.Transform(Weapon.GripOf(claw.Kind), state.Orientation));
        var tip = self.WorldToLocal(state.Position + Vector3.Transform(Weapon.GripOf(claw.Kind) + SickleClawShape.Edge(hand), state.Orientation));
        var a = Vector3.Normalize(want.Tip - want.Root);
        var b = Vector3.Normalize(tip - root);
        Console.WriteLine($"guard blade angle error {MathF.Acos(Math.Clamp(Vector3.Dot(a, b), -1f, 1f)) * 180f / MathF.PI:0.0} deg, root error {Vector3.Distance(root, want.Root) * 100f:0} cm, mass {game.World.GetMass(claw.Body.BodyId):0.00}");
    }
}
