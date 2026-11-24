using System.Numerics;

namespace MiniRing;

/// <summary>
/// 呼吸と循環（2026-11-18、ユーザーの設計）: 筋肉の短い疲れを、部位ごとの酸素の出入りで書く。
/// <list type="bullet">
/// <item>部位（<see cref="MuscleRegion"/>: 左腕・右腕・左脚・右脚・胴）の筋肉は、それぞれ酸素の蓄え（筋肉の中の酸素と、それで作り直せるエネルギー）を持つ。
/// 大きさは部位の質量 × 残った筋肉 × <see cref="FighterTuning.OxygenStorePerKg"/>。筋肉の仕事はここから出ていく。</item>
/// <item>中枢（心臓）から部位へ送れる速さの上限は血管の太さで、部位の質量 × 残った筋肉 × <see cref="FighterTuning.VesselPowerPerKg"/>。
/// 縮んでいる筋肉は自分の血管を押しつぶすので、働いている部位へ流れ込む速さは休んでいるときより下がる（<see cref="FighterTuning.VesselWorkingShare"/>）。</item>
/// <item>心臓は全部位へ送る速さの合計の上限（<see cref="FighterTuning.HeartPower"/> × 心臓の働き）。足りなければ、欲しい速さの比で分ける。</item>
/// <item>肺は血の酸素（<see cref="FighterTuning.BloodOxygenStore"/>）へ取り込む速さの上限（<see cref="FighterTuning.LungPower"/> × 肺の働き × 頸の脊髄）。
/// 心臓が送るのは血の酸素からで、血が空なら肺が取り込んだ分しか送れない。</item>
/// </list>
/// 蓄えが尽きた部位は、そこへ届く速さしか仕事ができない（腕の追従が落ちる）。届く速さを決めた所がボトルネック（<see cref="OxygenLimit"/>）で、
/// 血管なら腕の遅れ、心臓なら顔の青白さ（<see cref="Pallor"/>）、肺なら息のあえぎと唇の青さ（<see cref="Winded"/>・<see cref="Cyanosis"/>）に出る。
/// </summary>
public sealed partial class Fighter
{
    private float _recentMusclePower;
    private float _workDone;
    internal float StepMuscleBudget { get; private set; }
    internal float StepMuscleAuthorized { get; private set; }
    private readonly List<ServoWork> _servoWork = [];
    private readonly Dictionary<int, Vector3> _servoRate = [];

    private readonly record struct ServoWork(int Joint, int BodyA, int BodyB, float Speed, float Reserved, MuscleRegion Region, Quaternion BeforeA, Quaternion BeforeB);

    /// <summary>部位ごとの酸素の蓄えの残り（0..1、容量に対する割合。容量は肉の残りで変わるので割合で持つ）。</summary>
    private readonly float[] _oxygen = [1f, 1f, 1f, 1f, 1f];

    /// <summary>部位ごとに、この物理の歩で使ってよい仕事 [J]（蓄えと届いた酸素から。<see cref="UpdateStamina"/> が決める）。</summary>
    private readonly float[] _budget = new float[MuscleRegions.Count];

    /// <summary>部位ごとに、この歩で実際にした仕事 [J]（サーボは次の歩で精算する分を含む）。</summary>
    private readonly float[] _stepRegionWork = new float[MuscleRegions.Count];

    /// <summary>部位ごとに、酸素を流す前の蓄え [J]（この歩の放出の上限の元）。</summary>
    private readonly float[] _reserve = new float[MuscleRegions.Count];

    /// <summary>部位ごとの直近の仕事率 [W]（<see cref="FighterTuning.StaminaRecoveryWindow"/> でならす）。血管の押しつぶしと駆動の割合を決める。</summary>
    private readonly float[] _regionPower = new float[MuscleRegions.Count];

    /// <summary>部位ごとに、いま届けられる最大の速さ [W]（血管・心臓・肺の鎖を通った値。<see cref="VigorOf"/> の供給）。</summary>
    private readonly float[] _regionSupply = new float[MuscleRegions.Count];

    /// <summary>部位ごとに、前の歩で実際に届いた速さ [W]。</summary>
    private readonly float[] _regionDelivery = new float[MuscleRegions.Count];

    /// <summary>部位ごとに、届く速さを決めた所（前の歩）。</summary>
    private readonly OxygenLimit[] _regionLimit = new OxygenLimit[MuscleRegions.Count];

    /// <summary>調べもの用: この試合で部位ごとに筋肉がした仕事 [J]（<see cref="MuscleRegion"/> の順）。</summary>
    private readonly float[] _regionWork = new float[MuscleRegions.Count];

    /// <summary>血の酸素の残り（0..1、<see cref="FighterTuning.BloodOxygenStore"/> に対する割合）。</summary>
    private float _blood = 1f;

    /// <summary>肺が直近に取り込んだ速さ [W]（ならした値。息の速さの元）。</summary>
    private float _uptake;

    /// <summary>心臓が送りきれなかった割合（ならした値、0..1。顔の青白さの元）。</summary>
    private float _heartShortfall;

