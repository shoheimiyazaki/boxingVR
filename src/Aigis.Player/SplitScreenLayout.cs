using System.Numerics;
using Aigis.Render;

namespace Aigis.Player;

/// <summary>Stable split-screen layouts. The host owns this presentation policy; games continue to expose one scene.</summary>
public enum SplitScreenLayout
{
    Full,
    VerticalTwo,
    HorizontalTwo,
    GridFour,

    /// <summary>TASK-180 (tall-building extension, Phase 5): <see cref="HorizontalTwo"/>'s top/bottom
    /// split, PLUS a third small camera in the bottom-right corner (e.g. Triage's rider's-eye view of
    /// a moving elevator cabin) -- see <see cref="SplitScreenLayouts.Viewports"/> for the exact
    /// partition. Deliberately a real non-overlapping rectangle carve-out, not an overlay:
    /// <c>Aigis.Render.WindowedRenderer.RenderPrimitiveViewsAndPresent</c> rejects overlapping camera
    /// viewports outright (<c>ValidateViewsForSwapchain</c>), so an "inset ON TOP of" the bottom half
    /// is not representable here -- the bottom half's own view is instead shrunk to leave the inset's
    /// rectangle uncovered by any view (rendering as plain clear-color gutter, an ordinary and
    /// intentional split-screen border, not a bug).</summary>
    HorizontalTwoPlusInset,

    /// <summary>2026-08-31: the WHOLE framebuffer, PLUS a small camera OVERLAID on its bottom-right
    /// corner — Triage's 吹き出しカメラ, the pane that rides a body falling off a tower.
    /// <para><b>A true overlay, and that is the point.</b> The main view is the entire target whether
    /// the pane is up or not, so the picture underneath does not move, resize or re-letterbox when
    /// somebody falls (user report, 2026-08-31: 「吹き出しカメラの有無で親の画面の位置が揺れるのが
    /// 気持ち悪いです。また吹き出しカメラがあるとき背景が黒くなります」 — the first draft carved a
    /// column out of the main view instead, which shifted it sideways and left the strip above the
    /// inset as bare clear-colour). See <see cref="Aigis.Render.CameraView.IsOverlay"/> for what the
    /// renderers do to make an overlapping view legal.</para></summary>
    FullPlusInset,
}

/// <summary>Builds framebuffer-pixel viewports and the correctly aspect-fitted camera views for a layout.</summary>
public static class SplitScreenLayouts
{
    /// <summary>Black clear-colour gutter between the two storey views. Four physical pixels stays
    /// visible at ordinary resolutions without materially shrinking either playable view.</summary>
    public const int HorizontalDividerPixels = 4;
    public static IReadOnlyList<CameraView> Build(
        SplitScreenLayout layout,
        IReadOnlyList<IGameCamera> cameras,
        int targetWidthPx,
        int targetHeightPx)
    {
        ArgumentNullException.ThrowIfNull(cameras);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(targetWidthPx);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(targetHeightPx);
        ValidateMinimumExtent(layout, targetWidthPx, targetHeightPx, nameof(targetWidthPx), nameof(targetHeightPx));

        var required = RequiredCameraCount(layout);
        if (cameras.Count != required)
        {
            throw new ArgumentException($"{layout} requires exactly {required} cameras, got {cameras.Count}.", nameof(cameras));
        }

        var viewports = Viewports(layout, targetWidthPx, targetHeightPx);
        var views = new CameraView[required];
        for (var i = 0; i < required; i++)
        {
            var camera = cameras[i] ?? throw new ArgumentException("A split-screen layout cannot contain a null camera.", nameof(cameras));
            views[i] = CameraView.Create(viewports[i], camera.Eye, camera.ViewProjection, IsOverlayView(layout, i));
        }

        return views;
    }

