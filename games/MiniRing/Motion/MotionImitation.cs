using System.Numerics;

namespace MiniRing.Motion;

/// <summary>
/// 剣の動き（<see cref="SwordMotion"/>）をなぞる操作元。動きのフレームを、もとのボクサーの体への指令にする: 右手（上の手）の拳の
/// 位置と剣の向きの目標、左手の位置（両手で握っていれば左手の目標は握りから決まる）、頭のずれ（前傾・沈み）、胴のひねり、歩く向きと
/// 速さ。体はこれを位置として書き込まず、拳のバネ・手首の筋肉・胴を立てる力・脚で追う（追いつけなければ遅れる）。
/// 長さは腕の長さで、速さは <see cref="Rate"/> 倍で当てはめる。
/// </summary>
public sealed class MotionImitation : IFighterControl
{
    private readonly float _walkSpeed;
    private readonly float _maxTwist;
    private readonly Vector3? _faceWorld;
    private readonly bool _faceAway;
    private readonly float _leadIn;
    private float _waited;
    private float _time;

    /// <param name="motion">なぞる動き。</param>
    /// <param name="rate">再生の速さ（1 = 録った速さ）。小さな体は同じ重力の下では速く動く: 体の大きさの比 s なら 1/√s がつり合う。</param>
    /// <param name="loop">終わったら最初から繰り返す。</param>
    /// <param name="faceWorld">向く向き（ワールド、水平）。null なら相手の方を向く。</param>
    /// <param name="tuning">歩く速さ・胴のひねりの上限（体の調整と同じもの）。</param>
    /// <param name="faceAway">相手に背を向ける（<paramref name="faceWorld"/> が null のとき。ひとりで型を稽古する）。</param>
    /// <param name="leadIn">動き始める前に、最初の構えを取って向きを合わせる時間 [s]（実時間）。</param>
    /// <param name="anticipation">
    /// 先回り [s]（実時間）: 体へ渡す目標を、動きのこれだけ先の時刻から取る。拳のバネと手首の筋肉は目標から遅れて追うので、その遅れを
    /// 先に動き出して埋める（行き先を知っている人が早めに動くのと同じ）。
    /// </param>
    public MotionImitation(SwordMotion motion, float rate = 1f, bool loop = false, Vector3? faceWorld = null, FighterTuning? tuning = null, bool faceAway = false, float leadIn = 0f, float anticipation = 0f)
    {
        Anticipation = MathF.Max(0f, anticipation);
        _leadIn = MathF.Max(0f, leadIn);
        _faceAway = faceAway;
        Motion = motion ?? throw new ArgumentNullException(nameof(motion));
        if (!(rate > 0f))
        {
            throw new ArgumentOutOfRangeException(nameof(rate), rate, "再生の速さは正。");
        }

        Rate = rate;
        Loop = loop;
        _faceWorld = faceWorld;
        var t = tuning ?? FighterTuning.Default;
        _walkSpeed = t.WalkSpeed;
        _maxTwist = t.MaxTorsoTwistRadians;
    }

    /// <summary>なぞる動き。</summary>
    public SwordMotion Motion { get; }

    /// <summary>先回り [s]（実時間）。</summary>
    public float Anticipation { get; }

    /// <summary>再生の速さ。</summary>
    public float Rate { get; }

    /// <summary>終わったら繰り返すか。</summary>
    public bool Loop { get; }

    /// <summary>動きの中の今の時刻 [s]（録った速さで）。</summary>
    public float MotionTime => Loop ? _time % MathF.Max(1e-3f, Motion.Duration) : MathF.Min(_time, Motion.Duration);

    /// <summary>最初の構えを取り終えて、動き始めたか。</summary>
    public bool Started => _waited >= _leadIn;

    /// <summary>動きを最後までなぞり終えた（繰り返さないとき）。</summary>
    public bool Finished => !Loop && _time >= Motion.Duration;

    /// <summary>今の時刻のフレーム。</summary>
    public MotionFrame Current => Motion.Sample(MotionTime);

    public FighterCommand Read(in FighterSense sense, float dt)
    {
        if (_waited < _leadIn)
        {
            _waited += dt; // 最初の構えを取って、向きを合わせる（動きの時刻は進めない）
        }
        else
        {
            _time += dt * Rate;
        }

        // 体へ渡すのは先回りした時刻の目標（なぞれ方を測るのは今の時刻の Current と比べて）。
        var ahead = Loop ? (_time + (Anticipation * Rate)) % MathF.Max(1e-3f, Motion.Duration) : MathF.Min(_time + (Started ? Anticipation * Rate : 0f), Motion.Duration);
        var frame = Motion.Sample(ahead);
        Vector3 face;
        if (_faceWorld is { } fixedFace)
        {
            face = fixedFace;
        }
        else
        {
            face = sense.OpponentChest - sense.Self.Origin;
            face.Y = 0f;
            face = _faceAway ? -face : face;
        }

        face = face.LengthSquared() > 1e-8f ? Vector3.Normalize(face) : sense.Self.Forward;
        var command = ToCommand(frame, sense, face, Started ? Rate : 0f, _walkSpeed, _maxTwist);
        // 一人稽古は相手に背を向ける。背後の相手でなく、同じ高さの前方の仮想の的を蹴る。
        Vector3? soloAim = null;
        if (_faceAway && Motion.Kick is { } kick)
        {
            soloAim = (sense.Self.Origin + (face * kick.Standoff * sense.ArmReach)) with { Y = kick.AimWorld(sense).Y };
        }
        return WithKick(command, Motion, ahead, sense, soloAim);
    }

