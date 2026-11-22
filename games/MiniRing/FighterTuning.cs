using System.Numerics;
using System.Text.Json.Serialization;
using Aigis.Physics;

namespace MiniRing;

/// <summary>
/// ファイター（アクティブラグドール）の調整値。直書きの長さ・時間・力・エネルギーは人の大きさの単位（2026-11-20 に人形の単位から相似で換算: docs/plans/REAL_SCALE_PLAN.md §2.1）。
/// <para>
/// 体を保つ力は「骨盤ホバー PD + 直立 PD + 水平速度 P」（Unity 版 Triage の ActiveRagdollMotor 方式）。
/// ゲインは加速度単位（1/s², 1/s）で持ち、実行時に質量・慣性を掛けて力積にする — 体格を変えても挙動が保たれる。
/// 関節は Bepu の AngularServo / TwistServo（Hz・減衰比・力上限＝バネ・ダンパ）で駆動する。
/// Bepu の SpringSettings は換算質量に対して定義される（Aigis anti-pattern A12）ので、Unity の N/m 値は移植しない。
/// </para>
/// </summary>
public sealed record FighterTuning
{
    public BodyBuild Build { get; init; } = BodyBuild.Standard;
    /// <summary>見えない所を記憶・音で補う時定数 [s]。通常の反応 0.2 s より十分遅い。</summary>
    public const float VisionLagSeconds = 0.755f;
    /// <summary>視覚を失った方向の位置の振れ幅 [腕の長さ]。</summary>
    public const float VisionNoiseReach = 0.35f;
    /// <summary>Aigis の既定体格（約 1.7m）に対する縮尺。1 = 実物大（2026-11-20 まで 0.355 ≒ 60 cm の人形。表示は <see cref="DisplaySpace"/> で人形の大きさに縮める）。</summary>
    public float Scale { get; init; } = 1f;

    /// <summary>既定質量（約 67.6kg）への倍率。1 = 実物（2026-11-20 まで 0.1 ≒ 6.8 kg の人形）。</summary>
    public float MassScale { get; init; } = 1f;

    /// <summary>
    /// 頭の大きさの倍率（体の縮尺に対して）。0.8 で身長が頭 7.5 個分（人の比率。1.15 では 5.6 個分）。
    /// 0.72 では小さすぎて見た目が変だったので一度 1.15 に戻した（予測編 P7）が、人の比率にしてほしいという要望で 0.8 に（ダメージ編 D2）。
    /// </summary>
    public float HeadScale { get; init; } = 0.8f;

    /// <summary>
    /// 腕の長さ方向の倍率。腕の届く長さ（肩 → 肘 → 拳）を以前と同じ約 0.19m に保つ値。
    /// 以前は関節の重なり代が 3cm 固定で腕を大きく削っていたので 1.4 で補っていた。重なり代を体の縮尺に合わせた（<see cref="JointOverlap"/>）ので 1.18。
    /// </summary>
    public float ArmLengthScale { get; init; } = 1.18f;

    /// <summary>
    /// 関節の重なり代 [m]: 子の部位の箱が、関節から親の側へはみ出す長さ。Aigis の既定（3cm）は 1.7m の体用で、60cm の体では
    /// 前腕が肘から後ろへ 3cm（前腕の 1/4）飛び出していた。体の縮尺に合わせる。
    /// </summary>
    [JsonIgnore]
    public float JointOverlap => HumanoidRagdoll.Overlap * Scale;

    /// <summary>足首から足の中心までの前へのずれを決める値 [m]（Aigis の既定 5cm は 1.7m の体用。そのままでは、かかとが足首から後ろへ 5cm 飛び出していた）。</summary>
    [JsonIgnore]
    public float FootForwardOffset => HumanoidRagdoll.FootForwardOffset * Scale;
    public float GloveRadius { get; init; } = 0.0676f;

    // ── 体幹（加速度単位）
    public float StandHeightRatio { get; init; } = 0.97f;
    public float HoverKp { get; init; } = 35.5f;
    public float HoverKd { get; init; } = 8.34f;
    public float MaxHoverAccel { get; init; } = 2.2f * 9.81f;

    /// <summary>
    /// 脚が床を押して体を持ち上げられる力の上限（自分の体重の倍、脚の力 1 のとき）。人はしゃがんだ姿勢から体重の 2〜3 倍を押し上げる
    /// （人を背負って立ち上がれる）。腰を持ち上げる力の上限は、これと <see cref="MaxHoverAccel"/> の大きい方（docs/GRAPPLING.md §13）。
    /// </summary>
    public float LegLiftCapacity { get; init; } = 2.5f;

    /// <summary>
    /// 脚の力を抜く（腰を重力で落とす）のに使う、目標が今の腰より低い量の幅 [m]。目標が <see cref="DropReleaseDeadband"/> より低くなってからこの幅で
    /// 0 → 1 に連続に効く（段階ではない）。人の大きさの体で、ダッキングの腰の沈み（5〜10 cm）が全部効く幅。
    /// </summary>
    public float DropReleaseDepth { get; init; } = 0.0563f;
    /// <summary>目標の低さがこれ以下なら脚は力を抜かない [m]（立っている間の腰の揺れ・歩く上下動で緩めない）。</summary>
    public float DropReleaseDeadband { get; init; } = 0.0338f;
    /// <summary>力を抜いて腰を落とすとき、落ちる速さへのブレーキ（<see cref="HoverKd"/>）が残る割合。重力に任せて速く沈み、目標に近づくほどブレーキが戻って止まる。</summary>
    public float DropBrakeKeep { get; init; } = 0.15f;
    /// <summary>股・腹の筋肉が腰を下へ引く加速度の上限 [m/s²]。重力（9.8）だけでは 0.34 秒に 56 cm 落ちるのが上限で、膝を折って頭を落とす速さに足りないぶん。</summary>
    public float DropPull { get; init; } = 0.8f * 9.81f;
    public float UprightKp { get; init; } = 213f;
    public float UprightKd { get; init; } = 23.8f;
    public float MaxUprightAccel { get; init; } = 532.5f; // 上体を起こす角加速度の上限 [rad/s²]

    /// <summary>
    /// 胴の回旋筋（腹斜筋・多裂筋・回旋筋群）が出せる最大トルク [N·m]（TorqueScale 倍する前）。胸と腰を、向きの目標（両手の位置から決めるひねり）へ
    /// 鉛直軸のまわりで回すバネの強さを決める: バネ定数 Kp = これ / (胸の鉛直軸まわりの慣性 × <see cref="MaxTorsoTwistRadians"/>)（ひねりの可動域の端でちょうど
    /// この力）、減衰は <see cref="UprightKd"/> と同じ減衰比（<c>2ζ√Kp</c>）、角加速度の上限はこの力 / その部位の慣性。傾き（前後・左右）は従来の
    /// <see cref="UprightKp"/>・<see cref="UprightKd"/>・<see cref="MaxUprightAccel"/> のまま。
    /// 70 N·m は、人の体幹の回旋の静的な最大トルク 70〜120 N·m の下の端（成人、Mayer 1984 ほか。パンチの動作の動的な値は 150〜250 N·m 級でさらに大きいので、
    /// 控えめな側を採った）。PhysicsVersion 67 までは、傾きと同じバネ（Kp = 213 /s²）を鉛直軸にも使っていて、胸の慣性 0.217 kg·m² × 213 × 可動域 0.611 rad = 28 N·m
    /// （人の約 1/4〜1/5）しか出ず、胸の回旋は 2 rad/s（人 8〜12）にとどまった。測った値（docs/plans/DRIVE_DOE_ANALYSIS.md、docs/arena/drive/doe_trunk_grid.csv、基準の点）:
    /// 28 → 60 → 90 → 120 N·m（先行 1）で拳の最高速 4.70 → 4.90 → 4.99 → 4.97 m/s、胸の最大角速度 3.4 → 4.7 → 5.4 → 5.9 rad/s。90 N·m 以上は効きが飽和し、
    /// 80・90・120 N·m では、距離の差を 0.02 の余裕で測る試合のテスト（BuildReadTests.SameStyle_LongArms…）か、触れるだけの肘が当たるかどうかの縁のテスト
    /// （ElbowContactTests.AFasterElbow…）のどちらかが外れる（70・60 N·m は両方通る。縁の値に敏感な測り方で、物理の誤りではない）。
    /// </summary>
    public float TrunkRotatorTorque { get; init; } = 120f;

    /// <summary>腰の回旋筋のトルク上限 [N·m]。null なら胸と同じ値を使う。150 N·m で最大溜めの腰の角速度 3.67 rad/s。</summary>
    public float? PelvisRotatorTorque { get; init; } = 150f;

    /// <summary>
    /// 胴のひねりの目標が動く速さ（角速度）を、回すバネの速度の目標に入れる割合（0 = 位置のバネだけ、1 = 目標の速さへそのまま合わせる）。
    /// 拳を引く PD（<see cref="HandKd"/>）が目標の速度を先行に使うのと同じ構成（一つの現象には一つのモデル）: 位置のバネだけだと、減衰が
    /// 目標への追いつきを Kp·誤差 / Kd に抑え、動く目標に遅れて追う。倍率ではなく、1 = 目標の動きと同じ速さで回そうとする。
    /// </summary>
    public float TrunkTwistFeedForward { get; init; } = 1f;

