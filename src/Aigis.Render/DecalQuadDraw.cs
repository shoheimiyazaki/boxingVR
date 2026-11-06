using System.Numerics;

namespace Aigis.Render;

/// <summary>
/// One textured, alpha-blended quad in WORLD space — the surface a <see cref="DynamicTexture"/> is
/// shown on. Flat on the ground by default (a blood/scorch/tyre-mark stain sheet); tilted upright by
/// <see cref="Uprightness"/> for a standing sprite (2026-08-20: foliage).
/// <para>
/// <b>The quad's own basis</b>, built in two stages. First the yaw/uprightness pair as before: width
/// runs along <see cref="YawRadians"/>' horizontal direction and height along an axis interpolated
/// from that direction's in-plane perpendicular (ground-flat) to world +Y (upright). Then BOTH of
/// those axes are rotated together by <see cref="TiltRadians"/> about the horizontal axis whose
/// azimuth is <see cref="TiltAxisYawRadians"/> — an arbitrary lean, on top of whatever the first
/// stage produced. <see cref="Center"/> is NOT rotated: it is already a world position, and the tilt
/// is about the quad's own centre.
/// </para>
/// <para>
/// At <c>YawRadians = 0</c>, <c>Uprightness = 0</c>, <c>TiltRadians = 0</c> — all defaults — the
/// basis is exactly world +X and +Z, which is the ground-plane-only quad this type expressed before
/// any of these fields existed, computed to the same bits. The vertex shader branches the tilt out
/// entirely at <c>TiltRadians == 0</c> rather than relying on <c>cos(0) == 1</c> surviving shader
/// compilation, so every existing caller — all of which leave both tilt fields at their defaults —
/// renders byte-identically to before.
/// </para>
/// <para>
/// <b>Why an axis azimuth plus an angle rather than a full basis.</b> The push-constant block is a
/// mat4 plus FOUR vec4s = 128 bytes, exactly Vulkan's guaranteed minimum; a genuine right/up pair
/// would need more, and there is no more. Two floats are what the remaining lanes can carry, and a
/// horizontal axis plus an angle is the whole of what the case this exists for needs: a deck that
/// rolls and pitches under a stain (the ocean stage's ship). A decal genuinely projected onto
/// arbitrary geometry — box-projected onto a wall, conforming to a slope — is still a different
/// feature, and it would need a uniform buffer rather than push constants.
/// </para>
/// <para>
/// <b>UV convention</b>: <c>u</c> runs along the quad's own width axis and <c>v</c> along its height
/// axis — the axes AFTER the tilt, since the tilt turns the basis and the texture rides on it, so a
/// tilted quad shows the same picture seen obliquely rather than a resampled one. Row 0 of
/// <see cref="DynamicTexture.Rgba"/> is <c>v = 0</c>. For a ground-flat quad over
/// a tilemap that means <c>u</c> follows world +X, <c>v</c> follows world +Z and texture row 0 is the
/// tilemap's row 0 (north) — there is no flip anywhere on this path. Anything that paints into the
/// texture must agree, which is why the world→UV mapping lives in ONE place per consumer
/// (<c>Game.Triage.BloodSurfaceMath</c>) rather than being re-derived per call site. For an UPRIGHT
/// quad, <c>v = 0</c> is therefore the BOTTOM edge — a sprite's texture is authored with its roots in
/// row 0.
/// </para>
/// <para>
/// <b>Depth</b>: decals test depth (so a body occludes the stain under it) but do not write it, and
/// they draw after opaque geometry. A ground surface therefore has to sit slightly above the floor it
/// stains — the caller owns that offset, since only it knows how thick its floor is. For upright
/// sprites the same state is what lets overlapping quads blend into each other instead of z-fighting.
/// </para>
/// <para>
/// <b>Not lit.</b> The fragment shader is <c>texture(tex, uv) * Tint</c> and nothing else — there is
/// no normal, no light loop, and deliberately so for a stain (which inherits the look of the floor it
/// is painted on). A caller drawing something that ought to respond to the scene's light must fold
/// that into <see cref="Tint"/> itself; <c>TriageGame.BuildDecals</c> does exactly that from its
/// day/night cycle, or a midnight lawn would be lit like noon.
/// </para>
/// </summary>
/// <param name="Texture">The sheet or sprite. Must be a long-lived instance reused every frame — see
/// <see cref="DynamicTexture"/>'s note on instance identity; the renderers key their GPU image on
/// it and re-upload only when its <see cref="DynamicTexture.Revision"/> moves.</param>
/// <param name="Center">Quad centre, world space. For a ground-flat quad <c>Y</c> is the plane's
/// height; for an upright one it is the sprite's mid-height, so a tuft standing on the floor is
/// placed at <c>floorY + HalfExtents.Y</c>.</param>
/// <param name="HalfExtents">Half-size in metres along the quad's own width and height axes (see the
/// basis note above).</param>
/// <param name="Tint">Multiplies the sampled texel (RGB and alpha), so <see cref="Vector4.One"/>
/// draws the sheet as painted and lowering <c>W</c> fades the whole surface out.</param>
/// <param name="YawRadians">Rotation of the quad's width axis about world +Y. 0 — the default and
/// every pre-2026-08-20 caller — is world +X.</param>
/// <param name="Uprightness">0 lies the quad flat on the ground (the default), 1 stands it upright
/// with its height axis along world +Y. Values between tilt it, continuously; the height axis is
/// normalized, so a partly-tilted quad keeps its authored length.</param>
/// <param name="TiltAxisYawRadians">Azimuth of the HORIZONTAL axis the quad leans about, measured the
/// same way <paramref name="YawRadians"/> is: 0 is world +X, and the axis is always in the ground
/// plane. Ignored when <paramref name="TiltRadians"/> is 0.</param>
/// <param name="TiltRadians">How far to lean about that axis, right-handed about it (hard rule 4).
/// 0 — the default and every caller written before this existed — leaves the yaw/uprightness basis
/// untouched, bit for bit. A rolling deck passes its roll here with the roll axis' azimuth above; a
/// pitching one passes that axis turned a quarter turn.</param>
public readonly record struct DecalQuadDraw(
    DynamicTexture Texture,
    Vector3 Center,
    Vector2 HalfExtents,
    Vector4 Tint,
    float YawRadians = 0f,
    float Uprightness = 0f,
    float TiltAxisYawRadians = 0f,
    float TiltRadians = 0f)
{
    /// <summary>Draws <paramref name="texture"/> untinted, flat on the ground — the common case.</summary>
    public DecalQuadDraw(DynamicTexture texture, Vector3 center, Vector2 halfExtents)
        : this(texture ?? throw new ArgumentNullException(nameof(texture)), center, halfExtents, Vector4.One)
    {
    }
}
