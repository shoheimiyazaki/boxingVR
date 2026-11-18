using System.Numerics;
using MiniRing.Motion;
using MiniRing.Motion.Stick;

namespace MiniRing.Arena;

/// <summary>
/// 武器の型（<see cref="SwordMotion"/>: 棒術・刀・ロングソード・剣盾の型）を棒人形で走らせる（docs/STICK_MOTION.md §10）。ゲームの体と同じく
/// （<see cref="MotionImitation.ToCommand"/>）、右手・左手の位置（腕の長さ単位の体フレーム）、刃の向き、頭のずれ、胴のひねりを目標にする。
/// 体の移動（<see cref="MotionFrame.Velocity"/>、踏み込み）は走らせない（足は床に貼ったまま。踏み込む型では、その分だけ届かない・崩れる）。
/// </summary>
internal static class StickForm
{
    private const float Dt = 1f / 60f;

    /// <summary>型 <paramref name="form"/> を、キャラ <paramref name="ground"/> の体（武器を握った棒人形）で走らせる。</summary>
    public static StickResult Run(StickGround ground, SwordMotion form)
    {
        var body = ground.Body;
        var tuning = ground.Tuning;
        var reach = body.ArmReach;
        var guard = new StickFrame(body, body.Guard);
        var headTip = body.Segments[StickBody.Ids.Head].Tip;
        var headRest = guard.Tip(StickBody.Ids.Head) - guard.ShoulderCenter;
        var wrists = new[] { body.Segments[StickBody.Ids.ForearmL].Tip, body.Segments[StickBody.Ids.ForearmR].Tip };
        var pelvisRest = body.Segments[StickBody.Ids.Pelvis].Center;
        var pelvisY = guard.Center(StickBody.Ids.Pelvis).Y;
        var neckKp = MathF.Pow(2f * MathF.PI * tuning.NeckServoHz, 2f);
        var neckKd = 4f * MathF.PI * tuning.NeckServoHz;
        var twoHanded = body.GripHands.Count == 2;

        StickStep Step(float t, StickFrame now)
        {
            var frame = form.Sample(t);
            var self = new BodyFrame(now.ShoulderCenter, 0f);
            Vector3 World(Vector3 local) => self.LocalToWorld(local);
            var right = frame.Right * reach;
            var targets = new List<StickTarget>
            {
                new(StickBody.Ids.ForearmR, wrists[1], World(right), 1f, tuning.HandKp, tuning.HandKd),
                new(StickBody.Ids.Head, headTip, World(headRest + (frame.Head * reach)), 1f, neckKp, neckKd),
            };
            if (twoHanded || frame.Left != default)
            {
                targets.Add(new StickTarget(StickBody.Ids.ForearmL, wrists[0], World(frame.Left * reach), 1f, tuning.HandKp, tuning.HandKd));
            }

            if (body.Armed)
            {
                targets.Add(new StickTarget(StickBody.Ids.Weapon, body.Segments[StickBody.Ids.Weapon].Tip, World(right + (frame.Blade * ground.WeaponLength)), 1f, tuning.HandKp, tuning.HandKd));
            }

            var feet = 0.5f * (Ball(now, Hand.Left) + Ball(now, Hand.Right));
            targets.Add(new StickTarget(StickBody.Ids.Pelvis, pelvisRest, new Vector3(feet.X, pelvisY, feet.Y), 1f, tuning.MoveGain * tuning.Footwork.Centering, tuning.MoveGain));
            return new StickStep(targets, (true, true), [new StickYaw(StickBody.Ids.Chest, frame.Twist, 0.3f)]);
        }

        return StickMotion.Run(body, Step, form.Duration, Dt);
    }

    private static Vector2 Ball(StickFrame frame, Hand leg)
    {
        var sole = frame.Sole(leg).ToArray();
        var ball = 0.5f * (sole[2] + sole[3]);
        return new Vector2(ball.X, ball.Z);
    }
}
