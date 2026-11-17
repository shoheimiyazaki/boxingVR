using System.Numerics;

namespace MiniRing.Motion;

/// <summary>
/// VR の姿勢・ボタン → プレイヤーのファイター指令（ホスト非依存。<c>MiniRing.Vr.VrPlayerControl</c> は OpenXR の入力をこれへ渡すだけ）。
/// 左スティック: 歩き（視線基準） ／ 腕: コントローラ位置を肩基準で腕の長さ比に縮め、構えからの動きを <see cref="MotionGain"/> 倍して人形の体へ ／
/// グリップ（squeeze）: 掴む（クリンチ） ／ 向きは常に相手へロックオン。
/// <para>
/// <b>武器の持ち方</b>（<c>weaponMode</c>、2026-11-17）: トリガーを引いている手は握る、離している手は握った武器を手の中で滑らせる
/// （<see cref="HandCommand.Slide"/>）。武器の向きの目標（<see cref="HandCommand.Orientation"/>）は、コントローラの向きを体フレームへ写したもの
/// （武器のローカル X = コントローラの前 = grip 姿勢の −Z、ローカル Y = コントローラの上、ローカル Z = コントローラの右）。武器の無い試合では
/// どちらも出さず、今まで通り（Orientation null・Slide false）。
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

    private const float BodyYawSmoothing = 4f; // [1/s] 頭を振っても腕の写像が暴れないよう体の向きは緩く追う
    private const float StanceSmoothing = 0.8f; // [1/s] 構えの位置（頭の平均位置）の追従。これより速い頭の動き＝スリップ・ダッキング

    private readonly bool[] _gripHeld = new bool[2];
    private readonly bool[] _triggerHeld = new bool[2];
    private readonly Vector3[] _lastHandLocal = new Vector3[2];
    private readonly Quaternion[] _lastOrientation = [Quaternion.Identity, Quaternion.Identity];
    private float _bodyYaw;
    private Vector3 _stanceHead;
    private bool _calibrated;

    /// <summary>身長のキャリブレーション値 [m]。</summary>
    public float PlayerHeight { get; private set; } = 1.7f;

    /// <summary>プレイヤーの腕の長さの見積もり [m]（肩から拳まで。<see cref="PuppetMapping.ArmReachFromHeight"/>）。</summary>
    public float PlayerArmReach => PuppetMapping.ArmReachFromHeight(PlayerHeight);

    /// <summary>直近のフレームの視線のヨー [rad]。</summary>
    public float HeadYaw { get; private set; }

    /// <summary>武器を持つ試合か。</summary>
    public bool WeaponMode => weaponMode;

    /// <summary>キャリブレーションしたとき（目の高さ [m]、身長 [m]）。ホストが表示に使う。</summary>
    public Action<float, float>? OnCalibrated { get; set; }

    /// <summary>その手のトリガーを引いているか（武器を握っている）。</summary>
    public bool TriggerHeld(Hand hand) => _triggerHeld[(int)hand];

    public BoxingInput Read(in VrRawFrame frame, in FighterSense sense, float dt)
    {
        // カメラ（頭）はローカル −Z を向く。
        var headYaw = BodyFrame.YawOf(Vector3.Transform(-Vector3.UnitZ, frame.HeadRotation), _bodyYaw);
        HeadYaw = headYaw;
        if ((!_calibrated && frame.HeadPosition.Y > 0.8f) || frame.CalibratePressed)
        {
            PlayerHeight = PuppetMapping.PlayerHeightFromEye(frame.HeadPosition.Y);
            _bodyYaw = headYaw;
            _stanceHead = frame.HeadPosition;
            _calibrated = true;
            OnCalibrated?.Invoke(frame.HeadPosition.Y, PlayerHeight);
        }

        _bodyYaw = PuppetMapping.SmoothYaw(_bodyYaw, headYaw, BodyYawSmoothing, dt);
        // 肩は「構えの頭位置」から推定する（頭だけ振っても腕の写像は動かない）。頭のずれは体の軸の傾きとして別に渡す。
        _stanceHead = Vector3.Lerp(_stanceHead, frame.HeadPosition, 1f - MathF.Exp(-StanceSmoothing * dt));
        var shoulders = PuppetMapping.PlayerShoulderFrame(_stanceHead, _bodyYaw, PlayerHeight);
        var scale = PuppetMapping.Scale(sense.ArmReach, PlayerHeight);
        var move = PuppetMapping.StickToWorld(frame.LeftStick, headYaw);

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

        var command = new FighterCommand(move, sense.OpponentChest - sense.Self.Origin,
            HandOf(MiniRing.Hand.Left, frame),
            HandOf(MiniRing.Hand.Right, frame),
            PuppetMapping.HeadOffsetLocal(frame.HeadPosition, _stanceHead, _bodyYaw, scale));
        return new BoxingInput(command);
    }

    private HandCommand HandOf(Hand hand, in VrRawFrame frame)
    {
        var i = (int)hand;
        var grab = Held(_gripHeld, i, frame.Grip(hand), frame.Focused);
        if (!weaponMode)
        {
            return new HandCommand(_lastHandLocal[i], grab);
        }

        var holding = Held(_triggerHeld, i, frame.Trigger(hand), frame.Focused);
        return new HandCommand(_lastHandLocal[i], grab, Orientation: _lastOrientation[i], Slide: !holding);
    }

    // ヒステリシス付きの握り（半押しでチャタリングしない）。フォーカスを失ったら離す（iTVision の知見）。
    private static bool Held(bool[] state, int hand, float value, bool focused)
    {
        state[hand] = focused && (state[hand] ? value > GripOff : value > GripOn);
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
