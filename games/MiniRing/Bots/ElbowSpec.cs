using System.Numerics;

namespace MiniRing.Bots;

/// <summary>
/// 肘打ちの種類（ムエタイの肘）。Horizontal = 横肘（拳を耳へ畳み、肘を横から相手の顔へ振り抜く）、Upward = 上げ肘（肘を下から顎へ突き上げる）、
/// Downward = 振り下ろし肘（肘を上から斜めに打ち下ろす）。
/// </summary>
public enum ElbowTechnique { Horizontal, Upward, Downward }

/// <summary>
/// 肘打ちも目標の時間変化（経路）。拳の目標（畳んだ手を置く所）と肘の点の目標（IK の極）を <see cref="FighterCommand"/> の
/// <see cref="HandCommand.ElbowTarget"/> で渡し、体が肩・肘のサーボと胴のひねりで追う。威力・種類の倍率は持たず、肘の速さ・質量・当たる骨と皮膚が結果を決める
/// （肘の先の狭い面に力が集まる圧力は、ボクセルの接触が出す）。経路の値は手で決めた目標（<c>docs/MUAYTHAI.md</c> の近道）。
/// <para>
/// 寸法は腕の長さ倍。<paramref name="Standoff"/> はその肘が届く胸と胸の距離。<paramref name="Chamber"/> は肘を引く向き（肩から見た向き、右腕で
/// 外側が +X になるよう左右を掛けて使う）、<paramref name="Through"/> は当てたあと振り抜く向きの上乗せ（当てる点の向きに足す）。
/// <paramref name="HandAnchor"/> は畳んだ拳の置き場所（体フレーム、左右を掛ける）。<paramref name="Twist"/> は胴のひねり [rad]（肘の側の肩を前へ回す。
/// 当てる前に少し逆へ引いて、体のバネを縮める）。<paramref name="StepIn"/> は打つ間の踏み込み（0..1）。
/// </para>
/// </summary>
public readonly record struct ElbowSpec(ElbowTechnique Technique, float Duration, float Standoff, Vector3 Chamber, Vector3 Through, Vector3 HandAnchor,
    float Twist, float StepIn, Vector3 Lean)
{
    /// <summary>肘を引き終える割合。</summary>
    public const float ChamberShare = 0.22f;

    /// <summary>肘が当てる点を通る割合（そのあと振り抜く）。</summary>
    public const float ContactShare = 0.44f;

    /// <summary>肘の点の目標が肩からどれだけ離れるか（腕の長さ倍。極の向きだけが効くので、肘の点の目標が肩から外れすぎない程度）。</summary>
    public const float PoleDistance = 1.0f;

    /// <summary>体フレームでの肩の横の位置（腕の長さ倍。標準の体 0.283）。</summary>
    public const float ShoulderHalfWidth = 0.283f;


    private const float SweepEndShare = (2f * ContactShare) - ChamberShare;
    private const float FoldInShare = 0.30f;
    private const float FoldOutShare = 0.70f;

    public static ElbowSpec Of(ElbowTechnique technique) => technique switch
    {
        // 横肘: 肘を外へ開き、拳を耳（こめかみ）へ畳む。肩と胴のひねりで、肘を顔の高さの横から相手の顔の前を横切らせる。
        ElbowTechnique.Horizontal => new(technique, 1.007f, 0.90f, new Vector3(0.9f, 0.075f, 0.55f), new Vector3(-0.54f, 0f, 0f), new Vector3(0.20f, 0.27f, 0.05f), Twist: 1.01f, StepIn: 0.8f, Lean: new Vector3(0f, -0.03f, 0.30f)),
        // 上げ肘: 肘を下へ引き、拳を顎の横へ。肘を相手の顎の下から突き上げる。
        ElbowTechnique.Upward => new(technique, 0.9231f, 0.90f, new Vector3(0.35f, -0.7125f, 0.55f), new Vector3(0f, 0.54f, 0f), new Vector3(0.15f, 0.10f, 0.20f), Twist: 0.77f, StepIn: 0.8f, Lean: new Vector3(0f, -0.10f, 0.25f)),
        // 振り下ろし肘: 肘を高く引き、拳を頭の上へ。肘を上から斜めに打ち下ろす。
        ElbowTechnique.Downward => new(technique, 1.007f, 0.90f, new Vector3(0.2f, 0.7125f, 0.55f), new Vector3(0f, -0.54f, 0f), new Vector3(0.15f, 0.55f, 0.05f), Twist: 0.85f, StepIn: 0.8f, Lean: new Vector3(0f, -0.03f, 0.30f)),
        _ => throw new ArgumentOutOfRangeException(nameof(technique)),
    };

    /// <summary>
    /// その肘で当てたい点（ワールド）: 横肘はこめかみ・頬（相手の頭）、上げ肘は顎の下、振り下ろし肘は頭の上・額。威力は変えない（どこへ向かう経路かだけ）。
    /// </summary>
    public Vector3 AimWorld(in FighterSense sense) => Technique switch
    {
        ElbowTechnique.Upward => sense.OpponentHead - new Vector3(0f, 0.12f * sense.ArmReach, 0f),
        ElbowTechnique.Downward => sense.OpponentHead + new Vector3(0f, 0.10f * sense.ArmReach, 0f),
        _ => sense.OpponentHead,
    };

    /// <summary>
    /// 時刻 <paramref name="time"/> の指令。<paramref name="rest"/> は構えの肘の向き（肩から見た単位ベクトル）、<paramref name="aim"/> は当てたい点（体フレーム）。
    /// 経路は 構え → 肘を引く（拳を耳へ畳む）→ 当てる点へ → 振り抜く → 構えへ戻す。
    /// </summary>
    public (Vector3 HandTarget, Vector3 ElbowTarget) Evaluate(Hand hand, float time, float duration, Vector3 guardHand, Vector3 rest, Vector3 aim, float reach)
    {
        if (!float.IsFinite(time) || !float.IsFinite(duration) || duration <= 0f || !float.IsFinite(reach) || reach <= 0f)
        {
            throw new ArgumentOutOfRangeException(nameof(duration));
        }

        var t = Math.Clamp(time / duration, 0f, 1f);
        var side = hand.LocalSide();
        var shoulder = new Vector3(side * ShoulderHalfWidth * reach, 0f, 0f);
        var chamber = Unit(new Vector3(side * Chamber.X, Chamber.Y, Chamber.Z));
        var toward = Unit(aim - shoulder);
        var through = Unit(toward + new Vector3(side * Through.X, Through.Y, Through.Z));
        var anchor = new Vector3(side * HandAnchor.X, HandAnchor.Y, HandAnchor.Z) * reach;

        // フックと同じベジェ曲線を肩から見た向きに適用する。曲線の中点が狙いを向くよう制御点を選ぶ。
        var control = (2f * toward) - (0.5f * (chamber + through));
        var direction = t < ChamberShare
            ? Unit(Vector3.Lerp(rest, chamber, PunchPath.MinimumJerk(t / ChamberShare)))
            : t <= SweepEndShare
                ? Unit(PunchPath.Point(chamber, control, through, PunchPath.MinimumJerk((t - ChamberShare) / (SweepEndShare - ChamberShare))))
                : Unit(Vector3.Lerp(through, rest, PunchPath.MinimumJerk((t - SweepEndShare) / (1f - SweepEndShare))));

        // 拳は耳へ畳む（肘を引くあいだに構えから動かす）。肘を打ち抜いたあとも畳んだまま、構えへ戻すとき一緒に戻す。
        var fold = t <= FoldInShare ? Smooth(t / FoldInShare) : t <= FoldOutShare ? 1f : 1f - Smooth((t - FoldOutShare) / (1f - FoldOutShare));
        var handTarget = Vector3.Lerp(guardHand, anchor, fold);
        return (handTarget, shoulder + (direction * (PoleDistance * reach)));
    }

    /// <summary>構えの肘の向き（肩から見た単位ベクトル。肘を下・外・やや前へ逃がした形）。</summary>
    public static Vector3 RestDirection(Hand hand) => Unit(new Vector3(hand.LocalSide() * 0.3f, -0.9f, 0.2f));

    /// <summary>
    /// 時刻 <paramref name="time"/> の片手の指令（拳の目標 + <see cref="HandCommand.ElbowTarget"/>）。ボットと意図操作が共有する（<see cref="TechniqueExecution.Elbow"/>）。
    /// 体のバネを拳へ移す割合（Spring）は 0: 肘の速さは肩・肘のサーボと胴のひねり（<see cref="TorsoTwist"/>）と踏み込みから物理で出る。
    /// </summary>
    public HandCommand Command(Hand hand, float time, float duration, Vector3 guardHand, Vector3 aim, float reach)
    {
        var (handTarget, elbowTarget) = Evaluate(hand, time, duration, guardHand, RestDirection(hand), aim, reach);
        return new HandCommand(handTarget, false, ElbowTarget: elbowTarget);
    }

    /// <summary>胴を回す目標 [rad]（正で右肩が前）。当てる前は逆へ少し引いて体のバネを縮め、当たる点で肘の側の肩を前へ回し、戻す。</summary>
    public float TorsoTwist(Hand hand, float time, float duration)
    {
        var t = Math.Clamp(time / MathF.Max(1e-3f, duration), 0f, 1f);
        // 肘が狙いの方向を通る前に腰の目標を立ち上げ、腹と胸が追う時間を取る。
        const float windupEnd = 0.15f;
        const float driveEnd = 0.36f;
        const float release = 0.72f;
        const float preload = 0.28f;
        var profile = t <= windupEnd ? -preload * Smooth(t / windupEnd)
            : t <= driveEnd ? -preload + ((1f + preload) * Smooth((t - windupEnd) / (driveEnd - windupEnd)))
            : t <= release ? 1f : Smooth((1f - t) / (1f - release));
        return -hand.LocalSide() * Twist * profile;
    }

    /// <summary>体を出す強さ（0..1）: 肘を開く間に立ち上げ、当てるころ最大、戻す間に 0 へ（踏み込み・頭の前傾の山）。</summary>
    public static float Envelope(float time, float duration)
    {
        var t = Math.Clamp(time / MathF.Max(1e-3f, duration), 0f, 1f);
        const float start = 0.04f;
        const float end = 0.84f;
        return t < start ? 0f : MathF.Sin(MathF.PI * Math.Clamp((t - start) / (end - start), 0f, 1f));
    }

    /// <summary>踏み込みの強さ（移動指令の前向き成分の上乗せ）。</summary>
    public float StepEnvelope(float time, float duration) => Envelope(time, duration) * StepIn;

    /// <summary>頭の構えからのずれ（<see cref="FighterCommand.HeadOffset"/>）: 肘を出すとき上体を前へ傾け、肩を相手へ寄せる（腕の長さ倍 × 山）。</summary>
    public Vector3 HeadShift(Hand hand, float time, float duration, float reach)
        => new Vector3(hand.LocalSide() * Lean.X, Lean.Y, Lean.Z) * (reach * Envelope(time, duration));

    private static Vector3 Unit(Vector3 v) => v.LengthSquared() > 1e-8f ? Vector3.Normalize(v) : Vector3.UnitZ;

    private static float Smooth(float t) => t * t * (3f - (2f * t));
}
