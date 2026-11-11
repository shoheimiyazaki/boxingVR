using System.Numerics;

namespace Aigis.Render;

public enum OrthoView
{
    Front,
    Top,
    Side,
}

/// <summary>Builds view/projection matrices with Vulkan clip-space conventions.</summary>
public static class Camera
{
    /// <summary>The engine's conventional default vertical field of view (50 degrees), shared so
    /// callers building a perspective camera (agent screenshots, games, tools, tests) don't each
    /// repeat the same <c>50f * (MathF.PI / 180f)</c> literal.</summary>
    public const float DefaultFovYRadians = 50f * (MathF.PI / 180f);

    /// <summary>
    /// A perspective view-projection for Vulkan (right-handed, depth [0,1], Y-down).
    /// <c>System.Numerics.CreatePerspectiveFieldOfView</c> already gives depth [0,1];
    /// the one required fixup is flipping Y (Vulkan clip space points down).
    /// </summary>
    public static Matrix4x4 ViewProjection(
        Vector3 eye,
        Vector3 target,
        Vector3 up,
        float fovYRadians,
        float aspect,
        float near,
        float far)
    {
        var view = Matrix4x4.CreateLookAt(eye, target, up);
        var proj = Matrix4x4.CreatePerspectiveFieldOfView(fovYRadians, aspect, near, far);
        return view * ApplyVulkanClip(proj);
    }

    /// <summary>
    /// <see cref="ViewProjection"/> for an OFF-CENTRE frustum (see <see cref="FrustumWindow"/>): the
    /// same look-at, the same [0,1] depth, the same single Y flip. With
    /// <c>FrustumWindow.Symmetric(fov, aspect)</c> it produces the same matrix as
    /// <see cref="ViewProjection"/>.
    /// <para>
    /// <b>The flip is the whole Y column here, not just M22.</b> An off-centre projection carries a
    /// Y offset in M32 (<c>(top + bottom) / (top - bottom)</c>) that a symmetric one does not, and
    /// flipping clip Y means negating every term that feeds it. <see cref="ApplyVulkanClip"/> negates
    /// M22 alone, which is only the whole column when M12, M32 and M42 are zero.
    /// </para>
    /// <para>
    /// <b>Why <see cref="ApplyVulkanClip"/> was not simply widened to match.</b> Its one off-centre
    /// caller, <see cref="DirectionalShadowViewProjection"/>, fits a box whose centre lies on the
    /// light's view axis, so its M42 is float noise rather than zero; widening the flip would move
    /// shadow-map texels by that noise and every golden with a sun shadow with them, for no visible
    /// gain. The two agree on every symmetric projection.
    /// </para>
    /// </summary>
    public static Matrix4x4 OffCenterViewProjection(
        Vector3 eye,
        Vector3 target,
        Vector3 up,
        FrustumWindow window,
        float near,
        float far)
    {
        if (!window.IsValid)
        {
            throw new ArgumentOutOfRangeException(nameof(window), $"An off-centre frustum needs a window with positive width and height: {window}.");
        }

        return OffCenterViewProjection(Matrix4x4.CreateLookAt(eye, target, up), window, near, far);
    }

    /// <summary>
    /// Same as the look-at overload, but from an explicit right-handed <paramref name="view"/> matrix —
    /// for a tracked pose (OpenXR eye: position + quaternion, asymmetric FOV as tangents) where there is
    /// no target point to look at. <paramref name="window"/> is the frustum's tangents at unit distance.
    /// </summary>
    public static Matrix4x4 OffCenterViewProjection(Matrix4x4 view, FrustumWindow window, float near, float far)
    {
        if (!window.IsValid)
        {
            throw new ArgumentOutOfRangeException(nameof(window), $"An off-centre frustum needs a window with positive width and height: {window}.");
        }

        var proj = Matrix4x4.CreatePerspectiveOffCenter(
            window.Left * near, window.Right * near, window.Bottom * near, window.Top * near, near, far);
        proj.M12 = -proj.M12;
        proj.M22 = -proj.M22;
        proj.M32 = -proj.M32;
        proj.M42 = -proj.M42;
        return view * proj;
    }

    /// <summary>
    /// The matrix that takes a view's OWN clip coordinates to the clip coordinates of the whole
    /// target it is drawn into: <c>viewProjection * ViewportToTargetClip(viewport, target)</c> is the
    /// "full-target" form of a split-screen view's camera, which projects a world point to the pixel
    /// it actually lands on in the whole framebuffer.
    /// <para>
    /// Exists for the screen-space consumers that work in whole-target coordinates — the post-process
    /// pass's ambient occlusion, which rebuilds world positions from a whole-target uv, and HUD
    /// anchoring through <see cref="TryWorldToScreen"/>. An affine map on clip X and Y only (scale
    /// plus offset times W), so depth and the perspective divide are untouched; Y-down in both spaces,
    /// so it introduces no flip (hard rule 4).
    /// </para>
    /// </summary>
    public static Matrix4x4 ViewportToTargetClip(RenderViewport viewport, Vector2 targetSizePixels)
    {
        if (!viewport.IsValid || !(targetSizePixels.X > 0f) || !(targetSizePixels.Y > 0f))
        {
            throw new ArgumentOutOfRangeException(nameof(viewport), "A viewport and its target need positive sizes.");
        }

        var scaleX = viewport.WidthPx / targetSizePixels.X;
        var scaleY = viewport.HeightPx / targetSizePixels.Y;
        // Row-vector convention (clip = v * M): out.x = x * M11 + w * M41.
        var result = Matrix4x4.Identity;
        result.M11 = scaleX;
        result.M22 = scaleY;
        result.M41 = (2f * viewport.XPx + viewport.WidthPx) / targetSizePixels.X - 1f;
        result.M42 = (2f * viewport.YPx + viewport.HeightPx) / targetSizePixels.Y - 1f;
        return result;
    }

    /// <summary>
    /// Recovers the world-space EYE a perspective <paramref name="viewProjection"/> was built from —
    /// the apex where its four side planes meet.
    /// <para>
    /// <b>Why this exists (2026-09-19).</b> <see cref="SurfaceFinish"/> shades from the view
    /// direction, which made the renderers' optional <c>cameraPosition</c> argument suddenly matter
    /// to ordinary geometry rather than only to the specular sheen almost nobody switched on. The
    /// first attempt was to REQUIRE it and throw when a frame carrying a finish did not supply one.
    /// That is the wrong shape of fix, and the measurement says so plainly: it turned 81 existing
    /// visual tests red in one run, every single one of them a caller that renders a Triage scene
    /// through a camera it already has — the argument was not missing information, it was
    /// information the renderer was being handed twice and only reading once.
    /// </para>
    /// <para>
    /// Deriving it instead makes the argument an OVERRIDE rather than a requirement: no caller can
    /// forget it, nothing is silently shaded from the world origin, and the older
    /// <see cref="SpecularConfig"/> footgun (pass the eye or get an exception) goes away with it.
    /// </para>
    /// <para>
    /// <b>The method.</b> For this file's row-vector convention (<c>clip = v * M</c>), each clip-space
    /// inequality gives a world-space plane as a combination of M's COLUMNS — the standard
    /// Gribb-Hartmann extraction: left is <c>col1 + col4</c>, right <c>col4 - col1</c>, top
    /// <c>col4 - col2</c>. A perspective frustum's four side planes all pass through the eye, so any
    /// three of them intersect there exactly. Depth convention does not enter: only the side planes
    /// are used, so a Y-flipped, [0,1]-depth Vulkan clip matrix (<see cref="ApplyVulkanClip"/>) is
    /// handled without a special case.
    /// </para>
    /// <para>
    /// <b>Returns <see langword="false"/> for an ORTHOGRAPHIC projection</b>, where the side planes
    /// are parallel and there is no apex to find — the system is singular and there is genuinely no
    /// such point. A caller that needs a view direction for a parallel projection must supply one;
    /// this reports that honestly rather than returning a huge number from an ill-conditioned solve.
    /// </para>
    /// </summary>
    public static bool TryGetEye(in Matrix4x4 viewProjection, out Vector3 eye)
    {
        // Columns 1, 2 and 4 of the row-vector matrix, as (a, b, c, d) plane coefficients.
        var col1 = new Vector4(viewProjection.M11, viewProjection.M21, viewProjection.M31, viewProjection.M41);
        var col2 = new Vector4(viewProjection.M12, viewProjection.M22, viewProjection.M32, viewProjection.M42);
        var col4 = new Vector4(viewProjection.M14, viewProjection.M24, viewProjection.M34, viewProjection.M44);

        var left = col4 + col1;
        var right = col4 - col1;
        var top = col4 - col2;

        // Cramer's rule on the 3x3 of normals, against -d.
        var a = new Vector3(left.X, left.Y, left.Z);
        var b = new Vector3(right.X, right.Y, right.Z);
        var c = new Vector3(top.X, top.Y, top.Z);
        var determinant = Vector3.Dot(a, Vector3.Cross(b, c));

        // The tolerance is RELATIVE to the normals' own magnitudes: an orthographic matrix built for
        // a 200 m shadow cascade has tiny coefficients, and an absolute epsilon would call a
        // perfectly good perspective frustum singular on one scene and an orthographic one solvable
        // on another.
        var scale = a.Length() * b.Length() * c.Length();
        if (scale <= 0f || MathF.Abs(determinant) <= 1e-6f * scale)
        {
            eye = Vector3.Zero;
            return false;
        }

        // Three 3x3 determinants, each with one COLUMN of the system replaced by the right-hand side.
        // The system's rows are the three plane normals, so its columns are the X, Y and Z components
        // gathered across them — which is why the arguments below are assembled component-wise rather
        // than passed as a, b, c.
        var rhs = new Vector3(-left.W, -right.W, -top.W);
        eye = new Vector3(
            Determinant3(rhs, new Vector3(a.Y, b.Y, c.Y), new Vector3(a.Z, b.Z, c.Z)),
            Determinant3(new Vector3(a.X, b.X, c.X), rhs, new Vector3(a.Z, b.Z, c.Z)),
            Determinant3(new Vector3(a.X, b.X, c.X), new Vector3(a.Y, b.Y, c.Y), rhs)) / determinant;
        return float.IsFinite(eye.X) && float.IsFinite(eye.Y) && float.IsFinite(eye.Z);
    }

