using System.Numerics;
using Aigis.Render;

namespace Aigis.Player;

/// <summary>
/// A static, one-shot analytic stage-fit camera — originally Triage's gameplay camera (TASK-053,
/// docs/plans/triage-full-port.md's Part 1 spec), moved here under TASK-070 since it has no
/// Triage-specific logic: any <c>games/*</c> title with a rectangular stage bounding box can
/// reuse it. It is a <b>static, one-shot geometric fit</b> computed once when a level loads —
/// NOT a per-frame follow/orbit camera. There is no damping, dead-zone, shake, or player
/// tracking; the <i>stage bounding box</i> (not player positions) drives it, and pitch/yaw are
/// both constant.
///
/// <para><b>CRITICAL — do not "fix" this into a camera-relative control scheme.</b> Because yaw
/// is fixed at 0 and this camera never rotates, player input is mapped WORLD-RELATIVE: stick/WASD
/// X maps to world X, Y maps to world Z, unconditionally. A later reader adding "rotate movement
/// input by the camera's yaw" (the usual fix for a rotating third-person camera) would silently
/// change gameplay feel, because this camera's yaw is a constant, not a live value to compensate
/// for.</para>
///
/// <para><b>Derived for Aigis's own basis, not transcribed from Unity.</b> Aigis is right-handed,
/// Y-up. The invariant this engine-level class actually needs is just <b>the camera always sits
/// on the <c>+spanZ</c> side of the stage box and looks toward <c>-spanZ</c></b> — it never reads
/// a compass direction. "South" below is Triage's own tilemap vocabulary for that same <c>+Z</c>
/// side (see <c>Aigis.Content.TmapCoordinates</c>'s class doc for the convention already
/// established for level geometry there — a <c>games/*</c> title using a different compass
/// convention for its own level format still gets a correctly-placed camera, since this class
/// never imports that vocabulary, only the sign). The camera sits south of the
/// stage (larger Z) and looks north-and-down at it: <c>eye.Z = stageCenter.Z + distance*cos(pitch)</c>,
/// <c>eye.Y = distance*sin(pitch)</c>, aimed at <c>(stageCenter.X, 0, stageCenter.Z)</c>.
/// <b>This was originally transcribed from a Unity reference as "north of the stage, smaller Z"
/// — wrong for this engine's basis, since Unity's +Z is north while Aigis's +Z is south, so the
/// same sign that reads as "north side" in Unity is actually the south side here.</b> The bug
/// produced a 180-degree-rotated level (grass row rendering at the bottom instead of the top,
/// syringe zone on the left instead of the right) and, because movement is world-relative, made
/// W walk the player toward the camera (down-screen) instead of up-screen. Pinned against an
/// asymmetric (non-square) stage box by <c>StageFitCameraTests</c> — a symmetric box can't
/// distinguish a correct fit from one that's mirrored in X or in Z, so the test box deliberately
/// has <c>spanX != spanZ</c> and checks both signs independently; a further screen-space
/// projection test (<c>Fit_NorthProjectsAboveSouth_AndEastProjectsRightOfWest</c>) pins the
/// actual orientation (not just chirality), which the asymmetric-box test alone cannot catch —
/// it is symmetric under a 180-degree rotation about the stage center, so it passed even with
/// the sign bug in place.</para>
///
/// <para><b>The analytic "worst corner" distance solve.</b> Because yaw=0, tilting the camera by
/// <see cref="PitchRadians"/> about world +X leaves the horizontal (X) axis exactly aligned
/// with the camera's own right axis — camera-space horizontal framing and vertical/depth framing
/// decouple completely. At local coordinates <c>u</c> (=world X - centerX), <c>t</c> (=world Y),
/// <c>s</c> (=world Z - centerZ), the camera-space depth and "up" projections of a point work
/// out to:
/// <code>
/// depth(s, t) = distance - s*cos(pitch) - t*sin(pitch)
/// up(s, t)    = t*cos(pitch) - s*sin(pitch)
/// </code>
/// (derived from the camera's forward/up basis vectors <c>f=(0,-sin p,-cos p)</c>,
/// <c>up=(0,cos p,-sin p)</c> for yaw=0; the camera sits at <c>s=0,t=distance*sin(pitch)</c> in
/// this local frame, south of the stage now that the sign bug above is fixed). Requiring
/// <c>|u|/depth &lt;= tan(fovX/2)</c> (horizontal) and <c>|up|/depth &lt;= tan(fovY/2)</c>
/// (vertical) and solving each for <c>distance</c> at every one of the box's 8 corners
/// (<c>u=+-halfSpanX</c>, <c>s=+-halfSpanZ</c>, <c>t=0</c> or <c>topY</c>) gives, after dropping
/// terms that are always dominated by another (see below), the constraints implemented in
/// <see cref="ComputeDistance"/>:
/// <list type="bullet">
/// <item><b>Horizontal — only the near face's top corners bind.</b> <c>depth</c> only depends on
/// <c>s</c> and <c>t</c> via <c>-s*cos(pitch)-t*sin(pitch)</c>, and <c>|u|</c> is the same
/// <c>halfSpanX</c> at every corner, so the single worst (smallest-depth) horizontal case is
/// whichever corner MINIMIZES depth, i.e. MAXIMIZES <c>s*cos(pitch)+t*sin(pitch)</c> — since both
/// coefficients are positive at this pitch, that is unconditionally <c>s=+halfSpanZ</c> (the near
/// face, closest to the south-side camera) and <c>t=topY</c> (the top). The bottom corners of the
/// same near face are therefore always dominated (topY*sin(pitch) &gt;= 0) and contribute nothing
/// new — dropped rather than kept as dead code that used to read as a fourth independent
/// constraint.</item>
/// <item><b>Vertical — near face binds top AND bottom; far face's TOP corner can also bind.</b>
/// Unlike horizontal, <c>|up|/depth</c> is not monotonic in a single direction across corners
/// (the <c>|up|</c> numerator changes sign as <c>t</c> and <c>s</c> trade off), so both the near
/// face's ground corner and its top corner are checked, AND — the term the original derivation
/// missed, transcribed from the Unity reference without actually deriving all 8 corners — the
/// FAR face's top corner, whose large <c>|up|</c> (topY*cos(pitch) + halfSpanZ*sin(pitch), both
/// terms adding instead of partially cancelling as on the near face) can outweigh its depth
/// advantage for a sufficiently tall or deep stage. (The far face's ground corner is always
/// dominated by the near face's ground corner — same <c>|up|</c> numerator, but the near face's
/// <c>+halfSpanZ*cos(pitch)</c> depth term is never worse than the far face's
/// <c>-halfSpanZ*cos(pitch)</c> — so it is dropped for the same reason as the horizontal
/// bottom corners.)</item>
/// </list>
/// Missing the far-top term silently clipped tall/deep stages (confirmed numerically: a 20x15x8
/// stage needs distance ~28.8 including it vs. ~25.5 without, i.e. the far-top corner used to sit
/// beyond the frame) and made <see cref="ComputeDistance"/> non-monotone in <c>spanZ</c> for a
/// Z-dominant box — contradicting the "a bigger stage always needs at least as much distance"
/// invariant, now pinned directly by a Z-dominant-box test alongside the pre-existing X-dominant
/// one in <c>StageFitCameraTests</c>. Level4's own box (20x15x3, shallow) happens to need the
/// same distance with or without the far-top term, which is why this specific defect survived
/// review.</para>
/// </summary>
public static class StageFitCamera
{
    /// <summary>Constant camera depression from horizontal — never changes per-level or per-frame.
    ///
    /// <para><b>This value is coupled to the gravity of any game tuned against it, and nothing
    /// enforces that coupling.</b> World height projects onto the camera's up axis with a factor
    /// <c>cos(pitch)</c> (see the derivation in this class's doc), so at 60° a fall covers exactly
    /// 0.5 of the screen distance the same world distance travelled horizontally would. Since a
    /// fall through <c>h</c> takes <c>√(2h/g)</c>, on-screen fall speed goes as
    /// <c>cos(pitch)·√(g·h/2)</c>: changing this constant rescales the game's APPARENT gravity by
    /// <c>cos(newPitch)/cos(60°)</c> without touching gravity at all. Triage's −36 m/s² is 4%
    /// from reproducing a real 9.81 fall's on-screen speed at 60° precisely because 0.5 and
    /// √3.67 ≈ 1.92 nearly cancel — at 45° the same gravity would read 1.41× too fast. Treat a
    /// pitch change as a re-judgement of the hosting game's whole ballistic constant set (jump /
    /// throw / kick impulses and gravity together); a perceptual property like this cannot be
    /// pinned by a test, so the numbers and their derivation live in
    /// docs/plans/triage-full-port.md §9.0.1 instead.</para></summary>
    public const float PitchDegrees = 60f;

