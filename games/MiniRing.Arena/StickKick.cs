using System.Numerics;
using MiniRing.Bots;
using MiniRing.Motion.Stick;

namespace MiniRing.Arena;

/// <summary>
/// 蹴りの経路を棒人形で走らせる（docs/STICK_MOTION.md §6）: ゲームのボット（BoxerBot）が出すのと同じ目標の時間変化
/// （<see cref="KickSpec.Evaluate"/> の足首・膝の目標、体の向き <see cref="KickSpec.FaceYaw"/>・胴のひねり <see cref="KickSpec.TorsoTwist"/>、
/// 体重移し <c>BotStyle.KickShift</c>（投げの枝） の間と頭の寄せ）を、棒人形の目標にする。相手は構えた自分を鏡に写して、その蹴りの届く間合いに置く。
/// </summary>
internal sealed class StickKick
{
    /// <summary>蹴る前に構えている時間 [s]。</summary>
    private const float Before = 0.2f;

    /// <summary>蹴り終えてから見続ける時間 [s]（足を下ろして立ち直れるか）。</summary>
    private const float After = 0.6f;

    /// <summary>蹴り終えた足を、蹴る前に立っていた所へ（足の PD で）下ろす時間 [s]。その後に床へ貼る（一瞬で床へ引き下ろさない）。</summary>
    private const float Landing = 0.2f;

    private readonly StickBody _body;
    private readonly LegBody[] _legs;
    private readonly FighterTuning _tuning;

    public StickKick(StickBody body, LegBody left, LegBody right, FighterTuning tuning)
    {
        _body = body;
        _legs = [left, right];
        _tuning = tuning;
    }

    /// <summary>頭を目標へ追う固さ: 首のサーボ（<see cref="FighterTuning.NeckServoHz"/>）の固有角振動数の 2 乗と、臨界減衰。</summary>
    private float NeckKp => MathF.Pow(2f * MathF.PI * _tuning.NeckServoHz, 2f);

    private float NeckKd => 2f * 2f * MathF.PI * _tuning.NeckServoHz;

    /// <summary>標準体格の棒人形と、同じ体の脚の寸法（ゲームの体を立たせて測る）。</summary>
    public static StickKick Standard()
    {
        var tuning = FighterTuning.Default;
        using var game = new BoxingGame(tuning, seed: 1, corners: new BoxingCorners("stick", _ => new StandGuard(), _ => new StandGuard()));
        var body = StickBody.FromFighter(game, game.Cpu, tuning);
        return new StickKick(body, game.Cpu.LegBodyOf(Hand.Left), game.Cpu.LegBodyOf(Hand.Right), tuning);
    }

    public StickBody Body => _body;

    /// <summary>体重移しの教育の値（<c>BotStyle.KickShift</c>（投げの枝）・<c>BotStyle.KickLeanForward</c>（投げの枝）・<c>BotStyle.KickLeanSide</c>（投げの枝））。</summary>
    internal readonly record struct Teaching(float Shift, float Forward, float Side);

