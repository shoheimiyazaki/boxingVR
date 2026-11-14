using System.Numerics;

namespace MiniRing;

/// <summary>
/// 足の運びの調整値。長さは脚の長さ（股から足首まで）単位、角度は度。
/// </summary>
public sealed record FootworkTuning
{
    /// <summary>足の運びを使うか（偽なら以前の、体を引いて脚は正弦の歩行で振るだけの動き）。</summary>
    public bool Enabled { get; init; } = true;

    /// <summary>構えの前後の幅（前足の母指球から後ろ足の母指球まで）。</summary>
    public float StanceLength { get; init; } = 0.55f;

    /// <summary>構えの左右の幅。</summary>
    public float StanceWidth { get; init; } = 0.34f;

    /// <summary>前足（左）のつま先の向き（向いている方向から。負で右＝内へ）。</summary>
    public float LeadYawDegrees { get; init; } = -8f;

    /// <summary>後ろ足（右）のつま先の向き（負で右へ開く）。</summary>
    public float RearYawDegrees { get; init; } = -30f;

    /// <summary>構えでかかとを浮かせる角度（母指球に乗る）。</summary>
    public float HeelRaiseDegrees { get; init; } = 14f;

    /// <summary>足の置き場所（目標）からこれだけずれたら踏み直す。</summary>
    public float StepThreshold { get; init; } = 0.09f;

    /// <summary>足の向きがこれだけずれたら踏み直す。</summary>
    public float YawThresholdDegrees { get; init; } = 18f;

    /// <summary>1 歩の最大の歩幅。</summary>
    public float MaxStride { get; init; } = 0.8f;

    /// <summary>両足の間の最大の開き（踏み込んだ瞬間・下がった瞬間に、構えより広がってよい幅）。</summary>
    public float MaxSpread { get; init; } = 1.2f;

    /// <summary>1 歩にかける時間 [s]（小さな歩幅は短く、最大の歩幅は長く）。</summary>
    public float StepTimeMin { get; init; } = 0.09f;
    public float StepTimeMax { get; init; } = 0.17f;

    /// <summary>1 歩踏んでからこの時間 [s] までに両足とも置き直しが要れば、もう一方の足から踏む（交互に運ぶ）。</summary>
    public float FollowTime { get; init; } = 0.4f;

    /// <summary>踏み出す足を持ち上げる高さ（すり足に近く、低い）。</summary>
    public float StepLift { get; init; } = 0.05f;

    /// <summary>移動の意図をどれだけ先まで見越して足を置くか [s]。</summary>
    public float Lookahead { get; init; } = 0.3f;

    /// <summary>捕捉点が両足の真ん中からこれだけ離れるまでは、構えの中心を動かさない。</summary>
    public float CaptureDeadband { get; init; } = 0.08f;

    /// <summary>見越す距離の上限（速く動こうとしても、体から遠すぎる所へは置かない）。</summary>
    public float MaxLookahead { get; init; } = 0.45f;

    /// <summary>踏み出した足を置ける、体（1 歩の後の位置）からの水平距離の上限（脚が届く範囲に対する割合）。</summary>
    public float ReachShare { get; init; } = 0.9f;

    /// <summary>
    /// 倒立振子の捕捉点（重心 + 速度 / ω）が、両足を結ぶ線からこれだけ離れたら、その向きへ踏み出して体を受け止める。
    /// </summary>
    public float CaptureMargin { get; init; } = 0.16f;

    /// <summary>胴のひねりのうち、母指球を軸に足を回す（かかとを外へ返す）割合。足先から腰へひねりを伝える。</summary>
    public float PivotShare { get; init; } = 0.8f;

    /// <summary>ひねりきったとき、軸足のかかとをさらに浮かせる角度。</summary>
    public float PivotHeelRaiseDegrees { get; init; } = 22f;

    /// <summary>体が移動の指令に付いていく遅れ [s]（この間に足が先に踏み出す）。</summary>
    public float BodyLag { get; init; } = 0.04f;

