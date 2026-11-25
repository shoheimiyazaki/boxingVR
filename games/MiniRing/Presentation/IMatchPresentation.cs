using System.Numerics;
using Aigis.Player;
using Aigis.Render;

namespace MiniRing.Presentation;

/// <summary>デスクトップのホストが試合・リプレイの画面に求める物: 入力の受け渡し、顔の窓などの追加の視点、後片付け。</summary>
public interface IMatchPresentation : IPresentationGame<BoxingInput>, IDisposable
{
    /// <summary>主の視点に重ねる視点（顔の窓）。無ければ null。</summary>
    public IReadOnlyList<CameraView>? Views(IGameCamera main, Vector2 size);
}
