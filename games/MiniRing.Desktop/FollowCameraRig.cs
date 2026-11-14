using System.Diagnostics;
using System.Numerics;
using Aigis.Platform;
using Aigis.Player;
using Silk.NET.Input;

namespace MiniRing.Desktop;

/// <summary>
/// 軌道カメラを、プレイヤーの後ろへ減衰のバネでゆっくり追従させる（描画だけ。試合の計算・記録には入らない）。
/// 中ボタンで回した分は、そのまま「後ろからのずれ」として保つ（離しても元の後ろへは戻さない）。
/// </summary>
internal sealed class FollowCameraRig(OrbitCamera camera, float focusHeight)
{
    private const float RadiansPerPixel = 0.005f; // OrbitCamera.ApplyDrag の既定
    private long _last = Stopwatch.GetTimestamp();
    private float _userOffset;
    private bool _placed;

    public void Update(InputSnapshot input, FighterSense sense)
    {
        var now = Stopwatch.GetTimestamp();
        var dt = Math.Clamp((float)Stopwatch.GetElapsedTime(_last, now).TotalSeconds, 0f, 0.1f);
        _last = now;
        var player = sense.Self.Origin;
        var opponent = sense.OpponentChest;
        var focus = CameraFollow.Focus(player, opponent, focusHeight);
        var behind = CameraFollow.BehindYaw(player, opponent, camera.YawRadians);
        if (!_placed)
        {
            _placed = true;
            _userOffset = MathF.IEEERemainder(camera.YawRadians - behind, 2f * MathF.PI);
            camera.Target = focus;
            return;
        }

        camera.Target = CameraFollow.DampTarget(camera.Target, focus, CameraFollow.DefaultRate, dt);
        if (input.IsMouseButtonDown(MouseButton.Middle))
        {
            _userOffset = MathF.IEEERemainder(camera.YawRadians - behind, 2f * MathF.PI);
            return;
        }

        var next = CameraFollow.DampYaw(camera.YawRadians, behind + _userOffset, CameraFollow.DefaultRate, dt);
        camera.ApplyDrag(new Vector2(-(next - camera.YawRadians) / RadiansPerPixel, 0f), RadiansPerPixel);
    }
}
