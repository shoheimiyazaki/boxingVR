namespace Aigis.Render;

/// <summary>
/// GLSL for the world-space ground decal pipeline (<see cref="WorldDecalPipeline"/>) — the
/// textured, alpha-blended quad that shows a <see cref="DynamicTexture"/> stain sheet on the floor.
/// <para>
/// The quad's geometry is built in the vertex shader from the unit quad plus the draw's centre and
/// half-extents, exactly as the screen-space overlay-image pair
/// (<see cref="OverlaySource.OverlayImageVert"/>) builds its rect — so this needs no per-decal
/// vertex buffer and no model matrix. Unlike that pair it goes through the camera's
/// view-projection, which is the ONE matrix that carries this engine's Vulkan clip convention
/// (<see cref="Camera.ApplyVulkanClip"/>); there is no second Y flip anywhere on this path
/// (hard rule 4).
/// </para>
/// <para>
/// Compiled at build time into <see cref="PrecompiledShaders.WorldDecalVertSpirv"/>/
/// <see cref="PrecompiledShaders.WorldDecalFragSpirv"/> for the same reason the particle and
/// overlay pairs are: the first frame a body bleeds would otherwise stall on shaderc's native load
/// mid-play. See <see cref="PrecompiledShaders"/> for the regeneration procedure and
/// <c>PrecompiledShadersSyncTests</c> for the gate that catches a forgotten regeneration.
/// </para>
/// </summary>
public static class DecalSource
{
    /// <summary>Spans the unit quad over <c>center</c> along the basis
    /// <see cref="DecalQuadDraw.YawRadians"/>/<see cref="DecalQuadDraw.Uprightness"/> select, then
    /// leans that whole basis by <see cref="DecalQuadDraw.TiltRadians"/> about the horizontal axis at
    /// <see cref="DecalQuadDraw.TiltAxisYawRadians"/> — see that type's own basis and UV notes. The
    /// yaw/uprightness lanes ride in <c>halfExtents.zw</c>; the tilt pair has the block's fourth vec4
    /// to itself, which is what takes it to the 128-byte guaranteed minimum.</summary>
    public const string WorldDecalVert = """
        #version 450
        layout(location = 0) in vec2 inPos;
        layout(push_constant) uniform Push {
            mat4 viewProjection;
            vec4 center;
            vec4 halfExtents;   // xy = half size along the quad's own width/height axes,
                                // z = yaw about +Y, w = uprightness (0 = ground-flat, 1 = vertical)
            vec4 tint;
            vec4 tilt;          // x = azimuth of the horizontal tilt axis, y = tilt angle, zw unused
        } pc;
        layout(location = 0) out vec2 vUv;

        // Right-handed rotation of v by `angle` about the unit axis k (Rodrigues). Never called with
        // angle 0 -- see the branch below for why that case must not go through here at all.
        vec3 rotateAbout(vec3 v, vec3 k, float angle) {
            float s = sin(angle);
            float c = cos(angle);
            return (v * c) + (cross(k, v) * s) + (k * (dot(k, v) * (1.0 - c)));
        }

        void main() {
            float yaw = pc.halfExtents.z;
            float s = sin(yaw);
            float c = cos(yaw);
            // At yaw 0 these are exactly world +X and +Z (cos(0) and sin(0) are exact), and
            // normalize() of a unit axis is exact too -- so a ground-flat, unrotated quad lands on the
            // same vertices the pre-2026-08-20 form computed, bit for bit.
            vec3 right = vec3(c, 0.0, -s);
            vec3 groundUp = vec3(s, 0.0, c);
            vec3 up = normalize(mix(groundUp, vec3(0.0, 1.0, 0.0), pc.halfExtents.w));

            // Branched rather than folded into rotateAbout with angle 0. Rodrigues at 0 is v*1 + 0 + 0
            // ALGEBRAICALLY, but the compiler is free to contract those three terms into fused
            // multiply-adds in any order, and this shader's contract is that the default (tilt 0)
            // renders bit-identically to the form that had no tilt at all. The branch is uniform across
            // the draw, so it costs nothing measurable.
            float tilt = pc.tilt.y;
            if (tilt != 0.0) {
                // The axis is horizontal and its azimuth is measured exactly as `yaw` is above, so a
                // tilt axis of 0 is world +X and the same convention serves both lanes.
                float ts = sin(pc.tilt.x);
                float tc = cos(pc.tilt.x);
                vec3 axis = vec3(tc, 0.0, -ts);
                right = rotateAbout(right, axis, tilt);
                up = rotateAbout(up, axis, tilt);
            }

            // center is NOT rotated -- it is already the quad's world position, and the tilt is about
            // the quad's own centre.
            vec3 world = pc.center.xyz
                + right * (inPos.x * pc.halfExtents.x * 2.0)
                + up * (inPos.y * pc.halfExtents.y * 2.0);
            gl_Position = pc.viewProjection * vec4(world, 1.0);
            vUv = inPos + 0.5;
        }
        """;

    /// <summary>Samples the stain sheet and multiplies by the draw's tint. The pipeline blends
    /// <c>SrcAlpha</c>/<c>OneMinusSrcAlpha</c>, so a clean (fully transparent) texel leaves the
    /// floor underneath exactly as the lit scene drew it.</summary>
    public const string WorldDecalFrag = """
        #version 450
        layout(location = 0) in vec2 vUv;
        layout(push_constant) uniform Push {
            mat4 viewProjection;
            vec4 center;
            vec4 halfExtents;
            vec4 tint;
            vec4 tilt;      // unused here; the block must match the vertex stage's declaration.
        } pc;
        layout(set = 0, binding = 0) uniform sampler2D tex;
        layout(location = 0) out vec4 outColor;
        void main() { outColor = texture(tex, vUv) * pc.tint; }
        """;
}
