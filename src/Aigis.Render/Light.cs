using System.Collections.Immutable;
using System.Numerics;

namespace Aigis.Render;

/// <summary>
/// TASK-036 scope (2026-08-02): directional ("sun") + ambient only — point/spot are
/// deferred, see tasks/board.md. A directional light has direction but no position (like
/// sunlight, effectively infinitely far away); ambient has neither — it is a flat,
/// scene-wide floor term so nothing is ever pure black. Both feed the box/primitive
/// fragment shader (<see cref="ShaderSource.BoxFrag"/>) through one small uniform buffer —
/// see <c>HeadlessRenderer.Boxes.cs</c>/<c>WindowedRenderer.Primitives.cs</c>
/// (EnsureLightingDescriptor/UpdateLighting).
/// <para>
/// Uniform buffer over push constants: the box/primitive push-constant block
/// (<c>BoxPush</c>: mat4 + 3×vec4) is already 112 of the Vulkan-guaranteed-minimum 128
/// push-constant bytes. A genuine per-light Color+Intensity for both light types at once
/// needs 48 bytes (3×vec4) — it does not fit in the remaining 16 without dropping color
/// or relying on a device-specific larger limit, which would be a portability trap on a
/// project that already can't validate against real hardware here (no Vulkan SDK). A UBO
/// also leaves room to grow into an array for point lights later without another
/// pipeline-layout change.
/// </para>
/// </summary>
/// <param name="Direction">World-space, surface-toward-light (i.e. "which way is the sun",
/// NOT "which way its rays travel" — the opposite sign convention from Unity/Unreal's
/// forward-facing light direction). The fragment shader uses it directly as
/// <c>dot(worldNormal, normalize(Direction))</c>, so a light straight overhead is
/// <c>+Vector3.UnitY</c>, not <c>-Vector3.UnitY</c>. Never view-space — always the same
/// space as the vertex shader's world-space normal. Magnitude is ignored (the shader
/// re-normalizes); only <see cref="Intensity"/> controls brightness. Must not be zero or
/// non-finite — see <see cref="SceneLighting.Validate"/>, which every render call applies
/// before this reaches the GPU.</param>
/// <param name="Color">Multiplies the diffuse term; <see cref="Vector3.One"/> is neutral
/// (no tint).</param>
/// <param name="Intensity">Scalar multiplier on the diffuse term (the old hardcoded shader's
/// fixed <c>0.72</c>).</param>
public readonly record struct DirectionalLight(Vector3 Direction, Vector3 Color, float Intensity)
{
    /// <summary>Reproduces the shader's old hardcoded sun exactly (same direction, same 0.72
    /// diffuse contribution) so existing golden PNGs are unaffected by this default.
    /// Pre-normalized on the CPU (matching the old GPU-side <c>normalize(vec3(...))</c>
    /// literal's already-near-unit value); the shader normalizes again defensively for any
    /// caller-supplied direction, so this is "golden-PNG identical" rather than a
    /// guaranteed bit-for-bit float match — verify against the <c>visual</c>-tier goldens
    /// locally before relying on exact pixels.</summary>
    public static DirectionalLight Default { get; } = new(
        Vector3.Normalize(new Vector3(0.4f, 0.85f, 0.35f)), Vector3.One, 0.72f);
}

/// <summary>
/// TASK-151: a hemisphere ambient floor — <see cref="Color"/> ("sky") lit from directly above
/// (<c>worldNormal.y == 1</c>) blending to <see cref="GroundColor"/> lit from directly below
/// (<c>worldNormal.y == -1</c>), via <c>mix(GroundColor, Color, worldNormal.y * 0.5 + 0.5)</c> in
/// <see cref="ShaderSource.PrimitiveFrag"/>. Cheap — one extra <c>vec4</c> in the lighting UBO,
/// no per-material data — and it is what actually keeps a flat-colored box from reading as flat:
/// a pure flat ambient (this type's pre-TASK-151 shape) lights every face of a cube identically
/// regardless of which way it faces, so the only cue distinguishing faces was ever the sun's own
/// single directional term; a floor-facing underside (which the sun's direction can never reach)
/// used to receive exactly the same ambient as a sky-facing top.
/// <para>
/// <see cref="Default"/> sets <see cref="GroundColor"/> equal to <see cref="Color"/> — the
/// hemisphere mix then collapses to the old flat term for every normal, so every pre-TASK-151
/// caller (nothing has ever constructed a non-default <see cref="AmbientLight"/> before this
/// task) renders bit-for-bit identically. The 2-argument constructor is kept for the same
/// reason: source compatibility with any future caller that only wants the old flat behavior.
/// </para>
/// </summary>
/// <param name="Color">The "sky" ambient tint — a normal pointing straight up receives exactly
/// this color (before <see cref="Intensity"/>).</param>
/// <param name="GroundColor">The "ground" ambient tint — a normal pointing straight down
/// receives exactly this color (before <see cref="Intensity"/>).</param>
/// <param name="Intensity">Scalar multiplier applied after the sky/ground mix — same role as the
/// pre-TASK-151 single-color type's <c>Intensity</c>.</param>
public readonly record struct AmbientLight(Vector3 Color, Vector3 GroundColor, float Intensity)
{
    /// <summary>Pre-TASK-151 constructor shape: a flat ambient with no sky/ground split, kept so
    /// every caller written before the hemisphere split existed still compiles unchanged —
    /// equivalent to <see cref="GroundColor"/> == <paramref name="Color"/>.</summary>
    public AmbientLight(Vector3 Color, float Intensity) : this(Color, Color, Intensity)
    {
    }

    /// <summary>Reproduces the shader's old hardcoded 0.28 flat ambient floor (sky == ground, so
    /// the hemisphere mix is a no-op) — bit-for-bit identical to every pre-TASK-151 caller.</summary>
    public static AmbientLight Default { get; } = new(Vector3.One, Vector3.One, 0.28f);
}

