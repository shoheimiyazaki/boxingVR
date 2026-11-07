using System.Numerics;

namespace MiniRing;

/// <summary>
/// 単純な CPU 相手。間合いを詰めて打ち、近距離ではたまに掴んで振り回す。頭も振る（スリップ・ダッキング）。
/// <para>
/// CPU が決めるのは「拳をどの経路で動かすか」だけ（構え → 溜め → 目標の奥 → 構え の 3 点）。
/// ジャブ・ストレート・フック・アッパーの<b>強さの違いは決めていない</b>: 同じ物理（<see cref="PunchMechanics"/>）が
/// 経路と体の向きから、体重が乗るか手打ちになるかを決める。
/// </para>
/// 乱数は seed 固定（Aigis の決定性ルール）なので、同じ入力なら同じ試合になる。
/// </summary>
public sealed class AiControl : IFighterControl
{
    private const float GrabDuration = 1.3f;

    private readonly Random _random;
    private readonly float _aggression;
    private Action _action;
    private Hand _activeHand;
    private Path _path;
    private float _actionTime;
    private float _cooldown = 1.5f;
    private float _strafe;
    private float _strafeTimer;
    private Vector3 _head;
    private Vector3 _headTarget;
    private float _headTimer;

    public AiControl(int seed, float aggression01)
    {
        _random = new Random(seed);
        _aggression = Math.Clamp(aggression01, 0f, 1f);
    }

    private enum Action
    {
        Idle,
        Punch,
        Grab,
    }

    public FighterCommand Read(in FighterSense sense, float dt)
    {
        var self = sense.Self;
        var reach = sense.ArmReach;
        var toOpponent = sense.OpponentChest - self.Origin;
        toOpponent.Y = 0f;
        var distance = toOpponent.Length();
        var dir = distance > 1e-3f ? toOpponent / distance : self.Forward;
        if (!sense.OpponentAlive)
        {
            return new FighterCommand(Vector3.Zero, dir, new HandCommand(Guard(Hand.Left, reach), false), new HandCommand(Guard(Hand.Right, reach), false));
        }

        // 間合い管理: 普段は胸と胸が腕の長さの 1.3 倍、打つときだけ 0.9 倍まで踏み込む。左右へもふらふら動く。
        _strafeTimer -= dt;
        if (_strafeTimer <= 0f)
        {
            _strafeTimer = Range(0.6f, 1.6f);
            _strafe = Range(-0.6f, 0.6f);
        }

        var desired = _action == Action.Punch ? reach * 0.9f : reach * 1.3f;
        var approach = Math.Clamp((distance - desired) / reach * 3f, -1f, 1f);
        var move = (dir * approach) + (Vector3.Cross(Vector3.UnitY, dir) * _strafe);

        UpdateHead(reach, dt);
        _cooldown -= dt;
        _actionTime += dt;
        UpdateAction(distance, reach);

        var left = Guard(Hand.Left, reach);
        var right = Guard(Hand.Right, reach);
        var grip = false;
        if (_action == Action.Punch)
        {
            var target = self.WorldToLocal(_path.ToBody ? sense.OpponentChest : sense.OpponentHead);
            SetActive(ref left, ref right, _path.Evaluate(_actionTime, Guard(_activeHand, reach), target, reach));
        }
        else if (_action == Action.Grab)
        {
            var chestLocal = self.WorldToLocal(sense.OpponentChest);
            grip = _actionTime > 0.25f;
            var pull = new Vector3(-_activeHand.LocalSide() * 0.6f, 0.4f, -0.6f) * reach; // 体の反対側へ引き倒す
            SetActive(ref left, ref right, _actionTime < 0.9f ? chestLocal : chestLocal + pull);
            if (_actionTime > 0.9f)
            {
                move = -dir;
            }
        }

        return new FighterCommand(move, dir,
            new HandCommand(left, grip && _activeHand == Hand.Left),
            new HandCommand(right, grip && _activeHand == Hand.Right),
            _head);
    }

    /// <summary>ときどき頭を振る（スリップ・ダッキング・前傾）。頭の位置は体の軸の傾きとして体に伝わる。</summary>
    private void UpdateHead(float reach, float dt)
    {
        _headTimer -= dt;
        if (_headTimer <= 0f)
        {
            _headTimer = Range(0.4f, 1.4f);
            _headTarget = _random.NextSingle() < 0.5f
                ? Vector3.Zero
                : new Vector3(Range(-0.35f, 0.35f), Range(-0.4f, 0f), Range(-0.1f, 0.25f)) * reach;
        }

        _head = Vector3.Lerp(_head, _headTarget, 1f - MathF.Exp(-8f * dt));
    }

    private void UpdateAction(float distance, float reach)
    {
        var duration = _action == Action.Punch ? _path.Duration : _action == Action.Grab ? GrabDuration : 0f;
        if (_action != Action.Idle && _actionTime >= duration)
        {
            _action = Action.Idle;
            _cooldown = (1.4f + ((0.45f - 1.4f) * _aggression)) * Range(0.7f, 1.3f);
        }

        if (_action != Action.Idle || _cooldown > 0f || distance > reach * 1.6f)
        {
            return;
        }

        _activeHand = _random.NextSingle() < 0.5f ? Hand.Left : Hand.Right;
        _action = distance < reach * 0.9f && _random.NextSingle() < 0.2f ? Action.Grab : Action.Punch;
        _path = Path.Random(_random, _activeHand);
        _actionTime = 0f;
    }

    private void SetActive(ref Vector3 left, ref Vector3 right, Vector3 target)
    {
        if (_activeHand == Hand.Left)
        {
            left = target;
        }
        else
        {
            right = target;
        }
    }

    private float Range(float min, float max) => min + ((max - min) * _random.NextSingle());

    /// <summary>構え（体フレームのローカル）: 肩幅の内側・やや下・前。</summary>
    public static Vector3 Guard(Hand hand, float reach) => new Vector3(hand.LocalSide() * 0.2f, -0.1f, 0.5f) * reach;

    /// <summary>
    /// 拳の経路: 構え → 溜め（<see cref="PunchPath.Windup"/>）→ 目標の奥（打ち抜く）→ 構え。
    /// 溜めを横に取れば横から回り込む経路（フック）、下に取れば下から突き上げる経路（アッパー）、
    /// 取らなければまっすぐ（ジャブ・ストレート）になる。強さはこの経路の結果として物理で決まる。
    /// </summary>
    private readonly record struct Path(Hand Hand, float Lateral, float Low, float Overshoot, float Duration, bool ToBody)
    {
        public static Path Random(Random random, Hand hand)
        {
            var lateral = random.NextSingle();   // 0: まっすぐ … 1: 大きく横から
            var low = random.NextSingle() < 0.3f ? random.NextSingle() : 0f; // 下から
            return new Path(hand, lateral, low, 0.25f + (0.15f * random.NextSingle()), 0.35f + (0.15f * lateral), random.NextSingle() < 0.25f);
        }

        /// <summary>前半 60% で打ち出し（<see cref="PunchPath"/>）、後半で構えへ戻す。</summary>
        public Vector3 Evaluate(float time, Vector3 guard, Vector3 target, float reach)
        {
            var t = Math.Clamp(time / Duration, 0f, 1f);
            var outbound = PunchPath.Point(guard, PunchPath.Windup(Hand, Lateral, Low, reach), PunchPath.Through(target, Overshoot, reach), t / 0.6f);
            return Vector3.Lerp(outbound, guard, MathF.Max(0f, (t - 0.6f) / 0.4f));
        }
    }
}
