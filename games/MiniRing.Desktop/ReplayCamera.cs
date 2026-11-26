using System.Numerics;
using Aigis.Player;
using Aigis.Render;
using MiniRing.Replay;

namespace MiniRing.Desktop;

/// <summary>リプレイのカメラの方式。</summary>
internal enum ReplayCameraMode
{
    /// <summary>回転中心のまわりを回る（中心は WASD・QE・中ドラッグで動かす）。</summary>
    Orbit,

    /// <summary>FPS の視点で自由に飛ぶ（WASD で前後左右、QE で上下、マウスで向き）。</summary>
    Free,
}

/// <summary>カメラが追う相手。</summary>
internal enum ReplayFollow
{
    None,
    Both,
    Red,
    Blue,
}

/// <summary>
/// リプレイの自由なカメラ（描画だけ。試合の計算・記録には入らない）。回転中心を回る方式と FPS の方式を切り替えられ、
/// 選手を追う・リングの定位置へ置くこともできる。カメラは表示の空間（部屋）にあり、速さは物理の m/s を <see cref="DisplaySpace"/> で移す。
/// <para>マウスの上下は「上へ動かすと視線が上を向く」にそろえる（回転中心の方式では、今までの試合のカメラと上下が逆。ユーザーの指定）。</para>
/// </summary>
internal sealed class ReplayCamera : IGameCamera
{
    /// <summary>マウス 1 px あたりの回転 [rad]。</summary>
    public const float RadiansPerPixel = 0.005f;

    /// <summary>飛ぶ速さ [物理の m/s]（人が歩くくらい）。Shift で 3 倍、Ctrl で 0.3 倍。</summary>
    public const float MoveSpeed = 2.5f;

    /// <summary>追う点へ寄る速さ [1/s]（減衰のバネ）。</summary>
    public const float FollowRate = 6f;

    private const float MinPitch = -1.5f;
    private const float MaxPitch = 1.5f;
    private const float MinDistance = 0.08f;
    private const float MaxDistance = 12f;
    private readonly float _scale = DisplaySpace.Standard.Scale;
    private Vector3? _lastFollowPoint;

    public ReplayCameraMode Mode { get; private set; } = ReplayCameraMode.Orbit;

    public ReplayFollow Follow { get; set; } = ReplayFollow.Both;

    /// <summary>マウスの上下を反転する（既定は「上へ動かすと視線が上」）。</summary>
    public bool InvertY { get; set; }

    /// <summary>回転中心（表示の空間）。</summary>
    public Vector3 Pivot { get; private set; }

    public float Distance { get; private set; } = 1.3f;

    /// <summary>向き: 回転中心の方式では中心から見た目の方位、FPS では視線の方位。</summary>
    public float Yaw { get; private set; }

    /// <summary>上下の角: 回転中心の方式では目の高さの角（正で上から見下ろす）、FPS では視線の仰角（正で上を見る）。</summary>
    public float Pitch { get; private set; } = 0.3f;

    /// <summary>FPS の目の位置（表示の空間）。</summary>
    public Vector3 Position { get; private set; }

    public Vector3 Eye => Mode == ReplayCameraMode.Orbit ? Pivot + (Distance * OrbitDirection(Yaw, Pitch)) : Position;

    /// <summary>視線の向き（単位）。</summary>
    public Vector3 Forward => Mode == ReplayCameraMode.Orbit ? Vector3.Normalize(Pivot - Eye) : LookDirection(Yaw, Pitch);

    public string Label => Mode switch
    {
        ReplayCameraMode.Orbit => "カメラ: 回転中心",
        _ => "カメラ: FPS",
    } + Follow switch
    {
        ReplayFollow.Both => "・両者を追う",
        ReplayFollow.Red => "・赤を追う",
        ReplayFollow.Blue => "・青を追う",
        _ => "",
    } + (InvertY ? "・上下反転" : "");

    public Matrix4x4 ViewProjection(float aspect)
        => Camera.ViewProjection(Eye, Eye + Forward, Vector3.UnitY, Camera.DefaultFovYRadians, aspect, 0.01f, 200f);

    // ホストのドラッグ・ホイールは使わない（ReplayControls が方式に合わせて読む）。
    public void ApplyDrag(Vector2 pixelDelta) { }

    public void ApplyScroll(float scrollDelta) { }