    /// <summary>心臓が送っている速さの、今の心臓の上限に対する割合（ならした値、0..1。中継のバイタルの心拍の元。見た目だけ）。</summary>
    private float _heartLoad;

    /// <summary>
    /// 心臓の仕事の割合（0..1、見た目だけ）: 部位へ送っている酸素の速さ ÷ 今の心臓の上限 <see cref="HeartCapacity"/>（ならした値）。
    /// 全身で酸素を欲しがるほど、心臓が傷んで上限が下がるほど 1 に近づく。死んでいれば 0。試合の計算には使わない（中継の心拍数の元: <see cref="HeartRate"/>）。
    /// </summary>
    public float HeartLoad => IsDead ? 0f : _heartLoad;

    /// <summary>
    /// 心拍数 [拍/分]（見た目だけ）: 安静の <see cref="RestPulse"/> から、心臓の仕事の割合 <see cref="HeartLoad"/> に比例して <see cref="MaxPulse"/> まで（近道: 1 回の拍出量を一定と見る）。
    /// 死んでいれば 0。試合の計算には使わない。
    /// </summary>
    public float HeartRate => IsDead ? 0f : RestPulse + ((MaxPulse - RestPulse) * Math.Clamp(_heartLoad, 0f, 1f));

    /// <summary>安静の心拍数 [拍/分]（鍛えた選手のふつうの値）。</summary>
    public const float RestPulse = 60f;

    /// <summary>心臓がいっぱいに働いているときの心拍数 [拍/分]（成人の最大心拍のおおよそ）。</summary>
    public const float MaxPulse = 190f;

    /// <summary>調べもの用: 部位 <paramref name="region"/> の筋肉がこの試合でした仕事 [J]。</summary>
    internal float RegionWork(MuscleRegion region) => _regionWork[(int)region];

    /// <summary>部位 <paramref name="region"/> の酸素の蓄えの残り（0..1）。</summary>
    public float OxygenOf(MuscleRegion region) => _oxygen[(int)region];

    /// <summary>部位 <paramref name="region"/> へ前の歩で届いた酸素の速さ [W]。</summary>
    public float DeliveryOf(MuscleRegion region) => _regionDelivery[(int)region];

    /// <summary>部位 <paramref name="region"/> へ届く速さを前の歩で決めた所（蓄えが満ちていて欲しがっていなければ <see cref="OxygenLimit.None"/>）。</summary>
    public OxygenLimit LimitOf(MuscleRegion region) => _regionLimit[(int)region];

    /// <summary>血の酸素の残り（0..1）。肺が追いつかないと減る。</summary>
    public float BloodOxygen => _blood;

    /// <summary>テストが、試合で後のラウンドまで来た体を作る（した仕事の合計を直に置く: <see cref="Endurance"/> が落ちる）。</summary>
    internal void SetWorkDone(float joules) => _workDone = MathF.Max(0f, joules);

    /// <summary>テストが部位ごとに疲れた体を作る（蓄えの残りを直に置く）。</summary>
    internal void SetOxygen(MuscleRegion region, float fill) => _oxygen[(int)region] = Math.Clamp(fill, 0f, 1f);

    /// <summary>
    /// スタミナ（0..1）: いちばん減っている部位の酸素の蓄えの残り（腕を切られた部位は数えない）。メーター・ボットの判断・見た目の合図はこれを読む。
    /// 置くと（テストが疲れた体を作る）、すべての部位と血の酸素を同じ割合にする。
    /// </summary>
    public float Stamina
    {
        get
        {
            var least = 1f;
            for (var i = 0; i < MuscleRegions.Count; i++)
            {
                if (i < 2 && _arms[i].Severed)
                {
                    continue;
                }

                least = MathF.Min(least, _oxygen[i]);
            }

            return least;
        }

        internal set
        {
            var fill = Math.Clamp(value, 0f, 1f);
            Array.Fill(_oxygen, fill);
            _blood = fill;
        }
    }

    /// <summary>
    /// 筋肉の量の基準体格に対する比（<see cref="BodyBuild.MassRatio"/>: 体積に比例する質量と同じ H³ × M²）。心臓・肺・血の酸素の尺度に掛ける
    /// （部位の蓄えと血管は部位の質量そのものから出るので、体格の比はもう入っている）。1 kg あたりは体格によらず同じ（§1.7）。
    /// </summary>
    private float MuscleMassRatio => _tuning.Build.MassRatio;

    /// <summary>部位の質量 [kg]（体に付いている物理の体の質量。胴は回して支える筋の分だけ: <see cref="FighterTuning.TrunkMuscleShare"/>）。</summary>
    private float RegionMass(int region) => region switch
    {
        0 or 1 => ArmMass((Hand)region),
        (int)MuscleRegion.LeftLeg => LegSideMass(Hand.Left),
        (int)MuscleRegion.RightLeg => LegSideMass(Hand.Right),
        _ => (_world.GetMass(Parts.Chest) + _world.GetMass(Parts.Pelvis)
            + (Parts.Abdomen is { } abdomen ? _world.GetMass(abdomen) : 0f)) * _tuning.TrunkMuscleShare,
    };