    public const float PitchRadians = PitchDegrees * MathF.PI / 180f;

    /// <summary>Multiplies the raw analytic fit distance for a small amount of breathing room
    /// around the stage box (never shrinks it — see <see cref="ComputeDistance"/>'s
    /// <c>MathF.Max(1f, margin)</c> clamp).</summary>
    public const float Margin = 1.12f;

    /// <summary>The fit is always solved against a 16:9 reference frame, regardless of the
    /// window's actual live aspect ratio — the same "design for a reference resolution, then
    /// apply to whatever aspect the window actually has" pattern as a fixed orthographic size in
    /// other engines. The window's real aspect is only used later, when actually building the
    /// view-projection matrix (<see cref="StageFitGameCamera"/>), not here.</summary>
    public const float AssumedAspect = 16f / 9f;

    /// <summary>Absolute floor under the analytic distance — <c>12 / sin(pitch)</c> (~13.86 m at
    /// 60°) keeps the camera from sitting uncomfortably close for a tiny single-room stage. Note
    /// this constant is really an EYE-HEIGHT floor, not a distance floor per se: the eye's actual
    /// height above the floor is <c>distance*sin(pitch)</c>, and at the clamp,
    /// <c>(MinDistanceNumerator/sin(pitch))*sin(pitch) == MinDistanceNumerator</c> — i.e. this
    /// guarantees the eye never sits lower than 12 m up, independent of pitch, dividing by
    /// <c>sin(pitch)</c> purely to convert that height requirement into the corresponding
    /// distance along the camera's tilted view axis.</summary>
    private const float MinDistanceNumerator = 12f;

