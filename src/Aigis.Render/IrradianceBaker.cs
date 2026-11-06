using System.Numerics;

namespace Aigis.Render;

/// <summary>
/// How hard <see cref="IrradianceBaker"/> works. Every default is tuned for the offline trailer
/// path (<c>tools/Aigis.HighlightVideo</c>), where a bake happens once per shot and a second of
/// CPU is free; a caller that wants a preview can turn all of them down.
/// </summary>
/// <param name="ProbeSpacingMetres">Distance between probes. The cost of the whole bake is cubic
/// in this, and the quality it buys is NOT — L1 irradiance is a very low-frequency signal, so
/// halving the spacing mostly buys sharper light-leak boundaries, not better light. 0.75 m over a
/// Triage ward is ~15k probes.</param>
/// <param name="RaysPerProbe">Directions sampled per probe. Projecting onto L1 is itself a heavy
/// low-pass, so the noise floor falls fast: 32 is visibly blotchy, 128 is clean.</param>
/// <param name="Bounces">1 = surfaces in the environment are lit directly (plus the flat ambient
/// they sit in); 2 = they are additionally lit by the first bounce's own field, which is where
/// colour bleed comes from. Above 2 the change is not visible on a Triage ward.</param>
/// <param name="ExpandMetres">How far past the requested bounds to place probes horizontally. The
/// bounds handed in are usually a level's shadow box, which stops at its walls — without a margin
/// the outward-facing side of every perimeter wall would sit in the fade band.</param>
/// <param name="ExpandUpMetres">The same margin upward. Cameras in this engine look down at rooms
/// far more often than up, so the ceiling needs less headroom than the walls need width.</param>
/// <param name="FadeMetres">Passed through to <see cref="IrradianceVolume.FadeMetres"/>.</param>
/// <param name="NormalBiasMetres">Passed through to <see cref="IrradianceVolume.NormalBiasMetres"/>.
/// <see langword="null"/> derives it as half the probe spacing, which is the usual choice.</param>
/// <param name="MaxRayMetres">How far a probe ray looks for geometry before giving up and calling
/// it sky. Bounded because the alternative is paying for the backdrop: this engine's outdoor
/// levels put a two-kilometre ground slab and a painted skyline in the same instance list as the
/// room, and a ray that walks to them costs a great deal to learn something the sky term already
/// says.</param>
/// <param name="ShadowPointLights">Whether a bounce surface's point-light illumination is
/// occlusion-tested. On is more correct (a ceiling fixture stops lighting the floor of the room
/// below it); off is meaningfully faster on a level with many fixtures.</param>
/// <param name="IndirectIntensity">
/// Scales what SURFACES contribute, leaving the sky alone. 1 is the neutral, self-consistent
/// answer; below 1 keeps the occlusion (an indoor floor stops receiving open sky) while easing off
/// the bounce.
/// <para>
/// <b>It exists because Triage's ward is white, and that is not a detail.</b> The engine's shading
/// convention makes a surface's outgoing radiance <c>albedo * lit</c>, so a room of 0.8-0.9 albedo
/// walls under sixteen fixtures interreflects almost losslessly — physically correct, and close to
/// the classic white-furnace case where the bounce approaches the direct light itself. Measured on
/// a recorded Level4 ward at 1.0: an indoor surface's ambient term came back roughly 2.5x the flat
/// one it replaced, which brightened the room and, worse, filled in the very sun and fixture
/// shadows the picture is read by. The level's direct lighting was authored with no indirect term
/// at all, so it already carries that light; this is the knob that stops it being counted twice.
/// </para>
/// </param>
/// <param name="AmbientFloorFraction">Optional authored fill under heavy occlusion. Zero keeps the
/// traced field untouched. One lifts only probes whose SH reconstruction would fall below the
/// scene's flat hemisphere ambient for some normal. The ceiling still blocks traced sky rays; this
/// is a deliberate nonphysical readability floor applied after all bounces.</param>
public readonly record struct IrradianceBakeSettings(
    float ProbeSpacingMetres = 0.75f,
    int RaysPerProbe = 128,
    int Bounces = 2,
    float ExpandMetres = 4f,
    float ExpandUpMetres = 3f,
    float FadeMetres = 1.5f,
    float? NormalBiasMetres = null,
    float MaxRayMetres = 60f,
    bool ShadowPointLights = true,
    float IndirectIntensity = 1f,
    float AmbientFloorFraction = 0f)
{
    /// <summary>
    /// The shipped defaults — <b>and the only correct way to spell them</b>.
    /// <para>
    /// <c>new IrradianceBakeSettings()</c> does NOT produce this. A record struct's primary
    /// constructor never becomes its parameterless constructor, however many of its parameters
    /// have defaults, so the zero-argument form yields the all-zero struct instead: spacing 0,
    /// zero rays, zero bounces. The declared defaults above apply only when at least one argument
    /// is named (<c>new IrradianceBakeSettings(RaysPerProbe: 64)</c>), which is why this property
    /// is itself constructed with one. <see cref="IrradianceBaker.Bake"/> maps the all-zero struct
    /// onto this so the trap is a no-op for callers rather than a validation error, but a caller
    /// building settings by hand should start from here.
    /// </para>
    /// </summary>
    public static IrradianceBakeSettings Default { get; } = new(ProbeSpacingMetres: 0.75f);

    /// <summary>A quick, visibly noisier bake — for a preview or a test that only needs the shape
    /// of the answer. About twenty times cheaper than <see cref="Default"/>.</summary>
    public static IrradianceBakeSettings Fast { get; } = new(
        ProbeSpacingMetres: 1.5f,
        RaysPerProbe: 32,
        Bounces: 1,
        ShadowPointLights: false);

    internal void Validate()
    {
        if (!float.IsFinite(ProbeSpacingMetres) || ProbeSpacingMetres <= 0f)
        {
            throw new ArgumentOutOfRangeException(nameof(ProbeSpacingMetres), ProbeSpacingMetres, "Probe spacing must be finite and positive.");
        }

        ArgumentOutOfRangeException.ThrowIfLessThan(RaysPerProbe, 1, nameof(RaysPerProbe));
        ArgumentOutOfRangeException.ThrowIfLessThan(Bounces, 1, nameof(Bounces));

        if (!float.IsFinite(MaxRayMetres) || MaxRayMetres <= 0f)
        {
            throw new ArgumentOutOfRangeException(nameof(MaxRayMetres), MaxRayMetres, "Maximum ray length must be finite and positive.");
        }

        if (!float.IsFinite(ExpandMetres) || ExpandMetres < 0f || !float.IsFinite(ExpandUpMetres) || ExpandUpMetres < 0f)
        {
            throw new ArgumentOutOfRangeException(nameof(ExpandMetres), "Volume margins must be finite and non-negative.");
        }

        if (!float.IsFinite(IndirectIntensity) || IndirectIntensity < 0f)
        {
            throw new ArgumentOutOfRangeException(nameof(IndirectIntensity), IndirectIntensity, "Indirect intensity must be finite and non-negative.");
        }

        if (!float.IsFinite(AmbientFloorFraction) || AmbientFloorFraction < 0f || AmbientFloorFraction > 1f)
        {
            throw new ArgumentOutOfRangeException(nameof(AmbientFloorFraction), AmbientFloorFraction, "Ambient floor fraction must be finite and between zero and one.");
        }
    }
}

