using System.Numerics;

namespace MiniRing;

/// <summary>
/// 追従カメラの純粋な計算（描画の側だけで使う。試合の計算には入らないので、決定性・記録には響かない）。
/// カメラはプレイヤーの後ろ（相手とプレイヤーを結ぶ線の外側）から、位置・向きとも減衰するバネでゆっくり追う。
/// </summary>
public static class CameraFollow
{
    /// <summary>追従の速さ [1/s]。大きいほど素早く追う（時定数 1/rate 秒）。ゆるく追う値。</summary>
    public const float DefaultRate = 3.2f;

    /// <summary>注視点を相手の側へ寄せる割合（0 = プレイヤー、1 = 相手）。二人が画面に入る。</summary>
    public const float OpponentShare = 0.3f;

    /// <summary>
    /// カメラ基準のキー（X = カメラの右、Y = カメラの奥）を、ワールドの水平面の方向（X = ワールド X、Y = ワールド Z）へ直す。
    /// <paramref name="yawRadians"/> は <c>OrbitCamera.YawRadians</c>（目から注視点への向きが +Z から回る角）。長さは 1 以下に保つ。
    /// </summary>
    public static Vector2 ToWorld(Vector2 keys, float yawRadians)
    {
        // カメラの奥（視線の水平成分）= −(sin yaw, cos yaw)。右 = 奥 × 上 = (cos yaw, −sin yaw)。
        var forward = new Vector2(-MathF.Sin(yawRadians), -MathF.Cos(yawRadians));
        var right = new Vector2(MathF.Cos(yawRadians), -MathF.Sin(yawRadians));
        var world = (right * keys.X) + (forward * keys.Y);
        return world.LengthSquared() > 1f ? Vector2.Normalize(world) : world;
    }

    /// <summary>プレイヤーの真後ろ（相手から見てプレイヤーの向こう側）にカメラを置く向き [rad]。二人が重なるときは <paramref name="fallback"/>。</summary>
    public static float BehindYaw(Vector3 player, Vector3 opponent, float fallback)
    {
        var away = new Vector2(player.X - opponent.X, player.Z - opponent.Z);
        return away.LengthSquared() < 1e-6f ? fallback : MathF.Atan2(away.X, away.Y);
    }

    /// <summary>減衰のバネ（指数的に近づく。フレーム間隔に依らず同じ軌跡）。</summary>
    public static float Blend(float rate, float dt) => 1f - MathF.Exp(-MathF.Max(0f, rate) * MathF.Max(0f, dt));

    /// <summary>向きを最短で寄せる。</summary>
    public static float DampYaw(float current, float desired, float rate, float dt)
    {
        var delta = MathF.IEEERemainder(desired - current, 2f * MathF.PI);
        return current + (delta * Blend(rate, dt));
    }

    public static Vector3 DampTarget(Vector3 current, Vector3 desired, float rate, float dt)
        => Vector3.Lerp(current, desired, Blend(rate, dt));

    /// <summary>注視点: プレイヤーから相手の側へ少し寄せた点（高さは固定）。</summary>
    public static Vector3 Focus(Vector3 player, Vector3 opponent, float height)
    {
        var p = Vector3.Lerp(player, opponent, OpponentShare);
        return new Vector3(p.X, height, p.Z);
    }
}
