using System.Numerics;
using Aigis.Player;

namespace MiniRing.Desktop;

/// <summary>ホストに渡す 1 つのカメラ。今の画面（リプレイ）が自分のカメラを持っていればそれへ、無ければ既定のカメラへ渡す。</summary>
internal sealed class CameraSwitch(IGameCamera fallback, Func<IGameCamera?> current) : IGameCamera
{
    private IGameCamera Active => current() ?? fallback;

    public Vector3 Eye => Active.Eye;

    public Matrix4x4 ViewProjection(float aspect) => Active.ViewProjection(aspect);

    public void ApplyDrag(Vector2 pixelDelta) => Active.ApplyDrag(pixelDelta);

    public void ApplyScroll(float scrollDelta) => Active.ApplyScroll(scrollDelta);
}