/// <summary>
/// Ray-traces a scene's static geometry into an <see cref="IrradianceVolume"/> (TASK-245).
///
/// <para>
/// <b>It mirrors the shader rather than modelling physics.</b> The radiance a bounce ray collects
/// at a surface is literally what <see cref="ShaderSource.PrimitiveFrag"/> would paint there —
/// the same hemisphere ambient, the same <c>ndotl</c> sun term with no 1/π, the same
/// <c>1/(1+(d/range)^2)</c> point falloff, the same cone <c>smoothstep</c>. That is a deliberate
/// choice over "more correct": the whole value of this feature is that surfaces pick up the colour
/// of the surfaces beside them AS THE ENGINE DRAWS THEM. A radiometrically pure baker would
/// disagree with every pixel around it.
/// </para>
///
/// <para>
/// <b>Boxes, not meshes.</b> Every occluder is its instance's world AABB
/// (<see cref="PrimitiveInstance.WorldBounds"/>), so a sphere occludes as its bounding cube. At
/// 0.75 m probe spacing, projected onto four coefficients, the difference between a capsule and its
/// box is far below what the volume can represent — and it keeps the tracer to one slab test.
/// </para>
///
/// <para>
/// <b>Deterministic, with no random numbers anywhere.</b> Directions come from a spherical
/// Fibonacci lattice rotated per probe by golden-ratio angles, so the same inputs always produce
/// the same volume and a bake can be pinned by a test. The probe loop is parallel, but each probe
/// writes only its own slots and reads only shared immutable state, so the result does not depend
/// on scheduling either.
/// </para>
///
/// <para>
/// <b>What is deliberately not modelled.</b> Specular/mirror transport (L1 is diffuse-only),
/// participating media, and anything that moves — see <see cref="IrradianceVolume"/>'s remarks on
/// why a static field is the design and not a limitation for this path.
/// </para>
/// </summary>
public static class IrradianceBaker
{
    /// <summary>Below this contribution a point/spot light is skipped outright, shadow ray and
    /// all. With 16 fixtures on a Triage ward most of them are across the building from any given
    /// bounce surface, and their <c>1/(1+(d/range)^2)</c> term has already made them invisible long
    /// before the tracer gets to pay for occluding them.</summary>
    private const float LightCullThreshold = 0.002f;

    /// <summary>How far off a surface a shadow/bounce ray starts, in metres. Large enough that a
    /// box's own face cannot re-hit itself through floating-point error, small enough not to step
    /// through a thin wall.</summary>
    private const float RayEpsilon = 1e-3f;

    /// <summary>
    /// Bakes <paramref name="bounds"/> (a level's shadow box is the natural argument — it is the
    /// same "this is where the level is" answer, already computed) into a volume lit by
    /// <paramref name="lighting"/> and occluded by <paramref name="instances"/>.
    /// </summary>
    /// <param name="bounds">The region to cover, before <see cref="IrradianceBakeSettings.ExpandMetres"/>.</param>
    /// <param name="instances">The scene's STATIC geometry. Instances that do not intersect the
    /// expanded volume are dropped, which is what keeps a level's two-kilometre backdrop out of
    /// the tracer; <see cref="MaterialKind.OutlineShell"/> is dropped as well (it is a UI
    /// affordance, not a surface).</param>
    /// <param name="lighting">The scene's lighting. Its <see cref="SceneLighting.Ambient"/> is the
    /// sky every ray that escapes the geometry sees, so a bake inherits the level's own time of
    /// day. <see cref="SceneLighting.Irradiance"/> is ignored — this method produces that field,
    /// it does not consume it.</param>
    /// <param name="settings">Quality/cost knobs; see <see cref="IrradianceBakeSettings"/>.</param>
    public static IrradianceVolume Bake(
        ShadowMapConfig bounds,
        IReadOnlyList<PrimitiveInstance> instances,
        SceneLighting lighting,
        IrradianceBakeSettings settings = default)
    {
        ArgumentNullException.ThrowIfNull(instances);
        if (settings == default)
        {
            settings = IrradianceBakeSettings.Default;
        }

        settings.Validate();
        lighting.Validate();

        var margin = new Vector3(settings.ExpandMetres, 0f, settings.ExpandMetres);
        var min = Vector3.Min(bounds.AabbMin, bounds.AabbMax) - margin;
        var max = Vector3.Max(bounds.AabbMin, bounds.AabbMax) + margin + new Vector3(0f, settings.ExpandUpMetres, 0f);
        var spacing = settings.ProbeSpacingMetres;
        var countX = CountFor(max.X - min.X, spacing);
        var countY = CountFor(max.Y - min.Y, spacing);
        var countZ = CountFor(max.Z - min.Z, spacing);

        var scene = OccluderScene.Build(instances, min, max, spacing);
        var directions = FibonacciSphere(settings.RaysPerProbe);

        var probeCount = countX * countY * countZ;
        var red = new Half[probeCount * IrradianceVolume.ComponentsPerProbe];
        var green = new Half[probeCount * IrradianceVolume.ComponentsPerProbe];
        var blue = new Half[probeCount * IrradianceVolume.ComponentsPerProbe];
        var bias = settings.NormalBiasMetres ?? (spacing * 0.5f);
        var volume = new IrradianceVolume(min, new Vector3(spacing), countX, countY, countZ, red, green, blue, bias, settings.FadeMetres);

        // A probe buried in a wall or under the floor sees nothing and would bleed that nothing
        // onto every surface whose trilinear neighbourhood touches it. Found once, before any
        // tracing, and repaired once, after: see PatchInteriorProbes.
        var interior = new bool[probeCount];

        // `previous` is null on the first bounce, so a bounce surface is lit by the flat ambient it
        // has always sat in; from the second bounce on it is lit by the field the previous bounce
        // produced, which is the entire mechanism by which a green mat tints the wall beside it.
        IrradianceVolume? previous = null;
        for (var bounce = 0; bounce < settings.Bounces; bounce++)
        {
            var gather = previous;
            Parallel.For(0, probeCount, index =>
            {
                var z = index / (countX * countY);
                var remainder = index - (z * countX * countY);
                var y = remainder / countX;
                var x = remainder - (y * countX);
                var position = min + new Vector3(x * spacing, y * spacing, z * spacing);

                if (bounce == 0)
                {
                    interior[index] = scene.Contains(position);
                }

                Integrate(scene, lighting, settings, directions, position, index, gather, red, green, blue);
            });

            previous = new IrradianceVolume(min, new Vector3(spacing), countX, countY, countZ,
                (Half[])red.Clone(), (Half[])green.Clone(), (Half[])blue.Clone(), bias, settings.FadeMetres);
        }

        PatchInteriorProbes(volume, interior, red, green, blue);
        if (settings.AmbientFloorFraction > 0f)
        {
            ApplyAmbientFloor(lighting.Ambient, settings.AmbientFloorFraction, red, green, blue);
        }

        return volume;
    }