    /// <summary>
    /// 部位の残った筋肉（0..1）。蓄えも血管も残った肉の分だけ。折れた・切れた腕も、体に近い側の肩・肘の筋肉は残った形へ動く（<c>DriveStump</c>）ので
    /// 数え続ける（<see cref="Strength"/> には下限があるので容量は 0 にならない）。
    /// </summary>
    private float RegionMuscle(int region) => region switch
    {
        0 or 1 => 0.5f * (Strength(_arms[region].UpperArm) + Strength(_arms[region].Forearm)),
        (int)MuscleRegion.LeftLeg => SideLegMuscle(Hand.Left),
        (int)MuscleRegion.RightLeg => SideLegMuscle(Hand.Right),
        _ => CoreStrength,
    };

    /// <summary>部位の酸素の蓄えの容量 [J]（今の肉の残りから数え直す）。</summary>
    private float MeasureOxygenCapacity(int region) => _tuning.OxygenStorePerKg * RegionMass(region) * RegionMuscle(region);

    /// <summary>部位の酸素の蓄えの容量 [J]（歩の始めに <see cref="UpdateStamina"/> が数え直した値。歩の途中の削れは次の歩から効く）。</summary>
    private readonly float[] _capacity = new float[MuscleRegions.Count];

    private float OxygenCapacity(int region) => _capacity[region];

    /// <summary>調べもの用: 部位の酸素の蓄えの容量 [J]（この歩の値）と、この歩で使ってよい仕事 [J] の残り。</summary>
    internal (float Capacity, float Budget) OxygenBudgetOf(MuscleRegion region) => (_capacity[(int)region], _budget[(int)region]);

    /// <summary>心臓が全部位へ送れる速さ [W]（死ねば 0）。</summary>
    public float HeartCapacity => IsDead ? 0f : _tuning.HeartPower * MuscleMassRatio * HeartFunction;

    /// <summary>肺が血へ取り込める速さ [W]（横隔膜を動かす頸の脊髄を含む。死ねば 0）。</summary>
    public float LungCapacity => IsDead ? 0f : _tuning.LungPower * MuscleMassRatio * LungFunction * _cord[0];

    /// <summary>血の酸素の容量 [J]。</summary>
    private float BloodCapacity => MathF.Max(1e-3f, _tuning.BloodOxygenStore * MuscleMassRatio);

    /// <summary>
    /// 血管が部位へ流せる速さ [W]: 部位の質量 × 残った筋肉 × 1 kg あたり × 持久力。働いている筋肉は自分の血管を押しつぶすので、直近の仕事率 P が
    /// 休んだときの流れ V の <see cref="FighterTuning.VesselWorkingShare"/>（k）倍に届くまで、流れは V から kV へ直線に下がる（P ≥ kV では kV）。
    /// 休むほど速く戻り、kV より軽い仕事では蓄えが減らない（運動生理の臨界パワーにあたる値を、部位ごとに血管の太さから出す）。
    /// </summary>
    private float VesselFlow(int region)
    {
        var most = _tuning.VesselPowerPerKg * RegionMass(region) * RegionMuscle(region) * Endurance;
        var working = most * Math.Clamp(_tuning.VesselWorkingShare, 1e-3f, 1f);
        var power = _regionPower[region];
        return power >= working ? working : most - ((most - working) * power / working);
    }

    /// <summary>
    /// 心臓と肺が今送っている酸素の合計 [W]（前の歩で全部位へ届いた速さの合計）。
    /// </summary>
    internal float RecoveryPower
    {
        get
        {
            var sum = 0f;
            foreach (var delivered in _regionDelivery)
            {
                sum += delivered;
            }

            return sum;
        }
    }

    /// <summary>
    /// 試合を通した持久力（0..1]: exp(−試合でした筋肉の仕事の合計 / 持久の蓄え）。筋肉の糖（グリコーゲン）が減るほど血管から届いた酸素で
    /// 作り直せるエネルギーが落ちる近似。ラウンドの間の休みでは戻らないので、後のラウンドほど疲れやすい。持久の蓄え = <see cref="FighterTuning.StaminaEndurance"/>
    /// × 筋肉の量の比（1 kg あたりで同じ）。
    /// </summary>
    public float Endurance => MathF.Exp(-_workDone / MathF.Max(1e-3f, _tuning.StaminaEndurance * MuscleMassRatio));

    /// <summary>この試合で筋肉がした仕事の合計 [J]（<see cref="Endurance"/> の元）。</summary>
    internal float WorkDone => _workDone;

    /// <summary>直近に実際にした仕事の仕事率 [W]（全部位の合計。なめらかにしたもの）。</summary>
    internal float RecentMusclePower => _recentMusclePower;

    /// <summary>部位 <paramref name="region"/> の直近の仕事率 [W]。</summary>
    internal float RegionPower(MuscleRegion region) => _regionPower[(int)region];

    private bool FatigueEnabled => _tuning.StaminaFatigue;