    private const float FarClipFloor = 80f;
    private const float FarClipHeightMultiplier = 4f;

    /// <summary>Extra breathing room added beyond the solved distance's own farthest box corner
    /// when deriving <see cref="Fit"/>'s far clip plane — a fixed clip margin so floor decals or
    /// geometry sitting a little past the stage box's own edge (e.g. wall thickness) don't get
    /// clipped right at the boundary.</summary>
    private const float FarClipSlack = 5f;

    /// <summary>
    /// Solves the one free parameter — distance from <c>stageCenter</c> — for a stage box of
    /// <paramref name="spanX"/> (width) by <paramref name="spanZ"/> (depth) by <paramref name="topY"/>
    /// (height above the y=0 floor), at the camera's existing vertical FOV. Pure function: same
    /// inputs always give the same output, no I/O, no world/physics dependency — deliberately
    /// kept that way so it's unit-testable in isolation (per the task's own instruction).
    /// </summary>
    public static float ComputeDistance(
        float spanX,
        float spanZ,
        float topY,
        float fovYRadians = default,
        float aspect = AssumedAspect,
        float margin = Margin)
    {
        if (fovYRadians <= 0f)
        {
            fovYRadians = Camera.DefaultFovYRadians;
        }

        if (spanX < 0f || spanZ < 0f || topY < 0f)
        {
            throw new ArgumentOutOfRangeException(spanX < 0f ? nameof(spanX) : spanZ < 0f ? nameof(spanZ) : nameof(topY), "Stage box extents must be non-negative.");
        }

        var halfFovY = fovYRadians * 0.5f;
        var fovX = 2f * MathF.Atan(MathF.Tan(halfFovY) * aspect);
        var halfFovX = fovX * 0.5f;

        var pitch = PitchRadians;
        var sinP = MathF.Sin(pitch);
        var cosP = MathF.Cos(pitch);
        var halfSpanX = spanX * 0.5f;
        var halfSpanZ = spanZ * 0.5f; // distance from stage center to either face

        // Horizontal: only the near face's top corners can ever bind (see class doc comment) —
        // the near face's ground corners are always dominated (topY*sin(pitch) >= 0) and the far
        // face's corners are dominated by the near face's, so this single term is the whole
        // horizontal constraint.
        var dHoriz = halfSpanX / MathF.Tan(halfFovX) + halfSpanZ * cosP + topY * sinP;

        // Vertical: near face, ground level (t=0, s=+halfSpanZ).
        var dVertNearBottom = halfSpanZ * sinP / MathF.Tan(halfFovY) + halfSpanZ * cosP;

        // Vertical: near face, top of stage (t=topY, s=+halfSpanZ) -- raised to the stage's
        // height, farther "up" in camera space (more demanding) but also farther in depth (less
        // demanding), captured by the extra +-topY*sin(pitch) term.
        var dVertNearTop = MathF.Abs(topY * cosP - halfSpanZ * sinP) / MathF.Tan(halfFovY) + halfSpanZ * cosP + topY * sinP;

        // Vertical: far face, top of stage (t=topY, s=-halfSpanZ) -- the term the original
        // (Unity-transcribed) derivation missed. Its |up| numerator (topY*cos(pitch) +
        // halfSpanZ*sin(pitch)) adds both terms instead of partially cancelling them as the near
        // face's does, and can outweigh the far face's depth advantage (-halfSpanZ*cos(pitch))
        // for a tall or deep-enough stage -- see class doc comment for the numeric proof. The far
        // face's ground corner is never the binding one (always dominated by the near face's
        // ground corner) so it is not computed here.
        var dVertFarTop = (topY * cosP + halfSpanZ * sinP) / MathF.Tan(halfFovY) - halfSpanZ * cosP + topY * sinP;

        var raw = MathF.Max(MathF.Max(dHoriz, dVertNearBottom), MathF.Max(dVertNearTop, dVertFarTop));
        var distance = raw * MathF.Max(1f, margin);
        return MathF.Max(distance, MinDistanceNumerator / sinP);
    }