/// <summary>
/// TASK-151: a global (scene-wide, not per-material — see this task's own scope note on
/// <see cref="SceneLighting"/>) Blinn-Phong specular highlight. Deliberately NOT per-object/
/// per-material data: <c>MaterialPalette</c> is a flat per-object color only, and giving every
/// box its own roughness/shininess would be the "full PBR material system" TASK-151's acceptance
/// bar explicitly puts out of scope. One shininess/intensity pair for the whole frame is enough
/// to break up a flat-colored box's silhouette with a believable highlight without touching
/// per-object data at all.
/// <para>
/// Needs a view direction, which this engine has never had a per-fragment (or even a reliable
/// per-object) source for before this task — see <see cref="ShaderSource.PrimitiveFrag"/>'s
/// <c>SpecularContribution</c> for the same per-object <c>vWorldCenter</c> approximation
/// <see cref="PointLight"/> already established (an object's own center stands in for its true
/// surface position). <see cref="HeadlessRenderer.RenderPrimitiveScene"/>/
/// <see cref="WindowedRenderer.RenderPrimitiveSceneAndPresent"/>'s <c>cameraPosition</c>
/// parameter supplies the other half (where the eye actually is) — <b>fail-loud, not
/// silent-degrade</b>: passing <see cref="Intensity"/> &gt; 0 without a
/// <c>cameraPosition</c> throws (arch-reviewer TASK-151 finding: a silently-zeroed specular term
/// would be an undiagnosable "I set intensity but nothing changed" bug, the same class of
/// mistake <see cref="SceneLighting.Validate"/> already guards against for every other field).
/// </para>
/// </summary>
/// <param name="Shininess">Blinn-Phong exponent — higher is a smaller, sharper highlight. Must
/// be finite and positive.</param>
/// <param name="Intensity">Scalar multiplier on the specular term. <c>0</c> (see
/// <see cref="None"/>) disables specular entirely — cheapest possible no-op, and what every
/// pre-TASK-151 caller gets by default. Must be finite and non-negative.</param>
public readonly record struct SpecularConfig(float Shininess, float Intensity)
{
    /// <summary>No specular contribution — bit-for-bit identical to every pre-TASK-151 caller
    /// (the shininess value is irrelevant at zero intensity, but kept finite/positive so
    /// <see cref="SceneLighting.Validate"/> never needs a special case for "disabled").</summary>
    public static SpecularConfig None { get; } = new(32f, 0f);
}

/// <summary>
/// TASK-151: explicit tone-mapping/exposure decision for <see cref="ShaderSource.PrimitiveFrag"/>
/// — replacing an IMPLICIT decision (the old shader wrote raw <c>ambient + diffuse + specular</c>
/// straight to a UNORM color attachment, which Vulkan silently hard-clips to <c>[0,1]</c> on
/// write) with one the shader itself makes on purpose, per this task's own acceptance bar.
/// <para>
/// <see cref="Legacy"/> (the default) deliberately reproduces that OLD implicit-clamp behavior
/// bit-for-bit — <see cref="Enabled"/> <see langword="false"/> skips the new tonemap code path
/// entirely rather than running an "identity" tonemap curve, because no real filmic/Reinhard-style
/// curve is actually identity over <c>[0,1]</c> (that is the whole point of a tonemap: it
/// compresses highlights by gently rolling off the WHOLE range, not just values already past 1)
/// — so enabling it for every caller by default would silently perturb every existing golden PNG
/// this engine has, across every <c>games/Game.*</c> title, not just this task's own scope
/// (Triage). Callers that want the new curve (Triage's own <c>TriageGame.BuildLighting</c>)
/// opt in explicitly; every other caller's rendered output is unaffected by this type existing.
/// </para>
/// </summary>
/// <param name="Exposure">Multiplies the linear color before the tonemap curve
/// (<c>1 - exp(-color * Exposure)</c>, applied per-channel) — higher pushes more of the midtone
/// range toward the curve's highlight rolloff. Must be finite and positive. Ignored when
/// <see cref="Enabled"/> is <see langword="false"/>.</param>
/// <param name="Enabled">Whether the shader runs the explicit tonemap curve at all — see this
/// type's own doc comment for why "off" is not the same as "identity curve".</param>
public readonly record struct ToneMapping(float Exposure, bool Enabled)
{
    /// <summary>The old implicit-hardware-clamp behavior, reproduced bit-for-bit — every
    /// pre-TASK-151 caller gets exactly this.</summary>
    public static ToneMapping Legacy { get; } = new(1f, false);
}

