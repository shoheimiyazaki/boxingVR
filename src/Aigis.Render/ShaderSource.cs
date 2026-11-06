namespace Aigis.Render;

/// <summary>
/// Shared GLSL source for the box/primitive and debug-line pipelines — plain string
/// constants, safe to share between <see cref="HeadlessRenderer"/> and
/// <see cref="WindowedRenderer"/> (see docs/plans/human-play.md principle 3: only
/// side-effect-free logic is shared; each renderer still builds/owns its own
/// <c>VkPipeline</c>/<c>VkShaderModule</c> objects from this source).
/// </summary>
public static class ShaderSource
{
    /// <summary>
    /// <see cref="SurfaceDetail.SlabSeams"/>' groove spacing, in metres of world space — the C#-side
    /// half of <see cref="PrimitiveFrag"/>'s own <c>SLAB_SEAM_PERIOD_METERS</c>.
    /// <para>
    /// An exact <b>third</b> of the 1 m tile grid every <c>.tmap</c> level is authored on (2026-08-20,
    /// user request: 歩道のテクスチャを1/3ぐらいに). An integer division of the tile rather than a
    /// round 0.3, so every third joint still lands on a real cell boundary instead of the lattice
    /// drifting across the paving — which is the property the original 1 m value was chosen for and
    /// the only reason to prefer one nearby number over another.
    /// </para>
    /// <para>
    /// This is the ONE knob that scales the whole paving pattern: the groove's width is a fraction of
    /// the period (<c>DetailSeams</c>' <c>sharpness</c> is in period-squared units) and its depth is a
    /// fraction of the period too, so shrinking this shrinks the slabs, the joints and the bevel
    /// together rather than turning the paving into a dark grid of steep-walled trenches.
    /// </para>
    /// <para>Two declarations rather than one interpolated string, deliberately: keeping the GLSL a
    /// plain <c>const string</c> is what lets it be read, grepped and diffed as GLSL. The pair cannot
    /// drift silently — <c>SurfaceDetailTests</c> parses the value back out of the shader text and
    /// compares it numerically with this one.</para>
    /// </summary>
    public const float SlabSeamPeriodMeters = 1f / 3f;

    /// <summary>The emissive multiplier <see cref="MaterialKind.Emissive"/>'s doc comment names —
    /// the C#-side half of <see cref="PrimitiveFrag"/>'s own literal, pinned by the same test and for
    /// the same reason as <see cref="SlabSeamPeriodMeters"/>. Above 1 on purpose: a self-lit surface
    /// has to survive the tone curve as a lamp rather than as a bright painted box.</summary>
    public const float EmissiveGain = 1.8f;

    /// <summary>
    /// How far <see cref="MaterialKind.OutlineShell"/> pushes a vertex out from its mesh centre, in
    /// metres of world space — the C#-side half of <see cref="PrimitiveVert"/>'s own
    /// <c>OUTLINE_THICKNESS_METERS</c>, pinned against it by the same test and for the same reason
    /// as <see cref="SlabSeamPeriodMeters"/>.
    /// <para>
    /// The visible band is NARROWER than this on a box: the push is along each vertex's direction
    /// from the mesh centre (see <see cref="MaterialKind.OutlineShell"/> for why it is not along the
    /// normal), so a cube corner travels diagonally and each face ends up about
    /// <c>1/sqrt(3)</c> ≈ 58% of this value further out. A sphere grows by exactly this much. 5 cm
    /// therefore reads as roughly a 3 cm band on the boxes that make up most content.
    /// </para>
    /// <para>
    /// <b>World space, not screen space</b>, so the outline thins out as the camera pulls away
    /// instead of staying a fixed number of pixels. That is the deliberate trade: a pixel-width
    /// outline needs the viewport size in the lighting UBO and an aspect correction, and every
    /// camera that renders these cues today sits at a roughly fixed distance from the thing being
    /// outlined. Revisit it (by appending a viewport vec4 to the UBO, the way TASK-151/168 appended
    /// their own fields) the day a game wants outlines under a free zoom.
    /// </para>
    /// </summary>
    public const float OutlineShellThicknessMeters = 0.05f;

    /// <summary>Shared by cube/sphere/capsule — <c>shape.w</c> push constant selects the
    /// per-vertex deformation (0=cube passthrough, 1=sphere scale, 2=capsule scale+cap offset).
    /// <para>
    /// Used only by the legacy single-cube <c>BoxInstance</c> path
    /// (<see cref="HeadlessRenderer.RenderBoxes"/>/<see cref="HeadlessRenderer.RenderScene"/>,
    /// one <c>vkCmdDraw</c> per instance, push-constant-fed) — the multi-shape
    /// <c>PrimitiveInstance</c> path (cube/sphere/capsule) moved to <see cref="PrimitiveVert"/>'s
    /// instanced layout in TASK-152. Left byte-for-byte unchanged here rather than converted too:
    /// <see cref="HeadlessRenderer.RenderScene"/> shares its pipeline/layout with nothing this
    /// task needed to touch, and a dedicated test (<c>VulkanRenderTests.BoxRender_MatchesGoldenPixels</c>
    /// in Aigis.Tests) pixel-diffs it against a golden — reconverting it was extra blast radius
    /// for zero required acceptance criteria.
    /// </para>
    /// </summary>
    public const string BoxVert = """
        #version 450
        layout(location = 0) in vec3 inPos;
        layout(location = 1) in vec3 inNormal;
        layout(push_constant) uniform Push { mat4 mvp; vec4 color; vec4 shape; vec4 rotation; } pc;
        layout(location = 0) out vec3 vNormal;
        layout(location = 1) out vec4 vColor;
        void main() {
            vec3 local = inPos;
            if (pc.shape.w == 1.0) {
                local *= pc.shape.x;
            } else if (pc.shape.w == 2.0) {
                local = vec3(inPos.x * pc.shape.x,
                    inNormal.y * pc.shape.x + sign(inPos.y) * pc.shape.y,
                    inPos.z * pc.shape.x);
            }
            gl_Position = pc.mvp * vec4(local, 1.0);
            vec3 qv = pc.rotation.xyz;
            vNormal = inNormal + 2.0 * cross(qv, cross(qv, inNormal) + pc.rotation.w * inNormal);
            vColor = pc.color;
        }
        """;

    /// <summary>
    /// TASK-152: the instanced twin of <see cref="BoxVert"/> for the <c>PrimitiveInstance</c>
    /// (cube/sphere/capsule) path — same per-vertex deformation logic, same
    /// mvp/colour/shape/rotation fields, but read from a per-instance vertex-rate buffer
    /// (<see cref="PrimitiveInstancePacker"/>) instead of a push constant re-uploaded before every
    /// single <c>vkCmdDraw</c>. <c>inMvp0..3</c> are the mat4's four columns — Vulkan vertex input
    /// has no native mat4 attribute, so a mat4 instance attribute is always split into four vec4
    /// locations and reassembled with <c>mat4(col0, col1, col2, col3)</c>, which is exactly what a
    /// push-constant mat4's raw bytes were already being interpreted as. Both renderers
    /// (<see cref="HeadlessRenderer"/>/<see cref="WindowedRenderer"/>) bind one draw per
    /// <see cref="PrimitiveKind"/> present (see <see cref="PrimitiveInstancePacker.Batches"/>),
    /// with <c>firstInstance</c> selecting each kind's block — this shader does not know or care
    /// how many instances share it.
    /// </summary>
    /// <summary>
    /// TASK-148 extended this with a second baked matrix per instance, <c>inShadowMvp0..3</c>
    /// (<c>model * shadowViewProjection</c>, folded in on the CPU by
    /// <see cref="PrimitiveInstancePacker"/> exactly the way <c>inMvp0..3</c> already folds in
    /// <c>model * viewProjection</c> — same technique, second matrix) — so the fragment shader
    /// never needs a raw world-space position (this pipeline never had one: only the pre-composed
    /// mvp is packed, see <see cref="PrimitiveInstancePacker.WriteInstance"/>). When shadows are
    /// disabled for a frame the renderer packs <c>Matrix4x4.Identity</c> here — harmless, since
    /// <see cref="PrimitiveFrag"/> gates all shadow sampling on <c>light.shadowParams.x</c>.
    /// </summary>
    public const string PrimitiveVert = """
        #version 450
        layout(location = 0) in vec3 inPos;
        layout(location = 1) in vec3 inNormal;
        layout(location = 2) in vec4 inMvp0;
        layout(location = 3) in vec4 inMvp1;
        layout(location = 4) in vec4 inMvp2;
        layout(location = 5) in vec4 inMvp3;
        layout(location = 6) in vec4 inColor;
        layout(location = 7) in vec4 inShape;
        layout(location = 8) in vec4 inRotation;
        layout(location = 9) in vec4 inShadowMvp0;
        layout(location = 10) in vec4 inShadowMvp1;
        layout(location = 11) in vec4 inShadowMvp2;
        layout(location = 12) in vec4 inShadowMvp3;
        layout(location = 13) in vec4 inWorldCenter;
        layout(location = 14) in vec4 inWorldScale;
        // SurfaceFinish: x = roughness (NEGATIVE means "this instance has none", which is the
        // pre-2026-09-19 shading path), y = metallic, zw reserved. See SurfaceFinish's C# doc comment.
        layout(location = 15) in vec4 inFinish;
        layout(location = 0) out vec3 vNormal;
        layout(location = 1) out vec4 vColor;
        layout(location = 2) out vec4 vShadowClipPos;
        layout(location = 3) out vec3 vWorldCenter;
        layout(location = 4) out vec3 vWorldPos;
        layout(location = 5) out vec4 vFixtureShadowClipPos[4];
        layout(location = 9) out float vMaterial;
        layout(location = 10) out float vSurfaceDetail;
        layout(location = 11) out float vShellFacesAway;
        // The fragment's offset from the instance's own centre, in metres, IN THE INSTANCE'S OWN
        // FRAME -- i.e. before its rotation is applied. What SurfaceDetail.SlabSeamsFollowInstance
        // samples its field at, so that field is nailed to the object through both translation AND
        // rotation. See the fragment shader's own note at the sampling site.
        layout(location = 12) out vec3 vLocalOffset;
        // (roughness, metallic) straight through from inFinish. Identical at all three vertices, so
        // interpolation cannot move it off the value the packer wrote -- including the negative
        // sentinel, which must survive intact for the fragment stage's branch to be exact.
        layout(location = 13) out vec4 vFinish;
        // The instance's own HALF EXTENTS in metres (the same value at every vertex). Together with
        // vLocalOffset -- the fragment's offset from the instance centre, in the instance's own axes,
        // also in metres -- this gives the fragment both of the things the pixel classes need: a
        // METRIC lattice, so a texel is square whatever the part's proportions are, and a NORMALISED
        // position (offset / halfSize, +-1 on a box's faces), which is what picks the box face a
        // fragment is on.
        //
        // Published as the size rather than as the normalised position because the division is exact
        // here and singular there: a part's half extent is never zero, whereas the normalised
        // position passes through zero at the centre of every face.
        layout(location = 14) out vec3 vLocalHalfSize;
        // A photo decal's mapping (left x, top y, width), from a cube's otherwise unused inShape.xyz.
        layout(location = 15) flat out vec3 vDecal;
        layout(set = 0, binding = 6) uniform FixtureShadows { mat4 viewProjections[4]; } fixtureShadows;

        // MaterialKind.OutlineShell's shell thickness, in METRES of world space. Must stay equal to
        // ShaderSource.OutlineShellThicknessMeters on the C# side -- SurfaceDetailTests pins the
        // pair, and that constant's own doc comment carries the reasoning (including why the band a
        // box ends up with is about 58% of this).
        //
        // ASCII ONLY, as everywhere in this file's GLSL -- see PrimitiveFrag's note on what a
        // non-ASCII byte does to glslang's diagnostics.
        const float OUTLINE_THICKNESS_METERS = 0.05;

        void main() {
            mat4 mvp = mat4(inMvp0, inMvp1, inMvp2, inMvp3);
            mat4 shadowMvp = mat4(inShadowMvp0, inShadowMvp1, inShadowMvp2, inShadowMvp3);
            vec3 local = inPos;
            if (inShape.w == 1.0) {
                local *= inShape.x;
            } else if (inShape.w == 2.0) {
                local = vec3(inPos.x * inShape.x,
                    inNormal.y * inShape.x + sign(inPos.y) * inShape.y,
                    inPos.z * inShape.x);
            }
            // MaterialKind.OutlineShell (2 in worldScale's material lane): the inverted hull.
            vShellFacesAway = 0.0;
            if (inWorldScale.w > 1.5) {
                // WHICH SIDE OF THE SHELL IS THIS, without a camera position. Offsetting a point
                // along its own surface normal moves it AWAY from the eye exactly when that normal
                // faces away, so the sign of the resulting depth change IS the sign of
                // dot(normal, viewDir) -- and depth is something the pre-composed mvp can answer on
                // its own. Because the offset enters linearly, mvp * vec4(normal, 0.0) is the whole
                // difference between the two clip positions, so no probe distance has to be chosen
                // and only the SIGN of this value is ever read.
                //
                // Deliberately not `dot(normal, cameraPos - worldPos)`, which would be the obvious
                // formulation: cameraPosition is an OPTIONAL renderer argument (it exists for
                // specular, which most callers leave off), so keying an outline on it would make
                // every existing render call site that forgot it either throw or silently paint the
                // outlined object solid. This works for every caller, and for orthographic
                // projections too -- there w is constant, and the expression collapses to comparing
                // clip z.
                //
                // Cross-multiplied rather than divided: sign(z1/w1 - z0/w0) == sign(z1*w0 - z0*w1)
                // whenever both w are positive, which is true of anything in front of the camera.
                vec4 clipBase = mvp * vec4(local, 1.0);
                vec4 clipNormal = mvp * vec4(inNormal, 0.0);
                vShellFacesAway = clipNormal.z * clipBase.w - clipBase.z * clipNormal.w;

                // Push the vertex out along its own direction FROM THE MESH CENTRE, not along its
                // normal -- the cube's 8 corner positions each appear three times with three
                // different face normals, so a normal-based push separates the faces and leaves a
                // triangular gap at every corner, whereas a position-based one moves all three
                // copies identically and keeps the shell watertight. Faces stay planar either way
                // (all four corners of a cube face share the sign of the axis they span).
                //
                // Dividing by the instance's world scale is what makes the thickness a WORLD-space
                // constant: the offset is applied in unit-mesh space, and Model multiplies it back
                // by that same scale, so a 2 m crate and a 20 cm syringe get the same band.
                float radius = length(local);
                vec3 outward = radius > 1e-6 ? local / radius : vec3(0.0, 1.0, 0.0);
                local += outward * (OUTLINE_THICKNESS_METERS / max(inWorldScale.xyz, vec3(1e-4)));
            }
            gl_Position = mvp * vec4(local, 1.0);
            vShadowClipPos = shadowMvp * vec4(local, 1.0);
            vec3 qv = inRotation.xyz;
            vNormal = inNormal + 2.0 * cross(qv, cross(qv, inNormal) + inRotation.w * inNormal);
            vColor = inColor;
            // The instance's world-space center (same value at every vertex). It is retained for
            // the global sun/specular term; local lights use vWorldPos below so their falloff is
            // continuous across merged floor and wall instances.
            vWorldCenter = inWorldCenter.xyz;
            // A TRUE per-fragment world position for local-light attenuation (point and spot).
            // Specular deliberately still shades from vWorldCenter above -- see SpecularConfig's doc
            // comment). This is not a geometric approximation the way vWorldCenter is: gl_Position
            // above rasterizes `local * Model * viewProjection`, and Model factors into
            // scale-then-rotate-then-translate (PrimitiveInstancePacker.ValidateDecomposable pins
            // that per instance), so applying those same three factors to the same already-deformed
            // `local` gives the rasterized world position for every shape branch. Mathematically
            // equal, float-rounding aside (~1e-5 relative: gl_Position multiplies a CPU-folded
            // Model*viewProjection, this applies S/R/T separately) -- which a lighting term does not
            // care about, but do not read it as bit-for-bit.
            vec3 scaled = local * inWorldScale.xyz;
            // 2026-09-19: the four FINE SurfaceDetail classes sample this too (see the fragment
            // stage's detailPos select), and they get a per-instance PHASE added here.
            //
            // Without one, instance-anchored sampling makes two same-sized parts carry byte-identical
            // grain -- a bench's two planks, a row of crates, four bed castors. The phase is derived
            // from the instance's own SCALE, which is the only per-instance quantity available at this
            // point that is CONSTANT while the object moves: deriving it from the position instead
            // would reintroduce exactly the sliding this anchoring exists to stop. Parts of identical
            // size therefore still match, which is the residual limitation and is far less visible
            // than the alternative.
            //
            // Computed for every instance rather than branched: it is three multiplies and a fract on
            // a value that is uniform across the draw, and the fragment stage only reads it for the
            // classes that want it.
            vec3 detailPhase = fract(inWorldScale.xyz * vec3(7.31, 3.77, 5.19)) * 4.0;
            // Codes 5..8 only -- the four fine classes. Three classes are deliberately outside it
            // even though the fragment stage samples two of them instance-relative too:
            // SlabSeamsFollowInstance (4), whose whole point is a seam lattice that lines up with
            // the geometry carrying it; Regolith (9), which reads vWorldPos anyway; and PixelCloth
            // (10), whose texel lattice is anchored to the part's own half-size (vLocalHalfSize
            // below) so a sleeve and the sleeve on the other arm come out matching rather than
            // offset by a scale-derived fraction of a texel.
            vLocalOffset = scaled
                + ((inWorldCenter.w > 4.5 && inWorldCenter.w < 8.5) ? detailPhase : vec3(0.0));
            vLocalHalfSize = inWorldScale.xyz * 0.5;
            vWorldPos = inWorldCenter.xyz + scaled
                + 2.0 * cross(qv, cross(qv, scaled) + inRotation.w * scaled);
            // The MaterialKind selector (0 = Standard/ShadowCastOnly, 1 = Emissive, 2 = OutlineShell),
            // packed into worldScale's spare lane by PrimitiveInstancePacker.WriteInstance -- see
            // that write's own comment. Whole numbers, identical at all three vertices, so
            // interpolation cannot land it between two codes.
            vMaterial = inWorldScale.w;
            // SurfaceDetail's selector, packed into worldCenter's spare lane by
            // PrimitiveInstancePacker.WriteInstance -- see that write's own comment. Flat-shaded in
            // effect (the same whole number at all three vertices), so interpolation cannot land it
            // between two enum values.
            vSurfaceDetail = inWorldCenter.w;
            vFinish = inFinish;
            vDecal = inShape.xyz;
            for (int i = 0; i < 4; i++) {
                vFixtureShadowClipPos[i] = fixtureShadows.viewProjections[i] * vec4(vWorldPos, 1.0);
            }
        }
        """;