    /// <summary>
    /// 部位 <paramref name="region"/> の駆動の割合: 蓄えに余裕があれば 1、尽きるほど「届く速さ /（届く速さ + 直近の仕事率）」へ近づく
    /// （ただし続けて保てる力 <see cref="FighterTuning.VesselWorkingShare"/> × <see cref="Endurance"/> が上限）。固定の最低筋力は設けない。
    /// </summary>
    public float VigorOf(MuscleRegion region)
    {
        if (!FatigueEnabled)
        {
            return 1f;
        }

        var i = (int)region;
        var fresh = Freshness(i);
        var supply = _regionSupply[i];
        var power = fresh + ((1f - fresh) * supply / MathF.Max(1e-6f, supply + _regionPower[i]));
        // 続けて保てる力（2026-11-22、慢性の疲れ）: 蓄えが尽きた筋が続けて出せるのは、血管の流れが支える分（働く筋への流れの割合 × 持久力）だけ。
        // 動いていなくても重さを保つ力はここで頭打ちになるので、持久力が落ちた体は、蓄えが戻るまで構えを保てない（構えが下がる）。
        var sustain = fresh + ((1f - fresh) * _tuning.VesselWorkingShare * Endurance);
        return MathF.Min(power, sustain);
    }

    /// <summary>いちばん疲れた部位の駆動の割合（切られた腕は数えない）。調べもの・ボットの読み用。</summary>
    public float Vigor
    {
        get
        {
            var least = 1f;
            for (var i = 0; i < MuscleRegions.Count; i++)
            {
                if (i < 2 && _arms[i].Severed)
                {
                    continue;
                }

                least = MathF.Min(least, VigorOf((MuscleRegion)i));
            }

            return least;
        }
    }

    /// <summary>仕事率が下がると筋肉の短縮速度も落ちる（Hill の力・速度関係の近似）。腕ごと。</summary>
    public float HandSlownessOf(Hand hand)
    {
        if (!FatigueEnabled)
        {
            return 0f;
        }

        var vigor = VigorOf((MuscleRegion)(int)hand);
        return (1f - vigor) * _tuning.TiredHandSlowness / MathF.Max(1e-3f, vigor);
    }

    /// <summary>遅い方の腕の <see cref="HandSlownessOf"/>（切られた腕は数えない）。</summary>
    public float HandSlowness => MathF.Max(_arms[0].Severed ? 0f : HandSlownessOf(Hand.Left), _arms[1].Severed ? 0f : HandSlownessOf(Hand.Right));

    private float Freshness(int region) => Smooth(Math.Clamp(_oxygen[region] / MathF.Max(1e-3f, _tuning.TiredKnee), 0f, 1f));

    private void UpdateStamina(float dt)
    {
        RefreshOrgans();
        for (var i = 0; i < MuscleRegions.Count; i++)
        {
            _capacity[i] = MeasureOxygenCapacity(i);
        }

        foreach (var servo in _servoWork)
        {
            if (!_world.HasConstraint(servo.Joint))
            {
                continue;
            }

            if (UsesJointDrive)
            {
                var a = _world.GetState(servo.BodyA).Orientation;
                var b = _world.GetState(servo.BodyB).Orientation;
                var before = Quaternion.Concatenate(servo.BeforeB, Quaternion.Conjugate(servo.BeforeA));
                var afterRotation = Quaternion.Concatenate(b, Quaternion.Conjugate(a));
                var travel = Vector3.Transform(FighterMath.RotationError(before, afterRotation), servo.BeforeA);
                // The solver reports the impulse on A. The measured rotation is B relative to A.
                var impulseOnParent = _world.GetAngularServoImpulse(servo.Joint, servo.BeforeA, servo.BeforeB);
                Draw((int)servo.Region, MathF.Max(0f, -Vector3.Dot(impulseOnParent, travel) / dt));
                continue;
            }

            // 精算は実際の速さで数える: 拘束の力積 J がした仕事は J ×（前後の速さの平均）以下。向きは取れないので、
            // 解く前と解いた後の相対角速度の大きい方を掛けて安全側にする（止まった体を加速した分は後の速さに出る）。
            // 力積²の項は取り置き（ReserveServo）だけの上限で、静止して重さを支えるだけの力積を仕事として数えない。
            // 関節の相対角速度は、胴の筋と同じ時間（MuscleEffortWindow）でならしたベクトルで見る: 保っているだけの関節が歩ごとに震える分（正負に振れる）は
            // 平均で消え、実際に動いた分だけが仕事になる（2026-11-22、TrunkWorkProbe: 構えて立つだけで腕 1 本 88 W を数え、回復を止めていた）。
            var impulse = _world.GetConstraintImpulseMagnitude(servo.Joint);
            var relative = _world.GetState(servo.BodyA).AngularVelocity - _world.GetState(servo.BodyB).AngularVelocity;
            var smoothing = 1f - MathF.Exp(-dt / MathF.Max(1e-4f, _tuning.MuscleEffortWindow));
            var smoothed = _servoRate.TryGetValue(servo.Joint, out var previousRate) ? previousRate + ((relative - previousRate) * smoothing) : relative;
            _servoRate[servo.Joint] = smoothed;
            var spent = MathF.Min(servo.Reserved, impulse * smoothed.Length());
            Draw((int)servo.Region, spent);
        }

        _servoWork.Clear();
        SettleJointWork(dt);

        var follow = 1f - MathF.Exp(-dt / MathF.Max(1e-3f, _tuning.StaminaRecoveryWindow));
        var stepWork = 0f;
        for (var i = 0; i < MuscleRegions.Count; i++)
        {
            // 届く速さを止める仕事率は、この歩で実際に出た仕事（押しても出せなかった分は数えない: 空で押すだけでは流れは止まらない）。
            stepWork += _stepRegionWork[i];
            _regionPower[i] += ((_stepRegionWork[i] / dt) - _regionPower[i]) * follow;
            _stepRegionWork[i] = 0f;
        }

        _workDone += stepWork;
        _recentMusclePower += ((stepWork / dt) - _recentMusclePower) * follow;
        Circulate(dt);
        StepMuscleBudget = 0f;
        StepMuscleAuthorized = 0f;
        for (var i = 0; i < MuscleRegions.Count; i++)
        {
            // 余裕があるうちは瞬発力として蓄えを使え、減るほど放出時定数による供給へ移る。
            // サーボの最大トルクからの安全側見積もりが、満タンの通常動作まで制限することを避ける。
            var fresh = Freshness(i);
            var release = fresh + ((1f - fresh) * (1f - MathF.Exp(-dt / MathF.Max(1e-3f, _tuning.StaminaReleaseWindow))));
            var capacity = OxygenCapacity(i);
            _budget[i] = MathF.Min(_oxygen[i] * capacity, (_regionDelivery[i] * dt) + (_reserve[i] * release));
            StepMuscleBudget += _budget[i];
        }

        Voxels?.HealBruises(dt, _tuning.BruiseRecoveryRate);
    }

