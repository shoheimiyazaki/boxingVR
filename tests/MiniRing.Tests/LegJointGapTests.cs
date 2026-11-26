using System.Numerics;
using Aigis.Physics;
using MiniRing.Arena;
using MiniRing.Bots;

namespace MiniRing.Tests;

/// <summary>
/// 蹴りで振られる脚の関節が開かない（2026-11-25、ユーザーの指摘「つま先がかかとから離れないように。蹴りで大きな隙間ができる」）。
/// つま先の蝶番の位置の拘束が柔らかい（10.72 Hz）と、足を振る間につま先が母指球の点から 9〜44 cm 開いた。
/// 肘と同じ固さ（<see cref="FighterTuning.ToeHingeHz"/>）では、足が相手に触れる前の振りの間は数 mm〜2 cm。足首・膝の開きは測って出すだけ（docs/plans/TOE_GAP_STUDY.md の残り）。
/// 当たった瞬間の開き（止める力積が足だけに掛かる）は docs/plans/TOE_GAP_STUDY.md の残り。
/// </summary>
[TestClass]
[TestCategory("slow")]
public sealed class LegJointGapTests
{
    private static (float Toe, float Ankle, float Knee) SwingGaps(FighterTuning tuning, KickShin.Height h, bool rear)
    {
        var tunes = KickTunes.Load(h.File);
        var tune = rear ? tunes.Rear : tunes.Front;
        var spec = KickSpec.Of(h.Technique) with { Tunes = new KickTunes(tune, tune) };
        var foot = rear ? Footwork.Rear : Footwork.Lead;
        var heelHalf = tuning.HalfExtents.Foot;
        float toe = 0f, ankle = 0f, knee = 0f;
        var touched = false;
        KickMocap.Doll(BodyBuild.Standard, spec, rear ? Hand.Right : Hand.Left, tune.Standoff, tune.Seconds, 1, (game, _, time) =>
        {
            touched |= game.RecentHits.Any(x => x.Kick) || game.RecentCuts.Count > 0;
            if (touched || time > tune.Seconds)
            {
                return;
            }

            var me = game.Player;
            var heel = game.World.GetState(me.HeelId(foot));
            var tip = game.World.GetState(me.ToeId(foot));
            toe = MathF.Max(toe, Vector3.Distance(heel.Position + Vector3.Transform(FootGeometry.BallOnHeel(heelHalf), heel.Orientation),
                tip.Position + Vector3.Transform(FootGeometry.BallOnToe(heelHalf), tip.Orientation)));
            foreach (var joint in new[] { HumanoidRagdoll.RagdollJoint.LeftAnkle, HumanoidRagdoll.RagdollJoint.RightAnkle })
            {
                ankle = MathF.Max(ankle, me.JointGap(joint) ?? 0f);
            }

            foreach (var joint in new[] { HumanoidRagdoll.RagdollJoint.LeftKnee, HumanoidRagdoll.RagdollJoint.RightKnee })
            {
                knee = MathF.Max(knee, me.JointGap(joint) ?? 0f);
            }
        }, tuning: tuning);
        return (toe, ankle, knee);
    }

    [TestMethod]
    public void AKickingLeg_StaysJoinedWhileItSwings()
    {
        var mid = KickShin.Heights.First(x => x.File == "roundhouse_mid");
        var now = SwingGaps(FighterTuning.Default, mid, rear: true);
        Console.WriteLine($"振りの間の最大の開き（中段・後ろの足）: つま先 {now.Toe * 1000f:0} mm・足首 {now.Ankle * 1000f:0} mm・膝 {now.Knee * 1000f:0} mm");
        Assert.IsLessThan(0.03f, now.Toe, "the toe stays on its hinge at the ball of the foot");
    }
}