    /// <summary>
    /// Raise only the SH constant term of a probe enough to retain an authored hemisphere fill
    /// for every unit normal. For a linear field L0 + L1·n, the worst direction of the
    /// difference from the target field is the negative normalized L1 difference; its magnitude
    /// gives the analytic lift before half-float storage rounding. Bright bounced light and its direction are preserved.
    /// </summary>
    private static void ApplyAmbientFloor(
        AmbientLight ambient, float fraction, Half[] red, Half[] green, Half[] blue)
    {
        var sky = ambient.Color * (ambient.Intensity * fraction);
        var ground = ambient.GroundColor * (ambient.Intensity * fraction);
        var targetL0 = (sky + ground) * 0.5f;
        var targetL1Y = (sky - ground) * 0.5f;

        Lift(red, targetL0.X, targetL1Y.X);
        Lift(green, targetL0.Y, targetL1Y.Y);
        Lift(blue, targetL0.Z, targetL1Y.Z);

        static void Lift(Half[] channel, float floorL0, float floorL1Y)
        {
            for (var at = 0; at < channel.Length; at += IrradianceVolume.ComponentsPerProbe)
            {
                var l1x = (float)channel[at];
                var l1y = (float)channel[at + 1] - floorL1Y;
                var l1z = (float)channel[at + 2];
                var l0 = (float)channel[at + 3];
                var lift = MathF.Max(0f, floorL0 - l0 + MathF.Sqrt((l1x * l1x) + (l1y * l1y) + (l1z * l1z)));
                channel[at + 3] = (Half)(l0 + lift);
            }
        }
    }
    /// <summary>Probes along one axis: enough that the last one lands on or past the far face, and
    /// never fewer than two (a single-probe axis would make the volume a constant along it, which
    /// is a silently wrong answer rather than a coarse one).</summary>
    private static int CountFor(float extent, float spacing) =>
        Math.Max(2, (int)MathF.Ceiling(MathF.Max(extent, 0f) / spacing) + 1);

    /// <summary>
    /// One probe: fire every direction, ask what it sees, project the answers onto L0/L1.
    /// <para>
    /// The projection weights are the whole trick, so they are worth stating explicitly. For
    /// directions uniform on the sphere the least-squares linear fit of the sampled radiance field
    /// is <c>L(omega) ~ a0 + a1 . omega</c> with <c>a0 = mean(L)</c> and
    /// <c>a1 = 3 * mean(L * omega)</c>, and those two are exactly what is stored — so the shader
    /// reconstructs with one dot product and no scaling.
    /// </para>
    /// <para>
    /// <b>The fit is evaluated in the normal direction, NOT convolved with a cosine lobe, and that
    /// is a deliberate break with radiometry.</b> The physically-correct diffuse response would be
    /// <c>a0 + (2/3) a1 . n</c>, i.e. a directional term a third weaker. But the term this replaces
    /// is <c>HemisphereAmbient</c>, which reads the sky colour straight off the normal
    /// (<c>mix(ground, sky, n.y*0.5+0.5)</c>) with no cosine anywhere — so the (2/3) would have made
    /// every unoccluded surface in the engine change brightness the moment a volume was attached,
    /// upward-facing ones darker and downward-facing ones lighter, with no occlusion involved at
    /// all (measured: 0.062 against a 0.333 value). Matching the convention that is already on
    /// screen is worth more here than matching the textbook, and it is what makes
    /// <see cref="IrradianceVolume.Uniform"/> an exact identity rather than an approximation.
    /// </para>
    /// </summary>
    private static void Integrate(
        OccluderScene scene,
        SceneLighting lighting,
        IrradianceBakeSettings settings,
        ReadOnlySpan<Vector3> directions,
        Vector3 position,
        int probeIndex,
        IrradianceVolume? gather,
        Half[] red,
        Half[] green,
        Half[] blue)
    {
        var rotation = ProbeRotation(probeIndex);
        var l0 = Vector3.Zero;
        var l1X = Vector3.Zero;
        var l1Y = Vector3.Zero;
        var l1Z = Vector3.Zero;

        foreach (var baseDirection in directions)
        {
            var direction = Vector3.Normalize(Vector3.Transform(baseDirection, rotation));
            // The intensity scales SURFACES only. A ray that escapes returns sky radiance at full
            // strength, so turning the bounce down still leaves the occlusion -- which is the half
            // of this feature that darkens a room rather than brightening it.
            var radiance = scene.Trace(position, direction, settings.MaxRayMetres, out var hit)
                ? SurfaceRadiance(scene, lighting, settings, hit, gather) * settings.IndirectIntensity
                : SkyRadiance(lighting.Ambient, direction);

            l0 += radiance;
            l1X += radiance * direction.X;
            l1Y += radiance * direction.Y;
            l1Z += radiance * direction.Z;
        }

        var inverse = 1f / directions.Length;
        var directional = 3f * inverse;
        Store(red, probeIndex, l1X.X * directional, l1Y.X * directional, l1Z.X * directional, l0.X * inverse);
        Store(green, probeIndex, l1X.Y * directional, l1Y.Y * directional, l1Z.Y * directional, l0.Y * inverse);
        Store(blue, probeIndex, l1X.Z * directional, l1Y.Z * directional, l1Z.Z * directional, l0.Z * inverse);
    }