    /// <summary>
    /// 酸素を流す（1 歩）: 部位が欲しい速さ（蓄えの不足を埋める速さ、血管の流れまで）→ 心臓の上限で比を揃えて縮める → 血の酸素で足りる分だけ送る →
    /// 肺が血へ取り込む。流す前の部位の蓄え [J] を <see cref="_reserve"/> に残す（放出の上限の元）。
    /// </summary>
    private void Circulate(float dt)
    {
        Span<float> want = stackalloc float[MuscleRegions.Count];
        Span<float> vessel = stackalloc float[MuscleRegions.Count];
        Span<float> capacity = stackalloc float[MuscleRegions.Count];
        var wanted = 0f;
        for (var i = 0; i < MuscleRegions.Count; i++)
        {
            capacity[i] = OxygenCapacity(i);
            _reserve[i] = _oxygen[i] * capacity[i];
            vessel[i] = VesselFlow(i);
            want[i] = MathF.Min(vessel[i], (1f - _oxygen[i]) * capacity[i] / dt);
            wanted += want[i];
        }

        var heart = HeartCapacity;
        var heartScale = wanted > heart ? heart / wanted : 1f;
        var blood = _blood * BloodCapacity;
        var sent = wanted * heartScale * dt;
        var bloodScale = sent > blood ? blood / sent : 1f;
        var scale = heartScale * bloodScale;
        var delivered = 0f;
        for (var i = 0; i < MuscleRegions.Count; i++)
        {
            var flow = want[i] * scale;
            _regionDelivery[i] = flow;
            // 届けられる最大の速さ: 血管の流れを、心臓と血の比で縮めたもの（駆動の割合 VigorOf の供給）。
            _regionSupply[i] = vessel[i] * scale;
            _regionLimit[i] = want[i] <= 1e-6f ? OxygenLimit.None
                : bloodScale < 1f ? OxygenLimit.Lungs
                : heartScale < 1f ? OxygenLimit.Heart
                : want[i] >= vessel[i] ? OxygenLimit.Vessels
                : OxygenLimit.None;
            if (capacity[i] > 1e-6f)
            {
                _oxygen[i] = Math.Clamp(_oxygen[i] + (flow * dt / capacity[i]), 0f, 1f);
            }

            delivered += flow * dt;
        }

        var lungs = LungCapacity;
        var room = BloodCapacity - MathF.Max(0f, blood - delivered);
        var uptake = MathF.Min(lungs * dt, room);
        _blood = Math.Clamp((MathF.Max(0f, blood - delivered) + uptake) / BloodCapacity, 0f, 1f);
        var cue = 1f - MathF.Exp(-dt / CueWindow);
        _uptake += ((uptake / dt) - _uptake) * cue;
        _heartShortfall += ((1f - heartScale) - _heartShortfall) * cue;
        var load = heart > 1e-6f ? wanted * heartScale / heart : 1f;
        _heartLoad += ((float.IsFinite(load) ? Math.Clamp(load, 0f, 1f) : _heartLoad) - _heartLoad) * cue;
    }

    /// <summary>見た目の合図（息の速さ・顔色）をならす時間 [s]。息や顔色は一瞬では変わらない。</summary>
    private const float CueWindow = 2.518f;