    /// <summary>体を両足の間の支えの点へ寄せる速さ [1/s] と、その速度の上限 [m/s]。</summary>
    public float Centering { get; init; } = 5f;
    public float MaxCentering { get; init; } = 0.25f;

    /// <summary>体が支えの点からこれだけ離れたら、離れる向きの速さを抑え始める（その倍で止める）。体が足を置いて先へ行かない。</summary>
    public float Tether { get; init; } = 0.25f;

    /// <summary>足首を置き場所へ引くバネと減衰 [1/s², 1/s]（手の拳と同じ PD）と、その力の上限（TorqueScale / Scale 倍する前）。</summary>
    public float FootKp { get; init; } = 900f;
    public float FootKd { get; init; } = 50f;
    public float MaxFootForce { get; init; } = 150f;

    /// <summary>
    /// 蹴っている脚の足首を引く力の上限が、歩く足の何倍か。歩く足の上限は床の摩擦に勝つ分で決めた値で、蹴りは脚の筋肉の全力（股・膝・足首）を
    /// 足首の 1 点の力に置き換える近道。蹴りの足の速さが、人形の拳（3〜6 m/s）に比べて 1 m/s 台だったので上げた（KickContactTests の表）。
    /// </summary>
    public float KickFootForceGain { get; init; } = 4f;

    /// <summary>立っているときの膝の曲げ（骨盤の高さの上限を決める）。</summary>
    public float KneeBendDegrees { get; init; } = 30f;

    /// <summary>前後の体重のかけ方の幅（0.5 ± これ）。前へ出した頭・打つ拳で前足へ、反らした頭で後ろ足へ。</summary>
    public float WeightShift { get; init; } = 0.25f;
}

/// <summary>1 本の足の置き方（ワールド）: 母指球の位置（地面の上、持ち上げを含む）、つま先の向き、かかとの浮き。</summary>
/// <param name="Ball">母指球（つま先の付け根）の位置。Y は地面からの持ち上げ。</param>
/// <param name="Yaw">つま先の向き（ヨー、<see cref="BodyFrame"/> と同じ規約）。</param>
/// <param name="HeelRaise">かかとの浮き [rad]（母指球を軸につま先側へ下げる角度）。</param>
/// <param name="Swinging">踏み出している最中（宙にある）。</param>
public readonly record struct FootPose(Vector3 Ball, float Yaw, float HeelRaise, bool Swinging);

/// <summary>足の運びへの入力（ワールドの水平面 XZ を Vector2 の X, Y で表す）。</summary>
/// <param name="FacingYaw">向いている方向（ヨー）。</param>
/// <param name="Body">骨盤の水平位置。</param>
/// <param name="BodyVelocity">骨盤の水平速度。</param>
/// <param name="Omega">倒立振子の固有角速度 √(g / 重心の高さ) [1/s]。</param>
/// <param name="Intent">行きたい速度 [m/s]（移動の指令）。</param>
/// <param name="Twist">胴のひねり（−1..1、正で右肩が前 = 右の拳を打つ向き）。</param>
/// <param name="Weight">前後の体重のかけ方（−1..1、正で前足へ）。</param>
/// <param name="LegLength">脚の長さ [m]（股から足首）。</param>
/// <param name="LeadOverreach">前足が脚の届く範囲の外にある（踏み直しが要る）。</param>
/// <param name="RearOverreach">後ろ足が脚の届く範囲の外にある。</param>
/// <param name="LeadBall">前足の母指球の実際の位置（体を寄せる支えの点に使う。null なら計画の位置）。</param>
/// <param name="RearBall">後ろ足の母指球の実際の位置。</param>
/// <param name="Reach">脚が水平に届く距離 [m]（股の高さから。腰を落とすほど遠くへ届く）。0 なら脚の長さ。</param>
/// <param name="HipOffset">股（脚の付け根）の体の中心からの横のずれ [m]。届く範囲の中心は左右の脚でこれだけずれる。</param>
public readonly record struct FootworkInput(float FacingYaw, Vector2 Body, Vector2 BodyVelocity, float Omega, Vector2 Intent, float Twist, float Weight, float LegLength,
    bool LeadOverreach = false, bool RearOverreach = false, Vector2? LeadBall = null, Vector2? RearBall = null, float Reach = 0f, float HipOffset = 0f);

