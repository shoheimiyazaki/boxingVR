using System.Numerics;
using MiniRing.Bots;
using MiniRing.Weapons;

namespace MiniRing.Motion;

/// <summary>両手持ちの武器の左手の決め方（<see cref="VrPlayerMapper.TwoHand"/>）。</summary>
public enum VrTwoHand
{
    /// <summary>左右のコントローラが、それぞれの手を動かす。</summary>
    Both,

    /// <summary>① 右のコントローラが両手を動かす: 左のグリップを押している間、左手の目標は右の手の柄の上の点。</summary>
    Right,
}

/// <summary>
/// VR の姿勢・ボタン → プレイヤーのファイター指令（ホスト非依存。<c>MiniRing.Vr.VrPlayerControl</c> は OpenXR の入力をこれへ渡すだけ）。
/// 左スティック: 歩き（視線基準） ／ 腕: コントローラ位置を肩基準で腕の長さ比に縮め、構えからの動きを <see cref="MotionGain"/> 倍して人形の体へ ／
/// グリップ（squeeze）: 掴む（クリンチ） ／ 向きは常に相手へロックオン。
/// <para>
/// <b>武器の持ち方・旧</b>（<c>weaponMode</c>、2026-11-17。<see cref="GripHolds"/> が false のとき。合成の記録 <see cref="VrMotionSynth"/> が使う）:
/// トリガーを引いている手は握る、離している手は握った武器を手の中で滑らせる（<see cref="HandCommand.Slide"/>）。
/// 武器の向きの目標（<see cref="HandCommand.Orientation"/>）は、コントローラの向きを体フレームへ写したもの
/// （武器のローカル X = コントローラの前 = grip 姿勢の −Z、ローカル Y = コントローラの上、ローカル Z = コントローラの右）。武器の無い試合では
/// どちらも出さず、今まで通り（Orientation null・Slide false）。
/// </para>
/// <para>
/// <b>グリップで持つ</b>（<see cref="GripHolds"/>、2026-11-19。VR ホストの武器の試合）: グリップボタン = <see cref="HandCommand.Grip"/>（素手のときの掴みと同じ欄）。
/// 握っていない手でグリップを握って手が柄の近くにあれば持つ（拾う・添える。体の側）。武器を握っている手でグリップを<b>離した瞬間</b>だけ
/// <see cref="HandCommand.LetGo"/> を 1 歩出す（離しっぱなしでは出さない: 始めに握っていた武器をいきなり落とさない）。トリガーは使わない（<c>Slide</c> は false）。
/// </para>
/// <para>
/// <b>① 右手で両手</b>（<see cref="TwoHand"/> = <see cref="VrTwoHand.Right"/>）: 両手持ちの武器で、左右のグリップを押している間、左手の目標は左のコントローラでなく
/// 「右のコントローラの姿勢から決めた柄の上の点」（右手の目標から武器の軸に沿って柄頭の側へ、両手の間隔 <see cref="FormSwordsman.Spacing"/> × 人形の腕の長さ）。
/// 左手の武器の向きも右と同じ。左のグリップを離せば左手は左のコントローラを追う（片手持ち）。
/// </para>
/// <para>
/// <b>② 一人称</b>（<see cref="FirstPerson"/>）: カメラは HMD の姿勢のまま（頭に固定しない）。身長の合わせのときに「VR の空間 → ワールド」の変換
/// （<see cref="VrWorldPlacement"/>）を決めて固定し、体の頭の目標 = HMD のワールドの位置。体はその点へ頭を持っていく: 水平のずれは歩く指令
/// （<see cref="WalkToward"/>: ずれに比例・歩く速さで頭打ち。小さい間は <see cref="FighterCommand.HeadOffset"/> で傾けるだけ）。左スティックは変換の基準点を
/// ワールドで動かす（体が追って歩く）。位置・姿勢は書き込まない（§1.1）。
/// </para>
/// </summary>
public sealed class VrPlayerMapper(bool weaponMode = false)
{
    /// <summary>コントローラの動き（構えからのずれ）の増幅率。</summary>
    public const float MotionGain = 2f;