    /// <summary>腰から胸へひねりの指令が移る時定数 [s]。腰の解放後も胸に逆回転の溜めが残る。</summary>
    public float ChestTwistResponseSeconds { get; init; } = 0.012f;

    /// <summary>
    /// 胴のひねりの目標の速さの上限 [rad/s]（先行の飽和）。人の体幹の回旋の角速度の最高は、パンチ・投げで 15 rad/s ほど（骨盤 8〜12、胸 12〜15）。
    /// 目標が飛んだ（指令が一歩で変わった）ときの先行の暴れを抑える。
    /// </summary>
    public float TrunkTwistMaxRate { get; init; } = 15f;
    public float TurnSpeedRadians { get; init; } = 3.744f; // 向きを変える角速度の上限 [rad/s]（人形の単位の 2π × 0.596）

    // ── 移動・脚
    public float WalkSpeed { get; init; } = 0.923f;
    public float MoveGain { get; init; } = 5.96f;
    public float MaxMoveAccel { get; init; } = 6f;
    public float StrideLength { get; init; } = 0.451f;
    public float NeutralHipRadians { get; init; } = -8f * MathF.PI / 180f;
    public float NeutralKneeRadians { get; init; } = 14f * MathF.PI / 180f;
    public float GaitHipSwingRadians { get; init; } = 30f * MathF.PI / 180f;
    public float GaitKneeLiftRadians { get; init; } = 40f * MathF.PI / 180f;
    /// <summary>脚のサーボの固有振動数 [Hz]。足の運びで、踏み出した足を 0.17〜0.34 秒で置き場所まで運び切れる速さ（以前の歩行は 12、人形の単位）。</summary>
    public float LegServoHz { get; init; } = 11.92f;

    /// <summary>股・膝・足首のサーボが出せる最大トルク（TorqueScale 倍する前）。足を置き場所へ運び切れる強さ（以前は 120 / 100 / 100）。</summary>
    public float HipMaxTorque { get; init; } = 360f;
    public float KneeMaxTorque { get; init; } = 300f;
    public float AnkleMaxTorque { get; init; } = 900f;
    /// <summary>首（頭と胴をつなぐバネ）の固有振動数 [Hz]。低いほど打たれた頭が大きく揺れる。</summary>
    public float NeckServoHz { get; init; } = 5.36f;

    /// <summary>足の運び（足の置き場所のモデル。<see cref="MiniRing.Footwork"/>）。</summary>
    public FootworkTuning Footwork { get; init; } = new();

    /// <summary>つま先の蝶番のサーボの強さ（TorqueScale 倍する前）。</summary>
    public float ToeMaxTorque { get; init; } = 20f;

    /// <summary>
    /// つま先の蝶番の可動域の止め（上 25°・下 −60°）のバネの固さ [Hz]。つま先は軽い（足の 4 割）ので、止めが既定の 15 Hz では、
    /// 蹴りで足が振られる遠心の力に負けて止めを越えて 62° まで振れ、跳ね返って揺れた（ToeBounceProbe）。止めは足の指の骨と腱が伸びきる所なので固い。
    /// 60 Hz（人形の単位。人の大きさで 35.75 Hz）は 240 Hz の歩みで安定に解ける上限の側（歩みの 1/4）。可動域の中でつま先を戻すサーボは脚と同じ LegServoHz のまま
    /// （サーボまで 60 Hz にすると、足が 1 枚の固い板のようになって立ち方・倒れ方が変わった: 立つ・起き上がる・リングの縁から落ちるテストが外れた）。
    /// </summary>
    public float ToeStopHz { get; init; } = 35.75f;

    /// <summary>首のバネの減衰比。殴られて持っていかれた頭が、揺れ返さずに戻る（打たれていないときもぐらつかない）。</summary>
    public float NeckDampingRatio { get; init; } = 0.7f;

    /// <summary>首のバネが出せる最大トルク（TorqueScale 倍する前）。</summary>
    public float NeckMaxTorque { get; init; } = 60f;

    /// <summary>折れた首の、胴に残った側の破片を胴に固定するサーボの最大トルク（TorqueScale 倍する前）。固定の役で、動かす筋ではない（首の筋は折れたとき外れる）。胴の側の首の根は動かない。</summary>
    public float NeckWeldTorque { get; init; } = 500f;

    // ── 腕（サーボ + 拳への PD 力）
    public float ArmServoHz { get; init; } = 8.94f;
    public float ArmServoMaxSpeed { get; init; } = 23.83f;
    public float HandKp { get; init; } = 390.5f; // 目標 → 拳のバネ [1/s²]（約 3.1Hz）。固いほどきびきび追う
    public float HandKd { get; init; } = 31.6f; // 減衰比 ≒ 0.8（行き過ぎずに止まる）
    /// <summary>
    /// 体の動きを拳へ移すとき（<see cref="HandCommand.Spring"/>）、体幹の運動エネルギーが拳へ抜ける時定数 [s]。バネが伸びて戻るまでの速さ。
    /// </summary>
    public float BodyToFistTime { get; init; } = 0.0671f;

    /// <summary>体幹から抜けた運動エネルギーのうち、拳の速さになる割合（残りは筋肉と床で失われる）。</summary>
    public float BodyToFistEfficiency { get; init; } = 0.5f;

    /// <summary>拳の目標の先走り（体の動きから移った運動量）を体へ戻すバネの固有振動数 [Hz] と減衰比。低いほど先まで走る。</summary>
    public float LeadSpringHz { get; init; } = 1.787f;
    public float LeadSpringDamping { get; init; } = 0.4f;

    /// <summary>拳を出していく間（<see cref="HandCommand.Spring"/>）に、拳を目標へ引くバネに残す減衰の割合（減衰比 0.8 → 約 0.25）。目標の手前でブレーキをかけない。</summary>
    public float LooseHandDamping { get; init; } = 0.3f;
    public float MaxHandForce { get; init; } = 350f;

    /// <summary>
    /// 拳を引く指令（PD）の目標を肩から腕の長さの球の中へ縮めてから使うか。既定の false が試合の体（PhysicsVersion 65）: 指令はボットの置いた
    /// 点へ向け、届かない点を狙えば全力の指令になる（腕の形 = 肩・肘のサーボの IK はいつも届く範囲で解く）。true は前の体（PhysicsVersion 60、punch-path まで）を
    /// 測るための反事実（punchlab の <c>--clamp true</c>）。試合には使わない。
    /// </summary>
    public bool ClampHandTarget { get; init; }

    /// <summary>
    /// 拳を押す力のうち体に反作用の無い分（腕の筋力の反作用の半分を胸へ返さない近道・脚と腰から借りる分）が全部効く、力の向きの肩に対する
    /// 拳の速さ [m/s]。0 から smoothstep で上がる。止まった腕（伸びきった・押し付けた）の力は内力として全部を胸へ返す（届かない目標を
    /// 狙い続ける腕が体を前へ運ばない: ArmMuscleTests）。0.84 m/s は打撃の鋭さの始まり（PunchMechanics.SharpnessSpeedLow 0.587）と
    /// 終わり（1.343）の間の値で、測った値ではない近道（docs/DEBT.md「拳を引く筋肉」）。
    /// </summary>
    public float BodyBorrowSpeed { get; init; } = 0.839f;

    // ── 拳を引く筋肉（Hill 型、ArmMuscle。docs/BOXING.md「拳を引く筋肉」）
    // 時間と速さは人の値のまま（実物大。2026-11-20 まで人形の大きさの相似則で縮めていた: 時間 √0.355 = 0.596 倍・速さ 0.596 倍、
    // docs/SWORD_MOTION.md §5）。力の大きさは MaxHandForce（筋の断面 ∝ 大きさ²、BodyBuild.SectionRatio）。

    /// <summary>
    /// 筋肉の活性化の立ち上がりの時定数 [s]。人の筋は興奮から 10〜20 ms（Zajac 1989、Thelen 2003 は 15 ms）。
    /// </summary>
    public float MuscleActivationTime { get; init; } = 0.0151f;

    /// <summary>
    /// 筋肉の活性化の緩みの時定数 [s]。人の筋は 40〜80 ms（Thelen 2003 は 50 ms、Winters 1995 は 40〜60 ms）。60 ms。
    /// 全力で出していた主動筋がすぐには抜けない（打ち抜ける）、止める筋が遅れて効く、の元。
    /// </summary>
    public float MuscleDeactivationTime { get; init; } = 0.0604f;

    /// <summary>
    /// 肘の筋が力を出せなくなる肘の角速度 [rad/s]（Hill の最大の縮む速さを関節の角速度に直した値）。人の肘の伸筋・屈筋の線維 6〜9 cm ×
    /// 最大の縮む速さ 10 線維長/s（OpenSim の既定）÷ てこ 2 cm ≒ 40 rad/s（肘 90° の腕で、
    /// 拳を肩から押し出す速さ 0.21 m × 40 = 8.4 m/s。人形の単位では 67 rad/s・5.0 m/s だった）。
    /// </summary>
    public float ElbowMuscleMaxSpeed { get; init; } = 39.9f;

    /// <summary>
    /// 肩の筋（大胸筋・三角筋）が力を出せなくなる肩の角速度 [rad/s]。線維 12〜18 cm × 10 線維長/s ÷ てこ 4 cm ≒ 40 rad/s（肘と同じ）。
    /// てこ（肩から拳の距離、肘 90° で 0.42 m）が肘より長いので、拳を横・上下へ振る速さの上限は 16.6 m/s（フック・武器の振り）。
    /// </summary>
    public float ShoulderMuscleMaxSpeed { get; init; } = 39.9f;