/// <summary>
/// TASK-149: a positional light with distance-based falloff — the point/spot type TASK-036
/// deliberately deferred ("needs a UBO layout break, not additive", tasks/archive/2026-08.md).
/// Consumed by <see cref="ShaderSource.PrimitiveFrag"/> only (the legacy
/// <see cref="ShaderSource.BoxFrag"/>/<see cref="HeadlessRenderer.RenderScene"/> path stays
/// directional+ambient-only, same scope line TASK-148 already drew for shadows).
/// <para>
/// <b>Per-object, not per-fragment, evaluation</b>: this engine has no textures, specular or
/// per-fragment world position today (<see cref="ShaderSource.PrimitiveVert"/> only ever bakes a
/// pre-composed <c>mvp</c>, never a raw world-space position, to keep the instanced vertex format
/// small — see <c>PrimitiveInstancePacker</c>). TASK-149 adds exactly one more
/// per-instance attribute, the instance's own world-space center (<c>WorldCenter</c> in
/// <c>PrimitiveInstancePacker</c>), and evaluates every point light's
/// direction/attenuation ONCE per instance from that center rather than per-fragment from the
/// true surface point. This is a deliberate, documented approximation (an object near a light
/// reads brighter than one far from it — the acceptance bar this task actually needs — but a
/// single large surface does not itself gradient across its own face the way true per-fragment
/// point-light shading would). Upgrading to true per-fragment shading later needs its own
/// per-vertex world-position attribute and is out of this task's scope.
/// </para>
/// </summary>
/// <param name="Position">World-space light position.</param>
/// <param name="Color">Multiplies the point light's own diffuse contribution;
/// <see cref="Vector3.One"/> is neutral.</param>
/// <param name="Intensity">Scalar brightness at <see cref="Position"/> itself, before the
/// distance falloff <see cref="Range"/> describes. Must be finite and non-negative — see
/// <see cref="SceneLighting.Validate"/>.</param>
/// <param name="Range">Distance (world units) at which the light's contribution has fallen to
/// (approximately) zero — the shader's falloff is <c>intensity / (1 + (distance/range)^2)</c>,
/// clamped to 0 past <c>Range</c>. Must be finite and strictly positive (a zero/negative range
/// would divide by zero or invert the falloff on the GPU).</param>
public readonly record struct PointLight(Vector3 Position, Vector3 Color, float Intensity, float Range);

/// <summary>
/// TASK-168: a positional light constrained to a CONE — the type <see cref="PointLight"/>
/// deliberately was not (omnidirectional), and the one Triage's ported <c>LAMP</c> street light
/// actually needs (Unity <c>AlphaPrefabGenerator.BuildStreetLight</c>'s <c>LampSpot</c> child:
/// straight down, <c>spotAngle 55</c>, range 7, intensity 9).
/// <para>
/// <b>Evaluated PER-FRAGMENT, unlike <see cref="PointLight"/>.</b> This is the whole reason the
/// type could not be added as a flag on <see cref="PointLight"/>: point/specular shading is
/// evaluated once per OBJECT from the instance's world-space centre (see
/// <see cref="PointLight"/>'s own doc comment), and a cone whose entire purpose is to pool light
/// on the ground beneath the head cannot express itself that way at all — Triage's floor is
/// <c>CellRectMerge</c>d into a handful of large slabs, and a single slab's single centre either
/// is or is not inside the cone, so a per-object cone would read as an entire floor flipping
/// brightness rather than as a pool of light. <see cref="ShaderSource.PrimitiveVert"/> therefore
/// derives a true per-fragment world position (<c>vWorldPos</c>) for this term, and this term
/// only: <see cref="PointLight"/> and <see cref="SpecularConfig"/> still use the per-object
/// approximation, so every scene that authors no spot light renders bit-for-bit unchanged.
/// Migrating those two terms onto <c>vWorldPos</c> as well is a real visual improvement, but one
/// that moves every existing golden and so is filed separately (TASK-171) rather than smuggled in
/// here.
/// </para>
/// <para>
/// Not shadow-casting — the same explicit scope line TASK-148/149 drew (sun shadows only), and
/// what the reference fixture itself does (<c>shadows = None</c>, cut upstream for cost since
/// levels place several).
/// </para>
/// </summary>
/// <param name="Position">World-space light position (the cone's apex).</param>
/// <param name="Direction">World-space direction the cone POINTS — i.e. the way its rays
/// actually travel, so a lamp shining at the floor is <c>-Vector3.UnitY</c>.
/// <b>This is the OPPOSITE convention from <see cref="DirectionalLight.Direction"/></b>, which is
/// surface-toward-light ("which way is the sun") and would be <c>+Vector3.UnitY</c> for an
/// overhead source. The two conventions now coexist deliberately: a directional light has no
/// position and is only ever used inside a <c>dot(normal, toLight)</c>, whereas a spot has a real
/// aim axis that reads naturally as "where is it pointed". Magnitude is ignored (the shader
/// re-normalizes); must not be zero or non-finite — see <see cref="SceneLighting.Validate"/>.</param>
/// <param name="Color">Multiplies this light's diffuse contribution; <see cref="Vector3.One"/> is
/// neutral.</param>
/// <param name="Intensity">Scalar brightness at <see cref="Position"/> itself, before the
/// distance falloff <see cref="Range"/> describes and before the cone term. Must be finite and
/// non-negative.</param>
/// <param name="Range">Distance (world units) at which the contribution has fallen to
/// approximately zero — the same <c>1 / (1 + (distance/range)^2)</c> law
/// <see cref="PointLight.Range"/> already uses, reused rather than a second differently-shaped
/// falloff. Must be finite and strictly positive.</param>
/// <param name="ConeAngleDegrees">The FULL cone angle in degrees, matching Unity's
/// <c>Light.spotAngle</c> (which the reference authors as <c>55</c>) rather than the half-angle —
/// so a value ported from the reference needs no silent halving at the call site. The shader
/// receives cosines of the HALF-angle, converted once on the CPU by
/// <see cref="OuterCosine"/>/<see cref="InnerCosine"/>. Must be finite and in
/// <c>(0, 180)</c>.</param>
public readonly record struct SpotLight(
    Vector3 Position,
    Vector3 Direction,
    Vector3 Color,
    float Intensity,
    float Range,
    float ConeAngleDegrees)
{
    /// <summary>
    /// Fraction of the outer half-angle at which the cone is still at full brightness — the
    /// inner edge of the soft rim. The shader's cone term is
    /// <c>smoothstep(OuterCosine, InnerCosine, cos(angleToFragment))</c>, so this is what stops
    /// the cone from having a hard aliased edge. Deliberately a single documented constant rather
    /// than a per-light parameter: Unity's own spot light has exactly one
    /// <c>spotAngle</c> plus a fixed built-in falloff texture, so a per-light inner angle would be
    /// an invention with no reference value to port, and every extra per-light field costs UBO
    /// bytes (see <see cref="SceneLighting.MaxSpotLights"/>).
    /// </summary>
    public const float InnerConeFraction = 0.85f;

    /// <summary>
    /// Smallest accepted <see cref="ConeAngleDegrees"/> (transform-auditor TASK-168 finding, LOW).
    /// Below roughly this, <see cref="OuterCosine"/> and <see cref="InnerCosine"/> round to the SAME
    /// float, and GLSL <c>smoothstep(edge0, edge1, x)</c> with <c>edge0 == edge1</c> divides by zero —
    /// an undefined/NaN cone term rather than a very narrow beam. A pencil-thin spot is not a use case
    /// this engine has (the shipped fixture is 55°), so this is rejected at the boundary instead of
    /// clamped silently.
    /// </summary>
    public const float MinimumConeAngleDegrees = 0.01f;

    /// <summary>Cosine of the OUTER half-angle — the cone's hard boundary, beyond which the
    /// contribution is exactly zero.</summary>
    public float OuterCosine => MathF.Cos(float.DegreesToRadians(ConeAngleDegrees * 0.5f));

    /// <summary>Cosine of the INNER half-angle (<see cref="InnerConeFraction"/> of the outer),
    /// inside which the cone is at full brightness.
    /// <para>
    /// <b>Always strictly greater than <see cref="OuterCosine"/></b> for any valid
    /// <see cref="ConeAngleDegrees"/>: cosine is strictly decreasing over <c>[0°, 90°)</c> and
    /// <see cref="InnerConeFraction"/> is in <c>(0, 1)</c>, so the inner half-angle is strictly
    /// smaller and its cosine strictly larger. That inequality is exactly GLSL
    /// <c>smoothstep(edge0, edge1, x)</c>'s requirement (<c>edge0 &lt; edge1</c>, undefined
    /// otherwise) — swapping the two would produce a dark core inside a bright ring, which is why
    /// the ordering is derived here once instead of at the call site, and why
    /// <c>SpotLightRenderTests</c> asserts brightness falls off MONOTONICALLY from the cone's
    /// centre outward rather than merely asserting "the centre is lit".
    /// </para>
    /// </summary>
    public float InnerCosine => MathF.Cos(float.DegreesToRadians(ConeAngleDegrees * 0.5f * InnerConeFraction));
}