    /// <summary>握り・トリガーが「押した」になる値。</summary>
    public const float GripOn = 0.55f;

    /// <summary>握り・トリガーが「離した」になる値（ヒステリシス。半押しでチャタリングしない）。</summary>
    public const float GripOff = 0.35f;

    /// <summary>② 頭のずれ（人形の腕の長さ = 1）がこれより小さい間は歩かない（傾けるだけ）。頭の傾き（<see cref="FighterCommand.HeadOffset"/>）で追える範囲。</summary>
    public const float WalkDeadZone = 0.15f;

    /// <summary>② 歩く指令が 1（歩く速さの最大）になるまでの、デッドゾーンの外側の幅（腕の長さ = 1）。</summary>
    public const float WalkFullRange = 0.7f;

    /// <summary>② 頭の傾きの水平方向の上限（腕の長さ = 1）。これを超える動きは歩きで追う（傾けすぎて転ばない）。</summary>
    public const float LeanLimit = 0.3f;

    /// <summary>② スティックで基準点を動かす速さ ÷ 歩く速さ。体が押されて遅れても基準点が逃げ切らないよう 1 より小さく、<see cref="MaxLead"/> で頭打ちにもする。</summary>
    public const float StickWalkRatio = 0.8f;

    /// <summary>② 頭の目標が体の頭からこれ（腕の長さ = 1）より離れたら、基準点はそれ以上離れる向きへは動かない（体がロープ・相手に止められてもカメラが置いて行かれない）。</summary>
    public const float MaxLead = 1.2f;

    private const float BodyYawSmoothing = 4f; // [1/s] 頭を振っても腕の写像が暴れないよう体の向きは緩く追う
    private const float StanceSmoothing = 0.8f; // [1/s] 構えの位置（頭の平均位置）の追従。これより速い頭の動き＝スリップ・ダッキング

    private readonly bool[] _gripHeld = new bool[2];
    private readonly bool[] _triggerHeld = new bool[2];
    private readonly Vector3[] _lastHandLocal = new Vector3[2];
    private readonly Quaternion[] _lastOrientation = [Quaternion.Identity, Quaternion.Identity];
    private float _bodyYaw;
    private Vector3 _stanceHead;
    private bool _calibrated;
    private bool _placeAgain;

    /// <summary>身長のキャリブレーション値 [m]。</summary>
    public float PlayerHeight { get; private set; } = 1.7f;

    /// <summary>プレイヤーの腕の長さの見積もり [m]（肩から拳まで。<see cref="PuppetMapping.ArmReachFromHeight"/>）。</summary>
    public float PlayerArmReach => PuppetMapping.ArmReachFromHeight(PlayerHeight);

    /// <summary>直近のフレームの視線のヨー [rad]。</summary>
    public float HeadYaw { get; private set; }

    /// <summary>武器を持つ試合か。</summary>
    public bool WeaponMode => weaponMode;

    /// <summary>true ならグリップで持つ（拾う・添える・離すと放す）。false なら旧: トリガーで握り、離すと棒が滑る。</summary>
    public bool GripHolds { get; init; }

    /// <summary>両手持ちの武器の左手の決め方（<see cref="GripHolds"/> のときだけ）。既定は左右それぞれ。</summary>
    public VrTwoHand TwoHand { get; init; } = VrTwoHand.Both;

    /// <summary>② 一人称。カメラは HMD の姿勢のまま、体の頭を HMD のワールドの位置へ持っていく。</summary>
    public bool FirstPerson { get; init; }

    /// <summary>自分の武器（① の両手の間隔の種。null は武器なし・間隔を知らない → 左は左のコントローラ）。</summary>
    public WeaponKind? HeldWeapon { get; init; }

    /// <summary>② スティックの基準点の移動の最大の速さ [m/s]（ワールド）の元になる、人形の歩く速さ（<see cref="FighterTuning.WalkSpeed"/>）。</summary>
    public float WalkSpeed { get; init; } = FighterTuning.Default.WalkSpeed;

    /// <summary>② 一人称の変換（身長の合わせで決まる。合わせるまでは null）。ホストが目の描画に使う。</summary>
    public VrWorldPlacement? Placement { get; private set; }