    /// <summary>
    /// TASK-148's depth-only twin of <see cref="PrimitiveVert"/>, used solely by the shadow
    /// depth pass (<c>HeadlessRenderer.Shadows.cs</c> and its <see cref="WindowedRenderer"/>
    /// equivalent): same cube/sphere/capsule vertex-position
    /// deformation, but no fragment stage at all (the pipeline has no color attachment) and no
    /// normal/rotation math, since only the depth this vertex ends up at matters. Reads
    /// <c>inShadowMvp0..3</c> as its position matrix directly — this shader IS how that map gets
    /// built; <see cref="PrimitiveVert"/> only samples it afterward.
    /// </summary>
    public const string ShadowDepthVert = """
        #version 450
        layout(location = 0) in vec3 inPos;
        layout(location = 1) in vec3 inNormal;
        layout(location = 7) in vec4 inShape;
        layout(location = 9) in vec4 inShadowMvp0;
        layout(location = 10) in vec4 inShadowMvp1;
        layout(location = 11) in vec4 inShadowMvp2;
        layout(location = 12) in vec4 inShadowMvp3;
        void main() {
            mat4 mvp = mat4(inShadowMvp0, inShadowMvp1, inShadowMvp2, inShadowMvp3);
            vec3 local = inPos;
            if (inShape.w == 1.0) {
                local *= inShape.x;
            } else if (inShape.w == 2.0) {
                local = vec3(inPos.x * inShape.x,
                    inNormal.y * inShape.x + sign(inPos.y) * inShape.y,
                    inPos.z * inShape.x);
            }
            gl_Position = mvp * vec4(local, 1.0);
        }
        """;

    /// <summary>Depth-only vertex shader for one downward ceiling fixture. Unlike the sun's
    /// baked per-instance shadow matrix, this reconstructs world space then receives that
    /// fixture's dedicated projection as a push constant.</summary>
    public const string FixtureShadowDepthVert = """
        #version 450
        layout(location = 0) in vec3 inPos;
        layout(location = 1) in vec3 inNormal;
        layout(location = 7) in vec4 inShape;
        layout(location = 8) in vec4 inRotation;
        layout(location = 13) in vec4 inWorldCenter;
        layout(location = 14) in vec4 inWorldScale;
        layout(set = 0, binding = 6) uniform FixtureShadows { mat4 viewProjections[4]; } fixtureShadows;
        layout(push_constant) uniform Push { int shadowIndex; } pc;
        void main() {
            vec3 local = inPos;
            if (inShape.w == 1.0) {
                local *= inShape.x;
            } else if (inShape.w == 2.0) {
                local = vec3(inPos.x * inShape.x,
                    inNormal.y * inShape.x + sign(inPos.y) * inShape.y,
                    inPos.z * inShape.x);
            }
            vec3 scaled = local * inWorldScale.xyz;
            vec3 qv = inRotation.xyz;
            vec3 world = inWorldCenter.xyz + scaled
                + 2.0 * cross(qv, cross(qv, scaled) + inRotation.w * scaled);
            gl_Position = fixtureShadows.viewProjections[pc.shadowIndex] * vec4(world, 1.0);
        }
        """;

    /// <summary>
    /// TASK-036: directional ("sun") + ambient light, read from a small uniform buffer
    /// instead of the old hardcoded direction/coefficients — see <see cref="SceneLighting"/>'s doc
    /// comment for why a UBO and not more push-constant bytes. <c>sunDirectionIntensity</c>
    /// packs direction in <c>.xyz</c> and intensity in <c>.w</c> (mirrors the push-constant
    /// convention already used for <c>Shape</c>/<c>Rotation</c> in <see cref="BoxVert"/>);
    /// <c>ambientColorIntensity</c> does the same for the ambient floor.
    /// </summary>
    public const string BoxFrag = $$"""
        #version 450
        layout(location = 0) in vec3 vNormal;
        layout(location = 1) in vec4 vColor;
        layout(location = 0) out vec4 outColor;
        layout(set = 0, binding = 0) uniform Lighting {
            vec4 sunDirectionIntensity;
            vec4 sunColor;
            vec4 ambientColorIntensity;
            vec4 shadowParams;
            vec4 pointLightCount;
            vec4 unusedPointLights[512];
            vec4 ambientGroundColor;
            vec4 cameraPositionSpecularIntensity;
            vec4 specularShininessExposureToneEnabled;
            vec4 spotLightCount;
            vec4 unusedSpotLights[16];
            vec4 irradianceOriginEnabled;
            vec4 irradianceScale;
            vec4 irradianceBias;
            vec4 renderStyle;
        } light;
        {{CoverageDitherGlsl}}
        {{RenderStyleGlsl}}
        void main() {
            vec3 lightDir = normalize(light.sunDirectionIntensity.xyz);
            float diffuse = max(dot(normalize(vNormal), lightDir), 0.0) * light.sunDirectionIntensity.w;
            vec3 lit = light.ambientColorIntensity.rgb * light.ambientColorIntensity.a
                + light.sunColor.rgb * diffuse;
            outColor = vec4(ApplyRenderStyle(vColor.rgb * lit), 1.0);
        }
        """;

