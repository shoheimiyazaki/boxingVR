using System.Numerics;

namespace Aigis.Render;

/// <summary>
/// A framebuffer-pixel rectangle with a top-left, Y-down origin and half-open bounds
/// [X, X+Width) × [Y, Y+Height). A layout validates target containment and non-overlap for a
/// collection of these rectangles; this value validates only its own positive dimensions.
/// </summary>
public readonly record struct RenderViewport(int XPx, int YPx, int WidthPx, int HeightPx)
{
    public bool IsValid => WidthPx > 0 && HeightPx > 0;

    public float AspectRatio => IsValid ? (float)WidthPx / HeightPx : throw new InvalidOperationException("A viewport must have positive dimensions.");
}

/// <summary>
/// A world-to-Vulkan-clip camera transform, world-space eye position, and framebuffer-pixel view.
/// It does not introduce a second Y flip; viewport origin is top-left/Y-down and its dimensions are
/// always positive. The layout/renderer validates bounds and camera-view non-overlap.
/// </summary>
public readonly record struct CameraView
{
    private CameraView(Matrix4x4 viewProjection, Vector3 position, RenderViewport viewport, bool isOverlay)
    {
        if (!viewport.IsValid)
        {
            throw new ArgumentOutOfRangeException(nameof(viewport), "A camera view requires a positive viewport.");
        }

        if (!IsFinite(viewProjection) || !IsFinite(position))
        {
            throw new ArgumentOutOfRangeException(nameof(viewProjection), "Camera transform and position must be finite.");
        }

        ViewProjection = viewProjection;
        Position = position;
        Viewport = viewport;
        IsOverlay = isOverlay;
    }

    public Matrix4x4 ViewProjection { get; }
    public Vector3 Position { get; }
    public RenderViewport Viewport { get; }

    /// <summary>
    /// 2026-08-31: whether this view is drawn ON TOP of the views before it, rather than into a
    /// rectangle it has to itself.
    ///
    /// <para><b>Why the renderer needs to be told rather than working it out.</b> Overlap is normally
    /// a bug — two split-screen panes fighting over the same pixels — and both renderers reject it
    /// outright. An overlay is the one case where it is intended, so it is declared, and the rejection
    /// stays in force for every view that does not declare it.</para>
    ///
    /// <para><b>What it costs the renderer.</b> The windowed path shares one depth buffer across
    /// every view in a frame, so an overlay's geometry would otherwise be depth-tested against
    /// whatever the view underneath left there. It therefore clears colour AND depth inside its own
    /// rectangle first (<c>vkCmdClearAttachments</c>), which is also what makes the pane opaque
    /// rather than a double exposure. The headless compositor renders each view into its own target
    /// and copies in order, so there it costs nothing at all.</para>
    ///
    /// <para><b>Ordering is the caller's.</b> An overlay is drawn where it appears in the list, so a
    /// caller that wants it on top puts it last. Two overlays may overlap each other; the later one
    /// wins.</para>
    /// </summary>
    public bool IsOverlay { get; }

    /// <summary>
    /// Builds a view projection from this view's own framebuffer-pixel aspect ratio. Split-screen
    /// callers must use this factory rather than reusing a full-target projection matrix.
    /// </summary>
    public static CameraView Create(
        RenderViewport viewport, Vector3 position, Func<float, Matrix4x4> buildViewProjection, bool isOverlay = false)
    {
        ArgumentNullException.ThrowIfNull(buildViewProjection);
        if (!viewport.IsValid)
        {
            throw new ArgumentOutOfRangeException(nameof(viewport), "A camera view requires a positive viewport.");
        }

        return new CameraView(buildViewProjection(viewport.AspectRatio), position, viewport, isOverlay);
    }

    /// <summary>Rejects a default-initialized or otherwise invalid value at a renderer boundary.</summary>
    public void Validate()
    {
        if (!Viewport.IsValid || !IsFinite(ViewProjection) || !IsFinite(Position))
        {
            throw new ArgumentException("A camera view requires a finite transform, finite position, and positive viewport.");
        }
    }

    private static bool IsFinite(Vector3 value) => float.IsFinite(value.X) && float.IsFinite(value.Y) && float.IsFinite(value.Z);

    private static bool IsFinite(Matrix4x4 value) =>
        float.IsFinite(value.M11) && float.IsFinite(value.M12) && float.IsFinite(value.M13) && float.IsFinite(value.M14)
        && float.IsFinite(value.M21) && float.IsFinite(value.M22) && float.IsFinite(value.M23) && float.IsFinite(value.M24)
        && float.IsFinite(value.M31) && float.IsFinite(value.M32) && float.IsFinite(value.M33) && float.IsFinite(value.M34)
        && float.IsFinite(value.M41) && float.IsFinite(value.M42) && float.IsFinite(value.M43) && float.IsFinite(value.M44);
}
