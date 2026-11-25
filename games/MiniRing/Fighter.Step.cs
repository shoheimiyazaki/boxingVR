using System.Numerics;

namespace MiniRing;

/// <summary>
/// ステップ（2026-11-18）: 構えで縮めてある脚のバネを、床を蹴る力として体の移動へ放つ。
/// <list type="bullet">
/// <item>貯める: 両足が床に着いている（踏み出している足・蹴っている足が無い）間だけ、脚の筋肉が仕事率 <see cref="FootworkTuning.StepLoadPowerPerKg"/> でバネを縮める。
/// 仕事はスタミナから払う（疲れると貯まるのが遅い）。空いている分だけ腰が伸び上がる（貯めきると構えの高さ）。上限は <see cref="FootworkTuning.StepSpringPerKg"/> × 支える質量 × 脚の力。</item>
/// <item>放つ: 指令の向き（<see cref="FighterCommand.Step"/>）に対して腰より後ろにある、床に着いた足が蹴る（前へ出るなら後ろ足、下がるなら前足）。
/// 脚は股と母指球を結ぶ線に沿ってしか押せないので、移動の向きへの効率はその線の水平の傾き（足が腰の真下なら 0）で、水平の力は床の摩擦
/// （押し付ける力 = 体重 + 脚が押す力の鉛直の成分）までしか出ない。放ったエネルギーは湧かない: バネにあった分だけ。</item>
/// <item>その後: 体は勢いで滑り、足の運び（倒立振子の捕捉点）が前へ出て体を受け止め、構えを整え直す。足が宙にある間は貯められないので、
/// 「足を整え直す時間 + 筋肉がバネを縮め直す時間」がそのままクールダウンになる（時計は持たない）。</item>
/// </list>
/// </summary>
public sealed partial class Fighter
{
    /// <summary>脚のバネに貯まっているエネルギー [J]（ステップに放てる分）。</summary>
    public float StepSpring { get; private set; } = float.NaN;

    /// <summary>脚のバネに貯められる上限 [J]: 支える質量 × <see cref="FootworkTuning.StepSpringPerKg"/> × 弱い方の脚の力（体格・傷み）。</summary>
    public float StepSpringCapacity => MathF.Max(0f, _tuning.Footwork.StepSpringPerKg * SupportedMass * MathF.Min(LegStrength(true), LegStrength(false)));

    /// <summary>脚のバネの貯まり具合（0..1）。</summary>
    public float StepCharge => !float.IsFinite(StepSpring) || StepSpringCapacity <= 1e-6f ? 1f : Math.Clamp(StepSpring / StepSpringCapacity, 0f, 1f); // 生成直後（まだ数えていない）は構えて貯めてある。貯められない脚（力 0）は伸び上がらない

    /// <summary>これまでにステップで体の移動へ渡した運動エネルギーの合計 [J]（調べもの・テスト用）。</summary>
    internal float StepEnergyReleased { get; private set; }

    /// <summary>蹴り出した向き（水平の単位ベクトル）。体の勢いを足が受け止め終わるまで（<see cref="FootworkTuning.StepBrakeAccel"/> で減速する間）持つ。</summary>
    private Vector3? _glide;

