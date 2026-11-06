namespace Aigis.Render;

/// <summary>
/// GLSL for the miniature finishing pass (<see cref="PostProcessPipeline"/>, configured by
/// <see cref="PostProcessSettings"/>). One fullscreen triangle samples the frame the scene pass just
/// drew and writes the finished picture into the real target, under the HUD. The ambient occlusion it
/// darkens creases with is computed first, at half resolution, by <see cref="PostProcessOcclusionFrag"/>.
/// <para>
/// Compiled at build time into <see cref="PrecompiledShaders.PostProcessVertSpirv"/>/
/// <see cref="PrecompiledShaders.PostProcessFragSpirv"/>/<see cref="PrecompiledShaders.PostProcessOcclusionFragSpirv"/>
/// like every other built-in shader; see <see cref="PrecompiledShaders"/> for the regeneration procedure.
/// </para>
/// </summary>
public static class PostProcessSource
{
    /// <summary>Fullscreen triangle from <c>gl_VertexIndex</c> alone. <c>vUv.y = 0</c> is the TOP
    /// row: Vulkan clip space is Y-down, and the sampled image's row 0 is that same top row, so no
    /// flip happens anywhere on this path (hard rule 4).</summary>
    public const string PostProcessVert = """
        #version 450
        layout(location = 0) out vec2 vUv;
        vec2 positions[3] = vec2[](vec2(-1.0, -1.0), vec2(3.0, -1.0), vec2(-1.0, 3.0));
        void main() {
            gl_Position = vec4(positions[gl_VertexIndex], 0.0, 1.0);
            vUv = positions[gl_VertexIndex] * 0.5 + 0.5;
        }
        """;

    /// <summary>What both fragment stages read the scene's depth with. Expects a <c>depthTex</c>
    /// binding and a push block carrying <c>sampleMin</c>, <c>sampleMax</c> and <c>invViewProj</c>
    /// under those names.</summary>
    private const string DepthSampling = """

        const float GOLDEN_ANGLE = 2.39996323;

        // Whether a tap lands on ANOTHER split-screen view's pixels: inside the target but outside
        // this draw's rectangle. Such a tap is skipped, never clamped -- a neighbour drawn from another
        // camera is not part of this picture, and a clamped depth rebuilt along this view's ray is a
        // surface floating in front of the floor, which the occlusion draws as a dark band along the
        // seam (measured: a 15-pixel ramp in the first windowed run). A tap past the TARGET edge is
        // not foreign and keeps its clamp-to-edge read, so a single-camera frame, whose rectangle is
        // the whole target, is exactly what it was.
        // The whole-target rectangle answers before any arithmetic: it is every single-camera frame,
        // and this runs once per tap -- about a hundred times a pixel. Uniform across the draw, so
        // the branch costs nothing (measured on a Radeon 680M at 720p: finishing pass 8.6 -> 7.2 ms).
        bool Foreign(vec2 uv) {
            if (pc.sampleMin == 0u && pc.sampleMax == 0xFFFFFFFFu) {
                return false;
            }

            vec2 onTarget = clamp(uv, vec2(0.0), vec2(1.0));
            return any(lessThan(onTarget, unpackUnorm2x16(pc.sampleMin)))
                || any(greaterThan(onTarget, unpackUnorm2x16(pc.sampleMax)));
        }

        vec3 WorldAt(vec2 uv) {
            float d = textureLod(depthTex, uv, 0.0).r;
            vec4 h = pc.invViewProj * vec4(uv * 2.0 - 1.0, d, 1.0);
            return h.xyz / h.w;
        }

        """;

