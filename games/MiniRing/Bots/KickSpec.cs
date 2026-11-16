using System.Numerics;

namespace MiniRing.Bots;

/// <summary>
/// 蹴りの種類。Roundhouse = 回し蹴り（腰を回して足の甲・すねで横から）、Side = 横蹴り（体を横に向けて足の外側・かかとで押し出す）、
/// Back = 後ろ蹴り（背を向ける回転から足裏・かかとを後ろへ）、Axe = かかと落とし（脚を上げて振り下ろす）。
/// 後から足した 4 種は列挙の末尾（既存の番号は不変）。
/// </summary>
public enum KickTechnique { Front, Low, Middle, High, Knee, Roundhouse, Side, Back, Axe }

/// <summary>
/// 蹴りも筋肉が追う目標の経路。威力の倍率を持たず、質量・速さ・接触する骨と皮膚が結果を決める。
/// <para>
/// 回しながら出す 4 種（<see cref="Turns"/>）は、足首の目標に加えて体の向き（<see cref="FaceYaw"/>）と胴のひねり（<see cref="TorsoTwist"/>）の経路を持つ。
/// どれも <see cref="FighterCommand"/> の目標（<c>FaceDirection</c>・<c>TwistBias</c>・脚の目標）で、体が物理で追った結果が見た目になる。
/// <paramref name="Standoff"/> は、その蹴りが届く胸と胸の距離（腕の長さの倍数）: 足は腕より短く、高い所・回る蹴りほど近づく。
/// <paramref name="FootPitch"/> は足首の曲げの目標 [rad]（正でつま先が下がる）。足のどこが先に当たるか（甲・かかと）は、この足首の向きと脚の骨のボクセルの接触で決まる。
/// </para>
/// </summary>
public readonly record struct KickSpec(KickTechnique Technique, float Duration, float Lateral, float ChamberHeight, float Overshoot,
    float Twist = 0f, float Spin = 0f, float Standoff = 0.9f, float FootPitch = 0.25f)
{
    public const float ChamberShare = 0.28f;
    public const float ContactShare = 0.53f;

    public static KickSpec Of(KickTechnique technique) => technique switch
    {
        KickTechnique.Front => new(technique, 0.80f, 0f, 0.52f, 0.14f, Standoff: 0.95f, FootPitch: -0.45f),
        KickTechnique.Low => new(technique, 0.85f, 0.65f, 0.38f, 0.16f, Standoff: 0.95f),
        KickTechnique.Middle => new(technique, 0.95f, 0.72f, 0.60f, 0.15f, Standoff: 0.9f),
        KickTechnique.High => new(technique, 1.12f, 0.78f, 0.82f, 0.12f, Standoff: 0.8f),
        KickTechnique.Knee => new(technique, 0.75f, 0f, 0.56f, 0.08f, Standoff: 0.8f, FootPitch: 0.9f),
        // 回し蹴り: 膝を横へ張り出してたたみ、腰を回しながら横から振り抜く（足の甲が先に当たる: つま先を伸ばす）。
        KickTechnique.Roundhouse => new(technique, 0.90f, 0.85f, 0.60f, 0.18f, Twist: 0.6f, Spin: 1.0f, Standoff: 0.9f, FootPitch: 0.55f),
        // 横蹴り: 膝を胸へたたみ、体を横へ向けて（足が相手へ向く）、足の外側・かかとで真っすぐ押し出す。
        KickTechnique.Side => new(technique, 0.95f, 0.30f, 0.55f, 0.16f, Twist: 0.5f, Spin: 1.45f, Standoff: 0.95f, FootPitch: 0f),
        // 後ろ蹴り: 背を向ける回転（体ごと 150°）の中で脚を畳み、後ろへ伸ばす。足裏・かかとが当たる。
        KickTechnique.Back => new(technique, 1.00f, 0.20f, 0.50f, 0.12f, Twist: 0.5f, Spin: -2.6f, Standoff: 0.85f, FootPitch: -0.3f),
        // かかと落とし: 脚を相手の肩より上まで振り上げ（体は正面のまま）、かかとを先に振り下ろす。
        KickTechnique.Axe => new(technique, 1.05f, 0f, 0.85f, 0.10f, Standoff: 0.85f, FootPitch: -0.6f),
        _ => throw new ArgumentOutOfRangeException(nameof(technique)),
    };

    /// <summary>体の向き・胴のひねりを回しながら出す蹴りか（回し蹴り・横蹴り・後ろ蹴り・かかと落とし）。</summary>
    public bool Turns => Technique >= KickTechnique.Roundhouse;

    /// <summary>体の向きが蹴りの間に変わるか（変わるなら、狙いの体フレームの位置を毎歩求め直す）。</summary>
    public bool Rotates => Spin != 0f;

    /// <summary>
    /// その蹴りで当てたい点（ワールド）。足は腕より短いので、高い所ほど近づかないと届かない: 前蹴り・膝は腹（相手の腰）、ローは腿
    /// （腰の下）、ミドル・回し蹴りは胸、横蹴り・後ろ蹴りはみぞおち、ハイは頭、かかと落としは肩。威力は変えない（どこへ向かう経路かだけ）。
    /// </summary>
    public Vector3 AimWorld(in FighterSense sense)
    {
        var pelvis = sense.OpponentBody == default ? sense.OpponentChest - new Vector3(0f, 0.5f * sense.ArmReach, 0f) : sense.OpponentBody.Pelvis;
        return Technique switch
        {
            KickTechnique.High => sense.OpponentHead,
            KickTechnique.Middle or KickTechnique.Roundhouse => sense.OpponentChest,
            KickTechnique.Side or KickTechnique.Back => (sense.OpponentChest + pelvis) * 0.5f,
            KickTechnique.Axe => sense.OpponentHead + new Vector3(0f, 0.10f * sense.ArmReach, 0f), // 頭のてっぺん・肩の上
            KickTechnique.Low => pelvis - new Vector3(0f, 0.5f * sense.ArmReach, 0f),
            _ => pelvis,
        };
    }

    public LegCommand Evaluate(Hand leg, float time, float duration, Vector3 rest, Vector3 aim, float reach)
    {
        if (!float.IsFinite(time) || !float.IsFinite(duration) || duration <= 0f || !float.IsFinite(reach) || reach <= 0f)
        {
            throw new ArgumentOutOfRangeException(nameof(duration));
        }

        var t = Math.Clamp(time / duration, 0f, 1f);
        if (Turns)
        {
            return EvaluateTurning(leg, t, rest, aim, reach);
        }

        var chamber = rest + new Vector3(leg.LocalSide() * Lateral * reach, ChamberHeight * reach, 0.10f * reach);
        var contact = aim + new Vector3(0f, 0f, Overshoot * reach);
        // 膝打ちは足首を引き付けたまま膝を狙いへ運ぶ。IK の膝方向も同じ狙いを向く。
        if (Technique == KickTechnique.Knee)
        {
            contact = aim + new Vector3(0f, -0.30f * reach, -0.20f * reach);
        }

        var target = t <= ChamberShare ? Vector3.Lerp(rest, chamber, Smooth(t / ChamberShare))
            : t <= ContactShare ? Vector3.Lerp(chamber, contact, Smooth((t - ChamberShare) / (ContactShare - ChamberShare)))
            : t <= 0.76f ? Vector3.Lerp(contact, chamber, Smooth((t - ContactShare) / (0.76f - ContactShare)))
            : Vector3.Lerp(chamber, rest, Smooth((t - 0.76f) / 0.24f));
        var knee = Technique == KickTechnique.Knee ? aim : chamber + new Vector3(0f, 0.2f * reach, 0.15f * reach);
        var pitch = Technique == KickTechnique.Front ? -0.45f : Technique == KickTechnique.Knee ? 0.9f : 0.25f;
        return new LegCommand(target, knee, Quaternion.CreateFromAxisAngle(Vector3.UnitX, pitch));
    }

    /// <summary>
    /// 回しながら出す蹴りの足首の目標（体フレーム）。体の向きが回ると、狙いは体の横・後ろへ動くので、呼ぶ側は毎歩、
    /// 今の体フレームで求めた狙いを渡す。経路は 腿を畳む（構え）→ 当てる点 → 畳み直す → 戻す。
    /// </summary>
    private LegCommand EvaluateTurning(Hand leg, float t, Vector3 rest, Vector3 aim, float reach)
    {
        var side = leg.LocalSide();
        var chamber = Technique switch
        {
            // 膝を体の前・外へ張り出して足を引き付ける（足は股の高さ近く）。
            KickTechnique.Roundhouse => rest + new Vector3(side * Lateral * reach, ChamberHeight * reach, 0.05f * reach),
            KickTechnique.Side => rest + new Vector3(side * Lateral * reach, ChamberHeight * reach, 0.15f * reach),
            KickTechnique.Back => rest + new Vector3(side * Lateral * reach, ChamberHeight * reach, 0.05f * reach),
            // かかと落とし: 足を相手の狙いの真上・手前まで上げる。
            _ => aim + new Vector3(0f, ChamberHeight * reach * 0.40f, -0.20f * reach),
        };
        // 当たる点は狙いを通り越した先（Overshoot）。回る蹴りは体の向きが狙いへ向き直る分も含めて、狙いの方向へ。
        var along = aim.LengthSquared() > 1e-6f ? Vector3.Normalize(aim) : Vector3.UnitZ;
        var contact = aim + (Overshoot * reach * (Technique == KickTechnique.Axe ? new Vector3(0f, 0f, 1f) : along));
        var target = t <= ChamberShare ? Vector3.Lerp(rest, chamber, Smooth(t / ChamberShare))
            : t <= ContactShare ? Vector3.Lerp(chamber, contact, Smooth((t - ChamberShare) / (ContactShare - ChamberShare)))
            : t <= 0.76f ? Vector3.Lerp(contact, chamber, Smooth((t - ContactShare) / (0.76f - ContactShare)))
            : Vector3.Lerp(chamber, rest, Smooth((t - 0.76f) / 0.24f));
        var knee = chamber + new Vector3(0f, 0.2f * reach, 0.15f * reach);
        return new LegCommand(target, knee, Quaternion.CreateFromAxisAngle(Vector3.UnitX, FootPitch));
    }

    /// <summary>
    /// 体の向き・胴のひねりの時間変化の形（0..1）: 当たる前に回りきり、当たったあとも少し保ち、畳み直すころに戻す
    /// （足が出る前に腰・肩が動き出す。拳の溜めと同じ読める手がかり）。
    /// </summary>
    public static float TurnProfile(float time, float duration)
    {
        var t = Math.Clamp(time / MathF.Max(1e-3f, duration), 0f, 1f);
        return t <= 0.45f ? Smooth(t / 0.45f) : t <= 0.76f ? 1f : Smooth(1f - ((t - 0.76f) / 0.24f));
    }

    /// <summary>体の向きを相手へ向けた向きから回す角度 [rad]（ヨー、正で左回り。蹴る脚の側の腰が前へ出る向きに回る）。</summary>
    public float FaceYaw(Hand leg, float time, float duration) => -leg.LocalSide() * Spin * TurnProfile(time, duration);

    /// <summary>胴のひねりの上乗せ [rad]（正で右肩が前）。蹴る脚の側の肩・腰が前へ出る向き。</summary>
    public float TorsoTwist(Hand leg, float time, float duration) => -leg.LocalSide() * Twist * TurnProfile(time, duration);

    private static float Smooth(float t) => t * t * (3f - (2f * t));
}
