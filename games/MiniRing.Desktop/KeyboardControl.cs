using System.Numerics;
using Aigis.Platform;
using Silk.NET.Input;

namespace MiniRing.Desktop;

/// <summary>
/// HMD なしで遊ぶ/確かめるためのキーボード＋マウス操作（iTVision の WASD フォールバックに相当）。
/// WASD: 移動（カメラ基準） ／ 左クリック: 右ストレート ／ 右クリック: 左ジャブ ／
/// Shift+クリック: アッパー ／ Q・E: 左・右で掴む（押している間） ／ R: ラウンドやり直し。
/// 向きは常に相手へロックオン。
/// </summary>
internal sealed class KeyboardControl
{
    private readonly float[] _extend = new float[2];

    public BoxingInput Read(InputSnapshot input, float cameraYaw, FighterSense sense, float dt)
    {
        var stick = Vector2.Zero;
        if (input.IsKeyDown(Key.W))
        {
            stick.Y += 1f;
        }

        if (input.IsKeyDown(Key.S))
        {
            stick.Y -= 1f;
        }

        if (input.IsKeyDown(Key.D))
        {
            stick.X += 1f;
        }

        if (input.IsKeyDown(Key.A))
        {
            stick.X -= 1f;
        }

        // OrbitCamera の視線方向は −(sin yaw, ·, cos yaw)、つまりヨー yaw+π。キーを VR のスティックと同じ規約で扱う。
        var move = stick == Vector2.Zero ? Vector3.Zero : PuppetMapping.StickToWorld(Vector2.Normalize(stick), cameraYaw + MathF.PI, deadZone: 0f);
        var uppercut = input.IsKeyDown(Key.ShiftLeft);
        var left = Arm(Hand.Left, input.IsMouseButtonDown(MouseButton.Right), uppercut, sense.ArmReach, dt);
        var right = Arm(Hand.Right, input.IsMouseButtonDown(MouseButton.Left), uppercut, sense.ArmReach, dt);
        var command = new FighterCommand(move, sense.OpponentChest - sense.Self.Origin,
            new HandCommand(left, input.IsKeyDown(Key.Q)),
            new HandCommand(right, input.IsKeyDown(Key.E)));
        return new BoxingInput(command, input.WasKeyPressed(Key.R));
    }

    private Vector3 Arm(Hand hand, bool punching, bool uppercut, float reach, float dt)
    {
        var i = (int)hand;
        // 出すときは速く（≈0.08s）、戻すときはゆっくり（≈0.2s）。
        _extend[i] = Math.Clamp(_extend[i] + (dt * (punching ? 12f : -5f)), 0f, 1f);
        var side = hand.LocalSide();
        var punch = uppercut ? new Vector3(side * 0.05f, 0.35f, 0.7f) * reach : new Vector3(side * 0.05f, 0.05f, 1.0f) * reach;
        return Vector3.Lerp(AiControl.Guard(hand, reach), punch, _extend[i]);
    }
}