    /// <summary>The occlusion itself, for both fragment stages: the half-resolution pass evaluates it
    /// per texel, and the finishing pass falls back to it per pixel where the half-resolution result
    /// does not describe that pixel's surface. Needs <see cref="DepthSampling"/> and a push block with
    /// <c>heightScale</c> and <c>aoStrength</c>.</summary>
    private const string AmbientOcclusionCore = """
        // World-space: positions are rebuilt from depth, so the radius is in METRES and a gap under a
        // bed reads the same at any zoom. The disk is sized in pixels from how many metres one pixel
        // spans here, rotated per pixel by interleaved-gradient noise so the taps read as fine grain
        // rather than as banding rings.
        const int AO_TAPS = 24;
        const float AO_RADIUS = 0.7;

        // uv is the scene pixel evaluated; noisePixel indexes the rotation. The occlusion pass passes
        // its own texel there, not the scene pixel: interleaved-gradient noise is built so that ADJACENT
        // pixels disagree, and read only at every other scene pixel it does not, which the
        // reconstruction's blend then shows as blotches rather than fine grain.
        float AmbientOcclusion(vec2 uv, vec2 noisePixel) {
            if (textureLod(depthTex, uv, 0.0).r >= 1.0) {
                return 1.0;
            }

            vec2 tx = 1.0 / vec2(textureSize(depthTex, 0));
            vec3 p = WorldAt(uv);
            vec3 r = WorldAt(uv + vec2(tx.x, 0.0)) - p;
            vec3 l = p - WorldAt(uv - vec2(tx.x, 0.0));
            vec3 d = WorldAt(uv + vec2(0.0, tx.y)) - p;
            vec3 u = p - WorldAt(uv - vec2(0.0, tx.y));
            // The smaller difference on each axis, so a pixel on an edge takes its normal from its own
            // surface rather than from the drop to whatever is behind it.
            // A side that is another view's pixel is not a neighbour; the other side always is (a view
            // is never one pixel wide).
            vec3 dx = Foreign(uv + vec2(tx.x, 0.0)) ? l : Foreign(uv - vec2(tx.x, 0.0)) ? r : dot(r, r) < dot(l, l) ? r : l;
            vec3 dy = Foreign(uv + vec2(0.0, tx.y)) ? u : Foreign(uv - vec2(0.0, tx.y)) ? d : dot(d, d) < dot(u, u) ? d : u;
            vec3 n = normalize(cross(dx, dy));
            vec4 nearH = pc.invViewProj * vec4(uv * 2.0 - 1.0, 0.0, 1.0);
            if (dot(n, nearH.xyz / nearH.w - p) < 0.0) {
                n = -n;
            }

            float metresPerPixel = max(sqrt(min(dot(dx, dx), dot(dy, dy))), 1e-4);
            float radiusPx = clamp(AO_RADIUS / metresPerPixel, 2.0, 48.0 * pc.heightScale);
            float noise = fract(52.9829189 * fract(dot(noisePixel, vec2(0.06711056, 0.00583715))));
            float occlusion = 0.0;
            for (int i = 0; i < AO_TAPS; i++) {
                float t = sqrt((float(i) + 0.5) / float(AO_TAPS));
                float a = float(i) * GOLDEN_ANGLE + noise * 6.2831853;
                vec2 tap = uv + vec2(cos(a), sin(a)) * t * radiusPx * tx;
                if (Foreign(tap)) {
                    continue;
                }

                vec3 v = WorldAt(tap) - p;
                float dist = length(v);
                float facing = max(dot(n, v) / (dist + 1e-4) - 0.05, 0.0);
                occlusion += facing * clamp(1.0 - dist / (2.0 * AO_RADIUS), 0.0, 1.0);
            }

            return clamp(1.0 - pc.aoStrength * 1.6 * occlusion / float(AO_TAPS), 0.0, 1.0);
        }
        """;

    /// <summary>
    /// Screen-space ambient occlusion into a HALF-resolution R8 target, one texel per 2x2 block of the
    /// scene, which <see cref="PostProcessFrag"/> then reads back depth-aware.
    /// <para>
    /// <b>Why its own pass.</b> Inside the finishing pass it was the largest single cost: on a Radeon
    /// 680M at 1280x720 the finishing pass measured 8.6–10 ms with it and 3.6–4.0 ms without, of a
    /// ~25 ms GPU frame. The time follows the NUMBER of dependent depth reads (24 taps plus 5 for the
    /// normal), not their cache footprint — removing the per-pixel rotation, which scatters
    /// neighbouring pixels' taps, moved nothing — so the lever is fewer of them, i.e. fewer pixels.
    /// </para>
    /// <para>
    /// Each texel evaluates the scene pixel at the top-left of its block as the full-resolution pass did
    /// for that pixel (same radius in metres, same taps, same normal), so what the reconstruction blends
    /// is real per-pixel occlusion, not an average of a coarser depth. Only the per-texel rotation noise
    /// is indexed by the texel rather than the pixel — see the note on <c>AmbientOcclusion</c>.
    /// </para>
    /// </summary>
    public const string PostProcessOcclusionFrag = """
        #version 450
        layout(location = 0) in vec2 vUv;
        layout(location = 0) out vec4 outOcclusion;
        layout(set = 0, binding = 0) uniform sampler2D depthTex;
        layout(push_constant) uniform Push {
            uint sampleMin;
            uint sampleMax;
            float heightScale; // target height / 1080
            float aoStrength;
            mat4 invViewProj; // (uv*2-1, depth, 1) -> world
        } pc;
        """ + DepthSampling + AmbientOcclusionCore + """

        void main() {
            ivec2 fullSize = textureSize(depthTex, 0);
            ivec2 source = min(ivec2(gl_FragCoord.xy) * 2, fullSize - 1);
            outOcclusion = vec4(AmbientOcclusion((vec2(source) + 0.5) / vec2(fullSize), gl_FragCoord.xy));
        }
        """;

