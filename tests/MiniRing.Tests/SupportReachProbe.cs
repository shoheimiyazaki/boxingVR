using System.Globalization;
using System.Numerics;
using MiniRing.Bots;
using MiniRing.Motion;
using MiniRing.Weapons;

namespace MiniRing.Tests;

/// <summary>
/// 調べ物（2026-11-27、基準の体の移行）: 刀の両手の構えで、添える左手が柄頭の側の握りへ届くか。左の拳・左肩から握りまでの距離と腕の長さを時間で出す。
/// </summary>
[TestClass]
[TestCategory("Probe")]
public sealed class SupportReachProbe
{
    private sealed class Guard : IFighterControl
    {
        public FighterCommand Read(in FighterSense sense, float dt)
        {
            var face = sense.OpponentChest - sense.Self.Origin;
            face.Y = 0f;
            face = face.LengthSquared() > 1e-6f ? Vector3.Normalize(face) : Vector3.UnitZ;
            return FormSwordsman.GuardCommand(WeaponKind.Katana, sense, face, FighterTuning.Default.WalkSpeed, FighterTuning.Default.MaxTorsoTwistRadians);
        }
    }

    [TestMethod]
    public void KatanaGuard_LeftHandReach()
    {
        using var game = new BoxingGame(seed: 1, corners: SwordTraining.Corners(WeaponKind.Katana, 3.38f) with { Player = _ => new Guard() });
        var f = game.Player;
        var sword = game.WeaponOf(f)!;
        var (top, bottom) = Weapon.TwoHandGripsOf(WeaponKind.Katana);
        var inv = CultureInfo.InvariantCulture;
        Console.WriteLine(string.Create(inv, $"reach {f.ArmReach:F3} grips top {top.X:F3} bottom {bottom.X:F3} shoulder half {f.HalfExtents.Chest.X:F3}"));
        for (var i = 0; i <= 120; i++)
        {
            if (i % 10 == 0)
            {
                var s = game.World.GetState(sword.Body.BodyId);
                var grip = s.Position + Vector3.Transform(bottom, s.Orientation);
                var main = s.Position + Vector3.Transform(top, s.Orientation);
                var left = f.GloveCenter(Hand.Left);
                var shoulder = f.ShoulderPosition(Hand.Left);
                var rs = f.ShoulderPosition(Hand.Right);
                var local = f.Frame.WorldToLocal(main);
                Console.WriteLine(string.Create(inv,
                    $"t {i / 60f:F2} support {f.SupportOf(Hand.Right)} glove→grip {Vector3.Distance(left, grip):F3} shoulderL→grip {Vector3.Distance(shoulder, grip):F3} shoulderR→main {Vector3.Distance(rs, main):F3} main local ({local.X:F2},{local.Y:F2},{local.Z:F2})"));
            }

            game.Step(default, 1f / 60f);
        }
    }
}