    /// <summary>The determinant of the 3x3 whose COLUMNS are the three arguments — the shape Cramer's
    /// rule needs in <see cref="TryGetEye"/>, where each column in turn is replaced by the
    /// right-hand side.</summary>
    private static float Determinant3(Vector3 c0, Vector3 c1, Vector3 c2) =>
        (c0.X * ((c1.Y * c2.Z) - (c1.Z * c2.Y)))
        - (c1.X * ((c0.Y * c2.Z) - (c0.Z * c2.Y)))
        + (c2.X * ((c0.Y * c1.Z) - (c0.Z * c1.Y)));

    /// <summary>
    /// An orthographic view-projection along an ARBITRARY view direction (right-handed, depth
    /// [0,1], Y-down), the parallel-projection twin of <see cref="ViewProjection"/>.
    /// <para>
    /// Distinct from <see cref="OrthoViewProjection"/>, which frames a bounding box from one of
    /// three fixed axis views: that one answers "show me this scene from the front", while this one
    /// answers "keep my current camera orientation, just stop converging the lines" — the parallel
    /// mode a modelling tool toggles into, where the direction is wherever the user has orbited to
    /// and only the projection changes.
    /// </para>
    /// </summary>
    /// <param name="eye">Camera position, world space.</param>
    /// <param name="target">Point the camera looks at.</param>
    /// <param name="up">Camera up direction; must not be parallel to the view direction.</param>
    /// <param name="verticalExtent">World-space height of the visible region, metres. This is the
    /// parallel-projection equivalent of a field of view: halving it doubles the magnification.</param>
    /// <param name="aspect">Viewport width / height.</param>
    /// <param name="near">Near plane distance. May be negative, which is legitimate here and not in
    /// a perspective projection: geometry BEHIND an orthographic eye still projects correctly, so a
    /// caller that does not want to think about whether its eye is inside the model can pass a
    /// negative near and keep everything visible.</param>
    /// <param name="far">Far plane distance.</param>
    public static Matrix4x4 OrthographicViewProjection(
        Vector3 eye,
        Vector3 target,
        Vector3 up,
        float verticalExtent,
        float aspect,
        float near,
        float far)
    {
        if (!(verticalExtent > 0f) || !float.IsFinite(verticalExtent))
        {
            throw new ArgumentOutOfRangeException(nameof(verticalExtent), verticalExtent, "Must be finite and positive.");
        }

        if (!(aspect > 0f) || !float.IsFinite(aspect))
        {
            throw new ArgumentOutOfRangeException(nameof(aspect), aspect, "Must be finite and positive.");
        }

        if (!(far > near) || !float.IsFinite(near) || !float.IsFinite(far))
        {
            throw new ArgumentOutOfRangeException(nameof(far), far, "Require finite near < far.");
        }

        var view = Matrix4x4.CreateLookAt(eye, target, up);
        var projection = Matrix4x4.CreateOrthographic(verticalExtent * aspect, verticalExtent, near, far);
        return view * ApplyVulkanClip(projection);
    }

    /// <summary>
    /// A view-projection that frames the axis-aligned box [<paramref name="min"/>,
    /// <paramref name="max"/>] from <paramref name="viewDirection"/> — the fit-camera the
    /// agent uses to auto-frame a scene it just built (ported idea from iTFlow4's CameraFit).
    /// </summary>
    public static Matrix4x4 FitViewProjection(
        Vector3 min,
        Vector3 max,
        Vector3 viewDirection,
        float fovYRadians,
        float aspect,
        float margin = 1.15f)
        => FitViewProjection(min, max, viewDirection, fovYRadians, aspect, out _, margin);

    /// <summary>Same fit as the 5-argument overload, additionally reporting the solved
    /// <paramref name="eye"/> (TASK-151: a caller that wants the actual camera position — e.g. to
    /// feed <c>HeadlessRenderer.RenderPrimitiveScene</c>'s <c>cameraPosition</c> parameter for
    /// specular — would otherwise have to re-derive this exact same math by hand).</summary>
    public static Matrix4x4 FitViewProjection(
        Vector3 min,
        Vector3 max,
        Vector3 viewDirection,
        float fovYRadians,
        float aspect,
        out Vector3 eye,
        float margin = 1.15f)
    {
        var center = 0.5f * (min + max);
        var radius = MathF.Max(0.001f, 0.5f * (max - min).Length()) * margin;

        // Distance so the bounding sphere fits both the vertical and horizontal FOV.
        var fovX = 2f * MathF.Atan(MathF.Tan(fovYRadians * 0.5f) * aspect);
        var distance = radius / MathF.Sin(0.5f * MathF.Min(fovYRadians, fovX));

        var dir = Vector3.Normalize(viewDirection);
        eye = center - dir * distance;
        var up = MathF.Abs(dir.Y) > 0.99f ? Vector3.UnitZ : Vector3.UnitY;
        return ViewProjection(eye, center, up, fovYRadians, aspect, distance * 0.01f, distance * 4f + radius);
    }

    public static Matrix4x4 OrthoViewProjection(
        Vector3 aabbMin,
        Vector3 aabbMax,
        OrthoView view,
        float aspect,
        float margin = 1.2f)
    {
        if (aspect <= 0f || !float.IsFinite(aspect))
        {
            throw new ArgumentOutOfRangeException(nameof(aspect));
        }

        if (margin <= 0f || !float.IsFinite(margin))
        {
            throw new ArgumentOutOfRangeException(nameof(margin));
        }

        var center = (aabbMin + aabbMax) * 0.5f;
        var size = Vector3.Max(aabbMax - aabbMin, new Vector3(0.001f));
        var (planeWidth, planeHeight, eyeDirection, up) = view switch
        {
            OrthoView.Front => (size.X, size.Y, Vector3.UnitZ, Vector3.UnitY),
            OrthoView.Side => (size.Z, size.Y, -Vector3.UnitX, Vector3.UnitY),
            OrthoView.Top => (size.X, size.Z, Vector3.UnitY, -Vector3.UnitZ),
            _ => throw new ArgumentOutOfRangeException(nameof(view)),
        };
        var width = planeWidth * margin;
        var height = planeHeight * margin;
        if (width / height < aspect)
        {
            width = height * aspect;
        }
        else
        {
            height = width / aspect;
        }

        var diagonal = MathF.Max(1f, size.Length());
        var eye = center + eyeDirection * diagonal;
        var viewMatrix = Matrix4x4.CreateLookAt(eye, center, up);
        var projection = ApplyVulkanClip(Matrix4x4.CreateOrthographic(width, height, 0.01f, diagonal * 3f));
        return viewMatrix * projection;
    }

    private static Matrix4x4 ApplyVulkanClip(Matrix4x4 projection)
    {
        projection.M22 *= -1f;
        return projection;
    }

