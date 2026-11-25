using MiniRing.Motion;
using MiniRing.Weapons;

namespace MiniRing.Tests;

/// <summary>
/// 切っ先を目標へ合わせる手首の力（<c>Fighter.DriveBladeTip</c>、2026-11-25、docs/plans/FORM_DECAY_STUDY.md §10）。
/// </summary>
[TestClass]
[TestCategory("slow")]
public sealed class BladeTipTests
{
    private static ulong Checksum(FighterTuning tuning, BoxingCorners corners, float seconds)
    {
        using var game = new BoxingGame(tuning, seed: 3, corners: corners);
        for (var i = 0; i < (int)(seconds * 60f); i++)
        {
            game.Step(default, 1f / 60f);
        }

        return game.StateChecksum();
    }

    [TestMethod]
    public void BareFists_AreUntouched_ByTheBladeTipControl()
    {
        // 素手・グローブの拳は切っ先を持たない（拳そのものが打つ点）: 切っ先の力を入れても切っても、拳の試合はビット単位で同じ。
        var corners = new BoxingCorners("champion", s => Bots.BotCatalog.Create("champion", s), s => Bots.BotCatalog.Create("swarmer", s));
        Assert.AreEqual(Checksum(FighterTuning.Default with { BladeTipHz = 0f }, corners, 10f), Checksum(FighterTuning.Default, corners, 10f));
    }

    [TestMethod]
    public void AHeldKatana_ReportsItsTipAndTheTipTarget_AndHoldsTheTipOnItInAStillGuard()
    {
        // 構えた刀（刀の構えの指令を出し続ける）: 切っ先の目標（手の目標の位置 × 向きの目標）と実際の切っ先が Fighter から読め、構えの間は切っ先が目標の数 cm 以内にある。
        // 止まった構えでは切っ先の力が無くてもずれは 2 cm ほどで、力の効き目（向きを変えて止めたときの振れ）は VrWeaponHandlingTests が見る。
        static float MeanTipError(FighterTuning tuning)
        {
            using var game = new BoxingGame(tuning, seed: 1, corners: new BoxingCorners("stand", _ => new StandGuard(), _ => new GuardOnly(),
                PlayerWeapon: WeaponKind.Katana, PlayerTwoHanded: true));
            var sum = 0f;
            var n = 0;
            for (var i = 0; i < 240 * 4; i++)
            {
                game.Step(default, 1f / 240f);
                foreach (var hand in new[] { Hand.Left, Hand.Right })
                {
                    if (i > 240 && game.Player.BladeTipWorld(hand) is { } tip && game.Player.BladeTipTargetWorld(hand) is { } goal)
                    {
                        sum += System.Numerics.Vector3.Distance(tip, goal);
                        n++;
                    }
                }
            }

            Assert.IsGreaterThan(0, n, "the held katana reports its tip and the tip target");
            return sum / n;
        }

        var off = MeanTipError(SwordStance.Tuning with { BladeTipHz = 0f });
        var on = MeanTipError(SwordStance.Tuning);
        Console.WriteLine($"切っ先のずれ（構えの 3 秒の平均）: 力なし {off * 100f:0.0} cm、力あり {on * 100f:0.0} cm");
        Assert.IsLessThan(0.05f, on, "the tip stays within a few centimetres of its target in a still guard");
    }

    /// <summary>刀の構え（<see cref="Bots.FormSwordsman.GuardCommand"/>）を出し続けるだけの入力元。</summary>
    private sealed class GuardOnly : IFighterControl
    {
        public FighterCommand Read(in FighterSense sense, float dt)
        {
            var face = sense.OpponentChest - sense.Self.Origin;
            face.Y = 0f;
            face = face.LengthSquared() > 1e-6f ? System.Numerics.Vector3.Normalize(face) : System.Numerics.Vector3.UnitZ;
            return Bots.FormSwordsman.GuardCommand(WeaponKind.Katana, sense, face, FighterTuning.Default.WalkSpeed, FighterTuning.Default.MaxTorsoTwistRadians);
        }
    }
}
