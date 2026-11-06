using System.Numerics;
using Aigis.Render;

namespace Aigis.Player;

/// <summary>
/// Optional game-to-host bridge for work a game does OFF the main thread while its window must keep
/// responding — Triage's stage start, which builds a whole world (2026-10-02,
/// 「ステージ開始にすごく時間がかかる…フリーズしたときの白いやつになる」).
/// <para>
/// <b>What the host does while <see cref="IsLoading"/> is true.</b> It keeps pumping window messages
/// and calling <c>Step</c> (that is how the game notices the work has finished), but it stops asking
/// the game to describe a frame: it re-presents the LAST frame it drew, with whatever
/// <see cref="BuildLoadingOverlayShapes"/>/<see cref="BuildLoadingOverlayText"/> return laid on top,
/// and it takes no spawn portrait and no F9 capture. That is the point of the contract rather than an
/// economy: the game's world-describing code is then never run on the main thread while a worker is
/// building the next world, so state the two might share (a lazily-filled static cache, a reused
/// scratch list) is only ever touched by one thread at a time.
/// </para>
/// <para>
/// <b>The re-presented frame is the previous frame's lists, held by reference.</b> A game that hands
/// out reused buffers (Triage's <c>BuildRenderables</c>) must therefore not refill them while loading
/// — which it cannot, since nothing asks it to.
/// </para>
/// </summary>
public interface IBackgroundLoadingGame
{
    /// <summary>True while the game is building something on another thread. Read every frame.</summary>
    public bool IsLoading { get; }

    /// <summary>Panels drawn over the held frame while loading, in framebuffer pixels. Empty is legal
    /// (a short load the game does not want to flash a screen for).</summary>
    public IReadOnlyList<UiShapeDraw> BuildLoadingOverlayShapes(Vector2 screenSizePixels);

    /// <summary>Text drawn over <see cref="BuildLoadingOverlayShapes"/>, in framebuffer pixels.</summary>
    public IReadOnlyList<ScreenTextDraw> BuildLoadingOverlayText(Vector2 screenSizePixels);
}