/// <summary>
/// 足の置き場所を決めるモデル（倒立振子 + 構え）。ボクサーは左足が常に前（オーソドックス）。
/// <list type="bullet">
/// <item>体（骨盤）は両足の間の「支えの点」へ寄せられる。足が先に動き、体はそれに付いていく（体が引っ張られて足が後から付いてくるのではない）。</item>
/// <item>足の置き場所は、倒立振子の捕捉点（重心 + 速度 / ω、そこに足を置けば止まれる点）と移動の意図から決まる構えの位置。
///   置き場所から外れた足、届かなくなった足、捕捉点が両足の支えから外れたら、その向きの足から 1 歩ずつ踏む。</item>
/// <item>どちらの足から出るか: 行く向きの側の足から（前へは前足、後ろへは後ろ足、左へは左足、右へは右足）。もう一方が後から付いてくる（ステップ・ドラッグ）。</item>
/// <item>足は交差させず、左足が前・右足が後ろの順を保つ。</item>
/// <item>打つときは胴のひねりに合わせて軸足を母指球で回し（かかとを外へ返す）、体重を前へ移す。</item>
/// </list>
/// 物理ワールドに依存しない純粋な計画（<see cref="Fighter"/> がこれを脚の IK に渡す）。
/// </summary>
public sealed class Footwork
{
    public const int Lead = 0;
    public const int Rear = 1;

    private readonly FootworkTuning _tuning;
    private readonly Vector2[] _ball = new Vector2[2];
    private readonly float[] _yaw = new float[2];
    private int _swinging = -1;
    private int _lastFoot = -1;
    private float _sinceStep = float.PositiveInfinity;
    private Vector2 _from;
    private Vector2 _to;
    private float _fromYaw;
    private float _toYaw;
    private float _swingTime;
    private float _swingDuration;
    private float _twist;
    private float _weight;
    private Vector2 _reachCenter;
    private float _hipOffset;
    private float _facing;
    private float _reach;
    private float _legLength = 0.2f;

    public Footwork(FootworkTuning tuning)
    {
        ArgumentNullException.ThrowIfNull(tuning);
        _tuning = tuning;
    }

    /// <summary>踏み出している足（<see cref="Lead"/> / <see cref="Rear"/>）。両足が着いていれば −1。</summary>
    public int Swinging => _swinging;

    /// <summary>踏んだ歩数（調べもの・テスト用）。</summary>
    public int Steps { get; private set; }

    /// <summary>体（骨盤）を寄せる先: 両足の母指球の間で、体重のかけ方で前後に寄る点。</summary>
    public Vector2 SupportCenter { get; private set; }


    /// <summary>今の両足の位置で足を置き直す（立ち直ったとき・生成直後）。</summary>
    public void Reset(Vector2 leadBall, float leadYaw, Vector2 rearBall, float rearYaw)
    {
        _ball[Lead] = leadBall;
        _ball[Rear] = rearBall;
        _yaw[Lead] = leadYaw;
        _yaw[Rear] = rearYaw;
        _swinging = -1;
        _lastFoot = -1;
        _sinceStep = float.PositiveInfinity;
        _twist = 0f;
        _weight = 0f;
        SupportCenter = Vector2.Lerp(rearBall, leadBall, 0.5f);
    }

