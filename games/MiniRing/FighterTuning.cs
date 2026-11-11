using System.Numerics;
using System.Text.Json.Serialization;
using Aigis.Physics;

namespace MiniRing;

/// <summary>
/// ファイター（約 60cm のアクティブラグドール）の調整値。
/// <para>
/// 体を保つ力は「骨盤ホバー PD + 直立 PD + 水平速度 P」（Unity 版 Triage の ActiveRagdollMotor 方式）。
/// ゲインは加速度単位（1/s², 1/s）で持ち、実行時に質量・慣性を掛けて力積にする — 体格を変えても挙動が保たれる。
/// 関節は Bepu の AngularServo / TwistServo（Hz・減衰比・力上限＝バネ・ダンパ）で駆動する。
/// Bepu の SpringSettings は換算質量に対して定義される（Aigis anti-pattern A12）ので、Unity の N/m 値は移植しない。
/// </para>
/// </summary>
public sealed record FighterTuning
{
    /// <summary>Aigis の既定体格（約 1.7m）に対する縮尺。0.355 ≒ 60cm。</summary>
    public float Scale { get; init; } = 0.355f;

    /// <summary>既定質量（約 67.6kg）への倍率。0.1 ≒ 6.8kg。</summary>
    public float MassScale { get; init; } = 0.1f;

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
    public float GloveRadius { get; init; } = 0.024f;

    // ── 体幹（加速度単位）
    public float StandHeightRatio { get; init; } = 0.97f;
    public float HoverKp { get; init; } = 100f;
    public float HoverKd { get; init; } = 14f;
    public float MaxHoverAccel { get; init; } = 2.2f * 9.81f;
    public float UprightKp { get; init; } = 600f;
    public float UprightKd { get; init; } = 40f;
    public float MaxUprightAccel { get; init; } = 1500f;
    public float TurnSpeedRadians { get; init; } = 2f * MathF.PI;

    // ── 移動・脚
    public float WalkSpeed { get; init; } = 0.55f;
    public float MoveGain { get; init; } = 10f;
    public float MaxMoveAccel { get; init; } = 6f;
    public float StrideLength { get; init; } = 0.16f;
    public float NeutralHipRadians { get; init; } = -8f * MathF.PI / 180f;
    public float NeutralKneeRadians { get; init; } = 14f * MathF.PI / 180f;
    public float GaitHipSwingRadians { get; init; } = 30f * MathF.PI / 180f;
    public float GaitKneeLiftRadians { get; init; } = 40f * MathF.PI / 180f;
    /// <summary>脚のサーボの固有振動数 [Hz]。足の運びで、踏み出した足を 0.1〜0.2 秒で置き場所まで運び切れる速さ（以前の歩行は 12）。</summary>
    public float LegServoHz { get; init; } = 20f;

    /// <summary>股・膝・足首のサーボが出せる最大トルク（TorqueScale 倍する前）。足を置き場所へ運び切れる強さ（以前は 120 / 100 / 100）。</summary>
    public float HipMaxTorque { get; init; } = 360f;
    public float KneeMaxTorque { get; init; } = 300f;
    public float AnkleMaxTorque { get; init; } = 900f;
    /// <summary>首（頭と胴をつなぐバネ）の固有振動数 [Hz]。低いほど打たれた頭が大きく揺れる。</summary>
    public float NeckServoHz { get; init; } = 9f;

    /// <summary>足の運び（足の置き場所のモデル。<see cref="MiniRing.Footwork"/>）。</summary>
    public FootworkTuning Footwork { get; init; } = new();

    /// <summary>つま先の蝶番のサーボの強さ（TorqueScale 倍する前）。</summary>
    public float ToeMaxTorque { get; init; } = 20f;

    /// <summary>首のバネの減衰比。殴られて持っていかれた頭が、揺れ返さずに戻る（打たれていないときもぐらつかない）。</summary>
    public float NeckDampingRatio { get; init; } = 0.7f;

    /// <summary>首のバネが出せる最大トルク（TorqueScale 倍する前）。</summary>
    public float NeckMaxTorque { get; init; } = 60f;

    // ── 腕（サーボ + 拳への PD 力）
    public float ArmServoHz { get; init; } = 15f;
    public float ArmServoMaxSpeed { get; init; } = 40f;
    public float HandKp { get; init; } = 1100f; // 目標 → 拳のバネ [1/s²]（約 5.3Hz）。固いほどきびきび追う
    public float HandKd { get; init; } = 53f; // 減衰比 ≒ 0.8（行き過ぎずに止まる）
    public float MaxHandForce { get; init; } = 35f;