    private static void Store(Half[] channel, int probeIndex, float l1X, float l1Y, float l1Z, float l0)
    {
        var at = probeIndex * IrradianceVolume.ComponentsPerProbe;
        channel[at] = (Half)l1X;
        channel[at + 1] = (Half)l1Y;
        channel[at + 2] = (Half)l1Z;
        channel[at + 3] = (Half)l0;
    }

    /// <summary>What a ray that escaped every occluder sees — <c>HemisphereAmbient</c>'s own
    /// formula, evaluated for the ray's direction rather than a surface normal. Using the same
    /// expression is what makes an unoccluded probe reproduce the term this feature replaces.</summary>
    private static Vector3 SkyRadiance(AmbientLight ambient, Vector3 direction)
    {
        var t = (direction.Y * 0.5f) + 0.5f;
        return Vector3.Lerp(ambient.GroundColor, ambient.Color, t) * ambient.Intensity;
    }

    /// <summary>
    /// The radiance leaving <paramref name="hit"/> toward the probe: exactly what
    /// <see cref="ShaderSource.PrimitiveFrag"/> would compute for that point, evaluated on the CPU.
    /// </summary>
    private static Vector3 SurfaceRadiance(
        OccluderScene scene,
        SceneLighting lighting,
        IrradianceBakeSettings settings,
        in RayHit hit,
        IrradianceVolume? gather)
    {
        // MaterialKind.Emissive is painted by the shader as `vColor.rgb * EmissiveGain` and takes
        // no light at all, so it is an EMITTER here rather than a reflector. The gain is
        // deliberately NOT applied: it is a tone-curve trick that pushes a lamp lens past 1.0 so
        // the curve leaves it near-white, not a radiometric quantity, and feeding it back as bounce
        // light would make every painted backdrop in the level a floodlight.
        if (hit.Emissive)
        {
            return hit.Albedo;
        }

        var point = hit.Point + (hit.Normal * RayEpsilon);

        // The FIRST bounce lights this surface with direct light only — deliberately NOT with the
        // scene's flat ambient. That ambient is precisely the fill this whole feature exists to
        // replace, and starting the iteration from it double-counts: measured on a recorded Level4
        // ward it made an indoor wall's ambient term 3.2x the flat one, i.e. it would have made the
        // very "too bright" complaint that started this work worse rather than better. From the
        // second bounce on the fill is the previous bounce's own field — computed, occluded, and
        // carrying the colour of the surfaces it came from.
        var ambient = gather is { } field ? field.Sample(point, hit.Normal) : Vector3.Zero;

        var lit = ambient
            + SunContribution(scene, lighting, settings, point, hit.Normal)
            + PointContribution(scene, lighting, settings, point, hit.Normal)
            + SpotContribution(scene, lighting, settings, point, hit.Normal);

        return hit.Albedo * lit;
    }

    private static Vector3 SunContribution(
        OccluderScene scene, SceneLighting lighting, IrradianceBakeSettings settings, Vector3 point, Vector3 normal)
    {
        var sun = lighting.Sun;
        if (sun.Intensity <= 0f)
        {
            return Vector3.Zero;
        }

        var toSun = Vector3.Normalize(sun.Direction);
        var ndotl = Vector3.Dot(normal, toSun);
        if (ndotl <= 0f)
        {
            return Vector3.Zero;
        }

        return scene.Trace(point, toSun, settings.MaxRayMetres, out _)
            ? Vector3.Zero
            : sun.Color * (ndotl * sun.Intensity);
    }

    private static Vector3 PointContribution(
        OccluderScene scene, SceneLighting lighting, IrradianceBakeSettings settings, Vector3 point, Vector3 normal)
    {
        var total = Vector3.Zero;
        foreach (var light in lighting.PointLights)
        {
            var toLight = light.Position - point;
            var distance = toLight.Length();
            if (distance <= 1e-4f)
            {
                continue;
            }

            var direction = toLight / distance;
            var ndotl = Vector3.Dot(normal, direction);
            if (ndotl <= 0f)
            {
                continue;
            }

            var range = MathF.Max(light.Range, 1e-4f);
            var ratio = distance / range;
            var falloff = 1f / (1f + (ratio * ratio));
            var strength = light.Intensity * ndotl * falloff;
            if (strength <= LightCullThreshold)
            {
                continue;
            }

            if (settings.ShadowPointLights && scene.Trace(point, direction, distance, out _))
            {
                continue;
            }

            total += light.Color * strength;
        }

        return total;
    }