    /// <summary>
    /// Whether every pixel of the target is covered by some view, so nothing of the clear colour is
    /// ever seen.
    ///
    /// <para><b>What this decides is whether the clear colour is a DIVIDER or a BACKDROP</b>, and it
    /// has to be one or the other. A layout with a gutter wants it black, so two storey views read as
    /// two screens instead of one continuous image. A layout without one wants the game's own —
    /// because on a game whose sky is beyond its own far plane (Triage's is: the sky shell sits at
    /// 900 m and the stage fit's far clip is under 100), the clear colour IS the sky, and clearing to
    /// black paints the sky black. That is exactly what happened the moment Triage's rooftop map
    /// started using an overlaid pane: the whole frame switched to the multi-view path, which had
    /// hardcoded black since every layout it had then genuinely had a gutter
    /// (user report, 2026-08-31: 「吹き出しカメラが起動しているとき、空が黒くなります」).</para>
    /// </summary>
    /// <para><b>Named as the two exceptions rather than as the list that covers.</b> Every layout here
    /// tiles its target exactly except the two HORIZONTAL splits, and those only because they
    /// deliberately carry a <see cref="HorizontalDividerPixels"/> divider (and, for the inset variant,
    /// the strip above the pane). Writing it the other way round is how the first draft came to claim
    /// <see cref="SplitScreenLayout.VerticalTwo"/> and <see cref="SplitScreenLayout.GridFour"/> leave
    /// gutters when they tile exactly — caught by
    /// <c>SplitScreenLayoutTests.CoversTheWholeTarget_AgreesWithEachLayoutsOwnViewports</c>, which
    /// measures the viewports instead of restating this list.</para>
    public static bool CoversTheWholeTarget(SplitScreenLayout layout) =>
        layout is not (SplitScreenLayout.HorizontalTwo or SplitScreenLayout.HorizontalTwoPlusInset);

    /// <summary>Which view of a layout is drawn ON TOP of the others rather than into a rectangle of
    /// its own. Only <see cref="SplitScreenLayout.FullPlusInset"/>'s pane is, today — see
    /// <see cref="Aigis.Render.CameraView.IsOverlay"/>.</summary>
    public static bool IsOverlayView(SplitScreenLayout layout, int viewIndex) =>
        layout == SplitScreenLayout.FullPlusInset && viewIndex == 1;

    public static int RequiredCameraCount(SplitScreenLayout layout) => layout switch
    {
        SplitScreenLayout.Full => 1,
        SplitScreenLayout.VerticalTwo or SplitScreenLayout.HorizontalTwo => 2,
        SplitScreenLayout.HorizontalTwoPlusInset => 3,
        SplitScreenLayout.FullPlusInset => 2,
        SplitScreenLayout.GridFour => 4,
        _ => throw new ArgumentOutOfRangeException(nameof(layout)),
    };

    /// <summary>Fraction of the full framebuffer's width/height the inset camera occupies in
    /// <see cref="SplitScreenLayout.HorizontalTwoPlusInset"/> -- "roughly 20% of the screen", per
    /// TASK-180's own spec.</summary>
    public const float InsetFraction = 0.2f;

    private static void ValidateMinimumExtent(SplitScreenLayout layout, int widthPx, int heightPx, string widthParameterName, string heightParameterName)
    {
        if ((layout is SplitScreenLayout.VerticalTwo or SplitScreenLayout.GridFour
                or SplitScreenLayout.HorizontalTwoPlusInset or SplitScreenLayout.FullPlusInset) && widthPx < 2)
        {
            throw new ArgumentOutOfRangeException(widthParameterName, widthPx, $"{layout} requires a framebuffer width of at least two pixels.");
        }

        if ((layout is SplitScreenLayout.HorizontalTwo or SplitScreenLayout.GridFour
                or SplitScreenLayout.HorizontalTwoPlusInset or SplitScreenLayout.FullPlusInset) && heightPx < 2)
        {
            throw new ArgumentOutOfRangeException(heightParameterName, heightPx, $"{layout} requires a framebuffer height of at least two pixels.");
        }
    }

    private static RenderViewport[] Viewports(SplitScreenLayout layout, int widthPx, int heightPx) => layout switch
    {
        SplitScreenLayout.Full => [new RenderViewport(0, 0, widthPx, heightPx)],
        SplitScreenLayout.VerticalTwo =>
        [
            new RenderViewport(0, 0, widthPx / 2, heightPx),
            new RenderViewport(widthPx / 2, 0, widthPx - widthPx / 2, heightPx),
        ],
        SplitScreenLayout.HorizontalTwo => HorizontalTwoViewports(widthPx, heightPx),
        SplitScreenLayout.HorizontalTwoPlusInset => HorizontalTwoPlusInsetViewports(widthPx, heightPx),
        SplitScreenLayout.FullPlusInset => FullPlusInsetViewports(widthPx, heightPx),
        SplitScreenLayout.GridFour =>
        [
            new RenderViewport(0, 0, widthPx / 2, heightPx / 2),
            new RenderViewport(widthPx / 2, 0, widthPx - widthPx / 2, heightPx / 2),
            new RenderViewport(0, heightPx / 2, widthPx / 2, heightPx - heightPx / 2),
            new RenderViewport(widthPx / 2, heightPx / 2, widthPx - widthPx / 2, heightPx - heightPx / 2),
        ],
        _ => throw new ArgumentOutOfRangeException(nameof(layout)),
    };

