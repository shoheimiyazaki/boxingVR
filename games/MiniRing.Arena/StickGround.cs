using System.Numerics;
using MiniRing.Bots;
using MiniRing.Characters;
using MiniRing.Motion.Stick;
using MiniRing.Weapons;

namespace MiniRing.Arena;

/// <summary>
/// 倒れた相手へのとどめ（<see cref="GroundStrike"/>）を棒人形で走らせる（docs/STICK_MOTION.md §9）: ゲームのボットと同じ <see cref="GroundStrike"/> に
/// 作った感覚を渡し、出てきた指令（手・刃の向き・頭のずれ＝沈み）を、ゲームの体と同じ追い方で棒人形の目標にする。相手は床に横たわり、
/// 棒人形はその脇の立つ位置（<see cref="GroundMove.Stand"/>）に着いた所から始める（歩いて寄る所は走らせない）。
/// </summary>
internal sealed class StickGround
{
    private const float Dt = 1f / 60f;

    /// <summary>1 回のとどめを見る長さ [s]（構え → 沈み → 振りかぶり → 打つ → 戻る）。</summary>
    private const float Seconds = 1.8f;

    private readonly StickBody _body;
    private readonly FighterTuning _tuning;
    private readonly BotStyle _style;
    private readonly GroundTool _tool;
    private readonly float _length;
    private readonly float _standHeight;

    private StickGround(StickBody body, FighterTuning tuning, BotStyle style, GroundTool tool, float length, float standHeight)
    {
        _body = body;
        _tuning = tuning;
        _style = style;
        _tool = tool;
        _length = length;
        _standHeight = standHeight;
    }

    public StickBody Body => _body;

    /// <summary>体の調整値（キャラの体格）。</summary>
    public FighterTuning Tuning => _tuning;

    /// <summary>握りから武器の先までの長さ [m]（拳なら 0）。</summary>
    public float WeaponLength => _length;

    /// <summary>名簿のキャラ <paramref name="id"/> の体と武器を写した棒人形（武器は右手、両手持ちなら左手が握りの下）。</summary>
    public static StickGround For(string id)
    {
        var c = CharacterRoster.Get(id);
        var tuning = FighterTuning.Default with { Build = c.Build ?? BodyBuild.Standard };
        var corners = new BoxingCorners("stick", _ => new StandGuard(), _ => new StandGuard(), CpuWeapon: c.Weapon, CpuTwoHanded: FormSwordsman.Handles(c.Weapon), CpuBody: c.Build, CpuWear: c.Wear);
        using var game = new BoxingGame(tuning, seed: 1, corners: corners);
        var fighter = game.Cpu;
        var body = StickBody.FromFighter(game, fighter, tuning);
        var tool = GroundTool.Fist;
        var length = 0f;
        if (game.WeaponOf(fighter) is { } weapon && c.Weapon is { } kind)
        {
            length = Weapon.TipOf(kind).X - Weapon.GripOf(kind).X;
            var twoHanded = FormSwordsman.Handles(kind);
            var spacing = !twoHanded ? 0f : FormSwordsman.Spacing(kind);
            tool = new GroundTool(length / fighter.ArmReach, twoHanded, spacing);
            var mass = game.World.GetMass(weapon.Body.BodyId);
            // 手首: 両手で握るなら 2 つの手の偶力（手を引く力 × 握りの間隔）、片手なら手首のサーボ。
            var torque = twoHanded ? tuning.MaxHandForce * spacing * fighter.ArmReach : tuning.WristMaxTorque * tuning.TorqueScale;
            body = body.WithWeapon(BoxingGame.WeaponHand, length, mass, -Weapon.GripOf(kind).X, torque, twoHanded: twoHanded,
                backLength: Weapon.IsRod(kind) ? Weapon.GripOf(kind).X + Weapon.TipOf(kind).X : 0f);
        }

        var style = c.Resolve((string?)null);
        // 立つ高さ（骨盤の中心から足の裏、T ポーズ）: ゲームの体の _standHeight と同じ決め方。
        var rest = new StickFrame(body, StickPose.Rest(body.Dof));
        return new StickGround(body, tuning, style, tool, length, rest.Center(StickBody.Ids.Pelvis).Y - rest.LowestSole());
    }

