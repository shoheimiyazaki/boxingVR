namespace Aigis.Render;

/// <summary>
/// The screen-space finishing pass that turns a lit frame into a photograph of a miniature
/// (2026-10-05, user request 「光が美しい、ミニチュア感のあるレンダリング手法」). Passed to
/// <c>RenderPrimitiveScene</c>/<c>RenderPrimitiveSceneAndPresent</c>; <see langword="null"/> there
/// draws the frame exactly as before, so no existing golden moves.
/// <para>
/// <b>What sells "miniature".</b> A real tilt-shift photograph keeps one horizontal band sharp and
/// defocuses everything above and below it — the shallow depth of field of a macro lens, which the
/// eye reads as "this is small". The other terms are the light that makes such a photograph pretty:
/// highlights that bleed (<see cref="BloomIntensity"/>), a darkened frame edge
/// (<see cref="Vignette"/>) and a warm, slightly richer grade.
/// </para>
/// <para>
/// <b>Screen-space, not depth-based, on purpose.</b> The gameplay camera looks down at a fixed tilt, so
/// screen height already orders the stage by distance; a vertical band is what tilt-shift lenses
/// physically produce, and it needs no depth texture. The scene, particles and decals are blurred;
/// HUD surfaces drawn afterwards are not.
/// </para>
/// <para>All values are in display (gamma-encoded) space — the same Unorm space both renderers'
/// targets hold, so headless and windowed frames stay identical (hard rule 6).</para>
/// </summary>
/// <param name="FocusCenter">Screen height, 0 = top edge, 1 = bottom edge, of the sharp band's centre.</param>
/// <param name="FocusHalfHeight">Half the sharp band's height, in the same units.</param>
/// <param name="FocusFalloff">Distance beyond the band over which the blur ramps to its maximum.</param>
/// <param name="MaxBlurPixels">Blur disk radius at full defocus, in pixels at a 1080-line target
/// (scaled with the target height so a smaller window is not blurrier).</param>
/// <param name="BloomThreshold">Display-space brightness above which a pixel glows.</param>
/// <param name="BloomIntensity">Strength of the glow added back; 0 disables it.</param>
/// <param name="BloomRadiusPixels">Glow spread, in pixels at a 1080-line target.</param>
/// <param name="Vignette">0 = none; 1 = corners at roughly half brightness.</param>
/// <param name="Saturation">1 = unchanged.</param>
/// <param name="Contrast">1 = unchanged; above 1 steepens the mid-tones.</param>
/// <param name="Warmth">0 = neutral; positive pushes toward late-afternoon amber.</param>
/// <param name="Exposure">Multiplier applied before grading; 1 = unchanged.</param>
/// <param name="AmbientOcclusion">Strength of the screen-space contact shadow in creases and gaps —
/// under a bed, between a cabinet and the wall, where a body meets the floor. 0 = off; 1 = a crease
/// closed on all sides goes to roughly a third of its brightness. Read from the scene's own depth, so
/// it costs no extra geometry pass.</param>
public readonly record struct PostProcessSettings(
    float FocusCenter,
    float FocusHalfHeight,
    float FocusFalloff,
    float MaxBlurPixels,
    float BloomThreshold,
    float BloomIntensity,
    float BloomRadiusPixels,
    float Vignette,
    float Saturation,
    float Contrast,
    float Warmth,
    float Exposure,
    float AmbientOcclusion = 0f)
{
    /// <summary>The golden-hour miniature: a sharp middle band, gently defocused top and bottom,
    /// warm grade, soft highlights.</summary>
    public static PostProcessSettings Miniature { get; } = new(
        FocusCenter: 0.56f,
        FocusHalfHeight: 0.26f,
        FocusFalloff: 0.28f,
        MaxBlurPixels: 6f,
        BloomThreshold: 0.62f,
        BloomIntensity: 0.7f,
        BloomRadiusPixels: 26f,
        Vignette: 0.55f,
        Saturation: 1.12f,
        Contrast: 1.08f,
        Warmth: 0.6f,
        Exposure: 1.04f,
        AmbientOcclusion: 2.0f);

    /// <summary>Validated at the renderer boundary: a NaN here would blank the whole frame with no
    /// error anywhere (these renderers run without validation layers).</summary>
    internal void Validate()
    {
        Span<float> values =
        [
            FocusCenter, FocusHalfHeight, FocusFalloff, MaxBlurPixels, BloomThreshold, BloomIntensity,
            BloomRadiusPixels, Vignette, Saturation, Contrast, Warmth, Exposure, AmbientOcclusion,
        ];
        foreach (var value in values)
        {
            if (!float.IsFinite(value))
            {
                throw new ArgumentException($"{nameof(PostProcessSettings)} has a non-finite value: {this}.");
            }
        }

        if (MaxBlurPixels < 0f || BloomIntensity < 0f || BloomRadiusPixels < 0f || FocusFalloff <= 0f || Exposure <= 0f
            || AmbientOcclusion < 0f)
        {
            throw new ArgumentException(
                $"{nameof(PostProcessSettings)} needs non-negative blur/bloom sizes and a positive falloff and exposure: {this}.");
        }
    }
}