    private static Vector3 SpotContribution(
        OccluderScene scene, SceneLighting lighting, IrradianceBakeSettings settings, Vector3 point, Vector3 normal)
    {
        var total = Vector3.Zero;
        foreach (var light in lighting.SpotLights)
        {
            var toLight = light.Position - point;
            var distance = toLight.Length();
            if (distance <= 1e-4f)
            {
                continue;
            }

            var direction = toLight / distance;

            // The cone test uses -direction against the aim axis, because SpotLight.Direction is
            // the way the cone POINTS while `direction` is surface-toward-light — the same sign
            // trap ShaderSource.PrimitiveFrag's SpotLightContribution documents at length.
            var cone = Smoothstep(light.OuterCosine, light.InnerCosine, Vector3.Dot(-direction, Vector3.Normalize(light.Direction)));
            if (cone <= 0f)
            {
                continue;
            }

            var ndotl = Vector3.Dot(normal, direction);
            if (ndotl <= 0f)
            {
                continue;
            }

            var range = MathF.Max(light.Range, 1e-4f);
            var ratio = distance / range;
            var falloff = 1f / (1f + (ratio * ratio));
            var strength = light.Intensity * ndotl * falloff * cone;
            if (strength <= LightCullThreshold)
            {
                continue;
            }

            if (settings.ShadowPointLights && scene.Trace(point, direction, distance, out _))
            {
                continue;
            }

            total += light.Color * strength;
        }

        return total;
    }

    private static float Smoothstep(float edge0, float edge1, float x)
    {
        if (edge1 <= edge0)
        {
            return x >= edge1 ? 1f : 0f;
        }

        var t = Math.Clamp((x - edge0) / (edge1 - edge0), 0f, 1f);
        return t * t * (3f - (2f * t));
    }

    /// <summary>
    /// Replaces every probe that sits inside solid geometry with the average of its valid
    /// neighbours, repeatedly, so the repair spreads inward from the surface of the wall.
    /// <para>
    /// Without this a wall's interior probes hold near-black, and because trilinear filtering mixes
    /// the eight probes around a sample point, HALF the neighbourhood of any surface flush against
    /// that wall is that near-black. The visible result is a dark rind on every wall and floor —
    /// which reads as dirt, not as shadow. The shader's normal bias
    /// (<see cref="IrradianceVolume.NormalBiasMetres"/>) is the other half of the same fix.
    /// </para>
    /// </summary>
    private static void PatchInteriorProbes(IrradianceVolume volume, bool[] interior, Half[] red, Half[] green, Half[] blue)
    {
        var remaining = 0;
        foreach (var flagged in interior)
        {
            if (flagged)
            {
                remaining++;
            }
        }

        if (remaining == 0)
        {
            return;
        }

        // Bounded rather than run-to-convergence: a probe six cells deep inside a solid cannot
        // affect any visible surface, so spending passes to reach it is spending for nothing.
        const int MaxPasses = 4;
        var pending = (bool[])interior.Clone();
        for (var pass = 0; pass < MaxPasses && remaining > 0; pass++)
        {
            var filled = new List<(int Index, float[] Values)>();
            for (var z = 0; z < volume.CountZ; z++)
            {
                for (var y = 0; y < volume.CountY; y++)
                {
                    for (var x = 0; x < volume.CountX; x++)
                    {
                        var index = (((z * volume.CountY) + y) * volume.CountX) + x;
                        if (!pending[index])
                        {
                            continue;
                        }

                        var accumulator = new float[IrradianceVolume.ComponentsPerProbe * 3];
                        var count = 0;
                        AddNeighbour(x - 1, y, z);
                        AddNeighbour(x + 1, y, z);
                        AddNeighbour(x, y - 1, z);
                        AddNeighbour(x, y + 1, z);
                        AddNeighbour(x, y, z - 1);
                        AddNeighbour(x, y, z + 1);
                        if (count == 0)
                        {
                            continue;
                        }

                        for (var i = 0; i < accumulator.Length; i++)
                        {
                            accumulator[i] /= count;
                        }

                        filled.Add((index, accumulator));

                        void AddNeighbour(int nx, int ny, int nz)
                        {
                            if (nx < 0 || ny < 0 || nz < 0 || nx >= volume.CountX || ny >= volume.CountY || nz >= volume.CountZ)
                            {
                                return;
                            }

                            var neighbour = (((nz * volume.CountY) + ny) * volume.CountX) + nx;
                            if (pending[neighbour])
                            {
                                return;
                            }

                            var at = neighbour * IrradianceVolume.ComponentsPerProbe;
                            for (var i = 0; i < IrradianceVolume.ComponentsPerProbe; i++)
                            {
                                accumulator[i] += (float)red[at + i];
                                accumulator[IrradianceVolume.ComponentsPerProbe + i] += (float)green[at + i];
                                accumulator[(IrradianceVolume.ComponentsPerProbe * 2) + i] += (float)blue[at + i];
                            }

                            count++;
                        }
                    }
                }
            }

            if (filled.Count == 0)
            {
                return;
            }

            foreach (var (index, values) in filled)
            {
                var at = index * IrradianceVolume.ComponentsPerProbe;
                for (var i = 0; i < IrradianceVolume.ComponentsPerProbe; i++)
                {
                    red[at + i] = (Half)values[i];
                    green[at + i] = (Half)values[IrradianceVolume.ComponentsPerProbe + i];
                    blue[at + i] = (Half)values[(IrradianceVolume.ComponentsPerProbe * 2) + i];
                }

                pending[index] = false;
                remaining--;
            }
        }
    }

    /// <summary>
    /// <paramref name="count"/> directions spread evenly over the sphere by the spherical
    /// Fibonacci construction — the standard low-discrepancy set, and chosen over random sampling
    /// because a bake has to be reproducible (a golden that depends on an RNG is not a golden).
    /// </summary>
    private static Vector3[] FibonacciSphere(int count)
    {
        const float GoldenAngle = 2.39996323f; // π * (3 - sqrt(5))
        var directions = new Vector3[count];
        for (var i = 0; i < count; i++)
        {
            var y = 1f - (((i + 0.5f) / count) * 2f);
            var radius = MathF.Sqrt(MathF.Max(0f, 1f - (y * y)));
            var theta = GoldenAngle * i;
            directions[i] = new Vector3(MathF.Cos(theta) * radius, y, MathF.Sin(theta) * radius);
        }

        return directions;
    }