    /// <summary>
    /// TASK-148: the primitive-instanced pipeline's own fragment shader — split off from
    /// <see cref="BoxFrag"/> (which the legacy <see cref="BoxVert"/>/<c>RenderScene</c> path
    /// still uses unchanged) because shadow sampling needs <c>vShadowClipPos</c>, and
    /// <see cref="BoxVert"/> has no world-space position to compute it from (only a
    /// pre-composed mvp — see <see cref="PrimitiveVert"/>'s doc comment). Same directional +
    /// ambient math as <see cref="BoxFrag"/>, with the diffuse term additionally scaled by a
    /// 3x3 PCF shadow factor (<c>1.0</c> = fully lit, <c>0.0</c> = fully shadowed) sampled from
    /// <c>shadowMap</c>. <c>light.shadowParams</c> packs <c>x</c>=enabled(0/1),
    /// <c>y</c>=<see cref="ShadowMapConfig.ComparisonBias"/>, <c>z</c>=one shadow-map texel in
    /// UV space (<c>1/ShadowMapConfig.MapSize</c>), <c>w</c> unused.
    /// <para>
    /// Manual PCF (plain <c>sampler2D</c> + a 3x3 loop of raw-depth compares) rather than a
    /// hardware compare sampler: this engine has no existing compare-sampler infrastructure to
    /// extend, and 9 taps is already enough to turn a single ragdoll capsule's shadow from a
    /// stair-stepped blob into a soft edge at <see cref="ShadowMapConfig.MapSize"/> (TASK-148
    /// report has the before/after crop).
    /// </para>
    /// </summary>
    public const string PrimitiveFrag = $$"""
        #version 450
        layout(location = 0) in vec3 vNormal;
        layout(location = 1) in vec4 vColor;
        layout(location = 2) in vec4 vShadowClipPos;
        layout(location = 3) in vec3 vWorldCenter;
        layout(location = 4) in vec3 vWorldPos;
        layout(location = 5) in vec4 vFixtureShadowClipPos[4];
        layout(location = 9) in float vMaterial;
        layout(location = 10) in float vSurfaceDetail;
        layout(location = 11) in float vShellFacesAway;
        layout(location = 12) in vec3 vLocalOffset;
        layout(location = 13) in vec4 vFinish;
        layout(location = 14) in vec3 vLocalHalfSize;
        layout(location = 15) flat in vec3 vDecal;
        layout(location = 0) out vec4 outColor;

        struct PointLightGpu { vec4 positionRange; vec4 colorIntensity; };
        // TASK-168. Four vec4s = 64 std140-clean bytes; must stay byte-identical to
        // HeadlessRenderer.SpotLightGpu / WindowedRenderer.SpotLightGpu (pinned per FIELD, not just
        // by total size, in PrimitiveLightingUboLayoutTests -- every field here is a vec4, so a
        // field-order slip would leave the size correct and scramble the data).
        struct SpotLightGpu {
            vec4 positionRange;      // xyz = world position (cone apex), w = range
            vec4 colorIntensity;     // rgb = colour, a = intensity
            vec4 directionCosOuter;  // xyz = direction the cone POINTS, w = cos(outer half-angle)
            vec4 coneCosInner;       // x = cos(inner half-angle), yzw reserved
        };

        layout(set = 0, binding = 0) uniform Lighting {
            vec4 sunDirectionIntensity;
            vec4 sunColor;
            vec4 ambientColorIntensity;
            vec4 shadowParams;
            vec4 pointLightCount;
            PointLightGpu pointLights[256];
            // TASK-151: appended AFTER pointLights (never interspersed among the fields TASK-149
            // already pinned by offset -- see HeadlessRenderer.LightingUniform's doc comment) so
            // every existing std140 offset above stays byte-for-byte unchanged.
            vec4 ambientGroundColor;                    // rgb = AmbientLight.GroundColor, w unused
            vec4 cameraPositionSpecularIntensity;        // xyz = camera eye, w = SpecularConfig.Intensity
            vec4 specularShininessExposureToneEnabled;   // x = Shininess, y = Exposure, z = ToneMapping.Enabled (0/1), w unused
            // TASK-168: appended after TASK-151's fields for the same reason those were appended
            // after pointLights[8] -- a SEPARATE array rather than a third vec4 inside
            // PointLightGpu, because growing the per-light stride would move every std140 offset
            // after pointLights[8] that PrimitiveLightingUboLayoutTests already pins, for no gain.
            // Keeping spots in their own array also means a spot whose cone points away from
            // everything can never evict a point light that is actually lighting the player.
            vec4 spotLightCount;                         // x = count as float, yz = SceneLighting.PatchesOffsetXz, w reserved
            SpotLightGpu spotLights[4];
            // TASK-245: appended after the spot array, for the third time and for the same reason
            // TASK-151's and TASK-168's fields were -- every std140 offset above stays byte-for-byte
            // where PrimitiveLightingUboLayoutTests already pinned it.
            vec4 irradianceOriginEnabled;   // xyz = volume origin (world), w = 1 when a volume is bound
            vec4 irradianceScale;           // xyz = world metres -> texture coords, w = normal bias (m)
            vec4 irradianceBias;            // xyz = half-texel offset, w = edge fade width (m)
            vec4 renderStyle;               // x = RenderStyle, 0 = Natural
        } light;
        layout(set = 0, binding = 1) uniform sampler2D shadowMap;
        layout(set = 0, binding = 2) uniform sampler2D fixtureShadowMap0;
        layout(set = 0, binding = 3) uniform sampler2D fixtureShadowMap1;
        layout(set = 0, binding = 4) uniform sampler2D fixtureShadowMap2;
        layout(set = 0, binding = 5) uniform sampler2D fixtureShadowMap3;
        // TASK-245: one 3D texture per colour channel, each texel (L1.x, L1.y, L1.z, L0). Always
        // bound -- a 1x1x1 stand-in when no volume is set, because Vulkan requires every declared
        // descriptor to be valid whether or not the shader reads it.
        layout(set = 0, binding = 7) uniform sampler3D irradianceRed;
        layout(set = 0, binding = 8) uniform sampler3D irradianceGreen;
        layout(set = 0, binding = 9) uniform sampler3D irradianceBlue;
        // 2026-10-06: greyscale surface detail maps (Aigis.Render.SurfaceTextureSet), mean 0.5.
        layout(set = 0, binding = 10) uniform sampler2DArray surfaceTextures;

        // Triplanar: the instance has no UVs, so the map is projected along the three world axes and
        // blended by the geometric normal (sharpened, so a wall takes one projection and a bevel two).
        // vFinish.z = layer + 1 (0 = untextured), vFinish.w = metres per repeat (0 = 2 m). The result
        // multiplies the albedo: 1.0 where the map is at its mean, so colour is preserved.
        // ---- Facade windows (SurfaceDetail.FacadeWindows, 2026-10-06; styles 2026-10-07) -----------
        // Building faces drawn procedurally in the instance's own frame, so the pattern meets the
        // corners and scales with the box: storeys from its height, bays from its width. vDecal.x is
        // the STYLE (0 = the original punched grid, which tests pin):
        //   1 office punched     2 slab-balcony mansion   3 railing-balcony mansion
        //   4 tower mansion      5 glass curtain wall     6 ribbon-window office
        //   7 vertical strips    8 small apartment        9 brick-base office
        // Outputs: glass (0..1, glass colour), tint (multiplier on the solid wall), litCell (a
        // per-window hash for night lighting) and curtain (glass reads as reflective blue).
        // Far away a storey is under a pixel: everything fades to its average instead of shimmering.
        const vec3 WINDOW_GLASS = vec3(0.20, 0.24, 0.30);
        const vec3 CURTAIN_GLASS = vec3(0.30, 0.42, 0.55);
        const vec3 WINDOW_LIGHT = vec3(1.00, 0.80, 0.52) * 1.3;

        float Band(float x, float a, float b) {
            return step(a, x) * step(x, b);
        }

        float CellHash(vec2 cell, float face) {
            float seed = dot(vWorldCenter.xyz, vec3(0.131, 0.713, 0.377));
            return fract(sin(dot(vec4(cell, face, seed), vec4(12.9898, 78.233, 37.719, 4.581))) * 43758.5453);
        }

        void FacadeWindows(out float mask, out float lit, out float tint, out float curtain) {
            vec3 hs = max(vLocalHalfSize, vec3(1e-3));
            vec3 rel = abs(vLocalOffset / hs);
            bool xFace = rel.x > rel.z;
            float along = xFace ? vLocalOffset.z + hs.z : vLocalOffset.x + hs.x;
            float faceWidth = xFace ? 2.0 * hs.z : 2.0 * hs.x;
            // The long faces are the fronts: balconies go there, the ends get small windows.
            bool front = xFace ? hs.z >= hs.x : hs.x > hs.z;
            float up = vLocalOffset.y + hs.y;
            float face = xFace ? (vLocalOffset.x > 0.0 ? 1.0 : 2.0) : (vLocalOffset.z > 0.0 ? 3.0 : 4.0);
            float style = floor(vDecal.x + 0.5);
            mask = 0.0;
            lit = 0.0;
            tint = 1.0;
            curtain = 0.0;
            if (rel.y > 0.995) {
                return;
            }

            if (style < 0.5) {
                vec2 g = vec2(along, up) / vec2(2.4, 3.2);
                vec2 cell = floor(g);
                vec2 f = fract(g);
                float inWindow = Band(f.x, 0.18, 0.82) * Band(f.y, 0.30, 0.78) * step(1.0, cell.y);
                float fade0 = clamp(max(fwidth(g).x, fwidth(g).y) * 2.0 - 0.5, 0.0, 1.0);
                mask = mix(inWindow, 0.3, fade0);
                lit = mix(step(CellHash(cell, face), 0.45), 0.45, fade0);
                return;
            }

            float storey = style == 1.0 || style == 5.0 || style == 6.0 || style == 7.0 || style == 9.0 ? 3.6 : 2.9;
            // Bays sized so a whole number fit the face: the pattern closes at both corners.
            float bayTarget = style == 5.0 ? 1.5 : style == 7.0 ? 1.5 : style == 8.0 ? 3.2 : style == 1.0 || style == 9.0 ? 1.8 : 3.4;
            float bays = max(1.0, floor(faceWidth / bayTarget + 0.5));
            float bay = faceWidth / bays;
            vec2 g = vec2(along / bay, up / storey);
            vec2 cell = floor(g);
            vec2 f = fract(g);
            float fade = clamp(max(fwidth(g).x, fwidth(g).y) * 2.5 - 0.5, 0.0, 1.0);
            float groundFloor = 1.0 - step(1.0, cell.y);
            float glass = 0.0;
            float t = 1.0;
            float average = 0.35;

            if (style == 1.0) {
                glass = Band(f.x, 0.16, 0.84) * Band(f.y, 0.28, 0.80);
                t = 1.0 + 0.06 * (1.0 - Band(f.x, 0.10, 0.90));
                average = 0.35;
            } else if (style == 2.0 || style == 3.0 || style == 4.0) {
                bool balconies = front || style == 4.0;
                if (balconies) {
                    // A balcony storey, bottom to top: parapet, the recessed sliding doors (dark under
                    // the slab above), the slab edge.
                    float parapet = Band(f.y, 0.0, 0.36);
                    float recess = Band(f.y, 0.36, 0.86);
                    float doors = recess * Band(fract(along / 1.7), 0.06, 0.94);
                    glass = doors * Band(f.y, 0.40, 0.84);
                    // Strong contrast is the whole read (reference photos): white parapets and slab
                    // edges against a recess in shadow, darkest under the slab above.
                    t = parapet * (style == 3.0 ? mix(mix(1.30, 0.55, Band(fract(along / 0.14), 0.0, 0.25)), 1.11, clamp(fwidth(along / 0.14) * 2.0 - 0.5, 0.0, 1.0)) : 1.32)
                        + recess * mix(0.55, 0.28, smoothstep(0.66, 0.86, f.y))
                        + Band(f.y, 0.86, 1.0) * 1.22;
                    // Party walls between flats, and for the tower its columns every other bay.
                    float wallEvery = style == 4.0 ? 2.0 : 2.0;
                    float party = Band(fract(g.x / wallEvery) * wallEvery, 0.0, 0.07);
                    t = mix(t, style == 4.0 ? 0.96 : 0.9, party);
                    glass *= 1.0 - party;
                    average = 0.3;
                } else {
                    glass = Band(f.x, 0.36, 0.64) * Band(f.y, 0.40, 0.78);
                    average = 0.1;
                }
            } else if (style == 5.0) {
                float mullion = 1.0 - Band(f.x, 0.03, 0.97);
                float spandrel = Band(f.y, 0.0, 0.10);
                glass = (1.0 - mullion) * (1.0 - spandrel);
                t = mix(1.0, 0.8, spandrel);
                // Brighter toward the top, where the glass reflects open sky rather than the street.
                curtain = 1.0 + clamp(up / (2.0 * hs.y), 0.0, 1.0);
                average = 0.85;
            } else if (style == 6.0) {
                glass = Band(f.y, 0.32, 0.80) * (1.0 - Band(fract(along / 1.5), 0.0, 0.04));
                t = 1.04;
                average = 0.45;
            } else if (style == 7.0) {
                glass = Band(f.x, 0.30, 0.86) * Band(f.y, 0.08, 0.96);
                t = mix(1.08, 0.92, Band(f.y, 0.0, 0.08));
                curtain = 0.5;
                average = 0.5;
            } else if (style == 8.0) {
                float window = Band(f.x, 0.22, 0.78) * Band(f.y, 0.36, 0.80);
                float sill = Band(f.x, 0.16, 0.84) * Band(f.y, 0.24, 0.36);
                glass = window;
                t = mix(1.0, 1.18, sill) * mix(1.0, 0.6, Band(f.x, 0.16, 0.84) * Band(f.y, 0.80, 0.86));
                average = 0.25;
            } else {
                glass = Band(f.x, 0.16, 0.84) * Band(f.y, 0.28, 0.80);
                // Two storeys of brick and shopfront under the offices.
                float base = 1.0 - step(2.0, cell.y);
                t = mix(1.0, 0.62, base);
                glass = mix(glass, Band(f.y, 0.12, 0.86) * Band(f.x, 0.04, 0.96), base);
                average = 0.4;
            }

            // The ground floor: entrances and shopfronts rather than flats.
            if (style != 9.0 && style != 5.0) {
                glass = mix(glass, Band(f.y, 0.08, 0.80) * Band(f.x, 0.10, 0.90) * step(0.5, CellHash(cell, face + 9.0)), groundFloor);
                t = mix(t, 0.85, groundFloor);
            }

            mask = mix(glass, average, fade);
            tint = mix(t, 1.0, fade);
            lit = mix(step(CellHash(cell, face), 0.45), 0.45, fade);
        }

        // ---- Water (SurfaceDetail.Water, 2026-10-06) ------------------------------------------------
        // The same two-train swell SeaSurface.HeightAt positions the tiles with -- a long swell
        // skewed 0.28 across the ship and a 1.73x shorter chop crossing it -- differentiated here per
        // pixel, so the normal and the crest brightness are continuous across tile edges. vDecal
        // carries (drift metres, amplitude metres, wavenumber). Two faint short ripples on top give
        // the surface a glitter scale finer than a tile.
        vec3 WaterNormal(vec3 p, out vec3 albedoTint) {
            float drift = vDecal.x;
            float a = vDecal.y;
            float k = max(vDecal.z, 1e-3);
            float sx = p.x + drift;
            float swellPhase = k * (sx + 0.28 * p.z);
            float chopPhase = 1.73 * k * (0.62 * sx - 0.78 * p.z);
            float h = 0.62 * sin(swellPhase) + 0.38 * sin(chopPhase);
            vec2 grad = a * (0.62 * k * cos(swellPhase) * vec2(1.0, 0.28)
                + 0.38 * 1.73 * k * cos(chopPhase) * vec2(0.62, -0.78));
            float r1 = 3.7 * k;
            float r2 = 5.3 * k;
            grad += 0.035 * r1 * cos(r1 * dot(vec2(sx, p.z), vec2(0.8, 0.6))) * vec2(0.8, 0.6);
            grad += 0.025 * r2 * cos(r2 * dot(vec2(sx, p.z), vec2(-0.3, 0.95))) * vec2(-0.3, 0.95);
            // Crests lighter (foam-ward, thinner water), troughs deeper.
            albedoTint = vec3(0.78 + 0.32 * clamp(h * 0.5 + 0.5, 0.0, 1.0));
            return normalize(vec3(-grad.x, 1.0, -grad.y));
        }

        // 0 by day, 1 once the sun is down: windows light as the light fails.
        float WindowNight() {
            return clamp(1.0 - light.sunDirectionIntensity.w / 0.5, 0.0, 1.0);
        }

        vec3 SurfaceTextureAlbedo(vec3 worldPos, vec3 geometricNormal) {
            if (vFinish.z < 0.5) {
                return vec3(1.0);
            }

            float layer = vFinish.z - 1.0;
            if (vFinish.w < -0.5) {
                // Photo decal: R, G, B in three consecutive layers, mapped once over the XY plane.
                // Each layer is normalised to mean 0.5, so x2 keeps the photo's channel balance
                // relative to the instance colour, which the caller sets to the channel means.
                vec2 uv = clamp(vec2(vWorldPos.x - vDecal.x, vDecal.y - vWorldPos.y) / max(vDecal.z, 1e-3), 0.0, 1.0);
                return 2.0 * vec3(
                    texture(surfaceTextures, vec3(uv, layer)).r,
                    texture(surfaceTextures, vec3(uv, layer + 1.0)).r,
                    texture(surfaceTextures, vec3(uv, layer + 2.0)).r);
            }

            float metres = vFinish.w > 0.0 ? vFinish.w : 2.0;
            vec3 w = pow(abs(geometricNormal), vec3(4.0));
            w /= max(w.x + w.y + w.z, 1e-5);
            // Anti-tiling (2026-10-06, user report: periodic cracks on asphalt): a second read
            // at 0.37x the frequency, rotated ~33 degrees and offset, averaged with the first. A crack
            // that repeats every tile in one read lands somewhere unrelated in the other, so no single
            // feature tiles on a visible period; the mean stays 0.5, so colour is still preserved.
            vec3 p = worldPos / metres;
            vec3 q = vec3(0.84 * p.x - 0.55 * p.z, p.y, 0.55 * p.x + 0.84 * p.z) * 0.37 + vec3(0.31, 0.17, 0.63);
            float t1 = texture(surfaceTextures, vec3(p.z, -p.y, layer)).r * w.x
                + texture(surfaceTextures, vec3(p.x, p.z, layer)).r * w.y
                + texture(surfaceTextures, vec3(p.x, -p.y, layer)).r * w.z;
            float t2 = texture(surfaceTextures, vec3(q.z, -q.y, layer)).r * w.x
                + texture(surfaceTextures, vec3(q.x, q.z, layer)).r * w.y
                + texture(surfaceTextures, vec3(q.x, -q.y, layer)).r * w.z;
            return vec3(t1 + t2);
        }

        float SampleShadowPcf(sampler2D depthMap, vec3 ndc) {
            vec2 uv = ndc.xy * 0.5 + 0.5;
            if (uv.x < 0.0 || uv.x > 1.0 || uv.y < 0.0 || uv.y > 1.0 || ndc.z > 1.0) {
                return 1.0;
            }
            float currentDepth = ndc.z - light.shadowParams.y;
            float texel = light.shadowParams.z;
            float lit = 0.0;
            for (int x = -1; x <= 1; x++) {
                for (int y = -1; y <= 1; y++) {
                    float sampled = texture(depthMap, uv + vec2(float(x), float(y)) * texel).r;
                    lit += currentDepth > sampled ? 0.0 : 1.0;
                }
            }
            return lit / 9.0;
        }

        float SampleFixtureShadow(int index, vec3 ndc) {
            if (index == 0) return SampleShadowPcf(fixtureShadowMap0, ndc);
            if (index == 1) return SampleShadowPcf(fixtureShadowMap1, ndc);
            if (index == 2) return SampleShadowPcf(fixtureShadowMap2, ndc);
            return SampleShadowPcf(fixtureShadowMap3, ndc);
        }

        // Point/spot are deliberately NOT shadow-casting (sun shadows first; add per-light
        // shadows only if required). Both evaluate from vWorldPos so distance attenuation is
        // continuous across a single merged floor or wall instance.
        vec3 PointLightContribution(vec3 normal) {
            vec3 total = vec3(0.0);
            int count = int(light.pointLightCount.x);
            for (int i = 0; i < 256; i++) {
                if (i >= count) {
                    break;
                }
                vec3 toLight = light.pointLights[i].positionRange.xyz - vWorldPos;
                float dist = length(toLight);
                float range = max(light.pointLights[i].positionRange.w, 0.0001);
                vec3 lightDir = dist > 0.0001 ? toLight / dist : vec3(0.0, 1.0, 0.0);
                float ndotl = max(dot(normal, lightDir), 0.0);
                // A surface facing away from this light contributes EXACTLY zero (the whole term is
                // multiplied by ndotl), so everything below -- including up to nine shadow-map taps
                // for one of the first four lights -- would be multiplied away. Skipping it is
                // bit-for-bit identical output, and it is the single cheapest fragment-cost cut
                // available: with 17 point lights in a Triage ward, roughly half of every lit
                // surface's light iterations end here.
                if (ndotl <= 0.0) {
                    continue;
                }
                float falloff = 1.0 / (1.0 + (dist / range) * (dist / range));
                float fixtureShadow = i < int(light.shadowParams.w) ? SampleFixtureShadow(i, vFixtureShadowClipPos[i].xyz / vFixtureShadowClipPos[i].w) : 1.0;
                total += light.pointLights[i].colorIntensity.rgb * light.pointLights[i].colorIntensity.a * ndotl * falloff * fixtureShadow;
            }
            return total;
        }

        // Spot/cone lights also shade per fragment from vWorldPos. A 55deg cone aimed at the floor
        // therefore pools light on Triage's CellRectMerge'd floor slabs instead of making an
        // entire slab flip brightness. Specular still uses the per-object vWorldCenter.
        //
        // SIGN CONVENTION, and it is the OPPOSITE of both the sun and the point lights right above:
        // directionCosOuter.xyz is the direction the cone POINTS (the way its rays travel), whereas
        // light.sunDirectionIntensity.xyz and PointLightContribution's `lightDir` are both
        // surface-TOWARD-light. So the cone test needs -L (light -> fragment) dotted with the aim
        // axis, while the diffuse term below still needs +L. Do not "simplify" the two into one sign.
        //
        // Not shadow-casting, deliberately -- the same scope line TASK-148/149 drew, and what the
        // reference fixture itself does (shadows = None, cut upstream for cost).
        vec3 SpotLightContribution(vec3 normal) {
            vec3 total = vec3(0.0);
            int count = int(light.spotLightCount.x);
            for (int i = 0; i < 4; i++) {
                if (i >= count) {
                    break;
                }
                vec3 toLight = light.spotLights[i].positionRange.xyz - vWorldPos;
                float dist = length(toLight);
                if (dist < 0.0001) {
                    continue;
                }
                vec3 lightDir = toLight / dist;
                vec3 aim = normalize(light.spotLights[i].directionCosOuter.xyz);
                float spotCos = dot(-lightDir, aim);
                // cosInner > cosOuter always holds (cosine decreases as the half-angle grows, and
                // the inner half-angle is a fraction of the outer) -- GLSL smoothstep is undefined
                // when edge0 >= edge1, and swapping these two would render a dark core inside a
                // bright ring. SpotLight.InnerCosine derives the ordering once, on the CPU.
                float cone = smoothstep(
                    light.spotLights[i].directionCosOuter.w,
                    light.spotLights[i].coneCosInner.x,
                    spotCos);
                if (cone <= 0.0) {
                    continue;
                }
                float ndotl = max(dot(normal, lightDir), 0.0);
                float range = max(light.spotLights[i].positionRange.w, 0.0001);
                // Same falloff law PointLightContribution uses, reused rather than inventing a
                // second differently-shaped one.
                float falloff = 1.0 / (1.0 + (dist / range) * (dist / range));
                total += light.spotLights[i].colorIntensity.rgb
                    * light.spotLights[i].colorIntensity.a * ndotl * falloff * cone;
            }
            return total;
        }

        // TASK-151: hemisphere ambient -- mix(ground, sky, normal.y*0.5+0.5) collapses to the
        // pre-TASK-151 flat ambient term exactly when AmbientLight.GroundColor == AmbientLight.Color
        // (SceneLighting.Default's shape), so this is a strict superset, not a behavior change,
        // for every caller that never sets GroundColor.
        vec3 HemisphereAmbient(vec3 normal) {
            vec3 mixed = mix(light.ambientGroundColor.rgb, light.ambientColorIntensity.rgb, normal.y * 0.5 + 0.5);
            return mixed * light.ambientColorIntensity.a;
        }

        // TASK-245: the baked indirect-light field (IrradianceVolume), replacing the flat term
        // above wherever a volume covers this fragment. Three texture fetches and three dot
        // products: every scale factor was folded into the texels by IrradianceBaker, so there is
        // nothing to multiply here.
        //
        // Three details, each of which is a visible artefact when omitted:
        //
        //  * The NORMAL BIAS. A wall's own surface and the space behind it fall in the same
        //    trilinear neighbourhood at probe spacing, so a lit room bleeds through a wall into a
        //    dark one. Pushing the sample point along the normal first is the standard fix.
        //  * The EDGE FADE. A volume covers a level, not a world. Without a cross-fade back to
        //    HemisphereAmbient, geometry just outside it would snap to whatever the boundary probes
        //    happen to hold -- and with it, a volume is a strict local improvement that degrades to
        //    exactly the old behaviour everywhere it does not reach. Outside the box the fade is
        //    already 0, so no clamping of uvw is needed for correctness.
        //  * The max() against zero. An L1 fit of a field with strong directional contrast can
        //    reconstruct negative for normals pointing away from the light; negative ambient would
        //    subtract from the sun and punch black holes in a lit surface.
        vec3 AmbientTerm(vec3 normal) {
            vec3 flatAmbient = HemisphereAmbient(normal);
            if (light.irradianceOriginEnabled.w < 0.5) {
                return flatAmbient;
            }

            vec3 samplePos = vWorldPos + normal * light.irradianceScale.w;
            vec3 uvw = (samplePos - light.irradianceOriginEnabled.xyz) * light.irradianceScale.xyz
                     + light.irradianceBias.xyz;
            vec3 fadeUv = max(light.irradianceScale.xyz * light.irradianceBias.w, vec3(1e-6));
            vec3 toFace = min(uvw, vec3(1.0) - uvw) / fadeUv;
            float fade = clamp(min(min(toFace.x, toFace.y), toFace.z), 0.0, 1.0);
            if (fade <= 0.0) {
                return flatAmbient;
            }

            vec4 nd = vec4(normal, 1.0);
            vec3 probe = vec3(
                dot(texture(irradianceRed, uvw), nd),
                dot(texture(irradianceGreen, uvw), nd),
                dot(texture(irradianceBlue, uvw), nd));
            return mix(flatAmbient, max(probe, vec3(0.0)), fade);
        }

        // TASK-151: a global (not per-material -- see SpecularConfig's own doc comment) Blinn-Phong
        // highlight. View direction uses the SAME per-object approximation PointLightContribution
        // already established (vWorldCenter standing in for the true per-fragment surface position
        // -- this pipeline has never had one, see PrimitiveVert's doc comment) rather than inventing
        // a second, differently-shaped approximation next to it. Zero cost beyond one pow() when
        // light.cameraPositionSpecularIntensity.w (SpecularConfig.Intensity) is 0 -- the default for
        // every pre-TASK-151 caller.
        vec3 SpecularContribution(vec3 normal, vec3 sunDir, float sunNdotL) {
            float specularIntensity = light.cameraPositionSpecularIntensity.w;
            if (specularIntensity <= 0.0 || sunNdotL <= 0.0) {
                return vec3(0.0);
            }
            vec3 viewDir = normalize(light.cameraPositionSpecularIntensity.xyz - vWorldCenter);
            vec3 halfVec = normalize(sunDir + viewDir);
            float shininess = max(light.specularShininessExposureToneEnabled.x, 1.0);
            float spec = pow(max(dot(normal, halfVec), 0.0), shininess);
            return light.sunColor.rgb * (spec * specularIntensity);
        }

        // ---- SurfaceFinish: a real microfacet lobe, per instance --------------------------------
        //
        // 2026-09-19. Everything below runs ONLY for an instance that declares a SurfaceFinish
        // (vFinish.x >= 0); the legacy SpecularContribution above is what every other instance takes,
        // unchanged, which is what keeps every pre-existing golden byte-identical. See SurfaceFinish's
        // C# doc comment for why this is opt-in rather than the new default.
        //
        // Three differences from the legacy term, and each one is the reason a material reads as a
        // material rather than as "a box with a shiny spot":
        //
        //  * The width of the highlight is PER INSTANCE (roughness), not per scene. That alone is
        //    most of the difference between cloth and chrome.
        //  * F0 is tinted by the albedo for a metal, and the diffuse term is switched off for it.
        //    Metal reflects in its own colour and does not scatter -- this is the switch that makes
        //    brass look like brass instead of like yellow plastic.
        //  * The view direction is PER FRAGMENT (vWorldPos), not per object (vWorldCenter). The
        //    per-object approximation is fine for a 40-pixel prop across a ward and is visibly wrong
        //    at a metre, where one flat panel is most of the screen: the whole panel's highlight
        //    switches on and off together instead of travelling across it.

        const float PI = 3.14159265;

        // SurfaceFinish.MinimumRoughness on the C# side -- SurfaceFinishTests pins the pair, and that
        // constant's own doc comment carries the reasoning (a perfect mirror's highlight is sub-pixel
        // and sparkles, and this engine has nothing to filter that with).
        // ShaderSource.EmissiveGain on the C# side. Named rather than written inline at its use site
        // (where it was a bare `vColor.rgb * 1.8` until 2026-09-19) so the gate that pins the pair has
        // a stable thing to look for: it used to match on the surrounding expression text, and the
        // first edit to that expression turned the gate into a silent no-op.
        const float EMISSIVE_GAIN = 1.8;

        const float MIN_ROUGHNESS = 0.045;

        // How much brighter the environment a REFLECTION sees is than the diffuse ambient fill.
        //
        // This is a correction for missing information, not a fudge factor. AmbientTerm answers "how
        // much sky and ground bounce reaches this fragment" -- two colours and a hemisphere mix, plus
        // a baked irradiance volume where one exists. What a real reflective object shows is the
        // whole ROOM: the white floor under it, the lit wall beside it, the equipment across from it,
        // every one of which this engine lights to 0.6-0.9 and none of which any reflection here can
        // ever sample. Taking the sky-and-ground fill as the complete environment therefore
        // systematically under-reads it, and it under-reads it worst exactly where it matters most --
        // on a metal, which has no diffuse term at all and is nothing BUT this reflection.
        //
        // Measured on the material sweep (tools/Aigis.ModelViz --materials) with Triage's own noon
        // lighting: at a gain of 1 a horizontal steel plate of albedo 0.66 renders at 0.20 while the
        // same albedo as a dielectric renders at 0.63, i.e. bare steel came out three times darker
        // than the paint beside it and read as charcoal. 2.2 puts it a little under the dielectric,
        // which is where a real brushed plate sits, with its highlights still well clear of it.
        const float ENV_SPECULAR_GAIN = 2.2;

        // GGX/Trowbridge-Reitz normal distribution. `a` is roughness SQUARED, the usual
        // parameterisation -- it is what makes an even sweep of authored roughness look like an even
        // sweep of gloss.
        float FinishD(float ndoth, float a) {
            float a2 = a * a;
            float d = (ndoth * ndoth * (a2 - 1.0)) + 1.0;
            return a2 / max(PI * d * d, 1e-6);
        }

        // Schlick's Fresnel. The grazing-angle brightening this produces is, in a scene lit as flatly
        // as this one, the single strongest cue that a surface is a surface and not a painted shape.
        vec3 FinishF(vec3 f0, float vdoth) {
            float f = 1.0 - vdoth;
            float f2 = f * f;
            return f0 + ((vec3(1.0) - f0) * (f2 * f2 * f));
        }

        // Hammon's approximation to the Smith height-correlated visibility term, already including
        // the 1/(4 ndotl ndotv) denominator. One divide, no sqrt -- chosen over the exact form
        // because this runs per fragment on a pipeline that is already fragment-bound (see
        // PrimitiveDepthFrag's note on the prepass that exists for exactly that reason).
        float FinishV(float ndotl, float ndotv, float a) {
            return 0.5 / max(mix(2.0 * ndotl * ndotv, ndotl + ndotv, a), 1e-5);
        }

        // One light's specular contribution. `radiance` is that light's colour times whatever
        // attenuation, shadow and cone terms already apply to its diffuse term, so a specular
        // highlight is never brighter than the light that causes it and never survives into a shadow.
        vec3 FinishLobe(vec3 normal, vec3 viewDir, vec3 lightDir, vec3 radiance, vec3 f0, float a) {
            float ndotl = max(dot(normal, lightDir), 0.0);
            if (ndotl <= 0.0) {
                return vec3(0.0);
            }
            vec3 halfVec = normalize(lightDir + viewDir);
            float ndotv = max(dot(normal, viewDir), 1e-4);
            float ndoth = max(dot(normal, halfVec), 0.0);
            float vdoth = max(dot(viewDir, halfVec), 0.0);
            return radiance * ndotl * FinishD(ndoth, a) * FinishV(ndotl, ndotv, a) * FinishF(f0, vdoth);
        }

        // How many of the scene's point lights get a specular lobe. The diffuse loop runs to 256; a
        // second loop that long, with a normalize and a pow-equivalent inside it, is not affordable on
        // a fragment-bound pass. Eight is chosen because SceneLighting.SelectPointLights already hands
        // the renderer its lights NEAREST-FIRST, so the eight that reach this loop are the eight whose
        // highlights would actually be visible. A ninth lamp across the ward contributes a highlight
        // the size of a pixel.
        const int FINISH_SPECULAR_LIGHTS = 8;

        // Specular from the LOCAL lights (point, then spot), for a finish that is smooth enough to
        // show one. Skipped entirely above FINISH_LOCAL_SPECULAR_MAX_ROUGHNESS: a rough surface's
        // local highlight is a wide, dim smear that the diffuse term already accounts for, and the
        // whole point of a cap is that most surfaces in a ward are rough.
        //
        // The attenuation and cone maths are deliberately a copy of PointLightContribution's and
        // SpotLightContribution's rather than a refactor of them: those two are the hot loops for
        // every surface in the game, and threading an out-parameter through them to save a duplicated
        // falloff line would put this material's cost on every fragment that does not use it.
        const float FINISH_LOCAL_SPECULAR_MAX_ROUGHNESS = 0.62;

        vec3 FinishLocalSpecular(vec3 normal, vec3 viewDir, vec3 f0, float a) {
            vec3 total = vec3(0.0);
            int count = min(int(light.pointLightCount.x), FINISH_SPECULAR_LIGHTS);
            for (int i = 0; i < FINISH_SPECULAR_LIGHTS; i++) {
                if (i >= count) {
                    break;
                }
                vec3 toLight = light.pointLights[i].positionRange.xyz - vWorldPos;
                float dist = length(toLight);
                if (dist < 1e-4) {
                    continue;
                }
                vec3 lightDir = toLight / dist;
                float range = max(light.pointLights[i].positionRange.w, 0.0001);
                float falloff = 1.0 / (1.0 + (dist / range) * (dist / range));
                float fixtureShadow = i < int(light.shadowParams.w)
                    ? SampleFixtureShadow(i, vFixtureShadowClipPos[i].xyz / vFixtureShadowClipPos[i].w)
                    : 1.0;
                vec3 radiance = light.pointLights[i].colorIntensity.rgb
                    * light.pointLights[i].colorIntensity.a * falloff * fixtureShadow;
                total += FinishLobe(normal, viewDir, lightDir, radiance, f0, a);
            }

            int spots = int(light.spotLightCount.x);
            for (int i = 0; i < 4; i++) {
                if (i >= spots) {
                    break;
                }
                vec3 toLight = light.spotLights[i].positionRange.xyz - vWorldPos;
                float dist = length(toLight);
                if (dist < 1e-4) {
                    continue;
                }
                vec3 lightDir = toLight / dist;
                // Same opposite-sign convention SpotLightContribution documents at length: the cone
                // axis points the way the rays travel, the diffuse term wants surface-toward-light.
                float spotCos = dot(-lightDir, normalize(light.spotLights[i].directionCosOuter.xyz));
                float cone = smoothstep(
                    light.spotLights[i].directionCosOuter.w, light.spotLights[i].coneCosInner.x, spotCos);
                if (cone <= 0.0) {
                    continue;
                }
                float range = max(light.spotLights[i].positionRange.w, 0.0001);
                float falloff = 1.0 / (1.0 + (dist / range) * (dist / range));
                vec3 radiance = light.spotLights[i].colorIntensity.rgb
                    * light.spotLights[i].colorIntensity.a * falloff * cone;
                total += FinishLobe(normal, viewDir, lightDir, radiance, f0, a);
            }

            return total;
        }

        // ---- SurfaceDetail: a procedural bump/albedo field -------------------------------------
        //
        // See SurfaceDetail's own doc comment for why this is computed rather than sampled from a
        // normal map. Everything below is a function of ONE position -- vWorldPos for every class but
        // SlabSeamsFollowInstance (see the call site) -- so it tiles seamlessly across the
        // separately-drawn cells of one floor and needs no UVs, tangents, textures or samplers.

        // One integer-bit hash, deliberately NOT the usual fract(sin(dot(p, k)) * big) trick: sin()
        // is not bit-identical across drivers, and a golden PNG that depends on it stops being a
        // golden. Integer multiply/xor is exact everywhere.
        float DetailHash(vec2 lattice) {
            uvec2 q = uvec2(ivec2(lattice)) * uvec2(1597334673u, 3812015801u);
            uint n = (q.x ^ q.y) * 1597334673u;
            return float(n) * (1.0 / 4294967296.0);
        }

        // The pixel classes' own hash, and NOT DetailHash above. That one is a single integer
        // multiply and one xor, which is plenty when its output is about to be smoothed into value
        // noise -- every caller above it is -- and visibly not enough when the raw per-cell value IS
        // the picture: measured on the first render of a clothed figure, adjacent cells came out
        // correlated and the uniform read as a chessboard rather than as cloth. This adds a proper
        // avalanche finaliser (the murmur3 mix), which decorrelates neighbours for three more
        // instructions. `salt` is what gives one cell several independent draws.
        //
        // The +8192 bias keeps the lattice non-negative before the unsigned cast: the cast itself is
        // well defined on negatives, but the two coordinate multiplies then differ in how their high
        // bits land either side of the origin, which put a visible seam down the middle of a part.
        float PixelHash(vec2 lattice, uint salt) {
            uvec2 q = uvec2(ivec2(lattice) + 8192);
            uint n = (q.x * 73856093u) ^ (q.y * 19349663u) ^ salt;
            n ^= n >> 15; n *= 2246822519u;
            n ^= n >> 13; n *= 3266489917u;
            n ^= n >> 16;
            return float(n) * (1.0 / 4294967296.0);
        }

        // 2D value noise WITH its analytic gradient: .x = value in [0,1], .yz = d/du, d/dv (per unit
        // of the noise's OWN uv, so the caller applies the chain rule for its frequency). The
        // gradient is the part a bump map actually needs; the value is only used for the albedo
        // mottle. Four hashes, no texture, no screen-space derivative instructions.
        vec3 DetailNoise(vec2 p) {
            vec2 i = floor(p);
            vec2 f = p - i;
            vec2 u = f * f * (3.0 - 2.0 * f);
            vec2 du = 6.0 * f * (1.0 - f);
            float a = DetailHash(i);
            float b = DetailHash(i + vec2(1.0, 0.0));
            float c = DetailHash(i + vec2(0.0, 1.0));
            float d = DetailHash(i + vec2(1.0, 1.0));
            float k1 = b - a;
            float k2 = c - a;
            float k3 = a - b - c + d;
            return vec3(
                a + k1 * u.x + k2 * u.y + k3 * u.x * u.y,
                du.x * (k1 + k3 * u.y),
                du.y * (k2 + k3 * u.x));
        }

        // SurfaceDetail.SlabSeams' groove spacing, in METRES of world space. Must stay equal to
        // ShaderSource.SlabSeamPeriodMeters on the C# side -- SurfaceDetailTests pins the pair, and
        // that constant's own doc comment carries the reasoning. An exact 1/3 of the levels' 1 m tile
        // grid (2026-08-20 request: make the sidewalk texture about a third the size), so every third
        // joint still lands on a real cell boundary rather than the lattice drifting across it.
        //
        // ASCII ONLY BELOW THIS LINE, as everywhere in this file's GLSL: shaderc/glslang lexes a
        // non-ASCII byte in a comment as a syntax error at end of file, which is a spectacularly
        // unhelpful diagnostic to debug. Japanese belongs in the C# doc comments, not in here.
        const float SLAB_SEAM_PERIOD_METERS = 0.3333333;

        // The 2026-09-19 fine classes' distance fade, in metres from the camera. Full strength inside
        // NEAR, gone by FAR. NEAR is set past the reach of any close camera Triage actually has (the
        // fall camera and the elevator cabin both sit under 4 m from their subject) and FAR well
        // short of the stage-fit camera's ~40 m, so neither end is ever caught mid-fade in a shipped
        // view. See SurfaceDetailShading's own note for why fading is the only way to serve both
        // halves of the brief.
        const float FINE_DETAIL_NEAR = 5.0;
        const float FINE_DETAIL_FAR = 16.0;

        // Weave and dimple pitches, in cycles per metre. 40 is a 2.5 cm weave and 26 a 3.8 cm dimple
        // -- both several times coarser than the real thing, which is deliberate: below about a
        // centimetre the pattern is sub-pixel even in a close-up (see SurfaceDetail's note on coarse
        // frequencies), and what these are buying is the READING "cloth" / "moulded rubber", which
        // survives being coarser than life.
        // SurfaceDetail.PixelCloth's texel pitch, in METRES. One pitch for every axis, which is what
        // makes a texel square: a fixed texel COUNT across a part draws rectangles on any part that
        // is not square, and no part of a figure is.
        //
        // 4 cm is the Minecraft coarseness the brief asked for by name -- that idiom draws a 1.8 m
        // figure about 32 texels tall and this is 44, close enough that the reading is the same and
        // fine enough that a coat panel carries a dozen texels across rather than five.
        const float PIXEL_TEXEL_METERS = 0.040;

        // The pixel classes' own distance fade, wider at both ends than FINE_DETAIL_*. These are an
        // order of magnitude coarser than a weave, so they survive much further out before aliasing
        // -- and they have to, because unlike a trolley's brushed steel these are on the figures the
        // player is looking at from the gameplay camera, and a texture that vanishes at play distance
        // is not the one that was asked for. It still fades rather than not: at Triage's stage-fit
        // ~26 px/m a 5.5 cm texel is under two pixels, which crawls.
        // MEASURED against the stage-fit camera rather than picked: that camera's distance is solved
        // from the stage's own span (StageFitCamera.ComputeDistance), and Triage's rooms put it in
        // the high twenties to low thirties of metres. A fade that was done by 30 m therefore
        // deleted the whole effect from the one view the game is actually played in, which is not
        // what was asked for. NEAR is past that camera, so a figure in play carries the texture at
        // full amplitude, and FAR only catches the far corner of a large stage -- where a 5.5 cm
        // texel really is under a pixel and would crawl.
        const float PIXEL_DETAIL_NEAR = 34.0;
        const float PIXEL_DETAIL_FAR = 52.0;

        const float FABRIC_CYCLES_PER_METRE = 26.0;
        const float PEBBLE_CYCLES_PER_METRE = 26.0;

        // A groove lattice for SurfaceDetail.SlabSeams. Returns (mask, d/du, d/dv) of a mask that is
        // ~1 along each lattice line and ~0 inside a slab, so both the height field and the albedo
        // can read from one evaluation.
        //
        // `sharpness` is in PERIOD-squared units (d below is a fraction of one period), so the groove
        // stays the same fraction of a slab whatever the period is -- which is what makes shrinking
        // the period shrink the whole pattern rather than turning the paving into a dark grid. 55
        // gives a joint about 15% of a slab wide.
        vec3 DetailSeams(vec2 p, float period) {
            const float sharpness = 55.0;
            vec2 q = p / period;
            vec2 g = fract(q) - 0.5;          // 0 at the slab centre, +-0.5 at the seams
            vec2 d = 0.5 - abs(g);            // 0 AT the seam, 0.5 at the slab centre
            vec2 w = exp(-sharpness * d * d);
            // Chain rule: dw/dp = w * (-2*sharpness*d) * dd/dp, and dd/dp = -sign(g)/period.
            vec2 dw = w * (-2.0 * sharpness * d) * (-sign(g) / period);
            return vec3(w.x + w.y, dw.x, dw.y);
        }

        // Returns the perturbed normal, and an albedo MULTIPLIER (rgb, so a class may shift hue as
        // well as brightness) in `albedoTint`. Early-outs on SurfaceDetail.None so every instance
        // authored before this existed is byte-identical.
        //
        // Not every class perturbs the normal. SurfaceDetail.Patches deliberately does not -- see its
        // own branch below, and SurfaceDetail's doc comment on why "bump map" is the wrong tool for
        // this game's flat-shaded look.
        vec3 SurfaceDetailShading(vec3 normal, vec3 samplePos, float detail, out vec3 albedoTint) {
            albedoTint = vec3(1.0);
            if (detail < 0.5) {
                return normal;
            }

            // A tangent frame for the flat normal. Any consistent frame does for the isotropic noise;
            // the seam grid needs world X/Z specifically, which is what the |n.y| branch selects for
            // the up-facing surfaces (floors, paving) that are the only ones asking for seams.
            vec3 tangent = abs(normal.y) > 0.9
                ? vec3(1.0, 0.0, 0.0)
                : normalize(cross(vec3(0.0, 1.0, 0.0), normal));
            vec3 bitangent = cross(normal, tangent);
            vec2 uv = vec2(dot(samplePos, tangent), dot(samplePos, bitangent));

            // ---- SurfaceDetail.Patches: FLAT colour patches, no bump at all -----------------------
            //
            // 2026-08-20, user report: the grass previously used the two-octave bump below and read as
            // "a bit unnatural" -- correctly so. A smooth, continuously-shaded noise field is a
            // photoreal technique, and this game is flat-coloured boxes with hard edges and a bright
            // palette; a soft grey-green cloud sitting in the middle of that reads as a smear rather
            // than as ground.
            //
            // The replacement is the same noise QUANTISED to a handful of levels: the noise supplies
            // irregular, organic patch SHAPES, the quantise gives each patch a hard edge and a single
            // flat tone. That is the casual/stylised idiom (a lawn as a mosaic of two or three greens)
            // and it is strictly simpler than what it replaces -- no gradient, no tangent-space slope,
            // no normal perturbation, so the ground stays exactly as flat-shaded as every box on it.
            if (detail > 1.5 && detail < 2.5) {
                const float patchLevels = 4.0;
                // Two scales summed BEFORE quantising, so the patch outlines are not all one size and
                // the result cannot read as a single regular lattice.
                float field = DetailNoise(uv * 0.55).x * 0.68
                    + DetailNoise(uv * 1.5 + vec2(37.2, 11.7)).x * 0.32;
                // Stretched about the midpoint before quantising. Summing two octaves concentrates the
                // result near 0.5 (it is a sum of random variables), so the raw field only ever
                // reached the middle two of four levels -- measured on the first render, where the
                // lawn came out almost uniform. 1.9 is what pushes the tails into the outer levels.
                field = clamp((field - 0.5) * 1.9 + 0.5, 0.0, 0.999);
                float level = floor(field * patchLevels) / (patchLevels - 1.0);
                // Warm yellow-green through cool blue-green, and darker to lighter, together: a real
                // lawn varies in hue as much as in brightness, and two axes make four steps read as
                // distinct patches rather than as banding.
                albedoTint = mix(vec3(1.02, 0.88, 0.72), vec3(0.84, 1.13, 0.97), level);
                return normal;
            }

            // ---- The 2026-09-19 FINE classes: BrushedMetal, WovenFabric, WoodGrain, Pebbled -------
            //
            // These describe what a surface looks like from a METRE away, which the four classes
            // above deliberately do not: those were authored for floors and walls seen from the
            // gameplay camera, where this engine's own measurement says a 5 cm feature is sub-pixel.
            // A weave at a real thread pitch is far past that.
            //
            // So they FADE OUT WITH DISTANCE, and that is not a performance trick -- it is the only
            // way to have both things the brief asks for. Sampled at a few tenths of a pixel per
            // cycle, a fine field does not read as texture; it reads as noise that crawls when the
            // camera moves, because this engine has no mip chain, no anisotropic filtering and no
            // temporal accumulation to tame it with. Fading the AMPLITUDE (not the frequency) to zero
            // between FINE_DETAIL_NEAR and FINE_DETAIL_FAR leaves the surface exactly as clean as a
            // flat colour at stage-fit range, and fully textured in the close cameras that exist
            // (the fall camera, the elevator cabin, the title diorama, the portraits) and in
            // tools/Aigis.ModelViz.
            //
            // Note the MATERIAL half of the same brief -- SurfaceFinish's roughness/metallic/Fresnel
            // -- deliberately does NOT fade: a steel trolley has to read as steel from across the
            // ward, and a highlight is a low-frequency signal that survives any sampling rate.
            // CLOSED at the top for the same reason the `detailPos` select is: Regolith was
            // appended after these four (code 9) and is a BUMP class, so an open `> 4.5` would
            // have quietly rendered a dust plain as pebbled rubber.
            if (detail > 4.5 && detail < 8.5) {
                float camDistance = length(light.cameraPositionSpecularIntensity.xyz - vWorldPos);
                float fade = 1.0 - smoothstep(FINE_DETAIL_NEAR, FINE_DETAIL_FAR, camDistance);
                if (fade <= 0.001) {
                    return normal;
                }

                vec2 slope = vec2(0.0);
                float albedoScale = 1.0;

                if (detail < 5.5) {
                    // BrushedMetal. Streaks ALONG the tangent axis: the noise is sampled with its v
                    // coordinate compressed hard, so its features are long in u and short in v, and
                    // the normal is therefore perturbed almost entirely ACROSS the grain. That is
                    // what splits one highlight into many parallel ones -- see the enum's own note on
                    // why this is not, and does not pretend to be, anisotropic shading.
                    vec3 streak = DetailNoise(vec2(uv.x * 3.0, uv.y * 62.0));
                    slope = vec2(streak.y * 3.0, streak.z * 62.0) * 0.0016;
                    albedoScale = 1.0 + 0.055 * (streak.x - 0.5);
                } else if (detail < 6.5) {
                    // WovenFabric. Two perpendicular ridge families -- a cross-hatch rather than a
                    // noise field, because a weave is regular and reading it as regular is the whole
                    // cue. The phase offset between the two axes is what stops the ridges meeting in
                    // a grid of dots.
                    vec2 w = uv * (2.0 * PI * FABRIC_CYCLES_PER_METRE);
                    // Each direction's ridge is MODULATED BY THE OTHER, which is what makes a weave a
                    // weave: where the warp passes over it stands proud and the weft beneath it is
                    // flattened, and half a period later they swap. Two independent sine ridges (the
                    // first version) produce corduroy in whichever direction the camera does not
                    // foreshorten, because nothing about them alternates.
                    vec2 over = 0.5 + 0.5 * vec2(sin(w.y), -sin(w.x));
                    slope = vec2(cos(w.x) * over.x, cos(w.y) * over.y)
                        * (0.013 * FABRIC_CYCLES_PER_METRE);
                    // A slow mottle on top: real cloth is not uniform, and without this the weave
                    // reads as an embossed plastic sheet.
                    vec3 slack = DetailNoise(uv * 3.4);
                    slope += slack.yz * (3.4 * 0.004);
                    albedoScale = 1.0 + 0.07 * (slack.x - 0.5);
                } else if (detail < 7.5) {
                    // WoodGrain. Stretched noise along the first tangent axis plus a few darker
                    // lines through it. Most of the effect is in the ALBEDO rather than the normal --
                    // a planed board is smooth to the touch and striped to the eye, and treating
                    // grain as relief is what makes cartoon wood look like corrugated card.
                    // The growth-ring field: a coordinate that advances fast ACROSS the board and
                    // barely at all along it, wobbled by a slow noise so the rings are not straight.
                    // Taking fract() of that and making it a sawtooth is what produces LINES;
                    // sampling noise directly (the first attempt) produced soft blobs that read as
                    // marble, which is the classic way procedural wood goes wrong.
                    vec3 wander = DetailNoise(vec2(uv.x * 1.1, uv.y * 3.0));
                    // A SECOND, slower field warps the ring COUNT as well as their position, so the
                    // spacing opens and closes along the board. Without it the lines are perfectly
                    // parallel and evenly spaced, which reads as a corrugated panel rather than as
                    // timber -- measured on the detail chart, where one wander term alone produced a
                    // venetian blind.
                    vec3 drift = DetailNoise(vec2(uv.x * 0.45 + 5.1, uv.y * 0.8 + 2.7));
                    float rings = (uv.y * (14.0 + (drift.x * 11.0))) + (wander.x * 1.6);
                    float ring = abs(fract(rings) - 0.5) * 2.0;
                    // Late wood is a NARROW dark band and early wood a wide pale one -- an asymmetry
                    // that is most of what makes timber look like timber rather than like stripes.
                    // 0.30 rather than the 0.55 this first shipped at: at 0.55 the dark half of every
                    // ring was as wide as the light half, which reads as a dune field.
                    float lateWood = smoothstep(0.30, 0.0, ring);
                    vec3 fibre = DetailNoise(vec2(uv.x * 6.0, uv.y * 60.0));
                    albedoScale = 1.04 - (0.40 * lateWood) - (0.09 * (1.0 - fibre.x));
                    // Relief is deliberately tiny: a planed board is smooth to the touch and striped
                    // to the eye, and treating grain as real relief is what makes cartoon wood look
                    // like corrugated card.
                    slope = vec2(wander.y * 1.3, (wander.z * 4.0) + (lateWood * 6.0)) * 0.0011;
                } else {
                    // Pebbled. Round studs on a lattice rather than a product of two sine waves: a
                    // sine grid is a quilt of saddles, which shades as a woven mesh (measured -- the
                    // first version of this was indistinguishable from WovenFabric on the detail
                    // chart). Distance from the nearest lattice point gives a genuinely ROUND bump,
                    // and its gradient points radially out of that point, which is what makes each
                    // stud catch light on one side and shade on the other.
                    vec2 cellUv = uv * PEBBLE_CYCLES_PER_METRE;
                    vec2 cell = floor(cellUv);
                    vec2 toCentre = (cellUv - cell) - 0.5;
                    float r = length(toCentre) * 2.0;
                    // A dome that falls to zero at the cell edge, so neighbouring studs do not merge
                    // into a continuous rippled sheet.
                    float dome = 1.0 - smoothstep(0.55, 1.0, r);
                    vec2 radial = r > 1e-4 ? toCentre / max(length(toCentre), 1e-4) : vec2(0.0);
                    float jitter = DetailHash(cell);
                    slope = radial * (dome * (0.55 + 0.35 * jitter) * 0.62);
                    // The gaps between studs sit in their own shadow, which no normal perturbation
                    // can express on a flat surface -- so it goes in the albedo instead.
                    albedoScale = 0.82 + (0.26 * dome);
                }

                slope *= fade;
                albedoTint = vec3(mix(1.0, albedoScale, fade));
                float fineLength = length(slope);
                if (fineLength > 1.19) {
                    slope *= 1.19 / fineLength;
                }
                return normalize(normal - tangent * slope.x - bitangent * slope.y);
            }

            // ---- The 2026-09-20 PIXEL class: PixelCloth ------------------------------------------
            //
            // Visible square texels on a garment -- the Minecraft idiom, asked for by name. Three
            // things separate it from every class above:
            //
            //  1. HARD EDGES, NO BUMP. The value is constant across a texel and steps at its border.
            //     A normal perturbation would shade a gradient inside the cell and round off the one
            //     feature the class exists to show, exactly as SurfaceDetail.Patches' own note says.
            //  2. A TRIPLANAR LATTICE ON THE DOMINANT LOCAL AXIS, not the normal-derived tangent
            //     frame `uv` above. That frame rotates continuously around anything curved and
            //     shears a square cell into a diamond; projecting onto the two axes the fragment's
            //     own box face spans keeps every cell square. The face is picked off the NORMALISED
            //     offset (samplePos / vLocalHalfSize), whose largest component is the box face on a
            //     box and the normal's dominant axis on a sphere. The lattice itself is METRIC, so a
            //     texel is square whatever the part's proportions are.
            //  3. SAMPLED IN THE PART'S OWN FRAME (see the `detailPos` select at the call site).
            //     Every limb of a walking figure moves; a world-anchored lattice would swim through
            //     it the way the gondola's floor did.
            //
            // There were three of these classes for most of 2026-09-20 -- cloth, bare skin, and a
            // face painted onto the head. Both of the others are gone, on the user's instruction and
            // in that order: skin texels read as a rash, and a face painted in texels reads as
            // something DRAWN ON however it is tuned (it was tried as a full relief, then as a nose
            // alone, and modelling the face as geometry instead was rejected too). What survives is
            // the case the idiom was actually good for, a woven garment.
            //
            // CLOSED at the top, like both ranges above it, so the next class appended does not
            // silently inherit a pixel lattice.
            if (detail > 9.5 && detail < 10.5) {
                float pixelCamDistance = length(light.cameraPositionSpecularIntensity.xyz - vWorldPos);
                float pixelFade = 1.0 - smoothstep(PIXEL_DETAIL_NEAR, PIXEL_DETAIL_FAR, pixelCamDistance);
                if (pixelFade <= 0.001) {
                    return normal;
                }

                // Which of the part's own faces this fragment is on, and the two axes that face
                // spans. `halfSize` is only used to normalise, never to scale the grid.
                vec3 halfSize = max(vLocalHalfSize, vec3(1e-4));
                vec3 axis = abs(samplePos / halfSize);
                vec3 latticeSource = samplePos / PIXEL_TEXEL_METERS;
                vec2 lattice;
                if (axis.x >= axis.y && axis.x >= axis.z) {
                    lattice = vec2(latticeSource.z, latticeSource.y);
                } else if (axis.y >= axis.z) {
                    lattice = vec2(latticeSource.x, latticeSource.z);
                } else {
                    lattice = vec2(latticeSource.x, latticeSource.y);
                }

                vec2 cell = floor(lattice);
                // Quantised to three levels rather than used raw: a continuous per-texel random
                // reads as television snow, and the idiom being copied is a handful of tones from a
                // small palette. The second hash is an independent draw for the sparse darker texels
                // that keep the result from looking like a regular two-tone dither.
                float pixelLevel = floor(PixelHash(cell, 0u) * 3.0) * 0.5;
                float pixelSpeck = PixelHash(cell, 0x9E3779B9u);

                // Deliberately quiet, and much quieter than it shipped at this morning: at +-12% a
                // plain garment read as a chequerboard of two tones rather than as cloth (user,
                // 2026-09-20, who asked for the coat to be smoother than that). It still shifts hue
                // as well as value, for the reason Patches gives: two axes make three tones read as
                // weave rather than as banding.
                vec3 pixelTint = mix(vec3(0.945, 0.950, 0.968), vec3(1.042, 1.036, 1.016), pixelLevel);
                // About one texel in twelve is a darker fleck -- a fold, a seam, a worn patch.
                pixelTint *= pixelSpeck < 0.08 ? 0.945 : 1.0;

                albedoTint = mix(vec3(1.0), pixelTint, pixelFade);
                return normal;
            }

            // ---- Bump classes ---------------------------------------------------------------------
            // Per-class frequencies (cycles/metre) and amplitudes (metres of height). Coarse first
            // because it is the octave that survives a distant camera. FineGrain's smallest period
            // is 0.154 m, about four pixels at the stage camera; anything finer would alias.
            float coarseFreq, coarseAmp, fineFreq, fineAmp, seamDepthPerPeriod, seamShade, mottle;
            if (detail < 1.5) {            // Grain: asphalt/concrete
                coarseFreq = 1.6; coarseAmp = 0.055;
                fineFreq = 4.5;   fineAmp = 0.022;
                seamDepthPerPeriod = 0.0; seamShade = 0.0;  mottle = 0.10;
            } else if (detail > 10.5 && detail < 11.5) { // FineGrain: ward floor
                coarseFreq = 3.5; coarseAmp = 0.014;
                fineFreq = 6.5; fineAmp = 0.008;
                seamDepthPerPeriod = 0.0; seamShade = 0.0; mottle = 0.10;
            } else if (detail > 11.5 && detail < 12.5) { // Carpet: soft pile, strong fibre mottle
                coarseFreq = 2.4; coarseAmp = 0.004;
                fineFreq = 7.5; fineAmp = 0.003;
                seamDepthPerPeriod = 0.0; seamShade = 0.0; mottle = 0.30;
            } else if (detail > 8.5) {     // Regolith: an airless dust plain
                // The coarse octave is the whole difference from Grain, and it is nearly four times
                // COARSER: 0.45 cycles/m is a hollow about 2.2 m across, which at Triage's ~26 px/m
                // stage camera is ~58 px and therefore a feature a player can actually see. Grain's
                // own 1.6 cycles/m is 16 px and reads as a finish on a made surface -- correct for
                // concrete, and exactly what an unworked plain must not look like.
                //
                // The amplitude is the largest of any class here (0.12 m against Grain's 0.055) for
                // the same reason: a hollow that wide needs real depth or its shading is flat. Its
                // SLOPE is still the gentlest of the three (amp x freq = 0.054 against Grain's 0.088),
                // because slope is what the bump actually shades from -- so this is a broader, softer
                // relief rather than a rougher one, which is what dust is.
                coarseFreq = 0.45; coarseAmp = 0.12;
                fineFreq = 3.2;   fineAmp = 0.018;
                // Twice Grain's mottle, and monochrome (albedoTint below is vec3(albedoScale), so
                // every class here already is): regolith varies in brightness far more than a paved
                // surface, and in hue essentially not at all. See SurfaceDetail.Regolith.
                seamDepthPerPeriod = 0.0; seamShade = 0.0;  mottle = 0.20;
            //
            // A NEW BUMP ORDINAL GOES ABOVE THIS LINE, with its own closed test. The `else` below is the
            // catch-all and it is SlabSeams, so a class appended without a branch renders as paving --
            // silently, and with no clue in the picture that it was ever asked for. That is the same
            // trap the `detailPos` select two hundred lines down had to close a RANGE to avoid; here
            // the ordering already reads correctly and only the next author can keep it that way.
            } else {                       // SlabSeams: paving/tiling
                coarseFreq = 1.3; coarseAmp = 0.035;
                fineFreq = 4.0;   fineAmp = 0.016;
                // Depth as a FRACTION OF THE PERIOD, not in metres: a narrower joint has to be
                // shallower by the same factor or its walls get steeper as the paving shrinks, and the
                // bevel stops looking like the same joint. This is what makes the period the single
                // knob that scales the whole pattern.
                seamDepthPerPeriod = 0.10; seamShade = 0.34; mottle = 0.07;
            }

            vec3 coarse = DetailNoise(uv * coarseFreq);
            // Offset so the two octaves cannot share a lattice and produce a visible grid.
            vec3 fine = DetailNoise(uv * fineFreq + vec2(37.2, 11.7));

            // The height field's gradient in metres-per-metre. Each octave's own frequency comes out
            // of the chain rule.
            vec2 slope = (coarseAmp * coarseFreq) * coarse.yz + (fineAmp * fineFreq) * fine.yz;
            float albedoScale = 1.0 + mottle * ((coarse.x - 0.5) * 1.4 + (fine.x - 0.5) * 0.6);

            if (seamDepthPerPeriod > 0.0) {
                vec3 seams = DetailSeams(uv, SLAB_SEAM_PERIOD_METERS);
                // Grooves cut IN, so the height contribution is negative.
                slope -= (seamDepthPerPeriod * SLAB_SEAM_PERIOD_METERS) * seams.yz;
                albedoScale *= 1.0 - seamShade * min(seams.x, 1.0);
            }

            albedoTint = vec3(albedoScale);

            // A bound on the tilt, so no future amplitude/sharpness tuning can rotate a normal past
            // the surface's own horizon. Past it, HemisphereAmbient starts mixing in the GROUND
            // bounce colour and a groove picks up a warm cast that reads as rust; a normal pointing
            // away from the geometry would also take local lights it physically cannot see.
            // tan(50 deg). Measured inert at the values shipped above -- the steepest class
            // (SlabSeams, at a groove wall) peaks around |slope| 0.7, and it stays there across a
            // period change because the joint's depth scales with its width.
            float slopeLimit = 1.19;
            float slopeLength = length(slope);
            if (slopeLength > slopeLimit) {
                slope *= slopeLimit / slopeLength;
            }

            // Bump mapping: tilt the normal against the slope of the height field. Normalizing keeps
            // the diffuse term well-behaved however steep the field gets.
            return normalize(normal - tangent * slope.x - bitangent * slope.y);
        }

        // ---- Coverage dithering ----------------------------------------------------------------
        //
        // vColor.a is COVERAGE, realized as an ordered 4x4 Bayer dither rather than as alpha
        // blending. Why dither, on a pipeline whose whole point is being opaque:
        //   * no second pipeline, no blend state, and no change to the packer's (Kind, Material)
        //     batch order -- a blended material would need all three, because blending is only
        //     correct back-to-front and this pipeline's submission order is chosen for batching;
        //   * depth stays exact, so a see-through wall still occludes correctly and still reads as a
        //     surface at a definite distance rather than as a ghost;
        //   * ORDERED, not random: the threshold depends only on screen position, so a static
        //     surface's pattern is rock steady instead of crawling frame to frame.
        // A coverage of exactly 1 can never discard (the largest threshold is 15.5/16) and exactly 0
        // always does, so the default is byte-identical to the pre-2026-08-20 hardcoded opaque path.
        {{CoverageDitherGlsl}}

        {{RenderStyleGlsl}}

        void main() {
            // MaterialKind.AdditiveGlow (code 3) is the ONE material whose alpha is not coverage: it
            // is drawn by a BLENDING pipeline, so vColor.a is the weight its colour is added at and
            // dithering it here as well would spend the same number twice -- a half-alpha glow would
            // come out stippled AND at a quarter strength. Every other material is unchanged: for
            // codes 0..2 `glow` is 0 and the branch below is the same discard it always was.
            float glow = vMaterial > 2.5 ? 1.0 : 0.0;
            if (glow < 0.5 && vColor.a < CoverageThreshold(gl_FragCoord.xy)) {
                discard;
            }

            // MaterialKind.BladeGlint (code 5): a mirror lobe and nothing else. Placed ahead of
            // every other branch because it shares none of them -- no diffuse, no ambient, no
            // shadow, no tone curve, and no dither (its alpha is a blend weight; the `glow` gate
            // above already spared it, since that gate is `> 2.5` and this code is 5).
            //
            // Blinn-Phong rather than the GGX below it, on purpose. What this draws is a specular
            // HIGHLIGHT with no surface under it, so there is no Fresnel to weigh, no albedo to tint
            // it and no energy to conserve against -- and the one property that matters, how tight
            // the flash is, is one exponent instead of four terms. See MaterialKind.BladeGlint.
            if (vMaterial > 4.5) {
                vec3 glintNormal = normalize(vNormal);
                vec3 viewDir = normalize(light.cameraPositionSpecularIntensity.xyz - vWorldPos);
                // TWO-SIDED. A blade's flat is a plane a few millimetres thick and the highlight
                // belongs to whichever face is being looked at; without this, half of every heading
                // reflects off a surface pointing away from the camera and the flash never comes.
                if (dot(glintNormal, viewDir) < 0.0) {
                    glintNormal = -glintNormal;
                }

                vec3 sunDir = normalize(light.sunDirectionIntensity.xyz);
                vec3 halfVec = normalize(sunDir + viewDir);
                // Roughness carries the lobe WIDTH, mapped so that 0 is a mirror and 1 is a lobe
                // about as broad as the ordinary specular path's. The exponent is the usual
                // 2/a^2 - 2 for a Beckmann-equivalent Blinn lobe, clamped so a caller that forgets
                // its finish (roughness < 0, the "no finish" sentinel) still gets a sane flash
                // instead of an infinity.
                float glintRoughness = clamp(vFinish.x, 0.02, 1.0);
                float glintA = glintRoughness * glintRoughness;
                float glintPower = clamp(2.0 / (glintA * glintA) - 2.0, 4.0, 100000.0);
                float lobe = pow(max(dot(glintNormal, halfVec), 0.0), glintPower);
                // The sun's own intensity scales it: a blade in a dim ward catches less light than
                // one in the street, which is the entire difference between this and an emissive
                // band that shone the same everywhere.
                float sunStrength = max(light.sunDirectionIntensity.w, 0.0);
                outColor = vec4(ApplyRenderStyle(vColor.rgb * light.sunColor.rgb * (lobe * sunStrength)),
                                vColor.a * lobe);
                return;
            }

            // MaterialKind.OutlineShell: the second half of the inverted hull the vertex stage
            // expanded. Everything facing the camera is thrown away (vShellFacesAway carries the
            // sign of dot(normal, viewDir) -- see how the vertex stage derives it without a camera
            // position), so all that survives is the far side of the shell, visible only in the band
            // where it pokes out past the real object's silhouette: inside that silhouette the
            // object's own front faces are nearer and win the depth test.
            //
            // Painted flat, with no lighting and no tone curve, for the reason
            // MaterialKind.OutlineShell's doc comment gives: a UI affordance must not change colour
            // with the room it is standing in.
            // A RANGE, not a threshold, since AdditiveGlow's code sits above the shells'. For codes
            // 0..2 this is exactly the old `> 1.5`, so no existing pixel moves.
            if (vMaterial > 1.5 && vMaterial < 2.5) {
                if (vShellFacesAway <= 0.0) {
                    discard;
                }
                outColor = vec4(ApplyRenderStyle(vColor.rgb), 1.0);
                return;
            }

            // WHERE the procedural detail field is sampled. Every class but one reads the fragment's
            // WORLD position, which is what makes a floor's texture continuous across the separately
            // drawn cells it is built from -- and is exactly wrong for an instance that MOVES, since a
            // world-anchored field then slides across the surface carrying it. Code 4
            // (SurfaceDetail.SlabSeamsFollowInstance) samples the offset in the instance's OWN frame
            // instead, so the pattern travels with it.
            //
            // 2026-09-06: vLocalOffset, not `vWorldPos - vWorldCenter`. That difference is the offset
            // AFTER the instance's rotation, so it cancelled translation exactly and rotation not at
            // all -- which left the one surface this class exists for, Triage's gondola floor, sliding
            // its pattern as the car SWINGS (that car is a pendulum; the whole point of it is that it
            // rotates). The vertex shader already computes the pre-rotation offset for its own
            // world-position term, so nailing the field to the object in both is one varying.
            //
            // A RANGE at BOTH ends, and both ends have a reason -- and since 2026-09-20 a SECOND
            // range above Regolith.
            //
            // The four FINE classes (codes 5..8) are instance-anchored WITH SlabSeamsFollowInstance,
            // so the lower bound stays where it was and the upper one moved out to take them in.
            // They are authored for things that MOVE -- a carried bandage, a pushed trolley, a
            // walking character's sleeve -- and a world-anchored field on a moving surface is the
            // gondola bug this comment's own paragraph above records, at eighty pattern periods a
            // second instead of sixty a trip. The cost is that two same-sized parts carry identical
            // grain, which the vertex stage breaks up with a scale-derived phase (see it for why the
            // phase cannot come from the position).
            //
            // Regolith (code 9) is OUTSIDE it, and the upper bound is what keeps it out: it is a
            // ground surface built from hundreds of static cells plus a map apron, so it needs the
            // world anchoring that makes one continuous texture across separately-drawn geometry --
            // exactly what codes 0..3 need and for the same reason.
            //
            // PixelCloth (10) is instance-relative for the same reason as the fine classes and a
            // stronger one -- every limb of a walking figure moves, and a texel lattice sliding
            // through a character is that same gondola bug on something the player is looking
            // straight at. It is a SECOND range rather than an extension of the first precisely
            // because Regolith sits between the two and must stay world-anchored.
            //
            // FineGrain (11) and Carpet (12) are world-relative so their field continues across
            // floor-cell boundaries.
            vec3 detailPos = (vSurfaceDetail > 3.5 && vSurfaceDetail < 8.5)
                || (vSurfaceDetail > 9.5 && vSurfaceDetail < 10.5)
                ? vLocalOffset : vWorldPos;
            // Patches (2) and ScrollingGrain (15) only: the train map's scrolling ground -- see
            // SceneLighting.PatchesOffsetXz. ScrollingGrain is then shaded as plain Grain (1).
            bool scrollingGrain = vSurfaceDetail > 14.5 && vSurfaceDetail < 15.5;
            if ((vSurfaceDetail > 1.5 && vSurfaceDetail < 2.5) || scrollingGrain) {
                detailPos += vec3(light.spotLightCount.y, 0.0, light.spotLightCount.z);
            }
            float shadingDetail = scrollingGrain ? 1.0 : vSurfaceDetail;
            vec3 detailAlbedo;
            // FacadeWindows (13) is not a bump class: it must not reach SurfaceDetailShading, whose
            // catch-all would paint it as paving. It leaves the normal alone and paints windows.
            bool facadeWindows = vSurfaceDetail > 12.5 && vSurfaceDetail < 13.5;
            // Water (14), likewise not a bump class: its normal comes from the wave field.
            bool water = vSurfaceDetail > 13.5 && vSurfaceDetail < 14.5;
            float windowMask = 0.0;
            float windowLit = 0.0;
            detailAlbedo = vec3(1.0);
            vec3 normal = facadeWindows
                ? normalize(vNormal)
                : water
                    ? WaterNormal(vWorldPos, detailAlbedo)
                    : SurfaceDetailShading(normalize(vNormal), detailPos, shadingDetail, detailAlbedo);
            detailAlbedo *= SurfaceTextureAlbedo(vWorldPos, normalize(vNormal));
            if (facadeWindows) {
                float wallTint = 1.0;
                float curtain = 0.0;
                FacadeWindows(windowMask, windowLit, wallTint, curtain);
                detailAlbedo *= wallTint;
                vec3 glassColour = mix(WINDOW_GLASS, CURTAIN_GLASS, min(curtain, 1.0)) * (1.0 + 0.45 * max(curtain - 1.0, 0.0));
                detailAlbedo = mix(detailAlbedo, glassColour / max(vColor.rgb, vec3(0.05)), windowMask);
            }
            vec3 lightDir = normalize(light.sunDirectionIntensity.xyz);
            float sunNdotL = max(dot(normal, lightDir), 0.0);
            float diffuse = sunNdotL * light.sunDirectionIntensity.w;
            float shadow = 1.0;
            // sunNdotL > 0.0 is part of the gate for the same exactly-lossless reason
            // PointLightContribution skips a back-facing light: `shadow` is only ever multiplied
            // into `diffuse` (= sunNdotL * intensity) and into the specular term, which
            // SpecularContribution itself returns black for when sunNdotL <= 0. A surface facing
            // away from the sun therefore cannot observe its own sun shadow, and the nine PCF taps
            // that would compute it are pure waste.
            if (light.shadowParams.x > 0.5 && vShadowClipPos.w > 0.0 && sunNdotL > 0.0) {
                shadow = SampleShadowPcf(shadowMap, vShadowClipPos.xyz / vShadowClipPos.w);
            }
            // The DIFFUSE irradiance, shared by both shading paths. Split out of `lit` on 2026-09-19
            // so the microfacet path can multiply it by (1 - metallic) without recomputing four light
            // terms; for the legacy path the next line puts it straight back together, so the value
            // reaching `colorLinear` is bit-for-bit what it was.
            vec3 irradiance = AmbientTerm(normal)
                + light.sunColor.rgb * diffuse * shadow
                + PointLightContribution(normal)
                + SpotLightContribution(normal);
            vec3 lit = irradiance + SpecularContribution(normal, lightDir, sunNdotL) * shadow;

            // ---- SurfaceFinish: the microfacet path -------------------------------------------
            //
            // Entered only when this instance declares one. `finishSpecular` stays black otherwise
            // and `diffuseScale` stays 1, so an instance without a finish takes exactly the terms it
            // did before this existed.
            vec3 finishSpecular = vec3(0.0);
            vec3 diffuseScale = vec3(1.0);
            if (vFinish.x >= 0.0) {
                float roughness = clamp(vFinish.x, MIN_ROUGHNESS, 1.0);
                float metallic = clamp(vFinish.y, 0.0, 1.0);
                float a = roughness * roughness;
                // A metal has no diffuse lobe at all and reflects in its own albedo; a dielectric
                // reflects 4% neutrally and keeps all of its diffuse. This one mix is the whole
                // difference between brass and yellow plastic.
                vec3 f0 = mix(vec3(0.04), vColor.rgb, metallic);
                diffuseScale = vec3(1.0 - metallic);

                // Per fragment, not per object -- see this section's header for why vWorldCenter is
                // not good enough once one flat panel fills the screen.
                vec3 viewDir = normalize(light.cameraPositionSpecularIntensity.xyz - vWorldPos);

                finishSpecular = FinishLobe(
                    normal, viewDir, lightDir,
                    light.sunColor.rgb * light.sunDirectionIntensity.w * shadow, f0, a);

                if (roughness <= FINISH_LOCAL_SPECULAR_MAX_ROUGHNESS) {
                    finishSpecular += FinishLocalSpecular(normal, viewDir, f0, a);
                }

                // The ENVIRONMENT term, and the one that actually sells metal. There is no reflection
                // probe here, but there is already a directional ambient function -- the hemisphere
                // sky/ground mix, or the baked irradiance volume where one covers this fragment --
                // and sampling it along the MIRROR direction instead of the normal is a legitimate,
                // free approximation of a very rough environment map. A steel rail under a blue sky
                // and over a grey floor comes out blue on top and grey underneath, which is what a
                // steel rail does.
                //
                // Weighted by the standard split-sum environment-BRDF approximation: Fresnel toward
                // (1 - roughness) rather than toward white, so a rough metal keeps its own colour at
                // grazing angles instead of turning into a white rim.
                //
                // The sample DIRECTION carries the roughness, not the sample's weight. A mirror
                // samples along the reflection vector; a rough surface averages a wide cone around
                // it, and the limit of a very wide cone is the normal-facing average the diffuse
                // ambient already uses. Interpolating the direction is a one-mix stand-in for that
                // cone which costs nothing and behaves correctly at both ends.
                //
                // The first version of this instead multiplied the whole term by (1 - 0.85*roughness),
                // on the reasoning that a matte surface must not mirror the sky. Measured on the
                // material sweep, that was wrong in a way worth recording: it dimmed METAL, whose
                // entire appearance is this term (a metal has no diffuse lobe at all), so the bottom
                // row of the sweep came out near-black where bright steel belonged. Roughness makes a
                // reflection BLURRIER, not dimmer -- the energy has to go somewhere.
                vec3 reflectDir = mix(reflect(-viewDir, normal), normal, roughness * roughness);
                float ndotv = max(dot(normal, viewDir), 1e-4);
                float fresnelWeight = 1.0 - ndotv;
                fresnelWeight = fresnelWeight * fresnelWeight * fresnelWeight * fresnelWeight * fresnelWeight;
                vec3 envF = f0 + ((max(vec3(1.0 - roughness), f0) - f0) * fresnelWeight);
                finishSpecular += AmbientTerm(reflectDir) * envF * ENV_SPECULAR_GAIN;
            }
            // MaterialKind.Emissive: a self-lit surface drops every term above and drives its own
            // colour past 1.0 instead, so the tone curve below leaves it near-white at the centre
            // while the hue still survives. Mixed rather than branched -- `emissive` is exactly 0 or
            // 1 (OutlineShell, the only other code, has already returned above), so both materials
            // take the same instruction path and a shaded surface is bit-for-bit what it was before
            // this existed.
            // SurfaceDetail's albedo mottle rides on the SHADED term only. A bump map without one
            // is nearly invisible on a near-flat surface under a high sun (the normal barely moves
            // dot(N,L)); the albedo variation is what actually makes a slab read as ground rather
            // than as sheet plastic, and real material authoring pairs the two for the same reason.
            // detailAlbedo is exactly vec3(1.0) for SurfaceDetail.None, so this is a no-op there. It
            // is an rgb multiplier rather than a scalar so a class can shift HUE as well as value --
            // which is most of what makes the flat-patch grass read as a lawn instead of as banding.
            // Emissive is deliberately left alone: a lamp lens is not a weathered surface.
            //
            // `diffuseScale` and `finishSpecular` are exactly 1 and black for an instance with no
            // SurfaceFinish, so the shaded branch is bit-for-bit its pre-2026-09-19 self there; and
            // for one WITH a finish, `lit`'s legacy SpecularContribution term has already been
            // replaced by the lobe (SpecularConfig's global sheen does not apply to a finished
            // instance -- see SurfaceFinish's doc comment), which is why the diffuse below multiplies
            // `irradiance` rather than `lit`.
            float emissive = vMaterial > 0.5 ? 1.0 : 0.0;
            vec3 shadedDiffuse = vFinish.x >= 0.0 ? (irradiance * diffuseScale) : lit;
            vec3 colorLinear = mix(
                (vColor.rgb * shadedDiffuse * detailAlbedo) + finishSpecular,
                vColor.rgb * EMISSIVE_GAIN * (vFinish.w < -0.5 ? detailAlbedo : vec3(1.0)),
                emissive);
            colorLinear += windowMask * windowLit * WindowNight() * WINDOW_LIGHT;

            // TASK-151: an explicit tone-mapping decision, gated on ToneMapping.Enabled -- see
            // that type's own doc comment for why "disabled" must skip this code path entirely
            // rather than run an "identity" curve (no real tonemap curve is identity over [0,1]).
            if (light.specularShininessExposureToneEnabled.z > 0.5) {
                float exposure = light.specularShininessExposureToneEnabled.y;
                colorLinear = vec3(1.0) - exp(-colorLinear * exposure);
            }

            // A glow's alpha reaches the blend equation (SrcAlpha, One); everything else is opaque and
            // writes 1.0 exactly as before.
            outColor = vec4(ApplyRenderStyle(colorLinear), mix(1.0, vColor.a, glow));
        }
        """;