    /// <summary>Hill の式の曲がり a/F0（速筋の多い腕の筋で 0.2〜0.3: Hill 1938、Winters 1990）。</summary>
    public float HandMuscleCurvature { get; init; } = 0.25f;

    /// <summary>伸ばされながら出せる力の上限（等尺の何倍か）。人の筋で 1.3〜1.8（Katz 1939、Joyce 1969）。中ほどの 1.5。</summary>
    public float HandMuscleEccentricMax { get; init; } = 1.5f;

    /// <summary>伸ばされる側の力の、速さ 0 での傾きが縮む側の何倍か（Katz 1939 で 2〜6 倍）。急すぎると歩ごとに振れるので小さい側の 2。</summary>
    public float HandMuscleEccentricSlope { get; init; } = 2f;

    /// <summary>
    /// 肘のトルクが最大になる肘の角度 [rad]（0 = 伸びきり）。曲げ・伸ばしとも 80〜100°（Kulig 1984、Murray 1995）の 90°。
    /// 拳を引く筋肉の「構え」（最大の力・最大の縮む速さを測る姿勢）もこの角度。
    /// </summary>
    public float ElbowPeakRadians { get; init; } = MathF.PI / 2f;

    /// <summary>肘の曲げきりの角度 [rad]（肘のサーボの上限 140° と関節の 145°）。</summary>
    public float ElbowFlexLimitRadians { get; init; } = 145f * MathF.PI / 180f;

    /// <summary>
    /// 伸びきり・曲げきりでの肘のトルクの、最大に対する割合。伸ばすトルクは伸びきりの近くで最大の 5〜6 割、曲げるトルクは曲げきりで
    /// 5 割前後（Kulig 1984、Murray 1995）。
    /// </summary>
    public float ElbowEndTorque { get; init; } = 0.5f;

    /// <summary>
    /// 肘のてこ（肩から拳の距離が肘の角度で変わる割合 L1 L2 sin θ / r）を数える角度の下限 [rad]。伸びきり（θ → 0）でてこは 0 になり、
    /// 拳を肩から押し出す力は無限、要る肘の角速度も無限になる（特異姿勢）。そこから先は骨と靭帯（肘の止まり）が受け持つので、
    /// 筋のてこは 10° で頭打ちにする（構えのてこの約 1/8: 伸びきりの近くで外へ動く拳は、肘の角速度が 8 倍要るので速さの関係で力が抜ける）。
    /// 力の側は構えの力で頭打ち（ArmMuscle.Leverage）。
    /// </summary>
    public float ElbowMinLeverRadians { get; init; } = 10f * MathF.PI / 180f;

    /// <summary>手首の筋肉（武器を握ったとき、武器を向きの目標へ回すサーボ）の固さ [Hz]。</summary>
    public float WristServoHz { get; init; } = 8.94f;

    /// <summary>手首が武器を回す最大トルク（TorqueScale 倍する前。肩の 40 の 1/4）。重い武器ほど向きを変えるのが遅れる。</summary>
    public float WristMaxTorque { get; init; } = 10f;

    /// <summary>
    /// 両手で握っていた武器を片手で持つことになったときの、手首の角度のバネ（固さとトルク）の割合。両手の握りは、2 本の手首に加えて、
    /// 2 つの拳が握りの別々の所を押し引きする偶力で武器を支える。片手ではそれが手首 1 本になるので、1/3 にする（ユーザーの指定）。
    /// </summary>
    public float OneHandWristShare { get; init; } = 1f / 3f;

    /// <summary>
    /// 両手で握った棒の握りの点を棒に沿って動かす速さの上限 [m/s]（<see cref="Fighter"/> の握りの矛盾の解消）。両手の拳の目標の距離と
    /// 握りの間隔のずれを、棒の中心に近い方の手が滑って詰める。棒術の持ち替えで手が棒の上を滑る速さの下の方: 握りの間隔
    /// 31 cm のずれは 0.12 秒で詰まる。摩擦の上限から出した値ではなく、握ったまま滑る近道（CLAUDE.md §8）。
    /// </summary>
    public float GripSlideSpeed { get; init; } = 2.52f;

    /// <summary>棒を握る 2 つの手の間隔の下限 [m]（拳の幅ほど: 素手の拳 7.9 cm = <c>Weapon.HandSpacing</c>）。これより近づく握りにはしない。</summary>
    public float GripMinSpacing { get; init; } = 0.0845f;

    /// <summary>両手持ちで、添える手が握りをつかめる、拳と握る所の距離 [m]（これより近づいたら玉継手でつなぐ）。</summary>
    public float SupportGripReach { get; init; } = 0.0563f;

    /// <summary>
    /// 空いた手が <see cref="HandCommand.Grip"/> で武器を拾う・もう一方の手として添える、拳と柄の距離の上限 [m]。素手の拳の幅（7.9 cm =
    /// <c>Weapon.HandSpacing</c>）のおよそ 1.8 倍: 手の幅ほど（人形の縮尺 0.32 倍の手）の所へ手を持っていけば握れる。これより遠いと握れない
    /// （握った後は <see cref="SupportGripReach"/> まで拳を握りへ寄せてつなぐ）。VR の手は縮尺（約 3 倍）で人形へ写るので、実際の手では 15 cm ほどの許し。
    /// </summary>
    public float WeaponGraspReach { get; init; } = 0.141f;

    /// <summary>
    /// 生成で握らせた武器と握った本人の部位を「重ねて置いた」と見なす、格子を囲む箱どうしの隙間 [m]（これより近い組は、一度この隙間より離れるまで当たらない。
    /// <c>BoxingGame.MarkSpawnOverlaps</c>）。両手で握る六尺棒は、T 字の腕の肩の高さに両手の握りの間を横切って置かれ、胸・上腕とは重なり、
    /// 首の下とは箱が触れない程度の隙間で並ぶ。最初の歩で手が棒を引くとその隙間を詰めて首の下を 50〜80 セル削った（重なりだけを除いたとき、2026-11-20）。
    /// </summary>
    public float SpawnWeaponClearance { get; init; } = 0.0563f;

    /// <summary>
    /// 生成で握った本人の体に重ねて置いた（<see cref="SpawnWeaponClearance"/> より近い）武器が、握った本人の体に当たらない時間 [s]（生成の T 字の腕が構えへ追いつくまで。テレビ中継の助走 <c>TvShowRules.SettleSeconds</c> と同じ 1.68 秒）。
    /// 重なりだけを除くと、刀の組は 0.1 秒で一度離れ、腕が構えへ下りる 0.11〜0.27 秒に刀が自分の胸を約 100 セル削って、2 秒で倒れた（刀の訓練の世界、2026-11-20）。
    /// この後もまだ重なっている組は、<see cref="SpawnWeaponClearance"/> より離れるまで当たらない。
    /// </summary>
    public float SpawnWeaponSettleSeconds { get; init; } = 1.678f;

    /// <summary>
    /// 腕が使えなくなった（切り落とされた・骨が折れた）手の握りが抜けていく時定数 [s]。握りが持ちこたえる力は掴みの強さ
    /// （<see cref="GrabBreakForce"/>）から exp(−t / これ) で落ちていき、武器の重さ・振られる力を支えられなくなった所で武器が抜ける
    /// （止まっていれば、手と武器の軽い方の重さを支えられなくなる約 5 秒後）。筋肉に力を送る神経・血が絶たれた手の指がほどける速さの近似。
    /// </summary>
    public float DeadGripRelaxSeconds { get; init; } = 0.839f;

    /// <summary>
    /// 折れた骨の両側をつなぐ肉の、位置のバネの固さ [Hz]。折れ目で回る玉継手として接続面を近く保つ（35.75 Hz。人形の単位で 60 Hz = 240 Hz ステップの 1/4）。曲がる柔らかさは向きのバネで表す。
    /// </summary>
    public float FractureLinkHz { get; init; } = 35.75f;

    /// <summary>
    /// 折れた骨の両側をつなぐ肉の、向きのバネ（駆動の無いバネ: 目標は折れた瞬間のまっすぐな向きのまま）の固さを、折れた先が水平に
    /// 突き出したとき自分の重さでたわむ角 [rad] で決める: バネ定数 k = m g d / この角（m は折れた先とそこにぶら下がる部位の質量、d は折れ目から
    /// その重心まで）。0.6 rad（約 35°）: ぶらぶら揺れるが、重力に負けきって真下へ垂れはしない（ユーザーの指定 2026-11-13「完全には重力に負けない
    /// 程度」。初めの 1.5 Hz では前腕の先は真下へ垂れた）。握った武器の分だけ重い先は、さらにたわむ。出せるトルクの上限は、残った肉の断面が
    /// 曲げで持ちこたえる強さ（<see cref="Voxels.BoneBreak.BendTorque"/>）。
    /// </summary>
    public float FractureSagRadians { get; init; } = 0.6f;
    public float HandReactionShare { get; init; } = 0.5f;
    /// <summary>
    /// 疲れた腕の筋肉の、最大の縮む速さを下げる量 [s/m]: 拳の速さに直した最大の縮む速さ v_max（肘・肩それぞれ、構えの姿勢で）について
    /// 1 / v_max' = 1 / v_max + (1 − Vigor) / max(Vigor, 0.001) × この値（Hill の力と速さの関係は <see cref="ArmMuscle.ForceVelocity"/> の 1 つ。
    /// 疲れはその v_max を下げる）。蓄えの残りが半分の Vigor 0.5 で肘の v_max は 8.4 → 3.2 m/s。0 なら疲れで速さは落ちない（仕事予算は残る）。
    /// </summary>
    public float TiredHandSlowness { get; init; } = 1f / 4.028f;

