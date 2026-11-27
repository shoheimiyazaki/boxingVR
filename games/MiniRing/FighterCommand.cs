using System.Numerics;

namespace MiniRing;

public enum Hand
{
    Left = 0,
    Right = 1,
}

public static class HandExtensions
{
    /// <summary>
    /// 体フレームのローカル X でその手がある側（左 +1 / 右 −1）。
    /// Aigis は右手系・Y 上なので、+Z を向いた人の右手側は −X になる（右 = 前 × 上 = Z × Y = −X）。
    /// </summary>
    public static float LocalSide(this Hand hand) => hand == Hand.Left ? 1f : -1f;
}

/// <summary>
/// 片手ぶんの指令。目標はファイターの体フレーム（肩中心・向きヨー）のローカル座標 [m]。
/// <paramref name="Spring"/>（0..1）は体の動きを拳へ移す割合: 拳が体にバネでつながり、体幹の運動エネルギーが拳の速さに移る
/// （溜めで縮めた体のバネ・回避で動いた体の運動量が拳に乗る。<see cref="FighterTuning.BodyToFistEfficiency"/>）。ボットと意図操作が拳を出していく間に使う。VR と従来のキーボード操作は 0（自分の手をきっちり追う）。
/// 記録形式 v4 では胴のひねりとともに保存する。CPU の値は再生のたびに同じ入力から計算する。
/// <para><paramref name="Orientation"/> は、その手が武器を握っているときの武器の向きの目標（武器のローカル軸 → 体フレームの回転。武器の
/// ローカル X が打つ側の先）。拳が位置の目標へバネで引かれるのと同じく、手首の筋肉が武器をこの向きへバネで回す。null なら前腕に沿わせる。</para>
/// <para><paramref name="LetGo"/> は、その手が握っている武器（添えている武器）を放す。放した手は素手の拳になる（はめた物は外せない）。</para>
/// </summary>
/// <para><paramref name="Tuck"/>（0..1、2026-11-17）は肘の目標: 0 = 肘を下・外・やや後ろへ逃がす（今までの形。拳を出す・フックを振る腕）、
/// 1 = 脇を締めて肘を脇腹の前へ寄せる（胴を肘と前腕で覆う守り）。拳の目標と同じく腕の形の目標で、肩・肘のサーボが追う。</para>
/// <para><paramref name="Slide"/>（2026-11-17）は、握っている棒を手の中で滑らせる（VR のトリガーを離している）。滑っている手は棒に力を伝えず、
/// 握り直した所が新しい握りになる。棒のように握りの位置を選べる武器（<c>Weapon.SlideSpanOf</c>）だけで効き、他の武器・素手では無視する。
/// 既定の false は今まで通り握ったまま。</para>
/// <para><paramref name="ElbowTarget"/>（2026-11-17、肘打ち）は肘の点の目標（体フレームのローカル）。渡している間は、肘がその点の向きへ出る（IK の極。
/// <paramref name="Tuck"/> の代わり）うえ、上腕・前腕の骨のセルが拳・蹴りと同じ接触で相手に当たる。拳の目標（<paramref name="TargetLocal"/>）は
/// 畳んだ手を置く所（耳・こめかみ）。肘の点を速く動かす経路（<c>Bots.ElbowSpec</c>）と胴のひねりで、肘が物理から速さと質量を得る。
/// 威力・種類の倍率は持たない。</para>
public readonly record struct HandCommand(Vector3 TargetLocal, bool Grip, float Spring = 0f, Quaternion? Orientation = null, bool LetGo = false, HandShape Shape = HandShape.Seiken, float Tuck = 0f, bool Slide = false, Vector3? ElbowTarget = null);