    /// <summary>
    /// 脚のバネを貯める・放つ（足の運びの歩ごと、体を足の上へ運ぶ前）。<paramref name="groundY"/> は床の高さ、<paramref name="legs"/> は脚の力の倍率。
    /// </summary>
    private void StepWithLegSpring(in FighterCommand command, float groundY, float legs, float dt, float yawPush = 0f)
    {
        var t = _tuning.Footwork;
        var capacity = StepSpringCapacity;
        if (!float.IsFinite(StepSpring))
        {
            StepSpring = capacity; // 生成直後は構えて貯めてある
        }

        StepSpring = MathF.Min(StepSpring, capacity); // 脚が傷めば貯めておける分も減る
        // 脚ごとに、床に着いている脚の筋肉だけが自分のバネを縮め直す（宙にある足・蹴っている足は床を押せない）。両足が着いていれば全力、片足なら半分。
        var planted = 0f;
        var limbCount = _legParts.Length + _rearLegs.Length;
        for (var i = 0; i < limbCount; i++)
        {
            var (leg, pose) = StanceLeg(i);
            planted += !pose.Swinging && leg.Command is null && (_rearFootwork is null || !_detached.Contains(leg.Heel)) ? 1f / limbCount : 0f;
        }

        var step = command.ClampedStep;
        var share = step.Length();
        var releaseSeconds = FormValue(t.StepPushTime, FighterTuning.DynamicPunch.Footwork.StepPushTime);
        var release = 1f - MathF.Exp(-dt / MathF.Max(1e-4f, releaseSeconds));
        var pushed = share > 1e-4f && StepSpring > 1e-6f && Push(step / share, share, groundY, dt, release);
        if ((!pushed || _tuning.AdaptivePunchForm) && planted >= 1f && MathF.Abs(yawPush) > 1e-4f && StepSpring > 1e-6f)
        {
            // 両方に配分したときも、回旋へ使う分は踏み込み後に残った同じばねから払う。
            pushed |= PushYaw(yawPush, dt, release);
        }

        if (!pushed && planted > 0f && StepSpring < capacity)
        {
            // 仕事はスタミナから払う（疲れると縮め直すのが遅い）。
            var power = t.StepLoadPowerPerKg * SupportedMass * MathF.Max(0f, legs) * VigorOf(MuscleRegion.Legs) * planted;
            StepSpring += SpendMuscleEnergy(MuscleRegion.Legs, MathF.Min(capacity - StepSpring, power * dt));
        }
    }

    /// <summary>
    /// 脚のバネを、向き <paramref name="direction"/>（水平の単位ベクトル）へ割合 <paramref name="share"/> だけ放つ。蹴るのは、その向きに対して腰より後ろにある、
    /// 床に着いた足。脚は股と母指球を結ぶ線に沿って押すので、移動の向きへの効率はその線の水平の成分で、水平の力は床の摩擦までしか出ない。
    /// <paramref name="release"/> はこの刻みで解放できるばねエネルギーの割合。
    /// </summary>
    /// <returns>床を蹴ったか（蹴れる足が無い・足が腰の真下か前にあるなら蹴らない）。</returns>
    private bool Push(Vector3 direction, float share, float groundY, float dt, float release)
    {
        var t = _tuning.Footwork;
        var pelvis = _world.GetState(Parts.Pelvis);
        Leg? pusher = null;
        var best = float.PositiveInfinity;
        for (var i = 0; i < _legParts.Length + _rearLegs.Length; i++)
        {
            var (leg, pose) = StanceLeg(i);
            if (pose.Swinging || leg.Command is not null || (_rearFootwork is not null && _detached.Contains(leg.Heel)))
            {
                continue; // 宙にある足は蹴れない
            }

            var ball = BallOnGround(leg);
            var behind = ((ball.X - pelvis.Position.X) * direction.X) + ((ball.Y - pelvis.Position.Z) * direction.Z);
            if (behind < best)
            {
                best = behind;
                pusher = leg;
            }
        }

        if (pusher is null)
        {
            return false;
        }

        // 脚の線（母指球 → 股）。移動の向きへの成分が、脚の伸びが体を押す効率（足が腰の真下なら 0、腰より前なら押せない）。
        var foot = BallOnGround(pusher);
        var hip = pelvis.Position + Vector3.Transform(pusher.PivotOnPelvis, pelvis.Orientation);
        var axis = hip - new Vector3(foot.X, groundY, foot.Y);
        var length = axis.Length();
        if (length < 1e-4f)
        {
            return false;
        }

        axis /= length;
        var along = Vector3.Dot(axis, direction);
        if (along <= 1e-3f)
        {
            return false;
        }

        var mass = SupportedMass;
        var released = StepSpring * share * release;
        var velocity = Vector3.Dot(pelvis.LinearVelocity, direction);
        var forward = MathF.Max(0f, velocity);
        var gain = along * released; // 移動の向きへ渡る運動エネルギー（残りは脚の線の鉛直の成分: 体を支えるのに使われて消える）
        var impulse = mass * (MathF.Sqrt((forward * forward) + (2f * gain / mass)) - forward);
        // 床の摩擦: 水平の力 ≤ μ × (体重 + 脚が押す力の鉛直の成分)。脚が立っているほど押し付けが増えて滑らない（μ × 鉛直 / 水平 ≥ 1 なら上限なし）。
        var horizontal = MathF.Sqrt(MathF.Max(1e-8f, 1f - (axis.Y * axis.Y)));
        var grip = 1f - (t.StepFriction * axis.Y / horizontal);
        if (grip > 1e-4f)
        {
            var limit = t.StepFriction * mass * -_world.Gravity.Y * dt / grip;
            if (impulse > limit)
            {
                // 滑って押しきれない分はバネに残る（渡した運動エネルギーから逆算）。
                var reached = forward + (limit / mass);
                released = 0.5f * mass * ((reached * reached) - (forward * forward)) / along;
                impulse = limit;
            }
        }

        StepSpring = MathF.Max(0f, StepSpring - released);
        StepEnergyReleased += along * released;
        ApplySharedImpulse(direction * impulse);
        _glide = direction;
        return true;
    }