    /// <summary>
    /// TASK-148: a tight-fitting orthographic view-projection for a directional (sun) shadow
    /// map, from the SAME Vulkan clip-space fixup every other projection in this file uses
    /// (<see cref="ApplyVulkanClip"/>) — hard rule 4 in the strictest sense: a shadow matrix is
    /// a second projection, and it must agree with the camera's Y-down/[0,1]-depth convention
    /// in this one place rather than invent a second one next to it.
    /// <para>
    /// Fits the box [<paramref name="aabbMin"/>, <paramref name="aabbMax"/>] exactly (plus
    /// <paramref name="margin"/>) as seen from the light — the caller decides how generous that
    /// box is (TASK-148 uses the level's <c>StageBounds</c> with headroom above standing
    /// humanoids, not the whole dynamic scene, since a single fitted map was measured to cover
    /// every shipped Triage stage: the largest is 24x18 tiles at 1m tiles).
    /// </para>
    /// </summary>
    /// <param name="aabbMin">World-space minimum corner the shadow frustum must cover.</param>
    /// <param name="aabbMax">World-space maximum corner the shadow frustum must cover.</param>
    /// <param name="lightDirection"><see cref="DirectionalLight.Direction"/>'s convention:
    /// surface-toward-light (e.g. straight overhead is <c>+Vector3.UnitY</c>), NOT the direction
    /// the light's rays travel. The shadow "eye" sits on the opposite side of the box, along
    /// this direction, looking back toward it.</param>
    /// <param name="margin">Extra slack around the tight X/Y fit (fraction, e.g. 1.02 = 2%) so a
    /// caster exactly on the frustum edge does not clip in and out from float rounding.</param>
    public static Matrix4x4 DirectionalShadowViewProjection(Vector3 aabbMin, Vector3 aabbMax, Vector3 lightDirection, float margin = 1.02f)
    {
        if (margin <= 0f || !float.IsFinite(margin))
        {
            throw new ArgumentOutOfRangeException(nameof(margin));
        }

        var dir = lightDirection.LengthSquared() < 1e-12f ? Vector3.UnitY : Vector3.Normalize(lightDirection);
        var center = 0.5f * (aabbMin + aabbMax);
        var radius = MathF.Max(0.001f, 0.5f * (aabbMax - aabbMin).Length());
        var up = MathF.Abs(dir.Y) > 0.99f ? Vector3.UnitZ : Vector3.UnitY;

        // The eye sits toward the light (Direction points surface->light) far enough back that
        // the whole box is in front of it; CreateLookAt's own view space then has forward = -Z
        // (verified against System.Numerics's actual convention, not assumed from memory — see
        // the TASK-148 report for the empirical check), so every box corner projects to a
        // negative view-space Z, and "distance in front of the eye" is -viewSpaceZ.
        var eye = center + dir * (radius * 2f + 1f);
        var view = Matrix4x4.CreateLookAt(eye, center, up);

        Span<Vector3> corners =
        [
            new Vector3(aabbMin.X, aabbMin.Y, aabbMin.Z),
            new Vector3(aabbMax.X, aabbMin.Y, aabbMin.Z),
            new Vector3(aabbMin.X, aabbMax.Y, aabbMin.Z),
            new Vector3(aabbMax.X, aabbMax.Y, aabbMin.Z),
            new Vector3(aabbMin.X, aabbMin.Y, aabbMax.Z),
            new Vector3(aabbMax.X, aabbMin.Y, aabbMax.Z),
            new Vector3(aabbMin.X, aabbMax.Y, aabbMax.Z),
            new Vector3(aabbMax.X, aabbMax.Y, aabbMax.Z),
        ];

        float minX = float.MaxValue, maxX = float.MinValue;
        float minY = float.MaxValue, maxY = float.MinValue;
        float minViewZ = float.MaxValue, maxViewZ = float.MinValue;
        foreach (var corner in corners)
        {
            var v = Vector3.Transform(corner, view);
            minX = MathF.Min(minX, v.X); maxX = MathF.Max(maxX, v.X);
            minY = MathF.Min(minY, v.Y); maxY = MathF.Max(maxY, v.Y);
            minViewZ = MathF.Min(minViewZ, v.Z); maxViewZ = MathF.Max(maxViewZ, v.Z);
        }

        // near = distance to the closest corner (least negative view-space Z), far = distance
        // to the farthest (most negative) — same "positive distance along view direction"
        // convention every near/far pair in this file already uses.
        var near = MathF.Max(0.01f, -maxViewZ);
        var far = MathF.Max(near + 0.01f, -minViewZ);

        var halfW = (maxX - minX) * 0.5f * margin;
        var halfH = (maxY - minY) * 0.5f * margin;
        var cx = 0.5f * (minX + maxX);
        var cy = 0.5f * (minY + maxY);
        var ortho = Matrix4x4.CreateOrthographicOffCenter(cx - halfW, cx + halfW, cy - halfH, cy + halfH, near, far);
        return view * ApplyVulkanClip(ortho);
    }

    /// <summary>Builds the dedicated downward orthographic shadow transform for one fixed
    /// ceiling fixture. This is intentionally a Triage-style overhead-fixture approximation,
    /// not a generic six-face point-light cubemap: it gives correct, stable floor shadows for
    /// lights whose useful rays travel predominantly downward.</summary>
    public static Matrix4x4 DownwardFixtureShadowViewProjection(Vector3 lightPosition, float range, float floorY, float ceilingY)
    {
        if (!float.IsFinite(range) || range <= 0f)
        {
            throw new ArgumentOutOfRangeException(nameof(range));
        }

        if (!float.IsFinite(floorY) || !float.IsFinite(ceilingY) || ceilingY <= floorY)
        {
            throw new ArgumentOutOfRangeException(nameof(ceilingY));
        }

        var topY = MathF.Min(ceilingY, lightPosition.Y + 0.1f);
        if (topY <= floorY)
        {
            topY = ceilingY;
        }

        var min = new Vector3(lightPosition.X - range, floorY, lightPosition.Z - range);
        var max = new Vector3(lightPosition.X + range, topY, lightPosition.Z + range);
        return DirectionalShadowViewProjection(min, max, Vector3.UnitY);
    }

    /// <summary>
    /// Unprojects a window-space pixel (origin top-left, Y-down — <c>Aigis.Platform.InputSnapshot</c>'s
    /// convention) into a world-space ray, for mouse-picking. Implemented by inverting the exact
    /// <paramref name="viewProjection"/> matrix already used for rendering (rather than re-deriving
    /// from FOV/aspect by hand) so it can never independently drift from the forward projection.
    /// <para>
    /// Vulkan NDC is Y-down (via <see cref="ApplyVulkanClip"/>) and this engine's viewport is a
    /// standard positive-height viewport (no negative-height flip trick — see
    /// <c>WindowedRenderer.Primitives.cs</c>), so NDC.y maps to pixel Y with no further sign flip:
    /// both already agree that "y increases downward".
    /// </para>
    /// </summary>
    /// <param name="viewProjection">The same view-projection matrix used to render the frame the pixel was clicked in.</param>
    /// <param name="pixelPosition">Window-client-space pixel (origin top-left, Y-down).</param>
    /// <param name="windowSizePixels">Current window client size in pixels.</param>
    /// <returns>A world-space ray: <c>Origin</c> at the near plane, <c>Direction</c> normalized.</returns>
    public static (Vector3 Origin, Vector3 Direction) ScreenPointToRay(Matrix4x4 viewProjection, Vector2 pixelPosition, Vector2 windowSizePixels)
    {
        if (!Matrix4x4.Invert(viewProjection, out var inverse))
        {
            throw new InvalidOperationException("viewProjection must be invertible.");
        }

        var ndcX = pixelPosition.X / windowSizePixels.X * 2f - 1f;
        var ndcY = pixelPosition.Y / windowSizePixels.Y * 2f - 1f;
        var near = UnprojectNdc(inverse, ndcX, ndcY, 0f);
        var far = UnprojectNdc(inverse, ndcX, ndcY, 1f);
        return (near, Vector3.Normalize(far - near));
    }

    private static Vector3 UnprojectNdc(Matrix4x4 inverseViewProjection, float ndcX, float ndcY, float ndcZ)
    {
        var world = Vector4.Transform(new Vector4(ndcX, ndcY, ndcZ, 1f), inverseViewProjection);
        return new Vector3(world.X, world.Y, world.Z) / world.W;
    }

