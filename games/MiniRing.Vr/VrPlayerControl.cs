using System.Numerics;

namespace MiniRing.Vr;

/// <summary>
/// VR の姿勢・ボタン → プレイヤーのファイター指令。
/// 左スティック: 歩き（視線基準） ／ 腕: コントローラ位置を肩基準で腕の長さ比に縮めて人形の体へ ／
/// グリップ: 掴む ／ A・X: 身長キャリブレーション（まっすぐ立って押す。初回は自動）。
/// 向きは常に相手へロックオン（観戦視点から自分の体を回す操作は直感的でないため）。
/// </summary>
internal sealed class VrPlayerControl
{
    private const float GripOn = 0.55f;
    private const float GripOff = 0.35f;
    private const float BodyYawSmoothing = 4f; // [1/s] 頭を振っても腕の写像が暴れないよう体の向きは緩く追う
    private const float StanceSmoothing = 0.8f; // [1/s] 構えの位置（頭の平均位置）の追従。これより速い頭の動き＝スリップ・ダッキング

    private readonly bool[] _gripHeld = new bool[2];
    private readonly Vector3[] _lastHandLocal = new Vector3[2];
    private float _bodyYaw;
    private Vector3 _stanceHead;
    private bool _calibrated;

    public float PlayerHeight { get; private set; } = 1.7f;

    public BoxingInput Read(in XrInputFrame frame, in FighterSense sense, float dt)
    {
        // カメラ（頭）はローカル −Z を向く。
        var headYaw = BodyFrame.YawOf(Vector3.Transform(-Vector3.UnitZ, frame.HeadOrientation), _bodyYaw);
        if ((!_calibrated && frame.HeadPosition.Y > 0.8f) || frame.CalibratePressed)
        {
            PlayerHeight = PuppetMapping.PlayerHeightFromEye(frame.HeadPosition.Y);
            _bodyYaw = headYaw;
            _stanceHead = frame.HeadPosition;
            _calibrated = true;
            Console.WriteLine($"[vr] calibrated: eye {frame.HeadPosition.Y:0.00} m -> height {PlayerHeight:0.00} m");
        }

        _bodyYaw = PuppetMapping.SmoothYaw(_bodyYaw, headYaw, BodyYawSmoothing, dt);
        // 肩は「構えの頭位置」から推定する（頭だけ振っても腕の写像は動かない）。頭のずれは体の軸の傾きとして別に渡す。
        _stanceHead = Vector3.Lerp(_stanceHead, frame.HeadPosition, 1f - MathF.Exp(-StanceSmoothing * dt));
        var shoulders = PuppetMapping.PlayerShoulderFrame(_stanceHead, _bodyYaw, PlayerHeight);
        var scale = PuppetMapping.Scale(sense.ArmReach, PlayerHeight);
        var move = PuppetMapping.StickToWorld(frame.LeftStick, headYaw);

        // トラッキングが切れた手は最後の目標に留める（急に体の中心へ吸い込まれないように）。
        if (frame.LeftTracked)
        {
            _lastHandLocal[0] = PuppetMapping.HandTargetLocal(frame.LeftHand, shoulders, scale);
        }

        if (frame.RightTracked)
        {
            _lastHandLocal[1] = PuppetMapping.HandTargetLocal(frame.RightHand, shoulders, scale);
        }

        var command = new FighterCommand(move, sense.OpponentChest - sense.Self.Origin,
            new HandCommand(_lastHandLocal[0], Grip(0, frame.LeftGrip, frame.Focused)),
            new HandCommand(_lastHandLocal[1], Grip(1, frame.RightGrip, frame.Focused)),
            PuppetMapping.HeadOffsetLocal(frame.HeadPosition, _stanceHead, _bodyYaw, scale));
        return new BoxingInput(command);
    }

    // ヒステリシス付きの握り（半押しでチャタリングしない）。フォーカスを失ったら離す（iTVision の知見）。
    private bool Grip(int hand, float value, bool focused)
    {
        _gripHeld[hand] = focused && (_gripHeld[hand] ? value > GripOff : value > GripOn);
        return _gripHeld[hand];
    }
}