/// <summary>
/// 1 ステップ分の操作指令 — 入力元（VR / キーボード / AI）とラグドールの唯一の契約。
/// ラグドールはこれを位置として書き込まず、バネ・ダンパの目標として追う。
/// <para><paramref name="HeadOffset"/> は頭の構えからのずれ（体フレームのローカル [m]）: 前へ出せば前傾、横へずらせば
/// スリップ（横傾き）、下げればダッキング（沈み込み）。体の軸の向きは両手の位置とこの頭の位置から決まる。</para>
/// <para><paramref name="TwistBias"/> は胴のひねりの指令 [rad]（正で右肩が前）。分割胴の肘打ちでは、畳んだ拳の位置差に代わる腰の回旋目標として使う。</para>
/// <para>四足の体では <paramref name="LeftLeg"/> と <paramref name="RightLeg"/> が左右の前脚を動かし、後脚は足運びが動かす。</para>
/// <para><paramref name="Step"/>（2026-11-18、ステップ）は、脚のバネ（構えで縮めてある脚: <see cref="Fighter.StepSpring"/>）を体の移動へ放つ向き（ワールドの水平）と割合（長さ 0..1）。
/// 拳の <see cref="HandCommand.Spring"/> と同じく「体のバネをどこへ移すか」の目標で、位置や速さは書かない。前へ放てば後ろ足が床を蹴って一気に詰め、
/// 後ろへ放てば前足が蹴って下がる（どちらの足が蹴るかは、向きに対して腰より後ろにある足: 脚が押せる向きの幾何から）。放った後は足が体を受け止めて構えを整え直し、
/// 両足が床に着いてから脚の筋肉がバネを縮め直すまでは次のステップは弱い（クールダウンは時計ではなく、足の運びと筋肉の仕事から出る）。</para>
/// </summary>
public readonly record struct FighterCommand(Vector3 MoveWorld, Vector3 FaceDirection, HandCommand Left, HandCommand Right, Vector3 HeadOffset = default, float TwistBias = 0f, LegCommand? LeftLeg = null, LegCommand? RightLeg = null, Vector3 Step = default)
{
    /// <summary>水平・長さ 1 以下に正規化したステップの指令（有限でなければ 0）。</summary>
    public Vector3 ClampedStep
    {
        get
        {
            var flat = new Vector3(Step.X, 0f, Step.Z);
            var length = flat.Length();
            return !float.IsFinite(length) ? Vector3.Zero : length > 1f ? flat / length : flat;
        }
    }

    public HandCommand For(Hand hand) => hand == Hand.Left ? Left : Right;

    public LegCommand? LegFor(Hand leg) => leg == Hand.Left ? LeftLeg : RightLeg;

    /// <summary>水平・長さ 1 以下に正規化した移動指令。</summary>
    public Vector3 ClampedMove
    {
        get
        {
            var flat = new Vector3(MoveWorld.X, 0f, MoveWorld.Z);
            var length = flat.Length();
            return length > 1f ? flat / length : flat;
        }
    }
}

/// <summary>
/// 相手が握っている武器の見え方（ワールド座標）: 握り（鍔の側の手）・先の位置と、先の速度。<paramref name="Present"/> が false なら
/// 相手は武器を握っていない。剣士は相手の剣先の動きを見て、受ける・外す。
/// </summary>
public readonly record struct BladeSense(bool Present, Vector3 Hilt, Vector3 Tip, Vector3 TipVelocity);

/// <summary>
/// 自分の握っている武器の見え方: 握っているか（<paramref name="Held"/>）、握る所から先の端までの長さ（今の <paramref name="Reach"/> と、
/// 作ったときの <paramref name="FullReach"/>）[m]。折れて短くなった武器は Reach が FullReach より短い。<paramref name="Grip"/>（握る所）と
/// <paramref name="Blade"/>（握りから先へ向かう単位ベクトル）は、今の武器が実際にある所（ワールド。手に感じる剣の位置と向き）。
/// </summary>
public readonly record struct WeaponSense(bool Held, float Reach, float FullReach, Vector3 Grip = default, Vector3 Blade = default)
{
    /// <summary>折れて失った長さ [m]。</summary>
    public float Lost => Held ? MathF.Max(0f, FullReach - Reach) : 0f;
}

/// <summary>拳 1 つの見え方（ワールド座標の位置と速度）。</summary>
public readonly record struct GloveSense(Vector3 Position, Vector3 Velocity);

