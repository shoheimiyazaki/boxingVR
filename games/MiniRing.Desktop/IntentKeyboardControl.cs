using System.Numerics;
using Aigis.Platform;
using Silk.NET.Input;
using MiniRing.Weapons;

namespace MiniRing.Desktop;

/// <summary>キーを意図へ写すだけのホスト側。経路の生成はゲーム本体へ委ねる。</summary>
internal sealed class IntentKeyboardControl(WeaponKind? weapon = null, bool karate = false, MiniRing.Bots.BotStyle? style = null)
{
    private DesktopFighterControl _control = new(weapon, karate, style);
    private int? _round;
    // cameraYaw はカメラの向き（OrbitCamera.YawRadians）。矢印・WASD はカメラ基準で、入力の段階でワールドの方向へ直す。
    public BoxingInput Read(InputSnapshot input, FighterSense sense, float dt, int round, float cameraYaw)
    {
        // ラウンドのやり直しはキーボードの R だけ。コントローラの Start / Back では押し間違えで試合が戻っていた（ユーザーの指定）。
        var reset = input.WasKeyPressed(Key.R);
        if (reset || _round != round)
        {
            _control = new DesktopFighterControl(weapon, karate, style);
            _round = round;
        }

        static float Axis(bool positive, bool negative) => (positive ? 1f : 0f) - (negative ? 1f : 0f);
        var keys = new Vector2(
            Axis(input.IsKeyDown(Key.Right) || input.IsKeyDown(Key.D), input.IsKeyDown(Key.Left) || input.IsKeyDown(Key.A)),
            Axis(input.IsKeyDown(Key.Up) || input.IsKeyDown(Key.W), input.IsKeyDown(Key.Down) || input.IsKeyDown(Key.S)));
        var pad = GamepadIntent.Map(input.GamepadLeftStick,
            input.IsGamepadButtonDown(GamepadButton.West), input.IsGamepadButtonDown(GamepadButton.North),
            input.IsGamepadButtonDown(GamepadButton.South), cameraYaw);
        var movement = CameraFollow.ToWorld(keys, cameraYaw) + pad.World!.Value;
        if (movement.LengthSquared() > 1f)
        {
            movement = Vector2.Normalize(movement);
        }
        var intent = new FighterIntent(Vector2.Zero,
            input.IsKeyDown(Key.J) || input.IsMouseButtonDown(MouseButton.Right) || pad.Light,
            input.IsKeyDown(Key.K) || input.IsMouseButtonDown(MouseButton.Left) || pad.Heavy,
            input.IsKeyDown(Key.Space) || pad.Evade, movement,
            Step: input.IsKeyDown(Key.L) || input.IsKeyDown(Key.ShiftLeft) || input.IsGamepadButtonDown(GamepadButton.East));
        return new BoxingInput(_control.Read(intent, sense, dt), reset);
    }
}