    /// <summary>足 <paramref name="foot"/> の今の置き方（踏み出している足は宙の途中）。</summary>
    public FootPose Pose(int foot)
    {
        var t = _tuning;
        var radians = MathF.PI / 180f;
        var heel = t.HeelRaiseDegrees * radians;
        // ひねりの軸足: 右の拳（ひねり正）は後ろ足、左の拳（負）は前足を母指球で回す。
        var drive = foot == Rear ? MathF.Max(0f, _twist) : MathF.Max(0f, -_twist);
        var pivot = _tuning.PivotShare * _twist * MaxTwist * (foot == Rear ? (_twist > 0f ? 1f : 0.25f) : (_twist < 0f ? 1f : 0.25f));
        heel += drive * t.PivotHeelRaiseDegrees * radians;
        if (foot != _swinging)
        {
            return new FootPose(new Vector3(_ball[foot].X, 0f, _ball[foot].Y), _yaw[foot] + pivot, heel, false);
        }

        var u = SwingProgress;
        var s = u * u * (3f - (2f * u));
        var p = Vector2.Lerp(_from, _to, s);
        var lift = 4f * u * (1f - u) * t.StepLift * _legLength;
        return new FootPose(new Vector3(p.X, lift, p.Y), Lerp(_fromYaw, _toYaw, s) + pivot, heel * (1f - (0.5f * MathF.Sin(MathF.PI * u))), true);
    }

    /// <summary>胴のひねりの最大 [rad]（入力の −1..1 を角度へ戻す）。</summary>
    public float MaxTwist { get; init; } = 35f * MathF.PI / 180f;

    private float SwingProgress => _swingDuration <= 0f ? 1f : Math.Clamp(_swingTime / _swingDuration, 0f, 1f);

    /// <summary>1 ステップ分進める: 踏み出している足を運び、着いたら次の足を決める。</summary>
    public void Update(in FootworkInput input, float dt)
    {
        _twist = Math.Clamp(float.IsFinite(input.Twist) ? input.Twist : 0f, -1f, 1f);
        _weight = Math.Clamp(float.IsFinite(input.Weight) ? input.Weight : 0f, -1f, 1f);
        _legLength = MathF.Max(0.01f, input.LegLength);
        _sinceStep += dt;
        if (_swinging >= 0)
        {
            _swingTime += dt;
            if (_swingTime >= _swingDuration)
            {
                _ball[_swinging] = _to;
                _yaw[_swinging] = _toYaw;
                _swinging = -1;
            }
        }

        if (_swinging < 0)
        {
            // 着いている足は、実際に置かれた所を足の位置とする（踏み出した足が届かなかった・押されてずれたら、そこから計画し直す）。
            _ball[Lead] = input.LeadBall ?? _ball[Lead];
            _ball[Rear] = input.RearBall ?? _ball[Rear];
            PlanStep(input);
        }

        UpdateSupport(input);
    }

    /// <summary>構えの位置（向きの座標で、前足 = 左前・後ろ足 = 右後ろ）。</summary>
    private Vector2 StanceOffset(int foot)
    {
        var x = 0.5f * _tuning.StanceWidth * _legLength;
        var z = 0.5f * _tuning.StanceLength * _legLength;
        return foot == Lead ? new Vector2(x, z) : new Vector2(-x, -z);
    }

    private float StanceYaw(int foot) => (foot == Lead ? _tuning.LeadYawDegrees : _tuning.RearYawDegrees) * MathF.PI / 180f;

