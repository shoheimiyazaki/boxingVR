using System.Numerics;

namespace MiniRing.Desktop;

/// <summary>生のゲームパッド値をキーボードと同じ意図へ変換する。</summary>
internal static class GamepadIntent
{
    public const float DeadZone = 0.1f;
    public const float CameraRadiansPerSecond = 2f;

    // GLFW のスティックは Y が下向き。デッドゾーン外では入力の大きさを維持する。
    public static Vector2 Stick(Vector2 raw)
    {
        var length = raw.Length();
        if (!float.IsFinite(length) || length <= DeadZone)
        {
            return Vector2.Zero;
        }
        return length > 1f ? raw / length : raw;
    }

    public static FighterIntent Map(Vector2 raw, bool light, bool heavy, bool evade, float yaw)
    {
        var stick = Stick(raw);
        return new FighterIntent(Vector2.Zero, light, heavy, evade,
            CameraFollow.ToWorld(new Vector2(stick.X, -stick.Y), yaw));
    }
}