    // 左右の足の反力を偶力として扱い、並進力積は加えない。
    private bool PushYaw(float request, float dt, float release)
    {
        var feet = Vector2.Distance(BallOnGround(_legParts[0]), BallOnGround(_legParts[1]));
        var footwork = _tuning.Footwork;
        var pelvis = _world.GetState(Parts.Pelvis);
        var localAxis = Vector3.Transform(Vector3.UnitY, Quaternion.Conjugate(pelvis.Orientation));
        var inertia = _world.GetLocalInertiaDiagonal(Parts.Pelvis);
        var inverseInertia = Vector3.Dot(localAxis, localAxis / Vector3.Max(inertia, new Vector3(1e-4f)));
        var sign = MathF.Sign(request);
        var workSpeed = MathF.Max(0f, sign * pelvis.AngularVelocity.Y);
        var budget = StepSpring * MathF.Abs(request) * release;
        var velocityAfter = MathF.Sqrt(workSpeed * workSpeed + 2f * inverseInertia * budget);
        var fromEnergy = (velocityAfter - workSpeed) / inverseInertia;
        // 足の荷重は半分ずつと見積もり、摩擦と母指球の間隔で偶力を制限する。
        var fromGrip = footwork.StepFriction * SupportedMass * -_world.Gravity.Y * feet * 0.5f * dt;
        var impulse = MathF.Min(fromEnergy, fromGrip);
        if (impulse <= 1e-6f)
        {
            return false;
        }

        var energy = impulse * (workSpeed + 0.5f * inverseInertia * impulse);
        StepSpring = MathF.Max(0f, StepSpring - energy);
        StepEnergyReleased += energy;
        BodyTorque(Parts.Pelvis, Vector3.UnitY * (sign * impulse));
        return true;
    }

    /// <summary>
    /// 蹴り出した後の減速: 体を足の上へ運ぶ加速度 <paramref name="accel"/> のうち、蹴った向きの勢いを止める成分を <see cref="FootworkTuning.StepBrakeAccel"/> までにする
    /// （着いた足が膝を柔らかく曲げて勢いを受け止める）。勢いが行きたい速さ <paramref name="desired"/> まで落ちたら、普段の足の運びへ戻る。
    /// </summary>
    private Vector3 GlideBrake(Vector3 accel, Vector3 velocity, Vector3 desired)
    {
        if (_glide is not { } direction)
        {
            return accel;
        }

        var excess = Vector3.Dot(velocity, direction) - Vector3.Dot(desired, direction);
        if (excess <= 0f)
        {
            _glide = null;
            return accel;
        }

        var along = Vector3.Dot(accel, direction);
        var brake = _tuning.Footwork.StepBrakeAccel;
        return along < -brake ? accel + (direction * (-brake - along)) : accel;
    }

    /// <summary>脚のバネが空いている分だけ腰が伸び上がる高さ（立つ高さの割合、正。貯めきった構えでは 0）。</summary>
    private float StepSinkShare => _tuning.Footwork.StepSink * (1f - StepCharge);
}