    /// <summary>
    /// Shared post-lighting color treatments for the legacy box and instanced primitive shaders.
    /// Numeric selectors match <see cref="RenderStyle"/> and are pinned by RenderStyleTests.
    /// </summary>
    private const string RenderStyleGlsl = """
        // Applied after lighting and tone mapping. The zero/default style exits without
        // changing any arithmetic in the existing Natural path.
        vec3 ApplyRenderStyle(vec3 color) {
            int style = int(light.renderStyle.x);
            if (style == 0) {
                return color;
            }

            float luminance = dot(clamp(color, 0.0, 1.0), vec3(0.2126, 0.7152, 0.0722));
            if (style == 1) {
                // Warm, unglazed clay: keep the lightness model while replacing surface hue.
                return mix(vec3(0.24, 0.105, 0.065), vec3(0.95, 0.67, 0.47),
                           clamp(luminance * 1.25 + (CoverageThreshold(gl_FragCoord.xy) - 0.5) * 0.04, 0.0, 1.0));
            }
            if (style == 2) {
                // Four hard value bands retain each material's hue.
                float band = (floor(min(luminance, 0.999) * 4.0) + 0.5) / 4.0;
                return clamp(color * (band / max(luminance, 0.001)), 0.0, 1.0);
            }
            if (style == 3) {
                // A compact RGB palette with stable screen-space ordered dithering.
                float dither = (CoverageThreshold(gl_FragCoord.xy) - 0.5) * 0.1;
                return floor(clamp(color + dither, 0.0, 0.999) * 4.0) / 3.0;
            }
            // Ink wash: five broad grey values keep adjacent faces and props legible.
            float wash = floor(min(luminance, 0.999) * 5.0) / 4.0;
            return vec3(mix(0.08, 0.96, wash));
        }
        """;