    /// <summary>キャリブレーションしたとき（目の高さ [m]、身長 [m]）。ホストが表示に使う。</summary>
    public Action<float, float>? OnCalibrated { get; set; }

    /// <summary>その手のトリガーを引いているか（武器を握っている）。</summary>
    public bool TriggerHeld(Hand hand) => _triggerHeld[(int)hand];

    /// <summary>その手のグリップを押しているか（ヒステリシス付き）。</summary>
    public bool GripPressed(Hand hand) => _gripHeld[(int)hand];

    /// <summary>
    /// ② 次のフレームで変換を合わせ直す（ワールドを作り直して人形が別の所に立ったとき。身長の合わせはそのまま）。
    /// </summary>
    public void ResetPlacement()
    {
        _placeAgain = true;
        _kick.Cancel();
        _frontLeg = new FrontLegStrike();
    }

    /// <summary>
    /// フェイスボタンの操作（稽古）: A 単独（離したとき）= 身長の合わせ、4 つ同時 = 作り直しの要求（<see cref="ResetRequested"/>）、X = 左脚・Y = 右脚の蹴り
    /// （<see cref="KickRunner"/>）。true のとき、ホストは A/X の押した瞬間の合わせ・B/Y の記録を使わない。
    /// </summary>
    public bool FaceButtonControls { get; init; }

    /// <summary>直近の <see cref="Read"/> の歩で、4 つのフェイスボタンが同時に押された（ホストが稽古の世界を作り直す）。</summary>
    public bool ResetRequested { get; private set; }

    private readonly FaceButtonTracker _faceButtons = new();
    private readonly KickRunner _kick = new();
    private FrontLegStrike _frontLeg = new();

