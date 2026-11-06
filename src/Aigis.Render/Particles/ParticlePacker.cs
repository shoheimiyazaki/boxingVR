using System.Numerics;

namespace Aigis.Render;

/// <summary>
/// Turns a frame's <see cref="ParticleInstance"/> list into the exact per-instance vertex data
/// the particle pipelines consume: alpha-blended particles first (sorted far-to-near), additive
/// ones after, degenerate ones dropped.
/// <para>
/// This is the whole CPU side of particle rendering, and it lives here — outside either
/// renderer — for two reasons. It is identical work for <see cref="HeadlessRenderer"/> and
/// <see cref="WindowedRenderer"/> (docs/plans/human-play.md principle 3: share the
/// side-effect-free logic, never the Vulkan objects), and it is the part that can actually be
/// wrong in a way a screenshot hides — sort order and the billboard basis are asserted by plain
/// unit tests here instead of needing a GPU.
/// </para>
/// <para>
/// An instance of this is owned per renderer and reused every frame: its buffers only ever grow,
/// so a steady particle count settles into zero allocations per frame.
/// </para>
/// </summary>
public sealed class ParticlePacker
{
    /// <summary>Floats per instance: position(3) + size(1) + colour(4) + rotation(1). Must stay
    /// in lockstep with the vertex attribute layout in both renderers' particle pipelines and
    /// with <see cref="ShaderSource.ParticleVert"/>'s inputs.</summary>
    public const int FloatsPerInstance = 9;

    private float[] _buffer = [];
    private float[] _alphaDepths = [];
    private int[] _alphaIndices = [];
    private int[] _additiveIndices = [];

    /// <summary>Alpha-blended instances, at the front of <see cref="Data"/>, sorted far-to-near.</summary>
    public int AlphaCount { get; private set; }

    /// <summary>Additive instances, immediately after the alpha ones. Left in submission order:
    /// addition is commutative, so unlike alpha blending they need no sorting.</summary>
    public int AdditiveCount { get; private set; }

    /// <summary>Instances actually written (excludes the ones dropped as invisible).</summary>
    public int TotalCount => AlphaCount + AdditiveCount;

    /// <summary>The packed vertex data for exactly <see cref="TotalCount"/> instances. A span,
    /// not the array, so the reused backing store cannot be captured or resized by a caller.</summary>
    public ReadOnlySpan<float> Data => _buffer.AsSpan(0, TotalCount * FloatsPerInstance);

    /// <summary>
    /// The camera's world-space right and up axes, recovered from the very
    /// <paramref name="viewProjection"/> the frame is rendered with — the two vectors a
    /// billboard is spanned by.
    /// <para>
    /// Derived by inverting rather than by asking the caller for a camera: the engine's render
    /// entry points take only a matrix (<see cref="HeadlessRenderer.RenderPrimitiveScene"/>), and
    /// a separately-supplied camera basis could silently disagree with the matrix actually used —
    /// billboards would then face slightly away from the viewer with nothing to catch it. For a
    /// row-vector <c>V*P</c>, <c>inverse(V*P) = inverse(P)*inverse(V)</c>, and since
    /// <c>inverse(P)</c>'s first two rows are diagonal, rows 0 and 1 of the inverse are the
    /// camera's right and up axes scaled by a positive-then-negative factor. The negation on
    /// <c>Up</c> undoes Vulkan's Y-down clip flip (<see cref="Camera"/>), so the returned
    /// <c>Up</c> is the axis that points up <i>on screen</i>.
    /// </para>
    /// </summary>
    /// <returns>Orthonormal-ish (unit length, mutually perpendicular for any camera this engine
    /// builds) screen right/up in world space. Falls back to world X/Y for a non-invertible or
    /// degenerate matrix rather than producing NaN billboards.</returns>
    public static (Vector3 Right, Vector3 Up) CameraBasis(Matrix4x4 viewProjection)
    {
        if (!Matrix4x4.Invert(viewProjection, out var inverse))
        {
            return (Vector3.UnitX, Vector3.UnitY);
        }

        var right = new Vector3(inverse.M11, inverse.M12, inverse.M13);
        var up = -new Vector3(inverse.M21, inverse.M22, inverse.M23);
        if (right.LengthSquared() < 1e-20f || up.LengthSquared() < 1e-20f)
        {
            return (Vector3.UnitX, Vector3.UnitY);
        }

        return (Vector3.Normalize(right), Vector3.Normalize(up));
    }