    /// <summary>
    /// The coverage dither, shared VERBATIM by <see cref="PrimitiveFrag"/> and
    /// <see cref="PrimitiveDepthFrag"/> — one declaration rather than two copies because the depth
    /// prepass and the colour pass must discard exactly the same fragments. A copy that drifted by
    /// one table entry would leave a see-through surface writing depth it never shades (or the
    /// reverse), which shows up as geometry missing behind a dithered wall — a bug with no obvious
    /// cause at the far end of two files.
    /// </summary>
    private const string CoverageDitherGlsl = """
        float CoverageThreshold(vec2 fragCoord) {
            const float bayer[16] = float[16](
                 0.0,  8.0,  2.0, 10.0,
                12.0,  4.0, 14.0,  6.0,
                 3.0, 11.0,  1.0,  9.0,
                15.0,  7.0, 13.0,  5.0);
            ivec2 c = ivec2(fragCoord) & 3;
            return (bayer[(c.y * 4) + c.x] + 0.5) / 16.0;
        }
        """;

    /// <summary>
    /// The DEPTH-PREPASS fragment shader for the primitive pipeline: the same visibility rules as
    /// <see cref="PrimitiveFrag"/> (coverage dither, outline-shell facing) and none of its shading.
    /// Paired with a pipeline that writes depth with the colour mask off.
    /// <para>
    /// <b>Why a prepass exists at all.</b> <see cref="PrimitiveFrag"/> contains <c>discard</c>, which
    /// forces the hardware to defer the depth test until after the shader has run — so before this,
    /// EVERY rasterized fragment paid the full lighting cost even when a nearer surface later covered
    /// it. Measured on a four-AI Triage ward (1,700 instances, 17 point lights): the primitive pass
    /// was 22.7 ms of a 32 ms frame, and the same frame with the depth test forced early was 6.3 ms —
    /// i.e. roughly three quarters of the pass was shading pixels nobody sees.
    /// </para>
    /// <para>
    /// Forcing <c>early_fragment_tests</c> on the colour shader instead would be wrong, not merely
    /// approximate: a dithered-away fragment would still write depth and occlude what is behind it.
    /// This pass discards on the same rule first, so the depth buffer keeps the holes, and the colour
    /// pass then runs with depth writes off and <c>LESS_OR_EQUAL</c> — which the hardware IS free to
    /// test early, discard or not.
    /// </para>
    /// <para>
    /// Consumes a SUBSET of <see cref="PrimitiveVert"/>'s outputs (colour, material, shell facing) —
    /// legal in Vulkan, and the reason this pass costs about a millisecond rather than repeating the
    /// vertex stage's interpolation work for terms it would never read.
    /// </para>
    /// </summary>
    public const string PrimitiveDepthFrag = $$"""
        #version 450
        layout(location = 1) in vec4 vColor;
        layout(location = 9) in float vMaterial;
        layout(location = 11) in float vShellFacesAway;

        {{CoverageDitherGlsl}}

        void main() {
            if (vColor.a < CoverageThreshold(gl_FragCoord.xy)) {
                discard;
            }

            // MaterialKind.OutlineShell: PrimitiveFrag throws away everything facing the camera, so
            // those fragments must not write depth here either -- otherwise the shell would occlude
            // the very object it outlines.
            if (vMaterial > 1.5 && vMaterial < 2.5 && vShellFacesAway <= 0.0) {
                discard;
            }
        }
        """;