    /// <summary>
    /// Three non-overlapping rectangles tiling most of the framebuffer:
    /// <list type="number">
    /// <item>the top half, full width (view 0 — e.g. Triage's ground-floor camera),</item>
    /// <item>the bottom half MINUS a bottom-right corner column (view 1 — e.g. the roof-floor
    /// camera), and</item>
    /// <item>that bottom-right corner itself, <see cref="InsetFraction"/> of the full width/height
    /// (view 2 — the small inset, e.g. a moving elevator cabin's rider's-eye view).</item>
    /// </list>
    /// The thin strip directly above the inset (bottom half, same right-hand column, above the
    /// inset's own height) is deliberately left uncovered by any viewport rather than folded into
    /// view 1 as an L-shape (a rectangle-only renderer cannot draw an L in one viewport) — it
    /// renders as plain clear-color gutter, exactly like the borders any split-screen layout already
    /// has between its panes.
    /// </summary>
    private static RenderViewport[] HorizontalTwoPlusInsetViewports(int widthPx, int heightPx)
    {
        var insetWidth = Math.Max(1, (int)(widthPx * InsetFraction));
        var insetHeight = Math.Max(1, (int)(heightPx * InsetFraction));
        var divider = DividerPixelsFor(heightPx);
        var topHeight = (heightPx - divider) / 2;
        var bottomY = topHeight + divider;
        var bottomHeight = heightPx - bottomY;
        return
        [
            new RenderViewport(0, 0, widthPx, topHeight),
            new RenderViewport(0, bottomY, widthPx - insetWidth, bottomHeight),
            new RenderViewport(widthPx - insetWidth, heightPx - insetHeight, insetWidth, insetHeight),
        ];
    }

    /// <summary>
    /// The whole framebuffer (view 0), and an <see cref="InsetFraction"/>-sized rectangle inset from
    /// its bottom-right corner by <see cref="InsetMarginPixels"/> (view 1, drawn OVER it).
    /// <para>The margin exists so the pane reads as something sitting ON the picture rather than as a
    /// corner of the picture that has gone wrong: a rectangle flush with two screen edges looks like a
    /// rendering fault, one floating clear of them looks like a pane.</para>
    /// </summary>
    private static RenderViewport[] FullPlusInsetViewports(int widthPx, int heightPx)
    {
        var insetWidth = Math.Max(1, (int)(widthPx * InsetFraction));
        var insetHeight = Math.Max(1, (int)(heightPx * InsetFraction));
        var margin = Math.Min(InsetMarginPixels, Math.Min(widthPx - insetWidth, heightPx - insetHeight));
        return
        [
            new RenderViewport(0, 0, widthPx, heightPx),
            new RenderViewport(widthPx - insetWidth - margin, heightPx - insetHeight - margin, insetWidth, insetHeight),
        ];
    }

    /// <summary>How far an overlaid inset floats clear of the framebuffer's own corner, in pixels.
    /// Clamped when the target is too small to afford it.</summary>
    public const int InsetMarginPixels = 12;

    /// <summary>How many stacked pane SLOTS the right-hand column has room for — see
    /// <see cref="InsetSlot"/>. Four <see cref="InsetFraction"/>-tall panes plus their margins is
    /// about 85% of the frame's height; a fifth would run off the top and, long before that, four
    /// panes is already as many separate pictures as anybody can read at once.</summary>
    public const int InsetSlotCount = 4;