    /// <summary>Floor under the frustum's top-edge descent rate in
    /// <see cref="GroundCoverageHalfExtents"/>. At this class's fixed <see cref="PitchDegrees"/> the
    /// rate is only non-positive for a vertical FOV wide enough to put the top edge at or above the
    /// horizon (fovY &gt;= 2*pitch = 120°), where the frustum never meets the ground at all and the
    /// coverage is genuinely unbounded; clamping keeps that degenerate case a large finite number
    /// rather than an infinity/NaN a caller would silently propagate into geometry.</summary>
    private const float MinGroundDescent = 1e-3f;

    /// <summary>
    /// How far <see cref="Fit"/>'s solved camera actually SEES across the ground plane (y=0),
    /// as half-extents measured from the stage centre — i.e. the inverse question to
    /// <see cref="ComputeDistance"/>, and the one a caller must answer before it can lay ground
    /// out beyond the authored stage and know the frame is covered rather than guess at it
    /// (<c>Game.Triage</c>'s map apron is the first such caller).
    ///
    /// <para><b>Why <paramref name="aspect"/> is separate from the fit's own.</b> The fit is
    /// always solved against <see cref="AssumedAspect"/> (see that constant), but the matrix is
    /// built with the window's REAL aspect — so a wider-than-16:9 window keeps the same camera
    /// placement and simply sees further out in X. Coverage therefore has to be evaluated at
    /// whatever aspect the caller wants to guarantee, which is why this takes its own parameter
    /// while <see cref="ComputeDistance"/> is called here without one.</para>
    ///
    /// <para><b>The derivation</b> reuses the class doc comment's own camera basis. A frustum
    /// corner ray is <c>f + u*tan(fovY/2)*v + r*tan(fovX/2)*h</c> for <c>h,v in [-1,1]</c>, so its
    /// downward rate is <c>sin(pitch) -+ tan(fovY/2)*cos(pitch)</c> — smallest along the TOP edge
    /// (<c>v=+1</c>), which is therefore both the farthest ground hit in Z and, because horizontal
    /// spread grows with travelled distance, the widest one in X. The bottom edge
    /// (<c>v=-1</c>) gives the near ground hit. Both are returned as a symmetric bounding
    /// half-extent pair, since the real footprint is a trapezoid and callers lay out rectangles.</para>
    /// </summary>
    /// <param name="spanX">Stage bounding box width, as passed to <see cref="Fit"/>.</param>
    /// <param name="spanZ">Stage bounding box depth, as passed to <see cref="Fit"/>.</param>
    /// <param name="topY">Stage bounding box height, as passed to <see cref="Fit"/>.</param>
    /// <param name="aspect">The widest window aspect the caller wants covered — NOT necessarily
    /// the one the fit was solved at (see above).</param>
    /// <param name="fovYRadians">Vertical FOV; <see cref="Camera.DefaultFovYRadians"/> when omitted,
    /// matching <see cref="ComputeDistance"/>.</param>
    public static (float HalfX, float HalfZ) GroundCoverageHalfExtents(
        float spanX,
        float spanZ,
        float topY,
        float aspect = AssumedAspect,
        float fovYRadians = default)
    {
        if (fovYRadians <= 0f)
        {
            fovYRadians = Camera.DefaultFovYRadians;
        }

        if (aspect <= 0f)
        {
            throw new ArgumentOutOfRangeException(nameof(aspect), aspect, "Aspect ratio must be positive.");
        }

        // Deliberately NOT forwarding `aspect`/`fovYRadians` into the fit: the camera placement this
        // measures is the one Fit actually produces, and that is solved at AssumedAspect regardless
        // of the window (see the parameter doc above).
        var distance = ComputeDistance(spanX, spanZ, topY, fovYRadians);
        var tanHalfFovY = MathF.Tan(fovYRadians * 0.5f);
        var tanHalfFovX = tanHalfFovY * aspect;

        var sinP = MathF.Sin(PitchRadians);
        var cosP = MathF.Cos(PitchRadians);
        var eyeY = distance * sinP;
        var eyeZ = distance * cosP; // relative to the stage centre, matching Fit's own eye placement

        var farTravel = eyeY / MathF.Max(sinP - tanHalfFovY * cosP, MinGroundDescent);
        var nearTravel = eyeY / (sinP + tanHalfFovY * cosP);

        var farZ = eyeZ - (cosP + tanHalfFovY * sinP) * farTravel;
        var nearZ = eyeZ - (cosP - tanHalfFovY * sinP) * nearTravel;

        return (tanHalfFovX * farTravel, MathF.Max(MathF.Abs(farZ), MathF.Abs(nearZ)));
    }

