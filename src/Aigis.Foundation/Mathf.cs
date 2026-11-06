using System.Runtime.CompilerServices;

namespace Aigis.Foundation;

/// <summary>
/// Unity-compatible single-precision math helpers.
/// <para>
/// This exists so the large body of Unity-independent <c>*Calc</c>/<c>*Math</c>
/// gameplay logic in reference games (e.g. Triage) can be ported with zero edits.
/// Semantics deliberately match UnityEngine.Mathf.
/// </para>
/// </summary>
public static class Mathf
{
    public const float PI = 3.14159265358979f;
    public const float Deg2Rad = PI / 180f;
    public const float Rad2Deg = 180f / PI;
    public const float Epsilon = 1.401298E-45f;

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static float Clamp(float value, float min, float max)
        => value < min ? min : (value > max ? max : value);

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static float Clamp01(float value)
        => value < 0f ? 0f : (value > 1f ? 1f : value);

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static float Lerp(float a, float b, float t) => a + (b - a) * Clamp01(t);

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static float LerpUnclamped(float a, float b, float t) => a + (b - a) * t;

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static float MoveTowards(float current, float target, float maxDelta)
    {
        if (MathF.Abs(target - current) <= maxDelta)
        {
            return target;
        }

        return current + MathF.Sign(target - current) * maxDelta;
    }

    /// <summary>Unity-style "close enough" comparison (relative to magnitude).</summary>
    public static bool Approximately(float a, float b)
        => MathF.Abs(b - a) < MathF.Max(1E-06f * MathF.Max(MathF.Abs(a), MathF.Abs(b)), Epsilon * 8f);
}