    /// <summary>
    /// Tilt-shift defocus, ambient occlusion, bloom, grade, vignette. The glow is gathered from the
    /// SHARP scene and added to the defocused colour (one pass, no intermediate); the grade and
    /// vignette then see both.
    /// <para>
    /// <b>The defocus</b> is a golden-angle disk of <c>BLUR_TAPS</c> samples whose radius follows the
    /// pixel's distance outside the sharp band. Bright samples weigh more, which is what makes an
    /// out-of-focus highlight bloom into a soft disc rather than smear grey — the "bokeh" half of
    /// the macro look.
    /// </para>
    /// <para>
    /// <b>The occlusion</b> is read from the half-resolution target <see cref="PostProcessOcclusionFrag"/>
    /// filled: the four nearest texels, weighted bilinearly AND by how close each one's view depth is
    /// to this pixel's, so a crease's darkness stops at a silhouette instead of bleeding a pixel onto
    /// the bright floor beyond it.
    /// </para>
    /// <para>
    /// <b>The bloom</b> is a second, wider disk that keeps only what exceeds the threshold, weighted
    /// toward its centre. Single pass and approximate (no mip chain): its job is a soft halo around
    /// windows, lamps and sunlit walls, not physically correct glare.
    /// </para>
    /// </summary>
    public const string PostProcessFrag = """
        #version 450
        layout(location = 0) in vec2 vUv;
        layout(location = 0) out vec4 outColor;
        layout(set = 0, binding = 0) uniform sampler2D sceneTex;
        layout(set = 0, binding = 1) uniform sampler2D depthTex;
        layout(set = 0, binding = 2) uniform sampler2D occlusionTex;
        layout(push_constant) uniform Push {
            vec4 focus;   // x = band centre (uv.y), y = band half height, z = falloff, w = max blur px
            vec4 bloom;   // x = threshold, y = intensity, z = radius px, w = vignette
            vec4 grade;   // x = saturation, y = contrast, z = warmth, w = exposure
            // The rectangle this draw may SAMPLE, as two unorm16 pairs (uv min, uv max): the whole
            // target for one camera, a split-screen view's own rectangle once views have cameras of
            // their own. The texel size these two slots used to carry is textureSize() instead.
            uint sampleMin;
            uint sampleMax;
            float heightScale; // target height / 1080
            float aoStrength;
            mat4 invViewProj; // (uv*2-1, depth, 1) -> world, for the occlusion
        } pc;
        """ + DepthSampling + AmbientOcclusionCore + """

        const int BLUR_TAPS = 40;
        const int BLOOM_TAPS = 32;

        float luma(vec3 c) { return dot(c, vec3(0.2126, 0.7152, 0.0722)); }

        vec2 Texel() { return 1.0 / vec2(textureSize(sceneTex, 0)); }

        // How far the surface lies beyond the near plane along this pixel's own ray. Not clip w, which an
        // orthographic camera holds constant; not a dot with one forward axis, which an off-centre
        // whole-target camera (a split-screen quadrant) bends past 90 degrees in its far corner.
        float RayDepth(vec2 uv, float depth) {
            vec4 surface = pc.invViewProj * vec4(uv * 2.0 - 1.0, depth, 1.0);
            vec4 near = pc.invViewProj * vec4(uv * 2.0 - 1.0, 0.0, 1.0);
            return length(surface.xyz / surface.w - near.xyz / near.w);
        }

        float Occlusion(vec2 uv) {
            if (pc.aoStrength <= 0.0) {
                return 1.0;
            }

            float depth = textureLod(depthTex, uv, 0.0).r;
            if (depth >= 1.0) {
                return 1.0;
            }

            ivec2 fullSize = textureSize(depthTex, 0);
            ivec2 halfSize = textureSize(occlusionTex, 0);
            float z = max(RayDepth(uv, depth), 1e-3);
            // In occlusion texels, where texel q stands for scene pixel 2q (PostProcessOcclusionFrag).
            vec2 h = (uv * vec2(fullSize) - 0.5) * 0.5;
            ivec2 base = ivec2(floor(h));
            vec2 f = h - vec2(base);
            float sum = 0.0;
            float weightSum = 0.0;
            float sameSurface = 0.0;
            for (int i = 0; i < 4; i++) {
                ivec2 o = ivec2(i & 1, i >> 1);
                ivec2 q = clamp(base + o, ivec2(0), halfSize - 1);
                ivec2 source = min(q * 2, fullSize - 1);
                vec2 sourceUv = (vec2(source) + 0.5) / vec2(fullSize);
                if (Foreign(sourceUv)) {
                    continue;
                }

                float bilinear = (o.x == 1 ? f.x : 1.0 - f.x) * (o.y == 1 ? f.y : 1.0 - f.y);
                float dz = (RayDepth(sourceUv, texelFetch(depthTex, source, 0).r) - z) / z;
                // Half weight at a 5% depth difference along the ray, ~6% of it at 20%: one surface keeps
                // its neighbours, a step to another surface drops them. 1% was measured too: no closer
                // to the full-resolution picture, and a slanted floor's own texels then disagree often
                // enough to send a third more of the frame down the full-resolution fallback below.
                float match = 1.0 / (1.0 + 400.0 * dz * dz);
                float w = (bilinear + 1e-3) * match;
                sameSurface = max(sameSurface, match);
                sum += texelFetch(occlusionTex, q, 0).r * w;
                weightSum += w;
            }

            // No texel around stands for this pixel's surface -- a wire of a fine chain-link fence whose
            // blocks' evaluated pixels all landed on the floor seen through it. The blend would hand the
            // wire the floor's occlusion (measured on 1-1's gate: 14/255 lighter, half its contrast), so
            // such a pixel is evaluated here at full resolution, exactly as before the split. Falling
            // back on EVERY edge pixel instead (~5% of a ward frame) was measured too: no closer to the
            // full-resolution picture on any fence, and half of the saving gone to divergent waves.
            if (sameSurface < 0.5) {
                return AmbientOcclusion(uv, gl_FragCoord.xy);
            }

            return sum / weightSum;
        }

        void main() {
            vec4 sampledCenter = textureLod(sceneTex, vUv, 0.0);
            vec3 center = sampledCenter.rgb;

            float outside = abs(vUv.y - pc.focus.x) - pc.focus.y;
            float defocus = clamp(outside / pc.focus.z, 0.0, 1.0);
            defocus = defocus * defocus * (3.0 - 2.0 * defocus);
            float radius = defocus * pc.focus.w * pc.heightScale;
            vec2 texel = Texel();

            vec3 color = center;
            if (radius > 0.35) {
                vec3 sum = center;
                float weightSum = 1.0;
                for (int i = 0; i < BLUR_TAPS; i++) {
                    float r = sqrt((float(i) + 0.5) / float(BLUR_TAPS)) * radius;
                    float a = float(i) * GOLDEN_ANGLE;
                    vec2 tap = vUv + vec2(cos(a), sin(a)) * r * texel;
                    if (Foreign(tap)) {
                        continue;
                    }

                    vec3 s = textureLod(sceneTex, tap, 0.0).rgb;
                    float w = 1.0 + 4.0 * max(luma(s) - 0.7, 0.0);
                    sum += s * w;
                    weightSum += w;
                }
                color = sum / weightSum;
            }

            // Darkens the defocused colour too: a crease is dark in the photograph, blurred or not.
            color *= Occlusion(vUv);

            if (pc.bloom.y > 0.0) {
                vec3 glow = vec3(0.0);
                float glowWeight = 0.0;
                float bloomRadius = pc.bloom.z * pc.heightScale;
                for (int i = 0; i < BLOOM_TAPS; i++) {
                    float t = sqrt((float(i) + 0.5) / float(BLOOM_TAPS));
                    float a = float(i) * GOLDEN_ANGLE + 0.7;
                    vec2 tap = vUv + vec2(cos(a), sin(a)) * t * bloomRadius * texel;
                    float w = Foreign(tap) ? 0.0 : 1.0 - 0.75 * t;
                    vec3 s = textureLod(sceneTex, tap, 0.0).rgb;
                    glow += max(s - vec3(pc.bloom.x), vec3(0.0)) * w;
                    glowWeight += w;
                }
                // Tinted toward the warm light that makes the highlights, so the halo reads as sun.
                // max(): every tap of a seam pixel can be another view's, and 0/0 would blank it.
                color += glow / max(glowWeight, 1e-4) * pc.bloom.y * vec3(1.0, 0.86, 0.66) * 2.0;
            }

            color *= pc.grade.w;
            float warmth = pc.grade.z;
            color *= vec3(1.0 + 0.08 * warmth, 1.0 + 0.015 * warmth, 1.0 - 0.10 * warmth);
            float l = luma(color);
            color = mix(vec3(l), color, pc.grade.x);
            color = clamp(color, 0.0, 1.0);
            // Contrast as a smooth S around mid-grey: steepens the mid-tones without clipping ends.
            vec3 s = color * color * (3.0 - 2.0 * color);
            color = mix(color, s, clamp(pc.grade.y - 1.0, -1.0, 1.0));

            vec2 fromCenter = (vUv - 0.5) * vec2(1.0, 0.82);
            float edge = smoothstep(0.18, 0.62, length(fromCenter));
            vec3 vignetteTint = vec3(1.0, 0.93, 0.84);
            color *= mix(vec3(1.0), vignetteTint * (1.0 - 0.5 * edge), edge * pc.bloom.w);

            outColor = vec4(clamp(color, 0.0, 1.0), sampledCenter.a);
        }
        """;
}