    /// <summary>1 回のとどめの結果。</summary>
    /// <param name="Result">棒人形の動きの評価。</param>
    /// <param name="Name">打った技。</param>
    /// <param name="Started">打ち始めたか。</param>
    /// <param name="ContactSpeed">先（刃先・拳）が相手の高さを上から通り抜けた瞬間の速さ [m/s]（通らなければ 0）。</param>
    /// <param name="Miss">先が相手の点に最も近づいた距離 [m]。</param>
    internal readonly record struct Strike(StickResult Result, string Name, bool Started, float ContactSpeed, float Miss)
    {
        public override string ToString() => $"{Name} 当たりの速さ {ContactSpeed:0.00} m/s・外れ {Miss * 100f:0.0} cm・自傷 {Result.MaxSelfCut.Depth * 100f:0.0} cm（{Result.MaxSelfCut.Part}）・{Result.Summary()}";
    }

#if STICK_GROUND // 倒れた相手への攻め（GroundLab・BotStyle.Ground）は投げの枝 claude/throwing-grappling-mechanics-de662d にだけある。その枝を master へ入れたらこの #if を外す。
    /// <summary>
    /// 技 <paramref name="move"/>（cut・thrust-down・hammer・thrust）を、経路 <paramref name="ground"/>（null ならキャラの今の値）で 1 回打つ。
    /// </summary>
    public Strike Run(string move, GroundTuning ground)
    {
        var style = _style with { GroundDrive = 1f, GroundCut = move is "cut" or "hammer" ? 1f : 0f, GroundStomp = 0f, Ground = ground };
        var strike = new GroundStrike(style, _tool, seed: 1, club: false, ground);
        var plan = GroundLab.MoveOf(ground, move);
        var reach = _body.ArmReach;
        var guard = new StickFrame(_body, _body.Guard);
        var origin = guard.ShoulderCenter;
        Vector3 Rel(Vector3 world) => world - origin;
        var headTip = _body.Segments[StickBody.Ids.Head].Tip;
        var headRest = Rel(guard.Tip(StickBody.Ids.Head));
        var wrists = new[] { _body.Segments[StickBody.Ids.ForearmL].Tip, _body.Segments[StickBody.Ids.ForearmR].Tip };
        var handRest = new[] { Rel(guard.Tip(StickBody.Ids.ForearmL)), Rel(guard.Tip(StickBody.Ids.ForearmR)) };
        var pelvisRest = _body.Segments[StickBody.Ids.Pelvis].Center;
        var pelvisY = guard.Center(StickBody.Ids.Pelvis).Y;
        var forward = Vector3.UnitZ;
        var basis = new FighterCommand(Vector3.Zero, forward, new HandCommand(handRest[0], false), new HandCommand(handRest[1], false));

        // 相手: 床に横たわり、体の軸は X に沿う。いちばん近い点（胸の辺り）が、立つ位置から Stand だけ前。
        var aimZ = origin.Z + (plan.Stand * reach);
        var head = new Vector3(origin.X - (0.6f * reach), 0.03f, aimZ);
        var chest = new Vector3(origin.X - (0.15f * reach), 0.035f, aimZ);
        var pelvis = new Vector3(origin.X + (0.45f * reach), 0.035f, aimZ);
        var victim = new BodySense(chest + new Vector3(0f, 0f, -0.3f * reach), chest + new Vector3(0f, 0f, 0.3f * reach), pelvis);
        var weaponSense = _tool.Armed ? new WeaponSense(true, _length, _length) : default;
        var footwork = _tuning.Footwork;
        var neckKp = MathF.Pow(2f * MathF.PI * _tuning.NeckServoHz, 2f);
        var neckKd = 4f * MathF.PI * _tuning.NeckServoHz;
        var tip = _body.Armed ? _body.Segments[StickBody.Ids.Weapon].Tip : wrists[1];
        var tipSegment = _body.Armed ? StickBody.Ids.Weapon : StickBody.Ids.ForearmR;
        var started = false;
        var name = plan.Name;

        StickStep Step(float t, StickFrame now)
        {
            var self = new BodyFrame(now.ShoulderCenter, 0f);
            var sense = new FighterSense(self, reach, head, chest, false, OpponentBody: victim, OpponentDowned: true, MayStrikeDowned: true,
                SelfArmed: _tool.Armed, SelfWeapon: weaponSense);
            var command = strike.Read(sense, basis, Dt);
            started |= strike.Striking;
            name = strike.CurrentName ?? name;
            Vector3 World(Vector3 local) => self.LocalToWorld(local);
            var targets = new List<StickTarget>
            {
                new(StickBody.Ids.Head, headTip, World(headRest + command.HeadOffset), 1f, neckKp, neckKd),
                new(StickBody.Ids.ForearmL, wrists[0], World(command.Left.TargetLocal), 1f, _tuning.HandKp, _tuning.HandKd),
                new(StickBody.Ids.ForearmR, wrists[1], World(command.Right.TargetLocal), 1f, _tuning.HandKp, _tuning.HandKd),
            };
            if (_body.Armed && command.Right.Orientation is { } orientation)
            {
                var blade = Vector3.Transform(Vector3.UnitX, orientation);
                targets.Add(new StickTarget(StickBody.Ids.Weapon, tip, World(command.Right.TargetLocal + (blade * _length)), 1f, _tuning.HandKp, _tuning.HandKd));
            }

            // 腰: ゲームの体と同じく、頭を下げた量のうち（1 − HipHingeShare）を腰を落として（Fighter._crouch、倒れた相手を攻める間の上限 GroundCrouch）。
            var drop = MathF.Min(0f, command.HeadOffset.Y);
            var crouch = Math.Clamp((command.HeadOffset.Y - drop + (drop * (1f - _tuning.HipHingeShare))) / _standHeight, -_tuning.GroundCrouch, 0.05f);
            var feet = 0.5f * (Ball(now, Hand.Left) + Ball(now, Hand.Right));
            targets.Add(new StickTarget(StickBody.Ids.Pelvis, pelvisRest, new Vector3(feet.X, pelvisY + (crouch * _standHeight), feet.Y), 1f,
                _tuning.MoveGain * footwork.Centering, _tuning.MoveGain));
            return new StickStep(targets, (true, true));
        }

        var result = StickMotion.Run(_body, Step, Seconds, Dt);

        // 先が相手の高さ（胸の点の y）を上から通り抜けた瞬間の速さと、最も近づいた距離。
        var contact = 0f;
        var miss = float.MaxValue;
        for (var i = 1; i < result.Frames.Count; i++)
        {
            var p0 = result.Frames[i - 1].Point(tipSegment, tip);
            var p1 = result.Frames[i].Point(tipSegment, tip);
            miss = MathF.Min(miss, Vector3.Distance(p1, Nearest(p1, head, pelvis)));
            var surface = chest.Y + 0.01f;
            if (contact == 0f && p0.Y > surface && p1.Y <= surface && Vector2.Distance(new Vector2(p1.X, p1.Z), new Vector2(Nearest(p1, head, pelvis).X, aimZ)) < 0.6f * reach)
            {
                contact = Vector3.Distance(p0, p1) / Dt;
            }
        }

        return new Strike(result, name, started, contact, miss);
    }
#endif

    private static Vector3 Nearest(Vector3 p, Vector3 a, Vector3 b)
    {
        var ab = b - a;
        var t = Math.Clamp(Vector3.Dot(p - a, ab) / ab.LengthSquared(), 0f, 1f);
        return a + (ab * t);
    }

    private static Vector2 Ball(StickFrame frame, Hand leg)
    {
        var sole = frame.Sole(leg).ToArray();
        var ball = 0.5f * (sole[2] + sole[3]);
        return new Vector2(ball.X, ball.Z);
    }
}