/// <summary>
/// 相手の胴の見え方（ワールド座標の位置）: 両肩と腰。パンチは拳より先に腰と肩が動き出す（体重移動・ひねり・踏み込み）ので、
/// 上級者は拳ではなくここを見て次の拳を読む。
/// </summary>
public readonly record struct BodySense(Vector3 LeftShoulder, Vector3 RightShoulder, Vector3 Pelvis)
{
    public Vector3 Shoulder(Hand hand) => hand == Hand.Left ? LeftShoulder : RightShoulder;
}

/// <summary>入力元がファイターの状況を知るための読み取り専用情報。OpponentAlive は相手が打ってよい状態か
/// （KO されておらず、立っている）。倒れた相手には手を出さない。
/// <para>SelfHead と相手の拳（<paramref name="OpponentLeft"/> / <paramref name="OpponentRight"/>）は、ボットが飛んでくる拳を
/// 見て守るためのもの（ブロック・ダッキング・スリップ・スウェー）。<paramref name="OpponentVelocity"/> は相手の胴の速度で、
/// 拳の動きから歩く動きを差し引いて「パンチの動き出し」を見るのに使う。<paramref name="OpponentBody"/> は相手の両肩と腰
/// （打つ前の体の動きを読むため。見えないホストでは既定値）。<paramref name="SelfFaintness"/> は自分の意識が薄れた度合い（<see cref="Fighter.Faintness"/>、0..1）。</para>
/// <para><paramref name="OpponentBlade"/> は相手の握った武器の見え方、<paramref name="SelfArmed"/> は自分が武器を握っているか（null は分からない:
/// 武器を扱わない入力元のため）。武器を落とした剣士は拳で戦う（<see cref="Bots.DisarmedFallback"/>）。</para>
/// <para><paramref name="OpponentDowned"/> は相手の体が倒れている（ダウン・起き上がり中）、<paramref name="MayStrikeDowned"/> はその相手を攻めてよい（レフェリーがいない試合。
/// <see cref="BoxingCorners.Referee"/>）。<paramref name="OpponentAlive"/>（立っていて打ってよい）の意味は変わらない。レフェリーがいる試合では <paramref name="MayStrikeDowned"/> は常に false で、入力元は今までどおり倒れた相手へ手を出さない。</para>
/// <para><paramref name="Ring"/> はこの試合のリング（null はリングを指定しない試合の場 <see cref="RingLayout.DrillBoxing"/>）。ロープ際を知り、下がる余地を測るため。</para>
/// <para><paramref name="SelfWeapon"/> は自分の握っている武器の長さ（折れて短くなったか。<see cref="WeaponSense"/>）。</para>
/// <para>戦略の層（<see cref="Bots.GamePlan"/>、2026-11-18）の入力: <paramref name="SelfInjury"/> は自分の体の傷み（0..1、腕・脚の残った筋の断面・神経の働き・
/// 折れて垂れた先・落ちた部位から出す「失った力」。自分が感じる量。<see cref="Fighter.Injury"/>）、<paramref name="OpponentInjury"/> は相手の<b>見えている</b>傷み
/// （残った筋の断面・折れて垂れた先・落ちた部位だけ。意識・臓器・神経は入れない）、<paramref name="OpponentWinded"/> は相手の息の上がり（<see cref="TiredLook"/> が描くのと同じ
/// <see cref="Fighter.Winded"/>。見た目の手がかり）、<paramref name="Match"/> は試合の時刻・点の差・打ち合いの無い時間（<see cref="MatchSense"/>）。既定値は「傷み無し・
/// 息は普通・時刻を知らない」で、読まない入力元は今までと同じに動く。どれも読むだけで、体の物理には響かない。</para>
/// <para><paramref name="SelfLegOxygenLeft"/>・<paramref name="SelfLegOxygenRight"/> は自分の左右の脚の酸素の蓄えの残り（0..1、<see cref="Fighter.LegOxygen"/>。
/// 自分が感じる脚の疲れ。倒れた相手を踏む脚を、疲れた脚から元気な脚へ踏み替えるため: <see cref="Bots.HeelBarrage"/>）。既定は 1（疲れていない）。</para>
/// <para><paramref name="SelfSupplies"/> はこの試合で休みに武器を支給された回数（<see cref="MatchRules.ResupplyWeapons"/>）。武器を失って拳で戦っていた入力元が、
/// 支給された新しい武器を握ったと知るため（<see cref="Bots.DisarmedFallback"/>）。</para>
/// <para><paramref name="SelfBody"/> は自分の両肩と腰、<paramref name="SelfChin"/> は自分のあご（ワールド）。拳の間合いを肩からあごまでで測るため（<see cref="Bots.ReachGap"/>）。既定値なら胸・頭から測る。</para>
/// </summary>
public readonly record struct FighterSense(
    BodyFrame Self, float ArmReach, Vector3 OpponentHead, Vector3 OpponentChest, bool OpponentAlive,
    Vector3 SelfHead = default, GloveSense OpponentLeft = default, GloveSense OpponentRight = default, Vector3 OpponentVelocity = default,
    BodySense OpponentBody = default, float SelfFaintness = 0f, BladeSense OpponentBlade = default, bool? SelfArmed = null,
    TargetMap? OpponentTargets = null, RingLayout? Ring = null, WeaponSense SelfWeapon = default, float SelfStamina = 1f,
    VisionState? SelfVision = null, VisionState? OpponentVision = null, float OpponentArmReach = 0f,
    bool OpponentDowned = false, bool MayStrikeDowned = false, BodyBuild? SelfBuild = null, BodyBuild? OpponentBuild = null,
    LegBody? SelfLegLeft = null, LegBody? SelfLegRight = null,
    float SelfInjury = 0f, float OpponentInjury = 0f, float OpponentWinded = 0f, MatchSense Match = default, int SelfSupplies = 0, GrappleSense? Grapple = null,
    bool SelfDowned = false, float SelfLegOxygenLeft = 1f, float SelfLegOxygenRight = 1f, BodySense SelfBody = default, Vector3 SelfChin = default)
{
    /// <summary>自分の脚の寸法と立った形の膝（体フレーム）。分からなければ腕の長さからの見積もり。</summary>
    public LegBody SelfLeg(Hand leg) => (leg == Hand.Left ? SelfLegLeft : SelfLegRight) ?? LegBody.FromReach(leg, ArmReach);

    public GloveSense Opponent(Hand hand) => hand == Hand.Left ? OpponentLeft : OpponentRight;

    /// <summary>自分の脚 <paramref name="leg"/> の酸素の蓄えの残り（0..1）。</summary>
    public float SelfLegOxygen(Hand leg) => leg == Hand.Left ? SelfLegOxygenLeft : SelfLegOxygenRight;

    /// <summary>この試合のリング（分からなければ拳の試合の標準）。</summary>
    public RingLayout Arena => Ring ?? RingLayout.DrillBoxing;
}