    public BoxingInput Read(in VrRawFrame frame, in FighterSense sense, float dt)
    {
        // カメラ（頭）はローカル −Z を向く。
        var headYaw = BodyFrame.YawOf(Vector3.Transform(-Vector3.UnitZ, frame.HeadRotation), _bodyYaw);
        HeadYaw = headYaw;
        var calibratedNow = false;
        var events = FaceButtonControls ? _faceButtons.Step(frame.Buttons) : default;
        ResetRequested = events.Reset;
        if ((!_calibrated && frame.HeadPosition.Y > 0.8f) || frame.CalibratePressed || events.Calibrate)
        {
            PlayerHeight = PuppetMapping.PlayerHeightFromEye(frame.HeadPosition.Y);
            _bodyYaw = headYaw;
            _stanceHead = frame.HeadPosition;
            _calibrated = true;
            calibratedNow = true;
            OnCalibrated?.Invoke(frame.HeadPosition.Y, PlayerHeight);
        }

        _bodyYaw = PuppetMapping.SmoothYaw(_bodyYaw, headYaw, BodyYawSmoothing, dt);
        // 肩は「構えの頭位置」から推定する（頭だけ振っても腕の写像は動かない）。頭のずれは体の軸の傾きとして別に渡す。
        _stanceHead = Vector3.Lerp(_stanceHead, frame.HeadPosition, 1f - MathF.Exp(-StanceSmoothing * dt));
        var shoulders = PuppetMapping.PlayerShoulderFrame(_stanceHead, _bodyYaw, PlayerHeight);
        var scale = PuppetMapping.Scale(sense.ArmReach, PlayerHeight);
        var move = PuppetMapping.StickToWorld(frame.LeftStick, headYaw);
        var headOffset = PuppetMapping.HeadOffsetLocal(frame.HeadPosition, _stanceHead, _bodyYaw, scale);

        if (FirstPerson && _calibrated)
        {
            (move, headOffset) = FirstPersonMove(frame, sense, headYaw, scale, headOffset, move, calibratedNow, dt);
        }

        // トラッキングが切れた手は最後の目標に留める（急に体の中心へ吸い込まれないように）。
        foreach (var hand in new[] { MiniRing.Hand.Left, MiniRing.Hand.Right })
        {
            if (frame.Tracked(hand))
            {
                var i = (int)hand;
                _lastHandLocal[i] = PuppetMapping.HandTargetLocal(frame.HandPosition(hand), shoulders, scale, hand, PlayerHeight, MotionGain);
                _lastOrientation[i] = WeaponOrientation(frame.HandRotation(hand), shoulders);
            }
        }

        var grabLeft = Held(_gripHeld, 0, frame.Grip(MiniRing.Hand.Left), frame.Focused, out var releasedLeft);
        var grabRight = Held(_gripHeld, 1, frame.Grip(MiniRing.Hand.Right), frame.Focused, out var releasedRight);
        var followRight = GripHolds && TwoHand == VrTwoHand.Right && sense.SelfWeapon.Held && grabLeft && grabRight && HeldWeapon is { } w && FormSwordsman.Handles(w);

        var left = _lastHandLocal[0];
        var leftOrientation = _lastOrientation[0];
        if (followRight)
        {
            // ① 右のコントローラが両手を動かす: 右の手の目標から武器の軸（右の向きの前）に沿って、柄頭の側へ両手の間隔だけ。
            var axis = Vector3.Transform(Vector3.UnitX, _lastOrientation[1]);
            left = _lastHandLocal[1] - (axis * (FormSwordsman.Spacing(HeldWeapon!.Value) * sense.ArmReach));
            leftOrientation = _lastOrientation[1];
        }

        var letGo = GripHolds && sense.SelfWeapon.Held; // 握っているときだけ放す（握っていない手の離しは何も起こさない）
        var command = new FighterCommand(move, sense.OpponentChest - sense.Self.Origin,
            HandOf(MiniRing.Hand.Left, left, leftOrientation, grabLeft, letGo && releasedLeft, frame),
            HandOf(MiniRing.Hand.Right, _lastHandLocal[1], _lastOrientation[1], grabRight, letGo && releasedRight, frame),
            headOffset);
        if (sense.SelfBuild?.FourLegged == true)
        {
            command = command with
            {
                Left = command.Left with { Orientation = leftOrientation, Grip = false },
                Right = command.Right with { Orientation = _lastOrientation[1], Grip = false },
            };
            var sweep = frame.Focused && frame.LeftTrigger > 0.6f;
            return new BoxingInput(_frontLeg.Read(command, sense, sweep || (frame.Focused && frame.RightTrigger > 0.6f), sweep, dt));
        }

        if (FaceButtonControls)
        {
            // X = 左脚・Y = 右脚の蹴り（間合いで前蹴り・膝蹴り）。脚は追跡しないので、蹴りの経路を体が追う。手はコントローラを追ったまま。
            if (events.KickLeft)
            {
                _kick.Start(MiniRing.Hand.Left, sense);
            }
            else if (events.KickRight)
            {
                _kick.Start(MiniRing.Hand.Right, sense);
            }

            command = _kick.Apply(command, sense, dt);
        }

        return new BoxingInput(command);
    }

    /// <summary>
    /// ② 一人称の 1 歩: 変換を（必要なら）合わせ、スティックで基準点を動かし、頭の目標（HMD のワールドの位置）へ体を歩かせる指令と、頭の傾きを返す。
    /// </summary>
    private (Vector3 Move, Vector3 HeadOffset) FirstPersonMove(in VrRawFrame frame, in FighterSense sense, float headYaw, float scale,
        Vector3 headOffset, Vector3 stickMove, bool calibratedNow, float dt)
    {
        if (Placement is null || calibratedNow || _placeAgain)
        {
            // 身長の合わせ（A/X）・ワールドの作り直し: HMD の位置とヨーが、人形の頭と向きに重なるように。
            Placement = VrWorldPlacement.Align(frame.HeadPosition, headYaw, sense.SelfHead, sense.Self.YawRadians, scale);
            _placeAgain = false;
        }

        var placement = Placement!.Value;
        var reach = MathF.Max(1e-3f, sense.ArmReach);
        var target = placement.Point(frame.HeadPosition);
        var offset = target - sense.SelfHead;
        var flat = new Vector3(offset.X, 0f, offset.Z);

        // スティック: 基準点を歩く向き（VR の空間の向きをワールドへ回したもの）へ動かす。体の頭から離れすぎる向きへは動かさない。
        if (stickMove.LengthSquared() > 0f)
        {
            var direction = placement.Direction(stickMove) / scale; // 回すだけ（長さは変えない）
            var delta = direction * (WalkSpeed * StickWalkRatio * dt);
            var lead = flat.Length();
            if (lead > MaxLead * reach && Vector3.Dot(delta, flat) > 0f)
            {
                var away = flat / lead;
                delta -= away * Vector3.Dot(delta, away);
            }

            placement = placement.Moved(delta);
            Placement = placement;
            target = placement.Point(frame.HeadPosition);
            offset = target - sense.SelfHead;
            flat = new Vector3(offset.X, 0f, offset.Z);
        }

        // 頭の傾き: 水平は上限で止め（超えた分は歩いて追う）、高さ（ダッキング）はそのまま。
        var horizontal = new Vector2(headOffset.X, headOffset.Z);
        var limit = LeanLimit * reach;
        if (horizontal.Length() > limit)
        {
            horizontal = Vector2.Normalize(horizontal) * limit;
        }

        return (WalkToward(flat, reach), new Vector3(horizontal.X, headOffset.Y, horizontal.Y));
    }

