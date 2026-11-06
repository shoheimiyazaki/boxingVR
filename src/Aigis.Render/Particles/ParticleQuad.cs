using Silk.NET.Vulkan;

namespace Aigis.Render;

/// <summary>
/// The handful of particle-pipeline facts <see cref="HeadlessRenderer"/> and
/// <see cref="WindowedRenderer"/> must agree on exactly: the shared billboard mesh and the two
/// blend states. Both renderers build their own <c>VkPipeline</c> objects (human-play.md
/// principle 3 — never share Vulkan handles), but a silent disagreement about, say, which blend
/// factors "additive" means would show up only as a game looking subtly different in a window
/// than in the screenshots an agent verified it from. That failure is invisible to every test we
/// have, so the values live in one place instead.
/// </summary>
internal static class ParticleQuad
{
    /// <summary>Two triangles — no index buffer for six vertices.</summary>
    internal const uint VertexCount = 6;

    /// <summary>One <c>vec2</c> corner per vertex.</summary>
    internal const uint StrideBytes = 2 * sizeof(float);

    /// <summary>Growth floor for the per-instance buffer, so a scene that emits a few sparks at
    /// a time does not reallocate on nearly every burst.</summary>
    internal const int InstanceMinimumCapacity = 256;

    /// <summary>A unit quad over [-0.5,0.5]², the domain
    /// <see cref="ShaderSource.ParticleFrag"/>'s soft-disc falloff is written against. Winding is
    /// irrelevant here (the pipelines disable culling) but kept counter-clockwise-as-read to
    /// match the overlay quad.</summary>
    internal static readonly float[] Vertices =
    [
        -0.5f, -0.5f, 0.5f, -0.5f, 0.5f, 0.5f,
        -0.5f, -0.5f, 0.5f, 0.5f, -0.5f, 0.5f,
    ];

    /// <summary>
    /// The colour-blend attachment state for each <see cref="ParticleBlend"/> mode.
    /// <para>
    /// Both keep the destination alpha at 1 rather than accumulating source alpha into it: the
    /// headless colour target is read straight back into a PNG, and a semi-transparent frame
    /// would make every review image composite against whatever the viewer's background happens
    /// to be — the kind of "looks broken but isn't" that costs a whole debugging round.
    /// </para>
    /// </summary>
    internal static PipelineColorBlendAttachmentState BlendState(ParticleBlend blend) => new()
    {
        ColorWriteMask = ColorComponentFlags.RBit | ColorComponentFlags.GBit | ColorComponentFlags.BBit | ColorComponentFlags.ABit,
        BlendEnable = true,
        SrcColorBlendFactor = BlendFactor.SrcAlpha,
        DstColorBlendFactor = blend == ParticleBlend.Additive ? BlendFactor.One : BlendFactor.OneMinusSrcAlpha,
        ColorBlendOp = BlendOp.Add,
        SrcAlphaBlendFactor = BlendFactor.Zero,
        DstAlphaBlendFactor = BlendFactor.One,
        AlphaBlendOp = BlendOp.Add,
    };
}