    /// <summary>
    /// The forward counterpart to <see cref="ScreenPointToRay"/> — projects a world-space point
    /// to a window-client-space pixel (origin top-left, Y-down, matching
    /// <see cref="ScreenTextDraw.ScreenPosPixels"/> and <c>Aigis.Platform.InputSnapshot</c>) using
    /// the SAME <paramref name="viewProjection"/> matrix already used to render the frame — the
    /// single canonical site for the Vulkan clip-space Y-flip (<see cref="ApplyVulkanClip"/>) stays
    /// exactly where it already is (baked into <paramref name="viewProjection"/> by
    /// <see cref="ViewProjection"/>/<see cref="FitViewProjection(Vector3, Vector3, Vector3, float, float, float)"/>/<see cref="OrthoViewProjection"/>);
    /// this method must never apply a second one, or a caller building world-anchored screen-space
    /// UI (health bars, name tags) on top of a rendered frame would see it mirror vertically
    /// against the frame it's supposed to sit on.
    /// <para>
    /// Returns <see langword="false"/> — with <paramref name="pixelPosition"/> set to
    /// <see cref="Vector2.Zero"/> — for a point behind the camera (clip-space <c>w &lt;= 0</c>) or
    /// outside the rendered depth range (Vulkan NDC <c>z</c> outside [0,1]). The latter matters for
    /// floor-band orthographic cameras: without it, an object on another storey becomes a plausible
    /// but wrong world-anchored HUD marker in this view.
    /// This is the classic "a point behind the eye still projects to a valid-looking screen
    /// position after the perspective divide" bug: dividing by a negative <c>w</c> flips both NDC
    /// axes, so an unguarded caller can otherwise draw a phantom marker for something that should
    /// be invisible. A caller also wanting to hide off-screen (but in-front) points should compare
    /// the returned pixel position against its own window bounds — this method does not clip to
    /// the viewport, since some callers (e.g. a gauge that should slide toward an edge indicator)
    /// legitimately want the unclamped position of a point just outside the frame.
    /// </para>
    /// </summary>
    public static bool TryWorldToScreen(Matrix4x4 viewProjection, Vector3 worldPosition, Vector2 windowSizePixels, out Vector2 pixelPosition)
    {
        var clip = Vector4.Transform(new Vector4(worldPosition, 1f), viewProjection);
        if (clip.W <= 0f)
        {
            pixelPosition = Vector2.Zero;
            return false;
        }

        var ndcX = clip.X / clip.W;
        var ndcY = clip.Y / clip.W;
        var ndcZ = clip.Z / clip.W;
        if (ndcZ < 0f || ndcZ > 1f)
        {
            pixelPosition = Vector2.Zero;
            return false;
        }

        pixelPosition = new Vector2((ndcX + 1f) * 0.5f * windowSizePixels.X, (ndcY + 1f) * 0.5f * windowSizePixels.Y);
        return true;
    }
}

/// <summary>One unit-cube instance to draw: a model transform and a base color.</summary>
/// <param name="Model">Local (unit cube, [-0.5,0.5]^3) → world transform.</param>
/// <param name="Color">Base RGBA color (shaded by a fixed light in the renderer).</param>
public readonly record struct BoxInstance(Matrix4x4 Model, Vector4 Color);

/// <summary>A world-space debug line segment (gizmos, joints, velocity vectors).</summary>
/// <param name="A">Start point (world space).</param>
/// <param name="B">End point (world space).</param>
/// <param name="Color">RGBA color (drawn unlit).</param>
public readonly record struct DebugLine(Vector3 A, Vector3 B, Vector4 Color);

public enum PrimitiveKind
{
    Cube,
    Sphere,
    Capsule,

    /// <summary>
    /// A right circular cylinder along its local +Y axis. Sized like <see cref="Cube"/> — the
    /// caller bakes (diameter, height, diameter) into <see cref="PrimitiveInstance.Model"/> and
    /// leaves <see cref="PrimitiveInstance.Dimensions"/> at <see cref="Vector3.Zero"/> — NOT like
    /// <see cref="Sphere"/>/<see cref="Capsule"/>, whose radius the vertex shader applies. See
    /// <see cref="PrimitiveMeshes.BuildCylinderVertices"/> for why that convention is what lets a
    /// cylinder exist with no shader change, and why non-uniform scale is exactly right here.
    /// <para>
    /// Appended last on purpose: <c>PrimitiveInstancePacker</c>'s submission order follows this
    /// enum, so inserting a value mid-list would reorder the draw batches of every existing scene.
    /// </para>
    /// </summary>
    Cylinder,
}

/// <summary>
/// TASK-150: which pass(es) a <see cref="PrimitiveInstance"/> participates in. Pure CPU-side
/// submission metadata — it is never uploaded to the GPU as per-vertex/per-instance data and
/// never changes <see cref="ShaderSource"/>'s GLSL or either renderer's vertex layout, so adding
/// it needs no <c>PrecompiledShadersSyncTests</c> regeneration.
/// <para>
/// Submission distinguishes the colour pass, sun shadow pass and fixture shadow pass. This is an
/// explicit set of supported material policies rather than an arbitrary flags bitfield: a future
/// translucent blend mode still needs its own pipeline and depth/order contract.
/// </para>
/// </summary>
public enum MaterialKind
{
    /// <summary>Rasterizes normally in both the color pass and the sun shadow depth pass.</summary>
    Standard,

    /// <summary>
    /// TASK-150: an engine-level, render-only, shadow-casting, non-shaded surface — rasterizes
    /// into the sun shadow depth pass (so it still occludes light and casts a shadow) but is
    /// skipped entirely by the color pass (so it is genuinely invisible to the camera, not merely
    /// transparent/discarded per-fragment). Built for Triage's invisible interior ceilings
    /// (<c>TmapWorldBuilder.BuildRenderables</c>), but this is a general engine concept, not a
    /// Triage-only flag — any caller wanting an occluder that must not be seen can use it.
    /// </summary>
    ShadowCastOnly,

    /// <summary>
    /// A self-lit surface: the color pass draws it at its own <see cref="PrimitiveInstance.Color"/>
    /// scaled by <c>ShaderSource.EmissiveGain</c>, ignoring the scene's sun, ambient, point, spot and
    /// shadow terms entirely. It still rasterizes into the sun shadow depth pass, because a glowing
    /// object is still opaque and must still occlude.
    /// <para>This is what makes a small indicator read as a LIT LAMP rather than a brightly-painted
    /// box. A shaded surface can never exceed the light falling on it (<c>colorLinear = vColor * lit</c>),
    /// so an indicator in a dim room dims with the room — the exact opposite of what an indicator is
    /// for. Built for Triage's bed status strips, but this is a general engine concept: any caller
    /// with a surface that emits rather than reflects can use it.</para>
    /// <para><b>Not a bloom/glow post-process</b> — there is no light spill onto neighbours here. A
    /// caller that wants the surroundings lit too must add its own <see cref="PointLight"/> beside the
    /// emissive instance, which is what <c>TriageGame.BedLeds</c> does.</para>
    /// </summary>
    Emissive,

    /// <summary>
    /// A GLOWING OUTLINE around another instance — the "inverted hull" silhouette technique, drawn
    /// entirely by <c>ShaderSource.PrimitiveVert</c>/<c>PrimitiveFrag</c> rather than by an extra
    /// pipeline, pass or post-process.
    /// <para>
    /// <b>What the shader does with it.</b> The vertex stage pushes every vertex out along its own
    /// direction from the mesh centre by <see cref="ShaderSource.OutlineShellThicknessMeters"/> of
    /// WORLD space — a position-based push rather than a normal-based one, so a hard-edged mesh
    /// like the cube stays watertight instead of splitting open at its corners. The fragment stage
    /// then discards every fragment whose surface faces the camera and paints the rest flat in the
    /// instance's own <see cref="PrimitiveInstance.Color"/>. What survives is exactly the band where
    /// the enlarged FAR side of the shell sticks out past the real object's silhouette: an outline
    /// that hugs the object's actual shape from every angle, at a width that does not depend on how
    /// big the object is.
    /// </para>
    /// <para>
    /// <b>A shell is a SECOND instance, emitted by the caller</b> — normally a copy of the instance
    /// it outlines with only <see cref="PrimitiveInstance.Color"/> and
    /// <see cref="PrimitiveInstance.Material"/> replaced. The renderer never generates one, so a
    /// scene that wants no outlines pays nothing at all, and a caller stays free to outline a
    /// composite object part by part (which is what <c>Game.Triage</c>'s pickup preview does).
    /// </para>
    /// <para>
    /// <b>No new renderer arguments.</b> The near-side/far-side test needs the sign of
    /// <c>dot(normal, viewDir)</c>, but it derives that from the instance's own pre-composed mvp
    /// rather than from a camera position — see <c>ShaderSource.PrimitiveVert</c>'s
    /// <c>vShellFacesAway</c>. Deliberate: <c>cameraPosition</c> is an OPTIONAL renderer argument
    /// (it exists for <see cref="SpecularConfig"/>, which most callers leave off), so keying an
    /// outline on it would make every render call site that omits it either throw or silently paint
    /// the outlined object solid.
    /// </para>
    /// <para>
    /// <b>Colour is taken literally</b> — no sun, ambient, point/spot, shadow or tone curve. An
    /// affordance cue has to read the same in a dim ward as in the sunlit street, which is the same
    /// argument <see cref="Emissive"/>'s own doc comment makes. Animate it (a pulse, a fade) by
    /// varying the <see cref="PrimitiveInstance.Color"/> the caller packs each frame; there is no
    /// clock in the shader. That colour's W is still coverage, so a shell can also be dithered away
    /// entirely.
    /// </para>
    /// <para>
    /// <b>Shadows.</b> A shell still rasterizes into the sun depth pass, where it is drawn
    /// UNEXPANDED (the depth-only vertex shader never reads the material lane) — i.e. as an exact
    /// duplicate of the geometry it outlines, leaving the shadow map unchanged. Deliberate: keeping
    /// the depth pass's batch list "every material" is worth more than saving a handful of
    /// duplicated triangles.
    /// </para>
    /// </summary>
    OutlineShell,