    public float MaxTargetSpeed { get; init; } = 10.07f; // 目標の速度の上限 [m/s]（動きを 2 倍にしたので、速い腕の振りも追えるように）
    public float TargetVelocitySmoothing { get; init; } = 14.9f;

    // ── ボクシングの力学（PunchMechanics）
    /// <summary>体の質量を借りられたときに腕の力へ上乗せできる最大の割合（脚・腰の力）。</summary>
    public float BodyDrive { get; init; } = 1.5f;
    /// <summary>腕へ上乗せした体の力のうち、胴を拳の方向へ押し出す（体重移動として見える）割合。</summary>
    public float WeightShiftShare { get; init; } = 0.3f;
    /// <summary>両手の位置から決める胴のひねりの最大角。腰はその一部だけ回る。</summary>
    public float MaxTorsoTwistRadians { get; init; } = 35f * MathF.PI / 180f;
    public float HipTwistShare { get; init; } = 0.95f;

    /// <summary>腹部を別剛体に分け、手の目標だけで骨盤から胸までのひねりを駆動する試験設定。</summary>
    public bool SplitTorso { get; init; }
    /// <summary>分割胴の試験設定で、脚のばねを直進でなく腰の回旋に使う。</summary>
    public bool CompactTorsoWave { get; init; }

    /// <summary>拳の前方目標から脚の回旋と踏み込みの配分を連続に変える（分割胴）。</summary>
    public bool AdaptivePunchForm { get; init; }

    /// <summary>試験用。親子関節のトルクと床・相手との接触で駆動する。</summary>
    public bool JointDrive { get; init; }

    /// <summary>正の値で従来の接地制限付き補助を使う。姿勢・移動のゲインと脚の補助力上限に掛け、重力補償は既存の式を使う。</summary>
    public float JointSupportAssist { get; init; }

    /// <summary>関節駆動で、別工程のグローブ接触に従来の打撃処理を使う。</summary>
    public bool JointLegacyGloveImpact { get; init; }

    /// <summary>支持脚の股関節が骨盤の姿勢を受け持つ割合。</summary>
    [JsonInclude]
    internal float JointStanceHipShare { get; init; }

    /// <summary>重心の位置と速度のずれを、支持足の足首角へ返す無次元の係数。</summary>
    [JsonInclude]
    internal float JointStanceAnkleGain { get; init; }
    /// <summary>右手を出す間に左のガードを引く距離の上限（腕の長さの割合）。</summary>
    public float CompactLeftRetractShare { get; init; } = 0.08f;
    /// <summary>脚のばねを解放する入力が手の目標に追いつく時定数 [s]。</summary>
    public float WaveFootSeconds { get; init; } = 0.025f;
    public float WavePelvisSeconds { get; init; } = 0.012f;
    public float WaveAbdomenSeconds { get; init; } = 0.020f;
    public float WaveChestSeconds { get; init; } = 0.022f;
    public float WaveHandSeconds { get; init; } = 0.100f;
    /// <summary>脚のバネを全開放する手の目標の先行距離（腕の長さの割合）。</summary>
    public float WaveFootTravelShare { get; init; } = 0.12f;
    /// <summary>頭のずれ（前傾・スリップ）で胴を傾けられる最大角。</summary>
    public float MaxLeanRadians { get; init; } = 30f * MathF.PI / 180f;

    /// <summary>後ろへ反らす（スウェー）ときに腰が受け持つ割合（胸は全部）。上体ごと後ろへ倒れ、首だけで反らない。前へ倒すときは 0.25。</summary>
    public float HipBackLeanShare { get; init; } = 0.6f;

    /// <summary>沈み込みの最大（立つ高さに対する割合）。膝と股を曲げて腰を落とす。0.4 では、ボディを打つ・かわすたびに膝が床近くまで落ち、ダウンと見分けにくかった（2026-11-12 に 0.28）。</summary>
    public float MaxCrouch { get; init; } = 0.3f;

    /// <summary>
    /// レフェリーなしの試合（<see cref="BoxingCorners.Referee"/> = false）で、<see cref="MaxCrouch"/> の代わりに使う沈み込みの最大。倒れた相手は床すれすれに横たわるので
    /// （肩から床まで腕の 2 倍ほど）、立った高さの 0.3 倍の沈みでは腕が届かない。膝を深く折って腰を落とす（しゃがむ・片膝をつく）ぶんの深さ。
    /// 近道: 倒れた相手を打つとき専用に、腰を落とせる深さの上限だけ広げる（§8）。
    /// </summary>
    public float GroundCrouch { get; init; } = 0.75f;

    /// <summary>頭を下げる量のうち、腰から前へ折って（ヒップヒンジ）下げる割合。残りは膝と股を曲げて腰を落とす。ウィービング・ダッキングが膝だけで沈まない。</summary>
    public float HipHingeShare { get; init; } = 0.4f;

    /// <summary>腰から前へ折ったとき、頭が前へ出る分のうちお尻を後ろへ引いて打ち消す割合（1 で頭は足の上のまま）。</summary>
    public float HipsBackShare { get; init; } = 1f;

    /// <summary>股から上体を前へ折る（ヒップヒンジ）最大角。</summary>
    public float MaxHingeRadians { get; init; } = 40f * MathF.PI / 180f;

    // ── 床からの支え（Fighter.Support.cs、docs/GRAPPLING.md 段 1）
    /// <summary>
    /// 体を立たせる力（ホバー・起こすねじり・移動）を、床に着いている所の凸包（支持多角形）の中の圧力中心で出せる分に限る。
    /// false は支えの制限だけを外す（支えがどこからともなく出る。比べもの用。靭帯・肘の止まり・投げの場合分けの削除は戻らない）。
    /// </summary>
    public bool SupportFromGround { get; init; } = true;

    /// <summary>
    /// 脚を失った体（<see cref="Fighter.LegShortfall"/> &gt; 0）には、釣り合い・起き上がりの手足の制限を掛けない近道（比べもの用。2026-11-17 にユーザーの指定で既定は false = 近道なし: 片脚を失った体は踏み直せずに倒れ、這う）。
    /// false（既定）は脚を失った体も無傷の体と同じ支えのモデルを通る。true は前の近道（breakdemo の比べもの用）。
    /// </summary>
    public bool LegLossSupportShortcut { get; init; }

    /// <summary>立たせる力の制限のしかた（<see cref="MiniRing.SupportModel"/>）。比べるための切り替え。</summary>
    public SupportModel SupportModel { get; init; } = SupportModel.Soft;

    /// <summary>連続の釣り合い（<see cref="Fighter.Balance"/>）の支えの領域（凸包か、両足を直径とする円か）。</summary>
    public SupportRegion SupportRegion { get; init; } = SupportRegion.Polygon;

    /// <summary>
    /// 捕捉点が支えの領域の外へこれ × 脚の長さだけ出たら、立たせる力は 0（取り戻せない: 一歩の大きさ。足の運びの 1 歩の上限 MaxStride と同じ程度）。
    /// </summary>
    public float SupportRecovery { get; init; } = 0.5f;

    /// <summary>縦の支えが 0 になる支持領域からの距離を <see cref="SupportRecovery"/> の倍数で表す。</summary>
    public float SupportVerticalRecovery { get; init; } = 1.5f;

    /// <summary>縦の支持も捕捉点から見積もる。関節駆動の試験設定では従来の制御を使う。</summary>
    public bool UseCaptureForVerticalSupport { get; init; }

    /// <summary>
    /// 釣り合いの PID の積分の強さ [1/s]（docs/GRAPPLING.md §9）: 上体を起こすねじり・重心を足の上へ寄せる力に、ずれの漏れのある積分 × これ × 比例の固さ を足す。
    /// 0 で前の PD（バネ・ダンパ）。大きいほど、ずれたままの姿勢を遅れて強く押し戻し、戻りすぎて揺れ返す（共振の山が鋭くなる）。
    /// </summary>
    public float BalanceKi { get; init; }

    /// <summary>釣り合いの積分の漏れの時定数 [s]（古いずれを忘れる速さ）。</summary>
    public float BalanceIntegralSeconds { get; init; } = 1.678f;

    /// <summary>起き上がる間、重心が床に着いた手足の凸包の外へこれ × 脚の長さ出たら、手足は体を押し上げられない（docs/GRAPPLING.md §8）。</summary>
    public float RiseRecovery { get; init; } = 0.25f;

    /// <summary>起き上がる間、床に着いた手で押して重心を足の上へ移す、ばねの固さ [1/s²]（臨界減衰。手足の支えと、重心が足の上に無い分だけ効く）。</summary>
    public float RiseShift { get; init; } = 14.2f;

    /// <summary>起き上がる間、重心がまだ足に乗っていなくても上体を起こすねじりを残す割合（手で床を押して上体を支える分）。</summary>
    public float RiseTorsoFloor { get; init; } = 0.05f;

