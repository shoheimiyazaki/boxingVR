using System.Numerics;

namespace Aigis.Render;

/// <summary>
/// The per-view arithmetic of the finishing pass over a split screen, shared by
/// <see cref="WindowedRenderer"/> and <see cref="MultiViewHeadlessRenderer"/> so the two cannot drift
/// (hard rule 6). Each view's camera is re-expressed in whole-target clip space
/// (<see cref="Camera.ViewportToTargetClip"/>); while every view is a slice of ONE camera the target is
/// one picture and is sampled as one, and once the cameras differ each view's taps are confined to its
/// own rectangle — a neighbour's depth rebuilt through this view's camera is a surface that does not
/// exist, which the ambient occlusion draws as a dark seam.
/// </summary>
internal static class SplitViewFinish
{
    /// <summary>Writes each view's whole-target camera into <paramref name="wholeTargetCameras"/>
    /// (no allocation — the windowed frame calls this every frame) and returns whether they are all
    /// slices of one camera.</summary>
    public static bool WholeTargetCameras(IReadOnlyList<CameraView> views, Vector2 targetSize, Span<Matrix4x4> wholeTargetCameras)
    {
        for (var i = 0; i < views.Count; i++)
        {
            wholeTargetCameras[i] = views[i].ViewProjection * Camera.ViewportToTargetClip(views[i].Viewport, targetSize);
        }

        return SlicesOfOneCamera(wholeTargetCameras[..views.Count]);
    }

    /// <summary>The uv rectangle (min x, min y, max x, max y; top-left origin) a view's taps may read:
    /// <see langword="null"/> (the whole target) while the views are slices of one camera.</summary>
    public static Vector4? SampleRect(RenderViewport rect, Vector2 targetSize, bool slicesOfOneCamera) =>
        slicesOfOneCamera
            ? null
            : new Vector4(
                rect.XPx / targetSize.X, rect.YPx / targetSize.Y,
                (rect.XPx + rect.WidthPx) / targetSize.X, (rect.YPx + rect.HeightPx) / targetSize.Y);

    /// <summary>Whether every whole-target camera is the first one, to within float noise relative to
    /// the matrix's own scale.</summary>
    private static bool SlicesOfOneCamera(ReadOnlySpan<Matrix4x4> wholeTargetCameras)
    {
        if (wholeTargetCameras.Length < 2)
        {
            return true;
        }

        var first = wholeTargetCameras[0];
        var scale = 0f;
        for (var row = 0; row < 4; row++)
        {
            for (var column = 0; column < 4; column++)
            {
                scale = MathF.Max(scale, MathF.Abs(first[row, column]));
            }
        }

        var tolerance = 1e-5f * MathF.Max(scale, 1f);
        for (var i = 1; i < wholeTargetCameras.Length; i++)
        {
            for (var row = 0; row < 4; row++)
            {
                for (var column = 0; column < 4; column++)
                {
                    if (MathF.Abs(wholeTargetCameras[i][row, column] - first[row, column]) > tolerance)
                    {
                        return false;
                    }
                }
            }
        }

        return true;
    }
}