    /// <summary>
    /// Camera-facing particle billboard, drawn instanced: binding 0 is one shared unit quad
    /// (per-vertex corner in [-0.5,0.5]²), binding 1 is the per-instance data
    /// <see cref="ParticlePacker"/> packs. The quad is spanned in world space by the camera's
    /// right/up axes (pushed as <c>right</c>/<c>up</c>, recovered from the same view-projection
    /// by <see cref="ParticlePacker.CameraBasis"/>), which is what makes it face the viewer from
    /// any angle without a per-particle model matrix.
    /// <para>
    /// The corner is rotated in <i>quad space</i>, before being mapped onto the camera basis:
    /// rotating in world space would tilt the billboard out of the view plane instead of spinning
    /// it. Shared by both the alpha and additive pipelines — they differ only in blend state.
    /// </para>
    /// </summary>
    public const string ParticleVert = """
        #version 450
        layout(location = 0) in vec2 inCorner;
        layout(location = 1) in vec3 inCenter;
        layout(location = 2) in float inSize;
        layout(location = 3) in vec4 inColor;
        layout(location = 4) in float inRotation;
        layout(push_constant) uniform Push { mat4 viewProj; vec4 right; vec4 up; } pc;
        layout(location = 0) out vec2 vCorner;
        layout(location = 1) out vec4 vColor;
        void main() {
            float s = sin(inRotation);
            float c = cos(inRotation);
            vec2 corner = vec2(inCorner.x * c - inCorner.y * s, inCorner.x * s + inCorner.y * c);
            vec3 world = inCenter + (pc.right.xyz * corner.x + pc.up.xyz * corner.y) * inSize;
            gl_Position = pc.viewProj * vec4(world, 1.0);
            vCorner = corner;
            vColor = inColor;
        }
        """;