    /// <summary>起き上がる間、膝を浮かせて手と足で体を支える姿勢で腰を上げられる高さ（立つ高さに対する割合。腕の長さで床に手が届く腰の高さのおよそ）。</summary>
    public float RiseBearHeight { get; init; } = 0.7f;

    /// <summary>起き上がる間に腰を上げられる高さの上限（Fighter.RiseCap）をならす時定数 [s]（手足が着く・離れるのちらつきで腰が落ちない）。</summary>
    public float RiseCapSeconds { get; init; } = 0.503f;

    /// <summary>
    /// 倒れてからこの時間 [s] のうちに立ち直れば、審判はダウンを取らない（スリップ・投げられてすぐ立つ。2026-11-17、ユーザーの指定）。
    /// 意識が飛んでいる・立てない体はすぐ取る。ルールの値（起き上がりは床から 4〜5 秒かかるので、手をついてすぐ立ち直る程度）。
    /// </summary>
    public float DownGrace { get; init; } = 3.357f;

    /// <summary>倒れている体が、握った手を離さず・入力元の足の目標を受け付ける意識の下限（捨て身技。これより薄ければ倒れた体の反射だけ）。</summary>
    public float GroundGripAwareness { get; init; } = 0.6f;

    /// <summary>起き上がる間、手で押して重心を足の上へ移す加速度の上限 [m/s²]（膝・手の摩擦に勝って腰を足の上へ引ける大きさ）。</summary>
    public float RiseShiftMaxAccel { get; init; } = 15f;

    /// <summary>起き上がる間、足を重心の真下からつま先の向きへずらす量（足のかかと側の箱の半分の長さに対する倍。かかとが重心の下に来る）。</summary>
    public float RiseFootLead { get; init; } = 1f;

    /// <summary>部位の箱の角がこの高さ [m] より床に近ければ、床に着いている（支えに数える）。マットに着いた判定（CanvasTouch 3.4 cm）と同じ大きさ。</summary>
    public float SupportContactGap { get; init; } = 0.0282f;

    /// <summary>足の裏とマットの摩擦係数（ゴム底とキャンバスで 0.8〜1）。床が体を水平に押せる力の上限 = これ × 垂直抗力。</summary>
    public float SoleFriction { get; init; } = 0.9f;

    // ── 関節の靭帯（Fighter.Ligaments.cs、docs/GRAPPLING.md 段 6）
    /// <summary>
    /// 肘の靭帯（前の関節包・内側側副靭帯）が伸び始める過伸展のねじりの力 [N·m]（人の大きさの値。体には TorqueScale × 身長比³ を掛ける）。
    /// 屍体の肘の過伸展の試験で、損傷が出始める力は数十 N·m（35 を採る）。
    /// </summary>
    public float ElbowLigamentYield { get; init; } = 35f;

    /// <summary>
    /// 靭帯の伸びにくさ η [N·m·s/rad]（人の大きさの値）: 降伏を超えた分 (τ − τy) で、伸びの速さ (τ − τy) / η。
    /// 降伏の 2 倍の力をかけ続けると約 0.5 秒（人形の単位。実物大では約 0.84 秒）で切れる大きさ（関節技が極まってから「折れる」までの時間）。
    /// 段 2（相似の換算）: η の次元は N·m·s/rad（× 47.3）だが <see cref="TorqueScale"/> では × 28.2 にしかならないので、時間の倍率 1.678 を値に掛けた（50 → 83.9）。
    /// </summary>
    public float LigamentViscosity { get; init; } = 83.9f;

    /// <summary>
    /// 肘の止まり（関節の制限）のばねの固有振動数 [Hz]。Aigis の既定 15 Hz では（人形の単位）、軽い前腕は肘を反らせるねじり 1〜2 N·m（人形の単位）で 40° 以上めり込み、
    /// 前腕が回りきってしまった（GrapplingTests）。人形の単位の 60 Hz（人の大きさで 35.75 Hz）で同じねじりのめり込みは数度（固さ ∝ 振動数²）。240 Hz の歩で解ける上限の内。
    /// </summary>
    public float ElbowStopHz { get; init; } = 35.75f;

    /// <summary>
    /// 肘の関節（上腕と前腕をつなぐ位置の拘束）のバネの固さ [Hz]。物理エンジンの既定 8.94 Hz では、拳を打ち出す間に前腕が上腕から
    /// 最大 32 mm（セル 2 つ強）離れ、肘に隙間が見えた（右ストレート、punchlab。20 Hz で 9.5 mm・30 Hz で 4.8 mm・45 Hz で 2.5 mm）。
    /// 実際の肘は靭帯と関節包で隙間が開かないので、隙間がセルの半分（7 mm）を下回る固さにする。240 Hz の歩みで安定に解ける範囲
    /// （つま先の止め <see cref="ToeStopHz"/> 35.75 Hz・肘の止め <see cref="ElbowStopHz"/> と同じ桁）。
    /// </summary>
    public float ElbowHingeHz { get; init; } = 45f;

    /// <summary>靭帯が切れるまでの伸び [rad]（過伸展がこれだけ増えたら切れる。約 20°。靭帯の破断のひずみは降伏のおよそ 2 倍）。</summary>
    public float LigamentRuptureStretch { get; init; } = 0.35f;

    /// <summary>靭帯が切れた肘が、元の下限からさらに反れる角度 [rad]（骨と残りの組織が止める所。約 50°）。</summary>
    public float LigamentTornTravel { get; init; } = 0.87f;

    // ── 掴み・投げ
    public float GrabRadius { get; init; } = 0.113f;
    public float GrabBreakForce { get; init; } = 2600f;
    public float GrabbedDriveScale { get; init; } = 0.55f;

    // ── 状態
    public float FallUprightness { get; init; } = 0.4f;
    public float FallGrace { get; init; } = 0.252f;
    public float CollapseDuration { get; init; } = 0.503f;
    public float DownDriveScale { get; init; } = 0.05f;

    /// <summary>
    /// 床から立つ高さまで腰を上げきるのにかかる時間 [s]（意識がはっきりしているとき）。倒れている間も常に立とうとし、
    /// 腰を上げられる高さ（<see cref="Fighter.StandReach"/>）までこの速さで上げる。意識が戻る速さより遅くは上がらない。
    /// </summary>
    public float RiseTime { get; init; } = 4.196f;

    /// <summary>倒れている間、骨盤が立つ高さのこの割合より低ければ床にいる（Down）、高ければ起き上がる途中（GettingUp）。審判のダウンの判定には使わない（<see cref="DownFaintTime"/>）。</summary>
    public float DownHeightRatio { get; init; } = 0.55f;

    /// <summary>骨盤が立つ高さのこの割合まで戻り、上体が <see cref="RecoverUprightness"/> まで起きたら、立ち直った。</summary>
    public float RecoverHeightRatio { get; init; } = 0.85f;

    /// <summary>立ち直りの判定に要る、立った姿勢を保つ時間 [s]（起き上がる途中で一瞬届いただけでは立ち直らない）。</summary>
    public float RecoverHold { get; init; } = 0.503f;

    public float RecoverUprightness { get; init; } = 0.78f;
    public float NoGroundFallTime { get; init; } = 0.671f;

    // ── ボクセルの体: 打撃で削れ、動きに響き、骨が断たれたら落ち、臓器が壊れたら死ぬ（Voxels/BoxerBody）
    /// <summary>ボクセル 1 個の大きさの目安 [m]（部位の箱をこれに近い大きさで割り切る）。</summary>
    public float VoxelSize { get; init; } = 0.01408f;
    /// <summary>
    /// ダメージ 1 あたりに削る量（肉 1 ボクセル = 1。骨は 8、臓器は 1.2〜1.4）。試合の打撃は運動エネルギーから削る量を決める（<see cref="CarvePerJoule"/>）ので、
    /// これは削る量を渡さずにダメージだけで削るとき（テストや道具）に使う。
    /// </summary>
    public float CarvePerDamage { get; init; } = 4.3f;
    /// <summary>削れたボクセルが飛び散る速さ [m/s]（人形の 0.8 m/s を相似で人の大きさへ）。</summary>
    public float ChipSpeed { get; init; } = 1.343f;
    /// <summary>飛び散った破片にかかる重力の倍率（1 = 実際の重力。小さいほどふわりと長く舞う）。</summary>
    public float DebrisGravityScale { get; init; } = 0.35f;
    /// <summary>
    /// 運動エネルギー 1 J あたりに削る量（肉 1 ボクセル = 1）。削る量は拳の運動エネルギー（½ × 乗った質量 × 速さ²）に比例する:
    /// 体重の乗った速い 1 発ほど急に深く掘れ、手打ちはほとんど掘れない（非線形）。硬い頭蓋（骨）が、その奥の脳までの壁になる。
    /// 頭を人の比率（<see cref="HeadScale"/> 0.8）に小さくしたとき、正面への重い 1 発で脳が削れる割合が変わらないよう 45 → 12 にした（人形の単位。人の大きさで 0.426）。
    /// </summary>
    public float CarvePerJoule { get; init; } = 0.426f;

    /// <summary>1 発で削る量の上限（約 280 J ぶん。速さの測り違いのような外れの 1 発が、腕を丸ごと削らないように）。</summary>
    public float MaxCarve { get; init; } = 120f;

    /// <summary>
    /// 床に落ちた衝撃で潰す量の、拳の打撃（<see cref="CarvePerJoule"/>）に対する割合。床は平らで広い（拳より圧力が小さい）ので、
    /// 同じエネルギーでも潰れは浅い（キャンバスと詰め物の分も）。手で決めた値（docs/GRAPPLING.md §9）。
    /// </summary>
    public float CanvasCarveShare { get; init; } = 0.3f;