    /// <summary>
    /// An <see cref="OutlineShell"/> that is drawn <b>through</b> whatever stands in front of it — the
    /// same inverted-hull silhouette, submitted in a separate pass whose pipeline has the depth test
    /// (and depth write) switched off entirely.
    /// <para>
    /// <b>What it is for.</b> A fixed three-quarter camera puts walls between the viewer and the
    /// things they are steering; an actor that walks behind one simply vanishes. Painting the actor's
    /// silhouette on top of the wall is the standard answer, and it is a UI cue rather than geometry:
    /// it says "this is where the thing you are looking for is", not "this is what light does here".
    /// </para>
    /// <para>
    /// <b>How it differs from <see cref="OutlineShell"/>, and only how.</b> The shader treats the two
    /// identically — same hull expansion, same near-side discard, same literal colour, same lane value
    /// in the packed instance data — so nothing about <see cref="ShaderSource"/> changes and no SPIR-V
    /// is regenerated for it. The whole difference is CPU-side submission: an instance of this
    /// material lands in <c>PrimitiveInstancePacker.XRayBatches</c> instead of
    /// <c>VisibleBatches</c>, which means it skips the depth prepass, skips the ordinary colour pass,
    /// and is drawn afterwards by a pipeline with <c>DepthTestEnable = false</c>.
    /// </para>
    /// <para>
    /// <b>It casts no shadow and occludes nothing.</b> Unlike every other material it is excluded from
    /// <c>PrimitiveInstancePacker.Batches</c> as well, so the shadow depth passes never see it. A cue
    /// that is deliberately visible through a wall must not also throw that wall's light away — and,
    /// because its own pipeline never writes depth, it cannot hide anything drawn after it either.
    /// </para>
    /// <para>
    /// <b>Order between two of them is undefined by depth</b> — with no depth test, whichever is
    /// submitted last wins on any overlapping pixel. That is acceptable for a silhouette cue (they are
    /// flat single-colour bands) and is the direct cost of the technique; a caller that needs several
    /// to read as ordered must space them out or vary their colours.
    /// </para>
    /// </summary>
    XRayOutlineShell,

    /// <summary>
    /// A genuinely TRANSLUCENT self-lit surface: drawn by a blending pipeline that ADDS its colour to
    /// whatever is already there, weighted by <see cref="PrimitiveInstance.Color"/>'s W.
    ///
    /// <para><b>What this buys over <see cref="Emissive"/>.</b> Every other material on this pipeline
    /// is opaque, and a partial W is realized as an ordered dither — a stipple you can see through,
    /// not a blend (see <c>ShaderSource</c>'s coverage-dithering note for why that trade was right for
    /// surfaces). That is exactly wrong for something meant to READ as light rather than as a punctured
    /// surface: a shaft of glow, a beam, an aurora, a force field. Here W is a real continuous weight,
    /// so 0.2 is a fifth as bright rather than a fifth of the pixels.</para>
    ///
    /// <para><b>ADDITIVE, and that is a design decision rather than a shortcut.</b> Ordinary
    /// alpha-over (<c>SrcAlpha, OneMinusSrcAlpha</c>) is only correct drawn BACK TO FRONT, and this
    /// pipeline's submission order is chosen for batching — <see cref="PrimitiveInstancePacker"/>
    /// groups by (kind, material) so a scene of 900 instances costs a handful of draws. Adding
    /// alpha-over properly therefore means a per-frame depth sort that breaks that model. Addition is
    /// COMMUTATIVE, so a glow needs no sort at all and the batching is untouched — and for the things
    /// this material is for, adding light is also the physically right model.
    /// <b>The limit that follows: a glow can only brighten.</b> Dark or tinted glass is NOT expressible
    /// here and would need the sorted pass this deliberately does not add.</para>
    ///
    /// <para><b>Depth-tested but not depth-writing.</b> A glow is correctly hidden by anything solid in
    /// front of it, and never hides anything itself — including another glow, which is what lets two
    /// overlap and simply read as brighter.</para>
    ///
    /// <para><b>Casts no shadow, and is drawn in its own pass after the colour pass.</b> Light passing
    /// through a volume does not occlude the sun, so it is kept out of the depth passes entirely
    /// (<see cref="PrimitiveInstancePacker.Batches"/>) — the same call
    /// <see cref="XRayOutlineShell"/> makes, for its own reason. It shades exactly as
    /// <see cref="Emissive"/> does and shares that material's shader code; what separates the two is
    /// which batch list they land in and therefore which pipeline draws them.</para>
    /// </summary>
    AdditiveGlow,

    /// <summary>
    /// 2026-09-19: geometry drawn THROUGH walls at its own size — the other half of a
    /// through-the-wall cue, and the thing <see cref="XRayOutlineShell"/> cannot be.
    ///
    /// <para><b>What it is.</b> An ordinary self-lit surface — it shades exactly as
    /// <see cref="Emissive"/> does and shares that material's shader code — submitted into
    /// <c>PrimitiveInstancePacker.XRayBatches</c>, so like <see cref="XRayOutlineShell"/> it is drawn
    /// after the colour pass by a pipeline with the depth test off. Visible through walls, casts no
    /// shadow, occludes nothing. Unlike that material it is NOT expanded into an inverted hull and
    /// nothing is discarded, so what it paints is the subject's own silhouette at true size.</para>
    ///
    /// <para><b>What it is for.</b> A caller draws a subject twice in this material — once inflated
    /// and bright, once at true size and dark — and gets a dark figure inside a bright halo: the
    /// wallhack-glow look, which is what a player asked for by name. Doing it with geometry rather
    /// than with <see cref="XRayOutlineShell"/>'s hull is not stylistic. Batches are grouped by
    /// (kind, material) and submitted KIND-major, so a sphere's hull would be drawn after a cube's
    /// fill and leave bright patches inside the figure; two passes of one material and one kind land
    /// in a single batch, where submission order holds and the halo is reliably underneath.</para>
    ///
    /// <para><b>Order between two of them is decided by submission</b>, exactly as
    /// <see cref="XRayOutlineShell"/>'s own note says and for the same reason: with no depth test,
    /// whichever is submitted last wins on an overlapping pixel. Here that is the mechanism rather
    /// than a caveat.</para>
    /// </summary>
    XRayFill,

    /// <summary>
    /// 2026-09-20: a SPECULAR HIGHLIGHT that only exists at the angle a real one would — a mirror
    /// lobe evaluated per fragment against the sun and the eye, added to whatever is already there.
    ///
    /// <para><b>What it is for.</b> A polished blade does not glow; it catches the light at one angle
    /// and goes dark again the moment it turns. Drawing that as a scripted band travelling along the
    /// steel is what this replaces, and a player's verdict on it was 「今の刀の先端が白くなるのは
    /// よくわからない」 — because a highlight that appears regardless of which way the weapon is
    /// pointing is not read as a reflection at all. Here the brightness IS the angle: the flash
    /// happens when the surface's normal bisects sun and eye, so it fires on some swings and not
    /// others without anything scheduling it.</para>
    ///
    /// <para><b>What the shader does.</b> Blinn-Phong, one term, no diffuse and no ambient:
    /// <c>colour * pow(max(dot(N, H), 0), s)</c> with <c>H</c> the half-vector between the sun and
    /// the eye. The normal is flipped toward the viewer first, so a thin plate reflects off whichever
    /// of its faces is being looked at rather than only one. The exponent <c>s</c> comes from the
    /// instance's own <see cref="SurfaceFinish.Roughness"/>, which is what makes the flash SHARP:
    /// this material wants exponents in the thousands, far past anything the ordinary microfacet path
    /// is asked for, and a per-scene shininess could not give it one without widening every other
    /// specular in the frame.</para>
    ///
    /// <para><b>CPU-side it is <see cref="AdditiveGlow"/> exactly</b> — same blending pipeline, same
    /// batch list, excluded from the depth prepass, the colour pass and every shadow map, for the
    /// same reasons that material's own doc comment gives. Its alpha is likewise a blend WEIGHT and
    /// not coverage, and it is multiplied by the lobe as well, so a highlight that is off-angle
    /// contributes nothing rather than contributing black. Appended last in the packer's material
    /// order so every existing material keeps its submission position and no golden moves.</para>
    /// </summary>
    BladeGlint,

