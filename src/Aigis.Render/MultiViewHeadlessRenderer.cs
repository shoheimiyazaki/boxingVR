using System.Numerics;

namespace Aigis.Render;

/// <summary>
/// Headless twin of <see cref="WindowedRenderer.RenderPrimitiveViewsAndPresent"/> (hard rule 6): one
/// immutable scene through up to N camera views into one top-left-origin output, optionally finished by
/// the post-process pass exactly as the windowed split finishes it.
/// <para><b>How (2026-10-11).</b> The headless pipelines bake their viewport, so instead of N viewports
/// in one pass each view is drawn into ONE full-size target with its camera re-expressed in whole-target
/// clip space (<see cref="Camera.ViewportToTargetClip"/>) — an affine map on clip X/Y that puts the
/// view's picture on exactly the pixels its viewport covers, depth and perspective untouched — and only
/// that rectangle is copied out. The finishing pass then runs on that whole-target picture with the
/// same camera and the same tap confinement the windowed path uses (<see cref="SplitViewFinish"/>):
/// slices of one camera sample the whole target (which here IS the single-camera frame), diverged
/// cameras sample only their own rectangle. Costs one full-size render per view, which is fine for an
/// agent's screenshot.</para>
/// <para>Differs from the windowed path in one documented corner: an OVERLAY view's pane is not visible
/// to the finishing taps of the view underneath it (the windowed path reads it — see
/// <c>DrawPostProcessPerView</c>). No caller combines panes with the finishing pass.</para>
/// <para><b>The HUD (2026-10-23, for filming the shipped game's split frames).</b> With
/// <see cref="MultiViewLayers"/> the frame also carries what the windowed split draws beyond the scene:
/// each view's decals, and the foreground layer and the screen-space HUD over the whole target. The HUD
/// is screen-space and drawn after the finishing pass, so it is the same pixels whichever view's render
/// it lands in: every view's render draws the WHOLE HUD and only that view's rectangle is copied out,
/// and a first render of the HUD over the clear colour supplies the pixels no view covers (a split's
/// divider). The composite is therefore the HUD over the views, exactly as one windowed pass draws it.
/// One exception, the FOREGROUND layer: it is 3D, lit, and the windowed path lights it once with view 0's
/// eye, while here each view's render lights it with its own — identical unless the lighting has
/// specular, where views 1..N's foreground glint differs (GameHost passes no foreground to an automatic
/// split, so only explicit views can show it).</para>
/// </summary>
public sealed class MultiViewHeadlessRenderer : IDisposable
{
    private readonly int _targetWidthPx;
    private readonly int _targetHeightPx;
    private readonly HeadlessRenderer _target;
    private bool _disposed;