    /// <summary>
    /// A per-probe rotation of the shared direction set. Without it every probe samples the world
    /// along the SAME 128 rays, so the visibility term's error is identical everywhere and shows up
    /// as structure — a faint regular pattern across a wall — rather than as noise, which L1
    /// projection would have swallowed. Derived from the probe's index by two golden-ratio angles,
    /// so it is decorrelated between neighbours and still perfectly reproducible.
    /// </summary>
    private static Quaternion ProbeRotation(int probeIndex)
    {
        const float GoldenAngle = 2.39996323f;
        const float SilverAngle = 1.32471795f; // the plastic number, an unrelated irrational
        return Quaternion.CreateFromYawPitchRoll(
            GoldenAngle * probeIndex,
            SilverAngle * probeIndex,
            0f);
    }

    /// <summary>One occluder: a world AABB plus the two things a bounce ray needs from it.</summary>
    private readonly record struct Occluder(Vector3 Min, Vector3 Max, Vector3 Albedo, bool Emissive);

    /// <summary>What a ray found.</summary>
    private readonly record struct RayHit(Vector3 Point, Vector3 Normal, Vector3 Albedo, bool Emissive);

    /// <summary>
    /// The static geometry, in a uniform grid, with a ray query.
    /// <para>
    /// <b>Why a grid at all.</b> A Triage ward contributes ~670 boxes to the volume's region and a
    /// default bake fires a couple of million rays; testing every box against every ray is tens of
    /// billions of slab tests, which is minutes rather than seconds. A uniform grid is the right
    /// structure here rather than a BVH because the geometry IS a grid — a tile map's walls,
    /// floors and furniture are near-uniformly distributed over the level's own footprint, the case
    /// where a BVH's extra sophistication buys nothing.
    /// </para>
    /// </summary>
    private sealed class OccluderScene
    {
        /// <summary>An occluder overlapping more than this many cells is held in a separate list
        /// tested by every ray, instead of being written into each of those cells. Triage's outdoor
        /// levels contain ground slabs a hundred metres across; inserting one of those into forty
        /// thousand cells costs far more than testing it once per ray.</summary>
        private const int LargeOccluderCellLimit = 512;

        private readonly Occluder[] _occluders;
        private readonly int[] _cellStart;
        private readonly int[] _cellItems;
        private readonly int[] _large;
        private readonly Vector3 _gridMin;
        private readonly Vector3 _gridMax;
        private readonly float _cell;
        private readonly int _nx;
        private readonly int _ny;
        private readonly int _nz;

        private OccluderScene(
            Occluder[] occluders, int[] cellStart, int[] cellItems, int[] large,
            Vector3 gridMin, Vector3 gridMax, float cell, int nx, int ny, int nz)
        {
            _occluders = occluders;
            _cellStart = cellStart;
            _cellItems = cellItems;
            _large = large;
            _gridMin = gridMin;
            _gridMax = gridMax;
            _cell = cell;
            _nx = nx;
            _ny = ny;
            _nz = nz;
        }

        public static OccluderScene Build(IReadOnlyList<PrimitiveInstance> instances, Vector3 volumeMin, Vector3 volumeMax, float spacing)
        {
            // The volume's own box, grown by one ray length, is the region worth knowing about: a
            // ray cannot reach further, and everything beyond it is the backdrop the sky term
            // already answers for.
            var reach = new Vector3(spacing * 2f);
            var regionMin = volumeMin - reach;
            var regionMax = volumeMax + reach;

            var kept = new List<Occluder>();
            var gridMin = new Vector3(float.MaxValue);
            var gridMax = new Vector3(float.MinValue);
            foreach (var instance in instances)
            {
                // OutlineShell is a selection highlight drawn as an inverted hull around something
                // else; it is not a surface, and baking it would put a glowing ghost of whatever
                // was selected into the level's permanent lighting.
                if (instance.Material == MaterialKind.OutlineShell ||
                    (instance.Material != MaterialKind.ShadowCastOnly && instance.Color.W < 0.5f))
                {
                    continue;
                }

                var (lo, hi) = instance.WorldBounds();
                if (hi.X < regionMin.X || lo.X > regionMax.X ||
                    hi.Y < regionMin.Y || lo.Y > regionMax.Y ||
                    hi.Z < regionMin.Z || lo.Z > regionMax.Z)
                {
                    continue;
                }

                // ShadowCastOnly is Triage's invisible interior ceiling: it must occlude (that is
                // the entire reason a ward is not lit like a field) but it has no visible colour to
                // bounce -- the instances carry black. So it is a pure occluder, and the room below
                // is lit by its own fixtures and its own floor, not by an invented white ceiling.
                var albedo = instance.Material == MaterialKind.ShadowCastOnly
                    ? Vector3.Zero
                    : new Vector3(instance.Color.X, instance.Color.Y, instance.Color.Z);

                kept.Add(new Occluder(
                    Vector3.Clamp(lo, regionMin, regionMax),
                    Vector3.Clamp(hi, regionMin, regionMax),
                    albedo,
                    instance.Material == MaterialKind.Emissive));
                gridMin = Vector3.Min(gridMin, lo);
                gridMax = Vector3.Max(gridMax, hi);
            }

            if (kept.Count == 0)
            {
                return new OccluderScene([], [0], [], [], Vector3.Zero, Vector3.Zero, 1f, 0, 0, 0);
            }

            gridMin = Vector3.Max(gridMin, regionMin);
            gridMax = Vector3.Min(gridMax, regionMax);
            var size = Vector3.Max(gridMax - gridMin, new Vector3(1e-3f));

            // One cell per two probe spacings: fine enough that a cell holds a handful of boxes on
            // a tile map, coarse enough that the cell array stays small.
            var cell = MathF.Max(spacing * 2f, 0.25f);
            var nx = Math.Clamp((int)MathF.Ceiling(size.X / cell), 1, 512);
            var ny = Math.Clamp((int)MathF.Ceiling(size.Y / cell), 1, 512);
            var nz = Math.Clamp((int)MathF.Ceiling(size.Z / cell), 1, 512);

            var occluders = kept.ToArray();
            var cellCount = nx * ny * nz;
            var counts = new int[cellCount + 1];
            var large = new List<int>();
            var spans = new (int X0, int Y0, int Z0, int X1, int Y1, int Z1)[occluders.Length];

            for (var i = 0; i < occluders.Length; i++)
            {
                var span = SpanOf(occluders[i], gridMin, cell, nx, ny, nz);
                spans[i] = span;
                var cells = (long)(span.X1 - span.X0 + 1) * (span.Y1 - span.Y0 + 1) * (span.Z1 - span.Z0 + 1);
                if (cells > LargeOccluderCellLimit)
                {
                    large.Add(i);
                    spans[i] = (1, 0, 0, 0, 0, 0); // empty span: skipped by the fill loop below
                    continue;
                }

                for (var z = span.Z0; z <= span.Z1; z++)
                {
                    for (var y = span.Y0; y <= span.Y1; y++)
                    {
                        for (var x = span.X0; x <= span.X1; x++)
                        {
                            counts[(((z * ny) + y) * nx) + x + 1]++;
                        }
                    }
                }
            }

            for (var i = 0; i < cellCount; i++)
            {
                counts[i + 1] += counts[i];
            }

            var items = new int[counts[cellCount]];
            var cursor = (int[])counts.Clone();
            for (var i = 0; i < occluders.Length; i++)
            {
                var span = spans[i];
                for (var z = span.Z0; z <= span.Z1; z++)
                {
                    for (var y = span.Y0; y <= span.Y1; y++)
                    {
                        for (var x = span.X0; x <= span.X1; x++)
                        {
                            items[cursor[(((z * ny) + y) * nx) + x]++] = i;
                        }
                    }
                }
            }

            return new OccluderScene(occluders, counts, items, large.ToArray(), gridMin, gridMin + new Vector3(nx * cell, ny * cell, nz * cell), cell, nx, ny, nz);
        }

