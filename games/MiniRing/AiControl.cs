using System.Numerics;

namespace MiniRing;

/// <summary>
/// 単純な CPU 相手。間合いを詰めて左右のパンチを打ち、近距離ではたまに掴んで振り回す。
/// 乱数は seed 固定（Aigis の決定性ルール）なので、同じ入力なら同じ試合になる。
/// </summary>
public sealed class AiControl : IFighterControl
{
    private const float PunchDuration = 0.45f;
    private const float GrabDuration = 1.3f;

    private readonly Random _random;
    private readonly float _aggression;
    private Action _action;
    private Hand _activeHand;
    private float _actionTime;
    private float _cooldown = 1.5f;
    private float _strafe;
    private float _strafeTimer;

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

        // 間合い管理: 普段は胸と胸が腕の長さの 1.3 倍（近すぎると抱き合う）、打つときだけ 0.9 倍まで踏み込む。左右へもふらふら動く。
        _strafeTimer -= dt;
        if (_strafeTimer <= 0f)
        {
            _strafeTimer = Range(0.6f, 1.6f);
            _strafe = Range(-0.6f, 0.6f);
        }

        var desired = _action == Action.Punch ? reach * 0.9f : reach * 1.3f;
        var approach = Math.Clamp((distance - desired) / reach * 3f, -1f, 1f);
        var move = (dir * approach) + (Vector3.Cross(Vector3.UnitY, dir) * _strafe);

        _cooldown -= dt;
        _actionTime += dt;
        UpdateAction(distance, reach);

        var left = Guard(Hand.Left, reach);
        var right = Guard(Hand.Right, reach);
        var grip = false;
        if (_action == Action.Punch)
        {
            var headLocal = self.WorldToLocal(sense.OpponentHead);
            var t = 1f - MathF.Abs(1f - (2f * _actionTime / PunchDuration)); // 0→1→0 の三角波
            var strike = headLocal + (Vector3.Normalize(headLocal) * (0.3f * reach)); // 打ち抜くつもりで奥を狙う
            SetActive(ref left, ref right, Vector3.Lerp(Guard(_activeHand, reach), strike, Math.Clamp(t, 0f, 1f)));
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
            new HandCommand(right, grip && _activeHand == Hand.Right));
    }

    private void UpdateAction(float distance, float reach)
    {
        var duration = _action == Action.Punch ? PunchDuration : _action == Action.Grab ? GrabDuration : 0f;
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
        _action = distance < reach * 0.9f && _random.NextSingle() < 0.25f ? Action.Grab : Action.Punch;
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
}
