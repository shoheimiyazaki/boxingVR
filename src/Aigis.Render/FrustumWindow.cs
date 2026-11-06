namespace Aigis.Render;

/// <summary>
/// The rectangle a perspective camera sees, measured on the plane ONE METRE in front of the eye in
/// view space (so every value is a tangent of an angle): <see cref="Left"/>/<see cref="Right"/> along
/// the camera's right axis, <see cref="Bottom"/>/<see cref="Top"/> along its up axis, <b>+Y up</b> —
/// the convention <see cref="System.Numerics.Matrix4x4.CreatePerspectiveOffCenter"/> takes, before
/// <see cref="Camera"/>'s single Vulkan Y flip.
/// <para>
/// <b>Why a window rather than a field of view (2026-10-10).</b> A symmetric FOV + aspect can only
/// describe a frustum centred on the view axis. Splitting one camera's picture into several viewports
/// that still read as ONE picture needs each viewport to draw the matching SUB-rectangle of the
/// original frustum from the original eye — an off-centre frustum — and blending such a slice into a
/// player's own centred camera needs a value that can be interpolated corner by corner. A window is
/// both: <see cref="SubRect"/> cuts the slice, <see cref="Lerp"/> blends two windows.
/// </para>
/// </summary>
public readonly record struct FrustumWindow(float Left, float Right, float Bottom, float Top)
{
    /// <summary>Positive width and height and finite corners — what
    /// <see cref="Camera.OffCenterViewProjection(System.Numerics.Vector3, System.Numerics.Vector3, System.Numerics.Vector3, FrustumWindow, float, float)"/> requires.</summary>
    public bool IsValid =>
        float.IsFinite(Left) && float.IsFinite(Right) && float.IsFinite(Bottom) && float.IsFinite(Top)
        && Right > Left && Top > Bottom;

    /// <summary>The centred window <see cref="Camera.ViewProjection"/> builds from a vertical FOV and
    /// an aspect ratio (width / height).</summary>
    public static FrustumWindow Symmetric(float fovYRadians, float aspect)
    {
        if (!(fovYRadians > 0f && fovYRadians < MathF.PI) || !(aspect > 0f) || !float.IsFinite(aspect))
        {
            throw new ArgumentOutOfRangeException(
                nameof(fovYRadians), $"A symmetric window needs 0 < fov < pi and a positive aspect (fov {fovYRadians}, aspect {aspect}).");
        }

        var halfHeight = MathF.Tan(fovYRadians * 0.5f);
        var halfWidth = halfHeight * aspect;
        return new FrustumWindow(-halfWidth, halfWidth, -halfHeight, halfHeight);
    }

    /// <summary>
    /// The part of this window a screen sub-rectangle shows. The rectangle is in SCREEN fractions of
    /// the whole picture — <paramref name="u0"/>..<paramref name="u1"/> left to right,
    /// <paramref name="v0"/>..<paramref name="v1"/> <b>top to bottom</b> (the same Y-down origin as
    /// <see cref="RenderViewport"/>) — so a viewport's pixel rectangle divided by the target size can
    /// be passed straight in.
    /// </summary>
    public FrustumWindow SubRect(float u0, float v0, float u1, float v1)
    {
        var width = Right - Left;
        var height = Top - Bottom;
        // Screen v grows DOWN and this window's Y grows UP, so the top edge of the slice (v0) is the
        // one nearer Top.
        return new FrustumWindow(Left + u0 * width, Left + u1 * width, Top - v1 * height, Top - v0 * height);
    }

    /// <summary>Corner-wise linear blend; <paramref name="t"/> is not clamped.</summary>
    public static FrustumWindow Lerp(FrustumWindow from, FrustumWindow to, float t) => new(
        from.Left + (to.Left - from.Left) * t,
        from.Right + (to.Right - from.Right) * t,
        from.Bottom + (to.Bottom - from.Bottom) * t,
        from.Top + (to.Top - from.Top) * t);
}
