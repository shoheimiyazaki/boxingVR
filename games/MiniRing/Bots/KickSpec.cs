using System.Numerics;

namespace MiniRing.Bots;

/// <summary>
/// 蹴りの種類。Roundhouse = 回し蹴り（腰を回して足の甲・すねで横から）、Side = 横蹴り（体を横に向けて足の外側・かかとで押し出す）、
/// Back = 後ろ蹴り（背を向ける回転から足裏・かかとを後ろへ）、Axe = かかと落とし（脚を上げて振り下ろす）。
/// 後から足した 4 種は列挙の末尾（既存の番号は不変）。
/// </summary>
public enum KickTechnique { Front, Low, Middle, High, Knee, Roundhouse, Side, Back, Axe, RoundhouseHigh, RoundhouseLow }

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
    float Twist = 0f, float Spin = 0f, float Standoff = 0.9f, float FootPitch = 0.25f, float ShinPoint = 0.75f, KneePath? Practiced = null,
    float? Seconds = null, CapturedKick? Captured = null, KickTunes? Tunes = null)
{
    public const float ChamberShare = 0.28f;
    public const float ContactShare = 0.53f;

    /// <summary>下段の回し蹴りの狙い: 相手の腰から腿の中ほどまでの下がり（腕の長さの倍数。腿の長さ 0.72 の半分）。</summary>
    public const float ThighDrop = 0.35f;

    private static readonly Lazy<CapturedKick> LowCapture = new(() => CapturedKick.Load("roundhouse_low"));

    private static readonly Lazy<CapturedKick> HighCapture = new(() => CapturedKick.Load("roundhouse_high"));

    private static readonly Lazy<CapturedKick> MidCapture = new(() => CapturedKick.Load("roundhouse_mid"));

    private static readonly Lazy<CapturedKick> FrontCapture = new(() => CapturedKick.Load("front_mid"));

    private static readonly Lazy<KickTunes> FrontTunes = new(() => KickTunes.Load("front_mid"));

    private static readonly Lazy<KickTunes> LowTunes = new(() => KickTunes.Load("roundhouse_low"));

    private static readonly Lazy<KickTunes> MidTunes = new(() => KickTunes.Load("roundhouse_mid"));

    private static readonly Lazy<KickTunes> HighTunes = new(() => KickTunes.Load("roundhouse_high"));

    /// <summary>人の動きの手本と、練習の差分から作る回し蹴り。間合い・長さ・足首の曲げは後ろの足の差分（ボットは後ろの足で蹴る）。</summary>
    private static KickSpec FromCapture(KickTechnique technique, CapturedKick capture, KickTunes tunes)
        => new(technique, 0.90f, 0.85f, 0.60f, -0.20f, Twist: 0.6f, Spin: 1.0f, Standoff: tunes.Rear.Standoff, FootPitch: tunes.Rear.FootPitch,
            Seconds: tunes.Rear.Seconds, Captured: capture, Tunes: tunes);

    /// <summary>手作りの回し蹴り（kneelab 段 2 の通り道）。人の動きから作った既定との比べもの用。</summary>
    public static KickSpec HandmadeRoundhouse => Of(KickTechnique.Roundhouse) with { Captured = null, Tunes = null, Seconds = 1.14f, Standoff = 1.0f, FootPitch = -0.5f };

    /// <summary>手作りの前蹴り（足首の目標の経路）。人の動きから作った既定との比べもの用。</summary>
    public static KickSpec HandmadeFront => Of(KickTechnique.Front) with { Captured = null, Tunes = null, Seconds = null, Duration = 0.80f, Lateral = 0f, ChamberHeight = 0.52f, Overshoot = 0.14f, Twist = 0f, Spin = 0f, Standoff = 0.95f, FootPitch = -0.45f };

    /// <summary>蹴る脚の差分（<paramref name="lead"/> は構えの前の手の側。null は左）。人の動きの手本でない蹴りは null。</summary>
    public KickTune? TuneFor(Hand leg, Hand? lead = null) => Tunes?.For(leg, lead);

    /// <summary>蹴りの長さ [s]（蹴る脚ごとの差分があればそれ）。</summary>
    public float? SecondsFor(Hand leg, Hand? lead = null) => TuneFor(leg, lead)?.Seconds ?? Seconds;

    /// <summary>蹴りが届く胸と胸の距離（腕の長さの倍数。蹴る脚ごとの差分があればそれ）。</summary>
    public float StandoffFor(Hand leg, Hand? lead = null) => TuneFor(leg, lead)?.Standoff ?? Standoff;

    public static KickSpec Of(KickTechnique technique) => technique switch
    {
        // 前蹴り（中段）: 人の動き（極真空手の中段の前蹴り S02、ミット 1.04 m）の手本 front_mid.json を膝の制御でなぞる。狙いは胴（胸と腰の中点）。手作りの足首の経路は HandmadeFront（比べもの用）。
        KickTechnique.Front => FromCapture(technique, FrontCapture.Value, FrontTunes.Value),
        KickTechnique.Low => new(technique, 0.85f, 0.65f, 0.38f, 0.16f, Standoff: 0.95f),
        KickTechnique.Middle => new(technique, 0.95f, 0.72f, 0.60f, 0.15f, Standoff: 0.9f),
        KickTechnique.High => new(technique, 1.12f, 0.78f, 0.82f, 0.12f, Standoff: 0.8f),
        KickTechnique.Knee => new(technique, 0.75f, 0f, 0.56f, 0.08f, Standoff: 0.8f, FootPitch: 0.9f),
        // 回し蹴り（中段）: 胴（脇腹・肝臓）への蹴り。手本は人の下段と上段を狙いの高さで混ぜた物（roundhouse_mid.json。人のデータに中段の廻し蹴りが無い）。
        // 練習の差分（間合い・長さ・足首の曲げ・当たる瞬間の腿の向き・膝を開く拍子）は roundhouse_mid.tune.json。docs/arena/kick_shin_lmh_*.md。
        // Practiced は手作り（kneelab 段 2、docs/arena/kneelab_stage2_diego.md。長さ 1.14 s）で、HandmadeRoundhouse（比べもの用）だけが使う。
        KickTechnique.Roundhouse => FromCapture(technique, MidCapture.Value, MidTunes.Value) with { Practiced = new KneePath(0.938f, 0.101f, 1.194f, -0.022f, 0f, 0f, 0f, 1.345f, 0.47f, 0.275f, 0.706f, 0.424f) },
        // 回し蹴り（上段）: 人の廻し蹴り上段（肩〜頭）の手本。狙いは頭。
        KickTechnique.RoundhouseHigh => FromCapture(technique, HighCapture.Value, HighTunes.Value),
        // 回し蹴り（下段）: 人の廻し蹴り下段（膝〜腰のミット）の手本。狙いは相手の腿。
        KickTechnique.RoundhouseLow => FromCapture(technique, LowCapture.Value, LowTunes.Value),
        // 横蹴り: 膝を胸へたたみ、体を横へ向けて（足が相手へ向く）、足の外側・かかとで真っすぐ押し出す。
        KickTechnique.Side => new(technique, 0.95f, 0.30f, 0.55f, 0.16f, Twist: 0.5f, Spin: 1.45f, Standoff: 0.95f, FootPitch: 0f),
        // 後ろ蹴り: 背を向ける回転（体ごと 150°）の中で脚を畳み、後ろへ伸ばす。足裏・かかとが当たる。
        KickTechnique.Back => new(technique, 1.00f, 0.20f, 0.50f, 0.12f, Twist: 0.5f, Spin: -2.6f, Standoff: 0.85f, FootPitch: -0.3f),
        // かかと落とし: 脚を相手の肩より上まで振り上げ（体は正面のまま）、かかとを先に振り下ろす。
        KickTechnique.Axe => new(technique, 1.05f, 0f, 0.85f, 0.10f, Standoff: 0.85f, FootPitch: -0.6f),
        _ => throw new ArgumentOutOfRangeException(nameof(technique)),
    };

    /// <summary>体の向き・胴のひねりを回しながら出す蹴りか（回し蹴り・横蹴り・後ろ蹴り・かかと落とし）。</summary>
    public bool Turns => Technique >= KickTechnique.Roundhouse || Captured is not null;

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
            KickTechnique.Front when Captured is not null => (sense.OpponentChest + pelvis) * 0.5f, // 人の手本のミット（1.04 m = 脚の長さの 1.2 倍）は、人形では胸と腰の中点の高さ
            KickTechnique.High => sense.OpponentHead,
            KickTechnique.Middle => sense.OpponentChest,
            // 回し蹴りは脇腹・みぞおち（胸と腰の間）を狙う。胸の中心は高く、脚がほぼ伸びきらないと届かない（膝の制御の蹴りは曲げた脚で当てる）。
            KickTechnique.Roundhouse => (sense.OpponentChest + pelvis) * 0.5f,
            KickTechnique.RoundhouseHigh => sense.OpponentHead,
            // 回し蹴り（下段）は腿の外側・内側（腰から腿の長さの半分ほど下）を狙う。
            KickTechnique.RoundhouseLow => pelvis - new Vector3(0f, ThighDrop * sense.ArmReach, 0f),
            KickTechnique.Side or KickTechnique.Back => (sense.OpponentChest + pelvis) * 0.5f,
            KickTechnique.Axe => sense.OpponentHead + new Vector3(0f, 0.10f * sense.ArmReach, 0f), // 頭のてっぺん・肩の上
            KickTechnique.Low => pelvis - new Vector3(0f, 0.5f * sense.ArmReach, 0f),
            _ => pelvis,
        };
    }

    /// <summary>蹴りの脚の指令。<c>body</c> は自分の脚の寸法（膝の制御の蹴りが使う。null なら腕の長さからの見積もり: <see cref="LegBody.FromReach"/>）。</summary>
    public LegCommand Evaluate(Hand leg, float time, float duration, Vector3 rest, Vector3 aim, float reach, LegBody? body = null, Hand? lead = null)
    {
        if (!float.IsFinite(time) || !float.IsFinite(duration) || duration <= 0f || !float.IsFinite(reach) || reach <= 0f)
        {
            throw new ArgumentOutOfRangeException(nameof(duration));
        }

        var t = Math.Clamp(time / duration, 0f, 1f);
        if (UsesKnee)
        {
            var lb = body ?? LegBody.FromReach(leg, reach);
            return Captured is { } captured ? captured.Evaluate(leg, t, aim, lb, TuneFor(leg, lead)?.FootPitch ?? FootPitch, TuneFor(leg, lead))
                : Practiced is { } practiced ? practiced.Evaluate(leg, t, aim, lb, FootPitch) : EvaluateKnee(leg, t, aim, reach, lb);
        }

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

    /// <summary>膝の制御（<see cref="LegCommand.KneeLocal"/>）で動かす蹴りか。回し蹴りから順に移す（他は足首の経路のまま。docs/TAEKWONDO.md）。</summary>
    public bool UsesKnee => Technique is KickTechnique.Roundhouse or KickTechnique.RoundhouseHigh or KickTechnique.RoundhouseLow || (Technique == KickTechnique.Front && Captured is not null);

    /// <summary>回し蹴りの構え（抱え）の膝の向き: 前（相手の方）から横（蹴る脚の側）へ回した角 [rad]（1.2 ≈ 69°）。</summary>
    public const float KneeChamberAzimuth = 1.2f;

    /// <summary>構えの膝を股より上へ上げる角 [rad]（0.35 ≈ 20°。腿がほぼ水平になる）。</summary>
    public const float KneeChamberElevation = 0.35f;

    /// <summary>構えのすねの曲げ [rad]（1.7 ≈ 97°。膝を畳んで足を腿の後ろへ引き付ける）。</summary>
    public const float KneeChamberBend = 1.7f;

    /// <summary>当たる瞬間の膝の曲げの下限 [rad]（0.45 ≈ 26°。伸びきると足の先が先に当たる突きになり、すねの横で当てる回し蹴りでなくなる）。</summary>
    public const float KneeContactMinBend = 0.45f;

    /// <summary>
    /// 膝の制御の蹴りの経路。段 1（抱え）: 膝を蹴る側の横・腰の高さほどへ上げる（ねじり・曲げはフリー）。段 2（回し）: 膝を前（狙いの方）へ回しながら、
    /// ねじりをすねが狙いを向く角へ、曲げを伸ばす方へ。当たる瞬間は、すねの膝から <see cref="ShinPoint"/>（足首とすねの中間あたり）の点が狙いに来る
    /// ように、膝の位置と曲げを狙いまでの距離から連続に出す（股から狙いまでの距離を、腿の長さ + すねの <see cref="ShinPoint"/> 倍までに丸める。
    /// 届かないときは脚を伸ばしきった向きで届く所まで）。段 3: 畳み直す。段 4: 戻す（ねじり・曲げはフリー）。
    /// </summary>
    private LegCommand EvaluateKnee(Hand leg, float t, Vector3 aim, float reach, LegBody body)
    {
        var side = leg.LocalSide();
        var hip = body.Hip;
        var thigh = body.Thigh;
        var shinPoint = body.Shin * ShinPoint;
        var rest = Direction(hip, body.RestKnee);
        var chamber = Vector3.Normalize(new Vector3(side * MathF.Sin(KneeChamberAzimuth) * MathF.Cos(KneeChamberElevation),
            MathF.Sin(KneeChamberElevation), MathF.Cos(KneeChamberAzimuth) * MathF.Cos(KneeChamberElevation)));

        // 当たる瞬間の形: 股 → 狙い（さらに Overshoot ぶん先）の距離 D を、腿とすねの届く範囲に丸め、膝を「股を中心とする腿の長さの球」と
        // 「狙いを中心とするすねの ShinPoint の球」の交わりの円の上の、体の外・上寄りの点に置く。
        var toAim = aim - hip;
        var distance = toAim.Length();
        var u = distance > 1e-6f ? toAim / distance : Vector3.UnitZ;
        // 当たる瞬間も膝を伸ばしきらない（KneeContactMinBend）: 伸ばしきった脚で届くだけの所は、すねの横でなく足の先が先に当たる突きになる。
        var farthest = MathF.Sqrt((thigh * thigh) + (shinPoint * shinPoint) + (2f * thigh * shinPoint * MathF.Cos(KneeContactMinBend)));
        var d = Math.Clamp(distance + (Overshoot * reach), MathF.Abs(thigh - shinPoint) + (0.02f * thigh), farthest);
        var a = ((thigh * thigh) - (shinPoint * shinPoint) + (d * d)) / (2f * d);
        var rho = MathF.Sqrt(MathF.Max(0f, (thigh * thigh) - (a * a)));
        var contactPoint = hip + (u * d);
        var outward = new Vector3(side, 0.35f, -0.2f);
        var perpendicular = outward - (Vector3.Dot(outward, u) * u);
        perpendicular = perpendicular.LengthSquared() > 1e-8f ? Vector3.Normalize(perpendicular) : Vector3.UnitY;
        var contactKnee = hip + (u * a) + (perpendicular * rho);
        var contactAxis = Direction(hip, contactKnee);
        var (contactTwist, contactBend) = LegKnee.ForShin(contactAxis, Vector3.UnitZ, Direction(contactKnee, contactPoint));
        contactBend = Math.Min(contactBend, LegIk.MaxKnee);

        // 段ごとの膝の向き・ねじり・曲げ。
        Vector3 axis;
        float? twist = null;
        float? bend = null;
        if (t <= ChamberShare)
        {
            axis = Turn(rest, chamber, Smooth(t / ChamberShare));
        }
        else if (t <= ContactShare)
        {
            var p = (t - ChamberShare) / (ContactShare - ChamberShare);
            axis = Turn(chamber, contactAxis, Smooth(p));
            // 膝を回してから伸ばす（脚の鞭）: 曲げの変化は回しの後半に寄せる。
            var extend = Smooth(Math.Clamp((p - 0.25f) / 0.75f, 0f, 1f));
            twist = p > 0.35f ? contactTwist : null;
            bend = Lerp(KneeChamberBend, contactBend, extend);
        }
        else if (t <= 0.76f)
        {
            var p = (t - ContactShare) / (0.76f - ContactShare);
            axis = Turn(contactAxis, chamber, Smooth(p));
            twist = p < 0.5f ? contactTwist : null;
            bend = Lerp(contactBend, KneeChamberBend, Smooth(p));
        }
        else
        {
            axis = Turn(chamber, rest, Smooth((t - 0.76f) / 0.24f));
        }

        return LegCommand.Knee(hip + (axis * thigh), twist, bend, Quaternion.CreateFromAxisAngle(Vector3.UnitX, FootPitch));
    }

    private static Vector3 Direction(Vector3 from, Vector3 to)
    {
        var v = to - from;
        return v.LengthSquared() > 1e-10f ? Vector3.Normalize(v) : -Vector3.UnitY;
    }

    /// <summary>2 つの向き（単位）の間を股のまわりに回す。</summary>
    private static Vector3 Turn(Vector3 from, Vector3 to, float t)
    {
        var v = Vector3.Lerp(from, to, t);
        return v.LengthSquared() > 1e-8f ? Vector3.Normalize(v) : from;
    }

    private static float Lerp(float a, float b, float t) => a + ((b - a) * t);

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
    public float FaceYaw(Hand leg, float time, float duration) => Captured is { } captured ? captured.YawAt(leg, Math.Clamp(time / MathF.Max(1e-3f, duration), 0f, 1f)) : -leg.LocalSide() * (Practiced?.Spin ?? Spin) * TurnProfile(time, duration);

    /// <summary>胴のひねりの上乗せ [rad]（正で右肩が前）。蹴る脚の側の肩・腰が前へ出る向き。</summary>
    public float TorsoTwist(Hand leg, float time, float duration) => Captured is { } captured ? captured.TorsoAt(leg, Math.Clamp(time / MathF.Max(1e-3f, duration), 0f, 1f)) : -leg.LocalSide() * (Practiced?.Torso ?? Twist) * TurnProfile(time, duration);

    private static float Smooth(float t) => t * t * (3f - (2f * t));
}