    /// <summary>
    /// Packs <paramref name="particles"/> for this frame. Overwrites the previous frame's
    /// contents; nothing is retained between calls except the buffers themselves.
    /// </summary>
    /// <param name="particles">This frame's billboards, in any order.</param>
    /// <param name="viewProjection">The matrix this frame is rendered with — used only to depth-
    /// sort the alpha-blended subset, so it must be the same one passed to the draw call.</param>
    public void Pack(IReadOnlyList<ParticleInstance> particles, Matrix4x4 viewProjection)
    {
        ArgumentNullException.ThrowIfNull(particles);
        AlphaCount = 0;
        AdditiveCount = 0;
        if (particles.Count == 0)
        {
            return;
        }

        EnsureCapacity(particles.Count);

        var alpha = 0;
        var additive = 0;
        for (var i = 0; i < particles.Count; i++)
        {
            var particle = particles[i];
            if (!IsVisible(particle))
            {
                continue;
            }

            if (particle.Blend == ParticleBlend.Additive)
            {
                _additiveIndices[additive++] = i;
                continue;
            }

            _alphaDepths[alpha] = Depth(particle.Position, viewProjection);
            _alphaIndices[alpha] = i;
            alpha++;
        }

        // Ascending by depth (near first), then written out in reverse — the painter's-algorithm
        // order alpha blending needs. Sorting the key array alongside the index array avoids both
        // a comparer allocation and a sort over the (much larger) instance data itself.
        Array.Sort(_alphaDepths, _alphaIndices, 0, alpha);

        var write = 0;
        for (var i = alpha - 1; i >= 0; i--)
        {
            write = WriteInstance(_buffer, write, particles[_alphaIndices[i]]);
        }

        for (var i = 0; i < additive; i++)
        {
            write = WriteInstance(_buffer, write, particles[_additiveIndices[i]]);
        }

        AlphaCount = alpha;
        AdditiveCount = additive;
    }

    /// <summary>
    /// Post-perspective-divide depth: works for both the perspective and the orthographic
    /// cameras this engine builds, where a raw view-space <c>w</c> would be constant under
    /// orthographic projection and sort nothing.
    /// <para>
    /// Under perspective, a particle behind the eye has <c>w &lt;= 0</c> and is forced to sort as
    /// farthest — without that, dividing by a negative <c>w</c> flips it to the FRONT of the
    /// queue, where it would blend over the whole scene before the GPU clips it. Under
    /// orthographic projection <c>w</c> is always 1, so that branch never fires and a
    /// behind-the-camera particle sorts by its (negative) NDC z as if it were nearest. Harmless —
    /// the GPU clips <c>z &lt; 0</c> regardless, so it is never blended — but do not read this
    /// method as guaranteeing a behind-the-eye ordering for every projection.
    /// </para>
    /// </summary>
    private static float Depth(Vector3 position, Matrix4x4 viewProjection)
    {
        var clip = Vector4.Transform(new Vector4(position, 1f), viewProjection);
        return clip.W > 0f ? clip.Z / clip.W : float.MaxValue;
    }

    /// <summary>A particle with no size, no opacity, or a non-finite coordinate contributes
    /// nothing but a draw call and (for NaN) undefined rasterisation, so it never reaches the
    /// GPU. Fading to zero is the normal end of every particle's life, not an error.</summary>
    private static bool IsVisible(in ParticleInstance particle) =>
        particle.Size > 0f &&
        particle.Color.W > 0f &&
        float.IsFinite(particle.Size) &&
        float.IsFinite(particle.Rotation) &&
        float.IsFinite(particle.Position.X) && float.IsFinite(particle.Position.Y) && float.IsFinite(particle.Position.Z);

    private static int WriteInstance(float[] buffer, int write, in ParticleInstance particle)
    {
        buffer[write++] = particle.Position.X;
        buffer[write++] = particle.Position.Y;
        buffer[write++] = particle.Position.Z;
        buffer[write++] = particle.Size;
        buffer[write++] = particle.Color.X;
        buffer[write++] = particle.Color.Y;
        buffer[write++] = particle.Color.Z;
        buffer[write++] = particle.Color.W;
        buffer[write++] = particle.Rotation;
        return write;
    }

    private void EnsureCapacity(int instances)
    {
        if (_alphaIndices.Length >= instances)
        {
            return;
        }

        _buffer = new float[instances * FloatsPerInstance];
        _alphaDepths = new float[instances];
        _alphaIndices = new int[instances];
        _additiveIndices = new int[instances];
    }
}