    /// <summary>部位 <paramref name="region"/> の蓄えから <paramref name="joules"/> を使う（仕事の記録も足す）。</summary>
    private void Draw(int region, float joules)
    {
        var capacity = OxygenCapacity(region);
        if (capacity > 1e-6f)
        {
            _oxygen[region] = MathF.Max(0f, _oxygen[region] - (joules / capacity));
        }

        _stepRegionWork[region] += joules;
        _regionWork[region] += joules;
    }

    /// <summary>
    /// 顔の青白さ（0..1、見た目だけ）: 心臓が部位の欲しがる酸素を送りきれない割合（ならした値）。心臓の働きが落ちる・全身で酸素を欲しがるほど強い。
    /// 試合の計算には使わない。
    /// </summary>
    public float Pallor => IsDead ? 1f : Smooth(Math.Clamp(_heartShortfall / PallorFull, 0f, 1f));

    /// <summary>心臓がこの割合だけ送りきれないとき、顔の青白さがいちばん強い。</summary>
    private const float PallorFull = 0.5f;

    // サーボは次のソルバー歩で力を出す。予算を先に取り置き、解いた実際の力積で次の歩に精算する。
    // 取り置きは解く前で後の速さが分からないので、相対角速度の絶対値・最大逆慣性の力積²の上限で安全側へ見積もる。
    // 肩と肘が上腕を共有する交差項は二次項を 2 倍して含める。精算（UpdateStamina）は力積 × 前後の相対角速度の大きい方。
    // 接触や脚の受動拘束との連立までは解かないため、全身の厳密なエネルギー保存ではなく駆動予算の近似。
    private void BudgetArmServos(float leftAllowance, float rightAllowance, float dt)
    {
        foreach (var arm in _arms)
        {
            var allowance = (arm.Hand == Hand.Left ? leftAllowance : rightAllowance) / 3f;
            var vigor = VigorOf(arm.Region);
            if (_world.HasConstraint(arm.ShoulderServo))
            {
                var shoulder = ReserveServo(arm.Region, arm.ShoulderServo, Parts.Chest, arm.UpperArm, 40f * _tuning.TorqueScale * ControlScale * FaintArm * vigor * Strength(arm.UpperArm) * arm.ShoulderTorqueFactor, allowance, dt);
                _world.SetAngularServoSettings(arm.ShoulderServo, _tuning.ArmServoHz, shoulder);
            }

            if (_world.HasConstraint(arm.ElbowServo))
            {
                var elbow = ReserveServo(arm.Region, arm.ElbowServo, arm.UpperArm, arm.Forearm, 30f * _tuning.TorqueScale * ControlScale * FaintArm * vigor * Strength(arm.Forearm) * arm.ElbowTorqueFactor, allowance, dt);
                _world.SetTwistServoSettings(arm.ElbowServo, _tuning.ArmServoHz, elbow);
            }
            var wrist = arm.Held?.Wrist ?? arm.Support?.Wrist;
            var heldBody = arm.Held?.Body ?? arm.Support?.Body;
            if (wrist is { } joint && heldBody is { } body && _world.HasConstraint(joint))
            {
                var torque = ReserveServo(arm.Region, joint, arm.Forearm, body, arm.WristTorque(_tuning) * _tuning.TorqueScale * arm.WristShare * ControlScale * FaintArm * vigor * Strength(arm.Forearm), allowance, dt);
                _world.SetAngularServoSettings(joint, arm.WristHz(_tuning) * MathF.Sqrt(arm.WristShare), torque, arm.WristDamping(_tuning));
            }
        }
    }

    private float ReserveServo(MuscleRegion region, int joint, int a, int b, float torque, float allowance, float dt)
    {
        var beforeA = _world.GetState(a);
        var beforeB = _world.GetState(b);
        var speed = (beforeA.AngularVelocity - beforeB.AngularVelocity).Length();
        float MaxInverse(int id)
        {
            var inertia = _world.GetLocalInertiaDiagonal(id);
            return 1f / MathF.Max(1e-9f, MathF.Min(inertia.X, MathF.Min(inertia.Y, inertia.Z)));
        }

        var inverse = 2f * (MaxInverse(a) + MaxInverse(b));
        var impulse = torque * dt;
        var scale = FatigueEnabled ? MuscleWork.ForceScale(impulse * speed, 0.5f * impulse * impulse * inverse, 0f, allowance) : 1f;
        impulse *= scale;
        var reserved = (impulse * speed) + (0.5f * impulse * impulse * inverse);
        _budget[(int)region] = MathF.Max(0f, _budget[(int)region] - reserved);
        StepMuscleAuthorized += reserved;
        _servoWork.Add(new ServoWork(joint, a, b, speed, reserved, region, beforeA.Orientation, beforeB.Orientation));
        return torque * scale;
    }

    /// <summary>両脚で受け持つ仕事（歩く・脚のバネを縮める）の、両脚の予算の合計。</summary>
    private float LegsBudget => _budget[(int)MuscleRegion.LeftLeg] + _budget[(int)MuscleRegion.RightLeg];