        private static (int X0, int Y0, int Z0, int X1, int Y1, int Z1) SpanOf(
            in Occluder occluder, Vector3 gridMin, float cell, int nx, int ny, int nz) =>
            (Math.Clamp((int)MathF.Floor((occluder.Min.X - gridMin.X) / cell), 0, nx - 1),
             Math.Clamp((int)MathF.Floor((occluder.Min.Y - gridMin.Y) / cell), 0, ny - 1),
             Math.Clamp((int)MathF.Floor((occluder.Min.Z - gridMin.Z) / cell), 0, nz - 1),
             Math.Clamp((int)MathF.Floor((occluder.Max.X - gridMin.X) / cell), 0, nx - 1),
             Math.Clamp((int)MathF.Floor((occluder.Max.Y - gridMin.Y) / cell), 0, ny - 1),
             Math.Clamp((int)MathF.Floor((occluder.Max.Z - gridMin.Z) / cell), 0, nz - 1));

        /// <summary>Whether <paramref name="point"/> is inside any occluder — how a probe learns it
        /// was placed inside a wall. Shrunk by a millimetre so a probe sitting exactly ON a floor's
        /// top face (which the grid alignment makes common) is not called buried.</summary>
        public bool Contains(Vector3 point)
        {
            foreach (var index in _large)
            {
                if (Inside(_occluders[index], point))
                {
                    return true;
                }
            }

            if (_nx == 0 || !InsideGrid(point))
            {
                return false;
            }

            var cx = Math.Clamp((int)MathF.Floor((point.X - _gridMin.X) / _cell), 0, _nx - 1);
            var cy = Math.Clamp((int)MathF.Floor((point.Y - _gridMin.Y) / _cell), 0, _ny - 1);
            var cz = Math.Clamp((int)MathF.Floor((point.Z - _gridMin.Z) / _cell), 0, _nz - 1);
            var cellIndex = (((cz * _ny) + cy) * _nx) + cx;
            for (var i = _cellStart[cellIndex]; i < _cellStart[cellIndex + 1]; i++)
            {
                if (Inside(_occluders[_cellItems[i]], point))
                {
                    return true;
                }
            }

            return false;
        }

        private static bool Inside(in Occluder occluder, Vector3 point)
        {
            const float Shrink = 1e-3f;
            return point.X > occluder.Min.X + Shrink && point.X < occluder.Max.X - Shrink
                && point.Y > occluder.Min.Y + Shrink && point.Y < occluder.Max.Y - Shrink
                && point.Z > occluder.Min.Z + Shrink && point.Z < occluder.Max.Z - Shrink;
        }

        private bool InsideGrid(Vector3 point) =>
            point.X >= _gridMin.X && point.X <= _gridMax.X &&
            point.Y >= _gridMin.Y && point.Y <= _gridMax.Y &&
            point.Z >= _gridMin.Z && point.Z <= _gridMax.Z;

        /// <summary>
        /// Nearest hit along the ray, or false for a miss. 3D-DDA over the grid (Amanatides &amp;
        /// Woo), with the large-occluder list tested up front.
        /// <para>
        /// The subtle part is when it is safe to stop: a box registered in cell A may be hit at a
        /// distance that lies beyond A, so accepting the first hit found in the first non-empty
        /// cell would return a surface that something else occludes. The loop therefore keeps
        /// walking until the nearest hit so far is closer than the current cell's EXIT distance.
        /// </para>
        /// </summary>
        public bool Trace(Vector3 origin, Vector3 direction, float maxDistance, out RayHit hit)
        {
            hit = default;
            var best = maxDistance;
            var bestIndex = -1;
            var bestNormal = Vector3.Zero;

            foreach (var index in _large)
            {
                if (Intersect(_occluders[index], origin, direction, RayEpsilon, best, out var t, out var normal))
                {
                    best = t;
                    bestIndex = index;
                    bestNormal = normal;
                }
            }

            if (_nx > 0 && !TraceGrid(origin, direction, ref best, ref bestIndex, ref bestNormal))
            {
                // TraceGrid returns false only when the ray never enters the grid at all; any hit
                // already found against a large occluder still stands.
            }

            if (bestIndex < 0)
            {
                return false;
            }

            var occluder = _occluders[bestIndex];
            hit = new RayHit(origin + (direction * best), bestNormal, occluder.Albedo, occluder.Emissive);
            return true;
        }