    /// <summary>蹴り <paramref name="kind"/> を、スタイル <paramref name="style"/>（構えの向き・蹴りの長さ）と教育の値で走らせる。</summary>
    /// <param name="kind">蹴りの種類。</param>
    /// <param name="style">スタイル（構えの向き・蹴りの長さ）。</param>
    /// <param name="teaching">体重移しの教育の値。</param>
    /// <param name="dt">歩の間隔 [s]。</param>
    /// <param name="settings">IK の重み（null なら動きの既定）。</param>
    /// <param name="approach">蹴り始めに体が前へ動いている速さ [m/s]（ゲームのボットは歩いて詰めながら蹴る: kickbalance で測ると 0.3〜0.4 m/s）。</param>
    public StickResult Run(KickTechnique kind, BotStyle style, Teaching teaching, float approach = 0f, float dt = 1f / 60f, StickIkSettings? settings = null)
    {
        var spec = KickSpec.Of(kind);
        var reach = _body.ArmReach;
        var length = spec.Seconds ?? style.KickDuration;
        var kickSide = style.Lead == Hand.Left ? Hand.Right : Hand.Left;
        var lead = style.Lead.LocalSide();
        var guard = new StickFrame(_body, _body.Guard);
        var origin = guard.ShoulderCenter;
        Vector3 Local(Vector3 world) => world - origin; // 構えの体フレーム（向き 0）
        var headRest = Local(guard.Tip(StickBody.Ids.Head));
        var handRest = new[] { Local(guard.Tip(StickBody.Ids.ForearmL)), Local(guard.Tip(StickBody.Ids.ForearmR)) };
        var aimWorld = spec.AimWorld(Sense(guard, spec.Standoff * reach));
        var rest = new Vector3(kickSide.LocalSide() * 0.2f * reach, -1.4f * reach, 0f);
        var wrist = new[] { _body.Segments[StickBody.Ids.ForearmL].Tip, _body.Segments[StickBody.Ids.ForearmR].Tip };
        var headTip = _body.Segments[StickBody.Ids.Head].Tip;
        var foot = StickBody.Ids.Foot(kickSide);
        var shin = StickBody.Ids.Shin(kickSide);
        var ankle = _body.Segments[foot].Joint;
        var knee = _body.Segments[shin].Joint;
        var legBody = _legs[kickSide == Hand.Left ? 0 : 1];
        var total = (approach > 0f ? 0f : Before) + teaching.Shift + length + After;
        var pelvisRest = _body.Segments[StickBody.Ids.Pelvis].Center;
        var pelvisHeight = guard.Center(StickBody.Ids.Pelvis).Y;
        var guardAnkle = guard.Point(StickBody.Ids.Foot(kickSide), _body.Segments[StickBody.Ids.Foot(kickSide)].Joint);

        StickStep Step(float t, StickFrame now)
        {
            var before = approach > 0f ? 0f : Before; // 詰めながらなら、すぐ体重を移し始める
            var kickTime = t - before - teaching.Shift; // 負: 構え・体重を移す間
            var kicking = kickTime >= 0f && kickTime < length;
            var landing = kickTime >= length && kickTime < length + Landing;
            var yaw = kicking && spec.Turns && spec.Rotates ? spec.FaceYaw(kickSide, kickTime, length) : 0f;
            var twist = kicking && spec.Turns ? spec.TorsoTwist(kickSide, kickTime, length) : 0f;
            var turn = Quaternion.CreateFromAxisAngle(Vector3.UnitY, yaw);
            var frameOrigin = now.ShoulderCenter;
            Vector3 World(Vector3 local) => frameOrigin + Vector3.Transform(local, turn);
            var share = t < before ? 0f : teaching.Shift > 0f ? Math.Clamp((t - before) / teaching.Shift, 0f, 1f) : 1f;
            var lean = t < before + teaching.Shift + length ? new Vector3(lead * teaching.Side, 0f, teaching.Forward) * (reach * share) : Vector3.Zero;
            var footwork = _tuning.Footwork;
            var targets = new List<StickTarget>
            {
                new(StickBody.Ids.Head, headTip, World(headRest + lean), 1f, NeckKp, NeckKd),
                new(StickBody.Ids.ForearmL, wrist[0], World(handRest[0]), 0.3f, _tuning.HandKp, _tuning.HandKd),
                new(StickBody.Ids.ForearmR, wrist[1], World(handRest[1]), 0.3f, _tuning.HandKp, _tuning.HandKd),
            };
            if (kicking)
            {
                var aimLocal = Vector3.Transform(aimWorld - frameOrigin, Quaternion.Conjugate(turn));
                var command = spec.Evaluate(kickSide, kickTime, length, rest, aimLocal, reach, legBody);
                targets.Add(command.IsKneeControl
                    ? new StickTarget(shin, knee, World(command.KneeLocal!.Value), 2f, footwork.KneeKp, footwork.KneeKd)
                    : new StickTarget(foot, ankle, World(command.TargetLocal), 2f, footwork.FootKp, footwork.FootKd));
            }

            if (landing)
            {
                targets.Add(new StickTarget(foot, ankle, guardAnkle, 2f, footwork.FootKp, footwork.FootKd));
            }

            var lifted = kicking || landing;
            var support = kickSide == Hand.Left ? (!lifted, true) : (true, !lifted);
            // 骨盤: ゲームの体と同じく、足の支えの上へ寄せる（Fighter.ApplyBodyOverFeet: 蹴る間は軸足の母指球、両足なら体重のかけ方
            // = 頭の前への寄せで前足へ寄った真ん中）。寄せ方は速さの目標 Centering × ずれ・加速 MoveGain × 速さの差 = 固さ MoveGain × Centering、減衰 MoveGain。
            var stance = kickSide == Hand.Left ? Hand.Right : Hand.Left;
            var weight = Math.Clamp(lean.Z / (0.4f * reach), -1f, 1f);
            var leadFoot = Ball(now, style.Lead);
            var rearFoot = Ball(now, style.Lead == Hand.Left ? Hand.Right : Hand.Left);
            var over = lifted ? Ball(now, stance) : Vector2.Lerp(rearFoot, leadFoot, 0.5f + (0.5f * weight));
            targets.Add(new StickTarget(StickBody.Ids.Pelvis, pelvisRest, new Vector3(over.X, pelvisHeight, over.Y), 1f, _tuning.MoveGain * footwork.Centering, _tuning.MoveGain));
            var yaws = new List<StickYaw> { new(StickBody.Ids.Pelvis, yaw, 0.5f), new(StickBody.Ids.Chest, yaw + twist, 0.5f) };
            return new StickStep(targets, support, yaws);
        }

        return StickMotion.Run(_body, Step, total, dt, settings, velocity: new Vector3(0f, 0f, approach));
    }

    /// <summary>足 <paramref name="leg"/> の母指球（足の裏の前の点の真ん中）の水平の位置。</summary>
    private static Vector2 Ball(StickFrame frame, Hand leg)
    {
        var sole = frame.Sole(leg).ToArray();
        var ball = 0.5f * (sole[2] + sole[3]);
        return new Vector2(ball.X, ball.Z);
    }

    /// <summary>構えた自分を鏡に写した相手を、胸と胸の距離 <paramref name="distance"/> の前に置いた感覚（<see cref="KickSpec.AimWorld"/> が読む所だけ）。</summary>
    private FighterSense Sense(StickFrame guard, float distance)
    {
        var chest = guard.Center(StickBody.Ids.Chest);
        var middle = chest.Z + (0.5f * distance);
        Vector3 Mirror(Vector3 p) => new(-p.X, p.Y, (2f * middle) - p.Z);
        var self = new BodyFrame(guard.ShoulderCenter, 0f);
        var opponent = new BodySense(Mirror(guard.Joints[StickBody.Ids.UpperArmL]), Mirror(guard.Joints[StickBody.Ids.UpperArmR]), Mirror(guard.Center(StickBody.Ids.Pelvis)));
        return new FighterSense(self, _body.ArmReach, Mirror(guard.Center(StickBody.Ids.Head)), Mirror(chest), true, OpponentBody: opponent);
    }
}
