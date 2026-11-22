using System.Numerics;

namespace MiniRing.Arena;

/// <summary>
/// 疲れ方を見るための操作: その場で、弱・強を交互に切れ目なく打ち続ける（空振りでよい。相手は構えて立つだけの的）。
/// 打つ間隔は連打のテスト（<c>StaminaRushTests</c>）と同じ 0.2014 秒、押している割合は 0.67。入力の側だけで、強さは体から出る。
/// </summary>
internal sealed class RushControl : IFighterControl
{
    private readonly IntentControl _inner = new();
    private float _clock;

    public FighterCommand Read(in FighterSense sense, float dt)
    {
        _clock += dt;
        var phase = _clock / 0.2014f;
        var index = (int)MathF.Floor(phase);
        var down = phase - index < 0.67f;
        _inner.Input = new FighterIntent(Vector2.Zero, Light: down && index % 2 == 0, Heavy: down && index % 2 == 1);
        return _inner.Read(sense, dt);
    }
}