/// <summary>Scene-wide color treatment. Natural preserves the original shader output.</summary>
public enum RenderStyle
{
    Natural = 0,
    Clay = 1,
    Toon = 2,
    Pixel = 3,
    Ink = 4,
}

/// <summary>The whole-scene lighting state passed to a render call. Optional on every
/// Render*/RenderPrimitiveScene* entry point (defaults to <see cref="Default"/>, which is
/// bit-for-bit what the old hardcoded shader produced) so this did not require touching
/// <c>IGame.BuildRenderables</c> or any of its ~30 call sites — no game customizes lighting
/// yet, so exposing it through the game contract is deferred until one actually needs to
/// (see tasks/board.md TASK-036).
/// <para>
/// TASK-149 added <see cref="PointLights"/> — capped at <see cref="MaxPointLights"/> (see that
/// constant's own doc comment for the byte-budget reasoning) and, since a level may author more
/// than the cap (Level6 authors 34), selection down to the cap MUST happen before this record is
/// constructed for rendering: <see cref="SelectPointLights"/> is the one deterministic rule
/// (nearest-<see cref="MaxPointLights"/> to a caller-supplied viewpoint, hard rule 5) callers are
/// expected to use, rather than each caller inventing its own truncation. <see cref="Validate"/>
/// only checks that whatever was ultimately passed in already fits and is well-formed — it is a
/// boundary guard, not a selection policy.
/// </para></summary>
public readonly record struct SceneLighting(
    DirectionalLight Sun,
    AmbientLight Ambient,
    ImmutableArray<PointLight> PointLights,
    SpecularConfig Specular,
    ToneMapping ToneMap,
    ImmutableArray<SpotLight> SpotLights)
{
    /// <summary>Pre-TASK-149 constructor shape, kept so every caller written before point lights
    /// existed still compiles unchanged — equivalent to passing an empty <see cref="PointLights"/>,
    /// <see cref="SpecularConfig.None"/> and <see cref="ToneMapping.Legacy"/>.</summary>
    public SceneLighting(DirectionalLight Sun, AmbientLight Ambient)
        : this(Sun, Ambient, ImmutableArray<PointLight>.Empty, SpecularConfig.None, ToneMapping.Legacy, ImmutableArray<SpotLight>.Empty)
    {
    }

    /// <summary>Pre-TASK-151 constructor shape, kept so every caller written before specular/
    /// tone-mapping existed still compiles unchanged — equivalent to <see cref="SpecularConfig.None"/>
    /// and <see cref="ToneMapping.Legacy"/>.</summary>
    public SceneLighting(DirectionalLight Sun, AmbientLight Ambient, ImmutableArray<PointLight> PointLights)
        : this(Sun, Ambient, PointLights, SpecularConfig.None, ToneMapping.Legacy, ImmutableArray<SpotLight>.Empty)
    {
    }

    /// <summary>TASK-168: pre-spot-light constructor shape, kept so every caller written before
    /// <see cref="SpotLights"/> existed still compiles unchanged — equivalent to passing an empty
    /// <see cref="SpotLights"/>, which contributes exactly nothing in the shader, so such a caller's
    /// rendered output is bit-for-bit what it was before this task.</summary>
    public SceneLighting(DirectionalLight Sun, AmbientLight Ambient, ImmutableArray<PointLight> PointLights, SpecularConfig Specular, ToneMapping ToneMap)
        : this(Sun, Ambient, PointLights, Specular, ToneMap, ImmutableArray<SpotLight>.Empty)
    {
    }

    /// <summary>
    /// TASK-245: an optional baked indirect-light field — the engine's "cheap GI". When set, it
    /// REPLACES the shader's <c>HemisphereAmbient</c> term inside the volume's own box (and
    /// cross-fades back to it at the edges); when <see langword="null"/>, which is the default and
    /// what every caller written before this task passes, the shader takes its old path and the
    /// rendered frame is bit-for-bit what it was.
    /// <para>
    /// <b>Not part of the positional constructor, on purpose.</b> Every existing call site
    /// constructs this record positionally, and adding a seventh element would have touched all of
    /// them to say "nothing here". <c>lighting with { Irradiance = volume }</c> is how a caller
    /// opts in — which is also exactly how the highlight-video tool applies a volume on top of a
    /// recorded session's own lighting without rebuilding it.
    /// </para>
    /// <para>
    /// Bake one with <see cref="IrradianceBaker.Bake"/>. Pass the SAME instance every frame: the
    /// renderers cache the uploaded 3D textures by reference identity, exactly as
    /// <see cref="TextureData"/> and <see cref="DynamicTexture"/> already require, so handing over
    /// an equal-but-new volume each frame would re-upload it each frame.
    /// </para>
    /// </summary>
    public IrradianceVolume? Irradiance { get; init; }

    /// <summary>
    /// 2026-10-06: the greyscale detail maps <see cref="PrimitiveInstance.TextureLayer"/> selects —
    /// see <see cref="SurfaceTextureSet"/>. <see langword="null"/> (the default) leaves every
    /// instance untextured. Residency is by instance identity: pass the same set every frame.
    /// </summary>
    public SurfaceTextureSet? SurfaceTextures { get; init; }

    /// <summary>Color treatment for scene geometry; defaults to Natural.</summary>
    public RenderStyle Style { get; init; }

    /// <summary>
    /// Where the world-anchored <see cref="SurfaceDetail.Patches"/> pattern is sampled from, as an
    /// XZ offset added to the world position (default zero — every caller before it, unchanged).
    /// <para>2026-09-29, the train map: its ground does not move, the WORLD scrolls past it, so a
    /// lawn sampled at the world position stood still under a moving scene and looked bolted to the
    /// train (「芝のテクスチャが流れていく地面といっしょに動いていません」). Handing the distance the
    /// ground has run here makes the pattern flow with it. Patches only: the finer world-anchored
    /// classes are on the wagons, which do not scroll. Packed into the UBO's reserved
    /// <c>spotLightCount.yz</c>, so no std140 offset moves.</para>
    /// </summary>
    public System.Numerics.Vector2 PatchesOffsetXz { get; init; }

    /// <summary>Same default sun/ambient as before TASK-149, with no point lights, no spot lights,
    /// no specular and the legacy implicit-clamp tone response — bit-for-bit identical rendered
    /// output to every pre-TASK-149/TASK-151/TASK-168 caller.</summary>
    public static SceneLighting Default { get; } = new(
        DirectionalLight.Default, AmbientLight.Default, ImmutableArray<PointLight>.Empty, SpecularConfig.None, ToneMapping.Legacy, ImmutableArray<SpotLight>.Empty);

    /// <summary>
    /// TASK-149's per-frame light cap.
    /// <para><b>The paragraph that used to live here described a cap of 8 and was left behind when the
    /// value became 256</b> (commit <c>8f47e02</c>, which raised both this constant and
    /// <c>ShaderSource</c>'s <c>pointLights[]</c> together, so CPU and GPU do agree). Its reasoning —
    /// "8 lights costs 256 of the UBO's 384 bytes … individual rooms hold at most a handful; 8 gives
    /// comfortable headroom" — no longer describes this constant, so it is not restated as if it did.
    /// Measured 2026-08-15: the most any shipped level authors in TOTAL is 34 (Level6), so 256 is far
    /// above anything <see cref="SelectPointLights"/> can ever return, and the cap no longer bounds
    /// per-fragment light stacking in practice — level content does.</para>
    /// <para><b>Open:</b> <c>LeverAndLightFixtureTests.Profile_PreservesTheReferencesIntensityRatios_AndStaysUnderTheMeasuredPerLightCeiling</c>
    /// deliberately multiplies this cap by the brightest key's intensity so it "tracks both the scale
    /// factor and the cap", and it has been RED since the cap grew (256 × 0.15 = 38.4 against a 1.2
    /// ceiling derived when the cap was 8). Whether the answer is a re-derived intensity scale, a
    /// smaller cap, or an assertion keyed to realistic per-room overlap instead of the array size is a
    /// rendering decision — see TASK-178. Do not silently relax either number.</para></summary>
    public const int MaxPointLights = 256;

    /// <summary>Maximum number of fixed overhead fixtures that receive dedicated downward
    /// shadow maps. This is deliberately separate from <see cref="MaxPointLights"/>: all 256
    /// lights can illuminate, but only four static ceiling fixtures may cast the inexpensive
    /// orthographic fixture shadows.</summary>
    public const int MaxDownwardFixtureShadows = 4;

    /// <summary>
    /// TASK-168's per-frame SPOT light cap, a separate budget from <see cref="MaxPointLights"/>
    /// because spots live in their own appended UBO array rather than sharing point slots (see
    /// <c>HeadlessRenderer.Boxes.cs</c>'s <c>LightingUniform</c> doc comment for the std140
    /// offsets). That separation is not a byte-saving trick — it is what stops a spot whose cone
    /// points away from everything visible from evicting a point light that is actually lighting
    /// the player, which sharing one ranked array would do.
    /// <para>
    /// 4, not 8: a spot costs 64 UBO bytes to a point light's 32 (it carries a direction and two
    /// cone cosines as well), and spots are a fixture-scale feature — the only shipped authored
    /// spot source is the <c>LAMP</c> street light, of which Level2 places exactly one and no
    /// shipped level places more than a handful. 4 keeps the UBO at 656 bytes total, far inside
    /// the Vulkan-guaranteed-minimum 16KiB range, with room to raise this if a level ever clusters
    /// more.
    /// </para></summary>
    public const int MaxSpotLights = 4;

    /// <summary>
    /// TASK-168: the spot-light twin of <see cref="SelectPointLights"/> — the identical stable
    /// nearest-<paramref name="cap"/> rule (hard rule 5), applied to its own separate array and
    /// its own separate <see cref="MaxSpotLights"/> budget.
    /// <para>
    /// Selection is by distance to <paramref name="viewpoint"/> ALONE, deliberately not weighted by
    /// whether the cone happens to point at anything: a cone-aware ranking would need to know what
    /// is visible this frame (geometry the selection step does not have) and would make the chosen
    /// set flicker as the player moves through a cone's edge. Distance-only keeps the same
    /// reproducible, content-independent rule the point-light path already established.
    /// </para>
    /// </summary>
    /// <param name="authored">Every spot light a level authors — may exceed <paramref name="cap"/>.</param>
    /// <param name="viewpoint">World-space reference point distances are measured from — the
    /// caller's "what should be brightly lit right now" (Triage passes the player's live feet
    /// position, deterministic simulation state, exactly as it does for point lights).</param>
    /// <param name="cap">Defaults to <see cref="MaxSpotLights"/>; exposed for tests that want to
    /// pin the selection rule without depending on the UBO-sized constant.</param>
    public static ImmutableArray<SpotLight> SelectSpotLights(IReadOnlyList<SpotLight> authored, Vector3 viewpoint, int cap = MaxSpotLights)
    {
        ArgumentNullException.ThrowIfNull(authored);
        if (cap < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(cap), cap, "cap must be non-negative.");
        }

        return authored
            .Select((light, index) => (light, index, distanceSq: Vector3.DistanceSquared(light.Position, viewpoint)))
            .OrderBy(entry => entry.distanceSq)
            .ThenBy(entry => entry.index)
            .Take(cap)
            .Select(entry => entry.light)
            .ToImmutableArray();
    }

    /// <summary>
    /// The one deterministic selection rule TASK-149's acceptance bar requires (hard rule 5): a
    /// STABLE sort of <paramref name="authored"/> by squared distance to
    /// <paramref name="viewpoint"/>, ties broken by original authored index (never by "whichever
    /// the iteration order happened to reach" — LINQ's <c>OrderBy</c> is a
    /// guaranteed-stable sort per its own documented contract, and the explicit
    /// <c>ThenBy(originalIndex)</c> makes the tie-break itself independent of that stability
    /// guarantee too, so the result is reproducible even if a future edit swapped in a differently
    /// implemented sort), truncated to <paramref name="cap"/>.
    /// </summary>
    /// <param name="authored">Every point light a level authors — may exceed <paramref name="cap"/>
    /// (Level6 authors 34).</param>
    /// <param name="viewpoint">World-space reference point distances are measured from — the
    /// caller's choice of "what should be brightly lit". Triage uses the fixed stage centre so
    /// its enabled light set cannot pop as a player moves.</param>
    /// <param name="cap">Defaults to <see cref="MaxPointLights"/>; exposed for tests that want to
    /// pin the selection rule at a smaller cap without depending on the UBO-sized constant.</param>
    public static ImmutableArray<PointLight> SelectPointLights(IReadOnlyList<PointLight> authored, Vector3 viewpoint, int cap = MaxPointLights)
    {
        ArgumentNullException.ThrowIfNull(authored);
        if (cap < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(cap), cap, "cap must be non-negative.");
        }

        return authored
            .Select((light, index) => (light, index, distanceSq: Vector3.DistanceSquared(light.Position, viewpoint)))
            .OrderBy(entry => entry.distanceSq)
            .ThenBy(entry => entry.index)
            .Take(cap)
            .Select(entry => entry.light)
            .ToImmutableArray();
    }

    /// <summary>Rejects a zero/near-zero or non-finite <see cref="DirectionalLight.Direction"/>
    /// (would <c>normalize()</c> to NaN on the GPU, silently blacking out the whole scene),
    /// non-finite colours/intensities, more <see cref="PointLights"/> than <see cref="MaxPointLights"/>
    /// (a caller that skipped <see cref="SelectPointLights"/> — this guard exists so that mistake
    /// throws here with a clear message instead of silently truncating or overflowing the UBO),
    /// and any individual point light with a non-finite position/colour/intensity or a
    /// non-positive/non-finite <see cref="PointLight.Range"/> (would divide by zero or invert the
    /// falloff on the GPU) — the same boundary-validation discipline
    /// <c>PrimitiveInstance.Validate</c> already applies to render geometry. Called by every
    /// renderer's <c>UpdateLighting</c> before the value reaches the GPU, so a bad caller throws
    /// here instead of producing an undiagnosable black frame.</summary>
    /// <exception cref="ArgumentException">A non-finite colour/intensity, a direction whose
    /// length is non-finite or too small to normalize meaningfully, too many point lights, or a
    /// malformed point light.</exception>
    public void Validate()
    {
        if (!Enum.IsDefined(Style))
        {
            throw new ArgumentOutOfRangeException(nameof(Style), Style, "Unknown render style.");
        }

        ValidateDirection(Sun.Direction);
        ValidateFinite(Sun.Color, nameof(Sun.Color));
        ValidateFinite(Sun.Intensity, nameof(Sun.Intensity));
        ValidateFinite(Ambient.Color, nameof(Ambient.Color));
        ValidateFinite(Ambient.GroundColor, $"{nameof(Ambient)}.{nameof(Ambient.GroundColor)}");
        ValidateFinite(Ambient.Intensity, nameof(Ambient.Intensity));

        if (!float.IsFinite(Specular.Shininess) || Specular.Shininess <= 0f)
        {
            throw new ArgumentException(
                $"{nameof(Specular)}.{nameof(SpecularConfig.Shininess)} must be finite and positive, was {Specular.Shininess}.");
        }

        if (!float.IsFinite(Specular.Intensity) || Specular.Intensity < 0f)
        {
            throw new ArgumentException(
                $"{nameof(Specular)}.{nameof(SpecularConfig.Intensity)} must be finite and non-negative, was {Specular.Intensity}.");
        }

        if (ToneMap.Enabled && (!float.IsFinite(ToneMap.Exposure) || ToneMap.Exposure <= 0f))
        {
            throw new ArgumentException(
                $"{nameof(ToneMap)}.{nameof(ToneMapping.Exposure)} must be finite and positive when {nameof(ToneMap)}.{nameof(ToneMapping.Enabled)} is true, was {ToneMap.Exposure}.");
        }

        if (PointLights.Length > MaxPointLights)
        {
            throw new ArgumentException(
                $"{nameof(PointLights)} has {PointLights.Length} entries, more than {nameof(MaxPointLights)} ({MaxPointLights}) — call {nameof(SelectPointLights)} before constructing {nameof(SceneLighting)}.");
        }

        for (var i = 0; i < PointLights.Length; i++)
        {
            var light = PointLights[i];
            ValidateFinite(light.Position, $"{nameof(PointLights)}[{i}].{nameof(PointLight.Position)}");
            ValidateFinite(light.Color, $"{nameof(PointLights)}[{i}].{nameof(PointLight.Color)}");
            ValidateFinite(light.Intensity, $"{nameof(PointLights)}[{i}].{nameof(PointLight.Intensity)}");
            if (!float.IsFinite(light.Range) || light.Range <= 0f)
            {
                throw new ArgumentException(
                    $"{nameof(PointLights)}[{i}].{nameof(PointLight.Range)} must be finite and positive, was {light.Range}.");
            }
        }

        ValidateSpotLights();
    }

    /// <summary>TASK-168: the same boundary discipline <see cref="Validate"/> applies to
    /// <see cref="PointLights"/>, extended to <see cref="SpotLights"/> — plus the two failure modes
    /// unique to a cone: a zero-length <see cref="SpotLight.Direction"/> (would <c>normalize()</c> to
    /// NaN on the GPU and, because the cone term multiplies the whole contribution, black out or
    /// garbage every fragment the light reaches) and a <see cref="SpotLight.ConeAngleDegrees"/>
    /// outside <c>(0, 180)</c> (0 or negative is an empty cone that can never light anything, and
    /// &gt;= 180 is not a cone at all — the caller wanted a <see cref="PointLight"/>). Both fail loud
    /// here rather than producing an undiagnosable frame.</summary>
    private void ValidateSpotLights()
    {
        if (SpotLights.Length > MaxSpotLights)
        {
            throw new ArgumentException(
                $"{nameof(SpotLights)} has {SpotLights.Length} entries, more than {nameof(MaxSpotLights)} ({MaxSpotLights}) — call {nameof(SelectSpotLights)} before constructing {nameof(SceneLighting)}.");
        }

        for (var i = 0; i < SpotLights.Length; i++)
        {
            var light = SpotLights[i];
            var prefix = $"{nameof(SpotLights)}[{i}].";
            ValidateFinite(light.Position, prefix + nameof(SpotLight.Position));
            ValidateFinite(light.Color, prefix + nameof(SpotLight.Color));
            ValidateFinite(light.Intensity, prefix + nameof(SpotLight.Intensity));

            if (!float.IsFinite(light.Direction.LengthSquared()) || light.Direction.LengthSquared() < 1e-12f)
            {
                throw new ArgumentException(
                    $"{prefix}{nameof(SpotLight.Direction)} must be finite and non-zero (it is normalized on the GPU), was {light.Direction}.");
            }

            if (!float.IsFinite(light.Range) || light.Range <= 0f)
            {
                throw new ArgumentException(
                    $"{prefix}{nameof(SpotLight.Range)} must be finite and positive, was {light.Range}.");
            }

            if (!float.IsFinite(light.ConeAngleDegrees) || light.ConeAngleDegrees < SpotLight.MinimumConeAngleDegrees || light.ConeAngleDegrees >= 180f)
            {
                throw new ArgumentException(
                    $"{prefix}{nameof(SpotLight.ConeAngleDegrees)} is the FULL cone angle and must be finite and in [{SpotLight.MinimumConeAngleDegrees}, 180), was {light.ConeAngleDegrees}.");
            }
        }
    }

    private static void ValidateDirection(Vector3 direction)
    {
        if (!float.IsFinite(direction.LengthSquared()) || direction.LengthSquared() < 1e-12f)
        {
            throw new ArgumentException(
                $"{nameof(DirectionalLight)}.{nameof(DirectionalLight.Direction)} must be finite and non-zero, was {direction}.");
        }
    }

    private static void ValidateFinite(Vector3 v, string name)
    {
        if (!float.IsFinite(v.X) || !float.IsFinite(v.Y) || !float.IsFinite(v.Z))
        {
            throw new ArgumentException($"{name} must be finite, was {v}.");
        }
    }

    private static void ValidateFinite(float v, string name)
    {
        if (!float.IsFinite(v))
        {
            throw new ArgumentException($"{name} must be finite, was {v}.");
        }
    }
}