    public MultiViewHeadlessRenderer(int targetWidthPx, int targetHeightPx)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(targetWidthPx);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(targetHeightPx);
        _targetWidthPx = targetWidthPx;
        _targetHeightPx = targetHeightPx;
        _target = new HeadlessRenderer((uint)targetWidthPx, (uint)targetHeightPx);
    }

    /// <summary>Renders one immutable scene through every view and returns a packed RGBA frame. Views
    /// must not overlap unless the later one is an overlay (<see cref="CameraView.IsOverlay"/>).
    /// <paramref name="postProcess"/> finishes each view as the windowed split does;
    /// <see langword="null"/> draws the views unfinished.</summary>
    /// <param name="scene">The 3D scene every view draws.</param>
    /// <param name="views">The views, in drawing order.</param>
    /// <param name="clearColor">The backdrop, and the colour of any pixel no view covers.</param>
    /// <param name="postProcess">The finishing pass per view; <see langword="null"/> leaves views unfinished.</param>
    /// <param name="layers">Decals per view and the HUD over the whole target — see
    /// <see cref="MultiViewLayers"/>. <see langword="null"/> (the default) draws the scene alone, exactly as
    /// before it existed.</param>
    public byte[] Render(
        RenderScene scene, IReadOnlyList<CameraView> views, Vector4 clearColor, PostProcessSettings? postProcess = null,
        MultiViewLayers? layers = null)
    {
        ArgumentNullException.ThrowIfNull(scene);
        ArgumentNullException.ThrowIfNull(views);
        ThrowIfDisposed();
        ValidateViews(views);
        postProcess?.Validate();

        var targetSize = new Vector2(_targetWidthPx, _targetHeightPx);
        var wholeTargetCameras = new Matrix4x4[views.Count];
        var oneCamera = SplitViewFinish.WholeTargetCameras(views, targetSize, wholeTargetCameras);
        var packed = layers is null
            ? Clear(_targetWidthPx, _targetHeightPx, clearColor)
            : _target.RenderPrimitiveSceneForView(
                clearColor, wholeTargetCameras[0], [], [],
                layers.Text, particles: null, lighting: scene.Lighting, shadow: null,
                cameraPosition: views[0].Position, layers.Images, layers.Shapes, decals: null, meshes: null,
                layers.Foreground, postProcess: null, postProcessSampleRect: null);
        for (var i = 0; i < views.Count; i++)
        {
            var view = views[i];
            var pixels = _target.RenderPrimitiveSceneForView(
                clearColor, wholeTargetCameras[i], scene.Instances, scene.Lines,
                text: layers?.Text, particles: scene.Particles, lighting: scene.Lighting, shadow: scene.Shadow,
                cameraPosition: view.Position, images: layers?.Images, shapes: layers?.Shapes, decals: layers?.Decals,
                meshes: null, foreground: layers?.Foreground, postProcess: postProcess,
                postProcessSampleRect: SplitViewFinish.SampleRect(view.Viewport, targetSize, oneCamera));
            CopyView(pixels, view.Viewport, packed);
        }

        return packed;
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        _target.Dispose();
    }

    private void ValidateViews(IReadOnlyList<CameraView> views)
    {
        for (var i = 0; i < views.Count; i++)
        {
            var view = views[i];
            view.Validate();
            var viewport = view.Viewport;
            if (viewport.XPx < 0 || viewport.YPx < 0
                || (long)viewport.XPx + viewport.WidthPx > _targetWidthPx
                || (long)viewport.YPx + viewport.HeightPx > _targetHeightPx)
            {
                throw new ArgumentOutOfRangeException(nameof(views), "A camera viewport must be fully contained by the render target.");
            }

            // 2026-08-31: an OVERLAY view may cover the views before it -- see CameraView.IsOverlay.
            // This compositor renders each view into the target and copies them in order, so an
            // overlay simply overwrites what is under it and costs nothing extra here.
            if (view.IsOverlay)
            {
                continue;
            }

            for (var j = 0; j < i; j++)
            {
                if (Overlaps(viewport, views[j].Viewport))
                {
                    throw new ArgumentException("Camera viewports must not overlap.", nameof(views));
                }
            }
        }
    }

    private static bool Overlaps(RenderViewport a, RenderViewport b) =>
        a.XPx < (long)b.XPx + b.WidthPx && b.XPx < (long)a.XPx + a.WidthPx
        && a.YPx < (long)b.YPx + b.HeightPx && b.YPx < (long)a.YPx + a.HeightPx;

    private static byte[] Clear(int widthPx, int heightPx, Vector4 color)
    {
        var pixel = new[]
        {
            (byte)Math.Clamp(MathF.Round(color.X * 255f), 0f, 255f),
            (byte)Math.Clamp(MathF.Round(color.Y * 255f), 0f, 255f),
            (byte)Math.Clamp(MathF.Round(color.Z * 255f), 0f, 255f),
            (byte)Math.Clamp(MathF.Round(color.W * 255f), 0f, 255f),
        };
        var result = new byte[widthPx * heightPx * 4];
        for (var offset = 0; offset < result.Length; offset += 4)
        {
            pixel.CopyTo(result, offset);
        }

        return result;
    }

    private void CopyView(ReadOnlySpan<byte> source, RenderViewport viewport, Span<byte> destination)
    {
        var rowBytes = viewport.WidthPx * 4;
        for (var y = 0; y < viewport.HeightPx; y++)
        {
            var offset = ((viewport.YPx + y) * _targetWidthPx + viewport.XPx) * 4;
            source.Slice(offset, rowBytes).CopyTo(destination.Slice(offset, rowBytes));
        }
    }

    private void ThrowIfDisposed()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
    }
}

/// <summary>
/// What a split frame draws beyond its <see cref="RenderScene"/> — the windowed
/// <c>RenderPrimitiveViewsAndPresent</c>'s decals, foreground layer and HUD — for
/// <see cref="MultiViewHeadlessRenderer.Render"/>. Every member may be null (that layer is absent).
/// </summary>
/// <param name="Decals">World decals, projected through each view's own camera.</param>
/// <param name="Text">Screen-space HUD text, over the whole target.</param>
/// <param name="Images">Screen-space HUD images, over the whole target.</param>
/// <param name="Shapes">Screen-space HUD panels and gauges, over the whole target.</param>
/// <param name="Foreground">3D primitives over the HUD panels with their own camera — see <see cref="ForegroundLayer"/>.</param>
public sealed record MultiViewLayers(
    IReadOnlyList<DecalQuadDraw>? Decals = null,
    IReadOnlyList<ScreenTextDraw>? Text = null,
    IReadOnlyList<OverlayImageDraw>? Images = null,
    IReadOnlyList<UiShapeDraw>? Shapes = null,
    ForegroundLayer? Foreground = null);
