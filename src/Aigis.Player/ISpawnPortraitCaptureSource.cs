using System.Numerics;
using Aigis.Render;

namespace Aigis.Player;

/// <summary>Optional game-to-host bridge for a one-shot portrait captured after an entity spawns.
/// The game supplies camera intent only; the host owns the renderer and returns an immutable texture.</summary>
public interface ISpawnPortraitCaptureSource
{
    /// <summary>Dequeues the next pending spawn portrait, if any.</summary>
    public bool TryDequeueSpawnPortrait(out SpawnPortraitCaptureRequest request);

    /// <summary>Receives the host-rendered texture for <paramref name="request"/>.</summary>
    public void CompleteSpawnPortrait(SpawnPortraitCaptureRequest request, TextureData texture);

    /// <summary>Records a failed capture so the game can retain its deterministic fallback portrait.</summary>
    public void FailSpawnPortrait(SpawnPortraitCaptureRequest request, Exception failure);

    /// <summary>
    /// The <see cref="SpawnPortraitCaptureRequest.SizePixels"/> this game WILL ask for at a framebuffer
    /// of <paramref name="framebufferSizePixels"/>, or 0 for the host's default. Lets the host build its
    /// portrait renderer at the right size before the first request rather than on the gameplay frame
    /// that request arrives (a renderer is a whole Vulkan device: 0.25-0.6 s measured).
    /// </summary>
    public int PreferredSpawnPortraitSizePixels(Vector2 framebufferSizePixels) => 0;
}

/// <summary>Optional companion to <see cref="ISpawnPortraitCaptureSource"/> for games that can
/// provide subject-only geometry. It keeps a spawn portrait free from nearby level geometry while
/// the host retains ownership of the offscreen renderer and texture lifetime.</summary>
public interface ISpawnPortraitRenderableSource
{
    /// <summary>Builds only the spawned subject's geometry for its portrait capture.</summary>
    public IReadOnlyList<PrimitiveInstance> BuildSpawnPortraitRenderables(int subjectId);
}

/// <summary>Renderer-independent specification of one square spawn portrait capture.</summary>
public readonly record struct SpawnPortraitCaptureRequest(
    int SubjectId,
    Matrix4x4 ViewProjection,
    Vector3 CameraPosition,
    Vector4? ClearColor = null,
    int SizePixels = 0)
{
    /// <summary>The largest square a game may ask for. Not a Vulkan limit — a guard, so a game that
    /// computes this number from a window size cannot ask the host to allocate a 16k render target for
    /// a thumbnail.</summary>
    public const int MaxSizePixels = 512;

    /// <summary>
    /// Square edge length of the capture in pixels, or 0 for the host's own default.
    ///
    /// <para><b>On the request because only the game knows how big the picture will be drawn</b>
    /// (2026-09-19, 「肖像の解像度上げてください」). The overlay sampler is <c>Filter.Nearest</c>, so a
    /// portrait photographed at one size and drawn at another is either blocky (magnified) or crunchy
    /// (minified); Triage's HUD scales with the window, so the size it will draw this at is known only
    /// to it. A game that does not care omits this and gets the host's default, exactly as before.</para>
    /// </summary>
    public int SizePixels { get; } = SizePixels;

    /// <summary>
    /// What the portrait is photographed against, or null for the host's own default backdrop.
    /// <para>
    /// On the REQUEST rather than as a host constant because a portrait's backdrop is art direction,
    /// and art direction belongs to the game: Triage asks for white (2026-08-21, user request
    /// 「患者の肖像を、背景を白にしてください」), where the host's default is a near-black studio grey.
    /// A game that does not care omits it and gets exactly what it got before this existed.
    /// </para>
    /// </summary>
    public Vector4? ClearColor { get; } = ClearColor;

    /// <summary>Rejects malformed game-provided capture intent before either host reaches Vulkan.</summary>
    public void Validate()
    {
        ArgumentOutOfRangeException.ThrowIfNegative(SubjectId);
        ArgumentOutOfRangeException.ThrowIfNegative(SizePixels);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(SizePixels, MaxSizePixels);
        if (!IsFinite(ViewProjection) || !IsFinite(CameraPosition))
        {
            throw new ArgumentException("Portrait capture camera values must be finite.");
        }

        if (ClearColor is { } color &&
            !(float.IsFinite(color.X) && float.IsFinite(color.Y) && float.IsFinite(color.Z) && float.IsFinite(color.W)))
        {
            throw new ArgumentException("Portrait clear colour must be finite.");
        }
    }

    private static bool IsFinite(Matrix4x4 matrix) =>
        float.IsFinite(matrix.M11) && float.IsFinite(matrix.M12) && float.IsFinite(matrix.M13) && float.IsFinite(matrix.M14)
        && float.IsFinite(matrix.M21) && float.IsFinite(matrix.M22) && float.IsFinite(matrix.M23) && float.IsFinite(matrix.M24)
        && float.IsFinite(matrix.M31) && float.IsFinite(matrix.M32) && float.IsFinite(matrix.M33) && float.IsFinite(matrix.M34)
        && float.IsFinite(matrix.M41) && float.IsFinite(matrix.M42) && float.IsFinite(matrix.M43) && float.IsFinite(matrix.M44);

    private static bool IsFinite(Vector3 value) => float.IsFinite(value.X) && float.IsFinite(value.Y) && float.IsFinite(value.Z);
}