/// <summary>
/// 試合の時刻と点（<see cref="FighterSense.Match"/>、2026-11-18）: 自分から見た値。リングの上の誰でも分かる量（時計・ダウンの数・打ち合いの有無）と、
/// 自分が与えた・受けたダメージ（試合の得点の物差し。判定の点と同じ量）だけで、相手の体の中の値は持たない。
/// </summary>
/// <param name="RoundElapsed">このラウンドの経過 [s]（試合が動いていた時間）。</param>
/// <param name="RoundLength">ラウンドの長さ [s]。0 は決まっていない（時間の無いラウンド: K.O. のたびに作り直す試合）。</param>
/// <param name="RoundsLeft">このラウンドの後に残るラウンドの数（決まっていなければ 0）。</param>
/// <param name="DamageLead">点の差: 自分が与えたダメージ − 受けたダメージ（今の体が受けたダメージの合計の差。テレビ中継の試合では体がラウンドをまたぐので試合を通した差、時間の無いラウンドでは K.O. で体が作り直されると 0 に戻る）。</param>
/// <param name="DownLead">取ったダウン − 取られたダウン（試合を通して）。</param>
/// <param name="Quiet">打ち合いの無い時間 [s]: どちらかの打撃が最後に入って（得点が動いて）からの時間。ラウンドの始まりから数える。</param>
public readonly record struct MatchSense(float RoundElapsed = 0f, float RoundLength = 0f, int RoundsLeft = 0, float DamageLead = 0f, int DownLead = 0, float Quiet = 0f)
{
    /// <summary>ラウンドの進み（0..1）。長さが決まっていなければ 0。</summary>
    public float RoundProgress => RoundLength > 0f && float.IsFinite(RoundElapsed) ? Math.Clamp(RoundElapsed / RoundLength, 0f, 1f) : 0f;
}