    private void PlanStep(in FootworkInput input)
    {
        var t = _tuning;
        var leg = _legLength;
        var omega = MathF.Max(1f, input.Omega);
        // 捕捉点: いまの速度のまま倒れていく体を、ここに足を置けば止められる点。行きたい向きへは見越して置く。
        var capture = input.Body + (input.BodyVelocity / omega);
        // 構えの中心: 止まっていれば今の両足の真ん中（足を置き直すのは構えの形を直すときだけ。体を追って足を置くと、
        // 足 → 支え → 体 → 足 と回って、立っているだけで少しずつ歩いてしまう）。捕捉点が真ん中から離れたら（押された・動いている）その分だけ寄せる。
        var middle = Vector2.Lerp(_ball[Lead], _ball[Rear], 0.5f);
        var drift = capture - middle;
        var deadband = t.CaptureDeadband * leg;
        var center = middle + (drift.Length() > deadband ? drift * (1f - (deadband / drift.Length())) : Vector2.Zero)
            + ClampLength(input.Intent * t.Lookahead, t.MaxLookahead * leg);
        _reachCenter = input.Body + (input.BodyVelocity * (0.5f * t.StepTimeMax));
        _hipOffset = input.HipOffset;
        _facing = input.FacingYaw;
        _reach = t.ReachShare * (input.Reach > 0f ? input.Reach : leg);
        Span<Vector2> home = [Vector2.Zero, Vector2.Zero];
        Span<float> error = [0f, 0f];
        Span<bool> need = [false, false];
        for (var foot = 0; foot < 2; foot++)
        {
            home[foot] = center + Rotate(StanceOffset(foot), input.FacingYaw);
            var yawError = MathF.Abs(BodyFrame.DeltaAngle(_yaw[foot], input.FacingYaw + StanceYaw(foot)));
            error[foot] = Vector2.Distance(home[foot], _ball[foot]);
            need[foot] = error[foot] > t.StepThreshold * leg || yawError > t.YawThresholdDegrees * MathF.PI / 180f;
        }

        need[Lead] |= input.LeadOverreach;
        need[Rear] |= input.RearOverreach;
        // 捕捉点が両足を結ぶ線（支え）から外れたら、倒れる向きの足から踏み出して受け止める。
        var off = DistanceToSegment(capture, _ball[Lead], _ball[Rear]);
        var falling = off > t.CaptureMargin * leg;
        if (!need[Lead] && !need[Rear] && !falling)
        {
            return;
        }

        // 行く向き（足を置き直す向き）の側の足から出る: 前へは前足、後ろへは後ろ足、左へは左足、右へは右足。
        var travel = (home[Lead] - _ball[Lead]) + (home[Rear] - _ball[Rear]);
        if (falling)
        {
            travel = capture - Vector2.Lerp(_ball[Lead], _ball[Rear], 0.5f);
        }

        int foot0;
        if (need[Lead] != need[Rear] && !falling)
        {
            foot0 = need[Lead] ? Lead : Rear;
        }
        else if (_lastFoot >= 0 && _sinceStep < t.FollowTime && !falling)
        {
            foot0 = 1 - _lastFoot; // 踏んだばかりなら、もう一方の足が付いていく（同じ足で刻み続けない）
        }
        else if (travel.LengthSquared() > MathF.Pow(0.5f * t.StepThreshold * leg, 2f))
        {
            foot0 = Vector2.Dot(_ball[Lead] - _ball[Rear], travel) >= 0f ? Lead : Rear;
        }
        else
        {
            foot0 = error[Lead] >= error[Rear] ? Lead : Rear;
        }

        // 構えの制約（足を交差させない・開きすぎない）で先の足が動けなければ、もう一方の足から出る。
        if (!TryTarget(foot0, home, input.FacingYaw, out var target, out var targetYaw))
        {
            foot0 = 1 - foot0;
            if (!TryTarget(foot0, home, input.FacingYaw, out target, out targetYaw))
            {
                return;
            }
        }

        var distance = Vector2.Distance(target, _ball[foot0]);
        _swinging = foot0;
        _lastFoot = foot0;
        _sinceStep = 0f;
        _from = _ball[foot0];
        _to = target;
        _fromYaw = _yaw[foot0];
        _toYaw = _fromYaw + BodyFrame.DeltaAngle(_fromYaw, targetYaw);
        _swingTime = 0f;
        _swingDuration = Lerp(t.StepTimeMin, t.StepTimeMax, Math.Clamp(distance / (t.MaxStride * leg), 0f, 1f));
        Steps++;
    }

