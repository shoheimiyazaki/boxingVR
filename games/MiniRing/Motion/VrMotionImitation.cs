using System.Numerics;

namespace MiniRing.Motion;

/// <summary>
/// 記録した VR の動き（<see cref="VrMotionClip"/>）をなぞる操作元（<see cref="MotionImitation"/> と同じ考え方）。記録した、腕の長さ = 1 に正規化した
/// 両手の目標・武器の向き・<see cref="HandCommand.Slide"/>・頭のずれ・歩く向きを、この人形の腕の長さ（<c>sense.ArmReach</c>）で当てはめて
/// <see cref="FighterCommand"/> にする。体はこれを位置として書き込まず、バネで追う（CLAUDE.md §1.1。追いつけなければ遅れる）。
/// 再生は記録のフレームの時刻だけで進む（時計・乱数を使わない、§1.6）。
/// </summary>
public sealed class VrMotionImitation : IFighterControl
{
    private readonly float _leadIn;
    private readonly bool _faceAway;
    private float _waited;
    private float _time;

    /// <param name="clip">なぞる記録。</param>
    /// <param name="rate">再生の速さ（1 = 記録した速さ）。</param>
    /// <param name="loop">終わったら最初から繰り返す。</param>
    /// <param name="faceAway">相手に背を向けて稽古する（動画用）。false なら相手の方を向く。</param>
    /// <param name="leadIn">動き始める前に、最初の構えを取る時間 [s]（実時間）。</param>
    public VrMotionImitation(VrMotionClip clip, float rate = 1f, bool loop = false, bool faceAway = false, float leadIn = 0f)
    {
        Clip = clip ?? throw new ArgumentNullException(nameof(clip));
        if (!(rate > 0f))
        {
            throw new ArgumentOutOfRangeException(nameof(rate), rate, "再生の速さは正。");
        }

        Rate = rate;
        Loop = loop;
        _faceAway = faceAway;
        _leadIn = MathF.Max(0f, leadIn);
    }

    public VrMotionClip Clip { get; }

    public float Rate { get; }

    public bool Loop { get; }

    /// <summary>最初の構えを取り終えて、動き始めたか。</summary>
    public bool Started => _waited >= _leadIn;

    /// <summary>最後までなぞり終えた（繰り返さないとき）。</summary>
    public bool Finished => !Loop && _time >= Clip.Duration;

    /// <summary>記録の中の今の時刻 [s]。</summary>
    public float ClipTime => Loop ? _time % MathF.Max(1e-3f, Clip.Duration) : MathF.Min(_time, Clip.Duration);

    /// <summary>今の時刻のフレーム。</summary>
    public VrMotionFrame Current => Clip.Sample(ClipTime);

    public FighterCommand Read(in FighterSense sense, float dt)
    {
        if (_waited < _leadIn)
        {
            _waited += dt;
        }
        else
        {
            _time += dt * Rate;
        }

        var face = sense.OpponentChest - sense.Self.Origin;
        face.Y = 0f;
        face = _faceAway ? -face : face;
        face = face.LengthSquared() > 1e-8f ? Vector3.Normalize(face) : sense.Self.Forward;
        return ToCommand(Current, sense, face, Started);
    }

    /// <summary>
    /// 記録の 1 フレームを、この人形（腕の長さ <c>sense.ArmReach</c>）への指令にする。<paramref name="walk"/> が false なら歩かない（構えを取る間）。
    /// 歩く指令（<c>MoveLocal</c>）は記録のとき 0..1 に収めた値なので、そのまま体の向きへ回して使う。
    /// </summary>
    public static FighterCommand ToCommand(in VrMotionFrame frame, in FighterSense sense, Vector3 face, bool walk = true)
    {
        var reach = sense.ArmReach;
        var move = walk ? sense.Self.Rotate(frame.MoveLocal) : Vector3.Zero;
        return new FighterCommand(move, face,
            new HandCommand(frame.LeftTarget * reach, frame.LeftGrab, Orientation: frame.LeftWeaponOrientation, Slide: frame.LeftSlide),
            new HandCommand(frame.RightTarget * reach, frame.RightGrab, Orientation: frame.RightWeaponOrientation, Slide: frame.RightSlide),
            HeadOffset: frame.HeadOffset * reach);
    }
}