    /// <summary>
    /// ② 頭の目標までの水平のずれ（ワールド [m]）→ 歩く指令（長さ 0..1）。ずれが <see cref="WalkDeadZone"/> × 腕の長さより小さい間は 0（傾けるだけ）、
    /// そこから <see cref="WalkFullRange"/> × 腕の長さだけ離れるまでに比例して 1（歩く速さ）になる。
    /// </summary>
    public static Vector3 WalkToward(Vector3 flatOffset, float armReach)
    {
        var length = new Vector2(flatOffset.X, flatOffset.Z).Length();
        var dead = WalkDeadZone * armReach;
        if (length <= dead || armReach <= 0f)
        {
            return Vector3.Zero;
        }

        var speed = Math.Clamp((length - dead) / (WalkFullRange * armReach), 0f, 1f);
        return new Vector3(flatOffset.X, 0f, flatOffset.Z) / length * speed;
    }

    private HandCommand HandOf(Hand hand, Vector3 target, Quaternion orientation, bool grab, bool letGo, in VrRawFrame frame)
    {
        if (!weaponMode)
        {
            return new HandCommand(target, grab);
        }

        if (GripHolds)
        {
            return new HandCommand(target, grab, Orientation: orientation, LetGo: letGo);
        }

        var holding = Held(_triggerHeld, (int)hand, frame.Trigger(hand), frame.Focused, out _);
        return new HandCommand(target, grab, Orientation: orientation, Slide: !holding);
    }

    // ヒステリシス付きの握り（半押しでチャタリングしない）。フォーカスを失ったら離す（iTVision の知見）。
    // released は「押していたのを今ちょうど離した」（フォーカスを失った離しは含めない: 勝手に武器を落とさない）。
    private static bool Held(bool[] state, int hand, float value, bool focused, out bool released)
    {
        var was = state[hand];
        state[hand] = focused && (was ? value > GripOff : value > GripOn);
        released = was && focused && !state[hand];
        return state[hand];
    }

    /// <summary>
    /// コントローラの向き（grip 姿勢 → ワールド）を、武器の向きの目標（武器のローカル → 体フレームの回転）にする。
    /// 武器のローカル X = コントローラの前（−Z）、Y = 上（+Y）、Z = 右（+X）。
    /// </summary>
    public static Quaternion WeaponOrientation(Quaternion controller, in BodyFrame shoulders)
    {
        var x = shoulders.InverseRotate(Vector3.Transform(-Vector3.UnitZ, controller));
        var y = shoulders.InverseRotate(Vector3.Transform(Vector3.UnitY, controller));
        var z = Vector3.Cross(x, y);
        return Quaternion.Normalize(Quaternion.CreateFromRotationMatrix(new Matrix4x4(x.X, x.Y, x.Z, 0f, y.X, y.Y, y.Z, 0f, z.X, z.Y, z.Z, 0f, 0f, 0f, 0f, 1f)));
    }
}