    /// <summary>型の両手と脚を同じ指令へ落とす（稽古・試合・意図操作で共用）。</summary>
    public static FighterCommand Command(SwordMotion motion, float time, in FighterSense sense, Vector3 face, float rate, float walkSpeed, float maxTwist, Vector3 extraMove = default) =>
        WithKick(ToCommand(motion.Sample(time), sense, face, rate, walkSpeed, maxTwist, extraMove), motion, time, sense);

    /// <summary>脚の経路は既存の KickSpec。腕は構えの目標を保ち、接触の運動量で相手を押す。</summary>
    public static FighterCommand WithKick(FighterCommand command, SwordMotion motion, float time, in FighterSense sense, Vector3? aimWorld = null)
    {
        if (motion.Kick is not { } kick || time <= 0f || time >= motion.Duration)
        {
            return command;
        }

        var rest = new Vector3(Hand.Right.LocalSide() * 0.2f * sense.ArmReach, -1.4f * sense.ArmReach, 0f);
        var aim = sense.Self.WorldToLocal(aimWorld ?? kick.AimWorld(sense));
        return command with { RightLeg = kick.Evaluate(Hand.Right, time, motion.Duration, rest, aim, sense.ArmReach, sense.SelfLeg(Hand.Right)) };
    }

    /// <summary>型を書いた体の、肩の半幅 ÷ 腕の長さ（145 cm の体。<see cref="Bots.ElbowSpec.ShoulderHalfWidth"/> と同じ値）。</summary>
    public const float AuthoredShoulderHalfWidth = 0.283f;

    /// <summary>
    /// 今の体の肩の半幅 ÷ 腕の長さ（両肩の間の距離の半分。胴の回りに依らない）。肩が見えない入力では型を書いた体の値。
    /// </summary>
    public static float ShoulderHalfWidth(in FighterSense sense)
        => sense.SelfBody == default || sense.ArmReach <= 0f
            ? AuthoredShoulderHalfWidth
            : Vector3.Distance(sense.SelfBody.LeftShoulder, sense.SelfBody.RightShoulder) / (2f * sense.ArmReach);

    /// <summary>
    /// 型（腕の長さ = 1、肩の中心が原点）の両手を、今の体の肩から届くよう後ろへずらす量（体フレームの +Z = 前、腕の長さ）。2026-11-27（基準の体を 175 cm へ）:
    /// 型は肩の半幅 0.283 腕の体で書いた。肩の広い体（今の基準 0.344）では、体の中心の線の近くを握る両手が各々の肩から遠くなる（刀の中段で添える左手が 1.06 → 1.12 腕）。
    /// 各々の手について、型を書いた体の肩から手までの距離を、今の肩から同じ距離になる深さを求め、深さが足りない手の分だけ両手をそろって後ろへずらす
    /// （両手で 1 本の武器を握るとき、武器の向きと握りの間隔を変えない）。肩が狭い体では前へは出さない。型・武器・キャラごとに分けない 1 つの式。
    /// </summary>
    public static Vector3 FitBack(Vector3 right, Vector3 left, float shoulderHalfWidth)
    {
        var shift = 0f;
        foreach (var (hand, side) in new[] { (right, -1f), (left, 1f) }) // +X が左
        {
            if (hand.Z <= 0f)
            {
                continue;
            }

            var authored = hand - new Vector3(side * AuthoredShoulderHalfWidth, 0f, 0f);
            var lateral = hand.X - (side * shoulderHalfWidth);
            var depth = MathF.Sqrt(MathF.Max(0f, authored.LengthSquared() - (lateral * lateral) - (hand.Y * hand.Y)));
            shift = MathF.Max(shift, hand.Z - depth);
        }

        return new Vector3(0f, 0f, shift);
    }

    /// <summary>
    /// 剣の動きの 1 フレームを、体への指令にする（長さは腕の長さ <c>sense.ArmReach</c> で、歩く速さは <paramref name="rate"/> 倍で当てはめる。
    /// <paramref name="rate"/> が 0 なら歩かない）。
    /// </summary>
    public static FighterCommand ToCommand(MotionFrame frame, in FighterSense sense, Vector3 face, float rate, float walkSpeed, float maxTwist, Vector3 extraMove = default)
    {
        var reach = sense.ArmReach;
        var back = FitBack(frame.Right, frame.Left, ShoulderHalfWidth(sense));
        var right = (frame.Right - back) * reach;
        var left = (frame.Left - back) * reach;
        // 胴のひねりは拳の位置からも決まる（パンチの形）。動きの胴のひねりになるよう、その分を差し引いて上乗せする。
        var twistBias = frame.Twist - PunchMechanics.TorsoTwist(left, right, reach, maxTwist);
        // 歩く: 腰の速さ（体フレーム）を、腕の長さ・再生の速さで当てはめ、歩く速さの上限で割った指令にする。
        var move = (sense.Self.Rotate(frame.Velocity * (reach * rate)) / walkSpeed) + extraMove;
        return new FighterCommand(move, face,
            new HandCommand(left, false, Orientation: frame.LeftOrientation),
            new HandCommand(right, false, Orientation: frame.Orientation),
            HeadOffset: frame.Head * reach,
            TwistBias: twistBias);
    }
}