    /// <summary>
    /// Shades and writes depth exactly like <see cref="Standard"/> in the colour pass, but does not
    /// block the sun or point-light fixtures. For visually translucent cutaway glass whose ordered
    /// colour coverage must not produce a solid rectangular shadow. The shader uses Standard's code;
    /// only the CPU draw-batch membership differs.
    /// </summary>
    ShadowReceiveOnly,
}

/// <summary>
/// 2026-08-20: a per-instance PROCEDURAL surface texture, selected by appearance class, so a large
/// flat-coloured slab stops reading as sheet plastic. The fragment shader evaluates it from the
/// fragment's own world position (<c>ShaderSource.PrimitiveFrag</c>'s <c>SurfaceDetailShading</c>).
/// <para>
/// <b>A class may perturb the normal, the albedo, or both, and which one is an art-direction
/// decision rather than an implementation detail.</b> <see cref="Grain"/> and
/// <see cref="SlabSeams"/> are bump maps: they describe real relief (grit, a joint between paving
/// slabs) whose shading response is the point. <see cref="Patches"/> touches only the albedo, in
/// hard-edged flat steps, because it describes a surface whose variation is in COLOUR rather than in
/// shape. See its own doc comment — that distinction is the whole reason it exists.
/// </para>
/// <para>
/// <b>Procedural rather than a sampled map, and that is the engineering choice, not a
/// shortcut.</b> A texture-based normal map would need, in the primitive pipeline specifically: a
/// per-instance UV parameterization (there is none — every shape shares one unit mesh), a tangent
/// frame, a normal-map image plus sampler/descriptor (that pipeline's descriptor set is currently
/// shadow maps only), and an asset pipeline to author the maps. Evaluating the field from
/// <c>vWorldPos</c> instead needs none of those, costs no memory, tiles seamlessly across the
/// separately-drawn cells of a floor at any scale, and matches what this engine already does for
/// particle sprites (procedurally generated, no texture anywhere on that path).
/// </para>
/// <para>
/// <b>Frequencies are deliberately COARSE.</b> Measured on Triage's own stage-fit camera, a 50 m
/// stage across 1280 px is ~26 px/m, so a 5 cm asphalt grain is sub-pixel: it would contribute
/// aliasing rather than texture. This engine has no mip chain or normal-map filtering to tame that
/// with, so a feature has to be worth a few pixels to be worth evaluating at all.
/// </para>
/// <para>
/// <see cref="None"/> is the default and is byte-identical to the pre-2026-08-20 shading — the
/// shader early-returns on it, so every existing caller is unaffected.
/// </para>
/// <para>
/// Named by APPEARANCE, not by material: "grass" and "asphalt" are Triage art direction, whereas
/// "flat colour patches" and "dense fine grain" are what the shader actually computes. A second game
/// wanting a stylised sand flat reaches for <see cref="Patches"/> without the enum having to learn
/// about sand.
/// </para>
/// </summary>
public enum SurfaceDetail
{
    /// <summary>Perfectly smooth: the interpolated geometric normal and the instance's own flat
    /// colour, unmodified. The default, and what every instance authored before this existed
    /// gets.</summary>
    None,

    /// <summary>Dense, shallow grain — asphalt, concrete, unfinished plaster. High frequency, low
    /// amplitude: it breaks up a specular sheen and a flat albedo without reading as bumps.</summary>
    Grain,

    /// <summary>
    /// Irregular patches of FLAT colour with hard edges, and <b>no normal perturbation at all</b> —
    /// stylised grass, moss, a sand flat, a tiled-but-unbevelled floor.
    /// <para>
    /// <b>Why flat rather than bumpy, 2026-08-20.</b> This slot previously held a two-octave bump
    /// (named <c>Clumped</c>) and the user's verdict on the grass it produced was 「ちょっと不自然」.
    /// That is the right call: a smoothly-shaded noise field is a photoreal technique, and this
    /// engine's whole visual language is flat-coloured boxes with hard edges and a bright palette. A
    /// soft grey-green cloud in the middle of that reads as a smear, not as ground.
    /// </para>
    /// <para>
    /// The shape still comes from noise — so the patches are organic outlines rather than a quilt of
    /// squares — but the value is QUANTISED to a few levels, which gives each patch one flat tone and
    /// a hard boundary. It is also strictly less work than the bump it replaced: no gradient, no
    /// tangent-space slope, no normal to renormalize.
    /// </para>
    /// </summary>
    Patches,

    /// <summary>A grid of grooves at <c>ShaderSource.SlabSeamPeriodMeters</c> plus
    /// <see cref="Grain"/>'s own grain between them — paving slabs, tiling, panelled cladding. The
    /// only detail class with a preferred orientation and a fixed world period, because a seam grid
    /// that does not line up with the geometry it sits on reads as a defect. Both the joint's width
    /// and its depth are fractions of that period, so the period alone scales the whole pattern.</summary>
    SlabSeams,

    /// <summary>
    /// <see cref="SlabSeams"/> on a surface that MOVES: the same grooves, sampled relative to the
    /// instance's own centre instead of in world space, so the pattern travels with the object
    /// (2026-09-04, user report 「ゴンドラの地面のテクスチャが固定されていません。動くとテクスチャが
    /// ずれます」).
    /// <para>
    /// <b>Every other class is anchored in WORLD space, on purpose</b> — that is what lets a floor
    /// built from hundreds of separately-drawn cells carry one continuous texture with no UVs (see
    /// this enum's own doc comment). The cost is that a surface which moves slides through a
    /// stationary field: measured on Triage's rooftop gondola, a 2 m platform crossing ~20 m of rope
    /// swept about sixty slab periods across its own floor every trip.
    /// </para>
    /// <para>
    /// <b>What it cancels.</b> The shader samples the fragment's offset from the instance's own
    /// origin IN THE INSTANCE'S OWN AXES — the vertex stage's <c>local * worldScale</c>, i.e. before
    /// the rotation is applied. Both translation and rotation are therefore cancelled analytically,
    /// and the field is genuinely nailed to the object.
    /// </para>
    /// <para>
    /// <b>It sampled <c>vWorldPos - vWorldCenter</c> until 2026-09-06, and that cancelled translation
    /// ONLY.</b> The reasoning for it was that the one case this exists for is a hanging platform
    /// whose swing is a few degrees — measured at 1.6 cm of phase shift, under 5% of a seam period —
    /// and that a genuinely object-space field would need the pre-rotation local position passed
    /// through from the vertex stage. The second half turned out to cost one <c>vec3</c> varying,
    /// because that stage already computes exactly that vector for its own world-position term; the
    /// first half was re-reported by the user against the same gondola
    /// (「ゴンドラはテクスチャが固定されていないバグ」) and is what makes a swing rock its own floor's
    /// seams. The name is kept: what a caller wants from this is still "the pattern follows the
    /// object".
    /// </para>
    /// <para><b>The origin is the instance's own, which for an imported
    /// <see cref="PrimitiveInstance.Mesh"/> is its authored ORIGIN and need not be centred on it —
    /// see <see cref="PrimitiveInstance.WorldBounds"/>, where the same assumption has been got wrong
    /// three times. Every other detail class is byte-identical (this is a select on the existing
    /// detail selector).</b></para>
    /// <para>
    /// <b>Only worth it for a large, flat, visibly-textured mover.</b> A vertical lift is already
    /// unaffected (the seam lattice is sampled on the two axes across an up-facing surface, so purely
    /// vertical motion does not move it at all — Triage's construction hoists keep plain
    /// <see cref="SlabSeams"/> for exactly that reason).
    /// </para>
    /// </summary>
    SlabSeamsFollowInstance,

    /// <summary>
    /// Directional grain — brushed stainless, a rolled alloy panel, an extruded handrail. Fine
    /// streaks along ONE axis of the surface's tangent frame, perturbing the normal a little and the
    /// albedo a little.
    /// <para>
    /// <b>Not true anisotropic shading.</b> A real brushed metal spreads its highlight into a streak
    /// along the grain, which needs an anisotropic BRDF and a per-instance tangent direction; this
    /// has neither (see this enum's own note on why the pipeline has no UVs or tangent frame). What
    /// it does instead is perturb the normal ACROSS the grain only, which breaks one highlight into
    /// many parallel ones — close enough that the eye reads "brushed" at the range it matters, at
    /// the cost of an extra noise evaluation and nothing else.
    /// </para>
    /// <para>Pair it with <see cref="SurfaceFinish.Steel"/>; on a dielectric it reads as sanded
    /// paint, which is occasionally what is wanted and usually a mistake.</para>
    /// </summary>
    BrushedMetal,