    /// <summary>リングの横から両者を見る、最初の視点。</summary>
    public void Reset(ReplayFocus focus)
    {
        var line = focus.BlueChest - focus.RedChest;
        line.Y = 0f;
        var side = line.LengthSquared() > 1e-8f ? MathF.Atan2(line.X, line.Z) + (MathF.PI * 0.5f) : 0f;
        Mode = ReplayCameraMode.Orbit;
        Follow = ReplayFollow.Both;
        Pivot = FollowPoint(focus) ?? focus.RingCenter;
        Yaw = side;
        Pitch = 0.32f;
        Distance = 1.3f;
        _lastFollowPoint = null;
    }

    /// <summary>リングの定位置（0 = 中継の横、1 = 真上、2 = 赤の後ろ、3 = 青の後ろ）。回転中心の方式に戻す。</summary>
    public void Preset(int index, ReplayFocus focus)
    {
        var toBlue = focus.BlueChest - focus.RedChest;
        toBlue.Y = 0f;
        var behindRed = toBlue.LengthSquared() > 1e-8f ? MathF.Atan2(-toBlue.X, -toBlue.Z) : 0f;
        SetMode(ReplayCameraMode.Orbit);
        switch (index)
        {
            case 0:
                Follow = ReplayFollow.None;
                Pivot = focus.RingCenter;
                Yaw = 0f;
                Pitch = 0.22f;
                Distance = 2.4f;
                break;
            case 1:
                Follow = ReplayFollow.None;
                Pivot = focus.RingCenter;
                Pitch = 1.45f;
                Distance = 2.6f;
                break;
            case 2:
                Follow = ReplayFollow.Both;
                Pivot = FollowPoint(focus) ?? focus.RingCenter;
                Yaw = behindRed;
                Pitch = 0.18f;
                Distance = 0.9f;
                break;
            default:
                Follow = ReplayFollow.Both;
                Pivot = FollowPoint(focus) ?? focus.RingCenter;
                Yaw = behindRed + MathF.PI;
                Pitch = 0.18f;
                Distance = 0.9f;
                break;
        }

        _lastFollowPoint = null;
    }

    /// <summary>方式を切り替える。見ている物が飛ばないよう、目の位置と視線をそのまま引き継ぐ。</summary>
    public void SetMode(ReplayCameraMode mode)
    {
        if (mode == Mode) { return; }
        var eye = Eye;
        var forward = Forward;
        if (mode == ReplayCameraMode.Free)
        {
            Position = eye;
            Yaw = MathF.Atan2(forward.X, forward.Z);
            Pitch = MathF.Asin(Math.Clamp(forward.Y, -1f, 1f));
            Follow = ReplayFollow.None; // 自由に飛ぶときは追わない（F で追うと、相手と一緒に動く）
        }
        else
        {
            Pivot = eye + (forward * Distance);
            Yaw = MathF.Atan2(-forward.X, -forward.Z);
            Pitch = MathF.Asin(Math.Clamp(-forward.Y, -1f, 1f));
        }

        Mode = mode;
        Pitch = Math.Clamp(Pitch, MinPitch, MaxPitch);
        _lastFollowPoint = null;
    }

    public void ToggleMode() => SetMode(Mode == ReplayCameraMode.Orbit ? ReplayCameraMode.Free : ReplayCameraMode.Orbit);

    /// <summary>追う相手を順に切り替える（なし → 両者 → 赤 → 青）。赤・青を選んだら寄る。</summary>
    public void CycleFollow()
    {
        Follow = Follow switch
        {
            ReplayFollow.None => ReplayFollow.Both,
            ReplayFollow.Both => ReplayFollow.Red,
            ReplayFollow.Red => ReplayFollow.Blue,
            _ => ReplayFollow.None,
        };
        if (Mode == ReplayCameraMode.Orbit && Follow is ReplayFollow.Red or ReplayFollow.Blue)
        {
            Distance = MathF.Min(Distance, 0.75f);
        }

        _lastFollowPoint = null;
    }

    /// <summary>マウスで回す。上へ動かすと視線が上を向く（<see cref="InvertY"/> で逆）。</summary>
    public void Rotate(Vector2 pixelDelta)
    {
        var dy = (InvertY ? -1f : 1f) * pixelDelta.Y * RadiansPerPixel;
        Yaw -= pixelDelta.X * RadiansPerPixel;
        // 回転中心: 目を下げると視線が上を向く。FPS: 仰角を上げる。
        Pitch = Math.Clamp(Mode == ReplayCameraMode.Orbit ? Pitch + dy : Pitch - dy, MinPitch, MaxPitch);
    }