    /// <summary>床に落ちた衝撃で潰す量がこれ（ボクセル）より小さければ何もしない（座り込む・手を着く程度の当たり）。</summary>
    public float CanvasMinCarve { get; init; } = 0.5f;

    /// <summary>
    /// 素の武器（木・鉄）の打撃が、同じ運動エネルギーの拳（グローブ）の何倍深く潰して掘るか（上限 <see cref="MaxCarve"/> にも掛ける）。
    /// グローブの詰め物は当たる面を広げて衝撃を和らげるが、硬い武器は狭い面へ一気に掛ける。
    /// </summary>
    public float WeaponCarveScale { get; init; } = 3f;

    /// <summary>
    /// 打撃で相手へ運ぶ体の運動量のうち、当たった部位に残る（当たった点に掛かる）割合の上限。残りは全身を押す。
    /// 部位に残る分は頭を首の上でのけぞらせ、急所（あご・こめかみ）のように回転の中心から外れた所では頭を強くねじる。
    /// </summary>
    public float StrikeShare { get; init; } = 0.55f;

    /// <summary>
    /// 部位に残る割合が <see cref="StrikeShare"/> に達する拳の速さ [m/s]。これより遅い拳では速さに比例して減る:
    /// 部位に残る運動量は m v² に比例し（非線形）、速く振り抜いた 1 発ほど頭が持っていかれる。
    /// </summary>
    public float FullStrikeSpeed { get; init; } = 4.196f;

    /// <summary>打撃で当たった部位に残す運動量が、その部位に与える速さの上限 [m/s]（拳の速さの測り違いで部位を飛ばさない）。</summary>
    public float MaxStrikeSpeed { get; init; } = 10.07f;

    /// <summary>
    /// 意識を全部飛ばす、頭の回転の速さ [rad/s]（<see cref="Fighter.TwistHead"/>: 脳しんとう = (頭が得た回転の速さ ÷ この値)²）。拳・蹴り・肘・武器・床に頭を打った衝撃で 1 つの値
    /// （PhysicsVersion 59 まで拳は ω² × 0.0035、床は ω² × 0.000035 と 100 倍違う別の係数だった。docs/plans/EXCHANGE_REGRESSION_2026-11-19.md）。
    /// 値は測って決めた（人形の単位。人の大きさでは × 0.596 で 40 → 23.83。champion:swarmer・counter:slugger、種 4〜7 × 40 秒）: 30 / 35 / 40 / 50 rad/s で、頭への 1 発の脳しんとうの p90 が 1.09 / 0.72 / 0.99 / 1.26、
    /// 最大 5.3 / 3.9 / 5.4 / 1.9、ダウン 2.0 / 2.0 / 1.75 / 2.25 回/試合（試合ごとの揺れが大きく、単調には並ばない）。前の拳の係数 0.0035（ConcussionSpin にすると √(1 ÷ 0.0035) = 16.9 rad/s）では
    /// p90 2.78・最大 10.1。40 で普通の 1 発は 1 前後まで、あご・こめかみを速く振り抜く 1 発（頭が 40 rad/s 以上回る、拳の当たりの 1 % 未満）だけが 1 発で意識を飛ばす。
    /// 床に頭を打つ衝撃（頭の回転 中央 13・p90 47 rad/s）も同じ値で効く（前の床の係数の 18 倍）。
    /// 無限大にすると脳しんとうは起きない（調べもの・ドリル用）。
    /// </summary>
    public float ConcussionSpin { get; init; } = 23.83f;

    /// <summary>
    /// 脳しんとうから意識が戻る速さ [意識/秒]（一定）。意識を 0.95 飛ばされると、膝立ちまで戻るのに約 5 秒、立ち上がれるまで約 10 秒、
    /// はっきりするまで約 17 秒。軽く効いた程度（0.3）なら 7 秒ほどで抜ける。
    /// </summary>
    public float ConcussionRecoveryRate { get; init; } = 0.0417f;

    /// <summary>意識がこの値を下回ると体の張りが抜け始める（腰が落ち、ガードが下がる）。</summary>
    public float FullToneConsciousness { get; init; } = 0.75f;

    /// <summary>意識が途切れる手前（<see cref="KnockoutConsciousness"/> のすぐ上）で残る体の張り。腰は大きく落ちるが立っていられる。</summary>
    public float FaintTone { get; init; } = 0.5f;

    /// <summary>意識が途切れる手前（張りが <see cref="FaintTone"/>）で腰が落ちる量（立つ高さに対する割合）。</summary>
    public float FaintSag { get; init; } = 0.25f;

    /// <summary>
    /// 意識が途切れる手前で、拳を目標へ運ぶ力に残る割合（体の張りに加えて）。腕を上げていられず、ガードが重さで下がる
    /// （拳の目標はずらさない: VR の自分の手から拳がずれない。力が弱いだけなので、遅れて付いていく）。
    /// </summary>
    public float FaintArmStrength { get; init; } = 0.3f;

    /// <summary>
    /// 意識がこの値を下回ると、張りは <see cref="FaintTone"/> から <see cref="LimpConsciousness"/> で 0 へ抜けていき、
    /// 腰を上げられる高さも膝立ち・座り込み・床へと下がる（段階でなく連続）。
    /// </summary>
    public float KnockoutConsciousness { get; init; } = 0.3f;

    /// <summary>意識がこの値を下回ると張りは 0（全身の力が抜けて床に伸びる）。</summary>
    public float LimpConsciousness { get; init; } = 0.12f;

    /// <summary>
    /// 倒れている間の「起き上がろうとする試み」の強さ: 試みの山で、体の張りに足される量（張りが満ちていない分 1 − Tone に対する割合）。
    /// 意識が少しでもあれば体は動く: 張りが 0 の意識の浅いところ（<see cref="LimpConsciousness"/> 未満）でも、試みの間だけ体が張って
    /// 腕・頭・腰がわずかに動き、すぐ抜ける。意識が深いほど（<see cref="KnockoutConsciousness"/> へ近いほど）試みは強く・頻繁になる。
    /// </summary>
    public float StirTone { get; init; } = 0.45f;

    /// <summary>試みの山で、腰を上げようとする高さ（<see cref="Fighter.Rise"/>）に足す上限（立つ高さに対する割合）。保てない高さまで持ち上げようとして崩れる。</summary>
    public float StirReach { get; init; } = 0.3f;

    /// <summary>意識がわずかなときの試みの間隔 [s]。<see cref="StillSpeed"/> の静止で K.O. にする時間（<c>BoxingGame.StillKoSeconds</c>）より短くする（意識があれば必ずその間に動く）。</summary>
    public float StirPeriodSlow { get; init; } = 4.364f;

    /// <summary>意識が <see cref="KnockoutConsciousness"/> 以上のときの試みの間隔 [s]。</summary>
    public float StirPeriodFast { get; init; } = 2.182f;

    /// <summary>1 回の試みが力を込めている長さの、間隔に対する割合（残りは力を抜いて休む）。</summary>
    public float StirDuty { get; init; } = 0.55f;

    /// <summary>試みの間隔を毎回ずらす幅（±割合）。決まった拍子で動くと人に見えない。ずれは体ごとの決まった数列（種なしの乱数は使わない）。</summary>
    public float StirJitter { get; init; } = 0.3f;

    /// <summary>
    /// 体が「動いていない」とみなす速さ [m/s]: 全部位の重心の速さの最大がこれ未満。1 秒に 2.5 cm（目で動きと分からない程度）。
    /// 審判が見て判断する量（ルールの値）。
    /// </summary>
    public float StillSpeed { get; init; } = 0.0252f;

    /// <summary>倒れている間に手を着く所: 肩の真下からの前への距離（腕の長さに対する割合）。膝の前に手を着いて体を支える位置。</summary>
    public float PostReach { get; init; } = 0.35f;

    /// <summary>起き上がろうとする試みの山で、手を床より下へ押し込む量（腕の長さに対する割合 × 試みの強さ）。床を押して体を持ち上げる。</summary>
    public float PostPress { get; init; } = 0.15f;

    // ── 呼吸と循環（2026-11-18。全身で 1 つの蓄えから、部位ごとの酸素の蓄え・血管・心臓・肺へ。Fighter.Stamina.cs の先頭に全体の説明）──

    /// <summary>
    /// 筋肉 1 kg あたりの酸素の蓄え [J/kg]（筋肉の中の酸素と、それで作り直せるエネルギーを仕事に直した量。部位の蓄え = これ × 部位の質量 × 残った筋肉）。
    /// 拳を目標へ押す力がした仕事・保持の分（<see cref="IsometricCost"/>）・歩いて体を運ぶ分（<see cref="WalkCost"/>）が、その仕事をした部位の蓄えから出ていく。
    /// 2113 J/kg（人形の単位で 750 J/kg。基準体格の腕 3.3 kg で 6970 J）: 測った仕事の分け方（OxygenSplitProbe、人形の単位: 弱強の全力の連打で腕 1 本 14〜22 W、
    /// 脚 2 W・胴 1.5 W、ボット同士の 1 分で腕 1 本 1〜3 W。人の大きさでは × 16.8）と、ユーザーの目標（全力の連打が 10 秒、力が落ちながらさらに 10 秒。人の大きさで 17 秒ずつ）から。
    /// StaminaRushTests（種 3、人形の単位）で拳の速さ 0〜10 秒 2.81・10〜20 秒 2.18・20〜30 秒 1.70 m/s。360 J/kg・600 J/kg（人形の単位）では始めの 10 秒の中で腕が尽きた（2.18・2.69 m/s）。
    /// 脚と胴は同じ 1 kg あたりで大きい蓄えになるが、脚の関節のサーボの仕事をまだ数えていない（docs/DEBT.md）ので、ほとんど減らない。
    /// </summary>
    public float OxygenStorePerKg { get; init; } = 2113f;