    // ---- Everything from BrushedMetal on is INSTANCE-ANCHORED ---------------------------------
    //
    // The four fine classes sample their field in the INSTANCE'S own frame, like
    // SlabSeamsFollowInstance and unlike the four coarse ones. They are authored for things that
    // MOVE -- a carried bandage, a pushed trolley, a walking character's sleeve -- and a
    // world-anchored field on a moving surface slides across it, which is the defect
    // SlabSeamsFollowInstance was added to fix for one swinging gondola floor. At these
    // frequencies it is far worse: a sprinting character sweeps roughly eighty weave periods a
    // second, against that gondola's sixty per trip.
    //
    // The coarse classes stay world-anchored because their job is the opposite -- one continuous
    // texture across the hundreds of separately-drawn cells a floor is built from. The cost of
    // anchoring is that two same-sized parts would carry identical grain, which the vertex stage
    // breaks up with a scale-derived phase (see PrimitiveVert; the phase cannot come from the
    // position without reintroducing the slide).

    /// <summary>
    /// A woven lattice — a sheet, a uniform, a mattress cover, a canvas awning. Two perpendicular
    /// ridge families at right angles, so the surface catches light in a cross-hatch instead of as
    /// one flat plane, plus a faint albedo mottle for the irregularity of real cloth.
    /// <para>Its period is deliberately about 1.5 cm rather than a real thread pitch: below roughly
    /// a centimetre the pattern is sub-pixel even in a close-up and contributes aliasing rather than
    /// texture (see this enum's own note on coarse frequencies). What it is buying is the READING
    /// "this is cloth, not painted metal", which survives being coarser than life.</para>
    /// </summary>
    WovenFabric,

    /// <summary>
    /// Timber — stretched, roughly parallel grain lines with the occasional darker streak, in the
    /// albedo mostly and the normal slightly. Reads as a sawn board rather than as a brown box,
    /// which is the difference between a crate and a cube painted brown.
    /// <para>The grain runs along the first tangent axis, which for an up-facing surface is world X.
    /// A board authored running along Z therefore gets cross-grain; that is a real limitation of
    /// having no per-instance UVs, and it is acceptable because timber's grain direction is not
    /// something a player checks — its PRESENCE is.</para>
    /// </summary>
    WoodGrain,

    /// <summary>
    /// A field of shallow dimples — moulded rubber, a tyre sidewall, a non-slip tread plate, a
    /// textured grip. The bumpiest class here, and the only one whose relief is meant to be seen
    /// rather than merely felt in the shading.
    /// </summary>
    Pebbled,

    /// <summary>
    /// A dust plain pitted at two scales: broad shallow hollows with a fine powder grain over them,
    /// and a MONOCHROME albedo mottle (2026-09-18, 「レゴリス的な地面のテクスチャ」).
    /// <para>
    /// <b>Why it is neither <see cref="Grain"/> nor <see cref="Patches"/>, the two it sits between.</b>
    /// Grain is a single fine frequency — grit on a made surface, which reads as concrete however it
    /// is coloured, and a made surface is exactly what an unworked plain is not. Patches quantises to
    /// flat steps with hard edges, which is right for a lawn (a mosaic of separate plants) and wrong
    /// for dust (one continuous material dented at every scale). What this class adds over both is the
    /// COARSE octave: hollows a metre or two across, which at this game's ~26 px/m camera are the only
    /// features large enough to read as ground rather than as noise, and which is what makes a plain
    /// look walked-on rather than poured.
    /// </para>
    /// <para>
    /// <b>The mottle is deliberately colourless.</b> Every other class that tints shifts hue as well
    /// as value (see <see cref="Patches"/>' warm-to-cool ramp); regolith is famously the one surface
    /// where a hue shift would be a lie, so this one scales brightness only and leaves the caller's
    /// own colour to say which grey world this is.
    /// </para>
    /// <para>
    /// <b>World-anchored like every class but <see cref="SlabSeamsFollowInstance"/></b>, which is what
    /// lets a plain built from hundreds of separately-drawn floor cells plus a map apron carry one
    /// continuous field across every seam between them.
    /// </para>
    /// </summary>
    Regolith,

    /// <summary>
    /// 2026-09-20: a coarse, hard-edged PIXEL lattice on cloth — visible square texels, the
    /// Minecraft idiom, on a garment (user request 「ドット絵っぽい荒いテクスチャ」「荒さはマイン
    /// クラフトぐらいのイメージで」).
    /// <para>
    /// <b>Sampled in the instance's OWN frame</b>, like <see cref="SlabSeamsFollowInstance"/> and
    /// unlike everything else here — a character's every limb moves, and a world-anchored lattice
    /// would swim through a walking figure exactly the way the gondola's floor did. Nailing it to
    /// the part also makes the lattice line up with that part's own box, which is what a pixel
    /// texture on a boxy model has to do to read as a texture rather than as noise laid over one.
    /// </para>
    /// <para>
    /// <b>Axis-aligned squares, by projecting on the dominant LOCAL axis</b> — a triplanar select
    /// rather than this file's usual normal-derived tangent frame. That frame rotates continuously
    /// around a curved surface, which smears the cells into diamonds; picking the face the fragment
    /// is on and quantising the other two local axes gives a genuine square on every face of a box,
    /// which is what every part of <c>PlayerVisualRig</c> is. The lattice is METRIC rather than a
    /// texel COUNT per part, which would draw rectangles on anything that is not square, and no part
    /// of a figure is.
    /// </para>
    /// <para>
    /// <b>Quiet, and on ONE garment.</b> It shipped at ±12% across the whole figure. A plain tunic
    /// at that amplitude read as a chequerboard of two blues rather than as cloth, so the user took
    /// the texture off it; bare skin read as a rash, so that went too; and a face painted in texels
    /// read as something drawn on however it was tuned — a full relief first, then a nose alone, and
    /// modelling the face as geometry instead was rejected as well. What is left is the case the
    /// idiom was good for: a woven lab coat, at an amplitude low enough to break a flat panel up
    /// without being a pattern (2026-09-20, three rounds of the user's own review).
    /// </para>
    /// <para><b>No normal perturbation at all</b>, for the reason <see cref="Patches"/> records:
    /// the look is flat-shaded blocks, and a bump inside a texel would round off the very edge the
    /// texel exists to show. This class is pure albedo.</para>
    /// </summary>
    PixelCloth,

    /// <summary>Fine, restrained concrete grain for the fixed hospital floor. World anchored so
    /// adjacent floor cells share one field without seams.</summary>
    FineGrain,

    /// <summary>
    /// Pile carpet (2026-10-06, 「スポーン・回収ポイントはカーペットみたいな雰囲気のテクスチャに」):
    /// almost no relief — a carpet is soft, and a bump strong enough to read would make it rubber —
    /// but a strong, fine brightness mottle, which is what fibres catching light look like from a few
    /// metres. Its finest period stays above a few pixels at the stage camera, like
    /// <see cref="FineGrain"/>, so it reads as nap rather than aliasing. World anchored, so a mat
    /// built from many cells is one continuous rug.
    /// </summary>
    Carpet,

    /// <summary>
    /// Building windows (2026-10-06, 「ビルには側面も含めて窓をつけてください。夜は光らせて」): a
    /// storey/bay grid of glass on every vertical face, laid out in the instance's own frame so it
    /// meets the corners, lit window by window at night. NOT a bump class: the shader paints it
    /// directly and never passes it to the relief code. No roof windows, none on the ground floor.
    /// </summary>
    FacadeWindows,

    /// <summary>
    /// Open water (2026-10-06 look pass): the sea's tiles shade from the WAVE FIELD itself, not from
    /// their own faces. Normal and crest brightness are evaluated per pixel from the same two-train
    /// swell the CPU positions the tiles with, so neighbouring tiles shade continuously and the
    /// sea stops reading as a mosaic of squares. A cube carries no size in
    /// <see cref="PrimitiveInstance.Dimensions"/>, so that lane carries the field: X = the swell's
    /// drift in metres, Y = amplitude in metres (0 = flat), Z = wavenumber (radians per metre). NOT a
    /// bump class: the shader branches on it before the relief code.
    /// </summary>
    Water,

    /// <summary>
    /// <see cref="Grain"/> on ground that is MOVING past the camera without the geometry moving — the
    /// train map, where the train stands still and the world scrolls. Shaded exactly as Grain, but
    /// sampled with <see cref="SceneLighting.PatchesOffsetXz"/> added, the same offset that runs the
    /// lawn (<see cref="Patches"/>) with the ground. Plain Grain is world-anchored, so a static
    /// ballast strip carried its stones along WITH the train (2026-10-08, user report 「線路下の地面の
    /// テクスチャが電車といっしょに動いている」). A class of its own, not an offset on all Grain: the
    /// train's own decks and fittings use Grain too, and they must not swim.
    /// </summary>
    ScrollingGrain,
}