        private bool TraceGrid(Vector3 origin, Vector3 direction, ref float best, ref int bestIndex, ref Vector3 bestNormal)
        {
            var enter = 0f;
            var exit = best;
            if (!ClipToBox(origin, direction, _gridMin, _gridMax, ref enter, ref exit))
            {
                return false;
            }

            var entry = origin + (direction * MathF.Max(enter, 0f));
            var x = Math.Clamp((int)MathF.Floor((entry.X - _gridMin.X) / _cell), 0, _nx - 1);
            var y = Math.Clamp((int)MathF.Floor((entry.Y - _gridMin.Y) / _cell), 0, _ny - 1);
            var z = Math.Clamp((int)MathF.Floor((entry.Z - _gridMin.Z) / _cell), 0, _nz - 1);

            var stepX = direction.X > 0f ? 1 : -1;
            var stepY = direction.Y > 0f ? 1 : -1;
            var stepZ = direction.Z > 0f ? 1 : -1;
            var tDeltaX = SafeDelta(direction.X);
            var tDeltaY = SafeDelta(direction.Y);
            var tDeltaZ = SafeDelta(direction.Z);
            var tMaxX = NextBoundary(origin.X, direction.X, _gridMin.X, x, stepX);
            var tMaxY = NextBoundary(origin.Y, direction.Y, _gridMin.Y, y, stepY);
            var tMaxZ = NextBoundary(origin.Z, direction.Z, _gridMin.Z, z, stepZ);

            while (true)
            {
                var cellIndex = (((z * _ny) + y) * _nx) + x;
                for (var i = _cellStart[cellIndex]; i < _cellStart[cellIndex + 1]; i++)
                {
                    var candidate = _cellItems[i];
                    if (Intersect(_occluders[candidate], origin, direction, RayEpsilon, best, out var t, out var normal))
                    {
                        best = t;
                        bestIndex = candidate;
                        bestNormal = normal;
                    }
                }

                var cellExit = MathF.Min(tMaxX, MathF.Min(tMaxY, tMaxZ));
                if (bestIndex >= 0 && best <= cellExit)
                {
                    return true;
                }

                if (cellExit >= exit)
                {
                    return true;
                }

                if (tMaxX <= tMaxY && tMaxX <= tMaxZ)
                {
                    x += stepX;
                    if (x < 0 || x >= _nx)
                    {
                        return true;
                    }

                    tMaxX += tDeltaX;
                }
                else if (tMaxY <= tMaxZ)
                {
                    y += stepY;
                    if (y < 0 || y >= _ny)
                    {
                        return true;
                    }

                    tMaxY += tDeltaY;
                }
                else
                {
                    z += stepZ;
                    if (z < 0 || z >= _nz)
                    {
                        return true;
                    }

                    tMaxZ += tDeltaZ;
                }
            }
        }

        private float SafeDelta(float component) =>
            MathF.Abs(component) < 1e-8f ? float.PositiveInfinity : _cell / MathF.Abs(component);

        private float NextBoundary(float origin, float direction, float gridMin, int cell, int step)
        {
            if (MathF.Abs(direction) < 1e-8f)
            {
                return float.PositiveInfinity;
            }

            var boundary = gridMin + ((cell + (step > 0 ? 1 : 0)) * _cell);
            return (boundary - origin) / direction;
        }

        /// <summary>Ray/AABB slab test. Returns the nearest intersection strictly inside
        /// <c>(tMin, tMax)</c> along with the face normal, which is the axis that produced the
        /// entry distance, pointing back along the ray.</summary>
        private static bool Intersect(
            in Occluder occluder, Vector3 origin, Vector3 direction, float tMin, float tMax, out float t, out Vector3 normal)
        {
            t = 0f;
            normal = Vector3.Zero;
            var near = tMin;
            var far = tMax;
            var axis = -1;
            var sign = 0f;

            for (var i = 0; i < 3; i++)
            {
                var d = Component(direction, i);
                var o = Component(origin, i);
                var lo = Component(occluder.Min, i);
                var hi = Component(occluder.Max, i);

                if (MathF.Abs(d) < 1e-8f)
                {
                    if (o < lo || o > hi)
                    {
                        return false;
                    }

                    continue;
                }

                var inverse = 1f / d;
                var t0 = (lo - o) * inverse;
                var t1 = (hi - o) * inverse;
                var enterSign = -1f;
                if (t0 > t1)
                {
                    (t0, t1) = (t1, t0);
                    enterSign = 1f;
                }

                if (t0 > near)
                {
                    near = t0;
                    axis = i;
                    sign = enterSign;
                }

                if (t1 < far)
                {
                    far = t1;
                }

                if (near > far)
                {
                    return false;
                }
            }

            // axis < 0 means the ray started inside this box on every axis it could be tested
            // against. A probe buried in geometry produces exactly that; Contains() is what
            // classifies those probes, and here the box is simply not treated as a hit surface.
            if (axis < 0)
            {
                return false;
            }

            t = near;
            normal = axis switch
            {
                0 => new Vector3(sign, 0f, 0f),
                1 => new Vector3(0f, sign, 0f),
                _ => new Vector3(0f, 0f, sign),
            };
            return true;
        }

        private static bool ClipToBox(Vector3 origin, Vector3 direction, Vector3 min, Vector3 max, ref float enter, ref float exit)
        {
            for (var i = 0; i < 3; i++)
            {
                var d = Component(direction, i);
                var o = Component(origin, i);
                var lo = Component(min, i);
                var hi = Component(max, i);
                if (MathF.Abs(d) < 1e-8f)
                {
                    if (o < lo || o > hi)
                    {
                        return false;
                    }

                    continue;
                }

                var inverse = 1f / d;
                var t0 = (lo - o) * inverse;
                var t1 = (hi - o) * inverse;
                if (t0 > t1)
                {
                    (t0, t1) = (t1, t0);
                }

                enter = MathF.Max(enter, t0);
                exit = MathF.Min(exit, t1);
                if (enter > exit)
                {
                    return false;
                }
            }

            return true;
        }

        private static float Component(Vector3 v, int axis) => axis switch
        {
            0 => v.X,
            1 => v.Y,
            _ => v.Z,
        };
    }
}