    /// <summary>
    /// 筋肉 1 kg あたりの血管の太さ [W/kg]: 休んでいる部位へ中枢から届けられる酸素の速さの上限（部位の血管の流れ = これ × 部位の質量 × 残った筋肉 × 持久力）。
    /// 75.5 W/kg（人形の単位で 45 W/kg。基準体格の腕で 250 W、働いている腕へは <see cref="VesselWorkingShare"/> 倍の 112 W）: ラウンドの間の 17 秒で空の両腕が 6 割ほど戻る
    /// （StaminaTempoTests、人形の単位: 10 秒 59 %・16 秒 95 %）。片手の強い 1 発を 0.5 秒ごと（腕 1 本 146 W）なら 50 秒で 7 割残る。
    /// </summary>
    public float VesselPowerPerKg { get; init; } = 75.53f;

    /// <summary>
    /// 心臓が全部位へ送れる酸素の速さの合計 [W]（基準体格、心臓が無傷のとき。筋肉の量の比と心臓の働きに比例）。504 W（人形の単位で 30 W）: 空の両腕が休んで戻る速さ
    /// （250 W × 2）とちょうど同じ。全力の連打（両腕 112 W × 2 + 脚・胴 67 W）より上なので、無傷の心臓は戦っている間はボトルネックにならず、
    /// 心臓が傷むと腕の戻りがすぐ心臓で止まる。
    /// </summary>
    public float HeartPower { get; init; } = 503.5f;

    /// <summary>
    /// 肺が血へ酸素を取り込める速さ [W]（基準体格、肺が無傷のとき。筋肉の量の比・肺の働き・頸の脊髄に比例）。537 W（人形の単位で 32 W）: 無傷なら心臓より少し上
    /// （ふつうは肺がボトルネックにならない）。肺が傷む・息が詰まると血の酸素が減り、心臓が送れる分が肺の速さまで落ちる。
    /// </summary>
    public float LungPower { get; init; } = 537.1f;

    /// <summary>
    /// 血の酸素の蓄え [J]（基準体格）: 肺と部位のあいだのため。845 J（人形の単位で 30 J）: 心臓が全力で送って 1.7 秒ほど。小さいので、肺が追いつかなければすぐ空になり、
    /// 打たれて息が詰まる（<see cref="WindPerBruise"/>）と、ここが減る。
    /// </summary>
    public float BloodOxygenStore { get; init; } = 845.1f;

    /// <summary>
    /// 実際にした仕事の仕事率 P をならす時間 [s]（回復を決める P の指数平均）。1.68 秒（人形の単位で 1 秒）: 数発の連打の分が残り、手を止めると 2〜3 秒で抜ける。
    /// 以前の 2 秒は、空になってから構え直しても回復が始まるまで数秒かかった（2026-11-15 の測定）。
    /// </summary>
    public float StaminaRecoveryWindow { get; init; } = 1.678f;

    /// <summary>
    /// 働いている筋肉へ流れる割合: 縮んだ筋肉は自分の血管を押しつぶすので、部位の直近の仕事率が kV（V は休んだときの流れ）に届くと、流れは kV まで下がる
    /// （その間は V から直線に下がる）。kV より軽い仕事では蓄えが減らず、重い仕事ではその差で減る（運動生理の臨界パワーを部位ごとにしたもの）。
    /// 0.45（前の全身のモデルの臨界パワーの割合と同じ。基準体格の腕で 112 W）。
    /// </summary>
    public float VesselWorkingShare { get; init; } = 0.45f;

    /// <summary>
    /// 持久の蓄え [J]（基準体格）: 試合でした筋肉の仕事の合計がこれだけになると、心肺の供給と臨界パワーが 1/e に落ちる（<see cref="Fighter.Endurance"/>。
    /// 筋肉の糖が減っていく近似。ラウンドの間では戻らない）。1 ラウンド目は全力で動け、3 ラウンド目には疲れやすくなる長さに合わせる（StaminaFeelProbe）。
    /// </summary>
    public float StaminaEndurance { get; init; } = 42250f;

    /// <summary>低残量で蓄えを放出する時間 [s]。0.34 秒（打ち出し程度）へ連続に制限する。余裕があれば瞬発力として蓄え全量を使える。</summary>
    public float StaminaReleaseWindow { get; init; } = 0.336f;

    /// <summary>1 歩の仕事予算のうち肩・肘・手首サーボに先に取り置く割合。残りは両拳の PD・移送が共用する。</summary>
    public float StaminaServoShare { get; init; } = 0.5f;

    /// <summary>力を出し続けるだけで使うエネルギー [J/(N·s)]: 筋肉は動かなくても力を保つのにエネルギーを使う（構えた腕を上げているだけで疲れる）。</summary>
    public float IsometricCost { get; init; } = 0.00671f;

    /// <summary>体を水平に運ぶのに使うエネルギー [J/(kg·m)]（足を運ぶ仕事の近似）。</summary>
    public float WalkCost { get; init; } = 0.15f; // [J/(kg·m)] = [m/s²] なので相似で変わらない

    /// <summary>
    /// 駆動をスタミナの仕事予算で制限するか。偽は VR 調整と比較実験用の明示的な例外（出入りは計測する）。
    /// 固定の最低筋力 TiredStrength は廃止。立位を支える力は意識で決め、移動指令の水平仕事だけを制限する。
    /// </summary>
    public bool StaminaFatigue { get; init; } = true;

    /// <summary>
    /// スタミナがこれより多い間は筋の駆動割合を落とさない。下回ると蓄えの割合から心肺の供給割合へなめらかに近づく。
    /// 仕事予算そのものは残量によらず常に適用する。0.45（2026-11-17、0.6 から）: 全力のラッシュが蓄えの 55 % を使う 10 秒は落ちず、残りの 10 秒で落ちていく。
    /// </summary>
    public float TiredKnee { get; init; } = 0.45f;

    /// <summary>
    /// プレイヤー（赤コーナー）の体にスタミナの疲れを効かせるか。偽ならプレイヤーの体だけ <see cref="StaminaFatigue"/> = false で作る
    /// （スタミナは数えるが、腕の力は落ちない）。VR は手触りの調整が終わるまで偽（2026-11-12 のユーザーの決定。CLAUDE.md §8 の例外）。
    /// 調整値に入れておくと、記録（TuningJson）に残って再生でも同じになる。
    /// </summary>
    public bool PlayerFatigue { get; init; } = true;

    // ── 臓器への打撃（皮膚の上からの打撲 = 一時的、削れ = 恒久的）──

    /// <summary>
    /// 打撃の運動エネルギー 1 J が、臓器の表面に直接届いたときに増やす打撲（0..1）。拳の重い 1 発（約 280 J）が肝臓の真上に入ると
    /// 打撲はほぼいっぱいになる。
    /// </summary>
    public float BruisePerJoule { get; init; } = 0.004615f;

    /// <summary>衝撃が組織を伝わって減る距離 [m]（この深さごとに 1/e）。胸の厚み約 25 cm の体で、真上の 1 発は効き、反対側には届かない。</summary>
    public float BruiseFalloff { get; init; } = 0.0704f;

    /// <summary>打撲が引く速さ [/s]（いっぱいの打撲は 26 秒ほどで引く）。</summary>
    public float BruiseRecoveryRate { get; init; } = 0.0387f;

    /// <summary>胴の臓器がこの割合だけ削られるごとに、その臓器の働きが半分になる（脳の <see cref="BrainHalvingLoss"/> と同じ形）。</summary>
    public float OrganHalvingLoss { get; init; } = 0.2f;

    /// <summary>
    /// 腹（肝臓と腸）の働きが 0 のとき、体の張り（<see cref="Fighter.Tone"/>）に残る割合。肝臓を打たれると意識はあっても膝から力が抜ける
    /// （リバーブロー）。働きに比例して戻る。
    /// </summary>
    public float CoreToneFloor { get; init; } = 0.25f;

    /// <summary>
    /// 胴の臓器の打撲が 1 増えるごとに、その場で失うスタミナの蓄え [J]（息が詰まる。基準体格の値で、筋肉の量の比を掛ける）。338 J（人形の単位で 12 J）: 容量 120 J（人形の単位）のときの
    /// 割合 0.1 と同じ量（2026-11-17 に割合からエネルギーへ。容量を増やしたとき、同じ打撃で失うエネルギーが容量に比例して増えないように）。
    /// </summary>
    public float WindPerBruise { get; init; } = 338f;

    /// <summary>
    /// 中腰でまだ立っている体の意識が <see cref="KnockoutConsciousness"/> を下回ったまま、これだけ続いたらダウン（審判の判断の値:
    /// 2026-11-13、ユーザーの指定で 4 秒。人の大きさの時間で 6.7 秒）。それまでは相手が攻め続けてよい。足の裏以外がマットに着いた・倒れたら、待たずにすぐダウン。
    /// </summary>
    public float DownFaintTime { get; init; } = 6.713f;

