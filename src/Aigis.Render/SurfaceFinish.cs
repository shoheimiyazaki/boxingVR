namespace Aigis.Render;

/// <summary>
/// 2026-09-19: how a <see cref="PrimitiveInstance"/> REFLECTS, as opposed to
/// <see cref="SurfaceDetail"/>, which is how it is shaped, and <see cref="PrimitiveInstance.Color"/>,
/// which is what colour it is. Two numbers — roughness and metallic — plus the shading model in
/// <c>ShaderSource.PrimitiveFrag</c> that turns them into a highlight, a Fresnel rim and a
/// reflection of the sky.
///
/// <para>
/// <b>Why this exists.</b> Before it, specular was ONE knob for the whole scene
/// (<see cref="SpecularConfig"/>, whose own doc comment records the decision and the measurement
/// behind it). That is enough to say "these surfaces are not chalk", and it is not enough to say
/// anything else: a chrome scalpel, a cotton sheet, a rubber tyre and a painted steel bed rail all
/// took the same Blinn-Phong lobe at the same width, from a view direction approximated at the
/// OBJECT's centre rather than the fragment's. Looked at from across a ward that is invisible;
/// looked at from a metre away — which is what <c>tools/Aigis.ModelViz</c> exists to do — every
/// object in the game reads as the same matte plastic, whatever it is supposed to be made of. The
/// user's brief was exactly that: 「表面粗さ、色、金属感などの材質もそれらしいものにする」.
/// </para>
///
/// <para>
/// <b>Opt-in, and byte-identical when not opted into.</b> <see cref="PrimitiveInstance.Finish"/> is
/// nullable and defaults to <see langword="null"/>, which the packer writes as a negative roughness
/// and the shader reads as "take the pre-2026-09-19 path" — the same <see cref="ToneMapping.Legacy"/>
/// posture, and for the same reason: every other <c>games/Game.*</c> title and every existing golden
/// PNG must not move because Triage started authoring materials. There are consequently two shading
/// paths in that shader on purpose; the legacy one is not dead code.
/// </para>
///
/// <para>
/// <b>What a finish supersedes.</b> For an instance that carries one, <see cref="SpecularConfig"/>'s
/// global sheen is NOT applied — the finish's own lobe replaces it. Day/night still works without
/// the CPU-side scaling <c>TriageGame.BuildLighting</c> has to do for the global sheen, because the
/// lobe is weighted by the sun's own colour and intensity, which already go to zero at night.
/// </para>
///
/// <para>
/// <b>Not a full PBR material system, deliberately.</b> There is no albedo/normal/roughness texture
/// set, no image-based lighting probe and no anisotropy: this pipeline has no per-instance UVs and
/// no tangent frame (see <see cref="SurfaceDetail"/>'s doc comment on why its own field is
/// procedural). The environment term is the scene's existing hemisphere/irradiance ambient sampled
/// along the reflection vector, which costs nothing new and is what makes a metal read as metal in a
/// flat-lit scene.
/// </para>
/// </summary>
/// <param name="Roughness">How scattered the reflection is, in [0,1]. 0 is a mirror and 1 is
/// fully matte. The shader squares it into the GGX width, which is the usual perceptual
/// parameterisation — the interesting range for real materials is roughly 0.15 (polished) to 0.8
/// (raw timber, cloth), and values below <see cref="MinimumRoughness"/> are clamped because an
/// exact mirror produces a sub-pixel highlight that aliases into sparkle.</param>
/// <param name="Metallic">0 for a dielectric (paint, plastic, cloth, skin, wood, glass), 1 for bare
/// metal. It is a SWITCH between two physical models, not a slider of shininess: a metal reflects
/// in its own albedo colour and has no diffuse term at all, which is why brass looks like brass and
/// not like a yellow plastic with a white highlight. Intermediate values exist only to express a
/// surface that is partly one and partly the other within a single instance (a scuffed painted
/// panel); prefer 0 or 1.</param>
public readonly record struct SurfaceFinish(float Roughness, float Metallic)
{
    /// <summary>
    /// The floor the shader clamps <see cref="Roughness"/> to. A perfect mirror concentrates the
    /// whole highlight into less than one pixel, so it flickers in and out as the camera moves —
    /// the standard specular-aliasing failure, and this engine has neither a mip chain nor temporal
    /// filtering to hide it. 0.045 keeps the tightest authorable highlight a few pixels wide at the
    /// close range <c>tools/Aigis.ModelViz</c> inspects at.
    /// </summary>
    public const float MinimumRoughness = 0.045f;

    /// <summary>Flat emulsion, unfinished plaster, chalk. Barely any highlight — its job is to be
    /// the boring end of the scale against which everything else reads.</summary>
    public static SurfaceFinish Matte { get; } = new(0.92f, 0f);

    /// <summary>Ordinary painted joinery, a matte-finished appliance shell, a cardboard box's
    /// coated face. The default for "a thing that is painted and not new".</summary>
    public static SurfaceFinish Paint { get; } = new(0.62f, 0f);

    /// <summary>Injection-moulded plastic — an equipment housing, a bin, a chair shell. Glossier
    /// than paint and noticeably tighter, which is most of what separates the two by eye.</summary>
    public static SurfaceFinish Plastic { get; } = new(0.38f, 0f);

    /// <summary>Automotive clearcoat. The glossiest dielectric in the game by design: a car body is
    /// the one surface a player expects to see the sky in.</summary>
    public static SurfaceFinish CarPaint { get; } = new(0.20f, 0f);

    /// <summary>Glazed ceramic, enamel, a hospital's wipe-clean surfaces, glass. A tight highlight
    /// over a bright diffuse.</summary>
    public static SurfaceFinish Ceramic { get; } = new(0.16f, 0f);

    /// <summary>Tyre rubber, a cable sheath, a gasket. Dark and almost highlight-free, but not
    /// <see cref="Matte"/>: a faint broad sheen at grazing angles is what says "rubber" rather than
    /// "painted black".</summary>
    public static SurfaceFinish Rubber { get; } = new(0.82f, 0f);

    /// <summary>Woven cloth — a sheet, a uniform, a mattress cover. Rough, with the grazing-angle
    /// Fresnel that gives fabric its rim; pair it with <see cref="SurfaceDetail.WovenFabric"/> for
    /// the weave itself.</summary>
    public static SurfaceFinish Fabric { get; } = new(0.86f, 0f);

    /// <summary>Sealed or oiled timber. Pair with <see cref="SurfaceDetail.WoodGrain"/>.</summary>
    public static SurfaceFinish Wood { get; } = new(0.58f, 0f);

    /// <summary>Skin. A dielectric with a soft, wide highlight — no subsurface scattering here, but
    /// the sheen alone separates a face from a painted mannequin at close range.</summary>
    public static SurfaceFinish Skin { get; } = new(0.66f, 0f);

    /// <summary>Painted or powder-coated metal — a bed frame, a locker, a machine chassis. Reads as
    /// metal because of its SHAPE and its tight highlight, not because it is bare, so it is
    /// deliberately a dielectric: paint over steel is paint.</summary>
    public static SurfaceFinish CoatedMetal { get; } = new(0.33f, 0f);

    /// <summary>Bare stainless — an instrument, a trolley frame, a handrail. Pair with
    /// <see cref="SurfaceDetail.BrushedMetal"/> where the part is big enough to show the grain.</summary>
    public static SurfaceFinish Steel { get; } = new(0.34f, 1f);

    /// <summary>Chrome, a polished blade, a mirror-finish fitting. The tightest metal, and the one
    /// that most needs a real environment to reflect.</summary>
    public static SurfaceFinish Chrome { get; } = new(0.08f, 1f);

    /// <summary>Cast or extruded aluminium, galvanised sheet, an untreated alloy housing — metal
    /// that is unmistakably metal but has never been polished.</summary>
    public static SurfaceFinish RoughMetal { get; } = new(0.55f, 1f);

    /// <summary>Weathered, oxidised metal — a trench rail, a rusted hull plate, old machinery.
    /// Metallic, but scattered enough that it never catches a clean highlight.</summary>
    public static SurfaceFinish WeatheredMetal { get; } = new(0.74f, 1f);

    /// <summary>Whether this is a usable finish — finite, and both components in range. Checked once
    /// by the packer rather than trusted, for the reason
    /// <c>PrimitiveInstancePacker.DecomposeOf</c> records for its own inputs: a silently-wrong
    /// shading parameter has no diagnostic signal at all, it just looks slightly off.</summary>
    public bool IsValid =>
        float.IsFinite(Roughness) && float.IsFinite(Metallic)
        && Roughness is >= 0f and <= 1f
        && Metallic is >= 0f and <= 1f;
}
