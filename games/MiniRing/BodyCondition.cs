namespace MiniRing;

/// <summary>
/// 体の状態（各 0..1、1 = 万全）と、そこから決まる性能（純ロジック・物理非依存）。
/// <para>
/// 打たれた場所ごとに効く能力が違う:
/// 顎・こめかみ → 意識（反応が遅れる・全身の力が抜ける・0 で KO）、首 → 首（頭が据わらない・反応遅れ）、
/// 心臓 → 一瞬固まる＋スタミナ上限、肝臓 → <b>遅れて</b>脚とスタミナ（立っていられない）、みぞおち・ボディ → スタミナ、
/// 肩 → その腕が上がらない・力が伝わらない、肘 → 肘が伸び切らない・ガードが弱い、膝・脚 → 立てない・歩けない。
/// </para>
/// <para>
/// ダメージの一部は上限（Cap）を削る＝その試合中は戻らない。残りは休むと回復する。
/// スタミナは打つたびに（拳に掛けた力と仕事で）減り、心臓が傷むほど戻りが遅い。
/// </para>
/// </summary>
public sealed class BodyCondition
{
    private const float PermanentShare = 0.25f;
    private const float LiverDelay = 0.8f;

    private readonly float[] _shoulder = [1f, 1f];
    private readonly float[] _elbow = [1f, 1f];
    private readonly List<(float Due, float Amount)> _pendingLiver = [];
    private float _time;
    private float _sinceHeadHit = 10f;
    private float _consciousnessCap = 1f;
    private float _legsCap = 1f;
    private float _staminaCap = 1f;

    public float Consciousness { get; private set; } = 1f;
    public float Neck { get; private set; } = 1f;
    public float Stamina { get; private set; } = 1f;
    public float Heart { get; private set; } = 1f;
    public float Legs { get; private set; } = 1f;

    /// <summary>心臓への一撃で体が固まっている残り時間 [s]。</summary>
    public float HeartStun { get; private set; }

    /// <summary>直近の <see cref="Tick"/> で肝臓のダメージが表に出た（遅れて効いた）。</summary>
    public bool LiverKicked { get; private set; }

    public float Shoulder(Hand hand) => _shoulder[(int)hand];

    public float Elbow(Hand hand) => _elbow[(int)hand];

    public bool IsKnockedOut => Consciousness <= 0f;

    // ── 性能（すべて連続関数。状態の場合分けで性能を切り替えない） ─────────────────

    /// <summary>指令が体に届くまでの遅れ [s]: 意識・首・心臓の固まり。</summary>
    public float ReactionDelay => 0.02f + (0.30f * (1f - Consciousness)) + (0.12f * (1f - Neck)) + HeartStun;

    /// <summary>全身の駆動強度の倍率（意識が飛ぶほど力が抜ける）。</summary>
    public float DriveScale => Lerp(0.45f, 1f, Consciousness);

    /// <summary>脚の強さ（ホバー・歩行・脚サーボ）。</summary>
    public float LegStrength => Lerp(0.15f, 1f, Legs) * Lerp(0.6f, 1f, Consciousness);

    /// <summary>姿勢を保つ力（直立・ひねり・傾き）。意識が飛ぶほど上体が崩れる。</summary>
    public float PostureStrength => Lerp(0.6f, 1f, Consciousness);

    /// <summary>首の強さ（頭を据える力）。</summary>
    public float NeckStrength => Lerp(0.2f, 1f, Neck) * DriveScale;

    /// <summary>立っていられるか（脚か意識が尽きると崩れる）。</summary>
    public bool CanStand => Legs > 0.18f && Consciousness > 0.12f;

    /// <summary>ダウンから起き上がれるか。</summary>
    public bool CanGetUp => Legs > 0.35f && Consciousness > 0.35f && HeartStun <= 0f;

    /// <summary>腕を上げられる高さの上限（肩基準・腕の長さに対する比）。肩とスタミナが落ちるほど下がる。</summary>
    public float ArmRaiseLimit(Hand hand) => Lerp(-0.8f, 0.6f, MathF.Min(Shoulder(hand), 0.4f + (0.6f * Stamina)));

    public float ShoulderStrength(Hand hand) => Lerp(0.2f, 1f, Shoulder(hand)) * Lerp(0.5f, 1f, Stamina) * DriveScale;

    public float ElbowStrength(Hand hand) => Lerp(0.25f, 1f, Elbow(hand)) * DriveScale;

    /// <summary>腕の筋力の倍率（拳を引く力の上限）。</summary>
    public float HandForceScale(Hand hand) => Lerp(0.35f, 1f, Stamina) * Lerp(0.4f, 1f, Shoulder(hand) * Elbow(hand)) * DriveScale;

    /// <summary>体の質量を拳へ通せる度合い（肩・肘で固め、脚で地面を押す）。<see cref="PunchMechanics.Coupling"/> へ渡す。</summary>
    public float JointIntegrity(Hand hand) => Shoulder(hand) * Lerp(0.5f, 1f, Elbow(hand)) * Lerp(0.3f, 1f, Legs);