/// <summary>
/// TASK-148: opt-in sun shadow-map configuration for
/// <see cref="HeadlessRenderer.RenderPrimitiveScene"/> and
/// <see cref="WindowedRenderer.RenderPrimitiveSceneAndPresent"/>. Absent (the default
/// <c>null</c> parameter on both methods) runs no shadow pass at all and produces the exact
/// same image as before this task — existing callers/goldens are unaffected by this type
/// existing.
/// <para>
/// One fitted map, not cascades: measured against all 7 shipped Triage stages before deciding
/// (tasks/backlog-details.md#task-148) — the largest is 24x18 tiles at a 1m tile size, so a
/// single 2048&#178; map fitted to <paramref name="AabbMin"/>/<paramref name="AabbMax"/> gives
/// roughly 12-15mm/texel at that scale (24m or the ~30m stage-diagonal, divided by 2048 --
/// recomputed after an earlier draft of this comment claimed 85mm/texel, which does not survive
/// re-checking the same numbers), comfortably enough to avoid a visibly stair-stepped edge once
/// combined with the 3x3 PCF filter below.
/// </para>
/// </summary>
/// <param name="AabbMin">World-space minimum corner the shadow frustum must cover — pass a
/// level's <c>TriageGame.StageBounds.Min</c> with some negative Y headroom (physics jitter can
/// briefly push a collider a few cm below the nominal floor).</param>
/// <param name="AabbMax">World-space maximum corner. <b>Do not use a fixed
/// "<c>StageBounds.TopY + 2f</c>" margin</b> — that only covers the GROUND floor's headroom and
/// clips a <c>[HEIGHT]</c>-lifted mezzanine's own (higher) ceiling out of the shadow frustum's
/// near plane entirely (TASK-150 fix: measured against Level3's real mezzanine via
/// <see cref="Camera.DirectionalShadowViewProjection"/>). Pass the actual highest occluder Y the
/// level can put in front of the sun — for <c>Game.Triage</c> that is
/// <c>TmapWorldBuilder.CeilingTopY</c> (which already folds in enough headroom above every
/// floor's own ceiling slab) — or, for a scene with no authored ceiling, enough Y headroom above
/// <c>StageBounds.TopY</c> to cover a standing humanoid/prop.</param>
/// <param name="EnableDownwardFixtureShadows">Enables the bounded per-fixture downward shadow
/// maps for the first <see cref="SceneLighting.MaxDownwardFixtureShadows"/> point lights. This
/// is not a generic point-light cubemap option.</param>
public readonly record struct ShadowMapConfig(Vector3 AabbMin, Vector3 AabbMax, bool EnableDownwardFixtureShadows = false)
{
    /// <summary>Shadow depth-map resolution (square). See this type's doc comment for why this
    /// is enough for every shipped stage without cascades.</summary>
    public const uint MapSize = 2048;

    /// <summary>Slope-scaled depth bias applied by the shadow depth pass's own rasterizer state
    /// (<c>DepthBiasSlopeFactor</c>) — the primary defense against shadow acne on grazing-angle
    /// surfaces (a wall nearly edge-on to the sun). TASK-148's report documents the actual
    /// three-point sweep that led here, all measured on the same real Triage room (thin
    /// furniture at grazing angles to the sun), by the same "percent of frame pixels changing by
    /// more than 20/255 between shadow-off/on" metric: the FIRST tuning pass (slope 2.5 /
    /// constant 4) was checked only against a single large box occluder and looked clean there,
    /// but on the real room it was erasing the large majority of otherwise-correct shadow, down
    /// to just 0.44% of the frame. Simply lowering bias toward zero is not the fix either — that
    /// direction was also tried and measured, reaching 8.35% (i.e. mostly correct-looking
    /// coverage) but reintroducing visible acne on the box test. These shipped values land
    /// between the two: 1.25% of the frame (~2.8x the first pass's coverage), with no acne
    /// regression on the box test.</summary>
    public const float DepthBiasSlopeFactor = 0.75f;

    /// <summary>Constant depth bias, same rasterizer state, same tuning pass/finding as
    /// <see cref="DepthBiasSlopeFactor"/>.</summary>
    public const float DepthBiasConstantFactor = 1f;

    /// <summary>A second, smaller bias subtracted in NDC space during the shadow comparison
    /// itself (<see cref="ShaderSource.PrimitiveFrag"/>) — belt-and-suspenders on top of the
    /// rasterizer bias above, retuned alongside it (same finding as
    /// <see cref="DepthBiasSlopeFactor"/>).</summary>
    public const float ComparisonBias = 0.00015f;
}
