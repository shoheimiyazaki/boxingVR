using System.Numerics;
using Aigis.Platform;
using Silk.NET.Input;

namespace MiniRing.Desktop;

/// <summary>キーを意図へ写すだけのホスト側。経路の生成はゲーム本体へ委ねる。</summary>
internal sealed class IntentKeyboardControl
{
    private IntentControl _control = new();
    private int? _round;

    public BoxingInput Read(InputSnapshot input, FighterSense sense, float dt, int round)
    {
        var reset = input.WasKeyPressed(Key.R);
        if (reset || _round != round)
        {
            _control = new IntentControl();
            _round = round;
        }

        static float Axis(bool positive, bool negative) => (positive ? 1f : 0f) - (negative ? 1f : 0f);
        var direction = new Vector2(
            Axis(input.IsKeyDown(Key.Right) || input.IsKeyDown(Key.D), input.IsKeyDown(Key.Left) || input.IsKeyDown(Key.A)),
            Axis(input.IsKeyDown(Key.Up) || input.IsKeyDown(Key.W), input.IsKeyDown(Key.Down) || input.IsKeyDown(Key.S)));
        _control.Input = new FighterIntent(direction,
            input.IsKeyDown(Key.J) || input.IsMouseButtonDown(MouseButton.Right),
            input.IsKeyDown(Key.K) || input.IsMouseButtonDown(MouseButton.Left), input.IsKeyDown(Key.Space));
        return new BoxingInput(_control.Read(sense, dt), reset);
    }
}
