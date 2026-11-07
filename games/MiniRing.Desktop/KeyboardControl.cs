using System.Numerics;
using Aigis.Platform;
using Silk.NET.Input;

namespace MiniRing.Desktop;

/// <summary>
/// HMD なしで遊ぶ/確かめるためのキーボード＋マウス操作（iTVision の WASD フォールバックに相当）。
/// <list type="bullet">
/// <item>WASD: 移動（カメラ基準） ／ 矢印キー: 頭を振る（←→ スリップ・↑ 前傾・↓ ダッキング）</item>
/// <item>左クリック: 右手 ／ 右クリック: 左手。拳は相手の頭へ向かう。</item>
/// <item>Ctrl を押しながら: 横から回す経路 ／ Shift: 下から突き上げる経路 ／ Alt: ボディを狙う</item>
/// <item>Q・E: 左・右で掴む（押している間） ／ R: ラウンドやり直し</item>
/// </list>
/// 修飾キーが変えるのは<b>拳の経路だけ</b>。フックやアッパーとして強いかどうかは、その経路と体の向きから物理で決まる
/// （<see cref="PunchMechanics"/>）。向きは常に相手へロックオン。
/// </summary>
internal sealed class KeyboardControl
{
    private readonly float[] _extend = new float[2];
    private Vector3 _head;

    public BoxingInput Read(InputSnapshot input, float cameraYaw, FighterSense sense, float dt)
    {
        var stick = Vector2.Zero;
        stick.Y += input.IsKeyDown(Key.W) ? 1f : 0f;
        stick.Y -= input.IsKeyDown(Key.S) ? 1f : 0f;
        stick.X += input.IsKeyDown(Key.D) ? 1f : 0f;
        stick.X -= input.IsKeyDown(Key.A) ? 1f : 0f;

        // OrbitCamera の視線方向は −(sin yaw, ·, cos yaw)、つまりヨー yaw+π。キーを VR のスティックと同じ規約で扱う。
        var move = stick == Vector2.Zero ? Vector3.Zero : PuppetMapping.StickToWorld(Vector2.Normalize(stick), cameraYaw + MathF.PI, deadZone: 0f);

        var reach = sense.ArmReach;
        // 頭: 右矢印で体の右（ローカル −X）へスリップ。
        var headTarget = new Vector3(
            (input.IsKeyDown(Key.Left) ? 0.35f : 0f) - (input.IsKeyDown(Key.Right) ? 0.35f : 0f),
            input.IsKeyDown(Key.Down) ? -0.4f : 0f,
            input.IsKeyDown(Key.Up) ? 0.3f : 0f) * reach;
        _head = Vector3.Lerp(_head, headTarget, 1f - MathF.Exp(-12f * dt));

        var aim = sense.Self.WorldToLocal(input.IsKeyDown(Key.AltLeft) ? sense.OpponentChest : sense.OpponentHead);
        var lateral = input.IsKeyDown(Key.ControlLeft) ? 1f : 0f;
        var low = input.IsKeyDown(Key.ShiftLeft) ? 1f : 0f;
        var left = Arm(Hand.Left, input.IsMouseButtonDown(MouseButton.Right), aim, lateral, low, reach, dt);
        var right = Arm(Hand.Right, input.IsMouseButtonDown(MouseButton.Left), aim, lateral, low, reach, dt);
        var command = new FighterCommand(move, sense.OpponentChest - sense.Self.Origin,
            new HandCommand(left, input.IsKeyDown(Key.Q)),
            new HandCommand(right, input.IsKeyDown(Key.E)),
            _head);
        return new BoxingInput(command, input.WasKeyPressed(Key.R));
    }

    /// <summary>構え → 溜め（横 / 下）→ 狙いの奥、を 2 次ベジェで結ぶ。押している間だけ進み、離すと構えへ戻る。</summary>
    private Vector3 Arm(Hand hand, bool punching, Vector3 aim, float lateral, float low, float reach, float dt)
    {
        var i = (int)hand;
        // 出すときは速く（≈0.12s）、戻すときはゆっくり（≈0.2s）。
        _extend[i] = Math.Clamp(_extend[i] + (dt * (punching ? 8f : -5f)), 0f, 1f);
        var t = _extend[i];
        return PunchPath.Point(AiControl.Guard(hand, reach), PunchPath.Windup(hand, lateral, low, reach), PunchPath.Through(aim, 0.3f, reach), t);
    }
}