    /// <summary>
    /// The rectangle of one pane in a stacked right-hand column, counting up from the bottom-right
    /// corner: slot 0 is exactly <see cref="SplitScreenLayout.FullPlusInset"/>'s own pane, and each
    /// higher slot sits one pane-height plus a margin above it (2026-09-06, for the tower map's
    /// several simultaneous 吹き出しカメラ).
    ///
    /// <para><b>Addressed by SLOT rather than by position in a list, and that is the whole point of
    /// this overload existing.</b> A caller with a variable number of panes that packed them densely
    /// would move every pane whenever one opened or closed — the user's own 「カメラのサブスクリーンは
    /// カメラの増減時にワープしないように」. Handing each subject a slot it keeps for the round makes a
    /// pane appear and disappear IN PLACE, and leaves a gap in the column rather than sliding its
    /// neighbours, which is the correct trade: a gap is legible, a jump is not.</para>
    ///
    /// <para>Returns <see langword="null"/> when the frame cannot afford this slot — too small, or the
    /// slot is off the top. A caller drops that pane rather than drawing a squashed one.</para>
    /// </summary>
    /// <param name="slot">Which pane, counting up from the bottom-right corner.</param>
    /// <param name="widthPx">Framebuffer width.</param>
    /// <param name="heightPx">Framebuffer height.</param>
    public static RenderViewport? InsetSlot(int slot, int widthPx, int heightPx)
    {
        if (slot < 0 || slot >= InsetSlotCount || widthPx <= 0 || heightPx <= 0)
        {
            return null;
        }

        var insetWidth = Math.Max(1, (int)(widthPx * InsetFraction));
        var insetHeight = Math.Max(1, (int)(heightPx * InsetFraction));
        var margin = Math.Min(InsetMarginPixels, Math.Min(widthPx - insetWidth, heightPx - insetHeight));
        if (margin < 0)
        {
            return null;
        }

        var x = widthPx - insetWidth - margin;
        var y = heightPx - ((insetHeight + margin) * (slot + 1));
        return x < 0 || y < 0 ? null : new RenderViewport(x, y, insetWidth, insetHeight);
    }

    /// <summary>
    /// The rectangle of one pane pushed out to the frame's edge along <paramref name="direction"/>
    /// (2026-09-20, for the space stage's off-screen casualty panes:
    /// 「メイン画面に対して患者カメラの表示位置と画面外方向を一致させる」).
    ///
    /// <para><b>Addressed by DIRECTION rather than by slot, which is the opposite trade from
    /// <see cref="InsetSlot"/> and a deliberate one.</b> A slot's whole purpose is that a pane never
    /// moves; this pane's whole purpose is that it moves — it is telling the viewer which way something
    /// they cannot see is lying, and a pane that held still while its subject swung round to the other
    /// side would be pointing at nothing. The motion is continuous and driven by the subject's own
    /// bearing, so it reads as tracking rather than as the warping <see cref="InsetSlot"/>'s own note
    /// exists to prevent, which was panes jumping because a NEIGHBOUR appeared.</para>
    ///
    /// <para><paramref name="direction"/> is in SCREEN axes: X right, Y down. It is pushed until the
    /// pane touches the frame's inner margin on whichever axis runs out first, so a diagonal lands in
    /// a corner and a cardinal lands mid-edge. A zero or non-finite direction, or a frame too small to
    /// hold a pane, gives <see langword="null"/> — the caller drops that pane rather than drawing a
    /// squashed or centred one.</para>
    ///
    /// <para><b>Two panes on the same bearing WILL overlap</b>, and that is left alone rather than
    /// resolved by a packing rule: any rule that separated them would have to move one of them off its
    /// own bearing, which is the one thing this layout is for. The caller's subject count is small by
    /// construction (a concurrency cap), and two casualties in the same direction are legitimately one
    /// thing to fly at.</para>
    /// </summary>
    public static RenderViewport? EdgeInset(Vector2 direction, int widthPx, int heightPx) =>
        EdgeInset(direction, new RenderViewport(0, 0, widthPx, heightPx), InsetFraction, widthPx, heightPx);

