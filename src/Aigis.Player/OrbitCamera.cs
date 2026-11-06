using System.Numerics;
using Aigis.Render;

namespace Aigis.Player;

/// <summary>
/// A minimal camera controller for looking at a scene: orbit (yaw/pitch) around a fixed
/// target via mouse drag, zoom via scroll. This is the only place in the human-play stack
/// that assigns meaning to a window-space pixel delta (<c>Aigis.Platform.InputSnapshot</c>
/// deliberately returns raw, unconverted pixels — see its class doc comment) — the Y-down
/// pixel delta is inverted here so dragging up raises the camera to look down at the scene
/// from above (pitch increases; standard turntable-orbit UX, e.g. Blender/Unity), a UI
/// convention choice, not a coordinate-system correction.
/// </summary>
public sealed class OrbitCamera : IGameCamera
{
    private const float MinPitchRadians = -1.5f; // just short of +/-90 deg — avoids the eye crossing the up vector
    private const float MaxPitchRadians = 1.5f;
    private const float MinDistance = 0.5f;
    private const float MaxDistance = 50f;

    public Vector3 Target { get; set; } = Vector3.Zero;
    public float Distance { get; private set; }
    public float YawRadians { get; private set; }
    public float PitchRadians { get; private set; } = 0.4f;

    public OrbitCamera(float initialDistance = 4f)
    {
        Distance = Math.Clamp(initialDistance, MinDistance, MaxDistance);
    }

    /// <param name="pixelDelta">Mouse movement since the last tick, in window-space pixels
    /// (origin top-left, Y-down) — pass <c>InputSnapshot.MouseDelta</c> directly.</param>
    /// <param name="radiansPerPixel">Mouse sensitivity.</param>
    public void ApplyDrag(Vector2 pixelDelta, float radiansPerPixel = 0.005f)
    {
        YawRadians -= pixelDelta.X * radiansPerPixel;
        PitchRadians = Math.Clamp(PitchRadians - pixelDelta.Y * radiansPerPixel, MinPitchRadians, MaxPitchRadians);
    }

    public void ApplyScroll(float scrollDelta, float zoomFactorPerNotch = 0.9f)
    {
        Distance = Math.Clamp(Distance * MathF.Pow(zoomFactorPerNotch, scrollDelta), MinDistance, MaxDistance);
    }

    public Matrix4x4 ViewProjection(float aspect, float fovYRadians = Camera.DefaultFovYRadians, float near = 0.05f, float far = 200f)
        => Camera.ViewProjection(Eye, Target, Vector3.UnitY, fovYRadians, aspect, near, far);

    // Explicit IGameCamera implementations: the interface's 1-arg signatures don't match these
    // methods' own richer (defaulted-parameter) overloads, so GameHost's generic camera calls
    // route through here to the real methods with their defaults.
    Matrix4x4 IGameCamera.ViewProjection(float aspect) => ViewProjection(aspect);

    void IGameCamera.ApplyDrag(Vector2 pixelDelta) => ApplyDrag(pixelDelta);

    void IGameCamera.ApplyScroll(float scrollDelta) => ApplyScroll(scrollDelta);

    public Vector3 Eye => Target + Distance * SphericalDirection(YawRadians, PitchRadians);

    /// <summary>Right-handed, Y-up unit direction from <see cref="Target"/> toward the eye
    /// (not the view/forward direction) — yaw=pitch=0 gives +Z, pitch=+90deg gives +Y.</summary>
    private static Vector3 SphericalDirection(float yaw, float pitch)
    {
        var cosPitch = MathF.Cos(pitch);
        return new Vector3(cosPitch * MathF.Sin(yaw), MathF.Sin(pitch), cosPitch * MathF.Cos(yaw));
    }
}