    /// <summary>回転中心を画面の上で動かす（中ドラッグ）。追うのはやめる。</summary>
    public void Pan(Vector2 pixelDelta)
    {
        var forward = Forward;
        var right = Vector3.Normalize(Vector3.Cross(forward, Vector3.UnitY));
        var up = Vector3.Cross(right, forward);
        var perPixel = (Mode == ReplayCameraMode.Orbit ? Distance : 1f) * 0.0015f;
        var move = ((-pixelDelta.X * right) + (pixelDelta.Y * up)) * perPixel;
        if (Mode == ReplayCameraMode.Orbit) { Pivot += move; Follow = ReplayFollow.None; }
        else { Position += move; }
    }

    /// <summary>ホイール: 回転中心なら寄る・離れる、FPS なら前後に動く。</summary>
    public void Scroll(float notches)
    {
        if (notches == 0f) { return; }
        if (Mode == ReplayCameraMode.Orbit)
        {
            Distance = Math.Clamp(Distance * MathF.Pow(0.88f, notches), MinDistance, MaxDistance);
        }
        else
        {
            Position += Forward * (notches * 0.25f * _scale);
        }
    }

    /// <summary>
    /// キーで動かす。<paramref name="local"/> は (右, 上, 前) の向き（各 −1..1）。回転中心の方式では中心ごと水平に動かし（追うのはやめる）、
    /// FPS では視線の向きへ飛ぶ。上下（QE）はどちらも鉛直。
    /// </summary>
    public void Move(Vector3 local, float seconds, float speedFactor)
    {
        if (local == Vector3.Zero || !(seconds > 0f)) { return; }
        var step = MoveSpeed * _scale * speedFactor * seconds * (Mode == ReplayCameraMode.Orbit ? MathF.Max(0.4f, Distance / 1.3f) : 1f);
        var forward = Forward;
        if (Mode == ReplayCameraMode.Orbit)
        {
            forward.Y = 0f;
            forward = forward.LengthSquared() > 1e-8f ? Vector3.Normalize(forward) : Vector3.UnitZ;
        }

        var right = Vector3.Normalize(Vector3.Cross(forward, Vector3.UnitY));
        var move = ((local.X * right) + (local.Z * forward) + (local.Y * Vector3.UnitY)) * step;
        if (Mode == ReplayCameraMode.Orbit)
        {
            Pivot += move;
            Follow = ReplayFollow.None;
        }
        else
        {
            Position += move;
        }
    }

    /// <summary>追う点へ寄せる（毎フレーム）。回転中心は減衰のバネで点へ、FPS は点が動いた分だけ一緒に動く。</summary>
    public void Update(ReplayFocus focus, float seconds)
    {
        if (FollowPoint(focus) is not { } point)
        {
            _lastFollowPoint = null;
            return;
        }

        if (Mode == ReplayCameraMode.Orbit)
        {
            Pivot = CameraFollow.DampTarget(Pivot, point, FollowRate, seconds);
        }
        else if (_lastFollowPoint is { } last)
        {
            Position += point - last;
        }

        _lastFollowPoint = point;
    }

    private Vector3? FollowPoint(ReplayFocus focus) => Follow switch
    {
        ReplayFollow.Both => (focus.RedChest + focus.BlueChest) * 0.5f,
        ReplayFollow.Red => focus.RedChest,
        ReplayFollow.Blue => focus.BlueChest,
        _ => null,
    };

    /// <summary>回転中心から目への向き（yaw = pitch = 0 で +Z、pitch 正で上）。</summary>
    private static Vector3 OrbitDirection(float yaw, float pitch)
        => new(MathF.Cos(pitch) * MathF.Sin(yaw), MathF.Sin(pitch), MathF.Cos(pitch) * MathF.Cos(yaw));

    /// <summary>FPS の視線（yaw = 0 で +Z、pitch 正で上を見る）。</summary>
    private static Vector3 LookDirection(float yaw, float pitch)
        => new(MathF.Cos(pitch) * MathF.Sin(yaw), MathF.Sin(pitch), MathF.Cos(pitch) * MathF.Cos(yaw));
}
