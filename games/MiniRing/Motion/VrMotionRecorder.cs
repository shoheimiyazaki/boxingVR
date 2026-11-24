using System.Numerics;

namespace MiniRing.Motion;

/// <summary>
/// VR の動きを <see cref="VrMotionClip"/> に貯める。ホストは試合の 1 ステップごとに、その歩の生の入力・<see cref="VrPlayerMapper"/> が出した指令を
/// <see cref="Add"/> へ渡す。時刻は呼び出し側が持つ試合の刻みの和（時計を試合の計算に入れない、CLAUDE.md §1.6。記録は試合の外の付加機能）。
/// 写像後の値は人形の腕の長さで割り、腕の長さ = 1 に正規化して持つ（人形の大きさに依らず当てはめるため）。
/// </summary>
public sealed class VrMotionRecorder(string? weapon, string note = "")
{
    /// <summary>これより短い間隔のフレームは捨てる [s]（100 Hz。VR の表示 90 Hz はそのまま残る。240 Hz の物理の刻みをそのまま書くとファイルが大きくなる）。</summary>
    public const float MinInterval = 1f / 100f;

    private readonly List<VrMotionFrame> _frames = [];
    private float _start;
    private float _playerHeight = 1.7f;
    private float _playerArm = PuppetMapping.ArmReachFromHeight(1.7f);
    private float _puppetArm = 0.530f;
    private float _gain = VrPlayerMapper.MotionGain;

    public int Count => _frames.Count;

    /// <summary>1 歩ぶんを足す（<paramref name="time"/> は試合の刻みの和 [s]）。</summary>
    public void Add(float time, in VrRawFrame raw, in FighterCommand command, VrPlayerMapper mapper, float puppetArmReach)
    {
        ArgumentNullException.ThrowIfNull(mapper);
        if (!(puppetArmReach > 1e-3f))
        {
            throw new ArgumentOutOfRangeException(nameof(puppetArmReach), puppetArmReach, "The puppet arm length must be positive.");
        }

        if (_frames.Count == 0)
        {
            _start = time;
        }
        else if (time - _start < _frames[^1].Time + MinInterval - 1e-6f)
        {
            return;
        }

        _gain = mapper.HandGain;
        _playerHeight = mapper.PlayerHeight;
        _playerArm = mapper.PlayerArmReach;
        _puppetArm = puppetArmReach;
        var left = command.Left;
        var right = command.Right;
        var moveLocal = new BodyFrame(Vector3.Zero, mapper.HeadYaw).InverseRotate(command.ClampedMove);
        _frames.Add(new VrMotionFrame(time - _start,
            raw.HeadPosition, raw.HeadRotation,
            raw.LeftHand, raw.HandRotation(Hand.Left), raw.LeftTracked,
            raw.RightHand, raw.HandRotation(Hand.Right), raw.RightTracked,
            raw.LeftTrigger, raw.RightTrigger, raw.LeftGrip, raw.RightGrip,
            raw.LeftStick, raw.RightStick,
            left.TargetLocal / puppetArmReach, right.TargetLocal / puppetArmReach,
            left.Orientation, right.Orientation,
            left.Slide, right.Slide, left.Grip, right.Grip,
            command.HeadOffset / puppetArmReach, moveLocal));
    }

    /// <summary>ここまでを 1 本のクリップにする（フレームが無ければ null）。</summary>
    public VrMotionClip? Build()
        => _frames.Count == 0 ? null : new VrMotionClip(new VrMotionHeader(_playerHeight, _playerArm, _puppetArm, _gain, weapon, note), [.. _frames]);

    /// <summary>貯めたフレームを捨てて、次の稽古を最初から記録する。</summary>
    public void Reset() => _frames.Clear();
}
