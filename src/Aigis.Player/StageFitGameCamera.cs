using System.Numerics;
using Aigis.Render;

namespace Aigis.Player;

/// <summary>
/// <see cref="IGameCamera"/> wrapper around <see cref="StageFitCamera"/>'s one-shot analytic fit
/// — computed once (in the constructor, from the loaded level's own stage box) and never moved
/// again. <see cref="ApplyDrag"/>/<see cref="ApplyScroll"/> are deliberate no-ops, not
/// unimplemented stubs: this camera has no orbit/zoom concept at all (Part 1 of TASK-053's spec
/// — "no damping, no dead-zone, no shake, no player tracking, no zoom-to-fit"), so a mouse drag
/// or scroll during play simply has nothing to do.
/// <para>Moved here from <c>games/Game.Triage</c> under TASK-070 (2026-08-03): despite the name
/// TASK-053 originally gave it, this class and <see cref="StageFitCamera"/> have zero Triage-
/// specific logic — both operate purely on a caller-supplied stage bounding box — so any
/// <c>games/*</c> title that wants a static, one-shot stage-fit camera instead of
/// <see cref="OrbitCamera"/> can now reuse it directly. Renamed from <c>TriageStageCamera</c> to
/// <c>StageFitGameCamera</c> to match; Triage's own call sites were updated in the same
/// change.</para>
/// </summary>
public sealed class StageFitGameCamera : IGameCamera
{
    private const float NearClip = 0.1f;

    private readonly Vector3 _eye;
    private readonly Vector3 _target;
    private readonly float _farClip;

    /// <param name="stageCenterAtGround">Stage bounding box center at Y=0, world space.</param>
    /// <param name="spanX">Stage bounding box width (world X extent, metres).</param>
    /// <param name="spanZ">Stage bounding box depth (world Z extent, metres).</param>
    /// <param name="topY">Stage bounding box height above the floor (metres).</param>
    /// <param name="farClipFloor">Optional minimum far plane for authored distant scenery.</param>
    public StageFitGameCamera(Vector3 stageCenterAtGround, float spanX, float spanZ, float topY, float farClipFloor = 0f)
    {
        (_eye, _target, _farClip) = StageFitCamera.Fit(stageCenterAtGround, spanX, spanZ, topY);
        _farClip = MathF.Max(_farClip, farClipFloor);
    }

    /// <summary>Exposed for tests/diagnostics that want to assert on the solved placement
    /// directly rather than only through the resulting matrix.</summary>
    public Vector3 Eye => _eye;

    public Vector3 Target => _target;

    /// <summary>The near plane <see cref="ViewProjection"/> uses (every instance's). With <see cref="FarClipMetres"/>,
    /// <see cref="Eye"/>, <see cref="Target"/> and <see cref="FovYRadians"/> it is
    /// everything a caller needs to cut this camera's picture into off-centre slices
    /// (<see cref="Camera.OffCenterViewProjection(System.Numerics.Vector3, System.Numerics.Vector3, System.Numerics.Vector3, FrustumWindow, float, float)"/>) that reassemble to the same frame.</summary>
    public const float NearClipMetres = NearClip;

    /// <summary>The far plane <see cref="ViewProjection"/> uses — see <see cref="NearClipMetres"/>.</summary>
    public float FarClipMetres => _farClip;

    /// <summary>The vertical field of view <see cref="ViewProjection"/> uses (every instance's) — see
    /// <see cref="NearClipMetres"/> for why it is exposed.</summary>
    public const float FovYRadians = Camera.DefaultFovYRadians;

    public Matrix4x4 ViewProjection(float aspect) =>
        Camera.ViewProjection(_eye, _target, Vector3.UnitY, FovYRadians, aspect, NearClip, _farClip);

    public void ApplyDrag(Vector2 pixelDelta)
    {
        // Intentional no-op — see class doc comment.
    }

    public void ApplyScroll(float scrollDelta)
    {
        // Intentional no-op — see class doc comment.
    }
}
