using System.Numerics;

namespace Aigis.Player;

/// <summary>
/// The minimal camera surface <see cref="GameHost"/> needs: something that can turn a live
/// window aspect ratio into a view-projection matrix, and that can (optionally) react to mouse
/// drag/scroll. Introduced (TASK-053) so <see cref="GameHost.Options"/> can host cameras other
/// than <see cref="OrbitCamera"/> — specifically Triage's static, one-shot stage-fit camera
/// (<c>Aigis.Player.StageFitGameCamera</c>), which has no orbit/zoom concept at all and simply
/// no-ops <see cref="ApplyDrag"/>/<see cref="ApplyScroll"/>. <see cref="OrbitCamera"/> keeps
/// working unchanged for every other <c>games/*</c> title — this is an additional camera mode,
/// not a replacement (see that class's own doc comment).
/// </summary>
public interface IGameCamera
{
    /// <summary>Builds this frame's view-projection matrix for the given live window aspect ratio.</summary>
    public Matrix4x4 ViewProjection(float aspect);

    /// <summary>TASK-151: the camera eye, world-space — the one piece of camera state
    /// <see cref="ViewProjection"/>'s combined matrix does not expose on its own, needed by
    /// <c>Aigis.Render.SpecularConfig</c>'s view-dependent highlight. Every existing
    /// <see cref="IGameCamera"/> implementation already computes this internally to build its own
    /// view matrix; this only surfaces it.</summary>
    public Vector3 Eye { get; }

    /// <summary>Reacts to a mouse-drag delta (window-space pixels, origin top-left, Y-down) —
    /// a no-op for a camera with nothing to orbit.</summary>
    public void ApplyDrag(Vector2 pixelDelta);

    /// <summary>Reacts to a scroll-wheel delta — a no-op for a camera with nothing to zoom.</summary>
    public void ApplyScroll(float scrollDelta);
}