    /// <summary>
    /// 両脚で受け持つ仕事を、左右の脚の予算の比で分けて払う（左右がそろっていれば半分ずつ: 脚を 1 つの蓄えで数えていた頃と同じ減り方）。払えた量を返す。
    /// </summary>
    private float SpendLegsEnergy(float requested)
    {
        var left = _budget[(int)MuscleRegion.LeftLeg];
        var right = _budget[(int)MuscleRegion.RightLeg];
        var total = left + right;
        var share = total > 1e-9f ? left / total : 0.5f;
        return SpendMuscleEnergy(MuscleRegion.LeftLeg, requested * share) + SpendMuscleEnergy(MuscleRegion.RightLeg, requested * (1f - share));
    }

    /// <summary>両脚の駆動の割合（歩く・脚のバネ: 両脚が受け持つ。左右の平均）。</summary>
    private float LegsVigor => 0.5f * (VigorOf(MuscleRegion.LeftLeg) + VigorOf(MuscleRegion.RightLeg));

    /// <summary>脚 <paramref name="leg"/> の筋肉の部位（本人の左右。四足の後ろ脚は、同じ側の前脚と同じ部位で数える）。</summary>
    private MuscleRegion LegRegion(Leg leg) => leg.AigisLeft == _legParts[(int)Hand.Left].AigisLeft ? MuscleRegion.LeftLeg : MuscleRegion.RightLeg;

    /// <summary>本人の左右の脚の酸素の蓄えの残り（0..1。自分が感じる脚の疲れ: <see cref="FighterSense.SelfLegOxygenLeft"/>）。</summary>
    public float LegOxygen(Hand side) => _oxygen[(int)(side == Hand.Left ? MuscleRegion.LeftLeg : MuscleRegion.RightLeg)];

    /// <summary>
    /// 足の点を引く力（蹴り・踏み下ろしの <see cref="DriveFoot"/>、膝の制御）の仕事を、その脚の蓄えから払い、払える分まで力を縮める（腕の拳を引く力
    /// <see cref="ApplyArmDrive"/> と同じ数え方: 力積がした仕事 max(0, F·v dt) + |F|²dt²/(2m) + 力を保つ費用）。<paramref name="point"/> は力が掛かる体、
    /// <paramref name="velocity"/> はその点の速さ。反作用を受ける骨盤の分も数える。返すのは縮めた後の力。
    /// </summary>
    private Vector3 PayLegDrive(Leg leg, int point, Vector3 velocity, Vector3 force, float dt)
    {
        var region = LegRegion(leg);
        var pelvis = _world.GetState(Parts.Pelvis);
        var linear = (MathF.Max(0f, Vector3.Dot(force, velocity)) + MathF.Max(0f, Vector3.Dot(-force, pelvis.LinearVelocity))) * dt;
        var quadratic = force.LengthSquared() * dt * dt * 0.5f * ((1f / MathF.Max(1e-3f, _world.GetMass(point))) + (1f / MathF.Max(1e-3f, _world.GetMass(Parts.Pelvis))));
        var holding = _tuning.IsometricCost * force.Length() * dt;
        var budget = FatigueEnabled ? _budget[(int)region] : linear + quadratic + holding;
        var scale = MuscleWork.ForceScale(linear, quadratic, holding, budget);
        SpendMuscleEnergy(region, (linear * scale) + (quadratic * scale * scale) + (holding * scale));
        return force * scale;
    }

    private float SpendMuscleEnergy(MuscleRegion region, float requested)
    {
        var r = (int)region;
        var supplied = FatigueEnabled ? MathF.Min(requested, _budget[r]) : requested;
        _budget[r] = MathF.Max(0f, _budget[r] - supplied);
        StepMuscleAuthorized += supplied;
        Draw(r, supplied);
        return supplied;
    }

    private void ApplyLocomotion(Vector3 force, float dt)
    {
        if (LastCommand.ClampedMove.LengthSquared() <= 0f)
        {
            // 支持点へ戻す姿勢反射は既存の立位モデル。移動指令による仕事と区別する。
            ApplySharedImpulse(force * dt);
            RecordSupportForce(force * dt);
            return;
        }

        // 力積を受ける部位の質量と速度で仕事を数える（腰から打つ体の水平の力は骨盤だけ: ApplySharedImpulse）。
        var chestMass = UsesHipDrive ? 0f : _world.GetMass(Parts.Chest);
        var pelvisMass = _world.GetMass(Parts.Pelvis);
        var mass = chestMass + pelvisMass;
        var velocity = ((_world.GetState(Parts.Chest).LinearVelocity * chestMass) +
            (_world.GetState(Parts.Pelvis).LinearVelocity * pelvisMass)) / mass;
        var linear = MathF.Max(0f, Vector3.Dot(force, velocity)) * dt;
        var quadratic = force.LengthSquared() * dt * dt / (2f * mass);
        var walking = _tuning.WalkCost * SupportedMass * new Vector2(velocity.X, velocity.Z).Length() * LastCommand.ClampedMove.Length() * dt;
        var budget = FatigueEnabled ? LegsBudget : linear + quadratic + walking;
        var scale = MuscleWork.ForceScale(linear, quadratic, walking, budget);
        SpendLegsEnergy((linear * scale) + (quadratic * scale * scale) + (walking * scale));
        ApplySharedImpulse(force * (scale * dt));
        RecordSupportForce(force * (scale * dt));
    }

