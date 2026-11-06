using System.Collections.Immutable;

namespace Aigis.Render;

/// <summary>
/// One immutable frame-scene snapshot. A host builds this once after simulation, then may render
/// it through one or more <see cref="CameraView"/> instances without a game exposing renderer
/// resources or duplicating mutable scene lists.
/// </summary>
public sealed record RenderScene
{
    private RenderScene(
        ImmutableArray<PrimitiveInstance> instances,
        ImmutableArray<DebugLine> lines,
        ImmutableArray<ParticleInstance> particles,
        SceneLighting? lighting,
        ShadowMapConfig? shadow)
    {
        Instances = instances;
        Lines = lines;
        Particles = particles;
        Lighting = lighting;
        Shadow = shadow;
    }

    public ImmutableArray<PrimitiveInstance> Instances { get; }
    public ImmutableArray<DebugLine> Lines { get; }
    public ImmutableArray<ParticleInstance> Particles { get; }
    public SceneLighting? Lighting { get; }
    public ShadowMapConfig? Shadow { get; }

    public static RenderScene Create(
        IEnumerable<PrimitiveInstance> instances,
        IEnumerable<DebugLine> lines,
        IEnumerable<ParticleInstance>? particles = null,
        SceneLighting? lighting = null,
        ShadowMapConfig? shadow = null)
    {
        ArgumentNullException.ThrowIfNull(instances);
        ArgumentNullException.ThrowIfNull(lines);
        return new RenderScene(instances.ToImmutableArray(), lines.ToImmutableArray(),
            particles?.ToImmutableArray() ?? [], lighting, shadow);
    }
}