    /// <summary>
    /// The full fit: distance (<see cref="ComputeDistance"/>) plus the resulting eye position and
    /// far clip plane. <paramref name="stageCenterAtGround"/> is <c>(centerX, 0, centerZ)</c> — the
    /// look-at target is this exact point (Y=0), per the spec's "aimed at stageCenter at y=0".
    /// </summary>
    public static (Vector3 Eye, Vector3 Target, float FarClip) Fit(
        Vector3 stageCenterAtGround,
        float spanX,
        float spanZ,
        float topY,
        float fovYRadians = default)
    {
        var distance = ComputeDistance(spanX, spanZ, topY, fovYRadians);
        var pitch = PitchRadians;
        var cosP = MathF.Cos(pitch);
        // 2026-08-31: the eye's Y is the TARGET's Y plus the rise, not the rise alone. It used to be
        // the rise alone, which is bit-identical for every stage box whose centre sits at Y=0 -- i.e.
        // for every level in the game until Triage's twin-tower rooftop map, whose playable ground is
        // 44 m up. There the camera stayed 26 m above the STREET and 18 m BELOW the ward, so the fit
        // it solved was aimed upward: the shot came out as two towers seen from underneath with the
        // sky between them and not one roof tile on screen (measured on that map's first filmstrip).
        // Adding the target's own Y is what makes "fit this box" mean the same thing wherever the box
        // is, and it cannot move any existing level's camera by so much as a millimetre.
        var eye = new Vector3(
            stageCenterAtGround.X,
            stageCenterAtGround.Y + (distance * MathF.Sin(pitch)),
            stageCenterAtGround.Z + distance * MathF.Cos(pitch));

        // The farthest box corner from the eye (in camera-space depth) is the far face's ground
        // corner: depth = distance + halfSpanZ*cos(pitch) (see class doc comment's depth(s,t)
        // formula at s=-halfSpanZ, t=0). Deriving the far clip from that -- rather than only from
        // topY, which ignores the solved distance entirely -- matters once the stage box is large
        // enough that distance itself dominates (e.g. a 400x300x3 stage solves to distance~=396,
        // putting that corner at depth~=471, far beyond the old fixed max(80, topY*4)==80 far
        // plane -- the frame would render empty). FarClipSlack is pure breathing room beyond that
        // corner; FarClipFloor/FarClipHeightMultiplier are kept as a floor so small stages are
        // unaffected (Level4's 20x15x3 box still solves to exactly 80 here, unchanged).
        var halfSpanZ = spanZ * 0.5f;
        var farthestCornerDepth = distance + halfSpanZ * cosP + topY;
        var farClip = MathF.Max(FarClipFloor, MathF.Max(farthestCornerDepth + FarClipSlack, topY * FarClipHeightMultiplier));
        return (eye, stageCenterAtGround, farClip);
    }
}