    /// <summary>足 <paramref name="foot"/> を置き場所へ向けて 1 歩ぶん踏む先。構えの制約で動けない（ほとんど動かない）なら偽。</summary>
    private bool TryTarget(int foot, ReadOnlySpan<Vector2> home, float facingYaw, out Vector2 target, out float targetYaw)
    {
        var t = _tuning;
        var step = ClampLength(home[foot] - _ball[foot], t.MaxStride * _legLength);
        target = KeepStance(foot, _ball[foot] + step, _ball[1 - foot], facingYaw);
        // 脚が届く範囲（1 歩の後の体の位置から）に収める。
        var hip = _reachCenter + Rotate(new Vector2(foot == Lead ? _hipOffset : -_hipOffset, 0f), _facing);
        target = hip + ClampLength(target - hip, _reach);
        targetYaw = facingYaw + StanceYaw(foot);
        return Vector2.Distance(target, _ball[foot]) >= 0.25f * t.StepThreshold * _legLength
            || MathF.Abs(BodyFrame.DeltaAngle(_yaw[foot], targetYaw)) >= 0.5f * t.YawThresholdDegrees * MathF.PI / 180f;
    }

    /// <summary>足を交差させない: 向きの座標で、左足は右足より前・左に（最小の幅の半分以上）置く。</summary>
    private Vector2 KeepStance(int foot, Vector2 target, Vector2 other, float yaw)
    {
        var local = Unrotate(target - other, yaw);
        var minZ = 0.25f * _tuning.StanceLength * _legLength;
        var minX = 0.25f * _tuning.StanceWidth * _legLength;
        var maxSpread = _tuning.MaxSpread * _legLength;
        var sign = foot == Lead ? 1f : -1f; // 前足から見た相手の足は右後ろ、後ろ足から見れば左前
        local = new Vector2(sign * MathF.Max(sign * local.X, minX), sign * MathF.Max(sign * local.Y, minZ));
        local = ClampLength(local, maxSpread);
        return other + Rotate(local, yaw);
    }

    /// <summary>
    /// 支えの点: 実際の両足の間（体重のかけ方で前後へ寄る）。踏み出している足は、宙にある間は踏み出す前の位置から
    /// 進み具合に応じて移していく（体重は着いている足に残り、体は足を追って後から動く）。
    /// </summary>
    private void UpdateSupport(FootworkInput input)
    {
        Vector2 Actual(int foot)
        {
            var planned = Pose(foot).Ball;
            var actual = (foot == Lead ? input.LeadBall : input.RearBall) ?? new Vector2(planned.X, planned.Z);
            if (foot != _swinging)
            {
                return actual;
            }

            return Vector2.Lerp(_from, actual, SwingProgress);
        }

        var share = 0.5f + (_tuning.WeightShift * _weight);
        var support = Vector2.Lerp(Actual(Rear), Actual(Lead), share);
        SupportCenter = support;
    }

    internal static Vector2 Rotate(Vector2 local, float yaw)
    {
        var (s, c) = MathF.SinCos(yaw);
        return new Vector2((local.X * c) + (local.Y * s), (-local.X * s) + (local.Y * c));
    }

    internal static Vector2 Unrotate(Vector2 world, float yaw)
    {
        var (s, c) = MathF.SinCos(yaw);
        return new Vector2((world.X * c) - (world.Y * s), (world.X * s) + (world.Y * c));
    }

    private static float DistanceToSegment(Vector2 p, Vector2 a, Vector2 b)
    {
        var ab = b - a;
        var t = ab.LengthSquared() < 1e-12f ? 0f : Math.Clamp(Vector2.Dot(p - a, ab) / ab.LengthSquared(), 0f, 1f);
        return Vector2.Distance(p, a + (ab * t));
    }

    private static Vector2 ClampLength(Vector2 v, float max)
    {
        var length = v.Length();
        return length > max ? v * (max / length) : v;
    }

    private static float Lerp(float a, float b, float t) => a + ((b - a) * t);
}