    /// <summary>
    /// 力積による仕事 max(0, F·v dt + |F|²dt²/(2m)) と保持費を予算内に収める。
    /// 静止時も二次項を含めるため、v=0 を抜け道にして加速できない。制動の仕事は蓄えへ戻さない。
    /// </summary>
    private void ApplyArmDrive(Arm arm, Vector3 force, Vector3 armPart, Vector3 internalPart, bool weightShift, float dt)
    {
        // internalPart: 肩と拳の間の内力（止まった腕を押す力・肘のてこで最大の力を超えた分）。反作用は全部を胸へ（体を前へ運ばない）。
        var borrowed = force - internalPart;
        var bodyForce = weightShift ? (borrowed - armPart) * _tuning.WeightShiftShare : Vector3.Zero;
        var chestMass = _world.GetMass(Parts.Chest);
        var pelvisMass = _world.GetMass(Parts.Pelvis);
        var chestForce = (-armPart * _tuning.HandReactionShare) - internalPart + (bodyForce * chestMass / (chestMass + pelvisMass));
        var pelvisForce = bodyForce * pelvisMass / (chestMass + pelvisMass);
        var linear = 0f;
        var quadratic = 0f;
        void Include(int body, Vector3 applied)
        {
            linear += MathF.Max(0f, Vector3.Dot(applied, _world.GetState(body).LinearVelocity)) * dt;
            quadratic += applied.LengthSquared() * dt * dt / (2f * _world.GetMass(body));
        }

        Include(arm.Forearm, force);
        Include(Parts.Chest, chestForce);
        Include(Parts.Pelvis, pelvisForce);
        var holding = _tuning.IsometricCost * MathF.Min(force.Length(), arm.LastCap) * dt; // 筋が出している力（肘のてこで増えた分は骨が受ける）
        var requested = linear + quadratic + holding;
        var budget = FatigueEnabled ? _budget[(int)arm.Region] : requested;
        var scale = MuscleWork.ForceScale(linear, quadratic, holding, budget);
        SpendMuscleEnergy(arm.Region, (linear * scale) + (quadratic * scale * scale) + (holding * scale));
        arm.LastForce = force * scale;
        _world.ApplyLinearImpulse(arm.Forearm, force * (scale * dt));
        _world.ApplyLinearImpulse(Parts.Chest, -((armPart * _tuning.HandReactionShare) + internalPart) * (scale * dt));
        if (weightShift && bodyForce.LengthSquared() > 0f)
        {
            ApplySharedImpulse(bodyForce * (scale * dt));
        }
    }
}

/// <summary>
/// 酸素の蓄えと供給を分けて数える筋肉のまとまり（<see cref="Fighter"/> の呼吸・循環のモデル）。左右の腕は <see cref="Hand"/> と同じ番号。
/// </summary>
public enum MuscleRegion
{
    LeftArm = 0,
    RightArm = 1,

    /// <summary>本人の左脚（2026-11-24 に脚を左右に分けた。前は両脚で 1 つ）。</summary>
    LeftLeg = 2,
    Trunk = 3,

    /// <summary>本人の右脚。</summary>
    RightLeg = 4,
}

/// <summary>部位へ届く酸素の速さを決めた所（呼吸・循環のボトルネック）。</summary>
public enum OxygenLimit
{
    /// <summary>蓄えが満ちていて欲しがっていない、または鎖のどこにも当たっていない。</summary>
    None = 0,

    /// <summary>部位の血管の太さ（働いて押しつぶされた流れ）。腕の追従の遅れに出る。</summary>
    Vessels = 1,

    /// <summary>心臓が送れる速さの合計。顔の青白さに出る。</summary>
    Heart = 2,

    /// <summary>肺が血へ取り込む速さ（血の酸素が尽きた）。息のあえぎと唇の青さに出る。</summary>
    Lungs = 3,
}

/// <summary><see cref="MuscleRegion"/> の数。</summary>
internal static class MuscleRegions
{
    internal const int Count = 5;
}

/// <summary>同じ向きの力を縮めて仕事予算に合わせる。二次式の単調区間を二分する純関数。</summary>
internal static class MuscleWork
{
    internal static float ForceScale(float linear, float quadratic, float holding, float budget)
    {
        static float Cost(float s, float a, float b, float c) => MathF.Max(0f, (a * s) + (b * s * s)) + (c * s);
        if (Cost(1f, linear, quadratic, holding) <= budget)
        {
            return 1f;
        }

        var low = 0f;
        var high = 1f;
        for (var i = 0; i < 24; i++)
        {
            var mid = (low + high) * 0.5f;
            if (Cost(mid, linear, quadratic, holding) <= budget)
            {
                low = mid;
            }
            else
            {
                high = mid;
            }
        }

        return low;
    }
}