    /// <summary>
    /// <see cref="EdgeInset(Vector2, int, int)"/> confined to a SUB-RECTANGLE of the frame, and at a
    /// caller-chosen size (2026-09-21).
    ///
    /// <para><b>Why a region, rather than always the whole frame.</b> An edge pane is an overlay: it
    /// covers whatever is under it, and "whatever is under it" is sometimes the part of the picture the
    /// player is working in. On the space stage the panes were landing across the airlock a crew member
    /// was standing at, which makes a pane an obstruction rather than an aid
    /// (2026-09-21, 「割カメラとエアロックとかぶって操作ができない場合があります」). Handing the caller the
    /// rectangle to place WITHIN keeps that a decision of whoever knows what is in the picture, and
    /// leaves this function the one job it is good at: turning a direction into a position.</para>
    ///
    /// <para>The direction still means what it meant — it is resolved against the REGION's own centre
    /// and edges, so a squat region compresses the compass vertically without reordering it.</para>
    /// </summary>
    /// <param name="direction">Screen axes, X right and Y down. Zero or non-finite gives null.</param>
    /// <param name="region">Where panes may go. A region that hangs off any edge of the framebuffer is
    /// REFUSED rather than trimmed: the contract this overload sells is "inside the region", and a
    /// final clamp into the framebuffer would keep the pane on screen while quietly breaking exactly
    /// that (code-reviewer, 2026-09-21).</param>
    /// <param name="sizeFraction">The pane's size as a fraction of the FRAMEBUFFER, not of the region,
    /// so panes are the same size whatever band they are confined to.</param>
    /// <param name="framebufferWidthPx">The framebuffer, which the result is clamped into.</param>
    /// <param name="framebufferHeightPx">...and its height.</param>
    /// <param name="marginPixels">How far clear of the region's edges a pane stops, or null for
    /// <see cref="InsetMarginPixels"/>. <b>A caller whose rectangles must survive being computed at
    /// two different scales has to pass a proportional one</b>: a constant number of pixels does not
    /// commute with scaling, so the same layout worked out on a design canvas and on the framebuffer
    /// it is scaled onto lands in two different places (Triage's HUD works in design pixels and its
    /// shell in framebuffer pixels — 2026-09-21).</param>
    public static RenderViewport? EdgeInset(
        Vector2 direction, RenderViewport region, float sizeFraction,
        int framebufferWidthPx, int framebufferHeightPx, int? marginPixels = null)
    {
        if (framebufferWidthPx <= 0 || framebufferHeightPx <= 0
            || region.WidthPx <= 0 || region.HeightPx <= 0
            || !(sizeFraction > 0f) || sizeFraction > 1f
            || !float.IsFinite(direction.X) || !float.IsFinite(direction.Y))
        {
            return null;
        }

        if (region.XPx < 0 || region.YPx < 0
            || region.XPx + region.WidthPx > framebufferWidthPx
            || region.YPx + region.HeightPx > framebufferHeightPx)
        {
            return null;
        }

        var length = direction.Length();
        if (length < 1e-4f)
        {
            return null;
        }

        var unit = direction / length;
        var insetWidth = Math.Max(1, (int)(framebufferWidthPx * sizeFraction));
        var insetHeight = Math.Max(1, (int)(framebufferHeightPx * sizeFraction));
        if (insetWidth > region.WidthPx || insetHeight > region.HeightPx)
        {
            // The band cannot hold a pane at this size. Dropped rather than squashed, exactly as the
            // whole-frame case has always done when the framebuffer itself was too small.
            return null;
        }

        var margin = Math.Min(
            Math.Max(0, marginPixels ?? InsetMarginPixels),
            Math.Min(region.WidthPx - insetWidth, region.HeightPx - insetHeight));
        if (margin < 0)
        {
            return null;
        }

        // How far the pane's CENTRE may travel from the region's centre before the pane's own edge
        // reaches the margin. Zero on an axis is legal (a region exactly one pane wide) and simply
        // pins that axis, which is why the reach below skips a zero denominator rather than dividing.
        var reachX = ((region.WidthPx - insetWidth) * 0.5f) - margin;
        var reachY = ((region.HeightPx - insetHeight) * 0.5f) - margin;
        var travel = float.PositiveInfinity;
        if (MathF.Abs(unit.X) > 1e-4f)
        {
            travel = MathF.Min(travel, MathF.Max(0f, reachX) / MathF.Abs(unit.X));
        }

        if (MathF.Abs(unit.Y) > 1e-4f)
        {
            travel = MathF.Min(travel, MathF.Max(0f, reachY) / MathF.Abs(unit.Y));
        }

        if (!float.IsFinite(travel))
        {
            travel = 0f;
        }

        var centreX = region.XPx + (region.WidthPx * 0.5f) + (unit.X * travel);
        var centreY = region.YPx + (region.HeightPx * 0.5f) + (unit.Y * travel);
        var x = Math.Clamp(
            (int)MathF.Round(centreX - (insetWidth * 0.5f)), 0, framebufferWidthPx - insetWidth);
        var y = Math.Clamp(
            (int)MathF.Round(centreY - (insetHeight * 0.5f)), 0, framebufferHeightPx - insetHeight);
        return new RenderViewport(x, y, insetWidth, insetHeight);
    }

    private static RenderViewport[] HorizontalTwoViewports(int widthPx, int heightPx)
    {
        var divider = DividerPixelsFor(heightPx);
        var topHeight = (heightPx - divider) / 2;
        var bottomY = topHeight + divider;
        return
        [
            new RenderViewport(0, 0, widthPx, topHeight),
            new RenderViewport(0, bottomY, widthPx, heightPx - bottomY),
        ];
    }

    private static int DividerPixelsFor(int heightPx) => heightPx >= HorizontalDividerPixels + 2 ? HorizontalDividerPixels : 0;
}