/// <summary>A shared primitive mesh instance. Dimensions are (radius, halfLength, unused) for capsules and radius in X for spheres.
/// <para><see cref="Color"/>'s <b>W is coverage</b>, realized as an ordered dither in the fragment
/// shader (see <c>ShaderSource.PrimitiveFrag</c>): 1 is opaque — what every caller before
/// 2026-08-20 passed, and byte-identical to the old hardcoded behaviour — and a lower value drops a
/// stable, screen-space-patterned share of the surface's fragments so the scene behind shows
/// through. This is a per-fragment discard inside the existing OPAQUE pipeline, deliberately not a
/// blended one: see <see cref="MaterialKind"/>'s doc comment on why a genuinely blended material
/// would need its own pipeline, and <c>ShaderSource.PrimitiveFrag</c> for why dithering was the
/// better trade here.</para>
/// <para>
/// <b><see cref="Mesh"/> replaces the shape, not the instance</b> (2026-09-03). Set it and this
/// instance draws that <see cref="MeshData"/>'s triangles instead of <see cref="Kind"/>'s unit
/// shape, while keeping every other field's meaning — colour, coverage, material, detail, and the
/// <see cref="Model"/> that scales/rotates/places it. That is the whole design: externally authored
/// geometry becomes an ordinary member of a frame's instance list, so it reaches the lighting, the
/// shadow maps, the depth prepass, the playtest filmstrip and the trailer tool with no new plumbing
/// on any of those paths. See <see cref="MeshData"/> for why it is reference-identified, and
/// <c>PrimitiveInstancePacker</c> for how a meshed instance is batched.
/// </para>
/// <para>
/// <b><see cref="Finish"/> says what the surface is MADE OF</b> (2026-09-19) — roughness and
/// metallic, which together select a real microfacet highlight, a Fresnel rim and a reflection of
/// the scene's ambient sky. <see langword="null"/>, the default and what every instance authored
/// before it existed carries, takes the pre-existing shading path byte-for-byte: the one global
/// <see cref="SpecularConfig"/> sheen. See <see cref="SurfaceFinish"/> for why that is opt-in rather
/// than a new default, and note the division of labour between the three surface fields —
/// <see cref="Color"/> is what colour it is, <see cref="Detail"/> is how it is SHAPED, and this is
/// how it REFLECTS.
/// </para>
/// <para><see cref="TextureLayer"/> (2026-10-06): which layer of the frame's
/// <see cref="SceneLighting.SurfaceTextures"/> is projected onto this surface triplanarly in world
/// space, multiplying <see cref="Color"/> by a greyscale detail map — or −1, the default, for none.
/// <see cref="TextureTileMetres"/> is how many metres one repeat of the image covers (0 = 2 m). A
/// layer index past the set's end, or a frame with no set, renders as untextured.</para>
/// <para><b>Photo decal</b> (a NEGATIVE <see cref="TextureTileMetres"/>, 2026-10-06, for Fuji's
/// board): layers <see cref="TextureLayer"/>, +1 and +2 are a photo's R, G and B, mapped once —
/// no tiling, no triplanar blend — across a board standing in the world XY plane. A cube carries no
/// size in <see cref="Dimensions"/>, so that lane holds the mapping instead: X = the world x of the
/// photo's left edge, Y = the world y of its top edge, Z = its width in metres (the photo is
/// square). The photo multiplies <see cref="Color"/> per channel, emissive or not.</para>
/// <para><b>Colour texture</b> (<see cref="TextureInColour"/>, 2026-11-11): layers <see cref="TextureLayer"/>, +1 and +2
/// are an image's R, G and B (each normalised to mean 0.5, see <see cref="SurfaceTextureSet.Builder.AddColour"/>),
/// projected triplanarly and tiled exactly like a detail map, multiplying <see cref="Color"/> per channel. With
/// <see cref="Color"/> set to the image's mean colour (<see cref="ColourTexture.Mean"/>) the surface shows the image's
/// own colours: wood grain, plaster, fabric.</para></summary>
public readonly record struct PrimitiveInstance(
    PrimitiveKind Kind,
    Matrix4x4 Model,
    Vector3 Dimensions,
    Vector4 Color,
    MaterialKind Material = MaterialKind.Standard,
    SurfaceDetail Detail = SurfaceDetail.None,
    MeshData? Mesh = null,
    SurfaceFinish? Finish = null,
    int TextureLayer = -1,
    float TextureTileMetres = 0f,
    bool TextureInColour = false)
{
    /// <summary>
    /// This instance's world-space bounding box — the size it will actually be DRAWN at.
    /// <para>
    /// <b>Exists because the size lives in two different places depending on the kind, and reading
    /// only one of them is a mistake that has been made three times.</b> A cube and a cylinder bake
    /// their size into <see cref="Model"/>, so the lengths of its first three rows are their extents.
    /// A sphere and a capsule do NOT: their model matrix carries unit scale and the vertex shader
    /// applies the radius from <see cref="Dimensions"/> (see this type's own summary and
    /// <see cref="PrimitiveKind.Cylinder"/>). Code that measured every kind off the model rows
    /// therefore treated every sphere in the scene as a one-metre cube: measured on Triage's ambulance,
    /// that inflated a 0.37 m tyre into a box the highlight camera's occluder set, its review
    /// instrument and its vehicle-POV diagnostic all agreed was in the way when nothing was
    /// (2026-08-22).
    /// </para>
    /// <para>
    /// <b>The tight axis-aligned box, ROTATION INCLUDED.</b> The first version took the LENGTHS of the
    /// model's three rows as the world extents — but a row is the world image of a LOCAL axis, so that
    /// reading is only right for a diagonal basis. Every wall Triage authors that runs north-south is a
    /// quarter turn (<c>TmapWorldBuilder</c> yaws each segment by <c>atan2(-dz, dx)</c>), and those came
    /// back with X and Z swapped: an 8.5 m wall along Z was reported as 8.5 m along X. Measured at yaw
    /// 30 degrees the old reading said 8.5 x 3.0 x 0.2 where the truth is 7.46 x 3.0 x 4.42 — under-
    /// estimating one axis twentyfold, so it was not even conservative. The extent along a world axis is
    /// the absolute COLUMN sum of the 3x3, which is exact for any rotation
    /// (<c>PrimitiveInstanceBoundsTests</c> checks it against all eight corners).
    /// </para>
    /// </summary>
    public (Vector3 Min, Vector3 Max) WorldBounds()
    {
        // A mesh is the THIRD place a size can live, and it is neither of the two above: its extent
        // is in its own vertices, and Model only places them. Reading it off Model's rows the way a
        // cube is read treats every imported model as a one-metre box -- the identical mistake this
        // method's summary records for spheres, so it is answered here rather than left to be made a
        // fourth time. Note the mesh's local box need not be centred on its own origin (an OBJ is
        // authored wherever the modeller left it), so the CENTRE is transformed too.
        if (Mesh is { } mesh)
        {
            var (localMin, localMax) = mesh.Bounds;
            var localHalf = (localMax - localMin) * 0.5f;
            var meshCentre = Vector3.Transform((localMin + localMax) * 0.5f, Model);

            // Absolute column sums again -- exact for any rotation, for the reason the summary gives
            // -- but weighted by the mesh's OWN half extents instead of the unit cube's 0.5.
            var meshHalf = new Vector3(
                (MathF.Abs(Model.M11) * localHalf.X) + (MathF.Abs(Model.M21) * localHalf.Y) + (MathF.Abs(Model.M31) * localHalf.Z),
                (MathF.Abs(Model.M12) * localHalf.X) + (MathF.Abs(Model.M22) * localHalf.Y) + (MathF.Abs(Model.M32) * localHalf.Z),
                (MathF.Abs(Model.M13) * localHalf.X) + (MathF.Abs(Model.M23) * localHalf.Y) + (MathF.Abs(Model.M33) * localHalf.Z));

            return (meshCentre - meshHalf, meshCentre + meshHalf);
        }

        var centre = Model.Translation;
        var half = Kind switch
        {
            PrimitiveKind.Sphere => new Vector3(Dimensions.X),

            // (radius, halfLength, unused) along the capsule's own +Y. Bounding it by radius plus
            // half-length on all three axes is the one reading that holds whichever way it lies, and a
            // capsule's own axis is not recoverable from a unit-scale model matrix.
            PrimitiveKind.Capsule => new Vector3(
                Dimensions.X + Dimensions.Y, Dimensions.X + Dimensions.Y, Dimensions.X + Dimensions.Y),
            _ => new Vector3(
                MathF.Abs(Model.M11) + MathF.Abs(Model.M21) + MathF.Abs(Model.M31),
                MathF.Abs(Model.M12) + MathF.Abs(Model.M22) + MathF.Abs(Model.M32),
                MathF.Abs(Model.M13) + MathF.Abs(Model.M23) + MathF.Abs(Model.M33)) * 0.5f,
        };

        return (centre - half, centre + half);
    }
}