    /// <summary>手首の筋肉（武器を握ったとき、武器を向きの目標へ回すサーボ）の固さ [Hz]。</summary>
    public float WristServoHz { get; init; } = 15f;

    /// <summary>手首が武器を回す最大トルク（TorqueScale 倍する前。肩の 40 の 1/4）。重い武器ほど向きを変えるのが遅れる。</summary>
    public float WristMaxTorque { get; init; } = 10f;

    /// <summary>
    /// 両手で握っていた武器を片手で持つことになったときの、手首の角度のバネ（固さとトルク）の割合。両手の握りは、2 本の手首に加えて、
    /// 2 つの拳が握りの別々の所を押し引きする偶力で武器を支える。片手ではそれが手首 1 本になるので、1/3 にする（ユーザーの指定）。
    /// </summary>
    public float OneHandWristShare { get; init; } = 1f / 3f;

    /// <summary>両手持ちで、添える手が握りをつかめる、拳と握る所の距離 [m]（これより近づいたら玉継手でつなぐ）。</summary>
    public float SupportGripReach { get; init; } = 0.02f;
    public float HandReactionShare { get; init; } = 0.5f;
    public float MaxTargetSpeed { get; init; } = 6f; // 目標の速度の上限 [m/s]（動きを 2 倍にしたので、速い腕の振りも追えるように）
    public float TargetVelocitySmoothing { get; init; } = 25f;

    // ── ボクシングの力学（PunchMechanics）
    /// <summary>体の質量を借りられたときに腕の力へ上乗せできる最大の割合（脚・腰の力）。</summary>
    public float BodyDrive { get; init; } = 1.5f;
    /// <summary>腕へ上乗せした体の力のうち、胴を拳の方向へ押し出す（体重移動として見える）割合。</summary>
    public float WeightShiftShare { get; init; } = 0.3f;
    /// <summary>両手の位置から決める胴のひねりの最大角。腰はその一部だけ回る。</summary>
    public float MaxTorsoTwistRadians { get; init; } = 35f * MathF.PI / 180f;
    public float HipTwistShare { get; init; } = 0.45f;
    /// <summary>頭のずれ（前傾・スリップ）で胴を傾けられる最大角。</summary>
    public float MaxLeanRadians { get; init; } = 30f * MathF.PI / 180f;

    /// <summary>後ろへ反らす（スウェー）ときに腰が受け持つ割合（胸は全部）。上体ごと後ろへ倒れ、首だけで反らない。前へ倒すときは 0.25。</summary>
    public float HipBackLeanShare { get; init; } = 0.6f;

    /// <summary>沈み込みの最大（立つ高さに対する割合）。膝と股を曲げて腰を落とす。</summary>
    public float MaxCrouch { get; init; } = 0.4f;

    /// <summary>腰の沈みで足りない頭の下げを、股から上体を前へ折って補う最大角（ヒップヒンジ）。</summary>
    public float MaxHingeRadians { get; init; } = 20f * MathF.PI / 180f;

    // ── 掴み・投げ
    public float GrabRadius { get; init; } = 0.04f;
    public float GrabBreakForce { get; init; } = 260f;
    public float ThrowSpeed { get; init; } = 1.1f;
    public float ThrowAssist { get; init; } = 0.35f;
    public float GrabbedDriveScale { get; init; } = 0.55f;

    // ── 状態
    public float FallUprightness { get; init; } = 0.4f;
    public float FallGrace { get; init; } = 0.15f;
    public float CollapseDuration { get; init; } = 0.3f;
    public float DownDriveScale { get; init; } = 0.05f;
    public float DownTime { get; init; } = 1.6f;
    public float GetUpDuration { get; init; } = 1.0f;
    public float RecoverUprightness { get; init; } = 0.78f;
    public float NoGroundFallTime { get; init; } = 0.4f;