/// <summary>
/// 組み技のための見え方（docs/GRAPPLING.md）。相手の体の見える所（肘・手首・足首・重心とその速さ）と、相手の崩れ
/// （重心が足の支えの外へ出ている距離 <paramref name="OpponentMargin"/>、中なら負）、自分が掴んでいるか・掴まれているか、
/// 自分の肘にかかる過伸展の力の、靭帯の降伏に対する割合（<paramref name="SelfElbowLoadLeft"/> など。関節を極められた痛み）。
/// 左右は相手の本人の左右（Hand）。
/// </summary>
public readonly record struct GrappleSense(
    Vector3 OpponentCom, Vector3 OpponentComVelocity, float OpponentMargin,
    Vector3 OpponentLeftElbow, Vector3 OpponentRightElbow, Vector3 OpponentLeftWrist, Vector3 OpponentRightWrist,
    Vector3 OpponentLeftAnkle, Vector3 OpponentRightAnkle,
    bool HoldingLeft, bool HoldingRight, bool SelfGrabbed, float SelfElbowLoadLeft, float SelfElbowLoadRight,
    float OpponentElbowLoadLeft, float OpponentElbowLoadRight, float SelfMargin)
{
    public Vector3 OpponentElbow(Hand hand) => hand == Hand.Left ? OpponentLeftElbow : OpponentRightElbow;

    public Vector3 OpponentWrist(Hand hand) => hand == Hand.Left ? OpponentLeftWrist : OpponentRightWrist;

    public Vector3 OpponentAnkle(Hand leg) => leg == Hand.Left ? OpponentLeftAnkle : OpponentRightAnkle;

    public bool Holding(Hand hand) => hand == Hand.Left ? HoldingLeft : HoldingRight;

    public float OpponentElbowLoad(Hand hand) => hand == Hand.Left ? OpponentElbowLoadLeft : OpponentElbowLoadRight;
}

/// <summary>ファイターの操作元。VR・キーボード・AI がこれを実装する。</summary>
public interface IFighterControl
{
    public FighterCommand Read(in FighterSense sense, float dt);

    /// <summary>自分のパンチが当たった（ハプティクス等のフィードバック用）。</summary>
    public void OnPunchLanded(Hand hand, float damage)
    {
    }

    /// <summary>
    /// ラウンドが変わって作り直された操作元へ、前のラウンドの操作元（同じコーナー）を渡す。覚えたことを試合の中で引き継ぐため
    /// （学ぶボット）。試合を最初から再生すれば同じ順に呼ばれるので、記録の再生は一致する。
    /// </summary>
    public void CarryOver(IFighterControl previous)
    {
    }

    /// <summary>自分が打たれた（ボットが「守れたか」を知るため）。</summary>
    public void OnPunchTaken(in HitEvent hit)
    {
    }
}

/// <summary>外部（ホスト）が毎フレーム書き込む指令をそのまま返す操作元。デスクトップ/VR ホストが使う。</summary>
public sealed class ExternalControl : IFighterControl
{
    public FighterCommand Command { get; set; }

    public Action<Hand, float>? PunchLanded { get; set; }

    public FighterCommand Read(in FighterSense sense, float dt) => Command;

    public void OnPunchLanded(Hand hand, float damage) => PunchLanded?.Invoke(hand, damage);
}