    /// <summary>
    /// マットに「着いた」とみなす、体の部位の下端とマットの間 [m]（3.4 cm: 体のボクセル 2 つ半ほど）。
    /// 腰・上体・腕・拳がこれより低ければダウン（尻もち・倒れ込み・手を突いた）。
    /// </summary>
    public float CanvasTouch { get; init; } = 0.0338f;

    /// <summary>
    /// 脳がこの割合だけ削られるごとに、脳の働き（意識の上限）が半分になる（<see cref="Voxels.BoxerBody.BrainFunction"/>）。
    /// 10%: 10% 削れで 0.5（ふらつく）、20% で 0.25（膝をつく）、30% で 0.125（立てない）、40% で 0.06（起き上がろうともがくだけ）。
    /// </summary>
    public float BrainHalvingLoss { get; init; } = 0.1f;

    /// <summary>臓器がこの割合より少なくなったら死ぬ。</summary>
    public float OrganDeathFraction { get; init; } = 0.5f;
    /// <summary>
    /// 肉が無くなったときに残る力の割合（腕・脚・胴）。肉の残り f に対して力は MinMuscleStrength + (1 − MinMuscleStrength) × f。
    /// </summary>
    public float MinMuscleStrength { get; init; } = 0.15f;
    /// <summary>左右の脚の力の弱い方がこれを下回ると、立っていられずに倒れて起き上がれない。</summary>
    public float StandStrength { get; init; } = 0.35f;

    // ── 見た目: ローポリの体と打撃によるへこみ（BoxerModel / BoxerSkin）
    /// <summary>ローポリ形状を形を保ったまま何回細分化するか（1 回で三角形 4 倍）。変形の細かさが決まる。</summary>
    public int ModelSubdivisions { get; init; } = 1;
    /// <summary>ダメージ 1 あたりのへこみの深さ [m]。</summary>
    public float DentDepthPerDamage { get; init; } = 0.00141f;
    /// <summary>1 発でへこむ深さの上限 [m]。</summary>
    public float MaxDentDepthPerHit { get; init; } = 0.0225f;
    /// <summary>拳の表面からこの距離 [m] までのノードがへこみに引きずられる。</summary>
    public float DentSpread { get; init; } = 0.0451f;
    /// <summary>ノードが元の位置から動ける上限（部位の箱の最小の半径に対する割合）。ここで色が真っ赤になる。</summary>
    public float MaxDentRatio { get; init; } = 0.4f;

    /// <summary>関節の力上限の倍率。トルク ∝ 質量 × 長さ なので MassScale × Scale。</summary>
    [JsonIgnore]
    public float TorqueScale => MassScale * Scale;

    [JsonIgnore]
    public HumanoidRagdoll.HalfExtents HalfExtents
    {
        get
        {
            var d = HumanoidRagdoll.HalfExtents.Default;
            var h = new HumanoidRagdoll.HalfExtents(
                d.Pelvis * Scale, d.Chest * Scale, d.Head * (Scale * HeadScale),
                d.UpperArm * Scale * new Vector3(ArmLengthScale, 1f, 1f), d.Forearm * Scale * new Vector3(ArmLengthScale, 1f, 1f),
                d.Thigh * Scale, d.Shin * Scale, d.Foot * Scale * new Vector3(1f, 1f, 1f - FootGeometry.ToeShare)); // 足はかかと側（つま先は別の部位）
            var build = Build.Validate();
            return new HumanoidRagdoll.HalfExtents(h.Pelvis * build.ScaleOf(BodyPartKind.Pelvis), h.Chest * build.ScaleOf(BodyPartKind.Chest),
                h.Head * build.ScaleOf(BodyPartKind.Head), h.UpperArm * build.ScaleOf(BodyPartKind.UpperArm), h.Forearm * build.ScaleOf(BodyPartKind.Forearm),
                h.Thigh * build.ScaleOf(BodyPartKind.Thigh), h.Shin * build.ScaleOf(BodyPartKind.Shin), h.Foot * build.ScaleOf(BodyPartKind.Foot));
        }
    }

    [JsonIgnore]
    public HumanoidRagdoll.MassDistribution Masses
    {
        get
        {
            var m = HumanoidRagdoll.MassDistribution.Default;
            // 質量は部位の体積（寸法の積）に比例させる: 体格を変えても密度は同じ（基準体格では 1 倍で、従来の値のまま）。
            var build = Build.Validate();
            float V(BodyPartKind kind)
            {
                var s = build.ScaleOf(kind);
                return s.X * s.Y * s.Z;
            }

            return m with
            {
                Pelvis = m.Pelvis * MassScale * V(BodyPartKind.Pelvis), Chest = m.Chest * MassScale * V(BodyPartKind.Chest), Head = m.Head * MassScale * V(BodyPartKind.Head),
                UpperArm = m.UpperArm * MassScale * V(BodyPartKind.UpperArm), Forearm = m.Forearm * MassScale * V(BodyPartKind.Forearm), Thigh = m.Thigh * MassScale * V(BodyPartKind.Thigh),
                Shin = m.Shin * MassScale * V(BodyPartKind.Shin), Foot = m.Foot * MassScale * V(BodyPartKind.Foot) * (1f - FootGeometry.ToeShare), // かかと側（残りはつま先）
            };
        }
    }


    public static FighterTuning Default { get; } = new();

    /// <summary>その場での回旋を使う、分割胴のコンパクトなボクシング設定。</summary>
    public static FighterTuning CompactPunch { get; } = Default with
    {
        SplitTorso = true,
        CompactTorsoWave = true,
        ElbowMuscleMaxSpeed = 200f,
        ArmServoMaxSpeed = 50f,
        WaveHandSeconds = 0.035f,
    };

    /// <summary>踏み込みの並進と体幹の回旋を使う、分割胴のボクシング設定。</summary>
    public static FighterTuning DynamicPunch { get; } = Default with
    {
        SplitTorso = true,
        MaxTorsoTwistRadians = 65f * MathF.PI / 180f,
        TrunkRotatorTorque = 240f,
        ElbowMuscleMaxSpeed = 200f,
        WaveHandSeconds = 0.025f,
        WaveFootSeconds = 0.050f,
        WaveFootTravelShare = 0.24f,
        Footwork = Default.Footwork with { StepPushTime = 0.030f },
    };

    /// <summary>拳の目標距離から打ち分ける分割胴。腕のサーボ速度は両フォームで共通にする。</summary>
    public static FighterTuning AdaptivePunch { get; } = CompactPunch with
    {
        AdaptivePunchForm = true,
        ArmServoMaxSpeed = DynamicPunch.ArmServoMaxSpeed,
    };

    /// <summary>関節と接触を介した運動量伝達の試験設定。</summary>
    public static FighterTuning MomentumPunch { get; } = AdaptivePunch with
    {
        JointDrive = true,
        UseCaptureForVerticalSupport = true,
        JointStanceHipShare = 0.50f,
        JointStanceAnkleGain = 1f,
        LegServoHz = 22f,
        Footwork = AdaptivePunch.Footwork with { HeelRaiseDegrees = 0f, PivotHeelRaiseDegrees = 0f, CapturePlacementShare = 1f },
    };

    /// <summary>関節駆動のパンチと従来の立位補助を組み合わせた試合設定。</summary>
    public static FighterTuning HybridPunch { get; } = MomentumPunch with
    {
        JointSupportAssist = 0.5f,
        JointLegacyGloveImpact = true,
    };

    /// <summary>
    /// 面白さの測定で選んだ試合用の体（2026-11-22）: ダイナミック版（<see cref="DynamicPunch"/>）に、体幹の回旋トルク 360 N·m・意識の戻りの速さ 2 倍・
    /// 足で受け止められる捕捉点のずれの許容 2 倍（<see cref="SupportRecovery"/> 1.0・<see cref="SupportVerticalRecovery"/> 3）を重ねた。
    /// 標準ボット 3 組（champion:swarmer・slugger:outboxer・counter:jabber）× 種 1〜16 の面白さの総合点は 標準の体 154.6 → 182.9、別の種（21〜24）の
    /// 9 組平均で 154.2 → 193.4（`excitementleague --tune Preset=MatchPunch`。docs/arena/excitement_generations_2026-11-22.md）。
    /// 足の受け止め許容を戻すと打たれずに倒れる回数が 1.5 → 4.2 回/試合に増える（`SelfFallTests`）。
    /// <b>標準にはしていない</b>: 分割胴の体は質量の帳尻・酸素の部位・ボクセルの臓器位置・同梱の体モデルがまだ合っていない
    /// （標準にすると `ShippedModel_IfAny_FitsTheDefaultSettings`・`BoneAndOrganGoals_…`・`TheGuardPose_…`・`RestedFighterShowsCalmVitalsAndTheHeartBeats` ほか 7 件が落ちた。docs/DEBT.md「分割胴の体の組み込み」）。
    /// </summary>
    public static FighterTuning MatchPunch { get; } = DynamicPunch with
    {
        TrunkRotatorTorque = 360f,
        ConcussionRecoveryRate = 0.0834f,
        SupportRecovery = 1.0f,
        SupportVerticalRecovery = 3f,
    };

    internal static Vector3 Up => Vector3.UnitY;
}