    // ── ボクセルの体: 打撃で削れ、動きに響き、骨が断たれたら落ち、臓器が壊れたら死ぬ（Voxels/BoxerBody）
    /// <summary>ボクセル 1 個の大きさの目安 [m]（部位の箱をこれに近い大きさで割り切る）。</summary>
    public float VoxelSize { get; init; } = 0.005f;
    /// <summary>
    /// ダメージ 1 あたりに削る量（肉 1 ボクセル = 1。骨は 8、臓器は 1.2〜1.4）。試合の打撃は運動エネルギーから削る量を決める（<see cref="CarvePerJoule"/>）ので、
    /// これは削る量を渡さずにダメージだけで削るとき（テストや道具）に使う。
    /// </summary>
    public float CarvePerDamage { get; init; } = 4.3f;
    /// <summary>削れたボクセルが飛び散る速さ [m/s]（60cm の人形に合わせた値）。</summary>
    public float ChipSpeed { get; init; } = 0.8f;
    /// <summary>飛び散った破片にかかる重力の倍率（1 = 実際の重力。小さいほどふわりと長く舞う）。</summary>
    public float DebrisGravityScale { get; init; } = 0.35f;
    /// <summary>
    /// 運動エネルギー 1 J あたりに削る量（肉 1 ボクセル = 1）。削る量は拳の運動エネルギー（½ × 乗った質量 × 速さ²）に比例する:
    /// 体重の乗った速い 1 発ほど急に深く掘れ、手打ちはほとんど掘れない（非線形）。硬い頭蓋（骨）が、その奥の脳までの壁になる。
    /// 頭を人の比率（<see cref="HeadScale"/> 0.8）に小さくしたとき、正面への重い 1 発で脳が削れる割合が変わらないよう 45 → 12 にした。
    /// </summary>
    public float CarvePerJoule { get; init; } = 12f;

    /// <summary>1 発で削る量の上限（約 10 J ぶん。速さの測り違いのような外れの 1 発が、腕を丸ごと削らないように）。</summary>
    public float MaxCarve { get; init; } = 120f;

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
    public float FullStrikeSpeed { get; init; } = 2.5f;

    /// <summary>打撃で当たった部位に残す運動量が、その部位に与える速さの上限 [m/s]（拳の速さの測り違いで部位を飛ばさない）。</summary>
    public float MaxStrikeSpeed { get; init; } = 6f;

    /// <summary>脳しんとうの効き: 打撃で頭がねじられた速さ（頭の慣性で割った回転 [rad/s]）の 2 乗あたりに意識から引く量。</summary>
    public float ConcussionGain { get; init; } = 0.000035f;

    /// <summary>脳しんとうから意識が戻る速さ（時定数 [s]）。</summary>
    public float ConcussionRecovery { get; init; } = 5f;

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

    /// <summary>意識がこの値を下回ると意識が途切れ、体の張りは 0（全身の力が抜けて倒れきる）。</summary>
    public float KnockoutConsciousness { get; init; } = 0.3f;

    /// <summary>起き上がるのに要る意識（腰の落ちた、ふらつく体で起き上がる）。</summary>
    public float GetUpConsciousness { get; init; } = 0.55f;

    /// <summary>骨盤が支えようとしている高さより、立つ高さのこの割合だけ低いまま <see cref="CollapseTime"/> 過ぎたら、崩れ落ちた（ダウン）。</summary>
    public float CollapseDrop { get; init; } = 0.25f;
    public float CollapseTime { get; init; } = 0.25f;

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
    public float DentDepthPerDamage { get; init; } = 0.0005f;
    /// <summary>1 発でへこむ深さの上限 [m]。</summary>
    public float MaxDentDepthPerHit { get; init; } = 0.008f;
    /// <summary>拳の表面からこの距離 [m] までのノードがへこみに引きずられる。</summary>
    public float DentSpread { get; init; } = 0.016f;
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
            return new HumanoidRagdoll.HalfExtents(
                d.Pelvis * Scale, d.Chest * Scale, d.Head * (Scale * HeadScale),
                d.UpperArm * Scale * new Vector3(ArmLengthScale, 1f, 1f), d.Forearm * Scale * new Vector3(ArmLengthScale, 1f, 1f),
                d.Thigh * Scale, d.Shin * Scale, d.Foot * Scale * new Vector3(1f, 1f, 1f - FootGeometry.ToeShare)); // 足はかかと側（つま先は別の部位）
        }
    }

    [JsonIgnore]
    public HumanoidRagdoll.MassDistribution Masses
    {
        get
        {
            var m = HumanoidRagdoll.MassDistribution.Default;
            return m with
            {
                Pelvis = m.Pelvis * MassScale, Chest = m.Chest * MassScale, Head = m.Head * MassScale,
                UpperArm = m.UpperArm * MassScale, Forearm = m.Forearm * MassScale, Thigh = m.Thigh * MassScale,
                Shin = m.Shin * MassScale, Foot = m.Foot * MassScale * (1f - FootGeometry.ToeShare), // かかと側（残りはつま先）
            };
        }
    }

    public static FighterTuning Default { get; } = new();

    internal static Vector3 Up => Vector3.UnitY;
}