    /// <summary>
    /// A soft round sprite generated procedurally from the quad coordinate — no texture, and
    /// therefore no sampler, descriptor set or image upload anywhere in the particle path (this
    /// engine has no descriptor infrastructure yet, and a hard-edged square would read as
    /// confetti rather than as dust or fire).
    /// <para>
    /// The squared falloff is deliberate: a linear one leaves a visible disc edge once hundreds
    /// of sprites overlap. Alpha multiplies the instance colour's own alpha, so the same shader
    /// serves the alpha pipeline (where it is coverage) and the additive one (where it is
    /// brightness).
    /// </para>
    /// <para>
    /// Because the disc is radially symmetric, <c>vCorner</c>'s rotation cancels out here — so
    /// <see cref="ParticleInstance.Rotation"/> is currently invisible. Replacing this with a
    /// texture or any asymmetric pattern makes it visible <i>and</i> makes the billboard basis's
    /// sign observable; both need a render test at that point.
    /// </para>
    /// </summary>
    public const string ParticleFrag = """
        #version 450
        layout(location = 0) in vec2 vCorner;
        layout(location = 1) in vec4 vColor;
        layout(location = 0) out vec4 outColor;
        void main() {
            float d2 = dot(vCorner, vCorner) * 4.0;
            float falloff = clamp(1.0 - d2, 0.0, 1.0);
            outColor = vec4(vColor.rgb, vColor.a * falloff * falloff);
        }
        """;

    public const string LineVert = """
        #version 450
        layout(location = 0) in vec3 inPos;
        layout(location = 1) in vec3 inColor;
        layout(push_constant) uniform Push { mat4 viewProj; } pc;
        layout(location = 0) out vec3 vColor;
        void main() { gl_Position = pc.viewProj * vec4(inPos, 1.0); vColor = inColor; }
        """;

    public const string LineFrag = """
        #version 450
        layout(location = 0) in vec3 vColor;
        layout(location = 0) out vec4 outColor;
        void main() { outColor = vec4(vColor, 1.0); }
        """;
}
