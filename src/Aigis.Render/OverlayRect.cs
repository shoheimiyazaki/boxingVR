using System.Numerics;

namespace Aigis.Render;

/// <summary>
/// The <b>single</b> implementation of the overlay pipelines' pixel-space -> Vulkan-NDC mapping
/// (hard rule 4: the Y-down/[0,1]-depth convention is handled in ONE place per path). Four call
/// sites share it — <c>HeadlessRenderer.DrawOverlay</c>/<c>DrawOverlayImages</c> and their
/// <c>WindowedRenderer</c> twins — so a solid-colour text quad and a textured HUD icon can never
/// disagree about where a given pixel rect lands on screen.
/// <para>
/// TASK-159 extracted this from the two hand-copied copies that previously lived inline in the
/// text-overlay draw loops. That was safe while there were exactly two identical copies; adding a
/// textured path would have made four, and <see cref="OverlaySource"/>'s doc comment already warns
/// specifically against a "second, competing flip" appearing in this neighbourhood. Extracting it
/// is what keeps that warning enforceable rather than aspirational.
/// </para>
/// </summary>
internal static class OverlayRect
{
    /// <summary>
    /// Packs a pixel-space rect into the overlay shaders' <c>rect</c> push constant: a
    /// scale+translate pair applied to the shared unit quad's local <c>[-0.5, 0.5]</c> corners,
    /// NOT a matrix. <c>XY</c> scales the unit quad to <paramref name="sizePixels"/> as a fraction
    /// of the render target, doubled (NDC spans <c>[-1, 1]</c>, so a full-target rect needs scale
    /// 2); <c>ZW</c> translates <paramref name="centerPixels"/> into NDC.
    /// <para>
    /// There is deliberately <b>no Y sign flip</b> here, and this method deliberately does not call
    /// <see cref="Camera.ApplyVulkanClip"/>. Pixel space (origin top-left, Y increasing downward —
    /// <see cref="ScreenTextDraw"/>'s documented convention) already agrees with Vulkan's Y-down
    /// NDC. <see cref="Camera.ApplyVulkanClip"/>'s flip exists because the perspective-camera path
    /// starts from a Y-<i>up</i> world; applying it here would flip a HUD that was never Y-up to
    /// begin with. See <see cref="OverlaySource"/>'s doc comment, and
    /// <see cref="Camera.ScreenPointToRay"/>, which makes the same observation for mouse-picking.
    /// </para>
    /// </summary>
    /// <param name="centerPixels">Rect centre in pixels, origin top-left, Y down.</param>
    /// <param name="sizePixels">Rect width/height in pixels.</param>
    /// <param name="targetWidthPixels">Render-target width. <see cref="HeadlessRenderer"/> passes
    /// its fixed size; <see cref="WindowedRenderer"/> passes the current swapchain extent, which is
    /// what makes the same overlay list survive a window resize.</param>
    /// <param name="targetHeightPixels">Render-target height.</param>
    public static Vector4 ToNdcScaleTranslate(
        Vector2 centerPixels,
        Vector2 sizePixels,
        float targetWidthPixels,
        float targetHeightPixels) =>
        new(
            sizePixels.X / targetWidthPixels * 2f,
            sizePixels.Y / targetHeightPixels * 2f,
            (centerPixels.X / targetWidthPixels * 2f) - 1f,
            (centerPixels.Y / targetHeightPixels * 2f) - 1f);
}