    // ── 被弾 ───────────────────────────────────────────────────────────

    /// <summary><paramref name="damage"/> は 0..約 40 のダメージ点。<paramref name="side"/> は肩・肘・ガードのときどちらの腕か。</summary>
    public void ApplyHit(HitZone zone, Hand side, float damage)
    {
        var d = MathF.Max(0f, damage) / 100f;
        switch (zone)
        {
            case HitZone.Chin:
                HeadHit(1.6f * d);
                Neck = Hurt(Neck, 0.3f * d);
                HurtLegs(0.3f * d);
                break;
            case HitZone.Temple:
                HeadHit(1.3f * d);
                HurtLegs(0.2f * d);
                break;
            case HitZone.Head:
                HeadHit(0.7f * d);
                break;
            case HitZone.Neck:
                Neck = Hurt(Neck, 1.2f * d);
                HeadHit(0.5f * d);
                HurtStamina(0.3f * d);
                break;
            case HitZone.Heart:
                Heart = Hurt(Heart, 1.2f * d);
                HurtStamina(0.8f * d);
                _staminaCap = MathF.Max(0.3f, _staminaCap - (0.3f * d));
                HeartStun = MathF.Max(HeartStun, 0.15f + (2.5f * d)); // 一瞬、体が固まる
                break;
            case HitZone.Liver:
                // 肝臓は遅れて効く: 打たれた直後は平気そうで、少し後に脚から崩れる。
                _pendingLiver.Add((_time + LiverDelay, d));
                break;
            case HitZone.SolarPlexus:
                HurtStamina(1.2f * d);
                HeadHit(0.1f * d);
                break;
            case HitZone.Body:
                HurtStamina(0.6f * d);
                HurtLegs(0.15f * d);
                break;
            case HitZone.Shoulder:
                _shoulder[(int)side] = Hurt(_shoulder[(int)side], 1.6f * d);
                HurtStamina(0.1f * d);
                break;
            case HitZone.Elbow:
                _elbow[(int)side] = Hurt(_elbow[(int)side], 1.6f * d);
                break;
            case HitZone.Guard:
                // ガードの上からでも削れる。
                _elbow[(int)side] = Hurt(_elbow[(int)side], 0.25f * d);
                HurtStamina(0.15f * d);
                break;
            case HitZone.Knee:
                HurtLegs(1.5f * d);
                break;
            default:
                HurtLegs(0.6f * d);
                break;
        }
    }

    /// <summary>時間経過: 遅れて効く肝臓・心臓の固まりの解除・回復・打つ動作によるスタミナ消費。</summary>
    /// <param name="dt">経過時間 [s]。</param>
    /// <param name="fatigue">この間に打つ動作で使ったスタミナ（0..1 の割合）。<see cref="Fighter"/> が拳の力と仕事から求める。</param>
    public void Tick(float dt, float fatigue)
    {
        _time += dt;
        _sinceHeadHit += dt;
        HeartStun = MathF.Max(0f, HeartStun - dt);
        LiverKicked = false;
        for (var i = _pendingLiver.Count - 1; i >= 0; i--)
        {
            if (_pendingLiver[i].Due > _time)
            {
                continue;
            }

            var d = _pendingLiver[i].Amount;
            HurtLegs(1.6f * d);
            HurtStamina(0.9f * d);
            _pendingLiver.RemoveAt(i);
            LiverKicked = true;
        }

        if (!IsKnockedOut && _sinceHeadHit > 1.5f)
        {
            Consciousness = MathF.Min(_consciousnessCap, Consciousness + (0.06f * dt));
        }

        Neck = MathF.Min(1f, Neck + (0.02f * dt));
        Legs = MathF.Min(_legsCap, Legs + (0.03f * dt * Lerp(0.3f, 1f, Consciousness)));
        Stamina = Math.Clamp(Stamina + (0.04f * dt * Lerp(0.3f, 1f, Heart)) - MathF.Max(0f, fatigue), 0f, _staminaCap);
        for (var h = 0; h < 2; h++)
        {
            _shoulder[h] = MathF.Min(1f, _shoulder[h] + (0.005f * dt));
            _elbow[h] = MathF.Min(1f, _elbow[h] + (0.005f * dt));
        }
    }

    private void HeadHit(float amount)
    {
        Consciousness = Hurt(Consciousness, amount);
        _consciousnessCap = MathF.Max(0.2f, _consciousnessCap - (PermanentShare * amount));
        _sinceHeadHit = 0f;
    }

    private void HurtLegs(float amount)
    {
        Legs = Hurt(Legs, amount);
        _legsCap = MathF.Max(0.25f, _legsCap - (PermanentShare * amount));
    }

    private void HurtStamina(float amount) => Stamina = Hurt(Stamina, amount);

    private static float Hurt(float value, float amount) => Math.Clamp(value - amount, 0f, 1f);

    private static float Lerp(float a, float b, float t) => a + ((b - a) * Math.Clamp(t, 0f, 1f));
}
