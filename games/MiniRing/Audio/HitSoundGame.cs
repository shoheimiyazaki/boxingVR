using System.Numerics;
using Aigis.Physics;
using Aigis.Player;
using Aigis.Render;

namespace MiniRing.Audio;

/// <summary>
/// Aigis の <see cref="GameHost"/> 用: 別の試合（普段の試合・記録中の試合・再生）を包み、
/// 1 ステップごとにその間の打撃音を鳴らす。試合そのもの（物理・記録）には触れない。
/// 包んだ試合は呼び出し側のもの（ここでは破棄しない）。破棄するのは音だけ。
/// </summary>
public sealed class HitSoundGame : IGame<BoxingInput>
{
    private readonly IGame<BoxingInput> _inner;
    private readonly Func<IReadOnlyList<HitEvent>> _hits;
    private readonly HitSoundPlayer _sounds;

    /// <param name="inner">包む試合。</param>
    /// <param name="hits">直前の <see cref="Step"/> で起きた打撃。</param>
    /// <param name="sounds">鳴らす先。これはこのオブジェクトと一緒に破棄する。</param>
    public HitSoundGame(IGame<BoxingInput> inner, Func<IReadOnlyList<HitEvent>> hits, HitSoundPlayer sounds)
    {
        ArgumentNullException.ThrowIfNull(inner);
        ArgumentNullException.ThrowIfNull(hits);
        ArgumentNullException.ThrowIfNull(sounds);
        _inner = inner;
        _hits = hits;
        _sounds = sounds;
    }

    public PhysicsWorld World => _inner.World;
    public string StatusLine => _inner.StatusLine;
    public bool IsOver => _inner.IsOver;

    public void Step(BoxingInput input, float dt)
    {
        _inner.Step(input, dt);
        _sounds.PlayAll(_hits());
    }

    public (IReadOnlyList<PrimitiveInstance> Instances, IReadOnlyList<DebugLine> Lines) BuildRenderables() => _inner.BuildRenderables();

    public IReadOnlyList<ScreenTextDraw> BuildOverlayText(Matrix4x4 viewProjection, Vector2 screenSizePixels)
        => _inner.BuildOverlayText(viewProjection, screenSizePixels);

    public void Dispose() => _sounds.Dispose();
}
